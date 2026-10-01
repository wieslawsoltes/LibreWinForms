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
    // Reuse the original EDIT frame inventory, not values computed by the helper.
    // MaskedTextBox retains TextBoxBase's EDIT class/border styles. This is source
    // coverage of that shared contract, not a new native masked-editor receipt.
    [Theory]
    [InlineData(BorderStyle.None, 120, 40, 120, 40, 70, 18, 0)]
    [InlineData(BorderStyle.FixedSingle, 118, 38, 120, 40, 72, 20, 0)]
    [InlineData(BorderStyle.Fixed3D, 116, 36, 116, 36, 74, 22, 2)]
    public void PortableMaskedFrameRetainsEditClientLifecycle(
        BorderStyle border, int beforeWidth, int beforeHeight, int afterWidth, int afterHeight,
        int requestedWidth, int requestedHeight, int clientOrigin)
    {
        UseHeadlessPlatform(autoCloseWindows: false).BorderSizeValue = new(1, 1);
        using MaskedTextBox editor = new()
        {
            AutoSize = false, BorderStyle = border, Size = new Size(120, 40), Mask = "00-00", Text = "1234"
        };
        editor.ClientSize.Should().Be(new Size(beforeWidth, beforeHeight));
        _ = editor.Handle;
        editor.ClientSize.Should().Be(new Size(afterWidth, afterHeight));
        editor.PointToScreen(Point.Empty).Should().Be(new Point(clientOrigin, clientOrigin));
        editor.ClientSize = new Size(70, 18);
        editor.Size.Should().Be(new Size(requestedWidth, requestedHeight));
        editor.ClientSize.Should().Be(new Size(70, 18));
        using Graphics graphics = editor.CreateGraphics();
        graphics.VisibleClipBounds.Should().Be(new RectangleF(0, 0,
            requestedWidth - 2 * clientOrigin, requestedHeight - 2 * clientOrigin));
        editor.Text.Should().Be("12-34");
    }

    [Theory]
    [InlineData(BorderStyle.None, 0, 0, 120, 40)]
    [InlineData(BorderStyle.FixedSingle, 2, 2, 116, 36)]
    [InlineData(BorderStyle.Fixed3D, 1, 1, 114, 34)]
    public void PortableMaskedFramePaintUsesActualFormattingViewport(BorderStyle border, int x, int y, int width, int height)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.BorderSizeValue = new(1, 1);
        using MaskedTextBox editor = new()
        {
            AutoSize = false, BorderStyle = border, Size = new Size(120, 40), Mask = "00-00", Text = "1234"
        };
        using Bitmap bitmap = new(120, 40);

        editor.DrawToBitmap(bitmap, new Rectangle(Point.Empty, editor.Size));

        TextBoxPaintCall call = platform.TextBoxDraws.Should().ContainSingle().Subject;
        call.Text.Should().Be("12-34");
        call.Bounds.Should().Be(new Rectangle(x, y, width, height));
        call.Clip.Should().Be(new RectangleF(x, y, width, height));
    }

    [Fact]
    public void PortableMaskedFrameRetainedPaintAndPointerKeepTheSameClientFrame()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.BorderSizeValue = new(1, 1);
        using Form form = new() { AutoScaleMode = AutoScaleMode.None, ClientSize = new Size(200, 100) };
        using MaskedTextBox editor = new()
        {
            AutoSize = false, Bounds = new Rectangle(10, 20, 80, 30), Mask = "00-00", Text = "1234"
        };
        using Control child = new() { Bounds = new Rectangle(3, 4, 12, 8) };
        form.Controls.Add(editor);
        editor.Controls.Add(child);
        Point? childMouse = null;
        child.MouseDown += (_, e) => childMouse = e.Location;
        int parentClicks = 0, editorClicks = 0;
        form.MouseDown += (_, _) => parentClicks++;
        editor.MouseDown += (_, _) => editorClicks++;
        form.Show();
        platform.TextBoxDraws.Clear();
        form.Invalidate(true);
        form.Update();

        platform.TextBoxDraws.Should().Contain(call => call.Text == "12-34"
            && call.Bounds == new Rectangle(1, 1, 74, 24));
        platform.SendInput(LibreInputEventKind.PointerDown, position: new(10, 35), button: LibrePointerButton.Primary);
        platform.SendInput(LibreInputEventKind.PointerUp, position: new(10, 35), button: LibrePointerButton.Primary);
        parentClicks.Should().Be(0);
        editorClicks.Should().Be(0);
        platform.SendInput(LibreInputEventKind.PointerDown, position: new(17, 29), button: LibrePointerButton.Primary);
        childMouse.Should().Be(new Point(2, 3));
    }

    [Fact]
    public void PortableMaskedFrameBandsRetainColorsAndClipChildren()
    {
        UseHeadlessPlatform(autoCloseWindows: false).BorderSizeValue = new(1, 1);
        using MaskedTextBox editor = new()
        {
            AutoSize = false, Size = new Size(30, 20), BackColor = Color.Lime
        };
        using Control child = new() { Bounds = new Rectangle(-5, -5, 50, 40), BackColor = Color.Red };
        editor.Controls.Add(child);
        using Bitmap bitmap = new(30, 20);

        editor.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));

        bitmap.GetPixel(0, 0).ToArgb().Should().Be(SystemColors.ControlDark.ToArgb());
        bitmap.GetPixel(29, 0).ToArgb().Should().Be(SystemColors.ControlLightLight.ToArgb());
        bitmap.GetPixel(0, 19).ToArgb().Should().Be(SystemColors.ControlLightLight.ToArgb());
        bitmap.GetPixel(1, 1).ToArgb().Should().Be(SystemColors.ControlDarkDark.ToArgb());
        bitmap.GetPixel(28, 18).ToArgb().Should().Be(SystemColors.ControlLight.ToArgb());
        bitmap.GetPixel(2, 2).ToArgb().Should().Be(Color.Red.ToArgb());
        bitmap.GetPixel(27, 17).ToArgb().Should().Be(Color.Red.ToArgb());
    }

    [Fact]
    public void PortableMaskedFrameHonorsAsymmetricMetricsAndEmptyViewport()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false); // 11-by-13 border metrics.
        using MaskedTextBox editor = new()
        {
            AutoSize = false, BorderStyle = BorderStyle.FixedSingle, Size = new Size(60, 60),
            Mask = "00", Text = "12", BackColor = Color.Lime
        };
        using Bitmap bitmap = new(60, 60);
        editor.DrawToBitmap(bitmap, new Rectangle(Point.Empty, editor.Size));
        platform.TextBoxDraws.Should().ContainSingle().Which.Bounds.Should().Be(new Rectangle(22, 26, 16, 8));
        bitmap.GetPixel(10, 24).ToArgb().Should().Be(SystemColors.WindowFrame.ToArgb());
        bitmap.GetPixel(30, 12).ToArgb().Should().Be(SystemColors.WindowFrame.ToArgb());
        bitmap.GetPixel(11, 13).ToArgb().Should().Be(Color.Lime.ToArgb());
        platform.TextBoxDraws.Clear();
        editor.Size = new Size(2, 2);
        editor.DrawToBitmap(bitmap, new Rectangle(Point.Empty, editor.Size));
        platform.TextBoxDraws.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableMaskedFrameStyleChangePreservesSourceHandleLifecycle(bool created)
    {
        UseHeadlessPlatform(autoCloseWindows: false).BorderSizeValue = new(1, 1);
        using MaskedTextBox editor = new() { AutoSize = false, Size = new Size(80, 30) };
        if (created) _ = editor.Handle;
        Rectangle bounds = editor.Bounds;
        Size expected = created ? new Size(80, 30) : new Size(76, 26);
        int changed = 0;
        editor.BorderStyleChanged += (_, _) =>
        {
            changed++;
            editor.ClientSize.Should().Be(expected);
        };

        editor.BorderStyle = BorderStyle.FixedSingle;

        changed.Should().Be(1);
        editor.Bounds.Should().Be(bounds);
        editor.ClientSize.Should().Be(expected);
        editor.IsHandleCreated.Should().Be(created);
    }
}
