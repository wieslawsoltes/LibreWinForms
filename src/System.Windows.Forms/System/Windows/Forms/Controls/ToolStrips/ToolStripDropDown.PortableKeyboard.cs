// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
namespace System.Windows.Forms;

public partial class ToolStripDropDown
{
    [ThreadStatic]
    private static PortableKeyboardContinuation? s_portableKeyboardContinuation;

    internal static ToolStrip? GetPortableKeyboardTarget(Control? recipient = null)
    {
        if (GetPortableActiveDropDown(recipient) is { } menu)
            return menu;
        if (s_portableKeyboardContinuation is { IsLive: true } continuation
            && (recipient is null || ReferenceEquals(recipient, continuation.Owner)))
            return continuation.Strip;
        return null;
    }

    internal static bool IsPortableMenuInputAncestor(ToolStrip? parent)
    {
        if (parent is not { Visible: true, IsHandleCreated: true, IsDisposed: false, Disposing: false })
            return false;

        // MenuTimer also transitions siblings on an ancestor while its child
        // is active. Match that actual chain, not another Form's menu or a
        // persistent popup that has never entered portable menu input mode.
        ToolStrip? active = GetPortableKeyboardTarget();
        while (active is not null)
        {
            if (ReferenceEquals(active, parent))
                return true;
            active = active is ToolStripDropDown dropdown ? dropdown.OwnerToolStrip : null;
        }

        return false;
    }

    internal static bool SetPortableKeyboardContinuation(ToolStrip strip, bool requireMainMenu = false)
    {
        if (s_portableKeyboardContinuation is { IsLive: true } current && ReferenceEquals(current.Strip, strip))
            return true;
        ClearPortableKeyboardContinuation();
        if (s_portableKeyboardContinuation is not null
            || strip.IsDisposed || strip.Disposing || !strip.Visible || !strip.IsHandleCreated
            || strip.FindForm() is not { IsDisposed: false, Disposing: false, Visible: true } owner)
            return false;
        PortableKeyboardContinuation continuation = new(strip, owner);
        s_portableKeyboardContinuation = continuation;
        continuation.Attach();
        try
        {
            strip.KeyboardActive = true;
        }
        finally
        {
            if (ReferenceEquals(s_portableKeyboardContinuation, continuation)
                && (!continuation.IsLive || (requireMainMenu && (!strip.Enabled || !owner.IsPortableActivationOwner
                    || !ReferenceEquals(ToolStripManager.GetMainMenuStrip(owner), strip)))))
                ClearPortableKeyboardContinuation();
        }

        return ReferenceEquals(s_portableKeyboardContinuation, continuation) && continuation.IsLive;
    }

    internal static void ClearPortableKeyboardContinuation()
    {
        PortableKeyboardContinuation? continuation = s_portableKeyboardContinuation;
        s_portableKeyboardContinuation = null;
        if (continuation is not null)
        {
            continuation.Detach();
            // MenuDeactivate is public and can throw or establish another
            // continuation. It cannot retain or clear this retired lease.
            continuation.Strip.KeyboardActive = false;
        }
    }

    internal readonly struct PortableMenuKeyRelease
    {
        private readonly Form _owner;
        private readonly MenuStrip? _mainMenu;
        private readonly nint _mainMenuHandle;
        private readonly PortableKeyboardContinuation? _continuation;
        private readonly ToolStripDropDown? _dropDown;

        internal PortableMenuKeyRelease(Form owner)
        {
            _owner = owner;
            _mainMenu = ToolStripManager.GetMainMenuStrip(owner);
            _mainMenuHandle = _mainMenu is { IsHandleCreated: true } ? _mainMenu.Handle : 0;
            _continuation = s_portableKeyboardContinuation;
            _dropDown = GetPortableActiveDropDown(owner);
        }

        internal void Process()
        {
            // KeyUp filters and handlers can establish another menu without
            // sending input or moving focus. Default processing owns only the
            // exact menu/lease present before those callbacks.
            if (_dropDown is not null || GetPortableActiveDropDown(_owner) is not null
                || !ReferenceEquals(s_portableKeyboardContinuation, _continuation)
                || !ReferenceEquals(ToolStripManager.GetMainMenuStrip(_owner), _mainMenu)
                || (_mainMenu is not null && (!_mainMenu.IsHandleCreated || _mainMenu.Handle != _mainMenuHandle)))
                return;

            if (_continuation is { IsLive: true } continuation && ReferenceEquals(continuation.Owner, _owner))
            {
                continuation.Strip.NotifySelectionChange(item: null);
                // Deselecting paints and raises application callbacks. A new
                // lease, even for the same strip, belongs to that callback.
                if (ReferenceEquals(s_portableKeyboardContinuation, continuation))
                {
                    continuation.Strip.ResetPortableMenuKeyState();
                    ClearPortableKeyboardContinuation();
                }

                return;
            }

            if (_mainMenu is { Visible: true, Enabled: true, IsHandleCreated: true, IsDisposed: false, Disposing: false }
                && ReferenceEquals(_mainMenu.FindForm(), _owner))
                _mainMenu.OnMenuKey();
        }
    }

    private sealed class PortableKeyboardContinuation(ToolStrip strip, Form owner)
    {
        internal ToolStrip Strip { get; } = strip;
        internal Form Owner { get; } = owner;
        internal bool IsLive => Strip is { IsDisposed: false, Disposing: false, Visible: true, IsHandleCreated: true }
            && Owner is { IsDisposed: false, Disposing: false, Visible: true, IsHandleCreated: true }
            && ReferenceEquals(Strip.FindForm(), Owner);

        internal void Attach()
        {
            Strip.Disposed += End;
            Strip.HandleDestroyed += End;
            Strip.VisibleChanged += VisibilityChanged;
            Owner.Deactivate += End;
            Owner.HandleDestroyed += End;
            Owner.VisibleChanged += VisibilityChanged;
        }

        internal void Detach()
        {
            Strip.Disposed -= End;
            Strip.HandleDestroyed -= End;
            Strip.VisibleChanged -= VisibilityChanged;
            Owner.Deactivate -= End;
            Owner.HandleDestroyed -= End;
            Owner.VisibleChanged -= VisibilityChanged;
        }

        private void VisibilityChanged(object? sender, EventArgs e)
        {
            if (!IsLive)
                End(sender, e);
        }

        private void End(object? sender, EventArgs e)
        {
            if (ReferenceEquals(s_portableKeyboardContinuation, this))
                ClearPortableKeyboardContinuation();
        }
    }
}
#endif
