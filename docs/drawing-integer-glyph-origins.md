# Ordinary single-bit glyph origins

Ordinary GDI+ `SingleBitPerPixel` paint selects ProGPU's explicit
`TextHintingMode.IntegerPixel` for positioned outline glyphs when Graphics has a
known physical raster scale, square layout DPI and a translation-only source
frame. Synthetic bold/italic and CFF/color/bitmap routes keep their existing
policies. GDI source text never enters this policy through a shared enum value.
Grid fitting, antialiasing, ClearType and unknown target frames remain separate.

The adapter retains the actual selected run font, glyph indices, logical
positions, origin and measurements. Selection occurs at paint time, so a retained
layout can change rendering hint without rebuilding its shaped arrays. The
producer transforms and rounds only at the actual target boundary, including
physical DPI; Graphics does not pre-round positions or infer raster scale from
its layout DPI. `PreferGlyphAtlas` retains the intended ordinary atlas route,
including the producer's origin-preserving capacity fallback.

This uses the typed API from
[ProGPU #396](https://github.com/wieslawsoltes/ProGPU/pull/396). The consumer pin
and real runtime payload select commit
`8f769224e899669b0cf888db2f209de745b96e8a`, whose complete
[producer Build](https://github.com/wieslawsoltes/ProGPU/actions/runs/39145980376)
passed all 61 jobs and all 71 applicable checks, including Svg.Skia parity.
Source validation uses that exact checkout, its manifest-pinned
framework sources and its verified native package payload. This does not qualify
the consumer package until its own whole Build and original package checks pass.
This successor retains the original `9f82017` integer-origin policy and adds
the independent explicit grayscale transfer described in
[drawing-grayscale-contrast.md](drawing-grayscale-contrast.md).

## Evidence and remaining work

Prepared grid-fit paint also records an explicit integer raster-origin request
when its already-admitted coverage is aliased. This includes the binary branch
of the original gasp selection; grayscale stays Auto. The new producer entry
point retains the original font-owned placement array and source ink bounds and
rounds only after composing the final target translation. Font cache identity,
measurements, source GDI paths and interpreter/dropout selection are unchanged.

This connection uses the qualified API from
[ProGPU #399](https://github.com/wieslawsoltes/ProGPU/pull/399). Its five native
packages and all 204 runtime/SDK payload files were verified against the exact
successful producer before source execution. Original full-image comparisons,
consumer packages and application acceptance remain required. Existing unhinted
phase observations below do not independently qualify grid-fit placement.

The combined source build succeeds and all 993 Drawing tests pass with no skips
against that exact staged producer. Recording checks retain IntegerPixel for
admitted binary native paint and Auto for grayscale. These source checks do not
replace the original complete Microsoft image and application comparisons.

The additive [original grid-fit phase observation](https://github.com/wieslawsoltes/LibreWinForms/pull/189)
now retains 648 phase frames and eight whole-string controls. All prior 324 phase
frames and four controls match exactly. At the same two fonts, sizes and 96 DPI,
both SingleBitPerPixelGridFit inventories and Microsoft Sans Serif's binary
AntiAliasGridFit inventory match integer shifts at phase 0.5 on both axes.
Segoe UI's grayscale AntiAliasGridFit has its independent phase inventory and
retains Auto. These original placement observations do not qualify new fonts,
other DPIs, exact product coverage or its font interpreter.

The original application finished in 9.3557226 seconds under the unchanged
45-second/60-second bounds. The hosted job subsequently failed while removing
its isolated SDK because the compiler retained hostfxr.dll; the runner teardown
terminated that compiler. Its complete original artifact and hashes are retained,
and the job is not claimed green or rerun. An earlier setup-dotnet CLR crash
occurred before application execution and is retained separately. No desktop
input or local VM was used; the temporary diagnostic PR is closed unmerged.

The [original raster-phase observation](https://github.com/wieslawsoltes/LibreWinForms/actions/runs/39123876508)
retains 328 complete original frames, including four exact baseline controls.
At the observed positive 96-DPI positions, both original fonts' unhinted
single-bit rasters switch at phase 0.5 in each axis. The
[independent driver-placement observation](https://github.com/wieslawsoltes/LibreWinForms/actions/runs/39122980315)
reproduces all four unhinted original frames using design advances and natural
baselines. These observations support correcting raster placement without
altering logical advances. They do not establish general source raster parity.

`GraphicsIntegerGlyphOriginTests` contains 16 controls for immediate/retained
recording, unchanged layout and array reuse, separate source/target policies,
and complete bitmap comparisons against independently rounded positioned runs.
All 16 passed in the initial 991-test Drawing suite with no skips. With the
unchanged preceding adapter, the six policy controls and both complete bitmap
comparisons fail for the intended origin differences. An initial fixture omitted
explicit `StringDigitSubstitute.None`, causing seven retained-layout setup errors;
that failed run is preserved separately from the corrected negative control.
These authored Inter comparisons are not Microsoft reference images. The
unchanged 72-case original pair, source font identity, both native providers,
whole consumer packages and original grid/application gates remain required.
Monochrome dropout, hint execution, antialias contrast and ClearType remain open
in [#174](https://github.com/wieslawsoltes/LibreWinForms/issues/174).

The producer's [design research](https://github.com/wieslawsoltes/ProGPU/blob/8f769224e899669b0cf888db2f209de745b96e8a/docs/integer-glyph-raster-origin.md)
examines Skia, DirectWrite/Win2D, WebRender, Vello/Parley and HarfBuzz. This adapter
adopts the same separation between retained CPU layout and explicit target raster
policy. Its selection is O(1) per run with no new arrays, cache or native owner;
the existing producer performs O(G) compilation for G selected glyphs. It adds no
font discovery, reshaping, CPU bitmap rasterizer or global gamma override. Existing
warm allocation, cache residency, original image and final application timing
checks remain mandatory; no performance improvement is claimed.
