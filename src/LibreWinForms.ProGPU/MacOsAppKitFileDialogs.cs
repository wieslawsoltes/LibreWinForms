// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using LibreWinForms.Platform;
using ProGPU.Backend;

namespace LibreWinForms.ProGPU;

internal readonly record struct MacOsFileDialogNativeRequest(
    LibreFileDialogKind Kind,
    string Title,
    string Message,
    string InitialDirectory,
    string InitialName,
    IReadOnlyList<string> AllowedExtensions,
    bool AllowsMultipleSelection,
    bool ShowsHiddenFiles,
    bool ResolvesAliases,
    bool CanCreateDirectories,
    nint OwnerWindow);

internal readonly record struct MacOsFileDialogNativeResult(
    bool Accepted,
    IReadOnlyList<string> SelectedPaths);

internal interface IMacOsFileDialogNative
{
    bool IsAvailable { get; }

    MacOsFileDialogNativeResult Show(in MacOsFileDialogNativeRequest request);
}

internal interface IMacOsFileDialogOwnerResolver
{
    nint Resolve(LibreHandle owner);
}

internal sealed class ProGpuMacOsFileDialogOwnerResolver : IMacOsFileDialogOwnerResolver
{
    private readonly ILibreHandleRegistry _handles;

    internal ProGpuMacOsFileDialogOwnerResolver(ILibreHandleRegistry handles)
    {
        _handles = handles ?? throw new ArgumentNullException(nameof(handles));
    }

    public nint Resolve(LibreHandle owner)
    {
        if (owner.IsNull)
        {
            return 0;
        }

        if (!_handles.TryGet(owner, out SilkLibreWindow? window))
        {
            throw new ArgumentException("The file-dialog owner must be a live Silk.NET window.", nameof(owner));
        }

        NativeWindowHandle native = window.NativeHandle;
        if (native.Kind != NativeWindowKind.Cocoa || !native.IsValid)
        {
            throw new ArgumentException("The file-dialog owner must expose a live NSWindow.", nameof(owner));
        }

        return native.Handle;
    }
}

/// <summary>Selects files and folders with native AppKit panels on the owning UI thread.</summary>
public sealed class MacOsAppKitFileDialogService : ILibreFileDialogService
{
    private readonly ILibreDispatcher _dispatcher;
    private readonly IMacOsFileDialogNative _native;
    private readonly IMacOsFileDialogOwnerResolver _owners;

    public MacOsAppKitFileDialogService(
        ILibreDispatcher dispatcher,
        ILibreHandleRegistry handles)
        : this(
            dispatcher,
            AppKitMacOsFileDialogNative.Instance,
            new ProGpuMacOsFileDialogOwnerResolver(handles))
    {
    }

