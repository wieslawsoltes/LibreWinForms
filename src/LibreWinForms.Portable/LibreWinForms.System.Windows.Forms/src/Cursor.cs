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
    SizeAll = 3
}

/// <summary>
/// Portable compatibility surface for WPF-era code that toggles the WinForms cursor.
/// </summary>
public sealed class Cursor
{
    private static int s_visibilityDepth;
    private static Cursor? s_current;

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

    internal Cursor(PortableCursorKind portableKind)
    {
        PortableKind = portableKind;
    }

    /// <summary>
    /// Gets the platform-neutral cursor kind consumed by portable hosts.
    /// </summary>
    public PortableCursorKind PortableKind { get; }

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

    public override string ToString()
    {
        return $"[Cursor: {PortableKind}]";
    }
}
