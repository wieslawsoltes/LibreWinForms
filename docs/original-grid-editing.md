# Original Windows DXC layout failure — 2026-10-17

The exact Forms `4490cd8e4be696673efb1c6ae72dfc63b68dd18d` producer passes
all nine Build jobs (`39023126100`) and Docs (`39023126102`). The original grid
run `39025614225` retains all 52 source hashes, original input, and 20/60-second
limits. Microsoft completes all four phases in 7.313 seconds. Portable startup
now completes with the actual packaged Dxc compiler, then fails exact layout
before any editing input: vertical font autoscaling is 16 instead of 15. Client
height is 549 instead of 515; the grid is 368 instead of 345 pixels high. Font
family/size, Font.Height, horizontal autoscale and explicit row/column metrics
agree. This does not qualify portable editing or erase the Automatic failure.

The portable autoscale path currently uses the Drawing line metric; upstream
uses GDI TEXTMETRIC from the FontCache-selected HFONT. The read-only hosted
`librewinforms-font-metrics-diagnostic.yml` records actual native metrics, selected
face, font-data hash and original head/hhea/OS2 table bytes for 45 fixed font,
size and DPI cases. It creates no application window or input, retains a
30-second process limit and changes no product, sample or acceptance predicate.
It diagnoses the metric contract; it is not a font-parity qualification.

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

Hosted preparation recognizes the historical nine-job Build layout and the
ten-job layout that also requires `Live Windows theme resource pixels`. It checks
the complete named inventory, exact source head and successful status of every
job, alongside successful whole Build/Docs and the immutable artifact identity.
An equal job count cannot hide a duplicate, substituted or missing job. A
ten-job producer cannot qualify through a nine-job pin.

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

Run39012843688 closed verified Search with one Escape, then retained an inactive
Start surface covering all three native caption candidates. No caption click or
grid input occurred. The integration had omitted the original driver's ordinary
activation request. Restore that single `SetForegroundWindow` request after shell
dismissal, before the guarded optional caption click. Recheck the exact native
owner and original deadline afterward; a refused request never authorizes a click
through an obstruction. Artifact12093189687 preserves the original failure.

With the original activation restored, run39013197920 completed all four Microsoft
capture/edit phases in 5.734 seconds. The portable process produced no initial
observer snapshot before its original 20-second startup deadline. Its empty
streams, unchanged payload and all four reference captures remain retained in
artifact12092754938. This identifies a portable startup failure, not an editing
result. Optional hosted `diagnose_startup` runs the same controller and takes one
managed stack report from that exact owned portable child at 12 seconds, bounded
to five seconds and 2 MiB. The pinned diagnostic tool cannot extend controller
deadlines, retry input or terminate an application; only its own diagnostic child
is stopped at the bound. Diagnostic output never qualifies acceptance.

Managed-stack diagnostic run 39013729601 again completed the Microsoft phases,
then reached the portable startup deadline with no source snapshot. The stack
observer itself hit its five-second bound with empty output. Preserve original
artifact 12094121172; it does not identify the blocked native call.

The next diagnostic uses a source-built Windows SDK `MiniDumpNormal` helper.
The controller duplicates only its exact live child's process handle with
query/read/synchronize rights, inheriting that single handle into the helper.
The helper verifies its process ID and liveness, never opens a PID, attaches a
debugger, suspends a thread, injects input or terminates the application. At the
same 12-second observation point, both the parent and the SDK cancellation
callback impose five seconds and 32 MiB; only the helper is terminated at the
parent bound. The original controller still owns all application deadlines.
The helper uses CREATE_NEW, retains SDK default thread/module inclusion and
never requests heap/full-memory capture. Structural validation requires the
exact process identity and native x64/ARM64 metadata. Source-built native
controls exercise a real owned child, reject the wrong PID and refuse overwrite
before any application diagnostic. The original managed-stack path remains
available explicitly; neither diagnostic qualifies product acceptance.

Native diagnostic 39014330277 stopped in its ownership fixture before launching
the original pair: the helper compiled and wrote its own child's normal dump,
but the new parser rejected repeated directory entries. Retained earlier SDK
normal dumps demonstrate all-zero `(UnusedStream, 0, 0)` padding. The reader now
ignores only those exact empty slots, retaining uniqueness for active streams
and rejecting any nonempty unused entry. The [Windows SDK stream enumeration](https://learn.microsoft.com/en-us/windows/win32/api/minidumpapiset/ne-minidumpapiset-minidump_stream_type)
identifies UnusedStream as zero; the actual retained files establish its padding
layout. Original failure logs remain evidence, and native controls must pass
before application diagnosis.
