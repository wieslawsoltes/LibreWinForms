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
