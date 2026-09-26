// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE

using System.ComponentModel;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

public partial class ToolStripDropDown
{
    private ToolStripDropDown? _portableActivationRoot;
    private Form? _portableActivationOwner;
    private List<ToolStrip>? _portableActivationMenus;
    private List<ToolStripDropDown>? _portableActivationMembers;
    private int _portableOpeningDepth;
    private bool _portableActivationClosePending;

    internal bool PortablePopupTopMost => TopMost;

    private void BindPortablePopupOwner(ToolStripDropDown root)
    {
        if (!TopLevel)
            return;
        Form owner = root._portableActivationOwner
            ?? throw new InvalidOperationException("A top-level dropdown requires a native Form owner.");
        _ = Handle;
        SetPortablePopupOwner(owner);
        if (ReferenceEquals(root, this) && root._portableActivationMembers is { } members)
        {
            foreach (ToolStripDropDown member in members.ToArray())
            {
                if (member != this && member.TopLevel && member.Visible)
                    member.SetPortablePopupOwner(owner);
            }
        }
    }

    internal bool OnPortablePopupWindowDestroying()
    {
        // Native owner teardown is not a cancelable close request. The native
        // resource must be released; retain the source object for later reuse.
        bool visible = Visible;
        SetState(States.Visible, false);
        SetState(States.Created, false);
        ReleasePortableActivation();
        OwnerToolStrip?.ActiveDropDowns.Remove(this);
        ActiveDropDowns.Clear();
        CancelPortableCapture(updateCursor: false);
        OnHandleDestroyed(EventArgs.Empty);
        return visible;
    }

    internal void OnPortablePopupWindowClosed()
    {
        OnVisibleChanged(EventArgs.Empty);
        OnClosed(new ToolStripDropDownClosedEventArgs(ToolStripDropDownCloseReason.AppFocusChange));
    }

    private ToolStripDropDown BeginPortableActivationOpening()
    {
        ToolStripDropDown first = GetFirstDropDown();
        ToolStripDropDown root = first._portableActivationRoot
            ?? (first._portableActivationMembers is { Count: > 0 } ? first : this);
        _portableActivationRoot = root;
        (root._portableActivationMembers ??= []).Add(this);
        root._portableOpeningDepth++;
        (s_portableActivationRoots ??= []).Remove(root);
        s_portableActivationRoots.Add(root);
        if (ReferenceEquals(root, this))
        {
            // Like the native foreground snapshot, retain ownership before
            // Opening callbacks can activate another window. A source owner
            // takes precedence over the currently active, unrelated Form.
            Form? owner = SourceControlInternal?.FindForm() ?? OwnerToolStrip?.FindForm();
            if (owner is null && SourceControlInternal is null && OwnerToolStrip is null)
                owner = Form.ActiveForm;
            if (root._portableActivationOwner is Form previousOwner)
                previousOwner.Deactivate -= root.OnPortableOwnerDeactivated;
            root._portableActivationOwner = owner;
            if (owner is Form activationOwner)
                activationOwner.Deactivate += root.OnPortableOwnerDeactivated;
        }

        return root;
    }

