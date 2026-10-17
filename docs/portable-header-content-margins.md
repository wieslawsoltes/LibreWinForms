# Portable header content margins

Original grid run39034927655 passes all four input phases and exact layout, but
its portable header labels are clipped. The ProGPU visual-style provider assigns
every class three pixels on every content edge. DataGridView retains its original
23-pixel header and subtracts those margins, border widths and text margins;
changing font autoscaling or sample dimensions would hide this separate defect.

Canonical `DataGridViewHeaderCell.GetThemeMargins` explicitly selects Aero header
margins (left2, top1, right0, bottom2). The portable provider now selects that same
content policy for HEADER part1, preserving state and class-name case behavior.
Its content rectangle, content margins and inverse extent agree. Tiny content
rectangles remain nonnegative and within the original allocation. Other header
parts, other classes, sizing margins, paint colors/borders and source text
alignment remain unchanged. In particular, canonical TextExtensions deliberately
supports multiline vertical alignment; raw DrawTextEx limitations do not remove
that source behavior.

Eight focused provider cases cover original source margins, inverse extent,
small allocations and independent sibling parts. These controls are not original
Windows pixel qualification. Fresh whole-package grid execution and capture
review remain required, and missing scrollbar painting remains open in #174.
The local diagnostic used an explicitly reported Microsoft Sans Serif fallback
and a headless two-pixel theme; it is not original Segoe UI evidence. All prior
captures and failed diagnostic builds remain retained.
