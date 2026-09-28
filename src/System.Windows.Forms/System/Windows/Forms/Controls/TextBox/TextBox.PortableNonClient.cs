// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Drawing;
using System.Drawing.Drawing2D;

namespace System.Windows.Forms;

public partial class TextBox
{
    internal override Padding PortableWindowAdornments
    {
        get
        {
            Size border = BorderStyle switch
            {
                BorderStyle.FixedSingle => SystemInformation.BorderSize,
                BorderStyle.Fixed3D => SystemInformation.Border3DSize,
                _ => Size.Empty
            };
            return new Padding(border.Width, border.Height, border.Width, border.Height);
        }
    }

    // Native EDIT handles WS_BORDER as client content. Its pre-handle size
    // estimate still uses AdjustWindowRectEx; WS_EX_CLIENTEDGE stays non-client.
    internal override Padding PortableNonClientInsets => BorderStyle == BorderStyle.FixedSingle
        ? Padding.Empty : PortableWindowAdornments;

    private Rectangle PortableTextViewport
    {
        get
        {
            int factor = BorderStyle switch { BorderStyle.FixedSingle => 2, BorderStyle.Fixed3D => 1, _ => 0 };
            Size border = SystemInformation.BorderSize;
            int x = factor * border.Width;
            int y = factor * border.Height;
            Rectangle client = PortableClientRectangle;
            return new Rectangle(x, y, Math.Max(0, client.Width - 2 * x), Math.Max(0, client.Height - 2 * y));
        }
    }

    internal override void PaintPortableNonClient(PaintEventArgs e)
    {
        Rectangle bounds = new(Point.Empty, Size);
        if (BorderStyle == BorderStyle.Fixed3D)
            ControlPaint.DrawBorder3D(e.Graphics, bounds, Border3DStyle.Sunken);
    }

    internal override void PaintPortableClientBorder(PaintEventArgs e)
    {
        if (BorderStyle != BorderStyle.FixedSingle) return;
        Rectangle bounds = PortableClientRectangle;
        Size border = SystemInformation.BorderSize;
        Rectangle interior = new(border.Width, border.Height,
            Math.Max(0, bounds.Width - 2 * border.Width), Math.Max(0, bounds.Height - 2 * border.Height));
        GraphicsState state = e.Graphics.Save();
        try
        {
            e.Graphics.SetClip(bounds, CombineMode.Intersect);
            e.Graphics.ExcludeClip(interior);
            e.Graphics.FillRectangle(SystemBrushes.WindowFrame, bounds);
        }
        finally { e.Graphics.Restore(state); }
    }

    protected override void OnBorderStyleChanged(EventArgs e)
    {
        ReleasePortableTextLayout();
        Padding insets = PortableNonClientInsets;
        Invalidate(new Rectangle(-insets.Left, -insets.Top, Width, Height));
        base.OnBorderStyleChanged(e);
    }
}
#endif
