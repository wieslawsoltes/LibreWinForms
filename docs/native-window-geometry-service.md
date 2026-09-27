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
These cases test negative handle admission, not live service-window success or
owner-thread/lifetime qualification; those require an actual registered window. The delegated ProGPU
controller has separate 42-case policy evidence and a narrow live Cocoa ARM64
initial/move/resize/closing/disposal diagnostic. That evidence does not qualify
this new Forms composition, Intel Cocoa, native popup UI, pixels or packages.

### Scoped source validation

On macOS ARM64, exact Forms `19cc2cc3d` and ProGPU
`7bfef896c57d9f338e53db48f381a55d76edce47` were built using the actual backend test
project and its complete thirteen-project managed reference graph. The graph
contains no CMake/native producer or CAD submodule edge. SDK
`11.0.100-preview.5.26302.115` built the graph in 16.61 seconds with **zero warnings
and errors**, using task-owned external outputs/cache and an existing NuGet cache
as read-only fallback. No old engine output was substituted or overwritten.

Execution on the existing .NET 10.0.5 ARM64 host passed:

- Strict geometry gate: **10/10**, zero failures/skips, 132 ms.
- Unfiltered backend assembly: **123 passed, zero failed, eight existing Linux /
  Wayland skips**, 131 total, 3.423 seconds.

The actual build used `LibreWinFormsProGpuSourceRoot` set to the geometry checkout,
`NetCurrent=net10.0`, `MicrosoftNETCoreAppRefPackageVersion=`,
`LibreWinFormsUseProGpuSystemDrawing=true`, `LibreWinFormsReferenceMode=Project`,
`UseArtifactsOutput=true` and an isolated `ArtifactsPath`, with `-m:1`,
`-nodeReuse:false` and `UseSharedCompilation=false`. Evaluated output paths, build
binlog/log, exact commands and test logs remain under the task's
`artifacts/native-geometry/` directory. ProGPU outputs use its isolated
`build/bin/<project>/release` paths; Forms outputs use this worktree's own
`artifacts/bin/<project>/Release` paths.

Two setup-only failures remain recorded: invoking the isolated SDK11 executable
from the ProGPU directory could not satisfy that repository's SDK10 global.json;
invoking the net10 test DLL with the SDK11-only runtime host found no net10
runtime. The real graph was built from the Forms entry project with SDK11 and
executed using the already installed matching .NET10 host, without changing
global.json, runtime policy, dependencies or test assertions.

No GUI, VM, native producer or canonical Forms/Design source build ran in this
scope. The original canonical source suite and all package/native gates remain
required; source compilation does not qualify the new Forms native observer.
