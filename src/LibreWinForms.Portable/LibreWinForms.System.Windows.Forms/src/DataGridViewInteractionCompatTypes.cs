using System.ComponentModel;
using System.Drawing;
using System.Globalization;

namespace System.Windows.Forms;

public partial class DataGridView
{
    private const int PortableBorderThickness = 1;
    private const int PortableColumnHeaderHeight = 22;
    private const int PortableRowHeight = 20;

    private DataGridViewCell? _currentCell;
    private Control? _editingControl;
    private DataGridViewCell? _editingCell;
    private object? _editingOriginalValue;
    private bool _readOnly;

    public event EventHandler? CurrentCellChanged;

    [Browsable(false)]
    public DataGridViewCell? CurrentCell
    {
        get => _currentCell;
        set
        {
            ValidateCurrentCell(value);
            if (ReferenceEquals(_currentCell, value))
            {
                return;
            }

            if (IsCurrentCellInEditMode && !EndEdit())
            {
                throw new InvalidOperationException("The current cell edit could not be committed.");
            }

            _currentCell = value;
            OnCurrentCellChanged(EventArgs.Empty);
            Invalidate();
        }
    }

    [Browsable(false)]
    public DataGridViewRow? CurrentRow => _currentCell?.OwningRow;

    [Browsable(false)]
    public Control? EditingControl => _editingControl;

    [Browsable(false)]
    public bool IsCurrentCellInEditMode => _editingControl is not null;

    public bool ReadOnly
    {
        get => _readOnly;
        set
        {
            if (_readOnly == value)
            {
                return;
            }

            if (value)
            {
                CancelEdit();
            }

            _readOnly = value;
            Invalidate();
        }
    }

    public Rectangle GetCellDisplayRectangle(int columnIndex, int rowIndex, bool cutOverflow)
    {
        if (columnIndex < -1 || columnIndex >= Columns.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(columnIndex));
        }

