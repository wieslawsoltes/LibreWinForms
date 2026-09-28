// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Drawing;
using System.Drawing.Drawing2D;

namespace System.Windows.Forms;

public partial class Control
{
    // Logical child controls own these source adornments. Native top-level
    // decorations remain outside the backend's drawable client surface.
    internal virtual Padding PortableNonClientInsets => Padding.Empty;

    internal virtual void PaintPortableNonClient(PaintEventArgs e) { }

    private Rectangle PortableClientBoundsInWindow
    {
        get
        {
            Padding insets = PortableNonClientInsets;
            return new Rectangle(insets.Left, insets.Top, _clientWidth, _clientHeight);
        }
    }

    private void PaintPortableClientAndFrame(Graphics graphics, Rectangle windowClip)
    {
        if (PortableNonClientInsets == Padding.Empty)
        {
            PaintPortableClient(graphics, windowClip);
            return;
        }

        GraphicsState state = graphics.Save();
        try
        {
            graphics.SetClip(windowClip, CombineMode.Intersect);
            graphics.ExcludeClip(PortableClientBoundsInWindow);
            using (PaintEventArgs frame = new(graphics, windowClip))
            {
                PaintPortableNonClient(frame);
            }
        }
        finally { graphics.Restore(state); }

        Rectangle client = PortableClientBoundsInWindow;
        Rectangle clip = Rectangle.Intersect(windowClip, client);
        if (clip.Width <= 0 || clip.Height <= 0) return;
        clip.Offset(-client.X, -client.Y);
        state = graphics.Save();
        try
        {
            graphics.TranslateTransform(client.X, client.Y);
            graphics.SetClip(clip, CombineMode.Intersect);
            PaintPortableClient(graphics, clip);
        }
        finally { graphics.Restore(state); }
    }

    private void PaintPortableClient(Graphics graphics, Rectangle clip)
    {
        using PaintEventArgs paint = new(graphics, clip,
            DrawingEventFlags.SaveState | DrawingEventFlags.GraphicsStateUnclean);
        PaintWithErrorHandling(paint, PaintLayerBackground);
        paint.ResetGraphics();
        PaintWithErrorHandling(paint, PaintLayerForeground);
    }
}
#endif
