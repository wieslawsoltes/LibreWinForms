# MessageBox source and native modal lifetime

Portable `MessageBox.Show` now recognizes the optional
`ILibreModalMessageBoxService` handshake. The registered managed service uses the
same `PortableModalCompletion` and LIFO source `ThreadWindows` frames as
`Form.ShowDialog`, without creating a substitute Form. Existing explicit owner,
active-Form default owner and ownerless desktop/service-option selection remain
unchanged. Visible enabled source Forms are disabled by the existing source modal
loop; this is not a claim of system-wide native blocking.

The service registers cleanup before creating/showing its real typed window. Once
that exact window is shown and activated, `Begin` passes its optional
`ILibreModalWindow` to the source frame before entering the nested dispatcher.
Missing capability or explicitly disabled native policy keeps source-only
modality. Requested but unsupported native modality retains the provider's
original failure. Neither the service nor the handshake selects Cocoa factories,
changes startup defaults, casts handles, or adds native event polling.

## Completion and ownership

A key/button result, accepted chrome close, dispatcher-loop exit or failing Show
makes that exact Session terminal before returning/rethrowing. Further input
cannot alter the result or close a replacement generation. On the scoped path,
the service does not call native Close while native release is outstanding. An
accepted chrome close returns false to the immediate backend close callback after
publishing its result, preventing premature backend disposal; the nested loop
still terminates. Yes/No retains its existing rejected-close behavior.

The source caller requests release once, including on failure. Its exact native
completion marks only that original source frame ready. An older completion
cannot pop a newer frame. Existing source restoration detaches the frame before
re-enabling/activating original source handles, then runs that frame's cleanup.
Recreated handles do not inherit the old restoration. Cleanup callback reentry
can create another generation, but cannot cause the old Session to dispose it.
An uncertain native End is not retried or interpreted as successful release.

The managed service holds each scoped Session independently of completion
callbacks. If `ILibreWindow.Dispose` throws, the Session keeps the exact window;
all original pending entries are attempted once and the first cleanup exception
is retained. A coalesced creating-thread post retries pending cleanup once.
Persistent failure remains owned for a later service drain, not an infinite
post loop. A source Show/Begin exception remains primary if synchronous cleanup
also fails. Failed posting does not discard ownership.

Successful Session cleanup is **not native retirement proof**. The ProGPU source
backend already transfers its exact native window, renderer and render-view
leases to its provider-aware retirement queue before returning from logical
disposal. That queue remains responsible for deferred/failed actual retirement,
including the original creating-thread and render-dispatch guards. This change
does not replace that owner with a callback or infer completion from returned
Dispose.

## Compatibility and authored controls

Direct standalone `ManagedLibreMessageBoxService.Show(request)` keeps its prior
immediate managed close/dispose contract. Custom services implementing only
`ILibreMessageBoxService` retain the old source Begin/End wrapper. Only the typed
scoped overload participates in delayed native/source completion. Help rejection,
request validation, overrides and button result mappings remain unchanged.

`ManagedLibreMessageBoxModalLifecycleTests` authors service/window-double controls
for registration and exact-window Begin ordering, absent/disabled capability,
terminal input, chrome cancellation, deferred cleanup, original failures and
coalesced retry ownership. These do not prove native or source-frame completion.

`CanonicalMessageBoxModalCompletionTests` authors eight configurations through
public `MessageBox.Show`, the real managed service and the existing headless
source window contracts: explicit/active owner input blocking, disabled native
policy, nested out-of-order completion, replacement creation from a release
callback, primary/cleanup error ordering and null desktop-owner policy. A
test-only forwarding slot and native policy are restored in a disposal/finally
scope. Layout is a test double; no text, pixel or desktop parity is claimed.

All new controls are authored and unrun. No build, test, syntax/source verifier,
probe, GPU/UI/VM execution, CI dispatch or dependency acquisition was performed.
The qualified ProGPU gitlink and native-modal defaults remain unchanged. Final
package and real cross-platform MessageBox/dialog/popup owner input, focus,
release and retirement qualification is still required alongside the WPF source
counterpart; this is not automatic or fully qualified platform modality.
