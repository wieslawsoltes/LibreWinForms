using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Drawing;
using System.Drawing.Design;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.Design;
using System.Windows.Forms.Design.Behavior;

namespace System.ComponentModel.Design
{
    public class DesignSurface : IServiceProvider, IDisposable
    {
        private readonly IServiceProvider? _serviceProvider;
        private readonly Collection<Exception> _loadErrors = new();
        private PortableDesignerHost? _host;
        private DesignerLoader? _loader;
        private bool _disposed;
        private bool _loadCompletionRaised;
        private string[]? _reloadSelectionNames;

        public DesignSurface()
        {
            _host = new PortableDesignerHost(this, null);
        }

        public DesignSurface(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _host = new PortableDesignerHost(this, serviceProvider);
        }

        public event EventHandler? Flushed;
        public event EventHandler? Disposed;
        public event EventHandler<LoadedEventArgs>? Loaded;
        public event EventHandler? Loading;
        public event EventHandler? Unloaded;
        public event EventHandler? Unloading;

        public DesignSurfaceManager? Manager { get; internal set; }

        public bool IsLoaded { get; private set; }

        public ICollection LoadErrors => _loadErrors;

        public IContainer ComponentContainer
        {
            get
            {
                ObjectDisposedException.ThrowIf(_host is null, this);
                return _host;
            }
        }

        public object View { get; private set; } = new Panel();

        public void BeginLoad(DesignerLoader loader)
        {
            ArgumentNullException.ThrowIfNull(loader);

            BeginLoadCore(loader, replaceLoader: true);
        }

        private void BeginLoadCore(DesignerLoader loader, bool replaceLoader)
        {
            ObjectDisposedException.ThrowIf(_host is null, this);

            Loading?.Invoke(this, EventArgs.Empty);
            _loadErrors.Clear();
            _loadCompletionRaised = false;
            if (replaceLoader)
                _loader = loader;
            _host.BeginLoadState();

            try
            {
                loader.BeginLoad(_host);

                if (!_host.LoadCompleted && !loader.Loading)
                {
                    _host.EndLoad(_host.RootComponentClassName, true, Array.Empty<object>());
                }
            }
            catch (Exception ex)
            {
                if (!_host.LoadCompleted)
                    _host.EndLoad(_host.RootComponentClassName, false, new object[] { ex });
                else if (!_loadCompletionRaised)
                {
                    _loadErrors.Add(ex);
                    CompleteLoad(_host);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            try
            {
                try
                {
                    Unloading?.Invoke(this, EventArgs.Empty);
                }
                finally
                {
                    try
                    {
                        _loader?.Dispose();
                    }
                    finally
                    {
                        _loader = null;
                        try
                        {
                            _host?.Dispose();
                        }
                        finally
                        {
                            _host = null;
                            IsLoaded = false;
                            View = new Panel();
                            Unloaded?.Invoke(this, EventArgs.Empty);
                        }
                    }
                }
            }
            finally
            {
                Disposed?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Flush()
        {
            _loader?.Flush();
            Flushed?.Invoke(this, EventArgs.Empty);
        }

        public INestedContainer CreateNestedContainer(IComponent owningComponent)
        {
            return CreateNestedContainer(owningComponent, null);
        }

        public INestedContainer CreateNestedContainer(IComponent owningComponent, string? containerName)
        {
            ArgumentNullException.ThrowIfNull(owningComponent);
            ObjectDisposedException.ThrowIf(_host is null, this);

            return _host.CreateNestedContainer(owningComponent, containerName);
        }

        public object? GetService(Type serviceType)
        {
            if (_host is not null)
            {
                object? service = ((IServiceProvider)_host).GetService(serviceType);
                if (service is not null)
                    return service;
            }

            return _serviceProvider?.GetService(serviceType);
        }

        internal void CompleteLoad(PortableDesignerHost host)
        {
            if (_loadCompletionRaised || !ReferenceEquals(host, _host))
                return;

            _loadCompletionRaised = true;
            foreach (object? error in host.LoadErrors)
            {
                if (error is Exception exception)
                    _loadErrors.Add(exception);
                else if (error is not null)
                    _loadErrors.Add(new InvalidOperationException(error.ToString()));
            }

            IsLoaded = host.LoadSucceeded && _loadErrors.Count == 0;
            if (IsLoaded)
            {
                host.EnsureRootComponent();
                RestoreReloadSelection(host);
            }

            View = IsLoaded ? GetRootDesignerView(host) : new Panel();
            Loaded?.Invoke(this, new LoadedEventArgs(IsLoaded, _loadErrors));
        }

        internal void Reload(bool flush = true)
        {
            ObjectDisposedException.ThrowIf(_host is null, this);
            if (_loader is null)
                return;

            if (flush)
                Flush();
            _reloadSelectionNames = _host.GetSelectedComponentNames();
            Unloading?.Invoke(this, EventArgs.Empty);
            _host.ResetForReload();
            IsLoaded = false;
            View = new Panel();
            Unloaded?.Invoke(this, EventArgs.Empty);
            BeginLoadCore(_loader, replaceLoader: false);
        }

        private void RestoreReloadSelection(PortableDesignerHost host)
        {
            string[]? names = _reloadSelectionNames;
            _reloadSelectionNames = null;
            if (names is not { Length: > 0 })
                return;

            var restored = new List<object>(names.Length);
            foreach (string name in names)
            {
                if (host.GetInstance(name) is IComponent component)
                    restored.Add(component);
            }

            if (restored.Count > 0)
                host.SetSelectedComponents(restored, SelectionTypes.Replace);
        }

        internal void UnloadFailedDocument(PortableDesignerHost host)
        {
            if (!ReferenceEquals(host, _host))
                return;

            Unloading?.Invoke(this, EventArgs.Empty);
            host.ResetFailedLoad();
            IsLoaded = false;
            View = new Panel();
            Unloaded?.Invoke(this, EventArgs.Empty);
        }

        protected internal virtual IDesigner? CreateDesigner(IComponent component, bool rootDesigner)
        {
            ArgumentNullException.ThrowIfNull(component);
            ObjectDisposedException.ThrowIf(_host is null, this);

            IDesigner? designer = rootDesigner
                ? TypeDescriptor.CreateDesigner(component, typeof(IRootDesigner)) as IRootDesigner
                : TypeDescriptor.CreateDesigner(component, typeof(IDesigner));
            if (designer is not null)
                return designer;

            if (rootDesigner)
                return new PortableRootControlDesigner();
            if (component is Control control)
            {
                return PortableParentControlDesigner.Supports(control)
                    ? new PortableParentControlDesigner()
                    : new PortableControlDesigner();
            }

            return new PortableComponentDesigner();
        }

        private static object GetRootDesignerView(PortableDesignerHost host)
        {
            if (host.RootComponent is IComponent rootComponent
                && host.GetDesigner(rootComponent) is IRootDesigner rootDesigner
                && rootDesigner.SupportedTechnologies is { Length: > 0 } technologies)
            {
                return rootDesigner.GetView(technologies[0]);
            }

            return host.RootComponent as Control ?? new Panel();
        }
    }

    internal sealed class PortableDesignerHost : Container, IDesignerLoaderHost, IDesignerLoaderHost2, IDesignerSerializationManager, IComponentChangeService, ISelectionService, INameCreationService, ITypeDescriptorFilterService
    {
        private readonly DesignSurface _surface;
        private readonly IServiceProvider? _serviceProvider;
        private readonly Dictionary<IComponent, IDesigner> _designers = new();
        private readonly Dictionary<string, object> _instances = new(StringComparer.Ordinal);
        private readonly Dictionary<object, string> _names = new();
        private readonly Dictionary<Type, object> _services = new();
        private readonly Dictionary<Type, ServiceCreatorCallback> _serviceCreators = new();
        private readonly List<IDesignerSerializationProvider> _serializationProviders = new();
        private readonly List<object> _serializationErrors = new();
        private readonly List<object> _selection = new();
        private readonly PortableEventBindingService _eventBindingService;
        private readonly PortableDesignerSerializationService _designerSerializationService;
        private readonly PortableDesignerLayoutService _designerLayoutService;
        private PortableDesignerCommandSet? _standardCommandSet;
        private int _transactionDepth;
        private string _transactionDescription = string.Empty;
        private object? _serializationPropertyProvider;

        public PortableDesignerHost(DesignSurface surface, IServiceProvider? serviceProvider)
        {
            _surface = surface;
            _serviceProvider = serviceProvider;
            _eventBindingService = new PortableEventBindingService(this);
            _designerSerializationService = new PortableDesignerSerializationService(this);
            _designerLayoutService = new PortableDesignerLayoutService(this);
            _services[typeof(PortableDesignerLayoutService)] = _designerLayoutService;
        }

        public event EventHandler? Activated;
        public event EventHandler? Deactivated;
        public event EventHandler? LoadComplete;
        public event DesignerTransactionCloseEventHandler? TransactionClosed;
        public event DesignerTransactionCloseEventHandler? TransactionClosing;
        public event EventHandler? TransactionOpened;
        public event EventHandler? TransactionOpening;
        public event ComponentEventHandler? ComponentAdded;
        public event ComponentEventHandler? ComponentAdding;
        public event ComponentChangedEventHandler? ComponentChanged;
        public event ComponentChangingEventHandler? ComponentChanging;
        public event ComponentEventHandler? ComponentRemoved;
        public event ComponentEventHandler? ComponentRemoving;
        public event ComponentRenameEventHandler? ComponentRename;
        public event EventHandler? SelectionChanged;
        public event EventHandler? SelectionChanging;
        public event ResolveNameEventHandler? ResolveName;
        public event EventHandler? SerializationComplete;

        public bool LoadCompleted { get; private set; }

        public ICollection LoadErrors { get; private set; } = Array.Empty<object>();

        public bool LoadSucceeded { get; private set; }

        public bool CanReloadWithErrors { get; set; }

        public bool IgnoreErrorsDuringReload { get; set; }

        public IContainer Container => this;

        public bool InTransaction => _transactionDepth > 0;

        public bool Loading => !LoadCompleted;

        public IComponent? RootComponent { get; private set; }

        public string RootComponentClassName { get; private set; } = typeof(Panel).FullName!;

        public string TransactionDescription => _transactionDescription;

        public ContextStack Context { get; } = new();

        public PropertyDescriptorCollection Properties => _serializationPropertyProvider is null
            ? PropertyDescriptorCollection.Empty
            : TypeDescriptor.GetProperties(_serializationPropertyProvider);

        internal object? SerializationPropertyProvider
        {
            get => _serializationPropertyProvider;
            set => _serializationPropertyProvider = value;
        }

        internal IReadOnlyList<object> SerializationErrors => _serializationErrors;

        public object? PrimarySelection => _selection.Count > 0 ? _selection[0] : RootComponent;

        public int SelectionCount => _selection.Count;

        public void EnsureRootComponent()
        {
            if (RootComponent is not null)
                return;

            var rootComponent = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = System.Drawing.Color.White
            };
            Add(rootComponent, "Root");
            SetSelectedComponents(new object[] { rootComponent });
        }

        public void Activate()
        {
            Activated?.Invoke(this, EventArgs.Empty);
        }

        internal void Deactivate()
        {
            Deactivated?.Invoke(this, EventArgs.Empty);
        }

        public IComponent CreateComponent(Type componentClass)
        {
            return CreateComponent(componentClass, null);
        }

        public IComponent CreateComponent(Type componentClass, string? name)
        {
            ArgumentNullException.ThrowIfNull(componentClass);
            object? instance = Activator.CreateInstance(componentClass);
            if (instance is not IComponent component)
                throw new InvalidOperationException(componentClass.FullName + " is not a component.");

            string? componentName = string.IsNullOrWhiteSpace(name)
                ? CreateName(this, componentClass)
                : name;
            ValidateName(componentName);

            Add(component, componentName);
            return component;
        }

        public override void Add(IComponent? component, string? name)
        {
            if (component is null)
                return;
            if (ReferenceEquals(component.Site?.Container, this))
            {
                if (name is not null)
                    component.Site.Name = name;
                return;
            }

            string componentName = string.IsNullOrWhiteSpace(name)
                ? CreateName(this, component.GetType())
                : name;
            ValidateName(componentName);

            ComponentAdding?.Invoke(this, new ComponentEventArgs(component));
            base.Add(component, componentName);
            try
            {
                RegisterSiteComponent(component);
                bool isRootComponent = RootComponent is null;
                if (isRootComponent)
                {
                    RootComponent = component;
                    RootComponentClassName = component.GetType().FullName ?? component.GetType().Name;
                }

                InitializeDesigner(component, isRootComponent);

                ComponentAdded?.Invoke(this, new ComponentEventArgs(component));
            }
            catch
            {
                Remove(component);
                throw;
            }
        }

        public DesignerTransaction CreateTransaction()
        {
            return CreateTransaction(string.Empty);
        }

        public DesignerTransaction CreateTransaction(string description)
        {
            _transactionDepth++;
            _transactionDescription = description ?? string.Empty;
            TransactionOpening?.Invoke(this, EventArgs.Empty);
            TransactionOpened?.Invoke(this, EventArgs.Empty);
            return new PortableDesignerTransaction(_transactionDescription, CloseTransaction);
        }

        public void DestroyComponent(IComponent component)
        {
            if (component is null)
                return;

            Control? control = component as Control;
            component.Site?.Container?.Remove(component);
            if (control?.Parent is not null)
                control.Parent.Controls.Remove(control);
            component.Dispose();
        }

        public override void Remove(IComponent? component)
        {
            if (component is null || !ReferenceEquals(component.Site?.Container, this))
                return;

            PortableDesignerSite? site = component.Site as PortableDesignerSite;
            NotifyComponentRemoving(component);
            if (ReferenceEquals(RootComponent, component))
            {
                RootComponent = null;
                RootComponentClassName = typeof(Panel).FullName!;
            }

            RemoveWithoutUnsiting(component);
            try
            {
                NotifyComponentRemoved(component);
            }
            finally
            {
                site?.Dispose();
                component.Site = null;
            }
        }

        public void EndLoad(string baseClassName, bool successful, ICollection? errorCollection)
        {
            RootComponentClassName = string.IsNullOrEmpty(baseClassName) ? RootComponentClassName : baseClassName;
            LoadSucceeded = successful;
            LoadErrors = errorCollection ?? Array.Empty<object>();
            LoadCompleted = true;
            if (!successful)
                _surface.UnloadFailedDocument(this);
            _ = GetService(typeof(IMenuCommandService));
            if (successful)
                LoadComplete?.Invoke(this, EventArgs.Empty);
            _surface.CompleteLoad(this);
        }

        public IDesigner? GetDesigner(IComponent component)
        {
            ArgumentNullException.ThrowIfNull(component);
            return _designers.TryGetValue(component, out IDesigner? designer) ? designer : null;
        }

        public Type? GetType(string typeName)
        {
            if (_serviceProvider?.GetService(typeof(ITypeResolutionService)) is ITypeResolutionService resolver)
            {
                Type? resolved = resolver.GetType(typeName, false);
                if (resolved is not null)
                    return resolved;
            }

            return PortableWinFormsTypeResolver.Resolve(
                _serviceProvider?.GetService(typeof(ITypeResolutionService)) as ITypeResolutionService,
                typeName);
        }

        public void Reload()
        {
            _surface.Reload(flush: true);
        }

        internal void Reload(bool flush) => _surface.Reload(flush);

        internal void FlushSurface() => _surface.Flush();

        internal void BeginLoadState()
        {
            LoadCompleted = false;
            LoadSucceeded = false;
            LoadErrors = Array.Empty<object>();
            _serializationErrors.Clear();
        }

        internal void BeginSerializationOperation() => _serializationErrors.Clear();

        internal string[] GetSelectedComponentNames()
        {
            return _selection
                .OfType<IComponent>()
                .Select(GetName)
                .Where(static name => !string.IsNullOrEmpty(name))
                .Cast<string>()
                .ToArray();
        }

        internal void ResetForReload()
        {
            ResetDocument();
            BeginLoadState();
        }

        internal void ResetFailedLoad()
        {
            ResetDocument();
        }

        private void ResetDocument()
        {
            IComponent[] components = Components.Cast<IComponent>().ToArray();
            for (int i = components.Length - 1; i >= 0; i--)
                Remove(components[i]);
            for (int i = components.Length - 1; i >= 0; i--)
                components[i].Dispose();

            _selection.Clear();
            _instances.Clear();
            _names.Clear();
            _designers.Clear();
            _designerLayoutService.Reset();
            RootComponent = null;
            RootComponentClassName = typeof(Panel).FullName!;
        }

        public INestedContainer CreateNestedContainer(IComponent owningComponent, string? containerName)
        {
            ArgumentNullException.ThrowIfNull(owningComponent);
            IServiceProvider parentProvider = (IServiceProvider?)owningComponent.Site ?? this;
            return new PortableDesignerNestedContainer(owningComponent, this, parentProvider, containerName);
        }

        protected override object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IMenuCommandService))
                return GetMenuCommandService();

            if (_services.TryGetValue(serviceType, out object? service))
                return service;
            if (_serviceCreators.TryGetValue(serviceType, out ServiceCreatorCallback? creator))
            {
                service = creator(this, serviceType);
                if (service is not null)
                    _services[serviceType] = service;
                return service;
            }

            if (serviceType == typeof(IDesignerHost) || serviceType == typeof(IDesignerLoaderHost) || serviceType == typeof(IDesignerLoaderHost2))
                return this;
            if (serviceType == typeof(IContainer) || serviceType == typeof(IServiceContainer))
                return this;
            if (serviceType == typeof(IComponentChangeService))
                return this;
            if (serviceType == typeof(ISelectionService))
                return this;
            if (serviceType == typeof(ITypeDescriptorFilterService))
                return this;
            if (serviceType == typeof(IDesignerSerializationManager))
                return this;
            if (serviceType == typeof(IDesignerSerializationService))
                return _serviceProvider?.GetService(serviceType) ?? _designerSerializationService;
            if (serviceType == typeof(IEventBindingService))
                return _serviceProvider?.GetService(serviceType) ?? _eventBindingService;
            if (serviceType == typeof(INameCreationService))
                return _serviceProvider?.GetService(serviceType) ?? this;

            return _serviceProvider?.GetService(serviceType);
        }

