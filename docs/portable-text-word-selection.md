# Plain TextBox word selection

Acceptance application: the existing `WinFormsControlsTest.TextBoxes` form,
starting with its `textBox` and `multilineTextBox` controls. Double-click a word,
then extend and reverse the selection while holding the second press. The source
editor must retain the original word-drag seed and notify
derived/public mouse handlers after its default processing. ToolStripTextBox and
the editable ComboBox reuse that same plain editor; this does not specify
MaskedTextBox or RichTextBox behavior.

## Remaining unmasked source gap

The native input adapter already carries completed click pairs to
`ProcessPortableMouseDownDefault` as `MouseEventArgs.Clicks == 2`. That method
now uses word selection only when the text service explicitly supplies an owned
EDIT-boundary snapshot. The ordinary ProGPU provider does not yet supply that
capability: its unmasked controls still place a caret and extend by character.
Correct native click notifications and source state handling do not by themselves
implement the missing boundary classifier.

The existing retained layout owns original UTF-16 hit/caret/selection geometry.
Its row and grapheme capabilities do not define a clicked word. The source's
Ctrl+Backspace helper implements deletion, not a clicked-word span or reversal
policy. A wrapping or grapheme algorithm alone does not establish the Windows
EDIT selection contract.

## Native reference and observed policy

Extend the existing `eng/NativeTextBoxReference` stock-Windows application with
an explicit word-selection mode. Its old frame-reference mode remains separate.
Use real owned EDIT handles, native character-position/hit queries and native
mouse messages; retain the actual selection after the double press, outward
drag, reversal and release. Record public callback observations so the source
implementation preserves default-before-notification order.

The reference must include interword and trailing spaces, tabs, punctuation,
CRLF/empty rows, surrogate and combining sequences, bidi text, read-only controls
and password masking. It must record actual hit positions rather than inventing
character widths or a clickable rectangle for a hard break. Password source must
not be sent to the portable shaping service when the implementation is added.

Run this package-independent prerequisite in the existing early Windows
automation CI job, retaining its original matrix, locale check and ten-minute
timeout. It does not wait for, replace or qualify the separate package checks.
No local VM, platform matrix,
screen capture or general desktop audit is required to acquire these endpoints.
Synthetic HWND messages are not physical input or rendered desktop qualification.
Inspect the completed receipt before defining portable expected selections.

The first successful reference is the Windows automation job `109790362280`
of Build `36685521081`, PR head `7e8666e0410c9122803aaeeafcd6b17acd43a1f3`.
Its integration checkout is `bec9b19817cbf394990360403af1a8e0d12f8694`, a distinct
identity. The artifact `native-edit-word-selection-windows-latest` contains
12 cases, 200 requested coordinates, 196 observed gestures and 1,568 selection
states; four multiline CRLF coordinate requests are explicitly unavailable.
The receipt SHA-256 is
`72b334caab3a7f0a94cfda2590131e80eb9055464172f922b6db8d5008808630`.
This completed reference job is not a successful whole Build or product gate.

The observations distinguish these contracts:

- The actual native caret boundary and displayed row determine selection;
  a requested glyph index is not its substitute. At a new multiline row,
  selection starts in that row rather than selecting the preceding CRLF.
- ASCII punctuation can remain inside a word, trailing spaces are included,
  and a tab following spaces can start a separate span. Generic whitespace,
  UI Automation word units and Ctrl+Backspace do not reproduce these ranges.
- Dragging the initial leading-space range `[0,2]` back to native hit zero
  yields `[2,2]`. Unioning word ranges loses the native reversal behavior.
- Password double presses and the recorded drags retain the entire source
  selection, without exposing that source to the portable shaping provider.

The expanded reference passed Windows job `109799419524` of Build `36688369362`
on head `e7cf4da37ee5dee73df1d0311f6a067eeb911873` (integration checkout
`f8259147a0118b0b864a6476e2f30ee686ccbbcd`). Receipt SHA-256 is
`6a91435fbf228b11af930a6ffe6660ea7f80e91c8f7369bab128eb92a3b2baca`.
It records 18 cases, 294 requests, 286 observed gestures and 2,288 selection
states; eight coordinate requests are unavailable. Every observed default
word-break callback address is zero, so there is no borrowed callable default
implementation. The independent Uniscribe flags are observations, not a claim
that EDIT uses that specific API internally.

