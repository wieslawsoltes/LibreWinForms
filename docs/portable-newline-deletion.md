# Portable newline deletion

The portable source editor's collapsed-caret deletion now removes both code
units of an adjacent CRLF. Previously Backspace after CRLF removed only LF,
while Delete before it removed only CR; the surviving code unit remained a
hard break in the retained layout, so joining lines needed a second keystroke.

The existing source selection/edit path still owns mutation and notifications.
Only zero-length selections use the adjacent-pair rule. Explicit UTF-16 ranges
are not expanded. Lone CR and LF remain single-unit edits, surrogate pairs keep
their existing handling, and stored source text is never normalized. The same
translated KeyPress and key-cycle suppression paths remain authoritative.

Fourteen source cases cover forward/backward CRLF deletion, consecutive/empty
rows, standalone CR/LF, exact explicit selections, read-only input, public key
suppression, source caret indices and single edit/Modified notifications.
The canonical source floor increases from 593 to 607 without changing timeout
or skip policy. Native desktop comparison remains part of final validation.

This change builds on the retained hard-break rows and physical Enter support
in LibreWinForms #90 / ProGPU #202. It does not implement word deletion, undo,
IME, or vertical/visual-line navigation. Local evidence is retained under
`artifacts/newline-deletion/`; the initial method-filter attempt selected zero
tests and is not passing evidence.