        if (rowIndex < -1 || rowIndex >= Rows.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(rowIndex));
        }

        int rowHeaderWidth = GetPortableRowHeaderWidth();
        Rectangle bounds;
        if (columnIndex == -1 && rowIndex == -1)
        {
            bounds = new Rectangle(
                PortableBorderThickness,
                PortableBorderThickness,
                rowHeaderWidth,
                PortableColumnHeaderHeight);
        }
        else if (rowIndex == -1)
        {
            bounds = new Rectangle(
                GetPortableColumnX(columnIndex, rowHeaderWidth),
                PortableBorderThickness,
                GetPortableColumnWidth(Columns[columnIndex], rowHeaderWidth),
                PortableColumnHeaderHeight);
        }
        else if (columnIndex == -1)
        {
            bounds = new Rectangle(
                PortableBorderThickness,
                GetPortableRowY(rowIndex),
                rowHeaderWidth,
                PortableRowHeight);
        }
        else
        {
            bounds = new Rectangle(
                GetPortableColumnX(columnIndex, rowHeaderWidth),
                GetPortableRowY(rowIndex),
                GetPortableColumnWidth(Columns[columnIndex], rowHeaderWidth),
                PortableRowHeight);
        }

        return cutOverflow ? Rectangle.Intersect(bounds, ClientRectangle) : bounds;
    }

    public HitTestInfo HitTest(int x, int y)
    {
        if (x < PortableBorderThickness
            || y < PortableBorderThickness
            || x >= ClientSize.Width - PortableBorderThickness
            || y >= ClientSize.Height - PortableBorderThickness)
        {
            return HitTestInfo.Nowhere;
        }

        Rectangle topLeft = GetCellDisplayRectangle(-1, -1, cutOverflow: true);
        if (topLeft.Contains(x, y))
        {
            return new HitTestInfo(
                DataGridViewHitTestType.TopLeftHeader,
                columnIndex: -1,
                rowIndex: -1,
                topLeft.X,
                topLeft.Y);
        }

        for (int columnIndex = 0; columnIndex < Columns.Count; columnIndex++)
        {
            Rectangle columnHeader = GetCellDisplayRectangle(columnIndex, -1, cutOverflow: true);
            if (columnHeader.Contains(x, y))
            {
                return new HitTestInfo(
                    DataGridViewHitTestType.ColumnHeader,
                    columnIndex,
                    rowIndex: -1,
                    columnHeader.X,
                    columnHeader.Y);
            }
        }

        int rowIndex = (y - PortableBorderThickness - PortableColumnHeaderHeight) / PortableRowHeight;
        if (rowIndex < 0 || rowIndex >= Rows.Count)
        {
            return HitTestInfo.Nowhere;
        }

        Rectangle rowHeader = GetCellDisplayRectangle(-1, rowIndex, cutOverflow: true);
        if (rowHeader.Contains(x, y))
        {
            return new HitTestInfo(
                DataGridViewHitTestType.RowHeader,
                columnIndex: -1,
                rowIndex,
                rowHeader.X,
                rowHeader.Y);
        }

        for (int columnIndex = 0; columnIndex < Columns.Count; columnIndex++)
        {
            Rectangle cell = GetCellDisplayRectangle(columnIndex, rowIndex, cutOverflow: true);
            if (cell.Contains(x, y))
            {
                return new HitTestInfo(
                    DataGridViewHitTestType.Cell,
                    columnIndex,
                    rowIndex,
                    cell.X,
                    cell.Y);
            }
        }

        return HitTestInfo.Nowhere;
    }

    public virtual bool BeginEdit(bool selectAll)
    {
        if (_editingControl is not null)
        {
            return ReferenceEquals(_editingCell, _currentCell);
        }

        DataGridViewCell cell = _currentCell
            ?? throw new InvalidOperationException("CurrentCell is not set to a valid cell.");
        ValidateCurrentCell(cell);
        if (cell.ReadOnly)
        {
            return false;
        }

        Control? editor = CreatePortableEditingControl(cell);
        if (editor is null)
        {
            return false;
        }

        _editingCell = cell;
        _editingOriginalValue = cell.Value;
        _editingControl = editor;
        editor.Bounds = GetCellDisplayRectangle(cell.ColumnIndex, cell.RowIndex, cutOverflow: false);
        editor.KeyDown += OnPortableEditingControlKeyDown;
        Controls.Add(editor);

        if (selectAll)
        {
            if (editor is TextBoxBase textBox)
            {
                textBox.SelectAll();
            }
            else if (editor is ComboBox comboBox)
            {
                comboBox.SelectAll();
            }
        }

        OnEditingControlShowing(new DataGridViewEditingControlShowingEventArgs(editor));
        editor.Focus();
        Invalidate();
        return true;
    }

    public bool EndEdit()
    {
        if (_editingControl is null || _editingCell is null)
        {
            return true;
        }

        DataGridViewCell cell = _editingCell;
        object? value = GetPortableEditingValue(_editingControl);
        bool changed = !Equals(cell.Value, value);
        if (changed)
        {
            cell.Value = value;
        }

        DetachPortableEditingControl(restoreFocus: true);
        if (changed)
        {
            OnCellValueChanged(new DataGridViewCellEventArgs(cell.ColumnIndex, cell.RowIndex));
        }

        return true;
    }

    public bool CancelEdit()
    {
        if (_editingControl is null)
        {
            return true;
        }

        if (_editingCell is not null)
        {
            _editingCell.Value = _editingOriginalValue;
        }

        DetachPortableEditingControl(restoreFocus: true);
        return true;
    }

    internal void OnCellDetached(DataGridViewCell cell)
    {
        if (ReferenceEquals(_editingCell, cell))
        {
            CancelEdit();
        }

        if (!ReferenceEquals(_currentCell, cell))
        {
            return;
        }

        _currentCell = null;
        OnCurrentCellChanged(EventArgs.Empty);
        Invalidate();
    }

    internal void OnCellReadOnlyChanged(DataGridViewCell cell)
    {
        CancelReadOnlyCurrentCellEdit(cell);
        Invalidate();
    }

    internal void OnColumnReadOnlyChanged(DataGridViewColumn column)
    {
        if (_currentCell?.ColumnIndex == column.Index)
        {
            CancelReadOnlyCurrentCellEdit(_currentCell);
        }

        Invalidate();
    }

    internal void OnRowReadOnlyChanged(DataGridViewRow row)
    {
        if (ReferenceEquals(_currentCell?.OwningRow, row))
        {
            CancelReadOnlyCurrentCellEdit(_currentCell);
        }

        Invalidate();
    }

    protected virtual void OnCurrentCellChanged(EventArgs e)
    {
        CurrentCellChanged?.Invoke(this, e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DetachPortableEditingControl(restoreFocus: false);
            _currentCell = null;
        }

        base.Dispose(disposing);
    }

    private void CancelReadOnlyCurrentCellEdit(DataGridViewCell cell)
    {
        if (ReferenceEquals(_editingCell, cell) && cell.ReadOnly)
        {
            CancelEdit();
        }
    }

    private void ValidateCurrentCell(DataGridViewCell? cell)
    {
        if (cell is null)
        {
            return;
        }

        int rowIndex = cell.RowIndex;
        int columnIndex = cell.ColumnIndex;
        if (!ReferenceEquals(cell.DataGridView, this)
            || rowIndex < 0
            || rowIndex >= Rows.Count
            || columnIndex < 0
            || columnIndex >= Columns.Count
            || columnIndex >= Rows[rowIndex].Cells.Count
            || !ReferenceEquals(Rows[rowIndex].Cells[columnIndex], cell))
        {
            throw new ArgumentException("The specified cell does not belong to this DataGridView.", nameof(cell));
        }
    }

    private static Control? CreatePortableEditingControl(DataGridViewCell cell)
    {
        if (cell is DataGridViewComboBoxCell comboBoxCell)
        {
            var comboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Text = Convert.ToString(cell.Value, CultureInfo.CurrentCulture) ?? string.Empty
            };
            int selectedIndex = -1;
            for (int index = 0; index < comboBoxCell.Items.Count; index++)
            {
                object? item = comboBoxCell.Items[index];
                comboBox.Items.Add(item!);
                if (selectedIndex < 0 && Equals(item, cell.Value))
                {
                    selectedIndex = index;
                }
            }

            comboBox.SelectedIndex = selectedIndex;
            return comboBox;
        }

        if (cell is DataGridViewTextBoxCell)
        {
            return new TextBox
            {
                Text = Convert.ToString(cell.Value, CultureInfo.CurrentCulture) ?? string.Empty
            };
        }

        return null;
    }

    private static object? GetPortableEditingValue(Control editor)
    {
        if (editor is ComboBox comboBox)
        {
            return comboBox.SelectedIndex >= 0 ? comboBox.SelectedItem : comboBox.Text;
        }

        return editor.Text;
    }

    private void DetachPortableEditingControl(bool restoreFocus)
    {
        Control? editor = _editingControl;
        _editingControl = null;
        _editingCell = null;
        _editingOriginalValue = null;
        if (editor is null)
        {
            return;
        }

        editor.KeyDown -= OnPortableEditingControlKeyDown;
        if (ReferenceEquals(editor.Parent, this))
        {
            Controls.Remove(editor);
        }

        editor.Dispose();
        if (restoreFocus)
        {
            Focus();
        }

        Invalidate();
    }

    private void OnPortableEditingControlKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = CancelEdit();
            e.SuppressKeyPress = e.Handled;
        }
        else if (e.KeyCode is Keys.Enter or Keys.Return)
        {
            e.Handled = EndEdit();
            e.SuppressKeyPress = e.Handled;
        }
    }

    private int GetPortableRowHeaderWidth()
    {
        int maximum = Math.Max(0, (int)Math.Floor(ClientSize.Width * 0.35));
        return Math.Max(0, Math.Min(maximum, RowHeadersWidth));
    }

    private int GetPortableColumnX(int columnIndex, int rowHeaderWidth)
    {
        long x = PortableBorderThickness + rowHeaderWidth;
        for (int index = 0; index < columnIndex; index++)
        {
            x += GetPortableColumnWidth(Columns[index], rowHeaderWidth);
        }

        return (int)Math.Clamp(x, int.MinValue, int.MaxValue);
    }

    private int GetPortableColumnWidth(DataGridViewColumn column, int rowHeaderWidth)
    {
        if (column.AutoSizeMode == DataGridViewAutoSizeColumnMode.Fill && Columns.Count > 0)
        {
            int availableWidth = Math.Max(0, ClientSize.Width - rowHeaderWidth - PortableBorderThickness * 2);
            return Math.Max(40, availableWidth / Columns.Count);
        }

        return Math.Max(40, column.Width > 0 ? column.Width : 100);
    }

    private static int GetPortableRowY(int rowIndex)
    {
        long y = PortableBorderThickness + PortableColumnHeaderHeight + (long)rowIndex * PortableRowHeight;
        return (int)Math.Clamp(y, int.MinValue, int.MaxValue);
    }

    public sealed class HitTestInfo
    {
        public static readonly HitTestInfo Nowhere = new(
            DataGridViewHitTestType.None,
            columnIndex: -1,
            rowIndex: -1,
            columnX: -1,
            rowY: -1);

        internal HitTestInfo(
            DataGridViewHitTestType type,
            int columnIndex,
            int rowIndex,
            int columnX,
            int rowY)
        {
            Type = type;
            ColumnIndex = columnIndex;
            RowIndex = rowIndex;
            ColumnX = columnX;
            RowY = rowY;
        }

        public int ColumnIndex { get; }

        public int ColumnX { get; }

        public int RowIndex { get; }

        public int RowY { get; }

        public DataGridViewHitTestType Type { get; }

        public override bool Equals(object? obj)
        {
            return obj is HitTestInfo hitTestInfo
                && Type == hitTestInfo.Type
                && RowIndex == hitTestInfo.RowIndex
                && ColumnIndex == hitTestInfo.ColumnIndex;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Type, RowIndex, ColumnIndex);
        }

        public override string ToString()
        {
            return $"{{ Type:{Type}, Column:{ColumnIndex}, Row:{RowIndex} }}";
        }
    }
}
