using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.ComponentModel.Design.Serialization;
using System.Linq;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfWindow = System.Windows.Window;

namespace LibreWinForms.SdkSmoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--run-form", StringComparer.Ordinal))
        {
            return RunMainFormSmoke();
        }

        if (args.Contains("--run-dialog", StringComparer.Ordinal))
        {
            return RunOwnedDialogSmoke();
        }

        if (args.Contains("--run-designer", StringComparer.Ordinal))
        {
            return RunDesignerSmoke();
        }

        Console.WriteLine("LibreWinForms SDK smoke build loaded.");
        return 0;
    }

    private static int RunMainFormSmoke()
    {
        bool shown = false;
        bool closed = false;

        var form = new Forms.Form
        {
            Name = "LibreWinFormsSdkSmoke",
            Text = "LibreWinForms SDK Smoke",
            Width = 320,
            Height = 180,
            StartPosition = Forms.FormStartPosition.CenterScreen
        };

        using var closeTimer = new Timer(_ => form.Close(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        form.Shown += (_, _) =>
        {
            shown = true;
            closeTimer.Change(TimeSpan.FromMilliseconds(100), Timeout.InfiniteTimeSpan);
        };

        form.FormClosed += (_, _) => closed = true;

        Forms.Application.Run(form);

        if (!shown || !closed)
        {
            Console.Error.WriteLine($"LibreWinForms SDK smoke failed shown={shown} closed={closed}");
            return 2;
        }

        Console.WriteLine("LibreWinForms SDK smoke result=Success host=WPF formShown=True formClosed=True");
        return 0;
    }

    private static int RunOwnedDialogSmoke()
    {
        bool ownerLoaded = false;
        bool dialogShown = false;
        bool dialogClosed = false;
        bool ownerLinked = false;
        Forms.DialogResult dialogResult = Forms.DialogResult.None;

        var application = new WpfApplication();
        var ownerWindow = new WpfWindow
        {
            Title = "LibreWinForms SDK Dialog Owner",
            Width = 480,
            Height = 300
        };

        ownerWindow.Loaded += (_, _) =>
        {
            ownerLoaded = true;
            ownerWindow.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    var dialog = new Forms.Form
                    {
                        Name = "LibreWinFormsSdkOwnedDialog",
                        Text = "LibreWinForms SDK Owned Dialog",
                        Width = 340,
                        Height = 200,
                        StartPosition = Forms.FormStartPosition.CenterParent
                    };

                    var closeTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(100)
                    };
                    closeTimer.Tick += (_, _) =>
                    {
                        closeTimer.Stop();
                        dialog.DialogResult = Forms.DialogResult.OK;
                        dialog.Close();
                    };

                    dialog.Shown += (_, _) =>
                    {
                        dialogShown = true;
                        ownerLinked = WpfApplication.Current.Windows
                            .Cast<WpfWindow>()
                            .Any(window => !ReferenceEquals(window, ownerWindow)
                                && ReferenceEquals(window.Owner, ownerWindow));
                        closeTimer.Start();
                    };
                    dialog.FormClosed += (_, _) => dialogClosed = true;

                    dialogResult = dialog.ShowDialog(new WpfWindowOwner(ownerWindow));
                    closeTimer.Stop();
                    ownerWindow.Close();
                }),
                DispatcherPriority.ApplicationIdle);
        };

        application.Run(ownerWindow);

        if (!ownerLoaded || !dialogShown || !dialogClosed || !ownerLinked || dialogResult != Forms.DialogResult.OK)
        {
            Console.Error.WriteLine(
                $"LibreWinForms SDK owned dialog smoke failed ownerLoaded={ownerLoaded} dialogShown={dialogShown} " +
                $"dialogClosed={dialogClosed} ownerLinked={ownerLinked} result={dialogResult}");
            return 3;
        }

        Console.WriteLine(
            "LibreWinForms SDK owned dialog smoke result=Success host=WPF ownerLoaded=True " +
            "dialogShown=True dialogClosed=True ownerLinked=True result=OK");
        return 0;
    }

    private static int RunDesignerSmoke()
    {
        const string originalName = "toolStripContainer1";
        const string renamedName = "designerContainer";
        const string updatedText = "LibreWinForms designer smoke";

        using var surface = new DesignSurface();
        var loader = new DesignerSmokeLoader();
        surface.BeginLoad(loader);

        var host = surface.GetService(typeof(IDesignerHost)) as IDesignerHost;
        var component = host?.Container.Components[originalName] as Forms.ToolStripContainer;
        var changeService = component?.Site?.GetService(typeof(IComponentChangeService)) as IComponentChangeService;
        var selectionService = host?.GetService(typeof(ISelectionService)) as ISelectionService;
        var serializationManager = host?.GetService(typeof(IDesignerSerializationManager)) as IDesignerSerializationManager;
        PropertyDescriptor? textProperty = component is null ? null : TypeDescriptor.GetProperties(component)[nameof(Forms.Control.Text)];

        bool siteHasChangeService = changeService is not null;
        bool siteHasHost = ReferenceEquals(component?.Site?.GetService(typeof(IDesignerHost)), host);
        bool siteHasContainer = ReferenceEquals(component?.Site?.GetService(typeof(IContainer)), host?.Container);

        var localService = new DesignerSmokeService();
        if (component?.Site is IServiceContainer siteServices)
        {
            siteServices.AddService(typeof(DesignerSmokeService), localService);
        }

        bool siteLocalService = ReferenceEquals(component?.Site?.GetService(typeof(DesignerSmokeService)), localService);

        if (component?.Site?.GetService(typeof(IDictionaryService)) is IDictionaryService dictionary)
        {
            dictionary.SetValue("smoke", updatedText);
        }

        bool siteDictionary = string.Equals(
            (component?.Site?.GetService(typeof(IDictionaryService)) as IDictionaryService)?.GetValue("smoke") as string,
            updatedText,
            StringComparison.Ordinal);

        const string directComponentName = "directComponent1";
        var directComponent = new Component();
        bool directAdding = false;
        bool directAdded = false;
        bool directRemoving = false;
        bool directRemoved = false;
        ComponentEventHandler directAddingHandler = (sender, eventArgs) =>
        {
            directAdding |= ReferenceEquals(sender, host?.Container)
                && ReferenceEquals(eventArgs.Component, directComponent)
                && directComponent.Site is null;
        };
        ComponentEventHandler directAddedHandler = (sender, eventArgs) =>
        {
            directAdded |= ReferenceEquals(sender, host?.Container)
                && ReferenceEquals(eventArgs.Component, directComponent)
                && ReferenceEquals(directComponent.Site?.Container, host?.Container);
        };
        ComponentEventHandler directRemovingHandler = (sender, eventArgs) =>
        {
            directRemoving |= ReferenceEquals(sender, host)
                && ReferenceEquals(eventArgs.Component, directComponent)
                && ReferenceEquals(directComponent.Site?.Container, host?.Container);
        };
        ComponentEventHandler directRemovedHandler = (sender, eventArgs) =>
        {
            directRemoved |= ReferenceEquals(sender, host)
                && ReferenceEquals(eventArgs.Component, directComponent)
                && ReferenceEquals(directComponent.Site?.Container, host?.Container);
        };
        if (changeService is not null)
        {
            changeService.ComponentAdding += directAddingHandler;
            changeService.ComponentAdded += directAddedHandler;
            changeService.ComponentRemoving += directRemovingHandler;
            changeService.ComponentRemoved += directRemovedHandler;
        }

        host?.Container.Add(directComponent, directComponentName);
        bool directRegistered = ReferenceEquals(host?.Container.Components[directComponentName], directComponent)
            && string.Equals(serializationManager?.GetName(directComponent), directComponentName, StringComparison.Ordinal)
            && ReferenceEquals(serializationManager?.GetInstance(directComponentName), directComponent)
            && ReferenceEquals(directComponent.Site?.GetService(typeof(IDesignerHost)), host);
        host?.Container.Remove(directComponent);
        if (changeService is not null)
        {
            changeService.ComponentAdding -= directAddingHandler;
            changeService.ComponentAdded -= directAddedHandler;
            changeService.ComponentRemoving -= directRemovingHandler;
            changeService.ComponentRemoved -= directRemovedHandler;
        }

        bool directLifecycle = directAdding
            && directAdded
            && directRemoving
            && directRemoved
            && directRegistered
            && directComponent.Site is null
            && host?.Container.Components[directComponentName] is null
            && serializationManager?.GetInstance(directComponentName) is null;

        var toolboxItem = new System.Drawing.Design.ToolboxItem(typeof(Forms.Button));
        bool toolboxCreating = false;
        bool toolboxCreated = false;
        toolboxItem.ComponentsCreating += (_, eventArgs) =>
        {
            toolboxCreating = ReferenceEquals(eventArgs.DesignerHost, host);
        };
        toolboxItem.ComponentsCreated += (_, eventArgs) =>
        {
            toolboxCreated = eventArgs.Components is [Forms.Button];
        };

        var toolboxDefaults = new Hashtable
        {
            ["Parent"] = component!,
            [nameof(Forms.Control.Location)] = new System.Drawing.Point(24, 32),
            [nameof(Forms.Control.Size)] = new System.Drawing.Size(120, 28),
            [nameof(Forms.Control.Text)] = "Toolbox button"
        };
        IComponent[] toolboxComponents = host is null
            ? Array.Empty<IComponent>()
            : toolboxItem.CreateComponents(host, toolboxDefaults);
        var toolboxButton = toolboxComponents.Length == 1 ? toolboxComponents[0] as Forms.Button : null;
        IDesigner? toolboxDesigner = toolboxButton is null ? null : host?.GetDesigner(toolboxButton);
        bool toolboxCreation = toolboxCreating
            && toolboxCreated
            && toolboxButton is not null
            && string.Equals(toolboxItem.TypeName, typeof(Forms.Button).FullName, StringComparison.Ordinal)
            && string.Equals(toolboxItem.AssemblyName?.Name, typeof(Forms.Button).Assembly.GetName().Name, StringComparison.Ordinal)
            && ReferenceEquals(toolboxItem.GetType(host), typeof(Forms.Button))
            && ReferenceEquals(toolboxButton.Site?.Container, surface.ComponentContainer)
            && ReferenceEquals(host?.Container.Components[toolboxButton.Site?.Name], toolboxButton)
            && toolboxDesigner is IComponentInitializer
            && ReferenceEquals(toolboxButton.Parent, component)
            && component?.Controls.Contains(toolboxButton) == true
            && toolboxButton.Location == new System.Drawing.Point(24, 32)
            && toolboxButton.Size == new System.Drawing.Size(120, 28)
            && string.Equals(toolboxButton.Text, "Toolbox button", StringComparison.Ordinal)
            && host?.RootComponent is not null
            && host.GetDesigner(host.RootComponent) is IRootDesigner
            && ReferenceEquals(surface.View, host.RootComponent);
        if (toolboxButton is not null)
            host?.DestroyComponent(toolboxButton);
        toolboxCreation &= toolboxButton?.Site is null
            && toolboxButton?.Parent is null
            && (toolboxButton is null || component?.Controls.Contains(toolboxButton) == false)
            && (toolboxButton is null || host?.GetDesigner(toolboxButton) is null);

        DesignerSmokeTrackingDesigner.Reset();
        var attributedComponent = host?.CreateComponent(
            typeof(DesignerSmokeAttributedComponent),
            "attributedComponent1") as DesignerSmokeAttributedComponent;
        bool attributedDesigner = attributedComponent is not null
            && host?.GetDesigner(attributedComponent) is DesignerSmokeTrackingDesigner
            && DesignerSmokeTrackingDesigner.Initialized
            && ReferenceEquals(DesignerSmokeTrackingDesigner.DesignedComponent, attributedComponent);
        if (attributedComponent is not null)
            host?.DestroyComponent(attributedComponent);
        attributedDesigner &= DesignerSmokeTrackingDesigner.Disposed
            && attributedComponent?.Site is null
            && (attributedComponent is null || host?.GetDesigner(attributedComponent) is null);

        var interactionToolboxService = new DesignerSmokeToolboxService();
        (host as IServiceContainer)?.AddService(
            typeof(System.Drawing.Design.IToolboxService),
            interactionToolboxService);
        var interactionTool = new System.Drawing.Design.ToolboxItem(typeof(Forms.Button));
        interactionToolboxService.SetSelectedToolboxItem(interactionTool);
        int interactionComponentCount = host?.Container.Components.Count ?? 0;
        int transactionOpened = 0;
        int transactionClosed = 0;
        int runtimeMouseDown = 0;
        EventHandler transactionOpenedHandler = (_, _) => transactionOpened++;
        DesignerTransactionCloseEventHandler transactionClosedHandler = (_, _) => transactionClosed++;
        Forms.MouseEventHandler runtimeMouseDownHandler = (_, _) => runtimeMouseDown++;
        if (host is not null)
        {
            host.TransactionOpened += transactionOpenedHandler;
            host.TransactionClosed += transactionClosedHandler;
        }
        if (component is not null)
            component.MouseDown += runtimeMouseDownHandler;

        component?.RaiseMouseDown(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, 40, 48, 0));
        component?.RaiseMouseMove(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 0, 160, 80, 0));
        component?.RaiseMouseUp(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, 160, 80, 0));

        if (host is not null)
        {
            host.TransactionOpened -= transactionOpenedHandler;
            host.TransactionClosed -= transactionClosedHandler;
        }
        if (component is not null)
            component.MouseDown -= runtimeMouseDownHandler;

        Forms.Button? interactionButton = host?.Container.Components
            .Cast<IComponent>()
            .OfType<Forms.Button>()
            .SingleOrDefault();
        bool interactivePlacement = interactionButton is not null
            && ReferenceEquals(interactionButton.Parent, component)
            && interactionButton.Location == new System.Drawing.Point(40, 48)
            && interactionButton.Size == new System.Drawing.Size(120, 32)
            && ReferenceEquals(interactionButton.Site?.Container, host?.Container)
            && ReferenceEquals(host?.GetDesigner(interactionButton)?.Component, interactionButton)
            && selectionService?.GetComponentSelected(interactionButton) == true
            && interactionToolboxService.SelectedToolboxItemUsedCount == 1
            && interactionToolboxService.GetSelectedToolboxItem() is null
            && transactionOpened == 1
            && transactionClosed == 1
            && runtimeMouseDown == 0
            && component?.Capture == false
            && host?.Container.Components.Count == interactionComponentCount + 1;
        if (interactionButton is not null)
            host?.DestroyComponent(interactionButton);
        interactivePlacement &= interactionButton?.Site is null
            && interactionButton?.Parent is null
            && (interactionButton is null || component?.Controls.Contains(interactionButton) == false)
            && host?.Container.Components.Count == interactionComponentCount;

        var nestedContainer = component?.Site?.GetService(typeof(INestedContainer)) as INestedContainer;
        var nestedComponent = new Component();
        bool nestedAdding = false;
        bool nestedAdded = false;
        ComponentEventHandler nestedAddingHandler = (sender, eventArgs) =>
        {
            nestedAdding |= ReferenceEquals(sender, nestedContainer)
                && ReferenceEquals(eventArgs.Component, nestedComponent);
        };
        ComponentEventHandler nestedAddedHandler = (sender, eventArgs) =>
        {
            nestedAdded |= ReferenceEquals(sender, nestedContainer)
                && ReferenceEquals(eventArgs.Component, nestedComponent);
        };
        if (changeService is not null)
        {
            changeService.ComponentAdding += nestedAddingHandler;
            changeService.ComponentAdded += nestedAddedHandler;
        }

        nestedContainer?.Add(nestedComponent, "nestedComponent1");
        if (changeService is not null)
        {
            changeService.ComponentAdding -= nestedAddingHandler;
            changeService.ComponentAdded -= nestedAddedHandler;
        }

        const string originalNestedName = originalName + ".nestedComponent1";
        bool nestedOwner = ReferenceEquals(nestedContainer?.Owner, component);
        bool nestedSite = ReferenceEquals(nestedComponent.Site?.Container, nestedContainer)
            && nestedComponent.Site?.DesignMode == true
            && string.Equals((nestedComponent.Site as INestedSite)?.FullName, originalNestedName, StringComparison.Ordinal);
        bool nestedHasHost = ReferenceEquals(nestedComponent.Site?.GetService(typeof(IDesignerHost)), host);
        bool nestedHasContainer = ReferenceEquals(
            nestedComponent.Site?.GetService(typeof(IContainer)),
            host?.Container);
        bool nestedHasChangeService = ReferenceEquals(
            nestedComponent.Site?.GetService(typeof(IComponentChangeService)),
            changeService);
        bool nestedHasSiteLocalService = ReferenceEquals(
            nestedComponent.Site?.GetService(typeof(DesignerSmokeService)),
            localService);
        bool nestedSerialization = string.Equals(
                serializationManager?.GetName(nestedComponent),
                originalNestedName,
                StringComparison.Ordinal)
            && ReferenceEquals(serializationManager?.GetInstance(originalNestedName), nestedComponent);

        const string namedNestedName = originalName + ".tools.namedComponent1";
        INestedContainer? namedNestedContainer = component is null
            ? null
            : surface.CreateNestedContainer(component, "tools");
        var namedNestedComponent = new Component();
        namedNestedContainer?.Add(namedNestedComponent, "namedComponent1");
        bool namedNested = ReferenceEquals(namedNestedContainer?.Owner, component)
            && ReferenceEquals(namedNestedComponent.Site?.Container, namedNestedContainer)
            && string.Equals((namedNestedComponent.Site as INestedSite)?.FullName, namedNestedName, StringComparison.Ordinal)
            && ReferenceEquals(namedNestedComponent.Site?.GetService(typeof(IDesignerHost)), host)
            && ReferenceEquals(namedNestedComponent.Site?.GetService(typeof(IComponentChangeService)), changeService)
            && ReferenceEquals(namedNestedComponent.Site?.GetService(typeof(DesignerSmokeService)), localService);
        namedNestedContainer?.Remove(namedNestedComponent);
        namedNestedContainer?.Dispose();
        bool namedNestedRemoved = namedNestedComponent.Site is null
            && serializationManager?.GetInstance(namedNestedName) is null;

        if (component is not null && textProperty is not null)
        {
            changeService?.OnComponentChanging(component, textProperty);
            textProperty.SetValue(component, updatedText);
            changeService?.OnComponentChanged(component, textProperty, string.Empty, updatedText);
            selectionService?.SetSelectedComponents(new object[] { component }, SelectionTypes.Replace);
        }

        bool selected = component is not null && selectionService?.GetComponentSelected(component) == true;
        surface.Flush();
        bool persisted = loader.ContainsPropertyAssignment(originalName, nameof(Forms.Control.Text), updatedText);

        if (component?.Site is not null)
        {
            component.Site.Name = renamedName;
        }

        bool renamed = component is not null
            && ReferenceEquals(host?.Container.Components[renamedName], component)
            && host?.Container.Components[originalName] is null
            && ReferenceEquals(serializationManager?.GetInstance(renamedName), component)
            && serializationManager?.GetInstance(originalName) is null;
        const string renamedNestedName = renamedName + ".nestedComponent1";
        bool nestedRenamed = string.Equals(
                (nestedComponent.Site as INestedSite)?.FullName,
                renamedNestedName,
                StringComparison.Ordinal)
            && string.Equals(serializationManager?.GetName(nestedComponent), renamedNestedName, StringComparison.Ordinal)
            && ReferenceEquals(serializationManager?.GetInstance(renamedNestedName), nestedComponent)
            && serializationManager?.GetInstance(originalNestedName) is null;

        bool success = surface.IsLoaded
            && component is not null
            && siteHasChangeService
            && siteHasHost
            && siteHasContainer
            && siteLocalService
            && siteDictionary
            && directLifecycle
            && toolboxCreation
            && attributedDesigner
            && interactivePlacement
            && nestedContainer is not null
            && nestedOwner
            && nestedSite
            && nestedAdding
            && nestedAdded
            && nestedHasHost
            && nestedHasContainer
            && nestedHasChangeService
            && nestedHasSiteLocalService
            && nestedSerialization
            && namedNested
            && namedNestedRemoved
            && selected
            && persisted
            && renamed
            && nestedRenamed;

        Console.WriteLine(
            "LibreWinForms SDK designer smoke result=" + (success ? "Success" : "Partial")
            + $" loaded={surface.IsLoaded} component={component is not null}"
            + $" siteHasChangeService={siteHasChangeService} siteHasHost={siteHasHost} siteHasContainer={siteHasContainer}"
            + $" siteLocalService={siteLocalService} siteDictionary={siteDictionary} directLifecycle={directLifecycle}"
            + $" toolboxCreation={toolboxCreation} attributedDesigner={attributedDesigner} selected={selected}"
            + $" interactivePlacement={interactivePlacement}"
            + $" nestedOwner={nestedOwner} nestedSite={nestedSite} nestedAdding={nestedAdding} nestedAdded={nestedAdded}"
            + $" nestedHasHost={nestedHasHost} nestedHasContainer={nestedHasContainer}"
            + $" nestedHasChangeService={nestedHasChangeService}"
            + $" nestedHasSiteLocalService={nestedHasSiteLocalService} nestedSerialization={nestedSerialization}"
            + $" namedNested={namedNested} namedNestedRemoved={namedNestedRemoved}"
            + $" persisted={persisted} renamed={renamed} nestedRenamed={nestedRenamed}");
        return success ? 0 : 4;
    }

    private sealed class WpfWindowOwner : Forms.IWin32Window
    {
        private readonly WpfWindow _window;

        public WpfWindowOwner(WpfWindow window)
        {
            _window = window;
        }

        public IntPtr Handle => new WindowInteropHelper(_window).Handle;
    }

    private sealed class DesignerSmokeService
    {
    }

    private sealed class DesignerSmokeToolboxService : System.Drawing.Design.IToolboxService
    {
        private System.Drawing.Design.ToolboxItem? _selectedToolboxItem;

        public System.Drawing.Design.CategoryNameCollection CategoryNames { get; } = new(Array.Empty<string>());

        public string? SelectedCategory { get; set; }

        public int SelectedToolboxItemUsedCount { get; private set; }

        public event EventHandler? SelectedCategoryChanged;

        public event EventHandler? SelectedCategoryChanging;

        public void AddCreator(System.Drawing.Design.ToolboxItemCreatorCallback creator, string format)
        {
        }

        public void AddCreator(System.Drawing.Design.ToolboxItemCreatorCallback creator, string format, IDesignerHost host)
        {
        }

        public void AddLinkedToolboxItem(System.Drawing.Design.ToolboxItem toolboxItem, string category, IDesignerHost host)
        {
        }

        public void AddLinkedToolboxItem(System.Drawing.Design.ToolboxItem toolboxItem, IDesignerHost host)
        {
        }

        public void AddToolboxItem(System.Drawing.Design.ToolboxItem toolboxItem)
        {
        }

        public void AddToolboxItem(System.Drawing.Design.ToolboxItem toolboxItem, string category)
        {
        }

        public System.Drawing.Design.ToolboxItem DeserializeToolboxItem(object serializedObject)
        {
            return (System.Drawing.Design.ToolboxItem)serializedObject;
        }

        public System.Drawing.Design.ToolboxItem DeserializeToolboxItem(object serializedObject, IDesignerHost host)
        {
            return DeserializeToolboxItem(serializedObject);
        }

        public System.Drawing.Design.ToolboxItem? GetSelectedToolboxItem()
        {
            return _selectedToolboxItem;
        }

        public System.Drawing.Design.ToolboxItem? GetSelectedToolboxItem(IDesignerHost host)
        {
            return _selectedToolboxItem;
        }

        public System.Drawing.Design.ToolboxItemCollection GetToolboxItems()
        {
            return new System.Drawing.Design.ToolboxItemCollection(Array.Empty<System.Drawing.Design.ToolboxItem>());
        }

        public System.Drawing.Design.ToolboxItemCollection GetToolboxItems(string category)
        {
            return GetToolboxItems();
        }

        public System.Drawing.Design.ToolboxItemCollection GetToolboxItems(string category, IDesignerHost host)
        {
            return GetToolboxItems();
        }

        public System.Drawing.Design.ToolboxItemCollection GetToolboxItems(IDesignerHost host)
        {
            return GetToolboxItems();
        }

        public bool IsSupported(object serializedObject, IDesignerHost host)
        {
            return serializedObject is System.Drawing.Design.ToolboxItem;
        }

        public bool IsToolboxItem(object serializedObject)
        {
            return serializedObject is System.Drawing.Design.ToolboxItem;
        }

        public bool IsToolboxItem(object serializedObject, IDesignerHost host)
        {
            return IsToolboxItem(serializedObject);
        }

        public void Refresh()
        {
        }

        public void RemoveCreator(string format)
        {
        }

        public void RemoveCreator(string format, IDesignerHost host)
        {
        }

        public void RemoveToolboxItem(System.Drawing.Design.ToolboxItem toolboxItem)
        {
        }

        public void RemoveToolboxItem(System.Drawing.Design.ToolboxItem toolboxItem, string category)
        {
        }

        public void SelectedToolboxItemUsed()
        {
            SelectedToolboxItemUsedCount++;
            _selectedToolboxItem = null;
        }

        public object SerializeToolboxItem(System.Drawing.Design.ToolboxItem toolboxItem)
        {
            return toolboxItem;
        }

        public bool SetCursor()
        {
            return _selectedToolboxItem is not null;
        }

        public void SetSelectedToolboxItem(System.Drawing.Design.ToolboxItem toolboxItem)
        {
            _selectedToolboxItem = toolboxItem;
        }
    }

    private sealed class DesignerSmokeLoader : CodeDomDesignerLoader
    {
        private CodeCompileUnit? _writtenUnit;

        protected override CodeDomProvider? CodeDomProvider => null;

        protected override ITypeResolutionService? TypeResolutionService => null;

        protected override CodeCompileUnit Parse()
        {
            var unit = new CodeCompileUnit();
            var codeNamespace = new CodeNamespace("LibreWinForms.SdkSmoke");
            var codeClass = new CodeTypeDeclaration("DesignerSmokeForm");
            codeClass.BaseTypes.Add(typeof(Forms.Form));
            codeClass.Members.Add(new CodeMemberField(typeof(Forms.ToolStripContainer), "toolStripContainer1"));

            var initializeComponent = new CodeMemberMethod
            {
                Name = "InitializeComponent"
            };
            var component = new CodeFieldReferenceExpression(new CodeThisReferenceExpression(), "toolStripContainer1");
            initializeComponent.Statements.Add(new CodeAssignStatement(
                component,
                new CodeObjectCreateExpression(typeof(Forms.ToolStripContainer))));
            initializeComponent.Statements.Add(new CodeAssignStatement(
                new CodePropertyReferenceExpression(component, nameof(Forms.Control.Name)),
                new CodePrimitiveExpression("toolStripContainer1")));
            initializeComponent.Statements.Add(new CodeExpressionStatement(
                new CodeMethodInvokeExpression(
                    new CodePropertyReferenceExpression(new CodeThisReferenceExpression(), nameof(Forms.Control.Controls)),
                    "Add",
                    component)));
            codeClass.Members.Add(initializeComponent);
            codeNamespace.Types.Add(codeClass);
            unit.Namespaces.Add(codeNamespace);
            return unit;
        }

        protected override void Write(CodeCompileUnit unit)
        {
            _writtenUnit = unit;
        }

        public bool ContainsPropertyAssignment(string componentName, string propertyName, object expectedValue)
        {
            if (_writtenUnit is null)
                return false;

            foreach (CodeNamespace codeNamespace in _writtenUnit.Namespaces)
            {
                foreach (CodeTypeDeclaration codeClass in codeNamespace.Types)
                {
                    foreach (CodeMemberMethod method in codeClass.Members.OfType<CodeMemberMethod>())
                    {
                        foreach (CodeAssignStatement assignment in method.Statements.OfType<CodeAssignStatement>())
                        {
                            if (assignment.Left is CodePropertyReferenceExpression property
                                && string.Equals(property.PropertyName, propertyName, StringComparison.Ordinal)
                                && property.TargetObject is CodeFieldReferenceExpression field
                                && field.TargetObject is CodeThisReferenceExpression
                                && string.Equals(field.FieldName, componentName, StringComparison.Ordinal)
                                && assignment.Right is CodePrimitiveExpression value
                                && Equals(value.Value, expectedValue))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }
    }
}

[Designer(typeof(DesignerSmokeTrackingDesigner), typeof(IDesigner))]
public sealed class DesignerSmokeAttributedComponent : Component
{
}

public sealed class DesignerSmokeTrackingDesigner : IDesigner
{
    public static bool Disposed { get; private set; }

    public static IComponent? DesignedComponent { get; private set; }

    public static bool Initialized { get; private set; }

    public IComponent Component { get; private set; } = null!;

    public DesignerVerbCollection Verbs { get; } = new();

    public static void Reset()
    {
        DesignedComponent = null;
        Disposed = false;
        Initialized = false;
    }

    public void Dispose()
    {
        Disposed = true;
        Component = null!;
    }

    public void DoDefaultAction()
    {
    }

    public void Initialize(IComponent component)
    {
        Component = component;
        DesignedComponent = component;
        Initialized = true;
    }
}
