# GDI requested-quality transport

Canonical portable `TextRenderer` can now pass an explicit GDI font-quality
request to services implementing `ILibreGdiTextRendererService`. The request uses
the existing `FontQualityFromTextRenderingHint` mapping. It is not a mapping from
GDI quality to GDI+ coverage, TrueType interpreter, dropout or RGB filtering.
Legacy services receive their original calls, without extra settings reads.

`DrawingGdiTextPolicy` keeps requested quality separate from a nullable raw
`DrawingGdiSystemSettings` observation. Canonical calls read the currently
registered settings service's enabled/type/contrast values without normalization.
Unavailable settings and the generic compatibility-default service stay null;
orientation stays unknown because the current
settings interface does not supply it. No historical captured value becomes a
runtime default. Undefined requested quality rejects before source/font work.

Immediate calls capture the request and settings at that draw. The optional
`ILibreGdiTextLayoutService` captures an explicit requested quality with the
retained source/font identity: subsequent caller Graphics mutations cannot
silently replace that request. Its settings callback is invoked at actual paint,
not during layout, measurement, interaction or disposal. Disposal clears the
callback and rejects later drawing before invoking it. `DrawingTextLayout` also
offers a typed paint overload for current observations; ordinary layouts cannot
silently acquire a GDI request through that overload. Old creation/draw overloads
retain their existing behavior and source identity.
The new typed DrawingGdiText overloads require the extra final policy/quality
argument after metrics, preserving old explicit `default` argument binding.

The policy reaches the Drawing glyph paint boundary without changing layout,
glyph positions, measurements, caller Graphics state, source em realization,
coverage, native outline admission or pixel transfer. Measurement receives the
typed source request with unknown system settings, without reading the settings
service, and retains the existing layout algorithm. The new transport
does not enable GDI native hinting or fix the existing generic grayscale paint.
Original GDI quality/system-aware rasterization and full original pixels remain
unfinished in #174. GDI+ SystemDefault/ClearType and ordinary SingleBitPerPixel
remain independent work; no renderer or package qualification follows here.

Prepared controls cover all six canonical mappings, custom legacy dispatch with
settings access forbidden, immutable requested identity, raw/unknown settings,
invalid request rejection, retained font lifetime, paint-time callback timing and
post-disposal rejection. Current validation status is recorded separately; these
controls do not substitute for the original72 reference or source applications.
