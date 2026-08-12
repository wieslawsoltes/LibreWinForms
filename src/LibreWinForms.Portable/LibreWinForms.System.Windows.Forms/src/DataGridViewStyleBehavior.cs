using System.Data;
using System.Drawing;
using System.Globalization;

namespace System.Windows.Forms;

public partial class DataGridView
{
    private readonly DataGridViewCellStyle _defaultCellStyle = new()
    {
        Alignment = DataGridViewContentAlignment.MiddleLeft,
        BackColor = SystemColors.Window,
        ForeColor = SystemColors.ControlText,
        SelectionBackColor = SystemColors.Highlight,
        SelectionForeColor = SystemColors.HighlightText
    };
    private readonly DataGridViewCellStyle _columnHeadersDefaultCellStyle = new()
    {
        Alignment = DataGridViewContentAlignment.MiddleLeft,
        BackColor = SystemColors.Control,
        ForeColor = SystemColors.ControlText
    };
    private readonly DataGridViewCellStyle _rowHeadersDefaultCellStyle = new()
    {
        Alignment = DataGridViewContentAlignment.MiddleLeft,
        BackColor = SystemColors.Control,
        ForeColor = SystemColors.ControlText
    };
    private int _rowCount;
    private object? _dataSource;

    public event DataGridViewCellPaintingEventHandler? CellPainting;

    public event DataGridViewCellValueEventHandler? CellValueNeeded;

    public DataGridViewCellStyle AlternatingRowsDefaultCellStyle { get; set; } = new();

    public int ColumnCount => Columns.Count;

    public int ColumnHeadersHeight { get; set; } = PortableColumnHeaderHeight;

    public DataGridViewCellStyle ColumnHeadersDefaultCellStyle => _columnHeadersDefaultCellStyle;

    public DataGridViewCellStyle DefaultCellStyle => _defaultCellStyle;

    public object? DataSource
    {
        get => _dataSource;
        set
        {
            if (ReferenceEquals(_dataSource, value))
            {
                return;
            }

            _dataSource = value;
            BindPortableDataSource(value);
        }
    }

    public DataGridViewCellStyle RowHeadersDefaultCellStyle => _rowHeadersDefaultCellStyle;

    public DataGridViewCellStyle RowsDefaultCellStyle { get; set; } = new();

