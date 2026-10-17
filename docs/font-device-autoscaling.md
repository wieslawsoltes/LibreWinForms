# Font-owned device heights for portable autoscaling

Original grid run 39025614225 reached portable DXC startup within 20 seconds, then
failed the unchanged layout comparison. Segoe UI 9pt at 96 DPI had vertical
AutoScaleDimensions 16 instead of Microsoft's 15, expanding the client from 515 to 549
and the grid from 345 to 368. Both applications reported Font.Height 16. The source
ContainerControl contract uses GDI's selected device height, independently of
Drawing line spacing.

The shared producer's exact VDMX reader preserves original font-owned records.
Windows diagnostic 39026793093 contains 45 original GDI observations. All 30
Segoe UI/Arial records match their actual VDMX height, ascent and descent;
Consolas has no table. Original font/table hashes and failures remain retained.
There is no font-name or sample-size special case and no rounding of line height.

`DrawingFontMetrics.TryGetAutoScaleHeight` explicitly admits static TrueType,
nonsymbol, ANSI/default point fonts with an exact square-pixel VDMX record. It
uses the source FontCache's original requested-em ceiling at the canonical screen
reference DPI. It does not mutate fonts, metrics or placement. Pixel units,
vertical fonts, other charsets, variable fonts and absent table/size records keep
the previous unavailable-metadata policy. Malformed or duplicate table records
fail and cannot select a fallback. General missing-table GDI parity remains open.

The optional typed `ILibreFontAutoScaleMetrics` capability carries that result
from the actual text provider to canonical ContainerControl. Original alphabet
width measurement and rounding, reference DPI, font-change cache invalidation,
source font identity and the native Windows branch are preserved. Unavailable
providers retain `GetFontHeightForTarget`, including pixel-font behavior and
independence from a derived control's FontHeight override. A provider exception
or invalid returned height is not permission to use line spacing.

The consumer pins ProGPU `cc1b1a754fd65370e1b419f370bec479347adce8`, whose
Build `39027481242` passed all 59 jobs and Docs `39027481259` passed. No
capability is claimed on the previous producer package. Six Drawing controls and three canonical source cases
cover exact records/em sizes, source DPI/font identity, cache reuse, ordinary
line-height preservation, missing data/pixel fonts and failure propagation.
These are source controls, not the final original paired grid acceptance. Whole
producer CI, updated consumers, original input/captures and image review remain
required before release.
