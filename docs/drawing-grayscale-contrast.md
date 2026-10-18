# GDI+ grayscale text contrast

The Drawing adapter now selects explicit source coverage transfer for ordinary
GDI+ AntiAlias and AntiAliasGridFit paint. Contrast 0 through 12 is captured at
each draw; retained layouts keep their original font/glyph arrays and read the
current Graphics paint state. Native prepared outlines receive the transfer
only when their actual selected coverage is grayscale. GDI TextRenderer,
SystemDefault, ClearType and binary modes retain independent policy.

The consumer pins the matching producer from
[ProGPU PR397](https://github.com/wieslawsoltes/ProGPU/pull/397), commit
`3f4581cb7ebd22de61b82764a4b14875ba426533`. Its explicit metadata survives
recording, archive, managed/native paint and vector fallback. All 76 producer
checks passed, including every one of the 61 jobs in
[Build39135173910](https://github.com/wieslawsoltes/ProGPU/actions/runs/39135173910).
Source validation stages the real native, SDK, DX12 and Xlib payloads from that
exact build. The source consumer has 15 passing coverage controls; the consumer's
own whole Build and original package comparison remain independent gates.
These controls do not establish original Microsoft images or qualify the other
raster families.

The coordinated source graph subsequently advances to
`905d52ac185278db60c312a709cf5aa045772dd5` from
[ProGPU PR398](https://github.com/wieslawsoltes/ProGPU/pull/398). All 61 jobs in
[Build39138606765](https://github.com/wieslawsoltes/ProGPU/actions/runs/39138606765)
and all 76 head checks pass. This descendant retains the grayscale implementation
and adds independent native Close enabled state required by LibreWPF. Forms and
WPF must pin the same exact producer for canonical source integration; neither
consumer may substitute an older nested producer. Source validation uses this
commit's real staged Native/SDK, DX12 and Xlib payloads.

The preceding Forms `96fab7db3d064b1d8b491093b3c3d13155df2ea7` candidate passes
all ten jobs in Build39140416185 and all twelve checks. Its 19-package closure,
source provenance and native payloads are verified. These results retain their
original commit identity; the coordinated pin update requires fresh consumer CI.

The complete Drawing source suite passes all 993 tests without skips using this
exact producer and staged payload. The earlier source-only attempt had ten
missing-native-library failures; its results remain retained separately. No old
native binary or skipped test was used to satisfy the corrected source run.

The earlier 1a90/ProGPU9f package pair completed all 72 original cases in hosted
run39133925801. The six ordinary SingleBitPerPixel cases improved from177 to26
pixels per Microsoft Sans Serif frame and from75 to16 per Segoe UI frame.
Strict equality remains0/72. Of the other66 candidate frames,63 are byte-identical
and three Segoe UI AntiAliasGridFit frames changed bytes with unchanged differing-
pixel counts; these three remain unattributed. This pair does
not include the new contrast implementation. Issue174 retains the remaining
binary coverage, hinting, ClearType, GDI quality and application acceptance.