    public int RowCount
    {
        get => VirtualMode ? _rowCount : Math.Max(0, Rows.Count - (NewRowIndex >= 0 ? 1 : 0));
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _rowCount = value;
            if (!VirtualMode)
            {
                while (RowCount < value)
                {
                    Rows.Add();
                }

                while (RowCount > value)
                {
                    Rows.RemoveAt(RowCount - 1);
                }
            }

            Invalidate();
        }
    }

    public bool VirtualMode { get; set; }

    public void NotifyCurrentCellDirty(bool dirty)
    {
        if (dirty)
        {
            Invalidate();
        }
    }

    protected virtual void OnCellPainting(DataGridViewCellPaintingEventArgs e) =>
        CellPainting?.Invoke(this, e);

    protected virtual void OnCellValueNeeded(DataGridViewCellValueEventArgs e) =>
        CellValueNeeded?.Invoke(this, e);

    protected override void OnPaint(PaintEventArgs e)
    {
        PaintPortableGrid(e.Graphics, e.ClipRectangle);
        base.OnPaint(e);
    }

    private void BindPortableDataSource(object? dataSource)
    {
        Rows.Clear();
        Columns.Clear();
        if (dataSource is not DataTable table)
        {
            Invalidate();
            return;
        }

        foreach (DataColumn dataColumn in table.Columns)
        {
            var column = new DataGridViewTextBoxColumn
            {
                Name = dataColumn.ColumnName,
                HeaderText = dataColumn.Caption,
                ValueType = dataColumn.DataType
            };
            Columns.Add(column);
        }

        foreach (DataRow dataRow in table.Rows)
        {
            Rows.Add(dataRow.ItemArray);
        }

        Invalidate();
    }

    internal void PaintPortableCell(
        DataGridViewCellPaintingEventArgs e,
        Rectangle clipBounds,
        DataGridViewPaintParts paintParts)
    {
        Rectangle bounds = Rectangle.Intersect(e.CellBounds, clipBounds);
        if (bounds.IsEmpty)
        {
            return;
        }

        DataGridViewCellStyle style = e.CellStyle;
        bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
        if ((paintParts & (DataGridViewPaintParts.Background | DataGridViewPaintParts.SelectionBackground)) != 0)
        {
            Color color = selected && !style.SelectionBackColor.IsEmpty
                ? style.SelectionBackColor
                : !style.BackColor.IsEmpty ? style.BackColor : BackColor;
            using var brush = new SolidBrush(color);
            e.Graphics.FillRectangle(brush, bounds);
        }

        if ((paintParts & DataGridViewPaintParts.Border) != 0)
        {
            using var pen = new Pen(SystemColors.ControlDark);
            e.Graphics.DrawRectangle(pen, e.CellBounds.X, e.CellBounds.Y, Math.Max(0, e.CellBounds.Width - 1), Math.Max(0, e.CellBounds.Height - 1));
        }

        if ((paintParts & DataGridViewPaintParts.ContentForeground) != 0 && e.FormattedValue is not null)
        {
            Color color = selected && !style.SelectionForeColor.IsEmpty
                ? style.SelectionForeColor
                : !style.ForeColor.IsEmpty ? style.ForeColor : ForeColor;
            using var brush = new SolidBrush(color);
            Font font = style.Font ?? Font;
            e.Graphics.DrawString(
                Convert.ToString(e.FormattedValue, style.FormatProvider) ?? string.Empty,
                font,
                brush,
                Rectangle.Inflate(e.CellBounds, -2, -1));
        }
    }

    private void PaintPortableGrid(Graphics graphics, Rectangle clipBounds)
    {
        for (int columnIndex = 0; columnIndex < Columns.Count; columnIndex++)
        {
            Rectangle bounds = GetCellDisplayRectangle(columnIndex, -1, cutOverflow: true);
            if (!bounds.IntersectsWith(clipBounds))
            {
                continue;
            }

            PaintPortableGridCell(
                graphics,
                clipBounds,
                bounds,
                rowIndex: -1,
                columnIndex,
                Columns[columnIndex].HeaderText,
                ColumnHeadersDefaultCellStyle,
                DataGridViewElementStates.Displayed | DataGridViewElementStates.Visible);
        }

        int visibleRowCount = VirtualMode ? RowCount : Rows.Count;
        for (int rowIndex = 0; rowIndex < visibleRowCount; rowIndex++)
        {
            for (int columnIndex = 0; columnIndex < Columns.Count; columnIndex++)
            {
                Rectangle bounds = GetCellDisplayRectangle(columnIndex, rowIndex, cutOverflow: true);
                if (!bounds.IntersectsWith(clipBounds))
                {
                    continue;
                }

                object? value;
                if (VirtualMode)
                {
                    var valueArgs = new DataGridViewCellValueEventArgs(columnIndex, rowIndex);
                    OnCellValueNeeded(valueArgs);
                    value = valueArgs.Value;
                }
                else
                {
                    value = columnIndex < Rows[rowIndex].Cells.Count
                        ? Rows[rowIndex].Cells[columnIndex].Value
                        : null;
                }

                DataGridViewCellStyle style = rowIndex % 2 == 1 && !IsStyleEmpty(AlternatingRowsDefaultCellStyle)
                    ? MergeStyle(DefaultCellStyle, AlternatingRowsDefaultCellStyle)
                    : !IsStyleEmpty(RowsDefaultCellStyle)
                        ? MergeStyle(DefaultCellStyle, RowsDefaultCellStyle)
                        : DefaultCellStyle;
                DataGridViewElementStates state = DataGridViewElementStates.Displayed | DataGridViewElementStates.Visible;
                if (CurrentCell?.RowIndex == rowIndex && CurrentCell.ColumnIndex == columnIndex)
                {
                    state |= DataGridViewElementStates.Selected;
                }

                PaintPortableGridCell(graphics, clipBounds, bounds, rowIndex, columnIndex, value, style, state);
            }
        }
    }

    private void PaintPortableGridCell(
        Graphics graphics,
        Rectangle clipBounds,
        Rectangle cellBounds,
        int rowIndex,
        int columnIndex,
        object? value,
        DataGridViewCellStyle style,
        DataGridViewElementStates state)
    {
        object? formatted = FormatPortableValue(value, style);
        var args = new DataGridViewCellPaintingEventArgs(
            this,
            graphics,
            clipBounds,
            cellBounds,
            rowIndex,
            columnIndex,
            state,
            value,
            formatted,
            errorText: null,
            style,
            advancedBorderStyle: null,
            DataGridViewPaintParts.All);
        OnCellPainting(args);
        if (!args.Handled)
        {
            args.Paint(cellBounds, DataGridViewPaintParts.All);
        }
    }

    private static object? FormatPortableValue(object? value, DataGridViewCellStyle style)
    {
        if (value is null || value == DBNull.Value)
        {
            return style.NullValue;
        }

        if (!string.IsNullOrEmpty(style.Format) && value is IFormattable formattable)
        {
            return formattable.ToString(style.Format, style.FormatProvider);
        }

        return value;
    }

    private static bool IsStyleEmpty(DataGridViewCellStyle style) =>
        style.Alignment == DataGridViewContentAlignment.NotSet &&
        style.BackColor.IsEmpty &&
        style.ForeColor.IsEmpty &&
        style.Font is null;

    private static DataGridViewCellStyle MergeStyle(DataGridViewCellStyle basis, DataGridViewCellStyle overlay)
    {
        var style = basis.Clone();
        if (overlay.Alignment != DataGridViewContentAlignment.NotSet)
        {
            style.Alignment = overlay.Alignment;
        }

        if (!overlay.BackColor.IsEmpty)
        {
            style.BackColor = overlay.BackColor;
        }

        if (!overlay.ForeColor.IsEmpty)
        {
            style.ForeColor = overlay.ForeColor;
        }

        style.Font = overlay.Font ?? style.Font;
        return style;
    }
}
