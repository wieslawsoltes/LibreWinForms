# Portable event graphics

A public preview.69 Forms application aborted on macOS while repainting a
standard button held with Space. `ButtonStandardAdapter.PaintWorker` passed its
`PaintEventArgs` to `ControlPaint.DrawBorderSimple`, which could not obtain the
managed Graphics and threw `PlatformNotSupportedException`. No file dialog had
opened. The original application exited after 28.948 seconds within its original
180-second limit; that failure remains part of the acceptance record.

Both `PaintEventArgs` and `DrawItemEventArgs` already retain the correct Graphics
in `DrawingEventArgs`. Their Win32 `IGraphicsHdcProvider` cannot return the
portable Graphics type. A separate internal `IManagedGraphicsProvider` now
exposes that exact frame to `DeviceContextExtensions.TryGetGraphics`. Derived
paint-event classes inherit the same behavior.

The provider delegates the original creation request to `DrawingEventArgs`.
It does not create a replacement recorder, request an HDC, change transform or
clip state, or transfer ownership. Event disposal keeps caller-owned Graphics
alive. The ordinary Win32 implementation and its interface remain unchanged.

`CanonicalPaintGraphicsTests` exercises a standard button while Space is held
and compares the complete wrapped visual-style output with direct Graphics
output. Paint, owner-draw and derived table-layout events each retain identity
and translated frames, existing clips, and usable caller-owned Graphics after
event disposal. Before the repair, three of these seven cases failed: the
pressed button and both owner-draw cases. The four paint-wrapper controls passed.
The regression source is unchanged between the original and repaired runs.

The first provider-only repair removed all three exceptions but exposed the
pressed-border pixel failure: expected `#ACA899`, observed `#ADA69A`. Opaque solid
positive-size borders now reuse the existing portable DrawEdge integer bands,
matching the native branch's GDI policy. Transparent and dashed borders keep
their existing pen path. Additional complete-frame controls retain one-pixel
dimensions, translation, clipping and caller state. The earlier independent
Microsoft DrawEdge pixel controls remain unchanged.

These source rendering controls do not qualify physical input, native theme
parity or file-panel behavior. Whole producer CI and the original application
actions remain independent requirements in the integration tracking issue #160.