        public bool FilterAttributes(IComponent component, IDictionary attributes)
        {
            ArgumentNullException.ThrowIfNull(component);
            ArgumentNullException.ThrowIfNull(attributes);
            bool hasDesigner = _designers.TryGetValue(component, out IDesigner? designer);
            if (designer is IDesignerFilter filter)
            {
                filter.PreFilterAttributes(attributes);
                filter.PostFilterAttributes(attributes);
            }

            // Attribute providers can be added to a live design-time instance.
            // Keep the composed attribute set dynamic so TypeDescriptor observes
            // those instance providers instead of retaining a pre-provider cache.
            return false;
        }

        public bool FilterEvents(IComponent component, IDictionary events)
        {
            ArgumentNullException.ThrowIfNull(component);
            ArgumentNullException.ThrowIfNull(events);
            bool hasDesigner = _designers.TryGetValue(component, out IDesigner? designer);
            if (designer is IDesignerFilter filter)
            {
                filter.PreFilterEvents(events);
                filter.PostFilterEvents(events);
            }

            return hasDesigner;
        }

        public bool FilterProperties(IComponent component, IDictionary properties)
        {
            ArgumentNullException.ThrowIfNull(component);
            ArgumentNullException.ThrowIfNull(properties);
            bool hasDesigner = _designers.TryGetValue(component, out IDesigner? designer);
            if (designer is IDesignerFilter filter)
            {
                filter.PreFilterProperties(properties);
                filter.PostFilterProperties(properties);
            }

            return hasDesigner;
        }

        private IMenuCommandService GetMenuCommandService()
        {
            object? configuredService = null;
            if (_services.TryGetValue(typeof(IMenuCommandService), out object? localService))
            {
                configuredService = localService;
            }
            else if (_serviceCreators.TryGetValue(typeof(IMenuCommandService), out ServiceCreatorCallback? creator))
            {
                configuredService = creator(this, typeof(IMenuCommandService));
                if (configuredService is not null)
                    _services[typeof(IMenuCommandService)] = configuredService;
            }
            else
            {
                configuredService = _serviceProvider?.GetService(typeof(IMenuCommandService));
            }

            IMenuCommandService menuCommandService = configuredService as IMenuCommandService
                ?? new MenuCommandService(this);
            if (configuredService is null)
                _services[typeof(IMenuCommandService)] = menuCommandService;

            if (_standardCommandSet is null
                || !ReferenceEquals(_standardCommandSet.MenuCommandService, menuCommandService))
            {
                _standardCommandSet?.Dispose();
                _standardCommandSet = new PortableDesignerCommandSet(this, menuCommandService);
            }

            return menuCommandService;
        }

        object? IServiceProvider.GetService(Type serviceType)
        {
            return GetService(serviceType);
        }

        public void AddService(Type serviceType, ServiceCreatorCallback callback)
        {
            AddService(serviceType, callback, false);
        }

        public void AddService(Type serviceType, ServiceCreatorCallback callback, bool promote)
        {
            ArgumentNullException.ThrowIfNull(serviceType);
            ArgumentNullException.ThrowIfNull(callback);

            if (promote && _serviceProvider is IServiceContainer parent)
            {
                parent.AddService(serviceType, callback, true);
                return;
            }

            _serviceCreators[serviceType] = callback;
            _services.Remove(serviceType);
            if (serviceType == typeof(IMenuCommandService))
            {
                _standardCommandSet?.Dispose();
                _standardCommandSet = null;
            }
        }

        public void AddService(Type serviceType, object serviceInstance)
        {
            AddService(serviceType, serviceInstance, false);
        }

        public void AddService(Type serviceType, object serviceInstance, bool promote)
        {
            ArgumentNullException.ThrowIfNull(serviceType);
            ArgumentNullException.ThrowIfNull(serviceInstance);

            if (promote && _serviceProvider is IServiceContainer parent)
            {
                parent.AddService(serviceType, serviceInstance, true);
                return;
            }

            _services[serviceType] = serviceInstance;
            _serviceCreators.Remove(serviceType);
            if (serviceType == typeof(IMenuCommandService))
            {
                _standardCommandSet?.Dispose();
                _standardCommandSet = null;
            }
        }

        public void RemoveService(Type serviceType)
        {
            RemoveService(serviceType, false);
        }

        public void RemoveService(Type serviceType, bool promote)
        {
            ArgumentNullException.ThrowIfNull(serviceType);

            if (promote && _serviceProvider is IServiceContainer parent)
            {
                parent.RemoveService(serviceType, true);
                return;
            }

            _services.Remove(serviceType);
            _serviceCreators.Remove(serviceType);
            if (serviceType == typeof(IMenuCommandService))
            {
                _standardCommandSet?.Dispose();
                _standardCommandSet = null;
            }
        }

        public void AddSerializationProvider(IDesignerSerializationProvider provider)
        {
            if (provider is not null && !_serializationProviders.Contains(provider))
                _serializationProviders.Add(provider);
        }

        public object? CreateInstance(Type type, ICollection? arguments, string? name, bool addToContainer)
        {
            object?[]? argumentArray = null;
            if (arguments is not null)
            {
                argumentArray = new object?[arguments.Count];
                arguments.CopyTo(argumentArray, 0);
            }

            object? instance = Activator.CreateInstance(type, argumentArray ?? Array.Empty<object>());
            if (instance is null)
                return null;

            if (!string.IsNullOrEmpty(name))
                SetName(instance, name);
            if (addToContainer && instance is IComponent component)
                Add(component, name);
            return instance;
        }

