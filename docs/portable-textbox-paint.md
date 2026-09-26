# Portable plain TextBox content painting

The canonical portable control tree already calls `OnPaint`, but plain
`TextBox` previously depended on the Windows EDIT window procedure to draw its
content. That native painter does not run in the portable tree. An ordinary
`DataGridViewTextBoxEditingControl` could therefore contain `Alice` while showing
no editor text.

The portable-only `TextBox.OnPaint` now sends the complete source string, source
font, foreground color, canonical RTL-translated horizontal alignment, and
single-line/word-wrap flags to the existing typed `TextRenderer` service. The
ProGPU service retains responsibility for text shaping and drawing. There is no
grid-specific text painter, prefix-width estimate, or character-count layout.
Caller and source clips are intersected and graphics state is restored before
the normal paint event. Source text, enabled state, and password display changes
invalidate retained content.

Passwords are replaced before the renderer sees the string: a custom password
character is repeated per original UTF-16 code unit; the portable system-password
display policy uses U+25CF and takes precedence over that custom character. This
does not select an OS password glyph or change the source text/selection. Empty,
unfocused content displays `PlaceholderText`; disabled and placeholder content
use the existing gray system color. `MaskedTextBox` and `RichTextBox` are separate
source controls and are not flattened into this plain-text implementation. The
Windows-native compilation path is unchanged.

## Validation and remaining work

Twenty canonical source cases cover whole UTF-16 text and selection retention,
alignment/RTL flags, line/wrap flags, password masking without creating a password
handle, read-only/disabled/placeholder content, caller clip/state restoration,
zero width, the actual DataGridView editor's `DrawToBitmap` control-tree route,
and retained text/enabled invalidation. The typed test platform records the new
TextBox transport while preserving its existing independent text assertions.
A separate backend test uses real ProGPU `Graphics` and `Bitmap` to require
visible text ink and no pixels outside the source clip.

These tests are authored but not yet executed. They do not qualify the original
sample's native GUI or complete editing visuals. Selection highlighting, shaped
caret placement/blinking, pointer-to-text hit testing, horizontal/vertical edit
scrolling, IME composition, border/chrome pixel parity, and native password-handle
qualification remain separate work. Those behaviors must use the actual retained
text measurement/interaction service; this change does not approximate them or
claim that the full TextBox user experience is complete.
