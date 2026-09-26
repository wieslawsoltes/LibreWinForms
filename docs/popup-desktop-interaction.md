# Paired popup desktop interaction evidence

`eng/PopupInteractionApp/Program.cs` is one public `System.Windows.Forms`
application, compiled unchanged for Microsoft WinForms and the installed
LibreWinForms SDK. It contains ContextMenuStrip and MenuStrip cascades, a normal
DropDownList ComboBox, a normal ToolTip, and an outside editor. It does not call
ShowDropDown/PerformClick/Focus, dispatch managed input, set ActiveControl, or
install validation/DataError policy. Its 100 ms observer records public state,
source client-to-screen geometry and events; it does not make a phase succeed.

The existing installed-package popup smoke remains unchanged. That smoke checks
window admission/source paint/owner teardown, not this independent desktop case.
The upstream WinformsControlsTest menu/combo/tooltip forms and unit/UI integration
tests lack a shared, externally driven Microsoft-versus-portable scenario.

## Prepare and compile separately

Use a **successful, non-cancelled exact-head package producer** and retain its run,
job, archive hash and original package manifest. Do not qualify a failed producer
because this fixture builds. Stage the complete original private dependency feed;
the preparation tool records the three direct package hashes but does not replace
the existing full package/source-identity verifier. Main at `94eb1b90b` contains
physical popup admission; full keyboard phases additionally require the later
dropdown keyboard/outside-pointer and initial Alt/F10 changes (PRs 71/72).

On the later, explicitly authorized Windows desktop, create a fresh task-owned
parent directory and run (substitute actual successful package versions):

```powershell
python eng/librewinforms-prepare-popup-desktop.py `
  --destination C:\Temp\popup-interaction\prepared `
  --feed C:\Temp\qualified-forms-feed `
  --sdk-version 0.1.0-source-first-sdk `
  --canonical-version 0.1.0-source-first `
  --backend-version 0.1.0-source-first-backend `
  --dotnet-sdk 11.0.100-preview.5.26302.115
```

Preparation creates independent Microsoft/Portable projects outside the repository
build graph, byte-compares both Program.cs copies with the checked-in source, and
pins the compiler without roll-forward. It never builds or starts an application.
Run the following from the prepared directory, retaining both complete logs and
the original clean source commit. Use task-owned separate NuGet caches:

```powershell
$env:NUGET_PACKAGES = "$PWD\microsoft-packages"
dotnet build Microsoft/PopupInteractionApp.csproj -c Release --configfile NuGet.config
if ($LASTEXITCODE -ne 0) { throw 'Microsoft reference build failed' }
$env:NUGET_PACKAGES = "$PWD\portable-packages"
dotnet build Portable/PopupInteractionApp.csproj -c Release --configfile NuGet.config
if ($LASTEXITCODE -ne 0) { throw 'Installed portable build failed' }
```

No product backend/adapter override, native DLL replacement, package cache rewrite
or renderer fallback is part of this harness. Preserve the environment and loaded
native-module evidence separately from these file hashes. Restore any shell-local
cache variable after the task.

## Real input and screenshots

Use a dedicated, unlocked, unobstructed interactive desktop with no authentication
prompt or held keys/buttons. The root agent owns VM lifecycle and the one-VM rule;
this script never starts/reconfigures a VM or dismisses another application's UI.
Create the evidence parent first, then invoke from the repository:

```powershell
python eng/librewinforms-popup-desktop.py `
  --prepared-root C:\Temp\popup-interaction\prepared `
  --reference-app C:\Temp\popup-interaction\prepared\Microsoft\bin\Release\net10.0-windows\PopupInteractionApp.exe `
  --portable-app C:\Temp\popup-interaction\prepared\Portable\bin\Release\net10.0\PopupInteractionApp.exe `
  --evidence-parent C:\Temp\popup-interaction\evidence
```

Each process has one 60-second scenario deadline and an independent 60-second
application watchdog. Screenshot storage is bounded at 16 MiB of pixels per
image and 128 MiB per process. Only the freshly launched PID's windows receive input.
Source coordinates must agree exactly with the real main/popup client geometry;
WindowFromPoint and foreground PID are checked before native pointer input.
SendInput submits real down/up pairs, never direct source callbacks. A blocked
foreground, covered target, failed native call, changed geometry, missing popup,
wrong event or expired deadline fails and retains earlier evidence. No retry with
invented coordinates, OS input to unrelated windows or alternate renderer exists.

Fourteen equivalent raw phases cover baseline, context root/cascade/command,
outside-pointer dismissal, menu root/cascade/command, F10 selection/Down opening,
Alt selection, ComboBox opening/committed selection, and tooltip appearance.
Each phase requires two increasing immutable observer snapshots with unchanged
non-paint state, plus native geometry validation; both are rechecked around the
screenshot. The driver records actual desktop BMP
pixels cropped to the union of the process's visible native windows, source/native
rectangles, public events and completed input pairs. It preserves the executable
and managed entry assembly SHA-256, source/package preparation receipt, stdout,
stderr and final incomplete/completed-phase status. Normal final process stopping
is runner-owned cleanup, **not** a close/teardown qualification.

The screenshot crop can contain desktop background between owned windows: use a
clean dedicated desktop, not sensitive/unrelated work. A screenshot's existence,
source Paint event, or completed-phase exit is **not pixel/rendering parity**.
Inspect paired pixels, event order/reasons, focus, clipping and native-window
ownership independently before any acceptance claim. Linux/macOS external input,
multi-monitor/edge/DPI variants, cancellation/persistent menus and host OS tooltip
fidelity remain separate cases; this Windows driver must not fabricate them.

## Implementation validation and sources

At introduction the application and driver are **not compiled or run on a native
desktop**. `eng/tests/test_popup_desktop_harness.py` is offline preparation/safety
coverage only, never renderer or Windows input qualification. Its six cases pass
locally and run before the unchanged canonical source lane in normal CI. The first
offline run exposed a missing `returncode` field in the inert process fixture;
that fixture was corrected, without changing the PID-rejection assertion.

The Win32 driver follows Microsoft's original contracts for
[SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput),
[GetWindowThreadProcessId](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid)
and [GetDIBits](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-getdibits)
(inspected 2026-09-27). It checks complete input insertion, uses pointer-sized
native identities, and deselects its capture bitmap before reading actual pixels.
