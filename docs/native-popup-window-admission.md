# Native popup admission in the ProGPU window backend

`LibreWindowOptions.Popup` selects a dedicated ownership/display path in
`SilkLibreWindow`. It still uses the existing Silk window, retained paint tree,
ProGPU context/compositor, dispatcher and typed input/paint events. It does not
construct a `Form`, use a bitmap overlay, replace the renderer, or synthesize
managed menu input.

## Source-owned Cocoa factory

On macOS only, the actual Popup source path now calls
`NativePopupWindow.CreateCocoaPopupWindow` with the creating
`ProGpuDispatcher.Wake` callback. Ordinary macOS windows and all other platforms
keep the original Silk factory. A failed owned factory never falls back to GLFW.
The source projects only its native-host requirements: hidden creation, an empty
native title, NoAPI with context control disabled, normal state and no border.
Desktop position/size, topmost intent, transparency and scheduling options remain
unchanged. Source title metadata is retained without titling the native panel.
Ownerless hidden precreation does not manufacture a managed parent; the existing
typed preparation/show path binds and verifies the actual native owner.

Topmost level, opacity, content constraints, geometry, input permission and visible
nonactivating ordering must succeed on the owned provider. ProGPU owns exact native
option readback and actual geometry publication. Rejection, a thrown provider error
or reentrant close retires the same source window and cannot publish successful
source state. Failed cleanup stays attached to the original failure, with pending
native/render owners retained by the dispatcher. Ordinary optional window chrome
capabilities keep their existing behavior; unsupported button/taskbar decoration
is not mistaken for failed owned opacity or content-size admission.

The source owner remains the native poller. The popup participant only invokes the
existing provider DoEvents/DoUpdate drain, and its wake callback schedules source
work without another global poll. NativeWindowInput still supplies the one typed
pointer context (and no fake keyboard), cancellation and original event metadata.
The merged source [scroll contract](native-scroll-source-input.md) and menu/list
consumers retain point/line quantities and source gesture ownership. Unsupported
custom dropdown/list scroll policies remain explicit errors, not fabricated wheel
notches. Renderer initialization still precedes Show and borrows the actual owner's
device; source retirement still waits for the presentation surface/view lease.

This factory/option connection is authored against ProGPU
`f22b5b3f3416e16421ab6b2952b2eef1864d781e` (owned panel option support).
The checked-in dependency now pins that exact pending-qualification source;
the exact complete producer/consumer Builds remain required before publishing
this integration. The managed factory/option tests and source-wiring guard are
contract evidence, not native/UI evidence.
No native runtime was rebuilt or staged. Automatic modality, real AppKit popup
input/presentation and complete package/application qualification remain separate.

The isolated backend/test Release build against that exact pin passed with zero
errors and two existing IDE0017 warnings in ProGpuDragDropLifetimeTests. The first
build identified one new blank-line style error, corrected without suppressing
the analyzer. A single 30-second/no-skips focused run passed 131 cases: the new
factory/required-option contracts plus all existing NativePopupAdmission,
NativeWindowRetirementQueue and NativePointerInput cases. No source-wide build,
native-window creation, GPU run, VM, native rebuild or package staging was used.

After the committed cleanup-diagnostic preservation fix, only the cached backend
and test projects were rebuilt with project-reference builds disabled. Backend
compilation had zero warnings/errors; the test compilation retained the same two
unrelated IDE0017 warnings. The same four focused classes passed 132 cases with
zero failures/skips under the original 30-second bound, including an exception
whose virtual Data getter throws. Original native failures remain primary even
when cleanup diagnostics cannot be attached. The receipt is
`artifacts/cocoa-source-post-commit/cocoa-source-post-commit.trx`. This does not
qualify real AppKit, legacy scroll compatibility, automatic modality or packages.

Hidden ownerless creation is staging only. Before display, a popup requires a
live typed owner. Hidden owner changes call the pinned ProGPU
`NativePopupWindow.TryPrepareOwner` with the actual `IWindow` before publishing
the logical `Owner`.
The same-owner setter is a no-op. Changing an already visible owner is rejected
before mutation; callers must hide, set the owner, then show again. Reopening
re-resolves the live owner and uses `NativePopupWindow.TryShowOwned` on every
show, including Cocoa reattachment after hiding. Native rejection destroys the
surface, releases its handle and notifies `Closed`; cleanup exceptions cannot
replace the original admission/callback exception.
Logical teardown and `Closed` still complete after cleanup errors. The
[source dispatcher retirement queue](native-window-retirement.md) retains native
and renderer owners for retry: failed rendering cleanup must never destroy a
still-owned native surface. The first cleanup exception remains observable.

