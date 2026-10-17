// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms.VisualStyles;

namespace System.Windows.Forms;

public abstract partial class ScrollBar
{
    protected override void OnPaint(PaintEventArgs e)
    {
        bool vertical = _scrollOrientation == ScrollOrientation.VerticalScroll;
        Rectangle client = ClientRectangle;
        int length = vertical ? client.Height : client.Width;
        int breadth = vertical ? client.Width : client.Height;
        if (length > 0 && breadth > 0)
        {
            PortableScrollBarGeometry geometry = ReadPortableScrollBarGeometry();
            int arrow = geometry.Arrow;
            Rectangle thumbBounds = geometry.Part(geometry.Start, geometry.Thumb);
            Rectangle trackBounds = geometry.Part(arrow, geometry.Track);
            bool enabled = Enabled;
            bool themed = ScrollBarRenderer.IsSupported;
            ScrollBarState state = enabled ? ScrollBarState.Normal : ScrollBarState.Disabled;
            GraphicsState saved = e.Graphics.Save();
            try
            {
                e.Graphics.SetClip(client, CombineMode.Intersect);
                e.Graphics.SetClip(e.ClipRectangle, CombineMode.Intersect);
                if (themed)
                {
                    if (vertical)
                        ScrollBarRenderer.DrawUpperVerticalTrack(e.Graphics, trackBounds, state);
                    else
                        ScrollBarRenderer.DrawLeftHorizontalTrack(e.Graphics, trackBounds, state);
                }
                else
                {
                    using SolidBrush background = new(SystemColors.ScrollBar);
                    e.Graphics.FillRectangle(background, trackBounds);
                }

                // Native thumb metadata can extend beyond a tiny track. Keep its
                // measured frame, but never paint over the independent arrows.
                GraphicsState thumbClip = e.Graphics.Save();
                try
                {
                    e.Graphics.SetClip(trackBounds, CombineMode.Intersect);
                    if (themed)
                    {
                        if (vertical)
                            ScrollBarRenderer.DrawVerticalThumb(e.Graphics, thumbBounds, state);
                        else
                            ScrollBarRenderer.DrawHorizontalThumb(e.Graphics, thumbBounds, state);
                    }
                    else
                    {
                        ControlPaint.DrawButton(e.Graphics, thumbBounds, enabled ? ButtonState.Normal : ButtonState.Inactive);
                    }
                }
                finally { e.Graphics.Restore(thumbClip); }

                DrawArrow(Part(0, arrow), vertical ? ScrollButton.Up : ScrollButton.Left,
                    vertical ? ScrollBarArrowButtonState.UpNormal : ScrollBarArrowButtonState.LeftNormal);
                DrawArrow(Part(length - arrow, arrow), vertical ? ScrollButton.Down : ScrollButton.Right,
                    vertical ? ScrollBarArrowButtonState.DownNormal : ScrollBarArrowButtonState.RightNormal);
            }
            finally { e.Graphics.Restore(saved); }

            Rectangle Part(int offset, int extent) => vertical
                ? new(client.X, client.Y + offset, breadth, extent)
                : new(client.X + offset, client.Y, extent, breadth);

            void DrawArrow(Rectangle bounds, ScrollButton direction, ScrollBarArrowButtonState normal)
            {
                if (bounds.Width <= 0 || bounds.Height <= 0)
                    return;
                if (themed)
                    ScrollBarRenderer.DrawArrowButton(e.Graphics, bounds,
                        enabled ? normal : (ScrollBarArrowButtonState)((int)normal + 3));
                else
                    ControlPaint.DrawScrollButton(e.Graphics, bounds, direction,
                        enabled ? ButtonState.Normal : ButtonState.Inactive);
            }
        }

        base.OnPaint(e);
    }

    // Input and paint consume the same original source metrics, including tiny
    // tracks. Input never turns the native metadata outside the track into a hit.
    private PortableScrollBarGeometry ReadPortableScrollBarGeometry()
    {
        bool vertical = _scrollOrientation == ScrollOrientation.VerticalScroll;
        Rectangle client = ClientRectangle;
        int length = vertical ? client.Height : client.Width;
        int arrowMetric = vertical ? SystemInformation.VerticalScrollBarArrowHeightForDpi(DeviceDpi)
            : SystemInformation.GetHorizontalScrollBarArrowWidthForDpi(DeviceDpi);
        int thumbMetric = ScaleHelper.ScaleToDpi(vertical ? SystemInformation.VerticalScrollBarThumbHeight
            : SystemInformation.HorizontalScrollBarThumbWidth, DeviceDpi);
        int arrow = Math.Min(length / 2, arrowMetric);
        int track = length - (2 * arrow);
        int page = LargeChange;
        if (page < 0 || arrow < 0 || thumbMetric <= 0)
            throw new PlatformNotSupportedException("The source ScrollBar paint metrics exceed their valid numeric range.");

        long range = (long)_maximum - _minimum + 1;
        long last = (long)_maximum - Math.Max(0, page - 1);
        long position = !vertical && RightToLeft == RightToLeft.Yes
            ? (long)_minimum + _maximum - page + 1 - _value : _value;
        position = Math.Clamp(position, _minimum, last);
        int thumb = page == 0 ? thumbMetric
            : Math.Max(thumbMetric, RoundedScrollProduct(track, page, range));
        int start = arrow + RoundedScrollProduct(track - thumb, position - _minimum, last - _minimum);
        return new(vertical, client, arrow, track, thumb, start, _minimum, last);
    }

    private readonly record struct PortableScrollBarGeometry(bool Vertical, Rectangle Client,
        int Arrow, int Track, int Thumb, int Start, int Minimum, long Last)
    {
        internal Rectangle Part(int offset, int extent) => Vertical
            ? new(Client.X, Client.Y + offset, Client.Width, extent)
            : new(Client.X + offset, Client.Y, extent, Client.Height);
        internal int Axis(Point point) => Vertical ? point.Y - Client.Y : point.X - Client.X;
        internal int Length => Vertical ? Client.Height : Client.Width;
        internal PortableScrollBarPart Hit(Point point)
        {
            if (!Client.Contains(point)) return PortableScrollBarPart.None;
            int axis = Axis(point);
            if (axis < Arrow) return PortableScrollBarPart.Decrement;
            if (axis >= Length - Arrow) return PortableScrollBarPart.Increment;
            if (axis < Start) return PortableScrollBarPart.PageDecrement;
            if (axis >= Start + Thumb) return PortableScrollBarPart.PageIncrement;
            return PortableScrollBarPart.Thumb;
        }
    }

    // Integer MulDiv-style nearest rounding, including signed short-track
    // displacement and the native zero-span result. Source values stay intact.
    private static int RoundedScrollProduct(long first, long second, long divisor)
    {
        if (divisor == 0)
            return -1;
        long product = checked(first * second);
        long rounded = (Math.Abs(product) + divisor / 2) / divisor;
        return checked((int)(product < 0 ? -rounded : rounded));
    }
}
#endif
