// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;
using FluentAssertions;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

// Actual canonical MessageBox -> managed service -> source modal frame. The
// typed headless window supplies explicit completion; no native/UI proof.
public partial class CanonicalLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MessageBox_ActiveOrExplicitOwnerWaitsForExactNativeCompletion(bool explicitOwner)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        var service = new ObservedModalMessageBoxService(platform);
        using var forwarding = new MessageBoxForwardingScope(platform, service, native: true);
        using Form owner = new();
        using var child = new InputProbeControl();
        owner.Controls.Add(child);
        owner.Show();
        owner.Activate();
        child.Focus().Should().BeTrue();
        int keys = 0;
        child.KeyDown += (_, _) => keys++;
        LibreHandle ownerHandle = platform.GetWindowHandle(owner);
        int begins = platform.NativeModalBegins;
        platform.Post(() =>
        {
            platform.IsWindowEnabled(owner).Should().BeFalse();
            platform.SendControlInput(child, SourceMessageBoxKey(LibreKey.A));
            keys.Should().Be(0);
            platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.Enter);
        });

        DialogResult result = explicitOwner
            ? MessageBox.Show(owner, "Message", "Modal")
            : MessageBox.Show("Message", "Modal");

        result.Should().Be(DialogResult.OK);
        service.LastRequest.Owner.Should().Be(ownerHandle);
        LibreHandle message = platform.LastActivatedWindow;
        message.Should().NotBe(ownerHandle);
        platform.NativeModalBegins.Should().Be(begins + 1);
        platform.NativeModalReleases.Should().ContainKey(message);
        platform.IsWindowEnabled(owner).Should().BeFalse();
        platform.Handles.TryGet(message, out ILibreWindow? retained).Should().BeTrue();
        retained!.Visible.Should().BeTrue();
        platform.SendControlInput(child, SourceMessageBoxKey(LibreKey.A));
        keys.Should().Be(0);

        platform.CompleteNativeModal(message);

        platform.Handles.TryGet(message, out ILibreWindow? _).Should().BeFalse();
        platform.IsWindowEnabled(owner).Should().BeTrue();
        platform.LastActivatedWindow.Should().Be(ownerHandle);
        platform.SendControlInput(child, SourceMessageBoxKey(LibreKey.A));
        keys.Should().Be(1);
    }

    [Fact]
    public void MessageBox_DisabledNativePolicyStillUsesAndReleasesSourceFrame()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        var service = new ObservedModalMessageBoxService(platform);
        using var forwarding = new MessageBoxForwardingScope(platform, service, native: false);
        using Form owner = new();
        owner.Show();
        owner.Activate();
        LibreHandle message = default;
        int begins = platform.NativeModalBegins;
        platform.Post(() =>
        {
            platform.IsWindowEnabled(owner).Should().BeFalse();
            message = platform.LastActivatedWindow;
            platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.Escape);
        });

        MessageBox.Show(owner, "Message", "Modal", MessageBoxButtons.OKCancel).Should().Be(DialogResult.Cancel);

        platform.NativeModalBegins.Should().Be(begins);
        platform.NativeModalReleases.Should().NotContainKey(message);
        platform.IsWindowEnabled(owner).Should().BeTrue();
        platform.Handles.TryGet(message, out ILibreWindow? _).Should().BeFalse();
    }

    [Fact]
    public void MessageBox_ReentrantResultCannotPopNewerDeferredFrameOrRetireItsWindow()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        var service = new ObservedModalMessageBoxService(platform);
        using var forwarding = new MessageBoxForwardingScope(platform, service, native: true);
        using Form owner = new();
        owner.Show();
        owner.Activate();
        LibreHandle first = default;
        LibreHandle second = default;
        platform.Post(() =>
        {
            first = platform.LastActivatedWindow;
            platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.Enter);
            // Reenter before the first source loop returns, after its terminal result.
            platform.Post(() => platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.Escape));
            MessageBox.Show(owner, "Second", "Modal", MessageBoxButtons.OKCancel).Should().Be(DialogResult.Cancel);
            second = platform.LastActivatedWindow;
        });

        MessageBox.Show(owner, "First", "Modal").Should().Be(DialogResult.OK);
        first.Should().NotBe(second);
        platform.NativeModalReleases.Should().ContainKey(first).And.ContainKey(second);
        platform.CompleteNativeModal(first);
        platform.IsWindowEnabled(owner).Should().BeFalse();
        platform.Handles.TryGet(first, out ILibreWindow? _).Should().BeTrue();
        platform.Handles.TryGet(second, out ILibreWindow? _).Should().BeTrue();

        platform.CompleteNativeModal(second);

        platform.IsWindowEnabled(owner).Should().BeTrue();
        platform.Handles.TryGet(first, out ILibreWindow? _).Should().BeFalse();
        platform.Handles.TryGet(second, out ILibreWindow? _).Should().BeFalse();
    }

    [Fact]
    public void MessageBox_ReleasedSessionCallbackCanCreateIndependentReplacementGeneration()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        var service = new ObservedModalMessageBoxService(platform);
        using var forwarding = new MessageBoxForwardingScope(platform, service, native: true);
        using Form owner = new();
        owner.Show();
        owner.Activate();
        platform.Post(() => platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.Enter));
        MessageBox.Show(owner, "First", "Modal").Should().Be(DialogResult.OK);
        LibreHandle first = platform.LastActivatedWindow;
        LibreHandle replacement = default;
        service.AfterReleased = () =>
        {
            platform.Handles.TryGet(first, out ILibreWindow? _).Should().BeFalse();
            platform.Post(() => platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.Enter));
            MessageBox.Show(owner, "Replacement", "Modal").Should().Be(DialogResult.OK);
            replacement = platform.LastActivatedWindow;
        };

        platform.CompleteNativeModal(first);

        replacement.Should().NotBe(first);
        platform.Handles.TryGet(replacement, out ILibreWindow? live).Should().BeTrue();
        live!.Visible.Should().BeTrue();
        platform.IsWindowEnabled(owner).Should().BeFalse();
        platform.CompleteNativeModal(replacement);
        platform.IsWindowEnabled(owner).Should().BeTrue();
        platform.Handles.TryGet(replacement, out ILibreWindow? _).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MessageBox_SourceFinallyRestoresOwnerAndPreservesPrimaryFailure(bool failDuringShow)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        var primary = new InvalidOperationException("Original service failure");
        var cleanup = new InvalidOperationException("Source cleanup failure");
        var service = new FailingModalMessageBoxService(failDuringShow ? primary : null, cleanup);
        using var forwarding = new MessageBoxForwardingScope(platform, service, native: false);
        using Form owner = new();
        owner.Show();
        owner.Activate();

        Action show = () => MessageBox.Show(owner, "Failure", "Modal");
        Exception observed = show.Should().Throw<Exception>().Which;

        observed.Should().BeSameAs(failDuringShow ? primary : cleanup);
        service.CleanupCount.Should().Be(1);
        platform.IsWindowEnabled(owner).Should().BeTrue();
        platform.LastActivatedWindow.Should().Be(platform.GetWindowHandle(owner));
    }

    [Fact]
    public void MessageBox_DefaultDesktopRequestPreservesNullOwnerAndSourceOnlyModality()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        var service = new ObservedModalMessageBoxService(platform);
        using var forwarding = new MessageBoxForwardingScope(platform, service, native: false);
        using Form active = new();
        active.Show();
        active.Activate();
        platform.Post(() =>
        {
            platform.IsWindowEnabled(active).Should().BeFalse();
            platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.Enter);
        });

        MessageBox.Show("Message", "Desktop", MessageBoxButtons.OK, MessageBoxIcon.None,
            MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly).Should().Be(DialogResult.OK);

        service.LastRequest.Owner.IsNull.Should().BeTrue();
        platform.IsWindowEnabled(active).Should().BeTrue();
    }

    private static LibreInputEvent SourceMessageBoxKey(LibreKey key)
        => new(LibreInputEventKind.KeyDown, 1, LibreInputModifiers.None, key, null,
            default, default, LibrePointerButton.None);

    private sealed partial class HeadlessPlatform : ILibreModalMessageBoxService
    {
        internal ILibreModalMessageBoxService? ScopedMessageBoxes { get; set; }

        public LibreMessageBoxResult Show(in LibreMessageBoxRequest request, ILibreMessageBoxModalLifecycle lifecycle)
            => ScopedMessageBoxes is { } service ? service.Show(request, lifecycle) : Show(request);
    }

    private sealed class MessageBoxForwardingScope : IDisposable
    {
        private readonly HeadlessPlatform _platform;
        private readonly ILibreModalMessageBoxService? _previous;
        private readonly bool _native;

        internal MessageBoxForwardingScope(HeadlessPlatform platform, ILibreModalMessageBoxService service, bool native)
        {
            _platform = platform;
            _previous = platform.ScopedMessageBoxes;
            _native = platform.NativeModalSessions;
            platform.ScopedMessageBoxes = service;
            platform.NativeModalSessions = native;
        }

        public void Dispose()
        {
            _platform.ScopedMessageBoxes = _previous;
            _platform.NativeModalSessions = _native;
        }
    }

    private sealed class ObservedModalMessageBoxService : ILibreModalMessageBoxService
    {
        private readonly ManagedLibreMessageBoxService _service;
        internal LibreMessageBoxRequest LastRequest { get; private set; }
        internal Action? AfterReleased { get; set; }

        internal ObservedModalMessageBoxService(HeadlessPlatform platform)
            => _service = new(platform, platform.Handles, platform, platform, platform, new MessageBoxLayoutText());

        public LibreMessageBoxResult Show(in LibreMessageBoxRequest request) => _service.Show(request);

        public LibreMessageBoxResult Show(in LibreMessageBoxRequest request, ILibreMessageBoxModalLifecycle lifecycle)
        {
            LastRequest = request;
            return _service.Show(request, new ObservedMessageBoxLifecycle(lifecycle, this));
        }

        private sealed class ObservedMessageBoxLifecycle(
            ILibreMessageBoxModalLifecycle source, ObservedModalMessageBoxService owner) : ILibreMessageBoxModalLifecycle
        {
            public void Begin(ILibreModalWindow? exactWindow) => source.Begin(exactWindow);

            public void AfterSourceRelease(Action cleanup)
                => source.AfterSourceRelease(() =>
                {
                    cleanup();
                    Action? callback = owner.AfterReleased;
                    owner.AfterReleased = null;
                    callback?.Invoke();
                });
        }
    }

    private sealed class FailingModalMessageBoxService(Exception? primary, Exception cleanup) : ILibreModalMessageBoxService
    {
        internal int CleanupCount { get; private set; }

        public LibreMessageBoxResult Show(in LibreMessageBoxRequest request)
            => throw new InvalidOperationException("The source must select the typed lifecycle overload.");

        public LibreMessageBoxResult Show(in LibreMessageBoxRequest request, ILibreMessageBoxModalLifecycle lifecycle)
        {
            lifecycle.AfterSourceRelease(() =>
            {
                CleanupCount++;
                throw cleanup;
            });
            lifecycle.Begin(null);
            if (primary is not null) throw primary;
            return LibreMessageBoxResult.OK;
        }
    }

    // Layout-only service double. These lifecycle controls do not qualify text or pixels.
    private sealed class MessageBoxLayoutText : ILibreTextRendererService
    {
        public Size MeasureText(Graphics? graphics, string text, Font? font, Size proposedSize, LibreTextFormat format)
            => new(Math.Min(80, proposedSize.Width), 20);

        public void DrawText(Graphics graphics, string text, Font? font, Rectangle bounds,
            Color foreColor, Color backColor, LibreTextFormat format)
        {
        }
    }
}
