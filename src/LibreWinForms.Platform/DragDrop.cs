// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.Platform;

[Flags]
public enum LibreDragDropEffects
{
    None = 0,
    Copy = 1,
    Move = 2,
    Link = 4,
    Scroll = unchecked((int)0x80000000),
    All = Copy | Move | Scroll,
}

public enum LibreDragAction
{
    Continue,
    Drop,
    Cancel,
}

/// <summary>Exposes named drag data without coupling a backend to WinForms' IDataObject.</summary>
public interface ILibreDataTransfer
{
    IReadOnlyList<string> Formats { get; }

    bool Contains(string format, bool autoConvert);

    object? GetData(string format, bool autoConvert);
}

public sealed record LibreDragDropRequest(
    LibreHandle Source,
    ILibreDataTransfer Data,
    LibreDragDropEffects AllowedEffects,
    LibrePoint CursorOffset,
    bool UseDefaultDragImage);

public readonly record struct LibreDragTransition(
    LibreHandle Target,
    LibreDragDropEffects Effect);

/// <summary>
/// Keeps canonical target resolution and WinForms event dispatch in System.Windows.Forms while a
/// platform adapter owns the native or compositor drag loop.
/// </summary>
public interface ILibreDragDropSession
{
    LibreDragTransition Enter(
        LibreHandle hitTarget,
        int keyState,
        LibrePoint screenPosition,
        LibreDragDropEffects effect);

    LibreDragDropEffects Over(
        LibreHandle target,
        int keyState,
        LibrePoint screenPosition,
        LibreDragDropEffects effect);

    void Leave(LibreHandle target);

    LibreDragDropEffects Drop(
        LibreHandle target,
        int keyState,
        LibrePoint screenPosition,
        LibreDragDropEffects effect);

    LibreDragAction QueryContinue(int keyState, bool escapePressed);

    bool GiveFeedback(LibreDragDropEffects effect);
}

/// <summary>Owns platform drag-loop integration and drop-target publication.</summary>
public interface ILibreDragDropService
{
    bool IsSupported { get; }

    void SetTargetEnabled(LibreHandle target, bool enabled);

    LibreDragDropEffects DoDragDrop(LibreDragDropRequest request, ILibreDragDropSession session);
}

public sealed class UnsupportedLibreDragDropService : ILibreDragDropService
{
    public static UnsupportedLibreDragDropService Instance { get; } = new();

    private UnsupportedLibreDragDropService()
    {
    }

    public bool IsSupported => false;

    public void SetTargetEnabled(LibreHandle target, bool enabled)
    {
    }

    public LibreDragDropEffects DoDragDrop(LibreDragDropRequest request, ILibreDragDropSession session)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);
        return LibreDragDropEffects.None;
    }
}
