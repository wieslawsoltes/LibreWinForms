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
    [InlineData(false)]
    [InlineData(true)]
    public void NativeScroll_NormalRetargetsWhileMomentumKeepsItsOriginalSource(bool momentum)
    {
        RunNativeScroll((_, form, panel, provider, _) =>
        {
            using Panel second = AddSecondScrollPanel(form, panel);
            provider.Emit(ScrollGesture(1, momentum, -2));
            provider.Emit(ScrollGesture(4, momentum, -3) with { X = 150 });
            Assert.Equal(momentum ? -5 : -2, panel.AutoScrollPosition.Y);
            Assert.Equal(momentum ? 0 : -3, second.AutoScrollPosition.Y);
            provider.Emit(ScrollGesture(8, momentum, -4) with { X = 150 });
            Assert.Equal(momentum ? -9 : -2, panel.AutoScrollPosition.Y);
            Assert.Equal(momentum ? 0 : -7, second.AutoScrollPosition.Y);
        });
    }

    [Fact]
    public void NativeScroll_MomentumContinuesOutsideTheViewAndAfterPointerLeave()
    {
        RunNativeScroll((_, _, panel, provider, _) =>
        {
            provider.Emit(ScrollGesture(1, true, -2));
            provider.Emit(new(NativePointerEventKind.Leave, 400, 400, 2, -1, 0, NativePointerModifiers.None));
            provider.Emit(ScrollGesture(4, true, -3) with { X = 400, Y = 400 });
            Assert.Equal(-5, panel.AutoScrollPosition.Y);
        });
    }

    [Fact]
    public void NativeScroll_NormalHitTargetDoesNotBorrowButtonCapture()
    {
        RunNativeScroll((_, form, panel, provider, _) =>
        {
            using Panel second = AddSecondScrollPanel(form, panel);
            panel.Capture = true;
            provider.Emit(ScrollGesture(1, false, -3) with { X = 150 });
            Assert.Equal(0, panel.AutoScrollPosition.Y);
            Assert.Equal(-3, second.AutoScrollPosition.Y);
            Assert.True(panel.Capture);
        });
    }

    [Fact]
    public void NativeScroll_NormalEndHandsFractionsToMomentumButMomentumEndRetiresThem()
    {
        RunNativeScroll((_, _, panel, provider, _) =>
        {
            provider.Emit(ScrollGesture(1, false, -.75));
            provider.Emit(ScrollGesture(8, false, 0));
            provider.Emit(ScrollGesture(1, true, -.5));
            Assert.Equal(-1, panel.AutoScrollPosition.Y);
            provider.Emit(ScrollGesture(8, true, -.25));
            provider.Emit(ScrollGesture(4, true, -20)); // Ended gesture cannot revive.
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -.5));
            Assert.Equal(-1, panel.AutoScrollPosition.Y);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeScroll_PhaseCancellationRetiresDebtWithoutApplyingCancellationVector(bool momentum)
    {
        RunNativeScroll((_, _, panel, provider, _) =>
        {
            provider.Emit(ScrollGesture(1, momentum, -.75));
            provider.Emit(ScrollGesture(16, momentum, -20));
            if (momentum)
                provider.Emit(ScrollGesture(4, true, -20));
            provider.Emit(ScrollGesture(1, momentum, -.5));
            Assert.Equal(0, panel.AutoScrollPosition.Y);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void NativeScroll_RetiredMomentumNeverHitsAReplacementConsumer(int retirement)
    {
        RunNativeScroll((_, form, panel, provider, _) =>
        {
            using Panel second = AddSecondScrollPanel(form, panel);
            provider.Emit(ScrollGesture(1, true, -.75));
            switch (retirement)
            {
                case 0: panel.Dispose(); break;
                case 1: panel.Enabled = false; break;
                case 2: form.Controls.Remove(panel); break;
                case 3: provider.InputGeneration++; break;
                case 4:
                    provider.Emit(new(NativePointerEventKind.Cancel, 20, 20, 2, -1, 0, NativePointerModifiers.None));
                    break;
            }

            provider.Emit(ScrollGesture(4, true, -10) with { X = 150 });
            provider.Emit(ScrollGesture(8, true, -10) with { X = 150 });
            Assert.Equal(0, second.AutoScrollPosition.Y);
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -.5) with { X = 150 });
            Assert.Equal(0, second.AutoScrollPosition.Y);
        });
    }

    [Fact]
    public void NativeScroll_NewDirectInputCancelsMomentumWithoutLosingItsOwnFraction()
    {
        RunNativeScroll((_, _, panel, provider, _) =>
        {
            provider.Emit(ScrollGesture(1, true, -.75));
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -.5));
            provider.Emit(ScrollGesture(4, true, -20));
            provider.Emit(ScrollGesture(16, true, -20));
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -.5));
            Assert.Equal(-1, panel.AutoScrollPosition.Y);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void NativeScroll_InvalidDirectPhaseMetadataLeavesGestureAndPointerUntouched(int invalid)
    {
        RunNativeScroll((platform, form, panel, provider, target) =>
        {
            provider.Emit(ScrollGesture(1, true, -.75));
            Point position = Control.MousePosition;
            LibreNativeScrollMetadata scroll = target.LastInput.NativeScroll!.Value;
            scroll = invalid switch
            {
                0 => scroll with { Phase = 3, MomentumPhase = 0 },
                1 => scroll with { Phase = 64, MomentumPhase = 0 },
                2 => scroll with { MomentumPhase = 3 },
                3 => scroll with { MomentumPhase = 32 },
                4 => scroll with { MomentumPhase = 64 },
                _ => scroll with { Phase = 1 }
            };
            LibreInputEvent bad = target.LastInput with { NativeScroll = scroll, Position = new(90, 90) };
            Assert.Throws<ArgumentException>(() => platform.SendControlInput(form, bad));
            Assert.Equal(position, Control.MousePosition);
            provider.Emit(ScrollGesture(4, true, -.5));
            Assert.Equal(-1, panel.AutoScrollPosition.Y);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeScroll_FailedOrEndedOuterMomentumPreservesNestedReplacement(bool fail)
    {
        RunNativeScroll((_, _, panel, provider, _) =>
        {
            provider.Emit(ScrollGesture(1, true, -.25));
            bool entered = false;
            InvalidOperationException failure = new("old momentum callback");
            InvalidateEventHandler handler = (_, _) =>
            {
                if (entered) return;
                entered = true;
                provider.Emit(ScrollGesture(1, true, -.75));
                if (fail) throw failure;
            };
            panel.Invalidated += handler;
            Action end = () => provider.Emit(ScrollGesture(8, true, -1));
            if (fail) Assert.Same(failure, Assert.Throws<InvalidOperationException>(end));
            else end();
            panel.Invalidated -= handler;
            provider.Emit(ScrollGesture(4, true, -.5));
            Assert.Equal(-2, panel.AutoScrollPosition.Y);
        });
    }

    [Fact]
    public void NativeScroll_MayBeginAndStationaryRetainTheNormalGesture()
    {
        RunNativeScroll((_, _, panel, provider, _) =>
        {
            provider.Emit(ScrollGesture(32, false, 0));
            provider.Emit(ScrollGesture(1, false, -.75));
            provider.Emit(ScrollGesture(2, false, 0));
            provider.Emit(ScrollGesture(4, false, -.5));
            Assert.Equal(-1, panel.AutoScrollPosition.Y);
        });
    }

    private static NativePointerEvent ScrollGesture(uint phase, bool momentum, double y)
        => ScrollPacket(NativePointerScrollUnit.Points, 0, y) with
        { ScrollPhase = momentum ? 0 : phase, MomentumPhase = momentum ? phase : 0 };

    private static Panel AddSecondScrollPanel(Form form, Panel first)
    {
        first.Width = 100;
        Panel second = new() { Bounds = new Rectangle(125, 10, 100, 140), AutoScroll = true, AutoScrollMinSize = new Size(500, 500) };
        form.Controls.Add(second);
        return second;
    }
}
