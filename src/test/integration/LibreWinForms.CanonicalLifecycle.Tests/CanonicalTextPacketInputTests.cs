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
    public void RetiringHostedTextInputCancelsItsPacketAndPreservesActualKeyCycle(bool keyHeld)
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using ContextMenuStrip menu = new();
            TextBox hosted = AddHostedMenuTextBox(menu);
            menu.Show(owner, new Point(10, 80));
            ClickHostedMenuTextBox(platform, menu, hosted);
            hosted.TextChanged += (_, _) => menu.Close();

            if (keyHeld) SendDropdownKey(platform, owner, LibreKey.A, release: false);
            SendDropdownText(platform, owner, "aobsolete");

            hosted.Text.Should().Be("a");
            menu.Visible.Should().BeFalse();
            editor.Focused.Should().BeTrue();
            editor.Text.Should().BeEmpty();
            if (keyHeld)
            {
                SendDropdownText(platform, owner, "same-key");
                editor.Text.Should().BeEmpty();
                SendDropdownKeyUp(platform, owner, LibreKey.A);
            }

            SendDropdownText(platform, owner, "next");
            editor.Text.Should().Be("next");
            hosted.Text.Should().Be("a");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedInputAfterHostedRetirementOwnsItsSuppressionGeneration(bool nestedKeyHeld)
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using ContextMenuStrip menu = new();
            TextBox hosted = AddHostedMenuTextBox(menu);
            menu.Show(owner, new Point(10, 80));
            ClickHostedMenuTextBox(platform, menu, hosted);
            hosted.TextChanged += (_, _) =>
            {
                menu.Close();
                if (nestedKeyHeld) SendDropdownKey(platform, owner, LibreKey.B, release: false);
                SendDropdownText(platform, owner, "nested");
            };

            SendDropdownText(platform, owner, "aobsolete");

            hosted.Text.Should().Be("a");
            editor.Focused.Should().BeTrue();
            editor.Text.Should().Be("nested");
            SendDropdownText(platform, owner, "next");
            editor.Text.Should().Be("nestednext", "the retired outer packet does not own a nested input generation");
            if (nestedKeyHeld) SendDropdownKeyUp(platform, owner, LibreKey.B);
        });
    }
}
