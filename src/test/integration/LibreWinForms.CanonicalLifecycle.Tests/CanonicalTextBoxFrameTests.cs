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
    [InlineData(BorderStyle.None, 0)]
    [InlineData(BorderStyle.FixedSingle, 1)]
    [InlineData(BorderStyle.Fixed3D, 2)]
    public void PortableTextBoxFrame_ClientSizeAndScreenMappingUseSourceInsets(BorderStyle style, int inset)
    {
        UseHeadlessPlatform(autoCloseWindows: false).BorderSizeValue = new(1, 1);
        using Form form = new() { Location = new Point(100, 200), ClientSize = new Size(300, 200) };
        using TextBox editor = new()
        {
            AutoSize = false, BorderStyle = style, Bounds = new Rectangle(10, 20, 80, 30)
        };
        using Control child = new() { Bounds = new Rectangle(3, 4, 8, 6) };
        form.Controls.Add(editor);
        editor.Controls.Add(child);

        editor.ClientSize.Should().Be(new Size(80 - 2 * inset, 30 - 2 * inset));
        int actualInset = style == BorderStyle.FixedSingle ? 0 : inset;
        editor.PointToScreen(Point.Empty).Should().Be(new Point(110 + actualInset, 220 + actualInset));
        child.PointToScreen(Point.Empty).Should().Be(new Point(113 + actualInset, 224 + actualInset));
        child.PointToClient(new Point(120 + actualInset, 230 + actualInset)).Should().Be(new Point(7, 6));
        editor.ClientSize = new Size(70, 18);
        editor.Size.Should().Be(new Size(70 + 2 * inset, 18 + 2 * inset));
        editor.IsHandleCreated.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableTextBoxFrame_StyleChangeRetainsNativeHandleDependentClientState(bool created)
    {
        UseHeadlessPlatform(autoCloseWindows: false).BorderSizeValue = new(1, 1);
        using TextBox editor = new() { AutoSize = false, Size = new Size(80, 30) };
        if (created) _ = editor.Handle;
        Rectangle bounds = editor.Bounds;
        Size expected = created ? new Size(80, 30) : new Size(76, 26);
        int changed = 0;
        int createdEvents = 0;
        editor.HandleCreated += (_, _) =>
        {
            createdEvents++;
            editor.ClientSize.Should().Be(expected);
        };
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
        createdEvents.Should().Be(created ? 1 : 0);
    }

    [Theory]
    [InlineData(BorderStyle.None, 0)]
    [InlineData(BorderStyle.FixedSingle, 1)]
    [InlineData(BorderStyle.Fixed3D, 2)]
    public void PortableTextBoxFrame_FlatPaintKeepsFrameOutsideClientAndClipsChildren(BorderStyle style, int inset)
    {
        UseHeadlessPlatform(autoCloseWindows: false).BorderSizeValue = new(1, 1);
        using TextBox editor = new()
        {
            AutoSize = false, BorderStyle = style, Size = new Size(30, 20), BackColor = Color.Lime
        };
        using Control child = new()
        {
            Bounds = new Rectangle(-5, -5, 50, 40), BackColor = Color.Red
        };
        editor.Controls.Add(child);
        using Bitmap bitmap = new(40, 30);
        using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.Magenta);
        editor.DrawToBitmap(bitmap, new Rectangle(Point.Empty, editor.Size));

        // EDIT's FixedSingle border belongs to the client; ordinary child
        // painting can cover it. Fixed3D descendants stop at the true NC frame.
        int actualInset = style == BorderStyle.FixedSingle ? 0 : inset;
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
        {
            int pixel = bitmap.GetPixel(x, y).ToArgb();
            if (x >= 30 || y >= 20) pixel.Should().Be(Color.Magenta.ToArgb());
            else if (x >= actualInset && x < 30 - actualInset && y >= actualInset && y < 20 - actualInset)
                pixel.Should().Be(Color.Red.ToArgb());
            else
            {
                pixel.Should().NotBe(Color.Red.ToArgb());
                pixel.Should().NotBe(Color.Magenta.ToArgb());
            }
        }
    }

    [Fact]
    public void PortableTextBoxFrame_RetainedPaintAndPointerUseSameClientOrigin()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { AutoScaleMode = AutoScaleMode.None, ClientSize = new Size(200, 100) };
        using TextBox editor = new()
        {
            AutoSize = false, Bounds = new Rectangle(10, 20, 80, 30), Text = "source"
        };
        using Control child = new() { Bounds = new Rectangle(3, 4, 12, 8) };
        form.Controls.Add(editor);
        editor.Controls.Add(child);
        Point? mouse = null;
        child.MouseDown += (_, e) => mouse = e.Location;
        PointF? paintOrigin = null;
        Rectangle? paintClip = null;
        editor.Paint += (_, e) =>
        {
            using var transform = e.Graphics.Transform;
            paintOrigin = new PointF(transform.OffsetX, transform.OffsetY);
            paintClip = e.ClipRectangle;
        };
        form.Show();
        form.Invalidate();
        form.Update();
        paintOrigin.Should().Be(new PointF(2, 2));
        paintClip.Should().Be(editor.ClientRectangle);
        platform.LastRetainedLayerCount.Should().Be(3);
        platform.SendInput(LibreInputEventKind.PointerDown,
            position: new LibrePoint(17, 29), button: LibrePointerButton.Primary);
        mouse.Should().Be(new Point(2, 3));
        form.Close();
    }

    [Fact]
    public void PortableTextBoxFrame_CreateGraphicsUsesClientAndAncestorClip()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ClientSize = new Size(200, 100) };
        using TextBox editor = new() { AutoSize = false, Bounds = new Rectangle(10, 20, 30, 20) };
        using Control child = new() { Bounds = new Rectangle(20, 0, 20, 20) };
        form.Controls.Add(editor);
        editor.Controls.Add(child);
        using Graphics graphics = child.CreateGraphics();
        graphics.VisibleClipBounds.Should().Be(new RectangleF(0, 0, 6, 16));
    }

    [Fact]
    public void PortableTextBoxFrame_ExhaustedClientDoesNotPaintTextOrChildren()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using TextBox editor = new() { AutoSize = false, Size = new Size(2, 2), Text = "not visible" };
        using Control child = new() { Size = new Size(10, 10), BackColor = Color.Red };
        editor.Controls.Add(child);
        editor.ClientSize.Should().Be(Size.Empty);
        using Bitmap bitmap = new(2, 2);
        editor.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        platform.TextBoxDraws.Should().BeEmpty();
        bitmap.GetPixel(0, 0).ToArgb().Should().NotBe(Color.Red.ToArgb());
    }

    [Fact]
    public void PortableTextBoxFrame_HonorsAsymmetricPlatformBorderMetrics()
    {
        UseHeadlessPlatform(autoCloseWindows: false); // Deliberate 11-by-13 source metrics.
        using TextBox editor = new()
        {
            AutoSize = false, BorderStyle = BorderStyle.FixedSingle,
            Size = new Size(60, 50), BackColor = Color.Lime
        };
        editor.ClientSize.Should().Be(new Size(38, 24));
        editor.PointToScreen(Point.Empty).Should().Be(Point.Empty);
        using Bitmap bitmap = new(60, 50);
        editor.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.GetPixel(10, 24).ToArgb().Should().Be(SystemColors.WindowFrame.ToArgb());
        bitmap.GetPixel(30, 12).ToArgb().Should().Be(SystemColors.WindowFrame.ToArgb());
        bitmap.GetPixel(11, 13).ToArgb().Should().Be(Color.Lime.ToArgb());
        bitmap.GetPixel(48, 36).ToArgb().Should().Be(Color.Lime.ToArgb());
        bitmap.GetPixel(49, 36).ToArgb().Should().Be(SystemColors.WindowFrame.ToArgb());
        bitmap.GetPixel(48, 37).ToArgb().Should().Be(SystemColors.WindowFrame.ToArgb());
    }

    [Theory]
    [InlineData(BorderStyle.None, 160)]
    [InlineData(BorderStyle.FixedSingle, 158)]
    [InlineData(BorderStyle.Fixed3D, 156)]
    public void PortableTextBoxFrame_ScalingExcludesAndRestoresSourceAdornments(BorderStyle style, int width)
    {
        UseHeadlessPlatform(autoCloseWindows: false).BorderSizeValue = new(1, 1);
        using FrameGeometryProbe editor = new()
        {
            AutoSize = false, BorderStyle = style, Bounds = new Rectangle(10, 20, 80, 30)
        };
        editor.ScaledBounds().Width.Should().Be(width);
    }

    [Fact]
    public void PortableTextBoxFrame_SunkenPixelBandsKeepOriginalColorAndCornerOrder()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using TextBox editor = new() { AutoSize = false, Size = new Size(30, 20), BackColor = Color.Lime };
        using Bitmap bitmap = new(30, 20);
        editor.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.GetPixel(0, 0).ToArgb().Should().Be(SystemColors.ControlDark.ToArgb());
        bitmap.GetPixel(29, 0).ToArgb().Should().Be(SystemColors.ControlLightLight.ToArgb());
        bitmap.GetPixel(0, 19).ToArgb().Should().Be(SystemColors.ControlLightLight.ToArgb());
        bitmap.GetPixel(29, 19).ToArgb().Should().Be(SystemColors.ControlLightLight.ToArgb());
        bitmap.GetPixel(1, 1).ToArgb().Should().Be(SystemColors.ControlDarkDark.ToArgb());
        bitmap.GetPixel(28, 18).ToArgb().Should().Be(SystemColors.ControlLight.ToArgb());
    }

    private sealed class FrameGeometryProbe : TextBox
    {
        internal Rectangle ScaledBounds() => GetScaledBounds(Bounds, new SizeF(2, 2), BoundsSpecified.All);
    }
}
