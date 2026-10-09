// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using PopupInteractionApp;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public class PopupInteractionStartupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginalWorkloadAndSharedModalActionAreIndependentOfProvider(bool portable)
    {
        var ordinary = PopupInteractionStartup.Parse(["evidence", "run"], portable);
        Assert.False(ordinary.ModalDialog);
        Assert.False(ordinary.NativeModalSessions);
        var modal = PopupInteractionStartup.Parse(["evidence", "run", "--modal-dialog"], portable);
        Assert.True(modal.ModalDialog);
        Assert.False(modal.NativeModalSessions);
        Assert.Equal("evidence", modal.EvidenceDirectory);
        Assert.Equal("run", modal.RunId);
    }

    [Fact]
    public void NativeSessionOptionKeepsActualSourceActionAndOriginalArguments()
    {
        string[] arguments = ["evidence", "run", "--modal-dialog", "--libre-native-modal-sessions"];
        string[] original = (string[])arguments.Clone();
        var startup = PopupInteractionStartup.Parse(arguments, portable: true);
        Assert.True(startup.ModalDialog);
        Assert.True(startup.NativeModalSessions);
        Assert.Equal(original, arguments);
        Assert.Throws<PlatformNotSupportedException>(() => PopupInteractionStartup.Parse(arguments, portable: false));
    }

    [Theory]
    [InlineData("--modal-dialog", "--modal-dialog")]
    [InlineData("--libre-native-modal-sessions", "--libre-native-modal-sessions")]
    [InlineData("--modal-dialog", "--unknown")]
    [InlineData("--", "--libre-native-modal-sessions")]
    public void DuplicateOrUnrecognizedRunnerOptionsCannotSilentlyChangeWorkload(string first, string second)
        => Assert.Throws<ArgumentException>(() => PopupInteractionStartup.Parse(["evidence", "run", first, second], true));
}
