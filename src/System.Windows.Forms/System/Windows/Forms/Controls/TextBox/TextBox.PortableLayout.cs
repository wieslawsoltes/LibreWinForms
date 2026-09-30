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
    private uint _portableSelectionVersion;
    private uint _portableLayoutVersion;
    private uint _portableTextFocusVersion;
    private PortablePointerDispatchContext _portableTextPointerPress;
    private PortableTextPointerState? _portablePasswordPointerSelection;
    private uint _portablePasswordPointerLayoutVersion;
    private bool _portableCaretVisible = true;
    private Timer? _portableCaretTimer;
    private RectangleF _portableCaretBounds;
    private float? _portablePreferredCaretX;

    private ILibreTextLayout? GetPortableTextLayout(Graphics graphics, string text, TextFormatFlags flags,
        PortablePointerDispatchContext? pointerContext = null)
    {
        if (LibrePlatform.Current.TextRenderer is not ILibreTextLayoutService service)
        {
            ReleasePortableTextLayout();
            return null;
        }

        if (_portableTextLayout is not null && ReferenceEquals(service, _portableLayoutService) &&
            ReferenceEquals(Font, _portableLayoutFont) && _portableLayoutText == text &&
            _portableLayoutSize == PortableTextViewport.Size && _portableLayoutFlags == flags &&
            _portableLayoutDpiX == graphics.DpiX && _portableLayoutDpiY == graphics.DpiY)
            return _portableTextLayout;

        // The source clip stays fixed while this owned paragraph scrolls. The
        // ordinary non-layout renderer retains its original format contract.
        uint layoutVersion = _portableLayoutVersion;
        uint selectionVersion = _portableSelectionVersion;
        Font font = Font;
        Size size = PortableTextViewport.Size;
        ILibreTextLayout next = TextRenderer.CreatePortableTextLayout(service, graphics, text, font, size,
            flags | TextFormatFlags.NoClipping);
        if (pointerContext is { } context && (!context.IsCurrent || layoutVersion != _portableLayoutVersion
            || selectionVersion != _portableSelectionVersion || !ReferenceEquals(font, Font)
            || size != PortableTextViewport.Size || text != GetPortableDisplayText(placeholder: false)
            || flags != GetPortableEditorTextFlags() || !ReferenceEquals(service, LibrePlatform.Current.TextRenderer)))
        {
            next.Dispose();
            return null;
        }

        ILibreTextLayout? previous = _portableTextLayout;
        _portableTextLayout = next;
        uint publishedVersion = ++_portableLayoutVersion;
        _portableLayoutService = service;
        _portableLayoutFont = font;
        _portableLayoutText = text;
        _portableLayoutSize = size;
        _portableLayoutFlags = flags;
        _portableLayoutDpiX = graphics.DpiX;
        _portableLayoutDpiY = graphics.DpiY;
        _portableEnsureCaretVisible = true;
        _portablePreferredCaretX = null;
        // Publish before releasing the previous provider lease. Reentrant
        // disposal must not have its replacement generation overwritten.
        previous?.Dispose();
        return publishedVersion == _portableLayoutVersion && ReferenceEquals(_portableTextLayout, next) ? next : null;
    }

    private void ReleasePortableTextLayout()
    {
        ILibreTextLayout? previous = _portableTextLayout;
        _portableTextLayout = null;
        _portableLayoutVersion++;
        _portableLayoutService = null;
        _portableLayoutFont = null;
        _portableLayoutText = null;
        _portablePreferredCaretX = null;
        _portableCaretTimer?.Stop();
        _portableEnsureCaretVisible = true;
        previous?.Dispose();
    }

    private void DisposePortableTextInteraction()
    {
        RetirePortablePointerSelection();
        ReleasePortableTextLayout();
        _portableCaretTimer?.Dispose();
        _portableCaretTimer = null;
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
            EnsurePortableCaretVisible(caret, caretWidth);
        }

        if (placeholder) _portableTextScroll = PointF.Empty;
        Rectangle viewport = PortableTextViewport;
        var origin = new PointF(viewport.X - _portableTextScroll.X, viewport.Y - _portableTextScroll.Y);
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

    private bool EnsurePortableCaretVisible(LibreTextCaret caret, float caretWidth)
    {
        PointF previous = _portableTextScroll;
        // Center/right alignment and RTL paragraphs can extend left of the
        // layout frame. A zero-clamped offset cannot reveal those real carets.
        // Keep the current viewport whenever the complete caret already fits.
        _portableTextScroll.X = Math.Clamp(_portableTextScroll.X,
            caret.Position.X + Math.Min(caretWidth, PortableTextViewport.Width) - PortableTextViewport.Width, caret.Position.X);
        _portableTextScroll.Y = Math.Clamp(_portableTextScroll.Y,
            caret.Position.Y + Math.Min(caret.Height, PortableTextViewport.Height) - PortableTextViewport.Height, caret.Position.Y);
        _portableEnsureCaretVisible = false;
        return _portableTextScroll != previous;
    }

    private protected override void ScrollPortableTextCaretIntoView()
    {
        if (PortableTextViewport.Width <= 0 || PortableTextViewport.Height <= 0)
        {
            return;
        }

        ILibreTextLayout layout = GetPortableInputLayout()
            ?? throw new PlatformNotSupportedException("Caret scrolling requires a retained text-layout provider.");
        LibreTextCaret caret = layout.GetCaret(PortableSelectionActiveEnd, _portableCaretTrailing);
        if (EnsurePortableCaretVisible(caret, Math.Max(1, SystemInformation.CaretWidth)))
        {
            // Publish the offset before notification: a handler can repaint,
            // hit-test, replace text, or dispose the control synchronously.
            Invalidate();
        }
    }

    private ILibreTextLayout? GetPortableInputLayout(PortablePointerDispatchContext? pointerContext = null)
    {
        if (IsDisposed || !IsHandleCreated || LibrePlatform.Current.TextRenderer is not ILibreTextLayoutService)
            return null;
        string text = GetPortableDisplayText(placeholder: false);
        TextFormatFlags flags = GetPortableEditorTextFlags();
        if (_portableTextLayout is not null && _portableLayoutText == text &&
            ReferenceEquals(_portableLayoutFont, Font) && _portableLayoutSize == PortableTextViewport.Size &&
            _portableLayoutFlags == flags && ReferenceEquals(_portableLayoutService, LibrePlatform.Current.TextRenderer))
            return _portableTextLayout;
        using Graphics graphics = CreateGraphicsInternal();
        return GetPortableTextLayout(graphics, text, flags, pointerContext);
    }

    private void ApplyPortableLayoutCaret(int position, bool trailing, bool extend, bool vertical = false)
    {
        if (!vertical) _portablePreferredCaretX = null;
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

    private bool TryMovePortableRowCaret(Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        if (!Multiline || key is not (Keys.Up or Keys.Down or Keys.Home or Keys.End) ||
            (keyData & Keys.Modifiers & ~Keys.Shift) != Keys.None ||
            LibrePlatform.Current.TextRenderer is not ILibreTextRowNavigationService)
            return false;
        ILibreTextLayout? layout = GetPortableInputLayout();
        if (layout is null) return false;
        if (layout is not ILibreTextRowNavigation rows)
            throw new InvalidOperationException("The text provider declared row navigation but returned a layout without that capability.");
        bool vertical = key is Keys.Up or Keys.Down;
        LibreTextCaret next;
        if (vertical)
        {
            _portablePreferredCaretX ??= layout.GetCaret(PortableSelectionActiveEnd, _portableCaretTrailing).Position.X;
            next = rows.MoveCaretVertically(PortableSelectionActiveEnd, _portableCaretTrailing,
                key == Keys.Up ? -1 : 1, _portablePreferredCaretX.Value);
        }
        else next = rows.GetRowBoundary(PortableSelectionActiveEnd, _portableCaretTrailing, key == Keys.End);
        ApplyPortableLayoutCaret(next.TextPosition, next.IsTrailing, (keyData & Keys.Shift) != Keys.None, vertical);
        return true;
    }

    internal override bool ProcessPortableMouseDownDefault(MouseEventArgs e, in PortablePointerDispatchContext context)
    {
        if (e.Button != MouseButtons.Left) return true;
        if (!context.IsCurrent) return false;
        if (!Focused && !Focus()) return context.IsCurrent;
        if (!context.IsCurrent) return false;
        _portablePasswordPointerSelection = null;
        _portableWordPointerSelection = null;
        if (e.Clicks == 2 && PasswordProtect)
        {
            // EDIT selects the whole password, not a word in either the source
            // or its display mask. No hit test or source shaping is needed.
            PortableTextPointerState state = new(this);
            uint layoutVersion = _portableLayoutVersion;
            SelectAll();
            if (layoutVersion != _portableLayoutVersion
                || !state.IsCurrent(this, unchecked(state.SelectionVersion + 1), checkScroll: false)
                || !context.IsCurrent) return false;
            _portablePasswordPointerSelection = state;
            _portablePasswordPointerLayoutVersion = layoutVersion;
        }
        else if (e.Clicks == 2 && LibrePlatform.Current.TextRenderer is ILibreEditWordBoundaryService)
        {
            if (!ApplyPortableWordPointerDown(e, context)) return false;
        }
        else
        {
            if (!ApplyPortablePointerCaret(e, context, extend: (ModifierKeys & Keys.Shift) != 0,
                out bool selected)) return false;
            if (!selected) return true;
        }

        // Control already captured this exact press. Do not reacquire capture
        // after a selection callback has released/replaced it.
        _portableTextPointerPress = context;
        _portablePointerSelecting = true;
        return true;
    }

    internal override bool ProcessPortableMouseMoveDefault(MouseEventArgs e, in PortablePointerDispatchContext context)
    {
        if (_portablePointerSelecting && !_portableTextPointerPress.IsCurrentPress)
        {
            RetirePortablePointerSelection();
            return true;
        }

        if (!_portablePointerSelecting || !Capture || (e.Button & MouseButtons.Left) == 0) return true;
        if (_portablePasswordPointerSelection is { } selection)
        {
            // Moves retain the committed range without selecting again. A
            // handler's replacement selection (even the same range), text or
            // layout retires this mode instead of being overwritten on drag.
            bool currentSelection = _portablePasswordPointerLayoutVersion == _portableLayoutVersion
                && selection.IsCurrent(this, unchecked(selection.SelectionVersion + 1), checkScroll: false);
            if (!context.IsCurrent) return false;
            if (!currentSelection)
                RetirePortablePointerSelection();
            return true;
        }

        if (_portableWordPointerSelection is { } word)
            return ApplyPortableWordPointerMove(e, context, word);

        return ApplyPortablePointerCaret(e, context, extend: true, out _);
    }

    internal override void InvokePortableMouseUp(MouseEventArgs e, bool? nativeDoubleClick,
        bool nativeClickEligible, Func<bool>? isCurrentRelease)
    {
        // Release the old drag lease before public up/click callbacks can start
        // another press. Control's normal release need not raise CaptureChanged.
        RetirePortablePointerSelection();
        base.InvokePortableMouseUp(e, nativeDoubleClick, nativeClickEligible, isCurrentRelease);
    }

    private void RetirePortablePointerSelection()
    {
        _portablePointerSelecting = false;
        _portableTextPointerPress = default;
        _portablePasswordPointerSelection = null;
        _portableWordPointerSelection = null;
    }

    private bool ApplyPortablePointerCaret(MouseEventArgs e, in PortablePointerDispatchContext context,
        bool extend, out bool selected)
    {
        selected = false;
        PortableTextPointerState state = new(this);
        ILibreTextLayout? layout = GetPortableInputLayout(context);
        if (!context.IsCurrent || !state.IsCurrent(this, state.SelectionVersion)) return false;
        if (layout is null) return LibrePlatform.Current.TextRenderer is not ILibreTextLayoutService;
        if (!ReferenceEquals(layout, _portableTextLayout)) return false;
        uint layoutVersion = _portableLayoutVersion;
        LibreTextHit hit = layout.HitTest(new PointF(e.X - state.Viewport.X + state.Scroll.X,
            e.Y - state.Viewport.Y + state.Scroll.Y));
        if (!context.IsCurrent || !ReferenceEquals(layout, _portableTextLayout)
            || layoutVersion != _portableLayoutVersion || !state.IsCurrent(this, state.SelectionVersion)) return false;

        ApplyPortableLayoutCaret(hit.TextPosition, hit.IsTrailing, extend);
        // SelectInternal can invoke accessibility and Invalidated callbacks.
        // Exactly our own selection revision may advance, never a nested one.
        if (!context.IsCurrent || !ReferenceEquals(layout, _portableTextLayout)
            || layoutVersion != _portableLayoutVersion
            || !state.IsCurrent(this, unchecked(state.SelectionVersion + 1), checkScroll: false)) return false;
        selected = true;
        return true;
    }

    private readonly struct PortableTextPointerState
    {
        private readonly string _text;
        private readonly Font _font;
        private readonly TextFormatFlags _flags;
        private readonly ILibreTextRendererService _service;
        private readonly char _passwordChar;
        private readonly bool _useSystemPasswordChar;
        private readonly int _deviceDpi;
        private readonly uint _focusVersion;
        internal int TextLength => _text.Length;
        internal uint SelectionVersion { get; }
        internal Rectangle Viewport { get; }
        internal PointF Scroll { get; }

        internal PortableTextPointerState(TextBox owner)
        {
            _text = owner.Text;
            _font = owner.Font;
            _flags = owner.GetPortableEditorTextFlags();
            _service = LibrePlatform.Current.TextRenderer;
            _passwordChar = owner._passwordChar;
            _useSystemPasswordChar = owner._useSystemPasswordChar;
            _deviceDpi = owner.DeviceDpiInternal;
            _focusVersion = owner._portableTextFocusVersion;
            SelectionVersion = owner._portableSelectionVersion;
            Viewport = owner.PortableTextViewport;
            Scroll = owner._portableTextScroll;
        }

        internal bool IsCurrent(TextBox owner, uint selectionVersion, bool checkScroll = true)
            => selectionVersion == owner._portableSelectionVersion && owner.Focused
                && _focusVersion == owner._portableTextFocusVersion
                && _text == owner.Text && ReferenceEquals(_font, owner.Font)
                && _flags == owner.GetPortableEditorTextFlags()
                && ReferenceEquals(_service, LibrePlatform.Current.TextRenderer)
                && _passwordChar == owner._passwordChar && _useSystemPasswordChar == owner._useSystemPasswordChar
                && _deviceDpi == owner.DeviceDpiInternal && Viewport == owner.PortableTextViewport
                // Our own selection invalidation may synchronously paint and
                // reveal its caret. The hit frame need not survive that commit.
                && (!checkScroll || Scroll == owner._portableTextScroll);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        if (!Capture)
        {
            RetirePortablePointerSelection();
        }

        base.OnMouseCaptureChanged(e);
    }
}
#endif
