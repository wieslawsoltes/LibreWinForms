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
        DesignerFiltersFlowThroughTypeDescriptorAndPropertyGrid();
        CodeDomSerializationServiceRoundTripsPortableComponents();
        WaitCursorAndDesignerPaintPrimitivesAreFunctional();
        CollectionAndAlignmentEditorsCommitValues();
        Console.WriteLine("LibreWinForms Reporting designer contracts passed: rules=9 hooks=7 loader=3 filters=9 serialization=6 paint=6 editors=9.");
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

    private static void DesignerFiltersFlowThroughTypeDescriptorAndPropertyGrid()
    {
        using var surface = new ProbeFilterDesignSurface();
        surface.BeginLoad(new ProbeDesignerLoader());
        IDesignerHost host = (IDesignerHost)(surface.GetService(typeof(IDesignerHost))
            ?? throw new InvalidOperationException("Design surface did not publish an IDesignerHost."));
        var component = (ProbeFilterComponent)host.CreateComponent(typeof(ProbeFilterComponent), "FilteredComponent");
        TypeDescriptor.Refresh(component);

        Assert(component.Site?.GetService(typeof(ITypeDescriptorFilterService)) is ITypeDescriptorFilterService,
            "Sited component did not publish the typed descriptor-filter service.");
        PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(component);
        Assert(properties[nameof(ProbeFilterComponent.RemoveBefore)] is null,
            "PreFilterProperties did not remove its property.");
        Assert(properties[nameof(ProbeFilterComponent.RemoveAfter)] is null,
            "PostFilterProperties did not remove its property.");
        Assert(properties[nameof(ProbeFilterComponent.Visible)] is { DisplayName: "Filtered Visible", Category: "Filtered" },
            "PostFilterProperties did not replace the visible property descriptor.");

        EventDescriptorCollection events = TypeDescriptor.GetEvents(component);
        Assert(events[nameof(ProbeFilterComponent.RemoveBeforeEvent)] is null,
            "PreFilterEvents did not remove its event.");
        Assert(events[nameof(ProbeFilterComponent.RemoveAfterEvent)] is null,
            "PostFilterEvents did not remove its event.");
        AttributeCollection attributes = TypeDescriptor.GetAttributes(component);
        Assert(attributes[typeof(DescriptionAttribute)] is DescriptionAttribute { Description: "Filtered description" },
            "PreFilterAttributes did not publish its descriptor.");
        Assert(attributes[typeof(CategoryAttribute)] is CategoryAttribute { Category: "Filtered component" },
            "PostFilterAttributes did not publish its descriptor.");

        using var propertyGrid = new Forms.PropertyGrid { SelectedObject = component };
        string[] labels = propertyGrid.DisplayRows
            .Where(static row => !row.IsCategory)
            .Select(static row => row.Label)
            .ToArray();
        Assert(labels.Contains("Filtered Visible"),
            "PropertyGrid did not consume the designer-filtered property descriptor.");
        Assert(!labels.Contains(nameof(ProbeFilterComponent.RemoveBefore))
            && !labels.Contains(nameof(ProbeFilterComponent.RemoveAfter)),
            "PropertyGrid exposed properties removed by the component designer.");

        TypeDescriptionProvider inheritedProvider = TypeDescriptor.AddAttributes(
            component,
            InheritanceAttribute.Inherited);
        try
        {
            Assert(TypeDescriptor.GetAttributes(component)[typeof(InheritanceAttribute)]
                is InheritanceAttribute { InheritanceLevel: InheritanceLevel.Inherited },
                "Descriptor filtering cached away a live instance attribute provider.");
        }
        finally
        {
            TypeDescriptor.RemoveProvider(inheritedProvider, component);
        }
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

    private static void CollectionAndAlignmentEditorsCommitValues()
    {
        var editor = new ProbeCollectionEditor(typeof(ArrayList));
        Assert(editor.GetEditStyle(null) == UITypeEditorEditStyle.Modal, "CollectionEditor is not modal.");
        Assert(editor.Create(typeof(ProbeCollectionItem)) is ProbeCollectionItem, "CollectionEditor did not create the requested typed item.");
        var acceptedItems = new ArrayList();
        object? acceptedResult = editor.EditValue(null, new ProbeEditorService(acceptCollection: true), acceptedItems);
        Assert(ReferenceEquals(acceptedResult, acceptedItems)
            && acceptedItems.Count == 1
            && acceptedItems[0] is ProbeCollectionItem,
            "CollectionEditor did not commit the accepted item list.");

        var cancelledItems = new ArrayList();
        object? cancelledResult = editor.EditValue(null, new ProbeEditorService(acceptCollection: false), cancelledItems);
        Assert(ReferenceEquals(cancelledResult, cancelledItems) && cancelledItems.Count == 0,
            "CollectionEditor mutated the collection after cancellation.");

        var alignmentEditor = new ContentAlignmentEditor();
        Assert(alignmentEditor.GetEditStyle(null) == UITypeEditorEditStyle.DropDown, "ContentAlignmentEditor is not a drop-down editor.");
        var alignmentService = new ProbeEditorService(acceptCollection: false);
        object? alignment = alignmentEditor.EditValue(null, alignmentService, ContentAlignment.MiddleCenter);
        Assert(alignment is ContentAlignment.BottomRight,
            "ContentAlignmentEditor did not commit the selected alignment.");
        Assert(alignmentService.CloseDropDownCount == 1,
            "ContentAlignmentEditor did not close after a typed selection.");
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

    private sealed class ProbeFilterDesignSurface : DesignSurface
    {
        protected override IDesigner? CreateDesigner(IComponent component, bool rootDesigner)
        {
            if (component is ProbeFilterComponent)
                return new ProbeFilterDesigner();

            return base.CreateDesigner(component, rootDesigner);
        }
    }

    private sealed class ProbeFilterDesigner : ComponentDesigner
    {
        protected override void PreFilterAttributes(IDictionary attributes)
        {
            attributes[typeof(DescriptionAttribute)] = new DescriptionAttribute("Filtered description");
        }

        protected override void PostFilterAttributes(IDictionary attributes)
        {
            attributes[typeof(CategoryAttribute)] = new CategoryAttribute("Filtered component");
        }

        protected override void PreFilterEvents(IDictionary events)
        {
            events.Remove(nameof(ProbeFilterComponent.RemoveBeforeEvent));
        }

        protected override void PostFilterEvents(IDictionary events)
        {
            events.Remove(nameof(ProbeFilterComponent.RemoveAfterEvent));
        }

        protected override void PreFilterProperties(IDictionary properties)
        {
            properties.Remove(nameof(ProbeFilterComponent.RemoveBefore));
        }

        protected override void PostFilterProperties(IDictionary properties)
        {
            properties.Remove(nameof(ProbeFilterComponent.RemoveAfter));
            if (properties[nameof(ProbeFilterComponent.Visible)] is PropertyDescriptor descriptor)
            {
                properties[nameof(ProbeFilterComponent.Visible)] = TypeDescriptor.CreateProperty(
                    typeof(ProbeFilterComponent),
                    descriptor,
                    new DisplayNameAttribute("Filtered Visible"),
                    new CategoryAttribute("Filtered"));
            }
        }
    }

    private sealed class ProbeFilterComponent : Component
    {
        public int Visible { get; set; }
        public int RemoveBefore { get; set; }
        public int RemoveAfter { get; set; }

        public event EventHandler? VisibleEvent { add { } remove { } }
        public event EventHandler? RemoveBeforeEvent { add { } remove { } }
        public event EventHandler? RemoveAfterEvent { add { } remove { } }
    }

    private sealed class ProbeCollectionEditor : CollectionEditor
    {
        public ProbeCollectionEditor(Type type) : base(type)
        {
        }

        public object Create(Type itemType) => CreateInstance(itemType);

        protected override Type[] CreateNewItemTypes() => new[] { typeof(ProbeCollectionItem) };
    }

    private sealed class ProbeCollectionItem
    {
    }

    private sealed class ProbeEditorService : IServiceProvider, FormsDesign.IWindowsFormsEditorService
    {
        private readonly bool _acceptCollection;

        public ProbeEditorService(bool acceptCollection) => _acceptCollection = acceptCollection;

        public int CloseDropDownCount { get; private set; }

        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(FormsDesign.IWindowsFormsEditorService) ? this : null;
        }

        public void CloseDropDown() => CloseDropDownCount++;

        public void DropDownControl(Forms.Control control)
        {
            var list = (Forms.ListBox)control;
            list.SelectedIndex = list.Items.IndexOf(ContentAlignment.BottomRight);
        }

        public Forms.DialogResult ShowDialog(Forms.Form dialog)
        {
            Forms.Button add = dialog.Controls.Cast<Forms.Control>()
                .OfType<Forms.Button>()
                .Single(static button => button.Name == "AddButton");
            add.PerformClick();
            Forms.Button completion = dialog.Controls.Cast<Forms.Control>()
                .OfType<Forms.Button>()
                .Single(button => button.Name == (_acceptCollection ? "OkButton" : "CancelButton"));
            completion.PerformClick();
            return dialog.DialogResult;
        }
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
