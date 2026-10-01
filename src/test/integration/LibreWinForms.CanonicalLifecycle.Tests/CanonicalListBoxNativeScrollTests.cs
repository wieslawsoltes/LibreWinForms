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
    [InlineData(NativePointerScrollUnit.Lines)]
    [InlineData(NativePointerScrollUnit.Points)]
    public void NativeListScroll_ActualRowsRemainAlignedWithHitAndSelection(NativePointerScrollUnit unit)
    {
        RunNativeListScroll((_, _, list, provider, _) =>
        {
            int height = list.GetItemHeight(0);
            int selected = list.SelectedIndex;
            int changes = 0, wheel = 0;
            list.SelectedIndexChanged += (_, _) => changes++;
            list.MouseWheel += (_, _) => wheel++;
            double step = unit == NativePointerScrollUnit.Points ? height : 1;
            provider.Emit(ScrollPacket(unit, 0, -.75 * step));
            Assert.Equal(0, list.TopIndex);
            provider.Emit(ScrollPacket(unit, 0, -.75 * step));
            Assert.Equal(1, list.TopIndex);
            Assert.Equal(0, list.GetItemRectangle(1).Top);
            Assert.Equal(height, list.GetItemRectangle(2).Top);
            Assert.Equal(1, list.IndexFromPoint(1, 0));
            Assert.Equal(2, list.IndexFromPoint(1, height));
            Assert.Equal(selected, list.SelectedIndex);
            Assert.Equal(0, changes);
            Assert.Equal(0, wheel);
        });
    }

    [Theory]
    [InlineData(NativePointerScrollUnit.Lines)]
    [InlineData(NativePointerScrollUnit.Points)]
    public void NativeListScroll_EdgesAndReversalDoNotRetainOutwardDebt(NativePointerScrollUnit unit)
    {
        RunNativeListScroll((_, _, list, provider, _) =>
        {
            double step = unit == NativePointerScrollUnit.Points ? list.GetItemHeight(0) : 1;
            provider.Emit(ScrollPacket(unit, 0, .75 * step));
            provider.Emit(ScrollPacket(unit, 0, -.75 * step));
            Assert.Equal(0, list.TopIndex);
            provider.Emit(ScrollPacket(unit, 0, -.75 * step));
            Assert.Equal(1, list.TopIndex);
            provider.Emit(ScrollPacket(unit, 0, .75 * step));
            Assert.Equal(1, list.TopIndex);
            provider.Emit(ScrollPacket(unit, 0, .75 * step));
            Assert.Equal(0, list.TopIndex);
            provider.Emit(ScrollPacket(unit, 0, -double.MaxValue / 2));
            Assert.Equal(9, list.TopIndex);
            provider.Emit(ScrollPacket(unit, 0, -.75 * step));
            provider.Emit(ScrollPacket(unit, 0, .75 * step));
            Assert.Equal(9, list.TopIndex);
            provider.Emit(ScrollPacket(unit, 0, .75 * step));
            Assert.Equal(8, list.TopIndex);
        });
    }

    [Theory]
    [InlineData(0)] // collection version
    [InlineData(1)] // same-count item replacement (does not change ItemArray.Version)
    [InlineData(2)] // actual row metric
    [InlineData(3)] // viewport
    [InlineData(4)] // ordinary TopIndex change and return to the old numeric frame
    public void NativeListScroll_SourceFrameChangesRetireFractions(int change)
    {
        RunNativeListScroll((_, _, list, provider, _) =>
        {
            int height = list.GetItemHeight(0);
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -.75 * height));
            using Font replacement = new(list.Font.FontFamily, list.Font.Size + 7, list.Font.Style);
            switch (change)
            {
                case 0: list.Items.Add("added"); break;
                case 1: list.Items[0] = "replacement"; break;
                case 2: list.Font = replacement; break;
                case 3: list.Height += height; break;
                case 4: list.TopIndex = 1; list.TopIndex = 0; break;
            }

            int nextHeight = list.GetItemHeight(0);
            if (change == 2) Assert.NotEqual(height, nextHeight);
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -.5 * nextHeight));
            Assert.Equal(0, list.TopIndex);
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -.5 * nextHeight));
            Assert.Equal(1, list.TopIndex);
        });
    }

    [Fact]
    public void NativeListScroll_ReplacementRetiresCarryBeforeFormattingCallback()
    {
        RunNativeListScroll((_, _, list, provider, _) =>
        {
            list.FormattingEnabled = true;
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Lines, 0, -.75));
            bool entered = false;
            list.Format += (_, _) =>
            {
                if (entered) return;
                entered = true;
                provider.Emit(ScrollPacket(NativePointerScrollUnit.Lines, 0, -.5));
                Assert.Equal(0, list.TopIndex);
            };
            list.Items[0] = "replacement";
            Assert.True(entered);
            Assert.Equal(0, list.TopIndex);
        });
    }

    [Fact]
    public void NativeListScroll_PointScaleChangesCannotBorrowOldPixelFractions()
    {
        RunNativeListScroll((platform, form, list, provider, target) =>
        {
            int height = list.GetItemHeight(0);
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -.75 * height));
            LibreInputEvent input = target.LastInput;
            input = input with { NativeScroll = input.NativeScroll!.Value with { Y = -.25 * height, PointScale = 2 } };
            platform.SendControlInput(form, input);
            Assert.Equal(0, list.TopIndex);
            platform.SendControlInput(form, input);
            Assert.Equal(1, list.TopIndex);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeListScroll_CancelOrProviderGenerationRetiresRowFractions(bool cancel)
    {
        RunNativeListScroll((_, _, list, provider, _) =>
        {
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Lines, 0, -.75));
            if (cancel)
                provider.Emit(new(NativePointerEventKind.Cancel, 20, 20, 2, -1, 0, NativePointerModifiers.None));
            else
                provider.InputGeneration++;
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Lines, 0, -.5));
            Assert.Equal(0, list.TopIndex);
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Lines, 0, -.5));
            Assert.Equal(1, list.TopIndex);
        });
    }

    [Fact]
    public void NativeListScroll_MomentumKeepsItsActualListOutsideItsBounds()
    {
        RunNativeListScroll((_, _, list, provider, _) =>
        {
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Lines, 0, -.75) with { MomentumPhase = 1 });
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Lines, 0, -.5) with { MomentumPhase = 4, X = 230, Y = 190 });
            Assert.Equal(1, list.TopIndex);
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Lines, 0, -100) with { MomentumPhase = 16 });
            Assert.Equal(1, list.TopIndex);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeListScroll_FailureRetiresOnlyTheOriginalCarry(bool nested)
    {
        RunNativeListScroll((_, _, list, provider, _) =>
        {
            InvalidOperationException error = new("list invalidation");
            bool entered = false;
            InvalidateEventHandler handler = (_, _) =>
            {
                if (entered) return;
                entered = true;
                if (nested) provider.Emit(ScrollPacket(NativePointerScrollUnit.Lines, 0, -.5));
                throw error;
            };
            list.Invalidated += handler;
            Assert.Same(error, Assert.Throws<InvalidOperationException>(() => provider.Emit(
                ScrollPacket(NativePointerScrollUnit.Lines, 0, -1.25))));
            list.Invalidated -= handler;
            Assert.Equal(1, list.TopIndex);
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Lines, 0, -.5));
            Assert.Equal(nested ? 2 : 1, list.TopIndex);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeListScroll_InvalidationReplacementCannotInheritOldCarry(bool dispose)
    {
        RunNativeListScroll((_, form, list, provider, _) =>
        {
            using ListBox replacement = new() { Bounds = list.Bounds, BorderStyle = BorderStyle.None, IntegralHeight = false };
            replacement.Items.AddRange(Enumerable.Range(0, 12).Select(i => (object)$"new {i}").ToArray());
            bool entered = false;
            list.Invalidated += (_, _) =>
            {
                if (entered) return;
                entered = true;
                if (dispose)
                {
                    list.Dispose();
                    form.Controls.Add(replacement);
                    replacement.BringToFront();
                }
                else
                    list.Items[5] = "changed during invalidation";
            };
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Lines, 0, -1.75));
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Lines, 0, -.5));
            Assert.Equal(dispose ? 0 : 1, dispose ? replacement.TopIndex : list.TopIndex);
        });
    }

    [Fact]
    public void NativeListScroll_HorizontalComponentRejectsBeforeAnyRowMutation()
    {
        RunNativeListScroll((_, _, list, provider, _) =>
        {
            Assert.Throws<PlatformNotSupportedException>(() => provider.Emit(ScrollPacket(NativePointerScrollUnit.Lines, -1, -2)));
            Assert.Equal(0, list.TopIndex);
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Lines, 0, -.5));
            Assert.Equal(0, list.TopIndex);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NativeListScroll_UnsupportedModesStayExplicit(int mode)
    {
        RunNativeListScroll((_, _, list, provider, _) =>
        {
            switch (mode)
            {
                case 0: list.DrawMode = DrawMode.OwnerDrawVariable; break;
                case 1: list.SelectionMode = SelectionMode.MultiSimple; break;
                case 2: list.MultiColumn = true; break;
                case 3: list.HorizontalScrollbar = true; break;
            }

            Assert.Throws<PlatformNotSupportedException>(() => provider.Emit(ScrollPacket(NativePointerScrollUnit.Lines, 0, -1)));
        });
    }

    [Fact]
    public void NativeListScroll_SourceDpiChangeRetiresTheOriginalFrame()
    {
        if (RunDpiCaseInNewProcess()) return;
        RunNativeListScroll((platform, _, list, provider, _) =>
        {
            int originalDpi = list.DeviceDpi;
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -.75 * list.GetItemHeight(0)));
            platform.SetPresentationScales(2, 2);
            Assert.Equal(originalDpi * 2, list.DeviceDpi);
            int height = list.GetItemHeight(0);
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -.5 * height));
            Assert.Equal(0, list.TopIndex);
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -.5 * height));
            Assert.Equal(1, list.TopIndex);
        }, _ => Assert.True(Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NativeListScroll_ShortAndEmptyListsRemainAtTheirOriginalBound(int count)
    {
        RunNativeListScroll((_, _, list, provider, _) =>
        {
            list.Items.Clear();
            for (int index = 0; index < count; index++) list.Items.Add($"short {index}");
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Points, 0, -10000));
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Lines, 0, 10000));
            Assert.Equal(0, list.TopIndex);
        });
    }

    [Fact]
    public void NativeListScroll_ActualComboBoxPopupScrollsWithoutSelectionOrCommit()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddPortableCombo(owner);
            combo.Items.AddRange(Enumerable.Range(0, 20).Select(i => (object)$"extra {i}").ToArray());
            combo.MaxDropDownItems = 3;
            combo.DroppedDown = true;
            ListBox list = GetComboListThroughOwnerInput(platform, owner);
            ToolStripDropDown popup = (ToolStripDropDown)list.Parent!;
            int commits = 0;
            combo.SelectionChangeCommitted += (_, _) => commits++;
            using NativePointerTestContext provider = new();
            SourcePointerTarget target = new(popup, input => platform.SendControlInput(popup, input));
            using NativePointerInput subscription = new(provider, target);
            target.Subscription = subscription;
            Point point = popup.PointToClient(list.PointToScreen(new Point(2, 2)));
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Lines, 0, -2) with { X = point.X, Y = point.Y });
            Assert.Equal(2, list.TopIndex);
            Assert.Equal(0, list.SelectedIndex);
            Assert.Equal(0, combo.SelectedIndex);
            Assert.Equal(0, commits);
            Assert.True(combo.DroppedDown);
            Assert.Equal(2, list.IndexFromPoint(2, 2));
            combo.DroppedDown = false;
            provider.Emit(ScrollPacket(NativePointerScrollUnit.Lines, 0, -2));
            Assert.Equal(0, commits);
        });
    }

    private static void RunNativeListScroll(Action<HeadlessPlatform, Form, ListBox, NativePointerTestContext, SourcePointerTarget> action,
        Action<HeadlessPlatform>? configure = null)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        configure?.Invoke(platform);
        using Form form = new() { ClientSize = new Size(240, 200), AutoScaleMode = AutoScaleMode.None, ShowIcon = false };
        using ListBox list = new() { Location = new Point(10, 10), BorderStyle = BorderStyle.None, IntegralHeight = false };
        list.Size = new Size(160, list.GetItemHeight(0) * 3);
        list.Items.AddRange(Enumerable.Range(0, 12).Select(i => (object)$"row {i}").ToArray());
        list.SelectedIndex = 0;
        form.Controls.Add(list);
        form.Show();
        using NativePointerTestContext provider = new();
        SourcePointerTarget target = new(form, input => platform.SendControlInput(form, input));
        using NativePointerInput subscription = new(provider, target);
        target.Subscription = subscription;
        action(platform, form, list, provider, target);
    }
}
