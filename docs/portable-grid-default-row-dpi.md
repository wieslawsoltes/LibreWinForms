# Portable default grid-row font metrics

`DataGridViewRow` retains the canonical default-height policy: the application's
default font height plus nine pixels, cached once for the process. Portable
Drawing's parameterless `Font.Height` uses its 96-DPI metric, whereas canonical
control text uses the selected screen-reference DPI. Using the parameterless
metric produced undersized default rows on a high-DPI screen.

The default row now uses `Control.GetFontHeightForTarget(Control.DefaultFont)`,
the existing shared font-height calculation. It rounds the final line metric at
`ScaleHelper.InitialSystemDpi`; it does not multiply an already-realized font by
the current window scale. Native Windows continues to use `Font.Height` through
the same helper. The nine-pixel addition, three-pixel minimum, process cache,
cloning and explicit row/template heights are unchanged.

Six fresh-process regressions cover 192 and fractional 144 DPI, DPI-unaware
logical coordinates, pixel fonts, explicit 21-pixel template cloning/new rows,
and the application-default-font policy independent of a larger per-grid font.
Before the fix, three failed: the test font produced a 23-pixel row instead of
30 at 144 DPI or 37 at 192 DPI. The three compatibility controls passed.
After the change, all six regressions and all 687 canonical lifecycle tests
passed with zero skips. The source-first CI minimum is raised to 687; no
deadline or existing assertion changes.

This does not override source choices. The original custom-column sample in
issue #6 explicitly sets `RowTemplate.Height = 21` and column widths. Fixing
implicit defaults does not enlarge those explicit rows, qualify that sample's
pixels or establish complete Windows/macOS/Linux parity. Font autoscaling is
tracked separately in [portable-font-autoscale-dpi.md](portable-font-autoscale-dpi.md).
