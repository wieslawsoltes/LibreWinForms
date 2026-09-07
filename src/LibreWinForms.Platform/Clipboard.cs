// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.Platform;

/// <summary>
/// Owns the local operating system clipboard while keeping WinForms data-object semantics in
/// System.Windows.Forms.
/// </summary>
public interface ILibreClipboardService
{
    bool IsSupported { get; }

    void Clear();

    ILibreDataTransfer? GetData();

    void SetData(ILibreDataTransfer data, bool persist, int retryTimes, int retryDelay);
}

public sealed class UnsupportedLibreClipboardService : ILibreClipboardService
{
    public static UnsupportedLibreClipboardService Instance { get; } = new();

    private UnsupportedLibreClipboardService()
    {
    }

    public bool IsSupported => false;

    public void Clear()
        => throw CreateException();

    public ILibreDataTransfer? GetData()
        => throw CreateException();

    public void SetData(ILibreDataTransfer data, bool persist, int retryTimes, int retryDelay)
    {
        ArgumentNullException.ThrowIfNull(data);
        throw CreateException();
    }

    private static PlatformNotSupportedException CreateException()
        => new("The registered LibreWinForms backend does not provide a system clipboard.");
}
