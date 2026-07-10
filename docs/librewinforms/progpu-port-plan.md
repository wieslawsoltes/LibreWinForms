# LibreWinForms ProGPU Port Plan

## Goal

LibreWinForms is the cross-platform WinForms companion to LibreWPF. It should let existing WinForms-dependent code, including SharpDevelop, move from Windows-only WinForms to a ProGPU/Silk.NET platform without source changes where practical.

## Current lane

- Submodule branch: `librewinforms-progpu-port`.
- Package branding: `LibreWinForms.*`.
- Runtime API identities stay compatible with WinForms assemblies such as `System.Windows.Forms` and `WindowsFormsIntegration`.
- Package identities are present for `LibreWinForms.System.Windows.Forms`, `LibreWinForms.WindowsFormsIntegration`, and `LibreWinForms.Sdk`.
- `LibreWinForms.System.Windows.Forms` and `LibreWinForms.WindowsFormsIntegration` now build source-owned framework-identity assemblies from the portable compatibility implementation. They no longer need to depend on `LibreWPF.WinFormsCompat.*` alias packages in the source-owned lane.
- SharpDevelop is already switched to `ProGpuWpfUseLibreWinForms=true` for local LibreWPF validation, so future managed WinForms source movement can happen behind these package identities.

## Architecture decisions

1. Reuse upstream managed WinForms source instead of growing app-specific compatibility shims.
2. Keep platform code behind typed seams for windowing, input, painting, menus, popups, clipboard, dialogs, drag/drop, timers, system settings, accessibility, and GDI/GDI+.
3. Route rendering/composition through ProGPU retained scene primitives and Silk.NET surfaces.
4. Route local OS behavior through explicit adapters or a Win32 abstraction layer, not through reflected Win32-shaped objects.
5. Keep the source hot path reflection-free. Missing data is a typed contract gap.

## Migration stages

### Stage 0: package identity and SDK bridge

Ship `LibreWinForms.*` package identities so LibreWPF and SharpDevelop can depend on stable names now. This stage is now partially complete: the two runtime assemblies are source-owned packages, while the SDK package remains the stable switch point for future no-source-change WinForms apps.

### Stage 1: managed API source reuse

Move or source-link reusable managed API groups into LibreWinForms packages:

- resource APIs: `ResXResourceReader`, `ResXResourceWriter`, node and metadata helpers;
- design-time contracts: `System.Drawing.Design`, `System.Windows.Forms.Design`, `System.ComponentModel.Design` integration types;
- data/control models: `Control`, `ContainerControl`, `UserControl`, `Form`, `TreeView`, `ListView`, `PropertyGrid`, menus, context menus, dialogs, and data binding;
- `WindowsFormsIntegration` host contracts needed by WPF and SharpDevelop.

### Stage 2: portable platform services

Replace Win32/GDI dependencies in `System.Windows.Forms.Primitives`, `System.Private.Windows.Core`, and `System.Private.Windows.GdiPlus` with typed platform services:

- Silk.NET window, input, monitor, cursor, and message pump services;
- ProGPU paint, retained composition, clipping, text, hit testing, and render-target services;
- explicit clipboard, dialogs, drag/drop, launcher, timer, and system settings services;
- Win32 abstraction for APIs that need compatibility semantics.

### Stage 3: SharpDevelop validation

Switch SharpDevelop from `LibreWPF.WinFormsCompat.*` packages to `LibreWinForms.*` package identities. Validate:

- main workbench launch;
- menu, context-menu, combo-box, and popup paths;
- property pad and project pad hosted WinForms surfaces;
- FormsDesigner compile/load path;
- resource editor and WinForms-dependent add-ins.

Current validation:

