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
    [InlineData(LibreKey.F10, false)]
    [InlineData(LibreKey.LeftAlt, false)]
    [InlineData(LibreKey.RightAlt, false)]
    [InlineData(LibreKey.F10, true)]
    [InlineData(LibreKey.LeftAlt, true)]
    [InlineData(LibreKey.RightAlt, true)]
    public void BareMenuKeyActivatesOnReleaseAndReusesCanonicalSelectionWithoutMovingFocus(LibreKey key, bool rtl)
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using MenuStrip bar = AddMenuKeyBar(owner, rtl);
            ToolStripItem first = bar.Items[1];
            ToolStripItem second = bar.Items[2];
            int activated = 0;
            int deactivated = 0;
            bar.MenuActivate += (_, _) => activated++;
            bar.MenuDeactivate += (_, _) => deactivated++;
            SendMenuKey(platform, owner, key, down: true);
            SendMenuKey(platform, owner, key, down: true); // native repeat
            activated.Should().Be(0);
            first.Selected.Should().BeFalse();
            second.Selected.Should().BeFalse();
            SendMenuKey(platform, owner, key, down: false);
            activated.Should().Be(1);
            // OnMenuKey and rtlAware navigation preserve the first logical item;
            // the source layout mirrors its displayed position for RTL.
            first.Selected.Should().BeTrue();
            second.Selected.Should().BeFalse();
            if (rtl)
                first.Bounds.Left.Should().BeGreaterThan(second.Bounds.Left);
            else
                first.Bounds.Left.Should().BeLessThan(second.Bounds.Left);
            bar.Items[0].Selected.Should().BeFalse("disabled items retain canonical selection rules");
            editor.Focused.Should().BeTrue();
            platform.LastActivatedWindow.IsNull.Should().BeTrue();

            SendMenuKey(platform, owner, key, down: true);
            SendMenuKey(platform, owner, key, down: false);
            activated.Should().Be(1);
            deactivated.Should().Be(1);
            first.Selected.Should().BeFalse();
            second.Selected.Should().BeFalse();
            SendDropdownText(platform, owner, "x");
            editor.Text.Should().Be("x");
        });
    }

    [Theory]
    [InlineData("filter-down")]
    [InlineData("filter-up")]
    [InlineData("handled-down")]
    [InlineData("handled-up")]
    [InlineData("suppress-down")]
    [InlineData("shortcut")]
    public void MenuKeyRespectsCanonicalConsumptionBeforeDefaultActivation(string consumption)
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using MenuStrip bar = AddMenuKeyBar(owner);
            int activated = 0;
            int shortcut = 0;
            bar.MenuActivate += (_, _) => activated++;
            owner.KeyPreview = true;
            owner.KeyDown += (_, e) =>
            {
                if (consumption == "handled-down") e.Handled = true;
                if (consumption == "suppress-down") e.SuppressKeyPress = true;
            };
            owner.KeyUp += (_, e) => { if (consumption == "handled-up") e.Handled = true; };
            if (consumption == "shortcut")
            {
                ToolStripMenuItem command = (ToolStripMenuItem)bar.Items[1];
                command.ShortcutKeys = Keys.F10;
                command.Click += (_, _) => shortcut++;
            }

            CallbackKeyboardFilter filter = new(message =>
                message.WParam == (nint)Keys.F10 && ((consumption == "filter-down" && message.Msg == 0x100)
                    || (consumption == "filter-up" && message.Msg == 0x101)));
            Application.AddMessageFilter(filter);
            try
            {
                SendMenuKey(platform, owner, LibreKey.F10, down: true);
                SendMenuKey(platform, owner, LibreKey.F10, down: false);
            }
            finally
            {
                Application.RemoveMessageFilter(filter);
            }

            activated.Should().Be(0);
            shortcut.Should().Be(consumption == "shortcut" ? 1 : 0);
            SendDropdownText(platform, owner, "x");
            editor.Text.Should().Be("x");
        });
    }

    [Fact]
    public void MenuKeyInputClassificationAloneIsNotConsumption()
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using MenuStrip bar = AddMenuKeyBar(owner);
            int keyDowns = 0;
            int activations = 0;
            editor.PreviewKeyDown += (_, e) => e.IsInputKey = true;
            editor.KeyDown += (_, _) => keyDowns++;
            bar.MenuActivate += (_, _) => activations++;
            SendDropdownKey(platform, owner, LibreKey.F10);
            keyDowns.Should().Be(1);
            activations.Should().Be(1);
        });
    }

    [Theory]
    [InlineData("other-key")]
    [InlineData("text")]
    [InlineData("pointer")]
    [InlineData("focus")]
    [InlineData("shift-f10")]
    [InlineData("meta-f10")]
    [InlineData("altgr")]
    public void InterruptedOrModifiedMenuKeyDoesNotActivateOnRelease(string interruption)
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using MenuStrip bar = AddMenuKeyBar(owner);
            int activated = 0;
            bar.MenuActivate += (_, _) => activated++;
            LibreKey key = interruption is "shift-f10" or "meta-f10" ? LibreKey.F10 : LibreKey.RightAlt;
            LibreInputModifiers modifiers = interruption == "shift-f10" ? LibreInputModifiers.Shift
                : interruption == "meta-f10" ? LibreInputModifiers.Meta
                : interruption == "altgr" ? LibreInputModifiers.Alt | LibreInputModifiers.Control : LibreInputModifiers.Alt;
            SendMenuKey(platform, owner, key, down: true, modifiers);
            if (interruption == "other-key")
            {
                CallbackKeyboardFilter filter = new(_ => true);
                Application.AddMessageFilter(filter);
                try { SendDropdownKey(platform, owner, LibreKey.Space); }
                finally { Application.RemoveMessageFilter(filter); }
            }
            else if (interruption == "text") SendDropdownText(platform, owner, "a");
            else if (interruption == "pointer")
                platform.SendControlInput(owner, new LibreInputEvent(LibreInputEventKind.PointerDown,
                    1, LibreInputModifiers.Alt, LibreKey.Unknown, null, new LibrePoint(250, 150), default, LibrePointerButton.Primary));
            else if (interruption == "focus")
            {
                platform.SendFormInput(owner, LibreInputEventKind.FocusLost);
                platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
            }

            SendMenuKey(platform, owner, key, down: false);
            activated.Should().Be(0);
            bar.Items[1].Selected.Should().BeFalse();
        });
    }

    [Theory]
    [InlineData("hide-menu")]
    [InlineData("hide-owner")]
    [InlineData("dispose-menu")]
    [InlineData("replace-menu")]
    public void MenuKeyActivationCallbackCannotSelectOrRetainAnInvalidatedMenu(string change)
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using MenuStrip bar = AddMenuKeyBar(owner);
            using MenuStrip replacement = new();
            replacement.Items.Add("Replacement");
            owner.Controls.Add(replacement);
            bar.MenuActivate += (_, _) =>
            {
                if (change == "hide-menu") bar.Hide();
                else if (change == "hide-owner") owner.Hide();
                else if (change == "dispose-menu") bar.Dispose();
                else owner.MainMenuStrip = replacement;
            };
            ToolStripItem first = bar.Items[1];
            SendDropdownKey(platform, owner, LibreKey.F10);
            first.Selected.Should().BeFalse();
            owner.Show();
            platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
            editor.Focus().Should().BeTrue();
            SendDropdownText(platform, owner, "x");
            editor.Text.Should().Be("x");
            replacement.Items[0].Selected.Should().BeFalse();
        });
    }

    [Fact]
    public void MenuKeyUsesRecursiveMenuDiscoveryWithoutAnExplicitMainMenuStrip()
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using Panel container = new() { Bounds = new Rectangle(0, 80, 200, 80) };
            owner.Controls.Add(container);
            using MenuStrip bar = new();
            ToolStripItem command = bar.Items.Add("Command");
            container.Controls.Add(bar);
            owner.MainMenuStrip.Should().BeNull();
            SendDropdownKey(platform, owner, LibreKey.F10);
            command.Selected.Should().BeTrue();
            editor.Focused.Should().BeTrue();
        });
    }

    private static MenuStrip AddMenuKeyBar(Form owner, bool rtl = false)
    {
        MenuStrip bar = new() { RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No };
        bar.Items.Add(new ToolStripMenuItem("Disabled") { Enabled = false });
        bar.Items.Add(new ToolStripMenuItem("First"));
        bar.Items.Add(new ToolStripMenuItem("Second"));
        owner.Controls.Add(bar);
        owner.MainMenuStrip = bar;
        return bar;
    }

    private static void SendMenuKey(HeadlessPlatform platform, Form owner, LibreKey key, bool down,
        LibreInputModifiers? modifiers = null)
        => platform.SendControlInput(owner, new LibreInputEvent(down ? LibreInputEventKind.KeyDown : LibreInputEventKind.KeyUp,
            1, modifiers ?? (down && key is LibreKey.LeftAlt or LibreKey.RightAlt ? LibreInputModifiers.Alt : LibreInputModifiers.None),
            key, null, default, default, LibrePointerButton.None));
}
