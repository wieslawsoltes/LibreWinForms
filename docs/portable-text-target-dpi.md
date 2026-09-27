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

## Local source evidence

Actual source head `9f1657f4f0717fe6f88e2486574bf9c282ed181b` against ProGPU
`22f83ea5529e0d975a3b9a48b17371f79e6f003d`: canonical full suite **538 passed,
zero failed/skipped** (18.174s), including five additive target-DPI facts. Real
backend retained-frame/layer cases **2 passed, zero failed/skipped** (313ms).
Builds used SDK11 preview5 targeting net10.0 and tests used .NET10.0.5 ARM64.
Canonical build: zero errors,630 existing source warnings; backend build:0/0.

Evidence is retained under the task worktree's `artifacts/text-target-dpi`.
`canonical-final-tests.log` SHA256:
`4953fed1592c848c23176d20727dcebf74b5cac15f66561237cc29241403a208`.
`backend-dpi-tests.log` SHA256:
`dd60ac95611cea0ed2bb34727024259602b9bc0d7b5a9f090826aafd0c72f96f`.
The initial537-case result374pass/163fail is retained: its headless fixture used
Graphics nullability to classify the literal `managed`/`headless` sentinel text.
It now asserts explicit Graphics while retaining those text, flags and sizes;
the intervening corrected537/537 result is retained too. Two strict formatting
build failures were corrected; no analyzer, case, timeout or assertion was waived.
