# Native compute trace integration

The source graph advances ProGPU from the successful drawing-clip baseline
`3dc02026028c4ba42ddeef9f2a4232f660c51a9e` to
`b3f221d314133075382747372ad63349abdcd0a0` (ProGPU #221).
This retains every popup, text-margin and clip fix in the preceding Forms stack.
The additional native change is bounded, opt-in dispatch/submission attribution;
it does not change managed rendering, shader bytes, resource ownership,
compiler/adapter defaults or application deadlines.

LibreWPF's canonical integration requires its ProGPU pin to match this one. The
aligned source graph allows its separate failed-Showcase replay to request
`PROGPU_NATIVE_TRACE_COMPUTE=1`. Ordinary application and acceptance environments
do not enable it. Encoded work and queue submissions are not GPU completion.

This commit selects source; it does not stage or qualify new packages. Exact
whole-successful producer Builds and complete package/application gates remain
required. The prior ProGPU Build 36436941579 passed all 49 jobs and the Windows
popup diagnostic showed the restored More label and complete tooltip, but those
results do not qualify the newly combined graph or all platforms. See
[drawing clip evidence](portable-drawing-clip-parity.md).
