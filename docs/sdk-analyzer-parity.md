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

The SDK packaging project is intentionally independent of Arcade. Its original
analyzer project references and `GetPackageContent` calls explicitly retain the
SDK's resolved `NuGetPackageRoot`. Otherwise `ContinuousIntegrationBuild=true`
made Arcade look under the checkout's `.packages` while the SDK's actual restore
used a different NuGet root. The missing NETStandard.Library build import left
the compiler without its core reference assemblies. This was reproduced with
the exact CI pack properties (256 compiler errors), not attributed to Linux or
to generator behavior. No CI flag, original analyzer source, or cache file is
rewritten to fix it.

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
generator test class, requiring at least 16 executed tests and rejecting skips,
including the original absent-property controls and the explicit SDK-owner
controls. The original generator deliberately joins adjacent
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

Producer-byte checks run before any Project-mode consumer. Those consumers
legitimately rebuild the original analyzer paths with their own version/build
properties, so comparing the archive to those later outputs is not a comparison
to its producer generation. Both packaging lanes exposed this sequencing error
after the restore-root correction. Moving the entire compiler suite earlier
then moved the cold source build into its first 300-second diagnostic case;
the canonical Linux lane timed out there. The original mandatory SDK Project
smoke now retains that cold-build ownership, without adding a warmup or changing
any deadline.

After all producer byte guards and the package-only smoke, the gate compares
all 42 original source DLLs with the archive and copies those source bytes into
a read-only producer snapshot. Its manifest binds their hashes and original
paths, the entire package SHA256/path, configuration, exact clean source commit,
and actual recursive submodule/engine identity. The outer shell retains the
manifest digest independently and passes that same value to the later verifier.
After the original Project/Package smokes and drawing-identity checks, the full
compiler suite validates the snapshot and original archive both before and after
its cases. Later mutable `bin` files are never used as the producer reference.
Standalone invocations without a snapshot still compare the actual current
source outputs directly.

The Project compiler cases borrow the original SDK Project smoke's private
NuGet cache until the enclosing gate exits. Removing it early and restoring
identical reference DLLs at different absolute paths invalidated the source
runtime's `CoreCompileInputs` and caused another cold compile. The optional
`--project-packages` handoff requires the caller's scratch ownership, verified
producer snapshot, existing non-symlink cache and byte-identical installed SDK
archive. Project source-root globals also match the original smoke. This is
ordinary incremental building, not `NoBuild`, an extra warmup, or a timeout
extension. All compiler cases and diagnostic assertions still execute. Package
and damaged-SDK controls retain their separate fresh cache; standalone Project
invocations stay cold. Cache handoff is accepted only for the complete default
`--reference-mode Both` matrix: its Package cases populate the separate fresh
cache used by the ordinary upstream-generator control. Combining handoff with
Project-only or Package-only fails before consumer cases; all no-handoff modes
remain available unchanged. Routing and rejection controls live in
`eng/test-librewinforms-analyzer-cache.py`. Full compiler and CI timing evidence
for this handoff remains required; path stability alone is not a speed claim.

`eng/test-librewinforms-analyzer-snapshot.py` covers modified/missing/additional
snapshot files, symlinks, writable files, forged manifests/hashes, source and
submodule changes, configuration/package identity, and legitimate later consumer
rebuilds. These are transport fixtures, not replacement DLL/compiler tests. The
original four damaged-archive controls still reach the exact entry-byte comparer
against the validated snapshot; they are not short-circuited by a package hash
mismatch. All real compiler path, installed-package byte and diagnostic checks
remain unchanged. Qualification includes the whole source-first packing script,
not only a standalone pack followed by the analyzer harness.

Before producing its final SDK archive, the source-first packaging gate also
runs `eng/librewinforms-sdk-analyzer-pack-contract.py`. It packs the actual SDK
with `ContinuousIntegrationBuild=true` first using the default NuGet root and
then a new explicit private cache, with a separate 300-second bound per pack.
Both diagnostic archives must retain all 42 exact source-built analyzer files.
Their logs, commands and file hashes stay under the same uploaded evidence
prefix, outside the producer package feed. The private test cache is owned by
that verifier and removed on exit; no user/package cache content is copied or
altered. The final producer restore/pack then runs with its unchanged properties.

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