        public object? GetInstance(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            if (_instances.TryGetValue(name, out object? instance))
                return instance;

            for (int i = 0; i < Components.Count; i++)
            {
                if (Components[i]?.Site is PortableDesignerSite site
                    && site.TryGetNestedInstance(name, out instance))
                {
                    return instance;
                }
            }

            ResolveNameEventArgs args = new(name);
            ResolveName?.Invoke(this, args);
            return args.Value;
        }

        public string? GetName(object value)
        {
            if (value is null)
                return null;
            if (_names.TryGetValue(value, out string? name))
                return name;
            if (value is not IComponent component || component.Site is null)
                return null;

            return component.Site is INestedSite nestedSite ? nestedSite.FullName : component.Site.Name;
        }

        public object? GetSerializer(Type objectType, Type serializerType)
        {
            for (int i = _serializationProviders.Count - 1; i >= 0; i--)
            {
                object? serializer = _serializationProviders[i].GetSerializer(this, null, objectType, serializerType);
                if (serializer is not null)
                    return serializer;
            }

            return null;
        }

        internal CodeDomLocalizationModel GetLocalizationModel()
        {
            for (int i = _serializationProviders.Count - 1; i >= 0; i--)
            {
                if (_serializationProviders[i] is CodeDomLocalizationProvider localizationProvider)
                    return localizationProvider.Model;
            }

            return CodeDomLocalizationModel.None;
        }

        Type? IDesignerSerializationManager.GetType(string typeName)
        {
            return GetType(typeName);
        }

        public void RemoveSerializationProvider(IDesignerSerializationProvider provider)
        {
            _serializationProviders.Remove(provider);
        }

        public void ReportError(object errorInformation)
        {
            if (errorInformation is null)
                return;

            object error = errorInformation is Exception
                ? errorInformation
                : new InvalidOperationException(errorInformation.ToString());
            _serializationErrors.Add(error);
            LoadErrors = _serializationErrors.ToArray();
        }

        public void SetName(object instance, string name)
        {
            if (instance is null || string.IsNullOrEmpty(name))
                return;

            _instances[name] = instance;
            _names[instance] = name;
            if (instance is IComponent component && component.Site is not null)
                component.Site.Name = name;
        }

        protected override ISite CreateSite(IComponent component, string? name)
        {
            return new PortableDesignerSite(component, this, name);
        }

        private void RenameSiteComponent(
            IComponent component,
            IContainer container,
            string? oldName,
            string? newName,
            string? oldQualifiedName,
            string? newQualifiedName)
        {
            if (string.Equals(oldName, newName, StringComparison.Ordinal))
                return;

            if (!string.IsNullOrEmpty(newName))
            {
                ValidateName(newName);
                IComponent? existing = container.Components[newName];
                if (existing is not null && !ReferenceEquals(existing, component))
                    throw new ArgumentException("A component named '" + newName + "' already exists.", nameof(newName));
            }

            if (!string.IsNullOrEmpty(oldQualifiedName)
                && _instances.TryGetValue(oldQualifiedName, out object? oldInstance)
                && ReferenceEquals(oldInstance, component))
            {
                _instances.Remove(oldQualifiedName);
            }

            _names.Remove(component);
            if (!string.IsNullOrEmpty(newQualifiedName))
            {
                _instances[newQualifiedName] = component;
                _names[component] = newQualifiedName;
            }

            ComponentRename?.Invoke(this, new ComponentRenameEventArgs(component, oldName, newName));
        }

        private void RegisterSiteComponent(IComponent component)
        {
            RemoveName(component);
            string? name = component.Site is INestedSite nestedSite
                ? nestedSite.FullName
                : component.Site?.Name;
            if (string.IsNullOrEmpty(name))
                return;

            _instances[name] = component;
            _names[component] = name;
        }

        private void NotifyNestedComponentAdding(IContainer container, IComponent component)
        {
            ComponentAdding?.Invoke(container, new ComponentEventArgs(component));
        }

        private void NotifyNestedComponentAdded(IContainer container, IComponent component)
        {
            RegisterSiteComponent(component);
            InitializeDesigner(component, false);
            ComponentAdded?.Invoke(container, new ComponentEventArgs(component));
        }

        private void NotifyComponentRemoving(IComponent component)
        {
            ComponentRemoving?.Invoke(this, new ComponentEventArgs(component));
            DisposeDesigner(component);
        }

        private void NotifyComponentRemoved(IComponent component)
        {
            RemoveName(component);
            _eventBindingService.RemoveComponent(component);
            if (_selection.Contains(component))
            {
                SelectionChanging?.Invoke(this, EventArgs.Empty);
                _selection.Remove(component);
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }

            ComponentRemoved?.Invoke(this, new ComponentEventArgs(component));
        }

        private void RemoveName(object instance)
        {
            if (instance is null)
                return;

            if (_names.Remove(instance, out string? name))
            {
                _instances.Remove(name);
            }
        }

        private void InitializeDesigner(IComponent component, bool rootDesigner)
        {
            IDesigner? designer = _surface.CreateDesigner(component, rootDesigner);
            if (rootDesigner && designer is not IRootDesigner)
                throw new InvalidOperationException(component.GetType().FullName + " does not provide a root designer.");
            if (designer is null)
                return;

            _designers[component] = designer;
            try
            {
                designer.Initialize(component);
                if (designer.Component is null)
                    throw new InvalidOperationException(designer.GetType().FullName + " did not retain its component.");
            }
            catch
            {
                _designers.Remove(component);
                designer.Dispose();
                throw;
            }
        }

        private void DisposeDesigner(IComponent component)
        {
            if (!_designers.Remove(component, out IDesigner? designer))
                return;

            designer.Dispose();
        }

        public string CreateName(IContainer container, Type dataType)
        {
            ArgumentNullException.ThrowIfNull(dataType);

            string baseName = dataType.Name;
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = "component";
            baseName = char.ToLowerInvariant(baseName[0]) + baseName[1..];

            int index = 1;
            string candidate;
            do
            {
                candidate = baseName + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                index++;
            }
            while (!IsNameAvailable(container, candidate));

            return candidate;
        }

        public bool IsValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                !(char.IsLetter(name[0]) || name[0] == '_'))
            {
                return false;
            }

            for (int i = 1; i < name.Length; i++)
            {
                char ch = name[i];
                if (!char.IsLetterOrDigit(ch) && ch != '_')
                    return false;
            }

