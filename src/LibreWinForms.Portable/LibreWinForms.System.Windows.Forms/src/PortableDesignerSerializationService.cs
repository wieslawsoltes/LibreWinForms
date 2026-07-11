using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Linq;
using System.Windows.Forms;

namespace System.ComponentModel.Design;

/// <summary>
/// Provides the managed, in-process component serialization contract used by
/// designer clipboard operations. The payload retains a typed component graph
/// instead of using BinaryFormatter or reflected object shapes.
/// </summary>
internal sealed class PortableDesignerSerializationService : IDesignerSerializationService
{
    private static readonly HashSet<string> s_excludedProperties = new(StringComparer.Ordinal)
    {
        nameof(Component.Site),
        nameof(Control.Parent),
        nameof(Control.Controls),
        nameof(Control.Capture),
        nameof(Control.Name)
    };

    private readonly IDesignerHost _host;

    public PortableDesignerSerializationService(IDesignerHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public object Serialize(ICollection? objects)
    {
        if (objects is null || objects.Count == 0)
            return PortableDesignerSerializationPayload.Empty;

        var selectedComponents = new HashSet<IComponent>(ReferenceEqualityComparer.Instance);
        foreach (object? value in objects)
        {
            if (value is IComponent component
                && ReferenceEquals(component.Site?.Container, _host.Container))
            {
                selectedComponents.Add(component);
            }
        }

        if (selectedComponents.Count == 0)
            return PortableDesignerSerializationPayload.Empty;

        var context = new CaptureContext(_host);
        foreach (IComponent component in selectedComponents)
        {
            if (component is Control control && HasSelectedAncestor(control, selectedComponents))
                continue;

            context.Capture(component, parentId: null, childIndex: -1);
        }

        return new PortableDesignerSerializationPayload(context.BuildSnapshots());
    }

    public ICollection Deserialize(object serializationData)
    {
        if (serializationData is not PortableDesignerSerializationPayload payload
            || payload.Components.Length == 0)
        {
            return Array.Empty<IComponent>();
        }

        var createdComponents = new List<IComponent>(payload.Components.Length);
        var componentsById = new Dictionary<int, IComponent>(payload.Components.Length);
        try
        {
            foreach (PortableComponentSnapshot snapshot in payload.Components)
            {
                IComponent component = CreateComponent(snapshot);
                createdComponents.Add(component);
                componentsById.Add(snapshot.Id, component);
            }

            foreach (PortableComponentSnapshot snapshot in payload.Components)
                RestoreParent(snapshot, componentsById);

            foreach (PortableComponentSnapshot snapshot in payload.Components)
            {
                IComponent component = componentsById[snapshot.Id];
                ApplyProperties(component, snapshot.Properties, componentsById);
            }

            foreach (PortableComponentSnapshot snapshot in payload.Components)
                ApplyEvents(componentsById[snapshot.Id], snapshot.Events);

            return createdComponents.ToArray();
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
    }

    private IComponent CreateComponent(PortableComponentSnapshot snapshot)
    {
        string? preferredName = snapshot.PreferredName;
        IComponent component = !string.IsNullOrWhiteSpace(preferredName)
            && _host.Container.Components[preferredName] is null
                ? _host.CreateComponent(snapshot.ComponentType, preferredName)
                : _host.CreateComponent(snapshot.ComponentType);

        if (component is Control control && !string.IsNullOrEmpty(component.Site?.Name))
            control.Name = component.Site.Name;

        return component;
    }

    private static void RestoreParent(
        PortableComponentSnapshot snapshot,
        Dictionary<int, IComponent> componentsById)
    {
        if (snapshot.ParentId is not int parentId)
            return;
        if (!componentsById.TryGetValue(snapshot.Id, out IComponent? component)
            || component is not Control control
            || !componentsById.TryGetValue(parentId, out IComponent? parentComponent)
            || parentComponent is not Control parent)
        {
            throw new PortableDesignerSerializationException(
                $"Portable designer serialization cannot restore parent state for component id {snapshot.Id}.");
        }

        parent.Controls.Add(control);
        if (snapshot.ChildIndex >= 0 && parent.Controls.Contains(control))
            parent.Controls.SetChildIndex(control, Math.Min(snapshot.ChildIndex, parent.Controls.Count - 1));
    }

    private static void ApplyProperties(
        IComponent component,
        PortablePropertyValue[] values,
        Dictionary<int, IComponent> componentsById)
    {
        PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(component);
        foreach (PortablePropertyValue value in values)
        {
            PropertyDescriptor? property = properties[value.Name];
            if (property is null || property.IsReadOnly || s_excludedProperties.Contains(property.Name))
            {
                throw new PortableDesignerSerializationException(
                    $"Portable designer serialization cannot restore property '{GetComponentName(component)}.{value.Name}'.");
            }

            try
            {
                property.SetValue(component, RestoreValue(value.Value, componentsById));
            }
            catch (PortableDesignerSerializationException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw CreatePropertyException("restore", component, property, exception);
            }
        }
    }

    private void ApplyEvents(IComponent component, PortableEventValue[] values)
    {
        if (values.Length == 0)
            return;

        if (_host.GetService(typeof(IEventBindingService)) is not IEventBindingService eventBindingService)
        {
            throw new PortableDesignerSerializationException(
                $"Portable designer serialization cannot restore events for component '{GetComponentName(component)}' because no event-binding service is available.");
        }

        PropertyDescriptorCollection eventProperties;
        try
        {
            eventProperties = eventBindingService.GetEventProperties(TypeDescriptor.GetEvents(component));
        }
        catch (Exception exception)
        {
            throw new PortableDesignerSerializationException(
                $"Portable designer serialization could not inspect restorable events for component '{GetComponentName(component)}'.",
                exception);
        }

        foreach (PortableEventValue value in values)
        {
            PropertyDescriptor? eventProperty = eventProperties[value.EventName];
            if (eventProperty is null || eventProperty.IsReadOnly)
            {
                throw new PortableDesignerSerializationException(
                    $"Portable designer serialization cannot restore event '{GetComponentName(component)}.{value.EventName}'.");
            }

            try
            {
                eventProperty.SetValue(component, value.MethodName);
            }
            catch (Exception exception)
            {
                throw new PortableDesignerSerializationException(
                    $"Portable designer serialization could not restore event '{GetComponentName(component)}.{value.EventName}'.",
                    exception);
            }
        }
    }

    private static object? RestoreValue(
        object? value,
        Dictionary<int, IComponent> componentsById)
    {
        if (value is PortableComponentReference reference)
        {
            if (componentsById.TryGetValue(reference.ComponentId, out IComponent? component))
                return component;

            throw new PortableDesignerSerializationException(
                $"Portable designer serialization cannot resolve component id {reference.ComponentId}.");
        }

        if (value is PortableArrayValue arrayValue)
        {
            Array array = Array.CreateInstance(arrayValue.ElementType, arrayValue.Values.Length);
            for (int i = 0; i < arrayValue.Values.Length; i++)
                array.SetValue(RestoreValue(arrayValue.Values[i], componentsById), i);
            return array;
        }

        return value;
    }

    private static bool HasSelectedAncestor(Control control, HashSet<IComponent> selectedComponents)
    {
        for (Control? parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            if (selectedComponents.Contains(parent))
                return true;
        }

        return false;
    }

    private static PortableDesignerSerializationException CreateUnsupportedContentException(
        IComponent component,
        PropertyDescriptor property)
    {
        return new PortableDesignerSerializationException(
            $"Portable designer serialization does not yet support non-default content property '{GetComponentName(component)}.{property.Name}'.");
    }

    private static PortableDesignerSerializationException CreatePropertyException(
        string operation,
        IComponent component,
        PropertyDescriptor property,
        Exception innerException)
    {
        return new PortableDesignerSerializationException(
            $"Portable designer serialization could not {operation} property '{GetComponentName(component)}.{property.Name}'.",
            innerException);
    }

    private static string GetComponentName(IComponent component)
    {
        return component.Site?.Name ?? component.GetType().Name;
    }

    private sealed class CaptureContext
    {
        private readonly IDesignerHost _host;
        private readonly Dictionary<IComponent, PortableComponentSnapshotBuilder> _components =
            new(ReferenceEqualityComparer.Instance);

        public CaptureContext(IDesignerHost host)
        {
            _host = host;
        }

        public int Capture(IComponent component, int? parentId, int childIndex)
        {
            if (!ReferenceEquals(component.Site?.Container, _host.Container))
            {
                throw new PortableDesignerSerializationException(
                    $"Portable designer serialization cannot capture unsited or foreign component '{GetComponentName(component)}'.");
            }

            if (_components.TryGetValue(component, out PortableComponentSnapshotBuilder? existing))
            {
                existing.SetParent(parentId, childIndex);
                return existing.Id;
            }

            var builder = new PortableComponentSnapshotBuilder(
                _components.Count,
                component.GetType(),
                component.Site?.Name,
                parentId,
                childIndex);
            _components.Add(component, builder);

            if (component is Control control)
            {
                for (int index = 0; index < control.Controls.Count; index++)
                {
                    Control child = control.Controls[index];
                    if (ReferenceEquals(child.Site?.Container, _host.Container))
                        Capture(child, builder.Id, index);
                }
            }

            builder.Properties = CaptureProperties(component);
            builder.Events = CaptureEvents(component);
            return builder.Id;
        }

        public PortableComponentSnapshot[] BuildSnapshots()
        {
            return _components.Values
                .OrderBy(builder => builder.Id)
                .Select(builder => builder.Build())
                .ToArray();
        }

        private PortablePropertyValue[] CaptureProperties(IComponent component)
        {
            PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(component);
            var values = new List<PortablePropertyValue>(properties.Count);
            foreach (PropertyDescriptor property in properties)
            {
                if (s_excludedProperties.Contains(property.Name)
                    || property.Attributes.Contains(DesignerSerializationVisibilityAttribute.Hidden))
                {
                    continue;
                }

                if (property.Attributes.Contains(DesignerSerializationVisibilityAttribute.Content))
                {
                    try
                    {
                        if (property.ShouldSerializeValue(component))
                            throw CreateUnsupportedContentException(component, property);
                    }
                    catch (PortableDesignerSerializationException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        throw CreatePropertyException("inspect", component, property, exception);
                    }

                    continue;
                }

                if (property.IsReadOnly)
                    continue;

                try
                {
                    values.Add(new PortablePropertyValue(
                        property.Name,
                        CaptureValue(component, property, property.GetValue(component))));
                }
                catch (PortableDesignerSerializationException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw CreatePropertyException("read", component, property, exception);
                }
            }

            return values.ToArray();
        }

        private PortableEventValue[] CaptureEvents(IComponent component)
        {
            if (_host.GetService(typeof(IEventBindingService)) is not IEventBindingService eventBindingService)
                return Array.Empty<PortableEventValue>();

            try
            {
                PropertyDescriptorCollection eventProperties = eventBindingService.GetEventProperties(
                    TypeDescriptor.GetEvents(component));
                var values = new List<PortableEventValue>();
                foreach (PropertyDescriptor eventProperty in eventProperties)
                {
                    if (eventProperty.GetValue(component) is string methodName
                        && !string.IsNullOrWhiteSpace(methodName))
                    {
                        values.Add(new PortableEventValue(eventProperty.Name, methodName));
                    }
                }

                return values.ToArray();
            }
            catch (Exception exception)
            {
                throw new PortableDesignerSerializationException(
                    $"Portable designer serialization could not inspect events for component '{GetComponentName(component)}'.",
                    exception);
            }
        }

        private object? CaptureValue(
            IComponent owner,
            PropertyDescriptor property,
            object? value)
        {
            if (value is IComponent referencedComponent)
            {
                if (ReferenceEquals(referencedComponent, _host.RootComponent)
                    || !ReferenceEquals(referencedComponent.Site?.Container, _host.Container))
                {
                    throw new PortableDesignerSerializationException(
                        $"Portable designer serialization cannot copy external component reference '{GetComponentName(owner)}.{property.Name}'.");
                }

                if (referencedComponent is Control { Parent: not null } referencedControl
                    && !_components.ContainsKey(referencedControl.Parent))
                {
                    throw new PortableDesignerSerializationException(
                        $"Portable designer serialization cannot detach referenced control '{GetComponentName(referencedComponent)}' from its uncopied parent.");
                }

                return new PortableComponentReference(Capture(referencedComponent, parentId: null, childIndex: -1));
            }

            if (value is Array array)
            {
                if (array.Rank != 1)
                {
                    throw new PortableDesignerSerializationException(
                        $"Portable designer serialization does not support multidimensional array property '{GetComponentName(owner)}.{property.Name}'.");
                }

                Type elementType = array.GetType().GetElementType() ?? typeof(object);
                var items = new object?[array.Length];
                for (int i = 0; i < array.Length; i++)
                    items[i] = CaptureArrayItem(owner, property, array.GetValue(i));
                return new PortableArrayValue(elementType, items);
            }

            if (value is IEnumerable enumerable and not string)
            {
                foreach (object? item in enumerable)
                {
                    if (item is IComponent)
                    {
                        throw new PortableDesignerSerializationException(
                            $"Portable designer serialization does not yet support component collections in property '{GetComponentName(owner)}.{property.Name}'.");
                    }
                }
            }

            return value;
        }

        private object? CaptureArrayItem(
            IComponent owner,
            PropertyDescriptor property,
            object? value)
        {
            if (value is IComponent referencedComponent)
            {
                if (ReferenceEquals(referencedComponent, _host.RootComponent)
                    || !ReferenceEquals(referencedComponent.Site?.Container, _host.Container))
                {
                    throw new PortableDesignerSerializationException(
                        $"Portable designer serialization cannot copy external component reference '{GetComponentName(owner)}.{property.Name}'.");
                }

                return new PortableComponentReference(Capture(referencedComponent, parentId: null, childIndex: -1));
            }

            return value;
        }
    }

    private sealed class PortableComponentSnapshotBuilder
    {
        public PortableComponentSnapshotBuilder(
            int id,
            Type componentType,
            string? preferredName,
            int? parentId,
            int childIndex)
        {
            Id = id;
            ComponentType = componentType;
            PreferredName = preferredName;
            ParentId = parentId;
            ChildIndex = childIndex;
        }

        public int Id { get; }

        public Type ComponentType { get; }

        public string? PreferredName { get; }

        public int? ParentId { get; private set; }

        public int ChildIndex { get; private set; }

        public PortablePropertyValue[] Properties { get; set; } = Array.Empty<PortablePropertyValue>();

        public PortableEventValue[] Events { get; set; } = Array.Empty<PortableEventValue>();

        public void SetParent(int? parentId, int childIndex)
        {
            if (parentId is null)
                return;
            if (ParentId is not null && ParentId != parentId)
            {
                throw new PortableDesignerSerializationException(
                    $"Portable designer serialization found multiple parents for component id {Id}.");
            }

            ParentId = parentId;
            ChildIndex = childIndex;
        }

        public PortableComponentSnapshot Build()
        {
            return new PortableComponentSnapshot(
                Id,
                ComponentType,
                PreferredName,
                ParentId,
                ChildIndex,
                Properties,
                Events);
        }
    }

    private sealed class PortableDesignerSerializationPayload
    {
        public static readonly PortableDesignerSerializationPayload Empty =
            new(Array.Empty<PortableComponentSnapshot>());

        public PortableDesignerSerializationPayload(PortableComponentSnapshot[] components)
        {
            Components = components;
        }

        public PortableComponentSnapshot[] Components { get; }
    }

    private sealed record PortableComponentSnapshot(
        int Id,
        Type ComponentType,
        string? PreferredName,
        int? ParentId,
        int ChildIndex,
        PortablePropertyValue[] Properties,
        PortableEventValue[] Events);

    private readonly record struct PortablePropertyValue(string Name, object? Value);

    private readonly record struct PortableEventValue(string EventName, string MethodName);

    private readonly record struct PortableComponentReference(int ComponentId);

    private sealed record PortableArrayValue(Type ElementType, object?[] Values);

    private sealed class PortableDesignerSerializationException : InvalidOperationException
    {
        public PortableDesignerSerializationException(string message)
            : base(message)
        {
        }

        public PortableDesignerSerializationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
