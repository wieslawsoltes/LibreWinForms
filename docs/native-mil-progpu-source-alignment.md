# Native MIL ProGPU source alignment

The LibreWPF native MIL integration requires canonical LibreWinForms and LibreWPF
to consume the same ProGPU source commit. This change pins
`978a62eb06ad5435e128d540f3e25d95a7c2cf95`, the integration of ProGPU main #140
with native MIL and its post-merge fixes. It does not add a WinForms-local graphics
implementation or waive the exact source-graph gate.

The dependency remains under review in
[ProGPU #139](https://github.com/wieslawsoltes/ProGPU/pull/139), with consumer work in
[LibreWPF #115](https://github.com/wieslawsoltes/LibreWPF/pull/115).
Merge this alignment only after the ProGPU dependency and required CI are ready.
The base is `12b4a1be0`; its tree equals the previously selected `5aa13b540`.
Only this documentation and the ProGPU gitlink change.

ProGPU's System.Drawing tests pass 621/621 locally. That is upstream component
evidence, not a rebuilt or runtime-qualified LibreWinForms package. ProGPU still
has native/managed rendering test failures, pending SVG image review and remaining
exact-head package/platform qualification. LibreWinForms CI and the canonical
LibreWPF integration must run against this pin; preserve their normal gates.

The follow-up pin includes native sharp-rectangle/zero-extent stroke preparation
repairs and Windows fixture compilation correction. Native local qualification
is 18/19 suites and the focused managed stroke run passes 24/24; neither result
replaces this repository's own CI or application/package qualification.
