// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Drawing;

namespace System.Windows.Forms;

public partial class ComboBox
{
    private TextBox? _portableEditor;
    private bool _portableEditorRetiring;
    private bool _portableEditSelectionSet;
    private int _portableEditorSyncDepth;
    private uint _portableEditorTextVersion;

    private bool HasPortableEditorFocus => GetLivePortableEditor() is { Focused: true };

    private TextBox? GetLivePortableEditor()
        => DropDownStyle == ComboBoxStyle.DropDown
            && _portableEditor is { IsDisposed: false, Disposing: false } editor
            && ReferenceEquals(editor.ParentInternal, this) ? editor : null;

    private void EnsurePortableEditor()
    {
        if (DropDownStyle != ComboBoxStyle.DropDown || !IsHandleCreated || IsDisposed || Disposing || _portableEditorRetiring)
            return;
        if (_portableEditor is not null)
        {
            if (GetLivePortableEditor() is null)
                throw new InvalidOperationException("The ComboBox's source editor was removed or disposed while its owner is live.");
            return;
        }

        TextBox editor = new()
        {
            AutoSize = false,
            BorderStyle = BorderStyle.None,
            Multiline = false,
            TabStop = false,
            MaxLength = MaxLength,
            Text = WindowText
        };
        _portableEditor = editor;
        editor.TextChanged += PortableEditorTextChanged;
        editor.GotFocus += PortableEditorGotFocus;
        editor.LostFocus += PortableEditorLostFocus;
        editor.PreviewKeyDown += PortableEditorPreviewKeyDown;
        editor.KeyDown += PortableEditorKeyDown;
        editor.KeyPress += PortableEditorKeyPress;
        editor.KeyUp += PortableEditorKeyUp;
        editor.PortableHostedFocusLifetimeChanged += PortableEditorLifetimeChanged;
        try
        {
            Controls.Add(editor);
            LayoutPortableEditor();
            if (_portableEditSelectionSet)
                editor.Select(_portableSelectionStart, _portableSelectionLength);
        }
        catch (Exception original)
        {
            try { DisposePortableEditor(); }
            catch (Exception cleanup) { original.Data["PortableComboBoxEditCleanupException"] = cleanup; }
            throw;
        }
    }

    private void DisposePortableEditor()
    {
        if (_portableEditorRetiring)
            return;
        _portableEditorRetiring = true;
        TextBox? editor = _portableEditor;
        _portableEditor = null;
        _portableEditorTextVersion++;
        try
        {
            if (editor is null)
                return;
            _portableSelectionStart = editor.SelectionStart;
            _portableSelectionLength = editor.SelectionLength;
            _portableEditSelectionSet = true;
            editor.TextChanged -= PortableEditorTextChanged;
            editor.GotFocus -= PortableEditorGotFocus;
            editor.LostFocus -= PortableEditorLostFocus;
            editor.PreviewKeyDown -= PortableEditorPreviewKeyDown;
            editor.KeyDown -= PortableEditorKeyDown;
            editor.KeyPress -= PortableEditorKeyPress;
            editor.KeyUp -= PortableEditorKeyUp;
            editor.PortableHostedFocusLifetimeChanged -= PortableEditorLifetimeChanged;
            editor.Dispose();
        }
        finally { _portableEditorRetiring = false; }
    }

    private bool FocusPortableEditor()
    {
        EnsurePortableEditor();
        TextBox? editor = GetLivePortableEditor();
        return editor is { Focused: false, Visible: true, Enabled: true } && editor.Focus();
    }

    private protected override bool FocusInternal()
    {
        if (DropDownStyle != ComboBoxStyle.DropDown)
            return base.FocusInternal();

        nint handle = IsHandleCreated ? Handle : 0;
        TextBox? editor = GetLivePortableEditor();
        nint editorHandle = editor is { IsHandleCreated: true } ? editor.Handle : 0;
        Form? owner = FindForm();
        nint ownerHandle = owner is { IsHandleCreated: true } ? owner.Handle : 0;
        bool focused = base.FocusInternal();

        // RecreateHandle can retain the ComboBox as the already-notified Form
        // focus target while replacing its editor. Such a Focus call has no
        // GotFocus notification through which to redirect to the new child.
        // Complete only this request's still-owned live source generation;
        // base focus callbacks may have moved focus or replaced either window.
        if (focused && handle != 0 && editorHandle != 0 && ownerHandle != 0
            && IsHandleCreated && Handle == handle && !IsDisposed && !Disposing
            && base.Focused && ReferenceEquals(owner, FindForm())
            && owner is { IsHandleCreated: true, IsDisposed: false, Disposing: false, IsPortableActivationOwner: true }
            && owner.Handle == ownerHandle && ReferenceEquals(owner.PortableFocusedControl, this)
            && ReferenceEquals(editor, GetLivePortableEditor())
            && editor is { IsHandleCreated: true, IsDisposed: false, Disposing: false, Visible: true, Enabled: true }
            && editor.Handle == editorHandle)
        {
            editor.Focus();
        }

        // Child GotFocus is another public callback. Never restore this source
        // after it chooses a different target, replaces the handle or throws.
        return Focused;
    }

    private bool HandlePortableEditorGotFocus(EventArgs e)
    {
        if (DropDownStyle != ComboBoxStyle.DropDown)
            return false;
        EnsurePortableEditor();
        if (GetLivePortableEditor() is { Focused: false, Visible: true, Enabled: true } editor)
        {
            editor.Focus();
            // Its synchronous GotFocus handled the composite, or a callback
            // moved focus elsewhere. Neither belongs to this old notification.
            if (!base.Focused)
                return true;
        }

        if (Focused && !_canFireLostFocus)
        {
            _canFireLostFocus = true;
            base.OnGotFocus(e);
        }

        return true;
    }

