# Portable hosted menu input

A real TextBox hosted by ToolStripControlHost or ToolStripTextBox can now acquire
source logical focus after a pointer press inside a canonical dropdown. The
native popup stays nonactivating and the Form remains the actual activation
owner. Its keyboard callbacks reach the hosted Control through the existing
application filter, canonical preprocessing, key events and portable edit path.
No alternate editor, reflection or native focus hook is used.

The focus lease retains the actual host membership, popup and owner handle
identities, target and original owner focus. Source Focused/ContainsFocus and
GotFocus/LostFocus reflect that logical target. Closing restores the original
owner control only while the old lease still owns its focus. Canceled closes
retain editing. Reentrant replacement popups retain their own focus and input.

Private source lifetime notifications precede public visibility, enabled,
parent and handle callbacks. They cover the actual ancestor path and owner,
so an earlier throwing public handler cannot leave owner input pointing into a
hidden or detached editor. Lease identity is retired before focus callbacks.
Pointer dispatch also rechecks source handles and visibility after GotFocus;
a callback that closes the popup cannot receive the pending old mouse press.

ToolStripTextBox hover/focus invalidation uses the existing portable invalidation
path. The original Windows non-client redraw path is unchanged. This does not
claim professional non-client border pixel parity.

Ten source cases use actual Forms, ContextMenuStrip, ToolStripControlHost,
ToolStripTextBox and TextBox objects inside Application.Run. They cover typed
pointer admission, owner-delivered UTF-16 replacement and backspace, retained
selection/caret indices, filters and SuppressKeyPress, canceled Escape, outside
click, throwing hide/disable/unparent handlers, replacement during LostFocus and
closure during GotFocus. The original 369 cases remain mandatory.

The first five cases all failed against the previous source output, while its
369 existing cases passed. Four exposed missing focus/input ownership; the
ToolStripTextBox case additionally reached USER32.GetClientRect during hover.
After the initial connection the full 374 cases passed. The final lifetime
controls bring the full result to 379/379, zero skips, on macOS ARM64 / .NET
10.0.5. The full source build completed with zero errors and 622 existing
warnings against unchanged ProGPU source
`08f4343ef15328ba742cdcf11f8eb2daeefb5f7b`. All attempts, including the initial
fixture compiler failure, are retained under `artifacts/dropdown-keyboard/log`.

The explicit mnemonic source input change is composed separately: backend
`SystemTextInput` becomes WM_SYSCHAR while ordinary text remains WM_CHAR,
including AltGr and Option text. Both use the same admitted source focus owner.
Its eight additional cases raise the combined minimum to 387. The composed
387-case run and native backend transport are not qualified by the preceding
379-case result.

These are source lifecycle and editing contracts, not native GUI qualification.
No VM was used. Native desktop input, installed-package UI, caret/selection
pixels, measured pointer-to-character placement, scrolling and IME remain
separate requirements. The tests explicitly set source selection before text
replacement; no character-count or prefix-width hit approximation is introduced.
