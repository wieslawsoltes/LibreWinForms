# Portable tooltip text margins

The portable tooltip owns explicit DPI-scaled outer padding and measures its
body and optional title with `NoPadding`. Drawing must use the same text-margin
policy. Previously both draw calls omitted that flag; their extra text margins
reduced the body width and wrapped the final word into a row beyond the measured
height. The title also acquired margins absent from its measurement.

Keep `NoPadding` on both default draw calls. Retain outer padding, title/icon
layout, body wrapping, source font/DPI, owner-draw behavior and the caller's
`PopupEventArgs.ToolTipSize`. Do not enlarge a popup or relax clipping to conceal
the measurement/drawing disagreement. Native Windows behavior is unchanged.

Four source regressions use the real ProGPU text service to cover body/title,
default fit and caller-sized popups. They assert matching margin flags and that
unmodified measured bounds do not introduce another row at drawing time. All
four fail against the old source. The source CI gate explicitly selects them
with the existing ten-minute deadline and rejects skips.

The independent Windows desktop evidence before the fix is
`popup-fresh-tooltip.gHVQzN9g`: native Microsoft shows "Popup interaction tooltip"
while the portable popup shows only "Popup interaction". Both original apps
completed the fourteen-step input sequence under the original sixty-second
deadline. The strengthened tooltip capture requires a new Popup event and a
newly visible owned native window, not an earlier event or the owner's shadow.
This is a private diagnostic payload, not newly staged package qualification.
Final native appearance and cross-platform/package qualification remain separate.
