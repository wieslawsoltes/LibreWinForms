using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace System.ComponentModel.Design;

public abstract class UndoEngine : IDisposable
{
    private readonly Stack<UndoUnit> _unitStack = new();
    private readonly IDesignerHost _host;
    private readonly IComponentChangeService _componentChangeService;
    private IServiceProvider? _provider;
    private UndoUnit? _executingUnit;
    private EventHandler? _undoing;
    private EventHandler? _undone;

    protected UndoEngine(IServiceProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _host = (IDesignerHost)GetRequiredService(typeof(IDesignerHost));
        _componentChangeService = (IComponentChangeService)GetRequiredService(typeof(IComponentChangeService));
        Enabled = true;

        _host.TransactionOpening += OnTransactionOpening;
        _host.TransactionClosed += OnTransactionClosed;
        _componentChangeService.ComponentAdding += OnComponentAdding;
        _componentChangeService.ComponentAdded += OnComponentAdded;
        _componentChangeService.ComponentChanging += OnComponentChanging;
        _componentChangeService.ComponentChanged += OnComponentChanged;
        _componentChangeService.ComponentRemoving += OnComponentRemoving;
        _componentChangeService.ComponentRemoved += OnComponentRemoved;
        _componentChangeService.ComponentRename += OnComponentRename;
    }

    public bool UndoInProgress => _executingUnit is not null;

    public virtual bool Enabled { get; set; }

    public event EventHandler? Undoing
    {
        add => _undoing += value;
        remove => _undoing -= value;
    }

    public event EventHandler? Undone
    {
        add => _undone += value;
        remove => _undone -= value;
    }

    protected abstract void AddUndoUnit(UndoUnit unit);

    protected virtual UndoUnit CreateUndoUnit(string? name, bool primary)
    {
        return new UndoUnit(this, name);
    }

    protected virtual void DiscardUndoUnit(UndoUnit unit)
    {
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || _provider is null)
            return;

