// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;
using FluentAssertions;
using LibreWinForms.Platform;
using LibreWinForms.ProGPU;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PortableToolTipTextMarginsMatchMeasurementWithoutChangingCallerSize(bool title, bool resize)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { Bounds = new Rectangle(40, 50, 400, 220) };
        using Button target = new() { Bounds = new Rectangle(20, 30, 180, 28) };
        form.Controls.Add(target);
        form.Show();
        ToolTipTextProbe probe = new();
        ILibreTextRendererService? previous = platform.ActualTextRenderer;
        platform.ActualTextRenderer = probe;
        try
        {
            using ToolTip tip = new() { ToolTipTitle = title ? "Tooltip title" : string.Empty };
            Size requested = new(180, 28);
            if (resize)
                tip.Popup += (_, args) => args.ToolTipSize = requested;

            tip.Show("Popup interaction tooltip", target, new Point(7, 9));

            platform.Popups.Should().ContainSingle();
            probe.Draws.Should().HaveCount(title ? 2 : 1);
            foreach (var draw in probe.Draws)
            {
                draw.Format.Should().HaveFlag(LibreTextFormat.NoPadding,
                    "the source measured without text margins and already owns outer tooltip padding");
                if (!resize)
                    draw.Required.Height.Should().BeLessThanOrEqualTo(draw.Bounds.Height,
                        "drawing at the measured width must not introduce another wrapped row");
            }

            if (resize)
            {
                var bounds = platform.Popups.Values.Single().Request.ScreenBounds;
                new Size(bounds.Width, bounds.Height).Should().Be(requested);
            }

            tip.Hide(target);
            platform.Popups.Should().BeEmpty();
        }
        finally
        {
            platform.ActualTextRenderer = previous;
        }
    }

    private sealed class ToolTipTextProbe : ILibreTextRendererService
    {
        private readonly ProGpuTextRendererService _renderer = new();
        internal List<(string Text, Rectangle Bounds, LibreTextFormat Format, Size Required)> Draws { get; } = [];

        public Size MeasureText(Graphics? graphics, string text, Font? font, Size proposedSize, LibreTextFormat format)
            => _renderer.MeasureText(graphics, text, font, proposedSize, format);

        public void DrawText(Graphics graphics, string text, Font? font, Rectangle bounds,
            Color foreColor, Color backColor, LibreTextFormat format)
        {
            Size required = _renderer.MeasureText(graphics, text, font, bounds.Size, format);
            Draws.Add((text, bounds, format, required));
            _renderer.DrawText(graphics, text, font, bounds, foreColor, backColor, format);
        }
    }
}
