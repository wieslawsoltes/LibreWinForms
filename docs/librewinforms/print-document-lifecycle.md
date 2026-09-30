# Canonical PrintDocument lifecycle integration

The source-development graph pins ProGPU
`b05fa39759c96f1163ae15e2483c5aaf71c868cc` for the managed print-controller
lifecycle fix. The update includes the already-merged Direct2D brush and Android
staging changes, but does not change the drawing project's target framework,
project-reference edges, or dependency versions.

The actual printing implementation remains in ProGPU's `System.Drawing.Common`.
LibreWinForms adds no parallel `PrintDocument`, controller emulation, runtime
reflection, or printer fallback. Three shared contracts compile unchanged in
the canonical source tests and both isolated SDK consumers:

- A real `PreviewPrintController` produces one 8×8 preview page and reports
  `PrintToPreview` through the same begin/end event arguments.
- Cancellation during the begin event, controller startup, or page query keeps
  the exact application/controller completion order and cancellation state;
  canceled startup cannot query or render a page.
- Two pages share one independently cloned query/settings object. A 2px left
  margin produces literal page margins `(2,0,6,8)` on both 8×8 pages, while the
  original document settings remain unchanged.

The source lifecycle gate minimum is 153 tests: the previous 150 plus one
independent test for each printing contract. The existing package gate builds
the pinned ten-package drawing closure, compares exact source/package hashes,
and executes the same shared fixture in project and package modes. Prior API,
TableLayout, drawing-identity, and rejected-fallback checks remain intact.

## Boundaries

The [upstream ProGPU fix](https://github.com/wieslawsoltes/ProGPU/pull/189) and
this integration both require their full exact-head CI gates before merging.
The source pin alone does not establish a newly published NuGet release.
Ordinary immutable package versions are unchanged; existing installed packages
are not rewritten. LibreWinForms issue #18 is not declared fully delivered
until the matching source/package closure has been qualified.

These tests qualify managed preview and callback behavior, not printer
discovery, platform print dialogs, page-origin/DPI parity, or physical printer
submission. `StandardPrintController` still explicitly rejects native printing
without a platform adapter. ProGPU's focused lifecycle suite separately covers
all cancellation and exception boundaries.
