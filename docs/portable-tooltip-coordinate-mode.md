# Tooltip source/native coordinate ownership

An actual installed Windows ARM64 consumer at 192 DPI reported the same main
client rectangle from Forms and native Win32: `(1236,710,1120,500)`. Its automatic
tooltip appeared at native `(4160,2008,292,62)`, beyond the real tooltip target
`(1844,926,440,72)`. The passive inventory used no input and did not alter the
60-second desktop gate or remove that visible tooltip from screenshot bounds.
This is a placement failure, not successful desktop qualification.

`ToolTip.ShowPortable` already obtains screen coordinates from the source owner:
explicit positions use `PointToScreen`, and hover positions use the actual source
pointer location. Under SystemAware the owner publishes device-pixel coordinates.
`ProGpuPopupSurfaceService` nevertheless forced every popup to Logical coordinates;
the existing Silk conversion consequently applied display scale again on Windows.
Cursor coordinates and source bounds must not be divided to compensate for that
mislabeled backend request.

`LibrePopupSurfaceRequest.CoordinateMode` now explicitly carries the coordinate
space of both `ScreenBounds` and the local drawing recorder. Its original six-value
constructor and positional deconstruction remain unchanged, and omitted/default
mode remains Logical for existing callers. The canonical tooltip supplies the
actual created top-level owner's coordinate mode, not a mode inferred from DPI.
The service rejects unknown modes before native mutation, uses the requested mode
for new windows, and recreates a surface if its coordinate mode changes rather
than silently reinterpreting a live generation. Same-mode updates retain the
existing surface and its original bounds/drawing space.

The shared `LibreWindowCoordinates` conversion still owns native desktop units.
Windows device pixels, Retina Cocoa desktop points and backing scale remain
distinct; negative screen origins are not normalized or scaled independently.
Popup nonactivation, transparency, dismissal, native admission and actual drawing
paths are unchanged. This does not add tooltip theme, accessibility or DPI-transition
parity, and does not qualify a native desktop from source tests.

Eight new backend rows cover both coordinate modes at Windows/Cocoa scales,
negative origins, original constructor/deconstruction, atomic unknown-mode rejection,
and mode-change recreation followed by same-mode update. Four fresh-process source
cases exercise explicit and real typed pointer/timer tooltip paths at 192 DPI,
negative origins, Cocoa native units and explicit unaware mode. They preserve exact
source screen coordinates and existing cursor input, and inspect the actual typed
request. The source gate retains its entire suites, raises minima by eight/four,
and adds strict ten-case popup service and four-case source selections.

These new cases are authored but not yet executed. Exact-head compilation, original
whole CI and actual native desktop reruns remain required. Failed native inventory
is retained at
`/Volumes/1TB-macOS/librewinforms-popup-windows-current.Jb9uulU4/native-inventory/receipt.json`.