    internal MacOsAppKitFileDialogService(
        ILibreDispatcher dispatcher,
        IMacOsFileDialogNative native,
        IMacOsFileDialogOwnerResolver owners)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _owners = owners ?? throw new ArgumentNullException(nameof(owners));
    }

    public LibreFileDialogResult Show(in LibreFileDialogRequest request)
    {
        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException("File dialogs must be shown on the owning dispatcher thread.");
        }

        if (!_native.IsAvailable)
        {
            throw new PlatformNotSupportedException("The AppKit file-dialog adapter requires macOS.");
        }

        LibreFileDialogRequestValidator.Validate(request);
        if (request.Options.HasFlag(LibreFileDialogOptions.ShowHelp)
            && request.HelpRequested is not null)
        {
            throw new PlatformNotSupportedException(
                "AppKit file panels do not provide the canonical WinForms Help callback.");
        }

        nint owner = _owners.Resolve(request.Owner);
        MacOsFileDialogNativeRequest nativeRequest = CreateNativeRequest(request, owner);
        MacOsFileDialogNativeResult nativeResult = _native.Show(nativeRequest);
        if (!nativeResult.Accepted)
        {
            return new(
                false,
                [.. request.SelectedPaths],
                request.FilterIndex,
                request.Options.HasFlag(LibreFileDialogOptions.ReadOnlyChecked));
        }

        ArgumentNullException.ThrowIfNull(nativeResult.SelectedPaths);
        string[] paths = [.. nativeResult.SelectedPaths];
        if (paths.Length == 0 || paths.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("AppKit accepted the dialog without returning a filesystem path.");
        }

        if (!request.Options.HasFlag(LibreFileDialogOptions.MultiSelect) && paths.Length != 1)
        {
            throw new InvalidOperationException("AppKit returned multiple paths for a single-selection dialog.");
        }

        return new(
            true,
            paths,
            request.FilterIndex,
            request.Options.HasFlag(LibreFileDialogOptions.ReadOnlyChecked));
    }

    internal static MacOsFileDialogNativeRequest CreateNativeRequest(
        in LibreFileDialogRequest request,
        nint ownerWindow)
    {
        (string directory, string name) = ResolveInitialLocation(request);
        return new(
            request.Kind,
            request.Title.Length > 0 ? request.Title : request.Description,
            request.Title.Length > 0 ? request.Description : string.Empty,
            directory,
            name,
            ResolveAllowedExtensions(request),
            request.Kind == LibreFileDialogKind.OpenFile
                && request.Options.HasFlag(LibreFileDialogOptions.MultiSelect),
            request.Options.HasFlag(LibreFileDialogOptions.ShowHiddenFiles),
            request.Options.HasFlag(LibreFileDialogOptions.DereferenceLinks),
            request.Kind != LibreFileDialogKind.OpenFile
                && request.Options.HasFlag(LibreFileDialogOptions.ShowNewFolderButton),
            ownerWindow);
    }

    private static (string Directory, string Name) ResolveInitialLocation(
        in LibreFileDialogRequest request)
    {
        string selected = request.SelectedPaths.FirstOrDefault(
            static path => !string.IsNullOrWhiteSpace(path)) ?? string.Empty;
        if (selected.Length > 0
            && !Path.IsPathFullyQualified(selected)
            && request.InitialDirectory.Length > 0)
        {
            selected = Path.Join(request.InitialDirectory, selected);
        }

        if (request.Kind == LibreFileDialogKind.SelectFolder)
        {
            return (selected.Length > 0 ? selected : request.InitialDirectory, string.Empty);
        }

        if (selected.Length == 0)
        {
            return (request.InitialDirectory, string.Empty);
        }

        return (Path.GetDirectoryName(selected) ?? request.InitialDirectory, Path.GetFileName(selected));
    }

    private static List<string> ResolveAllowedExtensions(
        in LibreFileDialogRequest request)
    {
        if (request.Kind == LibreFileDialogKind.SelectFolder || request.Filters.Count == 0)
        {
            return [];
        }

        LibreFileDialogFilter filter = request.Filters[request.FilterIndex - 1];
        List<string> extensions = [];
        foreach (string pattern in filter.Patterns)
        {
            string trimmed = pattern.Trim();
            if (trimmed is "*" or "*.*")
            {
                return [];
            }

            string extension;
            if (trimmed.StartsWith("*.", StringComparison.Ordinal))
            {
                extension = trimmed[2..];
            }
            else if (!trimmed.Contains('*') && !trimmed.Contains('?')
                && !trimmed.Contains(Path.DirectorySeparatorChar)
                && !trimmed.Contains(Path.AltDirectorySeparatorChar))
            {
                extension = trimmed.TrimStart('.');
            }
            else
            {
                throw new PlatformNotSupportedException(
                    $"AppKit cannot represent the file-dialog wildcard pattern '{pattern}'.");
            }

            if (extension.Length > 0 && !extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                extensions.Add(extension);
            }
        }

        return extensions;
    }
}

