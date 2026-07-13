using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Identifies portable cursors without requiring platform hosts to inspect
/// native handles or cursor implementation details.
/// </summary>
public enum PortableCursorKind
{
    Default = 0,
    Wait = 1,
    IBeam = 2,
    SizeAll = 3,
    Custom = 4,
    SizeWE = 5,
    SizeNS = 6
}

/// <summary>
/// Portable compatibility surface for WPF-era code that toggles the WinForms cursor.
/// </summary>
public sealed class Cursor : IDisposable
{
    private static readonly Size s_portableSystemCursorSize = new(32, 32);
    private static int s_visibilityDepth;
    private static Cursor? s_current;
    private readonly Bitmap? _bitmap;
    private bool _isDisposed;

    public static Cursor Current
    {
        get => s_current ?? Cursors.Default;
        set => s_current = value;
    }

    public static Point Position { get; set; }

    public Cursor()
        : this(PortableCursorKind.Default)
    {
    }

    /// <summary>
    /// Initializes a portable cursor from a bounded Windows cursor container.
    /// </summary>
    public Cursor(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        _bitmap = PortableCursorDecoder.DecodeFile(fileName);
        PortableKind = PortableCursorKind.Custom;
    }

    internal Cursor(PortableCursorKind portableKind)
    {
        PortableKind = portableKind;
    }

    /// <summary>
    /// Gets the platform-neutral cursor kind consumed by portable hosts.
    /// </summary>
    public PortableCursorKind PortableKind { get; }

    /// <summary>
    /// Gets the decoded cursor size. Built-in portable cursors use the conventional
    /// 32-by-32 system cursor size.
    /// </summary>
    public Size Size
    {
        get
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            return _bitmap?.Size ?? s_portableSystemCursorSize;
        }
    }

    /// <summary>
    /// Draws the cursor without stretching it, clipping the decoded image to the
    /// supplied target rectangle in the same way as WinForms <c>Cursor.Draw</c>.
    /// </summary>
    public void Draw(Graphics graphics, Rectangle targetRect)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_bitmap is null)
        {
            throw new NotSupportedException(
                "Built-in portable cursors are rendered by the host and do not expose bitmap pixels.");
        }

        int targetX = targetRect.IsEmpty ? 0 : targetRect.X;
        int targetY = targetRect.IsEmpty ? 0 : targetRect.Y;
        int availableWidth = targetRect.IsEmpty ? _bitmap.Width : Math.Max(0, targetRect.Width);
        int availableHeight = targetRect.IsEmpty ? _bitmap.Height : Math.Max(0, targetRect.Height);
        int drawWidth = Math.Min(_bitmap.Width, availableWidth);
        int drawHeight = Math.Min(_bitmap.Height, availableHeight);
        if (drawWidth == 0 || drawHeight == 0)
        {
            return;
        }

        var destination = new Rectangle(targetX, targetY, drawWidth, drawHeight);
        var source = new Rectangle(0, 0, drawWidth, drawHeight);
        graphics.DrawImage(_bitmap, destination, source, GraphicsUnit.Pixel);
    }

    public static void Hide()
    {
        s_visibilityDepth--;
    }

    public static void Show()
    {
        if (s_visibilityDepth < 0)
        {
            s_visibilityDepth++;
        }
    }

    public void Dispose()
    {
        // System cursors are shared singleton instances and do not own pixels.
        // Matching WinForms, disposing one must not invalidate the corresponding
        // Cursors property for the rest of the process.
        if (_bitmap is null || _isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _bitmap?.Dispose();
    }

    public override string ToString()
    {
        return $"[Cursor: {PortableKind}]";
    }
}
