# Agent Guidance

## LibreWinForms Port Rules

LibreWinForms follows the same source-reuse and reflection-free rules as LibreWPF. Reuse upstream WinForms managed code wherever possible, and modify it only where a portable platform seam, ProGPU/Silk.NET backend, or Win32 abstraction is required.

Runtime reflection, duck-typed object probes, private-field scans, and fake WinForms-shaped compatibility objects are not acceptable product paths. Transitional package aliases are allowed only when documented with an exit path to source-built WinForms code.

Keep public package branding on `LibreWinForms.*`. Preserve runtime API identities such as `System.Windows.Forms` and `WindowsFormsIntegration` unless a separate code migration explicitly changes them.

Platform work should be typed and reusable: windowing/input, menus/popups, painting/composition, clipboard/dialogs, drag/drop, timers, system settings, and GDI/GDI+ shims should flow through narrow contracts implemented by Silk.NET, ProGPU, and explicit Win32/local-OS adapters.

Portable dropdown owner deactivation reuses the native bounded active-leaf close policy. Keep opening/visible ownership separate from AutoClose admission: canceled parent openings or closes can leave a live child, including persistent children. Retain its typed Form owner until the last member releases it; preserve cancellation, original callback exceptions, and forced root-disposal cleanup. Source lifecycle tests do not qualify native popup input or UI. See docs/portable-popup-deactivation.md.

Canonical top-level dropdowns stage a real hidden Popup window and bind a live typed Form owner before display. Preserve arranged coordinate bounds, nonactivating backend admission, precreated handle reuse, and independent popup topmost state. Native owner hide/minimize/destruction must release persistent as well as ordinary popup surfaces and reject reentrant admission during teardown; callback exceptions cannot leave stale handles or roll back a completed visibility transition. Keep reusable source dropdown objects and ordinary cancelable closes distinct from forced native resource loss. Native window creation alone does not qualify keyboard/menu-mode routing or platform UI. See docs/canonical-native-dropdowns.md.

Portable dropdown keyboard input retains the Form's real focus and reuses canonical menu preprocessing after caller filters. Retire typed MenuStrip continuation leases before public deactivation callbacks; hide, handle loss and disposal must not resurrect them. Share native close-reason expansion policy and keep ordinary owner editing unchanged without an active menu. Source keyboard contracts are not native desktop or hosted-editor qualification. See docs/canonical-dropdown-keyboard.md.

Bare Alt/F10 menu entry waits for an unconsumed matching release in the same live window generation. Preserve shortcut/filter/managed-handler precedence, interrupted-chord cancellation and original source MenuStrip selection. Capture exact main-menu and continuation identities before release callbacks; deselection/paint or MenuDeactivate replacements, including a new lease for the same strip, must survive old cleanup. Do not install native menu hooks or transfer focus to emulate activation. See docs/portable-menu-key-activation.md; source cases do not qualify system menus, Alt+mnemonic entry or native platform UX.

Native character input retains full Unicode scalars and distinguishes an actual plain callback from an unpaired Alt system character. Never infer layout text from key names or classify Option/AltGr plain text as a mnemonic. Pair modified/plain callbacks before application delivery, retain FIFO order across nested pumps, and cancel exact-owner pending/queued input on retirement. Own the actual GLFW callback slots and release them before input/window destruction; preserve original installation errors and later callback replacements. See docs/native-menu-characters.md; deterministic source/callback tests do not qualify physical keyboard layouts or desktop UI.

SharpDevelop is the initial integration driver. Prefer porting the real WinForms API/designer/resource code from this repository over expanding LibreWPF-local compatibility shims.

Portable plain-text clipboard editing uses canonical Clipboard conversion and
source UTF-16 selection mutation. Preserve virtual MaskedTextBox handlers,
password/read-only protection, failed-write source retention and original
parent/ShortcutsEnabled command ordering. Never flatten rich-text/protected
documents into this path or infer undo, IME or native clipboard qualification.

The portable SDK must retain the original shared and language-specific analyzer payload and resource satellites in Project and Package modes. Keep `LibreWinFormsSdkOwnsApplicationConfiguration` confined to suppressing the upstream full configuration generator when the SDK/caller already owns that policy; absent/false preserves upstream defaults. The explicit default-font supplement additionally requires the exact SDK Initialize-emission predicate, reuses the original invariant font parser/descriptor, and leaves absent-font and caller-owned initialization unchanged. Do not suppress WFO1000 globally, alter bootstrap behavior, or omit analyzers to avoid duplicate Initialize. Preserve the payload-hash, actual compiler negative/positive, missing-file, and fresh-process font gates in `docs/sdk-analyzer-parity.md` and `docs/sdk-application-default-font.md`.
