# Original Microsoft text raster observations

Independent no-input reference for LibreWinForms issue174. This probes the public
Microsoft APIs rather than copying either implementation. It creates no desktop
window and changes no system settings. It runs only on a disposable GitHub Windows
runner, with a45-second inner budget,60-second process deadline and16MiB PNG cap.

The complete72-case inventory crosses Microsoft Sans Serif9.75pt and Segoe UI9pt,
all six TextRenderingHint values, contrast0/4/12 and GDI TextRenderer versus GDI+
DrawString on original white384x64,96DPI ARGB bitmaps. Their independent layout
contracts remain unchanged; this does not assert that the two APIs share baselines
or glyph positions. Every full image is retained, together with decoded RGBA hashes,
ink extents, intermediate/color-channel counts, original font-byte identities and
read-only system smoothing settings. Font bytes themselves are not published.

The references consulted describe public behavior only:
- https://learn.microsoft.com/dotnet/api/system.drawing.text.textrenderinghint
- https://learn.microsoft.com/dotnet/api/system.drawing.graphics.textcontrast
- https://learn.microsoft.com/dotnet/api/system.windows.forms.textrenderer

The current portable Graphics writer ignores requested hint/contrast when recording
ordinary and prepared glyphs. Observations here distinguish source API behavior
before implementing its typed raster policy. They do not qualify current portable
rendering, application captures, source grid pixels, native input or any package.
No product baseline is replaced and qualified remains false.

This diagnostic branch replaces the existing manual theme workflow only to run
this isolated inventory. Do not merge that replacement workflow into the product.
