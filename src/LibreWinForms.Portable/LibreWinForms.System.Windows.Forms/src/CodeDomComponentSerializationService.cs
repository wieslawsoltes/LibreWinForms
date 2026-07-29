using System.Collections;
using System.IO;

namespace System.ComponentModel.Design.Serialization;

public sealed class CodeDomComponentSerializationService : ComponentSerializationService
{
    private readonly IServiceProvider? _serviceProvider;

    public CodeDomComponentSerializationService()
    {
    }

    public CodeDomComponentSerializationService(IServiceProvider? serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public override SerializationStore CreateStore() => new PortableSerializationStore(_serviceProvider);

    public override SerializationStore LoadStore(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        object payload = PortableDesignerSerializationService.Load(stream, _serviceProvider);
        return new PortableSerializationStore(_serviceProvider, payload);
    }

    public override void Serialize(SerializationStore store, object value) => GetStore(store).Add(value);

    public override void SerializeAbsolute(SerializationStore store, object value) => GetStore(store).Add(value);

    public override void SerializeMember(SerializationStore store, object owningObject, MemberDescriptor member)
        => GetStore(store).Add(owningObject);

    public override void SerializeMemberAbsolute(SerializationStore store, object owningObject, MemberDescriptor member)
        => GetStore(store).Add(owningObject);

    public override ICollection Deserialize(SerializationStore store) => GetStore(store).Deserialize();

    public override ICollection Deserialize(SerializationStore store, IContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        ICollection values = GetStore(store).Deserialize();
        foreach (object? value in values)
        {
            if (value is IComponent component && component.Site is null)
                container.Add(component);
        }

        return values;
    }

    public override void DeserializeTo(
        SerializationStore store,
        IContainer container,
        bool validateRecycledTypes,
        bool applyDefaults)
    {
        Deserialize(store, container);
    }

    private static PortableSerializationStore GetStore(SerializationStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store as PortableSerializationStore
            ?? throw new InvalidOperationException("The serialization store was created by another service.");
    }

    private sealed class PortableSerializationStore : SerializationStore
    {
        private readonly IServiceProvider? _serviceProvider;
        private readonly ArrayList _values = new();
        private object? _payload;
        private bool _closed;

        public PortableSerializationStore(IServiceProvider? serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public PortableSerializationStore(IServiceProvider? serviceProvider, object payload)
        {
            _serviceProvider = serviceProvider;
            _payload = payload ?? throw new ArgumentNullException(nameof(payload));
            _closed = true;
        }

        public override ICollection Errors => Array.Empty<object>();

        public void Add(object value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_closed)
                throw new InvalidOperationException("The serialization store is closed.");
            if (!_values.Contains(value))
                _values.Add(value);
        }

        public override void Close()
        {
            if (_closed)
                return;

            _closed = true;
            _payload = GetSerializationService().Serialize(_values);
        }

        public override void Save(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            Close();
            PortableDesignerSerializationService.Save(
                _payload ?? throw new InvalidOperationException("The serialization store has no payload."),
                stream);
        }

        public ICollection Deserialize()
        {
            Close();
            return _payload is null
                ? Array.Empty<object>()
                : GetSerializationService().Deserialize(_payload);
        }

        private IDesignerSerializationService GetSerializationService()
        {
            return _serviceProvider?.GetService(typeof(IDesignerSerializationService)) as IDesignerSerializationService
                ?? throw new InvalidOperationException("IDesignerSerializationService is not available.");
        }
    }
}
