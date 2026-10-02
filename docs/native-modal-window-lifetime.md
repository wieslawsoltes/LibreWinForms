# Native modal sessions in the Forms source host

The canonical popup/dialog path uses the existing owned Cocoa factory, typed
point/line input and shared rendering. This change consumes an already active
ProGPU native modal session; it does not begin sessions automatically or select
new factories, wheel policy, renderer or platform defaults.

An ordinary `SilkLibreWindow` asks the shared session to poll before its ordinary
provider poll. A true result forbids the latter, including release-pending and
reentrant native dispatch. An owned Cocoa popup instead drains its own queue,
without invoking the global modal poll. The exact `IWindow` and successful factory
choice remain source-owned. Native session identity uses that provider's initialized
`Native.Cocoa`, never its opaque `Handle` or a retention-query guess.

Hide, explicit Close and disposal wait for native End before hiding the provider.
This also guards Close itself: the owned provider can hide before raising Closing,
so delaying only final disposal would be too late. The real provider still owns
Closing and cancellation. A newer Show supersedes pending Hide, not pending Close
or retirement; disposal supersedes pending Close. Reentrant Close cannot repeat
the active provider transition. Completion is coalesced and wakes only the creating
dispatcher; it performs no source callback, native hide/destruction or extra poll.
The resumed source/retirement boundary rechecks current intent and native leases.

The existing retirement queue retains the exact host and cleanup owner after it
leaves ordinary polling. Input/controller, renderer and native-view cleanup wait
for native dispatch, modal release and rendering/initialization scopes. Acquired
texture/view completion remains inside the original render boundary. Failed
cleanup is retained for creating-thread retry; successful cleanup is not repeated.
An uncertain native End without completion is different: its original failure
remains terminal and retains the host, rather than retrying a consumed token or
interpreting absent retention as proof of successful release. Ordinary event errors
remain primary through the dispatcher's existing cleanup-failure handling.

Twenty-one authored CPU-only cases exercise the actual lifetime implementation and
retirement queue with recorded provider/session operations, including the actual
host's source-wiring guard. They retain the original queue tests and two-minute
hosted selector deadline. No NSPanel, device or desktop is fabricated by these
controls. A shared active session still requires source modal-scope entry/release
and focus ordering before automatic admission; pointer-gating an owned popup is
not native blocking of its ordinary Cocoa owner. WPF's paired Close integration
is separate. Full exact CI and original cross-platform popup applications remain
required before claiming native interaction, rendering or modality parity.

An accepted owned-provider Close has already hidden its panel before Closing.
Retirement observes that actual visibility and avoids a second write to its now
closing provider; getter callbacks must still preserve the current source intent.
Deferred input cleanup cannot deliver stale source tails: the existing actual
`INativePointerTarget.IsCurrent`, `INativeCharacterTarget.IsAlive` and
`DeliverInputAfterCharacters` paths retain their `_disposed` rejection.

## Focused postcommit checks

After implementation `7be01ad6fa` and the completion/closing controls through
`d869f0bd321e8709e74184687a4be60bbced097a`, all 21 new cases passed with zero
failures in a 30-second-capped, device-free source run. It compiled the unchanged
production `NativeModalWindowLifetime.cs`, `NativeWindowRetirementQueue.cs` and
complete new fixture with real cached ProGPU Backend, Silk 2.23 and xUnit assemblies.
The only test driver enumerated the original Fact/InlineData cases; assertions and
production bodies were not rewritten. The first run lacked Silk.Maths in its
isolated directory; copying that existing managed dependency corrected the test
driver's load failure, without a product/test assertion change or download.

The compiler used SDK 10.0.301/net10.0.9 references, Release optimization and
warnings-as-errors (only cached framework-version unification warning CS1701 was
suppressed). Compilation had no source warnings/errors. This is not the normal
full backend/test project graph: the actual hosted xUnit v3 project remains the
authoritative integration gate. The reused Backend binary came from the read-only
603 cache; its native modal-session/lifetime API sources are byte-identical to the
qualified pinned48 sources. It is not new runtime provenance and loaded no native
library. The isolated driver is `/private/tmp/forms-modal-source-check.5PWwokEY`:

```sh
/usr/local/share/dotnet/dotnet /private/tmp/forms-modal-source-check.5PWwokEY/ModalSourceTests.dll
```

All three changed C# files also passed syntax parsing; shell syntax and whitespace
checks passed. An exact source comparison confirms the three disposed input-tail
guards are unchanged. The existing hosted selector retains all 21 queue cases and
adds these 21 cases, with the same two-minute deadline and skip rejection; every
other gate is unchanged. Independent counterpart review found and confirmed the
already-hidden Close correction. No local full-source/native/renderer build,
native window/device/desktop execution, runtime staging or dependency-pin change
was performed. Complete exact-head hosted CI and application evidence remain open.
