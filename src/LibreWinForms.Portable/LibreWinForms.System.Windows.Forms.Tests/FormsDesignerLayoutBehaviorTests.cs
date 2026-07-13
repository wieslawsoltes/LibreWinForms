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
        AltBypassesSnapLinesButKeepsGridActive();
        ScrolledNestedToolboxCoordinatesUseTypedConversion();
        TransactionsChangesAndUndoCountsStayExact();
        LayoutServiceSourceStaysReflectionFree();
        Console.WriteLine(
            "LibreWinForms Forms Designer layout tests passed: grid=12 toolbox=2 snap=9 alt=2 coordinates=1 transactions=8 sourceGuard=7.");
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
        string path = FindSourceFile("PortableDesignerLayoutService.cs");
        string source = File.ReadAllText(path);
        string[] forbidden =
        {
            "System.Reflection",
            "BindingFlags",
            "GetProperty(",
            "GetField(",
            "GetMethod("
        };
        foreach (string token in forbidden)
            Assert(!source.Contains(token, StringComparison.Ordinal), $"Layout service reintroduced reflection token '{token}'.");

        Assert(source.Contains("service is WindowsFormsDesignerOptionService", StringComparison.Ordinal)
            && source.Contains("service is IDesignerOptionService", StringComparison.Ordinal),
            "Layout service stopped consuming SharpDevelop/native options through typed services.");
        Assert(source.Contains("target.PointToClient(source.PointToScreen(point))", StringComparison.Ordinal),
            "Layout service stopped using the typed coordinate conversion contract.");
    }

    private static string FindSourceFile(string fileName)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName,
                "src",
                "LibreWinForms.Portable",
                "LibreWinForms.System.Windows.Forms",
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

        internal Forms.Button AddButton(string name, Rectangle bounds)
        {
            var button = (Forms.Button)Host.CreateComponent(typeof(Forms.Button), name);
            button.Bounds = bounds;
            Root.Controls.Add(button);
            return button;
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

        internal void Select(Forms.Control control)
        {
            ((ISelectionService)Host.GetService(typeof(ISelectionService))!)
                .SetSelectedComponents(new object[] { control }, SelectionTypes.Replace);
        }

        public void Dispose()
        {
            _surface.Dispose();
            _services.Dispose();
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
