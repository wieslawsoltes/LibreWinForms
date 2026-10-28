# Portable source scrollbar painting

Canonical ScrollBar previously retained its native UserPaint=false policy while
the portable UpdateScrollInfo path did nothing. Embedded DataGridView scrollbars
therefore had no source painter. The portable source now paints its actual
client frame, shared source range/page/value and independent arrow/thumb parts
through ScrollBarRenderer and the typed visual-style service. Native Windows
code and source scroll callbacks remain unchanged. Range/value publication
invalidates the portable control, including while disabled; size changes redraw.

The geometry is checked against all 96 original Microsoft WindowsDesktop
10.0.12 observations from [run 39037040757](https://github.com/wieslawsoltes/LibreWPF/actions/runs/39037040757).
The retained fixture is unmodified native GetScrollInfo/GetScrollBarInfo output,
covering both axes, RTL, enabled/disabled state, page zero/full, a nonzero minimum,
clamped values and tiny controls. The original archive SHA256 is
`6354eeaf4bb5c9504cfd54a163c67bb7c7c71e779e86552e4a62e081015836cd`.
The reference used owned windows on a disposable hosted runner without injected
input. It is diagnostic-only and does not assert package or pixel qualification.

Integer proportional sizing and nearest signed placement use the source values
and real system arrow/thumb metrics. Horizontal RTL retains source reflection;
vertical source direction remains independent. Tiny-track thumb metadata may
extend outside the track, so the painter retains its frame and clips it at the
actual track before painting the independent arrows. The original value is not
clamped or modified by painting. Graphics state and public Paint notification
remain caller-owned.

The portable baseline theme now distinguishes scroll tracks from thumb borders
and paints directional arrow glyphs. This remains its explicit managed theme,
not a claim of Windows theme pixel parity. Source hover/pressed state and pointer
thumb dragging are implemented in [pointer defaults](portable-scrollbar-pointer.md).
Held-button repeat and independent native gesture/theme qualification remain unfinished.
Original package grid captures and full CI remain required before release.

Local validation passes 99 source paint/publication cases, all 33 existing native
scroll input cases and all 20 visual-style cases, with zero skips. Both source
and theme projects compile. These controls cover source geometry and managed
theme behavior; they do not replace the pending whole-producer and original
package desktop checks.
