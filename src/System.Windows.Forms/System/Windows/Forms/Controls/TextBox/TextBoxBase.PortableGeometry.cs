// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
namespace System.Windows.Forms;

public abstract partial class TextBoxBase
{
    private nint SendPortableTextQuery(uint message, WPARAM wParam = default, LPARAM lParam = default)
    {
        Message query = Message.Create(Handle, (int)message, unchecked((nint)wParam.Value), lParam.Value);
        WndProc(ref query);
        return query.Result;
    }

    private protected virtual nint QueryPortableTextGeometry(uint message, nint wParam, nint lParam)
        => throw new PlatformNotSupportedException("This text control does not provide retained source geometry.");
}
#endif