internal sealed unsafe class AppKitMacOsFileDialogNative : IMacOsFileDialogNative
{
    private const nint NSModalResponseOk = 1;
    private static readonly Lock s_frameworkLock = new();
    private static nint s_appKit;
    private static nint s_blockIsa;

    internal static AppKitMacOsFileDialogNative Instance { get; } = new();

    private AppKitMacOsFileDialogNative()
    {
    }

    public bool IsAvailable => OperatingSystem.IsMacOS();

    public MacOsFileDialogNativeResult Show(in MacOsFileDialogNativeRequest request)
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("The AppKit file-dialog adapter requires macOS.");
        }

        EnsureFrameworksLoaded();
        if (!ObjectiveC.SendBool(ObjectiveC.GetClass("NSThread"), Selectors.IsMainThread))
        {
            throw new InvalidOperationException("AppKit file dialogs must run on the macOS main thread.");
        }

        nint pool = ObjectiveC.Send(ObjectiveC.Send(ObjectiveC.GetClass("NSAutoreleasePool"), Selectors.Alloc), Selectors.Init);
        try
        {
            nint panel = request.Kind == LibreFileDialogKind.SaveFile
                ? ObjectiveC.Send(ObjectiveC.GetClass("NSSavePanel"), Selectors.SavePanel)
                : ObjectiveC.Send(ObjectiveC.GetClass("NSOpenPanel"), Selectors.OpenPanel);
            if (panel == 0)
            {
                throw new InvalidOperationException("AppKit did not create a file panel.");
            }

            Configure(panel, request);
            nint response = request.OwnerWindow == 0
                ? ObjectiveC.Send(panel, Selectors.RunModal)
                : RunOwnedSheet(panel, request.OwnerWindow);
            if (response != NSModalResponseOk)
            {
                return new(false, []);
            }

            return new(true, ReadSelectedPaths(panel, request.Kind));
        }
        finally
        {
            if (pool != 0)
            {
                ObjectiveC.SendVoid(pool, Selectors.Drain);
            }
        }
    }

    private static void Configure(nint panel, in MacOsFileDialogNativeRequest request)
    {
        if (request.Title.Length > 0)
        {
            ObjectiveC.SendVoid(panel, Selectors.SetTitle, ObjectiveC.CreateString(request.Title));
        }

        if (request.Message.Length > 0)
        {
            ObjectiveC.SendVoid(panel, Selectors.SetMessage, ObjectiveC.CreateString(request.Message));
        }

        if (request.InitialDirectory.Length > 0)
        {
            nint path = ObjectiveC.CreateString(request.InitialDirectory);
            nint url = ObjectiveC.Send(
                ObjectiveC.GetClass("NSURL"),
                Selectors.FileUrlWithPathIsDirectory,
                path,
                true);
            ObjectiveC.SendVoid(panel, Selectors.SetDirectoryUrl, url);
        }

        if (request.InitialName.Length > 0)
        {
            ObjectiveC.SendVoid(
                panel,
                Selectors.SetNameFieldStringValue,
                ObjectiveC.CreateString(request.InitialName));
        }

        ObjectiveC.SendVoid(panel, Selectors.SetShowsHiddenFiles, request.ShowsHiddenFiles);
        ObjectiveC.SendVoid(panel, Selectors.SetCanCreateDirectories, request.CanCreateDirectories);
        if (request.AllowedExtensions.Count > 0)
        {
            ObjectiveC.SendVoid(
                panel,
                Selectors.SetAllowedFileTypes,
                ObjectiveC.CreateStringArray(request.AllowedExtensions));
        }

        if (request.Kind != LibreFileDialogKind.SaveFile)
        {
            bool folders = request.Kind == LibreFileDialogKind.SelectFolder;
            ObjectiveC.SendVoid(panel, Selectors.SetCanChooseFiles, !folders);
            ObjectiveC.SendVoid(panel, Selectors.SetCanChooseDirectories, folders);
            ObjectiveC.SendVoid(panel, Selectors.SetAllowsMultipleSelection, request.AllowsMultipleSelection);
            ObjectiveC.SendVoid(panel, Selectors.SetResolvesAliases, request.ResolvesAliases);
        }
    }

    private static nint RunOwnedSheet(nint panel, nint owner)
    {
        nint application = ObjectiveC.Send(ObjectiveC.GetClass("NSApplication"), Selectors.SharedApplication);
        SheetContext context = new(application);
        GCHandle handle = GCHandle.Alloc(context);
        try
        {
            BlockLiteral block = new()
            {
                Isa = s_blockIsa,
                Flags = 1 << 30,
                Invoke = &SheetCompleted,
                Descriptor = BlockMetadata.Descriptor,
                Context = GCHandle.ToIntPtr(handle),
            };
            ObjectiveC.SendVoid(panel, Selectors.BeginSheetModalForWindow, owner, (nint)(&block));
            nint modalResult = ObjectiveC.Send(application, Selectors.RunModalForWindow, panel);
            return context.Completed ? context.Response : modalResult;
        }
        finally
        {
            handle.Free();
        }
    }

    [UnmanagedCallersOnly]
    private static void SheetCompleted(nint blockPointer, nint response)
    {
        BlockLiteral* block = (BlockLiteral*)blockPointer;
        SheetContext context = (SheetContext)GCHandle.FromIntPtr(block->Context).Target!;
        context.Response = response;
        context.Completed = true;
        ObjectiveC.SendVoid(context.Application, Selectors.StopModalWithCode, response);
    }

    private static string[] ReadSelectedPaths(nint panel, LibreFileDialogKind kind)
    {
        if (kind == LibreFileDialogKind.SaveFile)
        {
            nint url = ObjectiveC.Send(panel, Selectors.Url);
            return url == 0 ? [] : [ObjectiveC.ReadPath(url)];
        }

        nint urls = ObjectiveC.Send(panel, Selectors.Urls);
        nuint count = ObjectiveC.SendUnsigned(urls, Selectors.Count);
        string[] paths = new string[checked((int)count)];
        for (nuint index = 0; index < count; index++)
        {
            paths[checked((int)index)] = ObjectiveC.ReadPath(
                ObjectiveC.Send(urls, Selectors.ObjectAtIndex, index));
        }

        return paths;
    }

    private static void EnsureFrameworksLoaded()
    {
        if (Volatile.Read(ref s_appKit) != 0)
        {
            return;
        }

        lock (s_frameworkLock)
        {
            if (s_appKit == 0)
            {
                s_appKit = NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
                nint system = NativeLibrary.Load("/usr/lib/libSystem.B.dylib");
                s_blockIsa = NativeLibrary.GetExport(system, "_NSConcreteStackBlock");
            }
        }
    }

    private sealed class SheetContext(nint application)
    {
        internal nint Application { get; } = application;

        internal bool Completed { get; set; }

        internal nint Response { get; set; }
    }

