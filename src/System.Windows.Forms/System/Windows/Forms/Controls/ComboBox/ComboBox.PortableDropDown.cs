// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.ExceptionServices;

namespace System.Windows.Forms;

public partial class ComboBox
{
    private PortableComboBoxDropDown? _portableDropDown;
    private bool _portableDropDownRetiring;

    private bool IsPortableDropDownVisible => _portableDropDown is { Visible: true, IsHandleCreated: true, IsDisposed: false };

    private bool HasPortableDropDownFocus => _portableDropDown is { Visible: true, IsHandleCreated: true, IsDisposed: false } popup
        && ReferenceEquals(popup.GetPortableHostedKeyboardTarget(), popup.List) && popup.List.Focused;

    internal bool IsPortableDropDownOwner(ToolStripDropDown popup)
        => ReferenceEquals(_portableDropDown, popup) && IsPortableDropDownVisible;

    private void SetPortableDropDown(bool visible)
    {
        // CB_SHOWDROPDOWN has no effect on CBS_SIMPLE. It does not create a
        // second, floating list for the always-visible Simple style.
        if (DropDownStyle == ComboBoxStyle.Simple)
            return;
        if (!visible)
        {
            _portableDropDown?.RequestClose();
            return;
        }

        if (_portableDropDown is not null)
            return;
        if (_portableDropDownRetiring)
            throw new InvalidOperationException("A ComboBox dropdown cannot be opened while its source handle is being retired.");
        ValidatePortableDropDownStyle();
        if (!Visible || !Enabled || IsDisposed || Disposing || !IsHandleCreated)
            throw new InvalidOperationException("A portable ComboBox dropdown requires a live, visible, enabled source control.");
        if (FindForm() is not { Visible: true, IsHandleCreated: true, IsDisposed: false, Disposing: false })
            throw new InvalidOperationException("A portable ComboBox dropdown requires a live Form owner.");

        PortableComboBoxDropDown popup = new(this);
        _portableDropDown = popup;
        ExceptionDispatchInfo? failure = null;
        try
        {
            popup.Attach();
            if (popup.Prepare())
                popup.Show(this, new Point(RightToLeft == RightToLeft.Yes ? Width : 0, Height),
                    RightToLeft == RightToLeft.Yes ? ToolStripDropDownDirection.BelowLeft : ToolStripDropDownDirection.BelowRight);
        }
        catch (Exception exception)
        {
            failure = ExceptionDispatchInfo.Capture(exception);
        }

        // Exactly one cleanup attempt here. An original admission/callback
        // exception cannot be replaced by a second finally-driven Dispose.
        if ((failure is not null || !popup.Visible) && !popup.IsDisposed)
            CompletePortableDropDownStage(popup.Dispose, ref failure);
        failure?.Throw();
    }

    private static void CompletePortableDropDownStage(Action action, ref ExceptionDispatchInfo? failure)
    {
        try { action(); }
        catch (Exception exception)
        {
            if (failure is null)
                failure = ExceptionDispatchInfo.Capture(exception);
            else
                failure.SourceException.Data["PortableComboBoxCleanupException"] = exception;
        }
    }

    private void ValidatePortableDropDownStyle()
    {
        if (DropDownStyle != ComboBoxStyle.DropDownList || DrawMode != DrawMode.Normal)
            throw new NotSupportedException("Portable ComboBox popups currently require DropDownList and DrawMode.Normal. Editable, Simple-list and owner-drawn surfaces require their own source integration.");
    }

    private void DisposePortableDropDown()
    {
        if (_portableDropDownRetiring)
            return;
        _portableDropDownRetiring = true;
        try { _portableDropDown?.Dispose(); }
        finally { _portableDropDownRetiring = false; }
    }

    private bool IsPortableDropDownInputKey(Keys keyData)
        => (keyData & Keys.KeyCode) == Keys.F4
            || ((keyData & Keys.Alt) != 0 && (keyData & Keys.KeyCode) is Keys.Up or Keys.Down)
            || (IsPortableDropDownVisible && (keyData & Keys.KeyCode) is Keys.Enter or Keys.Escape);

