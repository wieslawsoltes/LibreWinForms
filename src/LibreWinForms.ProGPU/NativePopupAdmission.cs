// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;
using ProGPU.Backend;

namespace LibreWinForms.ProGPU;

/// <summary>Host lifetime around the original ProGPU native popup admission.</summary>
internal interface INativePopupAdmissionHost
{
    void VerifyAccess();
    bool Visible { get; }
    bool TryResolveOwner(LibreHandle owner, out NativeWindowHandle nativeOwner);
    bool PrepareOwner(NativeWindowHandle owner);
    bool ClearOwner();
    bool ShowOwned(NativeWindowHandle owner, Action showWithoutActivation);
    void ShowWithoutActivation();
    void Discard();
}

internal sealed class NativePopupAdmission(INativePopupAdmissionHost host)
{
    private bool _changing;

    internal LibreHandle Owner { get; private set; }

    internal static bool RequiresNonactivatingFront(NativeWindowKind kind, LibreWindowZOrder order)
    {
        if (kind != NativeWindowKind.Win32)
            return false;
        if (order != LibreWindowZOrder.Front)
            throw new PlatformNotSupportedException("Nonactivating Win32 popup ordering to the back is not supported.");
        return true;
    }

    internal void SetOwner(LibreHandle owner)
    {
        host.VerifyAccess();
        if (owner == Owner)
            return;
        if (_changing)
            throw new InvalidOperationException("Popup ownership cannot change during native admission.");
        if (host.Visible)
            throw new InvalidOperationException("Hide the popup before changing its owner.");

        NativeWindowHandle nativeOwner = NativeWindowHandle.Empty;
        if (!owner.IsNull && !host.TryResolveOwner(owner, out nativeOwner))
            throw new ArgumentException("The popup owner must resolve to a live native top-level window.", nameof(owner));

        _changing = true;
        try
        {
            bool admitted = owner.IsNull ? host.ClearOwner() : host.PrepareOwner(nativeOwner);
            if (!admitted)
                throw new PlatformNotSupportedException("The native host rejected hidden popup ownership.");
            host.VerifyAccess();
            if (host.Visible)
                throw new InvalidOperationException("Popup ownership preparation unexpectedly showed the window.");
            Owner = owner;
        }
        catch (Exception failure)
        {
            DiscardAfterFailure(failure);
            throw;
        }
        finally
        {
            _changing = false;
        }
    }

    internal void Show()
    {
        host.VerifyAccess();
        if (_changing)
            throw new InvalidOperationException("Popup visibility cannot change during native admission.");
        if (host.Visible)
            return;
        _changing = true;
        try
        {
            if (Owner.IsNull || !host.TryResolveOwner(Owner, out NativeWindowHandle nativeOwner))
                throw new InvalidOperationException("A popup requires a live native owner before it can be shown.");
            if (!host.ShowOwned(nativeOwner, host.ShowWithoutActivation))
                throw new PlatformNotSupportedException("The native host rejected nonactivating popup display.");
            host.VerifyAccess();
            if (!host.Visible)
                throw new InvalidOperationException("Native popup display did not make the owned window visible.");
        }
        catch (Exception failure)
        {
            DiscardAfterFailure(failure);
            throw;
        }
        finally
        {
            _changing = false;
        }
    }

    private void DiscardAfterFailure(Exception failure)
    {
        try
        {
            host.Discard();
        }
        catch (Exception cleanupFailure)
        {
            // Keep the failed admission/callback as the actual thrown exception.
            // Destruction still attempts every owned resource and Closed callback.
            failure.Data[nameof(NativePopupAdmission)] = cleanupFailure;
        }
    }
}
