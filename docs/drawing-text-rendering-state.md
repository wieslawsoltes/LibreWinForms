# Drawing text rendering state

`Graphics.TextRenderingHint` starts at `SystemDefault`, so callers retain the
system-selected quality policy. Its previous `ClearTypeGridFit` initializer
reported an explicit policy that the caller had never selected.

The property accepts the six defined values, 0 through 5. Undefined values
raise `InvalidEnumArgumentException` for `value` before changing the stored state.
Reading or assigning a defined value after disposal raises the existing Drawing
`ArgumentException`. The existing Save/Restore and nested container snapshots
retain the selected value; a new container starts at `SystemDefault`.

The implementation uses the existing owned Graphics state/disposal pattern.
Property access remains constant-time and does not allocate or initialize a GPU.
The contract is documented by the [Microsoft text rendering hint API](https://learn.microsoft.com/dotnet/api/system.drawing.graphics.textrenderinghint)
and [its six values](https://learn.microsoft.com/dotnet/api/system.drawing.text.textrenderinghint).
The retained original Microsoft Drawing tests also specify the initial default,
valid values, undefined-value rejection and disposed-state behavior.

Twelve source controls cover the default, all six values through saved/nested
contexts, four out-of-range integers and disposed access. Against the original
implementation six fail and six pass. After the correction the complete Drawing
suite passes all 921 tests, with no skips. Original failed logs and TRX are retained.

This corrects public state only. The current retained glyph writer still has
independent work to implement each requested raster/hinting policy and contrast.
ClearType pixel parity, system font-smoothing selection, busy-HDC behavior and
original package/application captures remain separate requirements in
[issue #174](https://github.com/wieslawsoltes/LibreWinForms/issues/174).
