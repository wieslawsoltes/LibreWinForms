// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.ComponentModel.Design.Serialization;
using System.Drawing;
using System.Drawing.Design;
using System.Windows.Forms.Design;

namespace System.ComponentModel.Design.Tests;

public class ReportingDesignerContractTests
{
    [Fact]
    public void CanonicalReportingDesigner_UsesLoaderFilterSerializationPaintAndEditorContracts()
    {
        EnsurePortableBackend();

        SelectionRulesMatchCanonicalValues();
        BasicLoaderTracksFlushDependenciesAndIdleReload();
        DesignerFiltersFlowThroughTypeDescriptor();
        CodeDomSerializationRoundTripsRealControls();
        DesignSurfaceManagerReleasesDisposedSurface();
        WaitCursorPaintAndEditorsUseCanonicalImplementations();
    }

    private static void SelectionRulesMatchCanonicalValues()
    {
        Assert.Equal(0x10000000, (int)SelectionRules.Moveable);
        Assert.Equal(0x40000000, (int)SelectionRules.Visible);
        Assert.Equal(unchecked((int)0x80000000), (int)SelectionRules.Locked);
        Assert.Equal(1, (int)SelectionRules.TopSizeable);
        Assert.Equal(2, (int)SelectionRules.BottomSizeable);
        Assert.Equal(4, (int)SelectionRules.LeftSizeable);
        Assert.Equal(8, (int)SelectionRules.RightSizeable);
        Assert.Equal(15, (int)SelectionRules.AllSizeable);

        using Panel control = new();
        using ControlDesigner designer = new();
        designer.Initialize(control);
        Assert.Equal(
            SelectionRules.Visible | SelectionRules.Moveable | SelectionRules.AllSizeable,
            designer.SelectionRules &
                (SelectionRules.Visible | SelectionRules.Moveable | SelectionRules.AllSizeable));
    }

    private static void BasicLoaderTracksFlushDependenciesAndIdleReload()
    {
        using ReportingDesignSurface surface = new();
        ProbeDesignerLoader loader = new();
        int loading = 0;
        int loaded = 0;
        int unloading = 0;
        int unloaded = 0;
        int flushed = 0;
        surface.Loading += (_, _) => loading++;
        surface.Loaded += (_, _) => loaded++;
        surface.Unloading += (_, _) => unloading++;
        surface.Unloaded += (_, _) => unloaded++;
        surface.Flushed += (_, _) => flushed++;
        surface.BeginLoad(loader);

        Assert.True(surface.IsLoaded);
        Assert.Equal(1, loader.LoadCount);
        Assert.False(loader.IsModified);
        Assert.Same(loader, surface.GetService(typeof(IDesignerLoaderService)));

        IDesignerHost host = Assert.IsAssignableFrom<IDesignerHost>(surface.GetService(typeof(IDesignerHost)));
        Assert.Equal("LibreWinForms.Reporting.Root", host.RootComponentClassName);

        loader.PublishPropertyProvider(new LoaderPropertyProvider { DocumentName = "report.srd" });
        IDesignerSerializationManager serializationManager = Assert.IsAssignableFrom<IDesignerSerializationManager>(
            surface.GetService(typeof(IDesignerSerializationManager)));
        Assert.Equal(
            "report.srd",
            serializationManager.Properties[nameof(LoaderPropertyProvider.DocumentName)]?.GetValue(loader.PropertyProviderValue));

        surface.Flush();
        Assert.Equal(0, loader.FlushCount);

        Panel root = Assert.IsType<Panel>(host.RootComponent);
        IComponentChangeService changes = Assert.IsAssignableFrom<IComponentChangeService>(
            surface.GetService(typeof(IComponentChangeService)));
        PropertyDescriptor textProperty = TypeDescriptor.GetProperties(root)[nameof(Control.Text)]!;
        changes.OnComponentChanging(root, textProperty);
        string oldText = root.Text;
        root.Text = "Modified report";
        changes.OnComponentChanged(root, textProperty, oldText, root.Text);
        Assert.True(loader.IsModified);
        Assert.Equal(1, loader.ModifyingCount);

        surface.Flush();
        surface.Flush();
        Assert.Equal(1, loader.FlushCount);
        Assert.False(loader.IsModified);

        _ = host.CreateComponent(typeof(Button), "DirtyBeforeReload");
        Assert.True(loader.IsModified);
        IDesignerLoaderService loaderService = Assert.IsAssignableFrom<IDesignerLoaderService>(
            surface.GetService(typeof(IDesignerLoaderService)));
        Assert.True(loaderService.Reload());
        Assert.True(loader.IsReloadPending);
        Assert.Equal(1, loader.LoadCount);

        PumpPortableDispatcherOnce();

        Assert.Equal(2, loader.LoadCount);
        Assert.Equal(2, loader.FlushCount);
        Assert.False(loader.IsModified);
        Assert.Equal((2, 2, 1, 1, 4), (loading, loaded, unloading, unloaded, flushed));
        Assert.Equal((2, 2, 1), (loader.BeginLoadCount, loader.EndLoadCount, loader.BeginUnloadCount));

        loader.ReloadNeeded = false;
        loader.RequestConditionalReload();
        PumpPortableDispatcherOnce();
        Assert.Equal(2, loader.LoadCount);
        Assert.False(loader.IsReloadPending);

        _ = host.CreateComponent(typeof(Button), "DiscardedChange");
        loader.RequestDiscardingReload();
        PumpPortableDispatcherOnce();
        Assert.Equal(3, loader.LoadCount);
        Assert.Equal(2, loader.FlushCount);
        Assert.True(loader.IsModified);

        using ReportingDesignSurface deferredSurface = new();
        DeferredProbeDesignerLoader deferred = new();
        deferredSurface.BeginLoad(deferred);
        Assert.True(deferred.Loading);
        Assert.False(deferredSurface.IsLoaded);
        Assert.False(Assert.IsAssignableFrom<IDesignerLoaderService>(
            deferredSurface.GetService(typeof(IDesignerLoaderService))).Reload());
        deferred.CompleteDependency();
        Assert.False(deferred.Loading);
        Assert.True(deferredSurface.IsLoaded);

        ReportingDesignSurface failingSurface = new();
        DeferredProbeDesignerLoader failing = new();
        failingSurface.BeginLoad(failing);
        IDesignerHost failingHost = Assert.IsAssignableFrom<IDesignerHost>(
            failingSurface.GetService(typeof(IDesignerHost)));
        failing.CompleteDependency(new InvalidOperationException("dependent load failed"));
        Assert.False(failingSurface.IsLoaded);
        Assert.Single(failingSurface.LoadErrors.Cast<object>());
        Assert.Null(failingHost.RootComponent);
        Assert.Empty(failingHost.Container.Components.Cast<IComponent>());
        Assert.Equal(1, failing.BeginUnloadCount);
        failingSurface.Dispose();
        Assert.Equal(2, failing.BeginUnloadCount);

        ReportingDesignSurface pendingSurface = new();
        DeferredProbeDesignerLoader pending = new();
        pendingSurface.BeginLoad(pending);
        pendingSurface.Dispose();
        Assert.Equal(1, pending.BeginUnloadCount);
    }