- `SharpDevelop.Full.LibreWpf` Release build succeeds through the `LibreWinForms.*` package lane.
- Fresh-cache package restore confirms `SharpDevelop.Full.LibreWpf` resolves `LibreWinForms.System.Windows.Forms/0.1.0-preview.sharpdevelop.1` and `LibreWinForms.WindowsFormsIntegration/0.1.0-preview.sharpdevelop.1`.
- WPF workbench main menu, AddInTree context menu, and ComboBox popups open.
- Hosted WinForms `PropertyGrid` and direct `ContextMenuStrip.Show(...)` paths work in the smoke harness.
- FormsDesigner compiles, is packaged into the full workbench, attaches to the LineCounter sample's `LineCounterBrowser.cs`, and loads a replayed `System.Windows.Forms.UserControl` design surface with 21 components through the source-owned LibreWinForms package lane.
- FormsDesigner mutation now selects a replayed `ToolStripContainer`, shows the changed `Text` value through the hosted `PropertyGrid`, and persists the change through the normal SharpDevelop CodeDOM merge path before the smoke restores the sample source.
- `DesignSurface.Flush()` retains and calls the active `CodeDomDesignerLoader`; the portable serializer emits typed `ToolStripContainer` child-panel expressions for named controls inside intrinsic panels.
- The loader preserves existing `CodeAttachEventStatement`/`CodeRemoveEventStatement` entries from parsed `InitializeComponent()` methods during flush, so current designer round trips do not delete existing event hookups while full event editing is still being ported.
- The portable design host provides `IEventBindingService`, seeds it from parsed event attach/remove statements, emits current event-service mappings during flush, and prefers app-provided event services over its fallback. Designer event-property edits can replace existing handler hookups through the standard WinForms service, while SharpDevelop's active C# service still owns compatible-method lookup, handler generation, and source navigation.
- The portable design host provides `INameCreationService`, assigns stable names to unnamed created components, serializes newly added controls through fields/properties/parent `Controls.Add(...)`, and cleans parent/name/event state during `DestroyComponent(...)` so deleted controls do not reappear in regenerated CodeDOM.
- The portable CodeDOM loader now evaluates `ComponentResourceManager.GetObject(...)`, `GetString(...)`, `GetStream(...)`, and `ApplyResources(...)`, including a typed property-descriptor fallback over resolved resource-set entries for portable controls. The serializer preserves `CodeDomLocalizationModel.PropertyReflection` shape by emitting `resources.ApplyResources(...)` calls for the root and named components when a `CodeDomLocalizationProvider` requests it.
- `IEventBindingService.ShowCode(component, event)` now creates and publishes a unique handler name when one is not already assigned, then delegates handler insertion/source navigation to the active app-provided service override and rolls the new binding back if navigation fails.
- The source-owned `System.Windows.Forms.dll` package now provides the portable WinForms ResX API slice (`ResXFileRef`, `ResXDataNode`, `ResXResourceReader`, and `ResXResourceWriter`) needed by SharpDevelop resource editing and localized designer files. Focused tests validate values, metadata, node round trips, comments, and relative file references through `BasePath`.
- SharpDevelop's transitional local `System.Resources.ResX*` shim has been disabled in the local LibreWPF wrapper validation lane, so fresh-cache package-mode builds now consume the package-owned LibreWinForms ResX API without duplicate type definitions.
- `PictureBox` implements `ISupportInitialize`, matching generated WinForms designer expectations for `BeginInit()`/`EndInit()` calls and unblocking SharpDevelop's historical WinForms exception dialog and designer-created picture boxes without app-local shims.
- ProGPU `System.Drawing.Common` now supports source-rect image draws and `ImageAttributes.ColorMatrix` through the native image-effect shader path. This is required for SharpDevelop ghosted icons, grayscale images, and overlay sprite extraction, and it belongs in ProGPU so future upstream WinForms managed code can call normal drawing APIs.
- The latest fresh-cache SharpDevelop combined smoke validates menus, context menus, combo boxes, resource smoke, solution build, FormsDesigner load, FormsDesigner property mutation, hosted PropertyGrid, direct WinForms `ContextMenuStrip`, and editor completion against refreshed `LibreWinForms.System.Windows.Forms`, `ProGPU.Scene`, `ProGPU.System.Drawing.Common`, and `ProGPU.Dxf` packages.
- The latest shutdown smoke also validates that queued LibreWPF hit-test callbacks fail closed after ProGPU WPF target/host disposal; no render-loop reset, dispatcher hit-test, or `PictureBox` initialization exception remains in the captured SharpDevelop log.
- `Control.Validating` and `Control.Validated` are now part of the source-owned compatibility surface and are raised during focus-loss validation. This covers SharpDevelop/ResourceToolkit dialogs that expect the standard WinForms validation event contract.
- The LibreWinForms CI and release workflows now bootstrap matching LibreWPF/ProGPU bridge packages from `wieslawsoltes/wpf` branch `progpu-rendering-port` before packing. `LibreWinForms.WindowsFormsIntegration` uses the `LibreWPF.Transport` package by default and keeps direct WPF assembly references only for explicit local artifact-root validation.
- GitHub repository metadata now matches the LibreWinForms lane: default branch `librewinforms-progpu-port`, LibreWinForms/WinForms/ProGPU/Silk.NET description, and cross-platform .NET SDK topics.
- The README package section is split into main LibreWinForms packages and bridge packages with NuGet badge columns, matching the LibreWPF documentation style.
- Latest package validation with a fresh cache and local bridge feed creates `LibreWinForms.System.Windows.Forms`, `LibreWinForms.WindowsFormsIntegration`, `LibreWinForms.Sdk`, the package manifest, the release bundle, and the checksum. Public-feed-only packing remains blocked until `ProGPU.System.Drawing.Common` and matching LibreWPF bridge packages are published for the selected preview.

