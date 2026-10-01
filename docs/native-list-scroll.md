# Native quantities in the canonical ListBox

The optional native-scroll source dispatcher now has an actual `ListBox`
consumer. This is also the existing `ListBox` hosted by the portable ComboBox
popup; it does not create a second list renderer or change popup selection.
Ordinary Windows processing and the legacy `MouseWheel` path are unchanged.

## Original source units and bounds

The admitted portable list already paints and hit-tests integer, fixed-height
rows using the same `FontHeight` exposed by `GetItemHeight`. Its viewport is the
client rectangle minus the existing source border. `TopIndex` is clamped to
`max(0, Items.Count - max(1, viewport.Height / rowHeight))` by the existing source
setter. Native consumption preserves that exact contract.

- Lines accumulate fractional row counts. Positive Y moves toward the first row.
- Points use the captured native-to-source `PointScale`, retaining the remaining
  distance in source pixels until a complete actual row height is accumulated.
  They do not become synthetic 120-unit wheel events or smooth fractional-row
  offsets. Painting, item rectangles and hit testing remain row-aligned.
- Reversing direction drops the old direction's fraction. Outward motion at
  either exhausted bound leaves no hidden debt. Empty/short lists stay at zero.
- A horizontal component rejects this consumer before any row mutation. The
  shared dispatcher may select only an ancestor supporting the whole vector.

Only the existing Normal, single-selection, single-column mode is admitted.
Owner-drawn/variable-height rows, horizontal scrollbars, multiple selection,
custom tab offsets and always-visible scrollbar controls remain unsupported.
Changing `ItemHeight` metadata in Normal mode does not invent a row metric.

## Same-frame ownership

The source-private frame retains an ownerless immutable identity and a *weak*
`ItemArray` reference, together with its version/count, viewport, actual row
height, source device DPI and current top index. A retained carry therefore does
not root a detached list through its collection. The shared dispatcher continues
to own stream/provider generation, source/consumer handles, scale/unit identity,
phase cancellation and exact momentum targeting.

Collection/font notifications retire the old frame. `ItemArray.SetItem` does not
increment its version, so the portable ListBox replacement path explicitly
retires the frame immediately after replacement, before formatting or selection
callbacks can pump more input. The global ItemArray contract is unchanged.
Ordinary TopIndex changes also retire old fractions, including a change away
from and back to the same numeric index.

An accepted native plan publishes its expected identity before the original
TopIndex setter invalidates the control. After callbacks it checks the exact
dispatch and source frame again. A failure or obsolete plan retires only its
original carry; nested new input survives, already-applied source writes remain,
and the original callback exception is preserved. Scrolling does not change
selection or commit/close a ComboBox popup.

## Bounded source checks

Actual canonical Forms source compiled on macOS ARM64 against the unchanged
qualified ProGPU pin `0d33ef68aaf9c9c58685f449e6ab386f5156fce5`:

- Source build: zero errors, 633 existing source/analyzer warnings.
- Test build: zero warnings/errors.
- 29 new `NativeListScroll_*` cases passed, zero skips. These include the actual
  hosted ComboBox list, points/lines, exact row/hit geometry, empty/short/bounded
  lists, reversal, font/viewport and real device-DPI changes, captured point-scale
  changes, collection/replacement invalidation before callbacks, native generation
  and cancellation, momentum outside the list, callback errors/nested input,
  disposal/replacement, unsupported modes and whole-vector admission.
- 76 unchanged selected scroll/ListBox/ComboBox cases passed, zero skips, including
  all 41 existing native-scroll cases and the original legacy-wheel behavior.

The source build used the repository SDK with `NetCurrent=net10.0`,
`MicrosoftNETCoreAppRefPackageVersion=` (empty),
`LibreWinFormsUseProGpuSystemDrawing=true`, `LibreWinFormsReferenceMode=Project`
and `LibreWinFormsProGpuSourceRoot` pointing at that exact qualified managed source.
After initial dependency compilation, owned source/test rebuilds used
`--no-restore -p:BuildProjectReferences=false`. Focused execution used the actual
`LibreWinForms.CanonicalLifecycle.Tests.dll` with:

```text
--filter-method '*NativeListScroll_*' --minimum-expected-tests 29 --fail-skips on --timeout 2m --no-progress --no-ansi
--filter-method '*NativeScroll_*' '*PortableListBox*' '*ComboBoxDropDown*' '*ComboBoxActualPopupPointer*' --minimum-expected-tests 41 --fail-skips on --timeout 2m --no-progress --no-ansi
```

These are source-dispatch and retained ownership checks using the existing
headless platform and typed native input test provider. They do not qualify
physical trackpad behavior, native Cocoa popup presentation, smooth pixel
scrolling, automatic factories/modality or complete application/package UI.
No native/GPU/VM execution, runtime staging, pin update or CI dispatch occurred.
