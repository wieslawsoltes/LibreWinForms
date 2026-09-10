// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.ObjectModel;
using System.Globalization;

namespace LibreWinForms.Platform;

/// <summary>
/// Describes one host input language without exposing an HKL or another
/// operating-system-specific keyboard-layout object.
/// </summary>
public sealed record LibreInputLanguageDescriptor
{
    public LibreInputLanguageDescriptor(
        nint token,
        string languageTag,
        string layoutId,
        string layoutName)
    {
        ArgumentOutOfRangeException.ThrowIfZero(token);

        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutId);
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutName);
        _ = CultureInfo.GetCultureInfo(languageTag);

        Token = token;
        LanguageTag = languageTag;
        LayoutId = layoutId;
        LayoutName = layoutName;
    }

    /// <summary>Gets the process-local identity used by canonical WinForms.</summary>
    public nint Token { get; }

    /// <summary>Gets the BCP 47 language tag.</summary>
    public string LanguageTag { get; }

    /// <summary>Gets the host layout identifier.</summary>
    public string LayoutId { get; }

    /// <summary>Gets the user-facing layout name.</summary>
    public string LayoutName { get; }
}

/// <summary>Supplies host input-language inventory and activation.</summary>
public interface ILibreInputLanguageService
{
    LibreInputLanguageDescriptor Current { get; }

    LibreInputLanguageDescriptor Default { get; }

    IReadOnlyList<LibreInputLanguageDescriptor> Installed { get; }

    bool TryGet(nint token, out LibreInputLanguageDescriptor descriptor);

    bool TryActivate(nint token);
}

/// <summary>
/// Conservative managed fallback for hosts without a native keyboard-layout
/// provider. It exposes only the creating thread's culture instead of
/// fabricating an installed-layout inventory.
/// </summary>
public sealed class DefaultLibreInputLanguageService : ILibreInputLanguageService
{
    public static DefaultLibreInputLanguageService Instance { get; } =
        new(CultureInfo.CurrentCulture);

    private readonly LibreInputLanguageDescriptor _language;
    private readonly ReadOnlyCollection<LibreInputLanguageDescriptor> _installed;

    public DefaultLibreInputLanguageService(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (string.IsNullOrEmpty(culture.Name))
        {
            culture = CultureInfo.GetCultureInfo("en-US");
        }

        nint token = culture.LCID;
        _language = new LibreInputLanguageDescriptor(
            token,
            culture.Name,
            culture.LCID.ToString("X8", CultureInfo.InvariantCulture),
            culture.DisplayName);
        _installed = Array.AsReadOnly([_language]);
    }

    public LibreInputLanguageDescriptor Current => _language;

    public LibreInputLanguageDescriptor Default => _language;

    public IReadOnlyList<LibreInputLanguageDescriptor> Installed => _installed;

    public bool TryGet(nint token, out LibreInputLanguageDescriptor descriptor)
    {
        if (token == _language.Token)
        {
            descriptor = _language;
            return true;
        }

        descriptor = null!;
        return false;
    }

    public bool TryActivate(nint token) => token == _language.Token;
}
