// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FluentAssertions;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 3)]
    public void PortableWindowUICues_QueryDoesNotInitializeManagedProperties(bool focus, int expected)
    {
        var platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.MenuAccessKeysUnderlinedValue = false;
        using Form form = new();
        using WindowCueProbe probe = new();
        using WindowCueProbe sibling = new();
        form.Controls.AddRange([probe, sibling]);
        form.Show();

        probe.QueryState().Should().Be(0);
        sibling.QueryState().Should().Be(0);
        (focus ? probe.FocusCues : probe.KeyboardCues).Should().BeFalse();
        probe.QueryState().Should().Be(expected);
        sibling.QueryState().Should().Be(expected);
        form.Close();
    }

    [Fact]
    public void PortableWindowUICues_ChangeUsesHierarchyAndUpdateUsesSubtree()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new();
        using WindowCueProbe parent = new();
        using WindowCueProbe child = new();
        using WindowCueProbe sibling = new();
        parent.Controls.Add(child);
        form.Controls.AddRange([parent, sibling]);
        form.Show();

        child.ChangeState(1, 3);
        parent.QueryState().Should().Be(3);
        sibling.QueryState().Should().Be(3);
        child.ChangeState(2, 2);
        parent.QueryState().Should().Be(1);
        sibling.QueryState().Should().Be(1);
        parent.UpdateState(2, 1);
        parent.QueryState().Should().Be(0);
        child.QueryState().Should().Be(0);
        sibling.QueryState().Should().Be(1);
        form.Close();
    }

    [Fact]
    public void PortableWindowUICues_NewHandleInheritsStateWithoutCreatingHiddenSiblings()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new();
        using WindowCueProbe parent = new();
        using WindowCueProbe hidden = new() { Visible = false };
        parent.Controls.Add(hidden);
        form.Controls.Add(parent);
        form.Show();
        parent.ChangeState(1, 3);
        hidden.IsHandleCreated.Should().BeFalse();
        hidden.QueryState().Should().Be(3);
        form.Close();
    }

    [Fact]
    public void PortableWindowUICues_LateManagedInitializationBroadcastsOriginalHidePolicy()
    {
        var platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.MenuAccessKeysUnderlinedValue = false;
        using Form form = new();
        using WindowCueProbe first = new();
        form.Controls.Add(first);
        form.Show();
        first.ChangeState(1, 3);
        first.ChangeState(2, 3);
        first.KeyboardCues.Should().BeTrue();
        first.FocusCues.Should().BeTrue();

        using WindowCueProbe added = new();
        form.Controls.Add(added);
        added.QueryState().Should().Be(0);
        added.FocusCues.Should().BeFalse();
        first.KeyboardCues.Should().BeFalse();
        first.FocusCues.Should().BeFalse();
        first.QueryState().Should().Be(3);
        form.Close();
    }

    [Fact]
    public void PortableWindowUICues_KeyboardClearsOnlyItsOwnFlagAndNoopDoesNotNotify()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new();
        using WindowCueProbe probe = new();
        form.Controls.Add(probe);
        form.Show();
        probe.ChangeState(1, 3);
        int notifications = 0;
        probe.ChangeUICues += (_, _) => notifications++;
        probe.ChangeState(1, 3);
        notifications.Should().Be(0);
        Message tab = Message.Create(probe.Handle, 0x100, (nint)Keys.Tab, 0);
        probe.PreProcessMessage(ref tab);
        probe.QueryState().Should().Be(2);
        Message menu = Message.Create(probe.Handle, 0x104, (nint)Keys.Menu, 0);
        probe.PreProcessMessage(ref menu);
        probe.QueryState().Should().Be(0);
        notifications.Should().Be(2);
        form.Close();
    }

    [Fact]
    public void PortableWindowUICues_PropagationDoesNotTargetReplacementHandles()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new();
        using WindowCueProbe first = new();
        using WindowCueProbe later = new();
        using WindowCueProbe added = new();
        form.Controls.AddRange([first, later]);
        form.Show();
        int laterNotifications = 0;
        int addedNotifications = 0;
        later.ChangeUICues += (_, _) => laterNotifications++;
        added.ChangeUICues += (_, _) => addedNotifications++;
        first.ChangeUICues += (_, _) =>
        {
            later.RecreateWindow();
            form.Controls.Add(added);
        };
        first.ChangeState(1, 3);
        later.QueryState().Should().Be(3);
        added.QueryState().Should().Be(3);
        laterNotifications.Should().Be(0);
        addedNotifications.Should().Be(0);
        form.Close();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableWindowUICues_NativeStylePaintReadsStateWithoutInitializingManagedCues(bool combo)
    {
        var platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.MenuAccessKeysUnderlinedValue = false;
        using Form form = new();
        using WindowCueProbe probe = new() { Visible = false };
        using Control control = combo
            ? new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }
            : new ListBox();
        control.Size = new Size(180, 48);
        if (control is ComboBox comboBox)
        {
            comboBox.Items.Add("Focus cue");
            comboBox.SelectedIndex = 0;
        }
        else if (control is ListBox listBox)
        {
            listBox.Items.Add("Focus cue");
            listBox.SelectedIndex = 0;
        }

        form.Controls.AddRange([control, probe]);
        form.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        control.Focus().Should().BeTrue();
        probe.QueryState().Should().Be(0);
        byte[] shown = probe.PaintPixels(control);
        probe.QueryState().Should().Be(0);
        probe.ChangeState(1, 1);
        byte[] hidden = probe.PaintPixels(control);
        probe.QueryState().Should().Be(1);
        hidden.SequenceEqual(shown).Should().BeFalse();
        probe.ChangeState(2, 1);
        probe.PaintPixels(control).Should().Equal(shown);
        form.Close();
    }

    private sealed class WindowCueProbe : Control
    {
        internal bool FocusCues => ShowFocusCues;
        internal bool KeyboardCues => ShowKeyboardCues;
        internal int QueryState() => (int)Send(0x129, 0);
        internal void ChangeState(int action, int flags) => Send(0x127, action | flags << 16);
        internal void UpdateState(int action, int flags) => Send(0x128, action | flags << 16);
        internal void RecreateWindow() => RecreateHandle();
        internal byte[] PaintPixels(Control control)
        {
            using Bitmap bitmap = new(control.ClientSize.Width, control.ClientSize.Height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                InvokePaint(control, new PaintEventArgs(graphics, control.ClientRectangle));
                graphics.Flush();
            }

            BitmapData data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] pixels = new byte[bitmap.Width * bitmap.Height * 4];
                for (int row = 0; row < bitmap.Height; row++)
                {
                    Marshal.Copy(data.Scan0 + row * data.Stride, pixels,
                        row * bitmap.Width * 4, bitmap.Width * 4);
                }

                return pixels;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private nint Send(int kind, int parameter)
        {
            Message message = Message.Create(Handle, kind, parameter, 0);
            WndProc(ref message);
            return message.Result;
        }

        protected override bool IsInputKey(Keys keyData) => true;
    }
}
