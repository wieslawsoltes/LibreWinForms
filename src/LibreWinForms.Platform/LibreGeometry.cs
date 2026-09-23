// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.Platform;

/// <summary>Backend-neutral integer point in logical client coordinates.</summary>
public readonly record struct LibrePoint(int X, int Y);

/// <summary>Backend-neutral integer size in logical client coordinates.</summary>
public readonly record struct LibreSize(int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>Backend-neutral integer rectangle in logical coordinates.</summary>
public readonly record struct LibreRectangle(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public int Right => checked(X + Width);

    public int Bottom => checked(Y + Height);
}
