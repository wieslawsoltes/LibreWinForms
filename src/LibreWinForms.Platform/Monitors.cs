// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.Platform;

/// <summary>Stable monitor information used by WinForms screen and DPI logic.</summary>
public readonly record struct LibreMonitor(
    string Id,
    LibreRectangle Bounds,
    LibreRectangle WorkArea,
    double DpiScale,
    bool IsPrimary,
    int BitsPerPixel = 32,
    string? DisplayName = null);

/// <summary>Backend-neutral monitor selection matching the WinForms largest-overlap/nearest behavior.</summary>
public static class LibreMonitorSelection
{
    public static LibreMonitor GetNearest(
        IReadOnlyList<LibreMonitor> monitors,
        LibreRectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0)
        {
            throw new InvalidOperationException("The platform monitor inventory is empty.");
        }

        int bestIndex = 0;
        double bestIntersection = IntersectionArea(monitors[0].Bounds, bounds);
        double bestDistance = DistanceSquared(monitors[0].Bounds, bounds);

        for (int index = 1; index < monitors.Count; index++)
        {
            double intersection = IntersectionArea(monitors[index].Bounds, bounds);
            double distance = DistanceSquared(monitors[index].Bounds, bounds);
            if (intersection > bestIntersection
                || (intersection == bestIntersection && intersection == 0 && distance < bestDistance))
            {
                bestIndex = index;
                bestIntersection = intersection;
                bestDistance = distance;
            }
        }

        return monitors[bestIndex];
    }

    private static double IntersectionArea(LibreRectangle left, LibreRectangle right)
    {
        long intersectionWidth = Math.Min((long)left.X + left.Width, (long)right.X + right.Width)
            - Math.Max(left.X, right.X);
        long intersectionHeight = Math.Min((long)left.Y + left.Height, (long)right.Y + right.Height)
            - Math.Max(left.Y, right.Y);
        return intersectionWidth > 0 && intersectionHeight > 0
            ? (double)intersectionWidth * intersectionHeight
            : 0;
    }

    private static double DistanceSquared(LibreRectangle left, LibreRectangle right)
    {
        long horizontal = AxisDistance(left.X, left.Width, right.X, right.Width);
        long vertical = AxisDistance(left.Y, left.Height, right.Y, right.Height);
        return (double)horizontal * horizontal + (double)vertical * vertical;
    }

    private static long AxisDistance(int firstStart, int firstLength, int secondStart, int secondLength)
    {
        long firstEnd = (long)firstStart + Math.Max(0, firstLength);
        long secondEnd = (long)secondStart + Math.Max(0, secondLength);
        if (firstEnd < secondStart)
        {
            return secondStart - firstEnd;
        }

        return secondEnd < firstStart ? firstStart - secondEnd : 0;
    }
}

public interface ILibreMonitorService
{
    IReadOnlyList<LibreMonitor> GetMonitors();

    LibreMonitor GetNearest(LibreRectangle bounds);
}
