# Source device text rows

The original grid label uses Microsoft Sans Serif9.75pt with charset0. The native
96DPI observation reports line height16 and ascent13; the ordinary Drawing hhea
formula yields14.7138671875 and11.984375. Rounding the completed paragraph or
changing sample bounds cannot correct the individual source rows.

`DrawingSourceText` explicitly selects the exact font-owned device line frame
provided by [ProGPU #391](https://github.com/wieslawsoltes/ProGPU/pull/391).
The complete original90-case Windows receipt and primary-source research are in
that producer's `docs/source-device-line-metrics.md`. Its parser preserves exact
original VDMX records, including legacy version-zero charset defaults, and keeps
missing/malformed/variable contracts separate. Source metrics are not a new
ordinary `Font.GetHeight` or `Graphics.DrawString` default.

The source adapter accepts ordinary horizontal point/pixel fonts with charset0/1,
square target DPI and an exact integral pixel em. It does not round fractional em
or substitute a nearest record. Missing/unsupported metrics preserve the previous
Drawing service path; malformed records reject rather than masquerading as absent.
The descriptor retains the exact immutable font and size. Disposal and mismatched
source owners fail through existing typed guards before publishing text commands.

`ProGpuTextRendererService` shares the selected frame across source margins,
single-line viewport accommodation, multiline measurement, height trimming,
painting and retained editor interaction. The existing formatted layout remains
responsible for hotkeys, digits, wrapping, ellipsis and complete source ownership.
Ordinary Drawing calls remain unchanged. Retained empty text has zero content
size and a real source caret band; glyph advances, outlines and raster policy are
not changed to hide remaining pixel differences.

Both managed and native provider routes consume the same prepared glyph positions
through `DrawPreparedGlyphRuns`. No native wire, alternate rasterizer or platform
input policy is added. The descriptor is paragraph-owned CPU metadata; it neither
creates GPU resources nor changes glyph cache/atlas identity. There is one bounded
existing VDMX query per source layout request, not per glyph, and no performance
improvement is claimed.

Authored Drawing controls use the existing owned static Inter fixture plus explicit
test-only VDMX records. They cover point/pixel realization, actual144/192DPI,
non-square rejection, blank/empty rows, trimming, identical direct/retained glyph
positions, ordinary metric preservation and mismatch/disposal rejection. Service
controls verify source measurement, drawing and editor rows together. These are
transport/ownership tests, not Microsoft pixel fixtures or native qualification.

Local validation includes 46 focused Drawing cases and the full 909-case Drawing
suite. The full Forms backend suite passes 590 cases with 12 explicit platform
skips. All 39 short-line and fixed-value source-service cases pass without skips.
The padding and short-line tests retain every original pixel comparison and now
use the realized source frame. Their independent reference still paints a full
line before clipping; authored 15-pixel controls require exact 3+4 and 5+7 margins
and top/center/bottom baselines 47/43/39 in a seven-pixel viewport at Y=35.
The initial device-line integration pinned ProGPU `f3b3ea24bee3e3434e67e116450915395ee69e5b`,
whose whole Build39081260342 passed all59 jobs and whose exact head passed all69
checks before PR391 merged. The subsequent qualified producer pin and its complete
build are recorded in [Linux source runtime integration](linux-source-runtime.md).
Before merge/release, run the complete Forms producer,
then validate original source grid and editor
captures, normal shutdown, native glyph rasterization and public package closure.
Issue [#174](https://github.com/wieslawsoltes/LibreWinForms/issues/174) remains open.
