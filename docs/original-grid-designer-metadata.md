# Original grid sample designer metadata

The original `dotnet/samples` DataGridView custom-column sample at commit
`acb39ceb13f910ae0f8f6298059c59102b749c41` predates WFO1000. Building its
unchanged custom column/editor with the current installed SDK reports nine
missing serialization contracts. This is a sample migration requirement, not a
reason to disable the SDK's original analyzer payload.

`eng/librewinforms-grid-designer-metadata.py` prepares **only two exact,
SHA-256-pinned input files**, into a new destination. The original files and
license remain separately retained by the caller. Five column properties gain
`DefaultValue` matching their actual uninitialized backing fields: `Mask` and
`ValidatingType` are null, `PromptChar` is NUL, and both inclusion flags are false.
The comments describing ordinary MaskedTextBox defaults do not initialize these
column fields; substituting underscore/true would describe a different contract.

The four writable editing-control interface properties gain explicit hidden
designer serialization. Their grid relationship, current formatted value, row
index and dirty state belong to the running grid, not designer persistence.
No getter, setter, constructor, event handler, input path or validation hook is
changed. The transformer reverses its nine insertions and requires exact original
bytes before accepting output. BOM and line endings are retained.

```sh
python3 eng/librewinforms-grid-designer-metadata.py \
  --source-root /path/to/original-sample \
  --destination /path/to/new-metadata-output
python3 eng/tests/test_grid_designer_metadata.py
```

The output is a two-file overlay plus a provenance receipt, not a complete sample
or a product binary. SDK/TFM adaptation is a separate recorded preparation change.
All nine additions must be disclosed when describing the prepared application;
do not call its source byte-identical to upstream. Other sample source/resources
and all original package archives stay unchanged. No WFO1000 suppression,
`NoWarn`, analyzer removal or generated-bootstrap substitution is performed.

Eight offline controls run in the existing canonical CI job. The real Linux
installed-package attempt before this preparation retained all nine WFO1000
errors and existing dependency vulnerability warnings. An attributed sample
still requires actual compilation and independent native grid/input/pixel
validation; metadata preparation does not qualify those contracts.

## CI evidence and main integration

Build `36289851880` at `01eec98d1` passed the original-grid metadata controls and
canonical source tests, but the later analyzer matrix timed out at the unchanged
300-second limit for `project-vb-negative`. Its log records successful restore in
846ms and Forms/Primitives/Design outputs, then no further progress. Earlier
CSharp negative/positive controls completed in 12.47s/12.91s. The available log
does not establish a compiler, analyzer or build-graph root cause; earlier NuGet
download timeouts do not explain this already-restored case by themselves.

The comparison Build `36289889933` at `117a2ef26` completed this VB negative
control in 20.08s with the expected WFO1000, and all 88 compiler contracts passed.
Those heads have identical product source, SDK packaging, analyzer/pack/source
scripts, NuGet/SDK/build properties and ProGPU pin. Their separate metadata and
capture-diagnostic changes do not establish a repair for the stall.

PR84 now integrates qualified main `61c2faf58`, retaining both the grid metadata
preparation and the conservative capture guard. All compiler cases, assertions,
cache ownership and deadlines remain unchanged. The previous failed Build is
still unqualified; the integrated head requires a new complete Build and Docs.
If the stall repeats, focused compiler/process evidence is required rather than
an unexplained retry or a widened limit.
