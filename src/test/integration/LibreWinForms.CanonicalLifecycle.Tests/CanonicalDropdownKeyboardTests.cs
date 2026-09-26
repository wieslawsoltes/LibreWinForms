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
    [Fact]
    public void DropdownOwnerKeysNavigateCanonicalItemsAndEnterClicksWithoutFocusTransfer()
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using ContextMenuStrip menu = new();
            ToolStripItem disabled = menu.Items.Add("Disabled");
            disabled.Enabled = false;
            menu.Items.Add(new ToolStripSeparator());
            ToolStripItem hidden = menu.Items.Add("Hidden");
            hidden.Visible = false;
            ToolStripItem first = menu.Items.Add("First");
            ToolStripItem second = menu.Items.Add("Second");
            int clicks = 0;
            first.Click += (_, _) => clicks++;
            menu.Show(editor, Point.Empty);

            SendDropdownKey(platform, owner, LibreKey.Down);
            first.Selected.Should().BeTrue();
            disabled.Selected.Should().BeFalse();
            hidden.Selected.Should().BeFalse();
            SendDropdownKey(platform, owner, LibreKey.Down);
            second.Selected.Should().BeTrue();
            SendDropdownKey(platform, owner, LibreKey.Up);
            first.Selected.Should().BeTrue();
            SendDropdownKey(platform, owner, LibreKey.Enter, release: false);
            clicks.Should().Be(1);
            menu.Visible.Should().BeFalse();
            SendDropdownText(platform, owner, "\r");
            editor.Text.Should().BeEmpty("the consumed Enter character must not reach the owner editor");
            SendDropdownKeyUp(platform, owner, LibreKey.Enter);
            SendDropdownText(platform, owner, "x");
            editor.Text.Should().Be("x");
            editor.Focused.Should().BeTrue();
            Form.ActiveForm.Should().BeSameAs(owner);
            menu.ContainsFocus.Should().BeFalse();
            platform.LastActivatedWindow.IsNull.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DropdownOwnerHorizontalKeysUseCanonicalCascadeDirectionAndEscape(bool rtl)
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using ContextMenuStrip menu = new() { RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No };
            ToolStripMenuItem parent = new("Parent");
            ToolStripItem child = parent.DropDownItems.Add("Child");
            menu.Items.Add(parent);
            menu.Show(editor, Point.Empty);
            SendDropdownKey(platform, owner, LibreKey.Down);
            parent.Selected.Should().BeTrue();
            SendDropdownKey(platform, owner, rtl ? LibreKey.Left : LibreKey.Right);
            parent.DropDown.Visible.Should().BeTrue();
            child.Selected.Should().BeTrue();
            SendDropdownKey(platform, owner, rtl ? LibreKey.Right : LibreKey.Left);
            parent.DropDown.Visible.Should().BeFalse();
            menu.Visible.Should().BeTrue();
            parent.Selected.Should().BeTrue();
            SendDropdownKey(platform, owner, LibreKey.Escape);
            menu.Visible.Should().BeFalse();
            editor.Focused.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData("&Open", "o")]
    [InlineData("Save", "s")]
    public void DropdownOwnerTextUsesCanonicalExplicitAndImplicitMnemonics(string caption, string text)
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using ContextMenuStrip menu = new();
            int clicks = 0;
            menu.Items.Add(caption).Click += (_, _) => clicks++;
            menu.Show(editor, Point.Empty);
            SendDropdownText(platform, owner, text);
            clicks.Should().Be(1);
            menu.Visible.Should().BeFalse();
            editor.Text.Should().BeEmpty();
            editor.Focused.Should().BeTrue();
        });
    }

    [Fact]
    public void DropdownDuplicateMnemonicsCycleCanonicalSelectionWithoutActivatingTheWindow()
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using ContextMenuStrip menu = new();
            ToolStripItem first = menu.Items.Add("&Alpha");
            ToolStripItem second = menu.Items.Add("&Again");
            int clicks = 0;
            first.Click += (_, _) => clicks++;
            second.Click += (_, _) => clicks++;
            menu.Show(editor, Point.Empty);
            SendDropdownText(platform, owner, "a");
            first.Selected.Should().BeTrue();
            SendDropdownText(platform, owner, "a");
            second.Selected.Should().BeTrue();
            clicks.Should().Be(0);
            SendDropdownKey(platform, owner, LibreKey.Enter);
            clicks.Should().Be(1);
            editor.Focused.Should().BeTrue();
            platform.LastActivatedWindow.IsNull.Should().BeTrue();
        });
    }

    [Fact]
    public void DropdownEscapeCancellationKeepsRoutingUntilTheActualClose()
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using ContextMenuStrip menu = new();
            ToolStripItem first = menu.Items.Add("First");
            bool cancel = true;
            List<ToolStripDropDownCloseReason> reasons = [];
            menu.Closing += (_, e) => { reasons.Add(e.CloseReason); e.Cancel = cancel; };
            menu.Show(editor, Point.Empty);
            SendDropdownKey(platform, owner, LibreKey.Escape);
            menu.Visible.Should().BeTrue();
            SendDropdownKey(platform, owner, LibreKey.Down);
            first.Selected.Should().BeTrue();
            cancel = false;
            SendDropdownKey(platform, owner, LibreKey.Escape);
            menu.Visible.Should().BeFalse();
            reasons.Should().Equal(ToolStripDropDownCloseReason.Keyboard, ToolStripDropDownCloseReason.Keyboard);
            SendDropdownText(platform, owner, "owner");
            editor.Text.Should().Be("owner");
        });
    }

    [Fact]
    public void DropdownKeyFiltersRunOnceBeforeResolvingAReplacementMenu()
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using ContextMenuStrip first = new();
            first.Items.Add("First");
            using ContextMenuStrip replacement = new();
            ToolStripItem selected = replacement.Items.Add("Replacement");
            first.Show(editor, Point.Empty);
            int filtered = 0;
            bool consume = true;
            CallbackKeyboardFilter filter = new(message =>
            {
                if (message.Msg != 0x0100)
                    return false;
                message.HWnd.Should().Be(editor.Handle);
                filtered++;
                if (!consume)
                {
                    first.Close();
                    replacement.Show(editor, Point.Empty);
                }

                return consume;
            });
            Application.AddMessageFilter(filter);
            try
            {
                SendDropdownKey(platform, owner, LibreKey.Down);
                first.Items[0].Selected.Should().BeFalse();
                consume = false;
                SendDropdownKey(platform, owner, LibreKey.Down);
                selected.Selected.Should().BeTrue();
                filtered.Should().Be(2);
            }
            finally
            {
                Application.RemoveMessageFilter(filter);
            }

            editor.Text.Should().BeEmpty();
        });
    }

    [Fact]
    public void PersistentDropdownDoesNotStealOwnerKeysButItsAutoCloseChildDoes()
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using ContextMenuStrip menu = new() { AutoClose = false };
            ToolStripMenuItem parent = new("Parent");
            ToolStripItem child = parent.DropDownItems.Add("Child");
            menu.Items.Add(parent);
            menu.Show(editor, Point.Empty);
            SendDropdownText(platform, owner, "x");
            editor.Text.Should().Be("x");
            parent.ShowDropDown();
            SendDropdownKey(platform, owner, LibreKey.Down);
            child.Selected.Should().BeTrue();
            SendDropdownKey(platform, owner, LibreKey.Escape);
            parent.DropDown.Visible.Should().BeFalse();
            menu.Visible.Should().BeTrue();
            SendDropdownText(platform, owner, "y");
            editor.Text.Should().Be("xy");
        });
    }

    [Fact]
    public void DropdownPreviewCallbackCanCloseTheMenuWithoutDispatchingIntoTheOwner()
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using ContextMenuStrip menu = new();
            menu.Items.Add("Open");
            int keyDowns = 0;
            menu.KeyDown += (_, _) => keyDowns++;
            menu.PreviewKeyDown += (_, e) => { e.IsInputKey = true; menu.Close(); };
            menu.Show(editor, Point.Empty);
            SendDropdownKey(platform, owner, LibreKey.A, release: false);
            keyDowns.Should().Be(0);
            SendDropdownText(platform, owner, "a");
            editor.Text.Should().BeEmpty();
            SendDropdownKeyUp(platform, owner, LibreKey.A);
            SendDropdownText(platform, owner, "b");
            editor.Text.Should().Be("b");
        });
    }

    [Fact]
    public void OwnerKeysWithoutAMenuRetainCanonicalPreviewAndEditorEvents()
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            owner.KeyPreview = true;
            List<string> order = [];
            owner.KeyDown += (_, _) => order.Add("owner");
            editor.KeyDown += (_, _) => order.Add("editor");
            SendDropdownKey(platform, owner, LibreKey.A);
            SendDropdownText(platform, owner, "a");
            order.Should().Equal("owner", "editor");
            editor.Text.Should().Be("a");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DropdownOwnerKeysContinueAcrossTheActualMenuStripWithoutNativeMenuMode(bool backwards)
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using MenuStrip bar = new();
            ToolStripMenuItem first = new("First");
            ToolStripMenuItem second = new("Second");
            first.DropDownItems.Add("First command");
            second.DropDownItems.Add("Second command");
            bar.Items.Add(first);
            bar.Items.Add(second);
            owner.Controls.Add(bar);
            owner.MainMenuStrip = bar;
            ToolStripMenuItem initial = backwards ? second : first;
            ToolStripMenuItem next = backwards ? first : second;
            initial.ShowDropDown();
            SendDropdownKey(platform, owner, LibreKey.Down);
            initial.DropDownItems[0].Selected.Should().BeTrue();
            SendDropdownKey(platform, owner, backwards ? LibreKey.Left : LibreKey.Right);
            initial.DropDown.Visible.Should().BeFalse();
            next.DropDown.Visible.Should().BeTrue();
            next.DropDownItems[0].Selected.Should().BeTrue();
            SendDropdownKey(platform, owner, LibreKey.Escape);
            next.DropDown.Visible.Should().BeFalse();
            next.Selected.Should().BeTrue();
            SendDropdownKey(platform, owner, LibreKey.Down);
            next.DropDown.Visible.Should().BeTrue();
            SendDropdownKey(platform, owner, LibreKey.Escape);
            SendDropdownKey(platform, owner, LibreKey.Escape);
            SendDropdownText(platform, owner, "x");
            editor.Text.Should().Be("x");
            editor.Focused.Should().BeTrue();
            Form.ActiveForm.Should().BeSameAs(owner);
        });
    }

    private static void SendDropdownKey(HeadlessPlatform platform, Form owner, LibreKey key, bool release = true)
    {
        platform.SendControlInput(owner, new LibreInputEvent(LibreInputEventKind.KeyDown,
            1, LibreInputModifiers.None, key, null, default, default, LibrePointerButton.None));
        if (release)
            SendDropdownKeyUp(platform, owner, key);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MenuStripActivationCallbackCannotRetainKeyboardOwnershipAfterHide(bool hideOwner)
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using MenuStrip bar = new();
            ToolStripMenuItem item = new("Menu");
            item.DropDownItems.Add("Command");
            bar.Items.Add(item);
            owner.Controls.Add(bar);
            owner.MainMenuStrip = bar;
            EventHandler hide = (_, _) =>
            {
                if (hideOwner)
                    owner.Hide();
                else
                    bar.Hide();
            };
            bar.MenuActivate += hide;
            item.ShowDropDown();
            SendDropdownKey(platform, owner, LibreKey.Escape);
            bar.MenuActivate -= hide;
            owner.Show();
            bar.Show();
            platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
            editor.Focus().Should().BeTrue();
            SendDropdownText(platform, owner, "x");
            editor.Text.Should().Be("x", "showing again must not resurrect a retired keyboard continuation");
        });
    }

    [Fact]
    public void ThrowingMenuDeactivateRetiresKeyboardOwnershipBeforeThePublicCallback()
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using MenuStrip bar = new();
            ToolStripMenuItem item = new("Menu");
            item.DropDownItems.Add("Command");
            bar.Items.Add(item);
            owner.Controls.Add(bar);
            owner.MainMenuStrip = bar;
            item.ShowDropDown();
            SendDropdownKey(platform, owner, LibreKey.Escape);
            var failure = new InvalidOperationException("menu deactivation callback failed");
            EventHandler throwing = (_, _) => throw failure;
            bar.MenuDeactivate += throwing;
            try
            {
                Action escape = () => SendDropdownKey(platform, owner, LibreKey.Escape);
                escape.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
            }
            finally
            {
                bar.MenuDeactivate -= throwing;
            }

            SendDropdownKeyUp(platform, owner, LibreKey.Escape);
            SendDropdownText(platform, owner, "x");
            editor.Text.Should().Be("x");
        });
    }

    private static void SendDropdownKeyUp(HeadlessPlatform platform, Form owner, LibreKey key)
        => platform.SendControlInput(owner, new LibreInputEvent(LibreInputEventKind.KeyUp,
            1, LibreInputModifiers.None, key, null, default, default, LibrePointerButton.None));

    private static void SendDropdownText(HeadlessPlatform platform, Form owner, string text)
        => platform.SendControlInput(owner, new LibreInputEvent(LibreInputEventKind.TextInput,
            1, LibreInputModifiers.None, LibreKey.Unknown, text, default, default, LibrePointerButton.None));

    private static void RunDropdownKeyboard(Action<HeadlessPlatform, Form, TextBox> action)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using TextBox editor = new() { Multiline = true, Bounds = new Rectangle(8, 8, 180, 60) };
        owner.Controls.Add(editor);
        owner.Shown += (_, _) => platform.Post(() =>
        {
            try
            {
                platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
                editor.Focus().Should().BeTrue();
                action(platform, owner, editor);
            }
            finally
            {
                owner.Close();
            }
        });
        Application.Run(owner);
    }

    private sealed class CallbackKeyboardFilter(Func<Message, bool> callback) : IMessageFilter
    {
        public bool PreFilterMessage(ref Message message) => callback(message);
    }
}
