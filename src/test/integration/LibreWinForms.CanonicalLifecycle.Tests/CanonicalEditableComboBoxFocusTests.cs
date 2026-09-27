// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;
using FluentAssertions;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Fact]
    public void EditableComboBoxStyleRecreationAutomaticallyFocusesReplacementEditorWithoutCompositeChurn()
    {
        RunDropdownKeyboard((platform, owner, other) =>
        {
            using ComboBox combo = AddEditableCombo(owner);
            TextBox original = GetEditableComboTextBox(combo);
            List<string> focus = [];
            combo.GotFocus += (_, _) => focus.Add("got");
            combo.LostFocus += (_, _) => focus.Add("lost");
            combo.DroppedDown = true;
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.DropDownStyle = ComboBoxStyle.DropDown;
            TextBox replacement = GetEditableComboTextBox(combo);
            original.IsDisposed.Should().BeTrue();
            replacement.Should().NotBeSameAs(original);
            replacement.Focused.Should().BeTrue("handle recreation itself must finish the existing focus request");
            combo.Focused.Should().BeTrue();
            focus.Should().BeEmpty();
            combo.SelectAll();
            SendDropdownText(platform, owner, "replacement");
            replacement.Text.Should().Be("replacement");
            combo.Text.Should().Be("replacement");
            other.Focus().Should().BeTrue();
            focus.Should().Equal("lost");
        });
    }

    [Fact]
    public void EditableComboBoxReplacementEditorFocusCallbackCanChooseAnotherOwnerControl()
    {
        RunDropdownKeyboard((platform, owner, other) =>
        {
            using ComboBox combo = AddEditableCombo(owner);
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            int lost = 0;
            int got = 0;
            combo.GotFocus += (_, _) => got++;
            combo.LostFocus += (_, _) => lost++;
            combo.ControlAdded += (_, e) =>
            {
                if (e.Control is TextBox editor)
                    editor.GotFocus += (_, _) => other.Focus();
            };
            combo.DropDownStyle = ComboBoxStyle.DropDown;
            other.Focused.Should().BeTrue();
            combo.Focused.Should().BeFalse();
            GetEditableComboTextBox(combo).Focused.Should().BeFalse();
            got.Should().Be(0);
            lost.Should().Be(1);
            SendDropdownText(platform, owner, "outside");
            other.Text.Should().Be("outside");
            combo.Text.Should().Be("Alpha");
        });
    }

    [Fact]
    public void EditableComboBoxReplacementEditorFocusExceptionPreservesActualFocusAndLaterCompositeLoss()
    {
        RunDropdownKeyboard((_, owner, other) =>
        {
            using ComboBox combo = AddEditableCombo(owner);
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            var failure = new InvalidOperationException("replacement editor focus");
            int lost = 0;
            int got = 0;
            combo.GotFocus += (_, _) => got++;
            combo.LostFocus += (_, _) => lost++;
            combo.ControlAdded += (_, e) =>
            {
                if (e.Control is TextBox editor)
                    editor.GotFocus += (_, _) => throw failure;
            };
            Action recreate = () => combo.DropDownStyle = ComboBoxStyle.DropDown;
            recreate.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
            GetEditableComboTextBox(combo).Focused.Should().BeTrue();
            combo.Focused.Should().BeTrue();
            got.Should().Be(0);
            other.Focus().Should().BeTrue();
            lost.Should().Be(1);
        });
    }

    [Fact]
    public void EditableComboBoxReplacementEditorFocusCanReplaceItsSourceHandle()
    {
        RunDropdownKeyboard((_, owner, _) =>
        {
            using ComboBox combo = AddEditableCombo(owner);
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            TextBox? retired = null;
            nint replacedHandle = 0;
            int callbacks = 0;
            combo.ControlAdded += (_, e) =>
            {
                if (e.Control is TextBox editor)
                {
                    retired = editor;
                    editor.GotFocus += (_, _) =>
                    {
                        callbacks++;
                        replacedHandle = combo.Handle;
                        combo.DropDownStyle = ComboBoxStyle.DropDownList;
                    };
                }
            };
            combo.DropDownStyle = ComboBoxStyle.DropDown;
            callbacks.Should().Be(1);
            retired.Should().NotBeNull();
            retired!.IsDisposed.Should().BeTrue();
            combo.DropDownStyle.Should().Be(ComboBoxStyle.DropDownList);
            combo.Handle.Should().NotBe(replacedHandle);
            combo.Controls.OfType<TextBox>().Should().BeEmpty();
            combo.DroppedDown.Should().BeFalse();
            combo.Focused.Should().BeTrue();
            Form.ActiveForm.Should().BeSameAs(owner);
        });
    }
}
