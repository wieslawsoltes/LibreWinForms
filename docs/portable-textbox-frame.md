# Portable TextBox client frames

The paired Windows popup captures from Forms `31b0fe6` showed a plain portable
TextBox without its source frame. The portable control tree treated every child
window as entirely client content, although `BorderStyle` still contributed to
the source preferred height. Painting a border over that content would leave
text, selection, caret, pointer coordinates and hosted controls inconsistent.

Plain TextBox now declares explicit source-owned non-client insets: none for
`None`, platform `BorderSize` for `FixedSingle`, and `Border3DSize` for `Fixed3D`.
These are distinct from public `Padding` and native top-level window decoration.
No insets are inferred for other controls, including rich/masked editors.

The common portable tree paints the frame in window coordinates, then paints
the ordinary client content in its translated, clipped client frame. Both flat
and retained paths clip descendants to the same client rectangle. Client sizing,
source adornment-aware scaling, screen conversion, pointer hit testing,
CreateGraphics and invalidation use that frame too. Style changes publish client
geometry before replacement-handle callbacks. Borderless controls retain their
existing paint path without extra frame clipping.

The fixed single border fills its actual metric bands, including unequal X/Y
metrics. Fixed3D reuses ControlPaint's sunken source edges. The portable DrawEdge
implementation now fills integer one-pixel bands instead of centered strokes,
which had partial coverage and missing degenerate corner segments. The original
side/color/corner order remains intact. ToolStripTextBox calls its original
professional border painter with the same focus/hover/disabled/high-contrast
policy, without a native window DC or a fake client rectangle.

## Evidence and outstanding qualification

Sixteen canonical cases cover all three styles, live/uncreated style changes, event
ordering, source metric variation, outer/client sizing, nested coordinate
mapping, both paint paths, clipping, actual offscreen frame/child pixels,
zero-client content, source scaling, and independent sunken-edge color samples.
The complete source suite passed locally on macOS ARM64: **751 passed, zero
failed, zero skipped**, running the net10.0 assembly on .NET 10.0.9. The final
incremental build had no warnings or errors; the preceding product build retained
634 warnings and no errors. Docs, shell syntax and diff checks also passed.
The first runs exposed a wrong pointer enum in the fixture, a missing fixture
metric reset, an obsolete whole-window scaling assertion, and incomplete frame
pixel coverage; these were corrected without skipping cases or relaxing limits.
Exact-head full package CI is still required before merge.

This is not modern Windows theme parity, complete native non-client input
semantics, or fresh desktop package qualification. The prior screenshots diagnose
the old missing frame; they do not validate this implementation. Fresh paired
Windows package captures and corresponding Linux/macOS checks remain required.
SystemInformation's separate DPI-specific border metric policy, native edit
formatting margins, themed/focused borders and rich/masked editor frames are not
silently qualified by these tests. ProGPU issue 197 remains open.
