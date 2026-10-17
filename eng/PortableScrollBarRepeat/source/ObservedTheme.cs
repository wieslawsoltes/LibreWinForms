using System.Drawing;
using LibreWinForms.Platform;

// Diagnostic decorator: forward every call to the actual supplied service.
internal sealed class ObservedTheme(ILibreVisualStyleService source) : ILibreVisualStyleService
{
    public bool IsEnabled => source.IsEnabled;
    public string ThemeFilename => source.ThemeFilename;
    public string ColorScheme => source.ColorScheme;
    public string ThemeSize => source.ThemeSize;
    public string DisplayName => source.DisplayName;
    public string Company => source.Company;
    public string Author => source.Author;
    public string Copyright => source.Copyright;
    public string Url => source.Url;
    public string Version => source.Version;
    public string Description => source.Description;
    public bool SupportsFlatMenus => source.SupportsFlatMenus;
    public int MinimumColorDepth => source.MinimumColorDepth;
    public bool IsElementDefined(string className, int part) => source.IsElementDefined(className, part);
    public void DrawBackground(Graphics graphics, string className, int part, int state, Rectangle bounds, Rectangle? clipRectangle)
    {
        source.DrawBackground(graphics, className, part, state, bounds, clipRectangle);
        PaintObservation.Record(graphics, className, part, state, bounds);
    }
    public Region? GetBackgroundRegion(string className, int part, int state, Rectangle bounds) => source.GetBackgroundRegion(className, part, state, bounds);
    public Rectangle GetBackgroundContentRectangle(string className, int part, int state, Rectangle bounds) => source.GetBackgroundContentRectangle(className, part, state, bounds);
    public Rectangle GetBackgroundExtent(string className, int part, int state, Rectangle contentBounds) => source.GetBackgroundExtent(className, part, state, contentBounds);
    public Size GetPartSize(string className, int part, int state, Rectangle? bounds, LibreVisualStyleSizeType type) => source.GetPartSize(className, part, state, bounds, type);
    public Color GetColor(string className, int part, int state, LibreVisualStyleColorProperty property) => source.GetColor(className, part, state, property);
    public int GetInteger(string className, int part, int state, LibreVisualStyleIntegerProperty property) => source.GetInteger(className, part, state, property);
    public bool GetBoolean(string className, int part, int state, LibreVisualStyleBooleanProperty property) => source.GetBoolean(className, part, state, property);
    public int GetEnumValue(string className, int part, int state, LibreVisualStyleEnumProperty property) => source.GetEnumValue(className, part, state, property);
    public string GetFilename(string className, int part, int state, LibreVisualStyleFilenameProperty property) => source.GetFilename(className, part, state, property);
    public string GetString(string className, int part, int state, LibreVisualStyleStringProperty property) => source.GetString(className, part, state, property);
    public Font? GetFont(string className, int part, int state, LibreVisualStyleFontProperty property) => source.GetFont(className, part, state, property);
    public Rectangle MeasureText(Graphics graphics, string className, int part, int state, Rectangle? bounds, string text, LibreVisualStyleTextFormat format) => source.MeasureText(graphics, className, part, state, bounds, text, format);
    public LibreVisualStyleHitTestCode HitTestBackground(Graphics graphics, string className, int part, int state, Rectangle bounds, Region? region, Point point, LibreVisualStyleHitTestOptions options) => source.HitTestBackground(graphics, className, part, state, bounds, region, point, options);
    public LibreVisualStyleTextMetrics GetTextMetrics(Graphics graphics, string className, int part, int state) => source.GetTextMetrics(graphics, className, part, state);
    public LibreVisualStyleMargins GetMargins(string className, int part, int state, LibreVisualStyleMarginProperty property) => source.GetMargins(className, part, state, property);
    public Point GetPoint(string className, int part, int state, LibreVisualStylePointProperty property) => source.GetPoint(className, part, state, property);
    public bool IsBackgroundPartiallyTransparent(string className, int part, int state) => source.IsBackgroundPartiallyTransparent(className, part, state);
    public Rectangle DrawEdge(Graphics graphics, string className, int part, int state, Rectangle bounds, LibreVisualStyleEdges edges, LibreVisualStyleEdgeStyle style, LibreVisualStyleEdgeEffects effects) => source.DrawEdge(graphics, className, part, state, bounds, edges, style, effects);
    public void DrawText(Graphics graphics, string className, int part, int state, Rectangle bounds, string text, bool disabled, LibreVisualStyleTextFormat format) => source.DrawText(graphics, className, part, state, bounds, text, disabled, format);
}
