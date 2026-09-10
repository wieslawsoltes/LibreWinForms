// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.Platform;

public enum LibreFileDialogKind
{
    OpenFile,
    SaveFile,
    SelectFolder,
}

[Flags]
public enum LibreFileDialogOptions
{
    None = 0,
    AddExtension = 1 << 0,
    AddToRecent = 1 << 1,
    CheckFileExists = 1 << 2,
    CheckPathExists = 1 << 3,
    DereferenceLinks = 1 << 4,
    RestoreDirectory = 1 << 5,
    ShowHelp = 1 << 6,
    ShowHiddenFiles = 1 << 7,
    SupportMultiDottedExtensions = 1 << 8,
    ValidateNames = 1 << 9,
    MultiSelect = 1 << 10,
    ReadOnlyChecked = 1 << 11,
    SelectReadOnly = 1 << 12,
    ShowPreview = 1 << 13,
    ShowReadOnly = 1 << 14,
    CheckWriteAccess = 1 << 15,
    CreatePrompt = 1 << 16,
    ExpandedMode = 1 << 17,
    OverwritePrompt = 1 << 18,
    OkRequiresInteraction = 1 << 19,
    ShowPinnedPlaces = 1 << 20,
    ShowNewFolderButton = 1 << 21,
    UseDescriptionForTitle = 1 << 22,
    AutoUpgradeEnabled = 1 << 23,
}

/// <summary>An immutable display name and set of shell wildcard patterns.</summary>
public sealed record LibreFileDialogFilter(string Name, IReadOnlyList<string> Patterns);

/// <summary>A path or platform-known folder identity offered as a navigation shortcut.</summary>
public readonly record struct LibreFileDialogPlace(string Path, Guid? KnownFolderGuid);

/// <summary>Backend-neutral state for one canonical file or folder selection session.</summary>
public readonly record struct LibreFileDialogRequest(
    LibreFileDialogKind Kind,
    string Title,
    string Description,
    string InitialDirectory,
    IReadOnlyList<string> SelectedPaths,
    string DefaultExtension,
    IReadOnlyList<LibreFileDialogFilter> Filters,
    int FilterIndex,
    LibreFileDialogOptions Options,
    Guid? ClientGuid,
    IReadOnlyList<LibreFileDialogPlace> CustomPlaces,
    Action? HelpRequested,
    LibreHandle Owner);

/// <summary>The candidate state returned by a file or folder selection service.</summary>
public readonly record struct LibreFileDialogResult(
    bool Accepted,
    IReadOnlyList<string> SelectedPaths,
    int FilterIndex,
    bool ReadOnlyChecked);

/// <summary>Selects filesystem paths without exposing Win32 common-dialog or shell objects.</summary>
public interface ILibreFileDialogService
{
    LibreFileDialogResult Show(in LibreFileDialogRequest request);
}

/// <summary>Explicit default for hosts that have not supplied desktop file/folder selection.</summary>
public sealed class UnsupportedLibreFileDialogService : ILibreFileDialogService
{
    public static UnsupportedLibreFileDialogService Instance { get; } = new();

    private UnsupportedLibreFileDialogService()
    {
    }

    public LibreFileDialogResult Show(in LibreFileDialogRequest request)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable file or folder dialogs.");
}
