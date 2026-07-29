using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace System.ComponentModel.Design;

/// <summary>
/// Provides the managed, in-process component serialization contract used by
/// designer clipboard operations. The payload retains a typed component graph
/// instead of using BinaryFormatter or reflected object shapes.
/// </summary>
internal sealed class PortableDesignerSerializationService : IDesignerSerializationService
{
    private static readonly byte[] s_storeMagic = "LWFCDS\0\x01"u8.ToArray();
    private const int MaxComponentCount = 10_000;
    private const int MaxMemberCount = 100_000;
    private const int MaxArrayLength = 1_000_000;
    private const int MaxStringByteLength = 16 * 1024 * 1024;
    private const int MaxConvertedByteLength = 64 * 1024 * 1024;

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

    internal static void Save(object serializationData, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(serializationData);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new ArgumentException("The serialization stream must be writable.", nameof(stream));
        if (serializationData is not PortableDesignerSerializationPayload payload)
        {
            throw new InvalidOperationException(
                "The serialization payload was created by another designer serialization service.");
        }

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(s_storeMagic);
        writer.Write(payload.Components.Length);
        foreach (PortableComponentSnapshot component in payload.Components)
        {
            writer.Write(component.Id);
            WriteString(writer, GetTypeIdentity(component.ComponentType));
            WriteNullableString(writer, component.PreferredName);
            writer.Write(component.ParentId.HasValue);
            if (component.ParentId is int parentId)
                writer.Write(parentId);
            writer.Write(component.ChildIndex);

            writer.Write(component.Properties.Length);
            foreach (PortablePropertyValue property in component.Properties)
            {
                WriteString(writer, property.Name);
                WriteValue(writer, property.Value);
            }

            writer.Write(component.Events.Length);
            foreach (PortableEventValue eventValue in component.Events)
            {
                WriteString(writer, eventValue.EventName);
                WriteString(writer, eventValue.MethodName);
            }
        }
    }