Next validation level:

- Broaden SharpDevelop resource-editor/resource-file save/load coverage beyond the current `LIBREWPF_SHARPDEVELOP_RESX_SMOKE=1` pass.
- Keep the workflow bridge bootstrap green on GitHub after ProGPU and LibreWPF preview packages publish, then remove reliance on local SharpDevelop feeds for the release lane.
- Broaden CodeDOM replay and serializer/code-generation round trips for real SharpDevelop source-navigation smoke, toolbox placement details, and more controls.
- Replace the copied compatibility control implementations with upstream managed WinForms source groups while preserving the existing source-owned package outputs.
- Make the normal LibreWPF SDK pack workflow restore from the required WPF private feeds so the package-mode validation feed can be rebuilt without ad hoc artifact refreshes.

## Exit criteria for temporary compatibility source

The `LibreWinForms.*` runtime packages have stopped depending on `LibreWPF.WinFormsCompat.*` in the source-owned lane. The next exit criteria are broader: replace copied compatibility code with reused upstream WinForms managed code, keep platform behavior behind typed ProGPU/Silk.NET services, and remove the old LibreWPF compatibility package fallback from the SDK after SharpDevelop and WPF samples pass package-mode validation.

## 2026-07-10 owned-dialog checkpoint

The WPF application host now resolves non-`Form` `IWin32Window` owners through the typed LibreWPF `HwndSource` handle registry and assigns the corresponding WPF root window as owner. The SDK smoke covers both `Application.Run(Form)` and `Form.ShowDialog(owner)`, including loaded owner linkage, `Shown`, `FormClosed`, and synchronous `DialogResult.OK`. Package-mode SharpDevelop validates the same path with its real `ExceptionBox` and workbench owner.

The modal state machine remains in managed WinForms/WPF. LibreWinForms must not add a second native event loop, reflect owner objects, or treat portable handles as Win32 HWNDs. Native host reentrancy and input-context lifetime are handled by the LibreWPF ProGPU/Silk host. Next dialog coverage should include nested modal ownership, cancellation, default/cancel keyboard actions, focus restoration, and real file/color/font dialog service flows.
