// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Drawing;

namespace System.Windows.Forms;

public partial class TextBox
{
    internal override Padding PortableWindowAdornments => PortableEditFrame.GetWindowAdornments(BorderStyle);

    // Preserve virtual adornments for non-client styles. FixedSingle's border
    // belongs to EDIT client content and does not query adornments here.
    internal override Padding PortableNonClientInsets => BorderStyle == BorderStyle.FixedSingle
        ? Padding.Empty : PortableWindowAdornments;

    private Rectangle PortableTextViewport => PortableEditFrame.GetTextViewport(PortableClientRectangle, BorderStyle);

    internal override void PaintPortableNonClient(PaintEventArgs e)
        => PortableEditFrame.PaintNonClient(e.Graphics, Size, BorderStyle);

    internal override void PaintPortableClientBorder(PaintEventArgs e)
        => PortableEditFrame.PaintClientBorder(e.Graphics, PortableClientRectangle, BorderStyle);

    protected override void OnBorderStyleChanged(EventArgs e)
    {
        ReleasePortableTextLayout();
        Padding insets = PortableNonClientInsets;
        Invalidate(new Rectangle(-insets.Left, -insets.Top, Width, Height));
        base.OnBorderStyleChanged(e);
    }
}
#endif
