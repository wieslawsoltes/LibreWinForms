using System;
using System.Collections.Generic;
using Forms = System.Windows.Forms;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class DataGridViewNewRowBehaviorTests
{
    public static void Run()
    {
        PlaceholderRequiresAColumnAndTracksTheBackedProperty();
        AllAddPathsInsertRealRowsBeforeThePlaceholder();
        ClearRetainsOnlyThePlaceholder();
        TogglingPreservesRealRows();
        ColumnMutationsKeepEveryRowShapeSynchronized();
        PublicPlaceholderRemovalAndReplacementFailClosed();
        Console.WriteLine("LibreWinForms DataGridView new-row tests passed: lifecycle=1 adds=5 clear=1 toggle=1 columns=5 guarded=3.");
    }

    private static void PlaceholderRequiresAColumnAndTracksTheBackedProperty()
    {
        var grid = new Forms.DataGridView();
        int changed = 0;
        grid.AllowUserToAddRowsChanged += (_, _) =>
        {
            changed++;
            AssertPlaceholderInvariant(grid);
        };

        Assert(grid.AllowUserToAddRows, "AllowUserToAddRows did not default to true.");
        AssertPlaceholderInvariant(grid);

        grid.Columns.Add(new Forms.DataGridViewTextBoxColumn());
        AssertPlaceholderInvariant(grid);
        Assert(grid.NewRowIndex == 0, "Adding the first column did not create the initial placeholder.");
        Assert(grid.Rows[0].Cells[0] is Forms.DataGridViewTextBoxCell,
            "The initial placeholder did not use the first column's cell type.");

        grid.AllowUserToAddRows = true;
        Assert(changed == 0, "Assigning the current AllowUserToAddRows value raised a change event.");

        Forms.DataGridViewRow oldPlaceholder = grid.Rows[grid.NewRowIndex];
        grid.AllowUserToAddRows = false;
        Assert(changed == 1 && grid.Rows.Count == 0 && grid.NewRowIndex == -1,
            "Disabling AllowUserToAddRows did not remove the placeholder before publishing the event.");
        Assert(oldPlaceholder.DataGridView is null && oldPlaceholder.Index == -1 && !oldPlaceholder.IsNewRow,
            "The disabled placeholder retained grid ownership or new-row state.");

        grid.AllowUserToAddRows = false;
        Assert(changed == 1, "Repeatedly disabling AllowUserToAddRows raised a duplicate change event.");

        grid.AllowUserToAddRows = true;
        Assert(changed == 2, "Re-enabling AllowUserToAddRows did not raise one change event.");
        AssertPlaceholderInvariant(grid);
    }

    private static void AllAddPathsInsertRealRowsBeforeThePlaceholder()
    {
        var grid = new Forms.DataGridView();
        grid.Columns.Add(new Forms.DataGridViewTextBoxColumn());
        grid.Columns.Add(new Forms.DataGridViewComboBoxColumn());

        int valuesIndex = grid.Rows.Add("alpha", "one");
        int emptyIndex = grid.Rows.Add();
        var explicitRow = new Forms.DataGridViewRow();
        int explicitIndex = grid.Rows.Add(explicitRow);
        var interfaceRow = new Forms.DataGridViewRow();
        ((ICollection<Forms.DataGridViewRow>)grid.Rows).Add(interfaceRow);
        var insertedAtEnd = new Forms.DataGridViewRow();
        grid.Rows.Insert(grid.Rows.Count, insertedAtEnd);

        Assert(valuesIndex == 0
            && emptyIndex == 1
            && explicitIndex == 2
            && interfaceRow.Index == 3
            && insertedAtEnd.Index == 4,
            "A real row insertion path did not return or assign its pre-placeholder index.");
        Assert(grid.NewRowIndex == 5 && grid.Rows.Count == 6,
            "Real row insertions did not keep one final placeholder.");
        Assert(ReferenceEquals(grid.Rows[2], explicitRow)
            && ReferenceEquals(grid.Rows[3], interfaceRow)
            && ReferenceEquals(grid.Rows[4], insertedAtEnd),
            "Explicit row insertion paths did not preserve row identity.");
        Assert(Equals(grid.Rows[0].Cells[0].Value, "alpha") && Equals(grid.Rows[0].Cells[1].Value, "one"),
            "The values Add path did not populate cells before inserting the row.");
        Assert(grid.Rows[0].Cells[1] is Forms.DataGridViewComboBoxCell,
            "The values Add path did not use the owning column's cell type.");
        AssertPlaceholderInvariant(grid);
    }

    private static void ClearRetainsOnlyThePlaceholder()
    {
        var grid = CreateGridWithOneColumn();
        var first = new Forms.DataGridViewRow();
        var second = new Forms.DataGridViewRow();
        grid.Rows.Add(first);
        grid.Rows.Add(second);
        Forms.DataGridViewRow placeholder = grid.Rows[grid.NewRowIndex];

        grid.Rows.Clear();

        Assert(grid.Rows.Count == 1 && grid.NewRowIndex == 0 && ReferenceEquals(grid.Rows[0], placeholder),
            "Rows.Clear did not retain the existing final placeholder.");
        Assert(first.DataGridView is null && first.Index == -1 && second.DataGridView is null && second.Index == -1,
            "Rows.Clear did not detach all real rows.");
        AssertPlaceholderInvariant(grid);
    }

    private static void TogglingPreservesRealRows()
    {
        var grid = CreateGridWithOneColumn();
        var first = new Forms.DataGridViewRow();
        var second = new Forms.DataGridViewRow();
        grid.Rows.Add(first);
        grid.Rows.Add(second);
        Forms.DataGridViewRow oldPlaceholder = grid.Rows[grid.NewRowIndex];

        grid.AllowUserToAddRows = false;
        Assert(grid.Rows.Count == 2 && grid.NewRowIndex == -1,
            "Disabling the placeholder removed or added real rows.");
        Assert(ReferenceEquals(grid.Rows[0], first) && ReferenceEquals(grid.Rows[1], second),
            "Disabling the placeholder reordered real rows.");
        Assert(first.Index == 0 && second.Index == 1,
            "Disabling the placeholder left stale real-row indices.");

        grid.AllowUserToAddRows = true;
        Assert(grid.Rows.Count == 3 && grid.NewRowIndex == 2,
            "Re-enabling the placeholder did not append exactly one row.");
        Assert(ReferenceEquals(grid.Rows[0], first) && ReferenceEquals(grid.Rows[1], second),
            "Re-enabling the placeholder replaced or reordered real rows.");
        Assert(!ReferenceEquals(grid.Rows[2], oldPlaceholder),
            "Re-enabling reused a detached placeholder instead of creating managed new-row state.");
        AssertPlaceholderInvariant(grid);
    }

    private static void ColumnMutationsKeepEveryRowShapeSynchronized()
    {
        var grid = new Forms.DataGridView();
        var firstColumn = new Forms.DataGridViewTextBoxColumn { Name = "first" };
        var lastColumn = new Forms.DataGridViewComboBoxColumn { Name = "last" };
        grid.Columns.Add(firstColumn);
        grid.Columns.Add(lastColumn);
        int rowIndex = grid.Rows.Add("left", "right");
        Forms.DataGridViewRow row = grid.Rows[rowIndex];
        Forms.DataGridViewCell firstCell = row.Cells[0];
        Forms.DataGridViewCell lastCell = row.Cells[1];

        var middleColumn = new Forms.DataGridViewTextBoxColumn { Name = "middle" };
        grid.Columns.Insert(1, middleColumn);
        Assert(row.Cells.Count == 3
            && ReferenceEquals(row.Cells[0], firstCell)
            && row.Cells[1] is Forms.DataGridViewTextBoxCell
            && ReferenceEquals(row.Cells[2], lastCell),
            "Inserting a column did not insert corresponding cells at the same index.");
        Assert(Equals(row.Cells[0].Value, "left") && row.Cells[1].Value is null && Equals(row.Cells[2].Value, "right"),
            "Inserting a column shifted or replaced existing cell values incorrectly.");
        AssertPlaceholderInvariant(grid);

        grid.Columns[1] = new Forms.DataGridViewComboBoxColumn { Name = "replacement" };
        Assert(row.Cells[1] is Forms.DataGridViewComboBoxCell && row.Cells[1].Value is null,
            "Replacing a column did not replace the corresponding row cell type.");
        AssertPlaceholderInvariant(grid);

        grid.Columns.RemoveAt(0);
        Assert(row.Cells.Count == 2
            && row.Cells[0] is Forms.DataGridViewComboBoxCell
            && ReferenceEquals(row.Cells[1], lastCell)
            && Equals(row.Cells[1].Value, "right"),
            "Removing a column did not remove the matching cell from each row.");
        Assert(firstColumn.Index == -1 && grid.Columns[0].Index == 0 && grid.Columns[1].Index == 1,
            "Removing a column left stale column indices.");
        AssertPlaceholderInvariant(grid);

        Forms.DataGridViewRow oldPlaceholder = grid.Rows[grid.NewRowIndex];
        grid.Columns.Clear();
        Assert(grid.Columns.Count == 0 && grid.NewRowIndex == -1 && grid.Rows.Count == 1,
            "Clearing columns did not remove only the placeholder.");
        Assert(row.Cells.Count == 0 && oldPlaceholder.DataGridView is null && !oldPlaceholder.IsNewRow,
            "Clearing columns left row cells or a live placeholder behind.");
        AssertPlaceholderInvariant(grid);

        grid.Columns.Add(new Forms.DataGridViewComboBoxColumn());
        Assert(row.Cells.Count == 1 && row.Cells[0] is Forms.DataGridViewComboBoxCell,
            "Adding a column after a clear did not recreate real-row cells.");
        Assert(grid.Rows.Count == 2 && grid.NewRowIndex == 1,
            "Adding a column after a clear did not recreate one final placeholder.");
        AssertPlaceholderInvariant(grid);
    }

    private static void PublicPlaceholderRemovalAndReplacementFailClosed()
    {
        var grid = CreateGridWithOneColumn();
        grid.Rows.Add("real");
        Forms.DataGridViewRow placeholder = grid.Rows[grid.NewRowIndex];

        AssertThrowsInvalidOperation(() => grid.Rows.RemoveAt(grid.NewRowIndex),
            "Rows.RemoveAt accepted the new-row placeholder.");
        AssertThrowsInvalidOperation(() => grid.Rows.Remove(placeholder),
            "Rows.Remove accepted the new-row placeholder.");
        AssertThrowsInvalidOperation(() => grid.Rows[grid.NewRowIndex] = new Forms.DataGridViewRow(),
            "The row indexer replaced the new-row placeholder.");

        Assert(grid.Rows.Count == 2 && grid.NewRowIndex == 1 && ReferenceEquals(grid.Rows[1], placeholder),
            "A rejected placeholder mutation changed the row collection.");
        AssertPlaceholderInvariant(grid);
    }

    private static Forms.DataGridView CreateGridWithOneColumn()
    {
        var grid = new Forms.DataGridView();
        grid.Columns.Add(new Forms.DataGridViewTextBoxColumn());
        return grid;
    }

    private static void AssertPlaceholderInvariant(Forms.DataGridView grid)
    {
        bool shouldHavePlaceholder = grid.AllowUserToAddRows && grid.Columns.Count > 0;
        int placeholderCount = 0;
        for (int rowIndex = 0; rowIndex < grid.Rows.Count; rowIndex++)
        {
            Forms.DataGridViewRow row = grid.Rows[rowIndex];
            Assert(row.Index == rowIndex && ReferenceEquals(row.DataGridView, grid),
                "A row has stale collection ownership or index state.");
            Assert(row.Cells.Count == grid.Columns.Count,
                "A row cell count does not match the current column count.");
            for (int columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
            {
                Forms.DataGridViewCell cell = row.Cells[columnIndex];
                Assert(cell.RowIndex == rowIndex
                    && cell.ColumnIndex == columnIndex
                    && ReferenceEquals(cell.DataGridView, grid)
                    && ReferenceEquals(cell.OwningRow, row),
                    "A synchronized cell has stale row, column, or grid ownership.");
            }

            if (row.IsNewRow)
            {
                placeholderCount++;
            }
        }

        if (shouldHavePlaceholder)
        {
            Assert(grid.Rows.Count > 0 && grid.NewRowIndex == grid.Rows.Count - 1,
                "The new-row placeholder is not the final row.");
            Assert(placeholderCount == 1 && grid.Rows[grid.NewRowIndex].IsNewRow,
                "The grid does not expose exactly one final new row.");
        }
        else
        {
            Assert(grid.NewRowIndex == -1 && placeholderCount == 0,
                "The grid exposes a placeholder while new rows are unavailable.");
        }
    }

    private static void AssertThrowsInvalidOperation(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
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
