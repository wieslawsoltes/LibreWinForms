using System;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.ComponentModel.Design.Serialization;
using System.Drawing;
using System.Drawing.Design;
using System.IO;
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
        BasicDesignerLoaderTracksChangesAndFlushesOnlyDirtyDocuments();
        BasicDesignerLoaderDefersCompletionAndReloadsAtIdle();
        DesignSurfaceManagerReleasesDisposedSurfaces();
        DesignerFiltersFlowThroughTypeDescriptorAndPropertyGrid();
        CodeDomSerializationServiceRoundTripsPortableComponents();
        CodeDomSerializationStoreRoundTripsThroughStream();
        WaitCursorAndDesignerPaintPrimitivesAreFunctional();
        CollectionAndAlignmentEditorsCommitValues();
        Console.WriteLine("LibreWinForms Reporting designer contracts passed: rules=9 hooks=7 loader=39 lifecycle=5 filters=10 serialization=6 paint=6 editors=9.");
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

    private static void BasicDesignerLoaderTracksChangesAndFlushesOnlyDirtyDocuments()
    {
        using var surface = new DesignSurface();
        var loader = new ProbeDesignerLoader();
        surface.BeginLoad(loader);

        Assert(surface.IsLoaded, "BasicDesignerLoader did not complete a successful surface load.");
        Assert(loader.LoadCount == 1, "BasicDesignerLoader did not perform exactly one load.");
        Assert(!loader.IsModified, "BasicDesignerLoader started in a modified state.");
        Assert(surface.GetService(typeof(IDesignerLoaderService)) is IDesignerLoaderService service
            && ReferenceEquals(service, loader),
            "BasicDesignerLoader did not register its typed loader service.");
        Assert(((IDesignerHost)surface.GetService(typeof(IDesignerHost))!).RootComponentClassName == "Portable.Reporting.Root",
            "BasicDesignerLoader did not publish its base component class name.");

        loader.PublishPropertyProvider(new LoaderPropertyProvider { DocumentName = "report.srd" });
        var manager = (IDesignerSerializationManager)surface.GetService(typeof(IDesignerSerializationManager))!;
        Assert(manager.Properties[nameof(LoaderPropertyProvider.DocumentName)]?.GetValue(loader.PropertyProviderValue) as string == "report.srd",
            "BasicDesignerLoader property provider did not flow into the serialization manager.");

        surface.Flush();
        Assert(loader.FlushCount == 0, "BasicDesignerLoader flushed a clean document.");

        var host = (IDesignerHost)surface.GetService(typeof(IDesignerHost))!;
        var changes = (IComponentChangeService)surface.GetService(typeof(IComponentChangeService))!;
        var root = (Forms.Panel)host.RootComponent;
        PropertyDescriptor textProperty = TypeDescriptor.GetProperties(root)[nameof(Forms.Control.Text)]!;
        changes.OnComponentChanging(root, textProperty);
        string oldText = root.Text;
        root.Text = "Modified report";
        changes.OnComponentChanged(root, textProperty, oldText, root.Text);
        Assert(loader.IsModified && loader.ModifyingCount == 1,
            "BasicDesignerLoader did not track a typed component change.");

        surface.Flush();
        Assert(loader.FlushCount == 1 && !loader.IsModified,
            "BasicDesignerLoader did not flush and clear one dirty document.");
        surface.Flush();
        Assert(loader.FlushCount == 1, "BasicDesignerLoader flushed the unchanged document twice.");
    }

    private static void BasicDesignerLoaderDefersCompletionAndReloadsAtIdle()
    {
        using (var reentrantSurface = new DesignSurface())
        {
            var reentrantLoader = new ReentrantBeginProbeDesignerLoader();
            reentrantSurface.BeginLoad(reentrantLoader);
            Assert(reentrantLoader.BeginLoadCount == 1 && reentrantLoader.Loading && !reentrantSurface.IsLoaded,
                "Reentrant load dependency recursively restarted OnBeginLoad.");
            reentrantLoader.CompleteDependency();
            Assert(reentrantSurface.IsLoaded && !reentrantLoader.Loading,
                "Reentrant load dependency did not complete the designer load.");
        }

        using (var asynchronousSurface = new DesignSurface())
        {
            var asynchronousLoader = new DeferredProbeDesignerLoader();
            int asynchronousLoadedCount = 0;
            asynchronousSurface.Loaded += (_, _) => asynchronousLoadedCount++;
            asynchronousSurface.BeginLoad(asynchronousLoader);
            Assert(asynchronousLoader.Loading && !asynchronousSurface.IsLoaded && asynchronousLoadedCount == 0,
                "DesignSurface force-completed a pending designer load dependency.");
            Assert(!((IDesignerLoaderService)asynchronousSurface.GetService(typeof(IDesignerLoaderService))!).Reload(),
                "IDesignerLoaderService accepted reload while a dependency was pending.");
            asynchronousLoader.CompleteDependency();
            Assert(!asynchronousLoader.Loading && asynchronousSurface.IsLoaded && asynchronousLoadedCount == 1,
                "DesignSurface did not complete after the final typed load dependency.");
        }

        var failingSurface = new DesignSurface();
        var failingLoader = new DeferredProbeDesignerLoader();
        int failedUnloadingCount = 0;
        int failedUnloadedCount = 0;
        failingSurface.Unloading += (_, _) => failedUnloadingCount++;
        failingSurface.Unloaded += (_, _) => failedUnloadedCount++;
        failingSurface.BeginLoad(failingLoader);
        IDesignerHost failingHost = (IDesignerHost)failingSurface.GetService(typeof(IDesignerHost))!;
        IComponent failedRoot = failingHost.RootComponent;
        failingLoader.CompleteDependency(new InvalidOperationException("dependent load failed"));
        Assert(!failingSurface.IsLoaded && failingSurface.LoadErrors.Count == 1,
            "Failed dependent load did not flow its error into DesignSurface.");
        Assert(failingHost.RootComponent is null && failingHost.Container.Components.Count == 0,
            "Failed dependent load retained its partial component tree.");
        Assert(failingSurface.View is Forms.Panel && !ReferenceEquals(failingSurface.View, failedRoot),
            "Failed dependent load exposed its partial root view.");
        Assert(failingLoader.BeginUnloadCount == 1 && failedUnloadingCount == 1 && failedUnloadedCount == 1,
            "Failed dependent load did not unload exactly one active document.");
        failingSurface.Dispose();
        Assert(failingLoader.BeginUnloadCount == 1,
            "Disposing a failed load invoked the document unload hook twice.");

        var reportedErrorSurface = new FailureProbeDesignSurface();
        var reportedErrorLoader = new ReportedErrorProbeDesignerLoader();
        reportedErrorSurface.BeginLoad(reportedErrorLoader);
        Assert(!reportedErrorSurface.IsLoaded && reportedErrorSurface.LoadErrors.Count == 1,
            "IDesignerSerializationManager.ReportError did not fail the designer load.");
        Assert(reportedErrorLoader.CreatedComponent is { IsDisposed: true }
            && reportedErrorSurface.RootDesigner is { IsDisposed: true },
            "Failed reported-error load did not dispose its partial component and designer.");
        Assert(((IDesignerHost)reportedErrorSurface.GetService(typeof(IDesignerHost))!).Container.Components.Count == 0,
            "Failed reported-error load retained a component in the designer host.");
        Assert(reportedErrorLoader.BeginUnloadCount == 1,
            "Failed reported-error load did not invoke its unload hook.");
        reportedErrorSurface.Dispose();
        Assert(reportedErrorLoader.BeginUnloadCount == 1,
            "Disposing a reported-error load invoked the unload hook twice.");

        var pendingSurface = new DesignSurface();
        var pendingLoader = new DeferredProbeDesignerLoader();
        pendingSurface.BeginLoad(pendingLoader);
        pendingSurface.Dispose();
        Assert(pendingLoader.BeginUnloadCount == 1,
            "Disposing a pending designer load skipped its document unload hook.");

        using var surface = new DesignSurface();
        var loader = new ProbeDesignerLoader();
        int loadingCount = 0;
        int loadedCount = 0;
        int unloadingCount = 0;
        int unloadedCount = 0;
        int flushedCount = 0;
        surface.Loading += (_, _) => loadingCount++;
        surface.Loaded += (_, _) => loadedCount++;
        surface.Unloading += (_, _) => unloadingCount++;
        surface.Unloaded += (_, _) => unloadedCount++;
        surface.Flushed += (_, _) => flushedCount++;
        surface.BeginLoad(loader);

        var host = (IDesignerHost)surface.GetService(typeof(IDesignerHost))!;
        var selection = (ISelectionService)surface.GetService(typeof(ISelectionService))!;
        var serializationManager = (IDesignerSerializationManager)surface.GetService(typeof(IDesignerSerializationManager))!;
        IComponent previousSelection = (IComponent)serializationManager.GetInstance("Root.NestedSelection")!;
        selection.SetSelectedComponents(new object[] { previousSelection }, SelectionTypes.Replace);
        host.CreateComponent(typeof(Forms.Button), "DirtyBeforeReload");
        Assert(loader.IsModified, "Component addition did not dirty the designer before reload.");

        var loaderService = (IDesignerLoaderService)surface.GetService(typeof(IDesignerLoaderService))!;
        Assert(loaderService.Reload() && loader.IsReloadPending,
            "IDesignerLoaderService did not schedule a supported reload.");
        Assert(loader.LoadCount == 1, "Designer reload ran synchronously instead of at idle.");
        Forms.Application.RaiseIdle(EventArgs.Empty);

        Assert(loader.LoadCount == 2 && loader.FlushCount == 1 && flushedCount == 1,
            "Designer reload did not flush once and load once at idle.");
        Assert(surface.IsLoaded && loadingCount == 2 && loadedCount == 2
            && unloadingCount == 1 && unloadedCount == 1,
            "DesignSurface reload lifecycle events were incomplete.");
        IComponent restoredSelection = (IComponent)serializationManager.GetInstance("Root.NestedSelection")!;
        Assert(!ReferenceEquals(previousSelection, restoredSelection)
            && ReferenceEquals(selection.PrimarySelection, restoredSelection),
            "Designer reload did not restore selection by its typed nested component name.");
        Assert(loader.BeginLoadCount == 2 && loader.EndLoadCount == 2 && loader.BeginUnloadCount == 1,
            "BasicDesignerLoader protected lifecycle hooks were not balanced.");

        loader.ReloadNeeded = false;
        loader.RequestConditionalReload();
        Forms.Application.RaiseIdle(EventArgs.Empty);
        Assert(loader.LoadCount == 2 && !loader.IsReloadPending,
            "Conditional reload ignored IsReloadNeeded or remained pending.");

        host.CreateComponent(typeof(Forms.Button), "DiscardedChange");
        loader.RequestDiscardingReload();
        Forms.Application.RaiseIdle(EventArgs.Empty);
        Assert(loader.LoadCount == 3 && loader.FlushCount == 1 && flushedCount == 1 && !loader.IsModified,
            "NoFlush reload did not discard a dirty designer document.");
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

    private static void DesignSurfaceManagerReleasesDisposedSurfaces()
    {
        using var manager = new DesignSurfaceManager();
        WeakReference surfaceReference = CreateDisposedManagedSurface(
            manager,
            out bool activeSurfaceCleared,
            out int disposedCount);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert(activeSurfaceCleared, "Disposing the active design surface did not clear its manager.");
        Assert(disposedCount == 1, "DesignSurface did not raise its typed Disposed event exactly once.");
        Assert(!surfaceReference.IsAlive, "DesignSurfaceManager retained a disposed design surface.");
    }

    private static WeakReference CreateDisposedManagedSurface(
        DesignSurfaceManager manager,
        out bool activeSurfaceCleared,
        out int disposedCount)
    {
        using var services = new ServiceContainer();
        DesignSurface surface = manager.CreateDesignSurface(services);
        int localDisposedCount = 0;
        surface.Disposed += (_, _) => localDisposedCount++;
        manager.ActiveDesignSurface = surface;
        var surfaceReference = new WeakReference(surface);

        surface.Dispose();
        surface.Dispose();

        activeSurfaceCleared = manager.ActiveDesignSurface is null;
        disposedCount = localDisposedCount;
        return surfaceReference;
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

    private static void CodeDomSerializationStoreRoundTripsThroughStream()
    {
        using var surface = new DesignSurface();
        IDesignerHost host = (IDesignerHost)(surface.GetService(typeof(IDesignerHost))
            ?? throw new InvalidOperationException("Design surface did not publish an IDesignerHost."));
        var source = (Forms.Panel)host.CreateComponent(typeof(Forms.Panel), "PersistedPanel");
        source.Location = new Point(23, 29);
        source.Size = new Size(140, 75);
        source.BackColor = Color.CornflowerBlue;
        var child = (Forms.Button)host.CreateComponent(typeof(Forms.Button), "PersistedButton");
        child.Location = new Point(7, 11);
        child.Size = new Size(80, 24);
        child.Text = "Open";
        source.Controls.Add(child);

        var service = new CodeDomComponentSerializationService(surface);
        using var stream = new MemoryStream();
        using (SerializationStore store = service.CreateStore())
        {
            service.Serialize(store, source);
            store.Save(stream);
        }

        Assert(stream.Length > 16, "CodeDom serialization store did not write a portable payload.");
        stream.Position = 0;
        using SerializationStore loadedStore = service.LoadStore(stream);
        ICollection values = service.Deserialize(loadedStore);

        Forms.Panel restored = values.Cast<object>().OfType<Forms.Panel>().Single();
        Assert(restored.Location == source.Location
            && restored.Size == source.Size
            && restored.BackColor == source.BackColor,
            "Stream-backed CodeDom serialization lost panel properties.");
        Assert(restored.Controls.Count == 1
            && restored.Controls[0] is Forms.Button restoredButton
            && restoredButton.Location == child.Location
            && restoredButton.Size == child.Size
            && restoredButton.Text == child.Text,
            "Stream-backed CodeDom serialization lost the typed child graph.");
        Assert(ReferenceEquals(restored.Site?.Container, host.Container)
            && ReferenceEquals(restored.Controls[0].Site?.Container, host.Container),
            "Stream-backed CodeDom serialization restored components outside the designer host.");
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
        public int BeginLoadCount { get; private set; }
        public int EndLoadCount { get; private set; }
        public int BeginUnloadCount { get; private set; }
        public int ModifyingCount { get; private set; }
        public bool IsModified => Modified;
        public bool IsReloadPending => ReloadPending;
        public object? PropertyProviderValue => PropertyProvider;
        public bool ReloadNeeded { get; set; } = true;

        public void PublishPropertyProvider(object provider) => PropertyProvider = provider;
        public void RequestConditionalReload() => Reload(ReloadOptions.Default);
        public void RequestDiscardingReload() => Reload(ReloadOptions.Force | ReloadOptions.NoFlush);

        protected override void OnBeginLoad()
        {
            BeginLoadCount++;
            base.OnBeginLoad();
        }

        protected override void OnBeginUnload()
        {
            BeginUnloadCount++;
            base.OnBeginUnload();
        }

        protected override void OnEndLoad(bool successful, ICollection? errors)
        {
            EndLoadCount++;
            base.OnEndLoad(successful, errors);
        }

        protected override void OnModifying()
        {
            ModifyingCount++;
            base.OnModifying();
        }

        protected override bool IsReloadNeeded() => ReloadNeeded;

        protected override void PerformLoad(IDesignerSerializationManager serializationManager)
        {
            LoadCount++;
            var root = (Forms.Panel)LoaderHost.CreateComponent(typeof(Forms.Panel), "Root");
            var selectionTarget = (Forms.Button)LoaderHost.CreateComponent(typeof(Forms.Button), "SelectionTarget");
            root.Controls.Add(selectionTarget);
            var nestedContainer = (INestedContainer)(root.Site?.GetService(typeof(INestedContainer))
                ?? throw new InvalidOperationException("Root component did not publish its nested container."));
            nestedContainer.Add(new Forms.Button(), "NestedSelection");
            SetBaseComponentClassName("Portable.Reporting.Root");
        }

        protected override void PerformFlush(IDesignerSerializationManager serializationManager) => FlushCount++;
    }

    private sealed class DeferredProbeDesignerLoader : BasicDesignerLoader
    {
        private IDesignerLoaderService? _loaderService;

        public int BeginUnloadCount { get; private set; }

        public void CompleteDependency(Exception? error = null)
        {
            IDesignerLoaderService service = _loaderService
                ?? throw new InvalidOperationException("No deferred load dependency is active.");
            _loaderService = null;
            service.DependentLoadComplete(
                successful: error is null,
                errorCollection: error is null ? null : new object[] { error });
        }

        protected override void PerformLoad(IDesignerSerializationManager serializationManager)
        {
            LoaderHost.CreateComponent(typeof(Forms.Panel), "Root");
            _loaderService = (IDesignerLoaderService)LoaderHost.GetService(typeof(IDesignerLoaderService))!;
            _loaderService.AddLoadDependency();
        }

        protected override void PerformFlush(IDesignerSerializationManager serializationManager)
        {
        }

        protected override void OnBeginUnload()
        {
            BeginUnloadCount++;
            base.OnBeginUnload();
        }
    }

    private sealed class ReentrantBeginProbeDesignerLoader : BasicDesignerLoader
    {
        private IDesignerLoaderService? _loaderService;

        public int BeginLoadCount { get; private set; }

        public void CompleteDependency()
        {
            IDesignerLoaderService service = _loaderService
                ?? throw new InvalidOperationException("No reentrant dependency is active.");
            _loaderService = null;
            service.DependentLoadComplete(successful: true, errorCollection: null);
        }

        protected override void OnBeginLoad()
        {
            BeginLoadCount++;
            base.OnBeginLoad();
            _loaderService = (IDesignerLoaderService)LoaderHost.GetService(typeof(IDesignerLoaderService))!;
            _loaderService.AddLoadDependency();
        }

        protected override void PerformLoad(IDesignerSerializationManager serializationManager)
        {
            LoaderHost.CreateComponent(typeof(Forms.Panel), "Root");
        }

        protected override void PerformFlush(IDesignerSerializationManager serializationManager)
        {
        }
    }

    private sealed class ReportedErrorProbeDesignerLoader : BasicDesignerLoader
    {
        public DisposableProbeComponent? CreatedComponent { get; private set; }
        public int BeginUnloadCount { get; private set; }

        protected override void PerformLoad(IDesignerSerializationManager serializationManager)
        {
            CreatedComponent = (DisposableProbeComponent)LoaderHost.CreateComponent(typeof(DisposableProbeComponent), "Root");
            serializationManager.ReportError(new InvalidOperationException("reported load error"));
        }

        protected override void PerformFlush(IDesignerSerializationManager serializationManager)
        {
        }

        protected override void OnBeginUnload()
        {
            BeginUnloadCount++;
            base.OnBeginUnload();
        }
    }

    private sealed class FailureProbeDesignSurface : DesignSurface
    {
        public DisposableProbeRootDesigner? RootDesigner { get; private set; }

        protected override IDesigner? CreateDesigner(IComponent component, bool rootDesigner)
        {
            if (rootDesigner && component is DisposableProbeComponent)
            {
                RootDesigner = new DisposableProbeRootDesigner();
                return RootDesigner;
            }

            return base.CreateDesigner(component, rootDesigner);
        }
    }

    private sealed class DisposableProbeComponent : Component
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class DisposableProbeRootDesigner : ComponentDesigner, IRootDesigner
    {
        public bool IsDisposed { get; private set; }

        public ViewTechnology[] SupportedTechnologies => new[] { ViewTechnology.Default };

        public object GetView(ViewTechnology technology) => new Forms.Panel();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class LoaderPropertyProvider
    {
        public string DocumentName { get; set; } = string.Empty;
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
