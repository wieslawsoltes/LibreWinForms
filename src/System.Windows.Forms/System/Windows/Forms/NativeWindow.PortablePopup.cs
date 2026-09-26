// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Runtime.ExceptionServices;
using LibreWinForms.Platform;

namespace System.Windows.Forms;

public unsafe partial class NativeWindow
{
    private NativeWindow? _portablePopupOwner;
    private HashSet<NativeWindow>? _portableOwnedPopups;
    private int _portableOwnerTransitionDepth;
    private bool _portablePopupClosing;

    private void CreatePortablePopupWindow(
        LibrePlatformServices services, CreateParams parameters, ToolStripDropDown dropDown)
    {
        LibreRectangle bounds = new(parameters.X, parameters.Y,
            Math.Max(1, parameters.Width), Math.Max(1, parameters.Height));
        LibreWindowCoordinateMode coordinates = ScaleHelper.IsThreadPerMonitorV2Aware
            ? LibreWindowCoordinateMode.DevicePixels : LibreWindowCoordinateMode.Logical;
        double scale = coordinates == LibreWindowCoordinateMode.DevicePixels
            ? services.Monitors.GetNearest(bounds).DpiScale : 1d;
        // Source dropdown bounds are already arranged in the active coordinate
        // mode. Do not apply Form's initial autoscaling to them a second time.
        _portableWindow = CreatePortableWindow(services, new LibreWindowCreateOptions(
            parameters.Caption ?? string.Empty, bounds,
            LibreWindowOptions.Popup | LibreWindowOptions.ToolWindow
                | (dropDown.PortablePopupTopMost ? LibreWindowOptions.TopMost : LibreWindowOptions.None),
            default, coordinates, scale, LibreWindowState.Normal,
            ShowInTaskbar: false, CanMinimize: false, CanMaximize: false,
            MinimumSize: default, MaximumSize: default, CanClose: false));
        _portableHandle = _portableWindow.Handle;
        _portableCoordinateMode = _portableWindow.CoordinateMode;
        _portablePresentationScale = _portableWindow.DpiScale;
    }

    internal void SetPortablePopupOwner(Form owner)
    {
        if (_portablePopupClosing)
            throw new InvalidOperationException("The popup handle is being destroyed.");
        if (owner.IsDisposed || owner.Disposing || !owner.IsHandleCreated || !owner.Visible
            || owner.WindowState == FormWindowState.Minimized
            || FromHandle(owner.Handle) is not NativeWindow nativeOwner
            || nativeOwner._portableWindow is null || nativeOwner._portableOwnerTransitionDepth != 0)
        {
            throw new InvalidOperationException("A dropdown requires a live, visible native Form owner.");
        }

        if (ReferenceEquals(_portablePopupOwner, nativeOwner))
            return;
        ILibreWindow window = _portableWindow
            ?? throw new InvalidOperationException("The dropdown platform window is unavailable.");
        ILibreWindow ownerWindow = nativeOwner._portableWindow;
        LibreHandle ownerHandle = nativeOwner._portableHandle;
        bool visible = window.Visible;
        try
        {
            if (visible)
                window.Hide();
            // Native admission can synchronously call back into source code.
            // Until it returns, this popup is not in the replacement owner's
            // lifetime set, so owner teardown cannot release it for us.
            window.Owner = ownerHandle;
            if (!ReferenceEquals(window, _portableWindow) || _portablePopupClosing
                || owner.IsDisposed || owner.Disposing || !owner.IsHandleCreated || !owner.Visible
                || owner.WindowState == FormWindowState.Minimized
                || !ReferenceEquals(FromHandle(owner.Handle), nativeOwner)
                || !ReferenceEquals(nativeOwner._portableWindow, ownerWindow)
                || nativeOwner._portableHandle != ownerHandle
                || nativeOwner._portableOwnerTransitionDepth != 0)
            {
                throw new InvalidOperationException("The native dropdown owner changed during popup admission.");
            }

            DetachPortablePopupOwner();
            _portablePopupOwner = nativeOwner;
            (nativeOwner._portableOwnedPopups ??= []).Add(this);
            if (visible)
                window.Show();
        }
        catch (Exception failure)
        {
            // A backend can already have raised Closed before throwing. Never
            // destroy a replacement generation created by that callback.
            if (ReferenceEquals(window, _portableWindow))
            {
                try { ClosePortablePopupWindow(disposeWindow: true); }
                catch (Exception cleanupFailure) { failure.Data[nameof(SetPortablePopupOwner)] = cleanupFailure; }
            }

            throw;
        }
    }

    private void DetachPortablePopupOwner()
    {
        _portablePopupOwner?._portableOwnedPopups?.Remove(this);
        _portablePopupOwner = null;
    }

    private ExceptionDispatchInfo? ClosePortableOwnedPopups()
    {
        if (_portableOwnedPopups is not { Count: > 0 } popups)
            return null;
        ExceptionDispatchInfo? failure = null;
        foreach (NativeWindow popup in popups.ToArray())
        {
            try
            {
                popup.ClosePortablePopupWindow(disposeWindow: true);
            }
            catch (Exception exception)
            {
                failure ??= ExceptionDispatchInfo.Capture(exception);
            }
        }

        return failure;
    }

    private void ClosePortablePopupWindow(bool disposeWindow)
    {
        if (_portablePopupClosing)
            return;
        _portablePopupClosing = true;
        _portableWindowEvents = null;
        ToolStripDropDown? popup = !HWND.IsNull && this is Control.ControlNativeWindow controlWindow
            ? controlWindow.GetControl() as ToolStripDropDown : null;
        bool wasVisible = false;
        ExceptionDispatchInfo? failure = null;
        try
        {
            try
            {
                if (popup is not null)
                    wasVisible = popup.OnPortablePopupWindowDestroying();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }

            try
            {
                ReleasePortableHandle(disposeWindow);
            }
            catch (Exception exception)
            {
                failure ??= ExceptionDispatchInfo.Capture(exception);
            }

            try
            {
                // The old handle is fully released. A Closed handler may now
                // show this source object with a new handle and another owner;
                // its own failure/Closed callback must not be suppressed.
                _portablePopupClosing = false;
                if (wasVisible)
                    popup?.OnPortablePopupWindowClosed();
            }
            catch (Exception exception)
            {
                failure ??= ExceptionDispatchInfo.Capture(exception);
            }
        }
        finally
        {
            _portablePopupClosing = false;
        }

        failure?.Throw();
    }
}
#endif