    private void OnPortableOwnerDeactivated(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _portableActivationOwner))
            return;
        _portableActivationClosePending = true;
        ClosePortableActivationMenus();
    }

    private void ClosePortableActivationMenus()
    {
        if (_portableOpeningDepth != 0 || !_portableActivationClosePending)
            return;
        _portableActivationClosePending = false;
        // Preserve the native bounded, deepest-active-first close policy,
        // including AutoClose admission and repeated canceled closing attempts.
        ToolStripManager.ModalMenuFilter.CloseDropDownsForActivationChange(
            _portableActivationMenus?.Count ?? 0,
            () => _portableActivationMenus is { Count: > 0 } menus ? menus[^1] : null);
    }

    private void EndPortableActivationOpening(ToolStripDropDown root, bool completed)
    {
        root._portableOpeningDepth--;
        if (!Visible)
            ReleasePortableActivation();
        // Never replace a user Opening/Opened exception with another callback
        // exception while unwinding. The visible chain retains its owner.
        if (completed)
            root.ClosePortableActivationMenus();
    }

    private void ReleasePortableActivation(bool disposing = false)
    {
        ToolStripDropDown? root = _portableActivationRoot
            ?? (_portableActivationMembers is { Count: > 0 } ? this : null);
        _portableActivationRoot = null;
        root?._portableActivationMenus?.Remove(this);
        root?._portableActivationMembers?.Remove(this);
        // A hidden/canceled root can still own a visible child. Retain the
        // chain's owner until its last opening/visible member has released it.
        if (root is not null && (root._portableActivationMembers is not { Count: > 0 }
            || (disposing && ReferenceEquals(root, this))))
        {
            if (root._portableActivationOwner is Form owner)
                owner.Deactivate -= root.OnPortableOwnerDeactivated;
            root._portableActivationOwner = null;
            if (root._portableActivationMembers is { } members)
            {
                foreach (ToolStripDropDown member in members)
                    member._portableActivationRoot = null;
                members.Clear();
            }

            root._portableActivationMenus?.Clear();
            root._portableActivationClosePending = false;
            s_portableActivationRoots?.Remove(root);
        }
    }

    private void SetVisibleCorePortable(bool visible)
    {
        if (_state[s_stateInSetVisibleCore] || visible == Visible)
        {
            return;
        }

        _state[s_stateInSetVisibleCore] = true;
        ToolStripDropDown? openingRoot = null;
        bool openingCompleted = false;
        try
        {
            if (visible)
            {
                openingRoot = BeginPortableActivationOpening();
                if (LayoutRequired)
                {
                    LayoutTransaction.DoLayout(this, this, PropertyNames.Visible);
                }

                CancelEventArgs openingEventArgs = new(cancel: DisplayedItems.Count == 0);
                OnOpening(openingEventArgs);
                if (openingEventArgs.Cancel)
                {
                    openingCompleted = true;
                    return;
                }

                ObjectDisposedException.ThrowIf(IsDisposed, this);
                BindPortablePopupOwner(openingRoot);

                try
                {
                    if (AutoClose)
                        (openingRoot._portableActivationMenus ??= []).Add(this);
                    if (OwnerToolStrip is not null)
                    {
                        OwnerToolStrip.ActiveDropDowns.Add(this);
                        OwnerToolStrip.SnapMouseLocation();
                        if (OwnerToolStrip.Capture)
                        {
                            Capture = true;
                        }
                    }

                    base.SetVisibleCore(visible);
                    if (TopLevel)
                        ApplyTopMost(true);
                }
                finally
                {
                    // Native admission can synchronously destroy the rejected
                    // window before Show throws. Do not publish Opened (or mask
                    // that failure) for a surface that no longer exists. A
                    // visible, admitted window retains the original callback
                    // behavior if a later managed visibility callback throws.
                    if (Visible && IsHandleCreated)
                        OnOpened(EventArgs.Empty);
                }

                openingCompleted = true;
                return;
            }

            ToolStripDropDownCloseReason reason = _closeReason;
            ResetCloseReason();

            ToolStripDropDownClosingEventArgs closingEventArgs = new(reason)
            {
                Cancel = reason != ToolStripDropDownCloseReason.CloseCalled && !AutoClose,
            };
            OnClosing(closingEventArgs);
            if (closingEventArgs.Cancel)
            {
                return;
            }

            DismissActiveDropDowns();
            CancelAutoExpand();

            try
            {
                base.SetVisibleCore(visible);
            }
            finally
            {
                if (!Visible)
                    ReleasePortableActivation();
                OwnerToolStrip?.ActiveDropDowns.Remove(this);
                ActiveDropDowns.Clear();
                if (Capture)
                {
                    Capture = false;
                }
            }

            OnClosed(new ToolStripDropDownClosedEventArgs(reason));

            if (!_saveSourceControl)
            {
                SourceControlInternal = null;
            }
        }
        finally
        {
            _state[s_stateInSetVisibleCore] = false;
            _saveSourceControl = false;
            if (openingRoot is not null)
                EndPortableActivationOpening(openingRoot, openingCompleted);
        }
    }
}

#endif