#pragma warning disable IDE1006 // Native ABI field names mirror the Objective-C block layout.
    [StructLayout(LayoutKind.Sequential)]
    private struct BlockLiteral
    {
        internal nint Isa;
        internal int Flags;
        internal int Reserved;
        internal delegate* unmanaged<nint, nint, void> Invoke;
        internal nint Descriptor;
        internal nint Context;
    }

    private static class BlockMetadata
    {
        internal static readonly nint Descriptor = CreateDescriptor();

        private static nint CreateDescriptor()
        {
            nint signature = AllocateUtf8("v@?q");
            BlockDescriptor* descriptor = (BlockDescriptor*)RuntimeHelpers.AllocateTypeAssociatedMemory(
                typeof(BlockMetadata),
                sizeof(BlockDescriptor));
            descriptor->Reserved = 0;
            descriptor->Size = (nuint)sizeof(BlockLiteral);
            descriptor->Signature = signature;
            return (nint)descriptor;
        }

        private static nint AllocateUtf8(string value)
        {
            int length = Encoding.UTF8.GetByteCount(value);
            byte* buffer = (byte*)RuntimeHelpers.AllocateTypeAssociatedMemory(
                typeof(BlockMetadata),
                length + 1);
            Span<byte> destination = new(buffer, length + 1);
            Encoding.UTF8.GetBytes(value, destination);
            destination[length] = 0;
            return (nint)buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BlockDescriptor
        {
            internal nuint Reserved;
            internal nuint Size;
            internal nint Signature;
        }
    }

    private static class Selectors
    {
        internal static readonly nint Alloc = ObjectiveC.GetSelector("alloc");
        internal static readonly nint Init = ObjectiveC.GetSelector("init");
        internal static readonly nint Drain = ObjectiveC.GetSelector("drain");
        internal static readonly nint IsMainThread = ObjectiveC.GetSelector("isMainThread");
        internal static readonly nint OpenPanel = ObjectiveC.GetSelector("openPanel");
        internal static readonly nint SavePanel = ObjectiveC.GetSelector("savePanel");
        internal static readonly nint SetTitle = ObjectiveC.GetSelector("setTitle:");
        internal static readonly nint SetMessage = ObjectiveC.GetSelector("setMessage:");
        internal static readonly nint SetDirectoryUrl = ObjectiveC.GetSelector("setDirectoryURL:");
        internal static readonly nint SetNameFieldStringValue = ObjectiveC.GetSelector("setNameFieldStringValue:");
        internal static readonly nint SetAllowedFileTypes = ObjectiveC.GetSelector("setAllowedFileTypes:");
        internal static readonly nint SetCanChooseFiles = ObjectiveC.GetSelector("setCanChooseFiles:");
        internal static readonly nint SetCanChooseDirectories = ObjectiveC.GetSelector("setCanChooseDirectories:");
        internal static readonly nint SetAllowsMultipleSelection = ObjectiveC.GetSelector("setAllowsMultipleSelection:");
        internal static readonly nint SetResolvesAliases = ObjectiveC.GetSelector("setResolvesAliases:");
        internal static readonly nint SetShowsHiddenFiles = ObjectiveC.GetSelector("setShowsHiddenFiles:");
        internal static readonly nint SetCanCreateDirectories = ObjectiveC.GetSelector("setCanCreateDirectories:");
        internal static readonly nint FileUrlWithPathIsDirectory = ObjectiveC.GetSelector("fileURLWithPath:isDirectory:");
        internal static readonly nint RunModal = ObjectiveC.GetSelector("runModal");
        internal static readonly nint BeginSheetModalForWindow = ObjectiveC.GetSelector("beginSheetModalForWindow:completionHandler:");
        internal static readonly nint SharedApplication = ObjectiveC.GetSelector("sharedApplication");
        internal static readonly nint RunModalForWindow = ObjectiveC.GetSelector("runModalForWindow:");
        internal static readonly nint StopModalWithCode = ObjectiveC.GetSelector("stopModalWithCode:");
        internal static readonly nint Url = ObjectiveC.GetSelector("URL");
        internal static readonly nint Urls = ObjectiveC.GetSelector("URLs");
        internal static readonly nint Count = ObjectiveC.GetSelector("count");
        internal static readonly nint ObjectAtIndex = ObjectiveC.GetSelector("objectAtIndex:");
    }
#pragma warning restore IDE1006
}

