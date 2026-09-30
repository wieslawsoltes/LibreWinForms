# Portable menu hover expansion

Canonical `MenuTimer` retains the native delay, selection, enabled/disposed
checks and sibling-transition close behavior. Its portable tick previously
required `ModalMenuFilter.InMenuMode`, but portable dropdowns use the typed
source activation chain instead of entering the Win32 message filter. The
result was a selected submenu item with a running timer that never expanded.

The portable tick now admits the item's live parent only when it belongs to the
current portable keyboard/menu chain. The active leaf and its ancestor strips
are included, so hovering a sibling can close the previous child and open the
next one. A main-menu continuation uses the existing live continuation lease.
Another Form's menu is not evidence for this parent's admission. Hidden, retired
or disposed parents and persistent `AutoClose=false` popups without menu-input
admission do not acquire it implicitly.

The Windows-source branch still uses its original Win32 menu-mode flag. No
platform activation, timer duration, source selection or native ownership
policy changes.

Eight canonical source regressions exercise actual pointer delivery and the
typed timer service: root and nested hover, sibling transition, close/hide/
disable, another owner's active menu and persistent-menu exclusion. Before the
fix all three positive expansion cases fail and the five rejection controls
pass. The source-first CI gate explicitly selects all eight, rejects skips and
retains its original ten-minute limit. Source tests do not establish native
desktop input, complete painted menu contents or cross-platform UI parity.

The test timer fixture retains all active registrations for these cases because
canonical pointer delivery starts both menu-expansion and item-hover timers.
Its existing single-last-timer helper remains unchanged for earlier tests.

In an unchanged Windows ARM64/Parallels D3D12 desktop diagnostic, the private
candidate copy opened the child menu and invoked its command through actual
pointer input. The original 60-second scenario then failed at
`04-context-command`: the child closed but the root menu remained visible.
The missing second parent-item label also remains unresolved. Only source Forms
was replaced over the earlier private owner-device candidate; the application,
input sequence, original package outputs and SDK were unchanged/hash-checked.
This is not full package or popup UX/UI qualification.
