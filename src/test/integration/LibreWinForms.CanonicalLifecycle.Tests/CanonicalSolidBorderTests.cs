// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;
using FluentAssertions;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Theory]
    [InlineData(4, 4, false)]
    [InlineData(1, 4, false)]
    [InlineData(4, 1, false)]
    [InlineData(1, 1, false)]
    [InlineData(4, 4, true)]
    [InlineData(1, 4, true)]
    [InlineData(4, 1, true)]
    [InlineData(1, 1, true)]
    public void PortableSolidBorder_RetainsExactIntegerEdgesAndCallerFrame(int width, int height, bool translated)
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using Bitmap bitmap = new(10, 10);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Magenta);
        if (translated)
        {
            graphics.TranslateTransform(1, 2);
        }

        graphics.SetClip(new Rectangle(0, 0, 5, 5));
        using var originalTransform = graphics.Transform;
        RectangleF originalClip = graphics.ClipBounds;
        ControlPaint.DrawBorder(graphics, new Rectangle(2, 2, width, height), SystemColors.ControlDark, ButtonBorderStyle.Solid);
        graphics.Transform.Elements.Should().Equal(originalTransform.Elements);
        graphics.ClipBounds.Should().Be(originalClip);

        for (int y = 0; y < 10; y++)
        for (int x = 0; x < 10; x++)
        {
            int localX = x - (translated ? 1 : 0), localY = y - (translated ? 2 : 0);
            bool inside = localX >= 2 && localX < 2 + width && localY >= 2 && localY < 2 + height;
            bool edge = localX == 2 || localX == 1 + width || localY == 2 || localY == 1 + height;
            bool clipped = localX < 0 || localX >= 5 || localY < 0 || localY >= 5;
            Color expected = inside && edge && !clipped ? SystemColors.ControlDark : Color.Magenta;
            bitmap.GetPixel(x, y).ToArgb().Should().Be(expected.ToArgb(), $"opaque border pixel ({x},{y})");
        }
    }
}
