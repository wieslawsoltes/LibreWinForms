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
the normal paint event. Source text, enabled state, password display, and focus
changes invalidate retained content. Live password changes retain the source
IME restriction notification and autocomplete-reset calls; this is not new
portable IME or autocomplete implementation.

Passwords are replaced before the renderer sees the string: a custom password
character is repeated per original UTF-16 code unit; the portable system-password
display policy uses U+25CF and takes precedence over that custom character. This
does not select an OS password glyph or change the source text/selection. Empty,
unfocused content displays `PlaceholderText`; disabled and placeholder content
use the existing gray system color. `MaskedTextBox` and `RichTextBox` are separate
source controls and are not flattened into this plain-text implementation. The
Windows-native compilation path is unchanged.

## Validation and remaining work

Twenty-two canonical source cases cover whole UTF-16 text and selection retention,
alignment/RTL flags, line/wrap flags, password masking without creating a password
handle, read-only/disabled/placeholder content, caller clip/state restoration,
zero width, the actual DataGridView editor's `DrawToBitmap` control-tree route,
retained text/enabled/focus invalidation, and existing password IME restriction
notifications. The typed test platform records the new
TextBox transport while preserving its existing independent text assertions.
A separate backend test uses real ProGPU `Graphics` and `Bitmap` to require
visible text ink and no pixels outside the source clip.

The first exact-source macOS ARM64 build passed the original 166 cases plus the
initial twenty paint cases (186/186, no skips). The reviewed focus/password
correction then passed the complete 188-case source suite, no skips, using .NET
11.0.0-preview.5.26302.115 to run the net10.0 assembly. The build had zero errors;
its 619 warnings include four new portable protected-override API inventory
warnings, not suppressed by this change. The actual backend text-renderer class
then passed all three tests with no skips, including the clipped-ink case, after
a zero-warning/zero-error build. Full exact-head CI is still pending. Local logs
are retained under `artifacts/textbox-paint/log`.

These source results do not qualify the original sample's native GUI or complete
editing visuals. Selection highlighting, shaped
caret placement/blinking, pointer-to-text hit testing, horizontal/vertical edit
scrolling, IME composition, border/chrome pixel parity, and native password-handle
qualification remain separate work. Those behaviors must use the actual retained
text measurement/interaction service; this change does not approximate them or
claim that the full TextBox user experience is complete.
