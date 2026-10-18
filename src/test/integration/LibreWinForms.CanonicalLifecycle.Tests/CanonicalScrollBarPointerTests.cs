// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;
using LibreWinForms.Platform;
using ProGPU.Backend;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void PortableScrollBarPointer_ArrowDefaultsPrecedeMouseNotifications(bool horizontal, bool forward, bool rtl)
    {
        RunNativeScrollBar(horizontal, (platform, form, bar, _, _) =>
        {
            bar.RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
            List<string> events = new();
            bool increment = forward != rtl;
            int expected = increment ? 53 : 47;
            bar.Scroll += (_, e) => events.Add(e.Type.ToString());
            bar.ValueChanged += (_, _) => events.Add("value");
            bar.MouseDown += (_, _) => { Assert.Equal(expected, bar.Value); events.Add("down"); };
            bar.MouseUp += (_, _) => events.Add("up");
            Point point = horizontal ? new(forward ? 176 : 3, 8) : new(8, forward ? 136 : 3);
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, point);
            Assert.Equal(expected, bar.Value);
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerUp, point);
            Assert.Equal(new[] { increment ? "SmallIncrement" : "SmallDecrement", "value", "down", "EndScroll", "up" }, events);
            Assert.False(bar.Capture);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableScrollBarPointer_TrackUsesLargeChange(bool forward)
    {
        RunNativeScrollBar(true, (platform, form, bar, _, _) =>
        {
            Point point = new(forward ? 155 : 25, 8);
            List<ScrollEventType> events = new();
            bar.Scroll += (_, e) => events.Add(e.Type);
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, point);
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerUp, point);
            Assert.Equal(forward ? 60 : 40, bar.Value);
            Assert.Equal(new[] { forward ? ScrollEventType.LargeIncrement : ScrollEventType.LargeDecrement, ScrollEventType.EndScroll }, events);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PortableScrollBarPointer_ThumbTracksAndCommitsActualRange(bool horizontal, bool rtl)
    {
        RunNativeScrollBar(horizontal, (platform, form, bar, _, _) =>
        {
            bar.RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
            bar.Value = rtl ? 91 : 0;
            List<ScrollEventType> events = new();
            bar.Scroll += (_, e) => events.Add(e.Type);
            Point start = horizontal ? new(22, 8) : new(8, 22);
            Point end = horizontal ? new(160, 8) : new(8, 120);
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, start);
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerMove, end);
            Assert.Equal(rtl ? 0 : 91, bar.Value);
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerUp, end);
            Assert.Equal(new[] { ScrollEventType.ThumbTrack, ScrollEventType.ThumbPosition, ScrollEventType.EndScroll }, events);
        });
    }

    [Theory]
    [InlineData(0)] // Source value away/back.
    [InlineData(1)] // Range away/back.
    [InlineData(2)] // Source resize away/back.
    [InlineData(3)] // Capture released.
    [InlineData(4)] // Disabled away/back.
    [InlineData(5)] // Hidden away/back.
    public void PortableScrollBarPointer_RetiredPressCannotResume(int retirement)
    {
        RunNativeScrollBar(true, (platform, form, bar, _, _) =>
        {
            bar.Value = 0;
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, new(22, 8));
            switch (retirement)
            {
                case 0: bar.Value = 1; bar.Value = 0; break;
                case 1: bar.Maximum = 101; bar.Maximum = 100; break;
                case 2: bar.Width = 181; bar.Width = 180; break;
                case 3: bar.Capture = false; break;
                case 4: bar.Enabled = false; bar.Enabled = true; break;
                case 5: bar.Hide(); bar.Show(); break;
            }

            int notifications = 0;
            bar.Scroll += (_, _) => notifications++;
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerMove, new(160, 8));
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerUp, new(160, 8));
            Assert.Equal(0, bar.Value);
            Assert.Equal(0, notifications);
        });
    }

    [Fact]
    public void PortableScrollBarPointer_ScrollCallbackOverrideRetiresObsoleteValueWrite()
    {
        RunNativeScrollBar(true, (platform, form, bar, _, _) =>
        {
            int calls = 0;
            bar.Scroll += (_, _) => { calls++; bar.Value = 70; };
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, new(176, 8));
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerUp, new(176, 8));
            Assert.Equal(70, bar.Value);
            Assert.Equal(1, calls);
        });
    }

    [Fact]
    public void PortableScrollBarPointer_HandlerNewValueRemainsAuthoritative()
    {
        RunNativeScrollBar(true, (platform, form, bar, _, _) =>
        {
            bar.Scroll += (_, e) => { if (e.Type == ScrollEventType.SmallIncrement) e.NewValue = 67; };
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, new(176, 8));
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerUp, new(176, 8));
            Assert.Equal(67, bar.Value);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableScrollBarPointer_CallbackFailureCannotResumeOldDrag(bool inValueChanged)
    {
        RunNativeScrollBar(true, (platform, form, bar, _, _) =>
        {
            InvalidOperationException failure = new("pointer callback");
            ScrollEventHandler scroll = (_, _) => throw failure;
            EventHandler changed = (_, _) => throw failure;
            if (inValueChanged) bar.ValueChanged += changed;
            else bar.Scroll += scroll;
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
                SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, new(176, 8))));
            bar.Scroll -= scroll;
            bar.ValueChanged -= changed;
            int count = 0;
            bar.Scroll += (_, _) => count++;
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerUp, new(176, 8));
            Assert.Equal(inValueChanged ? 53 : 50, bar.Value);
            Assert.Equal(0, count);
        });
    }

    [Fact]
    public void PortableScrollBarPointer_NestedPressOwnsItsRelease()
    {
        RunNativeScrollBar(true, (platform, form, bar, _, _) =>
        {
            bool nested = false;
            List<ScrollEventType> events = new();
            bar.Scroll += (_, e) =>
            {
                events.Add(e.Type);
                if (nested) return;
                nested = true;
                SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, new(3, 8));
            };
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, new(176, 8));
            Assert.Equal(47, bar.Value);
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerUp, new(3, 8));
            Assert.Equal(new[] { ScrollEventType.SmallIncrement, ScrollEventType.SmallDecrement, ScrollEventType.EndScroll }, events);
        });
    }

    [Fact]
    public void PortableScrollBarPointer_PublicNotificationsAreNotInput()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using PointerNotificationScrollBar bar = new() { Value = 50 };
        int scrolls = 0;
        bar.Scroll += (_, _) => scrolls++;
        bar.Notify();
        Assert.Equal(50, bar.Value);
        Assert.Equal(0, scrolls);
    }

    private sealed class PointerNotificationScrollBar : HScrollBar
    {
        internal void Notify()
        {
            OnMouseDown(new(MouseButtons.Left, 1, 1, 1, 0));
            OnMouseMove(new(MouseButtons.Left, 0, 70, 1, 0));
            OnMouseUp(new(MouseButtons.Left, 1, 70, 1, 0));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableScrollBarPointer_NativeButtonMetadataKeepsSourceRelease(bool cancel)
    {
        RunNativeScrollBar(true, (_, form, bar, provider, _) =>
        {
            Point point = form.PointToClient(bar.PointToScreen(new(176, 8)));
            List<ScrollEventType> events = new();
            bar.Scroll += (_, e) => events.Add(e.Type);
            provider.Emit(new(NativePointerEventKind.Down, point.X + .125, point.Y + .125,
                1, 0, 1, NativePointerModifiers.None));
            Assert.Equal(53, bar.Value);
            provider.Emit(new(cancel ? NativePointerEventKind.Cancel : NativePointerEventKind.Up,
                point.X + .125, point.Y + .125, 2, cancel ? -1 : 0, cancel ? 0 : 1, NativePointerModifiers.None));
            Assert.Equal(cancel ? new[] { ScrollEventType.SmallIncrement }
                : new[] { ScrollEventType.SmallIncrement, ScrollEventType.EndScroll }, events);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableScrollBarPointer_UserMouseKeepsDefaultInputOwnership(bool changedAfterDown)
    {
        RunNativeScrollBar(true, (platform, form, bar, _, _) =>
        {
            UserMouseScrollBar supplied = Assert.IsType<UserMouseScrollBar>(bar);
            int events = 0;
            bar.Scroll += (_, _) => events++;
            supplied.OwnMouse = !changedAfterDown;
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, new(176, 8));
            supplied.OwnMouse = true;
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerUp, new(176, 8));
            Assert.Equal(changedAfterDown ? 53 : 50, bar.Value);
            Assert.Equal(changedAfterDown ? 1 : 0, events);
        }, static () => new UserMouseScrollBar());
    }

    private sealed class UserMouseScrollBar : HScrollBar
    {
        internal bool OwnMouse { set => SetStyle(ControlStyles.UserMouse, value); }
    }

    [Fact]
    public void PortableScrollBarPointer_ReentrantDirectionGetterCannotRetireNestedMove()
    {
        RunNativeScrollBar(true, (platform, form, bar, _, _) =>
        {
            NativeScrollBarDirectionProbe supplied = Assert.IsType<NativeScrollBarDirectionProbe>(bar);
            bar.Value = 0;
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, new(22, 8));
            supplied.Reading = () =>
            {
                supplied.Reading = null;
                SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerMove, new(160, 8));
            };
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerMove, new(80, 8));
            Assert.Equal(91, bar.Value);
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerMove, new(22, 8));
            Assert.Equal(0, bar.Value);
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerUp, new(22, 8));
        }, static () => new NativeScrollBarDirectionProbe());
    }

    private static void SendScrollPointer(HeadlessPlatform platform, Form form, ScrollBar bar, LibreInputEventKind kind, Point point)
    {
        Point source = form.PointToClient(bar.PointToScreen(point));
        platform.SendControlInput(form, new(kind, 1, LibreInputModifiers.None, LibreKey.Unknown, null,
            new(source.X, source.Y), default, LibrePointerButton.Primary));
    }
}
