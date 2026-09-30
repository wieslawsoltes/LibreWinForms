// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;
using LibreWinForms.Platform;
using LibreWinForms.ProGPU;
using LibreWinForms.ProGPU.Tests;
using ProGPU.Backend;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativePointerProvider_RetiresActualDropdownHoverAndCapture(bool cancel)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using ContextMenuStrip menu = new() { AutoClose = false };
        using Panel control = new() { Size = new(100, 60) };
        menu.Items.Add(new ToolStripControlHost(control) { AutoSize = false, Size = control.Size });
        owner.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        menu.Show(owner, Point.Empty);
        Point point = menu.PointToClient(control.PointToScreen(new(20, 20)));
        Assert.True(menu.ClientRectangle.Contains(point));
        using NativePointerTestContext provider = new();
        SourcePointerTarget target = new(menu, input => platform.SendControlInput(menu, input));
        using NativePointerInput subscription = new(provider, target);
        target.Subscription = subscription;
        NativePointerEvent down = new(NativePointerEventKind.Down, point.X + .125, point.Y + .125,
            123.25, 0, 1, NativePointerModifiers.Shift | NativePointerModifiers.CapsLock);
        provider.Emit(down);
        Assert.True(control.Capture);
        Assert.Equal(MouseButtons.Left, Control.MouseButtons);
        Assert.Equal(Keys.Shift, Control.ModifierKeys);
        Point position = Control.MousePosition;
        int leaves = 0, releases = 0, clicks = 0, captures = 0, deactivations = 0;
        control.MouseLeave += (_, _) => leaves++;
        control.MouseUp += (_, _) => releases++;
        control.Click += (_, _) => clicks++;
        control.MouseCaptureChanged += (_, _) => captures++;
        owner.Deactivate += (_, _) => deactivations++;

        NativePointerEvent retirement = new(cancel ? NativePointerEventKind.Cancel : NativePointerEventKind.Leave,
            point.X + .125, point.Y + .125, 124.75, -1, 0, NativePointerModifiers.None);
        provider.Emit(retirement);
        provider.Emit(retirement);
        Assert.Equal(!cancel, control.Capture);
        Assert.Equal(cancel ? MouseButtons.None : MouseButtons.Left, Control.MouseButtons);
        Assert.Equal(Keys.Shift, Control.ModifierKeys);
        Assert.Equal(position, Control.MousePosition);
        Assert.Equal(1, leaves); Assert.Equal(cancel ? 1 : 0, captures);
        Assert.Equal(0, releases); Assert.Equal(0, clicks); Assert.Equal(0, deactivations);
        Assert.True(menu.Visible); Assert.Same(owner, Form.ActiveForm);
        Assert.Equal(retirement.Timestamp, target.LastInput.NativePointer!.Value.Timestamp);
        Assert.Equal(retirement.X, target.LastInput.NativePointer!.Value.X);
        provider.Emit(retirement with { Kind = NativePointerEventKind.Cancel });
    }

    [Fact]
    public void NativePointerProvider_CharacterFlushClosingPopupRejectsItsOldTail()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using ContextMenuStrip menu = new();
        menu.Items.Add("Command");
        owner.Show(); menu.Show(owner, Point.Empty);
        using NativePointerTestContext provider = new();
        SourcePointerTarget target = new(menu, input => platform.SendControlInput(menu, input));
        using NativePointerInput subscription = new(provider, target);
        target.Subscription = subscription;
        target.Flushing = menu.Dispose;
        provider.Emit(new(NativePointerEventKind.Down, 10, 10, 1, 0, 1, NativePointerModifiers.None));
        Assert.True(menu.IsDisposed);
        Assert.Equal(0, target.Deliveries);
        Assert.Equal(MouseButtons.None, Control.MouseButtons);
    }

    private sealed class SourcePointerTarget : INativePointerTarget
    {
        private readonly Control _source;
        private readonly nint _handle;
        private readonly Action<LibreInputEvent> _deliver;
        internal NativePointerInput? Subscription { get; set; }
        internal Action? Flushing { get; set; }
        internal LibreInputEvent LastInput { get; private set; }
        internal int Deliveries { get; private set; }
        internal SourcePointerTarget(Control source, Action<LibreInputEvent> deliver)
        {
            _source = source; _handle = source.Handle; _deliver = deliver;
        }

        public bool IsCurrent(NativePointerInput subscription)
            => ReferenceEquals(subscription, Subscription) && !_source.IsDisposed
                && _source.IsHandleCreated && _source.Handle == _handle
                && ReferenceEquals(Control.FromHandle(_handle), _source);
        public LibrePoint MapPoint(double x, double y)
            => LibreWindowCoordinates.ToManagedPoint(x, y, LibreWindowCoordinateMode.DevicePixels, 1, 1);
        public void FlushCharacters() => Flushing?.Invoke();
        public ProGpuDragCancellation? PrepareCancellation() => null;
        public void Input(in LibreInputEvent input)
        {
            Deliveries++; LastInput = input; _deliver(input);
        }
    }
}
