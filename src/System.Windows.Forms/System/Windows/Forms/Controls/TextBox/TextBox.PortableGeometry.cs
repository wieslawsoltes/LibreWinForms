// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using LibreWinForms.Platform;

namespace System.Windows.Forms;

public partial class TextBox
{
    private protected override nint QueryPortableTextGeometry(uint message, nint wParam, nint lParam)
    {
        int index = unchecked((int)wParam);
        // Single-line EDIT controls retain their original zero line result,
        // including EM_LINEINDEX requests beyond zero.
        if (!Multiline && message is PInvokeCore.EM_LINEFROMCHAR or PInvokeCore.EM_LINEINDEX)
        {
            return 0;
        }

        Point point = new(PARAM.SignedLOWORD(lParam), PARAM.SignedHIWORD(lParam));
        if (message == PInvokeCore.EM_CHARFROMPOS && !ClientRectangle.Contains(point))
        {
            return -1;
        }

        int length = TextLength;
        if (length == 0)
        {
            return message switch
            {
                PInvokeCore.EM_LINEFROMCHAR or PInvokeCore.EM_CHARFROMPOS => 0,
                PInvokeCore.EM_LINEINDEX => index is -1 or 0 ? 0 : -1,
                _ => -1,
            };
        }

        if (LibrePlatform.Current.TextRenderer is not ILibreTextSourceGeometryService)
        {
            throw new PlatformNotSupportedException("Text geometry queries require a retained source-geometry provider.");
        }

        ILibreTextLayout layout = GetPortableInputLayout()
            ?? throw new PlatformNotSupportedException("Text geometry queries require a retained layout.");
        if (layout is not ILibreTextSourceGeometry source)
        {
            throw new InvalidOperationException("The text provider declared source geometry but returned a layout without it.");
        }

        switch (message)
        {
            case PInvokeCore.EM_LINEFROMCHAR:
                if (index == -1)
                {
                    return SelectionLength == 0
                        ? source.GetCaretRowIndex(PortableSelectionActiveEnd, _portableCaretTrailing)
                        : source.GetRowIndexFromTextPosition(SelectionStart);
                }

                return source.GetRowIndexFromTextPosition(index < 0 ? length : Math.Min(index, length));

            case PInvokeCore.EM_LINEINDEX:
                if (index == -1)
                {
                    index = source.GetCaretRowIndex(PortableSelectionActiveEnd, _portableCaretTrailing);
                }

                return (uint)index < (uint)source.RowCount ? source.GetRowSourceStart(index) : -1;

            case PInvokeCore.EM_POSFROMCHAR:
                if ((uint)index >= (uint)length) return -1;
                PointF position = source.GetSourcePositionPoint(index);
                Point client = Point.Truncate(new PointF(position.X - _portableTextScroll.X, position.Y - _portableTextScroll.Y));
                return PARAM.FromPoint(client);

            case PInvokeCore.EM_CHARFROMPOS:
                LibreTextHit hit = layout.HitTest(new PointF(point.X + _portableTextScroll.X, point.Y + _portableTextScroll.Y));
                int row = Multiline ? source.GetCaretRowIndex(hit.TextPosition, hit.IsTrailing) : 0;
                // Keep the native EDIT message's packed 16-bit character/line
                // result; the existing public API retains its own last-char clamp.
                return unchecked((int)((uint)(ushort)hit.TextPosition | ((uint)(ushort)row << 16)));

            default:
                throw new InvalidOperationException("Unexpected source text geometry message.");
        }
    }
}
#endif
