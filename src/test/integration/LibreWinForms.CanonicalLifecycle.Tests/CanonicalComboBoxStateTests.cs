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
    [InlineData(ComboBoxStyle.Simple)]
    [InlineData(ComboBoxStyle.DropDown)]
    [InlineData(ComboBoxStyle.DropDownList)]
    public void PortableComboBoxObservationDoesNotCreateHandleOrUseNativeChildFocus(ComboBoxStyle style)
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using ComboBox combo = new() { DropDownStyle = style };
        combo.DroppedDown.Should().BeFalse();
        combo.Focused.Should().BeFalse();
        combo.IsHandleCreated.Should().BeFalse();
    }

    [Theory]
    [InlineData(ComboBoxStyle.Simple)]
    [InlineData(ComboBoxStyle.DropDown)]
    [InlineData(ComboBoxStyle.DropDownList)]
    public void PortableComboBoxCreatedClosedStateDoesNotSendUser32Messages(ComboBoxStyle style)
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using ComboBox combo = new() { DropDownStyle = style };
        combo.CreateControl();
        IntPtr handle = combo.Handle;
        int opened = 0;
        int closed = 0;
        combo.DropDown += (_, _) => opened++;
        combo.DropDownClosed += (_, _) => closed++;
        combo.DroppedDown.Should().BeFalse();
        combo.DroppedDown.Should().BeFalse();
        combo.Focused.Should().BeFalse();
        combo.Handle.Should().Be(handle);
        opened.Should().Be(0);
        closed.Should().Be(0);
    }

    [Theory]
    [InlineData(ComboBoxStyle.Simple)]
    [InlineData(ComboBoxStyle.DropDown)]
    [InlineData(ComboBoxStyle.DropDownList)]
    public void PortableComboBoxFocusUsesTheCanonicalOwnerFocus(ComboBoxStyle style)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using ComboBox combo = new() { DropDownStyle = style, Location = new Point(10, 10) };
        using TextBox other = new() { Location = new Point(10, 100) };
        owner.Controls.Add(combo);
        owner.Controls.Add(other);
        owner.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        combo.Focus().Should().BeTrue();
        combo.Focused.Should().BeTrue();
        other.Focused.Should().BeFalse();
        owner.ActiveControl.Should().BeSameAs(combo);
        other.Focus().Should().BeTrue();
        combo.Focused.Should().BeFalse();
        other.Focused.Should().BeTrue();
        owner.ActiveControl.Should().BeSameAs(other);
    }
}
