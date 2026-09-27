# Read-only X11 diagnostic keymap admission

The original grid-input diagnostic rejected an otherwise idle NumLock state.
GNOME restored that lock when the application received focus, so temporarily
toggling it off did not provide a stable diagnostic environment.

`eng/librewinforms-x11-keymap.py` provides opt-in admission for that specific
state. It does not change the application's keyboard handling or the existing
popup driver's default behavior. The guarded keyboard adapter inherits the
original diagnostic sender, including foreground checks, physical-key checks,
current key-row checks and cleanup of only its own queued key presses.

Admission requires all of the following:

- Group zero, no latched state, no pointer buttons, and no user-held modifiers.
  Only the sender's own Shift may appear in `mods`, `baseMods` and
  `compatState`. Xorg's `ProcXkbGetState` leaves the four grab/lookup reply
  members zero-initialized even under Shift; these must remain zero, not contain
  invented copies of the effective mask. See the primary
  [server implementation](https://github.com/XQuartz/xorg-server/blob/master/xkb/xkb.c).
- Either no locks or the exact otherwise-idle Mod2 lock state. Mod2 must contain
  exactly one key, mapped exclusively to NumLock and no other modifier.
- The lock must stay unchanged for the entire keyboard lifetime.
- Each requested route must translate to the requested symbol both with and
  without that lock. NumLock-sensitive keypad routes are therefore rejected.
- A fresh native connection to the same named display and root supplies actual
  XKB translation. A child-process timeout bounds foreign-library blocking within
  the existing three-second protocol and original application deadlines.

No lock/map mutation, global Xlib error handler, input-focus override or server
grab is used. The API semantics come from the primary
[Xlib XKB event and keymap documentation](https://xorg.freedesktop.org/archive/X11R7.7/doc/libX11/XKB/xkblib.html#Xkb_Event_and_Keymap_Functions).

Run the offline controls with:

```sh
python3 -B eng/tests/test_x11_keymap.py
python3 -B eng/tests/test_popup_x11_harness.py
```

The source CI lane runs the new 17-case control alongside the unchanged X11
driver controls. A read-only Ubuntu ARM64/XWayland check verified the actual
routes for `Alice`, `X`, Return, F2 and Escape with NumLock left on: all symbols
matched their lock-off controls, and the complete before/after XKB state matched.
That check injected no input and does not qualify grid editing, pixels, chrome,
other layouts or other desktop platforms.
