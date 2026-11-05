// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;
using FluentAssertions;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(0, false, true)]
    [InlineData(0, true, false)]
    [InlineData(0, true, true)]
    [InlineData(1, false, false)]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    [InlineData(1, true, true)]
    [InlineData(2, false, false)]
    [InlineData(2, false, true)]
    [InlineData(2, true, false)]
    [InlineData(2, true, true)]
    public void PortableListViewUnhostingPreservesManagedItemState(int operation, bool createHandle, bool selected)
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using ListView owner = new();
        ListViewGroup group = new("Original group");
        owner.Groups.Add(group);
        ListViewItem item = new("Original item") { Selected = selected, StateImageIndex = 14 };
        item.SubItems.Add("Second column");
        owner.Items.Add(item);
        item.Group = group;
        if (createHandle)
        {
            owner.CreateControl();
            owner.IsHandleCreated.Should().BeTrue();
        }

        switch (operation)
        {
            case 0: item.Remove(); break;
            case 1: owner.Items.Clear(); break;
            case 2: owner.Dispose(); break;
        }

        item.ListView.Should().BeNull();
        item.Group.Should().BeNull();
        group.Items.Count.Should().Be(0);
        item.Selected.Should().Be(selected);
        item.StateImageIndex.Should().Be(14);
        item.Text.Should().Be("Original item");
        item.SubItems[1].Text.Should().Be("Second column");

        // The actual detached item can be reused, including its saved flags.
        using ListView replacement = new();
        replacement.Items.Add(item);
        item.ListView.Should().BeSameAs(replacement);
        item.Selected.Should().Be(selected);
        item.StateImageIndex.Should().Be(14);
        replacement.CreateControl();
        item.Remove();
        item.ListView.Should().BeNull();
        item.Selected.Should().Be(selected);
        item.StateImageIndex.Should().Be(14);
    }

    [Fact]
    public void PortableListViewLiveSavedFlagsAndGroupChangesSurviveRemoval()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using ListView owner = new();
        ListViewGroup first = new("First");
        ListViewGroup second = new("Second");
        owner.Groups.AddRange([first, second]);
        ListViewItem item = owner.Items.Add("Item");
        item.Group = first;
        owner.CreateControl();

        item.Selected = true;
        item.Selected.Should().BeTrue();
        owner.SelectedItems.Count.Should().Be(1);
        item.Selected = false;
        item.Selected.Should().BeFalse();
        owner.SelectedItems.Count.Should().Be(0);
        item.Selected = true;
        item.StateImageIndex = 14;
        item.StateImageIndex.Should().Be(14);
        Assert.Throws<ArgumentOutOfRangeException>(() => item.StateImageIndex = 15);
        item.StateImageIndex.Should().Be(14);
        item.Checked.Should().BeTrue();
        item.Checked = false;
        item.StateImageIndex.Should().Be(0);
        item.Checked = true;
        item.StateImageIndex.Should().Be(1);
        item.Group = second;
        first.Items.Count.Should().Be(0);
        second.Items[0].Should().BeSameAs(item);

        item.Remove();
        item.ListView.Should().BeNull();
        item.Group.Should().BeNull();
        second.Items.Count.Should().Be(0);
        item.Selected.Should().BeTrue();
        item.Checked.Should().BeTrue();
        item.StateImageIndex.Should().Be(1);
    }

}
