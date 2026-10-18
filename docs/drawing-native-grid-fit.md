# Owned native grid-fit paint

Ordinary GDI+ `SingleBitPerPixelGridFit` and `AntiAliasGridFit` can record
owned native outlines while keeping the existing layout's glyph IDs, positions,
origin, advances and measurements. Selection requires a known physical raster
scale, square layout DPI, a translation-only source frame, an exact integral
device em, a solid brush and an ordinary static face without synthetic bold or
italic. Unknown, fractional, transformed, variable, color and bitmap cases retain
their existing path. This is a bounded implementation step for [#174](https://github.com/wieslawsoltes/LibreWinForms/issues/174),
not original Microsoft pixel qualification.

The original face's complete `gasp` table is validated before native capture.
Single-bit grid fitting selects monochrome outlines and binary vector coverage.
Antialiased grid fitting selects normal outlines and grayscale coverage when the
original range requests both; a grid-fit-only range uses monochrome outlines and
binary coverage. The symmetric ClearType bits do not replace the ordinary
grid-fit/grayscale bits. Missing tables and non-grid-fit antialiasing ranges keep
the existing path; malformed admitted tables reject before any glyph publication.
Every run's table policy is checked before capturing any mixed-font draw.

The producer executes the explicitly selected TrueType35 interpreter. Original
Windows observations still contain 1/64-pixel outline differences; this choice
does not assert that interpreter or FreeType target is an exact Microsoft policy.
Monochrome dropout, contrast, GDI source quality, ClearType, system preferences
and the remaining frame/font contracts are independent unfinished work. GDI
`DrawingGdiText` never enters this GDI+ policy through a shared enum value.

Each actual immutable font has an owned native CPU context. A bounded cache keeps
at most 16 captured generations and charges at most 8 MiB per font, including
source bytes, copied keys and geometry. Oversized generations remain uncached.
Keys retain raw raster scale, native policy, exact device em and complete original
glyph/position arrays. Origin remains a recording parameter. Callers retain a
temporary use until the recorder acquires its independent native lease; eviction
cannot invalidate either an active caller or recorded paint. Font decoding and
hinting do not happen during GPU replay. Abandoned native CPU owners keep their
existing finalizers; no GPU finalizer, RenderLock or inferred current device is
introduced. Preparation/recording failures retain their original exception while
all temporary uses are released.

`ProGPU.Scene.Native` is a real Drawing dependency. The isolated producer package
group must include it and its complete native closure in source-reference order.
Source applications and Drawing tests import `LibreWinForms.NativeRuntime.targets`
only at the executable/test boundary. Stage real payloads from the exact successful
whole producer Build in `artifacts/progpu-native/package`, or explicitly select
`ProGpuNativeRuntimeRoot`. Portable builds keep the runtime directory layout;
RID builds place libraries alongside their corresponding NuGet native dependencies.
Notices are retained, SDK archives are excluded, and Package-mode consumers keep
ordinary NuGet resolution. No package-cache or system-library patch is used.

Seventeen local controls cover original placement, all admitted policy choices,
excluded source contracts, malformed-table rejection, mutable caller arrays,
warm retained reuse and real bitmap replay after cache eviction. The complete
Drawing suite passes 973 tests without skips using the qualified native producer
and ordinary loader resolution. Thirty-six real-payload asset evaluations cover
source discovery, explicit roots, trailing separators, executable/test boundaries,
Package-mode isolation and RID selection. These local controls do not replace the
unchanged 72-case original Windows pair, both provider/package checks or complete
Forms application qualification. Those remain required before #174 can close.

The four existing binary-coverage image checks now read each complete image with
one public read-only `LockBits` capture. Every original alpha/color/ink assertion
remains, with corner and actual-ink samples checked against `GetPixel`. The original
per-pixel run and the complete post-change run both pass all 973 cases; original
logs and TRX are retained. This removes 48,000 individual GPU readbacks from that
inspection path without changing paint, image dimensions, fonts or expected pixels.
