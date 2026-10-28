# Source Windows runtime selection

Project applications and Drawing source tests use the exact qualified ProGPU
producer's owned Windows WebGPU runtime when that producer exposes the Windows
runtime staging contract. `LibreWinForms.WindowsRuntime.props` prepares the root
before the existing shared Backend import; the native runtime target imports the
Windows target itself only when the shared Xlib path did not already do so.
`ProGpuWgpuWindowsSourceRuntimeRoot` remains an explicit caller override, with
`ProGpuWgpuWindowsRuntimeRoot` and the producer's standard artifact path as defaults.

Backend owns `wgpu_native.dll`, its build and Windows runtime receipts, and the
five original license notices. The Forms raw native artifact selection excludes
only those eight top-level members for `win-x64` and `win-arm64`. Renderer libraries,
private files, other platforms and the source artifact itself remain intact. The
producer target selects the owned files and preserves its portable `runtimes/`
layout or explicit-RID executable-root layout. This selection does not enable
DXC or change the caller's compiler choice.

A new-capability Windows executable or test fails before build if its selected
DLL or receipt is missing. A portable executable on Windows requires both Windows
RID pairs. Non-Windows portable builds do not require Windows payloads at this
boundary; ordinary libraries, Package mode, disabled source Drawing and the older
qualified producer retain their existing behavior. The presence guard is not
provenance validation: qualified staging independently verifies original package
identity, source commit, complete payload inventory and producer hashes before
publishing those files.

`eng/tests/test_windows_source_runtime.py` exercises the actual Forms import and
ownership boundary using metadata-only producer recorders and inert payload
names. It runs on all three CI hosts so the portable guard is exercised against
the actual Windows host, without spoofing an OS or loading a native library.
These controls do not qualify a new producer, native rendering, packages or
original application pixels. The existing ProGPU pin remains unchanged.

CI and Release stage through immutable LibreWPF helper commit
`065e8cc0c027566f1316169063ba92f14fa715fb`. The six arguments retain the original
ProGPU commit, native, optional DX12 and Xlib destinations and add the Windows
destination plus the exact producer source root. The helper verifies committed
capability and unchanged verifier inputs, accepts legacy producers only when no
Windows payload is published, and validates new payloads with the producer's
own verifier before publication. Release uses this helper independently of the
selected canonical WPF source ref, including older-tag recovery. The Windows
theme source job stages only when its selected producer declares this capability;
its original no-input fixture and deadlines are unchanged.

This helper serializes Linux package inventory names with forward slashes on
every host. Windows path separators therefore cannot reject the same valid
two-architecture payload; exact member sets, original hashes, producer receipts
and ELF checks remain required. The producer pin and canonical WPF source are
independent of this staging-only correction.
