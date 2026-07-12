# LibreWinForms ProGPU Port

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
<TargetFramework>net10.0</TargetFramework>
<UseWindowsForms>true</UseWindowsForms>
```

`LibreWinForms.Sdk` supplies the portable WinForms package references and keeps Windows-shaped WinForms APIs available while the runtime is hosted through the portable ProGPU/LibreWPF stack.

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
<Project Sdk="LibreWinForms.Sdk/0.1.0-preview.10">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
</Project>
```

Older projects that still use `Microsoft.NET.Sdk.WindowsDesktop` should make the same SDK change and keep the existing WinForms properties.

4. Keep existing app dependencies in place. For example, a mixed WPF/WinForms app only changes the SDK line in the WinForms project:

```xml
<Project Sdk="LibreWinForms.Sdk/0.1.0-preview.10">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
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
| `LibreWinForms.Sdk` | [![NuGet](https://img.shields.io/nuget/vpre/LibreWinForms.Sdk.svg)](https://www.nuget.org/packages/LibreWinForms.Sdk) | Custom MSBuild SDK that redirects WinForms apps to the portable LibreWinForms package set. |
| `LibreWinForms.System.Windows.Forms` | [![NuGet](https://img.shields.io/nuget/vpre/LibreWinForms.System.Windows.Forms.svg)](https://www.nuget.org/packages/LibreWinForms.System.Windows.Forms) | Portable `System.Windows.Forms` API/runtime surface used by LibreWinForms apps. |
| `LibreWinForms.WindowsFormsIntegration` | [![NuGet](https://img.shields.io/nuget/vpre/LibreWinForms.WindowsFormsIntegration.svg)](https://www.nuget.org/packages/LibreWinForms.WindowsFormsIntegration) | Portable `WindowsFormsIntegration` bridge for LibreWPF-hosted WinForms content. |

### Bridge Packages

LibreWinForms consumes these bridge packages from the matching LibreWPF/ProGPU preview. CI and release workflows build a local bridge feed from `wieslawsoltes/wpf@progpu-rendering-port` before packing LibreWinForms, so the WinForms package lane can validate before the bridge packages are available on NuGet.org.

| Package | NuGet | Purpose |
| --- | --- | --- |
| `LibreWPF.Transport` | [![NuGet](https://img.shields.io/nuget/vpre/LibreWPF.Transport.svg)](https://www.nuget.org/packages/LibreWPF.Transport) | Managed WPF assembly identities and reference/runtime assets consumed by `WindowsFormsIntegration`. |
| `LibreWPF.Interop` | [![NuGet](https://img.shields.io/nuget/vpre/LibreWPF.Interop.svg)](https://www.nuget.org/packages/LibreWPF.Interop) | Portable service DTOs and typed interop contracts shared with LibreWPF and ProGPU. |
| `ProGPU.System.Drawing.Common` | [![NuGet](https://img.shields.io/nuget/vpre/ProGPU.System.Drawing.Common.svg)](https://www.nuget.org/packages/ProGPU.System.Drawing.Common) | ProGPU-backed portable `System.Drawing.Common` compatibility surface used by WinForms controls and resources. |

## Build And Release

```bash
LIBREWINFORMS_DEV_PACKAGE_VERSION=0.1.0-preview.10 ./eng/librewinforms-pack.sh
```

The package lane builds `LibreWinForms.System.Windows.Forms`, `LibreWinForms.WindowsFormsIntegration`, and `LibreWinForms.Sdk`, verifies README/release docs, writes the preview package manifest, and creates a release bundle with package hashes and a local-feed `NuGet.config`. It also cleans current-version package artifacts before packing and fails if a stale or unexpected current-version `.nupkg` would be published.

The pack script restores through an isolated cache under `artifacts/nuget/librewinforms-pack` by default and clears current-version LibreWPF/ProGPU bridge packages from that cache before restore. This keeps package-mode validation tied to the bridge feed built for the same run instead of a stale same-version package from a user/global NuGet cache.

When validating against unpublished LibreWPF/ProGPU bridge packages, build or restore those packages into a local feed and pass both the bridge package version and restore source:

```bash
LIBREWINFORMS_DEV_PACKAGE_VERSION=0.1.0-preview.10 \
LIBREWINFORMS_BRIDGE_PACKAGE_VERSION=0.1.0-preview.10 \
LIBREWINFORMS_RESTORE_SOURCES=/path/to/wpf/artifacts/packages/Release/NonShipping%3Bhttps://api.nuget.org/v3/index.json \
./eng/librewinforms-pack.sh
```

GitHub workflows:

- `LibreWinForms Build` builds the matching LibreWPF/ProGPU bridge feed, runs the preview package lane, and uploads package artifacts.
- `LibreWinForms Docs` verifies README and release docs against the preview package list.
- `LibreWinForms Release` resolves an immutable LibreWPF bridge tag or commit, records exact LibreWinForms/LibreWPF/ProGPU provenance, runs behavior and package-mode SDK smokes, builds preview packages/bundle artifacts, can publish to NuGet.org with `NUGET_API_KEY`, and creates a GitHub release for `librewinforms-v*` tags.

Release order matters: publish ProGPU and LibreWPF for the same version first, then publish LibreWinForms so downstream restores can resolve the full package dependency closure from NuGet.org.

See [docs/librewinforms-release.md](docs/librewinforms-release.md) and the ongoing port plan in [docs/librewinforms/progpu-port-plan.md](docs/librewinforms/progpu-port-plan.md).

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
