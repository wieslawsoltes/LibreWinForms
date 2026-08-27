// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.Platform;

/// <summary>Supplies host system settings used by canonical managed controls.</summary>
public interface ILibreSystemSettingsService
{
    bool HighContrast { get; }
}

/// <summary>Portable baseline used when a host does not expose OS accessibility settings.</summary>
public sealed class DefaultLibreSystemSettingsService : ILibreSystemSettingsService
{
    public static DefaultLibreSystemSettingsService Instance { get; } = new();

    private DefaultLibreSystemSettingsService()
    {
    }

    public bool HighContrast => false;
}