    internal static object Load(Stream stream, IServiceProvider? serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("The serialization stream must be readable.", nameof(stream));

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        byte[] magic = reader.ReadBytes(s_storeMagic.Length);
        if (!magic.AsSpan().SequenceEqual(s_storeMagic))
            throw new InvalidDataException("The stream is not a LibreWinForms component serialization store.");

        int componentCount = ReadCount(reader, MaxComponentCount, "component");
        var components = new PortableComponentSnapshot[componentCount];
        ITypeResolutionService? typeResolutionService =
            serviceProvider?.GetService(typeof(ITypeResolutionService)) as ITypeResolutionService;
        for (int componentIndex = 0; componentIndex < componentCount; componentIndex++)
        {
            int id = reader.ReadInt32();
            string componentTypeName = ReadString(reader);
            Type componentType = PortableWinFormsTypeResolver.Resolve(typeResolutionService, componentTypeName)
                ?? throw new InvalidDataException(
                    $"The component type '{componentTypeName}' could not be resolved.");
            string? preferredName = ReadNullableString(reader);
            int? parentId = reader.ReadBoolean() ? reader.ReadInt32() : null;
            int childIndex = reader.ReadInt32();

            int propertyCount = ReadCount(reader, MaxMemberCount, "property");
            var properties = new PortablePropertyValue[propertyCount];
            for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
            {
                properties[propertyIndex] = new PortablePropertyValue(
                    ReadString(reader),
                    ReadValue(reader));
            }

            int eventCount = ReadCount(reader, MaxMemberCount, "event");
            var events = new PortableEventValue[eventCount];
            for (int eventIndex = 0; eventIndex < eventCount; eventIndex++)
            {
                events[eventIndex] = new PortableEventValue(
                    ReadString(reader),
                    ReadString(reader));
            }

            components[componentIndex] = new PortableComponentSnapshot(
                id,
                componentType,
                preferredName,
                parentId,
                childIndex,
                properties,
                events);
        }

        return new PortableDesignerSerializationPayload(components);
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
            if (property is null || s_excludedProperties.Contains(property.Name))
            {
                throw new PortableDesignerSerializationException(
                    $"Portable designer serialization cannot restore property '{GetComponentName(component)}.{value.Name}'.");
            }

            try
            {
                if (value.Value is PortableCollectionValue collectionValue)
                {
                    RestoreCollection(component, property, collectionValue, componentsById);
                    continue;
                }
                if (property.IsReadOnly)
                {
                    throw new PortableDesignerSerializationException(
                        $"Portable designer serialization cannot restore read-only property '{GetComponentName(component)}.{value.Name}'.");
                }

                property.SetValue(component, RestoreValue(value.Value, property.PropertyType, componentsById));
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

    private static void RestoreCollection(
        IComponent component,
        PropertyDescriptor property,
        PortableCollectionValue value,
        Dictionary<int, IComponent> componentsById)
    {
        if (property.GetValue(component) is not IList collection
            || collection.IsReadOnly
            || collection.IsFixedSize)
        {
            throw new PortableDesignerSerializationException(
                $"Portable designer serialization cannot restore content collection '{GetComponentName(component)}.{property.Name}'.");
        }

        collection.Clear();
        foreach (object? item in value.Values)
            collection.Add(RestoreValue(item, typeof(object), componentsById));
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
        Type expectedType,
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
            Type elementType = expectedType.IsArray
                ? expectedType.GetElementType() ?? arrayValue.ElementType
                : arrayValue.ElementType;
            Array array = Array.CreateInstance(elementType, arrayValue.Values.Length);
            for (int i = 0; i < arrayValue.Values.Length; i++)
                array.SetValue(RestoreValue(arrayValue.Values[i], elementType, componentsById), i);
            return array;
        }

        if (value is PortableCollectionValue)
        {
            throw new InvalidDataException(
                "A portable content collection can only be restored through its owning property.");
        }

        if (value is PortableFontValue fontValue)
        {
            return new Font(
                fontValue.FamilyName,
                fontValue.Size,
                fontValue.Style,
                fontValue.Unit,
                fontValue.GdiCharSet,
                fontValue.GdiVerticalFont);
        }

        if (value is PortableCursorValue cursorValue)
        {
            return cursorValue.Kind switch
            {
                PortableCursorKind.Default => Cursors.Default,
                PortableCursorKind.Wait => Cursors.WaitCursor,
                PortableCursorKind.IBeam => Cursors.IBeam,
                PortableCursorKind.SizeAll => Cursors.SizeAll,
                PortableCursorKind.SizeWE => Cursors.SizeWE,
                PortableCursorKind.SizeNS => Cursors.SizeNS,
                _ => throw new InvalidDataException(
                    "Custom cursor pixels cannot be restored from this portable designer store.")
            };
        }

        if (value is PortablePaddingValue paddingValue)
        {
            return new Padding(
                paddingValue.Left,
                paddingValue.Top,
                paddingValue.Right,
                paddingValue.Bottom);
        }

        if (value is PortableConvertedValue convertedValue)
            return ConvertStoredValue(convertedValue, expectedType);

        return value;
    }

    private static object? ConvertStoredValue(PortableConvertedValue value, Type expectedType)
    {
        Type conversionType = Nullable.GetUnderlyingType(expectedType) ?? expectedType;
        if (conversionType == typeof(object))
        {
            conversionType = PortableWinFormsTypeResolver.Resolve(null, value.TypeName)
                ?? throw new InvalidDataException(
                    $"The serialized property value type '{value.TypeName}' could not be resolved.");
        }

        TypeConverter converter = TypeDescriptor.GetConverter(conversionType);
        try
        {
            return value.Kind switch
            {
                PortableConvertedValueKind.InvariantString
                    when converter.CanConvertFrom(typeof(string))
                    => converter.ConvertFromInvariantString(value.Text ?? string.Empty),
                PortableConvertedValueKind.Bytes
                    when converter.CanConvertFrom(typeof(byte[]))
                    => converter.ConvertFrom(
                        context: null,
                        CultureInfo.InvariantCulture,
                        value.Bytes ?? Array.Empty<byte>()),
                _ => throw new InvalidDataException(
                    $"Type '{conversionType.FullName}' cannot restore the serialized {value.Kind} value.")
            };
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidDataException(
                $"Type '{conversionType.FullName}' could not restore its serialized designer value.",
                exception);
        }
    }

    private static void WriteValue(BinaryWriter writer, object? value)
    {
        if (value is null)
        {
            writer.Write((byte)PortableValueKind.Null);
            return;
        }

        if (value is PortableComponentReference reference)
        {
            writer.Write((byte)PortableValueKind.ComponentReference);
            writer.Write(reference.ComponentId);
            return;
        }

        if (value is PortableArrayValue array)
        {
            writer.Write((byte)PortableValueKind.Array);
            WriteString(writer, GetTypeIdentity(array.ElementType));
            writer.Write(array.Values.Length);
            foreach (object? item in array.Values)
                WriteValue(writer, item);
            return;
        }

        if (value is PortableCollectionValue collection)
        {
            writer.Write((byte)PortableValueKind.Collection);
            writer.Write(collection.Values.Length);
            foreach (object? item in collection.Values)
                WriteValue(writer, item);
            return;
        }

        if (value is Font font)
        {
            writer.Write((byte)PortableValueKind.Font);
            WriteString(writer, font.FontFamily.Name);
            writer.Write(font.Size);
            writer.Write((int)font.Style);
            writer.Write((int)font.Unit);
            writer.Write(font.GdiCharSet);
            writer.Write(font.GdiVerticalFont);
            return;
        }

        if (value is Cursor cursor)
        {
            if (cursor.PortableKind == PortableCursorKind.Custom)
            {
                throw new InvalidDataException(
                    "Custom cursor pixels cannot be persisted by this portable designer store.");
            }

            writer.Write((byte)PortableValueKind.Cursor);
            writer.Write((int)cursor.PortableKind);
            return;
        }

        if (value is Padding padding)
        {
            writer.Write((byte)PortableValueKind.Padding);
            writer.Write(padding.Left);
            writer.Write(padding.Top);
            writer.Write(padding.Right);
            writer.Write(padding.Bottom);
            return;
        }

        Type valueType = value.GetType();
        TypeConverter converter = TypeDescriptor.GetConverter(value);
        if (converter.CanConvertTo(typeof(string))
            && converter.CanConvertFrom(typeof(string)))
        {
            writer.Write((byte)PortableValueKind.InvariantString);
            WriteString(writer, GetTypeIdentity(valueType));
            WriteNullableString(writer, converter.ConvertToInvariantString(value));
            return;
        }

        if (converter.CanConvertTo(typeof(byte[]))
            && converter.CanConvertFrom(typeof(byte[]))
            && converter.ConvertTo(
                context: null,
                CultureInfo.InvariantCulture,
                value,
                typeof(byte[])) is byte[] bytes)
        {
            if (bytes.Length > MaxConvertedByteLength)
                throw new InvalidDataException("The converted designer property value is too large.");

            writer.Write((byte)PortableValueKind.Bytes);
            WriteString(writer, GetTypeIdentity(valueType));
            writer.Write(bytes.Length);
            writer.Write(bytes);
            return;
        }

        throw new InvalidDataException(
            $"Type '{valueType.FullName}' cannot be persisted by the portable designer serialization store.");
    }

    private static object? ReadValue(BinaryReader reader)
    {
        PortableValueKind kind = (PortableValueKind)reader.ReadByte();
        return kind switch
        {
            PortableValueKind.Null => null,
            PortableValueKind.ComponentReference => new PortableComponentReference(reader.ReadInt32()),
            PortableValueKind.Array => ReadArrayValue(reader),
            PortableValueKind.Collection => ReadCollectionValue(reader),
            PortableValueKind.Font => new PortableFontValue(
                ReadString(reader),
                reader.ReadSingle(),
                (FontStyle)reader.ReadInt32(),
                (GraphicsUnit)reader.ReadInt32(),
                reader.ReadByte(),
                reader.ReadBoolean()),
            PortableValueKind.Cursor => new PortableCursorValue(
                ReadCursorKind(reader)),
            PortableValueKind.Padding => new PortablePaddingValue(
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32()),
            PortableValueKind.InvariantString => new PortableConvertedValue(
                ReadString(reader),
                PortableConvertedValueKind.InvariantString,
                ReadNullableString(reader),
                Bytes: null),
            PortableValueKind.Bytes => new PortableConvertedValue(
                ReadString(reader),
                PortableConvertedValueKind.Bytes,
                Text: null,
                ReadBytes(reader, MaxConvertedByteLength)),
            _ => throw new InvalidDataException($"Unknown portable designer value kind {(byte)kind}.")
        };
    }

    private static PortableArrayValue ReadArrayValue(BinaryReader reader)
    {
        string elementTypeName = ReadString(reader);
        Type elementType = PortableWinFormsTypeResolver.Resolve(null, elementTypeName)
            ?? throw new InvalidDataException(
                $"The array element type '{elementTypeName}' could not be resolved.");
        int count = ReadCount(reader, MaxArrayLength, "array item");
        var values = new object?[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = ReadValue(reader);
        return new PortableArrayValue(elementType, values);
    }

    private static PortableCollectionValue ReadCollectionValue(BinaryReader reader)
    {
        int count = ReadCount(reader, MaxArrayLength, "collection item");
        var values = new object?[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = ReadValue(reader);
        return new PortableCollectionValue(values);
    }

    private static PortableCursorKind ReadCursorKind(BinaryReader reader)
    {
        PortableCursorKind kind = (PortableCursorKind)reader.ReadInt32();
        return kind is PortableCursorKind.Default
            or PortableCursorKind.Wait
            or PortableCursorKind.IBeam
            or PortableCursorKind.SizeAll
            or PortableCursorKind.SizeWE
            or PortableCursorKind.SizeNS
                ? kind
                : throw new InvalidDataException($"Unknown portable cursor kind {(int)kind}.");
    }

    private static string GetTypeIdentity(Type type)
    {
        return type.AssemblyQualifiedName
            ?? type.FullName
            ?? throw new InvalidDataException("A designer serialization type has no stable identity.");
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > MaxStringByteLength)
            throw new InvalidDataException("The designer serialization string is too large.");
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        int length = ReadCount(reader, MaxStringByteLength, "string byte");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException();
        return Encoding.UTF8.GetString(bytes);
    }

    private static void WriteNullableString(BinaryWriter writer, string? value)
    {
        writer.Write(value is not null);
        if (value is not null)
            WriteString(writer, value);
    }

    private static string? ReadNullableString(BinaryReader reader)
    {
        return reader.ReadBoolean() ? ReadString(reader) : null;
    }

    private static byte[] ReadBytes(BinaryReader reader, int maximumLength)
    {
        int length = ReadCount(reader, maximumLength, "converted byte");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException();
        return bytes;
    }

    private static int ReadCount(BinaryReader reader, int maximum, string description)
    {
        int count = reader.ReadInt32();
        if ((uint)count > (uint)maximum)
        {
            throw new InvalidDataException(
                $"The portable designer {description} count {count} is outside the supported range.");
        }

        return count;
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
                        if (!property.ShouldSerializeValue(component))
                            continue;

                        if (property.GetValue(component) is not IList collection)
                            throw CreateUnsupportedContentException(component, property);

                        var items = new object?[collection.Count];
                        for (int i = 0; i < collection.Count; i++)
                            items[i] = CaptureArrayItem(component, property, collection[i]);
                        values.Add(new PortablePropertyValue(
                            property.Name,
                            new PortableCollectionValue(items)));
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
                    if (!property.ShouldSerializeValue(component))
                        continue;

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

    private sealed record PortableCollectionValue(object?[] Values);

    private sealed record PortableConvertedValue(
        string TypeName,
        PortableConvertedValueKind Kind,
        string? Text,
        byte[]? Bytes);

    private sealed record PortableFontValue(
        string FamilyName,
        float Size,
        FontStyle Style,
        GraphicsUnit Unit,
        byte GdiCharSet,
        bool GdiVerticalFont);

    private readonly record struct PortableCursorValue(PortableCursorKind Kind);

    private readonly record struct PortablePaddingValue(
        int Left,
        int Top,
        int Right,
        int Bottom);

    private enum PortableValueKind : byte
    {
        Null,
        ComponentReference,
        Array,
        Collection,
        Font,
        Cursor,
        Padding,
        InvariantString,
        Bytes
    }

    private enum PortableConvertedValueKind : byte
    {
        InvariantString,
        Bytes
    }

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
