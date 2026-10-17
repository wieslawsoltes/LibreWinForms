# Portable scrollbar pointer defaults

Portable source ScrollBar now handles a left press on either arrow or track and
tracks thumb drags through the canonical Control input dispatcher. These defaults
run before virtual/public MouseDown and MouseMove notifications. Calling protected
mouse notifications directly does not simulate input. Native Windows retains its
original window procedure.

Paint and hit selection share the existing per-axis DPI, range, page and RTL
geometry. The original 96-state Microsoft geometry fixture remains unchanged.
Arrows use SmallChange, track presses use LargeChange, and thumb movement clamps
to the source scrollable range. Scroll callbacks retain their OldValue/NewValue
contract; a handler's NewValue is authoritative. Thumb release emits
ThumbPosition followed by EndScroll; an arrow/track release emits EndScroll.
These event kinds retain the [WM_HSCROLL contract](https://learn.microsoft.com/en-us/windows/win32/controls/wm-hscroll); exact physical-gesture qualification is separate.

Each press retains the actual source/target handle, press/capture generations,
source range/value identity, parent, bounds and DPI. Source mutation, resize,
relocation, capture loss, disable/hide or replacement input cannot resume an old
press. Default value publication consumes its own generation token before public
callbacks. Reentrant input owns its replacement; callback failures keep their
original exception and retire only the old operation.

Hover and pressed states now reach the existing theme renderer for arrows, thumb
and the selected track region. Hover retains only the exact live source/target
handle; it does not confer press ownership after release. Press paint separately
requires the original press/capture/value generation. Track overlays clip to the
original track, and classic paint uses the existing pressed button/track colors.
No pixel palette or geometry is inferred from a reference screenshot. Capture
loss and leave invalidate stale visual state before public continuations.

The initial change covers arrow/track actions, thumb tracking and source hover/
pressed state. Held-button repeat is implemented in the later section below;
original Microsoft gesture/theme comparison and complete package desktop
validation remain required work in #174. These
source controls do not establish native gesture or theme parity. No release pin
or qualification exception is implied.

Source fixtures construct their actual custom controls only after registering
the owned headless platform. The isolated six-case visual gate reproduced a
setup failure masked by earlier tests in the full suite; its original CI and
local failures remain retained. A factory keeps platform registration, control
construction and disposal in the same fixture lifetime. Visual and custom
pointer/wheel cases must also pass in fresh processes, without prior-test state.

## Independent held-repeat reference

The Windows reference preserves seven original holds, native values/events and
captures, a15-second shared input interval and60-second process deadline. The
owner approved its bounded hosted-startup extension on17 October: the existing
normal-close helper may close one verified account overlay before launch; startup
may send at most two verified Start/Search Escape pairs, perform the existing
single owned-form activation and at most one verified owned-caption click.
No shell activation, foreign caption input or retry is added. The input clock
starts before shell actions and cannot reset before the holds. Including owned
cleanup, the ceiling is43 native action attempts. All65 offline controller,
shell, caption and normal-close guards pass after installation.

Workflow pushes stay read-only. Explicit dispatch with `hosted_startup=true` and
`observe_startup_only=false` selects the approved input. Earlier failed/observed
startup results remain retained; this helper is diagnostic preparation, not a
portable repeat implementation or release acceptance. Local VMs are not used.

The first approved startup run39064135409 completed startup in the original
budget, used one Search Escape pair and the owned-form activation, and made no
pointer move or press. It stopped at11.297seconds because the harness compared
the registered class name literally with `SCROLLBAR`. Original WinForms
`NativeWindow.WindowClass` superclasses system controls with generated names.
The first correction queried `RealGetWindowClassW` for the presumed original
standard control type, still requires exactly SCROLLBAR, records both class
names and rejects changes, failed queries or truncation. Exact PID, handle,
root, foreground, exposed point, geometry and held-input checks remain intact.
See [Microsoft's class-query explanation](https://devblogs.microsoft.com/oldnewthing/20101231-00/?p=11863).
This correction adds no input or timing allowance. All24 held-input/class
controls pass. The original failed artifact12112463458 is retained with SHA256
`e24201b00970caa8705dcae5e57bd3cc820cfa890794212675fa50783542a9de`.

Actual run39065755917 disproved the assumption that RealGetWindowClassW unwraps
these original WinForms controls: both Windows APIs returned the same generated
`WindowsForms10.ScrollBar.app.0.2360855_r3_ad1` class. It stopped before any pointer
action; the original artifact12113635743 and first error remain retained. The
reference now supplies its actual protected base CreateParams.ClassName through
a typed interface on the two original HScrollBar/VScrollBar probes, together
with both exact Windows class-name observations for each actual handle. The
controller requires source class ScrollBar and exact agreement with those
native receipts on every check, retaining PID, root, foreground, exposed point,
geometry, enabled state and input guards. It does not admit a generated-name
prefix or guess the original type from an alias. Original seven controls,
gestures, source processing and15/60-second bounds remain unchanged.

An opt-in startup-phase journal is restricted to read-only diagnostic runs.
The first diagnostic90012a8ebb application receipt reaches ready at0.7694seconds
and exits normally at0.8490seconds, with no input. It does not attribute the
earlier15-second startup failure or qualify any held gesture. The phase switch
is absent from ordinary reference invocation and cannot be combined with input.

## Held repeat implementation

Arrow and page holds now use the source-owned typed timer path described in
[portable-scrollbar-repeat.md](portable-scrollbar-repeat.md). Exact Windows timer
policy, original package gestures and theme appearance remain independent gates.
