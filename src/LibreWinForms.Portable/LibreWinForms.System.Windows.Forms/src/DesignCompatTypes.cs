using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.ComponentModel.Design.Serialization;
using System.Drawing;
using System.Reflection;

namespace System.Drawing.Design
{
    public sealed class CategoryNameCollection : ReadOnlyCollection<string>
    {
        public CategoryNameCollection(string[] value)
            : base(value)
        {
        }
    }

    public interface IPropertyValueUIService
    {
        event EventHandler? PropertyUIValueItemsChanged;

        void AddPropertyValueUIHandler(PropertyValueUIHandler newHandler);

        PropertyValueUIItem[] GetPropertyUIValueItems(ITypeDescriptorContext context, PropertyDescriptor propDesc);

        void NotifyPropertyValueUIItemsChanged();

        void RemovePropertyValueUIHandler(PropertyValueUIHandler newHandler);
    }

    public interface IToolboxService
    {
        CategoryNameCollection CategoryNames { get; }

        string? SelectedCategory { get; set; }

        void AddCreator(ToolboxItemCreatorCallback creator, string format);

        void AddCreator(ToolboxItemCreatorCallback creator, string format, IDesignerHost host);

        void AddLinkedToolboxItem(ToolboxItem toolboxItem, string category, IDesignerHost host);

        void AddLinkedToolboxItem(ToolboxItem toolboxItem, IDesignerHost host);

        void AddToolboxItem(ToolboxItem toolboxItem);

        void AddToolboxItem(ToolboxItem toolboxItem, string category);

        ToolboxItem DeserializeToolboxItem(object serializedObject);

        ToolboxItem DeserializeToolboxItem(object serializedObject, IDesignerHost host);

        ToolboxItem? GetSelectedToolboxItem();

        ToolboxItem? GetSelectedToolboxItem(IDesignerHost host);

        ToolboxItemCollection GetToolboxItems();

        ToolboxItemCollection GetToolboxItems(string category);

        ToolboxItemCollection GetToolboxItems(string category, IDesignerHost host);

        ToolboxItemCollection GetToolboxItems(IDesignerHost host);

        bool IsSupported(object serializedObject, IDesignerHost host);

        bool IsToolboxItem(object serializedObject);

        bool IsToolboxItem(object serializedObject, IDesignerHost host);

        void Refresh();

        void RemoveCreator(string format);

        void RemoveCreator(string format, IDesignerHost host);

        void RemoveToolboxItem(ToolboxItem toolboxItem);

        void RemoveToolboxItem(ToolboxItem toolboxItem, string category);

        void SelectedToolboxItemUsed();

        object SerializeToolboxItem(ToolboxItem toolboxItem);

        bool SetCursor();

        void SetSelectedToolboxItem(ToolboxItem toolboxItem);

        event EventHandler? SelectedCategoryChanged;

        event EventHandler? SelectedCategoryChanging;
    }

    public interface IToolboxUser
    {
        bool GetToolSupported(ToolboxItem tool);

        void ToolPicked(ToolboxItem tool);
    }

    public sealed class PaintValueEventArgs : EventArgs
    {
        public PaintValueEventArgs(ITypeDescriptorContext? context, object? value, Graphics graphics, Rectangle bounds)
        {
            Context = context;
            Value = value;
            Graphics = graphics;
            Bounds = bounds;
        }

        public Rectangle Bounds { get; }

        public ITypeDescriptorContext? Context { get; }

        public Graphics Graphics { get; }

        public object? Value { get; }
    }

    public delegate void PropertyValueUIHandler(ITypeDescriptorContext context, PropertyDescriptor propDesc, ArrayList valueUIItemList);

    public delegate void PropertyValueUIItemInvokeHandler(ITypeDescriptorContext context, PropertyDescriptor descriptor, PropertyValueUIItem invokedItem);

    public sealed class PropertyValueUIItem
    {
        public PropertyValueUIItem(Image image, PropertyValueUIItemInvokeHandler handler, string tooltip)
        {
            Image = image;
            InvokeHandler = handler;
            ToolTip = tooltip;
        }

        public Image Image { get; }

        public PropertyValueUIItemInvokeHandler InvokeHandler { get; }

        public string ToolTip { get; }

        public void Invoke(ITypeDescriptorContext context, PropertyDescriptor descriptor)
        {
            InvokeHandler(context, descriptor, this);
        }
    }

