// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Globalization;

namespace LibreWinForms.Platform;

[Flags]
public enum LibreColorDialogOptions
{
    None = 0,
    AllowFullOpen = 1,
    AnyColor = 2,
    FullOpen = 4,
    ShowHelp = 8,
    SolidColorOnly = 16,
}

/// <summary>Backend-neutral state for one canonical color-selection session.</summary>
public readonly record struct LibreColorDialogRequest(
    Color Color,
    IReadOnlyList<Color> CustomColors,
    LibreColorDialogOptions Options,
    Action? HelpRequested,
    LibreHandle Owner);

/// <summary>Result and caller-owned color snapshots returned by a color-selection service.</summary>
public readonly record struct LibreColorDialogResult(
    bool Accepted,
    Color Color,
    IReadOnlyList<Color> CustomColors);

/// <summary>Selects a color without exposing common-dialog structures or native handles.</summary>
public interface ILibreColorDialogService
{
    LibreColorDialogResult Show(in LibreColorDialogRequest request);
}

/// <summary>Explicit default for hosts that have not supplied portable color selection.</summary>
public sealed class UnsupportedLibreColorDialogService : ILibreColorDialogService
{
    public static UnsupportedLibreColorDialogService Instance { get; } = new();

    private UnsupportedLibreColorDialogService()
    {
    }

    public LibreColorDialogResult Show(in LibreColorDialogRequest request)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable color dialogs.");
}

/// <summary>
/// Managed color selection built from typed window, monitor, paint, text, input, handle, and
/// dispatcher contracts. The dialog is a real backend window and returns owned state snapshots.
/// </summary>
public sealed class ManagedLibreColorDialogService : ILibreColorDialogService
{
    private readonly ILibreDispatcher _dispatcher;
    private readonly ILibreHandleRegistry _handles;
    private readonly ILibreWindowService _windows;
    private readonly ILibreMonitorService _monitors;
    private readonly ILibrePaintService _painting;
    private readonly ILibreTextRendererService _text;

    public ManagedLibreColorDialogService(
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

    public LibreColorDialogResult Show(in LibreColorDialogRequest request)
    {
        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException("Color dialogs must be shown on the owning dispatcher thread.");
        }

        Validate(request);
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

    private static void Validate(in LibreColorDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.CustomColors);
        if (request.CustomColors.Count > 16)
        {
            throw new ArgumentException("A color dialog accepts at most 16 custom colors.", nameof(request));
        }

        const LibreColorDialogOptions supported = LibreColorDialogOptions.AllowFullOpen
            | LibreColorDialogOptions.AnyColor
            | LibreColorDialogOptions.FullOpen
            | LibreColorDialogOptions.ShowHelp
            | LibreColorDialogOptions.SolidColorOnly;
        if ((request.Options & ~supported) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Options, "Unknown color-dialog option.");
        }

