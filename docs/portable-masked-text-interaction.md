# Retained masked editor interaction

`MaskedTextBox` uses the existing optional `ILibreTextLayoutService` for its
owned formatted display. Painting, selection rectangles, actual carets, pointer
hits and automatic viewport scrolling reuse that same immutable generation.
The original `MaskedTextProvider` remains authoritative for literals, prompts,
editable positions, insertion/deletion, output formatting and password policy.
Neither public `Text` nor `TextMaskFormat` is an interaction source map.

`PortableRetainedTextLayoutOwner` owns the exact display/font/provider/format/
viewport/DPI cache and defers retired provider disposal until draw/query uses
finish. Failed cleanup retains its owner for retry; cleanup diagnostics cannot
replace a primary source/provider failure. Reentrant provider or source callbacks
cannot publish an obsolete hit, draw tail or replacement candidate. This helper
does not change ordinary `TextBox` or select a word-boundary capability.

The original mask/text/provider/display/edit operations are enclosed by a masked
source-mutation scope. Old-provider disposal waits until that complete operation
finishes, including its original selection updates. A retirement callback may
then replace the mask/text, without an obsolete outer setter overwriting it.
Nested operations share the scope; original errors retain precedence over a
failed cleanup at its exit. No new source-local shaping policy is introduced.

Pointer defaults use the existing source dispatch boundary before virtual/public
mouse notifications, with the original press/capture generation and UserMouse
opt-out. Protected notifications alone do not perform default selection. Visual
left/right movement consumes actual retained carets; source mask editing still
uses the canonical provider operations. Position queries consume the retained
display's source geometry and the same scroll offset; single-line source Y stays
zero. No prefix shaping or synthesized glyph/caret geometry is used.

Automatic scrolling translates glyphs, highlight and caret together inside the
unchanged formatting viewport. The canonical public masked `ScrollToCaret` no-op
and its `EM_SCROLLCARET` refusal remain unchanged. A paint-only text provider
retains the original paint fallback; it does not acquire inferred interaction.

Prompt hiding can shorten the visible display without editing the mask. Portable
display-only updates keep the cached signed source selection, separately from
the original ordered/display-limited public query results. Focus restoration
uses the full provider position inventory. An unfocused display never queries a
caret; a focused display whose saved endpoint is not visible also does not invent
one. Password projection happens before any layout or geometry service call and
preserves UTF-16 display length, including null-mask controls. Portable password
updates do not pass a source handle to USER32.

`CanonicalMaskedTextBoxInteractionTests.cs` exercises actual canonical controls
and the existing ProGPU retained managed layout against the unchanged qualified
dependency pin. Its twelve controls cover display/output separation, source
indices, signed dragging and original mask edits, handler/UserMouse ordering,
scrolling/refused messages, position queries, prompt/focus/read-only restoration,
password projection, font/size/DPI/handle replacement and reentrant query/draw
retirement. These are source/managed-layout controls, not independent Microsoft
pixel parity, native desktop caret/selection, IME, full word selection, provider
factory or package/application qualification.

The focused managed-source checkpoint passed all twelve new interaction cases,
the unchanged twenty-two masked paint and eleven masked frame cases, and eighteen
ordinary retained-text/row-navigation regressions: 63 passed, zero failed or
skipped. The production graph and fixture were compiled with the unchanged
qualified ProGPU source pin, SDK `11.0.100-preview.5.26302.115`, and
`BuildProjectReferences=false`; the fixture's copied Forms DLL had the exact
same SHA-256 as the newly built product. The fixture runs retained layout and
canonical source dispatch, without staging or executing a native renderer.
