# Portable scrollbar pointer defaults

Portable source ScrollBar now handles a left press on either arrow or track and
tracks thumb drags through the canonical Control input dispatcher. These defaults
run before virtual/public MouseDown and MouseMove notifications. Calling protected
mouse notifications directly does not simulate input. Native Windows retains its
original window procedure.

Paint and hit selection share the existing per-axis DPI, range, page and RTL
geometry. The original 96-state Microsoft geometry fixture remains unchanged.
Arrows use SmallChange, track presses use LargeChange, and thumb movement clamps
to the source scrollable range. Scroll callbacks retain their OldValue/NewValue
contract; a handler's NewValue is authoritative. Thumb release emits
ThumbPosition followed by EndScroll; an arrow/track release emits EndScroll.
These event kinds retain the [WM_HSCROLL contract](https://learn.microsoft.com/en-us/windows/win32/controls/wm-hscroll); exact physical-gesture qualification is separate.

Each press retains the actual source/target handle, press/capture generations,
source range/value identity, parent, bounds and DPI. Source mutation, resize,
relocation, capture loss, disable/hide or replacement input cannot resume an old
press. Default value publication consumes its own generation token before public
callbacks. Reentrant input owns its replacement; callback failures keep their
original exception and retire only the old operation.

This change covers initial arrow/track actions and thumb tracking. Held-button
repeat, pointer hover/pressed theme paint, original Microsoft gesture comparison
and complete package desktop validation remain required work in #174. These
source controls do not establish native gesture or theme parity. No release pin
or qualification exception is implied.