    private static void DesignerFiltersFlowThroughTypeDescriptor()
    {
        using ReportingDesignSurface surface = new();
        surface.BeginLoad(new ProbeDesignerLoader());
        IDesignerHost host = Assert.IsAssignableFrom<IDesignerHost>(surface.GetService(typeof(IDesignerHost)));
        ProbeFilterComponent component = Assert.IsType<ProbeFilterComponent>(
            host.CreateComponent(typeof(ProbeFilterComponent), "FilteredComponent"));
        TypeDescriptor.Refresh(component);

        Assert.IsAssignableFrom<ITypeDescriptorFilterService>(component.Site?.GetService(typeof(ITypeDescriptorFilterService)));
        PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(component);
        Assert.Null(properties[nameof(ProbeFilterComponent.RemoveBefore)]);
        Assert.Null(properties[nameof(ProbeFilterComponent.RemoveAfter)]);
        Assert.Equal("Filtered Visible", properties[nameof(ProbeFilterComponent.Visible)]?.DisplayName);
        Assert.Equal("Filtered", properties[nameof(ProbeFilterComponent.Visible)]?.Category);

        EventDescriptorCollection events = TypeDescriptor.GetEvents(component);
        Assert.Null(events[nameof(ProbeFilterComponent.RemoveBeforeEvent)]);
        Assert.Null(events[nameof(ProbeFilterComponent.RemoveAfterEvent)]);
        Assert.Equal(
            "Filtered description",
            Assert.IsType<DescriptionAttribute>(TypeDescriptor.GetAttributes(component)[typeof(DescriptionAttribute)]).Description);
        Assert.Equal(
            "Filtered component",
            Assert.IsType<CategoryAttribute>(TypeDescriptor.GetAttributes(component)[typeof(CategoryAttribute)]).Category);

        TypeDescriptionProvider inheritedProvider = TypeDescriptor.AddAttributes(component, InheritanceAttribute.Inherited);
        try
        {
            TypeDescriptor.Refresh(component);
            Assert.Equal(
                InheritanceLevel.Inherited,
                Assert.IsType<InheritanceAttribute>(
                    TypeDescriptor.GetAttributes(component)[typeof(InheritanceAttribute)]).InheritanceLevel);
        }
        finally
        {
            TypeDescriptor.RemoveProvider(inheritedProvider, component);
        }
    }

