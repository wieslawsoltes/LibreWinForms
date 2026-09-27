// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;
using FluentAssertions;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Fact]
    public void ComboBoxDropDownUsesActualListSourceAndNonactivatingOwnedPopup()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddPortableCombo(owner);
            int opening = 0;
            combo.DropDown += (_, _) => { opening++; combo.DroppedDown.Should().BeFalse(); };
            combo.DroppedDown = true;
            combo.DroppedDown = true;
            opening.Should().Be(1);
            combo.DroppedDown.Should().BeTrue();
            ListBox list = GetComboListThroughOwnerInput(platform, owner);
            ToolStripDropDown popup = list.Parent.Should().BeAssignableTo<ToolStripDropDown>().Subject;
            list.Items.Cast<string>().Should().Equal("Alpha", "Beta", "Gamma");
            list.SelectedIndex.Should().Be(0);
            list.Focused.Should().BeTrue();
            combo.Focused.Should().BeTrue("the canonical getter includes its actual focused list child");
            Form.ActiveForm.Should().BeSameAs(owner);
            platform.LastActivatedWindow.IsNull.Should().BeTrue();
            platform.LastWindowOptions.Options.Should().HaveFlag(LibreWindowOptions.Popup);
            platform.GetWindowOwner(popup).Should().Be(platform.GetWindowHandle(owner));
            popup.Refresh();
            platform.TextDrawStrings.Should().Contain(["Alpha", "Beta", "Gamma"]);
            combo.DroppedDown = false;
            popup.IsDisposed.Should().BeTrue();
            combo.Focused.Should().BeTrue();
        });
    }

    [Fact]
    public void ComboBoxOwnerKeyboardSelectsAndCommitsExactlyOnce()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddPortableCombo(owner);
            List<string> order = [];
            combo.SelectedIndexChanged += (_, _) => order.Add($"selected:{combo.SelectedIndex}");
            combo.SelectionChangeCommitted += (_, _) => { combo.DroppedDown.Should().BeTrue(); order.Add("commit"); };
            combo.DropDownClosed += (_, _) => { combo.DroppedDown.Should().BeFalse(); order.Add("closed"); };
            combo.DroppedDown = true;
            SendDropdownKey(platform, owner, LibreKey.Down);
            combo.SelectedIndex.Should().Be(1);
            SendDropdownKey(platform, owner, LibreKey.Enter, release: false);
            SendDropdownText(platform, owner, "\r");
            SendDropdownKeyUp(platform, owner, LibreKey.Enter);
            order.Should().Equal("selected:1", "commit", "closed");
            combo.Text.Should().Be("Beta");
            combo.DroppedDown.Should().BeFalse();
        });
    }

    [Fact]
    public void ComboBoxEscapeCancelsSelectionWithoutCommit()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddPortableCombo(owner);
            int commits = 0;
            int closed = 0;
            combo.SelectionChangeCommitted += (_, _) => commits++;
            combo.DropDownClosed += (_, _) => closed++;
            combo.DroppedDown = true;
            SendDropdownKey(platform, owner, LibreKey.Down);
            combo.SelectedIndex.Should().Be(1);
            SendDropdownKey(platform, owner, LibreKey.Escape);
            combo.SelectedIndex.Should().Be(0);
            combo.Text.Should().Be("Alpha");
            combo.DroppedDown.Should().BeFalse();
            combo.Focused.Should().BeTrue();
            commits.Should().Be(0);
            closed.Should().Be(1);
        });
    }

    [Fact]
    public void ComboBoxActualPopupPointerSelectsAndCommitsTheSourceRow()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddPortableCombo(owner);
            int commits = 0;
            combo.SelectionChangeCommitted += (_, _) => commits++;
            combo.DroppedDown = true;
            ListBox list = GetComboListThroughOwnerInput(platform, owner);
            ToolStripDropDown popup = (ToolStripDropDown)list.Parent!;
            Rectangle row = list.GetItemRectangle(1);
            Point point = popup.PointToClient(list.PointToScreen(new Point(row.Left + row.Width / 2, row.Top + row.Height / 2)));
            popup.ClientRectangle.Contains(point).Should().BeTrue();
            foreach (LibreInputEventKind kind in new[] { LibreInputEventKind.PointerMove, LibreInputEventKind.PointerDown, LibreInputEventKind.PointerUp })
                platform.SendControlInput(popup, new LibreInputEvent(kind, 1, LibreInputModifiers.None,
                    LibreKey.Unknown, null, new LibrePoint(point.X, point.Y), default, LibrePointerButton.Primary));
            combo.SelectedIndex.Should().Be(1);
            commits.Should().Be(1);
            combo.DroppedDown.Should().BeFalse();
            Form.ActiveForm.Should().BeSameAs(owner);
            platform.LastActivatedWindow.IsNull.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComboBoxOpensThroughCanonicalPointerOrF4(bool keyboard)
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddPortableCombo(owner);
            if (keyboard)
                SendDropdownKey(platform, owner, LibreKey.F4);
            else
            {
                Point p = owner.PointToClient(combo.PointToScreen(new Point(combo.Width / 2, combo.Height / 2)));
                foreach (LibreInputEventKind kind in new[] { LibreInputEventKind.PointerDown, LibreInputEventKind.PointerUp })
                    platform.SendControlInput(owner, new LibreInputEvent(kind, 1, LibreInputModifiers.None,
                        LibreKey.Unknown, null, new LibrePoint(p.X, p.Y), default, LibrePointerButton.Primary));
            }

            combo.DroppedDown.Should().BeTrue();
            SendDropdownKey(platform, owner, LibreKey.Escape);
            combo.DroppedDown.Should().BeFalse();
        });
    }

    [Fact]
    public void ComboBoxCallerKeyHandlerCanSuppressListNavigationAndCommit()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddPortableCombo(owner);
            int keys = 0;
            combo.KeyDown += (_, e) => { keys++; e.SuppressKeyPress = true; };
            combo.DroppedDown = true;
            SendDropdownKey(platform, owner, LibreKey.Down);
            SendDropdownKey(platform, owner, LibreKey.Enter);
            keys.Should().Be(2);
            combo.SelectedIndex.Should().Be(0);
            combo.DroppedDown.Should().BeTrue();
        });
    }

    [Fact]
    public void ComboBoxSecondOwnerPressClosesWithoutAnOutsideFilterReopen()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddPortableCombo(owner);
            int opened = 0;
            combo.DropDown += (_, _) => opened++;
            combo.DroppedDown = true;
            Point p = owner.PointToClient(combo.PointToScreen(new Point(combo.Width / 2, combo.Height / 2)));
            foreach (LibreInputEventKind kind in new[] { LibreInputEventKind.PointerDown, LibreInputEventKind.PointerUp })
                platform.SendControlInput(owner, new LibreInputEvent(kind, 1, LibreInputModifiers.None,
                    LibreKey.Unknown, null, new LibrePoint(p.X, p.Y), default, LibrePointerButton.Primary));
            combo.DroppedDown.Should().BeFalse();
            opened.Should().Be(1);
        });
    }

    [Fact]
    public void ComboBoxSelectionCallbackRetainsItsOwnReplacementSelection()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddPortableCombo(owner);
            combo.SelectedIndexChanged += (_, _) => { if (combo.SelectedIndex == 1) combo.SelectedIndex = 2; };
            combo.DroppedDown = true;
            SendDropdownKey(platform, owner, LibreKey.Down);
            combo.SelectedIndex.Should().Be(2);
            GetComboListThroughOwnerInput(platform, owner).SelectedIndex.Should().Be(2);
        });
    }

    [Fact]
    public void ComboBoxPendingOpeningCanBeCanceledAndReplacedByItsCallback()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddPortableCombo(owner);
            int opening = 0;
            combo.DropDown += (_, _) =>
            {
                if (++opening == 1)
                {
                    combo.DroppedDown = false;
                    combo.DroppedDown = true;
                }
            };
            combo.DroppedDown = true;
            opening.Should().Be(2);
            combo.DroppedDown.Should().BeTrue();
            GetComboListThroughOwnerInput(platform, owner).Focused.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("disable")]
    [InlineData("unparent")]
    [InlineData("dispose")]
    [InlineData("owner-loss")]
    public void ComboBoxSourceRetirementClosesItsActualPopup(string transition)
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddPortableCombo(owner);
            combo.DroppedDown = true;
            ListBox list = GetComboListThroughOwnerInput(platform, owner);
            Control popup = list.Parent!;
            int closed = 0;
            combo.DropDownClosed += (_, _) =>
            {
                combo.DroppedDown.Should().BeFalse();
                if (transition == "dispose")
                    popup.IsHandleCreated.Should().BeFalse("disposal closes the owned native surface before notifying");
                closed++;
            };
            if (transition == "hide") combo.Hide();
            else if (transition == "disable") combo.Enabled = false;
            else if (transition == "unparent") owner.Controls.Remove(combo);
            else if (transition == "dispose") combo.Dispose();
            else platform.SendFormInput(owner, LibreInputEventKind.FocusLost);
            combo.DroppedDown.Should().BeFalse();
            popup.IsDisposed.Should().BeTrue();
            popup.IsHandleCreated.Should().BeFalse();
            closed.Should().Be(1);
            combo.Dispose();
            popup.Dispose();
            closed.Should().Be(1, "retirement and repeated disposal must not duplicate the close notification");
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("hide")]
    [InlineData("dispose")]
    [InlineData("throw")]
    public void ComboBoxOpeningCallbackCannotPublishAnUnshownSurface(string transition)
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddPortableCombo(owner);
            int closed = 0;
            var failure = new InvalidOperationException("original opening failure");
            combo.DropDownClosed += (_, _) => closed++;
            combo.DropDown += (_, _) =>
            {
                if (transition == "cancel") combo.DroppedDown = false;
                else if (transition == "hide") combo.Hide();
                else if (transition == "dispose") combo.Dispose();
                else throw failure;
            };
            Action open = () => combo.DroppedDown = true;
            if (transition == "throw") open.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
            else open();
            combo.DroppedDown.Should().BeFalse();
            closed.Should().Be(0, "DropDownClosed must not describe a surface that never opened");
        });
    }

    [Fact]
    public void ComboBoxClosedCallbackCanOpenANewIndependentGeneration()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddPortableCombo(owner);
            combo.DroppedDown = true;
            Control original = GetComboListThroughOwnerInput(platform, owner).Parent!;
            int closed = 0;
            combo.DropDownClosed += (_, _) => { if (++closed == 1) combo.DroppedDown = true; };
            combo.DroppedDown = false;
            original.IsDisposed.Should().BeTrue();
            combo.DroppedDown.Should().BeTrue();
            Control replacement = GetComboListThroughOwnerInput(platform, owner).Parent!;
            replacement.Should().NotBeSameAs(original);
            combo.DroppedDown = false;
            replacement.IsDisposed.Should().BeTrue();
            closed.Should().Be(2);
        });
    }

    [Fact]
    public void ComboBoxCommitCallbackCannotCloseAReplacementGeneration()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddPortableCombo(owner);
            combo.DroppedDown = true;
            Control original = GetComboListThroughOwnerInput(platform, owner).Parent!;
            int committed = 0;
            combo.SelectionChangeCommitted += (_, _) =>
            {
                committed++;
                combo.DroppedDown = false;
                combo.DroppedDown = true;
            };
            SendDropdownKey(platform, owner, LibreKey.Enter);
            committed.Should().Be(1);
            original.IsDisposed.Should().BeTrue();
            combo.DroppedDown.Should().BeTrue();
        });
    }

    [Fact]
    public void ComboBoxClosePreservesFirstCallbackFailureAndStillReleasesTheNativeList()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddPortableCombo(owner);
            combo.DroppedDown = true;
            ListBox list = GetComboListThroughOwnerInput(platform, owner);
            Control popup = list.Parent!;
            var first = new InvalidOperationException("DropDownClosed failure");
            var cleanup = new InvalidOperationException("ListBox disposal failure");
            combo.DropDownClosed += (_, _) => throw first;
            list.Disposed += (_, _) => throw cleanup;
            Action close = () => combo.DroppedDown = false;
            close.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(first);
            combo.DroppedDown.Should().BeFalse();
            popup.IsHandleCreated.Should().BeFalse();
            list.IsHandleCreated.Should().BeFalse();
            list.IsDisposed.Should().BeTrue();
            popup.IsDisposed.Should().BeTrue();
            combo.Focused.Should().BeTrue();
        });
    }

    [Fact]
    public void ComboBoxDisposalRejectsAReentrantPopupBeforeTheSourceHandleRetires()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddPortableCombo(owner);
            combo.DroppedDown = true;
            Control popup = GetComboListThroughOwnerInput(platform, owner).Parent!;
            combo.DropDownClosed += (_, _) =>
            {
                Action reopen = () => combo.DroppedDown = true;
                reopen.Should().Throw<InvalidOperationException>();
            };
            combo.Dispose();
            combo.IsDisposed.Should().BeTrue();
            combo.DroppedDown.Should().BeFalse();
            popup.IsHandleCreated.Should().BeFalse();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComboBoxChangingOpenItemsCannotCommitAStaleIndex(bool replace)
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddPortableCombo(owner);
            int committed = 0;
            combo.SelectionChangeCommitted += (_, _) => committed++;
            combo.DroppedDown = true;
            if (replace) combo.Items[1] = "Replacement";
            else combo.Items.Add("Extra");
            Action navigate = () => SendDropdownKey(platform, owner, LibreKey.Down);
            navigate.Should().Throw<NotSupportedException>();
            combo.DroppedDown.Should().BeFalse();
            combo.SelectedIndex.Should().Be(0);
            committed.Should().Be(0);
        });
    }

    [Fact]
    public void ComboBoxPopupAdmissionFailurePreservesOriginalExceptionAndClosedState()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddPortableCombo(owner);
            var failure = new InvalidOperationException("native popup rejection");
            platform.PopupShowFailure = failure;
            try
            {
                Action open = () => combo.DroppedDown = true;
                open.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
                combo.DroppedDown.Should().BeFalse();
                combo.Focused.Should().BeTrue();
            }
            finally { platform.PopupShowFailure = null; }
        });
    }

    [Theory]
    [InlineData(ComboBoxStyle.DropDown, DrawMode.Normal)]
    [InlineData(ComboBoxStyle.DropDownList, DrawMode.OwnerDrawFixed)]
    [InlineData(ComboBoxStyle.DropDownList, DrawMode.OwnerDrawVariable)]
    public void ComboBoxUnsupportedSurfaceStylesFailBeforePublishingVisibility(ComboBoxStyle style, DrawMode mode)
    {
        RunDropdownKeyboard((_, owner, _) =>
        {
            using ComboBox combo = AddPortableCombo(owner);
            combo.DropDownStyle = style;
            combo.DrawMode = mode;
            Action open = () => combo.DroppedDown = true;
            open.Should().Throw<NotSupportedException>();
            combo.DroppedDown.Should().BeFalse();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComboBoxClosedPaintUsesActualSourceFontTextClipAndDirection(bool rtl)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Font font = new(FontFamily.GenericSansSerif, 14);
        using PaintablePortableComboBox combo = new()
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Size = new Size(180, 32), Font = font,
            RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No, ForeColor = Color.Navy
        };
        combo.Items.Add("A&🙂 אב");
        combo.SelectedIndex = 0;
        using Bitmap target = new(200, 60);
        using Graphics graphics = Graphics.FromImage(target);
        graphics.SetClip(new Rectangle(4, 3, 170, 24));
        RectangleF before = graphics.ClipBounds;
        combo.PaintContent(graphics, new Rectangle(6, 5, 150, 20));
        platform.TextDrawStrings.Should().ContainSingle().Which.Should().Be("A&🙂 אב");
        platform.LastDrawnTextFont.Should().BeSameAs(font);
        platform.LastDrawnTextColor.Should().Be(Color.Navy);
        platform.LastTextFormat.Should().HaveFlag(LibreTextFormat.NoPrefix);
        platform.LastTextFormat.Should().HaveFlag(LibreTextFormat.SingleLine);
        if (rtl) platform.LastTextFormat.Should().HaveFlag(LibreTextFormat.RightToLeft);
        else platform.LastTextFormat.Should().NotHaveFlag(LibreTextFormat.RightToLeft);
        RectangleF expectedClip = RectangleF.Intersect(before, combo.ClientRectangle);
        expectedClip.Intersect(new Rectangle(6, 5, 150, 20));
        platform.LastDrawnTextClip.Should().Be(expectedClip);
        graphics.ClipBounds.Should().Be(before);
        combo.SelectedIndex.Should().Be(0);
        combo.IsHandleCreated.Should().BeFalse();
    }

    private static ComboBox AddPortableCombo(Form owner)
    {
        ComboBox combo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(8, 90, 180, 24) };
        combo.Items.AddRange(["Alpha", "Beta", "Gamma"]);
        combo.SelectedIndex = 0;
        owner.Controls.Add(combo);
        combo.Focus().Should().BeTrue();
        return combo;
    }

    private static ListBox GetComboListThroughOwnerInput(HeadlessPlatform platform, Form owner)
    {
        Control? recipient = null;
        CallbackKeyboardFilter filter = new(message =>
        {
            if (message.Msg != 0x100) return false;
            recipient = Control.FromHandle(message.HWnd);
            return true;
        });
        Application.AddMessageFilter(filter);
        try { SendDropdownKey(platform, owner, LibreKey.F8); }
        finally { Application.RemoveMessageFilter(filter); }
        return recipient.Should().BeOfType<ListBox>().Subject;
    }

    private sealed class PaintablePortableComboBox : ComboBox
    {
        internal void PaintContent(Graphics graphics, Rectangle clip)
            => OnPaint(new PaintEventArgs(graphics, clip));
    }
}
