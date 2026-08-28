using System;
using System.ComponentModel;
using System.Linq;
using Forms = System.Windows.Forms;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class DataGridViewLookupSortBehaviorTests
{
    public static void Run()
    {
        NamedColumnsUseCaseInsensitiveLookup();
        SortingPreservesRowsAndTheNewRowPlaceholder();
        SortingRejectsForeignColumnsAndInvalidDirections();
        Console.WriteLine("LibreWinForms DataGridView lookup/sort tests passed: lookup=5 sort=8 guarded=2.");
    }

    private static void NamedColumnsUseCaseInsensitiveLookup()
    {
        var grid = new Forms.DataGridView();
        var nameColumn = new Forms.DataGridViewTextBoxColumn { Name = "nameColumn" };
        var valueColumn = new Forms.DataGridViewTextBoxColumn { Name = "valueColumn" };
        grid.Columns.AddRange([nameColumn, valueColumn]);

        Assert(ReferenceEquals(grid.Columns["nameColumn"], nameColumn), "Exact named-column lookup failed.");
        Assert(ReferenceEquals(grid.Columns["VALUECOLUMN"], valueColumn), "Named-column lookup was not case-insensitive.");
        Assert(grid.Columns["missing"] is null, "Missing named-column lookup did not return null.");
        Assert(grid.Columns.Contains("NAMECOLUMN") && grid.Columns.IndexOf("valueColumn") == 1,
            "Named-column Contains/IndexOf did not share lookup semantics.");
        Assert(ReferenceEquals(nameColumn.DataGridView, grid) && valueColumn.DataGridView == grid,
            "Columns did not publish their owning DataGridView.");
    }

    private static void SortingPreservesRowsAndTheNewRowPlaceholder()
    {
        var grid = new Forms.DataGridView();
        var nameColumn = new Forms.DataGridViewTextBoxColumn { Name = "name" };
        grid.Columns.Add(nameColumn);
        int rowsAdded = 0;
        int rowsRemoved = 0;
        int invalidated = 0;
        grid.RowsAdded += (_, _) => rowsAdded++;
        grid.RowsRemoved += (_, _) => rowsRemoved++;
        grid.Invalidated += (_, _) => invalidated++;

        int betaIndex = grid.Rows.Add("beta");
        int nullIndex = grid.Rows.Add((object?)null);
        int alphaIndex = grid.Rows.Add("Alpha");
        int secondAlphaIndex = grid.Rows.Add("Alpha");
        Forms.DataGridViewRow beta = grid.Rows[betaIndex];
        Forms.DataGridViewRow nullRow = grid.Rows[nullIndex];
        Forms.DataGridViewRow alpha = grid.Rows[alphaIndex];
        Forms.DataGridViewRow secondAlpha = grid.Rows[secondAlphaIndex];
        Forms.DataGridViewRow placeholder = grid.Rows[grid.NewRowIndex];
        int rowsAddedBeforeSort = rowsAdded;
        int invalidatedBeforeSort = invalidated;

        grid.Sort(nameColumn, ListSortDirection.Ascending);
        Assert(ReferenceEquals(grid.Rows[0], nullRow), "Ascending sort did not place null first.");
        Assert(ReferenceEquals(grid.Rows[1], alpha) && ReferenceEquals(grid.Rows[2], secondAlpha),
            "Ascending sort was not stable for equal values.");
        Assert(ReferenceEquals(grid.Rows[3], beta), "Ascending sort did not order comparable values.");
        Assert(grid.NewRowIndex == 4 && ReferenceEquals(grid.Rows[4], placeholder) && placeholder.IsNewRow,
            "Ascending sort moved or replaced the new-row placeholder.");
        Assert(grid.Rows.Select((row, index) => row.Index == index).All(value => value),
            "Ascending sort did not refresh row indices.");

        grid.Sort(nameColumn, ListSortDirection.Descending);
        Assert(ReferenceEquals(grid.Rows[0], beta), "Descending sort did not reverse comparable values.");
        Assert(ReferenceEquals(grid.Rows[1], alpha) && ReferenceEquals(grid.Rows[2], secondAlpha),
            "Descending sort did not keep equal values stable.");
        Assert(ReferenceEquals(grid.Rows[3], nullRow), "Descending sort did not place null last.");
        Assert(grid.NewRowIndex == 4 && ReferenceEquals(grid.Rows[4], placeholder),
            "Descending sort moved the new-row placeholder.");
        Assert(rowsAdded == rowsAddedBeforeSort && rowsRemoved == 0,
            "Sorting raised row add/remove lifecycle events.");
        Assert(invalidated - invalidatedBeforeSort == 2,
            "Sorting did not invalidate exactly once per completed order change.");
    }

    private static void SortingRejectsForeignColumnsAndInvalidDirections()
    {
        var grid = new Forms.DataGridView();
        var ownColumn = new Forms.DataGridViewTextBoxColumn();
        grid.Columns.Add(ownColumn);
        grid.Rows.Add("value");

        AssertThrows<ArgumentException>(
            () => grid.Sort(new Forms.DataGridViewTextBoxColumn(), ListSortDirection.Ascending),
            "Sorting accepted a foreign column.");
        AssertThrows<InvalidEnumArgumentException>(
            () => grid.Sort(ownColumn, (ListSortDirection)42),
            "Sorting accepted an invalid direction.");
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
