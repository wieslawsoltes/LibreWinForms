// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Globalization;

namespace LibreWinForms.Platform;

[Flags]
public enum LibreFontDialogOptions
{
    None = 0,
    AllowSimulations = 1,
    AllowVectorFonts = 2,
    AllowVerticalFonts = 4,
    AllowScriptChange = 8,
    FixedPitchOnly = 16,
    FontMustExist = 32,
    ScriptsOnly = 64,
    ShowApply = 128,
    ShowColor = 256,
    ShowEffects = 512,
    ShowHelp = 1024,
}

/// <summary>Immutable font-family metadata supplied by a typed backend catalog.</summary>
public readonly record struct LibreFontFamilyInfo(
    string Name,
    bool HasRegular,
    bool HasBold,
    bool HasItalic,
    bool HasBoldItalic,
    bool IsFixedPitch,
    bool IsVector,
    bool IsVertical,
    bool IsSymbol);

/// <summary>Enumerates platform fonts without exposing backend font objects.</summary>
public interface ILibreFontCatalog
{
    IReadOnlyList<LibreFontFamilyInfo> GetFamilies();
}

/// <summary>The selected state of a portable font dialog.</summary>
public readonly record struct LibreFontDialogSelection(
    string FamilyName,
    float SizeInPoints,
    FontStyle Style,
    byte GdiCharSet,
    bool GdiVerticalFont,
    Color Color);

/// <summary>Backend-neutral state and callbacks for one canonical font-selection session.</summary>
public readonly record struct LibreFontDialogRequest(
    LibreFontDialogSelection Selection,
    int MinimumSize,
    int MaximumSize,
    LibreFontDialogOptions Options,
    Action<LibreFontDialogSelection>? ApplyRequested,
    Action? HelpRequested,
    LibreHandle Owner);

/// <summary>Final state returned by a font-selection service.</summary>
public readonly record struct LibreFontDialogResult(bool Accepted, LibreFontDialogSelection Selection);

/// <summary>Selects a font without exposing CHOOSEFONT, LOGFONT, or native handles.</summary>
public interface ILibreFontDialogService
{
    LibreFontDialogResult Show(in LibreFontDialogRequest request);
}

/// <summary>Explicit default for hosts that have not supplied portable font selection.</summary>
public sealed class UnsupportedLibreFontDialogService : ILibreFontDialogService
{
    public static UnsupportedLibreFontDialogService Instance { get; } = new();

    private UnsupportedLibreFontDialogService()
    {
    }

    public LibreFontDialogResult Show(in LibreFontDialogRequest request)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable font dialogs.");
}

/// <summary>
/// Managed font selection built from typed catalog, window, monitor, paint, text, input, handle,
/// and dispatcher contracts. The dialog owns no backend font objects and uses a real host window.
/// </summary>
public sealed class ManagedLibreFontDialogService : ILibreFontDialogService
{
    private readonly ILibreDispatcher _dispatcher;
    private readonly ILibreHandleRegistry _handles;
    private readonly ILibreWindowService _windows;
    private readonly ILibreMonitorService _monitors;
    private readonly ILibrePaintService _painting;
    private readonly ILibreTextRendererService _text;
    private readonly ILibreFontCatalog _fonts;

