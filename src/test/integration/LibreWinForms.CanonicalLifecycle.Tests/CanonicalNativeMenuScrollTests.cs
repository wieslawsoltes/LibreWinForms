// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;
using LibreWinForms.ProGPU;
using LibreWinForms.ProGPU.Tests;
using ProGPU.Backend;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Fact]
    public void NativeMenuScroll_PointsRetainFractionsAndNeverInventMouseWheel()
    {
        RunNativeMenuScroll((menu, items, provider, packet) =>
        {
            Rectangle[] original = items.Select(item => item.Bounds).ToArray();
            int wheels = 0;
            menu.MouseWheel += (_, _) => wheels++;
            provider.Emit(packet with { ScrollY = -.75 });
            Assert.Equal(original, items.Select(item => item.Bounds));
            provider.Emit(packet with { ScrollY = -.75 });
            for (int i = 0; i < items.Length; i++)
                Assert.Equal(original[i].Top - 1, items[i].Bounds.Top);
            Assert.Equal(0, wheels);
        });
    }

    [Fact]
    public void NativeMenuScroll_LinesRecomputeActualVariableItemSteps()
    {
        RunNativeMenuScroll((_, items, provider, packet) =>
        {
            int first = items[0].Bounds.Top;
            int one = items[1].Bounds.Top - first;
            int two = items[2].Bounds.Top - items[1].Bounds.Top;
            Assert.NotEqual(one, two);
            provider.Emit(packet with { ScrollUnit = NativePointerScrollUnit.Lines, ScrollY = -2 });
            Assert.Equal(first - one - two, items[0].Bounds.Top);
            provider.Emit(packet with { ScrollUnit = NativePointerScrollUnit.Lines, ScrollY = 1 });
            Assert.Equal(first - one, items[0].Bounds.Top);
        });
    }

    [Fact]
    public void NativeMenuScroll_FractionalLinesDoNotBecomeFractionalPixels()
    {
        RunNativeMenuScroll((_, items, provider, packet) =>
        {
            int first = items[0].Bounds.Top;
            int step = items[1].Bounds.Top - first;
            provider.Emit(packet with { ScrollUnit = NativePointerScrollUnit.Lines, ScrollY = -.5 });
            Assert.Equal(first, items[0].Bounds.Top);
            provider.Emit(packet with { ScrollUnit = NativePointerScrollUnit.Lines, ScrollY = -.5 });
            Assert.Equal(first - step, items[0].Bounds.Top);
        });
    }

    [Fact]
    public void NativeMenuScroll_PointsClampAtActualHalfOpenDisplayEdges()
    {
        RunNativeMenuScroll((menu, items, provider, packet) =>
        {
            provider.Emit(packet with { ScrollY = -1e12 });
            Assert.Equal(menu.DisplayRectangle.Bottom - 1, items.Max(item => item.Bounds.Bottom));
            Rectangle[] bottom = items.Select(item => item.Bounds).ToArray();
            provider.Emit(packet with { ScrollY = -.75 });
            Assert.Equal(bottom, items.Select(item => item.Bounds));
            provider.Emit(packet with { ScrollY = 1e12 });
            Assert.Equal(menu.DisplayRectangle.Top, items.Min(item => item.Bounds.Top));
        });
    }

    [Fact]
    public void NativeMenuScroll_HorizontalRejectionCannotMoveVerticalItems()
    {
        RunNativeMenuScroll((_, items, provider, packet) =>
        {
            Rectangle[] before = items.Select(item => item.Bounds).ToArray();
            Assert.Throws<PlatformNotSupportedException>(() => provider.Emit(packet with { ScrollX = -.25, ScrollY = -8 }));
            Assert.Equal(before, items.Select(item => item.Bounds));
        });
    }

    [Fact]
    public void NativeMenuScroll_MomentumUsesThePopupAfterPointerLeaveAndCancelDropsTail()
    {
        RunNativeMenuScroll((_, items, provider, packet) =>
        {
            int first = items[0].Bounds.Top;
            provider.Emit(packet with { MomentumPhase = 1, ScrollY = -.75 });
            provider.Emit(new(NativePointerEventKind.Leave, 900, 900, 2, -1, 0, NativePointerModifiers.None));
            provider.Emit(packet with { MomentumPhase = 4, ScrollY = -.5, X = 900, Y = 900 });
            Assert.Equal(first - 1, items[0].Bounds.Top);
            provider.Emit(packet with { MomentumPhase = 16, ScrollY = -20 });
            provider.Emit(packet with { MomentumPhase = 4, ScrollY = -20 });
            Assert.Equal(first - 1, items[0].Bounds.Top);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeMenuScroll_ItemCallbackRetiresOldMovementAfterTheExactFirstWrite(bool close)
    {
        RunNativeMenuScroll((menu, items, provider, packet) =>
        {
            Rectangle[] before = items.Select(item => item.Bounds).ToArray();
            items[0].Moved = () =>
            {
                items[0].Moved = null;
                if (close) menu.Close();
                else provider.Emit(new(NativePointerEventKind.Cancel, packet.X, packet.Y, 2, -1, 0, NativePointerModifiers.None));
            };
            provider.Emit(packet with { ScrollY = -5 });
            Assert.Equal(before[0].Top - 5, items[0].Bounds.Top);
            for (int i = 1; i < items.Length; i++)
                Assert.Equal(before[i], items[i].Bounds);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeMenuScroll_OldItemFailureCannotRetireNestedFraction(bool fail)
    {
        RunNativeMenuScroll((_, items, provider, packet) =>
        {
            int first = items[0].Bounds.Top, second = items[1].Bounds.Top;
            InvalidOperationException failure = new("old item positioning");
            items[0].Moved = () =>
            {
                items[0].Moved = null;
                provider.Emit(packet with { ScrollY = -.75 });
                if (fail) throw failure;
            };
            Action move = () => provider.Emit(packet with { ScrollY = -5 });
            if (fail) Assert.Same(failure, Assert.Throws<InvalidOperationException>(move));
            else move();
            Assert.Equal(first - 5, items[0].Bounds.Top);
            Assert.Equal(second, items[1].Bounds.Top);
            provider.Emit(packet with { ScrollY = -.5 });
            Assert.Equal(first - 6, items[0].Bounds.Top);
            Assert.Equal(second - 1, items[1].Bounds.Top);
        });
    }

    [Fact]
    public void NativeMenuScroll_ItemLayoutMutationStopsTheRemainingOldPositions()
    {
        RunNativeMenuScroll((_, items, provider, packet) =>
        {
            Rectangle third = items[2].Bounds;
            items[0].Moved = () => { items[0].Moved = null; items[1].Height += 7; };
            provider.Emit(packet with { ScrollY = -5 });
            Assert.Equal(third, items[2].Bounds);
        });
    }

    [Fact]
    public void NativeMenuScroll_SameCountRemoveReinsertRetiresTheLayoutGeneration()
    {
        RunNativeMenuScroll((menu, items, provider, packet) =>
        {
            Rectangle second = items[1].Bounds;
            items[0].Moved = () =>
            {
                items[0].Moved = null;
                menu.Items.Remove(items[^1]);
                menu.Items.Add(items[^1]);
            };
            provider.Emit(packet with { ScrollY = -5 });
            Assert.Equal(second, items[1].Bounds);
        });
    }

    [Fact]
    public void NativeMenuScroll_UnavailableAdjacentItemKeepsItsOriginalStoredTop()
    {
        RunNativeMenuScroll((menu, items, provider, packet) =>
        {
            items[1].Available = false;
            int first = items[0].Bounds.Top;
            menu.SuspendLayout();
            items[1].SetSourceTop(first + 7);
            menu.ResumeLayout(false);
            Assert.NotEqual(7, items[2].Bounds.Top - first);
            provider.Emit(packet with { ScrollUnit = NativePointerScrollUnit.Lines, ScrollY = -1 });
            Assert.Equal(first - 7, items[0].Bounds.Top);
        });
    }

    [Fact]
    public void NativeMenuScroll_MissingVisibleItemTopDoesNotInventALineHeight()
    {
        RunNativeMenuScroll((_, items, provider, packet) =>
        {
            items[0].Height = 300;
            provider.Emit(packet with { ScrollY = -10 });
            Rectangle[] before = items.Select(item => item.Bounds).ToArray();
            Assert.Throws<PlatformNotSupportedException>(() => provider.Emit(packet with
                { ScrollUnit = NativePointerScrollUnit.Lines, ScrollY = -1 }));
            Assert.Equal(before, items.Select(item => item.Bounds));
        });
    }

    [Fact]
    public void NativeMenuScroll_ItemGenerationAndUnitChangesRetireFractionalDebt()
    {
        RunNativeMenuScroll((menu, items, provider, packet) =>
        {
            provider.Emit(packet with { ScrollY = -.75 });
            menu.SuspendLayout();
            menu.Items.Remove(items[^1]);
            menu.Items.Add(items[^1]);
            menu.ResumeLayout(false);
            int first = items[0].Bounds.Top;
            provider.Emit(packet with { ScrollY = -.5 });
            Assert.Equal(first, items[0].Bounds.Top);
            provider.Emit(packet with { ScrollY = -.5, ScrollUnit = NativePointerScrollUnit.Lines });
            Assert.Equal(first, items[0].Bounds.Top);
            provider.Emit(packet with { ScrollY = -.5, ScrollUnit = NativePointerScrollUnit.Lines });
            Assert.True(items[0].Bounds.Top < first - 1);
        });
    }

    private static void RunNativeMenuScroll(Action<ContextMenuStrip, NativeScrollMenuItem[], NativePointerTestContext, NativePointerEvent> action)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using ContextMenuStrip menu = new()
        {
            AutoClose = false, AutoSize = false, Size = new Size(180, 140), MaximumSize = new Size(180, 140),
            ShowImageMargin = false, ShowCheckMargin = false
        };
        NativeScrollMenuItem[] items = [new(24), new(40), new(28), new(48), new(32), new(36)];
        menu.Items.AddRange(items);
        owner.Show();
        menu.Show(owner, new Point(10, 10));
        Rectangle display = menu.DisplayRectangle;
        Assert.True(display.Height < items.Sum(item => item.Height));
        Assert.Equal(display.Top, items[0].Bounds.Top);
        using NativePointerTestContext provider = new();
        SourcePointerTarget target = new(menu, input => platform.SendControlInput(menu, input));
        using NativePointerInput subscription = new(provider, target);
        target.Subscription = subscription;
        NativePointerEvent packet = ScrollPacket(NativePointerScrollUnit.Points, 0, 0) with
        { X = display.Left + 10.25, Y = display.Top + 10.25 };
        action(menu, items, provider, packet);
    }

    private sealed class NativeScrollMenuItem : ToolStripMenuItem
    {
        internal Action? Moved { get; set; }
        internal NativeScrollMenuItem(int height)
        {
            Text = "Menu row";
            AutoSize = false;
            Size = new Size(120, height);
            Margin = Padding.Empty;
        }

        internal void SetSourceTop(int top) => SetBounds(new Rectangle(Bounds.X, top, Width, Height));

        protected override void OnBoundsChanged()
        {
            base.OnBoundsChanged();
            Moved?.Invoke();
        }
    }
}
