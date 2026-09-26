// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;
using FluentAssertions;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableFormActivationRequiresNativeConfirmationAndDeduplicatesEvents(bool preselectChild)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false };
        using TextBox editor = new();
        form.Controls.Add(editor);
        List<string> events = [];
        form.Activated += (_, _) => events.Add("activated");
        form.Deactivate += (_, _) => events.Add("deactivated");
        int gotFocus = 0;
        int lostFocus = 0;
        editor.GotFocus += (_, _) => gotFocus++;
        editor.LostFocus += (_, _) => lostFocus++;
        form.Show();
        if (preselectChild)
        {
            form.ActiveControl = editor;
        }

        form.Activate();
        platform.LastActivatedWindow.Should().Be(platform.GetWindowHandle(form));
        Form.ActiveForm.Should().BeNull();
        events.Should().BeEmpty();
        platform.SendFormInput(form, LibreInputEventKind.FocusGained);
        platform.SendFormInput(form, LibreInputEventKind.FocusGained);
        Form.ActiveForm.Should().BeSameAs(form);
        editor.Focused.Should().BeTrue();
        gotFocus.Should().Be(1);
        events.Should().Equal("activated");
        platform.SendFormInput(form, LibreInputEventKind.FocusLost);
        platform.SendFormInput(form, LibreInputEventKind.FocusLost);
        Form.ActiveForm.Should().BeNull();
        editor.Focused.Should().BeFalse();
        lostFocus.Should().Be(1);
        events.Should().Equal("activated", "deactivated");
    }

    [Fact]
    public void PortableFormActivationHandoffRejectsLatePreviousOwnerLoss()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form first = new() { ShowIcon = false };
        using Form second = new() { ShowIcon = false };
        List<string> events = [];
        first.Activated += (_, _) => events.Add("first+");
        first.Deactivate += (_, _) => events.Add("first-");
        second.Activated += (_, _) => events.Add("second+");
        second.Deactivate += (_, _) => events.Add("second-");
        first.Show();
        second.Show();
        platform.SendFormInput(first, LibreInputEventKind.FocusGained);
        platform.SendFormInput(second, LibreInputEventKind.FocusGained);
        first.ContainsFocus.Should().BeFalse();
        Form.ActiveForm.Should().BeSameAs(second);
        platform.SendFormInput(first, LibreInputEventKind.FocusLost);
        Form.ActiveForm.Should().BeSameAs(second);
        events.Should().Equal("first+", "first-", "second+");
        platform.SendFormInput(second, LibreInputEventKind.FocusLost);
        events.Should().Equal("first+", "first-", "second+", "second-");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableActiveFormHideOrDisposeEndsActivation(bool dispose)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false };
        int activated = 0;
        int deactivated = 0;
        form.Activated += (_, _) => activated++;
        form.Deactivate += (_, _) => deactivated++;
        form.Show();
        platform.SendFormInput(form, LibreInputEventKind.FocusGained);
        platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.LeftShift, modifiers: LibreInputModifiers.Shift);
        Control.ModifierKeys.Should().Be(Keys.Shift);
        if (dispose)
            form.Dispose();
        else
            form.Hide();
        Form.ActiveForm.Should().BeNull();
        Control.ModifierKeys.Should().Be(Keys.None);
        form.ContainsFocus.Should().BeFalse();
        activated.Should().Be(1);
        deactivated.Should().Be(1);
        if (!dispose)
        {
            platform.SendFormInput(form, LibreInputEventKind.FocusGained);
            activated.Should().Be(1, "a late event must not activate a hidden window");
            form.Show();
            platform.SendFormInput(form, LibreInputEventKind.FocusGained);
            activated.Should().Be(2);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableActivatedHandlerCanHideOrDisposeWithoutStaleFocus(bool dispose)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false };
        List<string> events = [];
        form.Activated += (_, _) =>
        {
            events.Add("activated");
            if (dispose)
                form.Dispose();
            else
                form.Hide();
        };
        form.Deactivate += (_, _) => events.Add("deactivated");
        form.Show();
        platform.SendFormInput(form, LibreInputEventKind.FocusGained);
        events.Should().Equal("activated", "deactivated");
        form.ContainsFocus.Should().BeFalse();
        Form.ActiveForm.Should().BeNull();
    }

    [Fact]
    public void PortableDeactivatedHandlerCanConfirmAnotherOwnerWithoutStaleActivation()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form first = new() { ShowIcon = false };
        using Form second = new() { ShowIcon = false };
        using Form third = new() { ShowIcon = false };
        first.Show();
        second.Show();
        third.Show();
        List<string> events = [];
        first.Deactivate += (_, _) =>
        {
            events.Add("first-");
            platform.SendFormInput(third, LibreInputEventKind.FocusGained);
        };
        second.Activated += (_, _) => events.Add("second+");
        third.Activated += (_, _) => events.Add("third+");
        platform.SendFormInput(first, LibreInputEventKind.FocusGained);
        platform.SendFormInput(second, LibreInputEventKind.FocusGained);
        Form.ActiveForm.Should().BeSameAs(third);
        first.ContainsFocus.Should().BeFalse();
        second.ContainsFocus.Should().BeFalse();
        third.ContainsFocus.Should().BeTrue();
        events.Should().Equal("first-", "third+");
    }

    [Fact]
    public void PortableFocusHandlerCanConfirmAnotherOwnerBeforeActivated()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form first = new() { ShowIcon = false };
        using Form second = new() { ShowIcon = false };
        using TextBox editor = new();
        first.Controls.Add(editor);
        first.Show();
        second.Show();
        int staleActivated = 0;
        first.Activated += (_, _) => staleActivated++;
        editor.GotFocus += (_, _) => platform.SendFormInput(second, LibreInputEventKind.FocusGained);
        platform.SendFormInput(first, LibreInputEventKind.FocusGained);
        Form.ActiveForm.Should().BeSameAs(second);
        first.ContainsFocus.Should().BeFalse();
        second.ContainsFocus.Should().BeTrue();
        staleActivated.Should().Be(0);
    }

    [Fact]
    public void PortablePreviousOwnerCanDisposePendingOwnerDuringDeactivation()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form first = new() { ShowIcon = false };
        using Form second = new() { ShowIcon = false };
        first.Show();
        second.Show();
        int activated = 0;
        second.Activated += (_, _) => activated++;
        first.Deactivate += (_, _) => second.Dispose();
        platform.SendFormInput(first, LibreInputEventKind.FocusGained);
        platform.SendFormInput(second, LibreInputEventKind.FocusGained);
        second.IsDisposed.Should().BeTrue();
        Form.ActiveForm.Should().BeNull();
        activated.Should().Be(0);
        first.ContainsFocus.Should().BeFalse();
        second.ContainsFocus.Should().BeFalse();
    }

    [Fact]
    public void PortableDeactivatedHandlerActivationRequestDoesNotReclaimOwnership()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form first = new() { ShowIcon = false };
        using Form second = new() { ShowIcon = false };
        first.Show();
        second.Show();
        first.Deactivate += (_, _) => first.Activate();
        platform.SendFormInput(first, LibreInputEventKind.FocusGained);
        platform.SendFormInput(second, LibreInputEventKind.FocusGained);
        platform.LastActivatedWindow.Should().Be(platform.GetWindowHandle(first));
        Form.ActiveForm.Should().BeSameAs(second);
        first.ContainsFocus.Should().BeFalse();
        second.ContainsFocus.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableDeactivationExceptionRollsBackOnlyPendingOwner(bool confirmThird)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form first = new() { ShowIcon = false };
        using Form second = new() { ShowIcon = false };
        using Form third = new() { ShowIcon = false };
        first.Show();
        second.Show();
        third.Show();
        InvalidOperationException expected = new("activation callback failure");
        EventHandler fail = (_, _) =>
        {
            if (confirmThird)
            {
                platform.SendFormInput(third, LibreInputEventKind.FocusGained);
            }

            throw expected;
        };
        first.Deactivate += fail;
        platform.SendFormInput(first, LibreInputEventKind.FocusGained);
        Action gain = () => platform.SendFormInput(second, LibreInputEventKind.FocusGained);
        gain.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(expected);
        Form.ActiveForm.Should().BeSameAs(confirmThird ? third : null);
        first.ContainsFocus.Should().BeFalse();
        second.ContainsFocus.Should().BeFalse();
        first.Deactivate -= fail;
        platform.SendFormInput(second, LibreInputEventKind.FocusGained);
        Form.ActiveForm.Should().BeSameAs(second);
        second.ContainsFocus.Should().BeTrue();
    }

    [Fact]
    public void PortableLateFocusLossPreservesCurrentOwnersModifierState()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form first = new() { ShowIcon = false };
        using Form second = new() { ShowIcon = false };
        first.Show();
        second.Show();
        platform.SendFormInput(first, LibreInputEventKind.FocusGained);
        platform.SendFormInput(second, LibreInputEventKind.FocusGained);
        platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.LeftShift, modifiers: LibreInputModifiers.Shift);
        Control.ModifierKeys.Should().Be(Keys.Shift);
        platform.SendFormInput(first, LibreInputEventKind.FocusLost);
        Control.ModifierKeys.Should().Be(Keys.Shift);
        Form.ActiveForm.Should().BeSameAs(second);
        platform.SendInput(LibreInputEventKind.KeyUp, key: LibreKey.LeftShift);
        Control.ModifierKeys.Should().Be(Keys.None);
    }
}
