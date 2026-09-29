# Agent Guidance

Native popup retirement belongs to the creating source dispatcher after logical
window teardown. Retain exact window identity and failed renderer cleanup owners
until rendering and provider-aware native disposal both complete. Drain after
callbacks/polling without another poll or recursive disposal; preserve original
errors and refuse dispatcher shutdown while native ownership remains pending.
Successful renderer cleanup is not repeated. Hide separately; never destroy a
native surface after failed renderer cleanup, even on providers without native
view-lease guards. See docs/native-window-retirement.md;
source retirement integration is not factory, modal-input or desktop admission.

Native popup preparation, Show admission and nonactivating visibility delegate
the actual IWindow to ProGPU's shared typed provider APIs. Preserve the source
renderer-before-show callback and rejected-setup disposal. Never reinterpret an
owned Cocoa panel as GLFW or replace rejected typed admission with handle setup.
Factory selection and native input/scroll/platform qualification remain separate.

Explicit source PointerLeave retires only exact-window hover, preserving capture
and held buttons; PointerCancel retires capture/press/hover without keyboard focus
loss. Append event values without changing the old wire contract. Preserve pointer
position/modifiers, nested generations, callback errors and other windows' input;
do not invent MouseUp/Click or feed retirement to the drag sampler. These source
events do not select native providers or qualify native drag/scroll/platform UI.

Native focus-loss input also retires pointer ownership in nonactivating windows
that never held focus. Clear exact-source capture, press and hover before public
callbacks, release only that window's button owners, and retain a nested source
or focus-input generation. Pointer callback errors cannot undo committed focus
loss or replace the original exception. This does not admit a native pointer
provider, wheel conversion or cross-window capture transfer.

Portable hover has one thread-owned source window, independent of keyboard
focus. Pointer entry into another native source root retires its predecessor
before callbacks, cancels only its old continuation and respects replacement
handle identity. Preserve canonical submenu leave/timer behavior without
fabricating native leave, capture transfer or desktop qualification.

Portable pointer callbacks retain the exact source window/target and input
generation. Retire hover before MouseLeave, preserve nested hover and press
ownership, and stop an obsolete event after public hover/focus callbacks.
Do not clear a replacement capture or continue into recreated source handles.
These source lifetime guards do not qualify native popup input or platform UX.
See docs/portable-pointer-reentrancy.md.

Portable plain TextBox frames distinguish source window-adjustment estimates
from actual client geometry: native EDIT's FixedSingle border is client content;
Fixed3D owns non-client insets. Keep pre-handle/source scaling and live sizing
contracts distinct, including cached ClientSize versus actual client extent.
Painting, child clips, screen/input/CreateGraphics/invalidation use the actual
frame; retained text, caret and pointer mapping share native formatting insets.
Non-client borders block click-through without invented client mouse events.
Native top-level decorations and other controls do not acquire inferred insets.
ToolStripTextBox reuses its original professional border colors/state. Integer
DrawEdge bands are not centered antialiased strokes; preserve color/corner order.
Preserve native invariant border metrics at requested DPIs. Source/API/offscreen
checks do not qualify OS themes or complete native non-client input.
See docs/portable-textbox-frame.md.

Portable native-style focus painting reads per-handle window UI state, not the
managed properties that lazily broadcast hidden cues. Reuse canonical cache/
notification handling, preserve Tab versus Alt/F10 masks, and inherit state on
actual handle creation. Revalidate snapshotted child handles after callbacks;
never update a replacement generation or infer global last input for INITIALIZE.
Source/offscreen evidence is not desktop parity. See docs/portable-window-ui-cues.md.

Shared desktop popup scenarios hover the active source editor before baseline
capture, using its fresh native-verified client rectangle and ordinary pointer
guards. Never click to normalize focus, invent neutral desktop coordinates,
ignore changed capture state or restart the original per-case deadline. This
precondition does not qualify a previous failed or incomplete reference run.

