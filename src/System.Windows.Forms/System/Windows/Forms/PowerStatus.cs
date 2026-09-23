// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using LibreWinForms.Platform;
#else
using Windows.Win32.System.Power;
#endif

namespace System.Windows.Forms;

public class PowerStatus
{
#if LIBREWINFORMS_PORTABLE
    private LibrePowerStatusSnapshot _powerStatus;
#else
    private SYSTEM_POWER_STATUS _systemPowerStatus;
#endif

    internal PowerStatus()
    {
    }

    public PowerLineStatus PowerLineStatus
    {
        get
        {
            UpdateSystemPowerStatus();
#if LIBREWINFORMS_PORTABLE
            return _powerStatus.PowerLineStatus switch
            {
                LibrePowerLineStatus.Offline => PowerLineStatus.Offline,
                LibrePowerLineStatus.Online => PowerLineStatus.Online,
                _ => PowerLineStatus.Unknown,
            };
#else
            return (PowerLineStatus)_systemPowerStatus.ACLineStatus;
#endif
        }
    }

    public BatteryChargeStatus BatteryChargeStatus
    {
        get
        {
            UpdateSystemPowerStatus();
#if LIBREWINFORMS_PORTABLE
            return GetBatteryChargeStatus(_powerStatus.BatteryChargeStatus);
#else
            return (BatteryChargeStatus)_systemPowerStatus.BatteryFlag;
#endif
        }
    }

    public int BatteryFullLifetime
    {
        get
        {
            UpdateSystemPowerStatus();
#if LIBREWINFORMS_PORTABLE
            return _powerStatus.BatteryFullLifetime;
#else
            return (int)_systemPowerStatus.BatteryFullLifeTime;
#endif
        }
    }

    public float BatteryLifePercent
    {
        get
        {
            UpdateSystemPowerStatus();
#if LIBREWINFORMS_PORTABLE
            float lifePercent = _powerStatus.BatteryLifePercent;
#else
            float lifePercent = _systemPowerStatus.BatteryLifePercent / 100f;
#endif
            return lifePercent > 1f ? 1f : lifePercent;
        }
    }

    public int BatteryLifeRemaining
    {
        get
        {
            UpdateSystemPowerStatus();
#if LIBREWINFORMS_PORTABLE
            return _powerStatus.BatteryLifeRemaining;
#else
            return (int)_systemPowerStatus.BatteryLifeTime;
#endif
        }
    }

    private void UpdateSystemPowerStatus()
    {
#if LIBREWINFORMS_PORTABLE
        _powerStatus = LibrePlatform.IsRegistered
            ? LibrePlatform.Current.PowerStatus.GetCurrentStatus()
            : DefaultLibrePowerStatusService.Instance.GetCurrentStatus();
#else
        PInvoke.GetSystemPowerStatus(out _systemPowerStatus);
#endif
    }

#if LIBREWINFORMS_PORTABLE
    private static BatteryChargeStatus GetBatteryChargeStatus(LibreBatteryChargeStatus status)
    {
        BatteryChargeStatus result = 0;
        if ((status & LibreBatteryChargeStatus.High) != 0)
        {
            result |= BatteryChargeStatus.High;
        }

        if ((status & LibreBatteryChargeStatus.Low) != 0)
        {
            result |= BatteryChargeStatus.Low;
        }

        if ((status & LibreBatteryChargeStatus.Critical) != 0)
        {
            result |= BatteryChargeStatus.Critical;
        }

        if ((status & LibreBatteryChargeStatus.Charging) != 0)
        {
            result |= BatteryChargeStatus.Charging;
        }

        if ((status & LibreBatteryChargeStatus.NoSystemBattery) != 0)
        {
            result |= BatteryChargeStatus.NoSystemBattery;
        }

        return (status & LibreBatteryChargeStatus.Unknown) != 0
            ? BatteryChargeStatus.Unknown
            : result;
    }
#endif
}
