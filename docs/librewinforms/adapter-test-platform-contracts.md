# Adapter test platform contracts

## Failure and correction

The complete ProGPU adapter suite on macOS ARM64 exposed eight failures in the
preview.65 source graph. The default-service test still expected every non-Linux
host to use unsupported file dialogs, although macOS selects the typed AppKit
adapter. Its failed assertion bypassed service disposal and left the shared
desktop-capture registration alive, causing four subsequent registration failures.
Three other failures exercised Linux-only XDG/Wayland entry points on macOS.

The tests now assert the actual Linux/AppKit/unsupported service selection and
dispose platform services even if an assertion fails. Tests that create those
global drawing registrations share the existing serialized collection. Product
service selection, native ownership, and OS admission guards are unchanged.

Linux-only portal request and Wayland export cases explicitly report skips on
other hosts; four previously returned without asserting anything. Their original
Linux success, error, request-lifetime, and protocol-disposal assertions remain.
The platform-independent invalid-parent case is separate from the Linux request
case. A non-Linux test verifies that the portal rejects before acquiring a parent
or invoking the portal, and the exporter rejects without calling its protocol.

## Validation scope

On 2026-09-26, the macOS ARM64 adapter suite passed 50 tests with eight explicit
skips: seven Linux adapter cases and the real Wayland compositor probe. The
unsupported-host checks and the actual AppKit framework-binding case ran.
The original failing run passed all 52 platform-contract tests and both canonical
Forms builds before reaching the adapter failures.

The same managed adapter test payload also ran in the Linux ARM64 Parallels VM
on .NET 10.0.11: 55 passed and three skipped. The skips were the real Wayland
compositor probe, the real AppKit framework probe, and the non-Linux rejection
case. All Linux portal request/export cases ran. The transferred test DLL's
verified SHA-256 was
`27b761d191c7509fc923262641038adbad25b4cc57c9b62c90cdff0be23a6160`.
This is Linux runtime execution of a host-built payload, not a Linux source build.

The complete `eng/librewinforms-source-first.sh` gate then completed successfully
on macOS ARM64 against LibreWinForms `37f1f434a` with these test changes and its
pinned ProGPU `b622c4b0` source. Both canonical Forms graphs built: the ordinary
graph had zero warnings/errors; the ProGPU drawing graph had 615 warnings and
zero errors. Platform contracts passed 52/52, the rebuilt adapter suite passed
50/58 with the same eight skips, canonical lifecycle passed 135/135, and focused
System.Drawing passed 621/621. Drawing ApiCompat reported zero missing types,
zero missing members, and 13 existing shape diagnostics, with no breaking changes.
The retired Portable vector ledger passed at 26 covered, zero to migrate, and
zero to retire. Documentation verification and `git diff --check` also passed.

A real Wayland compositor, visible applications, native rendering, final package
graphs, and cross-platform application parity remain separate gates.
