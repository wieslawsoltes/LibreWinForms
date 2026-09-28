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
or popup UX.

## Windows desktop result

The corrected driver at commit `0e1c7769d1b5ddada0167f794aec140391d3c989`
ran once against both existing application payloads in the Windows 11 ARM64
Parallels guest. Microsoft WinForms completed all 14 phases. The portable source
candidate completed phases 1–9, including both submenu command dismissals and
F10 menu selection, but still expired at phase 10: Down did not open File.
Both retained the original 60-second application deadline and identical scenario.

The portable run reused the existing private diagnostic payload with the
submenu-dismissal fix; it was not a newly qualified package. Driver SHA-256 was
`e53873bf7a89200d191659140da8a79b0dbf4ca5f3658f0d466a67973f068345`.
SDK, original package outputs and diagnostic replacement hashes were verified
before and after execution. Both owned child exits were observed. Raw receipts,
events, snapshots and screenshots remain under `popup-key-delivery.zMkbzFxg`.

The F10 screenshot confirms selection, not delivery or processing of Down.
The parent context menu also still omits the second item's text in this old
renderer payload. Therefore corrected key encoding alone does not fix the
portable failure, and neither keyboard nor visual parity is established. Trace
actual backend/source key delivery before changing product routing; preserve the
failed receipts and unchanged acceptance requirements.
