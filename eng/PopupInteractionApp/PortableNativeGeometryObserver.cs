// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Windows.Forms;
using LibreWinForms.Platform;
using LibreWinForms.ProGPU;
using ProGPU.Backend;

namespace PopupInteractionApp;

internal sealed partial class InteractionForm
{
    partial void RecordNativeGeometry(long sequence)
    {
        if (Environment.GetEnvironmentVariable("LIBREWINFORMS_POPUP_NATIVE_GEOMETRY") != "1") return;
        if (sequence is < 1 or > 650)
            throw new InvalidOperationException("Native geometry observer snapshot budget exceeded.");

        // Same UI timer and sequence as the source snapshot. No separate pump,
        // handle creation, source mutation, rendering or native input occurs here.
        object[] windows =
        [
            ObserveNativeGeometry("main", this),
            ObserveNativeGeometry("context", _popups["context"]),
            ObserveNativeGeometry("context-child", _popups["context-child"]),
            ObserveNativeGeometry("menu", _popups["menu"]),
            ObserveNativeGeometry("menu-child", _popups["menu-child"])
        ];
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "popup-native-geometry-v1", pid = Environment.ProcessId, sequence,
            coordinateSpace = "native-desktop-top-left-points", windows
        });
        if (bytes.Length > 32 * 1024)
            throw new InvalidOperationException("Native geometry observer receipt exceeds 32 KiB.");

        string pending = Path.Combine(_directory, "native-geometry.pending");
        using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            stream.Write(bytes);
        File.Move(pending, Path.Combine(_directory, $"native-geometry-{sequence:D8}.json"));
    }

    private static object ObserveNativeGeometry(string name, Control control)
    {
        bool handleCreated = control.IsHandleCreated;
        bool visible = control.Visible;
        object? sourceHandle = null;
        string? coordinateMode = null;
        double? dpiScale = null;
        double? framebufferScale = null;
        object? geometry = null;

        object Result(string? reason, bool available = false, bool cocoaComparisonAdmitted = false)
            => new { name, visible, handleCreated, sourceHandle, coordinateMode, dpiScale,
                framebufferScale, available, cocoaComparisonAdmitted, reason, geometry };

        if (!handleCreated || control.IsDisposed || control.Disposing)
            return Result("No existing live source handle.");
        nint handle = control.Handle;
        var token = new LibreHandle(handle, LibreHandleKind.Window);
        sourceHandle = new { value = handle.ToInt64(), kind = token.Kind.ToString() };
        if (!LibrePlatform.IsRegistered)
            return Result("No registered portable platform.");
        LibrePlatformServices platform = LibrePlatform.Current;
        if (platform.Windows is not SilkWindowService service)
            return Result("The registered window service does not provide Silk native geometry.");
        if (!platform.Handles.TryGet(token, out ILibreWindow? window) || window.Handle != token)
            return Result("Source handle is not a registered native top-level window.");

        try
        {
            LibreWindowCoordinateMode mode = window.CoordinateMode;
            double dpi = window.DpiScale;
            double framebuffer = window.FramebufferScale;
            if (!double.IsFinite(dpi) || dpi <= 0 || !double.IsFinite(framebuffer) || framebuffer <= 0)
                return Result("Window coordinate policy has invalid scales.");
            bool available = service.TryGetNativeGeometrySnapshot(token, out NativeWindowGeometrySnapshot snapshot);
            LibreWindowCoordinateMode currentMode = window.CoordinateMode;
            double currentDpi = window.DpiScale;
            double currentFramebuffer = window.FramebufferScale;
            // Recheck identity after the final provider property read, too.
            if (currentMode != mode || currentDpi != dpi || currentFramebuffer != framebuffer
                || control.IsDisposed || control.Disposing || !control.IsHandleCreated || control.Handle != handle
                || !platform.Handles.TryGet(token, out ILibreWindow? current) || !ReferenceEquals(current, window)
                || current.Handle != token)
                return Result("Source/native window identity or coordinate policy changed during observation.");

            coordinateMode = mode.ToString();
            dpiScale = dpi;
            framebufferScale = framebuffer;
            if (!available)
                return Result("Native geometry is unavailable for this live window/provider.");
            geometry = new
            {
                window = new { kind = snapshot.Window.Kind.ToString(), handle = snapshot.Window.Handle.ToInt64(),
                    display = snapshot.Window.Display.ToInt64(), descriptor = snapshot.Window.Descriptor },
                contentView = snapshot.ContentView.ToInt64(), cocoaWindowNumber = snapshot.CocoaWindowNumber,
                contentBounds = NativeBounds(snapshot.ContentBounds), frameBounds = NativeBounds(snapshot.FrameBounds),
                backingScale = snapshot.BackingScale
            };
            bool admitted = snapshot.Window.Kind == NativeWindowKind.Cocoa && snapshot.BackingScale == framebuffer;
            return Result(admitted ? null : "Cocoa comparison requires an actual Cocoa snapshot and equal backing/framebuffer scales.",
                available: true, cocoaComparisonAdmitted: admitted);
        }
        catch (Exception error)
        {
            // Observational failure remains explicit evidence, never source
            // geometry or a fabricated native rectangle substituted as success.
            string message = error.Message;
            return Result("Native geometry query failed: " + message[..Math.Min(message.Length, 512)]);
        }
    }

    private static object NativeBounds(NativeWindowBounds bounds)
        => new { x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height };
}
