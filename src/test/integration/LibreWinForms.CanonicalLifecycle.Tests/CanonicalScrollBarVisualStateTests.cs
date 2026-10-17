// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Fact]
    public void PortableScrollBarVisualState_ArrowHoverPressReleaseAndLeave()
    {
        RunNativeScrollBar(true, (platform, form, bar, _, _) =>
        {
            PaintableHScrollBar supplied = Assert.IsType<PaintableHScrollBar>(bar);
            Application.EnableVisualStyles();
            Assert.Equal(13, State(1, last: true));
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerMove, new(176, 8));
            Assert.Equal(14, State(1, last: true));
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, new(176, 8));
            Assert.Equal(15, State(1, last: true));
            Assert.Equal(9, State(1, last: false));
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerUp, new(176, 8));
            Assert.Equal(14, State(1, last: true));
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerMove, new(210, 150));
            Assert.Equal(13, State(1, last: true));

            int State(int part, bool last) => PaintScrollBarState(platform, supplied, part, last);
        }, static () => new PaintableHScrollBar());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableScrollBarVisualState_ThumbCaptureRetainsPressedOutside(bool cancel)
    {
        RunNativeScrollBar(true, (platform, form, bar, _, _) =>
        {
            PaintableHScrollBar supplied = Assert.IsType<PaintableHScrollBar>(bar);
            Application.EnableVisualStyles();
            bar.Value = 0;
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerMove, new(22, 8));
            Assert.Equal(2, PaintScrollBarState(platform, supplied, 2));
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, new(22, 8));
            Assert.Equal(3, PaintScrollBarState(platform, supplied, 2));
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerMove, new(210, 8));
            Assert.Equal(3, PaintScrollBarState(platform, supplied, 2));
            if (cancel) bar.Capture = false;
            else SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerUp, new(210, 8));
            Assert.Equal(1, PaintScrollBarState(platform, supplied, 2));
        }, static () => new PaintableHScrollBar());
    }

    [Fact]
    public void PortableScrollBarVisualState_DisabledAndSourceMutationCannotPaintOldPress()
    {
        RunNativeScrollBar(true, (platform, form, bar, _, _) =>
        {
            PaintableHScrollBar supplied = Assert.IsType<PaintableHScrollBar>(bar);
            Application.EnableVisualStyles();
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, new(176, 8));
            Assert.Equal(15, PaintScrollBarState(platform, supplied, 1));
            bar.SmallChange = 4;
            Assert.Equal(14, PaintScrollBarState(platform, supplied, 1));
            bar.Enabled = false;
            Assert.Equal(16, PaintScrollBarState(platform, supplied, 1));
            Assert.Equal(4, PaintScrollBarState(platform, supplied, 2));
        }, static () => new PaintableHScrollBar());
    }

    [Fact]
    public void PortableScrollBarVisualState_TrackPressedRegionStaysInsideOriginalTrack()
    {
        RunNativeScrollBar(true, (platform, form, bar, _, _) =>
        {
            PaintableHScrollBar supplied = Assert.IsType<PaintableHScrollBar>(bar);
            Application.EnableVisualStyles();
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, new(155, 8));
            Assert.Equal(3, PaintScrollBarState(platform, supplied, 4));
            Assert.Equal(1, PaintScrollBarState(platform, supplied, 2));
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerUp, new(155, 8));
            Assert.Equal(2, PaintScrollBarState(platform, supplied, 4));
        }, static () => new PaintableHScrollBar());
    }

    [Fact]
    public void PortableScrollBarVisualState_ReleaseInvalidationCannotNotifyOrRetireNestedPress()
    {
        RunNativeScrollBar(true, (platform, form, bar, _, _) =>
        {
            PaintableHScrollBar supplied = Assert.IsType<PaintableHScrollBar>(bar);
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, new(176, 8));
            bool nested = false;
            int ups = 0;
            bar.MouseUp += (_, _) => ups++;
            bar.Invalidated += (_, _) =>
            {
                if (nested) return;
                nested = true;
                SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerDown, new(3, 8));
            };
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerUp, new(176, 8));
            Assert.True(nested);
            Assert.True(bar.Capture);
            Assert.Equal(0, ups);
            Assert.Equal(50, bar.Value);
            SendScrollPointer(platform, form, bar, LibreInputEventKind.PointerUp, new(3, 8));
            Assert.Equal(1, ups);
            Assert.False(bar.Capture);
        }, static () => new PaintableHScrollBar());
    }

    private static int PaintScrollBarState(HeadlessPlatform platform, PaintableHScrollBar bar, int part, bool last = true)
    {
        using Bitmap bitmap = new(bar.Width, bar.Height);
        using Graphics graphics = Graphics.FromImage(bitmap);
        platform.ScrollBarPaintCalls = new();
        try
        {
            bar.PaintContent(graphics, bar.ClientRectangle);
            var calls = platform.ScrollBarPaintCalls.Where(call => call.Class == "SCROLLBAR" && call.Part == part);
            return (last ? calls.Last() : calls.First()).State;
        }
        finally { platform.ScrollBarPaintCalls = null; }
    }
}
