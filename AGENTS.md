# Agent Guidance

## LibreWinForms Port Rules

LibreWinForms follows the same source-reuse and reflection-free rules as LibreWPF. Reuse upstream WinForms managed code wherever possible, and modify it only where a portable platform seam, ProGPU/Silk.NET backend, or Win32 abstraction is required.

Runtime reflection, duck-typed object probes, private-field scans, and fake WinForms-shaped compatibility objects are not acceptable product paths. Transitional package aliases are allowed only when documented with an exit path to source-built WinForms code.

Keep public package branding on `LibreWinForms.*`. Preserve runtime API identities such as `System.Windows.Forms` and `WindowsFormsIntegration` unless a separate code migration explicitly changes them.

Platform work should be typed and reusable: windowing/input, menus/popups, painting/composition, clipboard/dialogs, drag/drop, timers, system settings, and GDI/GDI+ shims should flow through narrow contracts implemented by Silk.NET, ProGPU, and explicit Win32/local-OS adapters.

Portable dropdown owner deactivation reuses the native bounded active-leaf close policy. Keep opening/visible ownership separate from AutoClose admission: canceled parent openings or closes can leave a live child, including persistent children. Retain its typed Form owner until the last member releases it; preserve cancellation, original callback exceptions, and forced root-disposal cleanup. Source lifecycle tests do not qualify native popup input or UI. See docs/portable-popup-deactivation.md.

SharpDevelop is the initial integration driver. Prefer porting the real WinForms API/designer/resource code from this repository over expanding LibreWPF-local compatibility shims.

Portable plain-text clipboard editing uses canonical Clipboard conversion and
source UTF-16 selection mutation. Preserve virtual MaskedTextBox handlers,
password/read-only protection, failed-write source retention and original
parent/ShortcutsEnabled command ordering. Never flatten rich-text/protected
documents into this path or infer undo, IME or native clipboard qualification.

The portable SDK must retain the original shared and language-specific analyzer payload and resource satellites in Project and Package modes. Keep `LibreWinFormsSdkOwnsApplicationConfiguration` confined to suppressing the upstream full configuration generator when the SDK/caller already owns that policy; absent/false preserves upstream defaults. The explicit default-font supplement additionally requires the exact SDK Initialize-emission predicate, reuses the original invariant font parser/descriptor, and leaves absent-font and caller-owned initialization unchanged. Do not suppress WFO1000 globally, alter bootstrap behavior, or omit analyzers to avoid duplicate Initialize. Preserve the payload-hash, actual compiler negative/positive, missing-file, and fresh-process font gates in `docs/sdk-analyzer-parity.md` and `docs/sdk-application-default-font.md`.
