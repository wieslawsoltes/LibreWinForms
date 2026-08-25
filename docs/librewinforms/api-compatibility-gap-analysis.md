# LibreWinForms API Compatibility Gap Analysis

Date: 2026-08-20

Repository revision: `7087611f2145b2dc09bac8e78ce239b1fe819259`

Compared contract: .NET 10.0.11 `Microsoft.WindowsDesktop.App.Ref`

## Executive conclusion

LibreWinForms contains the full upstream WinForms source tree, but the portable package consumed by `LibreWinForms.Sdk` is not built from that tree. The shipping `System.Windows.Forms.dll` is built from a separate, approximately 26,000-line compatibility implementation under `src/LibreWinForms.Portable`. The SDK also replaces `System.Drawing.Common` with the ProGPU implementation, whose printing surface is currently only a small subset of the original API.

Therefore, the statement “the repository contains the full WinForms source” is true, while the stronger statement “the portable package is the full WinForms source with platform-specific implementations” is currently false.

The properties reported in issues [#10](https://github.com/wieslawsoltes/LibreWinForms/issues/10) through [#18](https://github.com/wieslawsoltes/LibreWinForms/issues/18) are not isolated omissions. They expose three structural problems:

1. The portable assembly is a separately authored compatibility surface, so upstream APIs do not arrive automatically.
2. Important upstream inheritance spines, especially `DataGridViewElement` and `DataGridViewBand`, are absent. Properties inherited from those types disappear together.
3. CI exercises selected applications and behavior scenarios but does not enforce the official WinForms public contract.

The correct direction is defined in the [source-first cross-platform plan](./source-first-cross-platform-plan.md): replace the copied compatibility implementation with builds of the canonical managed source and put platform behavior behind typed seams. The immediate fix should be to make that direction measurable with an API-compatibility gate, then migrate APIs by coherent source-owned subsystems.

## Implementation update: 2026-08-25

The source-first plan is now implemented as an active shadow graph on [LibreWinForms draft PR #27](https://github.com/wieslawsoltes/LibreWinForms/pull/27). The canonical `System.Windows.Forms` project can opt into source-project references to the ProGPU drawing stack without changing its `System.Windows.Forms` assembly or namespace identity. The ordinary NuGet/package reference mode remains the default, and the existing portable implementation remains temporarily available as a frozen comparison lane until the documented runtime and SharpDevelop cutover gates pass.

The coordinated drawing dependency is [ProGPU draft PR #140](https://github.com/wieslawsoltes/ProGPU/pull/140), pinned by the LibreWinForms submodule at exact commit `bd1c836d04af5613c50f3d4dc846aabb38ff1574`. ProGPU now has a suppression-controlled ApiCompat gate against `Microsoft.WindowsDesktop.App.Ref` 10.0.11. Its current reviewed result is 55 missing types, 319 missing members, 47 other shape diagnostics, and 421 total diagnostics, down from 1,052 total in this report's original audit, with no breaking changes or stale suppressions. Missing-type reductions can expose member diagnostics that were previously hidden, so subsystem completion and suppression diffs matter more than any one subtotal.

Implemented drawing groups include complete known-color brush/pen properties with allocation-free warmed caches, retained `Region` boolean geometry and clipping, a functional allocation-free warmed affine `Matrix` layer, functional `Blend`/`ColorBlend`/`LinearGradientBrush` state and typed rendering lowering, and retained `GraphicsPath` plus `GraphicsPathIterator` contracts with source-compatible point/type data and traversal, shaped text outlines, cardinal curves, cloning/composition, transforms, analytic bounds, fill and outline hit-testing, widening, perspective/bilinear warping, reversal, and adaptive flattening. Stroke expansion, path deformation, and shaped TrueType/CFF outline materialization are reusable typed ProGPU services with behavior, allocation, ApiCompat, and BenchmarkDotNet gates. Additional `Graphics`/image/font/icon APIs required by canonical WinForms, functional `ColorMap` remapping and defensive `ImageAttributes` state, clone-safe palette/property metadata, deterministic fixed and CPU-only optimal palette generation, complete `PixelFormat` and `ImageFormat` identities, typed scan0/caller-owned `LockBits` row conversion across packed/indexed/high-depth formats, functional `ConvertFormat` palette/alpha-threshold/ordered/spiral/error-diffusion quantization, truthful managed codec discovery, owned encoder parameters, functional PNG/BMP/JPEG saves with typed JPEG quality selection, CPU-only image resolution/tag/frame/bounds contracts, a managed buffered-graphics model, and a managed printing/controller/event model whose unavailable native operations fail at explicit platform boundaries are also present. The formatted-text group now includes exact string/span overload shapes, shaped cluster ranges, advanced direction/tab/digit/fallback/trailing-space behavior, cross-font visible format-control representatives, retained mnemonic underlines, whole-line `LineLimit`, and slash-aware `EllipsisPath`; the complete hosted drawing suite passes 150/150. Canonical source builds exercise these APIs directly, and the exact pinned `System.Drawing.Common.dll` is copied byte-for-byte into the canonical output. Exact source checkpoint `e181da7e32123d85807b32c93793a9b2b0dfcc31` passes the ordinary package lane, canonical source/submodule validation, package inspection, and fresh-cache source-package consumption. Remaining ProGPU incompatibilities and WinForms platform seams are recorded in the source-first plan and generated ApiCompat artifact rather than being filled with compatibility-only stubs.

## Scope and evidence

This report examined:

- the discussion in tracking issue [#9](https://github.com/wieslawsoltes/LibreWinForms/issues/9), including [the API-comparison request](https://github.com/wieslawsoltes/LibreWinForms/issues/9#issuecomment-5361043002);
- individual missing-API issues #10, #11, #12, #14, #15, #16, #17, and #18;
- the closed sample-driven fixes in [#3](https://github.com/wieslawsoltes/LibreWinForms/issues/3) and [#4](https://github.com/wieslawsoltes/LibreWinForms/issues/4);
- the portable package and SDK project files;
- the canonical WinForms and System.Drawing sources present in this repository;
- the ProGPU `System.Drawing.Common` source used by the portable SDK;
- the LibreWinForms CI workflow; and
- assembly-level output from Microsoft's `Microsoft.DotNet.ApiCompat.Tool` 10.0.400.

The official .NET documentation describes ApiCompat as a tool for comparing an implementation assembly with a contract/baseline assembly and for recording known differences in a suppression file: [API compatibility tools](https://learn.microsoft.com/dotnet/fundamentals/apicompat/overview), [assembly validation](https://learn.microsoft.com/dotnet/fundamentals/apicompat/assembly-validation), and [global tool reference](https://learn.microsoft.com/dotnet/fundamentals/apicompat/global-tool).

## What is actually shipped

### The full source tree exists but is not compiled into the portable package

The canonical project [System.Windows.Forms.csproj](../../src/System.Windows.Forms/System.Windows.Forms.csproj) builds the upstream source under `src/System.Windows.Forms/System/Windows/Forms` and uses the normal WinForms dependency projects.

The portable project [LibreWinForms.System.Windows.Forms.csproj](../../src/LibreWinForms.Portable/LibreWinForms.System.Windows.Forms/LibreWinForms.System.Windows.Forms.csproj) instead contains:

```xml
<Compile Include="src\**\*.cs" />
```

Its nearest [Directory.Build.props](../../src/LibreWinForms.Portable/Directory.Build.props) disables default compile items. It does not include or source-link the canonical `src/System.Windows.Forms` files.

At the audited revision, the two source areas have very different sizes:

| Source area | C# files | Lines | Role |
|---|---:|---:|---|
| `src/System.Windows.Forms/System/Windows/Forms` | 1,445 | 361,486 | Canonical upstream WinForms implementation |
| `src/LibreWinForms.Portable/LibreWinForms.System.Windows.Forms/src` | 34 | 26,417 | Portable compatibility implementation actually packaged |

Line count is not an API metric, but the approximately 14:1 difference explains why application-driven additions cannot currently imply general WinForms parity.

The portable implementation was introduced by commit `8831362e7` (`Add portable LibreWinForms runtime and SDK`) as a new, separate set of compatibility files. Subsequent commits have expanded those files in response to SharpDevelop and sample requirements. They have not changed the package to compile the canonical source tree.

### The SDK deliberately selects the portable assembly

[Sdk.props](../../src/LibreWinForms.Portable/LibreWinForms.Sdk/Sdk/Sdk.props) sets `UseWindowsForms=false` for the portable lane. [LibreWinForms.Sdk.targets](../../src/LibreWinForms.Portable/LibreWinForms.Sdk/targets/LibreWinForms.Sdk.targets) then adds `LibreWinForms.System.Windows.Forms` explicitly.

This is necessary to avoid the Windows-only framework reference, but it also means consumers see only the API surface declared by the portable compatibility project. The existence of a same-named type in the canonical project has no effect on compilation.

### System.Drawing is also substituted

The SDK removes the ordinary `System.Drawing.Common` reference and selects `ProGPU.System.Drawing.Common`. Consequently, `System.Drawing.Printing.PrintDocument` comes from ProGPU, not from [the full PrintDocument source](../../src/System.Drawing.Common/src/System/Drawing/Printing/PrintDocument.cs) in this repository.

At the audited revision, ProGPU's `PrintDocument` exposes only:

- `DocumentName`;
- `Print()`, which throws `PlatformNotSupportedException`; and
- `Dispose()`.

The upstream managed type also exposes page/printer/controller state, four events, protected event raisers, and `ToString()`. This directly explains issue [#18](https://github.com/wieslawsoltes/LibreWinForms/issues/18).

## Measured compatibility gap

The portable `System.Windows.Forms` project was rebuilt successfully from the audited revision. Its output and the current ProGPU drawing output were then compared with the .NET 10.0.11 Windows Desktop reference assemblies, using the official reference as the left/contract side and LibreWinForms as the right/implementation side.

Representative command:

```bash
apicompat \
  --left <Microsoft.WindowsDesktop.App.Ref>/ref/net10.0/System.Windows.Forms.dll \
  --right src/LibreWinForms.Portable/LibreWinForms.System.Windows.Forms/bin/Release/net10.0/System.Windows.Forms.dll
```

### Summary

| Assembly | Missing types (`CP0001`) | Missing members on types that exist (`CP0002`) | Other shape incompatibilities |
|---|---:|---:|---:|
| `System.Windows.Forms` | 584 | 4,235 | 271 |
| `System.Drawing.Common` (ProGPU) | 121 | 906 | 25 |
| **Combined** | **705** | **5,141** | **296** |

“Other shape incompatibilities” includes wrong base types, missing interfaces, sealed/unsealed differences, removed virtual dispatch, and assembly version differences.

These are diagnostic counts, not counts of C# property declarations. A property can create separate getter and setter diagnostics, and an event can create separate add/remove diagnostics. Conversely, members belonging to one of the 705 wholly missing types are not also reported as individual missing members. The 5,141 member diagnostics are therefore a lower bound on the total absent surface.

The results establish that the issue is assembly-wide and is not limited to the eight newly reported properties.

## Why the reported members are missing

| Issue | Reported API | Immediate cause | Structural cause |
|---|---|---|---|
| [#10](https://github.com/wieslawsoltes/LibreWinForms/issues/10) | `Label.AutoEllipsis` | Portable `Label` declares a small property subset and omits it. | The canonical `Label` source and text-layout state machine are not reused. |
| [#11](https://github.com/wieslawsoltes/LibreWinForms/issues/11) | `DataGridViewRow.DefaultCellStyle` | Portable `DataGridViewRow` has no such property. | Upstream rows inherit the property from `DataGridViewBand`; the portable type does not inherit that class. |
| [#12](https://github.com/wieslawsoltes/LibreWinForms/issues/12) | `DataGridViewCell.EditedFormattedValue` | The portable cell contains only a reduced editing/value model. | The upstream formatting, inherited-style, and editing-control pipeline is not reused. |
| [#14](https://github.com/wieslawsoltes/LibreWinForms/issues/14) | `DataGridViewColumn.Visible` | Portable columns have no `Visible` state. | Upstream columns inherit `Visible` from `DataGridViewBand`; the portable type derives directly from `Component`. |
| [#15](https://github.com/wieslawsoltes/LibreWinForms/issues/15) | `DataGridView.GridColor` | No property, change event, or renderer consumption exists. | The portable grid uses a task-specific painting model rather than the complete upstream appearance contract. |
| [#16](https://github.com/wieslawsoltes/LibreWinForms/issues/16) | `RowHeadersBorderStyle` and `DataGridViewHeaderBorderStyle` | The property and enum are absent. | The header/border API group was only partially recreated. |
| [#17](https://github.com/wieslawsoltes/LibreWinForms/issues/17) | `Application.ProductName` | Portable `Application` exposes only the startup members needed by current samples. | Application metadata has no typed portable provider, and the canonical implementation is not reused. |
| [#18](https://github.com/wieslawsoltes/LibreWinForms/issues/18) | `PrintDocument` properties/events/methods | The ProGPU replacement is a three-member platform stub. | API presence was coupled to the absence of a portable printer backend. |

### DataGridView is the clearest example

The current portable declarations are structurally different from WinForms:

```text
Official: DataGridViewElement -> DataGridViewBand -> DataGridViewRow / DataGridViewColumn
Portable:                           DataGridViewRow; DataGridViewColumn -> Component

Official: DataGridViewElement -> DataGridViewCell
Portable:                           DataGridViewCell
```

ApiCompat reports all of these base-type differences. Adding `DefaultCellStyle` to `DataGridViewRow` and `Visible` to `DataGridViewColumn` as unrelated auto-properties would make the two sample expressions compile, but it would leave the ownership, inherited style, state change, cloning, shared-row, layout, and painting contracts inconsistent. The fix should begin with the missing element/band hierarchy.

### API presence and platform support have been conflated

A cross-platform implementation can preserve a public type and its managed state even when a final OS operation is unsupported. For example, `PrintDocument` can expose its normal properties and events everywhere, while `Print()` reports that no printing backend is installed on a particular platform.

Omitting the members entirely causes source and binary incompatibility before the application can choose a supported fallback. That is a stronger and usually less useful failure mode than a deliberate runtime `PlatformNotSupportedException` at the platform boundary.

### Tests are application-driven rather than contract-driven

[librewinforms-ci.yml](../../.github/workflows/librewinforms-ci.yml) runs behavior tests, packs the product, and exercises an SDK smoke application. Those are valuable integration gates, but none compares the packaged public assemblies with `Microsoft.WindowsDesktop.App.Ref`.

The canonical project has a 14,576-line `PublicAPI.Shipped.txt`, but the portable project does not opt into that project's public API analyzer configuration and has no portable public API baseline of its own. As a result, CI can remain green while thousands of official members are absent.

Closed issues [#3](https://github.com/wieslawsoltes/LibreWinForms/issues/3) and [#4](https://github.com/wieslawsoltes/LibreWinForms/issues/4) show that sample-driven work is effective for finding important vertical behavior, but it is reactive: only APIs reached by the selected sample are added.

## Proposed fixes

### P0: Add an official API contract gate

Add an API-compatibility project or script under `eng/` and run it in the package lane after building the exact assemblies that will be packed.

The gate should:

1. Pin a supported contract version, initially `Microsoft.WindowsDesktop.App.Ref` 10.0.11 to match the current `net10.0` product.
2. Compare both packaged identities:
   - official `System.Windows.Forms.dll` against LibreWinForms `System.Windows.Forms.dll`;
   - official `System.Drawing.Common.dll` against ProGPU `System.Drawing.Common.dll`.
3. Commit an initial suppression file representing known debt, because failing on all current gaps would make the gate unusable.
4. Fail when a new incompatibility appears.
5. Fail on unnecessary suppressions, so each implemented API must remove its corresponding debt entry.
6. Upload the full diff and a grouped summary as CI artifacts.
7. Run small compile probes for the exact issue expressions, so regressions produce concise errors in addition to the complete diff.

The dashboard should group gaps by assembly, namespace, owning type, and diagnostic kind. It should not open one GitHub issue per missing accessor. Thousands of generated issues would hide dependencies such as the `DataGridViewBand` root cause and make prioritization worse. Create curated subsystem epics from the generated artifact instead.

Suggested issue groups are:

- core application/control/component model;
- layout, input, and windowing;
- text and standard controls;
- DataGridView;
- menus and ToolStrip;
- dialogs, clipboard, and drag/drop;
- design-time and ResX;
- visual styles and accessibility; and
- System.Drawing printing.

### P1: Fix the reported API groups coherently

#### Label

Port the canonical `Label.AutoEllipsis` contract together with the relevant text measurement, layout invalidation, change notification, and clipped-text tooltip behavior. Route the final measurement/drawing through the typed ProGPU text service already used by the host.

Do not stop at an unused auto-property: that would fix compilation but not the behavior expected by existing applications.

#### Application metadata

Expose `Application.ProductName` through a typed, reflection-free application metadata service. `LibreWinForms.Sdk` can generate and register metadata from MSBuild properties such as `Product`, `AssemblyName`, `Company`, and `Version`; native adapters may add explicitly typed executable metadata when available.

This preserves the public property without copying the upstream reflection-based discovery path, which would conflict with LibreWinForms' reflection-free product rules.

#### DataGridView

Treat issues #11, #12, #14, #15, and #16 as one source-migration epic:

1. Port/source-link `DataGridViewElement` and `DataGridViewBand` first.
2. Make `DataGridViewRow` and `DataGridViewColumn` derive from the band implementation and `DataGridViewCell` derive from the element implementation.
3. Port inherited style/state ownership, cloning, row-sharing, and state-change notifications.
4. Port the formatting/editing pipeline needed by `EditedFormattedValue`.
5. Port `GridColor`, `DataGridViewHeaderBorderStyle`, header-border properties, and their change events.
6. Make the portable renderer consume band visibility, inherited cell styles, grid color, and header border style.
7. Add API, state-transition, rendering, designer serialization, binding, virtual-mode, and editing tests.

This replaces several local fixes with the real WinForms object model on which many other missing members depend.

#### Printing

Move the complete managed `System.Drawing.Printing` model into ProGPU's `System.Drawing.Common` output and introduce a narrow typed printing backend, for example:

```csharp
internal interface IPrintingBackend
{
    PrintBackendSession CreateSession(PrinterSettings printerSettings);
}
```

The exact contract should be designed around the upstream print-controller lifecycle, not around a single native printing API. Managed classes, settings, events, and event raisers should exist on every target. A platform with no backend should fail when a real print session is requested, not by deleting compile-time API.

Possible adapters are an explicit Win32 printing adapter, a CUPS/local-OS adapter, and an unsupported adapter with a precise exception. Printing changes must be validated in the ProGPU repository as well as in the LibreWinForms package-consumer lane.

### P2: Make canonical source reuse the default development path

For each migrated subsystem:

1. Compile the canonical project and files under `src/System.Windows.Forms`; do not copy or source-link them into the compatibility project.
2. Preserve official `System.Windows.Forms` runtime identities and `LibreWinForms.*` package branding.
3. Replace direct Win32 calls at narrow, typed seams for windowing/input, painting/composition, menus/popups, clipboard/dialogs, drag/drop, timers, system settings, accessibility, printing, and GDI/GDI+.
4. Keep explicit Win32 and local-OS adapters next to the seam rather than forking the managed object model.
5. Delete the overlapping compatibility declaration when its canonical source group is enabled, so there is one authoritative definition of each type.
6. Run ApiCompat, behavior tests, unchanged Microsoft samples, and SharpDevelop before completing the migration.

A practical migration mechanism is a dedicated portable build configuration for the canonical project graph plus a small, reviewed list of Windows-only source exclusions while their typed seams are introduced. The desired trend is that exclusions, compatibility declarations, and API suppressions shrink together. Packaging should collect the canonical outputs under `LibreWinForms.*` package identities instead of changing their framework assembly identities.

### P3: Prevent upstream drift

After the portable assembly is predominantly source-built:

- record the upstream WinForms commit used by each release;
- automate upstream merge/rebase checks;
- run ApiCompat against every supported target framework contract;
- distinguish unsupported runtime behavior from absent public API in documentation and exceptions; and
- block new app-local WinForms-shaped compatibility types.

## Recommended implementation order

| Order | Work | Reason |
|---:|---|---|
| 1 | ApiCompat gate, suppression baseline, and compile probes | Makes the debt visible and prevents regression immediately. |
| 2 | `Label.AutoEllipsis` and typed `Application.ProductName` | Small, high-value compatibility slices that validate the workflow. |
| 3 | DataGridView element/band hierarchy and the five reported grid API groups | One structural change resolves multiple reports and unlocks many inherited APIs. |
| 4 | Complete managed printing surface plus backend seam in ProGPU | Cross-repository work with a clear public contract and platform boundary. |
| 5 | Remaining controls and infrastructure, grouped by canonical source subsystem | Converts the package from sample-shaped compatibility to sustained WinForms compatibility. |

## Definition of done

For an API group to be considered ported:

- its public signatures match the pinned official contract, including inheritance, interfaces, virtual modifiers, attributes where relevant, and nullable annotations;
- its normal managed state and events behave compatibly;
- platform-dependent work flows through a typed backend;
- unsupported operations fail at the platform boundary with documented behavior;
- designer serialization and common unchanged-source applications compile where the API participates;
- focused behavior tests and the existing SharpDevelop/package smokes pass; and
- the corresponding ApiCompat suppressions are removed.

For the overall source-reuse goal to be considered achieved, the `LibreWinForms.System.Windows.Forms` package must contain the assemblies built by the canonical WinForms project graph. `src/LibreWinForms.Portable/LibreWinForms.System.Windows.Forms` must no longer be a runtime implementation. Merely retaining the canonical tree elsewhere in the repository is not sufficient.

## Final answer to issue #9

An automated comparison is both possible and necessary; it does not require an AI bot. Microsoft ApiCompat can produce a deterministic assembly-level diff on every build. AI may help cluster and explain the results, but the contract gate must be a reproducible build tool.

The output should feed a compatibility dashboard and a manageable set of subsystem issues. The current findings show why: the reported properties share architectural causes, and opening one issue for each of more than five thousand member diagnostics would obscure the source-first fixes LibreWinForms actually needs.
