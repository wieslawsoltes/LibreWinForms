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

The coordinated drawing dependency is [ProGPU draft PR #140](https://github.com/wieslawsoltes/ProGPU/pull/140), pinned by the LibreWinForms submodule at exact commit `aa586804f0105c0db1f90130c0922030d78e85e2`. That commit descends from latest-main merge `600bf89f7aaabd26fdf5139f9000f5bb7f24699c`, which incorporates ProGPU `main` commit `fd8b07bc6b1d620090ad3bf28fc67972036e7b11` (preview 61), while retaining source-project development mode and the normal NuGet/package mode. ProGPU now has a suppression-controlled ApiCompat gate against `Microsoft.WindowsDesktop.App.Ref` 10.0.11. Its current reviewed result is 41 missing types, 189 missing members, 43 other shape diagnostics, and 273 total diagnostics, down from 1,052 total in this report's original audit, with no breaking changes or stale suppressions. Missing-type reductions can expose member diagnostics that were previously hidden, so subsystem completion and suppression diffs matter more than any one subtotal.

Implemented drawing groups include complete known-color brush/pen properties with allocation-free warmed caches, retained `Region` boolean geometry and clipping, a functional allocation-free warmed affine `Matrix` layer, functional `Blend`/`ColorBlend`/`LinearGradientBrush` state and typed rendering lowering, and retained `GraphicsPath` plus `GraphicsPathIterator` contracts with source-compatible point/type data and traversal, shaped text outlines, cardinal curves, cloning/composition, transforms, analytic bounds, fill and outline hit-testing, widening, perspective/bilinear warping, reversal, and adaptive flattening. Stroke expansion, path deformation, and shaped TrueType/CFF outline materialization are reusable typed ProGPU services with behavior, allocation, ApiCompat, and BenchmarkDotNet gates. Additional `Graphics`/image/font/icon APIs required by canonical WinForms, functional `ColorMap` remapping and defensive `ImageAttributes` state, clone-safe palette/property metadata, deterministic fixed and CPU-only optimal palette generation, complete `PixelFormat` and `ImageFormat` identities, typed scan0/caller-owned `LockBits` row conversion across packed/indexed/high-depth formats, functional `ConvertFormat` palette/alpha-threshold/ordered/spiral/error-diffusion quantization, truthful managed codec discovery, owned encoder parameters, functional PNG/BMP/JPEG saves with typed JPEG quality selection, CPU-only image resolution/tag/frame/bounds contracts, a managed buffered-graphics model, and a managed printing/controller/event model whose unavailable native operations fail at explicit platform boundaries are also present. The formatted-text group now includes exact string/span overload shapes, shaped cluster ranges, advanced direction/tab/digit/fallback/trailing-space behavior, cross-font visible format-control representatives, retained mnemonic underlines, whole-line `LineLimit`, and slash-aware `EllipsisPath`; the prior exact hosted checkpoint passes 150/150 and the current host-owned graphics-lifetime checkpoint passes 151/151 in exact ProGPU job `97794466158`. ProGPU `Graphics` can now bind an explicit target `WgpuContext` and exactly-once completion callback, allowing canonical `Control.CreateGraphics()` to record off the UI thread and commit through the typed paint service without `Graphics.FromHwnd`, reflection, or fake controls. Canonical source builds exercise these APIs directly, and the exact pinned `System.Drawing.Common.dll` is copied byte-for-byte into the canonical output. Exact source checkpoint `e181da7e32123d85807b32c93793a9b2b0dfcc31` passes the ordinary package lane, canonical source/submodule validation, package inspection, and fresh-cache source-package consumption; current source checkpoint `b9235eec56bf1a17b8d30e217f59c04e56ea9f76` retains the same 421-diagnostic API baseline and passes exact hosted workflow `32846039585` with platform 20/20, backend 9/9, lifecycle 8/8, drawing 151/151, canonical and comparison builds, both package modes, and fresh-cache validation. Its canonical package artifact `9562683351` has SHA-256 digest `ceba0c0f602d5d41ee92fe0b7a368c4a6d3c935a02134b5d09a4192a6746182d`; ProGPU evidence artifact `9562334850` has SHA-256 digest `36bfe80243a65ecd1f8b97973b4244124917e7031863b529039a31aef7add557`. Remaining ProGPU incompatibilities and WinForms platform seams are recorded in the source-first plan and generated ApiCompat artifact rather than being filled with compatibility-only stubs.

Exact source checkpoint `8728faed9d712edf466d710d3b7b7499acca5145` also makes canonical `Control.Update()` synchronous at the typed presentation boundary: pending paint is delivered on the owning dispatcher and its immediate presentation attempt completes before return, while a clean `Update()` does not manufacture invalidation or another `OnPaint`. A direct ProGPU host test covers cross-thread dispatcher completion and the canonical headless lifecycle gate covers callback ordering and clean-window behavior. Exact hosted workflow `32849867345` passes backend 10/10, lifecycle 8/8, the ordinary package lane, canonical source/submodule validation, and canonical pack/fresh-cache consumption while retaining the 421-diagnostic `System.Drawing` API baseline and all 151 drawing tests. Canonical package artifact `9564165940` has SHA-256 digest `541e09a24d71df943b8aa970b5d565072d9746a519d8e0935d4750d239d88ca2`. This changes no `System.Drawing` API debt and keeps the normal package graph independent of the development submodule mode.

Retained-control-layer source checkpoint `73f4716a80ba5bc6cc99bce9ae75c84b62d9f996` adds no fake controls and changes no `System.Drawing` API debt. Canonical `Control` traverses real managed controls and opens a typed layer by stable opaque handle; the ProGPU backend retains one `DrawingVisual` command stream per visible control, refreshes bounds/ancestor clips/z-order, re-records only layers intersecting the coalesced dirty rectangle, and removes omitted controls. The initial integration case records a form plus two children, then invalidates one child and proves the distant sibling receives no second `OnPaint` while its retained commands remain present. Transient `Control.CreateGraphics()` commands stay in a separate topmost layer and are cleared by the next invalidation. The exact local source-first gate passes platform 20/20, backend 10/10, lifecycle 9/9, drawing 151/151, the unchanged 421-diagnostic API baseline with no breaking changes, canonical and comparison builds, and both development graph modes. Pixel- or command-granular patches within one control are not implied by this control-layer checkpoint. Exact hosted workflow `32855510276` passes the canonical source/submodule and normal package jobs with platform 20/20, backend 10/10, lifecycle 9/9, drawing 151/151, and 421 unchanged API diagnostics with no breaks. Canonical package artifact `9566372153` has SHA-256 digest `e753b80e223be0c8f8bcdec2e828e5b1624c88fe700eba0f5ab79acc11eb538e`; exact-head docs workflow `32855510261` and artifact `9565988585` digest `961791b1b1eda7c7c5532d02b0bd26c92dec1c3ca92efa803bc20b5d835107fd` are green.

Typed-window-icon checkpoint `f32db1b602e75b9af93200742ab322d39e89db28` closes a platform behavior gap rather than changing the public WinForms or `System.Drawing` API surface. Canonical `Form.Icon` now reaches a typed `ILibreWindow.SetIcons` contract as immutable RGBA8 snapshots instead of returning from a portable no-op or exporting `HICON`. The source path preserves fixed-dialog/default-icon and `ShowIcon` decisions, creates an original and DPI-scaled small image through ProGPU `Icon`/`Bitmap`, converts BGRA lock data to Silk.NET's RGBA order, applies the icon after handle creation, clears it when `ShowIcon` is false, and restores it without calling USER32. Contract tests cover validation and snapshot ownership; the canonical lifecycle test covers exact pixel order and state transitions. Exact hosted workflow `32862333418` passes platform 22/22, backend 10/10, lifecycle 10/10, drawing 151/151, canonical and ordinary package lanes, and fresh-cache consumption while retaining the 421-diagnostic drawing API baseline. Packed `System.Windows.Forms.dll` SHA-256 is `8fe4168813af4d08471ffbf863216da26c808653b297477c422fd8a70aacf72d`; packed `LibreWinForms.Platform.dll` SHA-256 is `6b876251c9862eba7cbbb72fdb444a40d19e37069ae1611b93b7fc38aa81fac6`; canonical artifact `9569081563` has digest `be1d314a94ec76629fb6758015850a78aab56efffde1cb47e8ab25504309a72a`. This is evidence for the source-first architecture: an existing complete WinForms property did not need to be recreated in `src/LibreWinForms.Portable`; only its narrow platform operation needed a typed backend.

Typed-window-title checkpoint `0912c8419e7f7601a54827bc4a8999de6bbaa53c` applies the same correction to canonical `Form.Text`. The public property, metadata, events, managed cache, and native Windows behavior remain in the upstream source. A new non-null `ILibreWindow.Title` platform operation maps a live top-level title to Silk.NET without exposing its types to WinForms; logical child-control text remains managed because its `NativeWindow` has no platform window. Portable `Form.WindowText` no longer calls the upstream USER32 style refresh when the caption changes between empty and nonempty, while the Windows branch is unchanged. The canonical headless gate proves the initial title and live updated, empty, and restored values. Exact hosted workflow `32865091504` passes platform 22/22, backend 10/10, lifecycle 11/11, drawing 151/151, the unchanged 421-diagnostic API baseline, both package modes, and fresh-cache consumption. Packed `System.Windows.Forms.dll` SHA-256 is `c48d529b789f6e86d8c0b709374e372e8b968c6c8fe937780a8a09f088a0df90`; packed `LibreWinForms.Platform.dll` SHA-256 is `760bfb8a88a6a4dde132c391bf2c166237ffc2a39aa0a928a6d8f7d2e776c31b`; canonical artifact `9570160526` has digest `6a5682ddedeb1660f931b7e55588d07a938c582731e661f5bce5f3a2c93eb0a0`. This is another missing portable behavior fixed in the canonical implementation, not another public declaration added to `src/LibreWinForms.Portable`.

Typed-window-state checkpoint `b764e53e066866948447210403a8caa0eb08aae2` closes both outbound and inbound canonical `Form.WindowState` behavior without adding a second property declaration. A typed initial state enters `LibreWindowCreateOptions`, live canonical changes assign `ILibreWindow.State`, and Silk.NET's `StateChanged` event returns platform/user transitions through `ILibreWindowEvents`. Canonical portable state updates retain the upstream managed property and layout/size-lock bookkeeping while avoiding `ShowWindow` and `GetWindowPlacement`; the Windows branches are unchanged. Fullscreen is represented as maximized at the WinForms boundary because `FormWindowState` has no fullscreen value. Exact hosted workflow `32868838664` passes platform 22/22, backend 10/10, lifecycle 12/12, drawing 151/151, the unchanged 421-diagnostic API baseline with no breaks, both package modes, and fresh-cache consumption. Packed `System.Windows.Forms.dll` SHA-256 is `c06a3b485c447a5a642f8d570160bf5a212e9bfbf54a3502383364254de4f8b8`; packed `LibreWinForms.Platform.dll` SHA-256 is `20f3df8fa3b0c8746620beb5cb036a141c1c7aa8c225f08fa494e0e4912be5da`; canonical artifact `9571704330` has digest `8cbd0a854fb039dccd6a70d72c5815fc61cf513272062c866b0097d884426236`. This is another platform seam removed from the list of reasons to keep `src/LibreWinForms.Portable` as a runtime implementation.

Typed-topmost checkpoint `ffa72a7a4684ae3e2c644c655675d1a6c6d4e5d4` corrects canonical `Form.TopMost` without recreating its public property. Portable canonical creation carries pre-handle state through the existing extended-style translation into `LibreWindowOptions.TopMost`; this is required because native WinForms normally reapplies topmost state after handle creation. `ILibreWindow.TopMost` maps live post-handle changes to Silk.NET on the dispatcher thread instead of entering USER32 `SetWindowPos`; the native Windows branch remains unchanged. Exact hosted workflow `32872221383` passes platform 22/22, backend 10/10, lifecycle 13/13, drawing 151/151, the unchanged 421-diagnostic API baseline with no breaks, both package modes, and fresh-cache consumption. Packed `System.Windows.Forms.dll` SHA-256 is `fecab5cb1e8e08a03e705d18fa924e6a5261bb197159aeca3596ee7770b6042e`; packed `LibreWinForms.Platform.dll` SHA-256 is `1247f602de03b0061fc530f6d631b867177ace7af8230f3dbdcc25d83605bad7`; canonical artifact `9572859947` has digest `4a3bc4b27265057d8f8fdf1778c97ca83dc7f670572cc757c4b7165892cd7598`. This converts another property that was present in canonical source but behaviorally missing on the portable platform path.

Typed-window-border checkpoint `51ce8e5a679676bb263c935c27261ff0c091e699` connects canonical `FormBorderStyle` to a backend-neutral `LibreWindowBorder` with hidden, fixed, and resizable modes. `NativeWindow` retains canonical style and extended-style values for portable logical handles, so upstream `UpdateStylesCore` can compare and update managed styles without USER32 `GetWindowLong`, `SetWindowLong`, or `SetWindowPos`. Initial top-level creation derives decoration and resize options from the canonical style instead of always forcing a decorated window. Live border changes update `ILibreWindow.Border`, which maps directly to Silk.NET `WindowBorder`; portable `Form.OnStyleChanged` does not call the unavailable Win32 system-menu refresh. The native Windows branches remain unchanged. Exact hosted workflow `32876535344` passes platform 22/22, backend 10/10, lifecycle 14/14, drawing 151/151, the unchanged 421-diagnostic API baseline with no breaks, both package modes, and fresh-cache consumption; its package job passed on targeted attempt 2 after the first attempt failed before build on a transient nested-submodule network checkout. Packed `System.Windows.Forms.dll` SHA-256 is `29c9e9494fb89aa0f6250a87746f327d3f8e6cb6c099717cfe47785b74b75ad3`; packed `LibreWinForms.Platform.dll` SHA-256 is `7c543378c33142f806e67faef2640bc726172c1a64e447c0f4047669667bdb6e`; canonical artifact `9574415752` has digest `6524c9b1893a04bf87050c32f9a15bb7b0c93f7aa43ae98cf427ce5c37677d57`. Control-box and help-button presentation remains separate capability debt.

Typed-taskbar checkpoint `2fae3aa8626e992b4f3d1563835e6b4ec29c96eb` makes canonical `Form.ShowInTaskbar` part of `LibreWindowCreateOptions` and `ILibreWindow` rather than a fake compatibility property. Initial false state reaches the backend before a window becomes visible; live changes update the same handle through ProGPU `SilkWindowController.SetShowInTaskbar` instead of recreating a portable handle or creating WinForms' hidden Win32 taskbar owner. The portable extended-style cache composes the managed request with `WS_EX_TOOLWINDOW`, so tool-window borders suppress taskbar exposure and returning to a normal border restores the requested state. The native Windows branch retains upstream handle recreation and hidden-owner behavior. Exact hosted workflow `32883552219` passes platform 22/22, backend 10/10, lifecycle 15/15, drawing 151/151, the unchanged 421-diagnostic API baseline with no breaks, both package modes, and fresh-cache consumption. Packed `System.Windows.Forms.dll` SHA-256 is `9b3e935cd5336ad5196ec1252719bb62c40716d276d3a05e8bbc14dfd11e2050`; packed `LibreWinForms.Platform.dll` SHA-256 is `22fdb07b3c3fa891ec62b14dbebb8f4ca41e22186d7ba32d68dab7c6447dd251`; canonical artifact `9577056317` has digest `1919b9d19b091ef7d882681707d74786a2caace55b610ec417d566d1f4f8c75e`. ProGPU implements the native operation on Win32 and X11; Cocoa and Wayland currently report no taskbar capability, so real Dock/shell behavior remains explicit platform debt rather than a parity claim.

Typed-caption-capability checkpoint `5a946413b37c2fa061f7bfbf371c4654e96cb078` carries canonical `Form.MinimizeBox` and `Form.MaximizeBox` through creation and live `ILibreWindow.CanMinimize`/`CanMaximize` state. Portable style updates retain the same handle and apply the canonical `WS_MINIMIZEBOX` and `WS_MAXIMIZEBOX` bits through ProGPU `SilkWindowController`, which maps them to native Win32, Cocoa, and X11 chrome. The controller's decoration and resize state is synchronized with `LibreWindowBorder`, preventing a caption-button update from re-enabling resize on a fixed form. Native Windows source remains unchanged. The canonical case proves initial false/false, independent live enablement, live disablement, and handle stability. Exact hosted workflow `32886419913` passes attempt 1 with platform 22/22, backend 10/10, lifecycle 16/16, drawing 151/151, the unchanged 55-missing-type/319-missing-member/47-other (`421` total) ProGPU API baseline with no breaks, default canonical 0 warnings/0 errors, ProGPU canonical 613 warnings/0 errors, comparison 30 warnings/0 errors, both package modes, and fresh-cache consumption. Packed `System.Windows.Forms.dll` SHA-256 is `aa63005c9660780f3ef8e0414335812e2a151c80d95b1da24685180173c2a43e`; packed `LibreWinForms.Platform.dll` SHA-256 is `925cba36ba42a8525596d5588828c9808ef1ec09d60d0ea64dbf3fa3b71cfd3e`; canonical artifact `9578128420` has digest `283704d2d12a071b9c79b4c8084ede9198c25e21e6196e026a326cf510170751`; exact-head docs artifact `9577743913` has digest `68a5a04c82ce223900e8d26c84bfd9aa7e5cc1a91d1d40163b2b245df71b9d5c`. Wayland currently reports no minimize/maximize capability, and `HelpButton` still requires a separate typed contract.

Typed-size-constraint checkpoint `bb935baffc85c0ab1fc17fef8a1053341b724d3f` closes a behavioral gap in canonical `Form.MinimumSize` and `Form.MaximumSize` rather than adding public compatibility declarations. Initial managed limits enter `LibreWindowCreateOptions`; live changes call atomic `ILibreWindow.SetSizeConstraints` on the same handle. ProGPU converts managed limits with the active DPI/framebuffer coordinate contract and uses its existing native size-constraint controller, reapplying converted limits after scale changes. Zero maximum dimensions remain unbounded. The portable-only minimum-size update no longer enters USER32 `SetWindowPos`; native Windows retains its upstream nudge. Exact hosted workflow `32891634230` passes default canonical 0 warnings/0 errors, ProGPU canonical 613 warnings/0 errors, platform 22/22, backend 10/10, canonical lifecycle 17/17, drawing 151/151, the unchanged 421-diagnostic API baseline with no breaks, comparison 30 warnings/0 errors, both package modes, and fresh-cache consumption. Packed `System.Windows.Forms.dll` SHA-256 is `dc0abb730456ed113f1aa5da026d2a5ed99da1b390e7e82299e38ed9419947db`; packed `LibreWinForms.Platform.dll` SHA-256 is `9f0d467b11e93606320708781c46bd8c3dc0dbb2821848cc6fee31d7ce9cb366`; canonical artifact `9579992782` has digest `33b43c6fa60a69c4c4831ca5cf3f1379dcf10a6e9cd948a7306c871362b5af01`. The lifecycle case covers initial/live/unbounded/stable-handle behavior and this changes no `System.Drawing` API debt.

Typed-control-box checkpoint `e6424dca90eb1cd6a23ddcf458545619acf06ebf` closes canonical `Form.ControlBox` behavior through the same source-first path. `LibreWindowCreateOptions.CanClose` and live `ILibreWindow.CanClose` state carry the canonical `WS_SYSMENU` decision to ProGPU commit `b5214a7ac0230db4c47e3a2870a5a7b91fe28af0`, whose controller maps it to Win32 `WS_SYSMENU`, Cocoa closable style/button state, and X11 Motif close/menu functions. Wayland does not advertise the operation. LibreWinForms also suppresses effective minimize/maximize capability while the control box is absent, matching the dependency encoded by canonical Win32 styles, and restores the requested boxes when it returns. The headless case proves initial false state, live disable/restore, all three effective caption capabilities, and stable handle identity. Exact hosted LibreWinForms workflow `32897959321` passes platform 22/22, backend 10/10, lifecycle 18/18, drawing 151/151, the unchanged 421-diagnostic API baseline with no breaks, canonical and comparison builds, both package modes, and fresh-cache consumption. Packed `System.Windows.Forms.dll` SHA-256 is `b545f9361472a2c5c23fb486dedbb8c9989b82c7c66f7e61f719d69de638ee7b`; packed `LibreWinForms.Platform.dll` SHA-256 is `61b15400997c72aa8c7175e46d57c8db06f8f0720aa131f04589047dae75982c`; canonical artifact `9582270864` has digest `855e7659dd900e612149a925b700ff72d53c40afc58b36f3775d101934f914d1`. Exact ProGPU workflow `32898038506` passes all 26 jobs and evidence artifact `9582134639` has digest `0a89fb024c9d4c704b26c436f4739e521cb61a798613f88bd6ec615269f2bc87`. This adds no duplicate public WinForms API or runtime reflection.

Typed-opacity checkpoint `ff9fca5d51e9eba390bb3c9b48c75db1a8c4cd22` closes canonical top-level `Form.Opacity` behavior without copying its public declaration into the compatibility project. The canonical setter still owns clamping, `AllowTransparency`, layered-style state, metadata, and the unchanged native Windows branch; portable creation and live updates translate only the resulting whole-window value through `LibreWindowCreateOptions.Opacity` and `ILibreWindow.Opacity`. ProGPU commit `0304d87ef14cf6b29d524303562be44dc5ff15cf` adds the typed controller/platform operation and exact merge `d92d4b0666d5dedf9b34490ccd01408837546b17` incorporates latest `main` `9b1c2bd943b8f88ae42e45e5c45c4e9f870f467c` (preview 59). Win32, Cocoa, and X11 advertise the GLFW-backed capability; Wayland does not. The lifecycle case proves initial and live values, upper/lower clamping, upstream-compatible `NaN` handling, `AllowTransparency=false` reset, and stable handle identity. Exact hosted LibreWinForms workflow `32903900016` passes attempt 1 with platform 22/22, backend 10/10, lifecycle 19/19, drawing 151/151, the unchanged 421-diagnostic API baseline with no breaks, default canonical 0 warnings/0 errors, ProGPU canonical 613 warnings/0 errors, comparison 30 warnings/0 errors, both package modes, and fresh-cache consumption. The canonical artifact `9584342498` has digest `1a441f7be905a1ea523408d92bd0b903cf4d7f6c7e73a87d34ddca4c7323034f`; exact ProGPU workflow `32903880775` passes all 26 jobs and evidence artifact `9584236544` has digest `eb8aa6e84deb7c5ab3b550a75a69198ce67b61ad2e249288e1767d793ca76f69`. `TransparencyKey`, popup-window opacity for `ToolStripDropDown`, and real display-server pixel validation remain separate platform work. This changes no public WinForms or `System.Drawing` API debt.

The current typed z-order slice preserves canonical `Control.BringToFront()` and `SendToBack()` instead of adding compatibility methods. Logical child controls continue to reorder through the real `ControlCollection`; on the portable path the retained renderer consumes that managed order, so canonical `UpdateChildZOrder` no longer enters USER32 for nonexistent child HWNDs. Top-level controls route the canonical imperative operation through `ILibreWindow.SetZOrder` and ProGPU commit `aee8b1b7b0e6b31fbc011430483506bd580fc98e`. ProGPU advertises and implements front/back stacking for Win32, Cocoa, and X11; Wayland and generic GLFW explicitly return unsupported. The Windows source branch retains its original `SetWindowPos` behavior and flags. The lifecycle case proves child indices, absence of top-level dispatch for child changes, front/back dispatch for a form, and stable handle identity. Exact local validation passes default canonical 0 warnings/0 errors, ProGPU canonical 613 warnings/0 errors, platform 22/22, backend 10/10, lifecycle 20/20, drawing 151/151, the unchanged 421-diagnostic API baseline with no breaks, comparison 30 warnings/0 errors, canonical pack inspection, and fresh-cache consumption; the focused ProGPU capability matrix passes 5/5. The packed `System.Windows.Forms.dll` SHA-256 is `5155e1270bac3a2f81aa1dfba5b4d5e196c626a20ef595e45137cc86c454fede` and packed `LibreWinForms.Platform.dll` SHA-256 is `a7fa5042ec95899405bcb798e74e112fcf4e7b83e3c556ccb57ede7eefd9cb0d`. This slice does not claim activation/focus equivalence across window managers or real-compositor stacking validation, and it changes no public WinForms or `System.Drawing` API debt. Exact hosted evidence is pending.

### Typed stock-cursor checkpoint

The current source-first slice closes the common built-in `Control.Cursor` behavior gap in canonical WinForms without adding another public `Cursor` or `Cursors` declaration. All 28 stock properties construct on portable builds without calling USER32 and retain exact semantic identity; canonical ambient inheritance, `UseWaitCursor`, hover hit testing, capture, and `CursorChanged` flow into a narrow `ILibreWindow.SetCursor(LibreCursorShape)` operation. The Silk.NET adapter maps those semantic shapes to supported standard cursors and performs any fallback only at the platform edge. Exact local validation passes default canonical 0 warnings/0 errors, ProGPU canonical 613 warnings/0 errors, platform 22/22, backend 10/10, lifecycle 21/21, drawing 151/151, the unchanged 421-diagnostic API baseline with no breaks, comparison 0 warnings/0 errors, canonical pack inspection, and fresh-cache consumption with warnings treated as errors. Local packed `System.Windows.Forms.dll` SHA-256 is `c58ddb2d1d501cb2f79d70e254b91b601d3aac8bc1320935d0eeb33b2468b2f4`; local packed `LibreWinForms.Platform.dll` SHA-256 is `9ffb4239a0a015af3bd781c617186ef0a6d50a96d4d426aa6e4f2afd72cef060`.

Exact hosted validation for implementation commit `cfa48eb833b5e57fce65c47ceafb169c922fc708` passes attempt 1 in build workflow `32914918043` and docs workflow `32914918035`: platform 22/22, backend 10/10, lifecycle 21/21, drawing 151/151, the unchanged 421-diagnostic API baseline with no breaks, default canonical 0 warnings/0 errors, ProGPU canonical 613 warnings/0 errors, comparison 30 warnings/0 errors, both package modes, and fresh-cache consumption. Hosted packed hashes are `e8b3db84a3a65ec6e30f16b7f3644222ab399602edcaca3a2079b62e9442b3fb` for `System.Windows.Forms.dll` and `18e24dfce606350a05eaeeee3ccef245ce9bd56147262eb26123cbffc0308a22` for `LibreWinForms.Platform.dll`. Canonical artifact `9588053445`, immutable-package artifact `9587915476`, and docs artifact `9587800733` have digests `8defd113223c294fb98aa9b2700b3732693e2b8198c1f3cc8bfe2b2a7e851e18`, `c600cfb3cb2f1e4a5e026c1f0682a4cc6c94ce33204ca4e3439444363127ef0d`, and `0a032a5f91f1765ac524deb1547f43399553db90ee9c68a0cc8bbde04a23d6ea` respectively.

This is intentionally not a claim for the whole cursor subsystem. Custom `.cur` decoding, global `Cursor.Current`, clipping, pointer warping, hide/show balancing, drawing, hotspot/size discovery, and native handle interop still need typed services or explicit unsupported behavior. The implementation does not manufacture pseudo-HCURSOR values: requesting a Win32 handle from a portable stock cursor fails at that native boundary.

The prior z-order checkpoint's pending hosted evidence is now complete: LibreWinForms workflow `32910212351` and docs workflow `32910212353` pass attempt 1 with lifecycle 20/20 and the previously recorded aggregate counts. Canonical artifact `9586449728`, package artifact `9586461285`, and docs artifact `9586257065` have digests `df16e97a77cfd8d7ff85d2ab65b4458ce998882b994a8278549cd5514dee35e4`, `36f42f287e847d8e7b49748fa6a4388fdabc23c140e64f8ef60060b8629136ae`, and `32a25f00fc8924db8b751c600c27c286ca804443e2e5a33b6afc39d56a10855e` respectively.

### Managed child-parenting checkpoint

The current source-first slice removes another native assumption without adding a compatibility property or fake HWND relationship. Portable child handles are logical registry identities; canonical `ControlCollection` and `ParentInternal` remain authoritative for hierarchy, layout, painting, hit testing, input routing, and child z-order. `SetParentHandle` is consequently managed-only on the portable path and no longer sends logical handles through USER32 `GetParent`, `SetParent`, or parking-window calls. The native Windows branch is unchanged.

The lifecycle case creates the child handle before the form handle, then proves stable identity and canonical collection/event behavior across initial show, live moves between created parents, removal, and reattachment. Cursor hit testing verifies that routing follows the new managed tree and coordinate origin immediately. Exact local validation passes default canonical 0 warnings/0 errors, ProGPU canonical 613 warnings/0 errors, platform 22/22, backend 10/10, lifecycle 22/22, drawing 151/151, the unchanged 421-diagnostic API baseline with no breaks, comparison 30 warnings/0 errors, canonical pack inspection, and fresh-cache consumption with warnings treated as errors. Local packed `System.Windows.Forms.dll` SHA-256 is `ae09fe59fd2bb79ca11242f80d224f51cfe9e8e08725b2c185f667e4a5c68c32`; local packed `LibreWinForms.Platform.dll` SHA-256 is `115b46c29e87e753120f1c02c2fd66bf52ab04bd1011449d35881cb532ccc6f1`. This needs no ProGPU operation because there is no native child window to parent. ActiveX, MDI, foreign HWND hosting, native control subclasses, and top-level owner relationships are not covered by this checkpoint.

Exact hosted validation for implementation commit `7641f03a4201c88f089146dd6ede9c4772c1d924` passes attempt 1 in build workflow `32918214789` and docs workflow `32918214875`, with platform 22/22, backend 10/10, lifecycle 22/22, drawing 151/151, the unchanged 421-diagnostic API baseline with no breaks, canonical build baselines, comparison 30 warnings/0 errors, both package modes, and fresh-cache consumption. Hosted packed hashes are `72f64a28773851a7bdc3754d0c82316b4873707d21ced8ea896b1071356341f3` for `System.Windows.Forms.dll` and `153a17a5878448adbbed8468d9137ad6168bcd253206e6fbdf0af7c62f1b5cab` for `LibreWinForms.Platform.dll`. Canonical artifact `9589107043`, immutable-package artifact `9588976390`, and docs artifact `9588906012` have digests `8c678392208822c825cf0887c76c7c359afba12dfc7f59d3ff14964f5693be43`, `499905ebdd775906ea82f15697215f3b2c3896fd8a34def732eb02057491f600`, and `af0621fa743eb0541768396f0ef3c12e51d6b4f22e2dcbc5e71bb282494c2827` respectively.

### Portable base/Form handle-recreation checkpoint

Canonical `Control.RecreateHandleCore` previously entered USER32 parent lookup and parking-window logic even when a portable handle was a process-local logical identity. Its portable branch now preserves the canonical recreate state, destroy/create event ordering, `Created` state, and focus restoration while recreating only the target logical identity. Descendant logical handles and the managed `ControlCollection` tree stay stable. Canonical `Form.RecreateHandleCore` uses that lifecycle to replace its real top-level window through the existing typed platform service, without native placement, owner enumeration, or a second public `Form` implementation; non-manual `StartPosition` is preserved across the operation. Native Windows branches remain unchanged.

The lifecycle case directly recreates a created/focused child and then its containing form. It proves changed target handles, stable ancestor/descendant handles, exact `HandleDestroyed`/`HandleCreated` notification state, restored child focus, preserved form bounds/start position/visibility, and two typed platform-window creations. Exact local validation passes default canonical 0 warnings/0 errors, ProGPU canonical 613 warnings/0 errors, platform 22/22, backend 10/10, lifecycle 23/23, drawing 151/151, the unchanged 55-missing-type/319-missing-member/47-other (`421` total) ProGPU API baseline with no breaks, comparison 30 warnings/0 errors, canonical pack inspection, and fresh-cache consumption with warnings treated as errors. Packed `System.Windows.Forms.dll` SHA-256 is `2e74bf07ca8ab44172f4d3945ee1beaa90bd73fb10775578e777edac18524179`; packed `LibreWinForms.Platform.dll` SHA-256 is `bea6be217c8fd60d33a16a2088225c2b1a6c94ef471967759d0b13fb231f7688`. This checkpoint deliberately covers canonical base `Control` and `Form`; derived controls whose overrides perform native common-control, ActiveX, MDI, foreign-HWND, or other USER32 work before or after `base.RecreateHandleCore()` remain explicit migration debt. No ProGPU change was needed because the existing typed top-level window lifecycle already provides the required operation.

Exact hosted validation for implementation commit `5225f255ddf6ca4bea9caf67b14f2c7949df0e10` passes attempt 1 in build workflow `32921086928` and docs workflow `32921086983`, with the same 22/10/23/151 test counts, unchanged 421-diagnostic API baseline with no breaks, canonical build baselines, comparison 30 warnings/0 errors, both package modes, and fresh-cache consumption. Hosted packed hashes are `d190b209a24afee6943d515de5749a74c88b8b16f36fbb5b0d3946f2c5cd1140` for `System.Windows.Forms.dll` and `85594efe4bb1c85e9f0c41da35ed22bb8fb07edcfab09733074faa68779d3ef7` for `LibreWinForms.Platform.dll`. Canonical artifact `9590041161`, immutable-package artifact `9590216181`, and docs artifact `9589848397` have digests `541a73eafdca473cec0a2474cf7194fbf2719c1f83b51f83cce1ad7fc89a0b22`, `dc169a7643dfdf31b8164817aced063832419ee9a22ffcee5e2e25739cf9de90`, and `8550089fc217fbaa47683358ad394730dc95137fa4819ffa366390a7517c9e37` respectively.

The preview 60 dependency checkpoint is also green in hosted validation. Exact ProGPU merge `7d38d49082501458ce8867f7d58b232613f4c965` passes all 27 jobs in workflow `32923590735`; System.Drawing evidence artifact `9590783525` has digest `1a8e94c36939e6948ce62561cd187caf6e42d895d96d468f950210e6a2c9c95e`. Exact LibreWinForms pin commit `70bc325dd573584b86624184d20d755691528415` passes build workflow `32924229543` and docs workflow `32924229553` with platform 22/22, backend 10/10, lifecycle 23/23, drawing 151/151, the unchanged 421-diagnostic API baseline with no breaks, default canonical 0 warnings/0 errors, ProGPU canonical 613 warnings/0 errors, comparison 30 warnings/0 errors, both package modes, and fresh-cache consumption. Hosted packed hashes are `db930f43db7c44907d555d157e2b4408ffecdee81b9d02fdb9b1d5301b58c960` for `System.Windows.Forms.dll` and `6ae29c2ed354afba810ed084a5e4bb9f80675f632ce8ad195d84a7a7b067f174` for `LibreWinForms.Platform.dll`; canonical artifact `9591130823` has digest `79d9d411a63e4102e527cf5c9c5c99c62f9f3695bad7bd4eec05df704bae6289`.

The current design-time converter checkpoint removes `ImageFormatConverter` and `MarginsConverter` from ProGPU's missing-type baseline rather than adding equivalents to `src/LibreWinForms.Portable`. Both types have their official public shapes and are attached to the existing `ImageFormat` and `Margins` models through `TypeConverterAttribute`. Named image formats convert case-insensitively and expose the official ordered standard-value set; named and custom values produce complete designer `InstanceDescriptor` values. Margins convert four values using the active culture's list separator, recreate independent instances from property dictionaries, and preserve the existing non-negative invariant. The implementation uses only fixed cached public constructor/property descriptors mandated by `InstanceDescriptor`; it performs no assembly scan, private-field reflection, duck typing, or backend discovery. Exact local ProGPU validation passes 156/156 drawing tests and lowers the API baseline to 53 missing types, 319 missing members, 47 other diagnostics (`419` total) with no breaks or stale suppressions. Focused short-run benchmarks measure allocation-free `ImageFormat` name conversion at 10.28 ns and invariant `Margins` serialization at 26.88 ns with 88 B allocated on the ARM64 validation host.

Exact hosted validation is also green. ProGPU commit `be0d3d246e3ef0c074942052ae675a3f80b02992` passes all 27 jobs in workflow `32925830301`; System.Drawing evidence artifact `9591573785` has digest `83ccfc2341d60741efce89e666dd9d57e29695c049da4513e9dfac12cd596285`, confirms the 419-diagnostic baseline, and records x64 short-run means of 9.807 ns/0 B and 37.536 ns/88 B for the two converter benchmarks. Exact LibreWinForms pin commit `b25c9406d83cb43c283211a0b9090ec42349013f` passes build workflow `32926749260` and docs workflow `32926749246` with platform 22/22, backend 10/10, lifecycle 23/23, drawing 156/156, no ApiCompat breaks, default canonical 0 warnings/0 errors, ProGPU canonical 613 warnings/0 errors, comparison 30 warnings/0 errors, both package modes, and fresh-cache consumption. Hosted packed hashes are `8e51598000b4a0b76a5dfe386c0a70de70472227efdbf656d621b9885082de75` for `System.Windows.Forms.dll` and `88c97518c3c0fcaa20e9fa39146db17504e1f30611d0d5093e222b9ac26b21b8` for `LibreWinForms.Platform.dll`; canonical artifact `9591979513` has digest `1011669dfbef68a666ee5db1563ba39e04e614dd7126cc4e74991abcbbb28a0e`.

The current resource-converter checkpoint uses the public [.NET 10 `ImageConverter`](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.imageconverter?view=windowsdesktop-10.0) and [`IconConverter`](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.iconconverter?view=windowsdesktop-10.0) contracts plus isolated black-box observations of `System.Drawing.Common` 10.0.10; no upstream implementation source was inspected or copied. ProGPU commit `4635bbc824cd716a993c1c071c603bd4c6c0eb04` attaches both official converter types to the existing `Image` and `Icon` models. It provides designer/resource byte-array serialization, managed PNG/ICO round trips, expandable property metadata, official null/display behavior, and explicit rejection of unsupported source/value types without a Win32 or GDI+ dependency. Local validation passes platform 22/22, backend 10/10, lifecycle 23/23, and drawing 160/160 tests; lowers the API baseline to 51 missing types, 319 missing members, 47 other diagnostics (`417` total) with no breaks or stale suppressions; and passes the default canonical build at 0 warnings/0 errors, ProGPU canonical build at 613 warnings/0 errors, and portable comparison build at 30 warnings/0 errors. Fresh-cache package consumption also passes with warnings as errors. Locally packed SHA-256 hashes are `b205451974af3e28aa70c4409df5401e0de6b704b45ed6753ebfb0ea51046a67` for `System.Windows.Forms.dll` and `6db07ecb2302f93a2e243e40c0e88ffddca8dd7ac32ba4d320a82685750b75c6` for `LibreWinForms.Platform.dll`. An 8-by-8 PNG resource ShortRun on the ARM64 validation host measures 3.716 microseconds/1,768 B to serialize and 4.044 microseconds/1,752 B to deserialize; the three-iteration spread is retained as a coarse regression baseline rather than an optimization claim.

Exact coordinated hosted validation makes this the reviewed replacement for the prior design-time converter checkpoint. ProGPU workflow `32928725309` passes all 27 jobs at the exact pin; System.Drawing evidence artifact `9592557160` has digest `ff73fdaa1e1d8aefe807ad71da1845f41a54505da1c80171202e29b2b33c1bac`, confirms the 417-diagnostic API baseline and 160/160 tests, and records x64 short-run means of 5.404 microseconds/1,768 B and 3.466 microseconds/1,752 B for PNG serialization and deserialization. Exact LibreWinForms commit `35dc047ec80f7ff362353f6b704edd0bcb5106d7` passes build workflow `32929487411` and docs workflow `32929487468` with platform 22/22, backend 10/10, lifecycle 23/23, drawing 160/160, no ApiCompat breaks, default canonical 0 warnings/0 errors, ProGPU canonical 613 warnings/0 errors, comparison 30 warnings/0 errors, both package modes, and fresh-cache consumption. Hosted packed hashes are `5ac3b30284a8664e03700cfecb8102496c9dcd7e1651caa9090cf78c4c5323bd` for `System.Windows.Forms.dll` and `c779405aef5c152ea15363080078f47bdf7069cc665dc152a24946fea5cd6f30` for `LibreWinForms.Platform.dll`; canonical artifact `9592873987` has digest `2cc175f4828aa2b0ee55f14ffe84a6e7cd888502a62e2817f29f0fc2ba678d95`.

The next complete designer converter group is the public [.NET 10 `FontConverter`](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.fontconverter?view=windowsdesktop-10.0), including its nested name and unit converters. ProGPU commit `d7129e2ef40737b2c068310ed8e458561e8908e8` implements the metadata shape from the reference assembly and behavior from official documentation plus isolated `System.Drawing.Common` 10.0.10 black-box probes; no upstream implementation source was inspected or copied. `Font` and its `Name`/`Unit` properties now expose the official component-model converters. Culture-aware text and style round trips, constructor descriptors, immutable property recreation, the typed installed-family catalog, custom family names, and the official unit list all remain managed and cross-platform without GDI+, Win32, runtime assembly scans, or private reflection. Local ProGPU validation passes 164/164 tests and lowers the API baseline to 50 missing types, 319 missing members, 47 other diagnostics (`416` total) with no breaks or stale suppressions. The ARM64 ShortRun records 105.653 ns/352 B for invariant font serialization with a 21.801 ns three-iteration standard deviation; this is a coarse regression baseline, not an optimization claim. Complete local LibreWinForms validation passes platform 22/22, backend 10/10, lifecycle 23/23, and drawing 164/164 tests; default canonical 0 warnings/0 errors, ProGPU canonical 613 warnings/0 errors, and comparison 30 warnings/0 errors; both package modes; and fresh-cache source-package consumption with warnings treated as errors. Locally packed SHA-256 hashes are `ddf496e8723f5e7004da87f5de6e24614f3377d4234ab82f182a4b9e3f39cae6` for `System.Windows.Forms.dll` and `460398acb0dd7e495b567d1ba78ede9eaf0af91e7341ce993372a4974b50eaa5` for `LibreWinForms.Platform.dll`.

Exact coordinated hosted validation makes the font group the reviewed replacement checkpoint. ProGPU workflow `32931213767` passes all 27 jobs at the exact pin; System.Drawing evidence artifact `9593379700` has digest `b02856d9547e46bc3c5fd49799fc2f250b439f1c6e141ff03f3add6ea87c4a18`, confirms the 416-diagnostic API baseline and 164/164 tests, and records an x64 ShortRun mean of 151.93 ns/368 B for invariant font serialization with a 0.204 ns three-iteration standard deviation. Exact LibreWinForms commit `2783e9cbb94ff885478f1ebfc4641df695ca2d18` passes build workflow `32932031847` and docs workflow `32932031839` with platform 22/22, backend 10/10, lifecycle 23/23, drawing 164/164, no ApiCompat breaks, default canonical 0 warnings/0 errors, ProGPU canonical 613 warnings/0 errors, comparison 30 warnings/0 errors, both package modes, and fresh-cache consumption. Hosted packed hashes are `a8c6e87ba628e59a99fb9164ddf55946fc76a5ee159d35cc054ed7b9656b69bf` for `System.Windows.Forms.dll` and `80fd2b959be201cc17a3e3b3040825fc65054dc5825eae8d736e7c034a7bd222` for `LibreWinForms.Platform.dll`; canonical artifact `9593735643` has digest `6f14124f5c24eb46b9e7bd77a138636475d160f7cc81b5b1551c9cbe7c310689`.

### Current graphics-flush checkpoint

ProGPU implementation commit `03fa6eab9b5225cf2f06669f51d0925431570288`, retained by latest-main merge `600bf89f7aaabd26fdf5139f9000f5bb7f24699c`, adds the official `FlushIntention` identity and both `Graphics.Flush` overloads as a functional typed boundary. Bitmap graphics submit balanced retained commands and preserve logical clip state for later drawing. Host-owned graphics synchronously hand a balanced batch to an explicit callback; `Sync` then waits on the bound `WgpuContext`. Raw recorder-only graphics fail explicitly because they have no truthful submission target, and disposal remains exactly-once and independent of intermediate flushes. No HDC, GDI+, runtime reflection, private-field scan, or fake WinForms object is introduced.

LibreWinForms wires that callback into `SilkWindowService`: ordinary flush commits the transient retained batch before returning, while synchronous flush presents pending work before ProGPU polls the device. The canonical lifecycle driver uses the same callback shape and proves two flushes produce two commits, drawing continues between them, and final disposal does not duplicate a committed batch. The complete local source-first shadow gate passes the ProGPU ApiCompat gate at 49 missing types, 317 missing members, 47 other diagnostics (`413` total) with no breaks, drawing 170/170, platform 22/22, backend 10/10, lifecycle 24/24, default canonical 0 warnings/0 errors, ProGPU canonical 613 warnings/0 errors, and the frozen portable comparison at 30 warnings/0 errors. The ARM64/.NET 10.0.11 ShortRun records 155.858 ns mean and 40 B for one retained rectangle record+flush; a focused allocation gate caps that path at 64 B. Package and hosted results remain pending for the LibreWinForms commit that advances this pin.

### Current graphics-state checkpoint

ProGPU commit `8758d938ab2fb9d10fefe366c549f4a862f596aa` completes the next coherent `Graphics` state and transform group. It adds the official `CompositingMode` identity, functional source-over/source-copy composition, rendering-origin phase for hatch fills and pens, validated text-contrast state, allocation-free `TransformElements`, explicit append/prepend transform overloads, and rectangle visibility overloads. `SourceCopy` lowers to balanced typed `GpuBlendMode.Src` scopes across host and bitmap flushes, and save/restore retains all new state. Vector/glyph-atlas text validates and preserves `TextContrast` without pretending to invoke a missing GDI rasterizer. No HDC, GDI+, reflection, or private-state probe is introduced.

The focused state suite passes 8/8, including production bitmap readback for destination-alpha replacement, production hatch pixels for origin phase, balanced source-copy scopes across multiple hosted flushes, and zero managed allocation across 1,024 warmed `TransformElements` round trips. The complete ProGPU drawing suite passes 178/178, while ApiCompat improves to 48 missing types, 303 missing members, 47 other diagnostics (`398` total) with no breaks or stale suppressions. Downstream, the LibreWinForms ProGPU adapter rebuilds with 0 warnings/0 errors and passes 10/10 tests; canonical `System.Windows.Forms` rebuilds with 613 known compatibility warnings/0 errors and passes 24/24 lifecycle tests. Package and hosted evidence remain pending for the LibreWinForms commit that advances this pin.

### Current point/source-rectangle image checkpoint

ProGPU commit `875da5b2d717b9e09f0428cb5e70271dcc8d8788` completes eleven additional `Graphics.DrawImage` members over the existing typed retained-texture path: point/integer placement, unscaled and clipped drawing, point-anchored source cropping, and float-source destination rectangles with image attributes and abort callbacks. Production pixel tests prove exact placement and dimensions, clipping without stretching, source cropping, color remapping, and callback cancellation. Destination point arrays remain explicit debt until the retained renderer has a reviewed affine/perspective texture-mapping contract; they are not approximated with a bounding rectangle.

The focused image suite passes 5/5 and the complete drawing suite passes 183/183. ApiCompat removes eleven exact member suppressions and reaches 48 missing types, 292 missing members, 47 other diagnostics (`387` total) with no breaks or stale suppressions. Downstream, the LibreWinForms ProGPU adapter again builds at 0 warnings/0 errors and passes 10/10 tests; canonical `System.Windows.Forms` builds with 613 known compatibility warnings/0 errors and passes 24/24 lifecycle tests. The submodule remains the source-development graph, NuGet support remains intact, and hosted evidence is pending for the commit that advances this pin.

### Current coordinate-space checkpoint

ProGPU commit `4aa5ca5dc4fda0d11acb54daed1207f60e559780` adds the official `Drawing2D.CoordinateSpace` identity and all four array/span `Graphics.TransformPoints` members. World, page, and device conversions compose and invert the same world transform, page-unit/page-scale transform, and typed host base transform used for retained drawing. Caller-owned arrays and .NET 10 spans are updated in place; invalid spaces, empty inputs, non-invertible destinations, and disposed graphics fail explicitly. No platform coordinate query, HDC, GDI+, reflection, or private-field access is introduced.

The focused coordinate suite passes 5/5, including all six directed conversions with simultaneous world/page/host transforms and zero managed allocation across 1,024 warmed span conversions. The complete drawing suite passes 188/188. ApiCompat removes one missing type and four missing members, reaching 47 missing types, 288 missing members, 47 other diagnostics (`382` total) with no breaks or stale suppressions. Downstream adapter build/tests pass at 0 warnings/0 errors and 10/10; canonical `System.Windows.Forms` builds with 613 known compatibility warnings/0 errors and passes 24/24 lifecycle tests. Hosted evidence remains pending for the parent commit that advances this exact pin.

### Current graphics-container checkpoint

ProGPU commit `e970da334d4782761ed5523f7ac9a03915ca2526` adds the official sealed `Drawing2D.GraphicsContainer` and all four `Graphics.BeginContainer`/`EndContainer` members. Containers share one typed ordered state stack with `Graphics.Save`/`Restore`; public transform, page, clip, and rendering-quality state resets while the parent's effective transform and clip remain active through compact hidden state. Rectangle containers map source units into destination coordinates and compose the explicit LibreWinForms host transform. Ending, restoring across, or disposing nested scopes invalidates tokens and balances retained geometry clips without HDC, GDI+, runtime reflection, or private-state scans.

Twelve focused tests cover shape/defaults, state restoration, nested and rectangle transforms, inherited-clip pixels, token ownership/invalidation, invalid units, recorder balance, and a 256-byte-per-round-trip upper allocation bound across 1,024 warmed transitions. The complete drawing suite passes 200/200. ApiCompat removes one missing type and four missing members, reaching 46 missing types, 284 missing members, 47 other diagnostics (`377` total) with no breaks or stale suppressions. Downstream adapter build/tests pass at 0 warnings/0 errors and 10/10; canonical `System.Windows.Forms` builds with 613 known compatibility warnings/0 errors and passes 24/24 lifecycle tests. Hosted evidence remains pending for the parent commit that advances this exact pin.

### Current image-convenience checkpoint

ProGPU commit `ab95514d1ad3ea0a033d6be0517a247aeddff300` adds the official `Image.GetThumbnailImageAbort`, `Image.GetThumbnailImage`, and coordinate `Graphics.DrawIcon` API. Bitmap thumbnails use the existing typed retained-texture resize path and preserve the current managed callback behavior without invoking a removed GDI+ callback. Images without typed bitmap pixels fail explicitly instead of returning a blank result. Coordinate icon drawing preserves native size and placement through the existing unscaled retained-image command, without HDC, GDI+, runtime reflection, or private-state scans. Destination-point-array drawing remains explicit affine/perspective renderer debt.

Ten focused cases cover scaled pixels, callback behavior, dimension validation, unsupported image storage, icon pixels and placement, validation before recording, and a 4,608-byte-per-operation upper allocation bound across warmed thumbnail creation. The complete drawing suite passes 210/210. ApiCompat removes one missing type and two missing members, reaching 45 missing types, 282 missing members, 47 other diagnostics (`374` total) with no breaks or stale suppressions. The ARM64/.NET 10.0.11 ShortRun measured a 170.455 microsecond median, 192.464 microsecond mean, 38.656 microsecond standard deviation, and 7.77 KB allocated; three measured iterations and unavailable high process priority make it coarse subsystem evidence. Downstream adapter build/tests pass at 0 warnings/0 errors and 10/10; canonical `System.Windows.Forms` builds with 613 known compatibility warnings/0 errors and passes 24/24 lifecycle tests. Hosted evidence remains pending for the parent commit that advances this exact pin.

### Current drawing-identity checkpoint

ProGPU commit `051503be319348c917d31e075e1061941b849f5d` adds exact `Drawing2D.QualityMode`, `StringUnit`, and `Drawing2D.PenType` identities plus brush-derived `Pen.PenType`. The supported solid, hatch, texture, and linear-gradient brush hierarchy is classified through direct managed type matches. `PathGradient` remains an exact enum identity while its brush remains reviewed debt. `Pen.Transform` stays suppressed because the official transform changes the pen tip, ignores translation, and cannot truthfully be replaced by moving the stroke centerline; ProGPU needs a typed anisotropic stroke expansion/render/hit-test contract first.

Five focused tests cover every enum value, supported brush mapping, and zero allocation across 4,096 warmed `PenType` reads. The complete drawing suite passes 215/215. ApiCompat removes three missing types and one missing member, reaching 42 missing types, 281 missing members, 47 other diagnostics (`370` total) with no breaks or stale suppressions. Downstream adapter build/tests pass at 0 warnings/0 errors and 10/10; canonical `System.Windows.Forms` builds with 613 known compatibility warnings/0 errors and passes 24/24 lifecycle tests. No HDC, GDI+, native pen query, runtime reflection, or private-state scan is introduced. Hosted evidence remains pending for the parent commit that advances this exact pin.

### Current brush-base checkpoint

ProGPU commit `80848433b35cf866e29e8cb27e03b5e8eb041fc7` restores the official `Brush : MarshalByRefObject, ICloneable, IDisposable` base shape, abstract `Clone`, and protected virtual disposal hook. The former public abstract ProGPU renderer method is now an internal virtual seam, so third-party subclasses implement only the official contract and encounter an explicit typed-adapter boundary if drawn. Built-in solid, hatch, texture, and linear-gradient brushes retain typed lowering; solid brushes now clone independently and reject post-disposal use. Protected native-brush injection fails explicitly pending a Windows adapter rather than retaining an untyped pointer.

Four focused tests cover clone ownership, disposal, third-party inheritance hooks, the renderer boundary, and native-handle rejection. The complete drawing suite passes 219/219, and the drawing benchmark project builds with 0 warnings/errors. ApiCompat removes three missing members and three other-shape diagnostics, reaching 42 missing types, 278 missing members, 44 other diagnostics (`364` total) with no breaks or stale suppressions. Downstream adapter build/tests pass at 0 warnings/0 errors and 10/10; canonical `System.Windows.Forms` builds with 613 known compatibility warnings/0 errors and passes 24/24 lifecycle tests. The broad local headless build is independently blocked by an absent `microsoft-ui-xaml` theme checkout after the changed drawing assembly builds; hosted CI owns that populated graph. No GDI+, native brush cache, reflection, or fake compatibility object is introduced.

### Current pen-ownership checkpoint

ProGPU commit `a14af58a7542ea8f1b4976fde5c7e217c35f8d1b` restores the official sealed `Pen : MarshalByRefObject, ICloneable, IDisposable` shape and moves the ProGPU lowering method off the public API. A pen snapshots constructor/setter brushes, returns independent brush clones, deep-clones brush and dash state, owns disposal, and rejects use after disposal. Known-color brushes and pens are immutable process-wide resources, while cloning either yields an ordinary mutable object. The renderer reads owned brush state through an internal typed seam, so public defensive cloning is not added to the drawing hot path. No native pen/brush cache, HDC, GDI+, reflection, or fake WinForms object is introduced.

Six focused tests cover solid and hatch brush snapshots, defensive getters, clone independence, disposal, cached-resource immutability, and zero allocation across 100,000 warmed cached scalar read groups. The complete drawing suite passes 225/225. ApiCompat removes the remaining `Pen` shape suppression, reaching 42 missing types, 278 missing members, 43 other diagnostics (`363` total) with no breaks or stale suppressions. The ARM64/.NET 10.0.11 ShortRun measures 2.271 ns and 0 B per cached `Color`/`PenType`/`Width` operation. Complete downstream validation passes default canonical build at 0 warnings/0 errors, ProGPU canonical at the established 613 warnings/0 errors baseline, platform 22/22, adapter 10/10, lifecycle 24/24, and frozen comparison build at 30 warnings/0 errors. The source submodule remains the coordinated development graph and NuGet remains the normal consumer mode. Hosted evidence is pending for this exact pin.

### Current stock-icon checkpoint

ProGPU commit `aa586804f0105c0db1f90130c0922030d78e85e2` restores all 93 official `StockIconId` identities, the complete flags-shaped `StockIconOptions`, and the option-based `SystemIcons.GetStockIcon` overload. Explicit positive sizes and the portable 16×16/32×32 option sizes are honored. Requested icons are independent caller-owned resources, while the traditional static properties stay cached. All identifiers map to deterministic managed semantic glyphs, with functional link and selected overlays, and direct owned-bitmap transfer avoids the former PNG encode/decode round trip. Undefined identifiers, unknown flags, and invalid sizes fail explicitly.

This is a portable semantic fallback, not a claim of Windows shell artwork or local theme parity. Exact Windows, macOS, and Linux theme resolution remains a typed local-OS stock-icon provider task; `ShellIconSize` currently uses the portable 32×32 logical default until a shell-metrics service exists. There is no `HICON`, shell call, runtime reflection, private-state scan, or fake compatibility object in the managed path.

Nine focused tests cover public identity, sizing, ownership, caching, all 93 render paths, overlay pixels, validation, and warmed allocation. The complete drawing suite passes 234/234. ApiCompat removes one missing type and 89 missing members, reaching 41 missing types, 189 missing members, 43 other diagnostics (`273` total) with no breaks or stale suppressions. On ARM64/.NET 10.0.11 ShortRun, a plain folder measures a 1.490 microsecond median and 13.97 KB; a selected link document measures a 2.884 microsecond median and 14.65 KB. Complete downstream validation passes default canonical build 0 warnings/0 errors, ProGPU canonical 613 known warnings/0 errors, platform 22/22, adapter 10/10, lifecycle 24/24, and frozen comparison 30 warnings/0 errors. NuGet remains the normal consumer graph and the exact source submodule remains the coordinated plan graph. Hosted evidence is pending for this pin.

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
