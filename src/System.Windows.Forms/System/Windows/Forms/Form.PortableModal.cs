// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using LibreWinForms.Platform;

namespace System.Windows.Forms;

public partial class Form
{
    private PortableModalCompletion? _portableModalCompletion;

    internal PortableModalCompletion AttachPortableModalCompletion()
    {
        if (_portableModalCompletion is { IsSourceReleased: false })
            throw new InvalidOperationException("The previous native dialog is still releasing its source frame.");
        return _portableModalCompletion = new();
    }

    internal void BeginPortableNativeDialog()
    {
        _portableModalCompletion?.Begin(PortableModalWindow);
    }

    private void CompletePortableModalDialog(Action completed)
    {
        if (_portableModalCompletion is { } completion) completion.AfterSourceRelease(completed);
        else completed();
    }

    private void CleanupPortableModalDialog(
        nint cleanupHandle,
        Form? oldOwner,
        Form? portableOwner,
        nint portableOwnerHandle,
        LibreHandle externalPortableOwner,
        bool restoreExternalOwnerEnabled)
    {
        if (IsHandleCreated && Handle != cleanupHandle)
        {
            throw new InvalidOperationException("Native dialog completion cannot retire a replacement source handle.");
        }

        try
        {
            if (restoreExternalOwnerEnabled)
            {
                LibrePlatform.Current.ExternalWindowOwners.TrySetEnabled(
                    externalPortableOwner,
                    enabled: true);
            }

            if (portableOwner is { IsDisposed: false, Visible: true, IsHandleCreated: true }
                && portableOwner.Handle == portableOwnerHandle)
            {
                portableOwner.Activate();
            }
            else if (!externalPortableOwner.IsNull)
            {
                LibrePlatform.Current.ExternalWindowOwners.TryActivate(externalPortableOwner);
            }

            SetVisibleCore(false);
            if (IsHandleCreated)
            {
                if (OwnerInternal is not null && OwnerInternal.IsMdiContainer)
                {
                    OwnerInternal.Invalidate(true);
                    OwnerInternal.Update();
                }

                DestroyHandle();
            }

            SetState(States.Modal, false);
        }
        finally
        {
            Owner = oldOwner;
            Properties.RemoveValue(s_propDialogOwner);
        }
    }
}

// Native completion and source-stack completion are independent proofs. Source
// restoration cannot follow a returned Dispose or pop a newer modal frame.
internal sealed class PortableModalCompletion
{
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private ILibreModalWindow? _window;
    private List<Action>? _sourceCompletions;
    internal bool IsSourceReleased { get; private set; }

    internal void Begin(ILibreModalWindow? window)
    {
        VerifyAccess();
        if (_window is not null || IsSourceReleased)
            throw new InvalidOperationException("A source modal generation cannot begin twice.");
        _window = window; // Retain before provider Begin can call source code.
        if (window is not null && !window.BeginModalDialog()) _window = null;
    }

    internal void ReleaseNative(Action completed)
    {
        VerifyAccess();
        if (_window is { } window) window.ReleaseModalDialog(completed);
        else completed();
    }

    internal void AfterSourceRelease(Action completed)
    {
        VerifyAccess();
        if (IsSourceReleased) completed();
        else (_sourceCompletions ??= []).Add(completed);
    }

    internal void ReleaseSource()
    {
        VerifyAccess();
        IsSourceReleased = true;
        _window = null;
        List<Action>? callbacks = _sourceCompletions;
        _sourceCompletions = null;
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? first = null;
        if (callbacks is not null)
        {
            foreach (Action callback in callbacks)
            {
                try { callback(); }
                catch (Exception failure) { first ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure); }
            }
        }

        first?.Throw();
    }

    private void VerifyAccess()
    {
        if (_threadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Source modal restoration belongs to its creating thread.");
    }
}
#endif
