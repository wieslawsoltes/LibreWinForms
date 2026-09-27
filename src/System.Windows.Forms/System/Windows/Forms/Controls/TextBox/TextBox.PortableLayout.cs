// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Drawing;
using System.Drawing.Drawing2D;
using LibreWinForms.Platform;

namespace System.Windows.Forms;

public partial class TextBox
{
    private ILibreTextLayout? _portableTextLayout;
    private ILibreTextLayoutService? _portableLayoutService;
    private Font? _portableLayoutFont;
    private string? _portableLayoutText;
    private Size _portableLayoutSize;
    private TextFormatFlags _portableLayoutFlags;
    private float _portableLayoutDpiX, _portableLayoutDpiY;
    private PointF _portableTextScroll;
    private bool _portableEnsureCaretVisible = true;
    private bool _portableCaretTrailing, _portableApplyingCaret, _portablePointerSelecting;
    private bool _portableCaretVisible = true;
    private Timer? _portableCaretTimer;
    private RectangleF _portableCaretBounds;

    private ILibreTextLayout? GetPortableTextLayout(Graphics graphics, string text, TextFormatFlags flags)
    {
        if (LibrePlatform.Current.TextRenderer is not ILibreTextLayoutService service)
        {
            ReleasePortableTextLayout();
            return null;
        }

        if (_portableTextLayout is not null && ReferenceEquals(service, _portableLayoutService) &&
            ReferenceEquals(Font, _portableLayoutFont) && _portableLayoutText == text &&
            _portableLayoutSize == ClientSize && _portableLayoutFlags == flags &&
            _portableLayoutDpiX == graphics.DpiX && _portableLayoutDpiY == graphics.DpiY)
            return _portableTextLayout;

        // The source clip stays fixed while this owned paragraph scrolls. The
        // ordinary non-layout renderer retains its original format contract.
        ILibreTextLayout next = TextRenderer.CreatePortableTextLayout(service, graphics, text, Font, ClientSize,
            flags | TextFormatFlags.NoClipping);
        _portableTextLayout?.Dispose();
        _portableTextLayout = next;
        _portableLayoutService = service;
        _portableLayoutFont = Font;
        _portableLayoutText = text;
        _portableLayoutSize = ClientSize;
        _portableLayoutFlags = flags;
        _portableLayoutDpiX = graphics.DpiX;
        _portableLayoutDpiY = graphics.DpiY;
        _portableEnsureCaretVisible = true;
        return next;
    }

    private void ReleasePortableTextLayout()
    {
        _portableTextLayout?.Dispose();
        _portableTextLayout = null;
        _portableLayoutService = null;
        _portableLayoutFont = null;
        _portableLayoutText = null;
        _portableCaretTimer?.Stop();
        _portableEnsureCaretVisible = true;
    }

    private void DisposePortableTextInteraction()
    {
        ReleasePortableTextLayout();
        _portableCaretTimer?.Dispose();
        _portableCaretTimer = null;
        _portablePointerSelecting = false;
    }

    private void ResetPortableCaretBlink()
    {
        _portableCaretVisible = true;
        _portableEnsureCaretVisible = true;
        _portableCaretTimer?.Stop();
        if (!Focused || !Enabled || !IsHandleCreated || _portableTextLayout is null || IsDisposed) return;
        int interval = SystemInformation.CaretBlinkTime;
        if (interval <= 0) return;
        if (_portableCaretTimer is null)
        {
            _portableCaretTimer = new Timer();
            _portableCaretTimer.Tick += OnPortableCaretTick;
        }

        _portableCaretTimer.Interval = interval;
        _portableCaretTimer.Start();
    }

    private void OnPortableCaretTick(object? sender, EventArgs e)
    {
        if (!Focused || !Enabled || !IsHandleCreated || IsDisposed)
        {
            _portableCaretTimer?.Stop();
            return;
        }

        _portableCaretVisible = !_portableCaretVisible;
        Invalidate(Rectangle.Ceiling(_portableCaretBounds));
    }

