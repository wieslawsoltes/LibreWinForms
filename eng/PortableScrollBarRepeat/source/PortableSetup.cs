using System.Drawing;
using System.Windows.Forms;
using LibreWinForms.Platform;
using LibreWinForms.ProGPU;
using ProGPU.Backend;

internal static class PortableSetup
{
    internal static LibrePlatformServices Register()
    {
        if (LibrePlatform.IsRegistered) throw new InvalidOperationException("Unexpected earlier platform registration.");
        LibrePlatformServices source = ProGpuPlatform.CreateServices(false,
            new WgpuDx12CompilerOptions(WgpuDx12ShaderCompiler.Dxc));
        LibrePlatformServices decorated;
        try
        {
            decorated = new(source.Dispatcher, source.Timers, source.Handles, source.Windows,
                source.Monitors, source.Painting, source.DesktopCapture, source.NativeFonts,
                source.NativeGraphics, new ObservedTheme(source.VisualStyles), source.SystemSettings,
                source.TextRenderer, source.PowerStatus, source.MessageBoxes, source.ColorDialogs,
                source.FontDialogs, source.FileDialogs, source.InputLanguages, source.DragDrop,
                source.Popups, source.Clipboard);
        }
        catch { source.Dispose(); throw; }
        // All actual services transfer to the single decorated aggregate; the
        // temporary source aggregate is never separately disposed afterward.
        try { LibrePlatform.Register(decorated); }
        catch { decorated.Dispose(); throw; }
        return decorated;
    }

    internal static object Observe(Form form, List<ScrollBar> bars, string forms, long frequency)
    {
        LibrePlatformServices platform = LibrePlatform.Current;
        var token = new LibreHandle(form.Handle, LibreHandleKind.Window);
        if (platform.Windows is not SilkWindowService service
            || !platform.Handles.TryGet(token, out ILibreWindow? window) || window.Handle != token
            || !service.TryGetNativeGeometrySnapshot(token, out NativeWindowGeometrySnapshot native)
            || native.Window.Kind != NativeWindowKind.Win32 || native.Window.Handle == 0
            || native.BackingScale != 1 || window.FramebufferScale != 1 || window.DpiScale != 1 || form.DeviceDpi != 96)
            throw new InvalidOperationException("Actual owned 96-DPI Win32 geometry is required.");
        Rectangle client = form.RectangleToScreen(form.ClientRectangle);
        NativeWindowBounds bounds = native.ContentBounds;
        if (bounds.X != client.X || bounds.Y != client.Y || bounds.Width != client.Width || bounds.Height != client.Height)
            throw new InvalidOperationException("Original source/native root client rectangles differ.");
        object[] controls = bars.Select(bar =>
        {
            PaintObservation.PaintFrame paint = PaintObservation.Latest[bar];
            Rectangle screen = bar.RectangleToScreen(bar.ClientRectangle);
            if (!bar.IsHandleCreated || bar.IsDisposed || bar.Disposing || bar.Parent != form || paint.Handle != (long)bar.Handle
                || paint.Client != bar.ClientRectangle || paint.Screen != screen || !client.Contains(screen))
                throw new InvalidOperationException("Original painted control identity or geometry changed.");
            return (object)new
            {
                name = bar.Name, sourceHandle = (long)bar.Handle, sourceClass = bar is VScrollBar ? "VScrollBar" : "HScrollBar",
                client = Rect(screen), localClient = Rect(bar.ClientRectangle), bar.Minimum, bar.Maximum,
                bar.SmallChange, bar.LargeChange, bar.Value, enabled = bar.Enabled, visible = bar.Visible,
                paintCurrent = paint.Value == bar.Value,
                paint = paint.Parts.Select(p => new { part = p.Part, state = p.State, bounds = Rect(p.Bounds) }).ToArray()
            };
        }).ToArray();
        if (!platform.Handles.TryGet(token, out ILibreWindow? current) || !ReferenceEquals(current, window)
            || !form.IsHandleCreated || form.Handle != token.Value)
            throw new InvalidOperationException("Owned root changed during observation.");
        return new
        {
            schema = 1, qualified = false, pid = Environment.ProcessId, title = form.Text,
            hwnd = (long)native.Window.Handle, sourceHandle = (long)form.Handle,
            forms, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            dpi = form.DeviceDpi, coordinateMode = window.CoordinateMode.ToString(),
            keyboardDelay = SystemInformation.KeyboardDelay, keyboardSpeed = SystemInformation.KeyboardSpeed,
            stopwatchFrequency = frequency, tickMilliseconds = Environment.TickCount64,
            client = Rect(client), controls
        };
    }

    private static object Rect(Rectangle rectangle)
        => new { x = rectangle.X, y = rectangle.Y, width = rectangle.Width, height = rectangle.Height };
}
