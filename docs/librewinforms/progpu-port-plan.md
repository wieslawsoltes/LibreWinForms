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

## 2026-07-10 designer-host checkpoint

The portable designer host now reuses the upstream WinForms container/site architecture instead of placing components in an unrelated plain `Container`. `PortableDesignerHost` is the component container, creates design-mode sites that delegate typed services to the host, and preserves per-site `IServiceContainer` and `IDictionaryService` state. A sited control can therefore resolve `IDesignerHost`, `IContainer`, `IComponentChangeService`, selection, serialization, naming, and app-provided services through the standard `IComponent.Site` contract. Site rename state updates the serialization manager maps, and disposing a design surface now disposes the host, components, and site-local service containers.

`LibreWinForms.SdkSmoke --run-designer` covers a real `CodeDomDesignerLoader` round trip with a `ToolStripContainer`: load, component lookup, site service lookup, site-local service and dictionary state, selection, changed `Text` serialization, site rename, serialization-manager lookup, and disposal. This stays reflection-free and follows the managed upstream `DesignerHost : Container` plus host-aware `Site` pattern.

SharpDevelop's focused package-mode smoke now reports `selectedByService=True`, `selectedByContainer=True`, `selectedByGrid=True`, `flushPersisted=True`, `siteHasChangeService=True`, and `shouldSerializeText=True`. The previous partial result was caused by the smoke yielding while two normal view-switch reloads replaced the design surface; it mutated the first host and flushed the third. The smoke now waits for a stable live host before mutating and performs mutation/inspection/flush without yielding. No production SharpDevelop CodeDOM workaround was added.

Validation:

```text
LibreWinForms.SdkSmoke --run-designer       -> Success
SharpDevelop focused FormsDesigner smoke    -> Success; 21 components, 54 property rows, persisted Text assignment
SharpDevelop broad package-mode smoke        -> popups/build/ResX/FormsDesigner/PropertyGrid/AvalonDock/WinForms dialogs/completion pass
SharpDevelop shutdown                        -> exit code 0; LineCounter sources unchanged
```

## 2026-07-10 nested designer-container checkpoint

The portable designer site now follows the remaining upstream `DesignerHost.Site` contract: requesting `INestedContainer` lazily creates a site-owned managed `NestedContainer` and initializes its typed `IServiceContainer`. Nested sites implement `INestedSite`, preserve owner-qualified names, inherit site-local and host services without reflection, raise the normal component adding/added/removing/removed events, and dispose their services and component subtrees with the owning site. `IDesignerSerializationManager` resolves nested instances by qualified name and refreshes descendant names when an owner is renamed.

SharpDevelop no longer calls nonpublic `NestedContainer.GetService(...)` through `System.Reflection`. Its existing component-added hook now requests the public `INestedContainer` contract; current WinForms and LibreWinForms both initialize the nested service container as part of that request.

Validation:

```text
LibreWinForms.System.Windows.Forms Release build -> succeeds, 0 errors
LibreWinForms.SdkSmoke --run-designer            -> Success; nested owner/site/events/services/serialization/rename all true
SharpDevelop.Full.LibreWpf fresh-cache rebuild   -> succeeds, 286 warnings, 0 errors
SharpDevelop focused FormsDesigner               -> Success; 21 components, 54 rows, flushPersisted=True
SharpDevelop broad package-mode workbench        -> all popup/designer/AvalonDock/WinForms/editor gates pass, exit code 0
```

The next designer slices remain interactive toolbox selection/drop handling, parent-control adorners, extender providers, undo/redo transactions, verbs/menu commands, inherited components, localized resource round trips, and live event-handler source navigation. These must continue through managed WinForms contracts rather than app-local reflection.

## 2026-07-10 designer lifecycle and popup re-open checkpoint

`PortableDesignerHost` now owns the normal `Container.Add(...)` and `Remove(...)` lifecycle instead of relying on callers to raise design events. Direct and `CreateComponent(...)` additions receive stable generated names, root assignment, serialization registration, and `ComponentAdding`/`ComponentAdded`. Removal preserves the site through `ComponentRemoving`/`ComponentRemoved`, clears naming/event/selection state, then disposes the site and unsites the component. `DestroyComponent(...)` delegates to the component's actual host or nested container before disposal.

`DesignSurface` now creates its designer host during construction, matching upstream service availability, and exposes both public `CreateNestedContainer(...)` overloads. Named containers produce owner-qualified `INestedSite.FullName` values and inherit services through the owning component's site. The SDK designer smoke validates direct add/remove event ordering, site visibility during removal, serialization registration cleanup, named nested-container services, qualified names, and removal cleanup.

The hosted `ContextMenuStrip` bridge also preserves the requested strip's managed visible state when replacing an older WPF popup for the same strip. SharpDevelop's broad smoke records the actual `Opened` event separately from post-show visibility because another intentionally opened popup may correctly close the strip during reentrant dispatcher processing.

