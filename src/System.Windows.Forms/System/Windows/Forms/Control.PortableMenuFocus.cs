// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
namespace System.Windows.Forms;

public partial class Control
{
    private Control GetPortableFocusRoot()
    {
        Control root = GetPortableTopLevelControl();
        return root is ToolStripDropDown { PortableHostedFocusOwner: { } owner } ? owner : root;
    }

    internal Control? PortableFocusedControl => _portableFocusedControl;

    internal Control? GetPortableFocusRestoreTarget()
        => _portableFocusedControl?.GetPortableTopLevelControl() is ToolStripDropDown dropDown
            ? dropDown.PortableHostedFocusRestoreTarget : _portableFocusedControl;

    internal void SetPortableHostedControlFocus(Control target) => SetPortableFocus(target);

    internal void RestorePortableHostedControlFocus(Control expected, Control? restore)
    {
        if (!ReferenceEquals(_portableFocusedControl, expected))
            return;
        Control target = restore is { IsDisposed: false, Disposing: false, IsHandleCreated: true, Visible: true, Enabled: true }
            && ReferenceEquals(restore.GetPortableTopLevelControl(), this) ? restore : this;
        SetPortableFocus(target);
    }
}
#endif