    public ManagedLibreFontDialogService(
        ILibreDispatcher dispatcher,
        ILibreHandleRegistry handles,
        ILibreWindowService windows,
        ILibreMonitorService monitors,
        ILibrePaintService painting,
        ILibreTextRendererService text,
        ILibreFontCatalog fonts)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _handles = handles ?? throw new ArgumentNullException(nameof(handles));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
        _painting = painting ?? throw new ArgumentNullException(nameof(painting));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
    }

    public LibreFontDialogResult Show(in LibreFontDialogRequest request)
    {
        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException("Font dialogs must be shown on the owning dispatcher thread.");
        }

        Validate(request);
        using var session = new Session(
            _dispatcher,
            _handles,
            _windows,
            _monitors,
            _painting,
            _text,
            _fonts.GetFamilies(),
            request);
        return session.Show();
    }

    private static void Validate(in LibreFontDialogRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Selection.FamilyName);
        if (!float.IsFinite(request.Selection.SizeInPoints) || request.Selection.SizeInPoints <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Font size must be finite and positive.");
        }

        if (request.MinimumSize < 0 || request.MaximumSize < 0
            || (request.MaximumSize > 0 && request.MinimumSize > request.MaximumSize))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Font size limits are inconsistent.");
        }

        const LibreFontDialogOptions supported = LibreFontDialogOptions.AllowSimulations
            | LibreFontDialogOptions.AllowVectorFonts
            | LibreFontDialogOptions.AllowVerticalFonts
            | LibreFontDialogOptions.AllowScriptChange
            | LibreFontDialogOptions.FixedPitchOnly
            | LibreFontDialogOptions.FontMustExist
            | LibreFontDialogOptions.ScriptsOnly
            | LibreFontDialogOptions.ShowApply
            | LibreFontDialogOptions.ShowColor
            | LibreFontDialogOptions.ShowEffects
            | LibreFontDialogOptions.ShowHelp;
        if ((request.Options & ~supported) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Options, "Unknown font-dialog option.");
        }
    }

    private sealed class Session : ILibreWindowEvents, IDisposable
    {
        private const int WindowWidth = 720;
        private const int WindowHeight = 520;
        private const int RowHeight = 25;
        private const int VisibleRows = 9;
        private const int ButtonWidth = 88;
        private const int ButtonHeight = 30;
        private static readonly float[] s_standardSizes = [8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72];
        private static readonly Color[] s_colors =
        [
            Color.Black, Color.DimGray, Color.Maroon, Color.Red,
            Color.Olive, Color.Green, Color.Teal, Color.Blue,
            Color.Navy, Color.Purple, Color.Magenta, Color.Orange,
        ];

        private readonly ILibreDispatcher _dispatcher;
        private readonly ILibreHandleRegistry _handles;
        private readonly ILibreWindowService _windows;
        private readonly ILibreMonitorService _monitors;
        private readonly ILibrePaintService _painting;
        private readonly ILibreTextRendererService _text;
        private readonly LibreFontDialogRequest _request;
        private readonly LibreFontFamilyInfo[] _families;
        private readonly float[] _sizes;
        private readonly Color[] _colors;
        private readonly List<FontStyle> _styles = [];
        private ILibreWindow? _window;
        private Rectangle _familyBounds;
        private Rectangle _styleBounds;
        private Rectangle _sizeBounds;
        private Rectangle _underlineBounds;
        private Rectangle _strikeoutBounds;
        private Rectangle _colorBounds;
        private Rectangle _previewBounds;
        private Rectangle _applyBounds;
        private Rectangle _helpBounds;
        private Rectangle _okBounds;
        private Rectangle _cancelBounds;
        private int _familyIndex;
        private int _familyScroll;
        private int _styleIndex;
        private int _sizeIndex;
        private int _sizeScroll;
        private int _colorIndex;
        private bool _underline;
        private bool _strikeout;
        private FocusTarget _focus = FocusTarget.Family;
        private FocusTarget _pressed = FocusTarget.None;
        private string _familySearch = string.Empty;
        private bool _closed;
        private bool _accepted;

        internal Session(
            ILibreDispatcher dispatcher,
            ILibreHandleRegistry handles,
            ILibreWindowService windows,
            ILibreMonitorService monitors,
            ILibrePaintService painting,
            ILibreTextRendererService text,
            IReadOnlyList<LibreFontFamilyInfo> families,
            in LibreFontDialogRequest request)
        {
            _dispatcher = dispatcher;
            _handles = handles;
            _windows = windows;
            _monitors = monitors;
            _painting = painting;
            _text = text;
            _request = request;
            _families = FilterFamilies(families, request.Options);
            if (_families.Length == 0)
            {
                throw new InvalidOperationException("No installed fonts satisfy the requested font-dialog filters.");
            }

            _familyIndex = FindFamily(request.Selection.FamilyName);
            _familyScroll = ClampScroll(_familyIndex, _families.Length);
            RebuildStyles(request.Selection.Style);
            _sizes = CreateSizes(request.MinimumSize, request.MaximumSize, request.Selection.SizeInPoints);
            _sizeIndex = FindNearestSize(request.Selection.SizeInPoints);
            _sizeScroll = ClampScroll(_sizeIndex, _sizes.Length);
            _underline = request.Selection.Style.HasFlag(FontStyle.Underline);
            _strikeout = request.Selection.Style.HasFlag(FontStyle.Strikeout);
            (_colors, _colorIndex) = CreateColors(request.Selection.Color);
        }

        internal LibreFontDialogResult Show()
        {
            LibreRectangle bounds = CalculateWindowBounds();
            Layout(bounds.Width, bounds.Height);
            LibreSize fixedSize = new(bounds.Width, bounds.Height);
            _window = _windows.Create(new LibreWindowCreateOptions(
                "Font",
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
                CanClose: true), this);
            _window.Show();
            _painting.InvalidateAll(_window.Handle);
            _window.Activate();
            _dispatcher.RunNested(() => !_closed, CancellationToken.None);
            return new LibreFontDialogResult(_accepted, CreateSelection());
        }

        public bool Closing() => true;
        public void Closed() => _closed = true;
        public void BoundsChanged(LibreRectangle bounds) { Layout(bounds.Width, bounds.Height); Invalidate(); }
        public void StateChanged(LibreWindowState state) => _ = state;
        public void PresentationScaleChanged(double scale) { _ = scale; Invalidate(); }

        public void PaintRequested(ILibrePaintFrame frame)
        {
            Graphics graphics = frame.Graphics;
            using var background = new SolidBrush(SystemColors.Control);
            graphics.FillRectangle(background, 0, 0, frame.SurfaceBounds.Width, frame.SurfaceBounds.Height);
            DrawLabel(graphics, "Font", new Rectangle(_familyBounds.X, 14, _familyBounds.Width, 24));
            DrawLabel(graphics, "Font style", new Rectangle(_styleBounds.X, 14, _styleBounds.Width, 24));
            DrawLabel(graphics, "Size", new Rectangle(_sizeBounds.X, 14, _sizeBounds.Width, 24));
            DrawList(graphics, _familyBounds, _families.Length, _familyScroll, _familyIndex, static (session, index) => session._families[index].Name);
            DrawList(graphics, _styleBounds, _styles.Count, 0, _styleIndex, static (session, index) => StyleName(session._styles[index]));
            DrawList(graphics, _sizeBounds, _sizes.Length, _sizeScroll, _sizeIndex, static (session, index) => session._sizes[index].ToString("0.#", CultureInfo.CurrentCulture));

            if (_request.Options.HasFlag(LibreFontDialogOptions.ShowEffects))
            {
                DrawCheckBox(graphics, _underlineBounds, "Underline", _underline, FocusTarget.Underline);
                DrawCheckBox(graphics, _strikeoutBounds, "Strikeout", _strikeout, FocusTarget.Strikeout);
            }

            if (ShowColor)
            {
                DrawLabel(graphics, "Color", new Rectangle(_colorBounds.X, _colorBounds.Y - 25, 120, 22));
                DrawColors(graphics);
            }

            DrawPreview(graphics);
            if (_request.Options.HasFlag(LibreFontDialogOptions.ShowApply)) DrawButton(graphics, _applyBounds, "Apply", FocusTarget.Apply);
            if (_request.Options.HasFlag(LibreFontDialogOptions.ShowHelp)) DrawButton(graphics, _helpBounds, "Help", FocusTarget.Help);
            DrawButton(graphics, _okBounds, "OK", FocusTarget.OK);
            DrawButton(graphics, _cancelBounds, "Cancel", FocusTarget.Cancel);
        }

        public void Input(in LibreInputEvent inputEvent)
        {
            switch (inputEvent.Kind)
            {
                case LibreInputEventKind.KeyDown: HandleKeyDown(inputEvent.Key, inputEvent.Modifiers); break;
                case LibreInputEventKind.TextInput: HandleTextInput(inputEvent.Text); break;
                case LibreInputEventKind.PointerDown when inputEvent.Button == LibrePointerButton.Primary: HandlePointerDown(inputEvent.Position); break;
                case LibreInputEventKind.PointerUp when inputEvent.Button == LibrePointerButton.Primary: HandlePointerUp(inputEvent.Position); break;
            }
        }

        public void Dispose()
        {
            _window?.Dispose();
            _window = null;
        }

        private bool ShowColor => _request.Options.HasFlag(LibreFontDialogOptions.ShowColor);

        private void HandleKeyDown(LibreKey key, LibreInputModifiers modifiers)
        {
            switch (key)
            {
                case LibreKey.Tab: MoveFocus(modifiers.HasFlag(LibreInputModifiers.Shift) ? -1 : 1); break;
                case LibreKey.Up: MoveCurrent(-1); break;
                case LibreKey.Down: MoveCurrent(1); break;
                case LibreKey.PageUp: MoveCurrent(-VisibleRows); break;
                case LibreKey.PageDown: MoveCurrent(VisibleRows); break;
                case LibreKey.Home: MoveToBoundary(first: true); break;
                case LibreKey.End: MoveToBoundary(first: false); break;
                case LibreKey.Space: ActivateTarget(_focus); break;
                case LibreKey.Enter:
                case LibreKey.NumPadEnter:
                    if (_focus is FocusTarget.Underline or FocusTarget.Strikeout or FocusTarget.Color
                        or FocusTarget.Apply or FocusTarget.Help or FocusTarget.OK or FocusTarget.Cancel)
                    {
                        ActivateTarget(_focus);
                    }
                    else
                    {
                        Complete(accepted: true);
                    }

                    break;
                case LibreKey.Escape: Complete(accepted: false); break;
            }
        }

        private void HandleTextInput(string? text)
        {
            if (_focus != FocusTarget.Family || string.IsNullOrWhiteSpace(text)) return;
            _familySearch += text.Trim();
            int index = Array.FindIndex(_families, item => item.Name.StartsWith(_familySearch, StringComparison.CurrentCultureIgnoreCase));
            if (index < 0)
            {
                _familySearch = text.Trim();
                index = Array.FindIndex(_families, item => item.Name.StartsWith(_familySearch, StringComparison.CurrentCultureIgnoreCase));
            }

            if (index >= 0) SelectFamily(index);
        }

        private void HandlePointerDown(LibrePoint point)
        {
            if (TrySelectList(point, _familyBounds, _familyScroll, _families.Length, out int family)) { _focus = FocusTarget.Family; SelectFamily(family); return; }
            if (TrySelectList(point, _styleBounds, 0, _styles.Count, out int style)) { _focus = FocusTarget.Style; _styleIndex = style; Invalidate(); return; }
            if (TrySelectList(point, _sizeBounds, _sizeScroll, _sizes.Length, out int size)) { _focus = FocusTarget.Size; _sizeIndex = size; Invalidate(); return; }
            int color = HitTestColor(point);
            if (color >= 0) { _focus = FocusTarget.Color; _colorIndex = color; Invalidate(); return; }
            _pressed = HitTestTarget(point);
            if (_pressed != FocusTarget.None) { _focus = _pressed; Invalidate(); }
        }

        private void HandlePointerUp(LibrePoint point)
        {
            FocusTarget released = HitTestTarget(point);
            FocusTarget pressed = _pressed;
            _pressed = FocusTarget.None;
            Invalidate();
            if (pressed != FocusTarget.None && pressed == released) ActivateTarget(pressed);
        }

        private void MoveCurrent(int delta)
        {
            switch (_focus)
            {
                case FocusTarget.Family: SelectFamily(Math.Clamp(_familyIndex + delta, 0, _families.Length - 1)); break;
                case FocusTarget.Style: _styleIndex = Math.Clamp(_styleIndex + delta, 0, _styles.Count - 1); Invalidate(); break;
                case FocusTarget.Size: _sizeIndex = Math.Clamp(_sizeIndex + delta, 0, _sizes.Length - 1); _sizeScroll = ClampScroll(_sizeIndex, _sizes.Length); Invalidate(); break;
                case FocusTarget.Color when ShowColor: _colorIndex = Math.Clamp(_colorIndex + delta, 0, _colors.Length - 1); Invalidate(); break;
            }
        }

        private void MoveToBoundary(bool first)
        {
            switch (_focus)
            {
                case FocusTarget.Family: SelectFamily(first ? 0 : _families.Length - 1); break;
                case FocusTarget.Style: _styleIndex = first ? 0 : _styles.Count - 1; Invalidate(); break;
                case FocusTarget.Size: _sizeIndex = first ? 0 : _sizes.Length - 1; _sizeScroll = ClampScroll(_sizeIndex, _sizes.Length); Invalidate(); break;
            }
        }

        private void ActivateTarget(FocusTarget target)
        {
            switch (target)
            {
                case FocusTarget.Underline: _underline = !_underline; Invalidate(); break;
                case FocusTarget.Strikeout: _strikeout = !_strikeout; Invalidate(); break;
                case FocusTarget.Color: _colorIndex = (_colorIndex + 1) % _colors.Length; Invalidate(); break;
                case FocusTarget.Apply: _request.ApplyRequested?.Invoke(CreateSelection()); break;
                case FocusTarget.Help: _request.HelpRequested?.Invoke(); break;
                case FocusTarget.OK: Complete(accepted: true); break;
                case FocusTarget.Cancel: Complete(accepted: false); break;
            }
        }

        private void MoveFocus(int delta)
        {
            FocusTarget[] targets = GetFocusTargets();
            int index = Array.IndexOf(targets, _focus);
            if (index < 0) index = 0;
            _focus = targets[(index + delta + targets.Length) % targets.Length];
            _familySearch = string.Empty;
            Invalidate();
        }

        private FocusTarget[] GetFocusTargets()
        {
            var targets = new List<FocusTarget> { FocusTarget.Family, FocusTarget.Style, FocusTarget.Size };
            if (_request.Options.HasFlag(LibreFontDialogOptions.ShowEffects))
            {
                targets.Add(FocusTarget.Underline);
                targets.Add(FocusTarget.Strikeout);
            }

            if (ShowColor) targets.Add(FocusTarget.Color);
            if (_request.Options.HasFlag(LibreFontDialogOptions.ShowApply)) targets.Add(FocusTarget.Apply);
            if (_request.Options.HasFlag(LibreFontDialogOptions.ShowHelp)) targets.Add(FocusTarget.Help);
            targets.Add(FocusTarget.OK);
            targets.Add(FocusTarget.Cancel);
            return [.. targets];
        }

        private void SelectFamily(int index)
        {
            FontStyle requested = SelectedBaseStyle;
            _familyIndex = Math.Clamp(index, 0, _families.Length - 1);
            _familyScroll = ClampScroll(_familyIndex, _families.Length);
            RebuildStyles(requested);
            Invalidate();
        }

        private void RebuildStyles(FontStyle requested)
        {
            _styles.Clear();
            LibreFontFamilyInfo family = _families[_familyIndex];
            bool simulate = _request.Options.HasFlag(LibreFontDialogOptions.AllowSimulations);
            AddStyle(FontStyle.Regular, family.HasRegular || simulate);
            AddStyle(FontStyle.Bold, family.HasBold || simulate);
            AddStyle(FontStyle.Italic, family.HasItalic || simulate);
            AddStyle(FontStyle.Bold | FontStyle.Italic, family.HasBoldItalic || simulate);
            if (_styles.Count == 0) _styles.Add(FontStyle.Regular);
            FontStyle baseStyle = requested & (FontStyle.Bold | FontStyle.Italic);
            _styleIndex = Math.Max(0, _styles.IndexOf(baseStyle));
        }

        private void AddStyle(FontStyle style, bool available)
        {
            if (available) _styles.Add(style);
        }

        private FontStyle SelectedBaseStyle => _styles[Math.Clamp(_styleIndex, 0, _styles.Count - 1)];

        private LibreFontDialogSelection CreateSelection()
        {
            FontStyle style = SelectedBaseStyle;
            if (_underline) style |= FontStyle.Underline;
            if (_strikeout) style |= FontStyle.Strikeout;
            return new LibreFontDialogSelection(
                _families[_familyIndex].Name,
                _sizes[_sizeIndex],
                style,
                _request.Selection.GdiCharSet,
                _families[_familyIndex].IsVertical && _request.Options.HasFlag(LibreFontDialogOptions.AllowVerticalFonts),
                ShowColor ? _colors[_colorIndex] : _request.Selection.Color);
        }

        private void Complete(bool accepted)
        {
            _accepted = accepted;
            _window?.Close();
        }

        private LibreRectangle CalculateWindowBounds()
        {
            IReadOnlyList<LibreMonitor> monitors = _monitors.GetMonitors();
            if (monitors.Count == 0) throw new InvalidOperationException("The platform monitor inventory is empty.");
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
                if (string.IsNullOrEmpty(monitor.Id)) monitor = monitors[0];
                anchor = monitor.WorkArea;
            }

            int width = Math.Min(WindowWidth, Math.Max(1, monitor.WorkArea.Width));
            int height = Math.Min(WindowHeight, Math.Max(1, monitor.WorkArea.Height));
            int x = Math.Clamp(anchor.X + ((anchor.Width - width) / 2), monitor.WorkArea.X, Math.Max(monitor.WorkArea.X, monitor.WorkArea.Right - width));
            int y = Math.Clamp(anchor.Y + ((anchor.Height - height) / 2), monitor.WorkArea.Y, Math.Max(monitor.WorkArea.Y, monitor.WorkArea.Bottom - height));
            return new LibreRectangle(x, y, width, height);
        }

        private void Layout(int width, int height)
        {
            _familyBounds = new Rectangle(20, 40, 300, VisibleRows * RowHeight);
            _styleBounds = new Rectangle(340, 40, 170, VisibleRows * RowHeight);
            _sizeBounds = new Rectangle(530, 40, Math.Max(80, width - 550), VisibleRows * RowHeight);
            _underlineBounds = new Rectangle(20, 284, 130, 28);
            _strikeoutBounds = new Rectangle(160, 284, 130, 28);
            _colorBounds = new Rectangle(340, 310, Math.Max(250, width - 360), 28);
            _previewBounds = new Rectangle(20, 350, Math.Max(1, width - 40), 88);
            int buttonY = height - 50;
            _cancelBounds = new Rectangle(width - 20 - ButtonWidth, buttonY, ButtonWidth, ButtonHeight);
            _okBounds = new Rectangle(_cancelBounds.Left - 8 - ButtonWidth, buttonY, ButtonWidth, ButtonHeight);
            _applyBounds = new Rectangle(_okBounds.Left - 8 - ButtonWidth, buttonY, ButtonWidth, ButtonHeight);
            _helpBounds = new Rectangle(20, buttonY, ButtonWidth, ButtonHeight);
        }

        private void DrawList(
            Graphics graphics,
            Rectangle bounds,
            int count,
            int scroll,
            int selected,
            Func<Session, int, string> getText)
        {
            using var background = new SolidBrush(SystemColors.Window);
            using var border = new Pen(SystemColors.ControlDarkDark);
            graphics.FillRectangle(background, bounds);
            graphics.DrawRectangle(border, bounds);
            int visible = Math.Min(VisibleRows, count - scroll);
            for (int row = 0; row < visible; row++)
            {
                int index = scroll + row;
                var rowBounds = new Rectangle(bounds.X + 2, bounds.Y + (row * RowHeight) + 2, bounds.Width - 4, RowHeight - 1);
                bool isSelected = index == selected;
                if (isSelected)
                {
                    using var selection = new SolidBrush(SystemColors.Highlight);
                    graphics.FillRectangle(selection, rowBounds);
                }

                _text.DrawText(graphics, getText(this, index), null, Rectangle.Inflate(rowBounds, -4, 0),
                    isSelected ? SystemColors.HighlightText : SystemColors.WindowText, Color.Transparent,
                    LibreTextFormat.SingleLine | LibreTextFormat.VerticalCenter | LibreTextFormat.EndEllipsis | LibreTextFormat.NoPrefix);
            }
        }

        private void DrawCheckBox(Graphics graphics, Rectangle bounds, string text, bool value, FocusTarget target)
        {
            var box = new Rectangle(bounds.X + 2, bounds.Y + 5, 17, 17);
            using var fill = new SolidBrush(SystemColors.Window);
            using var border = new Pen(_focus == target ? SystemColors.Highlight : SystemColors.ControlDarkDark, _focus == target ? 2f : 1f);
            graphics.FillRectangle(fill, box);
            graphics.DrawRectangle(border, box);
            if (value)
            {
                using var mark = new Pen(SystemColors.WindowText, 2f);
                graphics.DrawLine(mark, box.X + 3, box.Y + 8, box.X + 7, box.Bottom - 4);
                graphics.DrawLine(mark, box.X + 7, box.Bottom - 4, box.Right - 3, box.Y + 3);
            }

            DrawLabel(graphics, text, new Rectangle(bounds.X + 25, bounds.Y, bounds.Width - 25, bounds.Height));
        }

        private void DrawColors(Graphics graphics)
        {
            int cellWidth = Math.Max(18, Math.Min(28, (_colorBounds.Width - 4) / _colors.Length));
            for (int index = 0; index < _colors.Length; index++)
            {
                var cell = new Rectangle(_colorBounds.X + (index * cellWidth), _colorBounds.Y, cellWidth - 3, 24);
                using var fill = new SolidBrush(_colors[index]);
                using var border = new Pen(index == _colorIndex ? SystemColors.Highlight : SystemColors.ControlDarkDark, index == _colorIndex ? 3f : 1f);
                graphics.FillRectangle(fill, cell);
                graphics.DrawRectangle(border, cell);
            }
        }

        private void DrawPreview(Graphics graphics)
        {
            using var background = new SolidBrush(SystemColors.Window);
            using var border = new Pen(SystemColors.ControlDarkDark);
            graphics.FillRectangle(background, _previewBounds);
            graphics.DrawRectangle(border, _previewBounds);
            LibreFontDialogSelection selection = CreateSelection();
            try
            {
                using var font = new Font(selection.FamilyName, selection.SizeInPoints, selection.Style, GraphicsUnit.Point);
                _text.DrawText(graphics, "AaBbYyZz", font, Rectangle.Inflate(_previewBounds, -8, -6), selection.Color,
                    Color.Transparent, LibreTextFormat.HorizontalCenter | LibreTextFormat.VerticalCenter | LibreTextFormat.SingleLine | LibreTextFormat.EndEllipsis | LibreTextFormat.NoPrefix);
            }
            catch (ArgumentException)
            {
                DrawLabel(graphics, "Preview unavailable", Rectangle.Inflate(_previewBounds, -8, -6));
            }
        }

        private void DrawLabel(Graphics graphics, string text, Rectangle bounds)
            => _text.DrawText(graphics, text, null, bounds, SystemColors.ControlText, Color.Transparent,
                LibreTextFormat.SingleLine | LibreTextFormat.VerticalCenter | LibreTextFormat.NoPrefix);

        private void DrawButton(Graphics graphics, Rectangle bounds, string text, FocusTarget target)
        {
            bool focused = _focus == target;
            bool pressed = _pressed == target;
            using var background = new SolidBrush(pressed ? SystemColors.ControlDark : focused ? SystemColors.Highlight : SystemColors.ControlLight);
            using var border = new Pen(focused ? SystemColors.Highlight : SystemColors.ControlDarkDark, focused ? 2f : 1f);
            graphics.FillRectangle(background, bounds);
            graphics.DrawRectangle(border, bounds);
            _text.DrawText(graphics, text, null, bounds, focused ? SystemColors.HighlightText : SystemColors.ControlText,
                Color.Transparent, LibreTextFormat.HorizontalCenter | LibreTextFormat.VerticalCenter | LibreTextFormat.SingleLine | LibreTextFormat.NoPrefix);
        }

        private FocusTarget HitTestTarget(LibrePoint point)
        {
            if (_request.Options.HasFlag(LibreFontDialogOptions.ShowEffects) && _underlineBounds.Contains(point.X, point.Y)) return FocusTarget.Underline;
            if (_request.Options.HasFlag(LibreFontDialogOptions.ShowEffects) && _strikeoutBounds.Contains(point.X, point.Y)) return FocusTarget.Strikeout;
            if (_request.Options.HasFlag(LibreFontDialogOptions.ShowApply) && _applyBounds.Contains(point.X, point.Y)) return FocusTarget.Apply;
            if (_request.Options.HasFlag(LibreFontDialogOptions.ShowHelp) && _helpBounds.Contains(point.X, point.Y)) return FocusTarget.Help;
            if (_okBounds.Contains(point.X, point.Y)) return FocusTarget.OK;
            if (_cancelBounds.Contains(point.X, point.Y)) return FocusTarget.Cancel;
            return FocusTarget.None;
        }

        private int HitTestColor(LibrePoint point)
        {
            if (!ShowColor || !_colorBounds.Contains(point.X, point.Y)) return -1;
            int cellWidth = Math.Max(18, Math.Min(28, (_colorBounds.Width - 4) / _colors.Length));
            return Math.Clamp((point.X - _colorBounds.X) / cellWidth, 0, _colors.Length - 1);
        }

        private static bool TrySelectList(LibrePoint point, Rectangle bounds, int scroll, int count, out int index)
        {
            index = -1;
            if (!bounds.Contains(point.X, point.Y)) return false;
            int row = (point.Y - bounds.Y) / RowHeight;
            index = scroll + row;
            return index >= 0 && index < count;
        }

        private void Invalidate()
        {
            if (_window is not null) _painting.InvalidateAll(_window.Handle);
        }

        private int FindFamily(string familyName)
        {
            int index = Array.FindIndex(_families, item => string.Equals(item.Name, familyName, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) return index;
            if (_request.Options.HasFlag(LibreFontDialogOptions.FontMustExist))
            {
                throw new ArgumentException($"Font family '{familyName}' is not installed.", nameof(familyName));
            }

            return 0;
        }

        private int FindNearestSize(float size)
        {
            int best = 0;
            float distance = float.MaxValue;
            for (int index = 0; index < _sizes.Length; index++)
            {
                float candidate = Math.Abs(_sizes[index] - size);
                if (candidate < distance) { best = index; distance = candidate; }
            }

            return best;
        }

        private static LibreFontFamilyInfo[] FilterFamilies(IReadOnlyList<LibreFontFamilyInfo> families, LibreFontDialogOptions options)
        {
            ArgumentNullException.ThrowIfNull(families);
            return [.. families
                .Where(static item => !string.IsNullOrWhiteSpace(item.Name))
                .Where(item => !options.HasFlag(LibreFontDialogOptions.FixedPitchOnly) || item.IsFixedPitch)
                .Where(item => options.HasFlag(LibreFontDialogOptions.AllowVectorFonts) || !item.IsVector)
                .Where(item => options.HasFlag(LibreFontDialogOptions.AllowVerticalFonts) || !item.IsVertical)
                .Where(item => !options.HasFlag(LibreFontDialogOptions.ScriptsOnly) || !item.IsSymbol)
                .OrderBy(static item => item.Name, StringComparer.CurrentCultureIgnoreCase)];
        }

        private static float[] CreateSizes(int minimum, int maximum, float current)
        {
            float min = minimum > 0 ? minimum : 1;
            float max = maximum > 0 ? maximum : 512;
            var sizes = new SortedSet<float>(s_standardSizes.Where(size => size >= min && size <= max));
            sizes.Add(Math.Clamp(current, min, max));
            if (sizes.Count == 0) sizes.Add(min);
            return [.. sizes];
        }

        private static int ClampScroll(int selected, int count)
            => Math.Clamp(selected - (VisibleRows / 2), 0, Math.Max(0, count - VisibleRows));

        private static (Color[] Colors, int SelectedIndex) CreateColors(Color color)
        {
            int index = Array.FindIndex(s_colors, candidate => candidate.ToArgb() == color.ToArgb());
            if (index >= 0)
            {
                return (s_colors, index);
            }

            Color selected = color.IsEmpty ? Color.Black : Color.FromArgb(color.R, color.G, color.B);
            Color[] colors = [.. s_colors, selected];
            return (colors, colors.Length - 1);
        }

        private static string StyleName(FontStyle style) => style switch
        {
            FontStyle.Bold => "Bold",
            FontStyle.Italic => "Italic",
            FontStyle.Bold | FontStyle.Italic => "Bold Italic",
            _ => "Regular",
        };

        private enum FocusTarget
        {
            None,
            Family,
            Style,
            Size,
            Underline,
            Strikeout,
            Color,
            Apply,
            Help,
            OK,
            Cancel,
        }
    }
}
