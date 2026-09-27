# Portable text target DPI

The installed Windows paired popup baseline had equal 192-DPI, 1120x500 clients,
but portable labels, editor text and ComboBox text were roughly half-sized.
The Drawing recorder advertised 96 DPI even when its surface was in device
pixels. The canonical row-height path independently used the same 96-DPI
`Font.Height`, so changing painting alone would retain incorrect layout.

This change requires the additive ProGPU Drawing target-DPI overload. Silk
device-pixel recorders use actual window DPI; logical recorders remain at 96
with their original presentation transform. One retained frame shares its
resolution across all control layers; a resolution change repaints all layers.
Direct and adorner recorders use the same target. No source Font, driver scale,
font family, native coordinate mapping or input path is rewritten.

Canonical font realization is distinct from the surface DPI. Native
`FontCache.Data.FromFont` converts the source font against `InitialSystemDpi`,
while PMv2 `Control.GetScaledFont` already scales its public source size by the
monitor ratio. Portable aware modes therefore capture the initial primary DPI,
including PMv2. All four TextRenderer draw/measure boundaries borrow an owned
pixel-font projection with the native rounded em size. They retain exact source
family/style/charset/vertical metadata without modifying the caller Font.
Pixel-like units are already pixels, not `SizeInPoints` interpreted at an
unrelated96-DPI reference. Null fonts retain the existing backend default policy.
This is an intentional change to the borrowed backend argument, not public Font
ownership or a new platform callback contract. Retained drawing copies glyph
arrays and retains the actual TtfFont/scalar size before this projection retires.

`FontHeight` uses the original source font's fractional line metric at the initial
system reference and rounds only the final height, matching native `Font.Height`;
it does not reuse the TextRenderer em-rounding step. Its existing source-font
cache invalidation remains authoritative. ListBox rows and real ComboBox editor
bounds use this metric. A caller Graphics still owns its clip, transform and DPI;
canonical TextRenderer does not reinterpret source font size using that target.
Ordinary `Graphics.DrawString`, `MeasureString` and `Font.GetHeight(Graphics)` keep
their actual-target semantics. SystemAware keeps the initial font reference on a
monitor change; native desktop virtualization is not emulated by multiplying text.

Focused source regressions cover pre-handle and live192-DPI metrics, logical
96 despite presentation scaling, font-cache/source updates across DPI/handle
replacement, and caller measurement DPI. Real backend recorded-command tests compare retained
frame/layer measurement and glyph sizes at 96 and 192. These are not native
appearance, installed-package, input or full popup parity qualification. The
original paired captures remain failure evidence; no new desktop run occurred.

The first PR87 version incorrectly multiplied PMv2 font scaling a second time.
Nine independent source controls now distinguish the font reference from Graphics
DPI. The original product with eight controls retained **six failures/two passes**
in `font-reference-baseline-tests.log` (source-only, no native execution). Literal
transition expectations are24→12→24px for a9pt source starting on a192-DPI window,
both when the primary/system reference is192 and when it is96. The public source
sizes are9→4.5→9pt in the first case and18→9→18pt in the second. Pixel12 remains12;
9.1pt at initial192 uses25px TextRenderer em but a24.266… fractional em for line
metrics. A144-DPI raw DrawString9pt records18px, while canonical TextRenderer at
initial192 records24px. These expectations come from checked-in canonical source,
not from comparing two paths which derive the same potentially incorrect Font.

## Local source evidence

Actual source head `9f1657f4f0717fe6f88e2486574bf9c282ed181b` against ProGPU
`22f83ea5529e0d975a3b9a48b17371f79e6f003d`: canonical full suite **538 passed,
zero failed/skipped** (18.174s), including five additive target-DPI facts. Real
backend retained-frame/layer cases **2 passed, zero failed/skipped** (313ms).
Builds used SDK11 preview5 targeting net10.0 and tests used .NET10.0.5 ARM64.
Canonical build: zero errors,630 existing source warnings; backend build:0/0.

Evidence is retained under the task worktree's `artifacts/text-target-dpi`.
`canonical-final-tests.log` SHA256:
`4953fed1592c848c23176d20727dcebf74b5cac15f66561237cc29241403a208`.
`backend-dpi-tests.log` SHA256:
`dd60ac95611cea0ed2bb34727024259602b9bc0d7b5a9f090826aafd0c72f96f`.
The initial537-case result374pass/163fail is retained: its headless fixture used
Graphics nullability to classify the literal `managed`/`headless` sentinel text.
It now asserts explicit Graphics while retaining those text, flags and sizes;
the intervening corrected537/537 result is retained too. Two strict formatting
build failures were corrected; no analyzer, case, timeout or assertion was waived.

The final combined source `74707f9e9` incorporates exact context-menu PR86
`cd94646ddd7744d686725ce5684634c65c03849c` and pins ProGPU
`aad4a98c4e9b1f3589eafbacd068111c5595c578`. Its complete **557/557** canonical
suite passed with zero skips in18.419s (build0errors/630existingwarnings).
The19 context-menu cases and pointer implementation remain byte-identical to
that parent; all five DPI cases remain included. Evidence is
`artifacts/text-target-dpi/integrated-557-{build,tests}.log`. Both prerequisite
whole Builds and the combined exact-head CI remain required before merging.
