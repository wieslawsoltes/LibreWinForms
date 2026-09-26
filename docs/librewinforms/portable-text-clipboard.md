# Portable TextBox clipboard editing

The canonical portable `TextBoxBase.Copy`, `Cut`, and `Paste` methods dispatch
their existing clipboard messages through the source control's virtual
`WndProc`. Windows builds retain the original native `SendMessage` calls.
Ordinary text controls use the existing `Clipboard` conversion/service and
source-owned UTF-16 selection replacement; no backend text copy is introduced.
`MaskedTextBox` retains its original mask-provider clipboard handlers.
Portable selection reads order the cached endpoints, matching the native
[`EM_GETSEL`](https://learn.microsoft.com/en-us/windows/win32/controls/em-getsel)
range contract even when `Select` receives a negative length.
This retains the source's cached anchor/direction while copy, cut and paste
consume the same nonnegative UTF-16 interval.

The portable command path also handles Ctrl+C/X/V and Shift+Delete/Insert after
the original parent-command and `ShortcutsEnabled` processing. Copy and cut
cannot export password text (including `UseSystemPasswordChar`). Read-only
controls allow copy but not cut/paste, and a failed clipboard write cannot
delete the ordinary TextBox selection. Paste retains canonical text-format
conversion, distinguishes missing text from present empty text, and applies
the existing user-input maximum length, casing and source notification policy.
It does not synthesize `KeyPress` events. NUL ends native text; single-line
controls stop at the first line break, while multiline controls retain it.

This connects ordinary plain-text clipboard editing, not rich-text transport,
undo/history, IME composition, native desktop clipboard interoperability, or
rendering qualification. RichTextBox public clipboard methods fail explicitly
until its document-owned transport exists, rather than silently flattening
formatted/protected content. Existing MaskedTextBox policies remain separate.

## Source provenance and regression coverage

The source algorithms are `MaskedTextBox.WmCopy` / `WmPaste` for clipboard
ownership and failure handling, `Clipboard.GetTypedDataIfAvailable` for the
unchanged typed/legacy DataObject conversion, and
`TextBoxBase.ReplacePortableSelection` for original text/selection mutation.
The native operation contracts were checked against Microsoft's
[WM_COPY](https://learn.microsoft.com/en-us/windows/win32/dataxchg/wm-copy),
[WM_CUT](https://learn.microsoft.com/en-us/windows/win32/dataxchg/wm-cut),
[WM_PASTE](https://learn.microsoft.com/en-us/windows/win32/dataxchg/wm-paste), and
[TextBoxBase.Paste](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.textboxbase.paste?view=windowsdesktop-10.0)
documentation. Native undo remains a separate unconnected contract; these
changes do not claim it from successful clipboard replacement.

`CanonicalClipboardEditingTests` exercises actual canonical controls with the
existing typed headless clipboard/input service: forward/reverse UTF-16 selection, source
events, absent/empty/converted text, password/read-only protection, failed
writes, length/casing/line boundaries, command ownership, original masked
handlers, and a real DataGridView editor entered by F2 and committed by Enter.
These are source behavior tests, not native OS clipboard or visual evidence.

The first compiled 193-case canonical run passed 192 cases and exposed a
remaining native `EM_SETPASSWORDCHAR` call during portable handle creation.
That native edit-window initialization now remains only in the Windows build;
portable password protection continues to read the original source fields.
The password clipboard cases cover both preexisting and newly created handles,
with either explicit or system password masking (195 total canonical cases).
This does not qualify password rendering or IME behavior.