    public class ToolboxItem
    {
        private ToolboxComponentsCreatedEventHandler? _componentsCreated;
        private ToolboxComponentsCreatingEventHandler? _componentsCreating;

        public ToolboxItem()
        {
        }

        public ToolboxItem(Type toolType)
        {
            Initialize(toolType);
        }

        public AssemblyName? AssemblyName { get; set; }

        public Bitmap Bitmap { get; set; } = new(1, 1);

        public string Company { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public ICollection Filter { get; set; } = Array.Empty<object>();

        public bool IsTransient { get; set; }

        public IDictionary Properties { get; } = new Hashtable();

        public string? TypeName { get; set; }

        public event ToolboxComponentsCreatedEventHandler? ComponentsCreated
        {
            add => _componentsCreated += value;
            remove => _componentsCreated -= value;
        }

        public event ToolboxComponentsCreatingEventHandler? ComponentsCreating
        {
            add => _componentsCreating += value;
            remove => _componentsCreating -= value;
        }

        public virtual void Initialize(Type? type)
        {
            if (type is null)
            {
                return;
            }

            TypeName = type.FullName ?? type.Name;
            AssemblyName = type.Assembly.GetName();
            DisplayName = type.Name;
        }

        public IComponent[] CreateComponents()
        {
            return CreateComponents(null!);
        }

        public IComponent[] CreateComponents(IDesignerHost host)
        {
            OnComponentsCreating(new ToolboxComponentsCreatingEventArgs(host));
            IComponent[] components = CreateComponentsCore(host, new Hashtable());
            if (components.Length > 0)
                OnComponentsCreated(new ToolboxComponentsCreatedEventArgs(components));
            return components;
        }

        public IComponent[] CreateComponents(IDesignerHost host, IDictionary defaultValues)
        {
            OnComponentsCreating(new ToolboxComponentsCreatingEventArgs(host));
            IComponent[] components = CreateComponentsCore(host, defaultValues);
            if (components.Length > 0)
                OnComponentsCreated(new ToolboxComponentsCreatedEventArgs(components));
            return components;
        }

        protected virtual IComponent[] CreateComponentsCore(IDesignerHost host)
        {
            Type? componentType = GetType(host);
            if (componentType is null || !typeof(IComponent).IsAssignableFrom(componentType))
                return Array.Empty<IComponent>();

            IComponent? component = host is null
                ? TypeDescriptor.CreateInstance(null, componentType, null, null) as IComponent
                : host.CreateComponent(componentType);
            return component is null ? Array.Empty<IComponent>() : new[] { component };
        }

        protected virtual IComponent[] CreateComponentsCore(IDesignerHost host, IDictionary defaultValues)
        {
            IComponent[] components = CreateComponentsCore(host);
            if (host is null)
                return components;

            for (int i = 0; i < components.Length; i++)
            {
                if (host.GetDesigner(components[i]) is not IComponentInitializer initializer)
                    continue;

                bool initialized = false;
                try
                {
                    initializer.InitializeNewComponent(defaultValues);
                    initialized = true;
                }
                finally
                {
                    if (!initialized)
                    {
                        for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
                            host.DestroyComponent(components[componentIndex]);
                    }
                }
            }

            return components;
        }

        public Type? GetType(IDesignerHost? host)
        {
            return GetType(host, AssemblyName, TypeName, false);
        }

        protected virtual Type? GetType(
            IDesignerHost? host,
            AssemblyName? assemblyName,
            string? typeName,
            bool reference)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            ITypeResolutionService? typeResolutionService = host?.GetService(typeof(ITypeResolutionService)) as ITypeResolutionService;
            if (reference && assemblyName is not null)
                typeResolutionService?.ReferenceAssembly(assemblyName);

            return PortableWinFormsTypeResolver.Resolve(typeResolutionService, typeName);
        }

        protected virtual void OnComponentsCreated(ToolboxComponentsCreatedEventArgs args)
        {
            _componentsCreated?.Invoke(this, args);
        }

        protected virtual void OnComponentsCreating(ToolboxComponentsCreatingEventArgs args)
        {
            _componentsCreating?.Invoke(this, args);
        }

        public override string ToString() => DisplayName;
    }

    public class ToolboxComponentsCreatedEventArgs : EventArgs
    {
        private readonly IComponent[]? _components;

