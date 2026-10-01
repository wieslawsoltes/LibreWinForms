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
            catch (Exception cleanupFailure) { failure.Data[nameof(OwnedPopupConfiguration)] = cleanupFailure; }
            throw;
        }
    }
}
