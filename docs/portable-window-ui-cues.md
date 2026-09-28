# Portable window UI cues

The original Microsoft popup consumer keeps focus indicators visible through its
14-phase interaction: its owner and ComboBox report UI-state flags 2 until F10,
then 0. The committed ComboBox paints its dotted focus rectangle. These were
bounded, read-only `WM_QUERYUISTATE` observations, not injected cue changes.

Portable ComboBox and ListBox painting previously called managed `ShowFocusCues`.
Its canonical lazy initialization can hide both focus and keyboard cues. Native
Windows controls instead paint against their window's state, without consulting
that managed property. A drawing operation must not initialize a different policy.

The portable default procedure now retains a separate per-handle window state.
New child handles inherit the live parent's state; top-level state starts with
the flags clear. `WM_QUERYUISTATE` reads it without initializing managed caches.
`WM_CHANGEUISTATE` propagates to the root; `WM_UPDATEUISTATE` updates the addressed
subtree. The original managed `WmUpdateUIState` still owns its cache, virtual
property reads, notifications and invalidation. Lazy property initialization
broadcasts the same hide masks as the original Windows source.

The source key policy remains unchanged: Tab clears hidden focus, Alt/F10 clear
hidden accelerators. Built-in ComboBox/ListBox paint reads the separate window
state. User-painted controls retain their virtual managed properties. ComboBox
explicitly routes these messages to the shared base policy without touching its
Windows-only registered mouse-enter message.

Propagation snapshots live child handles and revalidates membership/generation
after callbacks. It cannot deliver an old update to a replaced handle or create
hidden child handles. Newly created children inherit the already-published state.
This bridge implements SET/CLEAR and query; it does not infer `UIS_INITIALIZE`
from a guessed last input device or claim global OS last-input qualification.

## Validation

Nine source cases cover noninitializing queries, lazy property broadcast,
hierarchy versus subtree updates, handle inheritance, replacement during a
callback, no-op notifications and the separate keyboard flags. Two cases render
real ComboBox/ListBox pixels: initial shown focus equals explicit restored focus,
hidden focus changes pixels, and drawing never mutates the window state.

The first six cases failed on the previous implementation's absent portable
message route. Subsequent pixel tests exposed and fixed ComboBox's separate
registered-message path. The initial pixel fixture also needed its ordinary
typed headless focus-gained event; no production focus normalization was added.
CI retains the complete lifecycle suite and additionally requires all nine cases
with no skips. The complete local lifecycle run passed all 735 cases with zero
skips on .NET 10.0.9; the targeted nine cases also passed independently. Offscreen
tests do not replace the original desktop package
comparison, Windows/Linux/macOS appearance, native theme or final popup gate.

Primary contracts: [query](https://learn.microsoft.com/en-us/windows/win32/menurc/wm-queryuistate),
[change](https://learn.microsoft.com/en-us/windows/win32/menurc/wm-changeuistate),
and [update](https://learn.microsoft.com/en-us/windows/win32/menurc/wm-updateuistate).
