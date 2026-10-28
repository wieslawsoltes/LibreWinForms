using System.Runtime.InteropServices;

namespace ProGPU.SystemDrawing;

// Original implementation of the four documented read-only SystemParametersInfoW
// font-smoothing queries. No setting is written and no native handle is retained.
internal static partial class WindowsTextSystemSettings
{
    internal delegate bool Query(uint action, out uint value);

    internal static DrawingTextSystemSettings? Capture()
        => OperatingSystem.IsWindows() ? Capture(Read) : null;

    internal static DrawingTextSystemSettings Capture(Query query)
    {
        ArgumentNullException.ThrowIfNull(query);
        uint? enabled = query(0x004A, out uint value) ? value : null;
        uint? type = query(0x200A, out value) ? value : null;
        uint? contrast = query(0x200C, out value) ? value : null;
        uint? orientation = query(0x2012, out value) ? value : null;
        return new(enabled, type, contrast, orientation);
    }

    private static bool Read(uint action, out uint value)
        => SystemParametersInfo(action, 0, out value, 0);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint parameter, out uint value, uint flags);
}
