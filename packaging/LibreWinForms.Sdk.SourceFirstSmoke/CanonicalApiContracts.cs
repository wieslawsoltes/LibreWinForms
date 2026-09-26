// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Windows.Forms;

namespace LibreWinForms.TestContracts;

// Compiled unchanged against both the canonical source graph and the isolated
// SDK package consumer. These are real Forms controls, not API-shaped doubles.
internal static class CanonicalApiContracts
{
    internal const string ProductName = "LibreWinForms canonical API contracts";

    internal static void VerifyAll()
    {
        LabelAutoEllipsis();
        ButtonTextImageRelation();
        RowDefaultCellStyle();
        CellEditedFormattedValue();
        ColumnVisibility();
        GridColor();
        RowHeaderBorderStyle();
        ApplicationProductName();
        ApplicationContextLifetime();
        BindableComponentAndUpdateModes();
        BindingAndConversionEventArgs();
        NativeAndControlHandleLifetime();
        TableLayoutMixedSizing();
        TableLayoutSpansAndRtl();
        TableLayoutNestedInvalidation();
    }

    internal static void LabelAutoEllipsis()
    {
        using Label label = new() { Text = "Original label text" };
        Require(!label.AutoEllipsis, "Label.AutoEllipsis must default to false.");
        label.AutoEllipsis = true;
        label.AutoEllipsis = true;
        Require(label.AutoEllipsis && label.Text == "Original label text", "Enabling ellipsis must retain source text.");
        label.AutoEllipsis = false;
        Require(!label.AutoEllipsis && !label.IsHandleCreated, "Ellipsis property changes must not create a handle.");
    }

    internal static void ButtonTextImageRelation()
    {
        using Button button = new();
        Require(button.TextImageRelation == TextImageRelation.Overlay, "TextImageRelation default changed.");
        foreach (TextImageRelation value in new[]
        {
            TextImageRelation.Overlay, TextImageRelation.ImageAboveText, TextImageRelation.TextAboveImage,
            TextImageRelation.ImageBeforeText, TextImageRelation.TextBeforeImage
        })
        {
            button.TextImageRelation = value;
            Require(button.TextImageRelation == value, "ButtonBase must retain every declared TextImageRelation.");
        }

        Require((int)TextImageRelation.Overlay == 0 && (int)TextImageRelation.ImageAboveText == 1
            && (int)TextImageRelation.TextAboveImage == 2 && (int)TextImageRelation.ImageBeforeText == 4
            && (int)TextImageRelation.TextBeforeImage == 8, "TextImageRelation values changed.");
        RequireThrows<InvalidEnumArgumentException>(() => button.TextImageRelation = (TextImageRelation)3);
        Require(button.TextImageRelation == TextImageRelation.TextBeforeImage, "Invalid relation changed the previous value.");
    }

    internal static void RowDefaultCellStyle()
    {
        using DataGridView grid = CreateGrid();
        grid.DefaultCellStyle.ForeColor = Color.Navy;
        DataGridViewRow row = grid.Rows[0];
        DataGridViewCellStyle style = new() { ForeColor = Color.DarkRed, BackColor = Color.AliceBlue };
        row.DefaultCellStyle = style;
        Require(ReferenceEquals(row.DefaultCellStyle, style), "Rows[index].DefaultCellStyle lost its assigned style.");
        Require(row.Cells[0].InheritedStyle.ForeColor == Color.DarkRed
            && row.Cells[1].InheritedStyle.BackColor == Color.AliceBlue, "Cells must inherit the row style.");
        style.ForeColor = Color.DarkGreen;
        Require(row.Cells[0].InheritedStyle.ForeColor == Color.DarkGreen, "Live row style changes must reach cells.");
        Require(grid.Rows[1].Cells[0].InheritedStyle.ForeColor == Color.Navy, "A row style leaked into another row.");
        row.DefaultCellStyle = null;
        Require(row.Cells[0].InheritedStyle.ForeColor == Color.Navy, "Clearing a row style must restore grid inheritance.");
    }

