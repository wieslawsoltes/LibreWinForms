# Portable ComboBox observation and popup integration

A native macOS ARM64 run of the unchanged popup interaction application exposed
an unconditional `USER32.dll` call in `ComboBox.DroppedDown`. The installed
canonical PR74 package had created a real Form, but the first passive observer
tick aborted before producing a snapshot. No input, capture or AX inventory ran.

The retained evidence is
`/Volumes/1TB-macOS/librewinforms-popup-macos-geometry.vuh4WaJ3`: original producer
Build 36276743597, artifact 10916489916, all fourteen original package hashes,
eighteen byte-matched runtime assemblies, source SHA256
`4868f4d246ecebad04a8b04aeb919facd3ae8f72fe280f8bdadc90294a42dc0a`, and the original
stderr/exit receipt. The independent consumer compiled with zero warnings/errors;
the native process exited with signal 6 before the AX worker was started.

Nine new source cases cover all three ComboBox styles before handle creation,
after creating a closed control, and during real source owner-focus transitions.
Against unchanged runtime head `699b9c813`, the full suite passed all original
405 cases and failed precisely these nine new cases, with zero skips. They expose
both the dropdown-state `SendMessage` and child-HWND `GetFocus` escapes. Original
results remain in `artifacts/dropdown-keyboard/log/combobox-state-baseline-*`.

Portable focus must use the canonical source focus owner: there are no native
ComboBox edit/list HWNDs. Dropdown visibility must come from an actual owned
popup surface, not the native-notification `_dropDown` bookkeeping field. A
boolean-only implementation or synthetic open/close events cannot qualify it.
Actual popup/list rendering, keyboard and pointer selection, dismissal, teardown
and the independent native fourteen-phase scenarios remain required.
