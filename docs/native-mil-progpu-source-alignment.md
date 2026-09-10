# Native MIL ProGPU source alignment

The LibreWPF native MIL integration requires canonical LibreWinForms and LibreWPF
to consume the same ProGPU source commit. This change pins
`b88034192307d0f12f76af1461b54441c4041cf3`, the integration of ProGPU main #140
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
has pending hosted Windows GPU validation, SVG image review and remaining
exact-head package/platform qualification. LibreWinForms CI and the canonical
LibreWPF integration must run against this pin; preserve their normal gates.

The follow-up pin includes native sharp-rectangle/zero-extent stroke preparation
repairs, grouped/direct stroke agreement and Windows fixture corrections.
The latest local native run passes 19/19 suites. Managed renderer tests pass
4,576 with seven platform-specific skips; headless tests pass 280/280 and focused
cached-picture tests pass 39/39. These are ProGPU component results, not this
repository's own CI or application/package qualification.

The latest pin corrects an oriented-surface Viewport3D qualification fixture:
reversed winding now carries reversed normals before comparing exact visible
lighting. That entry passes locally on Metal. Failure-only cached-sibling
diagnostics preserve every pixel assertion. Hosted cached Viewport3D failures
on D3D12 and Vulkan, SVG review and exact-head release gates remain open.
