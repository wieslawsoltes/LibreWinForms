// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;

namespace LibreWinForms.Platform;

public enum LibreMessageBoxButtons
{
    OK,
    OKCancel,
    AbortRetryIgnore,
    YesNoCancel,
    YesNo,
    RetryCancel,
    CancelTryContinue,
}

public enum LibreMessageBoxIcon
{
    None,
    Error,
    Question,
    Warning,
    Information,
}

public enum LibreMessageBoxDefaultButton
{
    Button1,
    Button2,
    Button3,
    Button4,
}

[Flags]
public enum LibreMessageBoxOptions
{
    None = 0,
    DefaultDesktopOnly = 1,
    RightAlign = 2,
    RightToLeftReading = 4,
    ServiceNotification = 8,
}

public enum LibreMessageBoxResult
{
    None,
    OK,
    Cancel,
    Abort,
    Retry,
    Ignore,
    Yes,
    No,
    TryAgain,
    Continue,
}

/// <summary>Backend-neutral data for one synchronous message box.</summary>
public readonly record struct LibreMessageBoxRequest(
    string Text,
    string Caption,
    LibreMessageBoxButtons Buttons,
    LibreMessageBoxIcon Icon,
    LibreMessageBoxDefaultButton DefaultButton,
    LibreMessageBoxOptions Options,
    bool ShowHelp,
    LibreHandle Owner);

/// <summary>Displays modal messages without exposing native dialog handles or backend objects.</summary>
public interface ILibreMessageBoxService
{
    LibreMessageBoxResult Show(in LibreMessageBoxRequest request);
}

/// <summary>Explicit default for hosts that have not supplied portable modal dialogs.</summary>
public sealed class UnsupportedLibreMessageBoxService : ILibreMessageBoxService
{
    public static UnsupportedLibreMessageBoxService Instance { get; } = new();

    private UnsupportedLibreMessageBoxService()
    {
    }

    public LibreMessageBoxResult Show(in LibreMessageBoxRequest request)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable message boxes.");
}

/// <summary>
/// A managed modal message-box implementation built from the typed window, paint, text, monitor,
/// handle, input, and dispatcher contracts. The platform window remains a real backend window.
/// </summary>
public sealed class ManagedLibreMessageBoxService : ILibreMessageBoxService
{
    private readonly ILibreDispatcher _dispatcher;
    private readonly ILibreHandleRegistry _handles;
    private readonly ILibreWindowService _windows;
    private readonly ILibreMonitorService _monitors;
    private readonly ILibrePaintService _painting;
    private readonly ILibreTextRendererService _text;

    public ManagedLibreMessageBoxService(
        ILibreDispatcher dispatcher,
        ILibreHandleRegistry handles,
        ILibreWindowService windows,
        ILibreMonitorService monitors,
        ILibrePaintService painting,
        ILibreTextRendererService text)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _handles = handles ?? throw new ArgumentNullException(nameof(handles));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
        _painting = painting ?? throw new ArgumentNullException(nameof(painting));
        _text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public LibreMessageBoxResult Show(in LibreMessageBoxRequest request)
    {
        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException("Message boxes must be shown on the owning dispatcher thread.");
        }

        Validate(request);
        if (request.ShowHelp)
        {
            throw new PlatformNotSupportedException(
                "Portable message-box help requires a registered local-OS help launcher.");
        }

