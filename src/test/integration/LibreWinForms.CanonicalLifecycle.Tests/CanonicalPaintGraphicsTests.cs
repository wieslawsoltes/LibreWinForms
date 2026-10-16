// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using FluentAssertions;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void PortablePaintGraphics_WrappedRenderingRetainsOriginalFrameAndOwnership(
        int wrapper, bool transformed)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        Application.EnableVisualStyles();
        var renderer = new VisualStyleRenderer(VisualStyleElement.Button.PushButton.Normal);
        using Bitmap direct = new(40, 32), wrapped = new(40, 32);
        using Graphics expected = Graphics.FromImage(direct), actual = Graphics.FromImage(wrapped);
        foreach (Graphics graphics in new[] { expected, actual })
        {
            graphics.Clear(Color.Magenta);
            if (transformed) graphics.TranslateTransform(4, 5);
            graphics.SetClip(new Rectangle(3, 2, 22, 17));
        }

        Rectangle bounds = new(1, 1, 25, 19);
        renderer.DrawBackground(expected, bounds);
        using Matrix transform = actual.Transform;
        RectangleF clip = actual.ClipBounds;
        IDeviceContext context = wrapper switch
        {
            0 => new PaintEventArgs(actual, bounds),
            1 => new DrawItemEventArgs(actual, null, bounds, 0, DrawItemState.None),
            _ => new TableLayoutCellPaintEventArgs(actual, bounds, bounds, 0, 0)
        };
        try
        {
            renderer.DrawBackground(context, bounds);
            platform.VisualStyleDrawCount.Should().Be(2);
            actual.Transform.Elements.Should().Equal(transform.Elements);
            actual.ClipBounds.Should().Be(clip);
        }
        finally
        {
            context.Dispose();
        }

        // The event borrows the original frame. It must remain usable afterward.
        actual.ResetTransform(); actual.ResetClip();
        expected.ResetTransform(); expected.ResetClip();
        actual.FillRectangle(Brushes.Lime, new Rectangle(36, 28, 2, 2));
        expected.FillRectangle(Brushes.Lime, new Rectangle(36, 28, 2, 2));
        for (int y = 0; y < direct.Height; y++)
        for (int x = 0; x < direct.Width; x++)
            wrapped.GetPixel(x, y).ToArgb().Should().Be(direct.GetPixel(x, y).ToArgb(), $"original wrapped frame pixel ({x},{y})");
        wrapped.GetPixel(12, 12).ToArgb().Should().Be(Color.Purple.ToArgb());
    }

    [Fact]
    public void PortablePaintGraphics_StandardButtonPaintsWhileSpaceIsHeld()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var button = new PressedPaintingButton { Size = new Size(48, 24), Text = string.Empty, FlatStyle = FlatStyle.Standard };
        using Bitmap target = new(48, 24);
        using Graphics graphics = Graphics.FromImage(target);
        graphics.Clear(Color.Magenta);
        VisualStyleState original = Application.VisualStyleState;
        try
        {
            Application.VisualStyleState = VisualStyleState.NonClientAreaEnabled;
            button.PaintPressed(graphics);
            target.GetPixel(0, 12).ToArgb().Should().Be(SystemColors.ControlDark.ToArgb());
            target.GetPixel(24, 12).ToArgb().Should().Be(SystemColors.Control.ToArgb());
        }
        finally
        {
            Application.VisualStyleState = original;
        }
    }

    private sealed class PressedPaintingButton : Button
    {
        internal void PaintPressed(Graphics graphics)
        {
            OnKeyDown(new KeyEventArgs(Keys.Space));
            try
            {
                using PaintEventArgs args = new(graphics, ClientRectangle);
                OnPaint(args);
            }
            finally
            {
                OnKeyUp(new KeyEventArgs(Keys.Space));
            }
        }
    }
}
