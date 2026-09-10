# Native MIL ProGPU source alignment

The LibreWPF native MIL integration requires canonical LibreWinForms and LibreWPF
to consume the same ProGPU source commit. This change pins
`9e05651abbe4e6a9ed8adc4445eab9c210b09dde`, the integration of ProGPU main #140
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

The retained depth-slot repair now covers both transient and cached attachments.
The prior bound provably triggers AddressSanitizer stack-buffer-overflow in the
first cached Viewport3D case; the fixed ten-case matrix passes ASan/UBSan and the
full local native suite passes 19/19. Windows ARM64 production compilation at
this pin completed with both providers; runtime and hosted qualification remain
pending at that checkpoint. No general Direct2D/Win2D API completion is implied.

The selected follow-up retains the same native repair, supplies a bounded
Windows cold-D3D12 test allowance, and removes exactly 21 visually reviewed W3C
threshold differences without changing tolerances. The full Windows ARM64 native
suite at `03acd40c` passes 20/20 in the Parallels VM; its graphics test takes
314 seconds. Local native tests pass 19/19. The final `fc5670fc` change is only
SVG baseline documentation/inventory, not production code. Fresh exact-head CI,
payload provenance and package/application qualification remain required.

`9e05651a` separates direct straight-alpha masked-image blending from the mixed
retained image pipeline, preserving all existing pixel limits and MIL behavior.
Both local native providers rebuild, all 19 native suites and 121 managed native
interop tests pass, and local Metal masked-image pixels are unchanged. Hosted W3C
and resvg quality pass at `fc5670fc`; the new head still requires fresh CI,
especially the Windows WARP masked-image differential that exposed this issue.
