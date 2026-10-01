// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.ExceptionServices;
using LibreWinForms.Platform;

namespace System.Windows.Forms;

public partial class MaskedTextBox : IPortableRetainedTextLayoutOwner
{
    private PortableRetainedTextLayoutOwner? _portableMaskedLayout;
    private uint _portableMaskedSourceVersion, _portableMaskedSelectionVersion, _portableMaskedFocusVersion;
    private bool _portableMaskedTrailing, _portableMaskedApplyingCaret, _portableMaskedPointerSelecting;
    private PortablePointerDispatchContext _portableMaskedPress;
    private Timer? _portableMaskedCaretTimer;
    private bool _portableMaskedCaretVisible = true;
    private bool _portableMaskedHandleRetiring;
    private RectangleF _portableMaskedCaretBounds;
    private int _portableMaskedMutationDepth;

    private Rectangle PortableMaskedViewport => PortableEditFrame.GetTextViewport(PortableClientRectangle, BorderStyle);

    private string GetPortableMaskedDisplay()
    {
        string display = _flagState[s_isNullMask] ? WindowText : GetFormattedDisplayString();
        return _flagState[s_isNullMask] && _maskedTextProvider.IsPassword
            ? new string(_maskedTextProvider.PasswordChar, display.Length) : display;
    }

    private TextFormatFlags GetPortableMaskedFlags()
    {
        TextFormatFlags flags = TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix
            | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        flags |= RtlTranslateHorizontal(TextAlign) switch
        {
            HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
            HorizontalAlignment.Right => TextFormatFlags.Right,
            _ => TextFormatFlags.Left
        };
        if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
        return flags;
    }

    bool IPortableRetainedTextLayoutOwner.IsRetainedTextStateCurrent(in PortableRetainedTextState state)
        => !IsDisposed && !Disposing && !_portableMaskedHandleRetiring
            && state.SourceVersion == _portableMaskedSourceVersion
            && state.DeviceDpi == DeviceDpiInternal
            && state.Display == GetPortableMaskedDisplay() && ReferenceEquals(state.Font, Font)
            && state.Viewport == PortableMaskedViewport && state.Flags == GetPortableMaskedFlags()
            && ReferenceEquals(state.Service, LibrePlatform.Current.TextRenderer);

    bool IPortableRetainedTextLayoutOwner.IsRetainedTextMutationActive => _portableMaskedMutationDepth != 0;

    private void BeginPortableMaskedMutation() => _portableMaskedMutationDepth++;

    private void EndPortableMaskedMutation(Exception? originalFailure)
    {
        _portableMaskedMutationDepth--;
        if (_portableMaskedMutationDepth == 0) _portableMaskedLayout?.DrainRetired(originalFailure);
    }

    private PortableRetainedTextLayoutOwner.Use? AcquirePortableMaskedLayout(Graphics graphics)
    {
        if (LibrePlatform.Current.TextRenderer is not ILibreTextLayoutService service)
        {
            _portableMaskedLayout?.Invalidate();
            return null;
        }

        _portableMaskedLayout ??= new(this);
        PortableRetainedTextState state = new(GetPortableMaskedDisplay(), Font, PortableMaskedViewport,
            GetPortableMaskedFlags(), service, graphics.DpiX, graphics.DpiY, DeviceDpiInternal, _portableMaskedSourceVersion);
        return _portableMaskedLayout.Acquire(graphics, state);
    }

    private PortableRetainedTextLayoutOwner.Use? AcquirePortableMaskedInputLayout()
    {
        if (!IsHandleCreated || IsDisposed || Disposing || PortableMaskedViewport.Width <= 0
            || PortableMaskedViewport.Height <= 0) return null;
        using Graphics graphics = CreateGraphicsInternal();
        return AcquirePortableMaskedLayout(graphics);
    }

    private void InvalidatePortableMaskedLayout()
    {
        _portableMaskedSourceVersion++;
        _portableMaskedLayout?.Invalidate();
        _portableMaskedCaretTimer?.Stop();
    }

    private void ResetPortableMaskedCaret()
    {
        _portableMaskedCaretVisible = true;
        if (_portableMaskedLayout is not null) _portableMaskedLayout.NeedsCaretVisibility = true;
        _portableMaskedCaretTimer?.Stop();
        if (!Focused || !Enabled || !IsHandleCreated || IsDisposed || Disposing
            || _portableMaskedLayout?.HasLayout != true) return;
        int interval = SystemInformation.CaretBlinkTime;
        if (interval <= 0) return;
        if (_portableMaskedCaretTimer is null)
        {
            _portableMaskedCaretTimer = new();
            _portableMaskedCaretTimer.Tick += OnPortableMaskedCaretTick;
        }

        _portableMaskedCaretTimer.Interval = interval;
        _portableMaskedCaretTimer.Start();
    }

