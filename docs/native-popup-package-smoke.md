# Installed-package native popup smoke

The existing visible installed-SDK package program now exercises a real canonical
`ContextMenuStrip` and a nested `ToolStripDropDown` through the registered
ProGPU/Silk window service. Both are persistent (`AutoClose = false`) so owner
hide must retire their native surfaces independently of ordinary menu dismissal.

The program requires separate window-kind handles resolving to visible
`ILibreWindow` instances, the actual Form owner identity, hidden window borders,
no taskbar entry, positive platform extents, source paint callbacks on both
windows, and two close notifications. After hiding the owner, neither native
handle may remain in the registry, and neither source menu may be disposed.
There is no alternate menu renderer or headless substitute in the installed-SDK
program. A logical control handle cannot satisfy the window registry checks.

This extends the original designer/Form/ProgressBar paint smoke without removing
its assertions. The existing Ubuntu Xvfb, macOS and Windows package jobs run the
same program and retain their existing job and process watchdog timeouts. No
additional VM or forced renderer path is introduced.

This check does not inject native user input or compare screenshots, GPU
readback, focus restoration, edge placement or DPI. Those separate Windows
reference and portable Windows/Linux/macOS acceptance requirements remain under
[ProGPU #197](https://github.com/wieslawsoltes/ProGPU/issues/197). Passing source
paint callbacks is not proof of rendered pixel parity.

## Integration and retained failure

The PR70 integration includes qualified main `e7fe2106b2ea13e443b40011c864f5a125bf2ba6`,
including the merged popup keyboard, native Unicode, hosted-control and paired
desktop-harness work. The visible smoke's `Program.cs` remains byte-identical to
`dca9cc121e2a8345af39804c31eda652be08bcb5` (SHA256
`5fc4e099ee2a60fdbd9bcaada08d087bd05b0736e451c31f0d876682c0db8f29`).
Its original root/child checks, paint observations, owner-hide teardown and
60/120-second process watchdogs are unchanged. The original allocation suite
and all existing workflow gates remain intact.

The preceding [Build 36272718245](https://github.com/wieslawsoltes/LibreWinForms/actions/runs/36272718245)
failed in `Canonical source and ProGPU submodule lane`, step `Validate canonical
source and local ProGPU graph`: `FontQualityTests.WarmedPrivateMetricReadsAreAllocationFree`
expected zero bytes and observed 1024. The Drawing suite reported 648 passed,
one failed, zero skipped, 649 total. That failure prevented subsequent package
and visible smoke qualification; integrating main does not establish its cause
or claim it is solved. The separately reported package-free counter behavior is
tracked in [dotnet/runtime #134724](https://github.com/dotnet/runtime/issues/134724),
not treated as proof of this particular failure's cause or as a gate waiver.
The failed log remains retained, and the integrated head requires a fresh full
Build with the original strict assertions before qualification.

Focused integration checks passed: 13 offline desktop-harness cases,
documentation verification, shell syntax, actionlint and diff checks. The
unchanged visible program compiled in a fresh Package-mode consumer against the
qualified PR74 feed using SDK `11.0.100-preview.5.26302.115`, with zero warnings
and errors. Only package version placeholders were resolved as in the existing
visible-smoke runner. No application, VM or native input was run for this
integration; its new exact-head package and desktop jobs remain authoritative.