For all 286 double presses and 1,144 recorded moves, the following boundary-based
policy matches the receipt. Source endpoints, native stop flags, whitespace run
seams and CR-start boundaries form the observed boundary set. A double press
uses the preceding strict boundary, except a hit at an actual row-start may use
that boundary. Hit zero skips leading native-flagged whitespace. During dragging,
retain the original hit and both original selection endpoints: movement logically
left uses the inclusive preceding boundary with the original end; movement right
uses the original start with the inclusive following boundary. Returning to the
original hit restores the original selection. Do not use the most recent ordered
selection as a new anchor.

These observations also constrain the remaining boundary implementation:

- Soft-break and word-stop flags coincide in this receipt; it cannot distinguish
  those primitives. Raw flags miss some bidi whitespace/run seams and CR edges.
- Narrow NBSP `U+202F` breaks here, while NBSP `U+00A0` stays joined. Substituting
  a default Unicode line-break implementation without a compatibility contract
  would change the observed behavior.
- Isolated LF stays attached to preceding text; CR separates it. The two isolated
  break cases remain one native row. Do not infer visual rows from CR/LF scans.
- A long word selects across three soft-wrapped rows. Visual rows do not clamp
  word ranges.
- CRCRLF interior selections were not observed: apparent interior requests mapped
  elsewhere or were unavailable. Do not turn them into invented native expected
  ranges.

The existing caret and retained-row APIs cover the observed coordinate contract;
no extra public cluster-hit API is justified. The reusable boundary calculation
is still an implementation prerequisite; this finite receipt is not arbitrary
Unicode or physical-input qualification.

The current probe additionally records independent full-paragraph `ScriptItemize`
runs, copied `ScriptGetProperties` flags and `GetStringTypeW` CTYPE1 values.
Raw engine IDs are version-dependent diagnostics, not portable classifier values;
CTYPE1 entries describe original UTF-16 units, not Unicode scalars. Supplementary
letters/music/CJK and emoji variation-selector/ZWJ cases distinguish native run
handling from a presumed scalar property table. Their selections remain observed
outputs, not assertions chosen to fit the draft. Existing cases and deadlines
remain unchanged. At head `4a7a93872f2a02678941e0075c1c0deb7014b495`, Windows job
`109814811886` built and executed all 20 cases (326 observed gestures, eight
unavailable) in 3,878 ms. Its receipt hash is
`127d4e15d3ca03e778bd4fccc4eb8ddf96007ae6a55d9bbbd13c2399106b4001`.
The job failed afterward because the CI inventory still listed 18 cases; it is
not a passing reference or product gate. The updated verifier retains those
original cases and checks both new cases, loaded module hashes, complete source
partitions and raw/decoded classification fields.

The expanded 20-case reference subsequently passed Windows job `109819392226`
of Build `36694588514` at PR head `4a8ee20fc7798ab1fea30208652fdfa3b2727759`
(integration checkout `95f27d5250bea5f732aec6fe9dfda1f8271d440c`). Its receipt
SHA-256 is `fcb4c81e661ff7232bf580efcf79aaf36572a970c63a898ed7447cf70abf6503`:
326 observed gestures, eight unavailable requests and 3,665 ms elapsed. Every
observed script run has `needsWordBreaking == false`; the soft-break and word-stop
flags still coincide. This successful reference does not qualify the failed
whole Build, a portable classifier or desktop behavior.

Four additional authored discriminants retain all 20 original cases and requests:
the same Thai phrase twice, adjacent and space-separated, plus adjacent repeated
Lao and Khmer phrases. Eight original UTF-16 requests per new case use the same
native position/hit queries, drag/reversal sequence and unavailable-coordinate
handling. No word endpoint, selectable combining position or dictionary result is
chosen in advance. Their native runs must actually report `fNeedsWordBreaking`;
the verifier ties the copied raw property bit to a run covering that case's script
units, rather than accepting a hard-coded native script ID. Missing evidence
fails the observation and retains its receipt, not an invented selection.