        _host.TransactionOpening -= OnTransactionOpening;
        _host.TransactionClosed -= OnTransactionClosed;
        _componentChangeService.ComponentAdding -= OnComponentAdding;
        _componentChangeService.ComponentAdded -= OnComponentAdded;
        _componentChangeService.ComponentChanging -= OnComponentChanging;
        _componentChangeService.ComponentChanged -= OnComponentChanged;
        _componentChangeService.ComponentRemoving -= OnComponentRemoving;
        _componentChangeService.ComponentRemoved -= OnComponentRemoved;
        _componentChangeService.ComponentRename -= OnComponentRename;
        _provider = null;
    }

    protected object GetRequiredService(Type serviceType)
    {
        return GetService(serviceType)
            ?? throw new InvalidOperationException($"The required designer service '{serviceType.Name}' is unavailable.");
    }

    protected object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return _provider?.GetService(serviceType);
    }

    protected virtual void OnUndoing(EventArgs e)
    {
        _undoing?.Invoke(this, e);
    }

    protected virtual void OnUndone(EventArgs e)
    {
        _undone?.Invoke(this, e);
    }

    private string? GetName(object? component, bool generateNew)
    {
        string? name = component is IComponent value ? value.Site?.Name : null;
        if (name is null
            && component is not null
            && GetService(typeof(IReferenceService)) is IReferenceService referenceService)
            name = referenceService.GetName(component);
        if (name is null && generateNew)
            name = component?.GetType().Name ?? "(null)";
        return name;
    }

    private void CheckPopUnit(PopUnitReason reason)
    {
        if (reason == PopUnitReason.Normal && _host.InTransaction)
            return;

        UndoUnit unit = _unitStack.Pop();
        if (unit.IsEmpty)
        {
            if (_unitStack.Count == 0)
                DiscardUndoUnit(unit);
            return;
        }

        unit.Close();
        if (reason == PopUnitReason.TransactionCancel)
        {
            unit.Undo();
            if (_unitStack.Count == 0)
                DiscardUndoUnit(unit);
            return;
        }

        if (_unitStack.Count == 0)
            AddUndoUnit(unit);
    }

    private void OnTransactionOpening(object? sender, EventArgs e)
    {
        if (Enabled && _executingUnit is null)
            _unitStack.Push(CreateUndoUnit(_host.TransactionDescription, _unitStack.Count == 0));
    }

    private void OnTransactionClosed(object? sender, DesignerTransactionCloseEventArgs e)
    {
        if (_executingUnit is null && _unitStack.Count > 0)
        {
            CheckPopUnit(e.TransactionCommitted
                ? PopUnitReason.TransactionCommit
                : PopUnitReason.TransactionCancel);
        }
    }

    private void OnComponentAdding(object? sender, ComponentEventArgs e)
    {
        EnsureUnit("Add " + GetName(e.Component, true));
        foreach (UndoUnit unit in _unitStack)
            unit.ComponentAdding(e);
    }

    private void OnComponentAdded(object? sender, ComponentEventArgs e)
    {
        foreach (UndoUnit unit in _unitStack)
            unit.ComponentAdded(e);
        PopNormalUnitIfPresent();
    }

    private void OnComponentChanging(object? sender, ComponentChangingEventArgs e)
    {
        string name = GetName(e.Component, true) ?? "component";
        EnsureUnit(e.Member is null ? "Change " + name : $"Change {name}.{e.Member.Name}");
        foreach (UndoUnit unit in _unitStack)
            unit.ComponentChanging(e);
    }

    private void OnComponentChanged(object? sender, ComponentChangedEventArgs e)
    {
        foreach (UndoUnit unit in _unitStack)
            unit.ComponentChanged(e);
        PopNormalUnitIfPresent();
    }

    private void OnComponentRemoving(object? sender, ComponentEventArgs e)
    {
        EnsureUnit("Remove " + GetName(e.Component, true));
        foreach (UndoUnit unit in _unitStack)
            unit.ComponentRemoving(e);
    }

    private void OnComponentRemoved(object? sender, ComponentEventArgs e)
    {
        foreach (UndoUnit unit in _unitStack)
            unit.ComponentRemoved(e);
        PopNormalUnitIfPresent();
    }

    private void OnComponentRename(object? sender, ComponentRenameEventArgs e)
    {
        EnsureUnit($"Rename {e.OldName} to {e.NewName}");
        foreach (UndoUnit unit in _unitStack)
            unit.ComponentRename(e);
        PopNormalUnitIfPresent();
    }

    private void EnsureUnit(string name)
    {
        if (Enabled && _executingUnit is null && _unitStack.Count == 0)
            _unitStack.Push(CreateUndoUnit(name, true));
    }

    private void PopNormalUnitIfPresent()
    {
        if (_unitStack.Count > 0)
            CheckPopUnit(PopUnitReason.Normal);
    }

    private enum PopUnitReason
    {
        Normal,
        TransactionCommit,
        TransactionCancel
    }

    protected class UndoUnit
    {
        private readonly UndoEngine? _engine;
        private readonly List<UndoEvent> _events = new();
        private readonly List<ChangeUndoEvent> _openChanges = new();
        private readonly List<AddRemoveUndoEvent> _openAddRemoveEvents = new();
        private readonly HashSet<IComponent> _adding = new();
        private readonly List<SelectedComponent> _selection = new();
        private bool _reverse = true;

        public UndoUnit(UndoEngine engine, string? name)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            Name = name ?? string.Empty;

            if (engine.GetService(typeof(ISelectionService)) is ISelectionService selectionService)
            {
                foreach (object? selected in selectionService.GetSelectedComponents())
                {
                    if (selected is IComponent { Site: ISite site }
                        && !string.IsNullOrEmpty(site.Name)
                        && site.Container is not null)
                    {
                        _selection.Add(new SelectedComponent(site.Name, site.Container));
                    }
                }
            }
        }

        protected UndoUnit(string name)
        {
            Name = name ?? string.Empty;
        }

        public string Name { get; }

        public virtual bool IsEmpty => _events.Count == 0;

        protected UndoEngine UndoEngine => _engine
            ?? throw new InvalidOperationException("This undo unit is not attached to an undo engine.");

        public virtual void Close()
        {
            foreach (AddRemoveUndoEvent addRemoveEvent in _openAddRemoveEvents)
                addRemoveEvent.Commit();

            _openChanges.Clear();
            _openAddRemoveEvents.Clear();
            _adding.Clear();
        }

        public virtual void ComponentAdding(ComponentEventArgs e)
        {
            if (e.Component is not null)
                _adding.Add(e.Component);
        }

        public virtual void ComponentAdded(ComponentEventArgs e)
        {
            if (e.Component is null)
                return;
            _adding.Remove(e.Component);
            if (e.Component.Site?.Container is not INestedContainer)
            {
                var addition = new AddRemoveUndoEvent(e.Component, added: true);
                _openAddRemoveEvents.Add(addition);
                _events.Add(addition);
            }
        }

        public virtual void ComponentChanging(ComponentChangingEventArgs e)
        {
            if (e.Component is not IComponent { Site: not null } component
                || _adding.Contains(component))
                return;

            foreach (ChangeUndoEvent openChange in _openChanges)
            {
                if (openChange.Matches(component, e.Member))
                    return;
            }

            var change = new ChangeUndoEvent(component, e.Member);
            if (!change.IsValid)
                return;
            _openChanges.Add(change);
            _events.Add(change);
        }

        public virtual void ComponentChanged(ComponentChangedEventArgs e)
        {
        }

        public virtual void ComponentRemoving(ComponentEventArgs e)
        {
            if (e.Component is null || e.Component.Site?.Container is INestedContainer)
                return;

            var removal = new AddRemoveUndoEvent(e.Component, added: false);
            _openAddRemoveEvents.Add(removal);
            _events.Add(removal);
        }

        public virtual void ComponentRemoved(ComponentEventArgs e)
        {
            foreach (AddRemoveUndoEvent removal in _openAddRemoveEvents)
            {
                if (ReferenceEquals(removal.OpenComponent, e.Component))
                {
                    removal.Commit();
                    break;
                }
            }
        }

        public virtual void ComponentRename(ComponentRenameEventArgs e)
        {
            _events.Add(new RenameUndoEvent(e.OldName, e.NewName));
        }

        public virtual void Undo()
        {
            UndoEngine engine = UndoEngine;
            UndoUnit? previous = engine._executingUnit;
            engine._executingUnit = this;
            DesignerTransaction? transaction = null;
            try
            {
                if (previous is null)
                    engine.OnUndoing(EventArgs.Empty);

                transaction = engine._host.CreateTransaction();
                UndoCore(engine);
                transaction.Commit();
                transaction = null;
            }
            catch
            {
                transaction?.Cancel();
                transaction = null;
                throw;
            }
            finally
            {
                engine._executingUnit = previous;
                if (previous is null)
                    engine.OnUndone(EventArgs.Empty);
            }
        }

        protected virtual void UndoCore(UndoEngine engine)
        {
            foreach (UndoEvent undoEvent in _events)
                undoEvent.BeforeUndo(engine);

            var executionOrder = new List<UndoEvent>(_events.Count);
            bool restoreSelection = _reverse;
            if (_reverse)
            {
                for (int i = _events.Count - 1; i >= 0; i--)
                {
                    _events[i].Undo(engine);
                    executionOrder.Add(_events[i]);
                }
            }
            else
            {
                for (int i = 0; i < _events.Count; i++)
                {
                    _events[i].Undo(engine);
                    executionOrder.Add(_events[i]);
                }
            }

            foreach (UndoEvent undoEvent in executionOrder)
                undoEvent.AfterUndo(engine);

            if (restoreSelection)
                RestoreSelection(engine);

            _reverse = !_reverse;
        }

        private void RestoreSelection(UndoEngine engine)
        {
            if (engine.GetService(typeof(ISelectionService)) is not ISelectionService selectionService)
                return;

            List<IComponent> components = new(_selection.Count);
            foreach (SelectedComponent selected in _selection)
            {
                if (selected.Container.Components[selected.Name] is IComponent component)
                    components.Add(component);
            }
            selectionService.SetSelectedComponents(components, SelectionTypes.Replace);
        }

        private readonly record struct SelectedComponent(string Name, IContainer Container);

        private abstract class UndoEvent
        {
            public virtual void BeforeUndo(UndoEngine engine)
            {
            }

            public abstract void Undo(UndoEngine engine);

            public virtual void AfterUndo(UndoEngine engine)
            {
            }
        }

        private sealed class ChangeUndoEvent : UndoEvent
        {
            private readonly string _componentName;
            private readonly string? _memberName;
            private PortablePropertySnapshot? _before;
            private PortablePropertySnapshot? _after;
            private PortablePropertySnapshot? _pendingState;
            private bool _afterCaptured;

            public ChangeUndoEvent(IComponent component, MemberDescriptor? member)
            {
                _componentName = component.Site?.Name ?? string.Empty;
                _memberName = member?.Name;
                _before = PortablePropertySnapshot.Capture(component, member);
            }

            public bool IsValid => !string.IsNullOrEmpty(_componentName) && _before is not null;

            public bool Matches(IComponent component, MemberDescriptor? member)
            {
                return string.Equals(component.Site?.Name, _componentName, StringComparison.Ordinal)
                    && string.Equals(member?.Name, _memberName, StringComparison.Ordinal);
            }

            public override void BeforeUndo(UndoEngine engine)
            {
                if (_afterCaptured)
                    return;

                _afterCaptured = true;
                if (engine._host.Container.Components[_componentName] is IComponent component)
                    _after = PortablePropertySnapshot.Capture(component, _memberName);
            }

            public override void Undo(UndoEngine engine)
            {
                _pendingState = _before;
                (_before, _after) = (_after, _before);
            }

            public override void AfterUndo(UndoEngine engine)
            {
                PortablePropertySnapshot? state = _pendingState;
                _pendingState = null;
                if (state is not null
                    && engine._host.Container.Components[_componentName] is IComponent component)
                {
                    state.Apply(component, engine._componentChangeService);
                }
            }
        }

        private sealed class AddRemoveUndoEvent : UndoEvent
        {
            private PortableComponentSnapshot? _snapshot;
            private IComponent? _pendingRestoredComponent;

            public AddRemoveUndoEvent(IComponent component, bool added)
            {
                OpenComponent = component;
                ComponentName = component.Site?.Name ?? string.Empty;
                NextUndoAdds = !added;
                if (!added)
                    _snapshot = PortableComponentSnapshot.Capture(component);
            }

            public IComponent OpenComponent { get; }

            private string ComponentName { get; }

            private bool NextUndoAdds { get; set; }

            public void Commit()
            {
                _snapshot ??= PortableComponentSnapshot.Capture(OpenComponent);
            }

            public override void Undo(UndoEngine engine)
            {
                if (NextUndoAdds)
                {
                    _pendingRestoredComponent = _snapshot?.RestoreSkeleton(engine._host);
                }
                else if (engine._host.Container.Components[ComponentName] is IComponent component)
                {
                    engine._host.DestroyComponent(component);
                }

                NextUndoAdds = !NextUndoAdds;
            }

            public override void AfterUndo(UndoEngine engine)
            {
                IComponent? component = _pendingRestoredComponent;
                _pendingRestoredComponent = null;
                if (component is not null)
                    _snapshot?.ApplyState(component, engine._host);
            }
        }

        private sealed class RenameUndoEvent : UndoEvent
        {
            private string? _before;
            private string? _after;

            public RenameUndoEvent(string? before, string? after)
            {
                _before = before;
                _after = after;
            }

            public override void Undo(UndoEngine engine)
            {
                if (_after is null || engine._host.Container.Components[_after] is not IComponent component)
                    return;

                engine._componentChangeService.OnComponentChanging(component, null);
                component.Site!.Name = _before;
                engine._componentChangeService.OnComponentChanged(component, null, _after, _before);
                (_before, _after) = (_after, _before);
            }
        }
    }

    private sealed class PortablePropertySnapshot
    {
        private static readonly HashSet<string> s_excludedProperties = new(StringComparer.Ordinal)
        {
            nameof(Component.Site),
            nameof(Control.Parent),
            nameof(Control.Controls),
            nameof(Control.Capture)
        };

        private readonly PropertyValue[] _values;

        private PortablePropertySnapshot(PropertyValue[] values)
        {
            _values = values;
        }

        public static PortablePropertySnapshot? Capture(IComponent component, MemberDescriptor? member)
        {
            return Capture(component, member?.Name);
        }

        public static PortablePropertySnapshot? Capture(IComponent component, string? memberName)
        {
            PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(component);
            if (!string.IsNullOrEmpty(memberName))
            {
                PropertyDescriptor? property = properties[memberName];
                if (property is null || property.IsReadOnly || s_excludedProperties.Contains(property.Name))
                    return null;

                return TryCaptureValue(component, property, out PropertyValue value)
                    ? new PortablePropertySnapshot(new[] { value })
                    : null;
            }

            List<PropertyValue> values = new(properties.Count);
            foreach (PropertyDescriptor property in properties)
            {
                if (property.IsReadOnly
                    || s_excludedProperties.Contains(property.Name)
                    || property.Attributes.Contains(DesignerSerializationVisibilityAttribute.Hidden))
                {
                    continue;
                }

                if (TryCaptureValue(component, property, out PropertyValue value))
                    values.Add(value);
            }
            return values.Count == 0 ? null : new PortablePropertySnapshot(values.ToArray());
        }

        public void Apply(IComponent component, IComponentChangeService changeService)
        {
            PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(component);
            foreach (PropertyValue value in _values)
            {
                PropertyDescriptor? property = properties[value.Name];
                if (property is null || property.IsReadOnly)
                    continue;

                try
                {
                    object? oldValue = property.GetValue(component);
                    object? restoredValue = RestoreValue(value.Value);
                    changeService.OnComponentChanging(component, property);
                    property.SetValue(component, restoredValue);
                    changeService.OnComponentChanged(component, property, oldValue, restoredValue);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
                {
                }
            }
        }

        private static bool TryCaptureValue(
            IComponent component,
            PropertyDescriptor property,
            out PropertyValue value)
        {
            try
            {
                value = new PropertyValue(property.Name, CaptureValue(property.GetValue(component)));
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
            {
                value = default;
                return false;
            }
        }

        private static object? CaptureValue(object? value)
        {
            if (value is IComponent { Site: { Container: IContainer container } site }
                && !string.IsNullOrEmpty(site.Name))
            {
                return new ComponentReference(container, site.Name);
            }

            if (value is Array { Rank: 1 } array)
            {
                Type elementType = array.GetType().GetElementType() ?? typeof(object);
                var values = new object?[array.Length];
                for (int i = 0; i < array.Length; i++)
                    values[i] = CaptureValue(array.GetValue(i));
                return new ArrayValue(elementType, values);
            }

            return value;
        }

        private static object? RestoreValue(object? value)
        {
            if (value is ComponentReference reference)
            {
                return reference.Container.Components[reference.Name]
                    ?? throw new InvalidOperationException(
                        $"The referenced component '{reference.Name}' is unavailable during undo.");
            }

            if (value is ArrayValue arrayValue)
            {
                Array array = Array.CreateInstance(arrayValue.ElementType, arrayValue.Values.Length);
                for (int i = 0; i < arrayValue.Values.Length; i++)
                    array.SetValue(RestoreValue(arrayValue.Values[i]), i);
                return array;
            }

            return value;
        }

        private readonly record struct PropertyValue(string Name, object? Value);

        private readonly record struct ComponentReference(IContainer Container, string Name);

        private sealed record ArrayValue(Type ElementType, object?[] Values);
    }

    private sealed class PortableComponentSnapshot
    {
        private readonly Type _componentType;
        private readonly string _name;
        private readonly string? _parentName;
        private readonly bool _parentIsRoot;
        private readonly int _childIndex;
        private readonly PortablePropertySnapshot? _properties;
        private readonly PortableEventSnapshot? _events;

        private PortableComponentSnapshot(
            Type componentType,
            string name,
            string? parentName,
            bool parentIsRoot,
            int childIndex,
            PortablePropertySnapshot? properties,
            PortableEventSnapshot? events)
        {
            _componentType = componentType;
            _name = name;
            _parentName = parentName;
            _parentIsRoot = parentIsRoot;
            _childIndex = childIndex;
            _properties = properties;
            _events = events;
        }

        public static PortableComponentSnapshot? Capture(IComponent component)
        {
            if (string.IsNullOrEmpty(component.Site?.Name))
                return null;

            string? parentName = null;
            bool parentIsRoot = false;
            int childIndex = -1;
            if (component is Control { Parent: Control parent } control)
            {
                parentName = parent.Site?.Name;
                parentIsRoot = parent.Site?.Container is IDesignerHost host
                    && ReferenceEquals(host.RootComponent, parent);
                childIndex = parent.Controls.IndexOf(control);
            }

            return new PortableComponentSnapshot(
                component.GetType(),
                component.Site.Name,
                parentName,
                parentIsRoot,
                childIndex,
                PortablePropertySnapshot.Capture(component, memberName: null),
                PortableEventSnapshot.Capture(component));
        }

        public IComponent? RestoreSkeleton(IDesignerHost host)
        {
            IComponent component = host.Container.Components[_name]
                ?? host.CreateComponent(_componentType, _name);

            if (component is Control control)
            {
                Control? parent = _parentIsRoot
                    ? host.RootComponent as Control
                    : !string.IsNullOrEmpty(_parentName)
                        ? host.Container.Components[_parentName] as Control
                        : null;
                if (parent is not null && !ReferenceEquals(control.Parent, parent))
                    parent.Controls.Add(control);
                if (parent is not null && _childIndex >= 0 && parent.Controls.Contains(control))
                    parent.Controls.SetChildIndex(control, Math.Min(_childIndex, parent.Controls.Count - 1));
            }

            return component;
        }

        public void ApplyState(IComponent component, IDesignerHost host)
        {
            _properties?.Apply(
                component,
                (IComponentChangeService)host.GetService(typeof(IComponentChangeService))!);
            _events?.Apply(component, host);
        }
    }

    private sealed class PortableEventSnapshot
    {
        private readonly EventValue[] _values;

        private PortableEventSnapshot(EventValue[] values)
        {
            _values = values;
        }

        public static PortableEventSnapshot? Capture(IComponent component)
        {
            if (component.Site?.GetService(typeof(IEventBindingService)) is not IEventBindingService eventBindingService)
                return null;

            try
            {
                PropertyDescriptorCollection eventProperties = eventBindingService.GetEventProperties(
                    TypeDescriptor.GetEvents(component));
                var values = new List<EventValue>();
                foreach (PropertyDescriptor eventProperty in eventProperties)
                {
                    if (eventProperty.GetValue(component) is string methodName
                        && !string.IsNullOrWhiteSpace(methodName))
                    {
                        values.Add(new EventValue(eventProperty.Name, methodName));
                    }
                }

                return values.Count == 0 ? null : new PortableEventSnapshot(values.ToArray());
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
            {
                return null;
            }
        }

        public void Apply(IComponent component, IDesignerHost host)
        {
            if (host.GetService(typeof(IEventBindingService)) is not IEventBindingService eventBindingService)
                return;

            PropertyDescriptorCollection eventProperties;
            try
            {
                eventProperties = eventBindingService.GetEventProperties(TypeDescriptor.GetEvents(component));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
            {
                return;
            }

            foreach (EventValue value in _values)
            {
                PropertyDescriptor? eventProperty = eventProperties[value.Name];
                if (eventProperty is null || eventProperty.IsReadOnly)
                    continue;

                try
                {
                    eventProperty.SetValue(component, value.MethodName);
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
                {
                }
            }
        }

        private readonly record struct EventValue(string Name, string MethodName);
    }
}
