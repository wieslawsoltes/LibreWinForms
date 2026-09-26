# Canonical drawing identity and assembly-load collisions

Canonical LibreWinForms requires `ProGPU.System.Drawing.Common`. Its runtime
assembly is named `System.Drawing.Common` and has public key token
`c29c9752855ee183`. Microsoft's Windows drawing implementation has the same
simple assembly name but token `cc7b13ffcd2ddd51`; it does not implement the
portable ProGPU drawing contracts.

These changes prevent incompatible selected assets and improve the early
failure diagnostic. They do not repair an already-loaded Microsoft assembly,
qualify an isolated load context, or close every hosting scenario in issue #23.
An assembly-load context accepts only one version per simple assembly name;
remove the conflicting dependency and restart the process. See Microsoft's
[assembly-load-context rules](https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/understanding-assemblyloadcontext).

## Build and publish boundary

The shared `LibreWinForms.DrawingIdentity.targets` is shipped in both the SDK
and the canonical Forms package's `buildTransitive` assets. It uses
[`GetAssemblyIdentity`](https://learn.microsoft.com/en-us/visualstudio/msbuild/getassemblyidentity-task)
on actual selected files, checking the assembly name and public key token.
RAR identity metadata also selects renamed drawing inputs for verification;
the metadata is not accepted as proof of the file's identity.

| Boundary | Selected files |
| --- | --- |
| Before compilation, after reference-assembly selection | `ReferencePathWithRefAssemblies` |
| Before copy-local | `ReferenceCopyLocalPaths` |
| After output copying | Selected destinations and an existing loose drawing payload |
| Before single-file bundling; after normal publish-list computation | `FilesToBundle` and `ResolvedFileToPublish` |
| After publish copying | Selected loose publish destinations |

`LWFDRAW001` reports an incompatible identity and its path. `LWFDRAW002`
reports a missing selected file or required copy destination. Invalid managed
assembly files also fail the metadata-reading task. This is an ABI-family check,
not signature verification or proof of an exact aligned package version.
The existing source-first drawing hash checks remain authoritative and unchanged.

A Microsoft `PackageReference` with `ExcludeAssets="all"` does not fail simply
because it exists: only selected assets are inspected. Libraries without a
copy-local drawing asset do not need an invented loose DLL. Shared-output builds
do not require a skipped copy, but an existing incompatible output still fails.
Bundled drawing assemblies do not require loose published DLLs. Normal SDK
duplicate-publish-output rejection remains in force.

## Early SDK bootstrap diagnostic

The generated module initializer has a BCL-only outer body. A separate
`NoInlining` helper reads the typed `NativeFontInteropServices.IsRegistered`
property before a second `NoInlining` helper enters backend registration.
The first call performs no registration or font fallback. Keeping the helpers
separate lets the outer initializer report a missing drawing ABI even when
the loader fails while preparing the typed helper for execution.

Only assembly-binding failures at that ABI probe receive the dependency
diagnostic; ordinary backend errors are not relabeled. The original loader
exception is retained. There is no runtime reflection, resolver installation,
Microsoft drawing fallback, or attempt to replace an assembly already loaded
into the process.

## Executable qualification

`eng/test-drawing-runtime-identity.py` imports the exact shared targets and
generated bootstrap. Its fixtures exercise real assembly identities, renamed
RAR references, both fresh-process first-load orders, missing/wrong selected
assets, common-output/no-copy libraries, nested publish destinations, and
normal/pre-bundle/post-copy ordering.

The source-first package script runs the complete suite against its freshly
produced canonical package closure and consumer output. That additionally
builds and runs normal-SDK and LibreWinForms.Sdk consumers, verifies the actual
restored canonical package bytes, retains an excluded Microsoft package as a
passing control, and builds/runs normal and single-file publications. A wrong
selected compiler or publish asset must fail before compilation/publication.
The Microsoft negative control is pinned to package `10.0.12` and restored
only into an isolated temporary cache unless an explicit existing DLL is supplied.

Local development checks ran on .NET 10 and .NET 11. Local package-structure
checks used the current packaging projects with explicitly staged, aligned
published `.65` canonical ref/lib payloads; they were not a fresh canonical
source build. The full source-first CI producer and package gate remains
required. These loader/package checks are not native rendering qualification.
