# Existing-window native geometry queries

`SilkWindowService.TryGetNativeGeometrySnapshot(LibreHandle, out
ProGPU.Backend.NativeWindowGeometrySnapshot)` exposes the original ProGPU
controller's read-only native geometry for a window this service already owns.
It does not create or enumerate windows, attach a controller, apply desired
state, show, activate, focus, move, resize, pump or dispatch work to another UI
thread. This is an optional backend diagnostic, not a replacement for canonical
source `Bounds`, `Location`, `PointToScreen` or DPI policy.

The handle must have `LibreHandleKind.Window`, resolve to an actual
`SilkLibreWindow` in this registry, and belong to this service's registered window
set. The existing window's own dispatcher must admit the current thread; creation,
closed and disposed states reject the query. The query rechecks registry/object
and service membership after the controller returns. No external-owner registry,
opaque-pointer cast, logical-child geometry or fallback provider is used.
Unavailable/retired/foreign handles return `false` with a default output.

The snapshot is returned unchanged: actual native content/frame rectangles are
global top-left **desktop points**, not Forms source coordinates or framebuffer
pixels. `BackingScale` is separate and never multiplies desktop origins. Exact
native window and content-view identities are borrowed scalar observations, not
ownership or dereference permission. `CocoaWindowNumber` remains AppKit's actual
window-device number; independent CG PID/number/frame membership must establish
any observed external correlation. The service adds no AppKit interop, reflection,
AX-derived geometry, titlebar subtraction or inferred client rectangle.

A passive consumer checks `Control.IsHandleCreated` before reading the existing
top-level `Form` or `ToolStripDropDown.Handle`, then supplies that value as an
existing `LibreHandleKind.Window` token to the exact configured `SilkWindowService`.
The service remains the admission authority; source handles are never native
NSWindow pointers. Logical child controls and internal surfaces without a public
held source handle cannot be invented or matched by this API.

## Dependency and validation

This source requires the additive ProGPU geometry API from PR198, whose proposed
head is `7bfef896c57d9f338e53db48f381a55d76edce47`. The Forms submodule pin is
intentionally unchanged until that whole upstream Build and separate checks are
green and merged. The current pin alone cannot compile the new API call. A local
source-composition build can explicitly use
`LibreWinFormsProGpuSourceRoot=/Volumes/1TB-macOS/progpu-window-geometry.nsgUYGQa/`;
that is not installed-package qualification or permission to change the pin early.

Ten authored headless admission cases cover null/missing, non-window, unrelated,
released, foreign-registry and externally registered handles, with no native
creation. The unchanged unfiltered backend gate is retained (minimum 97 to 107),
with an additional strict ten-case, zero-skip gate over the same built assembly.
These cases are not yet compiled or executed. They do not claim live service-window success or owner-thread/lifetime
qualification; those require an actual registered window. The delegated ProGPU
controller has separate 42-case policy evidence and a narrow live Cocoa ARM64
initial/move/resize/closing/disposal diagnostic. That evidence does not qualify
this new Forms composition, Intel Cocoa, native popup UI, pixels or packages.

No GUI, VM or heavy/full source build was performed while authoring this seam.
The original source/backend suites and all package/native gates remain required.
