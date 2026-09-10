// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.Platform;

/// <summary>An opaque popup identity scoped to one owner window.</summary>
public readonly record struct LibrePopupId(long Value)
{
    public bool IsNull => Value == 0;
}

/// <summary>Backend-neutral dismissal behavior for a non-activating popup surface.</summary>
[Flags]
public enum LibrePopupDismissalPolicy
{
    /// <summary>The canonical owner explicitly controls popup lifetime.</summary>
    Explicit = 0,

    /// <summary>Dismiss when a pointer press occurs outside the popup.</summary>
    PointerPressedOutside = 1,

    /// <summary>Dismiss when the owner is deactivated.</summary>
    OwnerDeactivated = 2,
}

/// <summary>Typed creation and update state for one independent popup surface.</summary>
public readonly record struct LibrePopupSurfaceRequest(
    LibreHandle Owner,
    LibrePopupId Popup,
    LibreRectangle ScreenBounds,
    double DpiScale,
    bool InputTransparent,
    LibrePopupDismissalPolicy DismissalPolicy);

/// <summary>
/// Records non-activating content that may extend beyond an owner window without
/// exposing a native popup handle or renderer-specific visual.
/// </summary>
public interface ILibrePopupSurfaceService
{
    /// <summary>
    /// Creates a local-coordinate recorder for one popup. Disposing the recorder
    /// atomically replaces its retained content. The popup remains visible until hidden.
    /// </summary>
    System.Drawing.Graphics CreateGraphics(in LibrePopupSurfaceRequest request);

    /// <summary>Hides and releases a popup. Hiding a missing identity is idempotent.</summary>
    void Hide(LibreHandle owner, LibrePopupId popup);
}

/// <summary>Explicit default for hosts without independent popup surfaces.</summary>
public sealed class UnsupportedLibrePopupSurfaceService : ILibrePopupSurfaceService
{
    public static UnsupportedLibrePopupSurfaceService Instance { get; } = new();

    private UnsupportedLibrePopupSurfaceService()
    {
    }

    public System.Drawing.Graphics CreateGraphics(in LibrePopupSurfaceRequest request)
        => throw CreateException();

    public void Hide(LibreHandle owner, LibrePopupId popup)
        => throw CreateException();

    private static PlatformNotSupportedException CreateException()
        => new("This LibreWinForms host does not provide independent popup surfaces.");
}