    internal static void CellEditedFormattedValue()
    {
        using DataGridViewTextBoxCell detached = new();
        Require(detached.EditedFormattedValue is null, "A detached cell has no edited formatted value.");
        using DataGridView grid = CreateGrid();
        DataGridViewCell cell = grid.Rows[0].Cells[0];
        grid.CurrentCell = cell;
        Require(Equals(cell.EditedFormattedValue, "initial"), "Attached cell formatting lost its original value.");
        Require(grid.BeginEdit(selectAll: false), "The canonical cell must enter editing.");
        Require(grid.EditingControl is DataGridViewTextBoxEditingControl, "Text cells must use the real editing control.");
        ((DataGridViewTextBoxEditingControl)grid.EditingControl!).Text = "uncommitted";
        Require(Equals(cell.Value, "initial") && Equals(cell.EditedFormattedValue, "uncommitted"),
            "EditedFormattedValue must expose editor text without committing Value.");
        Require(grid.CancelEdit(), "CancelEdit failed.");
        Require(Equals(cell.Value, "initial") && Equals(cell.EditedFormattedValue, "initial"),
            "CancelEdit must discard edited formatting.");
        Require(grid.BeginEdit(selectAll: false), "The cell must be editable again after cancellation.");
        ((DataGridViewTextBoxEditingControl)grid.EditingControl!).Text = "committed";
        Require(grid.EndEdit(), "EndEdit failed.");
        Require(Equals(cell.Value, "committed") && Equals(cell.EditedFormattedValue, "committed")
            && grid.EditingControl is null, "Commit must publish the edited value and detach the editor.");
    }

    internal static void ColumnVisibility()
    {
        using DataGridView grid = CreateGrid();
        DataGridViewColumn column = grid.Columns[1];
        int changes = 0;
        grid.ColumnStateChanged += (_, e) =>
        {
            if (ReferenceEquals(e.Column, column) && e.StateChanged == DataGridViewElementStates.Visible)
            {
                changes++;
            }
        };
        Require(column.Visible && grid.Columns.GetColumnCount(DataGridViewElementStates.Visible) == 2,
            "Columns must initially be visible.");
        column.Visible = false;
        column.Visible = false;
        Require(!column.Visible && changes == 1 && grid.Columns.GetColumnCount(DataGridViewElementStates.Visible) == 1,
            "Hiding a column must update visibility and notify once.");
        column.Visible = true;
        Require(column.Visible && changes == 2 && grid.Columns.GetColumnCount(DataGridViewElementStates.Visible) == 2,
            "Showing a column must restore the visible count.");
    }

    internal static void GridColor()
    {
        using DataGridView grid = new();
        int changes = 0;
        grid.GridColorChanged += (_, _) => changes++;
        grid.GridColor = Color.CornflowerBlue;
        grid.GridColor = Color.CornflowerBlue;
        Require(grid.GridColor == Color.CornflowerBlue && changes == 1, "GridColor must retain and notify a changed opaque color once.");
        RequireThrows<ArgumentException>(() => grid.GridColor = Color.Empty);
        RequireThrows<ArgumentException>(() => grid.GridColor = Color.FromArgb(127, Color.Red));
        Require(grid.GridColor == Color.CornflowerBlue && changes == 1, "Invalid grid colors must leave state and events unchanged.");
    }

    internal static void RowHeaderBorderStyle()
    {
        using DataGridView grid = new();
        Require(grid.RowHeadersBorderStyle == DataGridViewHeaderBorderStyle.Raised, "Row header border default changed.");
        foreach (DataGridViewHeaderBorderStyle value in new[]
        {
            DataGridViewHeaderBorderStyle.Single, DataGridViewHeaderBorderStyle.Sunken,
            DataGridViewHeaderBorderStyle.None, DataGridViewHeaderBorderStyle.Raised
        })
        {
            grid.RowHeadersBorderStyle = value;
            Require(grid.RowHeadersBorderStyle == value, "RowHeadersBorderStyle failed to retain a supported value.");
        }

        Require((int)DataGridViewHeaderBorderStyle.Custom == 0 && (int)DataGridViewHeaderBorderStyle.Single == 1
            && (int)DataGridViewHeaderBorderStyle.Raised == 2 && (int)DataGridViewHeaderBorderStyle.Sunken == 3
            && (int)DataGridViewHeaderBorderStyle.None == 4, "DataGridViewHeaderBorderStyle values changed.");
        RequireThrows<ArgumentException>(() => grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Custom);
        RequireThrows<InvalidEnumArgumentException>(() => grid.RowHeadersBorderStyle = (DataGridViewHeaderBorderStyle)5);
        Require(grid.RowHeadersBorderStyle == DataGridViewHeaderBorderStyle.Raised, "An invalid border assignment changed state.");
    }