The display callback prepares the renderer, then uses ProGPU's shared
`ShowWithoutActivation` with the actual `IWindow`. GLFW providers temporarily
disable focus-on-show and restore the prior setting while the same initialized
native identity remains. Owned Cocoa providers use checked panel visibility,
never a GLFW call on an opaque panel handle. Typed Show admission retains the
panel across the source callback and rejects owner replacement, nested admission
and disposed/hidden success. There is no activating fallback. Explicit `Activate` is
rejected for a popup. Win32 front ordering reuses the existing same-rank
`SetTopMost` operation, whose native flags preserve nonactivation; its ordinary
`SetZOrder` operation lacks that flag. Popup ordering to the back is therefore
explicitly unsupported on Win32. Ordinary Form ownership, activation and
ordering paths are unchanged.

Input-transparent source windows call `NativeWindowInput.SetInputTransparent`
with the actual `IWindow`. The shared provider confirms GLFW pass-through using
`Native.Glfw`, or preserves independent transparency/enabled intent for an owned
Cocoa panel. An opaque panel handle is never cast to GLFW. Native rejection follows
the existing constructor cleanup path. The owned factory retains this provider
path without changing character/precision-scroll policy; desktop click routing remains a separate
application check.

## Platform boundaries

- Win32 admission uses the original same-thread top-level owner checks, hidden
  popup styles, tool-window/no-activate extended styles and mouse-activation
  subclass. The caller does not reinterpret an arbitrary handle as ownership.
- X11 admission uses the original same-display/root, hidden transient-owner,
  menu-type and override-redirect checks. A Silk owner must belong to the
  current dispatcher. External registry X11 owners remain rejected because that
  registry does not establish display-thread/lifetime serialization.
- Cocoa uses hidden preparation followed by native child attachment at the show
  boundary. The ordinary controller's `SetParent` is not used for popup owner
  assignment because `addChildWindow` can show the window. This does not add an
  automatic AppKit modal-session admission. The typed API also supports hidden
  source-scheduled owned-panel binding, now selected by the macOS Popup factory
  above. Automatic native modality and application qualification remain separate.
- Other native window kinds remain rejected by the existing ProGPU capability.

Native popup admission does not supply canonical menu keyboard routing,
outside-click dismissal, capture, native visibility/owner teardown policy or
platform visual parity by itself. Those source connections and actual native
application qualification remain separately required under issue #197.

## Source and contract evidence

The initial integration reused the native implementation from ProGPU
`08f4343ef15328ba742cdcf11f8eb2daeefb5f7b`:
`NativePopupWindow`, `Win32PopupConfiguration`, `X11PopupConfiguration` and
`CocoaPopupConfiguration`. Existing Win32 ordering flags and Cocoa owner/show
side effects were inspected in their native platform implementations. The local
LibreWPF `SilkNetWpfWindowDecorationService` provides the prior GLFW
focus-on-show host usage; this backend retains its actual previous attribute
instead of assuming it was enabled.

The typed-provider update pins ProGPU `d7cea09ba63458213becccc801d09819c9abe1c5`
and delegates preparation, display admission and visibility directly to its
shared provider-aware APIs. Eighteen new upstream managed cases cover callback
ownership/reentrancy, retirement, native identity and thread affinity; all
existing Forms admission/source/package checks remain. Local validation is
compilation only. The exact upstream and Forms Builds must pass before merge;
the source pin does not stage artifacts from a partial or failed producer.

The official [GLFW window visibility documentation](https://www.glfw.org/docs/3.4/window_guide.html#window_hide)
and [window attribute contract](https://www.glfw.org/docs/3.4/window_guide.html#window_attribs)
were inspected: ordinary show requests focus by default, while the existing
window's focus-on-show attribute can be changed independently. These APIs require
the owning/main window thread; they are not native ownership admission.

`NativePopupAdmissionTests` exercises the actual lifecycle coordinator through
typed recorded native operations: staged creation, late/stale owners, hidden
replacement, rejected live replacement, publication ordering, reentrancy,
nonactivation routing, cleanup/Closed notification and preserved exceptions.
Its ordering cases distinguish the existing Win32 and Cocoa/X11 operations.
The adapter calls the real pinned ProGPU admission APIs; the recorded operations
are tests, not a runtime fallback. The source-first gate retains its unfiltered
backend suite, increases its minimum from 45 to 69, then runs all 24 admission
cases separately with skipped tests rejected and the existing ten-minute test
bound. No test selection is removed.

Host validation on macOS ARM64 used the repository .NET 11 preview SDK, the
`net10.0` target and the exact pinned ProGPU source closure. The backend and
tests compiled with zero warnings/errors. All 24 admission cases passed with
zero skips. The unfiltered assembly passed 75 cases with eight existing skips:
seven Linux-only XDG/Wayland cases and one explicitly enabled real-Wayland
session fixture. An initial extra `--fail-skips on` full-suite probe correctly
rejected those platform skips; the retained full-suite CI policy is unchanged.
These results do not qualify actual popup desktop display, input or nonactivation
on any platform, and the integrated source/package CI gates remain required.
The normal-popup `Dispose` routing change was rebuilt and retested at exact
commit `7c1271831f90c94ef5f8559d7b4ccd18dc0c45ac`: zero build warnings/errors,
24 focused passes with zero skips, and the same unfiltered 75 passes/eight
platform skips. The recorded coordinator tests do not inject real GPU-resource
disposal failures.