    private bool HandlePortableEditorLostFocus(EventArgs e)
    {
        if (DropDownStyle != ComboBoxStyle.DropDown)
            return false;
        if (!Focused && _canFireLostFocus)
        {
            _canFireLostFocus = false;
            base.OnLostFocus(e);
        }

        return true;
    }

    private void PortableEditorGotFocus(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, GetLivePortableEditor()))
            OnGotFocus(e);
    }

    private void PortableEditorLostFocus(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, GetLivePortableEditor()) && !Focused)
        {
            _portableDropDown?.RequestClose();
            if (!Focused)
                OnLostFocus(e);
        }
    }

    private void PortableEditorLifetimeChanged(bool retiring)
    {
        if (retiring || GetLivePortableEditor() is not { Visible: true, Enabled: true, IsHandleCreated: true })
            _portableDropDown?.RequestClose();
    }

    private void PortableEditorPreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
    {
        if (!ReferenceEquals(sender, GetLivePortableEditor()))
            return;
        OnPreviewKeyDown(e);
        if (IsPortableDropDownInputKey(e.KeyData) || (IsPortableDropDownVisible && e.KeyData is Keys.Up or Keys.Down))
            e.IsInputKey = true;
    }

    private void PortableEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (!ReferenceEquals(sender, GetLivePortableEditor()))
            return;
        OnKeyDown(e);
        if (!e.Handled && !e.SuppressKeyPress && ReferenceEquals(sender, GetLivePortableEditor()))
            _portableDropDown?.NavigateFromEditor(e);
    }

    private void PortableEditorKeyPress(object? sender, KeyPressEventArgs e)
    {
        if (ReferenceEquals(sender, GetLivePortableEditor()))
            OnKeyPress(e);
    }

    private void PortableEditorKeyUp(object? sender, KeyEventArgs e)
    {
        if (ReferenceEquals(sender, GetLivePortableEditor()))
            OnKeyUp(e);
    }

    private void PortableEditorTextChanged(object? sender, EventArgs e)
    {
        if (_portableEditorSyncDepth != 0 || sender is not TextBox editor || !ReferenceEquals(editor, GetLivePortableEditor()))
            return;
        string text = editor.Text;
        if (text == WindowText)
            return;

        // The native EDIT path updates its text/current choice before emitting
        // CBN_EDITUPDATE then CBN_EDITCHANGE. It does not run Text's matching-item
        // setter or manufacture SelectionChangeCommitted/SelectedIndexChanged.
        uint version = ++_portableEditorTextVersion;
        _selectedIndex = -1;
        WindowText = text;
        _portableDropDown?.SynchronizeEditorSelection();
        OnTextUpdate(EventArgs.Empty);
        if (version == _portableEditorTextVersion && ReferenceEquals(editor, GetLivePortableEditor())
            && !IsDisposed && !Disposing)
            OnTextChanged(EventArgs.Empty);
    }

    private void SyncPortableEditorText()
    {
        _portableEditorTextVersion++;
        if (_portableEditorSyncDepth != 0 || GetLivePortableEditor() is not { } editor || editor.Text == WindowText)
            return;
        _portableEditorSyncDepth++;
        try { editor.Text = WindowText; }
        finally { _portableEditorSyncDepth--; }
    }

    private void SyncPortableEditorProperties()
    {
        if (GetLivePortableEditor() is { } editor)
            editor.MaxLength = MaxLength;
    }

    private void RestorePortableEditorText(string text, int start, int length)
    {
        TextBox? editor = GetLivePortableEditor();
        // Bypass only ComboBox.Text's matching-item policy, not Control's
        // canonical text notifications. The canceled snapshot may have no
        // selected item even when its edit text equals an item caption.
        base.Text = text;
        if (ReferenceEquals(editor, GetLivePortableEditor()) && WindowText == text && !IsDisposed && !Disposing)
            Select(start, length);
    }

    private int GetPortableEditSelectionStart()
        => GetLivePortableEditor()?.SelectionStart ?? Math.Min(_portableSelectionStart, WindowText.Length);

    private int GetPortableEditSelectionLength()
        => GetLivePortableEditor()?.SelectionLength
            ?? Math.Min(_portableSelectionLength, WindowText.Length - GetPortableEditSelectionStart());

    private void ApplyPortableEditSelection()
    {
        _portableEditSelectionSet = true;
        GetLivePortableEditor()?.Select(_portableSelectionStart, _portableSelectionLength);
    }

    private bool SetPortableEditSelectedText(string? value)
    {
        if (DropDownStyle != ComboBoxStyle.DropDown)
            return false;
        CreateControl();
        EnsurePortableEditor();
        TextBox editor = GetLivePortableEditor()
            ?? throw new InvalidOperationException("The editable ComboBox source could not create its actual editor.");
        editor.SelectedText = value ?? string.Empty;
        return true;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        LayoutPortableEditor();
    }

    private void LayoutPortableEditor()
    {
        if (GetLivePortableEditor() is not { } editor)
            return;
        Rectangle text = GetPortableComboBoxTextBounds();
        // TextBox already owns real source font metrics and text rendering.
        // Center its single line without approximating any character widths.
        int height = Math.Min(Math.Max(0, text.Height), editor.Font.Height);
        editor.Bounds = new Rectangle(text.X, text.Y + (text.Height - height) / 2, Math.Max(0, text.Width), height);
    }
}
#endif
