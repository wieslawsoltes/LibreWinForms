// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.Platform;

/// <summary>Supplies host system settings used by canonical managed controls.</summary>
public interface ILibreSystemSettingsService
{
    bool HighContrast { get; }

    LibreSize BorderSize { get; }

    LibreSize FixedFrameBorderSize { get; }

    LibreSize Border3DSize { get; }

    int VerticalScrollBarWidth { get; }

    int HorizontalScrollBarHeight { get; }

    int VerticalScrollBarArrowHeight { get; }

    int HorizontalScrollBarArrowWidth { get; }
}

/// <summary>Portable baseline used when a host does not expose OS system settings.</summary>
public sealed class DefaultLibreSystemSettingsService : ILibreSystemSettingsService
{
    public static DefaultLibreSystemSettingsService Instance { get; } = new();

    private DefaultLibreSystemSettingsService()
    {
    }

    public bool HighContrast => false;

    public LibreSize BorderSize => new(1, 1);

    public LibreSize FixedFrameBorderSize => new(3, 3);

    public LibreSize Border3DSize => new(2, 2);

    public int VerticalScrollBarWidth => 17;

    public int HorizontalScrollBarHeight => 17;

    public int VerticalScrollBarArrowHeight => 17;

    public int HorizontalScrollBarArrowWidth => 17;
}
