using System;
using System.Data;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class DataGridViewCompatibilityBehaviorTests
{
    public static void Run()
    {
        MaskedTextBoxAppliesPortableMasking();
        CellTemplatesCreateTypedCellsAndEditors();
        DataTablesPopulatePortableRowsAndColumns();
        PaintingContractsReplayThroughSystemDrawing();
        Console.WriteLine("LibreWinForms DataGridView compatibility tests passed: mask=7 templates=9 binding=8 paint=5.");
    }

    private static void MaskedTextBoxAppliesPortableMasking()
    {
        using var textBox = new Forms.MaskedTextBox("000-00")
        {
            TextMaskFormat = Forms.MaskFormat.IncludeLiterals,
            Text = "12345"
        };

        Assert(textBox.Text == "123-45", "MaskedTextBox did not format accepted input.");
        Assert(textBox.MaskCompleted && textBox.MaskFull, "MaskedTextBox did not report a completed mask.");
        textBox.TextMaskFormat = Forms.MaskFormat.ExcludePromptAndLiterals;
        Assert(textBox.Text == "12345", "MaskedTextBox did not honor TextMaskFormat.");
        textBox.ValidatingType = typeof(int);
        Assert(Equals(textBox.ValidateText(), 12345), "MaskedTextBox did not validate its text through the configured type.");
        textBox.Mask = string.Empty;
        textBox.Text = "portable";
        Assert(textBox.Text == "portable" && textBox.MaskCompleted, "Unmasked MaskedTextBox did not preserve ordinary text.");
    }

    private static void CellTemplatesCreateTypedCellsAndEditors()
    {
        using var grid = new Forms.DataGridView { AllowUserToAddRows = false };
        var column = new TestColumn();
        grid.Columns.Add(column);
        grid.Rows.Add("value");

        Forms.DataGridViewCell cell = grid.Rows.SharedRow(0).Cells[0];
        Assert(cell is TestCell, "DataGridViewColumn did not clone its typed CellTemplate.");
        Assert(ReferenceEquals(cell.OwningColumn, column), "Created cell did not expose its owning column.");
        Assert(column.ValueType == typeof(string), "Column ValueType was not retained.");
        grid.CurrentCell = cell;
        Assert(grid.BeginEdit(selectAll: false), "Custom cell did not begin editing.");
        Assert(grid.EditingControl is TestEditor, "Custom cell EditType did not create its editor.");
        Assert(((TestCell)cell).Initialized, "Custom cell InitializeEditingControl was not invoked.");
        Assert(grid.EndEdit(), "Custom cell editor did not end editing.");
    }

    private static void DataTablesPopulatePortableRowsAndColumns()
    {
        var table = new DataTable();
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Count", typeof(int));
        table.Rows.Add("alpha", 3);
        table.Rows.Add("beta", 7);

        using var grid = new Forms.DataGridView { AllowUserToAddRows = false, DataSource = table };
        Assert(grid.ColumnCount == 2 && grid.RowCount == 2, "DataTable binding produced the wrong grid dimensions.");
        Assert(grid.Columns[0].Name == "Name" && grid.Columns[1].ValueType == typeof(int),
            "DataTable binding did not preserve column metadata.");
        Assert(Equals(grid.Rows[0].Cells[0].Value, "alpha") && Equals(grid.Rows[1].Cells[1].Value, 7),
            "DataTable binding did not preserve row values.");
        Assert(grid.Columns.Add("Extra", "Extra header") == 2 && grid.Columns["Extra"]?.HeaderText == "Extra header",
            "String column addition did not preserve name/header metadata.");
    }

    private static void PaintingContractsReplayThroughSystemDrawing()
    {
        using var grid = new Forms.DataGridView { Size = new Size(160, 80), AllowUserToAddRows = false };
        grid.Columns.Add("Value", "Value");
        grid.Rows.Add("painted");
        using var bitmap = new Bitmap(160, 80);
        using Graphics graphics = Graphics.FromImage(bitmap);
        var args = new Forms.DataGridViewCellPaintingEventArgs(
            grid,
            graphics,
            new Rectangle(0, 0, 160, 80),
            grid.GetCellDisplayRectangle(0, 0, false),
            0,
            0,
            Forms.DataGridViewElementStates.Visible,
            "painted",
            "painted",
            null,
            grid.DefaultCellStyle,
            null,
            Forms.DataGridViewPaintParts.All);
        args.Paint(args.CellBounds, Forms.DataGridViewPaintParts.All);
        Assert(args.Graphics == graphics && args.Value as string == "painted", "Painting event state changed during replay.");
    }

    private sealed class TestColumn : Forms.DataGridViewColumn
    {
        public TestColumn()
            : base(new TestCell())
        {
            ValueType = typeof(string);
        }
    }

    private sealed class TestCell : Forms.DataGridViewTextBoxCell
    {
        public bool Initialized { get; private set; }

        public override Type EditType => typeof(TestEditor);

        public override void InitializeEditingControl(int rowIndex, object? initialFormattedValue, Forms.DataGridViewCellStyle dataGridViewCellStyle)
        {
            base.InitializeEditingControl(rowIndex, initialFormattedValue, dataGridViewCellStyle);
            Initialized = DataGridView?.EditingControl is TestEditor;
        }
    }

    private sealed class TestEditor : Forms.TextBox
    {
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
