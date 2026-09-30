# Portable editor hard breaks

The canonical portable TextBox consumes ProGPU's retained empty-row metadata for
LF, CR and CRLF. Empty rows have real line metrics and original UTF-16 caret
positions, not drawable placeholder glyphs. The same captured layout drives
painted caret position and pointer selection, including trailing rows.

Plain multiline TextBox translates an unmodified Enter key into its original
KeyPress/character-edit path when the host omits a control-character callback.
It uses the existing key-cycle suppression contract to reject a duplicate host
character. Read-only editing and public SuppressKeyPress still apply, and the
following ordinary text packet is not suppressed. This is confined to portable
plain TextBox; native Windows source and rich/masked editor semantics are not
replaced.

The change depends on LibreWinForms #89 and ProGPU #202. Three additional
fresh-process canonical cases raise the source-first floor from 590 to 593.
Against the unchanged parent, the empty-row/glyph and physical Enter cases fail;
the pointer source-index case is a passing control. Full source and exact-head
CI results remain required before merge.

The complete canonical source suite passes all 593 cases on macOS ARM64 with
zero failures/skips against ProGPU `d4e6e08c14bf5a013f73f4c160afeaa9aa37bcc0`.
The new cases include the actual recorded trailing-row caret rectangle,
read-only KeyPress behavior, duplicate Enter callbacks, public suppression and
the following ordinary text packet. All original 590 cases remain unchanged.
Baseline and final logs are retained under `artifacts/text-interaction`; local
source success does not qualify the native desktop or replace required PR CI.

This does not qualify native desktop rendering, popup editors, IME, visual-line
Home/End, up/down or word navigation. Those remaining editor/platform contracts
are still required before release.