internal static unsafe partial class ObjectiveC
{
    private const string Library = "/usr/lib/libobjc.A.dylib";
    private static readonly nint s_stringWithUtf8 = GetSelector("stringWithUTF8String:");
    private static readonly nint s_arrayWithObjectsCount = GetSelector("arrayWithObjects:count:");
    private static readonly nint s_path = GetSelector("path");
    private static readonly nint s_utf8String = GetSelector("UTF8String");

    internal static nint GetClass(string name) => ObjcGetClass(name);

    internal static nint GetSelector(string name) => SelRegisterName(name);

    internal static nint CreateString(string value)
        => Send(ObjectiveC.GetClass("NSString"), s_stringWithUtf8, value);

    internal static nint CreateStringArray(IReadOnlyList<string> values)
    {
        nint* objects = stackalloc nint[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            objects[index] = CreateString(values[index]);
        }

        return Send(ObjectiveC.GetClass("NSArray"), s_arrayWithObjectsCount, objects, (nuint)values.Count);
    }

    internal static string ReadPath(nint url)
    {
        nint path = Send(url, s_path);
        nint utf8 = Send(path, s_utf8String);
        return utf8 == 0
            ? throw new InvalidOperationException("AppKit returned a URL without a filesystem path.")
            : Marshal.PtrToStringUTF8(utf8)
                ?? throw new InvalidOperationException("AppKit returned an invalid filesystem path.");
    }

