# Original integer line coverage

Portable unsmoothed one-pixel grid borders previously used the generic retained
stroke and covered two pixel rows partially. Original Microsoft GDI+ evidence
shows that solid integer horizontal and vertical lines with Default pixel offset
cover one row or column completely, including both integer endpoints.

`Graphics.DrawLine` now records that exact coverage as an integer rectangle when
the actual pen is opaque solid, one pixel wide, centered, undashed and flat at
both ends; smoothing is None, pixel offset is Default, actual target DPI is96
on both axes, and the complete world/page/container/host transform has identity
linear terms and integer translation. Source endpoints must be finite integers
on a nonempty axis line. Original source clip, brush and transform remain owned
by the existing drawing context. The far edge, span and translated edges must
remain exactly representable; no epsilon, rounded endpoint or overflow repair
is admitted. Widened/custom/compound/textured pens retain their existing path.
EMF/WMF GDI LineTo retains its independent current-position geometry through an
explicit internal entry; GDI+ inclusive endpoint evidence does not admit a change
to the GDI replay contract. The unchanged four metafile current-position controls
detected this distinction in the first full-suite run; their original failure
and EventPipe evidence are retained, without changing their assertions.

The independent no-input Microsoft reference is run39063499450 at
6a7a11adf18721162833655e3d1a6d4e99f57ab3. Artifact12111469345 has SHA256
`b8afac29a45ecd53d2dbeed3b7508f60c3742b22423750a7b23ec205443c3b2f`.
It records48 complete32×32 frames, all original inputs and PNG hashes. The
original source differs from the artifact only in checkout line endings.
Fixtures under `System.Drawing.Common.Tests/Fixtures/OriginalLines` are test
resources only; product code uses no reference textures.

Eight Default/None horizontal, reversed, vertical and device-clip frames failed
before the fix and match every pixel afterward. Six independently matching
wider/scaled frames remain exact. Guards retain original stroke commands and
endpoints for unqualified antialiasing, Half offset, width, caps, alignment,
dash, translucent/gradient paint, scaling, fractional translation/endpoints,
other DPI, diagonal/empty input and unrepresentable edges/translation.

The other34 original frames remain separate unfinished sampling contracts.
In particular, antialiasing, Half offsets, fractional placement and diagonals
cannot use this endpoint policy indiscriminately. The full original package
grid capture, complete producer CI and downstream WPF consumer qualification
remain required before release. This source change alone does not qualify
complete GDI+ rendering or native desktop appearance.
