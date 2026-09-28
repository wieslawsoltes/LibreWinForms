# Portable drawing clip integration

The source graph pins ProGPU `3dc02026028c4ba42ddeef9f2a4232f660c51a9e`,
retaining the earlier pipeline-startup changes and adding the captured drawing
clip coordinate-frame and exact atlas/device pixel-mapping fixes from ProGPU
#220. Native and managed paths retain the existing source shapes, clip frames,
curve/Boolean semantics, alpha and fractional-mapping behavior. Renderer,
compiler, adapter defaults and application deadlines are unchanged.

The whole upstream [Build 36436941579](https://github.com/wieslawsoltes/ProGPU/actions/runs/36436941579)
completed successfully: all 49 jobs, including both Windows drawing references
and the independent native package consumer groups. Artifact
`progpu-packages-linux-x64` (10979158407) is version `0.1.0-preview.3240.ci`;
its downloaded archive SHA-256 is
`633a24ca80693b49c306b675b1aef43f84faaa811a4588c1bf0dd09833f0bd90`.
The separately produced native package archive (10979671898) has SHA-256
`1642bb6b641477d7b9fa2ef74ebd7944ef5cee531c8fa18b698a3081aa952829`.
The package sets are kept separate; same-ID/version packages are not overwritten
or silently mixed between artifacts.

The existing LibreWinForms producer builds the pinned source graph and retains
all source, package, visible-application, SDK/analyzer, isolation and platform
gates. Its whole successful Build and exact package/application checks are still
required before qualification. An upstream successful Build permits staging;
it does not qualify a downstream source pin or all popup appearance.

The preceding Windows popup candidate passes all fourteen input phases and
shows full tooltip text after the Forms tooltip-margin fix. Its old renderer
still omits the More menu label. That old screenshot is not evidence about the
new clipping payload: compare the original app again with verified new packages.
ProGPU's three separate recursive SVG exception mismatches and WPF ARM64
pending-compute/resize failure remain independent release gates.
