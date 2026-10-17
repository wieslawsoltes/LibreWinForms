// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using FluentAssertions;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    public static IEnumerable<object[]> OriginalScrollBarStates()
        => Enumerable.Range(0, 96).Select(index => new object[] { index });

    [Theory]
    [MemberData(nameof(OriginalScrollBarStates))]
    public void PortableScrollBarPaintRetainsOriginalNativeGeometry(int index)
    {
        // Immutable Microsoft WindowsDesktop 10.0.12 observations from run
        // 39037040757. No generated expectation from the product algorithm.
        using JsonDocument original = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "original-scrollbar-geometry.json")));
        original.RootElement.GetProperty("cases").GetArrayLength().Should().Be(96);
        JsonElement sample = original.RootElement.GetProperty("cases")[index];
        bool vertical = sample.GetProperty("vertical").GetBoolean();
        bool enabled = sample.GetProperty("enabled").GetBoolean();
        int length = sample.GetProperty("Length").GetInt32();
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        Application.EnableVisualStyles();
        using PaintableHScrollBar horizontal = new();
        using PaintableVScrollBar upright = new();
        ScrollBar bar = vertical ? upright : horizontal;
        bar.Size = vertical ? new(17, length) : new(length, 17);
        bar.Minimum = sample.GetProperty("minimum").GetInt32();
        bar.Maximum = sample.GetProperty("maximum").GetInt32();
        bar.LargeChange = sample.GetProperty("page").GetInt32();
        bar.Value = sample.GetProperty("value").GetInt32();
        bar.RightToLeft = sample.GetProperty("rtl").GetBoolean() ? RightToLeft.Yes : RightToLeft.No;
        bar.Enabled = enabled;
        int sourceValue = bar.Value;
        using Bitmap target = new(Math.Max(1, bar.Width), Math.Max(1, bar.Height));
        using Graphics graphics = Graphics.FromImage(target);
        platform.ScrollBarPaintCalls = new();
        try
        {
            if (vertical) upright.PaintContent(graphics, bar.ClientRectangle);
            else horizontal.PaintContent(graphics, bar.ClientRectangle);
            var thumb = platform.ScrollBarPaintCalls.Single(call => call.Class == "SCROLLBAR" && call.Part == (vertical ? 3 : 2));
            int start = sample.GetProperty("thumbStart").GetInt32();
            int end = sample.GetProperty("thumbEnd").GetInt32();
            thumb.Bounds.Should().Be(vertical ? new Rectangle(0, start, 17, end - start)
                : new Rectangle(start, 0, end - start, 17));
            thumb.State.Should().Be(enabled ? 1 : 4);
            int line = sample.GetProperty("line").GetInt32();
            var arrows = platform.ScrollBarPaintCalls.Where(call => call.Class == "SCROLLBAR" && call.Part == 1).ToArray();
            arrows.Length.Should().Be(line == 0 ? 0 : 2);
            if (line != 0)
            {
                arrows[0].Bounds.Should().Be(vertical ? new Rectangle(0, 0, 17, line) : new Rectangle(0, 0, line, 17));
                arrows[1].Bounds.Should().Be(vertical ? new Rectangle(0, length - line, 17, line) : new Rectangle(length - line, 0, line, 17));
            }

            bar.Value.Should().Be(sourceValue);
            bar.IsHandleCreated.Should().BeFalse();
            horizontal.HasPaintStyle.Should().BeTrue();
            upright.HasPaintStyle.Should().BeTrue();
        }
        finally { platform.ScrollBarPaintCalls = null; }
    }

    [Fact]
    public void PortableScrollBarPaintRestoresCallerClipAndPreservesPaintEvent()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        Application.EnableVisualStyles();
        using PaintableHScrollBar bar = new() { Size = new(100, 17) };
        using Bitmap target = new(100, 17);
        using Graphics graphics = Graphics.FromImage(target);
        graphics.Clear(Color.Transparent);
        graphics.SetClip(new Rectangle(20, 0, 30, 17));
        RectangleF before = graphics.ClipBounds;
        int notifications = 0;
        bar.Paint += (_, _) => notifications++;
        bar.PaintContent(graphics, new Rectangle(0, 0, 40, 17));
        graphics.ClipBounds.Should().Be(before);
        notifications.Should().Be(1);
        target.GetPixel(10, 8).A.Should().Be(0);
        target.GetPixel(25, 8).A.Should().Be(255);
        target.GetPixel(45, 8).A.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PortableScrollBarPaintSourcePublicationInvalidatesOwnedHandle(bool enabled)
    {
        RunNativeScrollBar(true, (_, _, bar, _, _) =>
        {
            bar.Enabled = enabled;
            int invalidations = 0;
            bar.Invalidated += (_, _) => invalidations++;
            foreach (Action change in new Action[]
            {
                () => bar.Value = 51,
                () => bar.Minimum = 1,
                () => bar.Maximum = 101,
                () => bar.LargeChange = 11,
            })
            {
                int before = invalidations;
                change();
                invalidations.Should().BeGreaterThan(before);
            }
        });
    }

    private sealed class PaintableHScrollBar : HScrollBar
    {
        internal bool HasPaintStyle => GetStyle(ControlStyles.UserPaint) && GetStyle(ControlStyles.ResizeRedraw);
        internal void PaintContent(Graphics graphics, Rectangle clip) => OnPaint(new PaintEventArgs(graphics, clip));
    }

    private sealed class PaintableVScrollBar : VScrollBar
    {
        internal bool HasPaintStyle => GetStyle(ControlStyles.UserPaint) && GetStyle(ControlStyles.ResizeRedraw);
        internal void PaintContent(Graphics graphics, Rectangle clip) => OnPaint(new PaintEventArgs(graphics, clip));
    }

    private sealed partial class HeadlessPlatform
    {
        internal List<(string Class, int Part, int State, Rectangle Bounds)>? ScrollBarPaintCalls { get; set; }
    }
}
