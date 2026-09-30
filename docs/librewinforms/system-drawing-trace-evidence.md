# Source Drawing failure evidence

The canonical source CI job explicitly opts into the pinned ProGPU Drawing
wrapper with `LIBREWINFORMS_DRAWING_TRACE=1`. Only `CONFIGURATION=Release`
(the existing default) uses that wrapper. Non-Release invocations keep the
original configured `dotnet test` command and print that tracing is disabled
when the opt-in was requested. Ordinary local invocations remain unchanged.

The wrapper runs the full unfiltered Drawing project once, not a selected
Metafile retry. At the failed checkpoint this was 813 tests. API-debt validation
still runs first. There is no new warmup, filter, JIT/GC switch, parallelism
policy, allocation allowance or timeout; the source job retains its 45-minute
outer deadline. The enumeration test still performs 16 warmup and 16 measured
walks, checks all 65,568 callbacks and enforces its original 4,096-byte ceiling.

Engine revision `2825220505f715aaed4299218cf2e8f8acc64fdc` adds the required
wrapper/verifier option:

```text
--require-method System.Drawing.Common.Tests.MetafileParserTests
                 WarmedEnumerationDoesNotAllocatePerRecordPayloads
```

Both exact CLR metadata fields must occur in the parsed runtime event stream.
The original font method remains mandatory. Complete parsing, event-loss
rejection, allocation-sample presence and the actual single-testhost PID/lifetime
handshake remain required. The receipt records each required method and its
observed status, including failures. Metadata presence is not proof of execution
inside the measured interval or of a sampled allocation there; a missing stack
is not evidence of zero allocations.

Every invocation creates an owned `artifacts/system-drawing-quality/run-*`
directory in this repository. CI always uploads that exact root as
`source-system-drawing-quality`, retaining status, test log/TRX, runtime/tool
identity, settings, parse receipts and any admitted failure traces for seven
days. Existing engine bounds remain 96 MiB collection stop, 128 MiB per trace,
256 MiB total and 30 seconds for collector finalization. Successful verified
traces are removed by the engine wrapper; their hashes and receipts remain.
Diagnostic failure never replaces the original nonzero test result.

Forms Build `36694588514`, job `109819392104`, failed at source head
`4a8ee20fc7798ab1fea30208652fdfa3b2727759` with 7,264 measured bytes and
812 passing Drawing tests. Its ProGPU pin was
`bb66c0f83622a68fd41a8f81f2a78134139a4f59`; no allocation trace was captured.
The cause remains unknown. Earlier success on that same engine pin and
`dotnet/runtime#134724` do not establish the cause of this failure. This change
collects evidence on the next normal CI run; it is not a performance fix or a
rerun of the old producer.

This integration requires the additive engine verifier revision before it can
be enabled in CI; the dependency pin must be qualified and updated separately.
Only static syntax and contract inspection were performed locally. The authored
controls, full suite and real trace admission await CI.
