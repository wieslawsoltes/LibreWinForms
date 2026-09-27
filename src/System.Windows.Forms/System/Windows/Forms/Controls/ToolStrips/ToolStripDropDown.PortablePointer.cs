// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE

namespace System.Windows.Forms;

public partial class ToolStripDropDown
{
    internal static void ProcessPortablePointerDown(Control? source, Point screenPosition)
    {
        PortableKeyboardContinuation? continuation = s_portableKeyboardContinuation;
        // Preserve ModalMenuFilter.ProcessMouseButtonPressed's initial bound,
        // live deepest-leaf lookup, client containment and owner-button exception.
        // Only coordinate conversion and the active queue use portable source
        // ownership; canonical visibility/Closing/Closed still perform dismissal.
        int count = GetPortableActiveDropDownCount();
        for (int index = 0; index < count; index++)
        {
            ToolStripDropDown? active = GetPortableActiveDropDown();
            if (active is null || active.ClientRectangle.Contains(active.PointToClient(screenPosition)))
                break;

            if (active.OwnerToolStrip is ToolStrip ownerStrip && ReferenceEquals(source, ownerStrip)
                && active.OwnerDropDownItem is ToolStripDropDownItem ownerItem
                && ownerItem.DropDownButtonArea.Contains(ownerStrip.PointToClient(screenPosition)))
            {
                // Let the real owner item perform its existing hide/show toggle.
                continue;
            }

            if (source is ComboBox combo && combo.IsPortableDropDownOwner(active)
                && combo.ClientRectangle.Contains(combo.PointToClient(screenPosition)))
            {
                // Its actual source MouseDown owns the close/open toggle.
                continue;
            }

            active.SetCloseReason(ToolStripDropDownCloseReason.AppClicked);
            active.Visible = false;
            if (GetPortableActiveDropDown() is null)
                active.OwnerItem?.Unselect();
        }

        // Escape can leave the top-level MenuStrip active without a dropdown.
        // Match the native filter's outside-press deselection/exit, but only for
        // the lease present when this input began, never a callback replacement.
        if (continuation is { IsLive: true }
            && ReferenceEquals(s_portableKeyboardContinuation, continuation)
            && GetPortableActiveDropDown() is null
            && !continuation.Strip.ClientRectangle.Contains(continuation.Strip.PointToClient(screenPosition)))
        {
            continuation.Strip.NotifySelectionChange(item: null);
            if (ReferenceEquals(s_portableKeyboardContinuation, continuation))
                ToolStripManager.ModalMenuFilter.ExitMenuMode();
        }
    }
}

#endif
