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
    [InlineData(NativePointerScrollUnit.Points, -2.5, -3.75, -2, -3)]
    [InlineData(NativePointerScrollUnit.Lines, -.5, -.5, -3, -5)]
    [InlineData(NativePointerScrollUnit.Points, -9, 0, -9, 0)]
    [InlineData(NativePointerScrollUnit.Points, 0, -9, 0, -9)]
    public void NativeScroll_ActualScrollableConsumerUsesSourceUnits(
        NativePointerScrollUnit unit, double x, double y, int expectedX, int expectedY)
    {
        RunNativeScroll((platform, form, panel, provider, target) =>
        {
            panel.HorizontalScroll.SmallChange = 6;
            panel.VerticalScroll.SmallChange = 10;
            int wheel = 0;
            panel.MouseWheel += (_, _) => wheel++;
            provider.Emit(ScrollPacket(unit, x, y));
            Assert.Equal(new Point(expectedX, expectedY), panel.AutoScrollPosition);
            Assert.Equal(0, wheel);
            Assert.Equal(x, target.LastInput.NativeScroll!.Value.X);
            Assert.Equal(y, target.LastInput.NativeScroll!.Value.Y);
            Assert.Equal(default, target.LastInput.Delta);
        });
    }

    [Fact]
    public void NativeScroll_FractionsReverseAndClampWithoutDebt()
    {
        RunNativeScroll((_, _, panel, provider, _) =>
        {
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -.75));
            Assert.Equal(0, panel.AutoScrollPosition.Y);
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -.75));
            Assert.Equal(-1, panel.AutoScrollPosition.Y);
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, .75));
            Assert.Equal(-1, panel.AutoScrollPosition.Y);
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, .75));
            Assert.Equal(0, panel.AutoScrollPosition.Y);
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, .9));
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -.75));
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -.75));
            Assert.Equal(-1, panel.AutoScrollPosition.Y);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeScroll_CancellationAndProviderGenerationRetireFractions(bool cancel)
    {
        RunNativeScroll((_, _, panel, provider, _) =>
        {
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -.75));
            if (cancel)
                provider.Emit(new(NativePointerEventKind.Cancel, 20, 20, 2, -1, 0, NativePointerModifiers.None));
            else
                provider.InputGeneration++;
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -.75));
            Assert.Equal(0, panel.AutoScrollPosition.Y);
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -.75));
            Assert.Equal(-1, panel.AutoScrollPosition.Y);
        });
    }

    [Fact]
    public void NativeScroll_UnitAndMetricChangesRetireFractions()
    {
        RunNativeScroll((_, _, panel, provider, _) =>
        {
            panel.VerticalScroll.SmallChange = 5;
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -.75));
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Lines, 0, -.1));
            Assert.Equal(0, panel.AutoScrollPosition.Y);
            panel.VerticalScroll.SmallChange = 6;
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Lines, 0, -.1));
            Assert.Equal(0, panel.AutoScrollPosition.Y);
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Lines, 0, -.1));
            Assert.Equal(-1, panel.AutoScrollPosition.Y);
        });
    }

    [Fact]
    public void NativeScroll_UnsupportedAxisCannotPartiallyMoveTheSupportedAxis()
    {
        RunNativeScroll((_, _, panel, provider, _) =>
        {
            panel.AutoScrollMinSize = new Size(0, 500);
            Assert.False(panel.HorizontalScroll.Visible);
            Assert.True(panel.VerticalScroll.Visible);
            Assert.Throws<PlatformNotSupportedException>(() => provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, -3, -8)));
            Assert.Equal(Point.Empty, panel.AutoScrollPosition);
        });
    }

    [Fact]
    public void NativeScroll_PhasesRemainExplicitBeforeCharactersOrSource()
    {
        RunNativeScroll((_, _, panel, provider, target) =>
        {
            int flushed = 0;
            target.Flushing = () => flushed++;
            Assert.Throws<PlatformNotSupportedException>(() => provider.Emit(
                ScrollPacket(NativePointerScrollUnit.Points, 0, -8) with { ScrollPhase = 1 }));
            Assert.Equal(0, flushed);
            Assert.Equal(Point.Empty, panel.AutoScrollPosition);
        });
    }

    [Fact]
    public void NativeScroll_CharacterCancellationRejectsAnOldPacket()
    {
        RunNativeScroll((_, _, panel, provider, target) =>
        {
            target.Flushing = () => provider.InputGeneration++;
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -8));
            Assert.Equal(Point.Empty, panel.AutoScrollPosition);
            Assert.Equal(0, target.Deliveries);
        });
    }

    [Fact]
    public void NativeScroll_LegacyWheelKeepsItsOriginalPixelPolicy()
    {
        RunNativeScroll((platform, form, panel, _, _) =>
        {
            int wheel = 0;
            panel.MouseWheel += (_, e) => { wheel++; Assert.Equal(-120, e.Delta); };
            platform.SendControlInput(form, new(LibreInputEventKind.PointerWheel, 1, LibreInputModifiers.None,
                LibreKey.Unknown, null, new(20, 20), new(0, -120), LibrePointerButton.None));
            Assert.Equal(-120, panel.AutoScrollPosition.Y);
            Assert.Equal(1, wheel);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeScroll_SourceFailureRetiresOnlyItsPublishedCarry(bool nested)
    {
        RunNativeScroll((_, _, panel, provider, _) =>
        {
            InvalidOperationException failure = new("source invalidation");
            bool entered = false;
            InvalidateEventHandler handler = (_, _) =>
            {
                if (entered) return;
                entered = true;
                if (nested) provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -.5));
                throw failure;
            };
            panel.Invalidated += handler;
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => provider.Emit(
                ScrollPacket(NativePointerScrollUnit.Points, 0, -1.25))));
            panel.Invalidated -= handler;
            Assert.Equal(-1, panel.AutoScrollPosition.Y);
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -.5));
            Assert.Equal(nested ? -2 : -1, panel.AutoScrollPosition.Y);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NativeScroll_DirectMalformedMetadataCannotChangeSourceState(int invalid)
    {
        RunNativeScroll((platform, form, panel, provider, target) =>
        {
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, 0));
            Point pointer = Control.MousePosition;
            Keys modifiers = Control.ModifierKeys;
            LibreNativePointerMetadata raw = target.LastInput.NativePointer!.Value;
            raw = invalid switch
            {
                0 => raw with { X = double.NaN },
                1 => raw with { Y = double.PositiveInfinity },
                2 => raw with { Timestamp = -1 },
                _ => raw with { Modifiers = (LibreNativePointerModifiers)256 }
            };
            LibreInputEvent malformed = target.LastInput with { NativePointer = raw, Position = new(90, 90) };
            Assert.Throws<ArgumentException>(() => platform.SendControlInput(form, malformed));
            Assert.Equal(pointer, Control.MousePosition);
            Assert.Equal(modifiers, Control.ModifierKeys);
            Assert.Equal(Point.Empty, panel.AutoScrollPosition);
        });
    }

    [Fact]
    public void NativeScroll_TargetReplacementCannotInheritFractions()
    {
        RunNativeScroll((_, form, panel, provider, _) =>
        {
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -.75));
            panel.Dispose();
            using Panel replacement = new() { Bounds = new Rectangle(10, 10, 180, 140), AutoScroll = true, AutoScrollMinSize = new Size(500, 500) };
            form.Controls.Add(replacement);
            replacement.BringToFront();
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -.75));
            Assert.Equal(0, replacement.AutoScrollPosition.Y);
        });
    }

    private static NativePointerEvent ScrollPacket(NativePointerScrollUnit unit, double x, double y)
        => new(NativePointerEventKind.Scroll, 20.25, 20.5, 1.25, -1, 0, NativePointerModifiers.Shift,
            x, y, unit) { ScrollProtocol = NativePointerScrollProtocol.AppKit };

    private static void RunNativeScroll(Action<HeadlessPlatform, Form, Panel, NativePointerTestContext, SourcePointerTarget> action)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ClientSize = new Size(240, 200), AutoScaleMode = AutoScaleMode.None, ShowIcon = false };
        using Panel panel = new() { Bounds = new Rectangle(10, 10, 180, 140), AutoScroll = true, AutoScrollMinSize = new Size(500, 500) };
        form.Controls.Add(panel);
        form.Show();
        using NativePointerTestContext provider = new();
        SourcePointerTarget target = new(form, input => platform.SendControlInput(form, input));
        using NativePointerInput subscription = new(provider, target);
        target.Subscription = subscription;
        action(platform, form, panel, provider, target);
    }
}