            return true;
        }

        public void ValidateName(string name)
        {
            if (!IsValidName(name))
                throw new ArgumentException("Invalid component name '" + name + "'.", nameof(name));
        }

        private bool IsNameAvailable(IContainer? container, string name)
        {
            if (_instances.ContainsKey(name))
                return false;

            return container?.Components[name] is null;
        }

        public void OnComponentChanged(object component, MemberDescriptor? member, object? oldValue, object? newValue)
        {
            ComponentChanged?.Invoke(this, new ComponentChangedEventArgs(component, member, oldValue, newValue));
        }

        public void OnComponentChanging(object component, MemberDescriptor? member)
        {
            ComponentChanging?.Invoke(this, new ComponentChangingEventArgs(component, member));
        }

        public bool GetComponentSelected(object component)
        {
            return _selection.Contains(component);
        }

        public ICollection GetSelectedComponents()
        {
            return _selection.ToArray();
        }

        public void SetSelectedComponents(ICollection components)
        {
            SetSelectedComponents(components, SelectionTypes.Replace);
        }

        public void SetSelectedComponents(ICollection components, SelectionTypes selectionType)
        {
            SelectionChanging?.Invoke(this, EventArgs.Empty);
            if ((selectionType & SelectionTypes.Replace) == SelectionTypes.Replace)
                _selection.Clear();
            foreach (object? component in components)
            {
                if (component is not null && !_selection.Contains(component))
                    _selection.Add(component);
            }
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void CloseTransaction(bool commit)
        {
            if (_transactionDepth > 0)
                _transactionDepth--;
            TransactionClosing?.Invoke(this, new DesignerTransactionCloseEventArgs(commit));
            TransactionClosed?.Invoke(this, new DesignerTransactionCloseEventArgs(commit));
            if (_transactionDepth == 0)
                _transactionDescription = string.Empty;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _standardCommandSet?.Dispose();
                _standardCommandSet = null;
                foreach (IDesigner designer in new List<IDesigner>(_designers.Values))
                    designer.Dispose();
                _designers.Clear();
                _designerLayoutService.Reset();

                for (int i = 0; i < Components.Count; i++)
                {
                    IComponent? component = Components[i];
                    if (component?.Site is PortableDesignerSite site)
                        site.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        private sealed class PortableDesignerCommandSet : IDisposable
        {
            private const string DesignerClipboardFormat = "CF_DESIGNERCOMPONENTS_V2";

            private readonly PortableDesignerHost _host;
            private readonly MenuCommand? _copyCommand;
            private readonly MenuCommand? _cutCommand;
            private readonly MenuCommand? _deleteCommand;
            private readonly MenuCommand? _pasteCommand;
            private readonly MenuCommand? _selectAllCommand;

            public PortableDesignerCommandSet(
                PortableDesignerHost host,
                IMenuCommandService menuCommandService)
            {
                _host = host;
                MenuCommandService = menuCommandService;

                if (menuCommandService.FindCommand(StandardCommands.Copy) is null)
                {
                    _copyCommand = new MenuCommand(OnCopy, StandardCommands.Copy);
                    menuCommandService.AddCommand(_copyCommand);
                }

                if (menuCommandService.FindCommand(StandardCommands.Cut) is null)
                {
                    _cutCommand = new MenuCommand(OnCut, StandardCommands.Cut);
                    menuCommandService.AddCommand(_cutCommand);
                }

                if (menuCommandService.FindCommand(StandardCommands.Delete) is null)
                {
                    _deleteCommand = new MenuCommand(OnDelete, StandardCommands.Delete);
                    menuCommandService.AddCommand(_deleteCommand);
                }

                if (menuCommandService.FindCommand(StandardCommands.Paste) is null)
                {
                    _pasteCommand = new MenuCommand(OnPaste, StandardCommands.Paste);
                    menuCommandService.AddCommand(_pasteCommand);
                }

                if (menuCommandService.FindCommand(StandardCommands.SelectAll) is null)
                {
                    _selectAllCommand = new MenuCommand(OnSelectAll, StandardCommands.SelectAll);
                    menuCommandService.AddCommand(_selectAllCommand);
                }

                _host.SelectionChanged += OnDesignerStateChanged;
                _host.Activated += OnDesignerStateChanged;
                _host.ComponentAdded += OnComponentStateChanged;
                _host.ComponentRemoved += OnComponentStateChanged;
                RefreshStatus();
            }

            public IMenuCommandService MenuCommandService { get; }

            public void Dispose()
            {
                _host.SelectionChanged -= OnDesignerStateChanged;
                _host.Activated -= OnDesignerStateChanged;
                _host.ComponentAdded -= OnComponentStateChanged;
                _host.ComponentRemoved -= OnComponentStateChanged;

                RemoveOwnedCommand(_copyCommand, StandardCommands.Copy);
                RemoveOwnedCommand(_cutCommand, StandardCommands.Cut);
                RemoveOwnedCommand(_deleteCommand, StandardCommands.Delete);
                RemoveOwnedCommand(_pasteCommand, StandardCommands.Paste);
                RemoveOwnedCommand(_selectAllCommand, StandardCommands.SelectAll);
            }

            private void OnDesignerStateChanged(object? sender, EventArgs e)
            {
                RefreshStatus();
            }

            private void OnComponentStateChanged(object? sender, ComponentEventArgs e)
            {
                RefreshStatus();
            }

            private void RefreshStatus()
            {
                IComponent[] copyableComponents = GetCopyableComponents();
                IComponent[] deletableComponents = GetDeletableComponents();
                if (_copyCommand is not null)
                    _copyCommand.Enabled = copyableComponents.Length > 0;
                if (_cutCommand is not null)
                    _cutCommand.Enabled = deletableComponents.Length > 0;
                if (_deleteCommand is not null)
                    _deleteCommand.Enabled = deletableComponents.Length > 0;
                if (_pasteCommand is not null)
                    _pasteCommand.Enabled = FindPasteParent() is not null && TryGetClipboardData(out _);
                if (_selectAllCommand is not null)
                    _selectAllCommand.Enabled = GetSelectableComponents().Length > 0;
            }

            private void OnCopy(object? sender, EventArgs e)
            {
                CopyComponentsToClipboard(GetCopyableComponents());
                RefreshStatus();
            }

            private void OnCut(object? sender, EventArgs e)
            {
                IComponent[] components = GetDeletableComponents();
                if (components.Length == 0 || !CopyComponentsToClipboard(components))
                    return;

                DeleteComponents(
                    components,
                    components.Length == 1 ? "Cut component" : "Cut components");
            }

            private void OnDelete(object? sender, EventArgs e)
            {
                IComponent[] components = GetDeletableComponents();
                if (components.Length == 0)
                    return;

                DeleteComponents(
                    components,
                    components.Length == 1 ? "Delete component" : "Delete components");
            }

            private void OnPaste(object? sender, EventArgs e)
            {
                Control? pasteParent = FindPasteParent();
                if (pasteParent is null
                    || !TryGetClipboardData(out object? serializationData)
                    || serializationData is null
                    || _host.GetService(typeof(IDesignerSerializationService)) is not IDesignerSerializationService serializer)
                {
                    return;
                }

                using DesignerTransaction transaction = _host.CreateTransaction("Paste components");
                var createdComponents = new List<IComponent>();
                bool commit = false;
                try
                {
                    ICollection deserialized = serializer.Deserialize(serializationData);
                    createdComponents.AddRange(deserialized.Cast<object>().OfType<IComponent>());
                    if (createdComponents.Count == 0)
                        return;

                    foreach (IComponent component in createdComponents)
                        ClearEventBindings(component);

                    var selectedComponents = new List<IComponent>();
                    foreach (IComponent component in createdComponents)
                    {
                        if (component is Control control)
                        {
                            if (control.Parent is not null)
                                continue;

                            pasteParent.Controls.Add(control);
                            OffsetPastedControl(control);
                            selectedComponents.Add(control);
                        }
                        else
                        {
                            selectedComponents.Add(component);
                        }
                    }

                    if (selectedComponents.Count == 0)
                        selectedComponents.AddRange(createdComponents);

                    _host.Activate();
                    _host.SetSelectedComponents(selectedComponents, SelectionTypes.Replace);
                    commit = true;
                }
                catch
                {
                    for (int i = createdComponents.Count - 1; i >= 0; i--)
                    {
                        IComponent component = createdComponents[i];
                        if (ReferenceEquals(component.Site?.Container, _host.Container))
                            _host.DestroyComponent(component);
                    }

                    throw;
                }
                finally
                {
                    if (commit)
                        transaction.Commit();
                    else
                        transaction.Cancel();
                    RefreshStatus();
                }
            }

            private bool CopyComponentsToClipboard(IComponent[] components)
            {
                if (components.Length == 0
                    || _host.GetService(typeof(IDesignerSerializationService)) is not IDesignerSerializationService serializer)
                {
                    return false;
                }

                object serializationData = serializer.Serialize(components);
                var dataObject = new DataObject();
                dataObject.SetData(DesignerClipboardFormat, serializationData);
                Clipboard.SetDataObject(dataObject);
                return true;
            }

            private void DeleteComponents(IComponent[] components, string transactionDescription)
            {
                IComponent? fallbackSelection = FindCommonControlParent(components) ?? _host.RootComponent;
                IComponent[] deletionComponents = ExpandDeletionComponents(components);
                using DesignerTransaction transaction = _host.CreateTransaction(transactionDescription);
                bool commit = false;
                try
                {
                    _host.SetSelectedComponents(Array.Empty<object>(), SelectionTypes.Replace);
                    foreach (IComponent component in deletionComponents.OrderBy(GetControlDepth))
                    {
                        if (ReferenceEquals(component.Site?.Container, _host.Container))
                            _host.OnComponentChanging(component, null);
                    }

                    foreach (IComponent component in deletionComponents.OrderByDescending(GetControlDepth))
                    {
                        if (!ReferenceEquals(component.Site?.Container, _host.Container))
                            continue;

                        _host.DestroyComponent(component);
                    }

                    if (fallbackSelection is not null
                        && (ReferenceEquals(fallbackSelection, _host.RootComponent)
                            || ReferenceEquals(fallbackSelection.Site?.Container, _host.Container)))
                    {
                        _host.SetSelectedComponents(
                            new object[] { fallbackSelection },
                            SelectionTypes.Replace);
                    }

                    commit = true;
                }
                finally
                {
                    if (commit)
                        transaction.Commit();
                    else
                        transaction.Cancel();
                    RefreshStatus();
                }
            }

            private void ClearEventBindings(IComponent component)
            {
                if (_host.GetService(typeof(IEventBindingService)) is not IEventBindingService eventBindingService)
                    return;

                PropertyDescriptorCollection eventProperties = eventBindingService.GetEventProperties(
                    TypeDescriptor.GetEvents(component));
                foreach (PropertyDescriptor eventProperty in eventProperties)
                {
                    if (!eventProperty.IsReadOnly && eventProperty.GetValue(component) is string)
                        eventProperty.SetValue(component, null);
                }
            }

            private IComponent[] ExpandDeletionComponents(IComponent[] components)
            {
                var deletionComponents = new HashSet<IComponent>(components);
                foreach (Control control in components.OfType<Control>())
                    AddSitedDescendants(control, deletionComponents);
                return deletionComponents.ToArray();
            }

            private void AddSitedDescendants(Control parent, HashSet<IComponent> components)
            {
                foreach (Control child in parent.Controls)
                {
                    if (ReferenceEquals(child.Site?.Container, _host.Container))
                        components.Add(child);
                    AddSitedDescendants(child, components);
                }
            }

            private void OnSelectAll(object? sender, EventArgs e)
            {
                IComponent[] components = GetSelectableComponents();
                _host.SetSelectedComponents(components, SelectionTypes.Replace);
                RefreshStatus();
            }

            private IComponent[] GetCopyableComponents()
            {
                return _host.GetSelectedComponents()
                    .Cast<object>()
                    .OfType<IComponent>()
                    .Where(component => !ReferenceEquals(component, _host.RootComponent)
                        && ReferenceEquals(component.Site?.Container, _host.Container))
                    .Distinct()
                    .ToArray();
            }

            private IComponent[] GetDeletableComponents()
            {
                var components = new List<IComponent>();
                foreach (object? selected in _host.GetSelectedComponents())
                {
                    if (selected is not IComponent component
                        || ReferenceEquals(component, _host.RootComponent)
                        || !ReferenceEquals(component.Site?.Container, _host.Container)
                        || IsInherited(component))
                    {
                        return Array.Empty<IComponent>();
                    }

                    components.Add(component);
                }

                return components.ToArray();
            }

            private Control? FindPasteParent()
            {
                Control? selectedControl = _host.PrimarySelection as Control;
                if (selectedControl is not null
                    && _host.GetDesigner(selectedControl) is PortableParentControlDesigner)
                {
                    return selectedControl;
                }

                return selectedControl?.Parent ?? _host.RootComponent as Control;
            }

            private bool TryGetClipboardData(out object? serializationData)
            {
                serializationData = null;
                IDataObject? dataObject = Clipboard.GetDataObject();
                if (dataObject is null || !dataObject.GetDataPresent(DesignerClipboardFormat))
                    return false;

                serializationData = dataObject.GetData(DesignerClipboardFormat);
                return serializationData is not null;
            }

            private void OffsetPastedControl(Control control)
            {
                if (control.Dock != DockStyle.None)
                    return;

                PropertyDescriptor? locationProperty = TypeDescriptor.GetProperties(control)[nameof(Control.Location)];
                if (locationProperty is null || locationProperty.IsReadOnly)
                    return;

                Point oldLocation = control.Location;
                Point newLocation = new(oldLocation.X + 10, oldLocation.Y + 10);
                _host.OnComponentChanging(control, locationProperty);
                locationProperty.SetValue(control, newLocation);
                _host.OnComponentChanged(control, locationProperty, oldLocation, newLocation);
            }

            private IComponent[] GetSelectableComponents()
            {
                return _host.Container.Components
                    .Cast<IComponent>()
                    .Where(component => !ReferenceEquals(component, _host.RootComponent))
                    .ToArray();
            }

            private static bool IsInherited(IComponent component)
            {
                return TypeDescriptor.GetAttributes(component)[typeof(InheritanceAttribute)] is InheritanceAttribute inheritance
                    && inheritance.InheritanceLevel != InheritanceLevel.NotInherited;
            }

            private void RemoveOwnedCommand(MenuCommand? command, CommandID commandId)
            {
                if (command is not null
                    && ReferenceEquals(MenuCommandService.FindCommand(commandId), command))
                {
                    MenuCommandService.RemoveCommand(command);
                }
            }

            private static int GetControlDepth(IComponent component)
            {
                int depth = 0;
                for (Control? parent = (component as Control)?.Parent; parent is not null; parent = parent.Parent)
                    depth++;
                return depth;
            }

            private static Control? FindCommonControlParent(IReadOnlyList<IComponent> components)
            {
                Control? commonParent = null;
                foreach (IComponent component in components)
                {
                    if (component is not Control control)
                        continue;

                    Control? parent = control.Parent;
                    if (commonParent is null)
                    {
                        commonParent = parent;
                        continue;
                    }

                    while (commonParent is not null
                        && !ReferenceEquals(commonParent, parent)
                        && (parent is null || !commonParent.Contains(parent)))
                    {
                        commonParent = commonParent.Parent;
                    }
                }

                return commonParent;
            }
        }

        private sealed class PortableDesignerTransaction : DesignerTransaction
        {
            private readonly Action<bool> _close;

            public PortableDesignerTransaction(string description, Action<bool> close)
                : base(description)
            {
                _close = close;
            }

            protected override void OnCancel()
            {
                _close(false);
            }

            protected override void OnCommit()
            {
                _close(true);
            }
        }

        private class PortableDesignerSite : ISite, IServiceContainer, IDictionaryService
        {
            private readonly IComponent _component;
            private readonly PortableDesignerHost _host;
            private readonly IContainer _container;
            private readonly PortableDesignerNestedContainer? _parentNestedContainer;
            private readonly Dictionary<object, object> _dictionary = new();
            private PortableDesignerNestedContainer? _nestedContainer;
            private string? _name;
            private bool _disposed;

            public PortableDesignerSite(
                IComponent component,
                PortableDesignerHost host,
                string? name,
                IContainer? container = null,
                PortableDesignerNestedContainer? parentNestedContainer = null)
            {
                _component = component;
                _host = host;
                _container = container ?? host;
                _parentNestedContainer = parentNestedContainer;
                _name = name;
            }

            public IComponent Component => _component;

            public IContainer Container => _container;

            public bool DesignMode => true;

            public string? Name
            {
                get => _name;
                set
                {
                    value ??= string.Empty;
                    if (string.Equals(_name, value, StringComparison.Ordinal))
                        return;

                    string? oldName = _name;
                    _host.RenameSiteComponent(
                        _component,
                        _container,
                        oldName,
                        value,
                        GetQualifiedName(oldName),
                        GetQualifiedName(value));
                    _name = value;
                    _nestedContainer?.RefreshComponentNames();
                }
            }

            protected virtual string? GetQualifiedName(string? name) => name;

            public object? GetService(Type serviceType)
            {
                ArgumentNullException.ThrowIfNull(serviceType);

                if (serviceType == typeof(ISite))
                    return this;
                if (serviceType == typeof(IDictionaryService))
                    return this;
                if (serviceType == typeof(INestedContainer))
                    return GetNestedContainer();
                if (serviceType == typeof(IServiceContainer) || serviceType == typeof(IContainer))
                    return ((IServiceProvider)_host).GetService(serviceType);

                if (_nestedContainer is not null)
                {
                    object? localService = _nestedContainer.GetServiceForSite(serviceType);
                    if (localService is not null)
                        return localService;
                }

                return _parentNestedContainer?.GetServiceForSite(serviceType)
                    ?? ((IServiceProvider)_host).GetService(serviceType);
            }

            object? IDictionaryService.GetKey(object? value)
            {
                if (value is null)
                    return null;

                foreach (KeyValuePair<object, object> pair in _dictionary)
                {
                    if (Equals(pair.Value, value))
                        return pair.Key;
                }

                return null;
            }

            object? IDictionaryService.GetValue(object key)
            {
                ArgumentNullException.ThrowIfNull(key);
                return _dictionary.TryGetValue(key, out object? value) ? value : null;
            }

            void IDictionaryService.SetValue(object key, object? value)
            {
                ArgumentNullException.ThrowIfNull(key);
                if (value is null)
                    _dictionary.Remove(key);
                else
                    _dictionary[key] = value;
            }

            void IServiceContainer.AddService(Type serviceType, ServiceCreatorCallback callback)
            {
                GetSiteServices().AddService(serviceType, callback);
            }

            void IServiceContainer.AddService(Type serviceType, ServiceCreatorCallback callback, bool promote)
            {
                GetSiteServices().AddService(serviceType, callback, promote);
            }

            void IServiceContainer.AddService(Type serviceType, object serviceInstance)
            {
                GetSiteServices().AddService(serviceType, serviceInstance);
            }

            void IServiceContainer.AddService(Type serviceType, object serviceInstance, bool promote)
            {
                GetSiteServices().AddService(serviceType, serviceInstance, promote);
            }

            void IServiceContainer.RemoveService(Type serviceType)
            {
                GetSiteServices().RemoveService(serviceType);
            }

            void IServiceContainer.RemoveService(Type serviceType, bool promote)
            {
                GetSiteServices().RemoveService(serviceType, promote);
            }

            private ServiceContainer GetSiteServices()
            {
                return GetNestedContainer().SiteServices;
            }

            private PortableDesignerNestedContainer GetNestedContainer()
            {
                return _nestedContainer ??= new PortableDesignerNestedContainer(
                    _component,
                    _host,
                    (IServiceProvider?)_parentNestedContainer ?? _host);
            }

            public bool TryGetNestedInstance(string name, out object? instance)
            {
                if (_nestedContainer is not null)
                    return _nestedContainer.TryGetComponent(name, out instance);

                instance = null;
                return false;
            }

            public void RefreshNestedComponentNames()
            {
                _nestedContainer?.RefreshComponentNames();
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _nestedContainer?.Dispose();
                _nestedContainer = null;
                _dictionary.Clear();
            }
        }

        private sealed class PortableNestedDesignerSite : PortableDesignerSite, INestedSite
        {
            private readonly PortableDesignerNestedContainer _container;

            public PortableNestedDesignerSite(
                IComponent component,
                PortableDesignerHost host,
                string? name,
                PortableDesignerNestedContainer container)
                : base(component, host, name, container, container)
            {
                _container = container;
            }

            public string? FullName => GetQualifiedName(Name);

            protected override string? GetQualifiedName(string? name)
            {
                if (name is null)
                    return null;

                string? ownerName = _container.OwnerNameValue;
                return string.IsNullOrEmpty(ownerName) ? name : ownerName + "." + name;
            }
        }

        private sealed class PortableDesignerNestedContainer : NestedContainer, IServiceProvider
        {
            private readonly PortableDesignerHost _host;
            private readonly IServiceProvider _parentProvider;
            private readonly string? _containerName;
            private ServiceContainer? _services;
            private bool _disposed;

            public PortableDesignerNestedContainer(
                IComponent owner,
                PortableDesignerHost host,
                IServiceProvider parentProvider,
                string? containerName = null)
                : base(owner)
            {
                _host = host;
                _parentProvider = parentProvider;
                _containerName = containerName;
                _ = SiteServices;
            }

            public ServiceContainer SiteServices => _services ??= new ServiceContainer(_parentProvider);

            public string? OwnerNameValue => OwnerName;

            protected override string? OwnerName
            {
                get
                {
                    string? ownerName = base.OwnerName;
                    if (string.IsNullOrEmpty(_containerName))
                        return ownerName;

                    return string.IsNullOrEmpty(ownerName)
                        ? _containerName
                        : ownerName + "." + _containerName;
                }
            }

            public override void Add(IComponent? component, string? name)
            {
                if (component is null)
                    return;
                if (ReferenceEquals(component.Site?.Container, this))
                {
                    if (name is not null)
                        component.Site.Name = name;
                    return;
                }

                string componentName = string.IsNullOrWhiteSpace(name)
                    ? _host.CreateName(this, component.GetType())
                    : name;
                _host.ValidateName(componentName);

                _host.NotifyNestedComponentAdding(this, component);
                base.Add(component, componentName);
                try
                {
                    _host.NotifyNestedComponentAdded(this, component);
                }
                catch
                {
                    Remove(component);
                    throw;
                }
            }

            public override void Remove(IComponent? component)
            {
                if (component is null || !ReferenceEquals(component.Site?.Container, this))
                    return;

                PortableDesignerSite? site = component.Site as PortableDesignerSite;
                _host.NotifyComponentRemoving(component);
                RemoveWithoutUnsiting(component);
                try
                {
                    _host.NotifyComponentRemoved(component);
                }
                finally
                {
                    site?.Dispose();
                    component.Site = null;
                }
            }

            protected override ISite CreateSite(IComponent component, string? name)
            {
                ArgumentNullException.ThrowIfNull(component);
                return new PortableNestedDesignerSite(component, _host, name, this);
            }

            protected override object? GetService(Type serviceType)
            {
                object? service = base.GetService(serviceType);
                if (service is not null)
                    return service;
                if (serviceType == typeof(IServiceContainer))
                    return SiteServices;

                return SiteServices.GetService(serviceType);
            }

            object? IServiceProvider.GetService(Type serviceType) => GetService(serviceType);

            public object? GetServiceForSite(Type serviceType) => GetService(serviceType);

            public bool TryGetComponent(string name, out object? instance)
            {
                for (int i = 0; i < Components.Count; i++)
                {
                    IComponent? component = Components[i];
                    if (component?.Site is null)
                        continue;

                    string? componentName = component.Site is INestedSite nestedSite
                        ? nestedSite.FullName
                        : component.Site.Name;
                    if (string.Equals(componentName, name, StringComparison.Ordinal))
                    {
                        instance = component;
                        return true;
                    }

                    if (component.Site is PortableDesignerSite site
                        && site.TryGetNestedInstance(name, out instance))
                    {
                        return true;
                    }
                }

                instance = null;
                return false;
            }

            public void RefreshComponentNames()
            {
                for (int i = 0; i < Components.Count; i++)
                {
                    IComponent? component = Components[i];
                    if (component is null)
                        continue;

                    _host.RegisterSiteComponent(component);
                    if (component.Site is PortableDesignerSite site)
                        site.RefreshNestedComponentNames();
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && !_disposed)
                {
                    _disposed = true;
                    for (int i = 0; i < Components.Count; i++)
                    {
                        IComponent? component = Components[i];
                        if (component?.Site is PortableDesignerSite site)
                            site.Dispose();
                        if (component is not null)
                            _host.RemoveName(component);
                    }

                    _services?.Dispose();
                    _services = null;
                }

                base.Dispose(disposing);
            }
        }
    }

    public class ComponentDesigner : IDesigner, IComponentInitializer, IDesignerFilter
    {
        private readonly DesignerVerbCollection _verbs = new();

        public IComponent Component { get; private set; } = null!;

        public virtual ICollection AssociatedComponents => Array.Empty<object>();

        protected virtual IComponent? ParentComponent => null;

        public virtual DesignerVerbCollection Verbs => _verbs;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
                Component = null!;
        }

        public virtual void DoDefaultAction()
        {
        }

        public virtual void Initialize(IComponent component)
        {
            ArgumentNullException.ThrowIfNull(component);
            Component = component;
        }

        protected object? GetService(Type serviceType)
        {
            return Component?.Site?.GetService(serviceType);
        }

        protected virtual void PreFilterAttributes(IDictionary attributes)
        {
        }

        protected virtual void PostFilterAttributes(IDictionary attributes)
        {
        }

        protected virtual void PreFilterEvents(IDictionary events)
        {
        }

        protected virtual void PostFilterEvents(IDictionary events)
        {
        }

        protected virtual void PreFilterProperties(IDictionary properties)
        {
        }

        protected virtual void PostFilterProperties(IDictionary properties)
        {
        }

        void IDesignerFilter.PreFilterAttributes(IDictionary attributes) => PreFilterAttributes(attributes);

        void IDesignerFilter.PostFilterAttributes(IDictionary attributes) => PostFilterAttributes(attributes);

        void IDesignerFilter.PreFilterEvents(IDictionary events) => PreFilterEvents(events);

        void IDesignerFilter.PostFilterEvents(IDictionary events) => PostFilterEvents(events);

        void IDesignerFilter.PreFilterProperties(IDictionary properties) => PreFilterProperties(properties);

        void IDesignerFilter.PostFilterProperties(IDictionary properties) => PostFilterProperties(properties);

        public virtual void InitializeExistingComponent(IDictionary? defaultValues)
        {
            ApplyDefaultValues(defaultValues);
        }

        public virtual void InitializeNewComponent(IDictionary? defaultValues)
        {
            ApplyDefaultValues(defaultValues);
        }

        protected virtual void ApplyDefaultValues(IDictionary? defaultValues)
        {
            if (Component is null || defaultValues is null)
                return;

            PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(Component);
            foreach (DictionaryEntry entry in defaultValues)
            {
                PropertyDescriptor? property = entry.Key switch
                {
                    PropertyDescriptor descriptor => descriptor,
                    string propertyName => properties[propertyName],
                    _ => null
                };
                if (property is not null && !property.IsReadOnly)
                    property.SetValue(Component, entry.Value);
            }
        }
    }

    internal class PortableComponentDesigner : ComponentDesigner
    {
    }

    internal class PortableControlDesigner : PortableComponentDesigner
    {
        private const int ResizeHandleSize = 6;
        private Control? _control;
        private PortableDesignerLayoutService? _layoutService;
        private PortableParentControlDesigner? _placementDesigner;
        private ToolboxItem? _placementTool;
        private Point _placementStart;
        private Control? _manipulationParent;
        private PortableDesignerPointerOperation _pointerOperation;
        private Point _pointerStart;
        private Rectangle _initialBounds;
        private Rectangle _initialManipulationBounds;
        private PortableDesignerMoveItem[] _moveItems = Array.Empty<PortableDesignerMoveItem>();
        private Control[] _movingControls = Array.Empty<Control>();
        private DesignerTransaction? _manipulationTransaction;
        private IComponentChangeService? _manipulationChangeService;
        private PropertyDescriptor? _locationProperty;
        private PropertyDescriptor? _sizeProperty;
        private bool _manipulationStarted;

        public override void Initialize(IComponent component)
        {
            base.Initialize(component);
            _control = (Control)component;
            _layoutService = GetService(typeof(PortableDesignerLayoutService)) as PortableDesignerLayoutService
                ?? throw new InvalidOperationException("The portable designer layout service is unavailable.");
            _control.AddDesignerMouseHandlers(OnDesignerMouseDown, OnDesignerMouseMove, OnDesignerMouseUp);
        }

        internal Control DesignedControl => _control
            ?? throw new InvalidOperationException("The portable control designer is not initialized.");

        internal virtual bool ParticipatesWithSnapLinesForLayout => true;

        internal virtual IList SnapLinesForLayout => CreateEdgeAndMarginSnapLines();

        internal PortableDesignerLayoutService LayoutService => _layoutService
            ?? throw new InvalidOperationException("The portable designer layout service is unavailable.");

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                FinishManipulation(commit: false);
                if (_control is not null)
                {
                    _control.RemoveDesignerMouseHandlers(OnDesignerMouseDown, OnDesignerMouseMove, OnDesignerMouseUp);
                    _control.Capture = false;
                }

                _control = null;
                _layoutService?.EndToolPlacement();
                _layoutService = null;
                _placementDesigner = null;
                _placementTool = null;
                _manipulationParent = null;
            }

            base.Dispose(disposing);
        }

        protected override void ApplyDefaultValues(IDictionary? defaultValues)
        {
            Control? control = Component as Control;
            Control? parent = defaultValues?["Parent"] as Control;
            base.ApplyDefaultValues(defaultValues);
            if (control is not null && parent is not null && !ReferenceEquals(control.Parent, parent))
                parent.Controls.Add(control);
        }

        private void OnDesignerMouseDown(object? sender, MouseEventArgs e)
        {
            if (_control is null || e.Button != MouseButtons.Left)
                return;

            IDesignerHost? host = GetService(typeof(IDesignerHost)) as IDesignerHost;
            IToolboxService? toolboxService = GetService(typeof(IToolboxService)) as IToolboxService;
            ToolboxItem? tool = host is null
                ? toolboxService?.GetSelectedToolboxItem()
                : toolboxService?.GetSelectedToolboxItem(host) ?? toolboxService?.GetSelectedToolboxItem();
            PortableParentControlDesigner? parentDesigner = tool is null || host is null
                ? null
                : FindParentDesigner(host, _control);
            if (tool is not null
                && parentDesigner is not null
                && parentDesigner.SupportsTool(tool)
                && PortableDesignerLayoutService.TryTranslatePoint(_control, parentDesigner.DesignedControl, e.Location, out Point parentPoint))
            {
                _placementDesigner = parentDesigner;
                _placementTool = tool;
                _placementStart = parentPoint;
                LayoutService.BeginToolPlacement(parentDesigner.DesignedControl);
                _control.Capture = true;
                return;
            }

            bool wasPrimarySelection = false;
            if (GetService(typeof(ISelectionService)) is ISelectionService selectionService)
            {
                wasPrimarySelection = ReferenceEquals(selectionService.PrimarySelection, _control);
                if (!wasPrimarySelection)
                    selectionService.SetSelectedComponents(new object[] { _control }, SelectionTypes.Replace);
            }

            BeginManipulation(host, wasPrimarySelection, e.Location);
        }

        private void OnDesignerMouseMove(object? sender, MouseEventArgs e)
        {
            if (_control is null)
                return;

            if (_placementDesigner is not null && _placementTool is not null)
            {
                if (PortableDesignerLayoutService.TryTranslatePoint(_control, _placementDesigner.DesignedControl, e.Location, out Point parentPoint))
                {
                    _placementDesigner.UpdateToolDrag(_placementStart, parentPoint);
                }

                return;
            }

            UpdateManipulation(e.Location);
        }

        private void OnDesignerMouseUp(object? sender, MouseEventArgs e)
        {
            if (_control is null || e.Button != MouseButtons.Left)
                return;

            PortableParentControlDesigner? parentDesigner = _placementDesigner;
            ToolboxItem? tool = _placementTool;
            _placementDesigner = null;
            _placementTool = null;

            if (parentDesigner is not null
                && tool is not null
                && PortableDesignerLayoutService.TryTranslatePoint(_control, parentDesigner.DesignedControl, e.Location, out Point parentPoint))
            {
                _control.Capture = false;
                parentDesigner.CreateTool(tool, _placementStart, parentPoint);
                return;
            }

            if (parentDesigner is not null || tool is not null)
                LayoutService.EndToolPlacement();

            FinishManipulation(commit: true);
        }

        private void BeginManipulation(IDesignerHost? host, bool wasPrimarySelection, Point location)
        {
            if (_control is null
                || host is null
                || ReferenceEquals(host.RootComponent, _control)
                || _control.Parent is not Control parent
                || _control.Dock != DockStyle.None
                || !PortableDesignerLayoutService.TryTranslatePoint(_control, parent, location, out Point parentPoint))
            {
                return;
            }

            PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(_control);
            bool canMove = TryCreateMoveItem(host, _control, parent, out PortableDesignerMoveItem primaryMoveItem);
            _locationProperty = canMove ? primaryMoveItem.LocationProperty : properties[nameof(Control.Location)];
            _sizeProperty = properties[nameof(Control.Size)];
            bool locked = IsLocked(_control, properties) || IsInheritedReadOnly(_control);
            bool canResize = !locked && !_control.AutoSize && _sizeProperty is { IsReadOnly: false };
            _pointerOperation = GetPointerOperation(location, wasPrimarySelection && canResize);
            if (_pointerOperation == PortableDesignerPointerOperation.Move && !canMove)
                _pointerOperation = PortableDesignerPointerOperation.None;
            if (_pointerOperation != PortableDesignerPointerOperation.Move && !canResize)
                _pointerOperation = canMove ? PortableDesignerPointerOperation.Move : PortableDesignerPointerOperation.None;
            if (_pointerOperation == PortableDesignerPointerOperation.None)
                return;

            _manipulationParent = parent;
            _pointerStart = parentPoint;
            _initialBounds = _control.Bounds;
            if (_pointerOperation == PortableDesignerPointerOperation.Move)
            {
                _moveItems = CreateMoveItems(host, _control, parent, primaryMoveItem);
                if (_moveItems.Length == 0)
                {
                    _pointerOperation = PortableDesignerPointerOperation.None;
                    _manipulationParent = null;
                    return;
                }

                _movingControls = new Control[_moveItems.Length];
                _initialManipulationBounds = _moveItems[0].InitialBounds;
                for (int index = 0; index < _moveItems.Length; index++)
                {
                    _movingControls[index] = _moveItems[index].Control;
                    if (index > 0)
                    {
                        _initialManipulationBounds = Rectangle.Union(
                            _initialManipulationBounds,
                            _moveItems[index].InitialBounds);
                    }
                }
            }
            else
            {
                _initialManipulationBounds = _initialBounds;
                _moveItems = Array.Empty<PortableDesignerMoveItem>();
                _movingControls = Array.Empty<Control>();
            }

            _manipulationStarted = false;
            if (_pointerOperation == PortableDesignerPointerOperation.Move)
            {
                LayoutService.BeginManipulation(
                    _control,
                    parent,
                    _initialManipulationBounds,
                    _movingControls);
            }
            else
            {
                LayoutService.BeginManipulation(_control, parent);
            }
            _control.Capture = true;
        }

        private void UpdateManipulation(Point location)
        {
            if (_control is null
                || _manipulationParent is null
                || _pointerOperation == PortableDesignerPointerOperation.None
                || !PortableDesignerLayoutService.TryTranslatePoint(_control, _manipulationParent, location, out Point parentPoint))
            {
                return;
            }

            int deltaX = parentPoint.X - _pointerStart.X;
            int deltaY = parentPoint.Y - _pointerStart.Y;
            Size dragSize = SystemInformation.DragSize;
            if (!_manipulationStarted
                && Math.Abs(deltaX) < dragSize.Width
                && Math.Abs(deltaY) < dragSize.Height)
            {
                return;
            }

            try
            {
                if (!_manipulationStarted)
                    StartManipulationTransaction();

                Rectangle manipulatedBounds = LayoutService.GetManipulatedBounds(
                    _control,
                    _pointerOperation == PortableDesignerPointerOperation.Move
                        ? _initialManipulationBounds
                        : _initialBounds,
                    _pointerOperation,
                    deltaX,
                    deltaY);
                if (_pointerOperation == PortableDesignerPointerOperation.Move)
                {
                    int snappedDeltaX = manipulatedBounds.Left - _initialManipulationBounds.Left;
                    int snappedDeltaY = manipulatedBounds.Top - _initialManipulationBounds.Top;
                    for (int index = 0; index < _moveItems.Length; index++)
                    {
                        PortableDesignerMoveItem item = _moveItems[index];
                        item.Control.Location = new Point(
                            item.InitialBounds.Left + snappedDeltaX,
                            item.InitialBounds.Top + snappedDeltaY);
                    }
                }
                else
                {
                    _control.Bounds = manipulatedBounds;
                }
            }
            catch
            {
                FinishManipulation(commit: false);
                throw;
            }
        }

        private void StartManipulationTransaction()
        {
            if (_control is null || _manipulationStarted)
                return;
            if (GetService(typeof(IDesignerHost)) is not IDesignerHost host)
                return;

            string transactionDescription = _pointerOperation == PortableDesignerPointerOperation.Move
                && _moveItems.Length > 1
                    ? $"Move {_moveItems.Length} controls"
                    : (_pointerOperation == PortableDesignerPointerOperation.Move ? "Move " : "Resize ")
                        + (_control.Site?.Name ?? _control.GetType().Name);
            _manipulationTransaction = host.CreateTransaction(transactionDescription);
            _manipulationChangeService = GetService(typeof(IComponentChangeService)) as IComponentChangeService;
            _manipulationStarted = true;
            if (_pointerOperation == PortableDesignerPointerOperation.Move)
            {
                for (int index = 0; index < _moveItems.Length; index++)
                {
                    PortableDesignerMoveItem item = _moveItems[index];
                    _manipulationChangeService?.OnComponentChanging(item.Control, item.LocationProperty);
                }
            }
            else if (PortableDesignerLayoutService.ChangesLocation(_pointerOperation) && _locationProperty is not null)
            {
                _manipulationChangeService?.OnComponentChanging(_control, _locationProperty);
            }
            if (PortableDesignerLayoutService.ChangesSize(_pointerOperation) && _sizeProperty is not null)
                _manipulationChangeService?.OnComponentChanging(_control, _sizeProperty);
        }

        private void FinishManipulation(bool commit)
        {
            Control? control = _control;
            if (control is not null)
                control.Capture = false;

            try
            {
                if (_manipulationStarted && control is not null)
                {
                    if (commit)
                    {
                        if (_pointerOperation == PortableDesignerPointerOperation.Move)
                        {
                            for (int index = 0; index < _moveItems.Length; index++)
                            {
                                PortableDesignerMoveItem item = _moveItems[index];
                                _manipulationChangeService?.OnComponentChanged(
                                    item.Control,
                                    item.LocationProperty,
                                    item.InitialBounds.Location,
                                    item.Control.Location);
                            }
                        }
                        else if (PortableDesignerLayoutService.ChangesLocation(_pointerOperation) && _locationProperty is not null)
                        {
                            _manipulationChangeService?.OnComponentChanged(
                                control,
                                _locationProperty,
                                _initialBounds.Location,
                                control.Location);
                        }

                        if (PortableDesignerLayoutService.ChangesSize(_pointerOperation) && _sizeProperty is not null)
                        {
                            _manipulationChangeService?.OnComponentChanged(
                                control,
                                _sizeProperty,
                                _initialBounds.Size,
                                control.Size);
                        }

                        _manipulationTransaction?.Commit();
                    }
                    else
                    {
                        if (_pointerOperation == PortableDesignerPointerOperation.Move)
                        {
                            for (int index = 0; index < _moveItems.Length; index++)
                            {
                                PortableDesignerMoveItem item = _moveItems[index];
                                item.Control.Bounds = item.InitialBounds;
                            }
                        }
                        else
                        {
                            control.Bounds = _initialBounds;
                        }

                        _manipulationTransaction?.Cancel();
                    }
                }
            }
            finally
            {
                _manipulationTransaction = null;
                _manipulationChangeService = null;
                _locationProperty = null;
                _sizeProperty = null;
                _moveItems = Array.Empty<PortableDesignerMoveItem>();
                _movingControls = Array.Empty<Control>();
                _initialManipulationBounds = Rectangle.Empty;
                _manipulationParent = null;
                _pointerOperation = PortableDesignerPointerOperation.None;
                _manipulationStarted = false;
                _layoutService?.EndManipulation(control);
            }
        }

        private PortableDesignerPointerOperation GetPointerOperation(Point location, bool canResize)
        {
            if (!canResize || _control is null)
                return PortableDesignerPointerOperation.Move;

            bool left = location.X <= ResizeHandleSize;
            bool right = location.X >= _control.Width - ResizeHandleSize;
            bool top = location.Y <= ResizeHandleSize;
            bool bottom = location.Y >= _control.Height - ResizeHandleSize;
            if (left && top)
                return PortableDesignerPointerOperation.ResizeTopLeft;
            if (right && top)
                return PortableDesignerPointerOperation.ResizeTopRight;
            if (left && bottom)
                return PortableDesignerPointerOperation.ResizeBottomLeft;
            if (right && bottom)
                return PortableDesignerPointerOperation.ResizeBottomRight;
            if (left)
                return PortableDesignerPointerOperation.ResizeLeft;
            if (right)
                return PortableDesignerPointerOperation.ResizeRight;
            if (top)
                return PortableDesignerPointerOperation.ResizeTop;
            if (bottom)
                return PortableDesignerPointerOperation.ResizeBottom;
            return PortableDesignerPointerOperation.Move;
        }

        private PortableDesignerMoveItem[] CreateMoveItems(
            IDesignerHost host,
            Control primary,
            Control parent,
            PortableDesignerMoveItem primaryItem)
        {
            var items = new List<PortableDesignerMoveItem>();
            if (GetService(typeof(ISelectionService)) is ISelectionService selectionService)
            {
                foreach (object? selected in selectionService.GetSelectedComponents())
                {
                    if (selected is Control control
                        && TryCreateMoveItem(host, control, parent, out PortableDesignerMoveItem item))
                    {
                        items.Add(item);
                    }
                }
            }

            if (items.Count == 0)
                items.Add(primaryItem);
            else if (!ReferenceEquals(items[0].Control, primary))
            {
                int primaryIndex = -1;
                for (int index = 1; index < items.Count; index++)
                {
                    if (ReferenceEquals(items[index].Control, primary))
                    {
                        primaryIndex = index;
                        break;
                    }
                }

                if (primaryIndex < 0)
                    items.Insert(0, primaryItem);
                else
                    (items[0], items[primaryIndex]) = (items[primaryIndex], items[0]);
            }

            return items.ToArray();
        }

        private static bool TryCreateMoveItem(
            IDesignerHost host,
            Control control,
            Control parent,
            out PortableDesignerMoveItem item)
        {
            PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(control);
            PropertyDescriptor? locationProperty = properties[nameof(Control.Location)];
            bool designerAllowsMove = host.GetDesigner(control) is not ControlDesigner controlDesigner
                || (controlDesigner.SelectionRules & (SelectionRules.Moveable | SelectionRules.Locked)) == SelectionRules.Moveable;
            if (!ReferenceEquals(control.Parent, parent)
                || !ReferenceEquals(control.Site?.GetService(typeof(IDesignerHost)), host)
                || control.Dock != DockStyle.None
                || locationProperty is null
                || locationProperty.IsReadOnly
                || IsInheritedReadOnly(control)
                || IsLocked(control, properties)
                || !designerAllowsMove)
            {
                item = default;
                return false;
            }

            item = new PortableDesignerMoveItem(control, control.Bounds, locationProperty);
            return true;
        }

        private static bool IsLocked(Control control, PropertyDescriptorCollection properties)
        {
            return properties["Locked"]?.GetValue(control) is true;
        }

        private static bool IsInheritedReadOnly(Control control)
        {
            return TypeDescriptor.GetAttributes(control)[typeof(InheritanceAttribute)]
                is InheritanceAttribute { InheritanceLevel: InheritanceLevel.InheritedReadOnly };
        }

        private ArrayList CreateEdgeAndMarginSnapLines()
        {
            Control control = DesignedControl;
            int width = control.Width;
            int height = control.Height;
            Padding margin = control.Margin;
            return new ArrayList(8)
            {
                new SnapLine(SnapLineType.Top, 0, SnapLinePriority.Low),
                new SnapLine(SnapLineType.Bottom, height - 1, SnapLinePriority.Low),
                new SnapLine(SnapLineType.Left, 0, SnapLinePriority.Low),
                new SnapLine(SnapLineType.Right, width - 1, SnapLinePriority.Low),
                new SnapLine(SnapLineType.Horizontal, -margin.Top, SnapLine.MarginTop, SnapLinePriority.Always),
                new SnapLine(SnapLineType.Horizontal, margin.Bottom + height, SnapLine.MarginBottom, SnapLinePriority.Always),
                new SnapLine(SnapLineType.Vertical, -margin.Left, SnapLine.MarginLeft, SnapLinePriority.Always),
                new SnapLine(SnapLineType.Vertical, margin.Right + width, SnapLine.MarginRight, SnapLinePriority.Always)
            };
        }

        private static PortableParentControlDesigner? FindParentDesigner(IDesignerHost host, Control source)
        {
            for (Control? current = source; current is not null; current = current.Parent)
            {
                if (host.GetDesigner(current) is PortableParentControlDesigner designer)
                    return designer;
            }

            return host.RootComponent is IComponent root
                ? host.GetDesigner(root) as PortableParentControlDesigner
                : null;
        }

        private readonly struct PortableDesignerMoveItem
        {
            internal PortableDesignerMoveItem(
                Control control,
                Rectangle initialBounds,
                PropertyDescriptor locationProperty)
            {
                Control = control;
                InitialBounds = initialBounds;
                LocationProperty = locationProperty;
            }

            internal Control Control { get; }

            internal Rectangle InitialBounds { get; }

            internal PropertyDescriptor LocationProperty { get; }
        }

    }

    internal class PortableParentControlDesigner : PortableControlDesigner
    {
        private Rectangle _dragBounds;

        internal override IList SnapLinesForLayout
        {
            get
            {
                IList snapLines = base.SnapLinesForLayout;
                Rectangle displayRectangle = DesignedControl.DisplayRectangle;
                snapLines.Add(new SnapLine(SnapLineType.Vertical, displayRectangle.Left, SnapLine.PaddingLeft, SnapLinePriority.Always));
                snapLines.Add(new SnapLine(SnapLineType.Vertical, displayRectangle.Right, SnapLine.PaddingRight, SnapLinePriority.Always));
                snapLines.Add(new SnapLine(SnapLineType.Horizontal, displayRectangle.Top, SnapLine.PaddingTop, SnapLinePriority.Always));
                snapLines.Add(new SnapLine(SnapLineType.Horizontal, displayRectangle.Bottom, SnapLine.PaddingBottom, SnapLinePriority.Always));
                return snapLines;
            }
        }

        internal static bool Supports(Control control)
        {
            return control is ContainerControl or Panel or GroupBox;
        }

        internal bool SupportsTool(ToolboxItem tool)
        {
            ArgumentNullException.ThrowIfNull(tool);
            IDesignerHost? host = GetService(typeof(IDesignerHost)) as IDesignerHost;
            Type? toolType = tool.GetType(host);
            return toolType is not null && typeof(IComponent).IsAssignableFrom(toolType);
        }

        internal void UpdateToolDrag(Point start, Point current)
        {
            _dragBounds = LayoutService.GetToolBounds(DesignedControl, start, current);
            DesignedControl.Invalidate(_dragBounds);
        }

        internal IComponent[] CreateTool(ToolboxItem tool, Point start, Point end)
        {
            ArgumentNullException.ThrowIfNull(tool);
            if (GetService(typeof(IDesignerHost)) is not IDesignerHost host)
            {
                LayoutService.EndToolPlacement();
                return Array.Empty<IComponent>();
            }

            Rectangle bounds = LayoutService.GetToolBounds(DesignedControl, start, end);
            Size dragSize = SystemInformation.DragSize;
            bool hasSize = bounds.Width >= dragSize.Width || bounds.Height >= dragSize.Height;
            var defaultValues = new Hashtable
            {
                ["Parent"] = DesignedControl,
                [nameof(Control.Location)] = hasSize
                    ? bounds.Location
                    : LayoutService.GetToolLocation(DesignedControl, start)
            };
            if (hasSize)
            {
                defaultValues[nameof(Control.Size)] = new Size(
                    Math.Max(bounds.Width, dragSize.Width * 2),
                    Math.Max(bounds.Height, dragSize.Height * 2));
            }

            DesignerTransaction? transaction = host.CreateTransaction("Create " + tool.DisplayName);
            bool commit = false;
            try
            {
                IComponent[] components = tool.CreateComponents(host, defaultValues);
                if (!hasSize)
                {
                    foreach (Control control in components.OfType<Control>())
                    {
                        if (control.Size.IsEmpty && !control.DefaultSizeForDesigner.IsEmpty)
                            control.Size = control.DefaultSizeForDesigner;
                    }
                }

                if (components.Length > 0
                    && GetService(typeof(ISelectionService)) is ISelectionService selectionService)
                {
                    host.Activate();
                    selectionService.SetSelectedComponents(components, SelectionTypes.Replace);
                }

                commit = true;
                return components;
            }
            finally
            {
                if (GetService(typeof(IToolboxService)) is IToolboxService toolboxService
                    && ReferenceEquals(toolboxService.GetSelectedToolboxItem(host) ?? toolboxService.GetSelectedToolboxItem(), tool))
                {
                    toolboxService.SelectedToolboxItemUsed();
                }

                if (transaction is not null)
                {
                    if (commit)
                        transaction.Commit();
                    else
                        transaction.Cancel();
                }

                _dragBounds = Rectangle.Empty;
                DesignedControl.Invalidate();
                LayoutService.EndToolPlacement();
            }
        }

        internal IComponent[] CreateToolCentered(ToolboxItem tool)
        {
            Point center = new(
                DesignedControl.ClientRectangle.Width / 2,
                DesignedControl.ClientRectangle.Height / 2);
            IComponent[] components = CreateTool(tool, center, center);
            foreach (Control control in components.OfType<Control>())
            {
                Point location = new(
                    Math.Max(0, center.X - control.Width / 2),
                    Math.Max(0, center.Y - control.Height / 2));
                control.Location = LayoutService.GetToolLocation(DesignedControl, location);
                LayoutService.EndToolPlacement();
            }

            return components;
        }
    }

    internal sealed class PortableRootControlDesigner : PortableParentControlDesigner, IRootDesigner, IToolboxUser
    {
        private static readonly ViewTechnology[] s_supportedTechnologies = { ViewTechnology.Default };

        public ViewTechnology[] SupportedTechnologies => (ViewTechnology[])s_supportedTechnologies.Clone();

        public object GetView(ViewTechnology technology)
        {
            if (technology != ViewTechnology.Default)
                throw new ArgumentException("Unsupported designer view technology.", nameof(technology));

            return Component as Control ?? new Panel();
        }

        public bool GetToolSupported(ToolboxItem tool)
        {
            return SupportsTool(tool);
        }

        public void ToolPicked(ToolboxItem tool)
        {
            ArgumentNullException.ThrowIfNull(tool);
            PortableParentControlDesigner target = GetSelectedParentDesigner() ?? this;
            if (target.SupportsTool(tool))
                target.CreateToolCentered(tool);
        }

        private PortableParentControlDesigner? GetSelectedParentDesigner()
        {
            if (GetService(typeof(IDesignerHost)) is not IDesignerHost host)
                return null;

            Control? selectedControl = (GetService(typeof(ISelectionService)) as ISelectionService)?.PrimarySelection as Control;
            for (Control? current = selectedControl; current is not null; current = current.Parent)
            {
                if (host.GetDesigner(current) is PortableParentControlDesigner designer)
                    return designer;
            }

            return null;
        }
    }

    public class DesignSurfaceManager : IServiceProvider, IDisposable
    {
        private readonly List<DesignSurface> _surfaces = new();
        private DesignSurface? _activeDesignSurface;

        public DesignSurface? ActiveDesignSurface
        {
            get => _activeDesignSurface;
            set
            {
                if (ReferenceEquals(_activeDesignSurface, value))
                {
                    ActivateSurface(value);
                    return;
                }

                if (_activeDesignSurface?.GetService(typeof(IDesignerHost)) is PortableDesignerHost oldHost)
                    oldHost.Deactivate();

                _activeDesignSurface = value;
                ActivateSurface(value);
            }
        }

        public DesignSurface CreateDesignSurface(IServiceProvider serviceProvider)
        {
            var surface = new DesignSurface(serviceProvider)
            {
                Manager = this
            };
            surface.Disposed += OnSurfaceDisposed;
            _surfaces.Add(surface);
            return surface;
        }

        public void Dispose()
        {
            foreach (DesignSurface surface in _surfaces.ToArray())
            {
                surface.Dispose();
            }

            _surfaces.Clear();
            ActiveDesignSurface = null;
        }

        public object? GetService(Type serviceType)
        {
            return ActiveDesignSurface?.GetService(serviceType);
        }

        private static void ActivateSurface(DesignSurface? surface)
        {
            if (surface?.GetService(typeof(IDesignerHost)) is IDesignerHost host)
                host.Activate();
        }

        private void OnSurfaceDisposed(object? sender, EventArgs e)
        {
            if (sender is not DesignSurface surface)
                return;

            surface.Disposed -= OnSurfaceDisposed;
            _surfaces.Remove(surface);
            surface.Manager = null;
            if (ReferenceEquals(_activeDesignSurface, surface))
                ActiveDesignSurface = null;
        }
    }

    public sealed class LoadedEventArgs : EventArgs
    {
        public LoadedEventArgs(bool hasSucceeded, ICollection? errors)
        {
            HasSucceeded = hasSucceeded;
            Errors = errors ?? Array.Empty<object>();
        }

        public ICollection Errors { get; }

        public bool HasSucceeded { get; }
    }

    public abstract class EventBindingService : IEventBindingService
    {
        private readonly IServiceProvider _provider;
        private readonly Dictionary<IComponent, Dictionary<string, string>> _eventMethods = new();

        protected EventBindingService(IServiceProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        protected abstract string CreateUniqueMethodName(IComponent component, EventDescriptor e);

        protected virtual void FreeMethod(IComponent component, EventDescriptor e, string methodName)
        {
        }

        protected abstract ICollection GetCompatibleMethods(EventDescriptor e);

        protected object? GetService(Type serviceType)
        {
            return _provider.GetService(serviceType);
        }

        protected abstract bool ShowCode();

        protected abstract bool ShowCode(int lineNumber);

        protected abstract bool ShowCode(IComponent component, EventDescriptor e, string methodName);

        protected virtual void UseMethod(IComponent component, EventDescriptor e, string methodName)
        {
        }

        protected virtual void ValidateMethodName(string methodName)
        {
        }

        string IEventBindingService.CreateUniqueMethodName(IComponent component, EventDescriptor e)
        {
            ArgumentNullException.ThrowIfNull(component);
            ArgumentNullException.ThrowIfNull(e);
            return CreateUniqueMethodName(component, e);
        }

        ICollection IEventBindingService.GetCompatibleMethods(EventDescriptor e)
        {
            ArgumentNullException.ThrowIfNull(e);
            return GetCompatibleMethods(e);
        }

        EventDescriptor? IEventBindingService.GetEvent(PropertyDescriptor property)
        {
            return property is EventPropertyDescriptor eventProperty ? eventProperty.Event : null;
        }

        PropertyDescriptorCollection IEventBindingService.GetEventProperties(EventDescriptorCollection events)
        {
            ArgumentNullException.ThrowIfNull(events);
            PropertyDescriptor[] properties = new PropertyDescriptor[events.Count];
            for (int i = 0; i < events.Count; i++)
            {
                properties[i] = new EventPropertyDescriptor(events[i]!, this);
            }

            return new PropertyDescriptorCollection(properties);
        }

        PropertyDescriptor IEventBindingService.GetEventProperty(EventDescriptor e)
        {
            ArgumentNullException.ThrowIfNull(e);
            return new EventPropertyDescriptor(e, this);
        }

        bool IEventBindingService.ShowCode()
        {
            return ShowCode();
        }

        bool IEventBindingService.ShowCode(int lineNumber)
        {
            return ShowCode(lineNumber);
        }

        bool IEventBindingService.ShowCode(IComponent component, EventDescriptor e)
        {
            ArgumentNullException.ThrowIfNull(component);
            ArgumentNullException.ThrowIfNull(e);

            string? methodName = GetEventMethodName(component, e);
            bool createdMethodBinding = false;
            if (string.IsNullOrWhiteSpace(methodName))
            {
                methodName = CreateUniqueMethodName(component, e);
                if (string.IsNullOrWhiteSpace(methodName))
                    return false;

                SetEventMethodName(component, e, methodName);
                createdMethodBinding = true;
            }

            bool shown = ShowCode(component, e, methodName);
            if (!shown && createdMethodBinding)
                SetEventMethodName(component, e, null);

            return shown;
        }

        private string? GetEventMethodName(IComponent component, EventDescriptor e)
        {
            return _eventMethods.TryGetValue(component, out Dictionary<string, string>? componentEvents)
                && componentEvents.TryGetValue(e.Name, out string? methodName)
                    ? methodName
                    : null;
        }

        internal PortableEventBinding[] GetEventBindings()
        {
            List<PortableEventBinding> bindings = new();
            foreach (KeyValuePair<IComponent, Dictionary<string, string>> componentEntry in _eventMethods)
            {
                EventDescriptorCollection events = TypeDescriptor.GetEvents(componentEntry.Key);
                foreach (KeyValuePair<string, string> eventEntry in componentEntry.Value)
                {
                    EventDescriptor? eventDescriptor = events.Find(eventEntry.Key, false);
                    if (eventDescriptor is not null && !string.IsNullOrWhiteSpace(eventEntry.Value))
                    {
                        bindings.Add(new PortableEventBinding(componentEntry.Key, eventDescriptor, eventEntry.Value));
                    }
                }
            }

            return bindings.ToArray();
        }

        internal void SetEventMethodName(IComponent component, string eventName, string? methodName)
        {
            if (string.IsNullOrWhiteSpace(eventName))
                return;

            EventDescriptor? eventDescriptor = TypeDescriptor.GetEvents(component).Find(eventName, false);
            if (eventDescriptor is not null)
                SetEventMethodName(component, eventDescriptor, methodName);
        }

        internal void RemoveComponent(IComponent component)
        {
            _eventMethods.Remove(component);
        }

        private void SetEventMethodName(IComponent component, EventDescriptor e, string? methodName)
        {
            string? oldMethodName = GetEventMethodName(component, e);
            if (string.Equals(oldMethodName, methodName, StringComparison.Ordinal))
                return;

            if (!string.IsNullOrEmpty(oldMethodName))
                FreeMethod(component, e, oldMethodName);

            if (string.IsNullOrEmpty(methodName))
            {
                if (_eventMethods.TryGetValue(component, out Dictionary<string, string>? componentEvents))
                {
                    componentEvents.Remove(e.Name);
                    if (componentEvents.Count == 0)
                        _eventMethods.Remove(component);
                }
                return;
            }

            ValidateMethodName(methodName);
            if (!_eventMethods.TryGetValue(component, out Dictionary<string, string>? events))
            {
                events = new Dictionary<string, string>(StringComparer.Ordinal);
                _eventMethods.Add(component, events);
            }

            events[e.Name] = methodName;
            UseMethod(component, e, methodName);
        }

        private sealed class EventPropertyDescriptor : PropertyDescriptor
        {
            private readonly EventBindingService _owner;

            public EventPropertyDescriptor(EventDescriptor @event, EventBindingService owner)
                : base(@event.Name, null)
            {
                Event = @event;
                _owner = owner;
            }

            public EventDescriptor Event { get; }

            public override Type ComponentType => Event.ComponentType!;

            public override bool IsReadOnly => false;

            public override Type PropertyType => typeof(string);

            public override bool CanResetValue(object component)
            {
                return component is IComponent target && _owner.GetEventMethodName(target, Event) is not null;
            }

            public override object? GetValue(object? component)
            {
                return component is IComponent target ? _owner.GetEventMethodName(target, Event) : null;
            }

            public override void ResetValue(object component)
            {
                if (component is IComponent target)
                    _owner.SetEventMethodName(target, Event, null);
            }

            public override void SetValue(object? component, object? value)
            {
                if (component is IComponent target)
                    _owner.SetEventMethodName(target, Event, value as string);
            }

            public override bool ShouldSerializeValue(object component)
            {
                return CanResetValue(component);
            }
        }
    }

    internal readonly struct PortableEventBinding
    {
        public PortableEventBinding(IComponent component, EventDescriptor eventDescriptor, string methodName)
        {
            Component = component;
            Event = eventDescriptor;
            MethodName = methodName;
        }

        public IComponent Component { get; }

        public EventDescriptor Event { get; }

        public string MethodName { get; }
    }

    internal sealed class PortableEventBindingService : EventBindingService
    {
        public PortableEventBindingService(IServiceProvider provider)
            : base(provider)
        {
        }

        protected override string CreateUniqueMethodName(IComponent component, EventDescriptor e)
        {
            string? componentName = component.Site?.Name;
            if (string.IsNullOrWhiteSpace(componentName))
                componentName = component.GetType().Name;

            return componentName + "_" + e.Name;
        }

        protected override ICollection GetCompatibleMethods(EventDescriptor e)
        {
            return Array.Empty<string>();
        }

        protected override bool ShowCode()
        {
            return false;
        }

        protected override bool ShowCode(int lineNumber)
        {
            return false;
        }

        protected override bool ShowCode(IComponent component, EventDescriptor e, string methodName)
        {
            return false;
        }

        protected override void ValidateMethodName(string methodName)
        {
            if (string.IsNullOrWhiteSpace(methodName))
                throw new ArgumentException("Method name must not be empty.", nameof(methodName));
        }
    }

    public class MenuCommandService : IMenuCommandService
    {
        private readonly Dictionary<CommandID, MenuCommand> _commands = new();
        private readonly IServiceProvider? _serviceProvider;
        private readonly DesignerVerbCollection _verbs = new();

        public MenuCommandService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public virtual DesignerVerbCollection Verbs => _verbs;

        public virtual void AddCommand(MenuCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);
            _commands[command.CommandID] = command;
            if (command is DesignerVerb verb && !_verbs.Contains(verb))
            {
                _verbs.Add(verb);
            }
        }

        public virtual void AddVerb(DesignerVerb verb)
        {
            ArgumentNullException.ThrowIfNull(verb);
            _verbs.Add(verb);
            AddCommand(verb);
        }

        public virtual MenuCommand? FindCommand(CommandID commandID)
        {
            _commands.TryGetValue(commandID, out MenuCommand? command);
            return command;
        }

        public virtual bool GlobalInvoke(CommandID commandID)
        {
            MenuCommand? command = FindCommand(commandID);
            if (command is null || !command.Enabled)
            {
                return false;
            }

            command.Invoke();
            return true;
        }

        public virtual void RemoveCommand(MenuCommand command)
        {
            if (command is null)
            {
                return;
            }

            _commands.Remove(command.CommandID);
        }

        public virtual void RemoveVerb(DesignerVerb verb)
        {
            if (verb is null)
            {
                return;
            }

            _verbs.Remove(verb);
            RemoveCommand(verb);
        }

        public virtual void ShowContextMenu(CommandID menuID, int x, int y)
        {
        }

        protected object? GetService(Type serviceType)
        {
            return _serviceProvider?.GetService(serviceType);
        }
    }

}
