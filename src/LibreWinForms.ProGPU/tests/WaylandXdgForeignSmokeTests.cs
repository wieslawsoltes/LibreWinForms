// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using FluentAssertions;
using ProGPU.Backend;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class WaylandXdgForeignSmokeTests
{
    public static bool IsEnabled
        => OperatingSystem.IsLinux()
            && string.Equals(
                Environment.GetEnvironmentVariable("LIBREWINFORMS_RUN_WAYLAND_XDG_FOREIGN_SMOKE"),
                "1",
                StringComparison.Ordinal)
            && string.Equals(
                Environment.GetEnvironmentVariable("GDK_BACKEND"),
                "wayland",
                StringComparison.Ordinal)
            && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    [Fact(
        Skip = "Requires an explicitly enabled real Wayland compositor session, GDK_BACKEND=wayland, and GTK4 test host.",
        SkipUnless = nameof(IsEnabled))]
    public void Exporter_ProducesARequestScopedHandleForARealWaylandToplevel()
    {
        using GtkWaylandToplevel window = GtkWaylandToplevel.Create();
        NativeWindowHandle native = new(
            NativeWindowKind.Wayland,
            window.Surface,
            window.Display,
            "wl_surface");

        using var exporter = new LibWaylandXdgForeignPortalParentExporter();
        exporter.TryExport(native, out IXdgPortalParentWindowLease? lease).Should().BeTrue();
        using (lease)
        {
            lease!.Identifier.Should().StartWith("wayland:");
            lease.Identifier.Length.Should().BeGreaterThan("wayland:".Length);
        }
    }

    private sealed class GtkWaylandToplevel : IDisposable
    {
        private nint _window;

        private GtkWaylandToplevel(nint window, nint display, nint surface)
        {
            _window = window;
            Display = display;
            Surface = surface;
        }

        internal nint Display { get; }

        internal nint Surface { get; }

        internal static GtkWaylandToplevel Create()
        {
            GtkNative.GdkSetAllowedBackends("wayland");
            GtkNative.GtkInit();
            nint window = GtkNative.GtkWindowNew();
            if (window == 0)
            {
                throw new InvalidOperationException("GTK4 could not create a Wayland smoke-test window.");
            }

            try
            {
                GtkNative.GtkWindowSetTitle(window, "LibreWinForms xdg-foreign smoke");
                GtkNative.GtkWindowSetDefaultSize(window, 320, 180);
                GtkNative.GtkWindowPresent(window);

                for (int attempt = 0; attempt < 200; attempt++)
                {
                    while (GtkNative.GMainContextIteration(0, mayBlock: 0) != 0)
                    {
                    }

                    nint gdkSurface = GtkNative.GtkNativeGetSurface(window);
                    if (gdkSurface != 0)
                    {
                        nint surface = GtkNative.GdkWaylandSurfaceGetWlSurface(gdkSurface);
                        nint display = GtkNative.GdkWaylandDisplayGetWlDisplay(
                            GtkNative.GdkSurfaceGetDisplay(gdkSurface));
                        if (surface != 0 && display != 0)
                        {
                            return new GtkWaylandToplevel(window, display, surface);
                        }
                    }

                    Thread.Sleep(10);
                }

                throw new InvalidOperationException("GTK4 did not publish a native Wayland toplevel in time.");
            }
            catch
            {
                GtkNative.GtkWindowDestroy(window);
                throw;
            }
        }

        public void Dispose()
        {
            nint window = Interlocked.Exchange(ref _window, 0);
            if (window != 0)
            {
                GtkNative.GtkWindowDestroy(window);
                while (GtkNative.GMainContextIteration(0, mayBlock: 0) != 0)
                {
                }
            }
        }
    }

    private static class GtkNative
    {
        private const string GtkLibrary = "libgtk-4.so.1";
        private const string GlibLibrary = "libglib-2.0.so.0";

        [DllImport(GtkLibrary, EntryPoint = "gtk_init")]
        internal static extern void GtkInit();

        [DllImport(GtkLibrary, EntryPoint = "gdk_set_allowed_backends")]
        internal static extern void GdkSetAllowedBackends(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string backends);

        [DllImport(GtkLibrary, EntryPoint = "gtk_window_new")]
        internal static extern nint GtkWindowNew();

        [DllImport(GtkLibrary, EntryPoint = "gtk_window_set_title")]
        internal static extern void GtkWindowSetTitle(
            nint window,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string title);

        [DllImport(GtkLibrary, EntryPoint = "gtk_window_set_default_size")]
        internal static extern void GtkWindowSetDefaultSize(nint window, int width, int height);

        [DllImport(GtkLibrary, EntryPoint = "gtk_window_present")]
        internal static extern void GtkWindowPresent(nint window);

        [DllImport(GtkLibrary, EntryPoint = "gtk_window_destroy")]
        internal static extern void GtkWindowDestroy(nint window);

        [DllImport(GtkLibrary, EntryPoint = "gtk_native_get_surface")]
        internal static extern nint GtkNativeGetSurface(nint window);

        [DllImport(GtkLibrary, EntryPoint = "gdk_surface_get_display")]
        internal static extern nint GdkSurfaceGetDisplay(nint surface);

        [DllImport(GtkLibrary, EntryPoint = "gdk_wayland_surface_get_wl_surface")]
        internal static extern nint GdkWaylandSurfaceGetWlSurface(nint surface);

        [DllImport(GtkLibrary, EntryPoint = "gdk_wayland_display_get_wl_display")]
        internal static extern nint GdkWaylandDisplayGetWlDisplay(nint display);

        [DllImport(GlibLibrary, EntryPoint = "g_main_context_iteration")]
        internal static extern int GMainContextIteration(nint context, int mayBlock);
    }
}
