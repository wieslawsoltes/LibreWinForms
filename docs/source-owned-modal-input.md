# Owned popup input proof on the source route

This child of the source modal-loop stack connects the shared ProGPU session gate
to the ordinary Forms popup factory/input/display route. `OnLoad` already creates
the real owned provider through `SourceWindowFactory`, attaches `NativeWindowInput`
and subscribes the typed `NativePointerInput` adapter. It now requires the actual
window/context pair to report `NativePopupWindow.SupportsModalInput`, with the
same current source subscription. A pointer-shaped interface or native handle is
not sufficient. Missing proof uses the existing failure-preserving source native
retirement path, never ordinary GLFW input or another renderer.

Every Show repeats proof before renderer preparation and after it returns, before
native visibility. Hidden ownerless handle creation remains supported, while the
existing bind/show owner checks still forbid visible ownerless success. Source
enabled/transparency intent, scroll units/handling, focus and native-session
release completion remain independent. Ordinary non-Cocoa/non-popup creation is
unchanged; the owner alone polls events.

Depends on authored ProGPU #286, exact initial producer 95c1a0bca. The source
gitlinks remain unchanged until the final qualified producer boundary. This is an
implementation-only [skip ci] stack: controls are authored, not run; no type build,
native/UI/VM execution, runtime staging or CI dispatch was performed. Automatic
modality remains off while final shared producer/source qualification is pending.
Enabling and validating the ordinary provider-qualified dialog/popup route in
both frameworks is mandatory final work, not satisfied by this capability query.