    internal static void ApplicationProductName()
    {
        Require(Application.ProductName == ProductName, "Application.ProductName must read the consumer's product metadata.");
        Require(Application.ProductName == ProductName, "Cached Application.ProductName changed.");
    }

    internal static void ApplicationContextLifetime()
    {
        using Form original = new();
        using Form replacement = new();
        using ApplicationContext context = new(original);
        Require(ReferenceEquals(context.MainForm, original) && context.Tag is null, "ApplicationContext constructor state changed.");
        object tag = new();
        context.Tag = tag;
        context.MainForm = replacement;
        int exits = 0;
        context.ThreadExit += (_, _) => exits++;
        context.ExitThread();
        Require(exits == 1 && ReferenceEquals(context.Tag, tag), "ApplicationContext must raise its explicit exit event.");
        context.Dispose();
        Require(replacement.IsDisposed && !original.IsDisposed && context.MainForm is null,
            "ApplicationContext must dispose only its current main form.");
    }

    internal static void BindableComponentAndUpdateModes()
    {
        using DataTable data = new();
        data.Columns.Add("Name", typeof(string));
        data.Rows.Add("source value");
        using Control control = new();
#pragma warning disable CA1859 // The public IBindableComponent contract itself is under test.
        IBindableComponent component = control;
#pragma warning restore CA1859
        BindingContext context = new();
        component.BindingContext = context;
        // A standalone Control has no parent lifecycle to refresh inherited
        // bindings. Attach the binding after creating the real component.
        control.CreateControl();
        Binding binding = new(nameof(Control.Text), data, "Name", formattingEnabled: true,
            DataSourceUpdateMode.OnPropertyChanged);
        component.DataBindings.Add(binding);
        binding.ReadValue();
        Require(ReferenceEquals(component.BindingContext, context) && ReferenceEquals(component.DataBindings[0], binding),
            "IBindableComponent must expose the actual control binding objects.");
        Require(binding.IsBinding && control.Text == "source value", "BindingContext must read the current source value.");
        binding.ControlUpdateMode = ControlUpdateMode.Never;
        binding.DataSourceUpdateMode = DataSourceUpdateMode.Never;
        Require(binding.ControlUpdateMode == ControlUpdateMode.Never && binding.DataSourceUpdateMode == DataSourceUpdateMode.Never,
            "Binding update-mode setters did not retain their values.");
        binding.ControlUpdateMode = ControlUpdateMode.OnPropertyChanged;
        binding.DataSourceUpdateMode = DataSourceUpdateMode.OnPropertyChanged;
        control.Text = "edited source";
        binding.WriteValue();
        Require(Equals(data.Rows[0]["Name"], "edited source"), "The actual binding must write the control value to its source.");
        Require((int)ControlUpdateMode.OnPropertyChanged == 0 && (int)ControlUpdateMode.Never == 1
            && (int)DataSourceUpdateMode.OnValidation == 0 && (int)DataSourceUpdateMode.OnPropertyChanged == 1
            && (int)DataSourceUpdateMode.Never == 2, "Binding update-mode enum values changed.");
    }

