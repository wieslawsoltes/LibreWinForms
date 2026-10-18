# Portable scrollbar held input

Portable ScrollBar arrows and page regions repeat through the typed dispatcher
timer service while the original Control press remains current. Each one-shot
registration owns a distinct identity. Release, capture loss, hiding, reparenting,
geometry changes, source-value/range changes and handle disposal retire its lease;
an already queued callback cannot act on a replacement press. Public Scroll
handlers still own NewValue, and nested input invalidates the outer continuation.
Thumb dragging retains its separate pointer policy and starts no timer.

Every tick hit-tests the current source geometry at the last owned pointer.
Leaving the pressed part pauses value changes; reentering after repeat began
performs the next step and resumes the short cadence. Paging stops changing Value
when the moving thumb reaches the pointer. The release emits the existing single
EndScroll. Timers do not manufacture mouse notifications or native Windows input.

The portable scheduling choice is 400 milliseconds initially and 60 milliseconds
thereafter. Original Windows observations show an approximately 400-millisecond
initial wait and mostly 62–64-millisecond subsequent deliveries, with scheduling
variation. Those observations do not prove an exact native requested interval or
its relationship to user settings. These portable constants are an explicit
implementation policy, not exact timing parity or a new qualification claim.

## Independent original observation

The Microsoft application at diagnostic source
`9503d44ce2702cf707dabe75c76a13aa37dfdd7a` completed all seven original holds in
[run 39066113148](https://github.com/wieslawsoltes/LibreWinForms/actions/runs/39066113148).
Artifact `12113681136` ZIP SHA-256:
`ab7bb688ae5a890e4c3588f039624a3c7d010439010349374bdf0ff7affa17d6`.

Independent review verified all ten retained source files against that revision
(CRLF normalization only), unchanged five-file application payload, both bitmap
hashes/dimensions, all 162 ordered source events and all seven original typed
ScrollBar identities. The four ordinary arrow holds produced 14 changes each;
leave/reenter produced 15; each page hold produced one change before the thumb
reached the pointer. Each sequence retained Scroll before ValueChanged and one
final EndScroll. The original application closed normally.

The controller recorded 23 pointer actions and 27 total attempts, with seven
accepted presses/releases, all pointer input inside the original 15-second
interval, process completion in 19.672 seconds under the original 60-second bound,
and no cleanup errors. Startup used one verified Search Escape and one owned
form activation; no caption click or additional startup input was added.

The initial bitmap has a Start overlay over part of the application. It is
retained as observed and does not qualify unobstructed appearance. The final
bitmap shows the seven bars. Input guards and the source events provide bounded
behavior evidence; they do not turn that initial bitmap into a pixel reference.
Code Review cannot return workflow CI status for this separate diagnostic revision,
so the artifact is application evidence, not whole-CI qualification.

Independent native/portable package gestures, active theme pixels, exact timer
policy and the original grid application acceptance remain required. Local VMs
were not used for this observation or the source tests.

## Source validation

The new 21 dispatcher/timer controls and all 1,313 canonical lifecycle tests pass
with zero failures or skips. They cover both orientations, horizontal RTL,
leave/reentry, page stopping, release, stale queued callbacks, source/range/size
changes, capture release/transfer, hide/reparent/dispose, nested replacement
presses, callback overrides/errors and the separate thumb path. An initial run
caught the missing immediate timer retirement in the public Capture setter;
the internal ownership hook now retires the previous control without inventing
a public MouseCaptureChanged notification. The native Windows branch is unchanged.
