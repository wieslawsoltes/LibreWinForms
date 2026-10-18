# Linux source application runtime

Linux Xlib windows require the ProGPU runtime that selects the actual source
display before creating GL. Package applications obtain this through
`ProGPU.Backend` and its normal runtime assets. Source applications import the
same producer target at their executable/test boundary through
`eng/LibreWinForms.XlibRuntime.targets`; ordinary libraries and Package mode
retain their independent asset selection.

Canonical Project mode selects the staged payload under the actual
`LibreWinFormsProGpuSourceRoot`. `ProGpuXlibSourceRuntimeRoot` can identify an
explicit staged directory. The backend's own source tests opt in with
`LibreWinFormsUseSourceXlibRuntime`; this does not change their framework mode.
Neither path builds, downloads or patches a missing runtime.
The runtime helper normalizes a private source-root copy before discovery and
import. An explicit global root without a trailing separator must select the
same payload bytes as its separated form, without changing the caller's value.
The original no-separator case silently selected no runtime assets; both forms
now participate in the metadata controls below.
The repository's source-project validation also joins this path explicitly;
it must not reject an existing producer solely because the global root lacks
a final separator. The repository controls execute that original validation
target as well as checking the resulting runtime items.

The source-first and canonical packaging jobs stage both Linux architectures
from the same whole successful ProGPU Build before source validation or packing.
The immutable LibreWPF staging helper at
`f9fc6aa1578a97cd6cf7ba8393c6cbd0cdba1b33` checks the package source identity,
receipts, ELF architecture, original bytes and complete license inventory.
Its complete [Build 39088830985](https://github.com/wieslawsoltes/LibreWPF/actions/runs/39088830985)
passed all 16 jobs and its documentation check passed. Release packaging must
select a canonical LibreWPF revision containing this four-argument helper;
older three-argument revisions cannot stage the Linux runtime.
Release packaging retains that same dependency closure. Runtime/native/SDK/DXC
directories remain separate.

The source runtime integration pins ProGPU
`a37f235057ee58b26b3322864c62dfeb1d041715`. Its complete
[Build 39093685811](https://github.com/wieslawsoltes/ProGPU/actions/runs/39093685811)
passed all 61 jobs and all 70 checks on that head passed before this pin update.
Both original Linux runtime archives match the producer artifact digests;
40 Forms source-asset evaluations pass against those exact files. The 24 native
staging controls also pass. These checks establish dependency and asset selection;
the complete Forms build and original application acceptance remain required.

The asset control evaluates both real repository and SDK imports with original
producer files. It covers portable and RID-specific applications, test hosts,
library/Package opt-outs and duplicate imports. These metadata/file checks do
not execute Linux code or qualify desktop rendering. Complete producer builds,
original application checks and public-package validation remain required.