    private void OnPortableMaskedCaretTick(object? sender, EventArgs e)
    {
        if (!Focused || !Enabled || !IsHandleCreated || IsDisposed || Disposing)
        {
            _portableMaskedCaretTimer?.Stop();
            return;
        }

        _portableMaskedCaretVisible = !_portableMaskedCaretVisible;
        Invalidate(Rectangle.Ceiling(_portableMaskedCaretBounds));
    }

    private bool PaintPortableMaskedLayout(PaintEventArgs e, Color foreground)
    {
        bool hadLayout = _portableMaskedLayout?.HasLayout == true;
        bool hasLayoutService = LibrePlatform.Current.TextRenderer is ILibreTextLayoutService;
        uint selectionVersion = _portableMaskedSelectionVersion, focusVersion = _portableMaskedFocusVersion;
        if (AcquirePortableMaskedLayout(e.Graphics) is not { } use) return hasLayoutService;
        Exception? failure = null;
        try
        {
            if (!Current()) return true;
            ILibreTextLayout layout = use.Layout;
            int activeEnd = PortableSelectionSourceActiveEnd;
            bool hasCaret = Focused && (uint)activeEnd <= (uint)GetPortableMaskedDisplay().Length;
            LibreTextCaret caret = default;
            if (hasCaret)
            {
                caret = layout.GetCaret(activeEnd, _portableMaskedTrailing);
                if (!Current()) return true;
            }

            float caretWidth = Math.Max(1, SystemInformation.CaretWidth);
            Rectangle viewport = PortableMaskedViewport;
            if (hasCaret && _portableMaskedLayout!.NeedsCaretVisibility)
                _portableMaskedLayout.RevealCaret(caret, caretWidth, viewport.Size);
            PointF scroll = _portableMaskedLayout!.Scroll;
            PointF origin = new(viewport.X - scroll.X, viewport.Y - scroll.Y);
            ReadOnlyMemory<RectangleF> selected = Focused || !HideSelection
                ? layout.GetSelectionRectangles(SelectionStart, SelectionLength) : default;
            if (!Current()) return true;
            using Region? selectionClip = selected.IsEmpty ? null : new();
            if (selectionClip is not null)
            {
                selectionClip.MakeEmpty();
                using SolidBrush highlight = new(Focused ? SystemColors.Highlight : SystemColors.Control);
                foreach (RectangleF box in selected.Span)
                {
                    RectangleF placed = new(box.X + origin.X, box.Y + origin.Y, box.Width, box.Height);
                    e.Graphics.FillRectangle(highlight, placed);
                    selectionClip.Union(placed);
                }
            }

            layout.Draw(e.Graphics, origin, foreground);
            if (!Current()) return true;
            if (selectionClip is not null)
            {
                GraphicsState state = e.Graphics.Save();
                try
                {
                    e.Graphics.SetClip(selectionClip, CombineMode.Intersect);
                    layout.Draw(e.Graphics, origin, Focused ? SystemColors.HighlightText : foreground);
                }
                finally { e.Graphics.Restore(state); }
                if (!Current()) return true;
            }

            _portableMaskedCaretBounds = hasCaret
                ? new(caret.Position.X + origin.X, caret.Position.Y + origin.Y, caretWidth, caret.Height) : default;
            if (hasCaret && Enabled && _portableMaskedCaretVisible)
            {
                using SolidBrush ink = new(ForeColor);
                e.Graphics.FillRectangle(ink, _portableMaskedCaretBounds);
            }

            if (!hadLayout) ResetPortableMaskedCaret();
            return true;
        }
        catch (Exception error) { failure = error; throw; }
        finally { use.Release(failure); }

        bool Current() => use.IsCurrent && selectionVersion == _portableMaskedSelectionVersion
            && focusVersion == _portableMaskedFocusVersion;
    }

    private protected override void SelectInternal(int start, int length, int textLength)
    {
        uint version = ++_portableMaskedSelectionVersion;
        bool applying = _portableMaskedApplyingCaret;
        _portableMaskedApplyingCaret = false;
        base.SelectInternal(start, length, textLength);
        if (version != _portableMaskedSelectionVersion || IsDisposed || Disposing) return;
        if (!applying) _portableMaskedTrailing = false;
        ResetPortableMaskedCaret();
        Invalidate();
    }

