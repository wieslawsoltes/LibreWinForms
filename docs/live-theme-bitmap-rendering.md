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

The default visual-style service now attempts an actual Windows horizontal-thumb
resource at an explicit 96-DPI drawing target. It requires a native opaque part,
image background, explicit Stretch, vertical five-frame layout, 96-DPI image
selection and one-pixel stretch bands. Unsupported metadata, transparent themes,
other parts/DPIs and contracted borders retain the existing path. No reference
image or captured texture is embedded or loaded by product code. The active
scrollbar appearance issue remains open pending native and application validation.

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

## Live source and remaining validation

`WindowsThemeBitmapSource` opens the current theme for each synchronous draw.
It copies the actual unselected `GBF_COPY` bitmap with GetDIBits and records it
through managed Graphics. Native `IsThemeBackgroundPartiallyTransparent` supplies
the opacity decision; the unused RGB32 fourth byte never supplies that decision.
The managed RGB image owns its copied bytes, and retained image commands survive
its explicit disposal. There is no native theme/image cache to outlive a theme
change. This initial policy incurs acquisition/allocation per paint; caching and
its theme-change lifetime need separate implementation and performance evidence.

The conversion DC, bitmap and theme are retired independently. Failed owners
remain on the creating thread's retry list, successful owners are cleared, and
cleanup errors preserve an original paint failure. The next admitted acquisition
first drains that thread's outstanding owners.

The hosted Windows CI lane runs the actual default service against all eight
original horizontal-thumb frames, without keyboard or mouse input. It separately
requires the native source to be selected, so a fallback cannot pass accidentally.
The first run rejected before paint because the part-defined query incorrectly
passed the visual state to its reserved argument. The corrected call uses zero,
as required by Windows and the original WinForms implementation; all subsequent
resource queries retain the actual state and every pixel/admission check remains.
The subsequent run reached image acquisition and exposed a second call mismatch:
the original thumb inventory succeeds with automatic property zero but returns
E_INVALIDARG for explicit TMT_DIBDATA. The source now directly requests the same
automatic property used by those original thumb resources. No failed query is
converted into a fallback image, and all original pixel checks remain intact.
Three portable controls cover RGB row/channel identity, independent byte ownership,
invalid sizes, and part/DPI rejection; the sixteen original image-renderer frames
remain independent. Hosted passage and original application captures are still
required before qualification. Vertical native sizing remains unavailable rather
than being replaced by an assumed Stretch value. All 192 filename queries in
run39070628504 were unavailable; the original arrow glyph is still unacquired.
Original active arrow resources, smaller targets and other source policies remain
required. After integration, whole producer CI and the original
packaged application must pass; local image comparisons alone do not qualify
the package or close #174. Local VMs were not used.
