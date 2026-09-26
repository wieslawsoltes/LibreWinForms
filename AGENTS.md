# Agent Guidance

## LibreWinForms Port Rules

LibreWinForms follows the same source-reuse and reflection-free rules as LibreWPF. Reuse upstream WinForms managed code wherever possible, and modify it only where a portable platform seam, ProGPU/Silk.NET backend, or Win32 abstraction is required.

Runtime reflection, duck-typed object probes, private-field scans, and fake WinForms-shaped compatibility objects are not acceptable product paths. Transitional package aliases are allowed only when documented with an exit path to source-built WinForms code.

Keep public package branding on `LibreWinForms.*`. Preserve runtime API identities such as `System.Windows.Forms` and `WindowsFormsIntegration` unless a separate code migration explicitly changes them.

Platform work should be typed and reusable: windowing/input, menus/popups, painting/composition, clipboard/dialogs, drag/drop, timers, system settings, and GDI/GDI+ shims should flow through narrow contracts implemented by Silk.NET, ProGPU, and explicit Win32/local-OS adapters.

SharpDevelop is the initial integration driver. Prefer porting the real WinForms API/designer/resource code from this repository over expanding LibreWPF-local compatibility shims.

Portable plain-text clipboard editing uses canonical Clipboard conversion and
source UTF-16 selection mutation. Preserve virtual MaskedTextBox handlers,
password/read-only protection, failed-write source retention and original
parent/ShortcutsEnabled command ordering. Never flatten rich-text/protected
documents into this path or infer undo, IME or native clipboard qualification.
