# Automation thread locale

The portable `System.Private.Windows.Core` automation helpers now obtain their
LCID from the calling thread's `CultureInfo.CurrentCulture` on non-Windows hosts.
Both `IDispatch.GetIDOfName` and `SetPropertyValue` previously called the Windows
`GetThreadLocale` entry point unconditionally (the remaining automation path in
issue #26). Windows and nonportable builds keep the original native call,
including native thread-locale selection independent of the managed culture.
No locale is cached, replaced with English, or inferred from the UI language.

## Validation

`bash eng/librewinforms-automation-locale.sh` builds the actual private Core and
its existing signed test assembly, then runs eight required cases without a
renderer or desktop runtime. A test-owned unmanaged vtable records the actual
helpers' LCID, HRESULT, dispatch identifier, and named property-put arguments.
English, Japanese, Arabic, and invariant cultures each exercise both helpers
independently, with a second culture change and a different UI culture.

On macOS ARM64, the changed source passes all eight cases with no skips. The
unchanged test executable fails all eight against the prior freshly produced
PR #54 package's private Core, at the original `KERNEL32.dll` calls. That baseline
DLL is SHA-256 `96e6b1a809714f8d72938fed2e4cafbd5401f1dd094708878e21b820a4992918`,
from immutable CI artifact `10905383442`. The negative control uses an isolated
test-output copy; no package cache, system DLL, or original build output is edited.

The PR adds focused Windows, Linux, and macOS CI jobs while retaining all existing
source, drawing, package, and visible consumer gates. Those fresh-checkout jobs
remain required before merge. The local test used the repository SDK 11 to build
the portable net10 target. A build with an explicitly absent ProGPU source root
also passes: this focused private-Core graph does not need a renderer checkout.
Incremental compilation has zero errors; full test-utility compilation retains
its 32 existing trimming warnings.

This repairs locale acquisition at the existing typed automation boundary. It
does not implement native COM activation, ActiveX hosting, or general OLE support
on non-Windows systems, and it does not change font fallback policy. No public
package release is claimed.
