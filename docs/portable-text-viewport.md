# Portable TextBox caret scrolling

`TextBoxBase.ScrollToCaret` previously queried USER32's rich-edit interface even
when the control had a portable handle. The portable branch now sends
`EM_SCROLLCARET` through the control's real virtual `WndProc`. It preserves the
existing no-handle deferral, empty-text fast path, custom message handlers and
MaskedTextBox's original no-scroll policy. Native Windows implementation code is
unchanged.

Plain TextBox handles the message using its owned retained layout generation.
The active signed UTF-16 selection endpoint and retained affinity select the
caret; no text, selection, wrapping, font or bidi state is recreated by the
scrolling policy. As specified by the [ScrollToCaret contract](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.textboxbase.scrolltocaret),
an unfocused control does not scroll. Empty viewports require no layout, and an
already-visible caret does not invalidate or rebuild the retained paragraph.

Explicit scrolling and automatic paint-time caret visibility share the same
viewport calculation. It preserves the current offset when the caret fits and
allows negative offsets for real centered/right-aligned/RTL overflow. Clamping
those offsets to zero made left-of-frame caret positions permanently invisible.
The source client clip remains fixed; glyphs, selection, caret and pointer hit
testing use the same translated paragraph.

The offset is published before invalidation, so input or repaint from an
application callback sees the new viewport immediately. Password rendering still
passes only the masked display string to the layout provider. Read-only text
retains selection and scrolling without edit notifications. Rich-document
scrolling remains an explicit unsupported capability instead of being flattened
into plain-text layout.

Focused source tests use the actual canonical controls and ProGPU retained layout
through the typed headless host. This is not native desktop/package appearance,
scrollbar interaction, rich-document scrolling, or the separate public character
position/physical-line query API qualification.

The initial 17 source cases reproduced 15 failures (including USER32 load errors)
and two passing no-op controls against the unchanged parent. All 19 final focused
cases pass on macOS ARM64 using .NET 10.0.9, including additional invalidation
callbacks that dispose the control or replace its text/layout. Each retained
case runs through the existing exact-one-case child-process check; theory cases
were split into named facts instead of weakening that receipt. The canonical
source gate increases its required count from 637 to 656 without changing skips,
assertions or deadlines. Logs are retained under `artifacts/text-scroll`.

The complete local canonical source suite passes all 656 cases with zero failures
or skips in 52 seconds. Exact-head hosted Build and Docs, native desktop behavior
and release validation remain separate required gates.