    private static void CodeDomSerializationRoundTripsRealControls()
    {
        using ReportingDesignSurface surface = new();
        IDesignerHost host = Assert.IsAssignableFrom<IDesignerHost>(surface.GetService(typeof(IDesignerHost)));
        Panel source = Assert.IsType<Panel>(host.CreateComponent(typeof(Panel), "PersistedPanel"));
        source.Location = new Point(23, 29);
        source.Size = new Size(140, 75);
        source.BackColor = Color.CornflowerBlue;
        Button child = Assert.IsType<Button>(host.CreateComponent(typeof(Button), "PersistedButton"));
        child.Location = new Point(7, 11);
        child.Size = new Size(80, 24);
        child.Text = "Open";
        source.Controls.Add(child);

        CodeDomComponentSerializationService service = new(surface);
        using SerializationStore store = service.CreateStore();
        service.Serialize(store, source);
        service.Serialize(store, child);
        store.Close();
        Assert.Throws<PlatformNotSupportedException>(() => store.Save(new MemoryStream()));
        ICollection restoredValues = service.Deserialize(store);
        Panel restored = Assert.Single(restoredValues.Cast<object>().OfType<Panel>());
        Button restoredChild = Assert.Single(restoredValues.Cast<object>().OfType<Button>());
        Assert.Equal(source.Location, restored.Location);
        Assert.Equal(source.Size, restored.Size);
        Assert.Equal(source.BackColor, restored.BackColor);
        Assert.Equal(child.Location, restoredChild.Location);
        Assert.Equal(child.Size, restoredChild.Size);
        Assert.Equal(child.Text, restoredChild.Text);
        Assert.Same(host.Container, restored.Site?.Container);
        Assert.Same(host.Container, restoredChild.Site?.Container);
    }

    private static void DesignSurfaceManagerReleasesDisposedSurface()
    {
        using DesignSurfaceManager manager = new();
        using ServiceContainer services = new();
        DesignSurface surface = manager.CreateDesignSurface(services);
        int disposedCount = 0;
        surface.Disposed += (_, _) => disposedCount++;
        manager.ActiveDesignSurface = surface;

        surface.Dispose();

        Assert.Null(manager.ActiveDesignSurface);
        Assert.Equal(1, disposedCount);
        Assert.Empty(manager.DesignSurfaces.Cast<DesignSurface>());
    }

    private static void WaitCursorPaintAndEditorsUseCanonicalImplementations()
    {
        Application.UseWaitCursor = true;
        Assert.True(Application.UseWaitCursor);
        Application.UseWaitCursor = false;
        Assert.False(Application.UseWaitCursor);

        Cursor.Current = Cursors.WaitCursor;
        Assert.Same(Cursors.WaitCursor, Cursor.Current);
        Cursor.Current = Cursors.Default;
        Assert.Same(Cursors.Default, Cursor.Current);

        using Bitmap bitmap = new(12, 12);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        ControlPaint.DrawBorder3D(graphics, new Rectangle(1, 1, 10, 10), Border3DStyle.Etched);
        ControlPaint.DrawGrabHandle(graphics, new Rectangle(4, 4, 4, 4), primary: true, enabled: true);
        Assert.NotEqual(0, bitmap.GetPixel(1, 1).A);
        Assert.NotEqual(0, bitmap.GetPixel(4, 4).A);

        ProbeCollectionEditor collectionEditor = new(typeof(ArrayList));
        Assert.Equal(UITypeEditorEditStyle.Modal, collectionEditor.GetEditStyle(null));
        Assert.IsType<ProbeCollectionItem>(collectionEditor.Create(typeof(ProbeCollectionItem)));
        ProbeCollectionItem acceptedItem = new();
        ArrayList accepted = [acceptedItem];
        Assert.Same(accepted, collectionEditor.EditValue(null, new ProbeEditorService(acceptCollection: true), accepted));
        Assert.Same(acceptedItem, Assert.Single(accepted.Cast<object>()));
        ArrayList canceled = [];
        Assert.Same(canceled, collectionEditor.EditValue(null, new ProbeEditorService(acceptCollection: false), canceled));
        Assert.Empty(canceled);

        ContentAlignmentEditor alignmentEditor = new();
        Assert.Equal(UITypeEditorEditStyle.DropDown, alignmentEditor.GetEditStyle(null));
        ProbeEditorService alignmentService = new(acceptCollection: false);
        Assert.Equal(
            ContentAlignment.BottomRight,
            alignmentEditor.EditValue(null, alignmentService, ContentAlignment.MiddleCenter));
        Assert.Equal(1, alignmentService.CloseDropDownCount);
    }

