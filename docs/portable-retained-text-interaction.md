# Portable retained TextBox interaction

The canonical portable TextBox uses the optional `ILibreTextLayoutService`
capability alongside the existing drawing/measurement service. The ProGPU
implementation captures a complete Drawing text paragraph and uses that same
generation for glyph drawing, selection rectangles, pointer hits and horizontal
caret movement. It does not introduce a second shaper or rewrite source text.

The cache key includes provider identity, original source Font identity, display
text, client size, format flags and target Graphics DPI. TextRenderer retains its
existing InitialSystemDpi font realization. Selection changes and repeated paints
reuse the generation; replacements and handle destruction dispose it. Password
layout sees only the displayed mask, not the secret source string.

Source paint owns the fixed client clip. Scrolling moves the paragraph and its
selection/caret together without translating that viewport. Focus, enabled state
and the platform caret timing control the managed blink timer. Left/right and
Shift selection use the signed source anchor. Pointer dragging uses actual
capture and releases its selection ownership when capture is lost. Public mouse
callbacks run before layout input and disposal is checked again afterwards.

Native Windows source remains unchanged. Providers without the optional
capability preserve their existing text-painting path; they are not advertised as
providing retained editor interaction.

## Qualification

Eight fresh-process canonical source cases exercise the actual ProGPU adapter,
not fabricated control objects. The full source-first gate retains its original
581 cases and raises its minimum to 589. Local results and exact dependency CI
must pass before this change is merged.

This is not full editor or native desktop qualification. Empty hard-break rows,
vertical/visual-line navigation, word movement, double-click word selection,
scrollbar integration and IME remain separate unfinished work. Cross-platform
rendered appearance, popup editors and native focus/capture validation remain
required before release.
