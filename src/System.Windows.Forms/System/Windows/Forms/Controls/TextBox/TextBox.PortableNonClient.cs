// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Drawing;

namespace System.Windows.Forms;

public partial class TextBox
{
    internal override Padding PortableNonClientInsets
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

    internal override void PaintPortableNonClient(PaintEventArgs e)
    {
        Rectangle bounds = new(Point.Empty, Size);
        if (BorderStyle == BorderStyle.FixedSingle)
        {
            // The shared frame painter has excluded the client. Fill the real
            // source-metric bands, including asymmetric platform metrics.
            e.Graphics.FillRectangle(SystemBrushes.WindowFrame, bounds);
        }
        else if (BorderStyle == BorderStyle.Fixed3D)
            ControlPaint.DrawBorder3D(e.Graphics, bounds, Border3DStyle.Sunken);
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
