# Native characters and portable menu mnemonics

The native input contract distinguishes ordinary `TextInput` from
`SystemTextInput`. The latter is appended to the existing event-kind enum,
preserving earlier values. Source replay produces `WM_CHAR` or `WM_SYSCHAR`
accordingly, reusing caller filters and canonical mnemonic preprocessing.
Keyboard layout text is never reconstructed from physical key names.

The pinned Silk.NET 2.23.0 GLFW input adapter narrows a Unicode scalar to one
`char` in its ordinary character event. The backend instead owns the full-width
GLFW character callbacks on its actual `Native.Glfw` window. It preserves the
previous plain subscriber but does not use that subscriber's narrowed text.
Supplementary scalars become their full UTF-16 pair, with the original timestamp
and modifier bits retained. Invalid Unicode scalars are rejected atomically.

GLFW 3.4 calls the modified-character callback before its optional plain-character
callback. A real plain callback is authoritative even with Alt/Option or AltGr:
it remains text. Only an unpaired Alt character without Control or Meta becomes
a system character. Pending input is completed before the next non-character
input or at the end of native event polling, on the serialized display thread.
This also preserves character ordering between two windows on that thread.

No application callback runs between the modified and plain halves. Completed
characters are queued in arrival order and removed before application delivery;
a nested event pump may drain subsequent entries without losing the interrupted
plain pair. Retiring a native owner cancels both its pending and queued input,
not another window's input. Delivery rechecks the actual owner lifetime.

The modified callback is an explicitly required capability of the pinned GLFW
version (deprecated by GLFW for removal in a future major version). An occupied
modified slot or missing GLFW identity fails admission rather than replacing an
unknown owner or guessing an OS policy. Installation rollback preserves the
original failure. Disposal retires callbacks before Silk input and window
destruction, attempts both slot releases, and preserves a later replacement.
Window initialization failure and disposal use exhaustive resource cleanup even
if another release or public callback throws.

The sequence and callback tests cover full Unicode, Option/AltGr, explicit
system characters, original subscribers, invalid/mismatched pairs, nested full
plain pairs, exact-owner cancellation, slot conflicts, failed rollback and
retired callbacks. They do not establish physical keyboard layouts, IME,
accessibility, caret pixels or desktop menu parity on Windows, Linux or macOS.
Those require the actual installed-package interaction gates. No native platform
qualification is inferred from deterministic callback tests.

## Focused validation

On macOS ARM64 / .NET 10.0.5, all 28 character sequence/callback cases passed
with zero skips. The existing independent popup-admission gate passed all 24
cases with zero skips. The unfiltered backend suite discovered 111 cases:
103 passed and eight existing Linux/real-Wayland cases were skipped on macOS.
CI retains the full backend run, raises its minimum from 69 to 97, and adds a
separate strict 28-case character gate without changing the 24-case popup gate.

The first test-project build rejected five mock-field naming violations and a
missing separator blank line. Changing the mock fields to properties and fixing
the spacing produced a build with zero warnings and zero errors; no analyzer
was disabled. Both build logs and all three test logs are retained under the
owned `artifacts/native-characters/log` directory. Physical native input and
installed-package execution of this change remain pending.

Primary implementation references:

- [Pinned GLFW character dispatch](https://github.com/glfw/glfw/blob/3.4/src/input.c)
- [Pinned Silk GLFW keyboard adapter](https://github.com/dotnet/Silk.NET/blob/v2.23.0/src/Input/Silk.NET.Input.Glfw/GlfwKeyboard.cs)
- [GLFW character callback contracts](https://www.glfw.org/docs/3.3/group__input.html)
