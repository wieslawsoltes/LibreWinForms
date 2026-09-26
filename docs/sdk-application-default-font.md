# Explicit application default font

The C# portable SDK now connects the existing `ApplicationDefaultFont` project
property to the original `Application.SetDefaultFont` method. For example:

```xml
<PropertyGroup>
  <UseWindowsForms>true</UseWindowsForms>
  <ApplicationDefaultFont>Arial, 14.25px, style=Bold, Italic</ApplicationDefaultFont>
</PropertyGroup>
```

Call `ApplicationConfiguration.Initialize()` before creating a window. Font
family availability and system text scaling remain the existing runtime's
responsibility. This setting is not an OS system-font provider, a native visual
qualification, or a fix for the unchanged official DataGridView sample: that
sample neither sets this property nor calls generated initialization.

## Source ownership and policy

The implementation reuses the repository's original C# analyzer
`ProjectFileReader.TryReadFont`, its invariant `FontConverter`, and shared
`ApplicationConfig.FontDescriptor.ToString`. No parser or font defaults are
duplicated in MSBuild. Invalid supported-format input retains WFO0002, including
the property, raw value, and original reason. The canonical descriptor sanitizes
family-name punctuation; it does not preserve arbitrary quoted family names.
This existing behavior is tested rather than replaced with new string escaping.

The SDK still owns the global `internal static partial ApplicationConfiguration`
class. Its existing `SetCompatibleTextRenderingDefault(false)` call is followed
by an optional private partial `ConfigureDefaultFont` hook. Only an explicit
nonempty parsed font supplies an implementation calling `SetDefaultFont`.
Otherwise C# erases the hook and call. No `EnableVisualStyles`, DPI-setting call,
new system font, window creation, or bootstrap behavior is introduced.

`LibreWinFormsSdkGeneratesApplicationConfiguration` shares the existing class
emission predicate: portable framework references and SystemWindowsForms enabled,
and `LibreWinFormsGenerateApplicationConfiguration` not false. There is no added
output-type restriction; an explicitly enabled Forms library retains its prior
class ownership. Both this effective property and the separate SDK ownership
marker are required for the supplement, so it cannot attach to an unrelated
upstream class. Top-level, block-namespace, and file-scoped consumers all use the
SDK's same global owner.

The compiler-visible effective flag is refreshed at MSBuild's actual editorconfig
property-capture phase, after `PrepareForBuild`, rather than frozen during project
evaluation. The SDK source-emission target retains its original execution-time
three-clause condition. Properties intended for a source generator must be set
before that compiler configuration is captured. The gate changes generation and
Forms enablement in `PrepareForBuild` targets and compares the captured flag with
actual class/supplement emission, including late caller opt-out and SDK enablement.
The portable-framework predicate is changed immediately before the compiler
policy-capture target, after reference resolution. Turning off the entire portable
graph during preparation also disables its existing Project-reference path
normalization and fails before compilation on the current macOS SDK (retained
MSB3202 evidence). That is not a supported Project source-graph transition and
is not repaired or qualified by this font change.

`LibreWinFormsGenerateApplicationConfiguration=false` leaves the class and font
policy to the caller. It suppresses both the supplement and its diagnostics,
even with an invalid `ApplicationDefaultFont`. Without SDK ownership, the
original generator continues to emit its original initialization and defaults.
VB diagnostic support remains unchanged; this is not a new VB bootstrap.

## Qualification

The source generator suite preserves all previous 16 cases and adds 12 controls,
with a 28-executed-test minimum and zero skips. Its original .NET Framework 4.7.2
reference oracle still marks the framework's missing `SetDefaultFont` member in
expected generated-source fixtures, just as the original font fixtures do.
That source-output check is separate from the current runtime consumers below.

The installed SDK analyzer contract retains its previous 22 compiler controls,
42-file source/package payload comparison, and four damaged-archive rejections.
It adds 16 consumers in each of Project and Package modes: three entrypoint
forms; punctuation sanitation; absent, empty, and whitespace defaults; invalid
format; caller-owned initialization; an ordinary library; and an explicitly
enabled Forms library, a fresh-process test-family discovery, and four late
MSBuild-property controls. That discovery
uses the actual runtime's public generic-family descriptor, not an assumed OS
font installation; it does not read the Control default-font cache. All applicable
generated source is compared to independent
expected statements. Modern consumers must compile without compiler warnings;
valid, default, and caller-owned cases execute in separate processes with a
60-second bound and check the actual `Control.DefaultFont` after initialization.
They do not show a window, dispatch input, or initialize a GPU fixture.

The gate retains compiler SARIF, generated source, exact selected analyzer
paths/hashes, package provenance, and runtime logs. The existing whole Build and
native application gates remain independent and mandatory. This compiler-policy
connection does not establish font metrics, auto-scale, title-bar, IME, or visual
parity on any native host.

## Primary references

The source files above and canonical `Application.SetDefaultFont`/`ScaleDefaultFont`
are the implementation provenance. Microsoft's
[MSBuild property contract](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props-desktop#applicationdefaultfont)
documents the invariant font syntax and the empty-property default. The
[SetDefaultFont contract](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.application.setdefaultfont)
requires initialization before the first window and retains runtime text scaling.
Both references were inspected for this change; neither warrants hardcoding an
OS font or changing the SDK's existing absent-property DPI/bootstrap policy.
