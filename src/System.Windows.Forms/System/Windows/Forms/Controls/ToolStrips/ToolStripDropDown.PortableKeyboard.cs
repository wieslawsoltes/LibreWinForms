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

    private static void SetPortableKeyboardContinuation(ToolStrip strip)
    {
        if (s_portableKeyboardContinuation is { IsLive: true } current && ReferenceEquals(current.Strip, strip))
            return;
        ClearPortableKeyboardContinuation();
        if (strip.IsDisposed || strip.Disposing || !strip.Visible || !strip.IsHandleCreated
            || strip.FindForm() is not { IsDisposed: false, Disposing: false, Visible: true } owner)
            return;
        PortableKeyboardContinuation continuation = new(strip, owner);
        s_portableKeyboardContinuation = continuation;
        continuation.Attach();
        try
        {
            strip.KeyboardActive = true;
        }
        finally
        {
            if (ReferenceEquals(s_portableKeyboardContinuation, continuation) && !continuation.IsLive)
                ClearPortableKeyboardContinuation();
        }
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
