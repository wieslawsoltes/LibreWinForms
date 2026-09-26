# Native popup admission in the ProGPU window backend

`LibreWindowOptions.Popup` selects a dedicated ownership/display path in
`SilkLibreWindow`. It still uses the existing Silk window, retained paint tree,
ProGPU context/compositor, dispatcher and typed input/paint events. It does not
construct a `Form`, use a bitmap overlay, replace the renderer, or synthesize
managed menu input.

Hidden ownerless creation is staging only. Before display, a popup requires a
live typed owner. Hidden owner changes call the pinned ProGPU
`NativePopupWindow.TryPrepareOwner` before publishing the logical `Owner`.
The same-owner setter is a no-op. Changing an already visible owner is rejected
before mutation; callers must hide, set the owner, then show again. Reopening
re-resolves the live owner and uses `NativePopupWindow.TryShowOwned` on every
show, including Cocoa reattachment after hiding. Native rejection destroys the
surface, releases its handle and notifies `Closed`; cleanup exceptions cannot
replace the original admission/callback exception.

The display callback temporarily disables GLFW focus-on-show and restores its
actual prior setting. There is no activating fallback. Explicit `Activate` is
rejected for a popup. Win32 front ordering reuses the existing same-rank
`SetTopMost` operation, whose native flags preserve nonactivation; its ordinary
`SetZOrder` operation lacks that flag. Popup ordering to the back is therefore
explicitly unsupported on Win32. Ordinary Form ownership, activation and
ordering paths are unchanged.

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
  NSPanel contract or AppKit modal-session admission.
- Other native window kinds remain rejected by the existing ProGPU capability.

Native popup admission does not supply canonical menu keyboard routing,
outside-click dismissal, capture, native visibility/owner teardown policy or
platform visual parity by itself. Those source connections and actual native
application qualification remain separately required under issue #197.

## Source and contract evidence

The native implementation is reused unchanged from ProGPU
`08f4343ef15328ba742cdcf11f8eb2daeefb5f7b`:
`NativePopupWindow`, `Win32PopupConfiguration`, `X11PopupConfiguration` and
`CocoaPopupConfiguration`. Existing Win32 ordering flags and Cocoa owner/show
side effects were inspected in their native platform implementations. The local
LibreWPF `SilkNetWpfWindowDecorationService` provides the prior GLFW
focus-on-show host usage; this backend retains its actual previous attribute
instead of assuming it was enabled.

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
