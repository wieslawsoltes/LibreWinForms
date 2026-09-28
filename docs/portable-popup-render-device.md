# Popup render-device ownership

Canonical dropdowns can create hidden native handles before binding their source
Form owner. Creating a separate WebGPU device at handle creation also creates a
separate pipeline-cache domain. In the Windows popup reproduction, the ordinary
path compute pipeline took 27.074 seconds to create for the owner, then began
compiling again for its context menu before the original 60-second scenario
deadline expired. These are synchronous pipeline API timings, not GPU completion
times or a statistical benchmark.

`SilkLibreWindow` now defers only a popup's rendering context and compositor.
Native window, ownership admission, input and character callbacks are still
created normally. Before nonactivating display, painting or an explicit Graphics
request, the popup initializes its renderer:

- A live typed Silk owner from the same service and dispatcher supplies the
  existing `WgpuContext.InitializeSharedDevice` lifetime. Adapter, compiler,
  limits, queue and device-domain pipeline cache are inherited.
- Every window still owns its surface, compositor, scene, glyph/path atlases and
  mutable drawing state. Shared devices do not merge window content.
- An explicit Graphics request before owner binding, or an admitted external
  native owner without a Silk rendering context, retains standalone rendering.
  Later owner changes do not replace an initialized device beneath live resources.
- Lost, retired, uninitialized or wrong-service/dispatcher Silk owners fail;
  they do not trigger adapter/device fallback. Initialization publishes only a
  complete context/compositor pair and cleans both up on failure.

Ordinary windows retain eager renderer creation. Native popup ownership,
nonactivation, platform capability checks, teardown, compiler/adapter defaults
and all existing application deadlines are unchanged.

## Regression and integration checks

The existing `LibreWinForms.Sdk.SourceFirstVisibleSmoke` now checks actual
installed-package window lifetimes in its original platform matrix and watchdog:

1. Precreating root/submenu handles adds no WebGPU context.
2. Explicit pre-owner Graphics creates and retires one standalone context.
3. Root/submenu paint callbacks expose distinct current contexts and surfaces,
   sharing the owner's actual device, queue, compiler and compute limits.
4. Owner hide releases both popup contexts without releasing the owner context;
   the owner can show and repaint afterward.

On Windows ARM64/Parallels D3D12 with the default FXC selection, the expanded
smoke built without warnings/errors. Its original successful-producer package
failed the hidden-precreation assertion; a private candidate-DLL copy passed
all lifecycle assertions. Original package outputs and unchanged copied files
were hash-checked before/after. This is a diagnostic regression, not a claim
that the new full package producer has passed. The 44 existing native popup
admission, popup service and geometry-service tests also pass without skips.

The unchanged desktop interaction scenario remains an independent gate. A private
candidate-DLL Windows diagnostic reached context-menu capture without repeating
the owner's path pipeline creation, but still expired at `03-context-child` under
the original 60-second limit. Submenu hover/input and incomplete menu painting are
not qualified by device reuse. Neither this diagnostic nor the lifecycle smoke
proves complete cross-platform popup UX/UI, exact current-package qualification,
or the separate native WPF ARM64 compute-execution gate.
