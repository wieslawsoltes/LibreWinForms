# Popup desktop navigation key identity

The Windows popup driver must inject the dedicated Down key, not its numeric
keypad counterpart. The original pair supplied VK_DOWN with scan zero and no
extended-key flag. The source scenario selected File after F10 but never opened
its dropdown after Down; source-level F10/Down regressions already pass. This is
evidence to inspect native input encoding, not proof of a source menu defect.

Resolve the requested key with MAPVK_VK_TO_VSC_EX and retain its low scan byte.
Explicitly retain the dedicated navigation cluster's E0 identity even when that
reverse mapping chooses an unprefixed keypad alias. Preserve other returned E0
prefixes, virtual-key mode and identical press/release identity; reject an
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

## Delivered-key diagnosis

A private copy of the same application added a passive `IMessageFilter` that
always returns false. No source input or focus was synthesized. The paired
original scenario again completed all 14 Microsoft phases and stopped at
portable phase 10. Its source trace records F10 (`121`) followed by NumPad2
(`98`), not Down (`40`). Microsoft received Down, but its raw message lacked the
extended-key bit. These observations explain why virtual-key-based Microsoft
processing passed while the physical-key callback identified a keypad key.

A separate read-only native probe in that Windows ARM64 guest, layout
`0x4090409`, returned `0x50` for both VK_DOWN and VK_NUMPAD2 in mapping modes 0
and 4. All ten dedicated navigation virtual keys likewise returned unprefixed
scan codes. This is actual native evidence, not the former mock assumption that
mode 4 necessarily returns E0 for those virtual keys. Raw paired traces and
receipts are retained under `popup-key-route.YbAJiBh1`; original SDK/application
and overlay hashes remained unchanged and both owned processes exited.

The driver now explicitly supplies E0 for Home/End, Page Up/Down, the four arrows
and Insert/Delete, matching the dedicated keys in Microsoft's scan-code table.
It does not change product mapping or turn NumPad2 into Down. Regressions cover
all ten observed unprefixed aliases, ordinary/keypad controls and the existing
prefixed, rejected and partial-pair cases. The ten alias cases fail before this
change; ordinary/keypad controls already pass. Native application validation
remains separate from these offline contracts.

The corrected driver (`eb09e17773318534de3628c726aedef919e3c166`, SHA-256
`145a08b23d4fc3613f0fb978735d99dbd487bb5e9643adf87d1da5329b1aec9e`)
then ran against the original unmodified application payloads. Both captured all
14 phases under the original 60-second deadline. Portable File now opens after
F10/Down; Escape, bare Alt and ComboBox Down/Enter complete with Beta committed.
The original and diagnostic payload hashes remained unchanged. Evidence is under
`popup-navigation-keys.VnzvZlF7`, with both owned child processes confirmed gone.

Image inspection still rejects visual parity: the old portable renderer omits
the More menu label, uses different control styling and has no visible tooltip
in its final capture. That tooltip capture exposed a second harness defect: its
Popup count was already one at 31,812 ms, before the final hover at about 38 s,
and the other visible owned window was the main window's shadow. Therefore this
14-capture result proves the keyboard progression, not a successful tooltip.

The final hover now requires both a newly raised Popup event and a newly visible
owned window relative to the pre-hover snapshot. Existing owner shadows or old
events cannot satisfy it. The same pointer/key sequence, application, screenshot
inspection and original deadline remain required; no delay or retry is added.
A regression exercises stale-event, existing-shadow and fresh-popup/window
combinations. A subsequent native run must establish the stronger outcome.
