# Original byte premultiplied bitmap readback

The Drawing bitmap converted premultiplied channels with a rounded division.
Original Microsoft `GetPixel`, read-only ARGB `LockBits` and PNG decoding instead
agree with a truncated reciprocal over every valid byte channel/alpha pair.
New owned PArgb bitmaps also began with straight CPU storage, so writing a PArgb
lock could convert the original bytes before the first read.

The fix initializes premultiplied storage from the selected format and uses the
existing byte conversion boundary consistently:

```text
alpha == 0: 0
channel > alpha: existing saturation to 255
otherwise: (channel * floor((255 * 65536) / alpha)) >> 16
```

The admitted multiplication is bounded by `255 * 65536`; it cannot overflow an
integer. The expression is an independently derived equivalent over the complete
valid byte domain, not a claim about Microsoft's internal instruction sequence.
Other sufficiently precise truncated reciprocals can produce the same finite
domain. Neither rounding nor flooring only the final division is equivalent.
Invalid premultiplied channels retain the previous saturation policy; they were
not used as evidence for the original valid-byte contract.

No glyph positions, coverage shaders, contrast state, alpha blending, native
renderer, GPU readback or texture ownership is changed. Existing CPU pixel
construction and conversion remain CPU work: O(1) arithmetic per channel and
O(number of pixels) for the existing bulk APIs, without a new cache or dependency.
The source constructor retains its existing caller-buffer ownership behavior.
This establishes eight-bit conversion, not sixteen-bit precision or text parity.

## Independent original observation

[Original run 39120252639](https://github.com/wieslawsoltes/LibreWinForms/actions/runs/39120252639)
uses diagnostic commit `875e718d8cdd292f9971271ff7fa39b1a89a188f` on a disposable
Windows ARM64 runner. It runs only Microsoft System.Drawing, without desktop
windows or input. The original 45-second application and 60-second controller
bounds remain; observation completed in 0.7143571 seconds.

The 256 by 256 input visits all 32,896 valid pairs `(p, alpha)`, with row alpha
and column p. For p <= alpha its raw premultiplied RGBA is
`(p, alpha-p, (73*p)%(alpha+1), alpha)`; other columns retain zero RGB and row
alpha. Original raw storage is identical before and after every read.

All four independently checked straight-color paths agree: original GetPixel,
ARGB LockBits, PNG decoded by Microsoft, and the same PNG decoded independently.
The exact complete-frame RGBA hashes are:

| Bytes | SHA256 |
| --- | --- |
| Raw premultiplied source before/after | `22877e4eeeb51d77d2ec1c4201d93f69f1d6e86d71fece77f2111692d8ba79ce` |
| Every straight-color read | `3d2b9524f0ff9a5e19c468a4568511c96d9ee4e2e67404901ef18de6beb42d71` |

The old rounded division differs at 32,246 complete-frame pixels; final-only floor
division differs at 1,560. The specified reciprocal expression differs at zero.
Artifact `12133514505` has SHA256
`738a62bd592954373b1d73ffa00d6b769444c80746b32f4ddc66538676f2ea80`.
The original probe source and receipt are retained in `eng/OriginalBitmapReadback`.
The product tests derive inputs from the original observation specification and
compare against independent complete-frame hashes, including a final raw-storage
check. They do not generate expected colors using the product formula.

Both new regressions failed against the previous implementation and pass after
the change. The complete local Drawing suite passes all 975 tests with zero
skips. Current-head CI, original text/application comparisons and release package
validation remain separate gates.

To reproduce the Microsoft-only probe on Windows, copy its `Program.cs`, project
and `global.json` into a new directory outside the repository build hierarchy,
build Release, then invoke the resulting executable with a new output directory.
The hosted observation used the same isolated source copy. Do not substitute
the portable Drawing assembly into that reference build.

## Public contract and provenance

[Microsoft PixelFormat](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.imaging.pixelformat)
specifies premultiplied components for PArgb;
[Bitmap.LockBits](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.bitmap.lockbits)
provides the public format-specific memory boundary. Those documents do not
specify the byte rounding above. The numeric evidence comes from original public
API observations, with no foreign implementation code or fixture image used in
product behavior. Existing retained GPU paint and CPU readback separation stays
unchanged.
