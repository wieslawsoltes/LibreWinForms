# Source-native dialog completion

The actual portable `Form.ShowDialog` loop now queries the exact window's optional
`ILibreModalWindow`. ProGPU implements it over the shared native session and
existing owner-only polling/retirement. Explicit service configuration requests
admission; unsupported providers reject, owned non-key popup windows cannot
become dialogs, and ordinary factories/input/default policy remain unchanged.

Native Begin callbacks cannot run End, hide, close or destroy the retained host
inside the transition. Requests reconcile afterward. Release callbacks only wake
the creating dispatcher; source completion runs through the existing retained
host drain. Uncertain Begin/End identity cleanup retains the exact source host.

Each canonical modal loop retains its original source ThreadWindows frame.
Native completion marks that frame ready; only ready frames at the source stack
top re-enable their windows. Detachment precedes activation callbacks. The
dialog's owner/focus restoration, hide, handle destruction and owner-property
cleanup wait for that same source completion. An older callback cannot pop a
newer nested frame. Absent/disabled capability keeps the synchronous source path.
Restoration snapshots each original source handle as well as its Form object;
recreated source windows cannot inherit an obsolete enable/activation request.
Deferred dialog cleanup likewise rejects a replacement source handle.

Automatic provider selection remains mandatory final integration work after the
paired WPF path is complete. Both factories already select owned Cocoa popup
surfaces and typed native input; this checkpoint does not claim application-wide
native modality, focus parity, package admission or desktop qualification.
Qualified dependency pins are unchanged. Tests are authored only, deliberately
not compiled/executed during the implementation-only phase; no CI was dispatched.
