# Independent Windows theme reference

This standalone project uses the installed Microsoft WindowsDesktop runtime and
its native VisualStyleRenderer. It has no project/package dependency on Libre
frameworks. The disposable hosted runner renders 60 original HEADER/SCROLLBAR
part/state/size combinations over two opaque backgrounds at explicit 96 DPI.
It retains every PNG hash, native property HRESULT/value, content rectangle,
margin and size result, plus runtime/theme/source identity. Unsupported property
reads remain recorded failures; they are never replaced with invented defaults.

The reference creates no application window and injects no input. It changes no
system theme or setting. Its original bounds are 120 captures, 16 MiB of images,
45 seconds of internal observation and a 60-second owned-child deadline. Partial
outputs and original errors are uploaded on failure; there is no retry loop.

Run the `Independent Windows theme reference` workflow explicitly. The results
support #174's theme implementation and comparisons; they do not qualify pointer
gestures, packages, application pixels or another Windows theme/DPI. Native
reference rasterization belongs only to this diagnostic and is not a portable
product rendering path. Local virtual machines are not used.
