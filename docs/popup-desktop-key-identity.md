# Popup desktop navigation key identity

The Windows popup driver must inject the dedicated Down key, not its numeric
keypad counterpart. The original pair supplied VK_DOWN with scan zero and no
extended-key flag. The source scenario selected File after F10 but never opened
its dropdown after Down; source-level F10/Down regressions already pass. This is
evidence to inspect native input encoding, not proof of a source menu defect.

Resolve the requested key with MAPVK_VK_TO_VSC_EX and retain its low scan byte and
E0 extended flag on both press and release. Preserve virtual-key mode; reject an
unmapped or unsupported prefix before injection. The original foreground/PID,
held-key/modifier, complete-pair and partial-pair cleanup guards are unchanged.
The same 14 phases, 60-second deadline, observer, screenshots and assertions run
for Microsoft and portable applications. No application control is driven through
source calls, and no product keyboard mapping or renderer is changed.

Contracts consulted:

- [Microsoft KEYBDINPUT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-keybdinput):
  E0 belongs to KEYEVENTF_EXTENDEDKEY; the release preserves that identity.
- [Microsoft MapVirtualKeyW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-mapvirtualkeyw):
  mapping mode 4 retains extended prefixes instead of discarding them.
- [Microsoft keyboard input](https://learn.microsoft.com/en-us/windows/win32/inputdev/about-keyboard-input):
  dedicated navigation keys and keypad keys differ by the extended flag.
- [GLFW input contract](https://www.glfw.org/docs/latest/input_guide.html#input_key):
  key callbacks report physical keys and platform scan codes, separately from text.

Offline regressions inspect the actual ctypes pair for ten navigation keys,
F10/Escape/Alt/Enter and keypad-2 controls, rejected mappings, and recovery of a
partially admitted extended pair. They do not qualify delivered Windows messages
or popup UX. A fresh paired native run is still required; the previous timed-out
receipt remains failed evidence and must not be relabeled.