    private void ApplyPortableMaskedCaret(int position, bool trailing, bool extend)
    {
        _portableMaskedTrailing = trailing;
        _portableMaskedApplyingCaret = true;
        try { SelectPortableCaret(position, extend); }
        finally { _portableMaskedApplyingCaret = false; }
    }

    private bool TryMovePortableMaskedCaret(Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        if (key is not (Keys.Left or Keys.Right) || (keyData & Keys.Modifiers & ~Keys.Shift) != Keys.None)
            return false;
        if (AcquirePortableMaskedInputLayout() is not { } use) return false;
        uint selectionVersion = _portableMaskedSelectionVersion;
        Exception? failure = null;
        try
        {
            bool extend = (keyData & Keys.Shift) != Keys.None;
            LibreTextCaret next;
            if (!extend && SelectionLength != 0)
            {
                LibreTextCaret first = use.Layout.GetCaret(SelectionStart);
                if (!use.IsCurrent || selectionVersion != _portableMaskedSelectionVersion) return true;
                LibreTextCaret last = use.Layout.GetCaret(SelectionStart + SelectionLength, true);
                next = (key == Keys.Left) == (first.Position.X <= last.Position.X) ? first : last;
            }
            else next = use.Layout.MoveCaret(PortableSelectionActiveEnd, _portableMaskedTrailing, key == Keys.Left ? -1 : 1);
            if (use.IsCurrent && selectionVersion == _portableMaskedSelectionVersion)
                ApplyPortableMaskedCaret(next.TextPosition, next.IsTrailing, extend);
            return true;
        }
        catch (Exception error) { failure = error; throw; }
        finally { use.Release(failure); }
    }

    internal override bool ProcessPortableMouseDownDefault(MouseEventArgs e, in PortablePointerDispatchContext context)
    {
        if (e.Button != MouseButtons.Left) return true;
        if (!context.IsCurrent) return false;
        if (!Focused && !Focus()) return context.IsCurrent;
        if (!context.IsCurrent) return false;
        if (!ApplyPortableMaskedPointer(e, context, (ModifierKeys & Keys.Shift) != 0, out bool selected)) return false;
        if (selected)
        {
            _portableMaskedPress = context;
            _portableMaskedPointerSelecting = true;
        }

        return true;
    }

    internal override bool ProcessPortableMouseMoveDefault(MouseEventArgs e, in PortablePointerDispatchContext context)
    {
        if (_portableMaskedPointerSelecting && !_portableMaskedPress.IsCurrentPress)
        {
            _portableMaskedPointerSelecting = false;
            _portableMaskedPress = default;
        }

        if (!_portableMaskedPointerSelecting || !Capture || (e.Button & MouseButtons.Left) == 0) return true;
        return ApplyPortableMaskedPointer(e, context, true, out _);
    }

    private bool ApplyPortableMaskedPointer(MouseEventArgs e, in PortablePointerDispatchContext context,
        bool extend, out bool selected)
    {
        selected = false;
        uint selectionVersion = _portableMaskedSelectionVersion, focusVersion = _portableMaskedFocusVersion;
        PointF scroll = _portableMaskedLayout?.Scroll ?? default;
        Rectangle viewport = PortableMaskedViewport;
        if (AcquirePortableMaskedInputLayout() is not { } use)
            return context.IsCurrent && LibrePlatform.Current.TextRenderer is not ILibreTextLayoutService;
        Exception? failure = null;
        try
        {
            if (!context.IsCurrent || !use.IsCurrent || !Focused || selectionVersion != _portableMaskedSelectionVersion
                || focusVersion != _portableMaskedFocusVersion || scroll != _portableMaskedLayout!.Scroll) return false;
            LibreTextHit hit = use.Layout.HitTest(new(e.X - viewport.X + scroll.X, e.Y - viewport.Y + scroll.Y));
            if (!context.IsCurrent || !use.IsCurrent || !Focused || selectionVersion != _portableMaskedSelectionVersion
                || focusVersion != _portableMaskedFocusVersion || scroll != _portableMaskedLayout!.Scroll) return false;
            ApplyPortableMaskedCaret(hit.TextPosition, hit.IsTrailing, extend);
            if (!context.IsCurrent || !use.IsCurrent || !Focused
                || unchecked(selectionVersion + 1) != _portableMaskedSelectionVersion
                || focusVersion != _portableMaskedFocusVersion) return false;
            selected = true;
            return true;
        }
        catch (Exception error) { failure = error; throw; }
        finally { use.Release(failure); }
    }

