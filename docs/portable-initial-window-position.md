# Initial portable window position

The canonical `Form` constructor and `Bounds` retain their source values until
native creation. `Form.FillInCreateParamsStartPosition` writes `CenterScreen` and
default-placement coordinates into `CreateParams`, not into source `Location`.
The portable provider creates a window using those parameters, but the Silk
provider suppresses bounds callbacks during its constructor. Previously there
was no final initial-position publication: `Control.PointToScreen` consequently
summed a stale top-level source origin even after the window had been positioned.

Portable `Control.CreateHandle` now reads the returned typed `ILibreWindow.Bounds`
position before raising `HandleCreated` and publishes it through canonical
`UpdateBounds`. This applies to actual platform-backed windows, including native
dropdowns, not logical child handles. No frame-decoration offset, screen-coordinate
rescaling, OS-name inference or driver correction is involved. Existing later
`BoundsChanged` events and explicit source `SetBounds` keep their original route.

Only initial X/Y is synchronized. Source width and client size remain owned by
canonical layout and autoscaling: PerMonitorV2 deliberately pre-sizes the native
surface before the source completes its own initial DPI scaling. Publishing that
pre-sized native width into source layout would apply the scale twice. This change
does not claim to complete the independent outer-frame/client-size contract.

The native object and logical handle are captured together and rechecked after
the provider read and `Move`/`LocationChanged` callbacks. Disposal or replacement
must not let the old source creation raise `HandleCreated` for a new generation.
Callback exceptions remain visible to the caller. A dropdown's later
`OnVisibleChanged` may explicitly reapply its source `_displayLocation`; the
existing canonical placement behavior is retained, not replaced by the initial
provider snapshot.

## Evidence and remaining qualification

The retained macOS source-composition attempt used original shared popup scenario
source and actual PR78 source DLLs. Its first two source snapshots reported the
Form client origin `(0,0)` with size `560x250`; an independent PID/title-matched
CoreGraphics window inventory reported a frame at `(620,409)` with size `560x278`.
That frame is **not** a client-origin oracle. AX failed with `-25204`; no desktop
input or screenshot capture ran. The attempt is not package, client-geometry or
UI qualification. Evidence remains under
`librewinforms-popup-source-macos.KGS978` on the external validation drive.

Thirteen new canonical source cases cover initial manual/center/default placement,
provider-adjusted position without a move callback, nested conversions, later and
retired-generation moves, callback reentry/disposal/failure, native popup reuse,
SystemAware Windows/Cocoa-declared coordinate units and unchanged PerMonitorV2
source scaling. The existing full source minimum grows from 479 to 492 without
changing selectors, skip rejection or deadlines. These cases are authored but
not compiled or executed yet; no new source-suite or native qualification is
claimed. Full source/package CI and the unchanged native paired driver remain
required. Native client geometry must come from an admitted provider contract,
never from subtracting an assumed titlebar from CoreGraphics frame bounds.
