# Native MIL ProGPU source alignment

## Native Window frame-inset dependency

The decorated portable Window sizing fix in
[LibreWPF #141](https://github.com/wieslawsoltes/LibreWPF/pull/141) requires the
typed ProGPU frame-inset contract from
[merged ProGPU #166](https://github.com/wieslawsoltes/ProGPU/pull/166). This
branch now pins the ProGPU `main` merge commit
`3755428f3ad9c269a5e4a9bc91e0a4175f68f695`; its PR head
`11503bde8ae27a0a2dda4d2a30be047c066dacaa` passed 45/45 checks before
merge. LibreWPF will repin to the same main commit after this alignment merges;
the canonical WinFormsIntegration source gate rejects divergent pins before
compilation. The contract does not add a WinForms-local frame implementation
or qualify Windows application layout. Merge this alignment only after its
new exact-head CI passes, then qualify LibreWPF #141 on that merged source graph.

## Historical alignment checkpoints

The LibreWPF native MIL integration requires canonical LibreWinForms and LibreWPF
to consume the same ProGPU source commit. This branch pins ProGPU `main` merge
commit `eed951cdd7af463d840d0e0b85088bcdb8c4cf24` from
[ProGPU #164](https://github.com/wieslawsoltes/ProGPU/pull/164),
matching the pending [LibreWPF #137](https://github.com/wieslawsoltes/LibreWPF/pull/137)
consumer. The pin supplies typed first-show placement and native-position
callbacks. Its exact PR head `6e62731033dbc7065de51e5979f35dedfc415ecc`
passed all 45 checks before merge; consumer exact-head CI remains required.

## Earlier alignment checkpoints

The preceding alignment pinned ProGPU
`main` merge commit `5b99b640a583c9f1cb69fd17731e000ab632baec` for
[ProGPU #162](https://github.com/wieslawsoltes/ProGPU/pull/162). Its exact PR
head `58351f4c5c1077fb41bf3656a9058db0da24bb8a` passed 45/45 checks,
including all native renderer platforms and package consumers, before the
merge. The C++ MIL fix makes Retina glyph ink match WPF layout advances;
the preceding ProGPU #161 path-atlas repair remains in this same main tree.
The merged-commit ProGPU package and LibreWPF application gates remain
separate requirements. This pin adds no WinForms-local graphics
implementation and does not waive this repository's CI or canonical LibreWPF
source-graph gate.

The dependency is merged; consumer work remains in
[LibreWPF #115](https://github.com/wieslawsoltes/LibreWPF/pull/115).
Merge this alignment only after the new LibreWinForms pin's required CI is green.

The preceding alignment pinned ProGPU `main` merge commit
`62b67e6cf34addff2e2bdc7ef959a3c50694939a` for
[ProGPU #161](https://github.com/wieslawsoltes/ProGPU/pull/161). Its exact PR
head `5f945f8e1dea81077e08aa0481896f88ab1f1730` passed 45/45 checks,
including the Windows ARM64 native renderer and all native package consumers,
before the merge. The fix admits the packaged LibreWPF paid Xceed DataGrid's
retained path set and a guideline-snapped zero-area fill; its local macOS
native live gate passed with an isolated C++ runtime overlay. That evidence
is historical for the new pin.

The preceding alignment pinned ProGPU `main` merge commit
`86f2f766d1f8e6b4041fa184de0fe9d03ae2840f` for
[ProGPU #139](https://github.com/wieslawsoltes/ProGPU/pull/139). Its tree is
identical to tested PR head `54adc6a005119d40fc25615b3823844c21453690`;
exact-head Build [34819727963](https://github.com/wieslawsoltes/ProGPU/actions/runs/34819727963)
completed 54/54 checks, including platform package consumers. Exact NuGet
`0.1.0-preview.3051.ci` also passed local macOS Metal and Windows VM x64
system-WARP/default-adapter consumers without source assembly overlays. That
evidence remains historical for the new pin.

The earlier alignment pinned
`586e52c7721f0957916159530a52e6d7e4ec1a3d`, the integration of ProGPU
main #140 with native MIL and its post-merge fixes. The following evidence is
historical and does not qualify the current pin.

The latest pin adds retained excluded-paragraph snapshots, shared C/managed
fragment caret navigation and the explicit neutral exclusion formatting
capability needed by the WPF adapter. Local native tests and consumer checks
pass; source Figure/Floater child placement remains unconnected. Fresh CI and
final package/application qualification remain required. Browser CI at the
preceding navigation head reports an evidence-readback timeout under its
unchanged 120-second deadline; that investigation is not resolved by this pin.

The preceding pin includes native excluded-paragraph and fragment interaction
transport, sorted native export manifests and native browser diagnostics. It
also includes the SVG checksum correction supported by an isolated old-shader
failure, paired managed/native curve tests and full ten-frame review. The
existing numeric performance limits, X64 requirement and quality gates remain
unchanged. Producer CI is still pending at alignment time; previous green
LibreWinForms checks do not qualify this new pin. Source Figure/Floater ownership,
application closure and final package/platform validation remain open.

The preceding pin includes main #160, the MSVC inline-fixture compile fix, native
measured text interaction, owned inline snapshots and the explicit neutral
inline-text provider capability. Focused neutral contract tests pass 2/2.
WPF source object/anchor admission and exact-head package qualification remain
open at that checkpoint, as did ProGPU's SVG performance checksum mismatch.
No gate is relaxed by dependency alignment.

The preceding pin adds native measured inline paragraphs and corrects the native
C++ SDK packaging graph: MIL's Direct2D core dependency is now staged for all
desktop RIDs and linked transitively by the exported CMake target. The preceding
38b6a7a4 exact-head package build failed on that missing static dependency; it
must not be used as qualified package evidence. Local native/managed consumers
pass with the fix, but fresh hosted CI and downstream application qualification
remain required. This pin does not admit WPF inline controls or anchored content.

The preceding pin adds shared native document row/cell placement and fixed column
tracks for the LibreWPF table dependency. Both native providers build, CTest
passes 20/20, managed contract tests pass 9/9, and the default consumer passes
locally with both providers. WPF source table interaction, automatic widths,
row spans and final exact-head package/platform qualification remain open.

The preceding pin corrects the native NuGet consumer's owner assertions: zero-list
queries return an owner in summary, list queries in ordered records. The complete
consumer passes locally against the current native libraries; exact-head all-RID
package CI remains required. No shader or package gate was changed.

The preceding pin adds native measured-block placement for the actual LibreWPF
BlockUIContainer dependency, keeping non-text metrics separate from paragraph
lines. Both native providers build; local native tests pass 20/20, managed
document-contract tests pass 8/8, and generated contracts verify. Source child
visual/editing integration and final package/platform qualification remain open.
No WinForms source behavior changes or package gates are bypassed by this pin.

The preceding pin adds native source-cluster word-space justification required by the
LibreWPF editor application. ProGPU local native tests pass 20/20, and the WPF
native host passes styled/RTL caret, selection and hit geometry coverage. Full
application qualification still stops at rich-editor decoration scopes; script-
specific justification and final exact-head package/platform/CI gates remain open.

The preceding change corrects only Dawn provider fixture assertions and documentation:
list-query owners belong to returned records, while a zero-list query returns its
owner in summary. The pinned provider reproduces the old assertion failure and
passes its complete native executable after the correction. All other ProGPU CI
checks passed at 9e05651a, and this repository's seven checks passed at c67b04a8.
Fresh exact-head CI and downstream application/package gates remain required.
The following earlier checkpoints retain their historical qualification scope.
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
