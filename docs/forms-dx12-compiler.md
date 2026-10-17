# Explicit Windows compiler selection

Windows package consumers can explicitly select the existing ProGPU DXC runtime:

```xml
<RuntimeIdentifier>win-arm64</RuntimeIdentifier>
<LibreWinFormsDx12ShaderCompiler>Dxc</LibreWinFormsDx12ShaderCompiler>
```

`win-x64` is also supported. This requires the rebuilt SDK/backend and an
executable package consumer with its ordinary bootstrap enabled. `Automatic`
retains the existing environment/default behavior; `Fxc` explicitly selects FXC.
An absent or empty compiler property also defaults to `Automatic` for existing
direct targets imports that do not import the SDK props.
Invalid values, unsupported RIDs, libraries, disabled portable references,
project mode and disabled bootstrap fail before application generation. There
is no implicit switch or fallback after a compiler failure.

DXC adds `ProGPU.Backend.Dx12` at the exact `LibreWinFormsProGpuPackageVersion`,
including central package management. The generated module initializer passes
an immutable compiler descriptor to `ProGpuPlatform.Register`. Direct hosts can
use the two-argument `Register`/`CreateServices` overloads or set
`SilkWindowService.Dx12CompilerOptions`. Existing overloads retain their binary
identities and defaults. No application argument or environment variable is
rewritten. Popups inherit their actual live owner's descriptor when unspecified;
an explicit conflict reaches the existing core rejection. Recovery uses the
replacement owner, and original unpublished renderer retirement remains intact.

Source and release packaging stage compiler/runtime/notices only from the exact
whole-successful ProGPU Build used by the other native packages. The compiler
payload has a separate staging directory and the original package validation.
The consumer's loaded native library, compiler architecture and manifests remain
validated by ProGPU. System libraries and package-cache files are never patched.

## Startup evidence and qualification

Original grid runs 39013197920 and 39013729601 complete Microsoft's editing
phases, but portable startup emits no source snapshot within 20 seconds. The
managed-stack observer also reaches its own bound without output.

Native diagnostic 39014636630 preserves the original 20/60-second controller
limits, input, payload and failures. Its five native helper controls pass first.
One read-only owned-process snapshot at age 12 seconds completes in 0.484 seconds,
retaining 3,994,419 bytes with ARM64 metadata, exact PID 6052 and normal-dump flags.
The UI stack includes the packaged `wgpuDevicePoll` while a WARP thread is active.
The matching-GUID Microsoft symbol map places that thread in
`CProgram::RemoveAssignDown`, called from `CProgram::Compile`: this points to
shader compilation during the observed GPU wait, not the Forms editor event
handler. One snapshot does not measure the complete startup interval or prove
that changing compilers fixes the application.

The explicit DXC path requires fresh whole producer qualification, the original
paired grid workload and image review before release. Host/SDK tests establish
transport, ownership and package selection only; they are not Windows startup,
pixel or input qualification. The original Automatic failures remain retained.

The original-grid workflow accepts an explicit `Dxc` compiler and an exact
producer JSON manifest after that producer's whole Build and Docs succeed.
The manifest includes the artifact identity/digest and every package's source
commit, version, size and SHA-256; the runner rechecks all nine Build jobs and
the complete downloaded feed. Its default retains the original checked-in
Automatic producer. An override does not relax any producer gate.

DXC selection is passed as SDK build properties with an explicit `win-arm64`
RID and the qualified package version. All 52 original source hashes and the
passive observer remain unchanged. The consumer must contain the exact compiler,
native runtime, manifests and notices. The original 20/60-second paired protocol
also checks the real device's DXC selection and library path from the existing
backend diagnostics, then rechecks all output hashes. A compiler request alone
does not qualify a loaded runtime or a completed application.

Build and Docs check out and verify the exact pull-request head (or the triggering
commit for branch/manual runs) in every owned lane. The GitHub synthetic merge
commit is not the qualified producer. Build 39021091670's canonical packages
record merge commit `940c0742f055474d48affa1a89f759b77950095b`, although its run
identifies PR head `715b8fddeb0778a49fb014d5b5e49f2c67bbf4e6`; that artifact is
retained as evidence and is not admitted as an exact-head producer. The actual
checkout check precedes builds; package repository metadata is never relabeled.
