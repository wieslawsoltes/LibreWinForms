using System;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class ScrollableControlBehaviorTests
{
    public static void Run()
    {
        ScrollMetricsClampAndPublishTypedState();
        DisplayRectangleDeflatesPaddingAfterScrolling();
        ScrolledCoordinatesFollowTheDisplayedChildTree();
        ScaleUpdatesBoundsAndDescendants();
        ClassCanvasEditingCoordinatesStayAlignedWithScrollAndZoom();
        Console.WriteLine(
            "LibreWinForms scroll/scale tests passed: metrics=1 padding=1 coordinates=1 recursiveScale=1 classCanvasEditing=1.");
    }

    private static void DisplayRectangleDeflatesPaddingAfterScrolling()
    {
        var panel = new Forms.Panel
        {
            AutoScroll = true,
            Padding = new Forms.Padding(5, 6, 7, 8),
            Size = new Size(100, 80)
        };
        panel.Controls.Add(new Forms.Control
        {
            Bounds = new Rectangle(80, 70, 120, 90)
        });

        panel.HorizontalScroll.Value = 30;
        panel.VerticalScroll.Value = 25;

        Assert(panel.DisplayRectangle == new Rectangle(-25, -19, 188, 146),
            "ScrollableControl.DisplayRectangle did not deflate the scrolled extent by Padding.");
    }

    private static void ScrollMetricsClampAndPublishTypedState()
    {
        var panel = new Forms.Panel
        {
            AutoScroll = true,
            Size = new Size(100, 80)
        };
        var canvas = new Forms.PictureBox
        {
            Bounds = new Rectangle(80, 70, 120, 90)
        };
        panel.Controls.Add(canvas);

        Assert(panel.HorizontalScroll.Visible, "Horizontal scrolling was not enabled for wide content.");
        Assert(panel.VerticalScroll.Visible, "Vertical scrolling was not enabled for tall content.");
        Assert(panel.HorizontalScroll.Maximum == 199 && panel.HorizontalScroll.LargeChange == 100,
            "Horizontal metrics do not describe the client/content extent.");
        Assert(panel.VerticalScroll.Maximum == 159 && panel.VerticalScroll.LargeChange == 80,
            "Vertical metrics do not describe the client/content extent.");

        int scrollEvents = 0;
        panel.Scroll += (_, _) => scrollEvents++;
        panel.HorizontalScroll.Value = 500;
        panel.VerticalScroll.Value = 500;
        Assert(panel.HorizontalScroll.Value == 100 && panel.VerticalScroll.Value == 80,
            "Scroll values were not clamped to extent minus viewport.");
        Assert(panel.AutoScrollPosition == new Point(-100, -80),
            "AutoScrollPosition did not expose the negative display offset.");
        Assert(panel.DisplayRectangle == new Rectangle(-100, -80, 200, 160),
            "DisplayRectangle did not preserve the logical content extent.");
        Assert(scrollEvents == 2, "Typed scroll changes did not publish exactly one event per axis.");

        panel.AutoScroll = false;
        Assert(panel.HorizontalScroll.Value == 0
            && panel.VerticalScroll.Value == 0
            && !panel.HorizontalScroll.Visible
            && !panel.VerticalScroll.Visible,
            "Disabling AutoScroll retained stale offsets or visibility.");
    }

    private static void ScrolledCoordinatesFollowTheDisplayedChildTree()
    {
        var root = new Forms.Panel
        {
            AutoScroll = true,
            Location = new Point(10, 20),
            Size = new Size(100, 80)
        };
        var child = new Forms.Control
        {
            Bounds = new Rectangle(80, 70, 120, 90)
        };
        root.Controls.Add(child);
        root.HorizontalScroll.Value = 30;
        root.VerticalScroll.Value = 25;

        Point screen = child.PointToScreen(new Point(4, 5));
        Assert(screen == new Point(64, 70),
            "PointToScreen ignored the scrollable parent's displayed child offset.");
        Assert(child.PointToClient(screen) == new Point(4, 5),
            "Scrolled PointToClient did not round-trip the displayed child position.");
        Assert(child.Location == new Point(80, 70),
            "Scrolling mutated the child's logical layout coordinates.");
    }

    private static void ScaleUpdatesBoundsAndDescendants()
    {
        var root = new ScaleProbeControl
        {
            Bounds = new Rectangle(3, 5, 100, 80)
        };
        var child = new Forms.Control
        {
            Bounds = new Rectangle(7, 9, 20, 14)
        };
        root.Controls.Add(child);

        root.Scale(new SizeF(1.5f, 2f));
        Assert(root.Bounds == new Rectangle(5, 10, 150, 160),
            "Control.Scale did not scale the owner bounds with deterministic rounding.");
        Assert(child.Bounds == new Rectangle(11, 18, 30, 28),
            "Control.Scale did not recursively scale child layout.");

        root.ScaleSelected(new SizeF(2f, 0.5f), Forms.BoundsSpecified.Size);
        Assert(root.Bounds == new Rectangle(5, 10, 300, 80),
            "ScaleControl ignored BoundsSpecified when scaling size only.");

        bool rejected = false;
        try
        {
            root.Scale(new SizeF(float.NaN, 1f));
        }
        catch (ArgumentOutOfRangeException)
        {
            rejected = true;
        }

        Assert(rejected, "Control.Scale accepted a non-finite scale factor.");
    }

    private static void ClassCanvasEditingCoordinatesStayAlignedWithScrollAndZoom()
    {
        var panel = new Forms.Panel
        {
            AutoScroll = true,
            Size = new Size(240, 160)
        };
        var pictureBox = new Forms.PictureBox
        {
            Size = new Size(640, 480)
        };
        panel.Controls.Add(pictureBox);
        panel.HorizontalScroll.Value = 75;
        panel.VerticalScroll.Value = 45;

        var editor = new Forms.TextBox
        {
            Bounds = new Rectangle(90, 70, 120, 24)
        };
        editor.Scale(new SizeF(1.5f, 1.5f));
        editor.Top -= panel.VerticalScroll.Value;
        editor.Left -= panel.HorizontalScroll.Value;
        panel.Controls.Add(editor);
        panel.Controls.SetChildIndex(editor, 0);

        Assert(editor.Bounds == new Rectangle(60, 60, 180, 36),
            "The ClassCanvas zoom/scroll editing sequence produced incorrect bounds.");
        Assert(ReferenceEquals(panel.Controls[0], editor),
            "The ClassCanvas editing control did not retain front-most child order.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class ScaleProbeControl : Forms.Control
    {
        public void ScaleSelected(SizeF factor, Forms.BoundsSpecified specified)
        {
            ScaleControl(factor, specified);
        }
    }
}
