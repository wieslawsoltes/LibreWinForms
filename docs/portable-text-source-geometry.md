# Portable TextBox source geometry

Plain portable TextBox now connects the canonical character/physical-line APIs
to its existing owned ProGPU paragraph: `GetCharIndexFromPosition`,
`GetCharFromPosition`, `GetPositionFromCharIndex`, `GetLineFromCharIndex`,
`GetFirstCharIndexFromLine` and `GetFirstCharIndexOfCurrentLine`.

The original public methods keep argument checks, handle creation, virtual
WndProc dispatch, packed signed coordinates and the public last-character clamp.
Portable EM_CHARFROMPOS, EM_POSFROMCHAR, EM_LINEFROMCHAR and EM_LINEINDEX messages
use an explicit optional `ILibreTextSourceGeometryService` capability. Existing
provider interfaces are unchanged; a missing capability fails explicitly.
Native Windows message processing is unchanged.

The same retained generation supplies glyphs, actual physical rows, original
UTF-16 source ownership, cluster edges, bidi alignment and target DPI. There are
no source newline scans, prefix measurements or independent shaping. Queries
apply the already-published viewport offset without scrolling, repainting,
editing selection or transferring focus. Password layout sees only its mask.

CRLF units belong to their preceding row; wrapped source boundaries belong to
the following row. Current-caret queries preserve the retained wrap affinity.
EM_LINEFROMCHAR(-1) uses the selection beginning when selected, while
EM_LINEINDEX(-1) uses the signed active caret. Blank and trailing rows retain
their real source indices. Cluster interiors use their existing leading edge,
never an invented glyph/caret position. Single-line zero-row results and
out-of-client packed-result normalization retain the canonical public policy.

Fifteen focused canonical cases exercise the real ProGPU provider and the
original public source methods. Coverage includes hard breaks, wrapping,
selection direction, password isolation, DPI/generation reuse, scroll-before-
paint, bidi/alignment, cluster interiors, argument/handle behavior and virtual
message interception. The short-row hit regression exposed a shared ProGPU
defect; its fix selects the actual row before horizontal cluster proximity.
The source-first minimum rises from 656 to 671 without relaxing deadlines or
skip policy. Local logs are retained under `artifacts/text-geometry`.

The final macOS ARM64 canonical build completed with zero errors. All 15 focused
cases and all 671 full canonical cases passed with zero failures/skips against
ProGPU `741e0502ce29de2b6491e33282119204a2bde4cd`. The initial focused run passed
14 and failed the short-row regression; those failure logs are retained too.

This depends on ProGPU #207 and LibreWinForms #94. RichTextBox/MaskedTextBox
specific geometry, native Windows/Linux/macOS appearance and behavioral
differentials, IME and full package/release validation remain separate work.
These source cases do not qualify those broader contracts.
