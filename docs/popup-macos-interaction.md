# Popup interaction on an actual macOS desktop

`eng/librewinforms-popup-macos.py` uses the existing shared `run_case`, `Session`
and fourteen-phase scenario in `librewinforms-popup-desktop.py`. Its application
is the identical checked-in `PopupInteractionApp/Program.cs`; it does not add
application actions, validation handlers, source focus calls or replacement
controls. The original 60-second scenario and application watchdog remain.

This is a portable-only run. Microsoft WinForms is independently observed on
Windows using the same Program bytes. Completed input/state phases are not
native pixel, theme, cross-platform or installed-package qualification.

## Preparation and native helper

Use the existing [successful producer/package preparation contract](popup-desktop-interaction.md),
including the complete original package manifest, private feed and owned cache.
Prepare with `--native-geometry` so that the normal Portable consumer compiles
the optional typed observer. Build that consumer through its original installed
SDK closure, without DLL overlays or package-cache edits. This driver requires
the actual native apphost at
`Portable/bin/Release/net11.0/PopupInteractionApp` and its matching DLL.
It checks both staged Program copies and the typed observer against their exact
preparation/source hashes. It opts the child into the observer only for this run
and restores the caller's environment afterward.

Compile the checked-in `eng/PopupDesktopNative.swift` into a **fresh task-owned**
output using the actual Apple SDK, for example:

```sh
task_dir=$(mktemp -d /path/to/existing/evidence/popup-native-helper.XXXXXX)
xcrun swiftc -parse-as-library -O -target "$(uname -m)-apple-macosx15.2" \
  -module-cache-path "$task_dir/module-cache" \
  -framework AppKit -framework ApplicationServices -framework CoreGraphics \
  -framework ScreenCaptureKit -framework ImageIO -framework UniformTypeIdentifiers \
  eng/PopupDesktopNative.swift -o "$task_dir/PopupDesktopNative"
```

Retain that compiler command/log, SDK identity and source/output hashes with the
run. The driver records both source and helper hashes but cannot establish their
build relationship from two files alone; its provenance explicitly says so.
It neither builds nor signs the helper, installs dependencies, changes TCC
permissions, requests authorization, starts a VM or dismisses any OS UI.

The native helper requires macOS 15.2 or later. This is this diagnostic driver's
capture boundary, not a change to the product's supported operating systems.
Permission **preflight** requires
Accessibility, CGEvent posting, physical input-state access and screen capture.
An absent permission/provider fails before launching the app and leaves a fresh
`driver-failure.json` with `appNotLaunched: true`. Authorize only through the
user's normal trusted workflow before a separately approved desktop run.

```sh
python3 eng/librewinforms-popup-macos.py \
  --prepared-root /path/to/task/prepared \
  --portable-app /path/to/task/prepared/Portable/bin/Release/net11.0/PopupInteractionApp \
  --native-helper /path/to/task/PopupDesktopNative \
  --evidence-parent /path/to/existing/evidence
```

## Independent native geometry and input

Each native inventory is read from current `CGWindowListCopyWindowInfo`, scoped
to the live owned PID, with exact window number, title and raw native frame.
The paired source/typed-native snapshot must have the same PID and sequence.
The existing [strict Cocoa geometry reader](native-window-geometry-service.md)
requires the typed controller's exact Cocoa window number and frame to match
that independent inventory, and the actual framebuffer scale to equal the
returned backing scale. Client and frame remain distinct.

Only the declared `ILibreWindow` policy maps native points to source coordinates:
`Logical` uses `FramebufferScale / DpiScale`; `DevicePixels` uses
`FramebufferScale`, with the original source rounding policy. After that exact
comparison succeeds, pointer input inverts the declared factor. It never fits
a multiplier to observed rectangles, guesses decoration sizes or changes the
application's DPI policy. An ambiguous source-to-native target fails.

The main window and four public ToolStrip dropdowns have typed client evidence.
Private ComboBox/ToolTip surfaces retain current PID/window-number/frame records
with `client: null`; no client is fabricated from a native frame. Their existing
source events and additional native-window predicates remain unchanged. This
does not claim exact private-surface client placement or source/native pairing.

Initial activation is a normal `NSRunningApplication.activate` request for the
owned, PID/title/geometry-verified main window. Every input checks the actual
frontmost PID and physical modifiers/requested key/buttons. The documented
`NSEvent.pressedMouseButtons` bitmask also rejects additional held mouse buttons;
the CoreGraphics typed enum is used only for its left/right/center cases. A pointer target
must also pass **system-wide** AX hit testing before movement and before its
click; app-restricted AX hit testing would miss covering windows and is not used.
AX messaging has a 250 ms bound. `AXError.cannotComplete` (including the earlier
observed -25204 failure) is a hard no-input result, not permission to fall back
to source rectangles or presumed z-order.

