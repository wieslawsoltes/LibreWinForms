using System.ComponentModel;
using System.Drawing;
using System.Globalization;

namespace System.Windows.Forms;

public enum DataGridViewContentAlignment
{
    NotSet = 0x000,
    TopLeft = 0x001,
    TopCenter = 0x002,
    TopRight = 0x004,
    MiddleLeft = 0x010,
    MiddleCenter = 0x020,
    MiddleRight = 0x040,
    BottomLeft = 0x100,
    BottomCenter = 0x200,
    BottomRight = 0x400
}

public enum DataGridViewTriState
{
    NotSet = 0,
    True = 1,
    False = 2
}

[Flags]
public enum DataGridViewElementStates
{
    None = 0,
    Displayed = 1,
    Frozen = 2,
    ReadOnly = 4,
    Resizable = 8,
    ResizableSet = 16,
    Selected = 32,
    Visible = 64
}

[Flags]
public enum DataGridViewPaintParts
{
    None = 0,
    Background = 1,
    Border = 2,
    ContentBackground = 4,
    ContentForeground = 8,
    ErrorIcon = 16,
    Focus = 32,
    SelectionBackground = 64,
    All = Background | Border | ContentBackground | ContentForeground | ErrorIcon | Focus | SelectionBackground
}

[Flags]
public enum DataGridViewDataErrorContexts
{
    Formatting = 1,
    Display = 2,
    PreferredSize = 4,
    RowDeletion = 8,
    Parsing = 0x100,
    Commit = 0x200,
    InitialValueRestoration = 0x400,
    LeaveControl = 0x800,
    CurrentCellChange = 0x1000,
    Scroll = 0x2000,
    ClipboardContent = 0x4000
}

public sealed class DataGridViewAdvancedBorderStyle
{
}

public class DataGridViewCellStyle : ICloneable
{
    public DataGridViewCellStyle()
    {
    }

    public DataGridViewCellStyle(DataGridViewCellStyle dataGridViewCellStyle)
    {
        ArgumentNullException.ThrowIfNull(dataGridViewCellStyle);
        Alignment = dataGridViewCellStyle.Alignment;
        BackColor = dataGridViewCellStyle.BackColor;
        DataSourceNullValue = dataGridViewCellStyle.DataSourceNullValue;
        Font = dataGridViewCellStyle.Font;
        ForeColor = dataGridViewCellStyle.ForeColor;
        Format = dataGridViewCellStyle.Format;
        FormatProvider = dataGridViewCellStyle.FormatProvider;
        NullValue = dataGridViewCellStyle.NullValue;
        Padding = dataGridViewCellStyle.Padding;
        SelectionBackColor = dataGridViewCellStyle.SelectionBackColor;
        SelectionForeColor = dataGridViewCellStyle.SelectionForeColor;
        Tag = dataGridViewCellStyle.Tag;
        WrapMode = dataGridViewCellStyle.WrapMode;
    }

    [DefaultValue(DataGridViewContentAlignment.NotSet)]
    public DataGridViewContentAlignment Alignment { get; set; }

    public Color BackColor { get; set; } = Color.Empty;

    [Browsable(false)]
    public object? DataSourceNullValue { get; set; } = DBNull.Value;

    public Font? Font { get; set; }

    public Color ForeColor { get; set; } = Color.Empty;

    [DefaultValue("")]
    public string Format { get; set; } = string.Empty;

    [Browsable(false)]
    public IFormatProvider FormatProvider { get; set; } = CultureInfo.CurrentCulture;

    [DefaultValue("")]
    public object? NullValue { get; set; } = string.Empty;

    public Padding Padding { get; set; }

    public Color SelectionBackColor { get; set; } = Color.Empty;

    public Color SelectionForeColor { get; set; } = Color.Empty;

    [Browsable(false)]
    public object? Tag { get; set; }

    [DefaultValue(DataGridViewTriState.NotSet)]
    public DataGridViewTriState WrapMode { get; set; }

    public virtual DataGridViewCellStyle Clone() => new(this);

    object ICloneable.Clone() => Clone();
}

public class DataGridViewCellValueEventArgs : EventArgs
{
    public DataGridViewCellValueEventArgs(int columnIndex, int rowIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);
        ColumnIndex = columnIndex;
        RowIndex = rowIndex;
    }

    public int ColumnIndex { get; }

    public int RowIndex { get; }

    public object? Value { get; set; }
}

