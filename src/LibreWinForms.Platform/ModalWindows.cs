// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.Platform;

/// <summary>Optional source-dialog lifetime on the exact created window.</summary>
public interface ILibreModalWindow
{
    /// <summary>
    /// Begins after this top-level is visible. False means explicitly disabled
    /// policy, not failed native admission; unsupported enabled requests throw.
    /// </summary>
    bool BeginModalDialog();

    /// <summary>
    /// Completes on the source dispatcher only after all native leases release.
    /// Returning, or the native identity disappearing, is not completion.
    /// </summary>
    void ReleaseModalDialog(Action completed);
}
