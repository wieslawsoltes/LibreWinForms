// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
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
    [InlineData(MaskFormat.ExcludePromptAndLiterals, "12")]
    [InlineData(MaskFormat.IncludePrompt, "12__")]
    [InlineData(MaskFormat.IncludeLiterals, "12-")]
    [InlineData(MaskFormat.IncludePromptAndLiterals, "12-__")]
    public void PortableMaskedPaintUsesDisplayRatherThanPublicOutput(MaskFormat format, string output)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Font font = new(FontFamily.GenericSansSerif, 14);
        using PaintableMaskedTextBox editor = new()
        {
            BorderStyle = BorderStyle.None,
            Mask = "00-00",
            Text = "12",
            TextMaskFormat = format,
            ForeColor = Color.Navy,
            Font = font
        };
        editor.Select(1, 1);
        using Bitmap target = new(160, 40);
        using Graphics graphics = Graphics.FromImage(target);

        editor.PaintContent(graphics, editor.ClientRectangle);

        TextBoxPaintCall call = platform.TextBoxDraws.Should().ContainSingle().Subject;
        call.Text.Should().Be("12-__");
        call.Bounds.Should().Be(editor.ClientRectangle);
        call.ForeColor.Should().Be(Color.Navy);
        editor.Font.Should().BeSameAs(font);
        AssertCanonicalProjectedFont(call.Font, font, 19f);
        editor.Text.Should().Be(output);
        editor.SelectionStart.Should().Be(1);
        editor.SelectionLength.Should().Be(1);
        editor.IsHandleCreated.Should().BeFalse();
    }

    [Theory]
    [InlineData(HorizontalAlignment.Left, RightToLeft.No, LibreTextFormat.Default)]
    [InlineData(HorizontalAlignment.Center, RightToLeft.No, LibreTextFormat.HorizontalCenter)]
    [InlineData(HorizontalAlignment.Right, RightToLeft.No, LibreTextFormat.Right)]
    [InlineData(HorizontalAlignment.Left, RightToLeft.Yes, LibreTextFormat.Right)]
    [InlineData(HorizontalAlignment.Center, RightToLeft.Yes, LibreTextFormat.HorizontalCenter)]
    [InlineData(HorizontalAlignment.Right, RightToLeft.Yes, LibreTextFormat.Default)]
    public void PortableMaskedPaintPreservesSourceAlignment(
        HorizontalAlignment alignment, RightToLeft direction, LibreTextFormat horizontal)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using PaintableMaskedTextBox editor = new()
        {
            Mask = "&&&",
            Text = "a&ב",
            TextAlign = alignment,
            RightToLeft = direction,
            Enabled = false
        };
        using Bitmap target = new(160, 40);
        using Graphics graphics = Graphics.FromImage(target);

        editor.PaintContent(graphics, editor.ClientRectangle);

        TextBoxPaintCall call = platform.TextBoxDraws.Should().ContainSingle().Subject;
        call.Text.Should().Be("a&ב");
        call.ForeColor.Should().Be(SystemColors.GrayText);
        call.Format.Should().Be(LibreTextFormat.TextBoxControl | LibreTextFormat.NoPrefix
            | LibreTextFormat.NoPadding | LibreTextFormat.SingleLine | horizontal
            | (direction == RightToLeft.Yes ? LibreTextFormat.RightToLeft : 0));
    }

    [Theory]
    [InlineData(false, false, "**-__")]
    [InlineData(false, true, "●●-__")]
    [InlineData(true, false, "****")]
    [InlineData(true, true, "●●●●")]
    public void PortableMaskedPaintNeverTransportsPasswordSource(bool nullMask, bool systemPassword, string display)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using PaintableMaskedTextBox editor = new()
        {
            Mask = nullMask ? string.Empty : "00-00",
            Text = nullMask ? "A🙂B" : "12",
            PasswordChar = '*',
            UseSystemPasswordChar = systemPassword
        };
        using Bitmap target = new(160, 40);
        using Graphics graphics = Graphics.FromImage(target);

        editor.PaintContent(graphics, editor.ClientRectangle);

        platform.TextBoxDraws.Should().ContainSingle().Which.Text.Should().Be(display);
        editor.Text.Should().Be(nullMask ? "A🙂B" : "12-");
        editor.IsHandleCreated.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false, "12-__")]
    [InlineData(false, true, "12-")]
    [InlineData(true, false, "12-")]
    [InlineData(true, true, "12-")]
    public void PortableMaskedPaintUsesCanonicalPromptVisibility(bool readOnly, bool hidePrompt, string display)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using PaintableMaskedTextBox editor = new()
        {
            Mask = "00-00", Text = "12", ReadOnly = readOnly, HidePromptOnLeave = hidePrompt
        };
        using Bitmap target = new(160, 40);
        using Graphics graphics = Graphics.FromImage(target);

        editor.PaintContent(graphics, editor.ClientRectangle);

        platform.TextBoxDraws.Should().ContainSingle().Which.Text.Should().Be(display);
    }

    [Fact]
    public void PortableMaskedPaintIntersectsAndRestoresTheOriginalGraphicsFrame()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using PaintableMaskedTextBox editor = new()
        {
            BorderStyle = BorderStyle.None, AutoSize = false, Size = new Size(80, 40), Mask = "00-00", Text = "1234"
        };
        using Bitmap target = new(120, 80);
        using Graphics graphics = Graphics.FromImage(target);
        graphics.TranslateTransform(7, 9);
        graphics.SetClip(new Rectangle(3, 4, 50, 30));
        RectangleF originalClip = graphics.ClipBounds;
        using Matrix originalTransform = graphics.Transform;

        editor.PaintContent(graphics, new Rectangle(10, 12, 100, 40));

        platform.TextBoxDraws.Should().ContainSingle().Which.Clip.Should().Be(new RectangleF(10, 12, 43, 22));
        graphics.ClipBounds.Should().Be(originalClip);
        using Matrix finalTransform = graphics.Transform;
        finalTransform.Elements.Should().Equal(originalTransform.Elements);
    }

    [Fact]
    public void PortableMaskedPaintChangesInvalidateContentWithoutInventingTextEvents()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ClientSize = new Size(200, 100), ShowIcon = false };
        using MaskedTextBox editor = new() { Mask = "00-00", Text = "12", HidePromptOnLeave = true };
        using Button other = new() { Bounds = new Rectangle(0, 50, 100, 25) };
        form.Controls.Add(editor);
        form.Controls.Add(other);
        form.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        other.Focus().Should().BeTrue();
        form.Update();
        platform.TextBoxDraws.Clear();
        int changes = 0;
        editor.TextChanged += (_, _) => changes++;

        editor.Focus().Should().BeTrue();
        form.Update();
        platform.TextBoxDraws.Should().Contain(call => call.Text == "12-__");
        platform.TextBoxDraws.Clear();
        other.Focus().Should().BeTrue();
        form.Update();
        platform.TextBoxDraws.Should().Contain(call => call.Text == "12-");
        platform.TextBoxDraws.Clear();
        editor.PasswordChar = '*';
        form.Update();
        platform.TextBoxDraws.Should().Contain(call => call.Text == "**-");
        changes.Should().Be(0);
        platform.TextBoxDraws.Clear();
        editor.Text = "34";
        form.Update();
        platform.TextBoxDraws.Should().Contain(call => call.Text == "**-");
        changes.Should().Be(1);
        platform.TextBoxDraws.Clear();
        editor.Enabled = false;
        form.Update();
        platform.TextBoxDraws.Should().Contain(call => call.Text == "**-" && call.ForeColor == SystemColors.GrayText);
    }

    [Fact]
    public void PortableMaskedPaintEmptyViewportDoesNotSubmitText()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using PaintableMaskedTextBox editor = new() { Width = 0, Mask = "00" };
        using Bitmap target = new(20, 20);
        using Graphics graphics = Graphics.FromImage(target);

        editor.PaintContent(graphics, new Rectangle(0, 0, 20, 20));

        platform.TextBoxDraws.Should().BeEmpty();
    }

    [Fact]
    public void PortableMaskedPaintParticipatesInTheOriginalGridEditorLifecycle()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ClientSize = new Size(400, 240), ShowIcon = false };
        using DataGridView grid = new() { Size = new Size(360, 200), AllowUserToAddRows = false };
        grid.Columns.Add(new DataGridViewColumn(new MaskedPaintCell()) { Width = 180 });
        grid.Rows.Add("1234");
        form.Controls.Add(grid);
        form.Show();
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.BeginEdit(selectAll: false).Should().BeTrue();
        MaskedPaintEditingControl editor = grid.EditingControl.Should().BeOfType<MaskedPaintEditingControl>().Subject;
        editor.Text = "5678";
        platform.TextBoxDraws.Clear();
        using Bitmap target = new(editor.Width, editor.Height);

        editor.DrawToBitmap(target, new Rectangle(Point.Empty, editor.Size));

        platform.TextBoxDraws.Should().ContainSingle().Which.Text.Should().Be("56-78");
        grid.IsCurrentCellInEditMode.Should().BeTrue();
        grid.EndEdit().Should().BeTrue();
        grid.Rows[0].Cells[0].Value.Should().Be("56-78");
    }

    private sealed class MaskedPaintCell : DataGridViewTextBoxCell
    {
        public override Type EditType => typeof(MaskedPaintEditingControl);

        public override void InitializeEditingControl(int rowIndex, object? initialFormattedValue, DataGridViewCellStyle style)
        {
            base.InitializeEditingControl(rowIndex, initialFormattedValue, style);
            MaskedPaintEditingControl editor = (MaskedPaintEditingControl)DataGridView!.EditingControl!;
            editor.Mask = "00-00";
            editor.Text = initialFormattedValue?.ToString();
        }
    }

    private sealed class MaskedPaintEditingControl : MaskedTextBox, IDataGridViewEditingControl
    {
        public MaskedPaintEditingControl() => BorderStyle = BorderStyle.None;

        public DataGridView? EditingControlDataGridView { get; set; }
        [AllowNull]
        public object EditingControlFormattedValue { get => Text; set => Text = value?.ToString(); }
        public int EditingControlRowIndex { get; set; }
        public bool EditingControlValueChanged { get; set; }
        public bool RepositionEditingControlOnValueChange => false;
        public Cursor EditingPanelCursor => Cursors.IBeam;

        public void ApplyCellStyleToEditingControl(DataGridViewCellStyle style)
        {
            Font = style.Font;
            ForeColor = style.ForeColor;
            BackColor = style.BackColor;
        }

        public bool EditingControlWantsInputKey(Keys keyData, bool dataGridViewWantsInputKey) => !dataGridViewWantsInputKey;
        public object GetEditingControlFormattedValue(DataGridViewDataErrorContexts context) => Text;
        public void PrepareEditingControlForEdit(bool selectAll)
        {
            if (selectAll) SelectAll();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            EditingControlValueChanged = true;
            EditingControlDataGridView?.NotifyCurrentCellDirty(true);
        }
    }

    private sealed class PaintableMaskedTextBox : MaskedTextBox
    {
        internal void PaintContent(Graphics graphics, Rectangle clip)
        {
            using PaintEventArgs args = new(graphics, clip);
            OnPaint(args);
        }
    }
}
