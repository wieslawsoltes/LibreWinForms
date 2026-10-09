// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace PopupInteractionApp;

internal readonly record struct PopupInteractionStartup(
    string EvidenceDirectory, string RunId, bool ModalDialog, bool NativeModalSessions)
{
    internal static PopupInteractionStartup Parse(string[] arguments, bool portable)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Length < 2 || string.IsNullOrWhiteSpace(arguments[0]) || string.IsNullOrWhiteSpace(arguments[1]))
            throw new ArgumentException("Supply an evidence directory and unique run identifier, optionally --modal-dialog and --libre-native-modal-sessions.", nameof(arguments));
        bool modal = false;
        bool native = false;
        for (int index = 2; index < arguments.Length; index++)
        {
            switch (arguments[index])
            {
                case "--modal-dialog" when !modal:
                    modal = true;
                    break;
                case "--libre-native-modal-sessions" when !native:
                    native = true;
                    break;
                default:
                    throw new ArgumentException("Unknown or duplicate popup runner option.", nameof(arguments));
            }
        }

        if (native && !portable)
            throw new PlatformNotSupportedException("Microsoft reference uses --modal-dialog with its original native dialog policy.");
        return new(arguments[0], arguments[1], modal || native, native);
    }
}
