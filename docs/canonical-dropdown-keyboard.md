# Canonical dropdown keyboard input

Portable native dropdown windows remain nonactivating. Keyboard input delivered
to their typed Form owner is redirected to the current visible AutoClose dropdown
after ordinary application message filters. The existing canonical preprocessing,
ToolStrip selection, item command, arrow, Enter, Escape and mnemonic algorithms
then process it. No replacement controls, focus transfer, Win32 menu hook, or
native-message simulation is involved.

The shared thread-local registry retains the existing opening/visible chain
lifetime, including an AutoClose child of a persistent parent and a child whose
root opening was canceled. It removes retired chains and resolves the target
again after filters, rather than borrowing a target across user callbacks.
Handled keys and menu-closing mnemonics cannot leak translated characters into
the owner editor during the same key cycle.

When an already-open dropdown returns to its MenuStrip, a typed continuation
retains that strip and Form without activating either window. Existing source
selection and expansion handle adjacent menus and reopening. Menu-mode exit,
owner deactivation and strip disposal release the continuation. Bare Alt/F10
menu-bar activation is connected separately in
`portable-menu-key-activation.md`. Hosted-editor focus inside ToolStripControlHost
and native platform qualification remain outstanding; this is not a claim of
complete keyboard UX.

`CanonicalDropdownKeyboardTests.cs` contains 16 source cases using actual Forms,
ContextMenuStrip, ToolStripMenuItem, MenuStrip and TextBox objects. Input enters
through the typed platform window callback during a genuine Application.Run
loop. The cases cover navigation and skipped items, LTR/RTL cascades, Enter and
translated-character ownership, explicit/implicit/duplicate mnemonics, canceled
Escape, replacing/consuming filters, persistent parents, reentrant preview
closure, ordinary owner input, and both MenuStrip continuation directions.

Local compilation and source execution are recorded separately from native
desktop qualification. Actual Windows, Linux and macOS menu input, full package
CI and visible application behavior remain required; these source fixtures do
not qualify native focus, IME, accessibility or all menu-bar behavior.

The first actual source run retained 311 passes and two MenuStrip failures at
the Win32-only HMENU refresh. After that boundary was removed, 314 of 316 passed;
the two remaining cases exposed premature automatic-expansion cancellation.
The existing reason-qualified close policy is now shared with the portable path,
and its getter recognizes the typed menu chain. The corrected build completed
with zero errors and 622 existing source warnings; all 316 source cases passed,
with no skips, on macOS ARM64 / .NET 10.0.5. The source drawing dependency was the
unchanged `08f4343ef15328ba742cdcf11f8eb2daeefb5f7b` checkout. Initial compiler and
runtime-selection failures are retained alongside these results in the owned
`artifacts/dropdown-keyboard/log` directory.

After composing the keyboard and outside-pointer changes, the full product build
again completed with zero errors and 622 existing warnings. The first combined
run passed 337 of 338 cases: one pointer fixture incorrectly assumed a parent's
trailing padding could not overlap its child popup. The corrected fixture uses
top padding and independently asserts parent inclusion, owner-item exclusion and
child exclusion before dispatch. The test-only rebuild had zero warnings/errors;
the complete combined source suite then passed 338 of 338, with zero skips, on
the same runtime and drawing source. Both attempts remain in the evidence
directory; this result does not substitute for package CI or native desktop input.
