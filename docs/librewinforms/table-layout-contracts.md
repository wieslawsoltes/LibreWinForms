# Canonical TableLayoutPanel contracts

Issue [#21](https://github.com/wieslawsoltes/LibreWinForms/issues/21) described a
declaration-only `TableLayoutPanel` in the old preview. The canonical runtime
uses the complete shared `TableLayout` engine, not that retired compatibility
implementation. The following fixtures qualify a bounded portable layout
contract without changing product code or claiming full layout parity.

The same `CanonicalApiContracts.cs` file compiles against the canonical source
graph and both isolated SDK consumers in `eng/librewinforms-pack-source-first.sh`.
Each source case has its own lifecycle test. The source-first minimum is 150
tests: the previous 147 plus these three, with no previous fixture removed.
All children are real `Panel` or `TableLayoutPanel` controls. Sizes and margins
are explicit; no font measurement, native-window pixels, substitute controls,
or implementation-derived expected values are involved.

## Independent geometry

| Contract | Inputs | Required result |
| --- | --- | --- |
| Mixed absolute/auto/percent sizing | Client 320×120; padding left/top/right/bottom 10/8/14/12; columns absolute 60, auto, 25%, 75%; auto child 40 wide with margins 3+5 | Content 296×100 at (10,8); columns 60/48/47/141; rows 40/60. The remaining 188 divides exactly 47:141. |
| Spans and RTL | Client 200×120; columns 40/60/100; rows 30/50/40; first child spans two rows and columns with margins 3/4/5/6 | LTR bounds (3,4,92,70); RTL bounds (105,4,92,70). Logical cell (1,1) still resolves the spanning child. Changing its column span to one produces (165,4,32,70) and releases cell (1,1). |
| Nested invalidation | Outer client 240×100; auto + percent columns. Inner auto-sized table: leaf 30×12, margins 1/2/3/4, fixed second column 20, padding 2/3/4/5 | Inner size (30+4+20+6)×(12+6+8) = 60×26; outer remainder width 180. Growing leaf to 50×22 gives inner 80×36 and remainder 160. Restoring the leaf restores every original bound. |

The mixed case repeats layout to exercise retained results. The span case checks
actual child bounds, track sizes, logical ownership, and invalidated assignments,
including asymmetric mirrored margins. The nested case first warms the real
preferred-size cache, then requests only outer layout after changing the leaf;
it does not manually measure or lay out the inner table to repair invalidation.

## Qualification boundary

Run `eng/librewinforms-source-first.sh` for the full source gate and
`eng/librewinforms-pack-source-first.sh` for fresh source-produced packages and
both SDK modes. The latter retains exact source/package hashes, isolated cache
consumption, and the drawing-identity checks; these tests do not introduce a
package fallback.

These deterministic managed geometry checks do not qualify every automatic
placement, percent/AutoSize overflow, DPI scaling, native rendering, or complex
nested layout case. A passing fixture is not by itself a blanket closure of the
historical issue or a claim that already-published preview packages contain the
new qualification.
