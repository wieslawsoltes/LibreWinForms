# Portable system DPI and desktop coordinates

## Contract

The portable source keeps the canonical process DPI policy. The first successful
`Application.SetHighDpiMode` call wins, including an explicit `DpiUnaware` call.
Later calls return false without changing that policy. A failed primary-monitor
query does not consume admission. This follows the documented
[process setter lifetime](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setprocessdpiawarenesscontext).
The SDK initialization policy is separate; it must not overwrite a successful
explicit application choice.

SystemAware captures the primary display's content DPI once, before source
controls are constructed. Its ordinary Form/ContainerControl DPI autoscaling
uses that fixed value. Window and popup coordinates use device pixels, while
later presentation changes do not run the PMv2 source scaling path. Explicit
unaware modes retain 96-DPI logical coordinates. Existing PMv2 initial scaling,
per-window events, source ordering and size changes remain separate.

`LibreMonitor.NativeCoordinateScale` declares device pixels per native desktop
coordinate unit. It is not inferred from content DPI or a video-mode/work-area
ratio. The actual GLFW Win32/X11 provider declares pixel desktop units. GLFW Cocoa
uses its backing-coordinate measurement. Unknown providers, including undeclared
Wayland desktop positioning, leave this capability unavailable; source Screen
conversion rejects missing or invalid metadata. Existing record constructors are
unchanged, but custom monitor services using Screen must supply this declaration.

Full monitor bounds and working area are distinct. The GLFW adapter reads monitor
position and current video-mode extent for full bounds, and the work-area query
for usable bounds. GLFW documents both as
[screen coordinates](https://www.glfw.org/docs/latest/monitor_guide.html), not a
framebuffer pixel measurement. The source Screen APIs convert those declared
units through the same coordinate policy used by their windows.

## Regression coverage and limits

`CanonicalSystemDpiTests` exercises actual canonical Forms through the typed
headless platform: primary-versus-nearest selection, fixed initial DPI, canonical
autoscaling, centering and point round trips, popup coordinates, explicit unaware
behavior, failed admission and process-policy preservation. Successful DPI facts
and the existing PMv2 facts run in fresh child processes because the real policy
cannot be reset. Each child has an exact method selector, a one-test/no-skip
receipt, an unchanged inner 30-second deadline and an owned 45-second outer bound.
The full source suite retains its existing ten-minute limit.

The first 401-case run passed 400 with one failed new fixture expectation: the
fixture assigned bounds after `AutoScaleDimensions` had already triggered layout.
It is retained as failed evidence. The corrected fixture uses normal designer
SuspendLayout/ResumeLayout ordering; no product autoscale algorithm was changed
to satisfy that expectation. Separate backend tests reject pixel-scale inference
from video modes, preserve distinct full/work-area bounds, and reject malformed
declared scales.

Final local source validation passed **405/405, zero skips**, including all 390
existing cases. The source build completed with zero errors (623 warnings,
including the lock-type style suggestion). The backend compiled with zero
warnings/errors; its strict monitor selection passed **14/14, zero skips**, and
the complete backend run passed **113**, with eight existing Linux/Wayland-only
skips (121 total). An earlier full-backend command supplied the discovered total
as `--minimum-expected-tests`; that option counts successful cases, so the command
returned 9 despite no failed tests. That failed command is retained separately;
the final command expects all 113 applicable successes without changing selection.
Logs are under the task-owned `artifacts/dropdown-keyboard/log/system-dpi-*` paths.
These are source-build observations, not a successful package producer or native
desktop qualification.

The motivating actual Windows 200% capture, reproduced using both PR72 and PR74
packages, reported source client `(1516,835,560,250)` at 96 DPI versus a PID/title-
matched HWND client `(3032,1670,1120,500)`. Those are failing baseline observations,
not qualification of this change. A new exact-package Windows comparison remains
required. Source/provider tests do not qualify native input, popup UI, physical
keyboard layouts, or heterogeneous multi-monitor SystemAware virtualization.
This change does not force PMv2, change the backend's default coordinate mode,
rescale an external driver, or claim complete PerMonitor-v1 behavior.

The integrated SDK/runtime source head `9add89167` reran all 405 canonical cases
with zero failures/skips. Its 25 fresh-process initialization composition
controls also passed, including explicit DpiUnaware and PerMonitorV2 choices
before the generated SystemAware call. Current source DLLs were hash-verified
in new consumer output directories; original installed package artifacts were
not replaced. See [SDK DPI evidence](sdk-application-high-dpi-mode.md) for this
source/package distinction and the still-required full installed matrix.