    internal static void BindingAndConversionEventArgs()
    {
        using Control source = new();
        Binding binding = new(nameof(Control.Text), source, nameof(Control.Text));
        BindingCompleteEventArgs success = new(binding, BindingCompleteState.Success, BindingCompleteContext.ControlUpdate);
        Require(ReferenceEquals(success.Binding, binding) && success.BindingCompleteState == BindingCompleteState.Success
            && success.BindingCompleteContext == BindingCompleteContext.ControlUpdate && success.ErrorText == string.Empty
            && success.Exception is null && !success.Cancel, "Successful binding event arguments lost constructor state.");
        Exception failure = new InvalidOperationException("conversion failed");
        BindingCompleteEventArgs error = new(binding, BindingCompleteState.Exception, BindingCompleteContext.DataSourceUpdate,
            "conversion failed", failure);
        Require(error.Cancel && ReferenceEquals(error.Exception, failure) && error.ErrorText == "conversion failed",
            "Failed binding event arguments must retain the exception and default to cancellation.");
        error.Cancel = false;
        Require(!error.Cancel, "BindingCompleteEventArgs.Cancel must remain writable.");
        ConvertEventArgs conversion = new("42", typeof(int));
        Require(conversion.DesiredType == typeof(int) && Equals(conversion.Value, "42"), "ConvertEventArgs constructor state changed.");
        conversion.Value = 42;
        Require(conversion.DesiredType == typeof(int) && Equals(conversion.Value, 42), "ConvertEventArgs must retain desired type and mutable value.");
    }

    internal static void NativeAndControlHandleLifetime()
    {
        using Control control = new();
        Require(Control.FromHandle(IntPtr.Zero) is null, "A null handle must not resolve a control.");
        control.CreateControl();
        IntPtr controlHandle = control.Handle;
        Require(controlHandle != IntPtr.Zero && ReferenceEquals(Control.FromHandle(controlHandle), control),
            "Control.FromHandle must resolve the real registered control.");
        control.Dispose();
        Require(Control.FromHandle(controlHandle) is null, "A disposed control must leave the handle registry.");

        HandleChangeWindow window = new();
        try
        {
            CreateParams parameters = new() { Caption = "Canonical handle contract", X = 1, Y = 2, Width = 30, Height = 40 };
            window.CreateHandle(parameters);
            IntPtr handle = window.Handle;
            Require(handle != IntPtr.Zero && ReferenceEquals(NativeWindow.FromHandle(handle), window)
                && Control.FromHandle(handle) is null, "NativeWindow must register its own handle without inventing a Control.");
            Require(window.CreateHandleCalls == 1 && window.Changes.Count == 1 && window.Changes[0] == handle,
                "CreateHandle or OnHandleChange override missed creation.");
            RequireThrows<InvalidOperationException>(() => window.CreateHandle(parameters));
            Require(window.CreateHandleCalls == 2 && window.Changes.Count == 1, "Rejected creation must dispatch without changing the handle.");
            window.DestroyHandle();
            Require(window.Handle == IntPtr.Zero && NativeWindow.FromHandle(handle) is null
                && window.Changes.Count == 2 && window.Changes[1] == IntPtr.Zero, "DestroyHandle must clear ownership and notify the override.");
            window.CreateHandle(parameters);
            Require(window.Handle != IntPtr.Zero && window.CreateHandleCalls == 3 && window.Changes.Count == 3,
                "A destroyed NativeWindow must support virtual recreation.");
        }
        finally
        {
            window.DestroyHandle();
        }
    }

