# Portable PropertyGrid font metrics and link defaults

PropertyGrid's row, help and command panes now use the shared
`Control.GetFontHeightForTarget(Font)` calculation. Portable Drawing's
parameterless `Font.Height` measures at 96 DPI, whereas canonical control text
uses `ScaleHelper.InitialSystemDpi`. The shared calculation rounds the final line
height at that reference DPI, keeps pixel fonts in pixels and preserves the
native Windows `Font.Height` branch.

The change retains the original row padding, both help-pane padding policies,
the command pane's integer rounding and verb-count calculation, and their
existing cache invalidation. Source fonts are neither replaced nor scaled again.
PropertyGrid continues to arrange its own panes through its canonical resize
path; a font-change regression explicitly resizes the grid before asserting
the rearranged command height.

Five fresh-process cases use the actual public PropertyGrid, selected property,
source editor and labels, plus typed designer verbs. They cover 192 and
fractional 144 DPI, logical 96 DPI, pixel fonts and font replacement followed by
resize. Before the metric fix, the 192-DPI editor was 15 pixels high instead of
29, the help title was 18 instead of 32 and the two-verb command pane was 50
instead of 92. The logical-coordinate and pixel-font controls already passed.

The font-change paint path also exposed an enabled-link crash on macOS: the
canonical Internet Explorer settings lookup dereferenced an unavailable registry
root. On non-Windows portable hosts, that lookup now returns no settings key,
reusing the existing missing-key colors (blue/red/purple) and underline policy.
Both portable and native Windows retain the original registry reads, color
parser and security handling. Explicit link colors and behavior, dark-mode and
high-contrast selection remain unchanged.

Four additional fresh-process cases cover public default colors, enabled and
visited link painting with SystemDefault behavior, and explicit colors with
NeverUnderline. Their Windows expectations read the real settings without
modifying the registry. Before the fix, three failed on the absent registry
root; the explicit-style control passed.

After both corrections, all nine focused cases and all 696 canonical lifecycle
tests passed on macOS ARM64, with zero skips. The source-first CI minimum is
raised from 687 to 696. The full source build and documentation verifier also
passed. Hosted CI and native Windows/Linux application qualification remain
separate gates.

These source regressions do not qualify native desktop pixels, popup placement,
physical input, Windows theme parity or complete application/package behavior.
No test deadline or existing assertion is relaxed.
