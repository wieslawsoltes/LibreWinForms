# Agent Guidance

Portable plain multiline TextBox Enter uses the existing translated KeyPress
and exact key-cycle suppression path. Preserve read-only/public-handler
precedence, duplicate host-character suppression and the following text packet;
do not replace rich/masked or native Windows semantics. Blank/trailing row carets
come from retained ProGPU row metadata, never painted placeholder glyphs or
newline scans in the source control. See docs/portable-editor-hard-breaks.md.

Portable TextBox painting and horizontal interaction share one owned optional
text-layout generation. Keep original UTF-16 selection anchors, exact source
font realization and target DPI in the cache contract. Source owns the fixed
viewport clip while glyphs, selection and caret scroll together. Retire layouts
and blink timers on real control/handle lifetime boundaries, recheck public
callback disposal, and never expose password source to the shaping provider.
Source cases do not qualify empty hard rows, complete editor navigation, IME or
native desktop appearance. See docs/portable-retained-text-interaction.md.

Portable text uses explicit Drawing target DPI: DevicePixels uses actual window
resolution, Logical stays96 with presentation scaling. Canonical TextRenderer
separately realizes already-scaled source fonts against InitialSystemDpi, as the
native FontCache does; do not multiply PMv2 font scaling by live target DPI again.
Pixel fonts remain pixels and FontHeight rounds the fractional line metric only
at the end, not the GDI em height. Preserve original source Font identity and raw
Graphics/DrawString semantics. Native appearance remains a separate gate; see
docs/portable-text-target-dpi.md.

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

Hosted dropdown controls borrow source logical focus from their live Form owner, never native popup activation. Retain actual ToolStripControlHost membership, owner/window identities and source UTF-16 selection. Retire the lease through internal source lifetime boundaries before public callbacks can throw or replace it; restore only the still-owned source focus. Keep input filters, canonical preprocessing and translated-character suppression connected to the same focus owner. Source focus and caret indices do not qualify drawn caret, hit-to-character placement or native desktop UI. See docs/portable-hosted-menu-input.md.

Editable portable ComboBox uses an actual canonical TextBox child and the canonical ListBox popup. Its source editor remains the Form focus owner, admitted by a distinct exact-generation keyboard target rather than a fabricated ToolStripControlHost lease. Preserve user-edit notifications separately from programmatic matching-item policy, public UTF-16 selection, original filters and composite focus callbacks. Only admitted list keys forward to ListBox defaults; edit Home/End and modifiers remain edit input. Retire obsolete handles/targets before reentrant work, and do not count source selection or content painting as caret, native theme or desktop qualification. See docs/portable-combobox-dropdown.md.

Portable text-packet retirement cancels its obsolete suffix independently of physical key-cycle suppression. Standalone text callbacks have no required future KeyUp; an outer callback cannot suppress a nested input generation. Preserve held-key suppression until its release and bind editable focus completion to the same live source/editor/Form handles.

Plain TextBox boundary navigation uses original UTF-16 endpoints and the cached signed selection anchor, not ordered public SelectionStart. Preserve filtered/managed key precedence and read-only selection without edit notifications. Multiline visual-line Home/End still needs retained layout; never infer wrapped positions from newline scans. Keep MaskedTextBox/RichTextBox semantics separate. See docs/portable-text-boundary-navigation.md.

SharpDevelop is the initial integration driver. Prefer porting the real WinForms API/designer/resource code from this repository over expanding LibreWPF-local compatibility shims.

Portable plain-text clipboard editing uses canonical Clipboard conversion and
source UTF-16 selection mutation. Preserve virtual MaskedTextBox handlers,
password/read-only protection, failed-write source retention and original
parent/ShortcutsEnabled command ordering. Never flatten rich-text/protected
documents into this path or infer undo, IME or native clipboard qualification.

The portable SDK must retain the original shared and language-specific analyzer payload and resource satellites in Project and Package modes. Keep `LibreWinFormsSdkOwnsApplicationConfiguration` confined to suppressing the upstream full configuration generator when the SDK/caller already owns that policy; absent/false preserves upstream defaults. The explicit default-font supplement additionally requires the exact SDK Initialize-emission predicate, reuses the original invariant font parser/descriptor, and leaves absent-font and caller-owned initialization unchanged. Do not suppress WFO1000 globally, alter bootstrap behavior, or omit analyzers to avoid duplicate Initialize. Preserve the payload-hash, actual compiler negative/positive, missing-file, and fresh-process font gates in `docs/sdk-analyzer-parity.md` and `docs/sdk-application-default-font.md`.

SDK-owned initialization honors ApplicationHighDpiMode through the original enum parser and SystemAware default, before the optional default-font hook. Keep caller-owned opt-out and actual compiler-phase ownership together; invalid values retain WFO0002 rather than becoming generated code or silently selecting another mode. Generated policy and runtime source/native coordinate mapping are distinct contracts: successful compilation does not qualify SystemAware desktop scaling. See docs/sdk-application-high-dpi-mode.md.

Initial native placement publishes the returned typed window position before source HandleCreated, preserving canonical source/client sizing and PerMonitorV2 autoscaling. Revalidate exact native object and handle identity after provider reads and source callbacks. Keep the canonical disposal-during-creation guard and later explicit popup placement; never fabricate client coordinates from frame decoration or rescale the validation driver. See docs/portable-initial-window-position.md; source tests are not native UI qualification.

Native geometry diagnostics resolve only an existing service-owned Silk window on its own dispatcher. Return the ProGPU controller snapshot unchanged in actual native desktop points, with backing scale separate. No Attach/Apply, source-bound fallback, external-owner adoption or guessed native identity is allowed; missing or retired handles return default/unavailable. See docs/native-window-geometry-service.md; the optional diagnostic does not qualify source-coordinate or popup UI parity.

Tooltip popup requests carry the actual source owner's coordinate mode for screen bounds and local drawing. Preserve the original Logical default for old callers, reject unknown modes before mutation and replace rather than reinterpret a live surface when its mode changes. Do not divide cursor/source coordinates to hide a backend mode mismatch or multiply global origins by backing scale. See docs/portable-tooltip-coordinate-mode.md; source tests do not qualify native desktop placement.
