# Text measurement must not clip to the proposed height

The actual Windows popup comparison exposed a clipped `File` menu label. At
192 DPI, Microsoft reported a 36-pixel menu item, while the portable source
reported 19 pixels. Both applications compiled the unchanged shared scenario.
The portable screenshot visibly cut off the text; this was not inferred from
a source-only layout test.

The shared `ProGpuTextRendererService.MeasureTextCore` passed the proposed height
to `Graphics.MeasureString` unless `SingleLine` was set. Canonical ToolStrip
layout does not require that flag. The resulting clipped measurement could be
cached and fed back into the source control's preferred size.

## Contract and change

[Microsoft's DrawText CALCRECT contract](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-drawtext)
extends the rectangle through the final line. Measurement now gives the existing
Drawing formatter unlimited height while retaining the original width and margin
calculation. No font, DPI policy, source text, source control sizing algorithm,
renderer default, draw clipping or trimming policy is changed. Ordinary drawing
still owns its finite viewport; this change is only the measurement boundary.

An independent Windows ARM64 `DrawTextExW` probe used a real memory DC and
24-pixel Segoe UI font, with all resources restored/released. It measured three
texts at two widths and five heights for six flag combinations (180 calls).
The 150 non-SingleLine cases, including the menu's center/vertical-center/hidden-
prefix flags, retained identical measurements across heights. The remaining
30 SingleLine+VerticalCenter calls retained native height-dependent alignment
results. Those raw results are preserved, not described as height invariant.
This change preserves the existing portable SingleLine measurement behavior;
it does not establish parity for that separate finite-height alignment contract.

## Evidence

- All five new regression tests failed on the original implementation. The
  failures include the menu flags, multiple lines and wrapping at short heights.
- The five new tests and the existing margin, single-line drawing and text-service
  cases pass together: **59 passed, zero failed/skipped**, .NET 10 ARM64.
  The build has zero warnings/errors. CI retains all 34 original height cases
  and adds these five, raising the explicit selected minimum to 39 without
  changing the deadline or skip policy.
- The unchanged Windows scenario was then run against a private diagnostic copy
  containing the rebuilt Forms backend and ProGPU219's Backend/Vector/Scene
  assemblies. All other output bytes, the original package consumer and the SDK
  were checked before/after. Its actual menu item is now 36 pixels high, and the
  retained baseline screenshot displays the complete `File` label.

Evidence is in `popup-current-macos.1qX16xPY`: `native-measure-height.json`,
`measurement-{baseline,final}-tests.log`, and `measurement-windows-evidence`.
The original successful Forms109 package consumer, failed run and screenshots
remain unchanged. Its Microsoft application completed all fourteen input/state
phases, but the first baseline image was blank; later context images show the
native UI. Phase completion is not a usable-image or pixel-parity certificate.

The private diagnostic copy is **not installed-package qualification**. Both
the original package and the diagnostic copy still exceeded the unchanged
60-second scenario limit after the context menu opened and began painting.
That separate responsiveness failure, full popup interaction, Linux/macOS
desktop behavior, theme/width differences and final package gates remain open.
