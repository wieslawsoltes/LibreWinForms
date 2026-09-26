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
    public void HostedMenuTextBoxReceivesOwnerInputWithSourceFocusAndUtf16Selection(bool toolStripTextBox)
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using ContextMenuStrip menu = new();
            TextBox hosted = AddHostedMenuTextBox(menu, toolStripTextBox);
            int gotFocus = 0;
            int ownerLostFocus = 0;
            hosted.GotFocus += (_, _) => gotFocus++;
            editor.LostFocus += (_, _) => ownerLostFocus++;
            menu.Show(owner, new Point(10, 80));
            ClickHostedMenuTextBox(platform, menu, hosted);
            hosted.Focused.Should().BeTrue();
            menu.ContainsFocus.Should().BeTrue();
            editor.Focused.Should().BeFalse();
            gotFocus.Should().Be(1);
            ownerLostFocus.Should().Be(1);
            Form.ActiveForm.Should().BeSameAs(owner);
            platform.LastActivatedWindow.IsNull.Should().BeTrue();

            hosted.Text = "A😀B";
            hosted.Select(1, 2);
            SendDropdownText(platform, owner, "🚀");
            hosted.Text.Should().Be("A🚀B");
            hosted.SelectionStart.Should().Be(3);
            hosted.SelectionLength.Should().Be(0);
            SendDropdownKey(platform, owner, LibreKey.Backspace);
            hosted.Text.Should().Be("AB");
            hosted.SelectionStart.Should().Be(1);
            editor.Text.Should().BeEmpty();
        });
    }

    [Fact]
    public void HostedMenuEscapePreservesCanceledEditingThenRestoresOwnerFocus()
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using ContextMenuStrip menu = new();
            TextBox hosted = AddHostedMenuTextBox(menu);
            int lostFocus = 0;
            int restoredFocus = 0;
            hosted.LostFocus += (_, _) => lostFocus++;
            editor.GotFocus += (_, _) => restoredFocus++;
            bool cancel = true;
            menu.Closing += (_, e) => e.Cancel = cancel;
            menu.Show(owner, new Point(10, 80));
            ClickHostedMenuTextBox(platform, menu, hosted);
            SendDropdownKey(platform, owner, LibreKey.Escape);
            menu.Visible.Should().BeTrue();
            hosted.Focused.Should().BeTrue();
            SendDropdownText(platform, owner, "a");
            hosted.Text.Should().Be("a");
            lostFocus.Should().Be(0);

            cancel = false;
            SendDropdownKey(platform, owner, LibreKey.Escape);
            menu.Visible.Should().BeFalse();
            hosted.Focused.Should().BeFalse();
            editor.Focused.Should().BeTrue();
            lostFocus.Should().Be(1);
            restoredFocus.Should().Be(1);
            SendDropdownText(platform, owner, "b");
            editor.Text.Should().Be("b");
            hosted.Text.Should().Be("a");
        });
    }

    [Fact]
    public void HostedMenuFiltersAndKeyPressSuppressionRetainCanonicalOrdering()
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using ContextMenuStrip menu = new();
            TextBox hosted = AddHostedMenuTextBox(menu);
            menu.Show(owner, new Point(10, 80));
            ClickHostedMenuTextBox(platform, menu, hosted);
            List<string> order = [];
            CallbackKeyboardFilter filter = new(message =>
            {
                if (message.Msg == 0x100)
                {
                    message.HWnd.Should().Be(hosted.Handle);
                    order.Add("filter");
                }

                return false;
            });
            hosted.KeyDown += (_, e) => { order.Add("key"); e.SuppressKeyPress = true; };
            hosted.KeyPress += (_, _) => order.Add("char");
            Application.AddMessageFilter(filter);
            try
            {
                SendMenuKey(platform, owner, LibreKey.A, down: true);
                SendDropdownText(platform, owner, "a");
                SendDropdownKeyUp(platform, owner, LibreKey.A);
            }
            finally
            {
                Application.RemoveMessageFilter(filter);
            }

            order.Should().Equal("filter", "key");
            hosted.Text.Should().BeEmpty();
            editor.Text.Should().BeEmpty();
            SendDropdownText(platform, owner, "b");
            hosted.Text.Should().Be("b");
        });
    }

    [Fact]
    public void HostedMenuOutsidePointerRestoresTheClickedOwnerEditor()
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using ContextMenuStrip menu = new();
            TextBox hosted = AddHostedMenuTextBox(menu);
            menu.Show(owner, new Point(10, 100));
            ClickHostedMenuTextBox(platform, menu, hosted);
            hosted.Focused.Should().BeTrue();
            Point point = owner.PointToClient(editor.PointToScreen(new Point(2, 2)));
            menu.ClientRectangle.Contains(menu.PointToClient(owner.PointToScreen(point))).Should().BeFalse();
            SendOutsideMenuPointer(platform, owner, point);
            menu.Visible.Should().BeFalse();
            editor.Focused.Should().BeTrue();
            hosted.Focused.Should().BeFalse();
            SendDropdownText(platform, owner, "owner");
            editor.Text.Should().Be("owner");
            hosted.Text.Should().BeEmpty();
        });
    }

    private static TextBox AddHostedMenuTextBox(ContextMenuStrip menu, bool toolStripTextBox = false)
    {
        ToolStripControlHost host = toolStripTextBox
            ? new ToolStripTextBox()
            : new ToolStripControlHost(new TextBox());
        host.AutoSize = false;
        host.Size = new Size(140, 30);
        menu.Items.Add(host);
        return (TextBox)host.Control;
    }

    private static void ClickHostedMenuTextBox(HeadlessPlatform platform, ContextMenuStrip menu, TextBox hosted)
    {
        Point point = menu.PointToClient(hosted.PointToScreen(new Point(hosted.Width / 2, hosted.Height / 2)));
        menu.ClientRectangle.Contains(point).Should().BeTrue();
        SendOutsideMenuPointer(platform, menu, point);
    }
}