    private static void EnsurePortableBackend()
    {
#if LIBREWINFORMS_PORTABLE
        if (!LibreWinForms.Platform.LibrePlatform.IsRegistered)
        {
            LibreWinForms.ProGPU.ProGpuPlatform.Register();
        }
#endif
    }

    private static void PumpPortableDispatcherOnce()
    {
#if LIBREWINFORMS_PORTABLE
        Assert.IsType<LibreWinForms.ProGPU.ProGpuDispatcher>(
            LibreWinForms.Platform.LibrePlatform.Current.Dispatcher).PumpOnce();
#else
        Application.DoEvents();
#endif
    }

    private sealed class ReportingDesignSurface : DesignSurface
    {
        protected internal override IDesigner? CreateDesigner(IComponent component, bool rootDesigner)
            => rootDesigner
                ? new ReportingRootDesigner()
                : component is ProbeFilterComponent ? new ProbeFilterDesigner() : null;
    }

#pragma warning disable CS0618 // IRootDesigner requires the legacy ViewTechnology contract.
    private sealed class ReportingRootDesigner : ComponentDesigner, IRootDesigner
    {
        public ViewTechnology[] SupportedTechnologies => [ViewTechnology.Default];

        public object GetView(ViewTechnology technology) => Component;
    }
#pragma warning restore CS0618

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
            _ = LoaderHost.CreateComponent(typeof(Panel), "Root");
            SetBaseComponentClassName("LibreWinForms.Reporting.Root");
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
            _ = LoaderHost.CreateComponent(typeof(Panel), "Root");
            _loaderService = Assert.IsAssignableFrom<IDesignerLoaderService>(
                LoaderHost.GetService(typeof(IDesignerLoaderService)));
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

    private sealed class LoaderPropertyProvider
    {
        public string DocumentName { get; set; } = string.Empty;
    }

    private sealed class ProbeFilterDesigner : ComponentDesigner
    {
        protected override void PreFilterAttributes(IDictionary attributes)
            => attributes[typeof(DescriptionAttribute)] = new DescriptionAttribute("Filtered description");

        protected override void PostFilterAttributes(IDictionary attributes)
            => attributes[typeof(CategoryAttribute)] = new CategoryAttribute("Filtered component");

        protected override void PreFilterEvents(IDictionary events)
            => events.Remove(nameof(ProbeFilterComponent.RemoveBeforeEvent));

        protected override void PostFilterEvents(IDictionary events)
            => events.Remove(nameof(ProbeFilterComponent.RemoveAfterEvent));

        protected override void PreFilterProperties(IDictionary properties)
            => properties.Remove(nameof(ProbeFilterComponent.RemoveBefore));

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
        public ProbeCollectionEditor(Type type)
            : base(type)
        {
        }

        public object Create(Type itemType) => CreateInstance(itemType);

        protected override Type[] CreateNewItemTypes() => [typeof(ProbeCollectionItem)];
    }

    private sealed class ProbeCollectionItem
    {
    }

    private sealed class ProbeEditorService : IServiceProvider, IWindowsFormsEditorService
    {
        private readonly bool _acceptCollection;

        public ProbeEditorService(bool acceptCollection) => _acceptCollection = acceptCollection;

        public int CloseDropDownCount { get; private set; }

        public object? GetService(Type serviceType)
            => serviceType == typeof(IWindowsFormsEditorService) ? this : null;

        public void CloseDropDown() => CloseDropDownCount++;

        public void DropDownControl(Control control)
        {
            RadioButton bottomRight = Assert.Single(
                control.Controls.Cast<Control>().OfType<RadioButton>(),
                button => button.Name == "_bottomRight");
            bottomRight.PerformClick();
        }

        public DialogResult ShowDialog(Form dialog)
        {
            dialog.Show();
            Assert.Single(Descendants(dialog).OfType<PropertyGrid>());
            DialogResult result = _acceptCollection ? DialogResult.OK : DialogResult.Cancel;
            dialog.DialogResult = result;
            return result;
        }

        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;

                foreach (Control descendant in Descendants(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
