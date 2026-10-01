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
    [InlineData(false, false, 44)]
    [InlineData(true, false, 44)]
    [InlineData(false, true, 56)]
    [InlineData(true, true, 56)]
    public void NativeScrollBar_WholeLinesUseSmallChangeAndSourceScrollOrder(bool horizontal, bool rtl, int expected)
    {
        RunNativeScrollBar(horizontal, (platform, form, bar, provider, _) =>
        {
            bar.RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
            var events = new List<(ScrollEventType Type, int Old, int New, ScrollOrientation Orientation)>();
            var values = new List<int>();
            int wheel = 0;
            bar.Scroll += (_, e) => { Assert.Equal(e.OldValue, bar.Value); events.Add((e.Type, e.OldValue, e.NewValue, e.ScrollOrientation)); };
            bar.ValueChanged += (_, _) => values.Add(bar.Value);
            bar.MouseWheel += (_, _) => wheel++;
            provider.Emit(BarPacket(horizontal, 2));
            Assert.Equal(expected, bar.Value);
            ScrollEventType type = rtl ? ScrollEventType.SmallIncrement : ScrollEventType.SmallDecrement;
            ScrollOrientation orientation = horizontal ? ScrollOrientation.HorizontalScroll : ScrollOrientation.VerticalScroll;
            Assert.Equal(new[] { type, type, ScrollEventType.EndScroll }, events.Select(e => e.Type));
            Assert.All(events, e => Assert.Equal(orientation, e.Orientation));
            Assert.Equal(new[] { rtl ? 53 : 47, expected }, values);
            Assert.Equal(0, wheel);
        });
    }

    [Fact]
    public void NativeScrollBar_FractionsStayInLinesNotValueOrWheelUnits()
    {
        RunNativeScrollBar(false, (_, _, bar, provider, _) =>
        {
            int events = 0;
            bar.Scroll += (_, _) => events++;
            provider.Emit(BarPacket(false, -.75));
            Assert.Equal(50, bar.Value);
            Assert.Equal(0, events);
            provider.Emit(BarPacket(false, -.5));
            Assert.Equal(53, bar.Value);
            Assert.Equal(2, events);
            provider.Emit(BarPacket(false, .75));
            Assert.Equal(53, bar.Value);
            provider.Emit(BarPacket(false, .5));
            Assert.Equal(50, bar.Value);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeScrollBar_ClampsToActualRangeAndDropsOutwardDebt(bool upper)
    {
        RunNativeScrollBar(false, (_, _, bar, provider, _) =>
        {
            bar.Value = upper ? 90 : 1;
            double outward = upper ? -1 : 1;
            provider.Emit(BarPacket(false, 2.75 * outward));
            Assert.Equal(upper ? 91 : 0, bar.Value);
            provider.Emit(BarPacket(false, -.75 * outward));
            Assert.Equal(upper ? 91 : 0, bar.Value);
            provider.Emit(BarPacket(false, -.5 * outward));
            Assert.Equal(upper ? 88 : 3, bar.Value);
        });
    }

    [Theory]
    [InlineData(0)] // Value away/back
    [InlineData(1)] // SmallChange away/back
    [InlineData(2)] // LargeChange away/back
    [InlineData(3)] // Minimum away/back
    [InlineData(4)] // Maximum away/back
    [InlineData(5)] // RightToLeft away/back
    [InlineData(6)] // Enabled away/back
    public void NativeScrollBar_SourceChangesCannotResurrectFractions(int change)
    {
        RunNativeScrollBar(false, (_, _, bar, provider, _) =>
        {
            provider.Emit(BarPacket(false, -.75));
            switch (change)
            {
                case 0: bar.Value = 51; bar.Value = 50; break;
                case 1: bar.SmallChange = 4; bar.SmallChange = 3; break;
                case 2: bar.LargeChange = 11; bar.LargeChange = 10; break;
                case 3: bar.Minimum = -1; bar.Minimum = 0; break;
                case 4: bar.Maximum = 101; bar.Maximum = 100; break;
                case 5: bar.RightToLeft = RightToLeft.Yes; bar.RightToLeft = RightToLeft.No; break;
                case 6: bar.Enabled = false; bar.Enabled = true; break;
            }
            provider.Emit(BarPacket(false, -.5));
            Assert.Equal(50, bar.Value);
            provider.Emit(BarPacket(false, -.5));
            Assert.Equal(53, bar.Value);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeScrollBar_CancelAndProviderGenerationRetireFractions(bool cancel)
    {
        RunNativeScrollBar(false, (_, _, bar, provider, _) =>
        {
            provider.Emit(BarPacket(false, -.75));
            if (cancel) provider.Emit(new(NativePointerEventKind.Cancel, 20, 20, 2, -1, 0, NativePointerModifiers.None));
            else provider.InputGeneration++;
            provider.Emit(BarPacket(false, -.5));
            Assert.Equal(50, bar.Value);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeScrollBar_BudgetRejectionPrecedesHoverAndPreservesPriorCarry(bool momentum)
    {
        RunNativeScrollBar(false, (platform, form, bar, provider, target) =>
        {
            provider.Emit(BarPacket(false, -.75) with { MomentumPhase = momentum ? 1U : 0U });
            LibreInputEvent accepted = target.LastInput;
            int callbacks = 0;
            bar.Scroll += (_, _) => callbacks++;
            bar.ValueChanged += (_, _) => callbacks++;
            bar.MouseEnter += (_, _) => callbacks++;
            bar.MouseLeave += (_, _) => callbacks++;
            Point pointer = Control.MousePosition;
            Keys modifiers = Control.ModifierKeys;
            LibreInputEvent rejected = accepted with
            {
                Position = momentum ? new(220, 170) : accepted.Position,
                NativeScroll = accepted.NativeScroll!.Value with { Y = -1024.25, Phase = momentum ? 0U : 1U,
                    MomentumPhase = momentum ? 4U : 0U }
            };
            Assert.Throws<PlatformNotSupportedException>(() => platform.SendControlInput(form, rejected));
            Assert.Equal(0, callbacks);
            Assert.Equal(50, bar.Value);
            Assert.Equal(pointer, Control.MousePosition);
            Assert.Equal(modifiers, Control.ModifierKeys);
            Assert.Equal(-1024.25, rejected.NativeScroll!.Value.Y);
            Assert.Same(accepted.NativeScroll!.Value.Stream, rejected.NativeScroll!.Value.Stream);
            provider.Emit(BarPacket(false, -.5) with { MomentumPhase = momentum ? 4U : 0U });
            Assert.Equal(53, bar.Value);
        });
    }

    [Fact]
    public void NativeScrollBar_ExactBudgetAndNearlyOneTailDoNotRoundToAnExtraLine()
    {
        RunNativeScrollBar(false, (_, _, bar, provider, _) =>
        {
            bar.Maximum = 10000;
            bar.Value = 5000;
            int lines = 0, ends = 0;
            bar.Scroll += (_, e) => { if (e.Type == ScrollEventType.EndScroll) ends++; else lines++; };
            provider.Emit(BarPacket(false, -Math.BitDecrement(1.0)));
            provider.Emit(BarPacket(false, -1024));
            Assert.Equal(8072, bar.Value);
            Assert.Equal(1024, lines);
            Assert.Equal(1, ends);
            provider.Emit(BarPacket(false, -.25));
            Assert.Equal(8075, bar.Value);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeScrollBar_UnsupportedPointsOrCrossAxisDoNotPartiallyChangeValue(bool points)
    {
        RunNativeScrollBar(false, (_, _, bar, provider, _) =>
        {
            int events = 0;
            bar.Scroll += (_, _) => events++;
            NativePointerEvent packet = points ? ScrollPacket(NativePointerScrollUnit.Points, 0, -3)
                : ScrollPacket(NativePointerScrollUnit.Lines, -1, -3);
            Assert.Throws<PlatformNotSupportedException>(() => provider.Emit(packet));
            Assert.Equal(50, bar.Value);
            Assert.Equal(0, events);
        });
    }

    [Fact]
    public void NativeScrollBar_DisabledSourceAndUnsupportedChildHostDoNotConsume()
    {
        RunNativeScrollBar(false, (_, _, bar, provider, _) =>
        {
            int events = 0;
            bar.Scroll += (_, _) => events++;
            bar.Enabled = false;
            Assert.Throws<PlatformNotSupportedException>(() => provider.Emit(BarPacket(false, -1)));
            Assert.Equal(50, bar.Value);
            bar.Enabled = true;
            using Label child = new() { Bounds = new Rectangle(0, 0, 25, 25) };
            bar.Controls.Add(child);
            Assert.Throws<PlatformNotSupportedException>(() => provider.Emit(BarPacket(false, -1)));
            Assert.Equal(50, bar.Value);
            Assert.Equal(0, events);
        });
    }

    [Fact]
    public void NativeScrollBar_HandlerNewValueUsesActualSetterAndRetiresOldFractions()
    {
        RunNativeScrollBar(false, (_, _, bar, provider, _) =>
        {
            bool first = true;
            var kinds = new List<ScrollEventType>();
            bar.Scroll += (_, e) => { kinds.Add(e.Type); if (first) { first = false; e.NewValue = 60; } };
            provider.Emit(BarPacket(false, -2.75));
            Assert.Equal(63, bar.Value);
            Assert.Equal(new[] { ScrollEventType.SmallIncrement, ScrollEventType.SmallIncrement, ScrollEventType.EndScroll }, kinds);
            provider.Emit(BarPacket(false, -.5));
            Assert.Equal(63, bar.Value);
            provider.Emit(BarPacket(false, -.5));
            Assert.Equal(66, bar.Value);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeScrollBar_SourceOverrideOrDisposalStopsOldScrollTail(bool dispose)
    {
        RunNativeScrollBar(false, (_, _, bar, provider, _) =>
        {
            int events = 0;
            bar.Scroll += (_, _) => { events++; if (dispose) bar.Dispose(); else { bar.Value = 61; bar.Value = 50; } };
            provider.Emit(BarPacket(false, -3));
            Assert.Equal(1, events);
            Assert.Equal(50, bar.Value);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeScrollBar_ValueCallbackFailurePreservesOnlyNestedCarry(bool nested)
    {
        RunNativeScrollBar(false, (_, _, bar, provider, _) =>
        {
            InvalidOperationException failure = new("scrollbar source callback");
            bool entered = false;
            EventHandler handler = (_, _) =>
            {
                if (entered) return;
                entered = true;
                if (nested) provider.Emit(BarPacket(false, -.5));
                throw failure;
            };
            bar.ValueChanged += handler;
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => provider.Emit(BarPacket(false, -1.25))));
            bar.ValueChanged -= handler;
            Assert.Equal(53, bar.Value);
            provider.Emit(BarPacket(false, -.5));
            Assert.Equal(nested ? 56 : 53, bar.Value);
        });
    }

    [Fact]
    public void NativeScrollBar_MomentumPinsTheSameConsumerOutsideItsBounds()
    {
        RunNativeScrollBar(false, (_, _, bar, provider, _) =>
        {
            provider.Emit(BarPacket(false, -.75) with { MomentumPhase = 1 });
            provider.Emit(BarPacket(false, -.5) with { MomentumPhase = 4, X = 230, Y = 170 });
            Assert.Equal(53, bar.Value);
            provider.Emit(BarPacket(false, -double.MaxValue) with { MomentumPhase = 16 });
            provider.Emit(BarPacket(false, -double.MaxValue) with { MomentumPhase = 4 });
            Assert.Equal(53, bar.Value);
        });
    }

    [Fact]
    public void NativeScrollBar_ZeroSmallChangeKeepsSourceEventsWithoutMovement()
    {
        RunNativeScrollBar(false, (_, _, bar, provider, _) =>
        {
            bar.LargeChange = 0;
            int events = 0, changed = 0;
            bar.Scroll += (_, _) => events++;
            bar.ValueChanged += (_, _) => changed++;
            provider.Emit(BarPacket(false, -2));
            Assert.Equal(0, bar.SmallChange);
            Assert.Equal(50, bar.Value);
            Assert.Equal(3, events);
            Assert.Equal(0, changed);
        });
    }

    [Fact]
    public void NativeScrollBar_UsesClampedSmallChangeAndPreservesProgrammaticValueAbovePageEnd()
    {
        RunNativeScrollBar(false, (_, _, bar, provider, _) =>
        {
            bar.LargeChange = 5;
            bar.SmallChange = 8;
            provider.Emit(BarPacket(false, -1));
            Assert.Equal(5, bar.SmallChange);
            Assert.Equal(55, bar.Value);
            bar.Value = 100; // Public Value may exceed Maximum-LargeChange+1.
            provider.Emit(BarPacket(false, 1));
            Assert.Equal(95, bar.Value);
            provider.Emit(BarPacket(false, -1));
            Assert.Equal(96, bar.Value);
        });
    }

    [Fact]
    public void NativeScrollBar_EndScrollNewValueIsNotDiscarded()
    {
        RunNativeScrollBar(false, (_, _, bar, provider, _) =>
        {
            bar.Scroll += (_, e) => { if (e.Type == ScrollEventType.EndScroll) e.NewValue = 70; };
            provider.Emit(BarPacket(false, -1.75));
            Assert.Equal(70, bar.Value);
            provider.Emit(BarPacket(false, -.5));
            Assert.Equal(70, bar.Value); // EndScroll override retired the old .75 line.
        });
    }

    private static NativePointerEvent BarPacket(bool horizontal, double lines)
        => ScrollPacket(NativePointerScrollUnit.Lines, horizontal ? lines : 0, horizontal ? 0 : lines);

    private static void RunNativeScrollBar(bool horizontal,
        Action<HeadlessPlatform, Form, ScrollBar, NativePointerTestContext, SourcePointerTarget> action)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ClientSize = new Size(240, 200), AutoScaleMode = AutoScaleMode.None, ShowIcon = false };
        using ScrollBar bar = horizontal ? new HScrollBar() : new VScrollBar();
        bar.Bounds = horizontal ? new Rectangle(10, 10, 180, 25) : new Rectangle(10, 10, 25, 140);
        bar.Minimum = 0;
        bar.Maximum = 100;
        bar.LargeChange = 10;
        bar.SmallChange = 3;
        bar.Value = 50;
        form.Controls.Add(bar);
        form.Show();
        using NativePointerTestContext provider = new();
        SourcePointerTarget target = new(form, input => platform.SendControlInput(form, input));
        using NativePointerInput subscription = new(provider, target);
        target.Subscription = subscription;
        action(platform, form, bar, provider, target);
    }
}
