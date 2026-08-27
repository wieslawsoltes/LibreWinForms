// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;

namespace LibreWinForms.Platform;

/// <summary>Renders portable visual-style backgrounds without exposing UxTheme handles or device contexts.</summary>
public interface ILibreVisualStyleService
{
    bool IsEnabled { get; }

    bool IsElementDefined(string className, int part);

    void DrawBackground(
        Graphics graphics,
        string className,
        int part,
        int state,
        Rectangle bounds,
        Rectangle? clipRectangle);

    /// <summary>Returns a caller-owned managed region, or null when the element has no drawable background.</summary>
    Region? GetBackgroundRegion(string className, int part, int state, Rectangle bounds);
}

/// <summary>Explicit default for hosts that do not provide portable visual-style rendering.</summary>
public sealed class UnsupportedLibreVisualStyleService : ILibreVisualStyleService
{
    public static UnsupportedLibreVisualStyleService Instance { get; } = new();

    private UnsupportedLibreVisualStyleService()
    {
    }

    public bool IsEnabled => false;

    public bool IsElementDefined(string className, int part) => false;

    public void DrawBackground(
        Graphics graphics,
        string className,
        int part,
        int state,
        Rectangle bounds,
        Rectangle? clipRectangle)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable visual-style background rendering.");

    public Region? GetBackgroundRegion(string className, int part, int state, Rectangle bounds)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable visual-style background regions.");
}
