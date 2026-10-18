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
`9f82017bdeadc7dd01e15c5cead93934bb1aefeb`, whose complete
[producer Build](https://github.com/wieslawsoltes/ProGPU/actions/runs/39125588718)
passed all 61 jobs. Source validation used that exact checkout, its manifest-pinned
framework sources and its verified native package payload. This does not qualify
the consumer package until its own whole Build and original package checks pass.

## Evidence and remaining work

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
All 16 pass, and the full Drawing suite passes 991 tests with no skips. With the
unchanged preceding adapter, the six policy controls and both complete bitmap
comparisons fail for the intended origin differences. An initial fixture omitted
explicit `StringDigitSubstitute.None`, causing seven retained-layout setup errors;
that failed run is preserved separately from the corrected negative control.
These authored Inter comparisons are not Microsoft reference images. The
unchanged 72-case original pair, source font identity, both native providers,
whole consumer packages and original grid/application gates remain required.
Monochrome dropout, hint execution, antialias contrast and ClearType remain open
in [#174](https://github.com/wieslawsoltes/LibreWinForms/issues/174).

The producer's [design research](https://github.com/wieslawsoltes/ProGPU/blob/fix/text-integer-raster-origin/docs/integer-glyph-raster-origin.md)
examines Skia, DirectWrite/Win2D, WebRender, Vello/Parley and HarfBuzz. This adapter
adopts the same separation between retained CPU layout and explicit target raster
policy. Its selection is O(1) per run with no new arrays, cache or native owner;
the existing producer performs O(G) compilation for G selected glyphs. It adds no
font discovery, reshaping, CPU bitmap rasterizer or global gamma override. Existing
warm allocation, cache residency, original image and final application timing
checks remain mandatory; no performance improvement is claimed.
