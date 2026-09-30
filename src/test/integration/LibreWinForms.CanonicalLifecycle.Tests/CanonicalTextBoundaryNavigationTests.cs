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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableTextBoundaryKeysMoveReadOnlyAndEditableSelectionWithoutEditing(bool readOnly)
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using TextBox editor = AddBoundaryEditor(owner, "A🙂BC");
            editor.ReadOnly = readOnly;
            editor.Select(1, 2);
            int changed = 0;
            int modified = 0;
            editor.TextChanged += (_, _) => changed++;
            editor.ModifiedChanged += (_, _) => modified++;
            SendDropdownKey(platform, owner, LibreKey.End);
            editor.SelectionStart.Should().Be(5);
            editor.SelectionLength.Should().Be(0);
            SendDropdownKey(platform, owner, LibreKey.Home);
            editor.SelectionStart.Should().Be(0);
            editor.SelectionLength.Should().Be(0);
            editor.Text.Should().Be("A🙂BC");
            editor.Modified.Should().BeFalse();
            changed.Should().Be(0);
            modified.Should().Be(0);
            editor.Focused.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(4, -2)]
    public void PortableTextBoundaryShiftRetainsForwardAndBackwardSelectionAnchor(int anchor, int length)
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using TextBox editor = AddBoundaryEditor(owner, "abcdef");
            editor.Select(anchor, length);
            SendDropdownKey(platform, owner, LibreKey.End, LibreInputModifiers.Shift);
            editor.SelectionStart.Should().Be(anchor);
            editor.SelectionLength.Should().Be(6 - anchor);
            SendDropdownKey(platform, owner, LibreKey.Home, LibreInputModifiers.Shift);
            editor.SelectionStart.Should().Be(0);
            editor.SelectionLength.Should().Be(anchor);
            SendDropdownKey(platform, owner, LibreKey.End, LibreInputModifiers.Shift);
            editor.SelectionStart.Should().Be(anchor);
            editor.SelectionLength.Should().Be(6 - anchor);
            editor.Text.Should().Be("abcdef");
        });
    }

    [Fact]
    public void PortableTextBoundaryControlKeysReachMultilineDocumentEnds()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using TextBox editor = AddBoundaryEditor(owner, "A🙂\r\nBC");
            editor.Multiline = true;
            editor.Focus().Should().BeTrue();
            editor.Select(3, 0);
            SendDropdownKey(platform, owner, LibreKey.End, LibreInputModifiers.Control);
            editor.SelectionStart.Should().Be(7);
            editor.SelectionLength.Should().Be(0);
            SendDropdownKey(platform, owner, LibreKey.Home, LibreInputModifiers.Control);
            editor.SelectionStart.Should().Be(0);
            editor.Select(3, 0);
            SendDropdownKey(platform, owner, LibreKey.Home, LibreInputModifiers.Control | LibreInputModifiers.Shift);
            editor.SelectionStart.Should().Be(0);
            editor.SelectionLength.Should().Be(3);
            SendDropdownKey(platform, owner, LibreKey.End, LibreInputModifiers.Control | LibreInputModifiers.Shift);
            editor.SelectionStart.Should().Be(3);
            editor.SelectionLength.Should().Be(4);
            editor.Text.Should().Be("A🙂\r\nBC");
        });
    }

    [Fact]
    public void PortableTextBoundaryKeysPreserveHandledAndFilteredInputPrecedence()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using TextBox editor = AddBoundaryEditor(owner, "abcdef");
            editor.Select(2, 1);
            KeyEventHandler reject = (_, e) => e.Handled = true;
            editor.KeyDown += reject;
            SendDropdownKey(platform, owner, LibreKey.End);
            editor.SelectionStart.Should().Be(2);
            editor.SelectionLength.Should().Be(1);
            editor.KeyDown -= reject;
            BoundaryKeyFilter filter = new();
            Application.AddMessageFilter(filter);
            try
            {
                SendDropdownKey(platform, owner, LibreKey.Home);
                filter.Count.Should().Be(1);
                editor.SelectionStart.Should().Be(2);
                editor.SelectionLength.Should().Be(1);
            }
            finally
            {
                Application.RemoveMessageFilter(filter);
            }

            SendDropdownKey(platform, owner, LibreKey.Home);
            editor.SelectionStart.Should().Be(0);
            editor.SelectionLength.Should().Be(0);
        });
    }

    [Fact]
    public void PortableTextBoundaryKeysUseSourceStateAfterTheManagedKeyHandler()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using TextBox editor = AddBoundaryEditor(owner, "old");
            editor.Select(1, 0);
            editor.KeyDown += (_, _) =>
            {
                editor.Text = "replacement";
                editor.Select(4, 0);
            };
            SendDropdownKey(platform, owner, LibreKey.End, LibreInputModifiers.Shift);
            editor.SelectionStart.Should().Be(4);
            editor.SelectionLength.Should().Be(7);
            editor.SelectedText.Should().Be("acement");
        });
    }

    [Fact]
    public void PortableTextBoundaryKeysKeepEmptyPasswordSelectionValid()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using TextBox editor = AddBoundaryEditor(owner, string.Empty);
            editor.UseSystemPasswordChar = true;
            editor.Focus().Should().BeTrue();
            foreach (LibreKey key in new[] { LibreKey.Home, LibreKey.End })
            {
                SendDropdownKey(platform, owner, key, LibreInputModifiers.Shift);
                editor.SelectionStart.Should().Be(0);
                editor.SelectionLength.Should().Be(0);
                editor.Text.Should().BeEmpty();
            }
        });
    }

    [Fact]
    public void PortableTextBoundaryKeysDoNotInventMultilineVisualLineBoundaries()
    {
        RunDropdownKeyboard((platform, owner, _) =>
        {
            using TextBox editor = AddBoundaryEditor(owner, "first\r\nsecond\r\nthird");
            editor.Multiline = true;
            editor.Focus().Should().BeTrue();
            editor.Select(9, 1);
            SendDropdownKey(platform, owner, LibreKey.Home);
            editor.SelectionStart.Should().Be(9);
            editor.SelectionLength.Should().Be(1);
            SendDropdownKey(platform, owner, LibreKey.End, LibreInputModifiers.Shift);
            editor.SelectionStart.Should().Be(9);
            editor.SelectionLength.Should().Be(1);
        });
    }

    private static TextBox AddBoundaryEditor(Form owner, string text)
    {
        TextBox editor = new() { Text = text, Bounds = new Rectangle(8, 140, 180, 60) };
        owner.Controls.Add(editor);
        editor.Focus().Should().BeTrue();
        return editor;
    }

    private static void SendDropdownKey(HeadlessPlatform platform, Form owner, LibreKey key, LibreInputModifiers modifiers)
    {
        platform.SendControlInput(owner, new LibreInputEvent(LibreInputEventKind.KeyDown,
            1, modifiers, key, null, default, default, LibrePointerButton.None));
        platform.SendControlInput(owner, new LibreInputEvent(LibreInputEventKind.KeyUp,
            1, modifiers, key, null, default, default, LibrePointerButton.None));
    }

    private sealed class BoundaryKeyFilter : IMessageFilter
    {
        public int Count { get; private set; }

        public bool PreFilterMessage(ref Message message)
        {
            if (message.Msg == 0x0100 && (Keys)(int)message.WParam is Keys.Home or Keys.End)
            {
                Count++;
                return true;
            }

            return false;
        }
    }
}
