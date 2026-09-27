# Popup interaction over an actual X11 connection

`eng/librewinforms-popup-x11.py` runs the same checked-in
`PopupInteractionApp/Program.cs` and calls the existing desktop driver's
`run_case`, `Session` and fourteen-phase `scenario`. No Linux replacement
application, input callback, validation handler, invented coordinate multiplier
or relaxed phase predicate is introduced. The single scenario deadline remains
60 seconds, as does the application's independent watchdog.

This driver runs the portable application only. Microsoft WinForms does not run
on Linux: use the independently collected Windows reference with exactly the
same source bytes. A completed Linux input/state sequence is not a paired pixel
or UI-parity result.

## Preparation

Use the existing [preparation and producer requirements](popup-desktop-interaction.md).
Retain the successful whole producer, complete original package feed and source
provenance; preparation's three direct package hashes are not a replacement for
the complete package manifest. The preparation tool can run on Linux without
building its staged Microsoft project. Build only the staged Portable project
using the pinned SDK, original private feed, a task-owned NuGet cache and the
ordinary installed SDK dependency closure. The admitted native apphost is exactly
`Portable/bin/Release/net11.0/PopupInteractionApp`, with its matching DLL beside it.
The driver checks identical Program.cs bytes and rejects other output paths or a
non-ELF apphost. No source DLL overlay or package-cache rewrite is part of this gate.

Create a private Python environment on the validation machine, not a global
installation:

```sh
python3 -m venv /path/to/fresh-task/x11-driver
/path/to/fresh-task/x11-driver/bin/python -m pip install \
  --require-hashes --only-binary=:all: -r eng/popup-x11-requirements.txt
```

The requirement file pins the original PyPI wheels for python-xlib 0.33 and six
1.17.0. Dependency versions and actual protocol/image metadata are recorded in
`x11.json`. Missing dependencies or required connection capabilities fail before
the application starts. The protocol binding uses its own connection's error
handler, not a process-global native Xlib error handler.

On an authorized, unobstructed interactive desktop, inherit the actual session's
DISPLAY/authentication environment. The driver does not discover credentials,
alter permissions, start a display server, run a VM, or dismiss another app's UI.

```sh
/path/to/fresh-task/x11-driver/bin/python eng/librewinforms-popup-x11.py \
  --prepared-root /path/to/fresh-task/prepared \
  --portable-app /path/to/fresh-task/prepared/Portable/bin/Release/net11.0/PopupInteractionApp \
  --evidence-parent /path/to/fresh-task/evidence
```

The evidence parent must already exist; every run creates a fresh child directory.

## Native admission and input

Admission requires a real X11 connection with XTEST 2.2, a live EWMH window
manager, activation requests, PID properties and independently queried client
geometry. XWayland can qualify these X11 contracts; a session environment name
neither admits nor rejects it. Native-Wayland-only windows without matching XIDs
remain unsupported, and no native Wayland coverage is claimed.

Visible windows are found through the actual root tree, including
override-redirect popups. Their `_NET_WM_PID`, translated client origin, server
width/height, border extent, transient owner and window type are recorded. An
override-redirect popup must have a live same-process transient owner. Source
client rectangles must equal these native rectangles exactly. No frame-size
subtraction, DPI inference or fallback coordinate system is used. The image crop
includes owned X window borders, not unrelated window-manager decorations.

Initial activation is an EWMH request to the window manager for the freshly
launched PID/title-matched app, never direct input-focus assignment. Every input
requires both the EWMH active-window PID and actual X11 keyboard-focus ownership.
Pointer targets are checked before motion and again at the actual server pointer
position before clicking. Requested keys, modifier keys and core pointer buttons
must not be held. Nonstandard left/middle/right button mappings are rejected,
never changed. Current server keycodes are resolved for the scenario's existing
F10, Down, Escape, Alt and Return keys; only complete XTEST down/up pairs count.
Failure cleanup releases only a down event this driver actually queued.

Window traversal is bounded to 4,096 nodes and 64 levels; property reads have a
4 KiB bound. Each protocol operation has a three-second bound within the original
scenario deadline. The original owned-process cleanup and incomplete receipts,
including rejected geometry, remain authoritative after failure.

## Capture is evidence, not automatic qualification

The driver preserves bytes from X11 root `GetImage`. It admits only the actual
declared depth-24, TrueColor, little-endian 32-bit BGRX layout with explicit RGB
masks and 32-bit scanline padding. Other layouts fail without conversion. Reply
depth/visual, complete stride/length and the shared 16 MiB image/128 MiB aggregate
budgets are checked. BMP output retains the original top-down bytes.

**Rootless XWayland may not expose compositor desktop pixels through root
GetImage.** Receipts explicitly retain `compositorCaptureVerified: false` and
`usableWindowPixelsVerified: false`, even when input/state phases complete. A
blank or otherwise unusable capture must be rejected during native qualification;
it is not permission to substitute source painting, a window pixmap, an ambient
portal, or a different screenshot provider. Capture validity and paired pixel
comparison remain separate from phase completion. Crops may contain background
between owned windows, so use a dedicated desktop without sensitive content.

## Implementation evidence and primary contracts

Offline tests exercise ownership gates, held input, exact native target transport,
partial-pair cleanup, real protocol timeout, geometry/owner records, pixel layout
and byte retention, source/output admission and reuse of all fourteen phases.
They do not import Xlib or connect to a desktop. The existing thirteen Windows
harness controls remain unchanged. Native Linux/XWayland execution, usable capture
and comparison with the matching Windows reference remain pending.

The final local offline run passed 18 new controls and all 13 existing controls,
with ResourceWarning treated as an error. A task-owned virtual environment
installed the hash-pinned original wheels and successfully serialized actual
python-xlib XTEST, ClientMessage and TranslateCoords requests without constructing
a Display or opening a connection. This verifies only the binding call shape,
not server admission or native input. No VM, desktop application, renderer build
or package producer was executed for these checks.

The implementation uses the original
[python-xlib 0.33 protocol bindings](https://github.com/python-xlib/python-xlib/tree/0.33/Xlib),
[XTEST extension](https://github.com/python-xlib/python-xlib/blob/0.33/Xlib/ext/xtest.py),
and [EWMH activation/PID contracts](https://specifications.freedesktop.org/wm-spec/latest/).
The first offline run exposed a test-only macOS `/var` versus `/private/var` path
comparison; the fixture now resolves its temporary root just as the CLI does.
No product or native assertion was relaxed.