Validation:

```text
LibreWinForms SDK designer smoke                  -> Success; directLifecycle/namedNested/namedNestedRemoved true
LibreWinForms preview package lane                -> packages, manifest, bundle, checksum succeed
SharpDevelop fresh-cache preview.6 rebuild        -> succeeds, 286 warnings, 0 errors
Focused FormsDesigner                             -> Success; 21 components, 54 rows, flushPersisted=True
Focused hosted ContextMenuStrip                   -> Opened; 3 items
Broad workbench                                   -> all popup/designer/AvalonDock/WinForms/editor gates pass, exit code 0
```

ProGPU remains source-clean at `895fe73` (`0.1.0-preview.6`); the local package closure was rebuilt from that exact submodule before application validation.

## 2026-07-10 toolbox and designer-instance checkpoint

The portable `System.Drawing.Design.ToolboxItem` now reuses the upstream managed creation contract instead of returning an empty component array. It initializes standard type metadata, resolves types through `ITypeResolutionService`, raises creating/created events, creates components through `IDesignerHost`, and delegates default values to `IComponentInitializer`. `DesignSurface.ComponentContainer` now exposes the host container through the standard public API.

`PortableDesignerHost` now owns designer instances with the same lifecycle ordering as upstream WinForms: create and index before `ComponentAdded`, initialize once, return from `GetDesigner`, dispose before removal, and remove from the index during cleanup. `DesignSurface.CreateDesigner(...)` uses `TypeDescriptor` for attributed third-party designers first. Source-owned portable controls receive typed component/control/root fallback designers until the larger upstream `System.Windows.Forms.Design` control-designer source groups are portable. The root designer supplies the design-surface view, and control initialization applies typed parent, location, size, text, and other property defaults without reflection probes.

Validation used packages rebuilt at `0.1.0-preview.sharpdevelop.1` against the coherent ProGPU preview.6 bridge feed:

```text
LibreWinForms SDK designer smoke               -> Success; toolboxCreation=True, attributedDesigner=True
LibreWinForms preview package lane             -> packages, manifest, bundle, checksum succeed
SharpDevelop fresh-cache full rebuild          -> succeeds, 286 warnings, 0 errors
SharpDevelop focused FormsDesigner             -> Success; toolboxCreated=True, toolboxRemoved=True, 54 rows
SharpDevelop broad package-mode workbench      -> all popup/build/ResX/designer/AvalonDock/WinForms/completion gates pass
SharpDevelop shutdown                          -> exit code 0; native input attach/detach balanced
```

The next toolbox step is visible selection/move/resize adorners, grid and snap-line behavior, palette-originated drag data, and command/undo integration. The framework creation, initialization, placement, and removal contract underneath that UI is now covered.

## 2026-07-10 interactive toolbox placement checkpoint

LibreWinForms now connects the standard `IToolboxService` selection contract to typed design-mode pointer handling. Source-owned control, parent-control, and root designers intercept design input before runtime control handlers, select controls in pointer mode, and capture selected-tool mouse down/move/up sequences. Parent designers create the selected tool through one `DesignerTransaction`, pass typed parent/location/size defaults to `ToolboxItem`, activate the host, replace the selection with the created components, and call `SelectedToolboxItemUsed()` exactly once. The root designer also implements `IToolboxUser` for standard toolbox double-click creation.

`WindowsFormsHost` now forwards mouse movement and captured mouse-up events, and promotes hits on unsited implementation children to the nearest sited design-mode ancestor. Composite controls such as `ToolStripContainer` therefore keep their internal panels while the owning designer receives the interaction. Runtime mouse handlers and normal hosted-control activation are not invoked for sited design-mode controls.

Validation used a coherent `0.1.0-preview.sharpdevelop.1` package set against ProGPU `895fe73` (`0.1.0-preview.6`):

```text
LibreWinForms.System.Windows.Forms Release build -> succeeds, 0 errors
LibreWinForms.WindowsFormsIntegration build       -> succeeds, 0 errors
LibreWinForms SDK designer smoke                  -> Success; interactivePlacement=True
LibreWinForms package lane                        -> packages, manifest, bundle, checksum succeed
SharpDevelop fresh-cache full rebuild             -> succeeds, 286 warnings, 0 errors
SharpDevelop focused FormsDesigner                -> toolboxCreated=True, toolboxRemoved=True, 54 rows
SharpDevelop broad package-mode workbench         -> all expected subsystem gates pass, exit code 0
Native input lifetime                             -> 5 attaches, 5 detaches
```

The remaining interaction layer is visual and command-oriented: selection glyphs, resize handles, moving/resizing existing controls, parent rules, grid/snap lines, palette-to-surface drag data, keyboard designer commands, and undo/redo units. ProGPU source remained unchanged.
