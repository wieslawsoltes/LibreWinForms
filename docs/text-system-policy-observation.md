# Current text-system observations

GDI's requested font quality and GDI+'s `TextRenderingHint` are different source
contracts. The existing raster selection remains unchanged by this transport.
In particular, no system setting aliases Proof/Draft to GDI+ single-bit coverage,
or ClearType/SystemDefault to a newly admitted grayscale or RGB policy.

## Raw observations and availability

`DrawingTextSystemSettings` retains four independent nullable `uint` values:
`Enabled`, `Type`, `Contrast`, and `Orientation`. Null means that particular query
was unavailable; a missing snapshot means the provider supplied no observations.
No historical value, RGB orientation, or compatibility default fills an absent
query. These sequential reads are not an atomic OS settings transaction.

The Drawing-owned Windows reader calls the documented read-only
[`SystemParametersInfoW`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow)
actions `SPI_GETFONTSMOOTHING` (0x004A), `SPI_GETFONTSMOOTHINGTYPE` (0x200A),
`SPI_GETFONTSMOOTHINGCONTRAST` (0x200C), and
`SPI_GETFONTSMOOTHINGORIENTATION` (0x2012), once each. Every successful DWORD,
including the original BOOL bits, survives without clamping. Failed queries
remain independent. Other operating systems return no native snapshot. The
reader neither writes system settings nor retains native handles.

## Paint and retained ownership

Ordinary GDI+ text captures an immutable `DrawingGdiPlusTextPolicy` at its actual
paint entry. SystemDefault and ClearTypeGridFit observe current settings; other
hints do not read them. Capture occurs before drawing clips or glyph commands,
and before sampling the target transform. Measurement and retained layout
creation do not query settings. A retained paragraph observes once when painted,
not once per glyph, run, or later picture replay. The captured hint and raw
TextContrast continue to select only their original admitted behavior.

`DrawingTextSystemPolicy.Bind` explicitly borrows an observation callback for one
Graphics target, including `Graphics.FromImage`. Null removes the override; an
installed callback may return null to express unknown state. Dispose clears the
callback. Callback exceptions remain the original exceptions and publish no
paint clips or commands. Disposed Graphics, provider replacement, hint/contrast
mutation, or recursive policy capture reject before publication. A permitted
transform change is sampled after the observation. Callbacks are observation
providers, not a facility for issuing unrelated drawing commands.

Canonical GDI uses the optional `ILibreTextSystemSettingsService` capability when
present. The built-in compatibility service obtains actual Drawing-owned Windows
observations rather than its invented compatibility property values. Existing
custom services without the capability retain the same three property reads and
unknown orientation. Untyped text-renderer services get no additional settings
reads. GDI measurement carries requested quality without current settings.

`DrawingGdiTextPolicy.FromSystemSnapshot` preserves raw per-query availability.
The original constructor and `DrawingGdiSystemSettings` remain compatible; the
legacy view is available only when the enabled/type/contrast values exist. The
original signed integers preserve their bits in the raw view.

The additive retained snapshot interface captures requested GDI quality with the
source/font generation and samples current settings only when drawn. Disposing
the original Font or changing the receiving Graphics hint does not replace that
request. Recursive paint and callback-driven layout retirement reject before
glyph publication; disposal releases the borrowed callback. The existing legacy
retained interface and overload behavior remain unchanged.

## Validation boundary

Focused source controls exercise raw words, independent failed queries, optional
canonical dispatch, absence of measurement reads, retained ownership, provider
retirement/reentrancy, original callback exceptions, and clips/transform preflight.
They preserve the qualified producer pin and do not execute pending native raster
changes. The original Microsoft font-policy captures, both provider/package
checks and final source application pixels remain separate unfinished acceptance.
This transport alone does not resolve LibreWinForms #174.
