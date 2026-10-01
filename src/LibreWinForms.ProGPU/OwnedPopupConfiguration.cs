// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.ProGPU;

internal static class OwnedPopupConfiguration
{
    internal static void Apply(Func<bool> configure, Func<bool> isLive, Action discard, string option)
    {
        try
        {
            if (!configure() || !isLive())
                throw new PlatformNotSupportedException($"The owned popup rejected {option}.");
        }
        catch (Exception failure)
        {
            // A failed native mutation is not staged desired state that may be
            // reapplied at Show. Retire the same provider through its source owner.
            try { discard(); }
            catch (Exception cleanupFailure) { AttachCleanup(failure, cleanupFailure, nameof(OwnedPopupConfiguration)); }
            throw;
        }
    }

    internal static void AttachCleanup(Exception failure, Exception cleanupFailure, string owner)
    {
        // Exception.Data is virtual and may itself throw or be read-only.
        // Cleanup diagnostics can never replace the original native failure.
        try { failure.Data[owner] = cleanupFailure; }
        catch { }
    }
}