    private bool PaintPortableTextLayout(PaintEventArgs e, string text, bool placeholder,
        TextFormatFlags flags, Color foreground)
    {
        bool hadLayout = _portableTextLayout is not null;
        ILibreTextLayout? layout = GetPortableTextLayout(e.Graphics, text, flags);
        if (layout is null) return false;
        LibreTextCaret caret = layout.GetCaret(placeholder ? 0 : PortableSelectionActiveEnd, _portableCaretTrailing);
        float caretWidth = Math.Max(1, SystemInformation.CaretWidth);
        if (_portableEnsureCaretVisible && Focused && !placeholder)
        {
            _portableTextScroll.X = Math.Clamp(_portableTextScroll.X,
                Math.Max(0, caret.Position.X + Math.Min(caretWidth, ClientSize.Width) - ClientSize.Width), Math.Max(0, caret.Position.X));
            _portableTextScroll.Y = Math.Clamp(_portableTextScroll.Y,
                Math.Max(0, caret.Position.Y + Math.Min(caret.Height, ClientSize.Height) - ClientSize.Height), Math.Max(0, caret.Position.Y));
            _portableEnsureCaretVisible = false;
        }

        if (placeholder) _portableTextScroll = PointF.Empty;
        var origin = new PointF(-_portableTextScroll.X, -_portableTextScroll.Y);
        ReadOnlyMemory<RectangleF> selected = !placeholder && (Focused || !HideSelection)
            ? layout.GetSelectionRectangles(SelectionStart, SelectionLength) : default;
        using Region? selectionClip = selected.IsEmpty ? null : new Region();
        if (selectionClip is not null)
        {
            selectionClip.MakeEmpty();
            using var highlight = new SolidBrush(Focused ? SystemColors.Highlight : SystemColors.Control);
            foreach (RectangleF box in selected.Span)
            {
                var placed = new RectangleF(box.X + origin.X, box.Y + origin.Y, box.Width, box.Height);
                e.Graphics.FillRectangle(highlight, placed);
                selectionClip.Union(placed);
            }
        }

        layout.Draw(e.Graphics, origin, foreground);
        if (selectionClip is not null)
        {
            GraphicsState state = e.Graphics.Save();
            try
            {
                e.Graphics.SetClip(selectionClip, CombineMode.Intersect);
                layout.Draw(e.Graphics, origin, Focused ? SystemColors.HighlightText : foreground);
            }
            finally { e.Graphics.Restore(state); }
        }

        _portableCaretBounds = new RectangleF(caret.Position.X + origin.X, caret.Position.Y + origin.Y,
            caretWidth, caret.Height);
        if (Focused && Enabled && !placeholder && _portableCaretVisible)
        {
            using var ink = new SolidBrush(ForeColor);
            e.Graphics.FillRectangle(ink, _portableCaretBounds);
        }

        if (!hadLayout && !placeholder) ResetPortableCaretBlink();
        return true;
    }

    private ILibreTextLayout? GetPortableInputLayout()
    {
        if (IsDisposed || !IsHandleCreated || LibrePlatform.Current.TextRenderer is not ILibreTextLayoutService)
            return null;
        string text = GetPortableDisplayText(placeholder: false);
        TextFormatFlags flags = GetPortableEditorTextFlags();
        if (_portableTextLayout is not null && _portableLayoutText == text &&
            ReferenceEquals(_portableLayoutFont, Font) && _portableLayoutSize == ClientSize &&
            _portableLayoutFlags == flags && ReferenceEquals(_portableLayoutService, LibrePlatform.Current.TextRenderer))
            return _portableTextLayout;
        using Graphics graphics = CreateGraphicsInternal();
        return GetPortableTextLayout(graphics, text, flags);
    }

    private void ApplyPortableLayoutCaret(int position, bool trailing, bool extend)
    {
        _portableCaretTrailing = trailing;
        _portableApplyingCaret = true;
        try { SelectPortableCaret(position, extend); }
        finally { _portableApplyingCaret = false; }
    }

    private bool TryMovePortableLayoutCaret(Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        if (key is not (Keys.Left or Keys.Right) ||
            (keyData & Keys.Modifiers & ~Keys.Shift) != Keys.None) return false;
        ILibreTextLayout? layout = GetPortableInputLayout();
        if (layout is null) return false;
        bool extend = (keyData & Keys.Shift) != Keys.None;
        LibreTextCaret next;
        if (!extend && SelectionLength != 0)
        {
            LibreTextCaret first = layout.GetCaret(SelectionStart);
            LibreTextCaret last = layout.GetCaret(SelectionStart + SelectionLength, trailing: true);
            bool firstBeforeLast = first.Position.Y < last.Position.Y ||
                (first.Position.Y == last.Position.Y && first.Position.X <= last.Position.X);
            next = (key == Keys.Left) == firstBeforeLast ? first : last;
        }
        else next = layout.MoveCaret(PortableSelectionActiveEnd, _portableCaretTrailing, key == Keys.Left ? -1 : 1);
        ApplyPortableLayoutCaret(next.TextPosition, next.IsTrailing, extend);
        return true;
    }

    private void ProcessPortableTextMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || IsDisposed || !Enabled || !IsHandleCreated) return;
        if (!Focused && !Focus()) return;
        if (IsDisposed || !IsHandleCreated) return;
        ILibreTextLayout? layout = GetPortableInputLayout();
        if (layout is null) return;
        LibreTextHit hit = layout.HitTest(new PointF(e.X + _portableTextScroll.X, e.Y + _portableTextScroll.Y));
        ApplyPortableLayoutCaret(hit.TextPosition, hit.IsTrailing, (ModifierKeys & Keys.Shift) != 0);
        if (IsDisposed || !IsHandleCreated) return;
        _portablePointerSelecting = true;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_portablePointerSelecting || !Capture || IsDisposed || (e.Button & MouseButtons.Left) == 0) return;
        ILibreTextLayout? layout = GetPortableInputLayout();
        if (layout is null) return;
        LibreTextHit hit = layout.HitTest(new PointF(e.X + _portableTextScroll.X, e.Y + _portableTextScroll.Y));
        ApplyPortableLayoutCaret(hit.TextPosition, hit.IsTrailing, extend: true);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        if (!Capture) _portablePointerSelecting = false;
        base.OnMouseCaptureChanged(e);
    }
}
#endif
