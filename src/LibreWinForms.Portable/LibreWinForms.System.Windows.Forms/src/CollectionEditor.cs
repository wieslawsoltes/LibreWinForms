using System.Collections;
using System.Collections.Generic;
using System.Drawing.Design;

namespace System.ComponentModel.Design;

public class CollectionEditor : UITypeEditor
{
    private Type? _collectionItemType;
    private Type[]? _newItemTypes;

    public CollectionEditor(Type type)
    {
        CollectionType = type ?? throw new ArgumentNullException(nameof(type));
    }

    protected Type CollectionType { get; }

    protected Type CollectionItemType => _collectionItemType ??= CreateCollectionItemType();

    protected Type[] NewItemTypes => _newItemTypes ??= CreateNewItemTypes();

    protected virtual Type CreateCollectionItemType()
    {
        if (CollectionType.IsArray)
            return CollectionType.GetElementType() ?? typeof(object);

        foreach (Type interfaceType in CollectionType.GetInterfaces())
        {
            if (interfaceType.IsGenericType
                && interfaceType.GetGenericTypeDefinition() == typeof(System.Collections.Generic.ICollection<>))
            {
                return interfaceType.GetGenericArguments()[0];
            }
        }

        return typeof(object);
    }

    protected virtual Type[] CreateNewItemTypes()
    {
        return CollectionItemType == typeof(object)
            ? Array.Empty<Type>()
            : new[] { CollectionItemType };
    }

    protected virtual object CreateInstance(Type itemType)
    {
        ArgumentNullException.ThrowIfNull(itemType);
        if (itemType == typeof(string))
            return string.Empty;

        return TypeDescriptor.CreateInstance(null, itemType, null, null)
            ?? throw new InvalidOperationException($"Could not create collection item '{itemType.FullName}'.");
    }

    public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context)
    {
        return UITypeEditorEditStyle.Modal;
    }
}
