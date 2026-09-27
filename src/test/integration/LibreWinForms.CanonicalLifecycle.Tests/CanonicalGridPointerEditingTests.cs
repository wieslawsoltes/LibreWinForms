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
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PortableGridPointerEntryRetainsEditorUntilEnter(bool useF2, bool newRow)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ClientSize = new Size(634, 446), ShowIcon = false };
        using Button initial = new() { Bounds = new Rectangle(26, 30, 100, 30) };
        using DataGridView grid = new() { Bounds = new Rectangle(26, 119, 577, 299) };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name", Width = 120 });
        if (!newRow)
            grid.Rows.Add("seed");
        form.Controls.Add(initial);
        form.Controls.Add(grid);
        form.ActiveControl = initial;
        form.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        initial.Focused.Should().BeTrue();

        DataGridViewCell cell = grid.Rows[0].Cells[0];
        Rectangle bounds = grid.GetCellDisplayRectangle(0, 0, cutOverflow: true);
        LibrePoint point = new(grid.Left + bounds.Left + bounds.Width / 2,
            grid.Top + bounds.Top + bounds.Height / 2);
        ClickCell();
        grid.CurrentCell.Should().BeSameAs(cell);
        if (useF2)
        {
            platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.F2);
            platform.SendInput(LibreInputEventKind.KeyUp, key: LibreKey.F2);
        }
        else
        {
            ClickCell();
        }

        grid.IsCurrentCellInEditMode.Should().BeTrue();
        DataGridViewTextBoxEditingControl editor = grid.EditingControl
            .Should().BeOfType<DataGridViewTextBoxEditingControl>().Subject;
        editor.Focused.Should().BeTrue();
        // A click into the live child must not end the source editing session.
        // Do not subscribe to CellValidating/RowValidating: those subscriptions
        // themselves invoke additional canonical focus correction.
        ClickCell();
        grid.EditingControl.Should().BeSameAs(editor);
        editor.Focused.Should().BeTrue();
        editor.SelectAll();
        platform.SendInput(LibreInputEventKind.TextInput, text: "Alice");
        editor.Text.Should().Be("Alice");
        platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.Enter);
        platform.SendInput(LibreInputEventKind.KeyUp, key: LibreKey.Enter);
        cell.Value.Should().Be("Alice");
        grid.IsCurrentCellInEditMode.Should().BeFalse();

        void ClickCell()
        {
            platform.SendInput(LibreInputEventKind.PointerDown,
                position: point, button: LibrePointerButton.Primary);
            platform.SendInput(LibreInputEventKind.PointerUp,
                position: point, button: LibrePointerButton.Primary);
        }
    }
}
