# Portable SDK analyzer ownership

`LibreWinForms.Sdk` supplies the repository's original WinForms analyzers in both
`Project` and `Package` reference modes. This is compiler integration, not a
replacement diagnostic implementation or native UI qualification.

## The missing contract

The portable SDK deliberately turns off the Windows Desktop SDK's
`UseWindowsForms` import. Previously this also omitted its analyzer payload.
As a result, a custom `Control` with a public property lacking serialization
policy compiled without the original `WFO1000` error.

The unchanged Microsoft DataGridView masked-column sample at dotnet/samples
commit `acb39ceb13f910ae0f8f6298059c59102b749c41` reproduced the omission: changing
only its SDK/target-framework wiring produced no WFO1000 diagnostics. Adding the
actual source-built shared and C# analyzers reported nine WFO1000 errors in
`MaskedTextBoxColumn` and `MaskedTextBoxEditingControl`. The sample bytes were
retained and compared with that commit; the errors were not manufactured by a
replacement sample or diagnostic.

Blindly adding the complete C# analyzer assembly exposed a second problem:
`ApplicationConfigurationGenerator` also emitted `Initialize`, conflicting with
the SDK's existing configuration class in an ordinary top-level program
(`CS0111`). Dropping that assembly, filtering its diagnostics, or changing the
application's runtime configuration is not the fix.

## Source and package contract

The SDK references these existing source projects without copying or rewriting
their diagnostic algorithms:

- `src/System.Windows.Forms.Analyzers/src`: shared implementation and resources.
- `src/System.Windows.Forms.Analyzers.CSharp/src`: C# diagnostics and generator.
- `src/System.Windows.Forms.Analyzers.VisualBasic/src`: VB diagnostics.

Packing reuses `eng/packageContent.targets` / `GetPackageContent`, including the
original resource satellites. The payload has three main assemblies and 39
satellite assemblies at the current source revision. C# and VB use their
respective `analyzers/dotnet/cs` and `analyzers/dotnet/vb` directories; the shared
assembly/resources use `analyzers/dotnet`. The Czech resource culture is also
named `cs`; it is not a second C# language selector.

Only the shared and selected language assembly become compiler `Analyzer` items.
Project mode builds the original projects, and package mode uses the installed
SDK's own files. A missing selected packaged DLL is a build error before
compilation. Roslyn compiler packages, code-fix/test dependencies, and PDBs are
not bundled as runtime dependencies or analyzer payload.

The compiler-visible `LibreWinFormsSdkOwnsApplicationConfiguration=true` marker
prevents only the original C# configuration generator from producing output.
Its behavior is unchanged when the property is absent or false. The SDK retains
its existing `Initialize` body and bootstrap. In particular,
`LibreWinFormsGenerateApplicationConfiguration=false` still means that the
caller owns the class; it does not silently re-enable upstream generation.
These controls include top-level, block-namespace, and file-scoped programs.

VB qualification here is the actual diagnostic assembly running on an ordinary
class-library consumer. It does not introduce or claim a new VB application
bootstrap or designer-host contract.

## Validation and retained evidence

`eng/librewinforms-source-first.sh` runs the complete 16-case configuration
generator test class, including the original absent-property controls and the
explicit SDK-owner controls. The original generator deliberately joins adjacent
application statements with CRLF. Six unmodified golden tests failed on an LF
checkout before this change. The fixture loader now reconstructs only those
documented statement joins in the expected text, with its own exact byte test;
the generator output and other fixture bytes are not normalized or changed.

`eng/librewinforms-pack-source-first.sh` invokes
`eng/librewinforms-analyzer-contract.py` on its freshly produced SDK and canonical
runtime package feed. The gate retains compiler SARIF, source inputs, generated
configuration, compiler analyzer paths/hashes, source revision, and package/file
hashes under `artifacts/log/analyzer-contract.*`. Its controls cover:

- C# and VB WFO1000 negative cases plus explicit serialization-policy positives,
  in both real Project and installed Package modes.
- Duplicate-Initialize prevention in all three C# entrypoint forms, caller-owned
  opt-out, and failure when the opted-out caller supplies no configuration.
- The ordinary upstream generator with the ownership property absent, including
  its original visual-style, rendering, and DPI defaults.
- Exact archive-to-source DLL equality and the complete resource inventory.
- Rejection of scratch archives with missing, modified, extra, or duplicate
  analyzer entries, and actual build rejection after each selected DLL is
  removed from a private extracted SDK copy (never a package cache).

The complete Build, existing source/package runtime checks, and visible native
application gates remain required and unchanged. Analyzer success does not prove
DataGridView editing, IME, native title bars, or rendering parity.

## Primary references

The source projects above are the authoritative implementation provenance.
Microsoft's [WFO1000 diagnostic documentation](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/compiler-messages/wfo1000)
and [.NET 9 serialization analyzer change](https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/9.0/security-analyzers)
were consulted for the observable contract: use an explicit `DefaultValue`,
`DesignerSerializationVisibility`, or `ShouldSerialize` policy to avoid accidental
designer serialization. This work preserves that policy rather than suppressing
WFO1000 globally. The SDK smoke control's private test-only double-buffering
probe is explicitly marked non-serialized; no product property is exempted.
