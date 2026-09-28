# Native glyph raster sharing integration

The source graph pins ProGPU `60e1f7bab521e377b2109af411f0443856046bd3`
from ProGPU PR #222. It shares exact native glyph coverage within rebuilt batches,
including equal outlines located at different segment offsets. Source outline
indices and every positioned draw remain separate; all original validation,
compiler/adapter defaults and compute/raster/SIMD/scalar execution paths remain.

This retains the complete Forms popup stack and owned startup-hover precondition.
It changes no Forms control, input, theme or managed glyph implementation.
LibreWPF must select this same ProGPU revision with this Forms source graph.

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
