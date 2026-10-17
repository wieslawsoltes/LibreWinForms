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

That public bitmap inventory reproduced the eight idle arrow frames but lacked
the separate glyph:24 active arrow frames retained their original differences.
The later source-file investigation below resolves those images independently;
no arrow policy is inferred from the thumb result.

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
run39070628504 were unavailable; they did not supply the arrow glyph. The later
source-file loader is described below. Smaller targets and other source policies
remain required. After integration, whole producer CI and the original
packaged application must pass; local image comparisons alone do not qualify
the package or close #174. Local VMs were not used.

## Arrow source resource investigation

The original public atlas observation in run39073996776 returned unavailable for
all84 stream/rectangle/name queries across28 cases. Vertical SizingType reported
PO_NOTFOUND, including default state zero. Both native owners retired. This does
not supply a missing sizing default or an arrow image.

The subsequent read-only observation in
[run39074238860](https://github.com/wieslawsoltes/LibreWinForms/actions/runs/39074238860)
retained the exact currently selected Aero.msstyles file, without installing or
changing a theme. Artifact12116480928 SHA256 is
`96cc1de631ae03e88a33c5f88d526e75b1bc78632f95b0a498c0fd6310398516`;
the source file SHA256 is
`40878bc58a991112004fb35c4130911c4c0bbaa94d9045365107d4f941c06f59`.
Five retained C# source files match revision
`3f4e74b15983334b2f7afb2a51d47365f1492b0c`. The bounded application completed in
0.073seconds with both native owners retired and no injected input. Separate
diagnostic workflow CI is unavailable through PR Code Review; these are original
application/resource observations, not whole-producer qualification.

Exploratory resource-table analysis identifies the ScrollBar part1/state0
IMAGEFILE and IMAGEFILE1 references as IMAGE1120 and IMAGE1121. Their unchanged
PNGs contain a17-by340 opaque background and a13-by260 image with actual alpha
coverage, respectively. The public bitmap query had returned only the first.
Centering each13-pixel selected frame at(2,2) inside its17-pixel background
reproduces every pixel of all32 independent original arrow captures. All32
captures are also byte-identical to the previously retained original observation.

The32 new managed renderer cases retain these original source PNGs only as test
fixtures, verify their hashes and all full reference frames, and dispose both
source images before reading the retained result. Together with the18 existing
thumb/ownership/state controls, all50 focused cases pass without skips. These
resource observations are independent of the live loader described below.

## Live arrow resource selection

The live arrow path reads the current theme path/color/size, opens that theme at
96DPI and maps its file with resource-only LoadLibraryEx flags. It checks the
native image/layout/selection properties, version-four resource marker and full
VMAP, CMAP, BCMAP and selected VARIANT tables before selecting image IDs. The
class map uses eight-byte alignment; base indices exclude leading variant names.
The actual ScrollBar inheritance chain contributes metadata, with closest-class
precedence and duplicate rejection within a class. Malformed unrelated records
or base chains also reject the complete candidate.

Only the explicit first minimum-size family is admitted. Both actual PNGs are
copied before releasing module storage, bounded before decode and fully prepared
before paint. The selected image keeps its real alpha. The current theme identity
is rechecked before recording either frame. The resource module joins the existing
independent retirement and failed-owner retry path; product code contains no
fixture path or hardcoded image resource IDs.

The hosted no-input test requires native selection and all32 original arrow
frames through the default service, alongside the eight horizontal-thumb frames.
Local tests validate whole-table rejection, inherited metadata and derived
precedence; macOS explicitly skips native selection. Empty inline scalar records
also reject even in unrelated classes, preserving whole-inventory validation.

The focused hosted observation
[39075893559](https://github.com/wieslawsoltes/LibreWinForms/actions/runs/39075893559)
passes all six tests with zero skips in13.732seconds, including every pixel in
all40 original frames. Six retained source files match diagnostic
`c4593971794ce4e4f3c1d55cf8290ba39f75267b`, whose product parent is
`96629a2b3b57243db0e9a13769cb721d93b942a8`. Artifact12117385604 has SHA256
`6e92aeac447717de26cdf5d72c32d1115ed84ac27e8f6a9fd61308533d9e1220`.
The application receipt records exit0, no injected input and qualified=false.
The later empty-scalar rejection retains separate local controls and must pass
the final head's hosted lane. This observation is not whole-CI qualification.

## Whole-table inheritance allocation

Inheritance validation uses bounded scratch storage, one original-path visit
marker per class, and the unchanged maximum of64 class visits. The complete
class map caps this storage at4096 integers before allocation. Shared ancestors
remain valid; cycles in unrelated classes and paths longer than64 still reject
the entire candidate before image publication. The source record and image
selection rules are unchanged.

A local measurement parses the complete original374-class/370-base table128
times after32 warmups, checking the selected descriptors on every call. Three
processes each report113,904 allocated bytes per parse before the change and
33,432 afterward. These are parser allocations on macOS ARM64 .NET10.0.9,
not full-paint allocation or application timing. All71 focused renderer/parser
controls pass; the two Windows-only native tests remain explicitly skipped
locally. Native full-frame and whole-package validation must cover this final
change. Per-paint resource acquisition and decoding remain separate work.

Final whole-producer/native passage, caching and
theme-change performance, other sizes/DPIs, original packaged grid paint and
physical input remain required. This implementation does not close #174 or
qualify the pending release.
