// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FluentAssertions;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Theory]
    [InlineData(HorizontalAlignment.Left, RightToLeft.No, LibreTextFormat.Default)]
    [InlineData(HorizontalAlignment.Center, RightToLeft.No, LibreTextFormat.HorizontalCenter)]
    [InlineData(HorizontalAlignment.Right, RightToLeft.No, LibreTextFormat.Right)]
    [InlineData(HorizontalAlignment.Left, RightToLeft.Yes, LibreTextFormat.Right)]
    [InlineData(HorizontalAlignment.Center, RightToLeft.Yes, LibreTextFormat.HorizontalCenter)]
    [InlineData(HorizontalAlignment.Right, RightToLeft.Yes, LibreTextFormat.Default)]
    public void PortableTextBoxPaintTransportsWholeSourceAndCanonicalAlignment(
        HorizontalAlignment alignment, RightToLeft direction, LibreTextFormat horizontal)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Font font = new(FontFamily.GenericSansSerif, 14);
        using PaintableTextBox editor = new()
        {
            AutoSize = false,
            BorderStyle = BorderStyle.None,
            Size = new Size(160, 40),
            Text = "A&🙂 אב",
            Font = font,
            ForeColor = Color.Navy,
            TextAlign = alignment,
            RightToLeft = direction
        };
        editor.Select(2, 2);
        using Bitmap target = new(180, 60);
        using Graphics graphics = Graphics.FromImage(target);

        editor.PaintContent(graphics, editor.ClientRectangle);

        TextBoxPaintCall call = platform.TextBoxDraws.Should().ContainSingle().Subject;
        call.Text.Should().Be(editor.Text);
        call.Font.Should().BeSameAs(font);
        call.Bounds.Should().Be(editor.ClientRectangle);
        call.ForeColor.Should().Be(Color.Navy);
        call.Format.Should().Be(LibreTextFormat.TextBoxControl | LibreTextFormat.NoPrefix
            | LibreTextFormat.NoPadding | LibreTextFormat.SingleLine | horizontal
            | (direction == RightToLeft.Yes ? LibreTextFormat.RightToLeft : 0));
        editor.Text.Should().Be("A&🙂 אב");
        editor.SelectionStart.Should().Be(2);
        editor.SelectionLength.Should().Be(2);
        editor.IsHandleCreated.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, true, LibreTextFormat.SingleLine)]
    [InlineData(true, true, LibreTextFormat.WordBreak)]
    [InlineData(true, false, LibreTextFormat.Default)]
    public void PortableTextBoxPaintUsesSourceLineAndWrapFlags(
        bool multiline, bool wrap, LibreTextFormat lineFlags)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using PaintableTextBox editor = new()
        {
            AutoSize = false,
            Size = new Size(120, 60),
            Multiline = multiline,
            WordWrap = wrap,
            Text = "first line\r\nsecond line"
        };
        using Bitmap target = new(120, 60);
        using Graphics graphics = Graphics.FromImage(target);

        editor.PaintContent(graphics, editor.ClientRectangle);

        TextBoxPaintCall call = platform.TextBoxDraws.Should().ContainSingle().Subject;
        call.Text.Should().Be(editor.Text);
        call.Format.Should().Be(LibreTextFormat.TextBoxControl | LibreTextFormat.NoPrefix
            | LibreTextFormat.NoPadding | lineFlags);
    }

    [Theory]
    [InlineData(false, "****")]
    [InlineData(true, "●●●●")]
    public void PortableTextBoxPaintNeverPassesPasswordSourceToRenderer(bool systemPassword, string displayed)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using PaintableTextBox editor = new()
        {
            Text = "A🙂B",
            PasswordChar = '*',
            UseSystemPasswordChar = systemPassword
        };
        editor.Select(1, 2);
        using Bitmap target = new(160, 60);
        using Graphics graphics = Graphics.FromImage(target);

        editor.PaintContent(graphics, editor.ClientRectangle);

        platform.TextBoxDraws.Should().ContainSingle().Which.Text.Should().Be(displayed);
        editor.Text.Should().Be("A🙂B");
        editor.SelectionStart.Should().Be(1);
        editor.SelectionLength.Should().Be(2);
        // Password handle creation belongs to the separately qualified native
        // message guard; this fixture exercises the real protected paint method.
        editor.IsHandleCreated.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    public void PortableTextBoxPaintPreservesEnabledReadOnlyAndPlaceholderContent(
        bool enabled, bool readOnly, bool placeholder)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using PaintableTextBox editor = new()
        {
            Text = placeholder ? string.Empty : "source",
            PlaceholderText = "hint",
            ForeColor = Color.DarkGreen,
            Enabled = enabled,
            ReadOnly = readOnly
        };
        using Bitmap target = new(160, 60);
        using Graphics graphics = Graphics.FromImage(target);

        editor.PaintContent(graphics, editor.ClientRectangle);

        TextBoxPaintCall call = platform.TextBoxDraws.Should().ContainSingle().Subject;
        call.Text.Should().Be(placeholder ? "hint" : "source");
        call.ForeColor.Should().Be(!enabled || placeholder ? SystemColors.GrayText : Color.DarkGreen);
        editor.Text.Should().Be(placeholder ? string.Empty : "source");
    }

    [Fact]
    public void PortableTextBoxPaintIntersectsClipAndRestoresCallerStateBeforePaintEvent()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using PaintableTextBox editor = new()
        {
            BorderStyle = BorderStyle.None,
            AutoSize = false,
            Size = new Size(80, 40),
            Text = "clipped"
        };
        using Bitmap target = new(120, 80);
        using Graphics graphics = Graphics.FromImage(target);
        graphics.TranslateTransform(7, 9);
        graphics.SetClip(new Rectangle(3, 4, 50, 30));
        RectangleF originalClip = graphics.ClipBounds;
        using Matrix originalTransform = graphics.Transform;
        bool notified = false;
        editor.Paint += (_, e) =>
        {
            notified = true;
            e.Graphics.ClipBounds.Should().Be(originalClip);
            using Matrix transform = e.Graphics.Transform;
            transform.Elements.Should().Equal(originalTransform.Elements);
        };

        editor.PaintContent(graphics, new Rectangle(10, 12, 100, 40));

        platform.TextBoxDraws.Should().ContainSingle().Which.Clip.Should().Be(new RectangleF(10, 12, 43, 22));
        notified.Should().BeTrue();
        graphics.ClipBounds.Should().Be(originalClip);
        using Matrix finalTransform = graphics.Transform;
        finalTransform.Elements.Should().Equal(originalTransform.Elements);
    }

    [Fact]
    public void PortableTextBoxWithZeroWidthDoesNotSubmitText()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using PaintableTextBox editor = new()
        {
            AutoSize = false,
            BorderStyle = BorderStyle.None,
            Size = new Size(0, 20),
            Text = "not drawable"
        };
        using Bitmap target = new(20, 20);
        using Graphics graphics = Graphics.FromImage(target);

        editor.PaintContent(graphics, new Rectangle(0, 0, 20, 20));

        platform.TextBoxDraws.Should().BeEmpty();
    }

    [Fact]
    public void OrdinaryDataGridViewEditorParticipatesInPortableControlTreePainting()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ClientSize = new Size(400, 240), ShowIcon = false };
        using DataGridView grid = new() { Bounds = new Rectangle(10, 10, 360, 200) };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name", Width = 180 });
        grid.Rows.Add("seed");
        form.Controls.Add(grid);
        form.Show();
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.BeginEdit(selectAll: false).Should().BeTrue();
        DataGridViewTextBoxEditingControl editor = grid.EditingControl
            .Should().BeOfType<DataGridViewTextBoxEditingControl>().Subject;
        editor.Text = "Alice";
        editor.IsHandleCreated.Should().BeTrue();
        platform.TextBoxDraws.Clear();
        using Bitmap target = new(editor.Width, editor.Height);

        editor.DrawToBitmap(target, new Rectangle(Point.Empty, editor.Size));

        TextBoxPaintCall call = platform.TextBoxDraws.Should().ContainSingle().Subject;
        call.Text.Should().Be("Alice");
        call.Font.Should().BeSameAs(editor.Font);
        call.Bounds.Should().Be(editor.ClientRectangle);
        editor.Text.Should().Be("Alice");
        grid.IsCurrentCellInEditMode.Should().BeTrue();
    }

    [Fact]
    public void PortableTextBoxTextAndEnabledChangesInvalidateRetainedContent()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ClientSize = new Size(200, 80), ShowIcon = false };
        using TextBox editor = new() { Text = "before", Size = new Size(160, 30) };
        form.Controls.Add(editor);
        form.Show();
        form.Update();
        platform.TextBoxDraws.Clear();

        editor.Text = "after";
        form.Update();
        platform.TextBoxDraws.Should().Contain(call => call.Text == "after");
        platform.TextBoxDraws.Clear();
        editor.Enabled = false;
        form.Update();
        platform.TextBoxDraws.Should().Contain(call => call.Text == "after" && call.ForeColor == SystemColors.GrayText);
    }

    [Fact]
    public void PortableTextBoxPlaceholderTracksControlAndNativeWindowFocus()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ClientSize = new Size(240, 120), ShowIcon = false };
        using TextBox editor = new() { PlaceholderText = "hint", Size = new Size(160, 30) };
        using Button other = new() { Bounds = new Rectangle(0, 50, 100, 30) };
        form.Controls.Add(editor);
        form.Controls.Add(other);
        form.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        other.Focus().Should().BeTrue();
        form.Update();
        platform.TextBoxDraws.Should().Contain(call => call.Text == "hint");
        platform.TextBoxDraws.Clear();

        editor.Focus().Should().BeTrue();
        form.Update();
        editor.Focused.Should().BeTrue();
        platform.LastRetainedLayerRepaintCount.Should().BeGreaterThan(0);
        platform.TextBoxDraws.Should().NotContain(call => call.Text == "hint");
        platform.TextBoxDraws.Clear();

        platform.SendInput(LibreInputEventKind.FocusLost);
        form.Update();
        editor.Focused.Should().BeFalse();
        platform.TextBoxDraws.Should().Contain(call => call.Text == "hint");
        platform.TextBoxDraws.Clear();

        platform.SendInput(LibreInputEventKind.FocusGained);
        form.Update();
        editor.Focused.Should().BeTrue();
        platform.LastRetainedLayerRepaintCount.Should().BeGreaterThan(0);
        platform.TextBoxDraws.Should().NotContain(call => call.Text == "hint");

        other.Focus().Should().BeTrue();
        form.Update();
        platform.TextBoxDraws.Should().Contain(call => call.Text == "hint");
        editor.Text.Should().BeEmpty();
    }

    [Fact]
    public void PortablePasswordChangesPreserveSourceImeRestrictionNotifications()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false };
        using TextBox editor = new() { Text = "secret", ImeMode = ImeMode.On };
        form.Controls.Add(editor);
        form.Show();
        editor.IsHandleCreated.Should().BeTrue();
        editor.Focused.Should().BeFalse();
        List<ImeMode> changes = [];
        editor.ImeModeChanged += (_, _) => changes.Add(editor.ImeMode);

        editor.PasswordChar = '*';
        form.Update();
        changes.Should().Equal(ImeMode.Disable);
        platform.TextBoxDraws.Should().Contain(call => call.Text == "******");
        editor.PasswordChar = '*';
        changes.Should().Equal(ImeMode.Disable);
        editor.PasswordChar = '\0';
        changes.Should().Equal(ImeMode.Disable, ImeMode.On);
        editor.Text.Should().Be("secret");
        editor.AutoCompleteMode.Should().Be(AutoCompleteMode.None);
    }

    private readonly record struct TextBoxPaintCall(
        string Text, Font Font, Rectangle Bounds, Color ForeColor, LibreTextFormat Format, RectangleF Clip);

    private sealed class PaintableTextBox : TextBox
    {
        internal void PaintContent(Graphics graphics, Rectangle clip)
        {
            using PaintEventArgs args = new(graphics, clip);
            OnPaint(args);
        }
    }
}
