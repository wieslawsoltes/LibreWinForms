# Portable application process isolation

`LibreWinForms.ApplicationIsolation` is a BCL-only launcher and primitive result
contract. A host that already loads Microsoft's `System.Drawing.Common` keeps
that assembly untouched. Deploy the portable application separately using the
ordinary `LibreWinForms.Sdk` package closure, then launch its apphost or its DLL
with an explicit dotnet executable. Do not add canonical Forms/Drawing packages
to the host or copy their assemblies into its output directory.

```csharp
var start = new PortableApplicationStartInfo(absolutePortableAppDll)
{
    DotNetHostPath = absoluteDotnetExecutable
};
start.Arguments.Add("document-name");
using PortableApplicationSession child = PortableApplication.Start(start);
PortableApplicationExit exit = await child.WaitForExitAsync(cancellationToken);
if (!exit.Succeeded)
    throw new InvalidOperationException($"Child exit {exit.ExitCode}: {exit.ProtocolError}");
if (exit.Reply!.Accepted)
    Console.WriteLine(exit.Reply.Value);
```

The child owns and disposes its real `Form`, `Control`, `Font`, `Image` and other
objects. After its UI closes it calls `PortableApplication.Complete(accepted,
text)`. Only the boolean and nullable string cross the boundary. Arguments are
ordinary strings passed through `ProcessStartInfo.ArgumentList`, not a command
shell. No UI object, handle, delegate, service or exception object is serialized.

Canceling `WaitForExitAsync` cancels only that wait. `Dispose` releases only the
host-side process handle; neither action closes or kills the child's GUI. A host
may wait again. An explicit caller termination/timeout policy may call
`Terminate`, which requests termination of the owned process tree, then await
exit. Do not use termination as normal window closure.

Each launch receives a fresh private result directory. The one-time JSON result
has a strict version, launch nonce and actual PID; duplicate/unknown fields,
missing results, invalid identities and oversized payloads fail closed. Text is
limited to 4,096 UTF-16 code units and the file to 32 KiB. Existing result files
are never overwritten. Stdout/stderr remain the application's normal logs.
`ResultDirectory` is retained, including on failure or handle disposal, for the
caller to inspect and remove only after the child has exited. A failed result
does not replace a nonzero process exit code.

This is application isolation, not a security sandbox, in-process compatibility
layer, assembly renaming, transparent marshaling, embedding or general RPC. The
caller chooses a trusted executable/deployment and its normal inherited runtime
environment. Existing APIs work inside the child, not across the process boundary.
There is no assembly scanning, binding hook, custom load context or Microsoft
Drawing replacement. Desktop interaction and native clipboard behavior require
their existing platform gates separately.

The usable pair is in `samples/LibreWinForms.IsolatedApplication`. The source
package gate compiles that pair, then runs fifteen real child-process contracts
against fresh packages: a host with Microsoft Drawing already loaded, actual
canonical child Control/Font/Bitmap state, exact Unicode/string arguments,
missing/malformed/oversized results, invalid version/identity/field/Unicode with
the actual nonzero exit retained, one-time publication, the
value limit, and wait-cancellation/Dispose lifetime. Both output Drawing DLLs
and the launcher are compared with their selected package bytes. Failed logs
and result files are retained. This gate does not launch visible GUI windows.

A source-linked BCL-only .NET 10 probe compiled with zero warnings/errors and
executed four cases: valid Unicode plus bad version and escaped lone-surrogate
key/value payloads. The three negative cases reproduced escaped exceptions
before correction and now retain the child's exit code 29 with a protocol error.
This does not qualify GUI execution. At `f39cb7fe6`, the Ubuntu package job
`108475083575` passed all fifteen installed-package cases and all 54 existing
analyzer/default-font contracts. Its later release inventory check correctly
rejected the new isolation package because the package list omitted it. The
follow-up adds only that exact package to the shared inventory; missing and
unexpected package checks remain intact. The complete replacement Build still
must pass before merge. The new package is not published to NuGet by this PR;
source-first feed consumption is explicit.

## Independent visible-window contract

The three existing visible installed-package CI jobs additionally run
`eng/test-application-isolation.py --window` against the same exact producer
feed. This is a separate case; it does not replace or skip any of the fifteen
protocol/lifetime cases. The Microsoft-first host starts one canonical child,
which obtains DefaultFont, MenuFont and MessageBoxFont, assigns them to real
Labels, and owns a Bitmap displayed by a PictureBox. Source paint must occur
for the Form, all three Labels and the PictureBox before queued normal closure.
Only after Application.Run exits and the child disposes its UI/platform does
it publish the primitive result. The host rechecks its original Drawing identity.

The runner byte-compares both Drawing DLLs and the isolation launcher with their
selected packages, rejects canonical dependencies in the host and Microsoft
Drawing in the child, and retains logs/results on success or failure. Linux uses
the existing Xvfb/software-device setup; macOS and Windows use their existing
visible-runner environment. The child retains the existing visible-smoke budget
(60 seconds, or 120 on Windows); normal success never terminates a process.

`--build-only` permits isolated compilation/payload inspection without launching
either application. Its receipt explicitly has success=false, contracts=0 and
guiExecuted=false. It cannot qualify the window or replace the CI command above.
Visible lifecycle/source paint is not pixel, native input, host embedding or
clipboard qualification. Those independent desktop cases remain required.

Initial build-only validation used the exact successful PR74 source-first feed
(Build 36276743597, canonical artifact 10916489916) on macOS ARM64 with SDK
11.0.100-preview.5.26302.115. Both host and child compiled with zero warnings and
errors; package-byte and dependency-isolation checks passed. The retained receipt
correctly records no executed contract and no GUI qualification. No VM or native
window was started for this compile-only check. Actual three-platform execution
is required from the independent window step before claiming this gate passed.
