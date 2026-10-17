# Independent Windows theme reference

This standalone project uses the installed Microsoft WindowsDesktop runtime and
its native VisualStyleRenderer. It has no project/package dependency on Libre
frameworks. The disposable hosted runner inspects 60 original HEADER/SCROLLBAR
part/state/size combinations over two opaque backgrounds at explicit 96 DPI.
It records the native IsElementDefined result for all 120 requests and renders
only defined elements. Undefined parts retain their requested identities and
explicit non-rendered outcomes; they never select a substitute theme part.
It retains every PNG hash, native property HRESULT/value, content rectangle,
margin and size result, plus runtime/theme/source identity. Unsupported property
reads remain recorded failures; they are never replaced with invented defaults.

The reference creates no application window and injects no input. It changes no
system theme or setting. Its original bounds are 120 captures, 16 MiB of images,
45 seconds of internal observation and a 60-second owned-child deadline. Partial
outputs and original errors are uploaded on failure; there is no retry loop.

The `Independent Windows theme reference` workflow runs on pull requests changing
this reference, or by explicit dispatch after it reaches the default branch. The results
support #174's theme implementation and comparisons; they do not qualify pointer
gestures, packages, application pixels or another Windows theme/DPI. Native
reference rasterization belongs only to this diagnostic and is not a portable
product rendering path. Local virtual machines are not used.

The first hosted inventory stopped after six HEADER part1 captures when Windows
rejected HEADER part2. That original failed run39051901038 and its complete
archive remain retained. Version2 records native unsupported-part outcomes so
the remaining independent requests can be inspected; it does not turn missing
reference images into pixel comparisons or product qualification.
