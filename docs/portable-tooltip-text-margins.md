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

## Windows result

The candidate at `a3dea95d92fc416bf0b95e5e9decf748888ae32c` changes only the
private Forms DLL over the same diagnostic payload. Both Microsoft and portable
apps complete all fourteen phases under the original sixty-second limit. The
portable final tooltip now visibly contains the entire "Popup interaction
tooltip" string in the same 289-by-48 window, captured at 42,677 ms. No popup
size, text, input sequence or renderer payload was changed to obtain that result.

The Forms DLL SHA-256 is
`2fb6a181ad3565025b46e42c0a23aa8077459b16f8483fe59951702a5f8eedaa`;
source tooltip SHA-256 is
`458a7e47538fd426996ceba26380170aa036ea4bbdf5121196c59246a83f4944`.
All original/candidate/SDK hashes were checked before and after execution, and
both child exits were observed. Original BMPs, inspected paired PNGs, source
snapshots and receipts are under `popup-tooltip-text.NsAdUb14`.

The thirteen selected tooltip source tests pass without skips. Broader source
selection also caught a new-test renderer-probe leak, so the probe now restores
the previous typed provider in `finally`, including on assertion failure.
The product fix remains just the two drawing flag changes. Different native
styling/placement and the older renderer's missing menu label remain open;
this result is not complete popup UI or released-package parity.
