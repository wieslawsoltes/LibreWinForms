# Original DataGridView desktop editing

`eng/OriginalGrid/OriginalGridObserver.cs` reads the original
dotnet/samples `windowsforms/datagridview/CSWinFormDataGridView` application at
commit `acb39ceb13f910ae0f8f6298059c59102b749c41`. It does not replace the sample,
insert runtime statements into its controls, or subscribe validation/input/focus
handlers. The older diagnostic validation handlers were not semantically inert:
canonical DataGridView conditionally repairs focus when validation handlers exist.

The opt-in observer uses Application.Idle to find the typed original form and a
100ms timer to read source state. It records exact UTF-16 text/selection, committed
cell value, source editing/focus state, DPI and screen rectangles. It rejects
VirtualMode and stops after 600 snapshots. It never changes focus, selection,
cell contents, validation arguments or editing state. Enable it only through
`LIBREWINFORMS_GRID_OBSERVER=1` for the prepared comparison applications.

The Windows driver reuses the existing PID/foreground/held-key-protected native
input and screenshot implementation. It launches Microsoft first, then portable,
with the same actual DPI and source client/grid/cell/font/row/column metrics.
Desktop origins may differ; sizes and source-relative placement may not. Both
processes must use the expected actual Forms/Drawing assembly locations.

Each app retains the original 20-second startup and absolute 60-second complete
input bound. Subsequent state transitions have a three-second bound inside it.
The protocol uses native click, F2, physical `alice` keys, Enter commit, reopening,
End, `x`, and Escape cancellation. It distinguishes live editor text from the
committed cell value, requires advancing stable observer snapshots, captures
actual desktop pixels, and stops on the first mismatch. Input is never clipboard
paste, synthetic managed dispatch or a control/property mutation. A user-held
key/modifier rejects input; Caps Lock/layout are not silently changed.

## Prepared producer requirements

Stage only a complete successful exact-head producer using the existing package
admission process. Preserve the original sample bytes and the independently
verified nine designer-only annotations from `librewinforms-grid-designer-metadata.py`.
Compile this observer identically into both copies via an external MSBuild
Compile item, after the portable generated bootstrap; do not edit sample bodies.
Record the observer SHA-256 in both `preparation.json` and `build-receipt.json`.
The prepared root must contain the identical `OriginalGridObserver.cs`.

The receipts retain the original preparation/build schema: `sourceCommit`,
`producerSuccess`, `sourceHead`, per-mode source-file digests, actual SDK host/hash,
and per-mode app paths, build exit status and complete output-file digest lists.
The driver rechecks these before launch and all output bytes after owned cleanup.
It strips secret-like inherited environment entries and external startup hooks.
No incomplete/cancelled producer or source overlay qualifies as a final package.

```text
python eng/librewinforms-original-grid-desktop.py --prepared-root C:\Temp\VerifiedGridPair --evidence C:\Temp\FreshGridEvidence
python3 eng/tests/test_original_grid_desktop.py
```

Evidence must be fresh; no result is overwritten. A successful result means only
the listed input phases passed. Header/border/caret pixels still need inspection;
masked columns, Unicode/layout/IME, other editors and Linux/macOS native input
remain independent requirements. Offline harness tests do not qualify desktop
behavior, and an observed editor value is not proof of painted text or a commit.
