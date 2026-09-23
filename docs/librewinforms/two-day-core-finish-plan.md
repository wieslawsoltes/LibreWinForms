# Two-Day Core Finish Plan

## Deadline and definition of done

The P0 core target is **end of 2026-09-10 (Europe/Warsaw)**, provided the GitHub API and the existing PR branches are writable during the qualification window.

“Core complete” means an unchanged SharpDevelop consumer can use the canonical source-built `System.Windows.Forms` runtime with ProGPU `System.Drawing.*` for its principal workbench, FormsDesigner, resource, edit, menu, dialog, and shutdown workflows on the supported desktop hosts. It does not mean that every historical GDI+ pixel difference or obsolete .NET Framework control has been eliminated.

## P0 scope

1. Canonical `Application`/`Form` lifecycle, nested modal dispatch, focus, keyboard, pointer, layout, DPI, and teardown.
2. Retained control painting, text, images, visual styles, adorners, popups, reversible feedback, and ProGPU presentation.
3. `ToolStrip`/`ContextMenuStrip`, tooltip and error-provider popup behavior used by SharpDevelop.
4. Canonical clipboard, file/color/font/message dialogs, timers, input languages, power, and system settings through typed platform services.
5. Application-local drag/drop for FormsDesigner/toolbox and widget transfers through canonical WinForms events.
6. SharpDevelop workbench, FormsDesigner load/mutate/save, ResX, property grid, context menu, clipboard, drag/drop, and clean-shutdown qualification.
7. Source-project/submodule development and immutable NuGet/package consumption from the same canonical runtime identity.

## Explicitly deferred from the two-day cutoff

- reducing every reviewed SVG.NET, W3C, or official `System.Drawing.Common` behavior difference to zero;
- restoring obsolete modern-WinForms stubs such as classic `DataGrid`, `MainMenu`, `ToolBar`, and `StatusBar` unless a qualified consumer proves one is required;
- cross-process rich clipboard formats and external-desktop XDND, Wayland data-device, or AppKit drag sessions;
- cross-application reversible overlays and platform accessibility bridges not exercised by the P0 consumer;
- broad performance optimization beyond regression budgets for startup, first presentation, retained painting, SVG representative fixtures, drag feedback, and allocation hot paths.

Deferred work remains tracked behavior debt; it is not replaced with compatibility-shaped objects or silent no-ops.

## Execution order

### Day 1 — runtime completion

- Land and validate the typed clipboard service, including desktop Unicode text and rich in-process designer data.
- Land and validate the ProGPU application-local drag loop and canonical logical-control hit testing.
- Audit the remaining SharpDevelop P0 paths for native calls or unsupported platform services; repair only reproducible consumer blockers.
- Run focused platform, backend, lifecycle, drawing, and package tests after each tranche.

### Day 2 — consumer and merge qualification

- Run the full SharpDevelop workbench/designer/resource/menu/clipboard/drag/shutdown sequence.
- Fix only failures that block the P0 definition of done, then rerun the focused and complete source-first gates.
- Qualify package mode and source-project mode from fresh caches.
- Run correctness corpus manifests and representative performance budgets as final regression gates.
- Reconcile the existing branches with their current bases, update PR descriptions, and make all required hosted checks green.

## PR and merge policy

- ProGPU remains consolidated in [ProGPU PR #140](https://github.com/wieslawsoltes/ProGPU/pull/140).
- LibreWinForms remains consolidated in [LibreWinForms PR #27](https://github.com/wieslawsoltes/LibreWinForms/pull/27).
- No additional PR is required. ProGPU is merged or otherwise made reachable first; LibreWinForms then advances the exact submodule gitlink.
- A PR is mergeable only when its head is conflict-free against the current base, every required hosted check is green, exact corpus manifests have no unexplained drift, and the package/source provenance gates pass.

## Current checkpoint

- ProGPU `bffde4689a5d6f0bd4e8de1290046bcb1728704a` is pushed on #140 and passes the local 621-test drawing suite, official-corpus manifest, ApiCompat, documentation, and package checks recorded in the main source-first plan.
- The next LibreWinForms head contains the exact ProGPU pin plus typed clipboard and application-local drag/drop implementations with focused contracts.
- The SharpDevelop P0 audit also removes AvalonEdit's remaining USER32 cursor-visibility dependency: canonical `Cursor.Hide`/`Show` now preserve the WinForms balanced display count and drive Silk cursor modes through `ILibreWindow`.
- The combined source-first gate passes native canonical Forms at 0 warnings/0 errors, ProGPU canonical Forms at the established 613 reviewed warnings/0 errors, platform 52/52, backend 54/54, lifecycle 131/131, drawing 621/621, ApiCompat 0 missing types/0 missing members/13 reviewed differences, and the retired-runtime ledger at 26/0/0.
- The locally available base refs report no merge conflict for either existing PR. Hosted mergeability and CI status must be rechecked after GitHub connectivity returns.
