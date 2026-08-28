// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using ProGPU.Backend;

namespace LibreWinForms.ProGPU;

/// <summary>
/// Exports a Silk/GLFW Wayland toplevel through xdg-foreign v2 without dispatching
/// the windowing backend's default Wayland event queue.
/// </summary>
public sealed class LibWaylandXdgForeignPortalParentExporter : IXdgPortalWaylandParentExporter, IDisposable
{
    private readonly IWaylandXdgForeignProtocol _protocol;
    private int _disposed;

    public LibWaylandXdgForeignPortalParentExporter()
        : this(new LibWaylandXdgForeignProtocol())
    {
    }

    internal LibWaylandXdgForeignPortalParentExporter(IWaylandXdgForeignProtocol protocol)
    {
        _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
    }

    public bool TryExport(
        NativeWindowHandle window,
        [NotNullWhen(true)] out IXdgPortalParentWindowLease? lease)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        lease = null;

        if (!OperatingSystem.IsLinux()
            || window.Kind != NativeWindowKind.Wayland
            || !window.IsValid
            || window.Display == 0)
        {
            return false;
        }

        try
        {
            if (!_protocol.TryExport(window.Display, window.Handle, out IWaylandXdgForeignExport? export))
            {
                return false;
            }

            if (string.IsNullOrEmpty(export.Handle) || export.Handle.Contains('\0'))
            {
                export.Dispose();
                throw new InvalidOperationException("The Wayland compositor returned an invalid xdg-foreign handle.");
            }

            lease = new XdgPortalParentWindowLease($"wayland:{export.Handle}", export.Dispose);
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _protocol.Dispose();
        }
    }
}

internal interface IWaylandXdgForeignExport : IDisposable
{
    string Handle { get; }
}

internal interface IWaylandXdgForeignProtocol : IDisposable
{
    bool TryExport(
        nint display,
        nint surface,
        [NotNullWhen(true)] out IWaylandXdgForeignExport? export);
}

internal sealed class LibWaylandXdgForeignProtocol : IWaylandXdgForeignProtocol
{
    private readonly Lock _sync = new();
    private readonly Dictionary<SessionKey, LibWaylandXdgForeignSession> _sessions = [];
    private WaylandProtocolMetadata? _metadata;
    private bool _disposed;

    public bool TryExport(
        nint display,
        nint surface,
        [NotNullWhen(true)] out IWaylandXdgForeignExport? export)
    {
        if (display == 0 || surface == 0)
        {
            export = null;
            return false;
        }

        LibWaylandXdgForeignSession session;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            SessionKey key = new(display, Environment.CurrentManagedThreadId);
            if (!_sessions.TryGetValue(key, out session!))
            {
                _metadata ??= new WaylandProtocolMetadata();
                session = new LibWaylandXdgForeignSession(display, _metadata);
                _sessions.Add(key, session);
            }
        }

        return session.TryExport(surface, out export);
    }

    public void Dispose()
    {
        LibWaylandXdgForeignSession[] sessions;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            sessions = [.. _sessions.Values];
            _sessions.Clear();
        }

        foreach (LibWaylandXdgForeignSession session in sessions)
        {
            session.Dispose();
        }
    }

    private readonly record struct SessionKey(nint Display, int ThreadId);
}

internal sealed unsafe class LibWaylandXdgForeignSession : IDisposable
{
    private readonly nint _display;
    private readonly WaylandProtocolMetadata _metadata;
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly RegistryState _registryState = new();
    private readonly HashSet<LibWaylandXdgForeignExport> _exports = [];
    private GCHandle _registryStateHandle;
    private nint _queue;
    private nint _registry;
    private nint _exporter;
    private bool _disposed;

