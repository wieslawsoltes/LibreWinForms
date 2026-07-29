using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing;
using System.Drawing.Design;
using System.IO;
using Forms = System.Windows.Forms;
using FormsDesign = System.Windows.Forms.Design;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class FormsDesignerLayoutBehaviorTests
{
    public static void Run()
    {
        SharpDevelopOptionsDriveGridMoveAndMidpointRounding();
        GridResizeSnapsOnlyActiveEdgesAndKeepsOneCell();
        GridToolboxPlacementUsesSnappedBounds();
        SnapLinesTakePrecedenceAndIncludePaddingMargins();
        SnapLineSelectionIsDeterministicPerAxis();
        SnapLineAdornersUpdateClearAndCommit();
        SnapLineResizeAndToolPlacementAdornersStayLive();
        SnapLineAdornersCancelWithDesignerDisposal();
        AltBypassesSnapLinesButKeepsGridActive();
        ScrolledNestedToolboxCoordinatesUseTypedConversion();
        TransactionsChangesAndUndoCountsStayExact();
        GroupMovePreservesGeometryOrderAndOneUndoUnit();
        GroupBoundsDriveSnapLinesAndAdorners();
        GroupMoveHonorsAltGridAndMemberExclusion();
        GroupMoveFiltersLockedReadOnlyAndDifferentParentControls();
        ReadOnlyPrimaryRejectsGroupMove();
        GroupMoveCancellationRestoresEveryControl();
        KeyboardCommandsRegisterAndTrackEligibility();
        KeyboardMoveUsesGridAndPixelStepsAtomically();
        KeyboardSnapLineModeUsesBoundedPixelSteps();
        KeyboardMoveFiltersSelectionWithoutReordering();
        KeyboardSizeUsesGridAndPixelStepsAtomically();
        KeyboardSizeFiltersSelectionAndMoveFailureRollsBack();
        LayoutServiceSourceStaysReflectionFree();
        PaintSurfaceRetirementStaysFrameBounded();
        RenderResourcesStayBoundedAndReusable();
        HostedLayoutAllocationsStayMeasured();
        Console.WriteLine(
            "LibreWinForms Forms Designer layout tests passed: grid=12 toolbox=2 snap=9 adorners=18 alt=4 coordinates=1 transactions=16 group=30 keyboard=89 sourceGuard=39 surfaceRetirement=12 renderResources=14 layoutAllocation=4.");
    }

    private static void SharpDevelopOptionsDriveGridMoveAndMidpointRounding()
    {
        using var fixture = new DesignerFixture(new SharpStyleDesignerOptionService(
            new Size(8, 8),
            snapToGrid: true,
            useSnapLines: false));
        Assert(ReferenceEquals(fixture.Host.GetService(typeof(DesignerOptionService)), fixture.Options),
            "SharpDevelop-shaped DesignerOptionService registration was not retained by the host.");

        Forms.Button moved = fixture.AddButton("moved", new Rectangle(13, 17, 31, 25));
        Drag(moved, new Point(15, 12), new Point(21, 19));
        Assert(moved.Bounds == new Rectangle(16, 24, 31, 25),
            "Grid move changed size or failed to snap only the location.");

        Forms.Button midpoint = fixture.AddButton("midpoint", new Rectangle(8, 8, 24, 24));
        Drag(midpoint, new Point(12, 12), new Point(16, 18));
        Assert(midpoint.Location == new Point(8, 16),
            "Exact grid midpoint did not use the native strict-lower rounding rule.");
    }

    private static void GridResizeSnapsOnlyActiveEdgesAndKeepsOneCell()
    {
        using var fixture = new DesignerFixture(new SharpStyleDesignerOptionService(
            new Size(10, 10),
            snapToGrid: true,
            useSnapLines: false));

        Forms.Button left = fixture.AddButton("left", new Rectangle(20, 20, 40, 40));
        fixture.Select(left);
        Drag(left, new Point(0, 20), new Point(7, 20));
        Assert(left.Bounds == new Rectangle(30, 20, 30, 40),
            "Left resize moved the fixed right edge or snapped the inactive axis.");

        Forms.Button right = fixture.AddButton("right", new Rectangle(100, 20, 40, 40));
        fixture.Select(right);
        Drag(right, new Point(40, 20), new Point(47, 20));
        Assert(right.Bounds == new Rectangle(100, 20, 50, 40),
            "Right resize moved the fixed left edge or snapped the inactive axis.");

        Forms.Button top = fixture.AddButton("top", new Rectangle(20, 100, 40, 40));
        fixture.Select(top);
        Drag(top, new Point(20, 0), new Point(20, 7));
        Assert(top.Bounds == new Rectangle(20, 110, 40, 30),
            "Top resize moved the fixed bottom edge or snapped the inactive axis.");

        Forms.Button bottom = fixture.AddButton("bottom", new Rectangle(100, 100, 40, 40));
        fixture.Select(bottom);
        Drag(bottom, new Point(20, 40), new Point(20, 47));
        Assert(bottom.Bounds == new Rectangle(100, 100, 40, 50),
            "Bottom resize moved the fixed top edge or snapped the inactive axis.");

        Forms.Button minimum = fixture.AddButton("minimum", new Rectangle(200, 20, 20, 20));
        minimum.MinimumSize = new Size(4, 4);
        fixture.Select(minimum);
        Drag(minimum, new Point(0, 10), new Point(19, 10));
        Assert(minimum.Bounds == new Rectangle(210, 20, 10, 20),
            "Grid resize shrank below one grid cell or moved the opposite edge.");
    }

    private static void GridToolboxPlacementUsesSnappedBounds()
    {
        using var fixture = new DesignerFixture(new SharpStyleDesignerOptionService(
            new Size(10, 10),
            snapToGrid: true,
            useSnapLines: false));
        var toolbox = new ProbeToolboxService();
        ((IServiceContainer)fixture.Host).AddService(typeof(IToolboxService), toolbox);
        toolbox.SetSelectedToolboxItem(new ToolboxItem(typeof(Forms.Button)));

        Drag(fixture.Root, new Point(13, 17), new Point(44, 42));

        Forms.Button created = fixture.FindOnlyButton();
        Assert(created.Bounds == new Rectangle(10, 20, 30, 20),
            "Toolbox drag did not snap both placement edges to the configured grid.");
        Assert(toolbox.SelectedToolboxItemUsedCount == 1,
            "Snapped toolbox placement did not consume the selected item exactly once.");
    }

    private static void SnapLinesTakePrecedenceAndIncludePaddingMargins()
    {
        using (var fixture = new DesignerFixture(new SharpStyleDesignerOptionService(
            new Size(10, 10),
            snapToGrid: true,
            useSnapLines: true)))
        {
            fixture.AddButton("target", new Rectangle(50, 30, 20, 20));
            Forms.Button candidate = fixture.AddButton("candidate", new Rectangle(20, 30, 20, 20));
            Drag(candidate, new Point(10, 10), new Point(33, 10));
            Assert(candidate.Left == 50,
                "UseSnapLines did not take precedence over the simultaneously enabled 10-pixel grid.");
        }

        AssertPaddingTolerance(deltaX: 0, expectedLeft: 13);
        AssertPaddingTolerance(deltaX: 8, expectedLeft: 13);
        AssertPaddingTolerance(deltaX: 9, expectedLeft: 22);
    }

    private static void AssertPaddingTolerance(int deltaX, int expectedLeft)
    {
        using var fixture = new DesignerFixture(new SharpStyleDesignerOptionService(
            new Size(10, 10),
            snapToGrid: false,
            useSnapLines: true));
        fixture.Root.Padding = new Forms.Padding(10);
        Forms.Button candidate = fixture.AddButton("candidate", new Rectangle(13, 30, 20, 20));
        candidate.Margin = new Forms.Padding(3, 0, 0, 0);

        Drag(candidate, new Point(10, 10), new Point(10 + deltaX, 22));
        Assert(candidate.Left == expectedLeft,
            $"Parent padding / child margin snap tolerance failed for delta {deltaX}.");
    }

    private static void SnapLineSelectionIsDeterministicPerAxis()
    {
        using (var fixture = CreateSnapLineFixture())
        {
            fixture.AddButton("lowEdge", new Rectangle(25, 100, 20, 20));
            fixture.AddButton("alwaysMargin", new Rectangle(15, 100, 20, 20));
            Forms.Button candidate = fixture.AddButton("candidate", new Rectangle(10, 30, 20, 20));
            Drag(candidate, new Point(10, 10), new Point(30, 10));
            Assert(candidate.Left == 35,
                "Equal-distance snap lines did not prefer the higher-priority margin match.");
        }

        using (var fixture = CreateSnapLineFixture())
        {
            fixture.AddButton("first", new Rectangle(25, 100, 20, 20));
            fixture.AddButton("second", new Rectangle(35, 100, 20, 20));
            Forms.Button candidate = fixture.AddButton("candidate", new Rectangle(10, 30, 20, 20));
            Drag(candidate, new Point(10, 10), new Point(30, 10));
            Assert(candidate.Left == 25,
                "Equal-distance/equal-priority snap lines did not preserve stable control order.");
        }

        using (var fixture = CreateSnapLineFixture())
        {
            fixture.AddButton("xTarget", new Rectangle(25, 100, 20, 20));
            fixture.AddButton("yTarget", new Rectangle(100, 35, 20, 20));
            Forms.Button candidate = fixture.AddButton("candidate", new Rectangle(10, 10, 20, 20));
            Drag(candidate, new Point(10, 10), new Point(30, 30));
            Assert(candidate.Location == new Point(25, 35),
                "Horizontal and vertical snap-line choices were not resolved independently.");
        }
    }

    private static DesignerFixture CreateSnapLineFixture()
    {
        return new DesignerFixture(new SharpStyleDesignerOptionService(
            new Size(10, 10),
            snapToGrid: false,
            useSnapLines: true));
    }

    private static void SnapLineAdornersUpdateClearAndCommit()
    {
        using var fixture = new DesignerFixture(new SharpStyleDesignerOptionService(
            new Size(10, 10),
            snapToGrid: true,
            useSnapLines: true));
        fixture.AddButton("target", new Rectangle(50, 30, 20, 20));
        Forms.Button candidate = fixture.AddButton("candidate", new Rectangle(20, 30, 20, 20));
        var adorners = (Forms.IPortableWinFormsAdornerSource)fixture.Root;

        BeginDrag(candidate, new Point(10, 10), new Point(33, 10));
        Assert(candidate.Left == 50 && adorners.SupportsPortableAdornments,
            "Snapped move did not activate the typed parent adorner source.");
        Assert(PaintAdornerCommandCount(adorners, fixture.Root.ClientSize) == 2,
            "Snapped move did not paint one matched line per axis.");
        long initialVersion = adorners.PortableAdornerVersion;

        candidate.RaiseMouseMove(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 0, 100, 10, 0));
        Assert(candidate.Left != 50,
            "Second move did not leave the stale horizontal-axis snap target.");
        Assert(PaintAdornerCommandCount(adorners, fixture.Root.ClientSize) == 1
            && adorners.PortableAdornerVersion > initialVersion,
            "Changing snap matches did not remove the stale line and invalidate the adorner source.");

        Forms.Keys previousModifiers = Forms.Control.ModifierKeys;
        try
        {
            Forms.Control.ModifierKeys = Forms.Keys.Alt;
            candidate.RaiseMouseMove(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 0, 100, 10, 0));
            Assert(PaintAdornerCommandCount(adorners, fixture.Root.ClientSize) == 0,
                "Alt bypass retained stale snap-line feedback.");
        }
        finally
        {
            Forms.Control.ModifierKeys = previousModifiers;
        }

        candidate.RaiseMouseUp(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, 100, 10, 0));
        Assert(!adorners.SupportsPortableAdornments
            && PaintAdornerCommandCount(adorners, fixture.Root.ClientSize) == 0,
            "Committed manipulation did not detach and clear the typed adorner source.");
    }

    private static void SnapLineResizeAndToolPlacementAdornersStayLive()
    {
        using (var fixture = CreateSnapLineFixture())
        {
            fixture.AddButton("target", new Rectangle(50, 30, 20, 20));
            Forms.Button candidate = fixture.AddButton("candidate", new Rectangle(20, 30, 20, 20));
            fixture.Select(candidate);
            var adorners = (Forms.IPortableWinFormsAdornerSource)fixture.Root;

            BeginDrag(candidate, new Point(candidate.Width, 10), new Point(45, 10));
            Assert(candidate.Bounds == new Rectangle(20, 30, 50, 20),
                "Right-edge resize did not snap to the target edge.");
            Assert(adorners.SupportsPortableAdornments
                && PaintAdornerCommandCount(adorners, fixture.Root.ClientSize) == 1,
                "Resize did not retain the active vertical snap-line adorner.");
            candidate.RaiseMouseUp(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, 45, 10, 0));
            Assert(!adorners.SupportsPortableAdornments,
                "Committed resize did not detach its parent adorner source.");
        }

        using (var fixture = CreateSnapLineFixture())
        {
            fixture.AddButton("target", new Rectangle(50, 30, 20, 20));
            var toolbox = new ProbeToolboxService();
            ((IServiceContainer)fixture.Host).AddService(typeof(IToolboxService), toolbox);
            toolbox.SetSelectedToolboxItem(new ToolboxItem(typeof(Forms.Button)));
            var adorners = (Forms.IPortableWinFormsAdornerSource)fixture.Root;

            BeginDrag(fixture.Root, new Point(43, 30), new Point(80, 60));
            Assert(adorners.SupportsPortableAdornments
                && PaintAdornerCommandCount(adorners, fixture.Root.ClientSize) == 2,
                "Toolbox drag did not publish its matched placement adorners.");
            fixture.Root.RaiseMouseUp(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, 80, 60, 0));
            Assert(!adorners.SupportsPortableAdornments
                && toolbox.SelectedToolboxItemUsedCount == 1,
                "Toolbox commit did not clear adorners and consume the selected tool.");
        }
    }

    private static void SnapLineAdornersCancelWithDesignerDisposal()
    {
        var fixture = CreateSnapLineFixture();
        fixture.AddButton("target", new Rectangle(50, 30, 20, 20));
        Forms.Button candidate = fixture.AddButton("candidate", new Rectangle(20, 30, 20, 20));
        var adorners = (Forms.IPortableWinFormsAdornerSource)fixture.Root;

        BeginDrag(candidate, new Point(10, 10), new Point(33, 10));
        Assert(adorners.SupportsPortableAdornments && candidate.Left == 50,
            "Cancellation fixture did not enter an active snapped manipulation.");
        fixture.Dispose();
        Assert(!adorners.SupportsPortableAdornments
            && PaintAdornerCommandCount(adorners, fixture.Root.ClientSize) == 0,
            "Designer disposal did not cancel and detach the active adorner source.");
    }

    private static void AltBypassesSnapLinesButKeepsGridActive()
    {
        using var fixture = new DesignerFixture(new SharpStyleDesignerOptionService(
            new Size(10, 10),
            snapToGrid: true,
            useSnapLines: true));
        fixture.AddButton("target", new Rectangle(50, 30, 20, 20));
        Forms.Button candidate = fixture.AddButton("candidate", new Rectangle(20, 30, 20, 20));

        Forms.Keys previousModifiers = Forms.Control.ModifierKeys;
        try
        {
            Forms.Control.ModifierKeys = Forms.Keys.Alt;
            Drag(candidate, new Point(10, 10), new Point(33, 10));
        }
        finally
        {
            Forms.Control.ModifierKeys = previousModifiers;
        }

        Assert(candidate.Left == 40,
            "Alt did not bypass snap-line adjustment while retaining the enabled grid.");

        candidate.Location = new Point(20, 30);
        Drag(candidate, new Point(10, 10), new Point(33, 10));
        Assert(candidate.Left == 50,
            "Ending the Alt-bypassed manipulation retained stale snap state.");
    }

    private static void ScrolledNestedToolboxCoordinatesUseTypedConversion()
    {
        using var fixture = new DesignerFixture(new SharpStyleDesignerOptionService(
            new Size(8, 8),
            snapToGrid: false,
            useSnapLines: false));
        var nested = (Forms.Panel)fixture.Host.CreateComponent(typeof(Forms.Panel), "nested");
        nested.Bounds = new Rectangle(20, 20, 100, 80);
        nested.AutoScroll = true;
        fixture.Root.Controls.Add(nested);
        var source = (Forms.Control)fixture.Host.CreateComponent(typeof(Forms.Control), "source");
        source.Bounds = new Rectangle(80, 70, 120, 90);
        nested.Controls.Add(source);
        nested.HorizontalScroll.Value = 30;
        nested.VerticalScroll.Value = 25;

        var toolbox = new ProbeToolboxService();
        ((IServiceContainer)fixture.Host).AddService(typeof(IToolboxService), toolbox);
        toolbox.SetSelectedToolboxItem(new ToolboxItem(typeof(Forms.Button)));
        Drag(source, new Point(5, 5), new Point(15, 15));

        Forms.Button created = fixture.FindOnlyButton();
        Assert(ReferenceEquals(created.Parent, nested) && created.Bounds == new Rectangle(55, 50, 10, 10),
            "Nested scrolled toolbox placement did not use target.PointToClient(source.PointToScreen(point)).");
    }

    private static void TransactionsChangesAndUndoCountsStayExact()
    {
        using var fixture = new DesignerFixture(new SharpStyleDesignerOptionService(
            new Size(8, 8),
            snapToGrid: true,
            useSnapLines: false));
        Forms.Button candidate = fixture.AddButton("candidate", new Rectangle(16, 16, 32, 24));
        using var undo = new ProbeUndoEngine(fixture.Host);
        var changes = (IComponentChangeService)fixture.Host.GetService(typeof(IComponentChangeService))!;
        int opened = 0;
        int closed = 0;
        int locationChanging = 0;
        int locationChanged = 0;
        int sizeChanging = 0;
        int sizeChanged = 0;
        fixture.Host.TransactionOpened += (_, _) => opened++;
        fixture.Host.TransactionClosed += (_, _) => closed++;
        changes.ComponentChanging += (_, e) => CountChange(e.Component, e.Member, candidate, ref locationChanging, ref sizeChanging);
        changes.ComponentChanged += (_, e) => CountChange(e.Component, e.Member, candidate, ref locationChanged, ref sizeChanged);

        Drag(candidate, new Point(16, 12), new Point(26, 22));
        fixture.Select(candidate);
        Drag(candidate, new Point(candidate.Width, candidate.Height), new Point(candidate.Width + 10, candidate.Height + 10));

        Assert(candidate.Bounds == new Rectangle(24, 24, 40, 32),
            "Grid manipulation produced unexpected final bounds before undo.");
        Assert(opened == 2 && closed == 2
            && locationChanging == 1 && locationChanged == 1
            && sizeChanging == 1 && sizeChanged == 1
            && undo.UndoCount == 2,
            "Grid manipulation changed the established transaction/change/undo cardinality.");
        Assert(undo.UndoOnce() && candidate.Bounds == new Rectangle(24, 24, 32, 24),
            "Resize undo did not restore the snapped pre-resize bounds.");
        Assert(undo.UndoOnce() && candidate.Bounds == new Rectangle(16, 16, 32, 24),
            "Move undo did not restore the original bounds.");
        Assert(undo.RedoOnce() && undo.RedoOnce() && candidate.Bounds == new Rectangle(24, 24, 40, 32),
            "Move/resize redo did not restore the snapped final bounds.");
    }

    private static void GroupMovePreservesGeometryOrderAndOneUndoUnit()
    {
        using var fixture = new DesignerFixture(new SharpStyleDesignerOptionService(
            new Size(10, 10),
            snapToGrid: true,
            useSnapLines: false));
        Forms.Button primary = fixture.AddButton("primary", new Rectangle(13, 17, 20, 20));
        Forms.Button sibling = fixture.AddButton("sibling", new Rectangle(43, 37, 30, 20));
        Forms.Button stationary = fixture.AddButton("stationary", new Rectangle(140, 80, 20, 20));
        int primaryIndex = fixture.Root.Controls.IndexOf(primary);
        int siblingIndex = fixture.Root.Controls.IndexOf(sibling);
        int stationaryIndex = fixture.Root.Controls.IndexOf(stationary);
        fixture.Select(primary, sibling);

        using var undo = new ProbeUndoEngine(fixture.Host);
        var changes = (IComponentChangeService)fixture.Host.GetService(typeof(IComponentChangeService))!;
        int opened = 0;
        int closed = 0;
        int changing = 0;
        int changed = 0;
        string description = string.Empty;
        fixture.Host.TransactionOpened += (_, _) =>
        {
            opened++;
            description = fixture.Host.TransactionDescription;
        };
        fixture.Host.TransactionClosed += (_, _) => closed++;
        changes.ComponentChanging += (_, e) => changing += IsLocationChangeFor(e.Component, e.Member, primary, sibling) ? 1 : 0;
        changes.ComponentChanged += (_, e) => changed += IsLocationChangeFor(e.Component, e.Member, primary, sibling) ? 1 : 0;

        Drag(primary, new Point(10, 10), new Point(24, 24));

        var selection = (ISelectionService)fixture.Host.GetService(typeof(ISelectionService))!;
        Assert(primary.Bounds == new Rectangle(30, 30, 20, 20)
            && sibling.Bounds == new Rectangle(60, 50, 30, 20)
            && stationary.Bounds == new Rectangle(140, 80, 20, 20),
            "Group grid move did not apply one snapped delta while preserving relative geometry.");
        Assert(fixture.Root.Controls.IndexOf(primary) == primaryIndex
            && fixture.Root.Controls.IndexOf(sibling) == siblingIndex
            && fixture.Root.Controls.IndexOf(stationary) == stationaryIndex,
            "Group move changed sibling/z-order in the parent control collection.");
        Assert(selection.SelectionCount == 2
            && ReferenceEquals(selection.PrimarySelection, primary)
            && selection.GetComponentSelected(sibling),
            "Dragging the primary control did not preserve the existing multi-selection.");
        Assert(opened == 1 && closed == 1 && changing == 2 && changed == 2
            && description == "Move 2 controls" && undo.UndoCount == 1,
            "Group move did not produce one typed transaction and one undo unit.");
        Assert(undo.UndoOnce()
            && primary.Bounds == new Rectangle(13, 17, 20, 20)
            && sibling.Bounds == new Rectangle(43, 37, 30, 20),
            "Group undo did not restore every initial bound atomically.");
        Assert(undo.RedoOnce()
            && primary.Bounds == new Rectangle(30, 30, 20, 20)
            && sibling.Bounds == new Rectangle(60, 50, 30, 20),
            "Group redo did not restore the common snapped delta atomically.");
    }

    private static void GroupBoundsDriveSnapLinesAndAdorners()
    {
        using var fixture = CreateSnapLineFixture();
        Forms.Button target = fixture.AddButton("target", new Rectangle(100, 100, 20, 20));
        Forms.Button primary = fixture.AddButton("primary", new Rectangle(20, 30, 20, 20));
        Forms.Button sibling = fixture.AddButton("sibling", new Rectangle(50, 30, 30, 20));
        fixture.Select(primary, sibling);
        var adorners = (Forms.IPortableWinFormsAdornerSource)fixture.Root;

        BeginDrag(primary, new Point(10, 10), new Point(45, 10));

        Rectangle movedGroupBounds = Rectangle.Union(primary.Bounds, sibling.Bounds);
        Assert(primary.Location == new Point(60, 30)
            && sibling.Location == new Point(90, 30)
            && movedGroupBounds.Right == target.Right,
            "Snap-line move did not align the selected group bounds to the external target.");
        Assert(adorners.SupportsPortableAdornments
            && PaintAdornerCommandCount(adorners, fixture.Root.ClientSize) == 1,
            "Group-bound snap did not publish the one matched external guide.");

        primary.RaiseMouseUp(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, 45, 10, 0));
        Assert(!adorners.SupportsPortableAdornments,
            "Committed group move did not clear its transient snap-line adorner.");
    }

    private static void GroupMoveHonorsAltGridAndMemberExclusion()
    {
        using (var fixture = CreateSnapLineFixture())
        {
            Forms.Button primary = fixture.AddButton("primary", new Rectangle(20, 20, 20, 20));
            Forms.Button sibling = fixture.AddButton("sibling", new Rectangle(50, 20, 20, 20));
            fixture.Select(primary, sibling);

            Drag(primary, new Point(10, 10), new Point(15, 10));
            Assert(primary.Location == new Point(25, 20) && sibling.Location == new Point(55, 20),
                "Selected group members were incorrectly retained as their own snap-line targets.");
        }

        using (var fixture = new DesignerFixture(new SharpStyleDesignerOptionService(
            new Size(10, 10),
            snapToGrid: true,
            useSnapLines: true)))
        {
            Forms.Button primary = fixture.AddButton("primary", new Rectangle(20, 30, 20, 20));
            Forms.Button sibling = fixture.AddButton("sibling", new Rectangle(50, 30, 20, 20));
            fixture.AddButton("target", new Rectangle(100, 100, 20, 20));
            fixture.Select(primary, sibling);

            Forms.Keys previousModifiers = Forms.Control.ModifierKeys;
            try
            {
                Forms.Control.ModifierKeys = Forms.Keys.Alt;
                Drag(primary, new Point(10, 10), new Point(23, 10));
            }
            finally
            {
                Forms.Control.ModifierKeys = previousModifiers;
            }

            Assert(primary.Location == new Point(30, 30) && sibling.Location == new Point(60, 30),
                "Alt did not bypass group snap lines while retaining group-grid snapping.");
        }
    }

    private static void GroupMoveFiltersLockedReadOnlyAndDifferentParentControls()
    {
        using var fixture = new DesignerFixture(new SharpStyleDesignerOptionService(
            new Size(8, 8),
            snapToGrid: false,
            useSnapLines: false));
        Forms.Button primary = fixture.AddButton("primary", new Rectangle(20, 20, 20, 20));
        LockedButton locked = fixture.AddControl(
            "locked",
            new LockedButton { Locked = true },
            new Rectangle(50, 20, 20, 20));
        ReadOnlyLocationButton readOnly = fixture.AddControl(
            "readOnly",
            new ReadOnlyLocationButton(),
            new Rectangle(80, 20, 20, 20));
        Forms.Panel nested = fixture.AddControl(
            "nested",
            new Forms.Panel(),
            new Rectangle(20, 100, 120, 80));
        Forms.Button differentParent = fixture.AddButton(
            "differentParent",
            new Rectangle(10, 10, 20, 20),
            nested);
        fixture.Select(primary, locked, readOnly, differentParent);

        int locationChanging = 0;
        int locationChanged = 0;
        var changes = (IComponentChangeService)fixture.Host.GetService(typeof(IComponentChangeService))!;
        changes.ComponentChanging += (_, e) => locationChanging += IsLocationChangeFor(e.Component, e.Member, primary) ? 1 : 0;
        changes.ComponentChanged += (_, e) => locationChanged += IsLocationChangeFor(e.Component, e.Member, primary) ? 1 : 0;

        Drag(primary, new Point(10, 10), new Point(25, 20));

        Assert(primary.Location == new Point(35, 30),
            "Eligible primary control did not receive the raw group delta.");
        Assert(locked.Location == new Point(50, 20)
            && readOnly.Location == new Point(80, 20)
            && differentParent.Location == new Point(10, 10),
            "Group move changed a locked, read-only, or different-parent selection member.");
        Assert(locationChanging == 1 && locationChanged == 1,
            "Filtered selection members emitted designer location changes.");
    }

    private static void ReadOnlyPrimaryRejectsGroupMove()
    {
        using var fixture = new DesignerFixture(new SharpStyleDesignerOptionService(
            new Size(8, 8),
            snapToGrid: false,
            useSnapLines: false));
        ReadOnlyLocationButton primary = fixture.AddControl(
            "readOnlyPrimary",
            new ReadOnlyLocationButton(),
            new Rectangle(20, 20, 20, 20));
        Forms.Button sibling = fixture.AddButton("sibling", new Rectangle(50, 20, 20, 20));
        fixture.Select(primary, sibling);
        int opened = 0;
        fixture.Host.TransactionOpened += (_, _) => opened++;

        Drag(primary, new Point(10, 10), new Point(30, 20));

        Assert(primary.Location == new Point(20, 20)
            && sibling.Location == new Point(50, 20)
            && opened == 0,
            "A read-only primary control initiated a partial group move.");
    }

    private static void GroupMoveCancellationRestoresEveryControl()
    {
        var fixture = new DesignerFixture(new SharpStyleDesignerOptionService(
            new Size(8, 8),
            snapToGrid: false,
            useSnapLines: false));
        Forms.Button primary = fixture.AddButton("primary", new Rectangle(20, 20, 20, 20));
        Forms.Button sibling = fixture.AddButton("sibling", new Rectangle(50, 30, 20, 20));
        fixture.Select(primary, sibling);
        using var undo = new ProbeUndoEngine(fixture.Host);
        int cancelled = 0;
        fixture.Host.TransactionClosed += (_, e) => cancelled += e.TransactionCommitted ? 0 : 1;

        BeginDrag(primary, new Point(10, 10), new Point(30, 25));
        Assert(primary.Location == new Point(40, 35) && sibling.Location == new Point(70, 45),
            "Cancellation fixture did not enter an active group move.");
        fixture.Dispose();

        Assert(primary.Bounds == new Rectangle(20, 20, 20, 20)
            && sibling.Bounds == new Rectangle(50, 30, 20, 20)
            && cancelled == 1
            && undo.UndoCount == 0,
            "Designer disposal did not cancel and restore the complete group transaction.");
    }

    private static void KeyboardCommandsRegisterAndTrackEligibility()
    {
        using var fixture = new DesignerFixture(new SharpStyleDesignerOptionService(
            new Size(8, 8),
            snapToGrid: true,
            useSnapLines: false));
        var commands = (IMenuCommandService)fixture.Host.GetService(typeof(IMenuCommandService))!;
        CommandID[] movementCommands =
        {
            FormsDesign.MenuCommands.KeyMoveUp,
            FormsDesign.MenuCommands.KeyMoveDown,
            FormsDesign.MenuCommands.KeyMoveLeft,
            FormsDesign.MenuCommands.KeyMoveRight,
            FormsDesign.MenuCommands.KeyNudgeUp,
            FormsDesign.MenuCommands.KeyNudgeDown,
            FormsDesign.MenuCommands.KeyNudgeLeft,
            FormsDesign.MenuCommands.KeyNudgeRight
        };
        CommandID[] sizeCommands =
        {
            FormsDesign.MenuCommands.KeySizeWidthIncrease,
            FormsDesign.MenuCommands.KeySizeWidthDecrease,
            FormsDesign.MenuCommands.KeySizeHeightIncrease,
            FormsDesign.MenuCommands.KeySizeHeightDecrease,
            FormsDesign.MenuCommands.KeyNudgeWidthIncrease,
            FormsDesign.MenuCommands.KeyNudgeWidthDecrease,
            FormsDesign.MenuCommands.KeyNudgeHeightIncrease,
            FormsDesign.MenuCommands.KeyNudgeHeightDecrease
        };
        AssertCommandsEnabled(commands, movementCommands, expected: false,
            "Root/no-selection state enabled keyboard movement commands.");
        AssertCommandsEnabled(commands, sizeCommands, expected: false,
            "Root/no-selection state enabled keyboard size commands.");

        Forms.Button eligible = fixture.AddButton("eligible", new Rectangle(16, 16, 24, 24));
        fixture.Select(eligible);
        AssertCommandsEnabled(commands, movementCommands, expected: true,
            "Eligible selection did not enable every standard move/nudge command.");
        AssertCommandsEnabled(commands, sizeCommands, expected: true,
            "Eligible selection did not enable every standard size/nudge command.");
        var status = new StatusRectangleProbeCommand();
        commands.AddCommand(status);
        Assert(commands.GlobalInvoke(FormsDesign.MenuCommands.KeyNudgeRight)
            && eligible.Bounds == new Rectangle(17, 16, 24, 24)
            && status.InvokeCount == 1
            && status.LastBounds == eligible.Bounds,
            "Single-selection keyboard feedback did not publish the control's exact updated bounds.");

        ReadOnlySizeButton readOnlySize = fixture.AddControl(
            "readOnlySize",
            new ReadOnlySizeButton(),
            new Rectangle(48, 16, 24, 24));
        fixture.Select(readOnlySize);
        AssertCommandsEnabled(commands, movementCommands, expected: true,
            "Writable location did not keep move commands enabled for a read-only-size control.");
        AssertCommandsEnabled(commands, sizeCommands, expected: false,
            "Read-only size state did not disable keyboard size commands.");

        LockedButton locked = fixture.AddControl(
            "locked",
            new LockedButton(),
            new Rectangle(80, 16, 24, 24));
        fixture.Select(locked);
        locked.Locked = true;
        PropertyDescriptor lockedProperty = TypeDescriptor.GetProperties(locked)[nameof(LockedButton.Locked)]!;
        ((IComponentChangeService)fixture.Host.GetService(typeof(IComponentChangeService))!)
            .OnComponentChanged(locked, lockedProperty, false, true);
        AssertCommandsEnabled(commands, movementCommands, expected: false,
            "Typed Locked change did not refresh keyboard move command status.");
        AssertCommandsEnabled(commands, sizeCommands, expected: false,
            "Typed Locked change did not refresh keyboard size command status.");
    }

    private static void KeyboardMoveUsesGridAndPixelStepsAtomically()
    {
        using var fixture = new DesignerFixture(new SharpStyleDesignerOptionService(
            new Size(8, 8),
            snapToGrid: true,
            useSnapLines: false));
        Forms.Button primary = fixture.AddButton("primary", new Rectangle(16, 16, 20, 20));
        Forms.Button sibling = fixture.AddButton("sibling", new Rectangle(48, 24, 30, 20));
        Forms.Button stationary = fixture.AddButton("stationary", new Rectangle(140, 80, 20, 20));
        int primaryIndex = fixture.Root.Controls.IndexOf(primary);
        int siblingIndex = fixture.Root.Controls.IndexOf(sibling);
        int stationaryIndex = fixture.Root.Controls.IndexOf(stationary);
        fixture.Select(primary, sibling);

        var commands = (IMenuCommandService)fixture.Host.GetService(typeof(IMenuCommandService))!;
        var status = new StatusRectangleProbeCommand();
        commands.AddCommand(status);
        using var undo = new ProbeUndoEngine(fixture.Host);
        int opened = 0;
        int closed = 0;
        int changing = 0;
        int changed = 0;
        int primaryInvalidated = 0;
        int siblingInvalidated = 0;
        int parentInvalidated = 0;
        fixture.Host.TransactionOpened += (_, _) => opened++;
        fixture.Host.TransactionClosed += (_, _) => closed++;
        var changes = (IComponentChangeService)fixture.Host.GetService(typeof(IComponentChangeService))!;
        changes.ComponentChanging += (_, e) => changing += IsLocationChangeFor(e.Component, e.Member, primary, sibling) ? 1 : 0;
        changes.ComponentChanged += (_, e) => changed += IsLocationChangeFor(e.Component, e.Member, primary, sibling) ? 1 : 0;
        primary.Invalidated += (_, _) => primaryInvalidated++;
        sibling.Invalidated += (_, _) => siblingInvalidated++;
        fixture.Root.Invalidated += (_, _) => parentInvalidated++;

        Assert(commands.GlobalInvoke(FormsDesign.MenuCommands.KeyMoveRight),
            "Enabled grid keyboard move was not invoked.");
        Assert(primary.Location == new Point(24, 16) && sibling.Location == new Point(56, 24),
            "Ordinary arrow command did not apply the configured 8-pixel grid delta to the group.");
        Assert(commands.GlobalInvoke(FormsDesign.MenuCommands.KeyNudgeDown),
            "Enabled pixel keyboard nudge was not invoked.");
        Assert(primary.Location == new Point(24, 17) && sibling.Location == new Point(56, 25),
            "Ctrl-arrow nudge did not apply exactly one pixel to the group.");
        Assert(stationary.Bounds == new Rectangle(140, 80, 20, 20)
            && fixture.Root.Controls.IndexOf(primary) == primaryIndex
            && fixture.Root.Controls.IndexOf(sibling) == siblingIndex
            && fixture.Root.Controls.IndexOf(stationary) == stationaryIndex,
            "Keyboard group movement changed an unselected control or parent child order.");
        var selection = (ISelectionService)fixture.Host.GetService(typeof(ISelectionService))!;
        Assert(selection.SelectionCount == 2
            && ReferenceEquals(selection.PrimarySelection, primary)
            && selection.GetComponentSelected(sibling),
            "Keyboard movement changed the established selection order.");
        Assert(opened == 2 && closed == 2 && changing == 4 && changed == 4 && undo.UndoCount == 2,
            "Keyboard group moves did not create one typed transaction/undo unit per command.");
        Assert(primaryInvalidated >= 2 && siblingInvalidated >= 2 && parentInvalidated >= 4,
            "Keyboard movement bypassed typed control/parent invalidation.");
        Assert(status.InvokeCount == 2 && status.LastBounds == new Rectangle(24, 17, 62, 28),
            "Keyboard movement did not publish the exact changed-group bounds through SetStatusRectangle.");
        Assert(undo.UndoOnce()
            && primary.Location == new Point(24, 16)
            && sibling.Location == new Point(56, 24),
            "Pixel-nudge undo did not restore the complete selected group.");
        Assert(undo.UndoOnce()
            && primary.Location == new Point(16, 16)
            && sibling.Location == new Point(48, 24),
            "Grid-move undo did not restore the complete selected group.");
        Assert(undo.RedoOnce() && undo.RedoOnce()
            && primary.Location == new Point(24, 17)
            && sibling.Location == new Point(56, 25),
            "Keyboard group move redo did not restore both command deltas.");
    }

    private static void KeyboardSnapLineModeUsesBoundedPixelSteps()
    {
        using var fixture = new DesignerFixture(new SharpStyleDesignerOptionService(
            new Size(10, 10),
            snapToGrid: true,
            useSnapLines: true));
        Forms.Button primary = fixture.AddButton("primary", new Rectangle(20, 20, 30, 20));
        Forms.Button sibling = fixture.AddButton("sibling", new Rectangle(70, 40, 20, 30));
        Forms.Button snapTarget = fixture.AddButton("snapTarget", new Rectangle(55, 20, 20, 20));
        fixture.Select(primary, sibling);

        var commands = (IMenuCommandService)fixture.Host.GetService(typeof(IMenuCommandService))!;
        Assert(commands.GlobalInvoke(FormsDesign.MenuCommands.KeyMoveRight),
            "Snap-line-mode ordinary move command was not invoked.");
        Assert(primary.Location == new Point(21, 20)
            && sibling.Location == new Point(71, 40)
            && snapTarget.Location == new Point(55, 20),
            "Snap-line-mode ordinary movement did not use the deliberate bounded one-pixel fallback.");
        Assert(commands.GlobalInvoke(FormsDesign.MenuCommands.KeySizeWidthIncrease),
            "Snap-line-mode ordinary size command was not invoked.");
        Assert(primary.Size == new Size(31, 20)
            && sibling.Size == new Size(21, 30)
            && snapTarget.Size == new Size(20, 20),
            "Snap-line-mode ordinary sizing did not use the deliberate bounded one-pixel fallback.");
    }

    private static void KeyboardMoveFiltersSelectionWithoutReordering()
    {
        using var fixture = new DesignerFixture(new SharpStyleDesignerOptionService(
            new Size(8, 8),
            snapToGrid: false,
            useSnapLines: false));
        Forms.Button primary = fixture.AddButton("primary", new Rectangle(20, 20, 20, 20));
        Forms.Button sibling = fixture.AddButton("sibling", new Rectangle(50, 30, 20, 20));
        LockedButton locked = fixture.AddControl(
            "locked",
            new LockedButton { Locked = true },
            new Rectangle(80, 20, 20, 20));
        ReadOnlyLocationButton readOnly = fixture.AddControl(
            "readOnly",
            new ReadOnlyLocationButton(),
            new Rectangle(110, 20, 20, 20));
        Forms.Button docked = fixture.AddButton("docked", new Rectangle(140, 20, 20, 20));
        docked.Dock = Forms.DockStyle.Left;
        Forms.Panel nested = fixture.AddControl(
            "nested",
            new Forms.Panel(),
            new Rectangle(20, 100, 120, 80));
        Forms.Button differentParent = fixture.AddButton(
            "differentParent",
            new Rectangle(10, 10, 20, 20),
            nested);
        int[] childOrder =
        {
            fixture.Root.Controls.IndexOf(primary),
            fixture.Root.Controls.IndexOf(sibling),
            fixture.Root.Controls.IndexOf(locked),
            fixture.Root.Controls.IndexOf(readOnly),
            fixture.Root.Controls.IndexOf(docked),
            fixture.Root.Controls.IndexOf(nested)
        };
        fixture.Select(primary, sibling, locked, readOnly, docked, differentParent);

        int changing = 0;
        int changed = 0;
        var changes = (IComponentChangeService)fixture.Host.GetService(typeof(IComponentChangeService))!;
        changes.ComponentChanging += (_, e) => changing += IsLocationChangeFor(e.Component, e.Member, primary, sibling) ? 1 : 0;
        changes.ComponentChanged += (_, e) => changed += IsLocationChangeFor(e.Component, e.Member, primary, sibling) ? 1 : 0;
        var commands = (IMenuCommandService)fixture.Host.GetService(typeof(IMenuCommandService))!;
        Assert(commands.GlobalInvoke(FormsDesign.MenuCommands.KeyNudgeRight),
            "Eligible filtered group did not invoke its keyboard nudge.");

        Assert(primary.Location == new Point(21, 20) && sibling.Location == new Point(51, 30),
            "Keyboard nudge did not retain the selected siblings' relative geometry.");
        Assert(locked.Location == new Point(80, 20)
            && readOnly.Location == new Point(110, 20)
            && docked.Location == new Point(140, 20)
            && differentParent.Location == new Point(10, 10),
            "Keyboard nudge changed a locked, read-only, docked, or different-parent selection member.");
        Assert(changing == 2 && changed == 2,
            "Filtered keyboard selection members emitted typed location changes.");
        Assert(fixture.Root.Controls.IndexOf(primary) == childOrder[0]
            && fixture.Root.Controls.IndexOf(sibling) == childOrder[1]
            && fixture.Root.Controls.IndexOf(locked) == childOrder[2]
            && fixture.Root.Controls.IndexOf(readOnly) == childOrder[3]
            && fixture.Root.Controls.IndexOf(docked) == childOrder[4]
            && fixture.Root.Controls.IndexOf(nested) == childOrder[5],
            "Filtered keyboard movement changed parent child order.");

        fixture.Select(readOnly, sibling);
        MenuCommand readOnlyMove = commands.FindCommand(FormsDesign.MenuCommands.KeyNudgeRight)!;
        Assert(!readOnlyMove.Enabled
            && !commands.GlobalInvoke(FormsDesign.MenuCommands.KeyNudgeRight)
            && sibling.Location == new Point(51, 30),
            "Read-only primary selection initiated a partial keyboard group move.");
    }

    private static void KeyboardSizeUsesGridAndPixelStepsAtomically()
    {
        using var fixture = new DesignerFixture(new SharpStyleDesignerOptionService(
            new Size(10, 10),
            snapToGrid: true,
            useSnapLines: false));
        Forms.Button primary = fixture.AddButton("primary", new Rectangle(20, 20, 30, 20));
        Forms.Button sibling = fixture.AddButton("sibling", new Rectangle(70, 40, 20, 30));
        int primaryIndex = fixture.Root.Controls.IndexOf(primary);
        int siblingIndex = fixture.Root.Controls.IndexOf(sibling);
        fixture.Select(primary, sibling);

        var commands = (IMenuCommandService)fixture.Host.GetService(typeof(IMenuCommandService))!;
        var status = new StatusRectangleProbeCommand();
        commands.AddCommand(status);
        using var undo = new ProbeUndoEngine(fixture.Host);
        int opened = 0;
        int closed = 0;
        int changing = 0;
        int changed = 0;
        fixture.Host.TransactionOpened += (_, _) => opened++;
        fixture.Host.TransactionClosed += (_, _) => closed++;
        var changes = (IComponentChangeService)fixture.Host.GetService(typeof(IComponentChangeService))!;
        changes.ComponentChanging += (_, e) => changing += IsSizeChangeFor(e.Component, e.Member, primary, sibling) ? 1 : 0;
        changes.ComponentChanged += (_, e) => changed += IsSizeChangeFor(e.Component, e.Member, primary, sibling) ? 1 : 0;

        Assert(commands.GlobalInvoke(FormsDesign.MenuCommands.KeySizeWidthIncrease),
            "Enabled Shift-arrow size command was not invoked.");
        Assert(primary.Size == new Size(40, 20) && sibling.Size == new Size(30, 30),
            "Shift-arrow size command did not apply the configured grid width to every eligible control.");
        Assert(commands.GlobalInvoke(FormsDesign.MenuCommands.KeyNudgeHeightDecrease),
            "Enabled Ctrl+Shift-arrow size nudge was not invoked.");
        Assert(primary.Size == new Size(40, 19) && sibling.Size == new Size(30, 29),
            "Ctrl+Shift-arrow size nudge did not apply exactly one height pixel.");
        Assert(primary.Location == new Point(20, 20)
            && sibling.Location == new Point(70, 40)
            && fixture.Root.Controls.IndexOf(primary) == primaryIndex
            && fixture.Root.Controls.IndexOf(sibling) == siblingIndex,
            "Keyboard size commands changed selected control locations or order.");
        Assert(opened == 2 && closed == 2 && changing == 4 && changed == 4 && undo.UndoCount == 2,
            "Keyboard group sizing did not create one typed transaction/undo unit per command.");
        Assert(status.InvokeCount == 2 && status.LastBounds == new Rectangle(20, 20, 80, 49),
            "Keyboard sizing did not publish the exact changed-group bounds through SetStatusRectangle.");
        Assert(undo.UndoOnce()
            && primary.Size == new Size(40, 20)
            && sibling.Size == new Size(30, 30),
            "Pixel-size undo did not restore the complete eligible group.");
        Assert(undo.UndoOnce()
            && primary.Size == new Size(30, 20)
            && sibling.Size == new Size(20, 30),
            "Grid-size undo did not restore the complete eligible group.");
        Assert(undo.RedoOnce() && undo.RedoOnce()
            && primary.Size == new Size(40, 19)
            && sibling.Size == new Size(30, 29),
            "Keyboard group size redo did not restore both command deltas.");
    }

    private static void KeyboardSizeFiltersSelectionAndMoveFailureRollsBack()
    {
        using (var fixture = new DesignerFixture(new SharpStyleDesignerOptionService(
            new Size(8, 8),
            snapToGrid: false,
            useSnapLines: false)))
        {
            Forms.Button primary = fixture.AddButton("primary", new Rectangle(20, 20, 24, 24));
            Forms.Button sibling = fixture.AddButton("sibling", new Rectangle(60, 20, 24, 24));
            LockedButton locked = fixture.AddControl(
                "locked",
                new LockedButton { Locked = true },
                new Rectangle(100, 20, 24, 24));
            ReadOnlySizeButton readOnly = fixture.AddControl(
                "readOnly",
                new ReadOnlySizeButton(),
                new Rectangle(140, 20, 24, 24));
            Forms.Button autoSize = fixture.AddButton("autoSize", new Rectangle(180, 20, 24, 24));
            autoSize.AutoSize = true;
            Forms.Panel nested = fixture.AddControl(
                "nested",
                new Forms.Panel(),
                new Rectangle(20, 100, 120, 80));
            Forms.Button differentParent = fixture.AddButton(
                "differentParent",
                new Rectangle(10, 10, 24, 24),
                nested);
            fixture.Select(primary, sibling, locked, readOnly, autoSize, differentParent);

            var commands = (IMenuCommandService)fixture.Host.GetService(typeof(IMenuCommandService))!;
            Assert(commands.GlobalInvoke(FormsDesign.MenuCommands.KeyNudgeWidthIncrease),
                "Eligible filtered group did not invoke its keyboard size nudge.");
            Assert(primary.Size == new Size(25, 24) && sibling.Size == new Size(25, 24),
                "Keyboard size nudge did not resize both eligible siblings.");
            Assert(locked.Size == new Size(24, 24)
                && readOnly.Size == new Size(24, 24)
                && autoSize.Size == new Size(24, 24)
                && differentParent.Size == new Size(24, 24),
                "Keyboard size nudge changed a locked, read-only, auto-size, or different-parent member.");

            fixture.Select(readOnly, sibling);
            Assert(!commands.FindCommand(FormsDesign.MenuCommands.KeyNudgeWidthIncrease)!.Enabled
                && !commands.GlobalInvoke(FormsDesign.MenuCommands.KeyNudgeWidthIncrease)
                && sibling.Size == new Size(25, 24),
                "Read-only-size primary initiated a partial keyboard group resize.");
        }

        using (var fixture = new DesignerFixture(new SharpStyleDesignerOptionService(
            new Size(8, 8),
            snapToGrid: false,
            useSnapLines: false)))
        {
            Forms.Button primary = fixture.AddButton("primary", new Rectangle(20, 20, 24, 24));
            ThrowingLocationButton throwing = fixture.AddControl(
                "throwing",
                new ThrowingLocationButton(),
                new Rectangle(60, 20, 24, 24));
            fixture.Select(primary, throwing);
            using var undo = new ProbeUndoEngine(fixture.Host);
            int cancelled = 0;
            fixture.Host.TransactionClosed += (_, e) => cancelled += e.TransactionCommitted ? 0 : 1;
            throwing.ThrowOnLocationChange = true;
            var commands = (IMenuCommandService)fixture.Host.GetService(typeof(IMenuCommandService))!;
            bool threw = false;
            try
            {
                commands.GlobalInvoke(FormsDesign.MenuCommands.KeyNudgeRight);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            Assert(threw
                && primary.Location == new Point(20, 20)
                && throwing.Location == new Point(60, 20)
                && cancelled == 1
                && undo.UndoCount == 0,
                "Failed keyboard group move did not restore every control and cancel its atomic transaction.");
        }
    }

    private static bool IsLocationChangeFor(
        object? component,
        MemberDescriptor? member,
        params Forms.Control[] expected)
    {
        if (!string.Equals(member?.Name, nameof(Forms.Control.Location), StringComparison.Ordinal))
            return false;
        for (int index = 0; index < expected.Length; index++)
        {
            if (ReferenceEquals(component, expected[index]))
                return true;
        }

        return false;
    }

    private static bool IsSizeChangeFor(
        object? component,
        MemberDescriptor? member,
        params Forms.Control[] expected)
    {
        if (!string.Equals(member?.Name, nameof(Forms.Control.Size), StringComparison.Ordinal))
            return false;
        for (int index = 0; index < expected.Length; index++)
        {
            if (ReferenceEquals(component, expected[index]))
                return true;
        }

        return false;
    }

    private static void AssertCommandsEnabled(
        IMenuCommandService commands,
        CommandID[] commandIds,
        bool expected,
        string message)
    {
        for (int index = 0; index < commandIds.Length; index++)
        {
            MenuCommand? command = commands.FindCommand(commandIds[index]);
            if (command is null || command.Enabled != expected)
                throw new InvalidOperationException(message + $" Command={commandIds[index]}");
        }
    }

    private static void CountChange(
        object? component,
        MemberDescriptor? member,
        Forms.Control expected,
        ref int locationCount,
        ref int sizeCount)
    {
        if (!ReferenceEquals(component, expected))
            return;
        if (string.Equals(member?.Name, nameof(Forms.Control.Location), StringComparison.Ordinal))
            locationCount++;
        if (string.Equals(member?.Name, nameof(Forms.Control.Size), StringComparison.Ordinal))
            sizeCount++;
    }

    private static void LayoutServiceSourceStaysReflectionFree()
    {
        string layoutSource = File.ReadAllText(FindSourceFile("PortableDesignerLayoutService.cs"));
        string designerSource = File.ReadAllText(FindSourceFile("ComponentModelDesignCompatTypes.cs"));
        string controlSource = File.ReadAllText(FindSourceFile("WinFormsCompatTypes.cs"));
        string contractSource = File.ReadAllText(FindSourceFile("IPortableWinFormsAdornerSource.cs"));
        string hostSource = File.ReadAllText(FindSourceFile("WindowsFormsHost.cs"));
        string[] forbidden =
        {
            "System.Reflection",
            "BindingFlags",
            "GetProperty(",
            "GetField(",
            "GetMethod("
        };
        foreach (string token in forbidden)
        {
            Assert(!layoutSource.Contains(token, StringComparison.Ordinal), $"Layout service reintroduced reflection token '{token}'.");
            Assert(!designerSource.Contains(token, StringComparison.Ordinal), $"Designer manipulation reintroduced reflection token '{token}'.");
            Assert(!controlSource.Contains(token, StringComparison.Ordinal), $"Control adorner path introduced reflection token '{token}'.");
            Assert(!contractSource.Contains(token, StringComparison.Ordinal), $"Adorner contract introduced reflection token '{token}'.");
            Assert(!hostSource.Contains(token, StringComparison.Ordinal), $"Adorner host path introduced reflection token '{token}'.");
        }

        Assert(layoutSource.Contains("service is WindowsFormsDesignerOptionService", StringComparison.Ordinal)
            && layoutSource.Contains("service is IDesignerOptionService", StringComparison.Ordinal),
            "Layout service stopped consuming SharpDevelop/native options through typed services.");
        Assert(layoutSource.Contains("target.PointToClient(source.PointToScreen(point))", StringComparison.Ordinal),
            "Layout service stopped using the typed coordinate conversion contract.");
        Assert(layoutSource.Contains("AddDesignerAdornerPaintHandler(PaintAdornments)", StringComparison.Ordinal)
            && layoutSource.Contains("RemoveDesignerAdornerPaintHandler(PaintAdornments)", StringComparison.Ordinal)
            && layoutSource.Contains("graphics.DrawLine(", StringComparison.Ordinal),
            "Layout service stopped publishing and painting typed snap-line adorners.");
        Assert(layoutSource.Contains("CacheGroupCandidateLines", StringComparison.Ordinal)
            && layoutSource.Contains("ContainsControl(movingControls, target)", StringComparison.Ordinal),
            "Layout service stopped snapping group bounds or excluding moving controls from targets.");
        Assert(layoutSource.Contains("GetKeyboardIncrement", StringComparison.Ordinal)
            && layoutSource.Contains("!precise && !options.UseSnapLines && options.SnapToGrid", StringComparison.Ordinal),
            "Layout service stopped deriving typed grid-versus-pixel keyboard increments.");
        Assert(designerSource.Contains("selectionService.GetSelectedComponents()", StringComparison.Ordinal)
            && designerSource.Contains("ReferenceEquals(control.Parent, parent)", StringComparison.Ordinal)
            && designerSource.Contains("Move {_moveItems.Length} controls", StringComparison.Ordinal),
            "Designer manipulation stopped using typed selection/parent/transaction contracts.");
        Assert(designerSource.Contains("MenuCommands.KeyMoveUp", StringComparison.Ordinal)
            && designerSource.Contains("MenuCommands.KeyNudgeRight", StringComparison.Ordinal)
            && designerSource.Contains("MenuCommands.KeySizeWidthIncrease", StringComparison.Ordinal)
            && designerSource.Contains("MenuCommands.KeyNudgeHeightDecrease", StringComparison.Ordinal),
            "Designer command set stopped owning the standard move/nudge/size command IDs.");
        Assert(designerSource.Contains("_host.OnComponentChanging(change.Control, change.Property)", StringComparison.Ordinal)
            && designerSource.Contains("_host.OnComponentChanged(", StringComparison.Ordinal)
            && designerSource.Contains("_host.CreateTransaction(description)", StringComparison.Ordinal),
            "Keyboard manipulation stopped using typed change and transaction contracts.");
        Assert(designerSource.Contains("MenuCommands.SetStatusRectangle", StringComparison.Ordinal)
            && designerSource.Contains("Rectangle.Union(changedBounds, changes[index].UpdatedBounds)", StringComparison.Ordinal),
            "Keyboard manipulation stopped publishing typed designer bounds feedback.");
        Assert(controlSource.Contains("IPortableWinFormsAdornerSource", StringComparison.Ordinal)
            && contractSource.Contains("PaintPortableAdornments", StringComparison.Ordinal),
            "Control stopped exposing the typed portable adorner contract.");
        Assert(hostSource.Contains("RenderPortableDesignerAdornments(drawingContext, control, bounds);", StringComparison.Ordinal)
            && hostSource.Contains("_portableDesignerAdornerSurfacePools", StringComparison.Ordinal),
            "WindowsFormsHost stopped rendering adorners after children on an isolated overlay surface.");
    }

    private static void PaintSurfaceRetirementStaysFrameBounded()
    {
        string hostSource = File.ReadAllText(FindSourceFile("WindowsFormsHost.cs"));

        Assert(hostSource.Contains("private readonly List<PortablePaintSurface> _pendingRetiredSurfaces = new();", StringComparison.Ordinal),
            "Paint surface replacement stopped tracking the current frame's retired surfaces.");
        Assert(hostSource.Contains("private readonly List<PortablePaintSurface> _safeRetiredSurfaces = new();", StringComparison.Ordinal),
            "Paint surface replacement stopped retaining one safe in-flight generation.");
        Assert(hostSource.Contains("pool.AdvanceRetiredSurfaces();", StringComparison.Ordinal),
            "Active paint surface pools stopped advancing retirement once per render.");
        Assert(hostSource.Contains("_pendingRetiredSurfaces.Add(current);", StringComparison.Ordinal),
            "Resized paint surfaces stopped entering the bounded retirement queue.");
        Assert(hostSource.Contains("_safeRetiredSurfaces.AddRange(_pendingRetiredSurfaces);", StringComparison.Ordinal),
            "Paint surface retirement stopped rotating pending surfaces through the safe generation.");
        Assert(hostSource.Contains("foreach (PortablePaintSurface surface in _safeRetiredSurfaces)", StringComparison.Ordinal),
            "Paint surface retirement stopped disposing the completed safe generation.");
        Assert(hostSource.Contains("CompletePortablePaintSurfaceSequence(listBox);", StringComparison.Ordinal)
            && hostSource.Contains("CompletePortablePaintSurfaceSequence(treeView);", StringComparison.Ordinal),
            "Owner-draw controls stopped completing their frame-local paint surface sequences.");
        Assert(hostSource.Contains("_surfaces.RemoveRange(_nextSurfaceIndex, surplusSurfaceCount);", StringComparison.Ordinal),
            "Owner-draw surface pools stopped retiring surplus high-water row surfaces.");
        Assert(!hostSource.Contains("_retiredSurfaces", StringComparison.Ordinal),
            "Paint surface pools reintroduced lifetime-long retired surface retention.");
        Assert(hostSource.Contains("public int PortablePaintSurfaceCount", StringComparison.Ordinal),
            "Paint surface diagnostics stopped reporting total retained surface count.");
        Assert(hostSource.Contains("public int PortableRetiredPaintSurfaceCount", StringComparison.Ordinal),
            "Paint surface diagnostics stopped reporting retired surface count.");
        Assert(hostSource.Contains("public long PortablePaintSurfacePixelBytes", StringComparison.Ordinal),
            "Paint surface diagnostics stopped reporting logical retained pixel bytes.");
    }

    private static void RenderResourcesStayBoundedAndReusable()
    {
        string hostSource = File.ReadAllText(FindSourceFile("WindowsFormsHost.cs"));
        string smokeSource = File.ReadAllText(FindSourceFile("Program.cs", "LibreWinForms.SdkSmoke"));

        Assert(hostSource.Contains("PortableColorBrushCacheLimit = 256", StringComparison.Ordinal)
            && hostSource.Contains("PortableFormattedTextCacheLimit = 2048", StringComparison.Ordinal),
            "Hosted WinForms render resource caches stopped enforcing their reviewed ownership bounds.");
        Assert(hostSource.Contains("_portableColorBrushCache.TryGetValue", StringComparison.Ordinal)
            && hostSource.Contains("_portableFormattedTextCache.TryGetValue", StringComparison.Ordinal),
            "Hosted WinForms rendering stopped reusing stable color brushes or formatted text.");
        Assert(hostSource.Contains("_portableColorBrushCache.Count >= PortableColorBrushCacheLimit", StringComparison.Ordinal)
            && hostSource.Contains("_portableFormattedTextCache.Count >= PortableFormattedTextCacheLimit", StringComparison.Ordinal),
            "Hosted WinForms render resource caches stopped enforcing their entry limits.");
        Assert(hostSource.Contains("_portableColorBrushCacheOrder.Dequeue()", StringComparison.Ordinal)
            && hostSource.Contains("_portableFormattedTextCacheOrder.Dequeue()", StringComparison.Ordinal)
            && hostSource.Contains("_portableFormattedTextCache.Remove(oldestKey)", StringComparison.Ordinal),
            "Hosted WinForms render resource caches stopped evicting oldest entries without whole-cache churn.");
        Assert(hostSource.Contains("brush.Freeze();", StringComparison.Ordinal)
            && hostSource.Contains("geometry.Freeze();", StringComparison.Ordinal),
            "Reusable hosted WinForms brushes or clip geometries stopped becoming immutable.");
        Assert(hostSource.Contains("_controlClipCache.GetValue(", StringComparison.Ordinal)
            && hostSource.Contains("cache.Bounds == bounds", StringComparison.Ordinal),
            "Stable hosted-control clip geometry stopped using weak, bounds-aware reuse.");
        Assert(hostSource.Contains("ClearPortableRenderResourceCaches();", StringComparison.Ordinal)
            && hostSource.Contains("_controlClipCache.Clear();", StringComparison.Ordinal)
            && hostSource.Contains("_portableFormattedTextCache.Clear();", StringComparison.Ordinal),
            "Replacing the hosted WinForms tree stopped releasing retained render resources.");
        Assert(hostSource.Contains("public int PortableColorBrushCacheCount", StringComparison.Ordinal)
            && hostSource.Contains("public int PortableFormattedTextCacheCount", StringComparison.Ordinal),
            "Hosted WinForms render resource ownership diagnostics were removed.");
        Assert(smokeSource.Contains("--run-render-allocation", StringComparison.Ordinal)
            && smokeSource.Contains("bytesPerFrame <= 250_000", StringComparison.Ordinal)
            && smokeSource.Contains("released={released}", StringComparison.Ordinal),
            "Package-mode WinForms validation stopped enforcing steady-state allocation and release.");
    }

    private static void HostedLayoutAllocationsStayMeasured()
    {
        string hostSource = File.ReadAllText(FindSourceFile("WindowsFormsHost.cs"));
        string smokeSource = File.ReadAllText(FindSourceFile("Program.cs", "LibreWinForms.SdkSmoke"));

        Assert(hostSource.Contains("Forms.Control.ControlCollection children = control.Controls;", StringComparison.Ordinal)
            && hostSource.Contains("for (int index = 0; index < children.Count; index++)", StringComparison.Ordinal)
            && !hostSource.Contains("var fillControls = new List<Forms.Control>();", StringComparison.Ordinal),
            "Hosted layout traversal reintroduced per-container collection or enumerator allocations.");
        Assert(smokeSource.Contains("--run-layout-allocation", StringComparison.Ordinal)
            && smokeSource.Contains("RunLayoutAllocationBenchmark()", StringComparison.Ordinal)
            && smokeSource.Contains("allocatedBytes != 0", StringComparison.Ordinal)
            && smokeSource.Contains("ArrangeForSmoke", StringComparison.Ordinal),
            "Package-mode WinForms validation stopped measuring hosted layout allocations.");
    }

    private static string FindSourceFile(
        string fileName,
        string projectName = "LibreWinForms.System.Windows.Forms")
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName,
                "src",
                "LibreWinForms.Portable",
                projectName,
                "src",
                fileName);
            if (File.Exists(candidate))
                return candidate;

            candidate = Path.Combine(
                directory.FullName,
                "src",
                "LibreWinForms.Portable",
                projectName,
                fileName);
            if (File.Exists(candidate))
                return candidate;

            candidate = Path.Combine(
                directory.FullName,
                "src",
                "LibreWinForms.Portable",
                "LibreWinForms.WindowsFormsIntegration",
                "src",
                fileName);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate designer source file '{fileName}'.");
    }

    private static void Drag(Forms.Control control, Point start, Point end)
    {
        control.RaiseMouseDown(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, start.X, start.Y, 0));
        control.RaiseMouseMove(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 0, end.X, end.Y, 0));
        control.RaiseMouseUp(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, end.X, end.Y, 0));
    }

    private static void BeginDrag(Forms.Control control, Point start, Point end)
    {
        control.RaiseMouseDown(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, start.X, start.Y, 0));
        control.RaiseMouseMove(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 0, end.X, end.Y, 0));
    }

    private static int PaintAdornerCommandCount(
        Forms.IPortableWinFormsAdornerSource adorners,
        Size size)
    {
        using var bitmap = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height));
        using Graphics graphics = Graphics.FromImage(bitmap);
        adorners.PaintPortableAdornments(
            new Forms.PaintEventArgs(
                graphics,
                new Rectangle(Point.Empty, size)));
        return graphics.DrawingContext.Commands.Count;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class DesignerFixture : IDisposable
    {
        private readonly ServiceContainer _services = new();
        private readonly DesignSurface _surface;

        internal DesignerFixture(SharpStyleDesignerOptionService options)
        {
            Options = options;
            _services.AddService(typeof(DesignerOptionService), options);
            _surface = new DesignSurface(_services);
            Host = (IDesignerHost)_surface.GetService(typeof(IDesignerHost))!;
            Root = (Forms.Panel)Host.CreateComponent(typeof(Forms.Panel), "root");
            Root.Size = new Size(400, 300);
        }

        internal IDesignerHost Host { get; }

        internal SharpStyleDesignerOptionService Options { get; }

        internal Forms.Panel Root { get; }

        internal Forms.Button AddButton(string name, Rectangle bounds, Forms.Control? parent = null)
        {
            var button = (Forms.Button)Host.CreateComponent(typeof(Forms.Button), name);
            button.Bounds = bounds;
            (parent ?? Root).Controls.Add(button);
            return button;
        }

        internal T AddControl<T>(string name, T control, Rectangle bounds, Forms.Control? parent = null)
            where T : Forms.Control
        {
            Host.Container.Add(control, name);
            control.Bounds = bounds;
            (parent ?? Root).Controls.Add(control);
            return control;
        }

        internal Forms.Button FindOnlyButton()
        {
            Forms.Button? found = null;
            foreach (IComponent component in Host.Container.Components)
            {
                if (component is not Forms.Button button)
                    continue;
                if (found is not null)
                    throw new InvalidOperationException("Expected exactly one toolbox-created button.");
                found = button;
            }

            return found ?? throw new InvalidOperationException("Toolbox placement did not create a button.");
        }

        internal void Select(params Forms.Control[] controls)
        {
            ((ISelectionService)Host.GetService(typeof(ISelectionService))!)
                .SetSelectedComponents(controls, SelectionTypes.Replace);
        }

        public void Dispose()
        {
            _surface.Dispose();
            _services.Dispose();
        }
    }

    private sealed class LockedButton : Forms.Button
    {
        public bool Locked { get; set; }
    }

    private sealed class ReadOnlyLocationButton : Forms.Button
    {
        [ReadOnly(true)]
        public new Point Location
        {
            get => base.Location;
            set => base.Location = value;
        }
    }

    private sealed class ReadOnlySizeButton : Forms.Button
    {
        [ReadOnly(true)]
        public new Size Size
        {
            get => base.Size;
            set => base.Size = value;
        }
    }

    private sealed class ThrowingLocationButton : Forms.Button
    {
        internal bool ThrowOnLocationChange { get; set; }

        public override Point Location
        {
            get => base.Location;
            set
            {
                if (ThrowOnLocationChange && value != base.Location)
                    throw new InvalidOperationException("Synthetic keyboard move failure.");
                base.Location = value;
            }
        }
    }

    private sealed class StatusRectangleProbeCommand : MenuCommand
    {
        internal StatusRectangleProbeCommand()
            : base((_, _) => { }, FormsDesign.MenuCommands.SetStatusRectangle)
        {
        }

        internal int InvokeCount { get; private set; }

        internal Rectangle LastBounds { get; private set; }

        public override void Invoke(object arg)
        {
            if (arg is Rectangle bounds)
            {
                InvokeCount++;
                LastBounds = bounds;
            }
        }
    }

    private sealed class SharpStyleDesignerOptionService : FormsDesign.WindowsFormsDesignerOptionService
    {
        internal SharpStyleDesignerOptionService(Size gridSize, bool snapToGrid, bool useSnapLines)
        {
            GridSize = gridSize;
            SnapToGrid = snapToGrid;
            UseSnapLines = useSnapLines;
            ShowGrid = snapToGrid && !useSnapLines;
        }
    }

    private sealed class ProbeUndoEngine : UndoEngine
    {
        private readonly Stack<UndoUnit> _undo = new();
        private readonly Stack<UndoUnit> _redo = new();

        internal ProbeUndoEngine(IServiceProvider provider)
            : base(provider)
        {
        }

        internal int UndoCount => _undo.Count;

        protected override void AddUndoUnit(UndoUnit unit)
        {
            _undo.Push(unit);
            _redo.Clear();
        }

        internal bool UndoOnce()
        {
            if (_undo.Count == 0)
                return false;
            UndoUnit unit = _undo.Pop();
            unit.Undo();
            _redo.Push(unit);
            return true;
        }

        internal bool RedoOnce()
        {
            if (_redo.Count == 0)
                return false;
            UndoUnit unit = _redo.Pop();
            unit.Undo();
            _undo.Push(unit);
            return true;
        }
    }

    private sealed class ProbeToolboxService : IToolboxService
    {
        private ToolboxItem? _selected;

        public CategoryNameCollection CategoryNames { get; } = new(Array.Empty<string>());

        public string? SelectedCategory { get; set; }

        internal int SelectedToolboxItemUsedCount { get; private set; }

        public event EventHandler? SelectedCategoryChanged
        {
            add { }
            remove { }
        }

        public event EventHandler? SelectedCategoryChanging
        {
            add { }
            remove { }
        }

        public void AddCreator(ToolboxItemCreatorCallback creator, string format) { }

        public void AddCreator(ToolboxItemCreatorCallback creator, string format, IDesignerHost host) { }

        public void AddLinkedToolboxItem(ToolboxItem toolboxItem, string category, IDesignerHost host) { }

        public void AddLinkedToolboxItem(ToolboxItem toolboxItem, IDesignerHost host) { }

        public void AddToolboxItem(ToolboxItem toolboxItem) { }

        public void AddToolboxItem(ToolboxItem toolboxItem, string category) { }

        public ToolboxItem DeserializeToolboxItem(object serializedObject) => (ToolboxItem)serializedObject;

        public ToolboxItem DeserializeToolboxItem(object serializedObject, IDesignerHost host) => DeserializeToolboxItem(serializedObject);

        public ToolboxItem? GetSelectedToolboxItem() => _selected;

        public ToolboxItem? GetSelectedToolboxItem(IDesignerHost host) => _selected;

        public ToolboxItemCollection GetToolboxItems() => new(Array.Empty<ToolboxItem>());

        public ToolboxItemCollection GetToolboxItems(string category) => GetToolboxItems();

        public ToolboxItemCollection GetToolboxItems(string category, IDesignerHost host) => GetToolboxItems();

        public ToolboxItemCollection GetToolboxItems(IDesignerHost host) => GetToolboxItems();

        public bool IsSupported(object serializedObject, IDesignerHost host) => serializedObject is ToolboxItem;

        public bool IsToolboxItem(object serializedObject) => serializedObject is ToolboxItem;

        public bool IsToolboxItem(object serializedObject, IDesignerHost host) => IsToolboxItem(serializedObject);

        public void Refresh() { }

        public void RemoveCreator(string format) { }

        public void RemoveCreator(string format, IDesignerHost host) { }

        public void RemoveToolboxItem(ToolboxItem toolboxItem) { }

        public void RemoveToolboxItem(ToolboxItem toolboxItem, string category) { }

        public void SelectedToolboxItemUsed()
        {
            SelectedToolboxItemUsedCount++;
            _selected = null;
        }

        public object SerializeToolboxItem(ToolboxItem toolboxItem) => toolboxItem;

        public bool SetCursor() => _selected is not null;

        public void SetSelectedToolboxItem(ToolboxItem toolboxItem) => _selected = toolboxItem;
    }
}