    internal static void TableLayoutMixedSizing()
    {
        using TableLayoutPanel table = new()
        {
            ClientSize = new Size(320, 120), Padding = new Padding(10, 8, 14, 12),
            Margin = Padding.Empty, AutoSize = false, BorderStyle = BorderStyle.None,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            ColumnCount = 4, RowCount = 2, GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        table.SuspendLayout();
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Panel absolute = CreateTableFill(Padding.Empty);
        Panel auto = new()
        {
            Size = new Size(40, 20), AutoSize = false, Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Margin = new Padding(3, 2, 5, 4)
        };
        Panel quarter = CreateTableFill(new Padding(2, 3, 4, 5));
        Panel remainder = CreateTableFill(Padding.Empty);
        table.Controls.Add(absolute, 0, 0);
        table.Controls.Add(auto, 1, 0);
        table.Controls.Add(quarter, 2, 0);
        table.Controls.Add(remainder, 3, 0);
        table.ResumeLayout(performLayout: true);

        // The 296px content width leaves 188 after 60 absolute + 48 auto.
        // Its 25:75 split is exactly 47:141, with no rounding oracle needed.
        for (int pass = 0; pass < 2; pass++)
        {
            table.PerformLayout();
            Require(table.DisplayRectangle == new Rectangle(10, 8, 296, 100), "Table padding changed the content rectangle.");
            RequireTableTracks(table, [60, 48, 47, 141], [40, 60]);
            RequireBounds(absolute, new Rectangle(10, 8, 60, 40), "absolute cell");
            RequireBounds(auto, new Rectangle(73, 10, 40, 20), "auto cell");
            RequireBounds(quarter, new Rectangle(120, 11, 41, 32), "25 percent cell with margins");
            RequireBounds(remainder, new Rectangle(165, 8, 141, 40), "75 percent cell");
        }
    }

    internal static void TableLayoutSpansAndRtl()
    {
        using TableLayoutPanel table = new()
        {
            ClientSize = new Size(200, 120), Padding = Padding.Empty, Margin = Padding.Empty,
            AutoSize = false, BorderStyle = BorderStyle.None, CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            ColumnCount = 3, RowCount = 3, GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        table.SuspendLayout();
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Panel span = CreateTableFill(new Padding(3, 4, 5, 6));
        Panel top = CreateTableFill(Padding.Empty);
        Panel middle = CreateTableFill(Padding.Empty);
        Panel bottom = CreateTableFill(Padding.Empty);
        table.Controls.Add(span, 0, 0);
        table.SetColumnSpan(span, 2);
        table.SetRowSpan(span, 2);
        table.Controls.Add(top, 2, 0);
        table.Controls.Add(middle, 2, 1);
        table.Controls.Add(bottom, 0, 2);
        table.SetColumnSpan(bottom, 3);
        table.ResumeLayout(performLayout: true);
        table.PerformLayout();
        RequireTableTracks(table, [40, 60, 100], [30, 50, 40]);
        RequireBounds(span, new Rectangle(3, 4, 92, 70), "LTR two-column/two-row span");
        RequireBounds(top, new Rectangle(100, 0, 100, 30), "LTR upper final column");
        RequireBounds(middle, new Rectangle(100, 30, 100, 50), "LTR middle final column");
        RequireBounds(bottom, new Rectangle(0, 80, 200, 40), "LTR full bottom row");
        Require(ReferenceEquals(table.GetControlFromPosition(1, 1), span), "A covered cell must resolve its spanning control.");

        table.RightToLeft = RightToLeft.Yes;
        table.PerformLayout();
        RequireTableTracks(table, [40, 60, 100], [30, 50, 40]);
        // Mirror the cell and its asymmetric margins, not logical cell indices.
        RequireBounds(span, new Rectangle(105, 4, 92, 70), "RTL two-column/two-row span");
        RequireBounds(top, new Rectangle(0, 0, 100, 30), "RTL upper final column");
        RequireBounds(middle, new Rectangle(0, 30, 100, 50), "RTL middle final column");
        RequireBounds(bottom, new Rectangle(0, 80, 200, 40), "RTL full bottom row");
        Require(table.GetPositionFromControl(span) == new TableLayoutPanelCellPosition(0, 0)
            && ReferenceEquals(table.GetControlFromPosition(1, 1), span), "RTL must preserve logical cell ownership.");

        table.SetColumnSpan(span, 1);
        table.PerformLayout();
        RequireBounds(span, new Rectangle(165, 4, 32, 70), "RTL changed column span");
        Require(table.GetColumnSpan(span) == 1 && table.GetRowSpan(span) == 2
            && table.GetControlFromPosition(1, 1) is null, "Changing a span must invalidate covered-cell assignments.");
    }

    internal static void TableLayoutNestedInvalidation()
    {
        using TableLayoutPanel outer = new()
        {
            ClientSize = new Size(240, 100), Padding = Padding.Empty, Margin = Padding.Empty,
            AutoSize = false, ColumnCount = 2, RowCount = 1,
            BorderStyle = BorderStyle.None, CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        outer.SuspendLayout();
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        TableLayoutPanel inner = new()
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Top | AnchorStyles.Left, Padding = new Padding(2, 3, 4, 5), Margin = Padding.Empty,
            ColumnCount = 2, RowCount = 1, BorderStyle = BorderStyle.None,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None, GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        inner.SuspendLayout();
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20));
        inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Panel leaf = new()
        {
            Size = new Size(30, 12), AutoSize = false, Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Margin = new Padding(1, 2, 3, 4)
        };
        inner.Controls.Add(leaf, 0, 0);
        inner.Controls.Add(CreateTableFill(Padding.Empty), 1, 0);
        inner.ResumeLayout(performLayout: false);
        Panel remainder = CreateTableFill(Padding.Empty);
        outer.Controls.Add(inner, 0, 0);
        outer.Controls.Add(remainder, 1, 0);
        outer.ResumeLayout(performLayout: true);
        outer.PerformLayout();

        // Preferred width = leaf 30 + margins 4 + fixed column 20 + padding 6.
        // Preferred height = leaf 12 + margins 6 + padding 8.
        RequireBounds(inner, new Rectangle(0, 0, 60, 26), "initial nested table");
        RequireBounds(leaf, new Rectangle(3, 5, 30, 12), "initial nested leaf");
        RequireBounds(remainder, new Rectangle(60, 0, 180, 100), "initial outer remainder");
        Require(inner.GetPreferredSize(Size.Empty) == new Size(60, 26), "Nested preferred size must include margins and padding.");
        Require(inner.GetPreferredSize(Size.Empty) == new Size(60, 26), "Warmed nested preferred-size cache changed.");

        leaf.Size = new Size(50, 22);
        // Request only the outer layout: do not repair the child cache by
        // explicitly laying out or measuring the inner table before asserting.
        outer.PerformLayout();
        RequireBounds(inner, new Rectangle(0, 0, 80, 36), "grown nested table");
        RequireBounds(leaf, new Rectangle(3, 5, 50, 22), "grown nested leaf");
        RequireBounds(remainder, new Rectangle(80, 0, 160, 100), "grown outer remainder");
        RequireTableTracks(outer, [80, 160], [100]);

        leaf.Size = new Size(30, 12);
        outer.PerformLayout();
        RequireBounds(inner, new Rectangle(0, 0, 60, 26), "restored nested table");
        RequireBounds(leaf, new Rectangle(3, 5, 30, 12), "restored nested leaf");
        RequireBounds(remainder, new Rectangle(60, 0, 180, 100), "restored outer remainder");
        RequireTableTracks(outer, [60, 180], [100]);
    }

    private static Panel CreateTableFill(Padding margin) => new()
    {
        Size = new Size(1, 1), AutoSize = false, Margin = margin, Dock = DockStyle.Fill
    };

    private static void RequireBounds(Control control, Rectangle expected, string name) =>
        Require(control.Bounds == expected, $"{name}: expected {expected}, actual {control.Bounds}.");

    private static void RequireTableTracks(TableLayoutPanel table, int[] columns, int[] rows)
    {
        int[] actualColumns = table.GetColumnWidths();
        int[] actualRows = table.GetRowHeights();
        Require(actualColumns.SequenceEqual(columns),
            $"Column widths: expected [{string.Join(", ", columns)}], actual [{string.Join(", ", actualColumns)}].");
        Require(actualRows.SequenceEqual(rows),
            $"Row heights: expected [{string.Join(", ", rows)}], actual [{string.Join(", ", actualRows)}].");
    }

    private static DataGridView CreateGrid()
    {
        DataGridView grid = new() { AllowUserToAddRows = false, Size = new Size(240, 120) };
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "first", Width = 80 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "second", Width = 90 });
        grid.Rows.Add("initial", "one");
        grid.Rows.Add("second", "two");
        return grid;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private sealed class HandleChangeWindow : NativeWindow
    {
        internal int CreateHandleCalls { get; private set; }

        internal List<IntPtr> Changes { get; } = [];

        public override void CreateHandle(CreateParams cp)
        {
            CreateHandleCalls++;
            base.CreateHandle(cp);
        }

        protected override void OnHandleChange()
        {
            base.OnHandleChange();
            Changes.Add(Handle);
        }
    }
}