        if (request.Options.HasFlag(LibreColorDialogOptions.FullOpen)
            && !request.Options.HasFlag(LibreColorDialogOptions.AllowFullOpen))
        {
            throw new ArgumentException("FullOpen requires AllowFullOpen.", nameof(request));
        }
    }

    private sealed class Session : ILibreWindowEvents, IDisposable
    {
        private const int WindowWidth = 560;
        private const int CollapsedHeight = 410;
        private const int ExpandedHeight = 500;
        private const int CellSize = 28;
        private const int CellGap = 5;
        private const int PaletteColumns = 8;
        private const int StandardRows = 6;
        private const int ButtonWidth = 88;
        private const int ButtonHeight = 30;
        private const int ButtonGap = 8;
        private static readonly Color[] s_standardColors = CreateStandardColors();
        private readonly ILibreDispatcher _dispatcher;
        private readonly ILibreHandleRegistry _handles;
        private readonly ILibreWindowService _windows;
        private readonly ILibreMonitorService _monitors;
        private readonly ILibrePaintService _painting;
        private readonly ILibreTextRendererService _text;
        private readonly LibreColorDialogRequest _request;
        private readonly Color[] _customColors = new Color[16];
        private ILibreWindow? _window;
        private Rectangle _standardBounds;
        private Rectangle _customBounds;
        private Rectangle _previewBounds;
        private Rectangle _hexBounds;
        private Rectangle _addCustomBounds;
        private Rectangle _expandBounds;
        private Rectangle _helpBounds;
        private Rectangle _okBounds;
        private Rectangle _cancelBounds;
        private Color _selectedColor;
        private int _selectedCell;
        private int _nextCustomIndex;
        private FocusTarget _focus = FocusTarget.Palette;
        private FocusTarget _pressed = FocusTarget.None;
        private string _hexText;
        private bool _replaceHexOnInput = true;
        private bool _expanded;
        private bool _closed;
        private bool _accepted;

        internal Session(
            ILibreDispatcher dispatcher,
            ILibreHandleRegistry handles,
            ILibreWindowService windows,
            ILibreMonitorService monitors,
            ILibrePaintService painting,
            ILibreTextRendererService text,
            in LibreColorDialogRequest request)
        {
            _dispatcher = dispatcher;
            _handles = handles;
            _windows = windows;
            _monitors = monitors;
            _painting = painting;
            _text = text;
            _request = request;
            _selectedColor = request.Color.IsEmpty ? Color.Black : request.Color;
            _hexText = ToHex(_selectedColor);
            _expanded = request.Options.HasFlag(LibreColorDialogOptions.FullOpen);
            Array.Fill(_customColors, Color.White);
            for (int index = 0; index < request.CustomColors.Count; index++)
            {
                Color color = request.CustomColors[index];
                _customColors[index] = color.IsEmpty ? Color.White : Color.FromArgb(color.R, color.G, color.B);
            }

            _selectedCell = FindColor(_selectedColor);
        }

        internal LibreColorDialogResult Show()
        {
            LibreRectangle bounds = CalculateWindowBounds(CurrentHeight);
            Layout(bounds.Width, bounds.Height);
            LibreSize fixedSize = new(bounds.Width, bounds.Height);
            var options = new LibreWindowCreateOptions(
                "Color",
                bounds,
                LibreWindowOptions.Decorated | LibreWindowOptions.ToolWindow,
                _request.Owner,
                LibreWindowCoordinateMode.Logical,
                InitialDpiScale: 1d,
                InitialState: LibreWindowState.Normal,
                ShowInTaskbar: false,
                CanMinimize: false,
                CanMaximize: false,
                MinimumSize: fixedSize,
                MaximumSize: fixedSize,
                CanClose: true);
            _window = _windows.Create(options, this);
            _window.Show();
            _painting.InvalidateAll(_window.Handle);
            _window.Activate();
            _dispatcher.RunNested(() => !_closed, CancellationToken.None);
            return new LibreColorDialogResult(
                _accepted,
                _selectedColor,
                (Color[])_customColors.Clone());
        }

        public bool Closing() => true;

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
            DrawLabel(graphics, "Basic colors", new Rectangle(20, 14, 272, 24));
            DrawPalette(graphics, _standardBounds, s_standardColors, startIndex: 0);
            DrawLabel(graphics, "Custom colors", new Rectangle(20, 255, 272, 24));
            DrawPalette(graphics, _customBounds, _customColors, s_standardColors.Length);
            DrawPreview(graphics);
            if (_expanded)
            {
                DrawLabel(graphics, "HTML color", new Rectangle(320, 148, 200, 22));
                DrawTextField(graphics, _hexBounds, $"#{_hexText}", _focus == FocusTarget.Hex);
                DrawButton(graphics, _addCustomBounds, "Add Custom", FocusTarget.AddCustom);
            }

            if (_request.Options.HasFlag(LibreColorDialogOptions.AllowFullOpen))
            {
                DrawButton(
                    graphics,
                    _expandBounds,
                    _expanded ? "Basic Colors" : "Define Custom",
                    FocusTarget.Expand);
            }

            if (_request.Options.HasFlag(LibreColorDialogOptions.ShowHelp))
            {
                DrawButton(graphics, _helpBounds, "Help", FocusTarget.Help);
            }

            DrawButton(graphics, _okBounds, "OK", FocusTarget.OK);
            DrawButton(graphics, _cancelBounds, "Cancel", FocusTarget.Cancel);
        }

        public void Input(in LibreInputEvent inputEvent)
        {
            switch (inputEvent.Kind)
            {
                case LibreInputEventKind.KeyDown:
                    HandleKeyDown(inputEvent.Key, inputEvent.Modifiers);
                    break;
                case LibreInputEventKind.TextInput:
                    HandleTextInput(inputEvent.Text);
                    break;
                case LibreInputEventKind.PointerDown when inputEvent.Button == LibrePointerButton.Primary:
                    HandlePointerDown(inputEvent.Position);
                    break;
                case LibreInputEventKind.PointerUp when inputEvent.Button == LibrePointerButton.Primary:
                    HandlePointerUp(inputEvent.Position);
                    break;
            }
        }

        public void Dispose()
        {
            _window?.Dispose();
            _window = null;
        }

        private int CurrentHeight => _expanded ? ExpandedHeight : CollapsedHeight;

        private void HandleKeyDown(LibreKey key, LibreInputModifiers modifiers)
        {
            switch (key)
            {
                case LibreKey.Tab:
                    MoveFocus(modifiers.HasFlag(LibreInputModifiers.Shift) ? -1 : 1);
                    break;
                case LibreKey.Left when _focus == FocusTarget.Palette:
                    SelectCell(_selectedCell - 1);
                    break;
                case LibreKey.Right when _focus == FocusTarget.Palette:
                    SelectCell(_selectedCell + 1);
                    break;
                case LibreKey.Up when _focus == FocusTarget.Palette:
                    SelectCell(_selectedCell - PaletteColumns);
                    break;
                case LibreKey.Down when _focus == FocusTarget.Palette:
                    SelectCell(_selectedCell + PaletteColumns);
                    break;
                case LibreKey.Backspace when _focus == FocusTarget.Hex && _expanded:
                    if (_hexText.Length > 0)
                    {
                        _hexText = _hexText[..^1];
                        _replaceHexOnInput = false;
                        Invalidate();
                    }

                    break;
                case LibreKey.Enter:
                case LibreKey.NumPadEnter:
                case LibreKey.Space:
                    ActivateFocusedTarget();
                    break;
                case LibreKey.Escape:
                    Complete(accepted: false);
                    break;
            }
        }

        private void HandleTextInput(string? text)
        {
            if (!_expanded || string.IsNullOrEmpty(text))
            {
                return;
            }

            char[] filtered = new char[text.Length];
            int filteredLength = 0;
            foreach (char character in text)
            {
                if (Uri.IsHexDigit(character))
                {
                    filtered[filteredLength++] = character;
                }
            }

            if (filteredLength == 0)
            {
                return;
            }

            if (_focus != FocusTarget.Hex || _replaceHexOnInput)
            {
                _focus = FocusTarget.Hex;
                _hexText = string.Empty;
                _replaceHexOnInput = false;
            }

            int available = 6 - _hexText.Length;
            if (available > 0)
            {
                _hexText += new string(filtered, 0, Math.Min(filteredLength, available)).ToUpperInvariant();
                if (_hexText.Length == 6)
                {
                    ApplyHexColor();
                }

                Invalidate();
            }
        }

        private void HandlePointerDown(LibrePoint point)
        {
            int cell = HitTestPalette(point);
            if (cell >= 0)
            {
                _focus = FocusTarget.Palette;
                SelectCell(cell);
                return;
            }

            _pressed = HitTestTarget(point);
            if (_pressed != FocusTarget.None)
            {
                _focus = _pressed;
                Invalidate();
            }
        }

        private void HandlePointerUp(LibrePoint point)
        {
            FocusTarget released = HitTestTarget(point);
            FocusTarget pressed = _pressed;
            _pressed = FocusTarget.None;
            Invalidate();
            if (pressed != FocusTarget.None && pressed == released)
            {
                ActivateTarget(pressed);
            }
        }

        private void ActivateFocusedTarget()
        {
            if (_focus == FocusTarget.Palette)
            {
                Complete(accepted: true);
                return;
            }

            ActivateTarget(_focus);
        }

        private void ActivateTarget(FocusTarget target)
        {
            switch (target)
            {
                case FocusTarget.Hex:
                    ApplyHexColor();
                    break;
                case FocusTarget.AddCustom:
                    ApplyHexColor();
                    _customColors[_nextCustomIndex] = _selectedColor;
                    _selectedCell = s_standardColors.Length + _nextCustomIndex;
                    _nextCustomIndex = (_nextCustomIndex + 1) % _customColors.Length;
                    Invalidate();
                    break;
                case FocusTarget.Expand:
                    SetExpanded(!_expanded);
                    break;
                case FocusTarget.Help:
                    _request.HelpRequested?.Invoke();
                    break;
                case FocusTarget.OK:
                    ApplyHexColor();
                    Complete(accepted: true);
                    break;
                case FocusTarget.Cancel:
                    Complete(accepted: false);
                    break;
            }
        }

        private void MoveFocus(int delta)
        {
            FocusTarget[] targets = GetFocusTargets();
            int index = Array.IndexOf(targets, _focus);
            if (index < 0)
            {
                index = 0;
            }

            _focus = targets[(index + delta + targets.Length) % targets.Length];
            _replaceHexOnInput = _focus == FocusTarget.Hex;
            Invalidate();
        }

        private FocusTarget[] GetFocusTargets()
        {
            var targets = new List<FocusTarget> { FocusTarget.Palette };
            if (_expanded)
            {
                targets.Add(FocusTarget.Hex);
                targets.Add(FocusTarget.AddCustom);
            }

            if (_request.Options.HasFlag(LibreColorDialogOptions.AllowFullOpen))
            {
                targets.Add(FocusTarget.Expand);
            }

            if (_request.Options.HasFlag(LibreColorDialogOptions.ShowHelp))
            {
                targets.Add(FocusTarget.Help);
            }

            targets.Add(FocusTarget.OK);
            targets.Add(FocusTarget.Cancel);
            return [.. targets];
        }

        private void SelectCell(int cell)
        {
            int count = s_standardColors.Length + _customColors.Length;
            _selectedCell = Math.Clamp(cell, 0, count - 1);
            _selectedColor = _selectedCell < s_standardColors.Length
                ? s_standardColors[_selectedCell]
                : _customColors[_selectedCell - s_standardColors.Length];
            _hexText = ToHex(_selectedColor);
            _replaceHexOnInput = true;
            Invalidate();
        }

        private void ApplyHexColor()
        {
            if (_hexText.Length != 6
                || !int.TryParse(_hexText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            {
                return;
            }

            _selectedColor = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            _selectedCell = FindColor(_selectedColor);
            _replaceHexOnInput = true;
            Invalidate();
        }

        private void SetExpanded(bool expanded)
        {
            if (expanded && !_request.Options.HasFlag(LibreColorDialogOptions.AllowFullOpen))
            {
                return;
            }

            _expanded = expanded;
            if (!_expanded && (_focus == FocusTarget.Hex || _focus == FocusTarget.AddCustom))
            {
                _focus = FocusTarget.Palette;
            }

            if (_window is not null)
            {
                LibreRectangle bounds = _window.Bounds;
                LibreMonitor monitor = _monitors.GetNearest(bounds);
                int width = Math.Min(WindowWidth, Math.Max(1, monitor.WorkArea.Width));
                int height = Math.Min(CurrentHeight, Math.Max(1, monitor.WorkArea.Height));
                _window.SetSizeConstraints(new LibreSize(width, height), new LibreSize(width, height));
                _window.Bounds = bounds with { Width = width, Height = height };
                Layout(width, height);
            }

            Invalidate();
        }

        private void Complete(bool accepted)
        {
            _accepted = accepted;
            _window?.Close();
        }

        private LibreRectangle CalculateWindowBounds(int height)
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

            int width = Math.Min(WindowWidth, Math.Max(1, monitor.WorkArea.Width));
            height = Math.Min(height, Math.Max(1, monitor.WorkArea.Height));
            int x = anchor.X + ((anchor.Width - width) / 2);
            int y = anchor.Y + ((anchor.Height - height) / 2);
            x = Math.Clamp(x, monitor.WorkArea.X, Math.Max(monitor.WorkArea.X, monitor.WorkArea.Right - width));
            y = Math.Clamp(y, monitor.WorkArea.Y, Math.Max(monitor.WorkArea.Y, monitor.WorkArea.Bottom - height));
            return new LibreRectangle(x, y, width, height);
        }

        private void Layout(int width, int height)
        {
            _standardBounds = new Rectangle(20, 42, PaletteColumns * (CellSize + CellGap), StandardRows * (CellSize + CellGap));
            _customBounds = new Rectangle(20, 280, PaletteColumns * (CellSize + CellGap), 2 * (CellSize + CellGap));
            _previewBounds = new Rectangle(320, 42, Math.Max(1, width - 340), 88);
            _hexBounds = new Rectangle(320, 174, Math.Max(1, width - 340), 32);
            _addCustomBounds = new Rectangle(320, 218, 112, ButtonHeight);
            _expandBounds = new Rectangle(320, _expanded ? 268 : 174, 112, ButtonHeight);
            int buttonY = height - 50;
            _cancelBounds = new Rectangle(width - 20 - ButtonWidth, buttonY, ButtonWidth, ButtonHeight);
            _okBounds = new Rectangle(_cancelBounds.Left - ButtonGap - ButtonWidth, buttonY, ButtonWidth, ButtonHeight);
            _helpBounds = new Rectangle(20, buttonY, ButtonWidth, ButtonHeight);
        }

        private void DrawPalette(Graphics graphics, Rectangle bounds, Color[] colors, int startIndex)
        {
            for (int index = 0; index < colors.Length; index++)
            {
                int row = index / PaletteColumns;
                int column = index % PaletteColumns;
                var cell = new Rectangle(
                    bounds.X + (column * (CellSize + CellGap)),
                    bounds.Y + (row * (CellSize + CellGap)),
                    CellSize,
                    CellSize);
                using var fill = new SolidBrush(colors[index]);
                using var border = new Pen(SystemColors.ControlDarkDark);
                graphics.FillRectangle(fill, cell);
                graphics.DrawRectangle(border, cell);
                if (_selectedCell == startIndex + index)
                {
                    using var selection = new Pen(SystemColors.Highlight, 3f);
                    graphics.DrawRectangle(selection, Rectangle.Inflate(cell, 2, 2));
                }
            }
        }

        private void DrawPreview(Graphics graphics)
        {
            using var fill = new SolidBrush(_selectedColor);
            using var border = new Pen(SystemColors.ControlDarkDark, 2f);
            graphics.FillRectangle(fill, _previewBounds);
            graphics.DrawRectangle(border, _previewBounds);
            DrawLabel(
                graphics,
                $"R {_selectedColor.R}   G {_selectedColor.G}   B {_selectedColor.B}",
                new Rectangle(_previewBounds.X, _previewBounds.Bottom + 6, _previewBounds.Width, 22));
        }

        private void DrawLabel(Graphics graphics, string value, Rectangle bounds)
            => _text.DrawText(
                graphics,
                value,
                font: null,
                bounds,
                SystemColors.ControlText,
                Color.Transparent,
                LibreTextFormat.SingleLine | LibreTextFormat.VerticalCenter | LibreTextFormat.NoPrefix);

        private void DrawTextField(Graphics graphics, Rectangle bounds, string value, bool focused)
        {
            using var background = new SolidBrush(SystemColors.Window);
            using var border = new Pen(focused ? SystemColors.Highlight : SystemColors.ControlDarkDark, focused ? 2f : 1f);
            graphics.FillRectangle(background, bounds);
            graphics.DrawRectangle(border, bounds);
            _text.DrawText(
                graphics,
                value,
                font: null,
                Rectangle.Inflate(bounds, -6, -2),
                SystemColors.WindowText,
                Color.Transparent,
                LibreTextFormat.SingleLine | LibreTextFormat.VerticalCenter | LibreTextFormat.NoPrefix);
        }

        private void DrawButton(Graphics graphics, Rectangle bounds, string value, FocusTarget target)
        {
            bool focused = _focus == target;
            bool pressed = _pressed == target;
            Color backgroundColor = pressed
                ? SystemColors.ControlDark
                : focused
                    ? SystemColors.Highlight
                    : SystemColors.ControlLight;
            Color foregroundColor = focused ? SystemColors.HighlightText : SystemColors.ControlText;
            using var background = new SolidBrush(backgroundColor);
            using var border = new Pen(focused ? SystemColors.Highlight : SystemColors.ControlDarkDark, focused ? 2f : 1f);
            graphics.FillRectangle(background, bounds);
            graphics.DrawRectangle(border, bounds);
            _text.DrawText(
                graphics,
                value,
                font: null,
                bounds,
                foregroundColor,
                Color.Transparent,
                LibreTextFormat.HorizontalCenter | LibreTextFormat.VerticalCenter | LibreTextFormat.SingleLine | LibreTextFormat.NoPrefix);
        }

        private int HitTestPalette(LibrePoint point)
        {
            int standard = HitTestGrid(point, _standardBounds, s_standardColors.Length);
            if (standard >= 0)
            {
                return standard;
            }

            int custom = HitTestGrid(point, _customBounds, _customColors.Length);
            return custom < 0 ? -1 : s_standardColors.Length + custom;
        }

        private static int HitTestGrid(LibrePoint point, Rectangle bounds, int count)
        {
            if (!bounds.Contains(point.X, point.Y))
            {
                return -1;
            }

            int column = (point.X - bounds.X) / (CellSize + CellGap);
            int row = (point.Y - bounds.Y) / (CellSize + CellGap);
            int localX = (point.X - bounds.X) % (CellSize + CellGap);
            int localY = (point.Y - bounds.Y) % (CellSize + CellGap);
            int index = (row * PaletteColumns) + column;
            return localX < CellSize && localY < CellSize && index < count ? index : -1;
        }

        private FocusTarget HitTestTarget(LibrePoint point)
        {
            if (_expanded && _hexBounds.Contains(point.X, point.Y)) return FocusTarget.Hex;
            if (_expanded && _addCustomBounds.Contains(point.X, point.Y)) return FocusTarget.AddCustom;
            if (_request.Options.HasFlag(LibreColorDialogOptions.AllowFullOpen)
                && _expandBounds.Contains(point.X, point.Y)) return FocusTarget.Expand;
            if (_request.Options.HasFlag(LibreColorDialogOptions.ShowHelp)
                && _helpBounds.Contains(point.X, point.Y)) return FocusTarget.Help;
            if (_okBounds.Contains(point.X, point.Y)) return FocusTarget.OK;
            if (_cancelBounds.Contains(point.X, point.Y)) return FocusTarget.Cancel;
            return FocusTarget.None;
        }

        private void Invalidate()
        {
            if (_window is not null)
            {
                _painting.InvalidateAll(_window.Handle);
            }
        }

        private int FindColor(Color color)
        {
            int index = Array.FindIndex(s_standardColors, item => item.ToArgb() == color.ToArgb());
            if (index >= 0)
            {
                return index;
            }

            index = Array.FindIndex(_customColors, item => item.ToArgb() == color.ToArgb());
            return index < 0 ? -1 : s_standardColors.Length + index;
        }

        private static string ToHex(Color color)
            => $"{color.R:X2}{color.G:X2}{color.B:X2}";

        private static Color[] CreateStandardColors()
            =>
            [
                Color.Black, Color.DimGray, Color.Gray, Color.DarkGray, Color.Silver, Color.LightGray, Color.Gainsboro, Color.White,
                Color.Maroon, Color.DarkRed, Color.Red, Color.OrangeRed, Color.DarkOrange, Color.Orange, Color.Gold, Color.Yellow,
                Color.Olive, Color.OliveDrab, Color.Green, Color.ForestGreen, Color.LimeGreen, Color.Lime, Color.Chartreuse, Color.YellowGreen,
                Color.Teal, Color.DarkCyan, Color.CadetBlue, Color.Cyan, Color.LightCyan, Color.SkyBlue, Color.DeepSkyBlue, Color.DodgerBlue,
                Color.Navy, Color.DarkBlue, Color.Blue, Color.RoyalBlue, Color.SlateBlue, Color.BlueViolet, Color.Indigo, Color.Purple,
                Color.DarkMagenta, Color.Magenta, Color.DeepPink, Color.HotPink, Color.Pink, Color.Brown, Color.Sienna, Color.Tan,
            ];

        private enum FocusTarget
        {
            None,
            Palette,
            Hex,
            AddCustom,
            Expand,
            Help,
            OK,
            Cancel,
        }
    }
}
