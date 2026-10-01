# Portable TextBox client frames

The paired Windows popup captures from Forms `31b0fe6` showed a plain portable
TextBox without its source frame. Follow-up original Microsoft EDIT API probes
then distinguished the window-sizing estimate from actual client geometry.
A single blanket inset would get FixedSingle coordinates and handle lifecycle wrong.

## Native reference and source contracts

The checked-in [reference program](../eng/NativeTextBoxReference/Program.cs) creates
owned hidden Form/TextBox handles and fails unless System.Windows.Forms loads
from the original Microsoft WindowsDesktop shared framework. It does not inject
desktop input or claim visible-window qualification. Its receipts record runtime,
architecture, assembly identity, raw metrics, client/window rectangles, formatting
rectangles, character positions, hit results and all ten DrawBorder3D pixel arrays.

The [themed PMv2 receipt](../eng/NativeTextBoxReference/reference/Windows-arm64-PerMonitorV2-themed.json)
and [classic DPI-unaware receipt](../eng/NativeTextBoxReference/reference/Windows-arm64-DpiUnaware-classic.json)
were captured on Windows ARM64 10.0.26200.9457 with Microsoft WindowsDesktop
11.0.0-preview.5.26302.115. The initial geometry probe also ran all six combinations
of DpiUnaware/SystemAware/PerMonitorV2 and visual styles on/off; these two extended
receipts additionally retain lifecycle/source-scaling and every named edge style.

For an outer 120x40 editor at the measured 96-DPI reference:

| BorderStyle | Pre-handle ClientSize | Live actual client | Client origin | Formatting inset |
| --- | --- | --- | --- | --- |
| None | 120x40 | 120x40 | 0,0 | 0,0 |
| FixedSingle | 118x38 | 120x40 | 0,0 | 2,2 |
| Fixed3D | 116x36 | 116x36 | 2,2 | 1,1 |

FixedSingle's WS_BORDER is EDIT client content even though AdjustWindowRectEx
reserves an estimated border. Its live ClientSize setter also retains the source
cached-public-size quirk: requesting 70x18 yields outer/actual client 72x20 but
public ClientSize 70x18. Paint and input use the actual extent, not that cache.
Fixed3D border hits report HTBORDER; FixedSingle border hits report HTCLIENT.
Single-line EM_POSFROMCHAR reports Y=0 even with a nonzero formatting-rectangle top;
multiline source positions include the formatting inset. Source text and UTF-16
indices are never rewritten.

Native SM_CXBORDER/SM_CYBORDER stay one pixel, and SM_CXEDGE/SM_CYEDGE stay two,
at requested DPIs 96, 120, 144, 192, 240 and 288. The portable DPI-specific border
query therefore retains the platform metric instead of multiplying it again.

## Implementation

Plain TextBox declares separate window-adjustment adornments and actual non-client
insets. Source pre-handle sizing and adornment-aware scaling use the estimate;
live client mapping uses actual insets. No border is inferred from Padding or for
other source control classes. Native top-level decoration remains backend-owned.

The frame arithmetic and painting now live in `PortableEditFrame`. Plain TextBox
keeps its original explicit overrides and retained-layout invalidation;
[MaskedTextBox](portable-masked-text-paint.md) explicitly selects the helper for
its same source EDIT class/styles. This does not infer frames for RichTextBox or
add a native masked-editor qualification claim.

Both flat and retained trees paint Fixed3D in window coordinates and clip ordinary
content/children to the translated actual client frame. FixedSingle paints its
client border before foreground and child content. Screen conversion, pointer
routing, CreateGraphics, invalidation and background fill use the same actual frame.
Non-client borders stop click-through without fabricating client MouseDown;
existing capture still receives outside-client input.

Retained layout, glyph/selection/caret origins, scrolling and pointer/source geometry
share the formatting viewport. Layout caches include its actual available extent.
ToolStripTextBox reuses the original professional painter's focus/hover/disabled/
high-contrast policy. Source ControlPaint DrawEdge uses integer bands and the
measured native colors, with right/bottom ownership of shared corners.

## Reproduction and qualification

On Windows with the repository's .NET 11 SDK, build the isolated reference project:

```powershell
dotnet build eng/NativeTextBoxReference/NativeTextBoxReference.csproj -c Release
dotnet eng/NativeTextBoxReference/bin/Release/net11.0-windows/NativeTextBoxReference.dll fresh-receipt.json PerMonitorV2 true
```

Use a new output name for each mode/theme combination; the program refuses to
overwrite a receipt. Its project disables repository build/dependency imports so
the reference cannot silently become a portable source consumer.

The focused gate contains 37 cases: the original 16 frame cases plus ten complete
8x8 native edge pixel comparisons, six DPI metric cases, three lifecycle cases,
border click-through rejection and retained source-geometry offsets. Existing
bordered editor tests retain selection, navigation, caret, clipping and scrolling
coverage with the measured one-pixel formatting inset. The full source suite has
772 cases: all passed locally on macOS ARM64/.NET 10.0.9, with zero failures or
skips. The focused 37-case gate also passed. The final source-test build and the
isolated Windows-reference cross-build had zero warnings/errors; docs, shell
syntax and diff checks passed. Exact-head full CI remains required before merge.

This is not modern Windows theme parity, complete non-client behavior, font/line
metric parity, or fresh desktop package qualification. Single-line paint-height,
themed/focused borders and rich/masked editor frames are not qualified by the API
fixtures. Prior screenshots diagnose the old package; they do not validate this
implementation. Fresh paired exact-package Windows captures and corresponding
Linux/macOS checks remain required. ProGPU issue 197 remains open.
