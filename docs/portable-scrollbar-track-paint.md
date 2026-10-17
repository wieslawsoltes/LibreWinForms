# Portable scrollbar track and gripper paint

The portable theme now paints SCROLLBAR track parts4–7 with their independently
observed opaque grey body and white leading edge: top for horizontal tracks,
left for vertical tracks. Normal, hot, pressed and disabled states retain the
same paint. The generic button palette previously changed the track to blue on
hover/press and omitted its orientation edge. Gripper parts8–9 contribute no
paint in these four states; a generic button fill/outline incorrectly replaced
the caller's existing pixels.

This uses the existing managed Graphics fills and caller transform/clip stack.
It does not change canonical scrollbar range, thumb/arrow geometry, native
Windows procedures or source input. Original Microsoft images are test fixtures,
not product textures. Undefined states and other theme parts retain their
independent policy; arrows, thumb shape and full theme parity remain required work.

The independent no-input [Microsoft reference run39052232473](https://github.com/wieslawsoltes/LibreWinForms/actions/runs/39052232473)
recorded the original120-request inventory under Windows11 build26200,
Aero NormalColor/NormalSize, at96DPI. The retained artifact12108405321 has SHA256
`f69281c14d126b4bb5f3cde085f54b8706a19ebe511eeb1ee49c031e0213f4f5`.
All32 complete horizontal/vertical track images and16 complete gripper images,
including both original backgrounds and every state, are retained byte-for-byte
with their original metadata and hashes. They fail against the prior renderer.
Four additional controls verify caller transform, intersected clips and no-op
gripper behavior over a distinct background. The earlier track isolation control
now distinguishes the native white edge from an invented thumb outline.

These original96DPI frames do not qualify other theme/DPI policies, actual
pointer gestures, held-button repeat or the complete package application.
The original grid pair and whole producer checks remain required by #174/#160.
