# GDI+ grayscale text contrast

The Drawing adapter now selects explicit source coverage transfer for ordinary
GDI+ AntiAlias and AntiAliasGridFit paint. Contrast 0 through 12 is captured at
each draw; retained layouts keep their original font/glyph arrays and read the
current Graphics paint state. Native prepared outlines receive the transfer
only when their actual selected coverage is grayscale. GDI TextRenderer,
SystemDefault, ClearType and binary modes retain independent policy.

The matching producer is ProGPU PR397. Its explicit metadata survives recording,
archive, managed/native paint and vector fallback. The source consumer has 15
passing coverage controls. Its public-package/native qualification and producer
pin are still pending the complete successful producer Build. These tests do
not establish original Microsoft images or qualify the other raster families.

The earlier 1a90/ProGPU9f package pair completed all 72 original cases in hosted
run39133925801. The six ordinary SingleBitPerPixel cases improved from177 to26
pixels per Microsoft Sans Serif frame and from75 to16 per Segoe UI frame.
Strict equality remains0/72. Of the other66 candidate frames,63 are byte-identical
and three Segoe UI AntiAliasGridFit frames changed bytes with unchanged differing-
pixel counts; these three remain unattributed. This pair does
not include the new contrast implementation. Issue174 retains the remaining
binary coverage, hinting, ClearType, GDI quality and application acceptance.