        using var session = new Session(
            _dispatcher,
            _handles,
            _windows,
            _monitors,
            _painting,
            _text,
            request);
        return session.Show();
    }

    private static void Validate(in LibreMessageBoxRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Text);
        ArgumentNullException.ThrowIfNull(request.Caption);
        if (!Enum.IsDefined(request.Buttons))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Buttons, "Unknown message-box button set.");
        }

        if (!Enum.IsDefined(request.Icon))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Icon, "Unknown message-box icon.");
        }

        if (!Enum.IsDefined(request.DefaultButton))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.DefaultButton, "Unknown default button.");
        }

        const LibreMessageBoxOptions supported = LibreMessageBoxOptions.DefaultDesktopOnly
            | LibreMessageBoxOptions.RightAlign
            | LibreMessageBoxOptions.RightToLeftReading
            | LibreMessageBoxOptions.ServiceNotification;
        if ((request.Options & ~supported) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Options, "Unknown message-box option.");
        }
    }

    private sealed class Session : ILibreWindowEvents, IDisposable
    {
        private const int OuterMargin = 20;
        private const int ContentGap = 16;
        private const int IconExtent = 32;
        private const int ButtonWidth = 88;
        private const int ButtonHeight = 30;
        private const int ButtonGap = 8;
        private const int MinimumWidth = 280;
        private const int MaximumWidth = 640;
        private const int MinimumHeight = 140;
        private readonly ILibreDispatcher _dispatcher;
        private readonly ILibreHandleRegistry _handles;
        private readonly ILibreWindowService _windows;
        private readonly ILibreMonitorService _monitors;
        private readonly ILibrePaintService _painting;
        private readonly ILibreTextRendererService _text;
        private readonly LibreMessageBoxRequest _request;
        private readonly List<ButtonModel> _buttons;
        private ILibreWindow? _window;
        private Rectangle _textBounds;
        private Rectangle _iconBounds;
        private int _selectedIndex;
        private int _pressedIndex = -1;
        private LibreMessageBoxResult _result;
        private bool _closed;

        internal Session(
            ILibreDispatcher dispatcher,
            ILibreHandleRegistry handles,
            ILibreWindowService windows,
            ILibreMonitorService monitors,
            ILibrePaintService painting,
            ILibreTextRendererService text,
            in LibreMessageBoxRequest request)
        {
            _dispatcher = dispatcher;
            _handles = handles;
            _windows = windows;
            _monitors = monitors;
            _painting = painting;
            _text = text;
            _request = request;
            _buttons = CreateButtons(request.Buttons);
            _selectedIndex = Math.Min((int)request.DefaultButton, _buttons.Count - 1);
        }

        internal LibreMessageBoxResult Show()
        {
            LibreRectangle bounds = CalculateWindowBounds();
            Layout(bounds.Width, bounds.Height);
            LibreMessageBoxResult closeResult = GetCloseResult();
            var options = new LibreWindowCreateOptions(
                _request.Caption,
                bounds,
                LibreWindowOptions.Decorated | LibreWindowOptions.ToolWindow,
                _request.Owner,
                LibreWindowCoordinateMode.Logical,
                InitialDpiScale: 1d,
                InitialState: LibreWindowState.Normal,
                ShowInTaskbar: false,
                CanMinimize: false,
                CanMaximize: false,
                MinimumSize: new LibreSize(bounds.Width, bounds.Height),
                MaximumSize: new LibreSize(bounds.Width, bounds.Height),
                CanClose: closeResult != LibreMessageBoxResult.None);
            _window = _windows.Create(options, this);
            _window.Show();
            _painting.InvalidateAll(_window.Handle);
            _window.Activate();
            _dispatcher.RunNested(
                () => !_closed && _result == LibreMessageBoxResult.None,
                CancellationToken.None);
            return _result != LibreMessageBoxResult.None ? _result : closeResult;
        }

        public bool Closing()
        {
            if (_result != LibreMessageBoxResult.None)
            {
                return true;
            }

            LibreMessageBoxResult closeResult = GetCloseResult();
            if (closeResult == LibreMessageBoxResult.None)
            {
                return false;
            }

            _result = closeResult;
            return true;
        }

        public void Closed() => _closed = true;

        public void BoundsChanged(LibreRectangle bounds)
        {
            Layout(bounds.Width, bounds.Height);
            Invalidate();
        }

        public void StateChanged(LibreWindowState state)
        {
            _ = state;
        }

        public void PresentationScaleChanged(double scale)
        {
            _ = scale;
            Invalidate();
        }

        public void PaintRequested(ILibrePaintFrame frame)
        {
            Graphics graphics = frame.Graphics;
            using var background = new SolidBrush(SystemColors.Control);
            graphics.FillRectangle(background, 0, 0, frame.SurfaceBounds.Width, frame.SurfaceBounds.Height);
            DrawIcon(graphics);
            LibreTextFormat textFormat = LibreTextFormat.WordBreak | LibreTextFormat.NoPrefix;
            if (_request.Options.HasFlag(LibreMessageBoxOptions.RightAlign))
            {
                textFormat |= LibreTextFormat.Right;
            }

            if (_request.Options.HasFlag(LibreMessageBoxOptions.RightToLeftReading))
            {
                textFormat |= LibreTextFormat.RightToLeft;
            }

            _text.DrawText(
                graphics,
                _request.Text,
                font: null,
                _textBounds,
                SystemColors.ControlText,
                Color.Transparent,
                textFormat);
            for (int index = 0; index < _buttons.Count; index++)
            {
                DrawButton(graphics, _buttons[index], index == _selectedIndex, index == _pressedIndex);
            }
        }

        public void Input(in LibreInputEvent inputEvent)
        {
            switch (inputEvent.Kind)
            {
                case LibreInputEventKind.KeyDown:
                    HandleKeyDown(inputEvent.Key, inputEvent.Modifiers);
                    break;
                case LibreInputEventKind.PointerDown when inputEvent.Button == LibrePointerButton.Primary:
                    _pressedIndex = HitTest(inputEvent.Position);
                    if (_pressedIndex >= 0)
                    {
                        SetSelectedIndex(_pressedIndex);
                    }

                    Invalidate();
                    break;
                case LibreInputEventKind.PointerUp when inputEvent.Button == LibrePointerButton.Primary:
                    int releasedIndex = HitTest(inputEvent.Position);
                    int pressedIndex = _pressedIndex;
                    _pressedIndex = -1;
                    Invalidate();
                    if (pressedIndex >= 0 && releasedIndex == pressedIndex)
                    {
                        Complete(_buttons[pressedIndex].Result);
                    }

                    break;
            }
        }

        public void Dispose()
        {
            _window?.Dispose();
            _window = null;
        }

        private void HandleKeyDown(LibreKey key, LibreInputModifiers modifiers)
        {
            _ = modifiers;
            switch (key)
            {
                case LibreKey.Enter:
                case LibreKey.NumPadEnter:
                case LibreKey.Space:
                    Complete(_buttons[_selectedIndex].Result);
                    break;
                case LibreKey.Escape:
                    LibreMessageBoxResult closeResult = GetCloseResult();
                    if (closeResult != LibreMessageBoxResult.None)
                    {
                        Complete(closeResult);
                    }

                    break;
                case LibreKey.Left:
                    MoveSelection(_request.Options.HasFlag(LibreMessageBoxOptions.RightToLeftReading) ? 1 : -1);
                    break;
                case LibreKey.Right:
                case LibreKey.Tab:
                    MoveSelection(_request.Options.HasFlag(LibreMessageBoxOptions.RightToLeftReading) ? -1 : 1);
                    break;
            }
        }

        private void MoveSelection(int delta)
        {
            int count = _buttons.Count;
            SetSelectedIndex((_selectedIndex + delta + count) % count);
        }

        private void SetSelectedIndex(int value)
        {
            if (_selectedIndex == value)
            {
                return;
            }

            _selectedIndex = value;
            Invalidate();
        }

        private void Complete(LibreMessageBoxResult result)
        {
            if (_result != LibreMessageBoxResult.None)
            {
                return;
            }

            _result = result;
            _window?.Close();
        }

        private LibreRectangle CalculateWindowBounds()
        {
            IReadOnlyList<LibreMonitor> monitors = _monitors.GetMonitors();
            if (monitors.Count == 0)
            {
                throw new InvalidOperationException("The platform monitor inventory is empty.");
            }

            LibreRectangle anchor;
            LibreMonitor monitor;
            if (!_request.Owner.IsNull && _handles.TryGet(_request.Owner, out ILibreWindow? owner))
            {
                anchor = owner.Bounds;
                monitor = _monitors.GetNearest(anchor);
            }
            else
            {
                monitor = monitors.FirstOrDefault(static item => item.IsPrimary);
                if (string.IsNullOrEmpty(monitor.Id))
                {
                    monitor = monitors[0];
                }

                anchor = monitor.WorkArea;
            }

            int maximumWidth = Math.Max(MinimumWidth, Math.Min(MaximumWidth, monitor.WorkArea.Width - 40));
            int contentWidthLimit = Math.Max(120, maximumWidth - (2 * OuterMargin)
                - (_request.Icon == LibreMessageBoxIcon.None ? 0 : IconExtent + ContentGap));
            Size measured = _text.MeasureText(
                graphics: null,
                _request.Text,
                font: null,
                new Size(contentWidthLimit, int.MaxValue),
                LibreTextFormat.WordBreak | LibreTextFormat.NoPrefix
                    | (_request.Options.HasFlag(LibreMessageBoxOptions.RightToLeftReading)
                        ? LibreTextFormat.RightToLeft
                        : LibreTextFormat.Default));
            int iconAndTextWidth = measured.Width
                + (_request.Icon == LibreMessageBoxIcon.None ? 0 : IconExtent + ContentGap);
            int buttonsWidth = (_buttons.Count * ButtonWidth) + ((_buttons.Count - 1) * ButtonGap);
            int width = Math.Clamp(
                Math.Max(iconAndTextWidth, buttonsWidth) + (2 * OuterMargin),
                Math.Min(MinimumWidth, maximumWidth),
                maximumWidth);
            int actualTextWidth = width - (2 * OuterMargin)
                - (_request.Icon == LibreMessageBoxIcon.None ? 0 : IconExtent + ContentGap);
            measured = _text.MeasureText(
                graphics: null,
                _request.Text,
                font: null,
                new Size(Math.Max(1, actualTextWidth), int.MaxValue),
                LibreTextFormat.WordBreak | LibreTextFormat.NoPrefix
                    | (_request.Options.HasFlag(LibreMessageBoxOptions.RightToLeftReading)
                        ? LibreTextFormat.RightToLeft
                        : LibreTextFormat.Default));
            int contentHeight = Math.Max(measured.Height, _request.Icon == LibreMessageBoxIcon.None ? 0 : IconExtent);
            int maximumHeight = Math.Max(MinimumHeight, monitor.WorkArea.Height - 40);
            int height = Math.Clamp(
                contentHeight + ButtonHeight + (3 * OuterMargin),
                Math.Min(MinimumHeight, maximumHeight),
                maximumHeight);
            int x = anchor.X + ((anchor.Width - width) / 2);
            int y = anchor.Y + ((anchor.Height - height) / 2);
            x = Math.Clamp(x, monitor.WorkArea.X, Math.Max(monitor.WorkArea.X, monitor.WorkArea.Right - width));
            y = Math.Clamp(y, monitor.WorkArea.Y, Math.Max(monitor.WorkArea.Y, monitor.WorkArea.Bottom - height));
            return new LibreRectangle(x, y, width, height);
        }

        private void Layout(int width, int height)
        {
            bool hasIcon = _request.Icon != LibreMessageBoxIcon.None;
            int textX = OuterMargin + (hasIcon ? IconExtent + ContentGap : 0);
            int buttonsWidth = (_buttons.Count * ButtonWidth) + ((_buttons.Count - 1) * ButtonGap);
            int buttonsX = _request.Options.HasFlag(LibreMessageBoxOptions.RightToLeftReading)
                ? OuterMargin
                : width - OuterMargin - buttonsWidth;
            int buttonsY = height - OuterMargin - ButtonHeight;
            _iconBounds = hasIcon
                ? new Rectangle(OuterMargin, OuterMargin, IconExtent, IconExtent)
                : Rectangle.Empty;
            _textBounds = new Rectangle(
                textX,
                OuterMargin,
                Math.Max(0, width - textX - OuterMargin),
                Math.Max(0, buttonsY - (2 * OuterMargin)));
            for (int index = 0; index < _buttons.Count; index++)
            {
                int visualIndex = _request.Options.HasFlag(LibreMessageBoxOptions.RightToLeftReading)
                    ? _buttons.Count - 1 - index
                    : index;
                _buttons[index].Bounds = new Rectangle(
                    buttonsX + (visualIndex * (ButtonWidth + ButtonGap)),
                    buttonsY,
                    ButtonWidth,
                    ButtonHeight);
            }
        }

        private void DrawIcon(Graphics graphics)
        {
            if (_iconBounds.IsEmpty)
            {
                return;
            }

            Color fillColor = _request.Icon switch
            {
                LibreMessageBoxIcon.Error => Color.Firebrick,
                LibreMessageBoxIcon.Question => Color.RoyalBlue,
                LibreMessageBoxIcon.Warning => Color.Goldenrod,
                LibreMessageBoxIcon.Information => Color.DodgerBlue,
                _ => Color.Transparent,
            };
            using var fill = new SolidBrush(fillColor);
            using var outline = new Pen(Color.FromArgb(160, Color.Black));
            graphics.FillEllipse(fill, _iconBounds);
            graphics.DrawEllipse(outline, _iconBounds);
            if (_request.Icon == LibreMessageBoxIcon.Error)
            {
                using var mark = new Pen(Color.White, 3f);
                graphics.DrawLine(mark, _iconBounds.Left + 9, _iconBounds.Top + 9, _iconBounds.Right - 9, _iconBounds.Bottom - 9);
                graphics.DrawLine(mark, _iconBounds.Right - 9, _iconBounds.Top + 9, _iconBounds.Left + 9, _iconBounds.Bottom - 9);
                return;
            }

            string symbol = _request.Icon switch
            {
                LibreMessageBoxIcon.Question => "?",
                LibreMessageBoxIcon.Information => "i",
                _ => "!",
            };
            _text.DrawText(
                graphics,
                symbol,
                font: null,
                _iconBounds,
                _request.Icon == LibreMessageBoxIcon.Warning ? Color.Black : Color.White,
                Color.Transparent,
                LibreTextFormat.HorizontalCenter | LibreTextFormat.VerticalCenter | LibreTextFormat.SingleLine | LibreTextFormat.NoPrefix);
        }

        private void DrawButton(Graphics graphics, ButtonModel button, bool selected, bool pressed)
        {
            Color backgroundColor = pressed
                ? SystemColors.ControlDark
                : selected
                    ? SystemColors.Highlight
                    : SystemColors.ControlLight;
            Color foregroundColor = selected ? SystemColors.HighlightText : SystemColors.ControlText;
            using var background = new SolidBrush(backgroundColor);
            using var border = new Pen(selected ? SystemColors.Highlight : SystemColors.ControlDarkDark, selected ? 2f : 1f);
            graphics.FillRectangle(background, button.Bounds);
            graphics.DrawRectangle(border, button.Bounds);
            _text.DrawText(
                graphics,
                button.Text,
                font: null,
                button.Bounds,
                foregroundColor,
                Color.Transparent,
                LibreTextFormat.HorizontalCenter | LibreTextFormat.VerticalCenter | LibreTextFormat.SingleLine | LibreTextFormat.NoPrefix);
        }

        private int HitTest(LibrePoint point)
        {
            for (int index = 0; index < _buttons.Count; index++)
            {
                if (_buttons[index].Bounds.Contains(point.X, point.Y))
                {
                    return index;
                }
            }

            return -1;
        }

        private void Invalidate()
        {
            if (_window is not null)
            {
                _painting.InvalidateAll(_window.Handle);
            }
        }

        private LibreMessageBoxResult GetCloseResult()
            => _request.Buttons switch
            {
                LibreMessageBoxButtons.OK => LibreMessageBoxResult.OK,
                LibreMessageBoxButtons.OKCancel
                    or LibreMessageBoxButtons.YesNoCancel
                    or LibreMessageBoxButtons.RetryCancel
                    or LibreMessageBoxButtons.CancelTryContinue => LibreMessageBoxResult.Cancel,
                _ => LibreMessageBoxResult.None,
            };

        private static List<ButtonModel> CreateButtons(LibreMessageBoxButtons buttons)
            => buttons switch
            {
                LibreMessageBoxButtons.OK => [new("OK", LibreMessageBoxResult.OK)],
                LibreMessageBoxButtons.OKCancel =>
                    [new("OK", LibreMessageBoxResult.OK), new("Cancel", LibreMessageBoxResult.Cancel)],
                LibreMessageBoxButtons.AbortRetryIgnore =>
                    [new("Abort", LibreMessageBoxResult.Abort), new("Retry", LibreMessageBoxResult.Retry), new("Ignore", LibreMessageBoxResult.Ignore)],
                LibreMessageBoxButtons.YesNoCancel =>
                    [new("Yes", LibreMessageBoxResult.Yes), new("No", LibreMessageBoxResult.No), new("Cancel", LibreMessageBoxResult.Cancel)],
                LibreMessageBoxButtons.YesNo =>
                    [new("Yes", LibreMessageBoxResult.Yes), new("No", LibreMessageBoxResult.No)],
                LibreMessageBoxButtons.RetryCancel =>
                    [new("Retry", LibreMessageBoxResult.Retry), new("Cancel", LibreMessageBoxResult.Cancel)],
                LibreMessageBoxButtons.CancelTryContinue =>
                    [new("Cancel", LibreMessageBoxResult.Cancel), new("Try Again", LibreMessageBoxResult.TryAgain), new("Continue", LibreMessageBoxResult.Continue)],
                _ => throw new ArgumentOutOfRangeException(nameof(buttons)),
            };

        private sealed class ButtonModel(string text, LibreMessageBoxResult result)
        {
            internal string Text { get; } = text;

            internal LibreMessageBoxResult Result { get; } = result;

            internal Rectangle Bounds { get; set; }
        }
    }
}
