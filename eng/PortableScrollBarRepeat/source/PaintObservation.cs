using System.Drawing;
using System.Windows.Forms;

internal sealed class PaintObservation : IDisposable
{
    [ThreadStatic] private static PaintObservation? current;
    private readonly PaintObservation? previous;
    private readonly ScrollBar source;
    private readonly Graphics graphics;
    private readonly nint handle;
    private readonly Rectangle client;
    private readonly Rectangle screen;
    private readonly int value;
    private readonly List<PartFrame> parts = [];
    private bool disposed;
    internal static readonly Dictionary<ScrollBar, PaintFrame> Latest = [];
    internal readonly record struct PartFrame(int Part, int State, Rectangle Bounds);
    internal sealed record PaintFrame(long Handle, Rectangle Client, Rectangle Screen, int Value, PartFrame[] Parts);

    internal PaintObservation(ScrollBar source, Graphics graphics)
    {
        if (!source.IsHandleCreated || source.IsDisposed || source.Disposing)
            throw new InvalidOperationException("Paint source is not live.");
        this.source = source; this.graphics = graphics;
        handle = source.Handle; client = source.ClientRectangle;
        screen = source.RectangleToScreen(client); value = source.Value;
        previous = current; current = this;
    }

    internal static void Record(Graphics graphics, string className, int part, int state, Rectangle bounds)
    {
        PaintObservation? owner = current;
        if (owner is null || !ReferenceEquals(owner.graphics, graphics)) return;
        if (!string.Equals(className, "SCROLLBAR", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected theme class in original scrollbar paint.");
        if (owner.parts.Count >= 8) throw new InvalidOperationException("Original scrollbar paint inventory exceeded.");
        owner.parts.Add(new(part, state, bounds));
    }

    internal void Commit()
    {
        if (disposed || current != this || source.IsDisposed || source.Disposing || !source.IsHandleCreated
            || source.Handle != handle || source.ClientRectangle != client || source.Value != value
            || source.RectangleToScreen(client) != screen)
            throw new InvalidOperationException("Source changed during original paint observation.");
        bool vertical = source is VScrollBar;
        PartFrame[] arrows = parts.Where(p => p.Part == 1).ToArray();
        PartFrame[] thumbs = parts.Where(p => p.Part == (vertical ? 3 : 2)).ToArray();
        PartFrame[] tracks = parts.Where(p => p.Part == (vertical ? 7 : 5) || p.Part == (vertical ? 6 : 4)).ToArray();
        if (arrows.Length != 2 || thumbs.Length != 1 || tracks.Length < 1)
            throw new InvalidOperationException("Original source paint did not contain its actual arrows, thumb and track.");
        Latest[source] = new((long)handle, client, screen, value, parts.ToArray());
    }

    public void Dispose()
    {
        if (disposed) return;
        if (current != this) throw new InvalidOperationException("Paint observation scope order changed.");
        current = previous; disposed = true;
    }
}
