// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
namespace System.Windows.Forms;

public partial class ToolStripDropDown
{
    private PortableHostedFocus? _portableHostedFocus;

    internal Form? PortableHostedFocusOwner => _portableHostedFocus is { IsLive: true } focus ? focus.Owner : null;
    internal Control? PortableHostedFocusRestoreTarget => _portableHostedFocus?.RestoreTarget;

    internal Control GetPortableHostedKeyboardTarget()
        => _portableHostedFocus is { IsLive: true } focus
            && ReferenceEquals(focus.Owner.PortableFocusedControl, focus.Target) ? focus.Target : this;

    // A composite owner may retain its actual editor in the Form while showing
    // a nonactivating list. This input target is distinct from a hosted focus
    // lease: overrides must validate their source/owner generations and actual
    // Form focus. Ordinary menus retain the original hosted-membership checks.
    internal virtual Control GetPortableKeyboardInputTarget()
        => GetPortableHostedKeyboardTarget();

    internal bool FocusPortableHostedControl(Control target)
    {
        if (!Visible || !IsHandleCreated || IsDisposed || Disposing
            || _portableActivationRoot?._portableActivationOwner is not { IsPortableActivationOwner: true } owner
            || !ContainsPortableHostedControl(target))
            return false;

        if (_portableHostedFocus is { IsLive: true } current && ReferenceEquals(current.Target, target))
        {
            owner.SetPortableHostedControlFocus(target);
            return target.Focused;
        }

        Control? restore = owner.GetPortableFocusRestoreTarget();
        PortableHostedFocus? previous = _portableHostedFocus;
        _portableHostedFocus = null;
        previous?.Detach();
        PortableHostedFocus focus = new(this, owner, target, restore);
        _portableHostedFocus = focus;
        focus.Attach();
        try
        {
            owner.SetPortableHostedControlFocus(target);
        }
        finally
        {
            if (ReferenceEquals(_portableHostedFocus, focus) && !focus.IsLive)
                focus.End();
        }

        return ReferenceEquals(_portableHostedFocus, focus) && target.Focused;
    }

    private bool ContainsPortableHostedControl(Control target)
    {
        foreach (ToolStripItem item in Items)
        {
            if (item is ToolStripControlHost host && ReferenceEquals(host.ParentInternal, this)
                && host.Visible && host.Enabled && (ReferenceEquals(host.Control, target) || host.Control.Contains(target)))
                return true;
        }

        return false;
    }

    private sealed class PortableHostedFocus(ToolStripDropDown menu, Form owner, Control target, Control? restoreTarget)
    {
        private readonly nint _menuHandle = menu.Handle;
        private readonly nint _ownerHandle = owner.Handle;
        private readonly List<Control> _controls = CaptureFocusPath(menu, target);
        internal Form Owner { get; } = owner;
        internal Control Target { get; } = target;
        internal Control? RestoreTarget { get; } = restoreTarget;
        internal bool IsLive => menu is { IsDisposed: false, Disposing: false, Visible: true, IsHandleCreated: true }
            && menu.Handle == _menuHandle
            && Owner is { IsDisposed: false, Disposing: false, Visible: true, IsHandleCreated: true, IsPortableActivationOwner: true, PortableHasWindowFocus: true }
            && Owner.Handle == _ownerHandle
            && Target is { IsDisposed: false, Disposing: false, Visible: true, Enabled: true, IsHandleCreated: true }
            && menu.ContainsPortableHostedControl(Target);

        internal void Attach()
        {
            foreach (Control control in _controls)
                control.PortableHostedFocusLifetimeChanged += Changed;
            Owner.PortableHostedFocusLifetimeChanged += Changed;
        }

        internal void Detach()
        {
            foreach (Control control in _controls)
                control.PortableHostedFocusLifetimeChanged -= Changed;
            Owner.PortableHostedFocusLifetimeChanged -= Changed;
        }

        private void Changed(bool retiring)
        {
            if (retiring || !IsLive)
                End();
        }

        private static List<Control> CaptureFocusPath(ToolStripDropDown menu, Control target)
        {
            List<Control> controls = [];
            for (Control? control = target; control is not null; control = control.ParentInternal)
            {
                controls.Add(control);
                if (ReferenceEquals(control, menu))
                    break;
            }

            return controls;
        }

        internal void End()
        {
            if (!ReferenceEquals(menu._portableHostedFocus, this))
                return;
            menu._portableHostedFocus = null;
            Detach();
            // Publish retirement before LostFocus/GotFocus callbacks can open a
            // replacement popup or deliberately choose another source control.
            Owner.RestorePortableHostedControlFocus(Target, RestoreTarget);
        }
    }
}
#endif
