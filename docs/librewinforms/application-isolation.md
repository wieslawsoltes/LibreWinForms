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

Implementation and package-consumer regressions are being added in this change;
no runtime qualification is claimed until exact-head CI passes.