        public ToolboxComponentsCreatedEventArgs(IComponent[]? components)
        {
            _components = components;
        }

        public IComponent[]? Components => (IComponent[]?)_components?.Clone();
    }

    public delegate void ToolboxComponentsCreatedEventHandler(object sender, ToolboxComponentsCreatedEventArgs e);

    public class ToolboxComponentsCreatingEventArgs : EventArgs
    {
        public ToolboxComponentsCreatingEventArgs(IDesignerHost? host)
        {
            DesignerHost = host;
        }

        public IDesignerHost? DesignerHost { get; }
    }

    public delegate void ToolboxComponentsCreatingEventHandler(object sender, ToolboxComponentsCreatingEventArgs e);

    public sealed class ToolboxItemCollection : ReadOnlyCollection<ToolboxItem>
    {
        public ToolboxItemCollection(ToolboxItem[] value)
            : base(value)
        {
        }
    }

    public delegate ToolboxItem? ToolboxItemCreatorCallback(object serializedObject, string format);

    public enum UITypeEditorEditStyle
    {
        None = 1,
        Modal = 2,
        DropDown = 3
    }

    public class UITypeEditor
    {
        public virtual bool IsDropDownResizable => false;

        protected static string CreateFilterEntry(UITypeEditor editor)
        {
            return editor switch
            {
                IconEditor => "Icon files (*.ico)|*.ico|All files (*.*)|*.*",
                ImageEditor => "Image files (*.bmp;*.gif;*.jpg;*.jpeg;*.png)|*.bmp;*.gif;*.jpg;*.jpeg;*.png|All files (*.*)|*.*",
                _ => "All files (*.*)|*.*"
            };
        }

        public virtual object? EditValue(ITypeDescriptorContext? context, IServiceProvider provider, object? value)
        {
            return value;
        }

        public virtual UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context)
        {
            return UITypeEditorEditStyle.None;
        }

        public virtual bool GetPaintValueSupported(ITypeDescriptorContext? context)
        {
            return false;
        }

        public virtual void PaintValue(PaintValueEventArgs e)
        {
        }
    }

    public class ImageEditor : UITypeEditor
    {
    }

    public class IconEditor : UITypeEditor
    {
    }

    public class ContentAlignmentEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context)
        {
            return UITypeEditorEditStyle.DropDown;
        }
    }
}

namespace System.Windows.Forms.Design
{
    public interface IUIService
    {
        IDictionary Styles { get; }

        bool CanShowComponentEditor(object component);

        IWin32Window? GetDialogOwnerWindow();

        void SetUIDirty();

        bool ShowComponentEditor(object component, IWin32Window parent);

        DialogResult ShowDialog(Form form);

        void ShowError(Exception ex);

        void ShowError(Exception ex, string message);

        void ShowError(string message);

        void ShowMessage(string message);

        void ShowMessage(string message, string caption);

        DialogResult ShowMessage(string message, string caption, MessageBoxButtons buttons);

        bool ShowToolWindow(Guid toolWindow);
    }

    public interface IWindowsFormsEditorService
    {
        void CloseDropDown();

        void DropDownControl(Control control);

        DialogResult ShowDialog(Form dialog);
    }

    public class DesignerOptions
    {
        public virtual bool EnableInSituEditing { get; }

        public virtual Size GridSize { get; }

        public virtual bool ObjectBoundSmartTagAutoShow { get; }

        public virtual bool ShowGrid { get; }

        public virtual bool SnapToGrid { get; }

        public virtual bool UseOptimizedCodeGeneration { get; }

        public virtual bool UseSmartTags { get; }

        public virtual bool UseSnapLines { get; }
    }

    public class WindowsFormsDesignerOptionService : DesignerOptionService
    {
        private bool _optionsPopulated;

        protected override void PopulateOptionCollection(DesignerOptionCollection options)
        {
            if (_optionsPopulated)
            {
                return;
            }

            _optionsPopulated = true;
            CreateOptionCollection(options, "WindowsFormsDesigner", this);
        }

        public Size GridSize { get; set; }

        public bool EnableInSituEditing { get; set; }

        public bool ObjectBoundSmartTagAutoShow { get; set; }

        public bool UseOptimizedCodeGeneration { get; set; }

        public bool UseSmartTags { get; set; }

        public bool UseSnapLines { get; set; }

        public bool ShowGrid { get; set; }

        public bool SnapToGrid { get; set; }
    }
}
