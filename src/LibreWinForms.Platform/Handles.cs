// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace LibreWinForms.Platform;

/// <summary>Classifies an opaque WinForms handle without exposing backend-native handles.</summary>
public enum LibreHandleKind
{
    Window,
    LogicalControl,
    Menu,
    GraphicsTarget,
    Timer,
}

/// <summary>A process-local WinForms identity token. It must never be passed to a native API.</summary>
public readonly record struct LibreHandle(nint Value, LibreHandleKind Kind)
{
    public bool IsNull => Value == 0;
}

/// <summary>Allocates and resolves strongly typed owners for opaque non-Windows handles.</summary>
public interface ILibreHandleRegistry
{
    int Count { get; }

    LibreHandle Allocate<T>(T target, LibreHandleKind kind) where T : class;

    bool TryGet<T>(LibreHandle handle, [NotNullWhen(true)] out T? target) where T : class;

    bool Release(LibreHandle handle);
}

/// <summary>Thread-safe process-local registry for logical WinForms handle identities.</summary>
public sealed class ManagedLibreHandleRegistry : ILibreHandleRegistry
{
    private sealed record Entry(object Target, LibreHandleKind Kind);

    private readonly ConcurrentDictionary<nint, Entry> _entries = new();
    private long _nextValue;

    public int Count => _entries.Count;

    public LibreHandle Allocate<T>(T target, LibreHandleKind kind) where T : class
    {
        ArgumentNullException.ThrowIfNull(target);

        while (true)
        {
            nint value = CreateOpaqueValue(Interlocked.Increment(ref _nextValue));
            if (_entries.TryAdd(value, new Entry(target, kind)))
            {
                return new LibreHandle(value, kind);
            }
        }
    }

    public bool TryGet<T>(LibreHandle handle, [NotNullWhen(true)] out T? target) where T : class
    {
        if (!handle.IsNull
            && _entries.TryGetValue(handle.Value, out Entry? entry)
            && entry.Kind == handle.Kind
            && entry.Target is T typedTarget)
        {
            target = typedTarget;
            return true;
        }

        target = null;
        return false;
    }

    public bool Release(LibreHandle handle)
        => !handle.IsNull
            && _entries.TryGetValue(handle.Value, out Entry? entry)
            && entry.Kind == handle.Kind
            && _entries.TryRemove(handle.Value, out _);

    private static nint CreateOpaqueValue(long sequence)
        => IntPtr.Size == sizeof(long)
            ? (nint)(long.MinValue | sequence)
            : (nint)(int.MinValue | checked((int)sequence));
}
