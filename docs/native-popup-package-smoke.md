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
