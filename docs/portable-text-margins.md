# Portable TextRenderer margins

The Windows original-grid comparison exposed portable labels starting left of
their Microsoft reference. The portable renderer omitted default overhang
padding and used a fixed one-pixel inset on each side for LeftAndRightPadding.
Measurement also added that fixed padding after wrapping at the full width.

The existing upstream WinForms `TextExtensions.GetTextMargins` calculation now
lives in one linked `Common/src/TextRendererMargins.cs` file. Native Forms still
supplies its original `FontCache.Data.Height`; its rounding and unknown-padding
behavior are unchanged. The portable backend supplies the rounded line height
of its actual realized Drawing font at the caller target DPI. Canonical
TextRenderer continues to realize source fonts against InitialSystemDpi first;
this change does not rescale fonts, replace their family, or modify source text.

Default padding is ceil(height/6) on the left and ceil(height/6 * 1.5) on the
right. LeftAndRightPadding uses multipliers 2 and 2.5, including when NoPadding
is also set. NoPadding alone has no margins and retains the editor layout path.
The italic allowance applies to all faces, as in canonical WinForms.

Drawing aligns and wraps in the inset content rectangle, but clips to the
original caller bounds so italic glyphs can use the reserved overhang area.
NoClipping preserves the caller Graphics clip without adding a text-bounds clip.
Graphics clip/transform state is restored. Background painting keeps the original
bounds. An exhausted content rectangle cannot become an unbounded point draw.
Measurement subtracts both margins before wrapping and adds them back once;
constrained widths retain at least one content pixel, including nonpositive input.
Empty text returns zero without acquiring margins.

Focused gates cover independent literal rounding/precedence cases, actual
rendered pixels with left/center/right/RTL alignment and italic faces, wrapping,
pixel/point fonts at 96/192 DPI, exhausted rectangles, empty input, and Graphics
state restoration. These are separate from full canonical source tests.

This fixes the missing padding contract, not every GDI metric difference. A
Drawing line-height metric is not claimed identical to GDI TEXTMETRIC for every
font and size. Complete Microsoft/portable sample pixel parity, native header
themes, input, and installed-package desktop qualification remain required.