The inventory is now 24 cases with unchanged 30-second observation, 60-second
reference-process and ten-minute Windows-job bounds. Existing native module,
CTYPE1, raw logical attribute, source-partition, callback and selection controls
remain intact. The new cases were not executed locally. They are prerequisites
for defining a faithful portable EDIT classifier, not production capability
admission. Microsoft's
[SCRIPT_PROPERTIES contract](https://learn.microsoft.com/en-us/windows/win32/api/usp10/ns-usp10-script_properties)
distinguishes languages requiring `fWordStop` information from whitespace-based
placement; [ScriptBreak](https://learn.microsoft.com/en-us/windows/win32/api/usp10/nf-usp10-scriptbreak)
requires whole native items rather than smaller formatting runs. These public
contracts inform the discriminants without establishing EDIT's implementation.

The 24-case reference passed Windows job `110178653752` of Build `36802156343`
at PR head `639fe6938027c6e2565ae84968e1010b8ed665d8` (integration checkout
`fa6818a1fc31cb736dbf115e30500d6c231ec64f`). Receipt SHA-256 is
`647da7cdd14bfad8c5b4567b553bcbfa5ceacfde3c3823524abc0271ad430a0b`.
It retains all 398 coordinate requests: 388 observed gestures, ten explicitly
unavailable coordinates, and 5,076 ms elapsed. The original 20 cases still have
326 observed gestures. Probe/project/helper source hashes match the exact PR
head with Windows CRLF; the execution and receipt retain matching process identity.
This successful reference is not evidence that the whole Build passed.

All four added cases actually report the copied `fNeedsWordBreaking` property.
The observed attributes and EDIT selections are nevertheless distinct:

| Case | `fWordStop` positions | `fSoftBreak` positions | Observed double-press selections |
| --- | --- | --- | --- |
| Thai adjacent | 0, 4, 7, 11 | 0, 4, 7, 11 | `[0,4]`, `[4,7]`, `[7,11]`, `[11,15]` |
| Thai spaced | 0, 4, 12 | 0, 4, 8, 12 | `[0,4]`, `[4,8]`, `[8,12]`, `[12,16]` |
| Lao adjacent | 0 | 0 | `[0,15]` throughout all 14 available gestures |
| Khmer adjacent | 0, 2, 4, 8, 9, 11, 13, 17 | 18 | `[0,18]` throughout all 16 gestures |

In the Thai spaced case, actual hit index 8 selects the preceding `[4,8]` range;
hit 9 selects `[8,12]`. Dragging left preserves the original selection end, and
returning to the anchor restores its original range. Both quarters requested at
Lao UTF-16 index 13 are unavailable; no native steps or substitute carets were
invented. Cluster-coordinate requests may resolve to a different native index.

Khmer disproves selecting `fWordStop` merely because `fNeedsWordBreaking` is true:
its interior word stops do not delimit EDIT's observed selection. These cases
distinguish the two native flag inventories, but do not establish a general
portable selection algorithm, dictionary equivalence or mixed-script policy.
In particular, ProGPU's existing generic UAX #14/cluster services are not proof
of the Thai dictionary-dependent boundaries observed here. Keep the production
word-boundary capability unselected until its real complete-source classifier
and original-generation lifetime contract are implemented and qualified.

## Owned boundary source seam

`ILibreEditWordBoundaryService` explicitly declares that its owned layouts supply
`ILibreEditWordBoundaryLayout`. A snapshot contains strictly increasing original
UTF-16 positions, including both endpoints, and a leading-content boundary from
the same immutable layout generation. The source validates this once per press;
missing or malformed declared capability rejects before selection. An undeclared
service retains the existing character-selection path.

The source retains the original raw hit, initial endpoints, signed anchor and
selection/layout/press identity through reversals. Actual row starts affect the
initial boundary choice, never clamp the word. Public selection replacements
(including the same range) retire the lease; provider reentry cannot publish an
obsolete selection or retire a nested replacement press. Password selection
bypasses this service entirely.

`CanonicalEditWordBoundaryTests` adds 24 authored fresh-process cases using real
retained source geometry and literal boundaries from the native observations.
These check source dispatch and lifetime, not a boundary classifier: the fixture
copies its boundary memory into the layout and does not enable the actual ProGPU
provider. They have not been executed locally. The PR remains draft until the
real provider and its focused regressions connect this seam to ordinary controls.

## Retained native provider connection (explicit source-build checkpoint)

`LibreWinFormsEnableNativeEditWordBoundaries=true` compiles the provider adapter
against ProGPU's retained `DrawingTextLayout.GetEditWordBoundaries` API. It is
off by default: the current qualified submodule is unchanged. The adapter queries
only its original owned drawing layout, copies the complete validated UTF-16
inventory once, and retires that cache with the layout. It never accepts another
source string/direction, reshapes a prefix, filters a grapheme-interior boundary,
or falls back after native rejection. `ProGpuEditWordBoundaryException.Result`
preserves the exact typed native status/error. The adapter never promotes a
rejected property domain or invents inventories for unqualified observations.

The ordinary `ProGpuTextRendererService` still does **not** declare
`ILibreEditWordBoundaryService`. Only explicit source fixtures declare it. The
build opt-in is an implementation checkpoint, not complete TextBox support; it
must be exercised against exact dependency source and then removed/enabled as
part of qualified dependency integration, not left as a substitute release gate.

`ProGpuEditWordBoundaryCaptureTests` checks the actual adapter source with the
opt-in enabled: 18 device-free cases cover owned memory, complete inventories,
interior UTF-16 positions, malformed publication and precise rejection. These
are transport/implementation controls, not new independent Microsoft cases.
`CanonicalNativeEditWordBoundaryTests` authors actual source-dispatch tests
through the real provider (generation reuse/retirement, original ASCII drag,
unknown-symbol atomic rejection and password-source exclusion). Those native
tests require a qualified owned runtime and have not been run locally.

Interior selection **indices** remain exact, but interior selection **geometry**
is not qualified. The retained modern interaction can return a nearest
caret stop and selects whole intersected cluster selection boxes. The original Win11
emoji inventory contains position 7; the separately observed Server2025 joiner
inventory contains position 4. Neither may be converted into another index.
Source painting avoids a caret query when no caret is needed; when caret painting
or explicit scrolling actually requires one, an EDIT-capable provider returning
a different endpoint now rejects instead of silently publishing that geometry.
The selection and original layout remain retained. The actual formatted/font
generation tested here returns source indices 7 and 4 unchanged; that is not
native geometric parity. Six authored actual-source cases preserve indices
through dispatch/paint/scroll and separately reject explicitly substituted provider
stops; they do not assert nearest-stop or whole-cluster geometry as native
EDIT parity. Local ARM64 managed-source validation against ProGPU's exact retained
API commit `ce48bd4a05fd7230c317203e60fee194a262b838` passed all 18 adapter controls
and all 30 source cases (the original 24 plus these six), without failures/skips.
The canonical source build used SDK `11.0.100-preview.5.26302.115`, the explicit
opt-in and `NetCurrent=net10.0`; it completed with zero errors and existing source
warnings. The two original multiline hit fixtures now choose an interior point
from their retained owning source-row frame, preserving every original expected
hit/range rather than rounding a caret onto another row edge. None of this used
the native classifier, GPU, a VM or staged runtime. Actual hit-to-interior placement,
partial selected ink, caret/scroll mapping and native UI remain required.

## Explicit retained EDIT geometry checkpoint

The source-build opt-in also implements `ILibreEditTextInteractionLayout` over
the same retained Drawing generation. TextBox selects this view only when the
service explicitly declares the EDIT word-boundary marker and the layout carries
the matching boundary capability. Ordinary services, WPF navigation and the
qualified submodule/default build remain unchanged; ProGPU's ordinary service
still does not declare that marker.

The original Windows selection-geometry receipt from run `36903729345`, SHA-256
`301650a7ba564d28783fc98d954fa23b4f71e16a9a6e19f4454bca927d2630c9`, independently
shows identical selected prints for emoji ranges 4–7/7–9/4–9, joiner ranges
1–4/4–6/1–6 and combining ranges 7–8/8–9/7–9. Interior caret/source X values
coincide with the owning end while every original selection index stays intact.
Three new actual-source tests preserve those exact relationships through
painting, scrolling and `EM_POSFROMCHAR`; they do not copy absolute native font
metrics or claim BGRX pixel parity.

ProGPU captures the original managed writer's graphemes before fallback and
shares that writer's existing segmentation helper. The explicit profile joins
only contiguous advance intervals with one original owner, row and bidi frame.
Selection covers the whole owner; an interior caret retains its requested index
at the actual retained trailing edge. An interior source-point query uses that
trailing X but keeps the original source row Y, not a native caret raster offset.
True source boundaries delegate the unchanged ordinary source-position mapping.
The source message retains its existing single-line Y=0 and client/scroll mapping.
No prefix shaping, interpolation, index snapping or query-time segmentation occurs.

Missing/malformed ownership, vertical frames, owner gaps, cross-row/bidi or
noncontiguous topology, missing/ambiguous trailing edges and shaping clusters
spanning different original graphemes reject explicitly. Cross-grapheme
ligatures and full classifier-domain/native package/editor qualification remain
release requirements; rejecting them is not completed ordinary TextBox support.
The previous geometry limitation above records the older `ce48bd4a` checkpoint,
not approval of nearest-stop or split fallback rectangles.

Managed actual-source validation against the new retained geometry checkpoint
passes all 30 unchanged word-boundary source cases, all three new geometry cases
and all 18 adapter cases, with zero failures/skips. The same 30 source cases ran
in two disjoint 15-case batches under unchanged 30-second diagnostic deadlines;
an earlier aggregate run terminated at that deadline after 29 successes and no
assertion failures. The final canonical build used SDK
`11.0.100-preview.5.26302.115`, `NetCurrent=net10.0` and the explicit opt-in,
with zero errors and existing source warnings. An opt-in-disabled adapter build
against the older `ce48bd4a` managed API also has zero warnings/errors. These
checks use no native classifier, staged runtime, GPU, desktop input or VM.

## Qualified retained API and native source integration

The submodule now pins ProGPU `60347a5f1026b8f95582535e6b7cfc8431a04499`,
whose whole Build `36915664259` passed all 59 jobs and 74 checks. The obsolete
`LibreWinFormsEnableNativeEditWordBoundaries` product compilation switch and
conditional adapter branches are removed. The real retained adapter and managed
geometry controls compile in ordinary builds. This supersedes the source-build
checkpoints above; `ProGpuTextRendererService` still does not advertise
`ILibreEditWordBoundaryService`.

Actual native CPU tests have the separate test-project selection
`LibreWinFormsTestNativeEditRuntime=true`. That selection requires a qualified
native runtime supplied by the caller; it does not change any product capability
or permit missing-library skips/fallback. The ordinary source lane remains
device-free until its runtime staging is connected.

Local ARM64 validation builds the actual pinned source with repository SDK
`11.0.100-preview.5.26302.115`, `NetCurrent=net10.0`,
`LibreWinFormsUseProGpuSystemDrawing=true`, `LibreWinFormsReferenceMode=Project`
and that native-test selection. It passes all 18 adapter cases, the unchanged
30 source cases in two disjoint 15-case batches and all three selection-geometry
cases. Each batch retains its 30-second test deadline and no skips.

The four actual-native source cases pass against the staged osx-arm64 library
from Build `36915664259` (package version `3435.ci`), SHA-256
`53be0560d745941f5b3d8c454c0da5e547cc6907c5669560aa1e9ce8339d44a0`.
Its loader dependency is the matching ARM64 Silk.NET.WebGPU.Native.WGPU 2.23.0
asset. No GPU/device or new native build is involved. The cases retain original
ASCII drag/reversal and layout retirement, password-source exclusion and typed
classifier rejection before selection/public notifications. The old U+3200
rejection is now the independently observed positive `[0,4]` inventory for
`a\u3200b `; the exact ProGPU package control preserves the same literal result.
U+327F remains the valid-Unicode `UnqualifiedBmpSymbolPolicy` source rejection,
including proof that the retained provider performed the native boundary query.

The ordinary marker remains blocked by real cross-grapheme geometry. The actual
macOS Arial file SHA-256
`525979822591a3447cfc49d943d6f7683508e25543407871c0ed8fed05fd2bd9`
shapes lam-alef into glyph 1019 at source cluster zero. Native words succeed with
`[0,2]`, but the EDIT caret at source index 1 and hits inside `[0,2)` reject
unretained interior ownership. For `alpha \u0644\u0627 beta `, words succeed
with `[0,6,9,14]`; unrelated source carets work while caret 7 and hits in `[6,8)`
retain the same explicit rejection. The tested Inter/Times/Georgia/Hoefler/
Baskerville `fi` strings did not actually form ligatures and are not evidence
for that contract. Original Windows lam-alef caret/hit/selected-ink observations
are still required before implementing or selecting ordinary EDIT interaction.

The existing original `NativeTextBoxReference` now has a separate
`--selection-geometry` invocation. It reuses the owned EDIT geometry observer
from the pinned ProGPU source and the Forms probe's existing owner, message,
selection and source-position helpers. Its original five emoji/joiner/combining/
bidi inputs and directed requests stay unchanged. Four additional inputs use
explicit Arial 20-pixel fonts: lam-alef alone and `alpha \u0644\u0627 beta `,
each in LTR and RTL. Requested source seams are `[0,1,2]` and `[6,7,8]`; no
caret or selected-pixel positions are asserted in advance.
The original cases retain all 19 observations. Each new case records 11 directed
selection observations with complete native hit scans; it does not require a
distinct terminal source-position pair to manufacture a gesture coordinate.

Each input retains all directed selections, before/after-scroll states, raw
`EM_POSFROMCHAR` results, every integer `EM_CHARFROMPOS` result in its native row,
actual owned caret coordinates and two independently cleared `WM_PRINTCLIENT`
byte buffers. The borrowed HFONT/LOGFONT, managed descriptor and original
`GetFontData` bytes/hash identify the base font. Fallback font identity remains
explicitly unqualified. Only the added inputs use a 300-by-60 target to stay
within the unchanged 128-MiB receipt bound; the original five targets remain
480-by-140. The 30-second observer, 60-second process and ten-minute job deadlines
are unchanged. The existing Windows reference job retains separate CreateNew
word and geometry receipts and verifies loaded-module/source hashes, exact
font bytes, source identity, callback order, caret ownership, full hit scans and
both pixel buffers. Ten offline corruption controls and a zero-warning/error
cross-build of the original Microsoft Windows probe pass locally. The new
Windows observations have not yet run and do not qualify an interior-caret rule.

## Password source selection

An admitted password double press uses canonical `SelectAll` once, before the
derived/public mouse notifications. It does not hit-test, create another layout,
or send password source to a text provider. Held moves retain that range without
reselecting. The lease belongs to the original press, text, focus, selection and
layout generation; a callback replacement retires it even if capture remains.
Release, lost capture and control disposal also retire the lease. Ordinary
single-click caret placement and character dragging remain unchanged.

`CanonicalTextWordSelectionTests` adds 13 focused source cases through the actual
provider input adapter: both mask modes, read-only selection, drag reversal,
callback overrides and exceptions, cancellation, capture/handle replacement,
selection reentry and the next ordinary press. Build `36688369362` passed all nine
jobs at `e7cf4da37ee5dee73df1d0311f6a067eeb911873`; its canonical source suite
passed 898 cases with no failures or skips, including these 13 cases. This is
hosted source/package evidence, not a local desktop validation run or validation
of the newer, unexecuted boundary-source seam.

This is the confirmed password special case, not the unresolved unmasked word
classifier. The implementation PR stays draft until general source word behavior
and its focused regressions accompany the reference. A successful reference
process alone is not a product fix. Ordinary Silk double-click classification, wheel policy,
native factory admission, IME and full platform/UI qualification remain separate.

## Native documentation boundary

Microsoft documents the edit-control word-break callback and its UTF-16 indices,
including atomic CRLF and CRCRLF treatment, but this is not a complete oracle for
double-click whitespace, punctuation or drag reversal. See
[EditWordBreakProcW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nc-winuser-editwordbreakprocw)
and [EM_SETWORDBREAKPROC](https://learn.microsoft.com/en-us/windows/win32/controls/em-setwordbreakproc).
The observed native selections, not an inferred wrapping algorithm, determine
the source selection policy.
