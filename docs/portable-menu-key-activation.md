# Portable MenuStrip activation

Bare Alt and F10 use the original release-triggered default-window behavior:
application filters, canonical preprocessing and managed keyboard handlers run
first. A handled or suppressed key does not activate a menu. An input-key
classification alone is not consumption. Left/right Alt retain their existing
public key identities while participating in shared menu-key and UI-cue policy.

The receiving Form retains one pending physical key/window generation. Repeat
does not re-arm a consumed press. Other keys, text, pointer presses and focus loss
cancel pending activation before callbacks or filters. Shift, Control and Meta
chords remain distinct; an AltGr release cannot activate a menu. Nested input or
window replacement during callbacks cannot finish an older transition.

On an eligible release, the source `GetMainMenuStrip` lookup and `OnMenuKey`
selection algorithm are reused, including explicit versus recursive discovery,
disabled items and RTL ordering. Only native menu-mode admission is replaced by
the existing typed continuation. The selected MenuStrip must belong to the live
receiving Form. Public MenuActivate callbacks are followed by owner, main-menu
and exact-continuation revalidation before item selection. A second bare key
exits the continuation without moving native or editor focus.

Shortcut matching retains the original per-container and MDI ownership logic;
portable root comparisons use actual Control ownership instead of querying
Win32 ancestors for synthetic handles. An F10 shortcut therefore precedes
default activation.

The new 25 source cases exercise these paths through the typed platform input
callback inside Application.Run. Six additional source cases retain the original
release target across KeyUp replacement, invalidation/paint, same-strip lease
replacement and actual owner-handle recreation. They join every existing source case; no
filter, timeout or assertion is relaxed. Native Windows/macOS/Linux GUI and
installed-package acceptance remain separate. This does not qualify Alt+mnemonic
entry, system/MDI menus, accessibility, IME or hosted dropdown editor focus.

The initial build completed with zero errors and 622 existing source warnings.
The first 363-case run passed 360 and rejected three incorrect RTL fixture
expectations: the unchanged source algorithm reverses the requested direction
again in its RTL-aware scanner, retaining the first logical enabled item while
mirroring its position. The corrected fixtures assert both the same logical item
and opposite physical ordering. All 363 cases then passed with zero skips on
macOS ARM64 / .NET 10.0.5. The initial failed log remains retained; the product
selection algorithm was not changed to satisfy the fixtures.

After exact pre-callback release identities and retirement ordering were added,
the complete 369-case source gate passed with zero skips in 1.437 seconds. Both
the synchronous paint and invalidation replacement cases executed; the real
owner-handle recreation case also accepts a subsequent independent key cycle.
The full product build had zero errors, the same 622 existing warnings, and one
extra blank-line formatting warning; that blank line was removed afterward.
Logs are retained as `menu-key-reentrancy-build.log` and
`menu-key-reentrancy-tests.log` under the owned dropdown-keyboard artifact tree.
