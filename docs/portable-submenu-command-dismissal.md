# Submenu command dismissal

An ordinary submenu command closes its ancestor dropdown chain, not just the
leaf that received the click. The portable visibility path omitted the existing
Windows-source `ItemClicked` expansion. Consequently the command fired and its
child closed, but the context root remained visible.

Both paths now reuse `DismissItemClickedChain`: retain SourceControl through
command delivery, use canonical `DismissAll` and unselect the root owner item.
Ancestor closure keeps its ordinary cancelable policy and default
`AppFocusChange` reason; the clicked leaf retains `ItemClicked`. This does not
force-close persistent menus or override Closing cancellation.

Portable menu-bar completion captures only the original continuation before
Closing callbacks. Dismissal/deselection may establish a replacement lease,
including one for the same bar; old cleanup cannot clear that identity. The
existing post-click keyboard-state cleanup likewise does not deactivate a new
live input chain through an already hidden dropdown. These checks reuse the
same typed menu-input ancestry policy as hover expansion. Native Windows menu
filter behavior remains unchanged.

Nine source regressions cover three-level pointer/programmatic commands,
SourceControl during delivery, leaf and ancestor vetoes, persistent roots,
command exceptions, menu-bar input restoration and replacement continuations on
the same or another bar. Original code fails five and passes four controls.
The fixed source passes all 94 selected submenu, dropdown, hover, context-pointer,
menu-key and menu-strip tests with no skips. The source CI gate explicitly selects
the nine new cases, retains every existing case and rejects skips under its
original ten-minute limit.

The unchanged Windows ARM64/Parallels/default D3D12/FXC desktop diagnostic now
captures phases 01 through 09: context submenu command and root dismissal,
outside-click dismissal, menu-bar submenu command and root dismissal, then F10
selection. It still reaches the original 60-second deadline at
`10-keyboard-menu`, where Down should open the selected menu. Only the private
Forms DLL over the earlier diagnostic candidate was replaced; original package,
application, input script, unchanged copied files and SDK were hash-checked.
No new full producer package was staged or qualified by this run.

These source contracts are not complete native popup UX/UI or installed-package
qualification. Missing painted menu text, platform focus/placement behavior and
the full desktop interaction scenario retain their separate gates.
