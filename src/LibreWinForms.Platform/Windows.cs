// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.Platform;

[Flags]
public enum LibreWindowOptions
{
    None = 0,
    Visible = 1,
    Resizable = 2,
    Decorated = 4,
    TopMost = 8,
    ToolWindow = 16,
}

public enum LibreWindowState
{
    Normal,
    Minimized,
    Maximized,
    FullScreen,
}

/// <summary>Typed creation data for an independent platform window.</summary>
public readonly record struct LibreWindowCreateOptions(
    string Title,
    LibreRectangle Bounds,
    LibreWindowOptions Options,
    LibreHandle Owner);

/// <summary>Events raised by a platform window on its dispatcher thread.</summary>
public interface ILibreWindowEvents
{
    /// <summary>Returns <see langword="true"/> to allow the close operation.</summary>
    bool Closing();

    void Closed();

    void BoundsChanged(LibreRectangle bounds);

    void PaintRequested(LibreRectangle dirtyRectangle);

    void Input(in LibreInputEvent inputEvent);
}

/// <summary>A top-level platform window paired with a logical WinForms handle.</summary>
public interface ILibreWindow : IDisposable
{
    LibreHandle Handle { get; }

    LibreRectangle Bounds { get; set; }

    LibreWindowState State { get; set; }

    bool Visible { get; }

    double DpiScale { get; }

    void Show();

    void Hide();

    void Activate();

    void Close();
}

/// <summary>Creates top-level windows without exposing Silk.NET or OS-specific handles.</summary>
public interface ILibreWindowService
{
    ILibreWindow Create(in LibreWindowCreateOptions options, ILibreWindowEvents events);
}
