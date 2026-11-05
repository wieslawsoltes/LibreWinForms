# Portable ListView item lifetime

Portable ListView handles do not own a comctl32 item inventory. Their original
managed item collections, selected flags and saved state-image indices remain
authoritative before and after handle creation. Native Windows keeps its original
state-message and focus behavior.

Unhosting no longer reads a nonexistent native item. Group reassignment and
unhosting also avoid writing native item packets, and removal does not send a
native focus-clear request. The existing source cleanup still removes group
membership, unhooks tooltips, releases accessibility ownership and detaches the
item. A detached item retains its selected/state-image flags and subitems for
reuse. Public portable selected/state-image access uses those same saved fields;
there is no fabricated native response or shim DLL.

The canonical source suite includes twelve remove/clear/dispose cases with and
without a created handle, selected and unselected items, grouping and rehosting.
An additional case changes live selected, checked, state-image and group values,
checks invalid index rejection, and verifies saved state after removal. The initial
contributor fix exposed a second USER32 write during grouped teardown; the original
failure remains retained. These source lifetime checks do not qualify native
focus, virtual-item interaction, drawing, accessibility or desktop application
behavior. Those contracts remain separate from the disposal defect in #195.
