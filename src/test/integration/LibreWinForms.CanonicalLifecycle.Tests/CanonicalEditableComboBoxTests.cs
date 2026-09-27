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
    public void EditableComboBoxUsesARealSourceTextBoxAndCompositeFocus()
    {
        RunDropdownKeyboard((platform, owner, other) =>
        {
            using ComboBox combo = AddEditableCombo(owner, focus: false);
            TextBox edit = GetEditableComboTextBox(combo);
            List<string> order = [];
            combo.GotFocus += (_, _) => order.Add("got");
            combo.LostFocus += (_, _) => order.Add("lost");
            combo.Focus().Should().BeTrue();
            edit.Focused.Should().BeTrue();
            combo.Focused.Should().BeTrue();
            combo.DroppedDown = true;
            edit.Focused.Should().BeTrue();
            combo.DroppedDown = false;
            edit.Focused.Should().BeTrue();
            order.Should().Equal("got");
            other.Focus().Should().BeTrue();
            order.Should().Equal("got", "lost");
            combo.Focused.Should().BeFalse();
            Form.ActiveForm.Should().BeSameAs(owner);
            platform.LastActivatedWindow.IsNull.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditableComboBoxTypedTextUsesCanonicalEditNotificationsWithoutCommit(bool open)
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddEditableCombo(owner);
            TextBox edit = GetEditableComboTextBox(combo);
            combo.SelectAll();
            if (open) combo.DroppedDown = true;
            List<string> order = [];
            combo.TextUpdate += (_, _) => order.Add($"update:{combo.Text}");
            combo.TextChanged += (_, _) => order.Add($"changed:{combo.Text}");
            combo.SelectedIndexChanged += (_, _) => order.Add("selected");
            combo.SelectionChangeCommitted += (_, _) => order.Add("commit");
            SendDropdownText(platform, owner, "x");
            order.Should().Equal("update:x", "changed:x");
            combo.Text.Should().Be("x");
            edit.Text.Should().Be("x");
            combo.SelectedIndex.Should().Be(-1);
            combo.SelectionStart.Should().Be(1);
            combo.SelectionLength.Should().Be(0);
            edit.Focused.Should().BeTrue();
            combo.DroppedDown.Should().Be(open);
        });
    }

    [Fact]
    public void EditableComboBoxProgrammaticTextPreservesMatchingAndDoesNotRaiseTextUpdate()
    {
        RunDropdownKeyboard((_, owner, _) =>
        {
            using ComboBox combo = AddEditableCombo(owner);
            TextBox edit = GetEditableComboTextBox(combo);
            int updates = 0;
            int commits = 0;
            combo.TextUpdate += (_, _) => updates++;
            combo.SelectionChangeCommitted += (_, _) => commits++;
            combo.Text = "Beta";
            combo.SelectedIndex.Should().Be(1);
            edit.Text.Should().Be("Beta");
            combo.Text = "unmatched";
            edit.Text.Should().Be("unmatched");
            combo.Text.Should().Be("unmatched");
            updates.Should().Be(0);
            commits.Should().Be(0);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditableComboBoxPublicSelectionUsesActualUtf16EditorState(bool beforeHandle)
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = new() { DropDownStyle = ComboBoxStyle.DropDown, Bounds = new Rectangle(8, 90, 180, 24), Text = "A😀B" };
            if (beforeHandle) combo.Select(3, -2);
            owner.Controls.Add(combo);
            if (!beforeHandle) combo.Select(1, 2);
            combo.Focus().Should().BeTrue();
            TextBox edit = GetEditableComboTextBox(combo);
            combo.SelectedText.Should().Be("😀");
            edit.SelectedText.Should().Be("😀");
            SendDropdownText(platform, owner, "🚀");
            combo.Text.Should().Be("A🚀B");
            combo.SelectionStart.Should().Be(3);
            combo.SelectionLength.Should().Be(0);
            edit.SelectionStart.Should().Be(3);
            combo.Select(1, 2);
            combo.SelectedText = "Z";
            combo.Text.Should().Be("AZB");
            edit.Text.Should().Be("AZB");
            combo.SelectionStart.Should().Be(2);
        });
    }

    [Fact]
    public void EditableComboBoxMaxLengthAppliesToInputNotProgrammaticText()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddEditableCombo(owner);
            combo.MaxLength = 3;
            combo.Text = "longer";
            GetEditableComboTextBox(combo).Text.Should().Be("longer");
            combo.SelectAll();
            SendDropdownText(platform, owner, "abcd");
            combo.Text.Should().Be("abc");
            combo.MaxLength = 0;
            SendDropdownText(platform, owner, "d");
            combo.Text.Should().Be("abcd");
        });
    }

    [Fact]
    public void EditableComboBoxOpenListKeepsRealEditorAsFilteredInputTarget()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddEditableCombo(owner);
            TextBox edit = GetEditableComboTextBox(combo);
            combo.DroppedDown = true;
            List<string> order = [];
            CallbackKeyboardFilter filter = new(message =>
            {
                if (message.Msg == 0x100)
                {
                    message.HWnd.Should().Be(edit.Handle);
                    order.Add("filter");
                }
                return false;
            });
            combo.KeyDown += (_, e) => { order.Add("key"); e.SuppressKeyPress = true; };
            combo.KeyPress += (_, _) => order.Add("char");
            Application.AddMessageFilter(filter);
            try
            {
                SendDropdownKey(platform, owner, LibreKey.Down, release: false);
                SendDropdownText(platform, owner, "x");
                SendDropdownKeyUp(platform, owner, LibreKey.Down);
            }
            finally { Application.RemoveMessageFilter(filter); }
            order.Should().Equal("filter", "key");
            combo.SelectedIndex.Should().Be(0);
            combo.Text.Should().Be("Alpha");
            edit.Focused.Should().BeTrue();
        });
    }

    [Fact]
    public void EditableComboBoxDownAndEnterUseExistingListSelectionWithoutMovingEditFocus()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddEditableCombo(owner);
            TextBox edit = GetEditableComboTextBox(combo);
            int commits = 0;
            combo.SelectionChangeCommitted += (_, _) => commits++;
            combo.DroppedDown = true;
            SendDropdownKey(platform, owner, LibreKey.Down);
            combo.SelectedIndex.Should().Be(1);
            edit.Text.Should().Be("Beta");
            edit.Focused.Should().BeTrue();
            SendDropdownKey(platform, owner, LibreKey.Enter);
            combo.DroppedDown.Should().BeFalse();
            combo.SelectedIndex.Should().Be(1);
            edit.Text.Should().Be("Beta");
            edit.Focused.Should().BeTrue();
            commits.Should().Be(1);
        });
    }

    [Theory]
    [InlineData(LibreKey.Home)]
    [InlineData(LibreKey.End)]
    public void EditableComboBoxDoesNotHijackEditHomeEndForListNavigation(LibreKey key)
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddEditableCombo(owner);
            combo.SelectedIndex = 1;
            combo.DroppedDown = true;
            int keys = 0;
            combo.KeyDown += (_, _) => keys++;
            SendDropdownKey(platform, owner, key);
            combo.SelectedIndex.Should().Be(1);
            keys.Should().Be(1);
            GetEditableComboTextBox(combo).Focused.Should().BeTrue();
            // This is a routing assertion, not qualification of the TextBox's
            // still-separate caret navigation or native glyph hit placement.
        });
    }

    [Fact]
    public void EditableComboBoxEscapeRestoresUnmatchedTextAndSelectionWithoutCommit()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddEditableCombo(owner);
            combo.SelectAll();
            SendDropdownText(platform, owner, "custom");
            combo.Select(1, 3);
            int commits = 0;
            combo.SelectionChangeCommitted += (_, _) => commits++;
            combo.DroppedDown = true;
            SendDropdownKey(platform, owner, LibreKey.Down);
            combo.SelectedIndex.Should().Be(0);
            SendDropdownKey(platform, owner, LibreKey.Escape);
            combo.DroppedDown.Should().BeFalse();
            combo.SelectedIndex.Should().Be(-1);
            combo.Text.Should().Be("custom");
            combo.SelectionStart.Should().Be(1);
            combo.SelectionLength.Should().Be(3);
            GetEditableComboTextBox(combo).SelectedText.Should().Be("ust");
            commits.Should().Be(0);
        });
    }

    [Fact]
    public void EditableComboBoxLeavingCompositeClosesPopupAndDeliversTextToNewOwnerControl()
    {
        RunDropdownKeyboard((platform, owner, other) =>
        {
            using ComboBox combo = AddEditableCombo(owner);
            combo.DroppedDown = true;
            int lost = 0;
            combo.LostFocus += (_, _) => lost++;
            other.Focus().Should().BeTrue();
            combo.DroppedDown.Should().BeFalse();
            combo.Focused.Should().BeFalse();
            lost.Should().Be(1);
            SendDropdownText(platform, owner, "outside");
            other.Text.Should().Be("outside");
            combo.Text.Should().Be("Alpha");
        });
    }

    [Fact]
    public void EditableComboBoxGotFocusCallbackCanMoveFocusWithoutAStaleOuterNotification()
    {
        RunDropdownKeyboard((_, owner, other) =>
        {
            using ComboBox combo = AddEditableCombo(owner, focus: false);
            List<string> order = [];
            combo.GotFocus += (_, _) => { order.Add("got"); other.Focus(); };
            combo.LostFocus += (_, _) => order.Add("lost");
            combo.Focus();
            other.Focused.Should().BeTrue();
            combo.Focused.Should().BeFalse();
            order.Should().Equal("got", "lost");
        });
    }

    [Fact]
    public void EditableComboBoxThrowingGotFocusKeepsMatchingLaterLoss()
    {
        RunDropdownKeyboard((_, owner, other) =>
        {
            using ComboBox combo = AddEditableCombo(owner, focus: false);
            var failure = new InvalidOperationException("composite focus callback");
            combo.GotFocus += (_, _) => throw failure;
            int lost = 0;
            combo.LostFocus += (_, _) => lost++;
            Action focus = () => combo.Focus();
            focus.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
            GetEditableComboTextBox(combo).Focused.Should().BeTrue();
            other.Focus().Should().BeTrue();
            lost.Should().Be(1);
        });
    }

    [Fact]
    public void EditableComboBoxTextUpdateCallbackReplacementRemainsAuthoritative()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddEditableCombo(owner);
            combo.SelectAll();
            combo.TextUpdate += (_, _) => combo.Text = "replacement";
            List<string> changed = [];
            combo.TextChanged += (_, _) => changed.Add(combo.Text);
            SendDropdownText(platform, owner, "x");
            combo.Text.Should().Be("replacement");
            GetEditableComboTextBox(combo).Text.Should().Be("replacement");
            changed.Should().Equal("replacement");
        });
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("disable")]
    [InlineData("unparent")]
    [InlineData("dispose")]
    public void EditableComboBoxRetiresEditorAndPopupOwnershipBeforeFurtherInput(string transition)
    {
        RunDropdownKeyboard((platform, owner, other) =>
        {
            using ComboBox combo = AddEditableCombo(owner);
            TextBox edit = GetEditableComboTextBox(combo);
            combo.DroppedDown = true;
            if (transition == "hide") combo.Hide();
            else if (transition == "disable") combo.Enabled = false;
            else if (transition == "unparent") owner.Controls.Remove(combo);
            else combo.Dispose();
            combo.DroppedDown.Should().BeFalse();
            if (transition == "dispose") edit.IsDisposed.Should().BeTrue();
            other.Focus().Should().BeTrue();
            SendDropdownText(platform, owner, "next");
            other.Text.Should().Be("next");
            edit.Text.Should().Be("Alpha");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditableComboBoxActualEditorAndArrowHaveDistinctPointerRoles(bool rtl)
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddEditableCombo(owner);
            combo.RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
            TextBox edit = GetEditableComboTextBox(combo);
            Point editPoint = owner.PointToClient(edit.PointToScreen(new Point(edit.Width / 2, edit.Height / 2)));
            SendEditableComboPointer(platform, owner, editPoint);
            combo.DroppedDown.Should().BeFalse();
            edit.Focused.Should().BeTrue();
            Point arrowPoint = owner.PointToClient(combo.PointToScreen(new Point(rtl ? 3 : combo.Width - 3, combo.Height / 2)));
            edit.Bounds.Contains(combo.PointToClient(owner.PointToScreen(arrowPoint))).Should().BeFalse();
            SendEditableComboPointer(platform, owner, arrowPoint);
            combo.DroppedDown.Should().BeTrue();
            edit.Focused.Should().BeTrue();
            SendEditableComboPointer(platform, owner, arrowPoint);
            combo.DroppedDown.Should().BeFalse();
            edit.Focused.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("disable")]
    [InlineData("unparent")]
    public void EditableComboBoxInvalidEditorCannotRemainTheOpenMenuInputTarget(string transition)
    {
        RunDropdownKeyboard((platform, owner, other) =>
        {
            using ComboBox combo = AddEditableCombo(owner);
            using TextBox edit = GetEditableComboTextBox(combo);
            combo.DroppedDown = true;
            if (transition == "hide") edit.Hide();
            else if (transition == "disable") edit.Enabled = false;
            else combo.Controls.Remove(edit);
            combo.DroppedDown.Should().BeFalse();
            other.Focus().Should().BeTrue();
            SendDropdownText(platform, owner, "outside");
            other.Text.Should().Be("outside");
            combo.Text.Should().Be("Alpha");
        });
    }

    [Fact]
    public void EditableComboBoxActualListPointerCommitsWithoutAHostedFocusLeaseOrCompositeFocusChurn()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddEditableCombo(owner);
            TextBox edit = GetEditableComboTextBox(combo);
            int got = 0;
            int lost = 0;
            int commits = 0;
            combo.GotFocus += (_, _) => got++;
            combo.LostFocus += (_, _) => lost++;
            combo.SelectionChangeCommitted += (_, _) => commits++;
            combo.DroppedDown = true;
            ToolStripDropDown popup = platform.LastWindowControl.Should().BeAssignableTo<ToolStripDropDown>().Subject;
            ListBox list = popup.Controls.OfType<ListBox>().Should().ContainSingle().Which;
            list.Focused.Should().BeFalse();
            edit.Focused.Should().BeTrue();
            Rectangle row = list.GetItemRectangle(1);
            Point point = popup.PointToClient(list.PointToScreen(new Point(row.Left + row.Width / 2, row.Top + row.Height / 2)));
            SendEditableComboPointer(platform, popup, point);
            combo.SelectedIndex.Should().Be(1);
            combo.Text.Should().Be("Beta");
            edit.Text.Should().Be("Beta");
            combo.DroppedDown.Should().BeFalse();
            popup.IsDisposed.Should().BeTrue();
            edit.Focused.Should().BeTrue();
            combo.Focused.Should().BeTrue();
            got.Should().Be(0);
            lost.Should().Be(0);
            commits.Should().Be(1);
            Form.ActiveForm.Should().BeSameAs(owner);
            platform.LastActivatedWindow.IsNull.Should().BeTrue();
        });
    }

    [Fact]
    public void EditableComboBoxStyleRecreationRetiresOldSourceEditorAndPopup()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using ComboBox combo = AddEditableCombo(owner);
            TextBox oldEditor = GetEditableComboTextBox(combo);
            combo.DroppedDown = true;
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            oldEditor.IsDisposed.Should().BeTrue();
            combo.DroppedDown.Should().BeFalse();
            combo.Controls.OfType<TextBox>().Should().BeEmpty();
            combo.DropDownStyle = ComboBoxStyle.DropDown;
            TextBox current = GetEditableComboTextBox(combo);
            current.Should().NotBeSameAs(oldEditor);
            combo.Focus();
            combo.SelectAll();
            SendDropdownText(platform, owner, "new");
            current.Text.Should().Be("new");
            combo.Text.Should().Be("new");
        });
    }

    [Fact]
    public void EditableComboBoxTextUpdateCanDisposeWithoutAnObsoleteTextChanged()
    {
        RunDropdownKeyboard((platform, owner, other) =>
        {
            using ComboBox combo = AddEditableCombo(owner);
            TextBox edit = GetEditableComboTextBox(combo);
            combo.DroppedDown = true;
            int changed = 0;
            combo.TextUpdate += (_, _) => combo.Dispose();
            combo.TextChanged += (_, _) => changed++;
            SendDropdownText(platform, owner, "x");
            combo.IsDisposed.Should().BeTrue();
            edit.IsDisposed.Should().BeTrue();
            combo.DroppedDown.Should().BeFalse();
            changed.Should().Be(0);
            other.Focus().Should().BeTrue();
            SendDropdownText(platform, owner, "after");
            other.Text.Should().Be("after");
        });
    }

    private static void SendEditableComboPointer(HeadlessPlatform platform, Control root, Point point)
    {
        root.ClientRectangle.Contains(point).Should().BeTrue();
        foreach (LibreInputEventKind kind in new[] { LibreInputEventKind.PointerMove, LibreInputEventKind.PointerDown, LibreInputEventKind.PointerUp })
            platform.SendControlInput(root, new LibreInputEvent(kind, 1, LibreInputModifiers.None,
                LibreKey.Unknown, null, new LibrePoint(point.X, point.Y), default, LibrePointerButton.Primary));
    }

    private static ComboBox AddEditableCombo(Form owner, bool focus = true)
    {
        ComboBox combo = new() { DropDownStyle = ComboBoxStyle.DropDown, Bounds = new Rectangle(8, 90, 180, 24) };
        combo.Items.AddRange(["Alpha", "Beta", "Gamma"]);
        combo.SelectedIndex = 0;
        owner.Controls.Add(combo);
        if (focus) combo.Focus().Should().BeTrue();
        return combo;
    }

    private static TextBox GetEditableComboTextBox(ComboBox combo)
        => combo.Controls.OfType<TextBox>().Should().ContainSingle().Which;
}
