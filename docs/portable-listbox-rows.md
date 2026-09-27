# Canonical portable ListBox rows

The original `System.Windows.Forms.ListBox` now owns normal, single-selection,
single-column row painting and input on the portable control tree. A ComboBox
popup can host this actual control through `ToolStripControlHost`; it does not
need a replacement list-shaped widget or a separate selection model.

## Source contract

- The original `Items`, `SelectedItems`, `SelectedIndices`, `SelectedIndex`,
  formatting and selection-event paths remain authoritative. Normal row height
  comes from the actual `Font.Height`. `TextRenderer` receives original formatted
  source text, including Unicode, ampersands and the requested RTL/tab policy.
- One source viewport/row mapping supplies paint rectangles, `GetItemRectangle`,
  `IndexFromPoint`, page movement and `TopIndex`. The viewport uses the existing
  canonical border painter. Row drawing intersects both caller and control clips
  and restores the caller graphics state before the public Paint event.
  `GetItemRectangle` retains the full valid row rectangle above/below the visible
  viewport, including negative Y; visibility clips painting and input, not the
  public geometry result.
- Typed left-pointer down changes selection before MouseDown; the subsequent
  canonical MouseUp stays available to a hosting ComboBox for acceptance. A
  selection callback that disposes or recreates the source handle cannot receive
  a late MouseDown from that old generation.
- Up/Down/Home/End/PageUp/PageDown run as default key operations only after the
  existing filter, preprocessing and managed KeyDown path. They use the public
  selection setter and reveal the selected row. Enter/Escape are not accepted or
  canceled by ListBox: the hosting control owns those decisions.
- Wheel scrolling retains partial detents and the actual system scroll-line
  setting. It honors handled wheel events and does not change selection.
  `TopIndex` clamps to the available source rows. Removing a selected live item
  preserves the original selection-change notification.

Native Windows compilation retains its original LISTBOX messages. The portable
path does not pass logical child handles to `LB_GETITEMHEIGHT`, `LB_GETITEMRECT`,
`LB_ITEMFROMPOINT`, `LB_GETTOPINDEX` or `LB_GETSEL`.

## Explicit remaining capabilities

Owner draw, multiple/no selection, multiple columns, horizontal scrollbar
controls, always-visible scrollbars and custom tab offsets are rejected at
portable rendering/input/row-metric admission. Their public APIs and native
Windows paths remain present. Scrollbar widgets, native integral-height window
sizing, drag/autoscroll selection, incremental text search and platform-specific
pixel/accessibility parity still require their actual source/platform contracts.
Wheel/key/programmatic row scrolling is not a claim that scrollbar widgets exist.

The bounded hosted ComboBox should use `DrawMode.Normal`, `SelectionMode.One`,
`MultiColumn=false` and `BorderStyle.None`. It retains its own commit/cancel and
DropDown event policy; ListBox selection changes are not acceptance notifications.

## Provenance and validation

Implementation reuses this repository's original `ListBox.cs`,
`ListBox.ObjectCollection.cs`, `ListControl.GetItemText`,
`Control.WmMouseDown`, `Control.ProcessPortableKeyMessage`, `TextRenderer` and
`ControlPaint` source. Microsoft documentation inspected for the public contracts:
[IndexFromPoint](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.listbox.indexfrompoint?view=windowsdesktop-10.0),
[TopIndex](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.listbox.topindex?view=windowsdesktop-10.0),
and [GetItemRectangle](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.listbox.getitemrectangle?view=windowsdesktop-10.0).
The latter explicitly retains offscreen rectangles, unlike the older source XML
comment. The original native implementation forwards the returned RECT without
viewport clipping; [LB_GETITEMRECT](https://learn.microsoft.com/en-us/windows/win32/controls/lb-getitemrect)
defines that rectangle in list client coordinates. The portable query follows
that distinction; its paint/input paths still reject outside-viewport points.

`CanonicalListBoxPortableTests.cs` adds 33 source cases: before/after-handle row
geometry and borders, changed fonts, clipped/RTL source paint, formatting
reentrancy, typed pointer/key/wheel routing, handled input, live selection
collections/removal, empty lists, host acceptance-key preservation, disposal and
explicit unsupported-mode rejection. All 33 now pass within the integrated
**479/479 source run, zero failures/skips**. The build has zero errors; its six
new portable protected overrides are reported by the existing public-API
declaration analyzer as warnings. The original independent cases and deadline
remain unchanged. Source tests alone do not qualify a native desktop ComboBox
or rendered pixels; full hosted CI and exact-package desktop checks remain.
