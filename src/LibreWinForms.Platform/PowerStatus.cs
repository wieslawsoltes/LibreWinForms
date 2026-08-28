// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.Platform;

/// <summary>Identifies the host AC power state without exposing a WinForms enum.</summary>
public enum LibrePowerLineStatus
{
    Offline,
    Online,
    Unknown,
}

/// <summary>Identifies host battery states without exposing a WinForms enum.</summary>
[Flags]
public enum LibreBatteryChargeStatus
{
    None = 0,
    High = 1 << 0,
    Low = 1 << 1,
    Critical = 1 << 2,
    Charging = 1 << 3,
    NoSystemBattery = 1 << 4,
    Unknown = 1 << 5,
}

/// <summary>Contains one atomic host power-status observation.</summary>
public readonly record struct LibrePowerStatusSnapshot(
    LibrePowerLineStatus PowerLineStatus,
    LibreBatteryChargeStatus BatteryChargeStatus,
    int BatteryFullLifetime,
    float BatteryLifePercent,
    int BatteryLifeRemaining);

/// <summary>Supplies current host power information to canonical WinForms.</summary>
public interface ILibrePowerStatusService
{
    LibrePowerStatusSnapshot GetCurrentStatus();
}

/// <summary>Portable fallback for hosts that do not publish power information.</summary>
public sealed class DefaultLibrePowerStatusService : ILibrePowerStatusService
{
    public static DefaultLibrePowerStatusService Instance { get; } = new();

    private DefaultLibrePowerStatusService()
    {
    }

    public LibrePowerStatusSnapshot GetCurrentStatus()
        => new(
            LibrePowerLineStatus.Unknown,
            LibreBatteryChargeStatus.Unknown,
            BatteryFullLifetime: -1,
            BatteryLifePercent: 1f,
            BatteryLifeRemaining: -1);
}
