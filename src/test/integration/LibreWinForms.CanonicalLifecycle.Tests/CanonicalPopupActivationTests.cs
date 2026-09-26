// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
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
    public void PortableContextMenuClosesWhenItsOwnerLosesActivation(bool handoff)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using Form other = new() { ShowIcon = false };
        using Panel source = new();
        owner.Controls.Add(source);
        owner.Show();
        other.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        List<ToolStripDropDownCloseReason> closed = [];
        menu.Closed += (_, args) => closed.Add(args.CloseReason);
        menu.Show(source, Point.Empty);
        menu.Visible.Should().BeTrue();
        menu.SourceControl.Should().BeSameAs(source);

        if (handoff)
            platform.SendFormInput(other, LibreInputEventKind.FocusGained);
        else
            platform.SendFormInput(owner, LibreInputEventKind.FocusLost);

        menu.Visible.Should().BeFalse();
        closed.Should().Equal(ToolStripDropDownCloseReason.AppFocusChange);
        menu.SourceControl.Should().BeNull();
        platform.SendFormInput(owner, LibreInputEventKind.FocusLost);
        closed.Should().HaveCount(1);
    }

    [Fact]
    public void PortableContextMenuAutoCloseFalseIgnoresOwnerDeactivation()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        using ContextMenuStrip menu = new() { AutoClose = false };
        menu.Items.Add("Open");
        int closing = 0;
        menu.Closing += (_, _) => closing++;
        menu.Show(owner, Point.Empty);
        platform.SendFormInput(owner, LibreInputEventKind.FocusLost);
        menu.Visible.Should().BeTrue();
        closing.Should().Be(0);
        menu.Close();
        menu.Visible.Should().BeFalse();
        closing.Should().Be(1);
    }

    [Fact]
    public void PortableContextMenuCanceledDeactivationRemainsSubscribedUntilAcceptedClose()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        int closing = 0;
        bool cancel = true;
        menu.Closing += (_, args) =>
        {
            closing++;
            args.CloseReason.Should().Be(ToolStripDropDownCloseReason.AppFocusChange);
            args.Cancel = cancel;
        };
        menu.Show(owner, Point.Empty);
        platform.SendFormInput(owner, LibreInputEventKind.FocusLost);
        closing.Should().Be(1);
        menu.Visible.Should().BeTrue();
        cancel = false;
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        platform.SendFormInput(owner, LibreInputEventKind.FocusLost);
        closing.Should().Be(2);
        menu.Visible.Should().BeFalse();
    }

    [Fact]
    public void PortableNestedContextMenusCloseThroughTheirExistingDropDownChain()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        using ContextMenuStrip menu = new();
        var parent = new ToolStripMenuItem("More");
        parent.DropDownItems.Add("Child");
        menu.Items.Add(parent);
        List<string> closed = [];
        menu.Closed += (_, args) => closed.Add($"parent:{args.CloseReason}");
        parent.DropDown.Closed += (_, args) => closed.Add($"child:{args.CloseReason}");
        menu.Show(owner, Point.Empty);
        parent.ShowDropDown();
        menu.Visible.Should().BeTrue();
        parent.DropDown.Visible.Should().BeTrue();
        platform.SendFormInput(owner, LibreInputEventKind.FocusLost);
        menu.Visible.Should().BeFalse();
        parent.DropDown.Visible.Should().BeFalse();
        closed.Should().Equal("child:AppFocusChange", "parent:AppFocusChange");
    }

    [Fact]
    public void PortableMenuStripDropDownUsesItsContainingFormOwner()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using MenuStrip strip = new();
        var parent = new ToolStripMenuItem("File");
        parent.DropDownItems.Add("Open");
        strip.Items.Add(parent);
        owner.Controls.Add(strip);
        owner.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        parent.ShowDropDown();
        parent.DropDown.Visible.Should().BeTrue();
        platform.SendFormInput(owner, LibreInputEventKind.FocusLost);
        parent.DropDown.Visible.Should().BeFalse();
    }

    [Fact]
    public void PortableContextMenuReopenReleasesItsPreviousOwnerSubscription()
    {
        _ = UseHeadlessPlatform(autoCloseWindows: false);
        using PopupActivationOwner first = new() { ShowIcon = false };
        using PopupActivationOwner second = new() { ShowIcon = false };
        first.Show();
        second.Show();
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        menu.Show(first, Point.Empty);
        menu.Close();
        menu.Show(second, Point.Empty);
        first.RaiseDeactivated();
        menu.Visible.Should().BeTrue();
        second.RaiseDeactivated();
        menu.Visible.Should().BeFalse();
    }

    [Fact]
    public void PortableContextMenuDisposedBeforeOwnerDeactivationDoesNotCloseAgain()
    {
        _ = UseHeadlessPlatform(autoCloseWindows: false);
        using PopupActivationOwner owner = new() { ShowIcon = false };
        owner.Show();
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        menu.Show(owner, Point.Empty);
        menu.Dispose();
        int closing = 0;
        menu.Closing += (_, _) => closing++;
        owner.RaiseDeactivated();
        closing.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableNestedMenuClosesChildBeforePersistentOrCanceledRoot(bool rootAutoClose)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        using ContextMenuStrip menu = new() { AutoClose = rootAutoClose };
        var parent = new ToolStripMenuItem("More");
        parent.DropDownItems.Add("Child");
        menu.Items.Add(parent);
        int rootClosing = 0;
        menu.Closing += (_, args) => { rootClosing++; args.Cancel = true; };
        menu.Show(owner, Point.Empty);
        parent.ShowDropDown();
        platform.SendFormInput(owner, LibreInputEventKind.FocusLost);
        parent.DropDown.Visible.Should().BeFalse();
        menu.Visible.Should().BeTrue();
        rootClosing.Should().Be(rootAutoClose ? 1 : 0);
    }

    [Fact]
    public void PortableNestedMenuCancellationUsesTheNativeBoundedActiveLeafPolicy()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        using ContextMenuStrip menu = new();
        var parent = new ToolStripMenuItem("More");
        parent.DropDownItems.Add("Child");
        menu.Items.Add(parent);
        bool cancel = true;
        int childClosing = 0;
        int rootClosing = 0;
        parent.DropDown.Closing += (_, args) => { childClosing++; args.Cancel = cancel; };
        menu.Closing += (_, _) => rootClosing++;
        menu.Show(owner, Point.Empty);
        parent.ShowDropDown();
        platform.SendFormInput(owner, LibreInputEventKind.FocusLost);
        childClosing.Should().Be(2, "the original filter attempts the active leaf once per initially registered dropdown");
        rootClosing.Should().Be(0);
        menu.Visible.Should().BeTrue();
        parent.DropDown.Visible.Should().BeTrue();
        cancel = false;
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        platform.SendFormInput(owner, LibreInputEventKind.FocusLost);
        childClosing.Should().Be(3);
        rootClosing.Should().Be(1);
        menu.Visible.Should().BeFalse();
        parent.DropDown.Visible.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PortableMenuDeactivationDuringOpeningWaitsForTheActualOpeningChain(bool child, bool opened)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        using ContextMenuStrip menu = new();
        var parent = new ToolStripMenuItem("More");
        parent.DropDownItems.Add("Child");
        menu.Items.Add(parent);
        ToolStripDropDown target = child ? parent.DropDown : menu;
        if (opened)
            target.Opened += (_, _) => platform.SendFormInput(owner, LibreInputEventKind.FocusLost);
        else
            target.Opening += (_, _) => platform.SendFormInput(owner, LibreInputEventKind.FocusLost);
        int rootClosed = 0;
        int childClosed = 0;
        menu.Closed += (_, _) => rootClosed++;
        parent.DropDown.Closed += (_, _) => childClosed++;
        menu.Show(owner, Point.Empty);
        if (child)
            parent.ShowDropDown();
        menu.Visible.Should().BeFalse();
        parent.DropDown.Visible.Should().BeFalse();
        rootClosed.Should().Be(1);
        childClosed.Should().Be(child ? 1 : 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableMenuFailedOrCanceledOpeningDoesNotDuplicateOwnerSubscription(bool throwOpening)
    {
        _ = UseHeadlessPlatform(autoCloseWindows: false);
        using PopupActivationOwner owner = new() { ShowIcon = false };
        owner.Show();
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        var failure = new InvalidOperationException("opening rejected");
        System.ComponentModel.CancelEventHandler reject = (_, args) =>
        {
            if (throwOpening) throw failure;
            args.Cancel = true;
        };
        menu.Opening += reject;
        Action show = () => menu.Show(owner, Point.Empty);
        if (throwOpening)
            show.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        else
            show();
        menu.Visible.Should().BeFalse();
        menu.Opening -= reject;
        int closing = 0;
        menu.Closing += (_, args) => { closing++; args.Cancel = true; };
        menu.Show(owner, Point.Empty);
        owner.RaiseDeactivated();
        closing.Should().Be(1);
        menu.Visible.Should().BeTrue();
    }

    [Fact]
    public void PortableMenuOpenedExceptionRetainsOwnershipOfTheVisibleMenu()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        var failure = new InvalidOperationException("opened handler failed");
        menu.Opened += (_, _) => throw failure;
        Action show = () => menu.Show(owner, Point.Empty);
        show.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        menu.Visible.Should().BeTrue();
        platform.SendFormInput(owner, LibreInputEventKind.FocusLost);
        menu.Visible.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PortableChildOpenedInsideRootOpeningSharesThePendingOwnerLoss(bool opened, bool cancelRoot)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        using ContextMenuStrip menu = new();
        var parent = new ToolStripMenuItem("More");
        parent.DropDownItems.Add("Child");
        menu.Items.Add(parent);
        if (opened)
            parent.DropDown.Opened += (_, _) => platform.SendFormInput(owner, LibreInputEventKind.FocusLost);
        else
            parent.DropDown.Opening += (_, _) => platform.SendFormInput(owner, LibreInputEventKind.FocusLost);
        menu.Opening += (_, args) =>
        {
            parent.ShowDropDown();
            parent.DropDown.Visible.Should().BeTrue("the enclosing opening transaction has not committed");
            args.Cancel = cancelRoot;
        };
        menu.Show(owner, Point.Empty);
        menu.Visible.Should().BeFalse();
        parent.DropDown.Visible.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableHiddenRootRetainsItsVisibleChildOwnerAfterCanceledOrFailedOpening(bool throwOpening)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        using ContextMenuStrip menu = new();
        var parent = new ToolStripMenuItem("More");
        parent.DropDownItems.Add("Child");
        menu.Items.Add(parent);
        var failure = new InvalidOperationException("root opening failed");
        menu.Opening += (_, args) =>
        {
            parent.ShowDropDown();
            if (throwOpening) throw failure;
            args.Cancel = true;
        };
        Action show = () => menu.Show(owner, Point.Empty);
        if (throwOpening)
            show.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        else
            show();
        menu.Visible.Should().BeFalse();
        parent.DropDown.Visible.Should().BeTrue();
        platform.SendFormInput(owner, LibreInputEventKind.FocusLost);
        parent.DropDown.Visible.Should().BeFalse();
    }

    [Fact]
    public void PortableHiddenRootReopeningReplacesTheRetainedChildOwnerExactlyOnce()
    {
        _ = UseHeadlessPlatform(autoCloseWindows: false);
        using PopupActivationOwner first = new() { ShowIcon = false };
        using PopupActivationOwner second = new() { ShowIcon = false };
        first.Show();
        second.Show();
        using ContextMenuStrip menu = new();
        var parent = new ToolStripMenuItem("More");
        parent.DropDownItems.Add("Child");
        menu.Items.Add(parent);
        System.ComponentModel.CancelEventHandler cancel = (_, args) =>
        {
            parent.ShowDropDown();
            args.Cancel = true;
        };
        menu.Opening += cancel;
        menu.Show(first, Point.Empty);
        menu.Opening -= cancel;
        menu.Show(second, Point.Empty);
        int attempts = 0;
        menu.Closing += (_, args) => { attempts++; args.Cancel = true; };
        first.RaiseDeactivated();
        attempts.Should().Be(0);
        second.RaiseDeactivated();
        attempts.Should().Be(2, "one subscription runs the two-entry native close policy");
    }

    [Fact]
    public void PortableLogicalMenuHandleDoesNotChangeTheOwnerWindowTopMost()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        using ContextMenuStrip menu = new() { AutoClose = false };
        menu.Items.Add("Open");
        _ = menu.Handle;
        // Dropdowns currently have logical handles, not native popup windows.
        // Keep this source-only route safe without claiming native topmost.
        platform.LastWindowTopMost.Should().BeFalse();
        menu.AutoClose = true;
        platform.LastWindowTopMost.Should().BeFalse();
        menu.AutoClose = false;
        platform.LastWindowTopMost.Should().BeFalse();
    }

    [Fact]
    public void PortableExplicitRootCloseRetainsAChildThatCanceledClosing()
    {
        _ = UseHeadlessPlatform(autoCloseWindows: false);
        using PopupActivationOwner owner = new() { ShowIcon = false };
        owner.Show();
        using ContextMenuStrip menu = new();
        var parent = new ToolStripMenuItem("More");
        parent.DropDownItems.Add("Child");
        menu.Items.Add(parent);
        bool cancel = true;
        parent.DropDown.Closing += (_, args) => args.Cancel = cancel;
        menu.Show(owner, Point.Empty);
        parent.ShowDropDown();
        menu.Close();
        menu.Visible.Should().BeFalse();
        parent.DropDown.Visible.Should().BeTrue();
        cancel = false;
        owner.RaiseDeactivated();
        parent.DropDown.Visible.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableHiddenRootRetainsPersistentChildAndReleasesDisposedChain(bool disposeRoot)
    {
        _ = UseHeadlessPlatform(autoCloseWindows: false);
        using PopupActivationOwner owner = new() { ShowIcon = false };
        owner.Show();
        using ContextMenuStrip menu = new();
        var parent = new ToolStripMenuItem("More");
        parent.DropDownItems.Add("Child");
        parent.DropDown.AutoClose = false;
        menu.Items.Add(parent);
        menu.Opening += (_, args) => { parent.ShowDropDown(); args.Cancel = true; };
        menu.Show(owner, Point.Empty);
        parent.DropDown.Visible.Should().BeTrue();
        int attempts = 0;
        parent.DropDown.Closing += (_, _) => attempts++;
        owner.RaiseDeactivated();
        attempts.Should().Be(0);
        parent.DropDown.Visible.Should().BeTrue();
        if (disposeRoot)
            menu.Dispose();
        else
            parent.DropDown.Dispose();
        owner.RaiseDeactivated();
        attempts.Should().Be(0);
    }

    [Fact]
    public void PortableMenuFailedHideRetainsTheStillVisibleOwnerSubscription()
    {
        _ = UseHeadlessPlatform(autoCloseWindows: false);
        using PopupActivationOwner owner = new() { ShowIcon = false };
        owner.Show();
        using ContextMenuStrip menu = new();
        ToolStripItem item = menu.Items.Add("Open");
        menu.Show(owner, Point.Empty);
        item.Select();
        item.Selected.Should().BeTrue();
        var failure = new InvalidOperationException("selection invalidation failed");
        bool fail = true;
        menu.Invalidated += (_, _) =>
        {
            if (fail) throw failure;
        };
        Action close = () => menu.Close();
        close.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        menu.Visible.Should().BeTrue();
        fail = false;
        owner.RaiseDeactivated();
        menu.Visible.Should().BeFalse();
    }

    private sealed class PopupActivationOwner : Form
    {
        // Source lifecycle control only; native activation is exercised by the
        // typed FocusGained/FocusLost cases above, not fabricated by this hook.
        internal void RaiseDeactivated() => OnDeactivate(EventArgs.Empty);
    }
}
