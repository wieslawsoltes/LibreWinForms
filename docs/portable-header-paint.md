# Portable header background paint

HEADER part1 states1–3 now use their independently observed Aero background:
normal is opaque white with one gray right-edge separator; hot and pressed use
their complete flat state fills without a surrounding outline. This replaces
the generic button border that appeared around column headers and, after the
canonical row-header rotation, around row headers. Other theme parts retain
their existing renderer. The source grid still owns row/column borders.

The original Microsoft reference is [run39052232473](https://github.com/wieslawsoltes/LibreWinForms/actions/runs/39052232473),
artifact12108405321, archive SHA256
`f69281c14d126b4bb5f3cde085f54b8706a19ebe511eeb1ee49c031e0213f4f5`.
It used Windows11 build26200, Aero NormalColor/NormalSize, explicit96DPI and
Microsoft WindowsDesktop10.0.12 on runtime10.0.13. Twelve byte-preserved header
PNGs cover all three states, two backgrounds and both100×23 and11×7 allocations.
They are test fixtures only; product paint uses the existing managed Graphics
fill path, with no native rasterization or captured-image fallback.

All13 focused controls pass: twelve complete RGBA comparisons against those
original images, plus translated, intersected-clip and caller-state restoration.
The native query reports zero raw header content margins; the canonical
DataGridView Aero content override remains independently `(2,1,0,2)`. Do not
change that source layout policy, fonts, row heights or original sample bounds
to compensate for a paint difference.

The first reference run39051901038 stopped after six images when Windows rejected
HEADER part2. Its original failure is retained. The completed reference records
all120 requests,108 rendered images and12 explicit undefined-part outcomes.
Missing parts are not substitutes or successful pixel comparisons. These
offscreen controls do not establish another theme/DPI, whole-package grid pixels
or native pointer parity. Original paired application acceptance and complete
producer CI remain required for release; #174 tracks the remaining work.
