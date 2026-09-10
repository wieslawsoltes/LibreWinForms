// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;
using Silk.NET.GLFW;

namespace LibreWinForms.ProGPU;

/// <summary>
/// Bridges portable WinForms data objects to the process clipboard and publishes interoperable
/// Unicode text through GLFW. Rich/custom formats remain available to this process, which is the
/// behavior required by the WinForms designer clipboard pipeline.
/// </summary>
public sealed class ProGpuClipboardService : ILibreClipboardService
{
    private static readonly string[] s_textFormats = ["UnicodeText", "System.String", "Text"];

    private readonly ProGpuDispatcher _dispatcher;
    private readonly Func<string?> _readSystemText;
    private readonly Action<string> _writeSystemText;
    private ILibreDataTransfer? _managedData;
    private string? _publishedText;

    public ProGpuClipboardService(ProGpuDispatcher dispatcher)
        : this(dispatcher, ReadGlfwText, WriteGlfwText)
    {
    }

    internal ProGpuClipboardService(
        ProGpuDispatcher dispatcher,
        Func<string?> readSystemText,
        Action<string> writeSystemText)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _readSystemText = readSystemText ?? throw new ArgumentNullException(nameof(readSystemText));
        _writeSystemText = writeSystemText ?? throw new ArgumentNullException(nameof(writeSystemText));
    }

    public bool IsSupported => true;

    public void Clear()
        => Send(() =>
        {
            _managedData = null;
            _publishedText = null;
            TryWriteSystemText(string.Empty);
        });

    public ILibreDataTransfer? GetData()
    {
        ILibreDataTransfer? result = null;
        Send(() =>
        {
            string? systemText = TryReadSystemText();
            if (!string.IsNullOrEmpty(systemText))
            {
                result = string.Equals(systemText, _publishedText, StringComparison.Ordinal)
                    ? _managedData
                    : new TextDataTransfer(systemText);
                return;
            }

            // A rich in-process payload may have no text representation. Preserve it until this
            // process explicitly clears or replaces the clipboard.
            result = _publishedText is null ? _managedData : null;
        });

        return result;
    }

    public void SetData(ILibreDataTransfer data, bool persist, int retryTimes, int retryDelay)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(retryTimes);
        ArgumentOutOfRangeException.ThrowIfNegative(retryDelay);

        Send(() =>
        {
            _managedData = data;
            _publishedText = GetText(data);
            if (_publishedText is not null)
            {
                TryWriteSystemText(_publishedText);
            }
        });
    }

    private void Send(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.Send(action);
        }
    }

    private string? TryReadSystemText()
    {
        try
        {
            return _readSystemText();
        }
        catch (Exception exception) when (IsClipboardUnavailable(exception))
        {
            return _publishedText;
        }
    }

    private void TryWriteSystemText(string text)
    {
        try
        {
            _writeSystemText(text);
        }
        catch (Exception exception) when (IsClipboardUnavailable(exception))
        {
            // Headless test and server sessions still retain the canonical in-process data object.
        }
    }

    private static string? GetText(ILibreDataTransfer data)
    {
        foreach (string format in s_textFormats)
        {
            if (data.Contains(format, autoConvert: true)
                && data.GetData(format, autoConvert: true) is string text)
            {
                return text;
            }
        }

        return null;
    }

    private static bool IsClipboardUnavailable(Exception exception)
        => exception is DllNotFoundException
            or EntryPointNotFoundException
            or GlfwException
            or InvalidOperationException;

    private static unsafe string? ReadGlfwText()
        => GlfwProvider.GLFW.Value.GetClipboardString(window: null);

    private static unsafe void WriteGlfwText(string text)
        => GlfwProvider.GLFW.Value.SetClipboardString(window: null, text);

    private sealed class TextDataTransfer(string text) : ILibreDataTransfer
    {
        public IReadOnlyList<string> Formats => s_textFormats;

        public bool Contains(string format, bool autoConvert)
            => s_textFormats.Contains(format, StringComparer.Ordinal);

        public object? GetData(string format, bool autoConvert)
            => Contains(format, autoConvert) ? text : null;
    }
}
