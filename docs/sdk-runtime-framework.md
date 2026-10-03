# SDK and canonical runtime target frameworks

The public portable consumer TFMs are `net10.0` and `net11.0`, without a Windows
platform suffix. The repository's .NET 11 build SDK and upstream `NetCurrent`
default do not describe the published payload: the source-first packer builds
both canonical Forms and its ProGPU backend with `NetCurrent=net10.0`.

Issue [#155](https://github.com/wieslawsoltes/LibreWinForms/issues/155) identified
the contradiction between that payload and the SDK's hard-coded net11-only
consumer check. This change does not relabel any binary, change package versions,
or substitute a different runtime assembly set.

## Consumer and source graph policy

The minimal C# application contract is:

```xml
<Project Sdk="LibreWinForms.Sdk/0.1.0-preview.65">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
```

The example shows the project's shape; already-published SDK packages are
immutable. The correction requires a newly built/published SDK containing it,
not an in-place change to preview.65. A .NET 10 consumer can use the net10 payload;
a .NET 11 consumer can use that same payload. A package explicitly selecting a
net11 payload cannot admit a net10 consumer. Other consumer TFMs remain explicit
errors, not a promise of future runtime compatibility.

Each SDK Project-mode runtime, designer, and backend reference passes the
consumer's TFM as `NetCurrent`. This activates the existing stable .NET 10
support-package branch for net10 consumers while preserving net11 source builds.
The source projects still decide their own `TargetFramework`; the SDK does not
globally force the netstandard analyzer/generator projects to the application TFM.
Project mode still needs the checkout's pinned build tools and qualified ProGPU
gitlink; supporting a net10 application does not retarget the upstream build SDK.

The SDK records `LibreWinFormsPackagedRuntimeTargetFramework` alongside the exact
runtime/backend package versions in `Sdk/LibreWinForms.Sdk.Versions.props`.
`LibreWinFormsRuntimeTargetFramework` is the packing input (default net10.0 for
the current release lane); the source-first packer passes its actual shared
payload TFM explicitly. Unknown/missing metadata fails closed. The authored
package contract compares the declaration against the actual runtime/backend
`lib` and `ref` assets and nuspec dependency groups, not the SDK packaging
project's own TFM. Changing versions manually remains subject to NuGet's actual
asset compatibility checks; metadata cannot make incompatible binaries usable.

## Application initialization remains owned by the SDK

An SDK C# `Exe`/`WinExe` with absent Forms properties now enables the same
portable bootstrap and configuration path as explicit `UseWindowsForms=true`.
The caller's value is retained until the project body is evaluated; the SDK then
disables `UseWindowsForms` before importing Microsoft.NET.Sdk targets, avoiding
the WindowsDesktop target requirement. Explicit false and
`LibreWinFormsUseSystemWindowsForms=false` remain opt-outs. An ordinary library
does not gain initialization; an explicitly enabled Forms library retains its
existing behavior. No VB application bootstrap is added.

The existing module initializer, Drawing ABI check/replacement, modal startup
parsing, analyzer payload, configuration owner, DPI/default-font hooks, and
separate `GenerateApplicationBootstrap`/`GenerateApplicationConfiguration`
opt-outs are unchanged. Canonical WFI remains the separately qualified package
path; this correction does not replace it with a compatibility shim or add WPF
to an ordinary Forms application.

## Authored controls; execution deferred

`eng/librewinforms-sdk-framework-contract.py` consumes already-produced packages,
compares their declared/actual framework contracts, and uses the real SDK for
minimal net10/net11 builds, initialization opt-outs, diagnostic-code rejection,
and Project-reference framework metadata. It never launches an application or
initializes a GPU. Its future invocation follows the unchanged original
source/package/analyzer gates in the source-first packing script. Existing
net11 template, visible popup and canonical WFI inventories are not replaced.

No build, restore, test, XML verifier, package probe, native UI run, or CI dispatch
was performed while authoring this fix. These controls do not establish package
runtime or desktop qualification. Dependency pins and default branches remain
unchanged.
