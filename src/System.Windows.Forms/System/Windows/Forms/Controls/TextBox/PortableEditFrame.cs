// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Drawing;
using System.Drawing.Drawing2D;

namespace System.Windows.Forms;

// Explicitly selected by plain and masked EDIT controls, not all TextBoxBase
// derivatives. Rich-edit/native top-level frames retain their own contracts.
internal static class PortableEditFrame
{
    internal static Padding GetWindowAdornments(BorderStyle style)
    {
        Size border = style switch
        {
            BorderStyle.FixedSingle => SystemInformation.BorderSize,
            BorderStyle.Fixed3D => SystemInformation.Border3DSize,
            _ => Size.Empty
        };
        return new Padding(border.Width, border.Height, border.Width, border.Height);
    }

    // Native EDIT handles WS_BORDER as client content. Its pre-handle size
    // estimate still uses AdjustWindowRectEx; WS_EX_CLIENTEDGE stays non-client.
    internal static Padding GetNonClientInsets(BorderStyle style)
        => style == BorderStyle.FixedSingle ? Padding.Empty : GetWindowAdornments(style);

    internal static Rectangle GetTextViewport(Rectangle client, BorderStyle style)
    {
        int factor = style switch { BorderStyle.FixedSingle => 2, BorderStyle.Fixed3D => 1, _ => 0 };
        Size border = SystemInformation.BorderSize;
        int x = factor * border.Width;
        int y = factor * border.Height;
        return new Rectangle(x, y, Math.Max(0, client.Width - 2 * x), Math.Max(0, client.Height - 2 * y));
    }

    internal static void PaintNonClient(Graphics graphics, Size size, BorderStyle style)
    {
        if (style == BorderStyle.Fixed3D)
        {
            ControlPaint.DrawBorder3D(graphics, new Rectangle(Point.Empty, size), Border3DStyle.Sunken);
        }
    }

    internal static void PaintClientBorder(Graphics graphics, Rectangle client, BorderStyle style)
    {
        if (style != BorderStyle.FixedSingle)
        {
            return;
        }

        Size border = SystemInformation.BorderSize;
        Rectangle interior = new(border.Width, border.Height,
            Math.Max(0, client.Width - 2 * border.Width), Math.Max(0, client.Height - 2 * border.Height));
        GraphicsState state = graphics.Save();
        try
        {
            graphics.SetClip(client, CombineMode.Intersect);
            graphics.ExcludeClip(interior);
            graphics.FillRectangle(SystemBrushes.WindowFrame, client);
        }
        finally
        {
            graphics.Restore(state);
        }
    }
}
#endif
