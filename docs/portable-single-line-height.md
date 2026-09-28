# Single-line text in short source viewports

The original DataGridView sample uses explicit 21-pixel rows even when its
realized font has a 32-pixel line metric. Microsoft still paints the clipped cell
text. Portable source editing and Enter commit succeeded, but the committed cell
was blank: its EndEllipsis format reached Drawing with a shorter height than a
full line, and vertical trimming removed the entire string. The editor's retained
text path did not have that failure.

`ProGpuTextRendererService` now gives a SingleLine draw at least its actual font
line height, retaining top/center/bottom positioning relative to the original
viewport. The original rectangle still owns background and clipping; NoClipping
still preserves the caller's existing clip. Width and asymmetric margins remain
unchanged, so horizontal character/word/path ellipsis still applies. Single-line
measurement uses an unconstrained height, matching DrawText's CALCRECT behavior.
Multiline layout, generic Drawing trimming, retained editor shaping, source Font,
row/column sizes and DPI are unchanged.

The platform contract is documented by
[Microsoft DrawText](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-drawtext):
clipping does not replace an oversized font, single-line vertical alignment uses
the caller rectangle, and CALCRECT extends the rectangle to bound the text.
This change does not claim exact GDI TEXTMETRIC/raster parity for every face.

The focused gate compares every pixel with an independently drawn full-height
line cropped to the original viewport: top/center/bottom, three padding policies,
and untrimmed/character/word ellipsis. Additional cases retain horizontal
character/word/path trimming and unconstrained single-line measurement. The
original 27 pixel cases produced 25 failures and two passing controls on the
unchanged renderer; no tolerance, font size or source geometry was adjusted.

On macOS ARM64, all 34 focused cases pass with zero skips using the actual
renderer (.NET 10.0.9, SDK 11 preview 5; build zero warnings/errors). Reading each
complete GPU bitmap once through read-only LockBits retains every exact pixel
comparison and reduces this test run from 147 seconds to 672 milliseconds;
this is test readback cost, not an application rendering-performance claim.

Actual Windows application pixels, native header themes, masked editors,
other platforms and the separate cold-start deadline remain release gates.
