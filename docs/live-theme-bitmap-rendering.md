# Live theme bitmap rendering

`ProGpuThemeBitmapRenderer` records an original borrowed bitmap frame as nine
image rectangles using the source sizing margins and explicit nearest sampling.
The observed thumb resources have one-pixel stretch centers; a live source must
qualify its sampling policy rather than infer a generic native filtering default.
The renderer preserves the caller's
transform and intersected clip, restores sampling state, and keeps adjacent
atlas states outside the selected source frame. The complete descriptor is
validated before any draw command. Targets smaller than the fixed borders
return unsupported; they do not acquire an invented border-contraction policy.

This is an internal rendering component. The default portable visual-style
service is not connected to a live native theme source yet. No reference image
or captured texture is embedded or loaded by product code, and the active
scrollbar appearance issue remains open.

## Original source evidence

The separate no-input Microsoft observation in
[run39069566592](https://github.com/wieslawsoltes/LibreWinForms/actions/runs/39069566592),
source `bda6f7f84e4a541b8c4b156f11b25b7a736e2962`, reads the actual theme's
`GetThemeBitmap` resources, image count/layout and sizing margins. Artifact
12115165182 has SHA256
`2dde66edbee808817c1a86b30ce45f66c8599dcbb179365a5303eb5fde8a53a2`.

Both borrowed and copied queries returned device-dependent `BITMAP` objects,
not directly readable `DIBSECTION` memory. A separate explicit `GetDIBits`
observation converts only owned, unselected copies to top-down RGB32. It retains
the original return values, all four bytes and the independent missing-raw-bits
outcome. All 144 queries, 40 copied bitmap retirements and 40 conversion-DC
retirements were reviewed. Three distinct arrays total33,660 bytes. Their fourth
byte is255 in this inventory; this does not establish alpha semantics for other
theme resources. Failed native outputs use initialized storage so an untouched
result cannot accidentally reuse an already-retired handle.

The two thumb resources and their native margins reproduce all16 complete
horizontal/vertical, normal/hot/pressed/disabled frames over both backgrounds
from the earlier independent DrawThemeBackground run39052232473. Test fixtures
retain the original arrays, metadata and PNGs unchanged. The rendering tests
also dispose the borrowed bitmap after recording and verify the retained image
content, caller transform/clip/sampling restoration, invalid frames and separate
small-target rejection.

The arrow background resource reproduces the eight idle frames. Its separate
glyph is still missing:24 active arrow frames retain their original differences.
No glyph, theme/DPI default, animation, border contraction or transparent-image
policy is inferred from the thumb result.

## Remaining integration

A live source must own the actual selected native resource and theme generation,
carry the original source DPI/state/layout/margins and preserve failure-owned
cleanup. Theme replacement, caching and final image lifetime need independent
controls. Original active arrow resources, smaller targets and other source
policies remain required. After integration, whole producer CI and the original
packaged application must pass; local image comparisons alone do not qualify
the package or close #174. Local VMs were not used.
