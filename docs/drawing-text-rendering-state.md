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

Ordinary GDI+ `SingleBitPerPixel` and `SingleBitPerPixelGridFit` now select binary
glyph coverage in both immediate and retained draws. The previous writer always
recorded grayscale coverage. Selection happens at paint time, leaving original
glyph IDs, positions, layout, brush, clips and transforms unchanged. It uses the
existing GPU aliased path, with no CPU pixel conversion or per-frame font work.
Both modes share this coverage requirement; they still require distinct native
hint execution, and binary vector coverage does not implement monochrome dropout.

Source GDI `TextRenderer` has separate quality semantics. `DrawingGdiText.Draw`
and `CreateLayout` retain that source identity explicitly, including the path
without VDMX metrics. The Forms backend uses these entry points for ordinary
control text and retained editors. An exact device line frame is independent of
raster policy; neither the presence of VDMX nor a single-bit GDI+ enum is used to
infer GDI binary coverage. No mutable Graphics mode override is used, and source
identity cannot leak into a subsequent ordinary draw. The current GDI paint
policy is preserved while its font-quality/system-aware raster work is completed.

Four recording regressions fail before the correction. Controls cover paint-time
selection, retained placement identity, both source metric paths and subsequent
ordinary draws. Four actual bitmap controls examine every pixel of opaque black
single-bit text over white, requiring only the two original colors and real ink.
These establish binary coverage, not equality to the retained Microsoft images.

Immediate text paint now retires its temporary layout clip even when glyph or
decoration preparation throws. Previously a failed brush conversion left that
clip active, hiding subsequent paint outside the failed text rectangle. Cleanup
removes only the scope created by that draw and preserves the original failure;
already recorded glyph commands are not rolled back. The retained path already
had this cleanup. Twelve controls cover both source identities, immediate and
retained drawing, failures before glyphs and during underline preparation, and
actual subsequent bitmap pixels. Eight fail before the fix; the four retained
controls pass unchanged.

The writer still has independent work to implement complete native hinting,
font/size policy, monochrome dropout, contrast and source GDI quality selection.
ClearType pixel parity, system font-smoothing selection, busy-HDC behavior and
original package/application captures remain separate requirements in
[issue #174](https://github.com/wieslawsoltes/LibreWinForms/issues/174).
