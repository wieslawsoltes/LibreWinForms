// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
namespace System.Windows.Forms;

public partial class ToolStripDropDown
{
    [ThreadStatic]
    private static List<ToolStripDropDown>? s_portableActivationRoots;

    internal static ToolStripDropDown? GetPortableActiveDropDown(Control? recipient = null)
    {
        if (s_portableActivationRoots is not { } roots)
            return null;
        for (int rootIndex = roots.Count - 1; rootIndex >= 0; rootIndex--)
        {
            ToolStripDropDown root = roots[rootIndex];
            if (recipient is not null
                && !ReferenceEquals(root._portableActivationOwner, recipient)
                && !(recipient is ToolStripDropDown dropDown
                    && ReferenceEquals(dropDown._portableActivationRoot, root)))
                continue;
            if (root._portableActivationMenus is not { } menus)
                continue;
            for (int menuIndex = menus.Count - 1; menuIndex >= 0; menuIndex--)
            {
                if (menus[menuIndex] is ToolStripDropDown menu && IsPortableInputMenu(menu))
                    return menu;
            }
        }

        return null;
    }

    internal static int GetPortableActiveDropDownCount()
    {
        int count = 0;
        if (s_portableActivationRoots is { } roots)
        {
            foreach (ToolStripDropDown root in roots)
            {
                if (root._portableActivationMenus is not { } menus)
                    continue;
                foreach (ToolStrip menu in menus)
                {
                    if (menu is ToolStripDropDown dropDown && IsPortableInputMenu(dropDown))
                        count++;
                }
            }
        }

        return count;
    }

    private static bool IsPortableInputMenu(ToolStripDropDown menu)
        => menu.AutoClose && menu.Visible && menu.IsHandleCreated && !menu.IsDisposed && !menu.Disposing
            && menu._portableActivationRoot?._portableActivationOwner is
                { IsDisposed: false, Disposing: false, Visible: true, IsHandleCreated: true };
}
#endif
