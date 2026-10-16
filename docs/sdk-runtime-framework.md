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
<Project Sdk="LibreWinForms.Sdk/0.1.0-preview.70">
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

### Global Forms requests

An IDE, command line, or parent MSBuild task can supply `UseWindowsForms` as a
global property. In preview.67 that prevented the SDK's later assignment to
`false`, so Microsoft's original WindowsDesktop check rejected a portable
`net10.0` project with `NETSDK1136`. Supplying the property only in the project
body did not reproduce this failure in the published package. This identifies
one reproducible cause of [#166](https://github.com/wieslawsoltes/LibreWinForms/issues/166);
the reporter's Visual Studio invocation still needs confirmation.

`Sdk.targets` now marks only `UseWindowsForms` as local at the point where the
SDK consumes the request. Its incoming true/false value still selects portable
Forms and initialization, with normal global-property precedence over the
earlier project body. The SDK then clears the WindowsDesktop flag before the
Microsoft targets import. `LibreWinFormsUseSystemWindowsForms=false` remains an
opt-out; other global properties, supported TFMs and package identities retain
their existing rules. See Microsoft's
[TreatAsLocalProperty contract](https://learn.microsoft.com/en-us/visualstudio/msbuild/project-element-msbuild).

The package framework harness adds actual compiler cases for global true,
global false, and an explicit portable-Forms opt-out under global true. Each
checks generated initialization, original analyzer diagnostics and final
compiler references as well as the cleared WindowsDesktop flag. Local source
evaluation alone does not qualify the final package or the reported IDE build.

## Authored controls; execution deferred

`eng/librewinforms-sdk-framework-contract.py` consumes already-produced packages,
compares their declared/actual framework contracts, and uses the real SDK for
minimal net10/net11 builds, initialization opt-outs, diagnostic-code rejection,
and Project-reference framework metadata. It never launches an application or
initializes a GPU. Its future invocation follows the unchanged original
source/package/analyzer gates in the source-first packing script. Existing
net11 template, visible popup and canonical WFI inventories are not replaced.

The combined framework harness requires the repository's .NET 11 SDK host so it
can compile both TFMs. A net10 target compiled by that host is not qualification
of a .NET 10-only toolchain. Final qualification must also restore/build the
minimal net10 Package-mode application with the actual stable .NET 10 SDK,
including the original packaged analyzers, generated configuration/bootstrap,
and Drawing replacement. That distinct package-consumer gate remains pending;
no compiler-host compatibility or runtime success is inferred from TFM admission.

No build, restore, test, XML verifier, package probe, native UI run, or CI dispatch
was performed while authoring this fix. These controls do not establish package
runtime or desktop qualification. Dependency pins and default branches remain
unchanged.

## Integration framework controls (2026-10-11)

The package lane verified all 42 original analyzer files, four rejected archive
controls and 88 compiler contracts, then exposed a stale framework assertion:
a C# consumer was incorrectly required to reference the Visual Basic analyzer.
The SDK correctly selects the shared analyzer plus the consumer's language
analyzer while retaining all three original assemblies in its package.

Framework observation now evaluates C# and Visual Basic projects separately for
both net10.0 and net11.0. Each must report the same three original runtime
references at the requested TFM and exactly the shared/language analyzer pair at
netstandard2.0. Missing, extra or retargeted analyzers still fail. All four real
GetTargetFrameworks evaluations pass locally using the current SDK source
imports; this is source evaluation, not a packaged application result. Complete
package CI remains required before release.

## Complete Project graph policy (2026-10-11)

The actual source-first package consumer exposed concurrent writes to
Accessibility dependency metadata and Forms.Primitives resources. NuGet-added
transitive project references had lost the canonical direct references' global
properties. MSBuild consequently scheduled two instances of the same source
project against one output directory.

The SDK now restores Project mode with MSBuild static graph evaluation, selected
before the Microsoft SDK imports NuGet targets. This preserves per-edge
AdditionalProperties during restore; recursive path-only restore otherwise
selects the upstream net11 default even for a net10 consumer. After NuGet adds
transitive compiler references, canonical Forms and the selected ProGPU source
closure receive the same original framework and Drawing policy as direct edges.
Caller-owned projects and original analyzer references keep their own policy.
Source-root discovery reuses the source checkout's existing ProGPU props.

The original net11 source-first application compiled with four parallel build
nodes and completed its unchanged API/designer/Drawing smoke. No source output
was built twice. New restored-graph controls pass for net10 and net11: all 20
runtime references retain the selected policy, both original analyzers remain
netstandard2.0, and unrelated direct/transitive caller projects remain unchanged.
These checks use actual source SDK imports. The corresponding packaged SDK
controls run in the existing full package lane before release; source checks do
not replace that lane or the separate stable .NET 10 compiler-host gate.