    internal LibWaylandXdgForeignSession(nint display, WaylandProtocolMetadata metadata)
    {
        _display = display;
        _metadata = metadata;

        try
        {
            _queue = LibWaylandClient.DisplayCreateQueue(display);
            if (_queue == 0)
            {
                throw new InvalidOperationException("libwayland could not create an isolated xdg-foreign event queue.");
            }

            nint wrapper = LibWaylandClient.ProxyCreateWrapper(display);
            if (wrapper == 0)
            {
                throw new InvalidOperationException("libwayland could not create a display proxy wrapper.");
            }

            try
            {
                LibWaylandClient.ProxySetQueue(wrapper, _queue);
                WlArgument* arguments = stackalloc WlArgument[1];
                arguments[0]._n = 0;
                _registry = LibWaylandClient.ProxyMarshalArrayFlags(
                    wrapper,
                    opcode: 1,
                    _metadata.RegistryInterface,
                    LibWaylandClient.ProxyGetVersion(wrapper),
                    flags: 0,
                    arguments);
            }
            finally
            {
                LibWaylandClient.ProxyWrapperDestroy(wrapper);
            }

            if (_registry == 0)
            {
                throw new InvalidOperationException("libwayland could not create an isolated registry proxy.");
            }

            LibWaylandClient.ProxySetQueue(_registry, _queue);
            _registryStateHandle = GCHandle.Alloc(_registryState);
            if (LibWaylandClient.ProxyAddDispatcher(
                    _registry,
                    &DispatchRegistry,
                    GCHandle.ToIntPtr(_registryStateHandle),
                    data: 0) != 0)
            {
                throw new InvalidOperationException("libwayland rejected the xdg-foreign registry dispatcher.");
            }

            Roundtrip("discover xdg-foreign globals");
            BindExporterIfAvailable();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal bool TryExport(
        nint surface,
        [NotNullWhen(true)] out IWaylandXdgForeignExport? export)
    {
        VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_exporter == 0)
        {
            Roundtrip("refresh xdg-foreign globals");
            BindExporterIfAvailable();
            if (_exporter == 0)
            {
                export = null;
                return false;
            }
        }

        ExportState state = new();
        GCHandle stateHandle = GCHandle.Alloc(state);
        nint exported = 0;
        try
        {
            WlArgument* arguments = stackalloc WlArgument[2];
            arguments[0]._n = 0;
            arguments[1]._o = surface;
            exported = LibWaylandClient.ProxyMarshalArrayFlags(
                _exporter,
                opcode: 1,
                _metadata.ExportedInterface,
                LibWaylandClient.ProxyGetVersion(_exporter),
                flags: 0,
                arguments);
            if (exported == 0)
            {
                throw new InvalidOperationException("The Wayland compositor did not create an xdg-exported object.");
            }

            LibWaylandClient.ProxySetQueue(exported, _queue);
            if (LibWaylandClient.ProxyAddDispatcher(
                    exported,
                    &DispatchExported,
                    GCHandle.ToIntPtr(stateHandle),
                    data: 0) != 0)
            {
                throw new InvalidOperationException("libwayland rejected the xdg-exported event dispatcher.");
            }

            Roundtrip("receive the xdg-foreign toplevel handle");
            state.ThrowIfFailed();
            if (string.IsNullOrEmpty(state.Handle))
            {
                throw new InvalidOperationException("The Wayland compositor did not return an xdg-foreign handle.");
            }

            LibWaylandXdgForeignExport owned = new(this, exported, stateHandle, state.Handle);
            _exports.Add(owned);
            exported = 0;
            stateHandle = default;
            export = owned;
            return true;
        }
        catch
        {
            if (exported != 0)
            {
                DestroyProtocolObject(exported);
            }

            if (stateHandle.IsAllocated)
            {
                stateHandle.Free();
            }

            throw;
        }
    }

    internal void Release(LibWaylandXdgForeignExport export)
    {
        ArgumentNullException.ThrowIfNull(export);
        VerifyAccess();
        if (_exports.Remove(export))
        {
            export.ReleaseNative();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        VerifyAccess();
        _disposed = true;

        foreach (LibWaylandXdgForeignExport export in _exports.ToArray())
        {
            export.ReleaseNative();
        }

        _exports.Clear();
        if (_exporter != 0)
        {
            DestroyProtocolObject(_exporter);
            _exporter = 0;
        }

        if (_registry != 0)
        {
            LibWaylandClient.ProxyDestroy(_registry);
            _registry = 0;
        }

        if (_registryStateHandle.IsAllocated)
        {
            _registryStateHandle.Free();
        }

        if (_queue != 0)
        {
            LibWaylandClient.EventQueueDestroy(_queue);
            _queue = 0;
        }
    }

    private void BindExporterIfAvailable()
    {
        _registryState.ThrowIfFailed();
        if (_exporter != 0 || _registryState.ExporterName == 0)
        {
            return;
        }

        WlArgument* arguments = stackalloc WlArgument[4];
        arguments[0]._u = _registryState.ExporterName;
        arguments[1]._s = (nint)_metadata.ExporterInterfaceName;
        arguments[2]._u = Math.Min(_registryState.ExporterVersion, 1u);
        arguments[3]._n = 0;
        _exporter = LibWaylandClient.ProxyMarshalArrayFlags(
            _registry,
            opcode: 0,
            _metadata.ExporterInterface,
            version: arguments[2]._u,
            flags: 0,
            arguments);
        if (_exporter == 0)
        {
            throw new InvalidOperationException("libwayland could not bind zxdg_exporter_v2.");
        }

        LibWaylandClient.ProxySetQueue(_exporter, _queue);
    }

    private void Roundtrip(string operation)
    {
        _registryState.ThrowIfFailed();
        if (LibWaylandClient.DisplayRoundtripQueue(_display, _queue) < 0)
        {
            int error = LibWaylandClient.DisplayGetError(_display);
            throw new InvalidOperationException($"libwayland failed to {operation} (display error {error}).");
        }

        _registryState.ThrowIfFailed();
    }

    private static void DestroyProtocolObject(nint proxy)
        => _ = LibWaylandClient.ProxyMarshalArrayFlags(
            proxy,
            opcode: 0,
            interfacePointer: null,
            LibWaylandClient.ProxyGetVersion(proxy),
            flags: 1,
            arguments: null);

    private void VerifyAccess()
    {
        if (Environment.CurrentManagedThreadId != _threadId)
        {
            throw new InvalidOperationException("A Wayland xdg-foreign session must be used on its creating thread.");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int DispatchRegistry(
        nint implementation,
        nint target,
        uint opcode,
        WlMessage* message,
        WlArgument* arguments)
    {
        RegistryState state = (RegistryState)GCHandle.FromIntPtr(implementation).Target!;
        return state.Dispatch(opcode, arguments);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int DispatchExported(
        nint implementation,
        nint target,
        uint opcode,
        WlMessage* message,
        WlArgument* arguments)
    {
        ExportState state = (ExportState)GCHandle.FromIntPtr(implementation).Target!;
        return state.Dispatch(opcode, arguments);
    }

    private sealed class RegistryState
    {
        internal uint ExporterName { get; private set; }

        internal uint ExporterVersion { get; private set; }

        private Exception? Error { get; set; }

        internal int Dispatch(uint opcode, WlArgument* arguments)
        {
            try
            {
                if (opcode == 0)
                {
                    string? interfaceName = Marshal.PtrToStringUTF8(arguments[1]._s);
                    if (string.Equals(interfaceName, "zxdg_exporter_v2", StringComparison.Ordinal))
                    {
                        ExporterName = arguments[0]._u;
                        ExporterVersion = arguments[2]._u;
                    }
                }
                else if (opcode == 1 && arguments[0]._u == ExporterName)
                {
                    ExporterName = 0;
                    ExporterVersion = 0;
                }
            }
            catch (Exception exception)
            {
                Error ??= exception;
            }

            return 0;
        }

        internal void ThrowIfFailed()
        {
            if (Error is Exception error)
            {
                throw new InvalidOperationException("The Wayland registry callback failed.", error);
            }
        }
    }

    private sealed class ExportState
    {
        internal string? Handle { get; private set; }

        private Exception? Error { get; set; }

        internal int Dispatch(uint opcode, WlArgument* arguments)
        {
            try
            {
                if (opcode == 0)
                {
                    Handle = Marshal.PtrToStringUTF8(arguments[0]._s);
                }
            }
            catch (Exception exception)
            {
                Error ??= exception;
            }

            return 0;
        }

        internal void ThrowIfFailed()
        {
            if (Error is Exception error)
            {
                throw new InvalidOperationException("The xdg-exported callback failed.", error);
            }
        }
    }
}

internal sealed class LibWaylandXdgForeignExport(
    LibWaylandXdgForeignSession owner,
    nint proxy,
    GCHandle stateHandle,
    string handle) : IWaylandXdgForeignExport
{
    private LibWaylandXdgForeignSession? _owner = owner;
    private nint _proxy = proxy;
    private GCHandle _stateHandle = stateHandle;

    public string Handle { get; } = handle;

    public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(this);

    internal unsafe void ReleaseNative()
    {
        nint proxy = Interlocked.Exchange(ref _proxy, 0);
        if (proxy != 0)
        {
            _ = LibWaylandClient.ProxyMarshalArrayFlags(
                proxy,
                opcode: 0,
                interfacePointer: null,
                LibWaylandClient.ProxyGetVersion(proxy),
                flags: 1,
                arguments: null);
        }

        if (_stateHandle.IsAllocated)
        {
            _stateHandle.Free();
        }
    }
}

internal sealed unsafe class WaylandProtocolMetadata
{
    private const string LibraryName = "libwayland-client.so.0";

    internal WaylandProtocolMetadata()
    {
        nint library = NativeLibrary.Load(LibraryName);
        RegistryInterface = (WlInterface*)NativeLibrary.GetExport(library, "wl_registry_interface");
        WlInterface* surfaceInterface = (WlInterface*)NativeLibrary.GetExport(library, "wl_surface_interface");

        ExporterInterface = Allocate<WlInterface>();
        ExportedInterface = Allocate<WlInterface>();
        ExporterInterfaceName = AllocateUtf8("zxdg_exporter_v2");
        byte* exportedInterfaceName = AllocateUtf8("zxdg_exported_v2");

        WlInterface** exportTypes = (WlInterface**)Allocate<nint>(2);
        exportTypes[0] = ExportedInterface;
        exportTypes[1] = surfaceInterface;

        WlMessage* exporterMethods = Allocate<WlMessage>(2);
        exporterMethods[0] = new(AllocateUtf8("destroy"), AllocateUtf8(string.Empty), null);
        exporterMethods[1] = new(AllocateUtf8("export_toplevel"), AllocateUtf8("no"), exportTypes);

        WlMessage* exportedMethods = Allocate<WlMessage>();
        exportedMethods[0] = new(AllocateUtf8("destroy"), AllocateUtf8(string.Empty), null);
        WlMessage* exportedEvents = Allocate<WlMessage>();
        exportedEvents[0] = new(AllocateUtf8("handle"), AllocateUtf8("s"), null);

        *ExporterInterface = new(
            ExporterInterfaceName,
            version: 1,
            methodCount: 2,
            exporterMethods,
            eventCount: 0,
            events: null);
        *ExportedInterface = new(
            exportedInterfaceName,
            version: 1,
            methodCount: 1,
            exportedMethods,
            eventCount: 1,
            exportedEvents);
    }

    internal WlInterface* RegistryInterface { get; }

    internal WlInterface* ExporterInterface { get; }

    internal WlInterface* ExportedInterface { get; }

    internal byte* ExporterInterfaceName { get; }

    private static T* Allocate<T>(int count = 1)
        where T : unmanaged
    {
        int byteCount = checked(sizeof(T) * count);
        nint memory = RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(WaylandProtocolMetadata), byteCount);
        new Span<byte>((void*)memory, byteCount).Clear();
        return (T*)memory;
    }

    private static byte* AllocateUtf8(string value)
    {
        int byteCount = checked(Encoding.UTF8.GetByteCount(value) + 1);
        byte* memory = Allocate<byte>(byteCount);
        int written = Encoding.UTF8.GetBytes(value, new Span<byte>(memory, byteCount - 1));
        memory[written] = 0;
        return memory;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly unsafe struct WlInterface(
    byte* name,
    int version,
    int methodCount,
    WlMessage* methods,
    int eventCount,
    WlMessage* events)
{
    internal readonly byte* _name = name;
    internal readonly int _version = version;
    internal readonly int _methodCount = methodCount;
    internal readonly WlMessage* _methods = methods;
    internal readonly int _eventCount = eventCount;
    internal readonly WlMessage* _events = events;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly unsafe struct WlMessage(byte* name, byte* signature, WlInterface** types)
{
    internal readonly byte* _name = name;
    internal readonly byte* _signature = signature;
    internal readonly WlInterface** _types = types;
}

[StructLayout(LayoutKind.Explicit)]
internal struct WlArgument
{
    [FieldOffset(0)] internal int _i;
    [FieldOffset(0)] internal uint _u;
    [FieldOffset(0)] internal int _f;
    [FieldOffset(0)] internal nint _s;
    [FieldOffset(0)] internal nint _o;
    [FieldOffset(0)] internal uint _n;
    [FieldOffset(0)] internal nint _a;
    [FieldOffset(0)] internal int _h;
}

internal static unsafe partial class LibWaylandClient
{
    private const string LibraryName = "libwayland-client.so.0";

    [LibraryImport(LibraryName, EntryPoint = "wl_display_create_queue")]
    internal static partial nint DisplayCreateQueue(nint display);

    [LibraryImport(LibraryName, EntryPoint = "wl_event_queue_destroy")]
    internal static partial void EventQueueDestroy(nint queue);

    [LibraryImport(LibraryName, EntryPoint = "wl_display_roundtrip_queue")]
    internal static partial int DisplayRoundtripQueue(nint display, nint queue);

    [LibraryImport(LibraryName, EntryPoint = "wl_display_get_error")]
    internal static partial int DisplayGetError(nint display);

    [LibraryImport(LibraryName, EntryPoint = "wl_proxy_create_wrapper")]
    internal static partial nint ProxyCreateWrapper(nint proxy);

    [LibraryImport(LibraryName, EntryPoint = "wl_proxy_wrapper_destroy")]
    internal static partial void ProxyWrapperDestroy(nint wrapper);

    [LibraryImport(LibraryName, EntryPoint = "wl_proxy_set_queue")]
    internal static partial void ProxySetQueue(nint proxy, nint queue);

    [LibraryImport(LibraryName, EntryPoint = "wl_proxy_get_version")]
    internal static partial uint ProxyGetVersion(nint proxy);

    [LibraryImport(LibraryName, EntryPoint = "wl_proxy_destroy")]
    internal static partial void ProxyDestroy(nint proxy);

    [LibraryImport(LibraryName, EntryPoint = "wl_proxy_marshal_array_flags")]
    internal static partial nint ProxyMarshalArrayFlags(
        nint proxy,
        uint opcode,
        WlInterface* interfacePointer,
        uint version,
        uint flags,
        WlArgument* arguments);

    [LibraryImport(LibraryName, EntryPoint = "wl_proxy_add_dispatcher")]
    internal static partial int ProxyAddDispatcher(
        nint proxy,
        delegate* unmanaged[Cdecl]<nint, nint, uint, WlMessage*, WlArgument*, int> dispatcher,
        nint implementation,
        nint data);
}
