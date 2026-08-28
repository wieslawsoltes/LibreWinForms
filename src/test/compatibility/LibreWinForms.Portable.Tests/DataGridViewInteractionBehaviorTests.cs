using System;
using System.Drawing;
using System.IO;
using Forms = System.Windows.Forms;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class DataGridViewInteractionBehaviorTests
{
    public static void Run()
    {
        DisplayGeometryAndHitTestingShareOneContract();
        CurrentCellValidationAndLifecycleStayConsistent();
        ReadOnlyStatePreventsOrCancelsEditing();
        TextEditingCommitsAndCancelsThroughChildControls();
        ComboBoxEditingUsesTypedColumnItems();
        HostRoutesDataGridViewInteractionWithoutReflection();
        Console.WriteLine("LibreWinForms DataGridView interaction tests passed: geometry=14 current=9 edit=24 host=10.");
    }

    private static void DisplayGeometryAndHitTestingShareOneContract()
    {
        using Forms.DataGridView grid = CreateGrid();
        Assert(grid.GetCellDisplayRectangle(-1, -1, cutOverflow: false) == new Rectangle(1, 1, 40, 22),
            "Top-left header geometry is incorrect.");
        Assert(grid.GetCellDisplayRectangle(0, -1, cutOverflow: false) == new Rectangle(41, 1, 80, 22),
            "First column-header geometry is incorrect.");
        Assert(grid.GetCellDisplayRectangle(-1, 0, cutOverflow: false) == new Rectangle(1, 23, 40, 20),
            "First row-header geometry is incorrect.");
        Assert(grid.GetCellDisplayRectangle(0, 0, cutOverflow: false) == new Rectangle(41, 23, 80, 20),
            "First cell geometry is incorrect.");
        Assert(grid.GetCellDisplayRectangle(1, 1, cutOverflow: false) == new Rectangle(121, 43, 90, 20),
            "Second-row/second-column geometry is incorrect.");

        Forms.DataGridView.HitTestInfo topLeft = grid.HitTest(2, 2);
        Assert(topLeft.Type == Forms.DataGridViewHitTestType.TopLeftHeader
            && topLeft.ColumnIndex == -1
            && topLeft.RowIndex == -1
            && topLeft.ColumnX == 1
            && topLeft.RowY == 1,
            "Top-left header hit metadata is incorrect.");

        Forms.DataGridView.HitTestInfo columnHeader = grid.HitTest(45, 2);
        Assert(columnHeader.Type == Forms.DataGridViewHitTestType.ColumnHeader
            && columnHeader.ColumnIndex == 0
            && columnHeader.RowIndex == -1,
            "Column-header hit metadata is incorrect.");

        Forms.DataGridView.HitTestInfo rowHeader = grid.HitTest(2, 25);
        Assert(rowHeader.Type == Forms.DataGridViewHitTestType.RowHeader
            && rowHeader.ColumnIndex == -1
            && rowHeader.RowIndex == 0,
            "Row-header hit metadata is incorrect.");

        Forms.DataGridView.HitTestInfo cell = grid.HitTest(125, 45);
        Assert(cell.Type == Forms.DataGridViewHitTestType.Cell
            && cell.ColumnIndex == 1
            && cell.RowIndex == 1
            && cell.ColumnX == 121
            && cell.RowY == 43,
            "Cell hit metadata is incorrect.");
        Assert(cell.Equals(grid.HitTest(130, 50)) && cell.GetHashCode() == grid.HitTest(130, 50).GetHashCode(),
            "Equivalent cell hits do not compare equally.");
        Assert(cell.ToString() == "{ Type:Cell, Column:1, Row:1 }", "HitTestInfo text contract is incorrect.");
        Assert(ReferenceEquals(grid.HitTest(250, 100), Forms.DataGridView.HitTestInfo.Nowhere),
            "Blank client area did not return HitTestInfo.Nowhere.");
        Assert((int)Forms.DataGridViewHitTestType.VerticalScrollBar == 6,
            "DataGridViewHitTestType does not preserve the WinForms numeric contract.");

        grid.Size = new Size(150, 55);
        Assert(grid.GetCellDisplayRectangle(1, 1, cutOverflow: true) == new Rectangle(121, 43, 29, 12),
            "Overflow clipping does not intersect the client rectangle.");
        Assert(grid.GetCellDisplayRectangle(1, 1, cutOverflow: false) == new Rectangle(121, 43, 90, 20),
            "Unclipped cell geometry was unexpectedly truncated.");
        AssertThrows<ArgumentOutOfRangeException>(() => grid.GetCellDisplayRectangle(-2, 0, false),
            "Invalid negative column index was accepted.");
        AssertThrows<ArgumentOutOfRangeException>(() => grid.GetCellDisplayRectangle(0, grid.Rows.Count, false),
            "Out-of-range row index was accepted.");
    }

    private static void CurrentCellValidationAndLifecycleStayConsistent()
    {
        using Forms.DataGridView grid = CreateGrid();
        int changed = 0;
        grid.CurrentCellChanged += (_, _) => changed++;
        Forms.DataGridViewCell first = grid.Rows[0].Cells[0];
        Forms.DataGridViewCell second = grid.Rows[1].Cells[1];

        grid.CurrentCell = first;
        Assert(ReferenceEquals(grid.CurrentCell, first) && ReferenceEquals(grid.CurrentRow, grid.Rows[0]),
            "CurrentCell did not publish its current row.");
        Assert(changed == 1, "Initial CurrentCell assignment did not raise exactly one event.");
        grid.CurrentCell = first;
        Assert(changed == 1, "Idempotent CurrentCell assignment raised an event.");

        using Forms.DataGridView foreignGrid = CreateGrid();
        AssertThrows<ArgumentException>(() => grid.CurrentCell = foreignGrid.Rows[0].Cells[0],
            "CurrentCell accepted a cell owned by another DataGridView.");
        Assert(ReferenceEquals(grid.CurrentCell, first) && changed == 1,
            "Rejected foreign CurrentCell assignment changed state.");

        grid.CurrentCell = second;
        Assert(ReferenceEquals(grid.CurrentRow, grid.Rows[1]) && changed == 2,
            "CurrentCell did not move to the second row.");
        grid.Rows.RemoveAt(1);
        Assert(grid.CurrentCell is null && grid.CurrentRow is null && changed == 3,
            "Removing the current row did not clear current-cell state.");
        grid.CurrentCell = null;
        Assert(changed == 3, "Idempotent null CurrentCell assignment raised an event.");
    }

    private static void ReadOnlyStatePreventsOrCancelsEditing()
    {
        using Forms.DataGridView grid = CreateGrid();
        Forms.DataGridViewCell cell = grid.Rows[0].Cells[0];
        grid.CurrentCell = cell;

        cell.ReadOnly = true;
        Assert(!grid.BeginEdit(selectAll: false), "Cell ReadOnly state allowed editing.");
        cell.ReadOnly = false;
        grid.Rows[0].ReadOnly = true;
        Assert(cell.ReadOnly && !grid.BeginEdit(selectAll: false), "Row ReadOnly state allowed editing.");
        grid.Rows[0].ReadOnly = false;
        grid.Columns[0].ReadOnly = true;
        Assert(cell.ReadOnly && !grid.BeginEdit(selectAll: false), "Column ReadOnly state allowed editing.");
        grid.Columns[0].ReadOnly = false;
        grid.ReadOnly = true;
        Assert(cell.ReadOnly && !grid.BeginEdit(selectAll: false), "Grid ReadOnly state allowed editing.");

        grid.ReadOnly = false;
        Assert(grid.BeginEdit(selectAll: false), "Writable cell did not begin editing.");
        ((Forms.TextBox)grid.EditingControl!).Text = "discarded";
        grid.Columns[0].ReadOnly = true;
        Assert(!grid.IsCurrentCellInEditMode && Equals(cell.Value, "alpha"),
            "Becoming read-only did not cancel and discard the active edit.");
    }

    private static void TextEditingCommitsAndCancelsThroughChildControls()
    {
        using Forms.DataGridView grid = CreateGrid();
        Forms.DataGridViewCell first = grid.Rows[0].Cells[0];
        Forms.DataGridViewCell second = grid.Rows[1].Cells[0];
        grid.CurrentCell = first;
        int showing = 0;
        int valueChanged = 0;
        grid.EditingControlShowing += (_, e) =>
        {
            showing++;
            Assert(ReferenceEquals(e.Control, grid.EditingControl), "EditingControlShowing exposed a different child editor.");
        };
        grid.CellValueChanged += (_, e) =>
        {
            valueChanged++;
            Assert(e.ColumnIndex == 0, "CellValueChanged reported the wrong column.");
        };

        Assert(grid.BeginEdit(selectAll: true), "Text cell did not begin editing.");
        Assert(grid.IsCurrentCellInEditMode && grid.EditingControl is Forms.TextBox,
            "Text cell did not create a text child editor.");
        Forms.TextBox editor = (Forms.TextBox)grid.EditingControl!;
        Assert(ReferenceEquals(editor.Parent, grid) && grid.Controls.Contains(editor),
            "Text editor was not parented to the DataGridView.");
        Assert(editor.Bounds == grid.GetCellDisplayRectangle(0, 0, cutOverflow: false),
            "Text editor bounds do not share DataGridView cell geometry.");
        Assert(editor.SelectionStart == 0 && editor.SelectionLength == editor.TextLength,
            "BeginEdit(selectAll: true) did not select the text editor contents.");
        Assert(showing == 1 && editor.Focused, "Text editor did not show and receive focus exactly once.");
        Assert(grid.BeginEdit(selectAll: false), "Repeated BeginEdit rejected the active current-cell edit.");
        Assert(showing == 1, "Repeated BeginEdit created a second editor.");

        editor.Text = "committed";
        Assert(grid.EndEdit(), "EndEdit rejected a writable text value.");
        Assert(Equals(first.Value, "committed") && valueChanged == 1,
            "EndEdit did not commit once and raise CellValueChanged once.");
        Assert(!grid.IsCurrentCellInEditMode && grid.EditingControl is null && editor.IsDisposed,
            "EndEdit did not detach and dispose the text editor.");
        Assert(grid.EndEdit() && valueChanged == 1, "Repeated EndEdit raised a spurious value change.");

        Assert(grid.BeginEdit(selectAll: false), "Second text edit did not begin.");
        Forms.TextBox cancelledEditor = (Forms.TextBox)grid.EditingControl!;
        cancelledEditor.Text = "cancelled";
        Forms.KeyEventArgs escape = new(Forms.Keys.Escape);
        cancelledEditor.RaiseKeyDown(escape);
        Assert(escape.Handled && escape.SuppressKeyPress && Equals(first.Value, "committed") && valueChanged == 1,
            "Escape did not cancel the text edit without publishing a value change.");

        Assert(grid.BeginEdit(selectAll: false), "Keyboard-commit text edit did not begin.");
        Forms.TextBox enterEditor = (Forms.TextBox)grid.EditingControl!;
        enterEditor.Text = "enter";
        Forms.KeyEventArgs enter = new(Forms.Keys.Enter);
        enterEditor.RaiseKeyDown(enter);
        Assert(enter.Handled && enter.SuppressKeyPress && Equals(first.Value, "enter") && valueChanged == 2,
            "Enter did not commit the text edit.");

        Assert(grid.BeginEdit(selectAll: false), "CurrentCell-switch text edit did not begin.");
        ((Forms.TextBox)grid.EditingControl!).Text = "switch";
        grid.CurrentCell = second;
        Assert(Equals(first.Value, "switch") && valueChanged == 3 && ReferenceEquals(grid.CurrentCell, second),
            "Changing CurrentCell did not commit the prior edit before moving.");
    }

    private static void ComboBoxEditingUsesTypedColumnItems()
    {
        using var grid = new Forms.DataGridView
        {
            Size = new Size(240, 100),
            AllowUserToAddRows = false
        };
        var column = new Forms.DataGridViewComboBoxColumn { Width = 100 };
        column.Items.Add("one");
        column.Items.Add("two");
        grid.Columns.Add(column);
        grid.Rows.Add("one");
        Forms.DataGridViewCell cell = grid.Rows[0].Cells[0];
        Assert(cell is Forms.DataGridViewComboBoxCell comboCell && comboCell.Items.Count == 2,
            "Combo-box column items were not copied into created cells.");

        grid.CurrentCell = cell;
        Assert(grid.BeginEdit(selectAll: true) && grid.EditingControl is Forms.ComboBox,
            "Combo-box cell did not create a combo child editor.");
        Forms.ComboBox editor = (Forms.ComboBox)grid.EditingControl!;
        Assert(editor.Items.Count == 2 && editor.SelectedIndex == 0 && Equals(editor.SelectedItem, "one"),
            "Combo child editor did not preserve typed cell items and selection.");
        editor.SelectedIndex = 1;
        Assert(grid.EndEdit() && Equals(cell.Value, "two"), "Combo child editor did not commit the selected item.");
    }

    private static void HostRoutesDataGridViewInteractionWithoutReflection()
    {
        string repositoryRoot = FindRepositoryRoot();
        string interactionPath = Path.Combine(
            repositoryRoot,
            "src",
            "LibreWinForms.Portable",
            "LibreWinForms.System.Windows.Forms",
            "src",
            "DataGridViewInteractionCompatTypes.cs");
        string hostPath = Path.Combine(
            repositoryRoot,
            "src",
            "LibreWinForms.Portable",
            "LibreWinForms.WindowsFormsIntegration",
            "src",
            "WindowsFormsHost.cs");
        string interactionSource = File.ReadAllText(interactionPath);
        string hostSource = File.ReadAllText(hostPath);
        string combinedSource = interactionSource + hostSource;

        foreach (string forbidden in new[] { "System.Reflection", "BindingFlags", "GetProperty(", "GetField(", "GetMethod(" })
        {
            Assert(!combinedSource.Contains(forbidden, StringComparison.Ordinal),
                $"DataGridView interaction path reintroduced forbidden reflection token '{forbidden}'.");
        }

        Assert(hostSource.Contains("dataGridView.HitTest(x, y)", StringComparison.Ordinal),
            "Hosted pointer selection does not use typed DataGridView.HitTest.");
        Assert(hostSource.Contains("Forms.DataGridViewHitTestType.Cell", StringComparison.Ordinal),
            "Hosted pointer selection does not branch on typed hit-test values.");
        Assert(hostSource.Contains("dataGridView.CurrentCell = cell", StringComparison.Ordinal),
            "Hosted pointer selection does not assign the typed current cell.");
        Assert(hostSource.Contains("dataGridView.BeginEdit(selectAll: true)", StringComparison.Ordinal),
            "Hosted pointer activation does not use the typed edit lifecycle.");
        Assert(hostSource.Contains("dataGridView.GetCellDisplayRectangle", StringComparison.Ordinal),
            "Hosted rendering does not share typed DataGridView display geometry.");
        Assert(!hostSource.Contains("GetDataGridViewColumnWidth", StringComparison.Ordinal),
            "Hosted rendering retained a second DataGridView geometry implementation.");
        Assert(interactionSource.Contains("Controls.Add(editor)", StringComparison.Ordinal)
            && interactionSource.Contains("new TextBox", StringComparison.Ordinal)
            && interactionSource.Contains("new ComboBox", StringComparison.Ordinal),
            "DataGridView editing is not backed by real typed child controls.");
    }

    private static Forms.DataGridView CreateGrid()
    {
        var grid = new Forms.DataGridView
        {
            Size = new Size(300, 120),
            RowHeadersWidth = 40,
            AllowUserToAddRows = false
        };
        grid.Columns.Add(new Forms.DataGridViewTextBoxColumn { Name = "first", Width = 80 });
        grid.Columns.Add(new Forms.DataGridViewTextBoxColumn { Name = "second", Width = 90 });
        grid.Rows.Add("alpha", "one");
        grid.Rows.Add("beta", "two");
        return grid;
    }

    private static string FindRepositoryRoot()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))
                    && Directory.Exists(Path.Combine(directory.FullName, "src", "LibreWinForms.Portable")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new InvalidOperationException("Could not locate the LibreWinForms repository root.");
    }

    private static void AssertThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