Portable tooltip body/title drawing must match their NoPadding measurement;
the source already owns DPI-scaled outer padding. Keep wrapping, real fonts,
title/icon layout, OwnerDraw and caller-specified ToolTipSize authoritative.
Do not enlarge the popup to hide newly introduced text margins. Source layout
contracts do not qualify native appearance; see docs/portable-tooltip-text-margins.md.

Submenu ItemClicked dismissal shares canonical ancestor rollup and SourceControl
retention through command delivery. Keep leaf/ancestor cancellation and persistent
menus distinct from forced native teardown. Capture the original menu continuation
before callbacks; old closure and post-click keyboard cleanup must not deactivate
a replacement input chain, including one on the same menu bar. See
docs/portable-submenu-command-dismissal.md.

Portable menu hover expansion uses the actual live source menu/keyboard chain,
not Win32 ModalMenuFilter state or another owner's menu. Retain ancestor/sibling
transitions, source timer delays and existing selected/enabled checks; hidden or
retired parents and unadmitted persistent menus cannot expand implicitly. Keep
the native Windows branch unchanged. See docs/portable-menu-hover.md.

Popup renderer initialization waits for the actual typed owner when a hidden
handle is precreated. Reuse only a live same-service/dispatcher owner's device
through ProGPU's shared lifetime; keep surfaces, compositors and atlases separate.
Explicit pre-owner Graphics remains standalone. Never substitute another device
after initialization or weaken native ownership/nonactivation admission. See
docs/portable-popup-render-device.md; device reuse is not popup UI qualification.

Portable TextRenderer measurement extends the native CALCRECT rectangle through
the final line; proposed height is not a fitting or trimming viewport. Preserve
the width constraint, font realization, padding and independent drawing clip.
Do not compensate clipped menu autosizing with a fixed menu height or DPI factor.
Finite-height single-line vertical-alignment measurement remains a separate
native contract. See docs/portable-text-measurement-height.md.

Portable TextRenderer margins share the original native rounding policy. Use
the realized font metric, preserve LeftAndRightPadding precedence and NoPadding
editor layout, subtract margins before wrapping, and clip at the outer caller
rectangle so overhang can use the padding. Restore Graphics state; do not claim
all GDI metrics or native theme parity. See docs/portable-text-margins.md.

Portable PropertyGrid row/help/command metrics use the shared screen-reference
font height, preserving padding, rounding, cached invalidation and canonical
resize-driven pane arrangement. Non-Windows link settings reuse the existing
missing-IE-settings defaults; both portable and native Windows still read real
user preferences. Preserve explicit link styles and source font ownership.
See docs/portable-property-grid-font-metrics.md.

Portable DataGridView default rows use the shared screen-reference font height
for Control.DefaultFont plus the original nine pixels. Preserve the process
cache, minimum height, cloning and explicit row/template sizes; a different grid
font does not implicitly enable row autosizing. See docs/portable-grid-default-row-dpi.md.

Portable font autoscaling measures both dimensions at the canonical screen
reference DPI. Reuse the selected font's shared target-height calculation,
not parameterless Font.Height, a derived control's FontHeight override or the
measurement string's ink bounds. Preserve width rounding, pixel-font units and
the native Windows branch. See docs/portable-font-autoscale-dpi.md.

Portable collapsed-caret Backspace/Delete removes an adjacent source CRLF pair
as one break. Keep explicit UTF-16 selections exact, standalone CR/LF and
surrogate-pair behavior intact, and existing key suppression/read-only/event
ordering. This edit policy does not normalize stored source or infer native
desktop qualification. See docs/portable-newline-deletion.md.

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
Source cases do not qualify complete editor navigation, IME or
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

Plain TextBox navigation uses original UTF-16 endpoints and the cached signed selection anchor, not ordered public SelectionStart. Preserve filtered/managed key precedence and read-only selection without edit notifications. Multiline Home/End and Up/Down require the optional retained row-navigation capability; never infer wrapped positions from newline scans. Retain preferred X through vertical movement, reset it on nonvertical selection or layout replacement, and keep MaskedTextBox/RichTextBox semantics separate. See docs/portable-text-row-navigation.md.

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
