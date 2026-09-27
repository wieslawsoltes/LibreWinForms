# Portable font autoscaling uses one reference DPI

The original DataGridView sample in issue #6 exposed a mixed-unit source layout
on Ubuntu ARM64 at 192 DPI. The wholly successful PR #94 Build
`36317923429` and Docs `36317923453` supplied the unchanged source-first packages.
The actual source/native client was 1374 by 515, with font autoscale dimensions
13 by 15 for Noto Sans 8.25. The owned X11 client capture independently showed a
wide, shallow form. This is not a compositor/chrome or Windows pixel comparison.

`ContainerControl` measured the average character width through portable
`TextRenderer` at the canonical screen-reference DPI, but used `Font.Height`
for the vertical metric. Portable Drawing's parameterless height uses 96 DPI.
The resulting unequal scaling affected `AutoScaleMode.Font` containers, not
only DataGridView or this sample.

Both dimensions now use the existing canonical font reference policy. The
vertical metric shares `Control.GetFontHeightForTarget`: measure the selected
source font at `ScaleHelper.InitialSystemDpi`, then round the final line metric
up. Do not multiply by the live monitor DPI again, modify source font size,
or substitute the measurement string's ink height. The control's protected
`FontHeight` cache can be overridden by derived controls and is deliberately
not the source of autoscale dimensions. Width measurement and its original
integer rounding, source font ownership, autoscale cache invalidation and the
native Win32 autoscale branch are unchanged. The helper remains non-public.

Six fresh-process canonical cases cover 192-DPI and fractional 144-DPI point
fonts, 96-DPI logical coordinates, pixel fonts, a derived line-metric override,
and live font replacement (after the canonical handle-dependent invalidation
boundary). Against the unchanged implementation, the four device-DPI
cases fail and the logical/pixel controls pass. The full source gate retains
every prior case and increases its minimum from 675 to 681.

Local macOS ARM64 validation passes all six focused cases and the complete
681-case suite with zero skips (61.450 seconds). The canonical rebuild has
632 existing warnings and zero errors; the final test-project rebuild has zero
warnings and errors. Documentation and shell/whitespace checks pass.

Fresh exact-head CI and an updated native package run remain required. This
change does not qualify all sample dimensions, explicit grid row/column sizes,
native chrome, masked editors or rendering parity. The original sample's Enter
failure remains separate: the latest native diagnostic stopped before typing
when the environment reasserted Num Lock after focus. Its strict key-state
failure, original root-capture failure, client image and package receipts are
retained; no input check was waived and no failed producer package was staged.