    internal override void InvokePortableMouseUp(MouseEventArgs e, bool? nativeDoubleClick,
        bool nativeClickEligible, Func<bool>? isCurrentRelease)
    {
        _portableMaskedPointerSelecting = false;
        _portableMaskedPress = default;
        base.InvokePortableMouseUp(e, nativeDoubleClick, nativeClickEligible, isCurrentRelease);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        if (!Capture)
        {
            _portableMaskedPointerSelecting = false;
            _portableMaskedPress = default;
        }

        base.OnMouseCaptureChanged(e);
    }

    private protected override nint QueryPortableTextGeometry(uint message, nint wParam, nint lParam)
    {
        if (message is PInvokeCore.EM_LINEFROMCHAR or PInvokeCore.EM_LINEINDEX) return 0;
        Point point = new(PARAM.SignedLOWORD(lParam), PARAM.SignedHIWORD(lParam));
        if (message == PInvokeCore.EM_CHARFROMPOS && !PortableClientRectangle.Contains(point)) return -1;
        int length = GetPortableMaskedDisplay().Length;
        if (length == 0) return message == PInvokeCore.EM_CHARFROMPOS ? 0 : -1;
        if (LibrePlatform.Current.TextRenderer is not ILibreTextSourceGeometryService)
            throw new PlatformNotSupportedException("Masked text position queries require retained source geometry.");
        if (AcquirePortableMaskedInputLayout() is not { } use)
            throw new PlatformNotSupportedException("Masked text position queries require a retained layout.");
        Exception? failure = null;
        try
        {
            if (use.Layout is not ILibreTextSourceGeometry source)
                throw new InvalidOperationException("The text provider declared source geometry but returned a layout without it.");
            Rectangle viewport = PortableMaskedViewport;
            PointF scroll = _portableMaskedLayout!.Scroll;
            switch (message)
            {
                case PInvokeCore.EM_POSFROMCHAR:
                    int index = unchecked((int)wParam);
                    if ((uint)index >= (uint)length) return -1;
                    PointF position = source.GetSourcePositionPoint(index);
                    if (!use.IsCurrent) throw new InvalidOperationException("The masked display generation changed during its position query.");
                    return PARAM.FromPoint(Point.Truncate(new(position.X + viewport.X - scroll.X, 0)));
                case PInvokeCore.EM_CHARFROMPOS:
                    LibreTextHit hit = use.Layout.HitTest(new(point.X - viewport.X + scroll.X, point.Y - viewport.Y + scroll.Y));
                    if (!use.IsCurrent) throw new InvalidOperationException("The masked display generation changed during its hit query.");
                    return unchecked((int)(ushort)hit.TextPosition);
                default:
                    throw new InvalidOperationException("Unexpected masked source text geometry message.");
            }
        }
        catch (Exception error) { failure = error; throw; }
        finally { use.Release(failure); }
    }

    protected override void OnFontChanged(EventArgs e)
    {
        InvalidatePortableMaskedLayout();
        base.OnFontChanged(e);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _portableMaskedHandleRetiring = true;
        _portableMaskedPointerSelecting = false;
        _portableMaskedPress = default;
        Exception? failure = null;
        try { InvalidatePortableMaskedLayout(); }
        catch (Exception error) { failure = error; }
        try { _portableMaskedCaretTimer?.Dispose(); _portableMaskedCaretTimer = null; }
        catch (Exception error)
        {
            if (failure is null) failure = error;
            else PortableRetainedTextLayoutOwner.PreserveCleanup(failure, error);
        }

        try { base.OnHandleDestroyed(e); }
        catch (Exception error)
        {
            if (failure is null) failure = error;
            else PortableRetainedTextLayoutOwner.PreserveCleanup(failure, error);
        }

        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    protected override void Dispose(bool disposing)
    {
        Exception? failure = null;
        if (disposing)
        {
            _portableMaskedPointerSelecting = false;
            _portableMaskedPress = default;
            try { _portableMaskedLayout?.Close(); }
            catch (Exception error) { failure = error; }
            try { _portableMaskedCaretTimer?.Dispose(); _portableMaskedCaretTimer = null; }
            catch (Exception error)
            {
                if (failure is null) failure = error;
                else PortableRetainedTextLayoutOwner.PreserveCleanup(failure, error);
            }
        }

        try { base.Dispose(disposing); }
        catch (Exception error)
        {
            if (failure is null) failure = error;
            else PortableRetainedTextLayoutOwner.PreserveCleanup(failure, error);
        }

        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
#endif
