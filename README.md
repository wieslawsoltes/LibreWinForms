# LibreWinForms ProGPU Port

[![Telegram Community](https://img.shields.io/badge/Telegram-Community-26A5E4?logo=telegram&logoColor=white)](https://t.me/+HblJUymBc544ODY0)

This branch ports WinForms-shaped APIs onto the ProGPU/Silk.NET platform while reusing as much managed WinForms code as possible. The public package brand is LibreWinForms, with the custom SDK package `LibreWinForms.Sdk`, so existing WinForms projects can start by switching the project SDK and keeping normal WinForms source unchanged.

Current focus areas:

- Reuse managed WinForms code for application model, controls, layout, events, data binding, drawing integration, and WPF interop where practical.
- Replace Windows-only User32/GDI+/native hosting dependencies with typed LibreWinForms seams backed by ProGPU, Silk.NET, and the shared LibreWPF interop layer.
- Package the portable runtime as a preview SDK and NuGet set that can be consumed from a local feed or NuGet.org.
- Keep SharpDevelop, LibreWPF `WindowsFormsHost`, and mixed WPF/WinForms smoke apps as compatibility gates while the port fills out.

The active development and default GitHub branch is `librewinforms-progpu-port`. Preview releases are produced from this branch by the LibreWinForms CI/release workflows and are tagged as `librewinforms-v<version>` after the matching ProGPU and LibreWPF bridge packages are available.

## Getting Started: Switch From WinForms To LibreWinForms

LibreWinForms is packaged as an MSBuild SDK so normal WinForms apps can move to the ProGPU/Silk.NET platform through the project file first. Keep application code, resources, existing package references, and normal `System.Windows.Forms` type usage unchanged unless the app uses Windows-only interop, raw HWND assumptions, native controls, designer-only APIs, or unsupported graphics APIs.

1. Start from an existing SDK-style WinForms project and keep a clean commit of the working WinForms version.

2. Make sure the project targets the supported preview TFM:

```xml
<TargetFramework>net11.0</TargetFramework>
<UseWindowsForms>true</UseWindowsForms>
```

`LibreWinForms.Sdk` supplies canonical source-built WinForms plus the typed ProGPU/Silk.NET backend. Package mode is the default; source checkouts can select project mode explicitly.

3. Change only the project SDK.

Before:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
</Project>
```

After:

```xml
<Project Sdk="LibreWinForms.Sdk/0.1.0-preview.45">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net11.0</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
</Project>
```

Older projects that still use `Microsoft.NET.Sdk.WindowsDesktop` should make the same SDK change and keep the existing WinForms properties.

4. Keep existing app dependencies in place. For example, a mixed WPF/WinForms app only changes the SDK line in the WinForms project:

```xml
<Project Sdk="LibreWinForms.Sdk/0.1.0-preview.45">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net11.0</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Some.WinForms.Library" Version="1.2.3" />
  </ItemGroup>
</Project>
```

5. Restore and run the app normally:

```bash
dotnet restore
dotnet run
```

6. Treat Windows-only interop, custom HWND hosting, native common controls, P/Invoke-heavy owner-draw paths, GDI handles, and designer-only APIs as the first compatibility review points. Normal WinForms managed code should remain source-compatible as the portable runtime fills out.

## NuGet Packages

The preview package set is defined in `eng/librewinforms-package-list.sh` and validated by the release workflow.

### LibreWinForms Packages

| Package | NuGet | Purpose |
| --- | --- | --- |
| `LibreWinForms.Sdk` | [![NuGet](https://img.shields.io/nuget/vpre/LibreWinForms.Sdk.svg)](https://www.nuget.org/packages/LibreWinForms.Sdk) | Custom MSBuild SDK that selects canonical WinForms and the ProGPU backend by default. |
| `LibreWinForms.System.Windows.Forms` | [![NuGet](https://img.shields.io/nuget/vpre/LibreWinForms.System.Windows.Forms.svg)](https://www.nuget.org/packages/LibreWinForms.System.Windows.Forms) | Canonical source-built `System.Windows.Forms` implementation and reference assets. |
| `LibreWinForms.ProGPU` | [![NuGet](https://img.shields.io/nuget/vpre/LibreWinForms.ProGPU.svg)](https://www.nuget.org/packages/LibreWinForms.ProGPU) | Typed ProGPU/Silk.NET platform backend for canonical WinForms. |
| `LibreWinForms.WindowsFormsIntegration` | [![NuGet](https://img.shields.io/nuget/vpre/LibreWinForms.WindowsFormsIntegration.svg)](https://www.nuget.org/packages/LibreWinForms.WindowsFormsIntegration) | Real LibreWPF `WindowsFormsIntegration` source built and qualified against canonical LibreWinForms. |

### Bridge Packages

The canonical runtime and its ten-package ProGPU drawing closure are built from this repository and its pinned ProGPU submodule. `WindowsFormsIntegration` is built from the real LibreWPF source at a recorded commit; the release handoff requires exact LibreWinForms and ProGPU provenance, compares the generated managed-contract documents, and rejects the retired compatibility package identity.

| Package | NuGet | Purpose |
| --- | --- | --- |
| `LibreWPF.Transport` | [![NuGet](https://img.shields.io/nuget/vpre/LibreWPF.Transport.svg)](https://www.nuget.org/packages/LibreWPF.Transport) | Managed WPF assembly identities and reference/runtime assets consumed by `WindowsFormsIntegration`. |
| `LibreWPF.Interop` | [![NuGet](https://img.shields.io/nuget/vpre/LibreWPF.Interop.svg)](https://www.nuget.org/packages/LibreWPF.Interop) | Portable service DTOs and typed interop contracts shared with LibreWPF and ProGPU. |
| `ProGPU.System.Drawing.Common` | [![NuGet](https://img.shields.io/nuget/vpre/ProGPU.System.Drawing.Common.svg)](https://www.nuget.org/packages/ProGPU.System.Drawing.Common) | ProGPU-backed portable `System.Drawing.Common` compatibility surface used by WinForms controls and resources. |

## Build And Release

```bash
LIBREWINFORMS_DEV_PACKAGE_VERSION=0.1.0-preview.45 ./eng/librewinforms-pack.sh
```

The package lane builds canonical `LibreWinForms.System.Windows.Forms`, `LibreWinForms.ProGPU`, `LibreWinForms.Sdk`, and the exact ten-package ProGPU drawing closure. It consumes only a separately qualified canonical WFI source package plus its `LibreWPF.Interop` and `ProGPU.DirectX` source-built dependencies, verifies exact source/dependency provenance and the generated Forms contract, verifies docs, writes the preview manifest, creates a release bundle with hashes and a local-feed `NuGet.config`, and fails if a stale or unexpected current-version package would be published.

The pack script restores through an isolated cache under `artifacts/nuget/librewinforms-pack` by default and clears current-version LibreWPF/ProGPU bridge packages from that cache before restore. This keeps package-mode validation tied to the bridge feed built for the same run instead of a stale same-version package from a user/global NuGet cache.

Build canonical WFI from a LibreWPF checkout first, then pass the qualified source output and exact LibreWPF commit to the package lane. A matching LibreWPF SDK feed is used only by the mixed-desktop package smoke:

```bash
LIBREWINFORMS_DEV_PACKAGE_VERSION=0.1.0-preview.45 \
LIBREWINFORMS_PROGPU_PACKAGE_VERSION=0.1.0-preview.62 \
LIBREWINFORMS_CANONICAL_WFI_SOURCE_ROOT=/path/to/LibreWPF \
LIBREWINFORMS_CANONICAL_WFI_EXPECTED_COMMIT=<librewpf-commit> \
./eng/librewinforms-build-canonical-wfi.sh

LIBREWINFORMS_DEV_PACKAGE_VERSION=0.1.0-preview.45 \
LIBREWINFORMS_PROGPU_PACKAGE_VERSION=0.1.0-preview.62 \
LIBREWINFORMS_CANONICAL_WFI_PACKAGE_SOURCE=/path/to/LibreWPF/artifacts/packages/CanonicalWinForms \
LIBREWINFORMS_CANONICAL_WFI_COMMIT=<librewpf-commit> \
./eng/librewinforms-pack.sh
```

`LIBREWINFORMS_PROGPU_PACKAGE_VERSION` labels the current drawing closure built from the submodule. Canonical Forms and backend packages target `net10.0`, so WFI and `net10.0` through later .NET consumers resolve one qualified assembly set.

GitHub workflows:

- `LibreWinForms Build` compiles canonical WFI from LibreWPF source, stages a LibreWPF SDK feed for the mixed-desktop smoke, runs the preview package lane, and uploads package artifacts.
- `LibreWinForms Docs` verifies README and release docs against the preview package list.
- `LibreWinForms Public Package Smoke` restores only from NuGet.org and builds the unchanged `net11.0` WinForms template on Ubuntu and macOS after publication.
- `LibreWinForms Release` resolves canonical WFI source and LibreWPF SDK refs, records exact LibreWinForms/LibreWPF/ProGPU provenance, runs canonical WFI and SDK package smokes, builds preview packages/bundle artifacts, can publish to NuGet.org with `NUGET_API_KEY`, and creates a GitHub release for `librewinforms-v*` tags.

Release order is source-qualified: canonical WFI must be built from the selected LibreWPF commit against this exact LibreWinForms checkout before the bundle can be created. SharpDevelop remains the downstream mixed-desktop consumer gate.

See [docs/librewinforms-release.md](docs/librewinforms-release.md) and the ongoing port plan in [docs/librewinforms/progpu-port-plan.md](docs/librewinforms/progpu-port-plan.md).

## Performance Gates

The historical mixed-desktop comparison smoke included deterministic Release workloads for hosted
WinForms rendering, layout, paint-surface retirement, and render-resource
ownership. The render workload records 100 labels for 2,000 frames after warming
brush, text, clip, and retained-drawing caches. It reopens one persistent WPF
`DrawingVisual` for 2,000 frames, matching the real visual lifecycle instead of
manufacturing a new visual and inheritance context for every frame. On an Apple
arm64 development host with .NET 10, retaining unchanged host drawing content
reduced allocation from more than 35,000 to 368 bytes per recorded frame. The
2,000-frame workload completes in roughly 2.2 ms. The gate allows at most 2,000
bytes/frame, requires exactly one unchanged retained-drawing build, requires an
actual rebuild after text/color invalidation, and requires all retained drawing
and render-resource caches to release when the hosted tree is detached.

The same gate churns 2,200 unique text/color combinations and now reports
managed retention before and after detaching the hosted tree. On the same host,
the deliberately saturated 256-brush/512-text/512-drawing caches retain about
2.60 MB at their high-water mark and leave about 0.26 MB after detach; the
2,200 invalidating renders take roughly 115 ms. Compared with the former
2,048-entry text caches, this cuts high-water managed retention by about 81%
while keeping steady retained replay and mutation rebuild behavior unchanged.
The count limits preserve text reuse for scrolling while the detach assertion
prevents the bounded cache from becoming a lifetime leak.

Those figures measure managed allocation traffic, managed heap retention, and
CPU recording time, not process RSS, GPU residency, or device execution. Paint
surface pixel ownership, retained resource counts, and zero-allocation layout
passes are checked independently so an allocation improvement cannot hide an
unbounded cache or graphics-resource leak. Current release gating places
drawing correctness and allocation assertions in ProGPU's
`System.Drawing.Common.Tests`; the canonical WFI package smoke owns assembly
identity and host-child construction rather than retaining a second WinForms
runtime solely to host benchmarks.

## Original Upstream README

# Windows Forms

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](https://github.com/dotnet/winforms/blob/main/LICENSE.TXT)

Windows Forms (WinForms) is a UI framework for building Windows desktop applications. It is a .NET wrapper over Windows user interface libraries, such as User32 and GDI+. It also offers controls and other functionality that is unique to Windows Forms.

Windows Forms also provides one of the most productive ways to create desktop applications based on the visual designer provided in Visual Studio. It enables drag-and-drop of visual controls and other similar functionality that make it easy to build desktop applications.

## Windows Forms Out-Of-Process Designer

For information about the WinForms Designer supporting the .NET runtime and the changes between the .NET Framework Designer (supporting .NET Framework up to version 4.8.1) vs. the .NET Designer (supporting .NET 6, 7, 8, 9+), please see [Windows Forms Designer Documentation](https://learn.microsoft.com/dotnet/desktop/winforms/controls-design/designer-differences-framework?view=netdesktop-8.0).

**Important:** As a Third Party Control Vendor, when you migrate controls from .NET Framework to .NET, your control libraries _at runtime_ are expected to work as before in the context of the respective new TFM (special modernization or security changes in the TFM kept aside, but those are rare breaking changes). Depending on the richness of your control's design-time support, the migration of control designers from .NET Framework to .NET might need to take a series of areas with breaking changes into account. The provided link points out additional resources which help in that migration process.

## Relationship to .NET Framework

This codebase is a fork of the Windows Forms code in the .NET Framework 4.8. 
We started the migration process by targeting .NET Core 3.0, when we've strived to bring the two runtimes to a parity. Since then, we've done a number of changes, including [breaking changes](https://docs.microsoft.com/dotnet/core/compatibility/winforms), which diverged the two. For more information about breaking changes, see the [Porting guide][porting-guidelines].

## The bar for innovation and new features

WinForms is a technology which was originally introduced as a part of .NET Framework 1.0 on February 13th, 2002. It's primary focus was and is to be a Rapid Application Tool for Windows based Apps, and that principal sentiment has not changed over the years. WinForms at the time addressed developer's requests for

* A framework for stable, monolithic Line of Business Apps, even with extremely complicated and complex domain-specific workflows
* The ability to easily provide rich user interfaces
* A safe and - over the first 3 versions of .NET Framework - increasingly performant way to communicate across process boundaries via various Windows Communication Services, or access on-site databases via ADO.NET providers.
* A very easy to use, visual what-you-see-is-what-you-get designer, which requires little ramp-up time, and was primarily focused to support 96 DPI resolution-based, pixel-coordinated drag & drop design strategies.
* A flexible, .NET reflection-based Designer extensibility model, utilizing the .NET Component Model.
* Visual Controls and Components, which provide their own design-time functionality through Control Designers

Over time, and with a growing need to address working scenarios with multi-monitor, high resolution monitors, significantly more powerful hardware, and much more, WinForms has continued to be modernized.

And then there is the evolution of Windows: When new versions of Windows introduce new or change existing APIs or technologies - WinForms needs to keep up and adjust their APIs accordingly.

And  **that** is still the primary motivation for once to modernize and innovate, but also the bar to reach for potential innovation areas we either need or want to consider:

* Areas, where for example for security concerns, the Windows team needed to take an depending area out-of-proc, and we see and extreme performance hit in WinForms Apps running under a new Service Pack or a new Windows Version
* New features to comply with updated industry standards for accessibility.
* HighDPI and per Monitor V2-Scenarios.
* Picking up changed or extended Win32 Control functionality, to keep controls in WinForms working the way the Windows team wants them to be used.
* Addressing Performance and Security issues
* Introducing ways to support asynchronous calls interatively, to enable apps to pick up migration paths via Windows APIs projection/Windows Desktop Bridge, enable scenarios for async WebAPI, SignalR, Azure Function, etc. calls, so WinForms backends can modernized and even migrated to the cloud.

What would not make the bar: 
* New functionality which modern Desktop UIs like WPF or WinUI clearly have already
* Functionality, which would "stretch" a Windows Desktop App to be a mobile, Multi-Media or IoT app.
* Domain-specific custom controls, which are already provided by the vast variety of third party control vendors

**A note about Visual Basic**: Visual Basic .NET developers make up about 20% of WinForms developers. We welcome changes that are specific to VB if they address a bug in a customer-facing scenario. Issues and PRs should describe the customer-facing scenario and, if possible, include images showing the problem before and after the proposed changes. Due to limited bandwidth, we cannot prioritize VB-specific changes that are solely for correctness or code cleanliness. However, VB remains important to us, and we aim to fix any critical issues that arise.

## Please note

:warning: This repository contains only implementations for Windows Forms for [.NET platform](https://github.com/dotnet/core).<br />
It does not contain either:
* The .NET Framework variant of Windows Forms. Issues with .NET Framework, including Windows Forms, should be filed on the [Developer Community](https://developercommunity.visualstudio.com/spaces/61/index.html) or [Product Support](https://support.microsoft.com/contactus?ws=support) websites. They should not be filed on this repository.
* The Windows Forms Designer implementations. Issues with the Designer can be filed via VS Feedback tool (top right-hand side icon in Visual Studio) or be filed in this repo using the Windows Forms out-of-process designer issue template.

# How can I contribute?

We welcome contributions! Many people all over the world have helped make this project better.

* [Contributing][contributing] explains what kinds of changes we welcome
* [Developer Guide][developer-guide] explains how to build and test
* [Get Up and Running with Windows Forms .NET][getting-started] explains how to get started building Windows Forms applications.


## How to Engage, Contribute, and Provide Feedback

Some of the best ways to contribute are to try things out, file bugs, join in design conversations, and fix issues.

* The [contributing guidelines][contributing] and the more general [.NET contributing guide][net-contributing] define contributing rules.
* The [Developer Guide][developer-guide] defines the setup and workflow for working on this repository.
* If you have a question or have found a bug, [file an issue](https://github.com/dotnet/winforms/issues/new?template=bug_report.md).
* Use [daily builds][developer-guide] if you want to contribute and stay up to date with the team.

## Reporting security issues

Security issues and bugs should be reported privately via email to the Microsoft Security Response Center (MSRC) <secure@microsoft.com>. You should receive a response within 24 hours. If for some reason you do not, please follow up via email to ensure we received your original message. Further information, including the MSRC PGP key, can be found in the [Security TechCenter](https://www.microsoft.com/msrc/faqs-report-an-issue). Also see info about related [Microsoft .NET Core and ASP.NET Core Bug Bounty Program](https://www.microsoft.com/msrc/bounty-dot-net-core).

## Code of Conduct

This project uses the [.NET Foundation Code of Conduct](https://dotnetfoundation.org/code-of-conduct) to define expected conduct in our community. Instances of abusive, harassing, or otherwise unacceptable behavior may be reported by contacting a project maintainer at conduct@dotnetfoundation.org.

## License

.NET (including the Windows Forms repository) is licensed under the [MIT license](LICENSE.TXT).

## .NET Foundation

.NET Windows Forms is a [.NET Foundation](https://www.dotnetfoundation.org/projects) project.<br />
See the [.NET home repository](https://github.com/Microsoft/dotnet) to find other .NET-related projects.

[contributing]: CONTRIBUTING.md
[developer-guide]: docs/developer-guide.md
[getting-started]: docs/getting-started.md
[net-contributing]: https://github.com/dotnet/runtime/blob/master/CONTRIBUTING.md
[porting-guidelines]: docs/porting-guidelines.md
