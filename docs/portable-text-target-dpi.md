# Portable text target DPI

The installed Windows paired popup baseline had equal 192-DPI, 1120x500 clients,
but portable labels, editor text and ComboBox text were roughly half-sized.
The Drawing recorder advertised 96 DPI even when its surface was in device
pixels. The canonical row-height path independently used the same 96-DPI
`Font.Height`, so changing painting alone would retain incorrect layout.

This change requires the additive ProGPU Drawing target-DPI overload. Silk
device-pixel recorders use actual window DPI; logical recorders remain at 96
with their original presentation transform. One retained frame shares its
resolution across all control layers; a resolution change repaints all layers.
Direct and adorner recorders use the same target. No source Font, driver scale,
font family, native coordinate mapping or input path is rewritten.

Canonical `FontHeight` keeps its cache but invalidates it when the owning target
DPI changes, including native-handle replacement. Before handle creation it uses
the declared source screen policy. ListBox row drawing/input geometry and the
editable ComboBox's real TextBox bounds use that same metric. Context-free
TextRenderer measurement receives an explicit source-screen Graphics; caller
Graphics remains authoritative. Default/logical and pixel-font behavior stays
unchanged. PMv2's existing source Font selection/scaling policy is not changed.

Focused source regressions cover pre-handle and live 192-DPI metrics, logical
96 despite presentation scaling, DPI/handle replacement cache invalidation, and
caller measurement DPI. Real backend recorded-command tests compare retained
frame/layer measurement and glyph sizes at 96 and 192. These are not native
appearance, installed-package, input or full popup parity qualification. The
original paired captures remain failure evidence; no new desktop run occurred.
