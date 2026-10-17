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

## Hosted build preparation

`Original grid package preparation` builds both original applications on a
disposable Windows ARM64 runner without launching either application or sending
input. It selects the complete successful Forms producer
`97c1a5e0d86371c46e5f13e63f79c16817b2d4e8`, Build `38975594619` and Docs
`38975594625`. All nine original Build jobs and the exact package artifact must
remain successful and available. The checked package inventory retains all 17
archive members, their hashes and their actual source-repository identities.

The source contract records the original 26-file sample inventory and both
retained prepared manifests. Preparation reads immutable Git objects, verifies
every original blob, applies only the existing nine designer annotations and
the original SDK/framework projection, then requires all 52 prepared file hashes
to match the previous pair. It retains the original license, passive observer,
SDK `11.0.100-preview.5.26302.115`, bootstrap ordering and package-source mapping.
Prepared source files are written only after the complete source batch validates.

Both builds keep the original five-minute compilation bound, isolated package
cache and exact package-assembly checks. Their output inventories and failed
logs are retained. These are build-only receipts: they do not resolve the earlier
Linux Alice/Enter failure, qualify native input or waive the original desktop
protocol. The unchanged 20/60-second desktop bounds and independent pixel review
remain required before any acceptance claim.

## Explicit hosted input

Manual dispatch may set `run_original_input=true` after the bounded hosted Forms
input approval. The default is false, and automatic pushes only prepare/build.
Both exact original consumers must compile before the unchanged desktop driver
runs. Its original startup/process/transition limits, source/layout/payload
checks, PID/foreground/held-input guards, action sequence and four captures per
application remain unchanged. Retain all evidence on failure without retry.

The first approved hosted run39005226688 stopped during Microsoft startup at the
foreground-ownership guard, before any input or capture and before portable
launch. The two source snapshots record a ready Microsoft form, but cannot
identify the other foreground owner. Original failure artifact12090916682 and
the compiled-pair artifact12090239572 are retained. This is no input acceptance
or proof of a product fault.

Before launching either application, hosted setup reuses LibreWPF's existing
bounded desktop preflight: it records actual session, foreground and native
window identities, and may send one ordinary WM_CLOSE only to the verified
Windows Microsoft-account startup window. Exact process path, native class,
session and unchanged identity are required; unknown/replaced/reappearing owners
or a refused close fail. Search and Start remain observed without input or
termination. The preflight does not change application deadlines, force focus,
change settings or permit shell keys. Its nine original controls are retained.
The first Forms failure did not record the foreign owner; this preflight must
not be cited as a diagnosed fix without the actual new observations.

The owner approved the additional hosted startup input on 17 October 2026.
Explicit `--hosted-startup` now permits at most two verified Start/Search Escape
presses across the pair, only at first Microsoft startup, and one verified native
caption click per application. The reused WPF helpers retain exact system-image,
session/PID/class/foreground checks, HTCAPTION/point ownership, held-input and
partial-release guards. Local/self-hosted/foreign repository calls are rejected.
All startup work uses the original 20-second startup deadline inside the original
60-second process budget; shell dismissal additionally has its three-second bound.
The original eleven keys, two cell clicks and four captures per application remain
unchanged. Startup actions and helper hashes are recorded independently, with
advancing source observations proving unchanged editor/content/layout state.
Original guards are not bypassed, deadlines do not restart, and failed actions are
not retried. The ordinary default path still uses its original activation request.
The two earlier foreground failures remain retained; the extension is not evidence
of a product repair or acceptance until the actual paired workload passes.
