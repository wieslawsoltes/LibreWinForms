# SDK application DPI policy

The C# portable SDK connects `ApplicationHighDpiMode` to the original
`Application.SetHighDpiMode` API. For example:

```xml
<PropertyGroup>
  <UseWindowsForms>true</UseWindowsForms>
  <ApplicationHighDpiMode>SystemAware</ApplicationHighDpiMode>
</PropertyGroup>
```

Call `ApplicationConfiguration.Initialize()` before creating a window. An absent
or empty property uses the original `SystemAware` default. The original defined
values are `DpiUnaware`, `SystemAware`, `PerMonitor`, `PerMonitorV2`, and
`DpiUnawareGdiScaled`. Parsing retains upstream case-insensitive enum names and
defined numeric values; undefined names/numbers report the original WFO0002
diagnostic naming the property and value. Arbitrary text is never inserted into
generated C#.

## Source ownership and ordering

The implementation reuses `ProjectFileReader.TryReadHighDpiMode` and
`PropertyDefaultValue.DpiMode` from the original WinForms analyzer. It does not
implement another MSBuild parser. Only the SDK's actual global partial
configuration class receives `ConfigureHighDpiMode`; both the existing SDK-owner
marker and compiler-phase Initialize-emission predicate are required.

The SDK keeps its compatible-text-rendering call, followed by DPI policy and then
the optional default-font hook. This matches the original
`ApplicationConfigurationInitializeBuilder` requirement that DPI precede font
selection. Existing backend bootstrap and visual-style policy are unchanged.
Top-level, block-namespace and file-scoped callers retain one global Initialize.

`LibreWinFormsGenerateApplicationConfiguration=false` leaves Initialize and its
DPI policy entirely to the caller, including ignoring invalid project-property
values. Ordinary libraries without Forms enabled receive no supplement. An
explicitly enabled Forms library retains its existing SDK class ownership.
Late properties are consumed at the existing editorconfig capture phase; no
evaluation-time cached ownership flag is introduced. The ordinary upstream
generator remains unchanged when SDK ownership is absent/false. VB diagnostics
are unchanged; this does not add a VB application bootstrap.

## Gates and limits

The source-generator gate retains all 28 existing cases and adds 14 controls,
requiring 42 executed cases and zero skips. The historical .NET Framework 4.7.2
oracle checks exact generated text and marks the modern API/enum it lacks;
modern installed consumers separately require those APIs to compile normally.

The source-first package gate retains all 54 existing compiler controls and adds
17 in each Project/Package mode: absent/empty defaults, all five modes, case and
numeric forms, three invalid values, caller ownership, two late-property cases,
and explicit process policy before Initialize (PerMonitorV2 and DpiUnaware).
Each valid DPI consumer invokes actual Initialize in a fresh process and
checks public `Application.HighDpiMode`, without creating a window or sending
input. Independent expected source checks enforce DPI-before-font ordering.
All existing exact 42-file analyzer payload comparisons, four damaged archives,
missing-file controls, source identity checks and 300-second case bounds remain.

The generated default call must not overwrite a successful earlier process DPI
choice. Native Windows rejects a second process-policy setter; the portable
runtime must retain that same one-time admission contract, including explicitly
chosen DpiUnaware. A getter's default enum value is not evidence that a policy
was already chosen. This prerequisite is tested using the public setter and
actual Initialize, not by suppressing the generated call.

These are compiler and managed policy contracts, not native DPI qualification.
SystemAware primary-monitor sampling, logical/device coordinate mapping, source
auto-scale, native popup placement and desktop pixels require their separate
runtime implementation and real Windows/reference evidence. In particular,
honoring the property alone does not repair the observed two-to-one portable
source/native rectangle mismatch.

## Primary references

The original parser, shared defaults, generator builder and
`Application.SetHighDpiMode` source are the implementation provenance. The
inspected Microsoft [desktop SDK property contract](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props-desktop#applicationhighdpimode)
specifies the SystemAware default and generated call. The
[HighDpiMode API contract](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.highdpimode?view=windowsdesktop-10.0)
distinguishes modes, while the [automatic scaling contract](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/forms/autoscale)
describes source layout scaling. None of these references makes a generated call
proof of native monitor, placement or rendering parity.

## Local implementation evidence

The analyzer source compiled with SDK `11.0.100-preview.5.26302.115`; all 42
generator cases passed on .NET 10.0.5 with zero skips. The final incremental
compile had zero warnings/errors. Existing cache/snapshot controls passed 9/10
cases respectively, and the documentation verifier passed.

A new SDK package from product commit `cb8edd82e` was compiled and packed with
`ContinuousIntegrationBuild=true`. Its SHA256 was
`5927e258579f3bc4a6cc54069ab6c7ae5f07ccd3c2b3f5edc8073e64ea46bc77`.
The original 44-case installed-Package run passed, including exact 42-file
producer comparisons and four rejected archive controls. Runtime dependencies
were unchanged packages from successful PR74 Build `36276743597`, artifact
`10916489916`; this is not a new complete source/package producer qualification.

The two subsequent prior-policy cases both failed against that unchanged
runtime: explicit PerMonitorV2 and explicit DpiUnaware were overwritten by the
generated SystemAware call. Their compile/runtime logs are retained separately
under `artifacts/sdk-dpi-evidence/prior-policy-negative`. These negative controls
require the separate runtime first-successful-policy fix before the composed
88-case Project/Package matrix can pass. No native window or input was exercised.
