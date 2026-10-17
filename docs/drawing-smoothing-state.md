# Drawing smoothing state

The portable `Graphics.SmoothingMode` property previously started at `AntiAlias`,
stored speed/quality aliases verbatim and accepted invalid values or access after
disposal. The canonical source tests in
`src/System.Drawing.Common/tests/System/Drawing/GraphicsTests.cs` establish the
original alias and exception contracts. The portable property now starts at
`None`, maps `Default`/`HighSpeed` to `None` and `HighQuality` to `AntiAlias`, and
rejects invalid input without replacing the previous state. Existing saved
graphics and container contexts retain that normalized state. Undefined enum
values receive `InvalidEnumArgumentException`; the defined `Invalid` sentinel
receives `ArgumentException`. Disposed access retains the existing graphics
disposal guard.

This correction concerns state, not completed rendering parity. Line and path
recording still do not transport the smoothing setting into their retained
commands. Original grid captures show partially covered cell borders. The
independent `eng/WindowsThemeReference/LineRasterReference.cs` observation records
unaltered Microsoft GDI+ lines to distinguish sample position, endpoints,
antialiasing, transforms and clips before changing shared drawing behavior.

Microsoft's [pixel-offset contract](https://learn.microsoft.com/windows/win32/api/gdiplusenums/ne-gdiplusenums-pixeloffsetmode)
distinguishes integer sample centers for Default/None from half-integer centers
for Half. An unconditional geometry offset or simply marking every primitive
aliased does not establish this original contract. Keep text/image sampling,
pen cap/dash/width semantics and final original grid/package gates separate.

The focused state controls cover defaults, all five accepted values, invalid
sentinel/undefined values, disposal, saved states and nested containers. Before
the fix ten of twelve cases failed. Native line observation, full image
comparisons and application qualification remain independent required work.

Local validation on17 October passed all32 focused state/container controls and
the documentation verifier. The first full VSTest invocation passed869 of870;
the unchanged isolated font-metric gate charged8,048 bytes. Its original TRX/log
is retained. One bounded diagnostic using the existing complete-suite EventPipe
wrapper passed870/870, with759,293 parsed events,24,599 allocation samples and
zero lost events. Both required method records and the actual testhost lifetime
were verified. The diagnostic uses the repository's SDK10 launcher rather than
the first invocation's SDK11 VSTest launcher; its success neither attributes the
earlier charge nor qualifies an allocation fix. No warmup, zero-byte assertion,
GC/JIT setting or source test was changed. Exact-head hosted CI remains required.