The shared semantic F10, Down, Escape, Alt and Return keys map to Apple's
documented physical key codes; bare Alt uses the left Option key. No text is
guessed from key names. Native CGEvent down/up objects are both created before
either is posted, with no throwing work between the pair. The driver never
releases a preexisting user-held modifier. CGEvent posting has no delivery
result: only the unchanged subsequent application-state assertions can prove
the expected input effect. Desktop/system shortcuts are not disabled or bypassed.

Each helper subprocess is bounded by three seconds (capture five) and the
remaining original scenario deadline. Original app-owned termination and failure
retention stay in shared `run_case`. Native requests, replies and errors are
retained separately, limited to 1,024 operations and 8 MiB. No other process is
terminated. Native inventories admit at most 4,096 system records/32 owned
windows, and typed JSON retains its original size/schema/identity checks.

## ScreenCaptureKit evidence

The helper passes the union of the independently observed owned **native frames**
directly to `SCScreenshotManager.captureImage(in:)`. Apple declares that rectangle
in global screen points; the API chooses the returned image resolution. There is
no output-size override, fitted DPI multiplier, drawing-context resize, stitched
window capture, obsolete CGWindow image API, or fallback screenshot provider.
The actual returned CGImage width, height, row stride, component/pixel bits,
bitmap/alpha information, color-space name and pixels-per-requested-point ratios
are retained. Those image ratios describe capture output, never input mapping.

ImageIO encodes the returned image as BMP without resizing. The driver checks
the actual BMP dimensions/length against the native receipt; this is not a claim
that encoded bytes are identical to the CGImage's internal storage. Capture
checks foreground and exact owned native window identities/frames before and
after the async API. Shared Session additionally rechecks the whole source and
native state after capture. Actual images remain bounded to 4,096 pixels/edge,
16 MiB/image and the existing 128 MiB aggregate limit. Unsupported formats,
oversized images or encoding failure remain failures.

The crop retains native chrome geometry, but `usableWindowPixelsVerified` and
`qualified` remain false. Inspect every retained image and compare against the
unchanged Microsoft reference before claiming desktop parity. A crop may contain
background between owned windows; use a dedicated desktop without private
content. Cross-monitor capture behavior, AX availability, Option/F10 desktop
policy, private popup geometry and native theme/selection/caret remain actual
qualification questions, not conclusions from these offline controls.

## Evidence and primary contracts

Twenty offline controls pass without loading AppKit/ScreenCaptureKit, launching
an app/helper, accessing a desktop or sending input. They cover typed identity
and geometry rejection, explicit policy mapping, private-client absence,
input/request boundaries, deadline/error retention, actual capture metadata,
opt-in restoration and unchanged shared scenario delegation. All 17 existing
preparation/desktop, 22 X11 and 25 native-geometry controls also passed, with
ResourceWarning treated as an error; actionlint and diff checks passed.

The actual helper compiled with zero warnings/errors using Apple Swift 6.3.1
and SDK 26.4, first at the host default deployment target, then explicitly for
`arm64-apple-macosx15.2`. The latter Mach-O declares minimum macOS 15.2. It has
**not been executed**, including preflight, permission checks, window queries or
input. Source SHA-256 is
`620ecbff9a7b1944166cdab721be72b1b7d426a09e156ad7e342988b41f28e55`;
the explicit-target binary SHA-256 is
`2d075ee00c78993c20e99fcbbd5281e16fd8bdb9bbf1274678075628d5574696`.
Original compiler logs, binaries and metadata remain in the task-owned
`librewinforms-popup-macos-compile.4LRC33mG` evidence directory. The subprocess
reply allocation trusts this reviewed helper, whose source caps its own reply
at 64 KiB; substituting an arbitrary executable is not a sandboxed operation.
Retain the exact compiled helper identity for every actual run.

Final source review caught that raw button indices 3/4 are not members of Swift's
three-case `CGMouseButton`; a failable conversion could reject every input.
The helper now uses the public complete button-state bitmask rather than casting
unknown native enum values. Its final exact source compiled again with zero
warnings/errors for macOS 15.2; prior binaries/logs remain historical only.
After integrating the reviewed observer package gate, all 13 additional package
verifier controls also passed. Shared application/scenario bytes remain unchanged.

The successful complete package producer, real desktop
fourteen-phase execution, capture inspection and paired reference comparison
remain required. No native Windows/Linux/macOS gate is replaced.

Primary API contracts were checked against the installed Apple SDK declarations
(`SCScreenshotManager.h`, `CGWindow.h`, `CGEvent*.h`, `AXUIElement.h`, Carbon
`Events.h`) and Apple's official documentation:

- [ScreenCaptureKit rectangle capture](https://developer.apple.com/documentation/screencapturekit/scscreenshotmanager/captureimage(in:completionhandler:)) specifies native screen points and returns the actual CGImage.
- [CGImageDestinationCreateWithData](https://developer.apple.com/documentation/imageio/cgimagedestinationcreatewithdata(_:_:_:_:)) supplies the native ImageIO encoding boundary.
- [NSRunningApplication activation](https://developer.apple.com/documentation/appkit/nsrunningapplication/activate(options:)) supplies ordinary owned-application activation, not a source focus override.
