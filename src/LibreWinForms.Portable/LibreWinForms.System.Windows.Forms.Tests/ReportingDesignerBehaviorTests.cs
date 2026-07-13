using System;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.ComponentModel.Design.Serialization;
using System.Drawing;
using System.Drawing.Design;
using System.Linq;
using Forms = System.Windows.Forms;
using FormsDesign = System.Windows.Forms.Design;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class ReportingDesignerBehaviorTests
{
    public static void Run()
    {
        SelectionRuleValuesMatchWinForms();
        ControlDesignerRoutesTypedPaintAndPointerHooks();
        BasicDesignerLoaderCompletesPortableSurfaceLoad();
        CodeDomSerializationServiceRoundTripsPortableComponents();
        WaitCursorAndDesignerPaintPrimitivesAreFunctional();
        CollectionAndAlignmentEditorsExposeExpectedStyles();
        Console.WriteLine("LibreWinForms Reporting designer contracts passed: rules=9 hooks=7 loader=3 serialization=6 paint=6 editors=3.");
    }

    private static void SelectionRuleValuesMatchWinForms()
    {
        Assert((int)FormsDesign.SelectionRules.Moveable == 0x10000000, "Moveable selection bit changed.");
        Assert((int)FormsDesign.SelectionRules.Visible == 0x40000000, "Visible selection bit changed.");
        Assert((int)FormsDesign.SelectionRules.Locked == unchecked((int)0x80000000), "Locked selection bit changed.");
        Assert((int)FormsDesign.SelectionRules.TopSizeable == 1, "Top selection bit changed.");
        Assert((int)FormsDesign.SelectionRules.BottomSizeable == 2, "Bottom selection bit changed.");
        Assert((int)FormsDesign.SelectionRules.LeftSizeable == 4, "Left selection bit changed.");
        Assert((int)FormsDesign.SelectionRules.RightSizeable == 8, "Right selection bit changed.");
        Assert((int)FormsDesign.SelectionRules.AllSizeable == 15, "AllSizeable mask changed.");
        Assert(Forms.Cursors.SizeAll.PortableKind == Forms.PortableCursorKind.SizeAll, "SizeAll cursor lost its typed identity.");
    }

    private static void ControlDesignerRoutesTypedPaintAndPointerHooks()
    {
        using var control = new Forms.Panel { Size = new Size(120, 80) };
        control.Site = new DesignModeSite(control);
        var designer = new ProbeControlDesigner();
        designer.Initialize(control);

        FormsDesign.SelectionRules rules = designer.SelectionRules;
        Assert((rules & FormsDesign.SelectionRules.Visible) != 0, "Designer control is not visibly selectable.");
        Assert((rules & FormsDesign.SelectionRules.Moveable) != 0, "Designer control is not moveable.");
        Assert((rules & FormsDesign.SelectionRules.AllSizeable) == FormsDesign.SelectionRules.AllSizeable, "Designer control is not sizeable.");

        using var bitmap = new Bitmap(16, 16);
        using Graphics graphics = Graphics.FromImage(bitmap);
        control.RaisePaint(new Forms.PaintEventArgs(graphics, new Rectangle(0, 0, 16, 16)));
        control.RaiseMouseDown(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, 4, 5, 0));
        control.RaiseMouseMove(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 0, 9, 11, 0));
        control.RaiseMouseUp(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, 9, 11, 0));

        Assert(designer.PaintCount == 1, "Designer adornment paint was not routed exactly once.");
        Assert(designer.DragBeginCount == 1 && designer.DragMoveCount == 1 && designer.DragEndCount == 1,
            "Designer pointer drag hooks were not routed exactly once.");
        Assert(designer.CursorCount == 1, "Designer cursor hook was not routed on pointer movement.");
        Assert(Forms.Cursor.Position == new Point(9, 11), "Designer pointer coordinates were not published as typed screen state.");
        Assert(designer.CursorPositionObserved == new Point(9, 11),
            "Designer cursor hook observed the previous pointer position.");

        designer.Dispose();
        control.RaiseMouseDown(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, 2, 3, 0));
        Assert(designer.DragBeginCount == 1, "Disposed designer retained its pointer subscription.");
    }

    private static void BasicDesignerLoaderCompletesPortableSurfaceLoad()
    {
        using var surface = new DesignSurface();
        var loader = new ProbeDesignerLoader();
        surface.BeginLoad(loader);

        Assert(surface.IsLoaded, "BasicDesignerLoader did not complete a successful surface load.");
        Assert(loader.LoadCount == 1, "BasicDesignerLoader did not perform exactly one load.");
        surface.Flush();
        Assert(loader.FlushCount == 1, "BasicDesignerLoader did not perform exactly one flush.");
    }

    private static void CodeDomSerializationServiceRoundTripsPortableComponents()
    {
        using var surface = new DesignSurface();
        IDesignerHost host = (IDesignerHost)(surface.GetService(typeof(IDesignerHost))
            ?? throw new InvalidOperationException("Design surface did not publish an IDesignerHost."));
        var source = (Forms.Panel)host.CreateComponent(typeof(Forms.Panel), "SourcePanel");
        source.Location = new Point(13, 17);
        source.Size = new Size(90, 40);

        var service = new CodeDomComponentSerializationService(surface);
        using SerializationStore store = service.CreateStore();
        service.Serialize(store, source);
        store.Close();
        ICollection values = service.Deserialize(store);

        Assert(values.Count == 1, "CodeDom component serialization did not restore one component.");
        var restored = (Forms.Panel)values.Cast<object>().Single();
        Assert(!ReferenceEquals(restored, source), "CodeDom component serialization returned the source instance.");
        Assert(restored.Location == source.Location && restored.Size == source.Size,
            "CodeDom component serialization lost portable component properties.");
        Assert(ReferenceEquals(restored.Site?.Container, host.Container),
            "CodeDom component serialization restored outside the designer host.");
    }

    private static void WaitCursorAndDesignerPaintPrimitivesAreFunctional()
    {
        Forms.Application.UseWaitCursor = true;
        Assert(Forms.Application.UseWaitCursor
            && ReferenceEquals(Forms.Cursor.Current, Forms.Cursors.WaitCursor),
            "Application.UseWaitCursor did not publish the typed wait cursor.");
        Forms.Application.UseWaitCursor = false;
        Assert(!Forms.Application.UseWaitCursor
            && ReferenceEquals(Forms.Cursor.Current, Forms.Cursors.Default),
            "Application.UseWaitCursor did not restore the typed default cursor.");

        using var bitmap = new Bitmap(12, 12);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        Forms.ControlPaint.DrawBorder3D(graphics, new Rectangle(1, 1, 10, 10), Forms.Border3DStyle.Etched);
        Forms.ControlPaint.DrawGrabHandle(graphics, new Rectangle(4, 4, 4, 4), primary: true, enabled: true);
        Assert(bitmap.GetPixel(1, 1).A != 0, "Etched border did not render its outer edge.");
        Assert(bitmap.GetPixel(4, 4).A != 0, "Grab handle did not render its border.");
    }

    private static void CollectionAndAlignmentEditorsExposeExpectedStyles()
    {
        var editor = new ProbeCollectionEditor(typeof(ArrayList));
        Assert(editor.GetEditStyle(null) == UITypeEditorEditStyle.Modal, "CollectionEditor is not modal.");
        Assert(editor.Create(typeof(ProbeCollectionItem)) is ProbeCollectionItem, "CollectionEditor did not create the requested typed item.");

        var alignmentEditor = new ContentAlignmentEditor();
        Assert(alignmentEditor.GetEditStyle(null) == UITypeEditorEditStyle.DropDown, "ContentAlignmentEditor is not a drop-down editor.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class ProbeControlDesigner : FormsDesign.ControlDesigner
    {
        public int PaintCount { get; private set; }
        public int CursorCount { get; private set; }
        public int DragBeginCount { get; private set; }
        public int DragMoveCount { get; private set; }
        public int DragEndCount { get; private set; }
        public Point CursorPositionObserved { get; private set; }

        protected override void OnPaintAdornments(Forms.PaintEventArgs pe) => PaintCount++;
        protected override void OnSetCursor()
        {
            CursorCount++;
            CursorPositionObserved = Forms.Cursor.Position;
        }
        protected override void OnMouseDragBegin(int x, int y) => DragBeginCount++;
        protected override void OnMouseDragMove(int x, int y) => DragMoveCount++;
        protected override void OnMouseDragEnd(bool cancel) => DragEndCount++;
    }

    private sealed class ProbeDesignerLoader : BasicDesignerLoader
    {
        public int LoadCount { get; private set; }
        public int FlushCount { get; private set; }

        protected override void PerformLoad(IDesignerSerializationManager serializationManager)
        {
            LoadCount++;
            LoaderHost.CreateComponent(typeof(Forms.Panel), "Root");
        }

        protected override void PerformFlush(IDesignerSerializationManager serializationManager) => FlushCount++;
    }

    private sealed class ProbeCollectionEditor : CollectionEditor
    {
        public ProbeCollectionEditor(Type type) : base(type)
        {
        }

        public object Create(Type itemType) => CreateInstance(itemType);
    }

    private sealed class ProbeCollectionItem
    {
    }

    private sealed class DesignModeSite : ISite
    {
        public DesignModeSite(IComponent component) => Component = component;

        public IComponent Component { get; }
        public IContainer? Container => null;
        public bool DesignMode => true;
        public string? Name { get; set; }
        public object? GetService(Type serviceType) => null;
    }
}
