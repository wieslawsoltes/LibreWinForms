// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE

namespace System.Windows.Forms;

public partial class ToolStripDropDown
{
    internal static void ProcessPortablePointerDown(Control? source, Point screenPosition)
    {
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

            active.SetCloseReason(ToolStripDropDownCloseReason.AppClicked);
            active.Visible = false;
            if (GetPortableActiveDropDown() is null)
                active.OwnerItem?.Unselect();
        }
    }
}

#endif
