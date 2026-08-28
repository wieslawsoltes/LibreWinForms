// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using LibreWinForms.Platform;
using ProGPU.Backend;
using Tmds.DBus.Protocol;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public class XdgPortalFileDialogTests
{
    [Fact]
    public void Show_MapsCanonicalOpenStateAndCommitsLocalPortalUris()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        RecordingPortal portal = new(new(
            XdgPortalResponse.Success,
            ["file:///tmp/first.txt", "file:///tmp/second%20file.md"],
            2,
            true));
        var service = CreateService(portal, "x11:2a");
        LibreFileDialogRequest request = CreateRequest() with
        {
            Options = LibreFileDialogOptions.MultiSelect | LibreFileDialogOptions.ShowReadOnly,
            Filters =
            [
                new LibreFileDialogFilter("Text", ["*.txt"]),
                new LibreFileDialogFilter("Markdown", ["*.md"]),
            ],
            FilterIndex = 1,
        };

        LibreFileDialogResult result = service.Show(request);

        result.Accepted.Should().BeTrue();
        result.SelectedPaths.Should().Equal("/tmp/first.txt", "/tmp/second file.md");
        result.FilterIndex.Should().Be(2);
        result.ReadOnlyChecked.Should().BeTrue();
        portal.Request.ParentWindow.Should().Be("x11:2a");
        portal.Request.Kind.Should().Be(LibreFileDialogKind.OpenFile);
        portal.Request.InitialDirectory.Should().Be("/tmp");
        portal.Request.SelectedPaths.Should().Equal("seed.txt");
        portal.Request.Multiple.Should().BeTrue();
        portal.Request.ShowReadOnly.Should().BeTrue();
    }

    [Fact]
    public void Show_PreservesCandidateStateWhenPortalCancels()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        RecordingPortal portal = new(new(XdgPortalResponse.Cancelled, [], null, null));
        var service = CreateService(portal, string.Empty);
        LibreFileDialogRequest request = CreateRequest() with
        {
            Kind = LibreFileDialogKind.SaveFile,
            Options = LibreFileDialogOptions.ReadOnlyChecked,
        };

        LibreFileDialogResult result = service.Show(request);

        result.Accepted.Should().BeFalse();
        result.SelectedPaths.Should().Equal("seed.txt");
        result.FilterIndex.Should().Be(1);
        result.ReadOnlyChecked.Should().BeTrue();
        portal.Request.Kind.Should().Be(LibreFileDialogKind.SaveFile);
    }

    [Fact]
    public void Show_RejectsPortalErrorsAndNonLocalOrAmbiguousSelections()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        LibreFileDialogRequest request = CreateRequest();
        Action failed = () => CreateService(
            new RecordingPortal(new(XdgPortalResponse.Other, [], null, null)),
            string.Empty).Show(request);
        Action remote = () => CreateService(
            new RecordingPortal(new(XdgPortalResponse.Success, ["https://example.com/file.txt"], null, null)),
            string.Empty).Show(request);
        Action multiple = () => CreateService(
            new RecordingPortal(new(XdgPortalResponse.Success, ["file:///tmp/a", "file:///tmp/b"], null, null)),
            string.Empty).Show(request);

        failed.Should().Throw<InvalidOperationException>().WithMessage("*could not complete*");
        remote.Should().Throw<InvalidOperationException>().WithMessage("*non-local filesystem URI*");
        multiple.Should().Throw<InvalidOperationException>().WithMessage("*multiple paths*");
    }

    [Fact]
    public void Show_RequiresDispatcherAndRoutesUnsupportedHelpToFallbackPolicy()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        RecordingPortal portal = new(new(XdgPortalResponse.Success, ["file:///tmp/file.txt"], null, null));
        var service = CreateService(portal, string.Empty);
        LibreFileDialogRequest helpRequest = CreateRequest() with
        {
            Options = LibreFileDialogOptions.ShowHelp,
            HelpRequested = () => { },
        };

        Action help = () => service.Show(helpRequest);
        Action hidden = () => service.Show(CreateRequest() with
        {
            Options = LibreFileDialogOptions.ShowHiddenFiles,
        });
        Exception? wrongThreadError = null;
        Thread thread = new(() =>
        {
            try
            {
                service.Show(CreateRequest());
            }
            catch (Exception exception)
            {
                wrongThreadError = exception;
            }
        });
        thread.Start();
        thread.Join();

        help.Should().Throw<PlatformNotSupportedException>().WithMessage("*Help action*");
        hidden.Should().Throw<PlatformNotSupportedException>().WithMessage("*hidden files*");
        wrongThreadError.Should().BeOfType<InvalidOperationException>();
        portal.CallCount.Should().Be(0);
    }

    [Fact]
    public void ParentWindow_FormatsX11ButNeverLeaksRawWaylandSurface()
    {
        XdgPortalParentWindow.Format(new(NativeWindowKind.X11, (nint)0x2A, 0, "X11"))
            .Should().Be("x11:2a");
        XdgPortalParentWindow.Format(new(NativeWindowKind.Wayland, (nint)0x2A, 0, "Wayland"))
            .Should().BeEmpty();
        XdgPortalParentWindow.Format(NativeWindowHandle.Empty).Should().BeEmpty();
    }

    [Fact]
    public void ParentProvider_HoldsWaylandExportForExactlyOneRequestLease()
    {
        RecordingWaylandExporter exporter = new("wayland:exported-token");
        var provider = new ProGpuXdgPortalParentWindowProvider(new ManagedLibreHandleRegistry(), exporter);

        IXdgPortalParentWindowLease lease = provider.AcquireNative(
            new(NativeWindowKind.Wayland, (nint)0x2A, 0, "Wayland"));
        lease.Identifier.Should().Be("wayland:exported-token");
        exporter.NativeHandle.Should().Be((nint)0x2A);

        lease.Dispose();
        lease.Dispose();

        exporter.ReleaseCount.Should().Be(1);
    }

    [Fact]
    public void ParentProvider_RejectsInvalidWaylandExportAndServiceReleasesLease()
    {
        RecordingWaylandExporter invalidExporter = new("wayland:");
        var provider = new ProGpuXdgPortalParentWindowProvider(
            new ManagedLibreHandleRegistry(),
            invalidExporter);
        Action invalid = () => provider.AcquireNative(
            new(NativeWindowKind.Wayland, (nint)0x2A, 0, "Wayland"));
        invalid.Should().Throw<InvalidOperationException>().WithMessage("*invalid xdg-foreign identifier*");
        invalidExporter.ReleaseCount.Should().Be(1);

        RecordingParentProvider parent = new("x11:2a");
        RecordingPortal portal = new(new(
            XdgPortalResponse.Success,
            ["file:///tmp/file.txt"],
            null,
            null));
        var service = new XdgDesktopPortalLibreFileDialogService(new ProGpuDispatcher(), portal, parent);

        service.Show(CreateRequest());

        parent.ReleaseCount.Should().Be(1);
    }

    [Fact]
    public void PreferredService_FallsBackOnlyWhenPortalIsUnavailable()
    {
        LibreFileDialogRequest request = CreateRequest();
        RecordingDialog unavailable = new(new PlatformNotSupportedException("missing portal"));
        RecordingDialog fallback = new(new LibreFileDialogResult(true, ["/tmp/fallback"], 1, false));
        var preferred = new PreferredLinuxLibreFileDialogService(unavailable, fallback);

        preferred.Show(request).SelectedPaths.Should().Equal("/tmp/fallback");
        fallback.CallCount.Should().Be(1);

        RecordingDialog cancelled = new(new LibreFileDialogResult(false, ["seed.txt"], 1, false));
        RecordingDialog unused = new(new LibreFileDialogResult(true, ["/tmp/wrong"], 1, false));
        new PreferredLinuxLibreFileDialogService(cancelled, unused).Show(request).Accepted.Should().BeFalse();
        unused.CallCount.Should().Be(0);

        RecordingDialog failed = new(new InvalidOperationException("portal response error"));
        Action show = () => new PreferredLinuxLibreFileDialogService(failed, unused).Show(request);
        show.Should().Throw<InvalidOperationException>().WithMessage("portal response error");
        unused.CallCount.Should().Be(0);
    }

    [Fact]
    public void PreferredService_DisposesOwnedAdaptersOnce()
    {
        RecordingDialog preferred = new(new LibreFileDialogResult(false, [], 0, false));
        RecordingDialog fallback = new(new LibreFileDialogResult(false, [], 0, false));
        var service = new PreferredLinuxLibreFileDialogService(preferred, fallback);

        service.Dispose();
        service.Dispose();

        preferred.DisposeCount.Should().Be(1);
        fallback.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void PortalCodec_EmitsTypedOptionsWithNullTerminatedPaths()
    {
        XdgFileChooserRequest request = new(
            LibreFileDialogKind.SelectFolder,
            "x11:2a",
            "Choose folder",
            "/tmp",
            ["child"],
            string.Empty,
            [new LibreFileDialogFilter("Folders", ["*"])],
            1,
            false,
            true,
            true);

        Dictionary<string, VariantValue> options = TmdsXdgFileChooserPortal.BuildOptions(request, "token_1");

        options["handle_token"].GetString().Should().Be("token_1");
        options["modal"].GetBool().Should().BeTrue();
        options["directory"].GetBool().Should().BeTrue();
        options["multiple"].GetBool().Should().BeFalse();
        options["current_folder"].GetArray<byte>().Should().Equal(
            [.. System.Text.Encoding.UTF8.GetBytes("/tmp/child"), (byte)0]);
        options["filters"].Count.Should().Be(1);
        options["current_filter"].GetItem(0).GetString().Should().Be("Folders");
        options["choices"].GetItem(0).GetItem(3).GetString().Should().Be("true");
    }

    [Fact]
    public void PortalCodec_ParsesUrisFilterAndReadOnlyChoice()
    {
        Tmds.DBus.Protocol.Array<Struct<uint, string>> patterns = new()
        {
            Struct.Create(0u, "*.md"),
        };
        Tmds.DBus.Protocol.Array<Struct<string, string>> choices = new()
        {
            Struct.Create("librewinforms_read_only", "true"),
        };
        Dictionary<string, VariantValue> values = new()
        {
            ["uris"] = VariantValue.Array(new[] { "file:///tmp/readme.md" }),
            ["current_filter"] = Struct.Create("Markdown", patterns),
            ["choices"] = choices,
        };

        XdgFileChooserResult result = TmdsXdgFileChooserPortal.ParseResult(
            XdgPortalResponse.Success,
            values,
            [new LibreFileDialogFilter("Markdown", ["*.md"])]);

        result.Uris.Should().Equal("file:///tmp/readme.md");
        result.FilterIndex.Should().Be(1);
        result.ReadOnlyChecked.Should().BeTrue();
    }

    private static XdgDesktopPortalLibreFileDialogService CreateService(
        IXdgFileChooserPortal portal,
        string parent)
        => new(new ProGpuDispatcher(), portal, new StaticParentProvider(parent));

    private static LibreFileDialogRequest CreateRequest()
        => new(
            LibreFileDialogKind.OpenFile,
            "Choose file",
            string.Empty,
            "/tmp",
            ["seed.txt"],
            "txt",
            [new LibreFileDialogFilter("Text", ["*.txt"])],
            1,
            LibreFileDialogOptions.None,
            null,
            [],
            null,
            default);

    private sealed class StaticParentProvider(string parent) : IXdgPortalParentWindowProvider
    {
        public IXdgPortalParentWindowLease Acquire(LibreHandle owner)
            => new XdgPortalParentWindowLease(parent);
    }

    private sealed class RecordingParentProvider(string parent) : IXdgPortalParentWindowProvider
    {
        public int ReleaseCount { get; private set; }

        public IXdgPortalParentWindowLease Acquire(LibreHandle owner)
            => new XdgPortalParentWindowLease(parent, () => ReleaseCount++);
    }

    private sealed class RecordingWaylandExporter(string identifier) : IXdgPortalWaylandParentExporter
    {
        public nint NativeHandle { get; private set; }

        public int ReleaseCount { get; private set; }

        public bool TryExport(
            NativeWindowHandle window,
            [NotNullWhen(true)] out IXdgPortalParentWindowLease? lease)
        {
            NativeHandle = window.Handle;
            lease = new XdgPortalParentWindowLease(identifier, () => ReleaseCount++);
            return true;
        }
    }

    private sealed class RecordingPortal(XdgFileChooserResult result) : IXdgFileChooserPortal
    {
        public int CallCount { get; private set; }

        public XdgFileChooserRequest Request { get; private set; }

        public XdgFileChooserResult Show(in XdgFileChooserRequest request)
        {
            CallCount++;
            Request = request;
            return result;
        }
    }

    private sealed class RecordingDialog : ILibreFileDialogService, IDisposable
    {
        private readonly LibreFileDialogResult? _result;
        private readonly Exception? _exception;

        public RecordingDialog(LibreFileDialogResult result) => _result = result;

        public RecordingDialog(Exception exception) => _exception = exception;

        public int CallCount { get; private set; }

        public int DisposeCount { get; private set; }

        public LibreFileDialogResult Show(in LibreFileDialogRequest request)
        {
            CallCount++;
            if (_exception is not null)
            {
                throw _exception;
            }

            return _result!.Value;
        }

        public void Dispose() => DisposeCount++;
    }
}
