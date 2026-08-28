using System;
using System.Collections;
using System.Drawing;
using System.Linq;
using Forms = System.Windows.Forms;
using FormsDesign = System.Windows.Forms.Design;
using FormsBehavior = System.Windows.Forms.Design.Behavior;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class FormsDesignerSnapLineBehaviorTests
{
    public static void Run()
    {
        SnapLineContractsMatchNativeDesignerBehavior();
        ControlDesignersPublishTypedEdgeMarginAndPaddingLines();
        PublicSnapLineOverridesRemainExtensible();
        Console.WriteLine("LibreWinForms Forms Designer snap-line contracts passed: lines=30 overrides=4.");
    }

    private static void SnapLineContractsMatchNativeDesignerBehavior()
    {
        Assert((int)FormsBehavior.SnapLineType.Top == 0
            && (int)FormsBehavior.SnapLineType.Bottom == 1
            && (int)FormsBehavior.SnapLineType.Left == 2
            && (int)FormsBehavior.SnapLineType.Right == 3
            && (int)FormsBehavior.SnapLineType.Horizontal == 4
            && (int)FormsBehavior.SnapLineType.Vertical == 5
            && (int)FormsBehavior.SnapLineType.Baseline == 6,
            "SnapLineType values changed from native WinForms.");
        Assert((int)FormsBehavior.SnapLinePriority.Low == 1
            && (int)FormsBehavior.SnapLinePriority.Medium == 2
            && (int)FormsBehavior.SnapLinePriority.High == 3
            && (int)FormsBehavior.SnapLinePriority.Always == 4,
            "SnapLinePriority values changed from native WinForms.");

        var baseline = new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Baseline, 7, "Text", FormsBehavior.SnapLinePriority.High);
        Assert(baseline.IsHorizontal && !baseline.IsVertical, "Baseline orientation changed.");
        Assert(baseline.Offset == 7 && baseline.Filter == "Text" && baseline.Priority == FormsBehavior.SnapLinePriority.High,
            "SnapLine constructor state changed.");
        baseline.AdjustOffset(-3);
        Assert(baseline.Offset == 4, "SnapLine.AdjustOffset did not mutate the public offset.");
        Assert(baseline.ToString() == "SnapLine: {type = Baseline, offset = 4, priority = High, filter = Text}",
            "SnapLine.ToString changed from the native contract.");

        var top1 = new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Top, 0);
        var top2 = new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Top, 10);
        var left = new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Left, 0);
        Assert(FormsBehavior.SnapLine.ShouldSnap(top1, top2), "Unfiltered like-type lines did not snap.");
        Assert(!FormsBehavior.SnapLine.ShouldSnap(top1, left), "Unlike snap-line types snapped.");
        Assert(!FormsBehavior.SnapLine.ShouldSnap(top1, new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Top, 2, "Text")),
            "Filtered and unfiltered lines snapped.");
        Assert(FormsBehavior.SnapLine.ShouldSnap(
            new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Baseline, 0, "Text"),
            new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Baseline, 1, "Text")),
            "Equal custom filters did not snap.");
        Assert(!FormsBehavior.SnapLine.ShouldSnap(
            new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Baseline, 0, "Text"),
            new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Baseline, 1, "Icon")),
            "Different custom filters snapped.");
        Assert(FormsBehavior.SnapLine.ShouldSnap(
            new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Vertical, 0, "Margin.Left"),
            new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Vertical, 1, "Margin.Right")),
            "Opposing horizontal margins did not snap.");
        Assert(FormsBehavior.SnapLine.ShouldSnap(
            new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Vertical, 0, "Padding.Left"),
            new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Vertical, 1, "Margin.Left")),
            "Matching parent padding and child margin did not snap.");
    }

    private static void ControlDesignersPublishTypedEdgeMarginAndPaddingLines()
    {
        using var control = new Forms.Control
        {
            Size = new Size(40, 30),
            Margin = new Forms.Padding(1, 2, 3, 4)
        };
        var designer = new FormsDesign.ControlDesigner();
        designer.Initialize(control);

        IList snapLines = designer.SnapLines;
        Assert(designer.ParticipatesWithSnapLines, "ControlDesigner stopped participating with snap lines.");
        Assert(snapLines.Count == 8, "ControlDesigner did not publish four edge and four margin lines.");
        Assert(FindLine(snapLines, FormsBehavior.SnapLineType.Top, null).Offset == 0, "Top edge offset changed.");
        Assert(FindLine(snapLines, FormsBehavior.SnapLineType.Bottom, null).Offset == 29, "Bottom edge offset changed.");
        Assert(FindLine(snapLines, FormsBehavior.SnapLineType.Left, null).Offset == 0, "Left edge offset changed.");
        Assert(FindLine(snapLines, FormsBehavior.SnapLineType.Right, null).Offset == 39, "Right edge offset changed.");
        Assert(FindLine(snapLines, FormsBehavior.SnapLineType.Horizontal, "Margin.Top").Offset == -2, "Top margin offset changed.");
        Assert(FindLine(snapLines, FormsBehavior.SnapLineType.Horizontal, "Margin.Bottom").Offset == 34, "Bottom margin offset changed.");
        Assert(FindLine(snapLines, FormsBehavior.SnapLineType.Vertical, "Margin.Left").Offset == -1, "Left margin offset changed.");
        Assert(FindLine(snapLines, FormsBehavior.SnapLineType.Vertical, "Margin.Right").Offset == 43, "Right margin offset changed.");
        Assert(snapLines.Cast<FormsBehavior.SnapLine>().Take(4).All(line => line.Priority == FormsBehavior.SnapLinePriority.Low),
            "Control edges did not retain low priority.");
        Assert(snapLines.Cast<FormsBehavior.SnapLine>().Skip(4).All(line => line.Priority == FormsBehavior.SnapLinePriority.Always),
            "Control margins did not retain always priority.");

        using var parent = new Forms.Panel
        {
            Size = new Size(100, 80),
            Padding = new Forms.Padding(5, 6, 7, 8)
        };
        var parentDesigner = new FormsDesign.ParentControlDesigner();
        parentDesigner.Initialize(parent);
        IList parentLines = parentDesigner.SnapLines;
        Assert(parentLines.Count == 12, "ParentControlDesigner did not add four parent padding lines.");
        Assert(FindLine(parentLines, FormsBehavior.SnapLineType.Vertical, "Padding.Left").Offset == 5, "Left padding offset changed.");
        Assert(FindLine(parentLines, FormsBehavior.SnapLineType.Vertical, "Padding.Right").Offset == 93, "Right padding offset changed.");
        Assert(FindLine(parentLines, FormsBehavior.SnapLineType.Horizontal, "Padding.Top").Offset == 6, "Top padding offset changed.");
        Assert(FindLine(parentLines, FormsBehavior.SnapLineType.Horizontal, "Padding.Bottom").Offset == 72, "Bottom padding offset changed.");
    }

    private static void PublicSnapLineOverridesRemainExtensible()
    {
        using var control = new Forms.Control();
        var customLine = new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Baseline, 13, "Custom", FormsBehavior.SnapLinePriority.Medium);
        var designer = new CustomSnapLineDesigner(customLine);
        designer.Initialize(control);

        Assert(!designer.ParticipatesWithSnapLines, "Custom designer participation override was ignored.");
        Assert(designer.SnapLines.Count == 1, "Custom designer snap-line override was ignored.");
        Assert(ReferenceEquals(designer.SnapLines[0], customLine), "Custom designer snap-line identity was not preserved.");
        Assert(FormsBehavior.SnapLine.ShouldSnap((FormsBehavior.SnapLine)designer.SnapLines[0]!,
            new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Baseline, 20, "Custom")),
            "Custom public snap line did not participate in ShouldSnap.");
    }

    private static FormsBehavior.SnapLine FindLine(IList lines, FormsBehavior.SnapLineType type, string? filter)
    {
        return lines.Cast<FormsBehavior.SnapLine>().Single(line => line.SnapLineType == type && line.Filter == filter);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class CustomSnapLineDesigner : FormsDesign.ControlDesigner
    {
        private readonly IList _snapLines;

        public CustomSnapLineDesigner(FormsBehavior.SnapLine snapLine)
        {
            _snapLines = new ArrayList { snapLine };
        }

        public override bool ParticipatesWithSnapLines => false;

        public override IList SnapLines => _snapLines;
    }
}
