// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.Platform;

/// <summary>Optional message-box connection to the caller's source modal frame.</summary>
public interface ILibreModalMessageBoxService : ILibreMessageBoxService
{
    LibreMessageBoxResult Show(in LibreMessageBoxRequest request, ILibreMessageBoxModalLifecycle lifecycle);
}

/// <summary>
/// A single creating-thread source modal generation. The source caller, not the
/// message-box service, requests release after Show returns or throws.
/// </summary>
public interface ILibreMessageBoxModalLifecycle
{
    /// <summary>
    /// Begins the exact visible window's optional native session before pumping.
    /// Null means that the actual window has no native-modal capability; it does
    /// not select a provider or disable the existing source modal frame.
    /// </summary>
    void Begin(ILibreModalWindow? exactWindow);

    /// <summary>
    /// Registers cleanup before showing the window. It runs only after native
    /// release and the source frame's ordered restoration. A failing cleanup
    /// must keep its own retry owner; a returned Dispose is not native proof.
    /// </summary>
    void AfterSourceRelease(Action cleanup);
}
