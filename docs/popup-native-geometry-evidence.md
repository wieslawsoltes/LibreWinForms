# Popup native geometry evidence

The optional portable popup observer records actual native geometry through
`SilkWindowService.TryGetNativeGeometrySnapshot`, on the existing source UI timer.
It never creates handles, changes focus, performs input or substitutes source
bounds for an unavailable native result. The main form and four public menu
popup objects are observable; the private native ComboBox/ToolTip surfaces are
not fabricated from their logical control handles.

Preparation opts in with `--native-geometry`. Both consumer projects retain the
same `Program.cs`; only Portable compiles the helper. Runtime collection also
requires `LIBREWINFORMS_POPUP_NATIVE_GEOMETRY=1`. The immutable sidecar is written
before the source snapshot with the same PID/sequence. Existing Windows and X11
consumers remain unchanged when the diagnostic is disabled.

`eng/librewinforms-popup-native-geometry.py` compares a source snapshot, its
matching native sidecar, and an independently captured CoreGraphics window list.
Every visible public window must have a unique source/native/content-view
identity, a same-PID independently correlated window number, and an exact native
frame match. Main-window title must match too. The AppKit number is not assumed
to be a global CoreGraphics identity without this correlation.

Native content and frame rectangles stay separate, in raw top-left desktop
points. The recorded source coordinate mode and actual DPI/framebuffer scales
apply the existing `LibreWindowCoordinates` policy, including midpoint rounding
away from zero. Native backing scale must independently equal framebuffer scale.
No ratio is fitted to source/native sizes; no titlebar height, source correction,
coordinate clamp or frame-as-client fallback is allowed. Wrong source geometry
therefore fails even when the independent frame matches.

The reader does not inject input or capture pixels. Its result remains
`qualified=false`; matching files alone cannot prove a currently live process,
fresh desktop state, source painting, interaction, or package provenance. Extra
same-PID native windows remain explicit unmatched inventory. A desktop driver
must still bind captures to its own live child, maintain the original deadline,
verify foreground/point ownership, and collect real interaction/pixel evidence.

The 22 offline reader cases pass, covering stale/malformed snapshots, independent
identity/frame mismatch, unavailable and logical handles, alias rejection,
explicit coordinate policies, nonfinite/overflowed values, bounded input and
unmatched native surfaces. The original shared/X11 suites also pass (13/22).
Observer, service and actual application/package qualification remain separate.