    private void HandlePortableDropDownKey(KeyEventArgs e)
    {
        if (e.Handled || e.SuppressKeyPress)
            return;
        if (e.KeyCode == Keys.F4 || (e.Alt && e.KeyCode is Keys.Up or Keys.Down))
        {
            e.SuppressKeyPress = true;
            SetPortableDropDown(!IsPortableDropDownVisible);
        }
        else if (_portableDropDown is { Visible: true } popup && e.KeyCode is Keys.Enter or Keys.Escape)
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Enter)
                popup.Commit();
            else
                popup.Cancel();
        }
    }

    private void HandlePortableDropDownMouse(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location) && Enabled && Visible)
            SetPortableDropDown(!IsPortableDropDownVisible);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        try { base.OnEnabledChanged(e); }
        finally { Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Rectangle bounds = ClientRectangle;
        if (bounds.Width > 2 && bounds.Height > 2)
        {
            GraphicsState state = e.Graphics.Save();
            try
            {
                e.Graphics.SetClip(bounds, CombineMode.Intersect);
                e.Graphics.SetClip(e.ClipRectangle, CombineMode.Intersect);
                Color background = Enabled ? BackColor : SystemColors.Control;
                using SolidBrush brush = new(background);
                e.Graphics.FillRectangle(brush, bounds);
                ControlPaint.DrawBorder(e.Graphics, bounds, SystemColors.WindowFrame, ButtonBorderStyle.Solid);
                Rectangle content = Rectangle.Inflate(bounds, -1, -1);
                int buttonWidth = Math.Min(content.Width, SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi));
                Rectangle button = new(RightToLeft == RightToLeft.Yes ? content.Left : content.Right - buttonWidth,
                    content.Top, buttonWidth, content.Height);
                if (button.Width > 0)
                    ControlPaint.DrawComboButton(e.Graphics, button,
                        !Enabled ? ButtonState.Inactive : IsPortableDropDownVisible ? ButtonState.Pushed : ButtonState.Normal);
                Rectangle text = new(RightToLeft == RightToLeft.Yes ? button.Right : content.Left,
                    content.Top, content.Width - buttonWidth, content.Height);
                if (text.Width > 2)
                {
                    text.Inflate(-1, 0);
                    TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
                    if (RightToLeft == RightToLeft.Yes)
                        flags |= TextFormatFlags.RightToLeft | TextFormatFlags.Right;
                    TextRenderer.DrawText(e.Graphics, Text, Font, text,
                        Enabled ? ForeColor : SystemColors.GrayText, flags);
                    if (Focused && ShowFocusCues)
                        ControlPaint.DrawFocusRectangle(e.Graphics, text, ForeColor, background);
                }
            }
            finally { e.Graphics.Restore(state); }
        }

        base.OnPaint(e);
    }

    private sealed class PortableComboBoxDropDown : ToolStripDropDown
    {
        private readonly ComboBox _combo;
        private readonly List<Control> _sourcePath = [];
        private ObjectCollection.Entry[] _entries = [];
        private object[] _items = [];
        private readonly ToolStripControlHost _host;
        private nint _sourceHandle;
        private Form? _sourceOwner;
        private bool _canceled;
        private bool _opened;
        private bool _retired;
        private bool _closedNotified;
        private bool _disposingPopup;
        private bool _syncingSelection;
        private bool _listPointerPressed;
        private int _originalIndex;
        internal ListBox List { get; }

        internal PortableComboBoxDropDown(ComboBox combo)
        {
            _combo = combo;
            AutoSize = false;
            Padding = new Padding(1);
            Margin = Padding.Empty;
            List = new ListBox
            {
                BorderStyle = BorderStyle.None, DrawMode = DrawMode.Normal,
                SelectionMode = SelectionMode.One, MultiColumn = false,
                IntegralHeight = false, Margin = Padding.Empty
            };
            _host = new ToolStripControlHost(List) { AutoSize = false, Margin = Padding.Empty, Padding = Padding.Empty };
            Items.Add(_host);
            List.SelectedIndexChanged += ListSelectionChanged;
            List.PreviewKeyDown += (_, e) =>
            {
                if (_combo.IsPortableDropDownInputKey(e.KeyData))
                    e.IsInputKey = true;
            };
            List.KeyDown += (_, e) => _combo.OnKeyDown(e);
            List.MouseDown += (_, e) => _listPointerPressed = e.Button == MouseButtons.Left
                && SourceIsLive && List.IndexFromPoint(e.Location) >= 0;
            List.MouseUp += (_, e) =>
            {
                bool pressed = _listPointerPressed;
                _listPointerPressed = false;
                if (pressed && e.Button == MouseButtons.Left && List.IndexFromPoint(e.Location) == List.SelectedIndex && List.SelectedIndex >= 0)
                    Commit();
            };
        }

        internal void Attach()
        {
            _sourceHandle = _combo.Handle;
            _sourceOwner = _combo.FindForm();
            for (Control? control = _combo; control is not null; control = control.ParentInternal)
            {
                _sourcePath.Add(control);
                control.PortableHostedFocusLifetimeChanged += SourceLifetimeChanged;
            }
            _combo.SelectedIndexChanged += SourceSelectionChanged;
        }

        private bool SourceIsLive => !_canceled && !_retired && ReferenceEquals(_combo._portableDropDown, this)
            && _combo is { IsDisposed: false, Disposing: false, Visible: true, Enabled: true, IsHandleCreated: true }
            && _combo.Handle == _sourceHandle && ReferenceEquals(_combo.FindForm(), _sourceOwner)
            && _sourceOwner is { IsDisposed: false, Disposing: false, Visible: true, IsHandleCreated: true, IsPortableActivationOwner: true };

        private void SourceLifetimeChanged(bool retiring)
        {
            if (retiring || !SourceIsLive)
                RequestClose();
        }

        internal void RequestClose()
        {
            _canceled = true;
            if (Visible)
                Close();
            else
                Retire();
        }

        internal bool Prepare()
        {
            _combo._dropDown = true; // Native notification bookkeeping, never the visibility getter.
            _combo.OnDropDown(EventArgs.Empty);
            if (!SourceIsLive)
            {
                return false;
            }
            _combo.ValidatePortableDropDownStyle();
            _entries = _combo.Items.InnerList.ToArray();
            _items = _entries.Select(entry => entry.Item).ToArray();
            List.Font = _combo.Font;
            List.ForeColor = _combo.ForeColor;
            List.BackColor = _combo.BackColor;
            List.RightToLeft = _combo.RightToLeft;
            foreach (object item in _items)
            {
                string text = _combo.GetItemText(item);
                // Format is a public callback. Never finish an obsolete source
                // generation or admit stale indices after it changes Items.
                if (!SourceIsLive)
                    return false;
                if (!HasCurrentItems())
                    throw new NotSupportedException("Changing ComboBox items while formatting its portable list requires a new dropdown.");
                List.Items.Add(text);
            }
            _originalIndex = _combo.SelectedIndex;
            List.SelectedIndex = _originalIndex;
            int rows = Math.Min(Math.Max(_items.Length, 1), _combo.MaxDropDownItems);
            int height = _combo.DropDownHeight == DefaultDropDownHeight
                ? checked(List.ItemHeight * rows + Padding.Vertical) : _combo.DropDownHeight;
            int width = Math.Max(_combo.Width, _combo.DropDownWidth);
            List.Size = new Size(Math.Max(1, width - Padding.Horizontal), Math.Max(1, height - Padding.Vertical));
            _host.Size = List.Size;
            Size = new Size(width, height);
            if (_originalIndex >= 0)
                List.TopIndex = Math.Max(0, _originalIndex - rows + 1);
            return SourceIsLive;
        }

        protected override void OnOpening(CancelEventArgs e)
        {
            base.OnOpening(e);
            e.Cancel |= !SourceIsLive;
        }

        protected override void OnOpened(EventArgs e)
        {
            _opened = true;
            base.OnOpened(e);
            if (!SourceIsLive)
                RequestClose();
            else if (!FocusPortableHostedControl(List) && SourceIsLive && Visible)
                throw new InvalidOperationException("The portable ComboBox list could not acquire source focus from its live Form owner.");
            _combo.Invalidate();
        }

        private bool HasCurrentItems()
        {
            if (_combo.Items.Count != _entries.Length)
                return false;
            for (int i = 0; i < _entries.Length; i++)
                if (!ReferenceEquals(_combo.Items.InnerList[i], _entries[i]) || !ReferenceEquals(_entries[i].Item, _items[i]))
                    return false;
            return true;
        }

        private bool AdmitSelection()
        {
            if (!SourceIsLive || !Visible)
                return false;
            if (!HasCurrentItems())
            {
                RequestClose();
                throw new NotSupportedException("Changing ComboBox items while the portable list is open requires a new dropdown. No stale list selection was committed.");
            }
            return true;
        }

        private void ListSelectionChanged(object? sender, EventArgs e)
        {
            if (_syncingSelection || !AdmitSelection())
                return;
            _syncingSelection = true;
            try
            {
                _combo.SelectedIndex = List.SelectedIndex;
                // A caller may choose another item from SelectedIndexChanged.
                // Keep that actual source selection, not the pre-callback row.
                if (SourceIsLive && HasCurrentItems())
                    List.SelectedIndex = _combo.SelectedIndex;
            }
            finally { _syncingSelection = false; }
            _combo.Invalidate();
        }

        private void SourceSelectionChanged(object? sender, EventArgs e)
        {
            if (_syncingSelection || !AdmitSelection())
                return;
            _syncingSelection = true;
            try { List.SelectedIndex = _combo.SelectedIndex; }
            finally { _syncingSelection = false; }
            _combo.Invalidate();
        }

        internal void Commit()
        {
            if (!AdmitSelection())
                return;
            if (List.SelectedIndex >= 0)
                _combo.OnSelectionChangeCommittedInternal(EventArgs.Empty);
            if (ReferenceEquals(_combo._portableDropDown, this) && !IsDisposed)
                Close(ToolStripDropDownCloseReason.ItemClicked);
        }

        internal void Cancel()
        {
            if (!AdmitSelection())
                return;
            _combo.SelectedIndex = _originalIndex;
            if (ReferenceEquals(_combo._portableDropDown, this) && !IsDisposed)
                Close(ToolStripDropDownCloseReason.Keyboard);
        }

        protected override void OnClosed(ToolStripDropDownClosedEventArgs e)
        {
            ExceptionDispatchInfo? failure = null;
            CompletePortableDropDownStage(Retire, ref failure);
            CompletePortableDropDownStage(NotifyClosed, ref failure);
            CompletePortableDropDownStage(() => base.OnClosed(e), ref failure);
            CompletePortableDropDownStage(Dispose, ref failure);
            failure?.Throw();
        }

        private void Retire()
        {
            if (_retired)
                return;
            _retired = true;
            foreach (Control control in _sourcePath)
                control.PortableHostedFocusLifetimeChanged -= SourceLifetimeChanged;
            _sourcePath.Clear();
            _combo.SelectedIndexChanged -= SourceSelectionChanged;
            if (ReferenceEquals(_combo._portableDropDown, this))
            {
                _combo._portableDropDown = null;
                _combo._dropDown = false;
                _combo._dropDownWillBeClosed = false;
            }
            _combo.Invalidate();
        }

        private void NotifyClosed()
        {
            if (!_opened || _closedNotified)
                return;
            _closedNotified = true;
            _combo.OnDropDownClosed(EventArgs.Empty);
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {
                base.Dispose(false);
                return;
            }
            if (_disposingPopup || IsDisposed)
                return;
            _disposingPopup = true;
            ExceptionDispatchInfo? failure = null;
            try
            {
                CompletePortableDropDownStage(Retire, ref failure);
                // Release the owned native resource before arbitrary hosted
                // Disposed callbacks can throw. ToolStripControlHost removes
                // itself before disposing its child; finish the strip even if
                // that child callback fails.
                if (IsHandleCreated)
                    CompletePortableDropDownStage(DestroyHandle, ref failure);
                CompletePortableDropDownStage(_host.Dispose, ref failure);
                CompletePortableDropDownStage(() => base.Dispose(true), ref failure);
                if (!Visible)
                    CompletePortableDropDownStage(NotifyClosed, ref failure);
            }
            finally { _disposingPopup = false; }
            failure?.Throw();
        }
    }
}
#endif
