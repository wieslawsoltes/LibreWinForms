# Portable application pipeline startup

The source graph now pins ProGPU
`9e471863b351770ad59a23bdfe79579bb3c3a9f2`. It includes the original
operation-zero all-line raster specialization (ProGPU #218), opt-in pipeline
creation diagnostics (#217), and exact single-sample pipeline reuse (#219).
The managed and both native path providers retain their full curve/Boolean
paths; source shapes, sample quality, alpha, clips and device ownership do not
change. Single-sample cache selection checks actual binding-layout identity and
preserves format, blend, mask and alpha keys; four-sample rendering remains
distinct. Renderer/compiler/adapter defaults and application deadlines stay
unchanged.

This is a source submodule integration, not a download of a partially successful
upstream package artifact. The existing canonical source-first CI builds and
packages this exact graph and runs its original source, visible package,
isolation, analyzer and platform gates. Both the upstream ProGPU Build and the
whole LibreWinForms producer Build must succeed before final integration and
package acceptance; no gate is removed or weakened.

## Original grid evidence

The unchanged original sample at dotnet/samples
`acb39ceb13f910ae0f8f6298059c59102b749c41` previously failed the original
20-second startup observation with the older exact package. A private diagnostic
copy of Forms commit `621cbd92fe473d476d4bfdc3a4388d8c4bd1578a` with only the
new Backend/Vector/Scene DLLs reached its observer within that bound. Its
existing physical keyboard/pointer sequence passed in 17.454 seconds: ready,
edit, type, Enter commit, reopen, additional edit and Escape cancellation.
The 20-second startup, 60-second overall and 3-second transition limits, original
source and observer were unchanged. Package/SDK and copied DLL hashes were
checked before and after. This diagnostic copy is not a qualified package or
a cold-driver-cache/statistical startup benchmark.

The exact new pipeline-reuse regression bodies passed on Windows ARM64 (12
executed, zero skipped), including full pixel comparisons for matching targets,
four-sample separation, render ordering, repeated cache counts and disposal.
The preceding path specialization passed all six independent Windows source
cases. These focused checks do not establish complete application compatibility.

A fresh unchanged Microsoft original-grid run on the same SDK reproduces the
paragraph gap also seen in the portable renderer. Native DrawTextEx measurements
and the live native label both preserve the original repeated LF characters.
The earlier screenshot's apparent missing gap is not reproducible evidence of
a portable newline bug; source text and layout policy have not been rewritten.
Native header styling and other pixel/input/platform gates remain separate.

The original-grid issue remains open until the complete successful producer's
exact packages pass the original application checks. Native themes, masked
editors, Linux desktop behavior and cross-platform popup UX are not qualified
by this source pin or the diagnostic run.