    internal static nint Send(nint receiver, nint selector) => ObjcMsgSend(receiver, selector);

    internal static nint Send(nint receiver, nint selector, nint value)
        => ObjcMsgSendNInt(receiver, selector, value);

    internal static nint Send(nint receiver, nint selector, nuint value)
        => ObjcMsgSendNUInt(receiver, selector, value);

    internal static nint Send(nint receiver, nint selector, string value)
        => ObjcMsgSendString(receiver, selector, value);

    internal static nint Send(nint receiver, nint selector, nint value, bool value2)
        => ObjcMsgSendNIntBool(receiver, selector, value, value2 ? (byte)1 : (byte)0);

    internal static nint Send(nint receiver, nint selector, nint* objects, nuint count)
        => ObjcMsgSendObjectsCount(receiver, selector, objects, count);

    internal static bool SendBool(nint receiver, nint selector)
        => ObjcMsgSendBool(receiver, selector) != 0;

    internal static nuint SendUnsigned(nint receiver, nint selector)
        => ObjcMsgSendUnsigned(receiver, selector);

    internal static void SendVoid(nint receiver, nint selector)
        => ObjcMsgSendVoid(receiver, selector);

    internal static void SendVoid(nint receiver, nint selector, nint value)
        => ObjcMsgSendVoidNInt(receiver, selector, value);

    internal static void SendVoid(nint receiver, nint selector, bool value)
        => ObjcMsgSendVoidBool(receiver, selector, value ? (byte)1 : (byte)0);

    internal static void SendVoid(nint receiver, nint selector, nint value, nint value2)
        => ObjcMsgSendVoidNIntNInt(receiver, selector, value, value2);

    [LibraryImport(Library, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint ObjcGetClass(string name);

    [LibraryImport(Library, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint SelRegisterName(string name);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    private static partial nint ObjcMsgSend(nint receiver, nint selector);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    private static partial nint ObjcMsgSendNInt(nint receiver, nint selector, nint value);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    private static partial nint ObjcMsgSendNUInt(nint receiver, nint selector, nuint value);

    [LibraryImport(Library, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint ObjcMsgSendString(nint receiver, nint selector, string value);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    private static partial nint ObjcMsgSendNIntBool(nint receiver, nint selector, nint value, byte value2);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    private static partial nint ObjcMsgSendObjectsCount(
        nint receiver,
        nint selector,
        nint* objects,
        nuint count);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    private static partial byte ObjcMsgSendBool(nint receiver, nint selector);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    private static partial nuint ObjcMsgSendUnsigned(nint receiver, nint selector);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    private static partial void ObjcMsgSendVoid(nint receiver, nint selector);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    private static partial void ObjcMsgSendVoidNInt(nint receiver, nint selector, nint value);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    private static partial void ObjcMsgSendVoidBool(nint receiver, nint selector, byte value);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    private static partial void ObjcMsgSendVoidNIntNInt(
        nint receiver,
        nint selector,
        nint value,
        nint value2);
}
