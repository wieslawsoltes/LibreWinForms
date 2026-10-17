// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PortableScrollBarRepeat_ArrowHoldsPreserveEventOrder(bool horizontal, bool rtl)
    {
        RunNativeScrollBar(horizontal, (platform, form, bar, _, _) =>
        {
            bar.RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
            Point point = horizontal ? new(176, 8) : new(8, 136);
            List<string> events = [];
            bar.Scroll += (_, e) => events.Add(e.Type.ToString());
            bar.ValueChanged += (_, _) => events.Add("value");
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, point);
            Assert.Equal(TimeSpan.FromMilliseconds(400), platform.LastTimerInterval);
            Assert.False(platform.LastTimerRepeating);
            Action first = platform.CaptureTimerCallback();
            platform.FireTimer();
            Assert.Equal(TimeSpan.FromMilliseconds(60), platform.LastTimerInterval);
            first(); // A queued callback from the retired initial registration.
            platform.FireTimer();
            Assert.Equal(horizontal && rtl ? 41 : 59, bar.Value);
            Action queued = platform.CaptureTimerCallback();
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerUp, point);
            Assert.False(platform.HasActiveTimer);
            queued();
            Assert.Equal(horizontal && rtl ? 41 : 59, bar.Value);
            string step = horizontal && rtl ? "SmallDecrement" : "SmallIncrement";
            Assert.Equal(new[] { step, "value", step, "value", step, "value", "EndScroll" }, events);
        });
    }

    [Fact]
    public void PortableScrollBarRepeat_LeavePausesAndReentryResumesWithoutNewInitialDelay()
    {
        RunNativeScrollBar(true, (platform, form, bar, _, _) =>
        {
            Point inside = new(176, 8), outside = new(195, 8);
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, inside);
            platform.FireTimer();
            Assert.Equal(56, bar.Value);
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerMove, outside);
            platform.FireTimer();
            platform.FireTimer();
            Assert.Equal(56, bar.Value);
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerMove, inside);
            Assert.Equal(59, bar.Value);
            Assert.Equal(TimeSpan.FromMilliseconds(60), platform.LastTimerInterval);
            platform.FireTimer();
            Assert.Equal(62, bar.Value);
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerUp, inside);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableScrollBarRepeat_PageStopsAtMovingThumb(bool horizontal)
    {
        RunNativeScrollBar(horizontal, (platform, form, bar, _, _) =>
        {
            bar.SmallChange = 1;
            bar.LargeChange = 10;
            Point point = horizontal ? new(111, 8) : new(8, 87);
            List<ScrollEventType> events = [];
            bar.Scroll += (_, e) => events.Add(e.Type);
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, point);
            Assert.Equal(60, bar.Value);
            platform.FireTimer();
            platform.FireTimer();
            Assert.Equal(60, bar.Value);
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerUp, point);
            Assert.Equal(new[] { ScrollEventType.LargeIncrement, ScrollEventType.EndScroll }, events);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void PortableScrollBarRepeat_RetirementRejectsQueuedTick(int retirement)
    {
        RunNativeScrollBar(true, (platform, form, bar, _, _) =>
        {
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, new(176, 8));
            Action queued = platform.CaptureTimerCallback();
            switch (retirement)
            {
                case 0: bar.Value = 54; bar.Value = 53; break;
                case 1: bar.Maximum = 101; bar.Maximum = 100; break;
                case 2: bar.Width = 181; bar.Width = 180; break;
                case 3: bar.Capture = false; break;
                case 4: bar.Enabled = false; bar.Enabled = true; break;
                case 5: bar.Hide(); bar.Show(); break;
                case 6: form.Controls.Remove(bar); form.Controls.Add(bar); break;
                case 7: bar.Dispose(); break;
                case 8: form.Capture = true; break;
            }

            Assert.False(platform.HasActiveTimer);
            queued();
            Assert.Equal(53, bar.Value);
        });
    }

    [Fact]
    public void PortableScrollBarRepeat_CallbackSourceReplacementRetiresTimer()
    {
        RunNativeScrollBar(true, (platform, form, bar, _, _) =>
        {
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, new(176, 8));
            int calls = 0;
            bar.Scroll += (_, _) => { calls++; bar.Value = 70; };
            platform.FireTimer();
            Assert.Equal(70, bar.Value);
            Assert.Equal(1, calls);
            Assert.False(platform.HasActiveTimer);
        });
    }

    [Fact]
    public void PortableScrollBarRepeat_CallbackNewValueRemainsAuthoritative()
    {
        RunNativeScrollBar(true, (platform, form, bar, _, _) =>
        {
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, new(176, 8));
            bar.Scroll += (_, e) => e.NewValue = 65;
            platform.FireTimer();
            Assert.Equal(65, bar.Value);
            Assert.True(platform.HasActiveTimer);
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerUp, new(176, 8));
        });
    }

    [Fact]
    public void PortableScrollBarRepeat_NestedPressOwnsItsTimer()
    {
        RunNativeScrollBar(true, (platform, form, bar, _, _) =>
        {
            Point point = new(176, 8);
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, point);
            bool replace = true;
            bar.Scroll += (_, _) =>
            {
                if (!replace) return;
                replace = false;
                SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerUp, point);
                SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, point);
            };
            platform.FireTimer();
            Assert.Equal(56, bar.Value); // Only the nested down commits.
            Assert.Equal(TimeSpan.FromMilliseconds(400), platform.LastTimerInterval);
            platform.FireTimer();
            Assert.Equal(59, bar.Value);
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerUp, point);
        });
    }

    [Fact]
    public void PortableScrollBarRepeat_ThrowingCallbackCannotRepeatAgain()
    {
        RunNativeScrollBar(true, (platform, form, bar, _, _) =>
        {
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, new(176, 8));
            Action queued = platform.CaptureTimerCallback();
            bar.Scroll += (_, _) => throw new InvalidOperationException("original failure");
            Assert.Equal("original failure", Assert.Throws<InvalidOperationException>(() => platform.FireTimer()).Message);
            Assert.False(platform.HasActiveTimer);
            queued();
            Assert.Equal(53, bar.Value);
        });
    }

    [Fact]
    public void PortableScrollBarRepeat_ThumbDragDoesNotStartTimer()
    {
        RunNativeScrollBar(true, (platform, form, bar, _, _) =>
        {
            bar.Value = 0;
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, new(22, 8));
            Assert.False(platform.HasActiveTimer);
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerMove, new(160, 8));
            Assert.False(platform.HasActiveTimer);
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerUp, new(160, 8));
        });
    }
}
