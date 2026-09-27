// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Drawing;
using System.Drawing.Drawing2D;

namespace System.Windows.Forms;

public partial class ListBox
{
    private int _portableWheelRemainder;

    private void EnsurePortableListMode()
    {
        if (_drawMode != DrawMode.Normal || _selectionMode != SelectionMode.One || _multiColumn
            || _horizontalScrollbar || _useCustomTabOffsets || _scrollAlwaysVisible)
        {
            throw new PlatformNotSupportedException(
                "Portable ListBox rendering and input require normal, single-selection, single-column rows. "
                + "Owner draw, multiple selection, custom tab stops and scrollbar controls are not implemented.");
        }
    }

    // Portable children have no OS non-client surface. Use the canonical border
    // painter inside the client bounds, and use this same remaining area for
    // drawing, point lookup, page movement and scrolling.
    private Rectangle GetPortableListViewport()
    {
        Rectangle bounds = ClientRectangle;
        int inset = _borderStyle switch
        {
            BorderStyle.FixedSingle => 1,
            BorderStyle.Fixed3D => 2,
            _ => 0
        };
        return new Rectangle(inset, inset,
            Math.Max(0, bounds.Width - 2 * inset), Math.Max(0, bounds.Height - 2 * inset));
    }

    private int GetPortableVisibleRowCount()
        => Math.Max(1, GetPortableListViewport().Height / Font.Height);

    private int GetPortableTopIndex()
        => Math.Clamp(_topIndex, 0, Math.Max(0, Items.Count - GetPortableVisibleRowCount()));

    private void SetPortableTopIndex(int index)
    {
        int top = Math.Clamp(index, 0, Math.Max(0, Items.Count - GetPortableVisibleRowCount()));
        if (_topIndex != top)
        {
            _topIndex = top;
            Invalidate();
        }
    }

    private Rectangle GetPortableRowRectangle(int index)
    {
        Rectangle viewport = GetPortableListViewport();
        int height = Font.Height;
        return new Rectangle(viewport.X,
            checked(viewport.Y + (index - GetPortableTopIndex()) * height), viewport.Width, height);
    }

    private void EnsurePortableSelectedRowVisible()
    {
        int selected = SelectedIndex;
        int top = GetPortableTopIndex();
        int rows = GetPortableVisibleRowCount();
        if (selected >= 0)
            SetPortableTopIndex(selected < top ? selected : selected >= top + rows ? selected - rows + 1 : top);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        EnsurePortableListMode();
        Rectangle viewport = GetPortableListViewport();
        GraphicsState state = e.Graphics.Save();
        try
        {
            e.Graphics.SetClip(ClientRectangle, CombineMode.Intersect);
            e.Graphics.SetClip(e.ClipRectangle, CombineMode.Intersect);
            if (_borderStyle == BorderStyle.Fixed3D)
                ControlPaint.DrawBorder3D(e.Graphics, ClientRectangle, Border3DStyle.Sunken);
            else if (_borderStyle == BorderStyle.FixedSingle)
                ControlPaint.DrawBorder(e.Graphics, ClientRectangle, SystemColors.WindowFrame, ButtonBorderStyle.Solid);

            e.Graphics.SetClip(viewport, CombineMode.Intersect);
            TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            if (_useTabStops)
                flags |= TextFormatFlags.ExpandTabs;
            if (RightToLeft == RightToLeft.Yes)
                flags |= TextFormatFlags.RightToLeft | TextFormatFlags.Right;

            ItemArray items = Items.InnerArray;
            int version = items.Version;
            for (int index = GetPortableTopIndex(); index < items.Count; index++)
            {
                Rectangle row = GetPortableRowRectangle(index);
                if (row.Top >= viewport.Bottom)
                    break;
                if (!row.IntersectsWith(e.ClipRectangle) || row.Width <= 0 || viewport.Height <= 0)
                    continue;

                string text = GetItemText(Items[index]);
                // Formatting can run user code and retire or replace the list.
                // Do not render old item indices into a changed collection.
                if (IsDisposed || Disposing || items.Version != version)
                    break;

                bool selected = SelectedItems.GetSelected(index);
                Color background = selected ? SystemColors.Highlight : BackColor;
                Color foreground = !Enabled ? SystemColors.GrayText : selected ? SystemColors.HighlightText : ForeColor;
                using (SolidBrush brush = new(background))
                    e.Graphics.FillRectangle(brush, row);
                TextRenderer.DrawText(e.Graphics, text, Font, row, foreground, flags);
                if (selected && Focused && ShowFocusCues)
                    ControlPaint.DrawFocusRectangle(e.Graphics, row, foreground, background);
            }
        }
        finally
        {
            e.Graphics.Restore(state);
        }

        if (!IsDisposed && !Disposing)
            base.OnPaint(e);
    }

    protected override bool IsInputKey(Keys keyData)
        => (keyData & Keys.Alt) == 0 && (keyData & Keys.KeyCode) is Keys.Up or Keys.Down
            || base.IsInputKey(keyData);

    internal override void ProcessPortableDefaultKeyMessage(ref Message message)
    {
        if (message.MsgInternal != PInvokeCore.WM_KEYDOWN || ModifierKeys != Keys.None || !Enabled)
            return;

        Keys key = (Keys)(int)message.WParamInternal;
        if (key is not (Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown))
            return;

        EnsurePortableListMode();
        if (Items.Count == 0)
            return;

        int current = SelectedIndex;
        int next = key switch
        {
            Keys.Home => 0,
            Keys.End => Items.Count - 1,
            Keys.Up => current < 0 ? 0 : current - 1,
            Keys.Down => current + 1,
            Keys.PageUp => current < 0 ? 0 : current - Math.Max(1, GetPortableVisibleRowCount() - 1),
            Keys.PageDown => current < 0 ? 0 : current + Math.Max(1, GetPortableVisibleRowCount() - 1),
            _ => current
        };
        // ProcessKeyMessage already offered the original key to managed events,
        // filters and preprocessing. The default mutation uses the public source
        // setter so selection collections, data binding and events stay together.
        SelectedIndex = Math.Clamp(next, 0, Items.Count - 1);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        nint handle = IsHandleCreated ? Handle : 0;
        if (Enabled && e.Button == MouseButtons.Left)
        {
            int index = IndexFromPoint(e.Location);
            if (index != NoMatches)
                SelectedIndex = index;
        }

        // Native LISTBOX default processing changes selection before the
        // canonical Control.WmMouseDown raises the managed MouseDown event.
        if (!IsDisposed && !Disposing && (handle == 0 || (IsHandleCreated && Handle == handle)))
            base.OnMouseDown(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (IsDisposed || Disposing || !Enabled || e is HandledMouseEventArgs { Handled: true })
            return;

        EnsurePortableListMode();
        long delta = (long)_portableWheelRemainder + e.Delta;
        long detents = delta / SystemInformation.MouseWheelScrollDelta;
        _portableWheelRemainder = (int)(delta % SystemInformation.MouseWheelScrollDelta);
        int lines = SystemInformation.MouseWheelScrollLines;
        long rows = detents * (lines < 0 ? GetPortableVisibleRowCount() : lines);
        SetPortableTopIndex((int)Math.Clamp(GetPortableTopIndex() - rows, 0, int.MaxValue));
        if (e is HandledMouseEventArgs handled)
            handled.Handled = true;
    }
}
#endif