public class DataGridViewCellCancelEventArgs : CancelEventArgs
{
    public DataGridViewCellCancelEventArgs(int columnIndex, int rowIndex)
    {
        ColumnIndex = columnIndex;
        RowIndex = rowIndex;
    }

    public int ColumnIndex { get; }

    public int RowIndex { get; }
}

public class DataGridViewCellPaintingEventArgs : HandledEventArgs
{
    private readonly DataGridView _dataGridView;

    public DataGridViewCellPaintingEventArgs(
        DataGridView dataGridView,
        Graphics graphics,
        Rectangle clipBounds,
        Rectangle cellBounds,
        int rowIndex,
        int columnIndex,
        DataGridViewElementStates cellState,
        object? value,
        object? formattedValue,
        string? errorText,
        DataGridViewCellStyle cellStyle,
        DataGridViewAdvancedBorderStyle? advancedBorderStyle,
        DataGridViewPaintParts paintParts)
    {
        ArgumentNullException.ThrowIfNull(dataGridView);
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(cellStyle);
        if ((paintParts & ~DataGridViewPaintParts.All) != 0)
        {
            throw new InvalidEnumArgumentException(nameof(paintParts), (int)paintParts, typeof(DataGridViewPaintParts));
        }

        _dataGridView = dataGridView;
        Graphics = graphics;
        ClipBounds = clipBounds;
        CellBounds = cellBounds;
        RowIndex = rowIndex;
        ColumnIndex = columnIndex;
        State = cellState;
        Value = value;
        FormattedValue = formattedValue;
        ErrorText = errorText;
        CellStyle = cellStyle;
        AdvancedBorderStyle = advancedBorderStyle;
        PaintParts = paintParts;
    }

    public Graphics Graphics { get; }

    public DataGridViewAdvancedBorderStyle? AdvancedBorderStyle { get; }

    public Rectangle CellBounds { get; }

    public Rectangle ClipBounds { get; }

    public int RowIndex { get; }

    public int ColumnIndex { get; }

    public DataGridViewElementStates State { get; }

    public object? Value { get; }

    public object? FormattedValue { get; }

    public string? ErrorText { get; }

    public DataGridViewCellStyle CellStyle { get; }

    public DataGridViewPaintParts PaintParts { get; }

    public void Paint(Rectangle clipBounds, DataGridViewPaintParts paintParts) =>
        _dataGridView.PaintPortableCell(this, clipBounds, paintParts);

    public void PaintBackground(Rectangle clipBounds, bool cellsPaintSelectionBackground)
    {
        DataGridViewPaintParts parts = DataGridViewPaintParts.Background | DataGridViewPaintParts.Border;
        if (cellsPaintSelectionBackground)
        {
            parts |= DataGridViewPaintParts.SelectionBackground;
        }

        Paint(clipBounds, parts);
    }

    public void PaintContent(Rectangle clipBounds) =>
        Paint(
            clipBounds,
            DataGridViewPaintParts.ContentBackground |
            DataGridViewPaintParts.ContentForeground |
            DataGridViewPaintParts.ErrorIcon);
}

public delegate void DataGridViewCellPaintingEventHandler(object? sender, DataGridViewCellPaintingEventArgs e);

public delegate void DataGridViewCellValueEventHandler(object? sender, DataGridViewCellValueEventArgs e);

public delegate void DataGridViewCellCancelEventHandler(object? sender, DataGridViewCellCancelEventArgs e);

public interface IDataGridViewEditingControl
{
    DataGridView EditingControlDataGridView { get; set; }

    object EditingControlFormattedValue { get; set; }

    int EditingControlRowIndex { get; set; }

    bool EditingControlValueChanged { get; set; }

    Cursor EditingPanelCursor { get; }

    bool RepositionEditingControlOnValueChange { get; }

    void ApplyCellStyleToEditingControl(DataGridViewCellStyle dataGridViewCellStyle);

    bool EditingControlWantsInputKey(Keys keyData, bool dataGridViewWantsInputKey);

    object GetEditingControlFormattedValue(DataGridViewDataErrorContexts context);

    void PrepareEditingControlForEdit(bool selectAll);
}
