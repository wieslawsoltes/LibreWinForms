# Native glyph raster sharing integration

The source graph pins ProGPU `2f47c475cf9143e1943452b40885c37c2de3e849`
from ProGPU PR #222. It shares exact native glyph coverage within rebuilt batches,
including equal outlines located at different segment offsets. Source outline
indices and every positioned draw remain separate; all original validation,
compiler/adapter defaults and compute/raster/SIMD/scalar execution paths remain.

This retains the complete Forms popup stack and owned startup-hover precondition.
The follow-up [window UI-cue correction](portable-window-ui-cues.md) separates
native-style focus painting from managed cue initialization. It changes no font,
theme or glyph implementation. LibreWPF must select this same ProGPU revision
with this Forms source graph.

The pin includes the atlas-growth fixture correction after ProGPU Build
`36459867439` failed: distinct unused control-point bytes now require separate
raster jobs without changing coverage. All original growth/retention assertions
remain, with an additional exact job-count assertion. Local five-mode success
does not qualify the producer or this package graph.

Forms Build `36461567631` passed all nine jobs at `f9fb54132`, before the window
UI-cue correction; that success does not qualify the new control changes.
The previous Forms popup Build `36453578175` passed on product commit `05a9aadcf`;
the preceding native integration Build `36451582979` also passed. Neither result
qualifies this new dependency pin. WPF Build `36451685592` passed 12 of 13 jobs
but still failed the original ARM64 native Showcase resize. Its separate traced
replay passed and identified thousands of glyph dispatches, not a proven stall
cause. Exact-head full CI and original package/application acceptance remain.

No native package is staged by this source update. Staging still requires an
entire successful producer Build at the exact pinned commit; failed and canceled
producers remain ineligible. The independent visual comparison and remaining
Windows/Linux/macOS popup qualification are not replaced by native unit results.
