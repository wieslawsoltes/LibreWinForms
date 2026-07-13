using System.Collections;
using System.Collections.Generic;
using System.Drawing.Design;
using System.Windows.Forms;
using System.Windows.Forms.Design;

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

    public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider provider, object? value)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (value is not IList source
            || provider.GetService(typeof(IWindowsFormsEditorService)) is not IWindowsFormsEditorService editorService)
        {
            return value;
        }

        using var form = new PortableCollectionEditorForm(this, source);
        if (editorService.ShowDialog(form) != DialogResult.OK)
            return value;
        if (context is not null && !context.OnComponentChanging())
            return value;

        object result = CommitItems(value, source, form.Items);
        context?.OnComponentChanged();
        return result;
    }

    private object CommitItems(object value, IList source, IReadOnlyList<object> items)
    {
        if (CollectionType.IsArray)
        {
            Array array = Array.CreateInstance(CollectionItemType, items.Count);
            for (int i = 0; i < items.Count; i++)
                array.SetValue(items[i], i);
            return array;
        }

        if (source.IsReadOnly || source.IsFixedSize)
            return value;

        source.Clear();
        for (int i = 0; i < items.Count; i++)
            source.Add(items[i]);
        return value;
    }

    private sealed class PortableCollectionEditorForm : Form
    {
        private readonly CollectionEditor _editor;
        private readonly ListBox _itemsList;
        private readonly List<object> _items = new();

        public PortableCollectionEditorForm(CollectionEditor editor, IList source)
        {
            _editor = editor;
            Name = "CollectionEditorForm";
            Text = "Collection Editor";
            ClientSize = new System.Drawing.Size(420, 280);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            _itemsList = new ListBox
            {
                Name = "ItemsList",
                Location = new System.Drawing.Point(12, 12),
                Size = new System.Drawing.Size(292, 220)
            };
            foreach (object? item in source)
            {
                if (item is not null)
                    AddItem(item);
            }

            var addButton = new Button
            {
                Name = "AddButton",
                Text = "Add",
                Location = new System.Drawing.Point(316, 12),
                Size = new System.Drawing.Size(92, 27),
                Enabled = editor.NewItemTypes.Length > 0
            };
            addButton.Click += (_, _) => AddNewItem();

            var removeButton = new Button
            {
                Name = "RemoveButton",
                Text = "Remove",
                Location = new System.Drawing.Point(316, 47),
                Size = new System.Drawing.Size(92, 27)
            };
            removeButton.Click += (_, _) => RemoveSelectedItem();

            var okButton = new Button
            {
                Name = "OkButton",
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new System.Drawing.Point(212, 241),
                Size = new System.Drawing.Size(92, 27)
            };
            var cancelButton = new Button
            {
                Name = "CancelButton",
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new System.Drawing.Point(316, 241),
                Size = new System.Drawing.Size(92, 27)
            };

            Controls.Add(_itemsList);
            Controls.Add(addButton);
            Controls.Add(removeButton);
            Controls.Add(okButton);
            Controls.Add(cancelButton);
            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        public IReadOnlyList<object> Items => _items;

        private void AddNewItem()
        {
            Type[] itemTypes = _editor.NewItemTypes;
            if (itemTypes.Length == 0)
                return;

            AddItem(_editor.CreateInstance(itemTypes[0]));
            _itemsList.SelectedIndex = _itemsList.Items.Count - 1;
        }

        private void AddItem(object item)
        {
            _items.Add(item);
            _itemsList.Items.Add(item);
        }

        private void RemoveSelectedItem()
        {
            int selectedIndex = _itemsList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _items.Count)
                return;

            _items.RemoveAt(selectedIndex);
            _itemsList.Items.RemoveAt(selectedIndex);
            _itemsList.SelectedIndex = Math.Min(selectedIndex, _itemsList.Items.Count - 1);
        }
    }
}
