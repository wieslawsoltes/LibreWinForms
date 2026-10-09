// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed partial class NativeModalWindowLifetimeTests
{
    [Fact]
    public void DialogSourceCompletionWaitsForReleaseAndCreatingDispatcherPump()
    {
        var fixture = new Fixture();
        fixture._lifetime.BeginDialog();
        int restored = 0;
        fixture._lifetime.ReleaseDialog(() => restored++);
        Assert.Equal(0, restored);
        fixture._session.Complete();
        Assert.Equal(0, restored);
        Assert.Equal(1, fixture._wakes);
        fixture._lifetime.Pump();
        Assert.Equal(1, restored);
        Assert.Equal(1, fixture._session._begins);
        fixture._lifetime.BeginDialog();
        Assert.Equal(2, fixture._session._begins);
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("close")]
    [InlineData("retire")]
    [InlineData("release")]
    public void DialogBeginCallbackCannotReleaseOrDestroyNativeTransition(string action)
    {
        var fixture = new Fixture();
        int restored = 0;
        fixture._session._begin = () =>
        {
            if (action == "hide") fixture._lifetime.Hide();
            else if (action == "close") fixture._lifetime.Close();
            else if (action == "retire") fixture._lifetime.Retire();
            else fixture._lifetime.ReleaseDialog(() => restored++);
            Assert.Equal(0, fixture._session._releases);
            Assert.Equal(0, fixture.Window._hides);
            Assert.Equal(0, fixture.Window._closes);
            Assert.Equal(0, restored);
            return new TestDialogLease();
        };
        fixture._lifetime.BeginDialog();
        Assert.Equal(1, fixture._session._releases);
        fixture._session.Complete();
        if (action == "retire") Assert.True(fixture._lifetime.CanRetire());
        else fixture._lifetime.Pump();
        Assert.Equal(action == "release" ? 1 : 0, restored);
        Assert.Equal(action == "hide" || action == "retire" ? 1 : 0, fixture.Window._hides);
        Assert.Equal(action == "close" ? 1 : 0, fixture.Window._closes);
    }

    [Fact]
    public void DialogBeginUncertainFailureCannotRestoreAfterQueryDisappears()
    {
        var fixture = new Fixture();
        var failure = new InvalidOperationException("identity release after Begin failed");
        fixture._session._begin = () => { fixture._session._held = false; throw failure; };
        Assert.Same(failure, Record.Exception(fixture._lifetime.BeginDialog));
        int restored = 0;
        Assert.Same(failure, Record.Exception(() => fixture._lifetime.ReleaseDialog(() => restored++)));
        Assert.Same(failure, Record.Exception(fixture._lifetime.Close));
        Assert.Equal(0, restored);
        Assert.Equal(0, fixture.Window._closes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DialogRejectsHiddenAndQueueOnlyHosts(bool queueOnly)
    {
        var fixture = new Fixture(queueOnly);
        fixture.Window._visible = queueOnly;
        Assert.Throws<InvalidOperationException>(fixture._lifetime.BeginDialog);
        Assert.Equal(0, fixture._session._begins);
    }

    [Fact]
    public void DialogVisibilityReadCannotSupersedeNewCloseIntent()
    {
        var fixture = new Fixture();
        fixture.Window._readVisible = fixture._lifetime.Close;
        Assert.Throws<InvalidOperationException>(fixture._lifetime.BeginDialog);
        Assert.Equal(0, fixture._session._begins);
        Assert.Equal(1, fixture.Window._closes);
    }

    private sealed class TestDialogLease : INativeModalDialogLease { }
}
