# Separate host and portable application

The Host intentionally references Microsoft `System.Drawing.Common` and the
BCL-only launcher. PortableApp uses the ordinary canonical `LibreWinForms.Sdk`
package closure and the same BCL-only launcher. Their output directories must
remain separate; do not combine dependency files or exchange drawing/UI objects.

This sample currently consumes the repository's `0.1.0-source-first` packages,
not a newly published NuGet release. First run the normal
`eng/librewinforms-pack-source-first.sh` gate from the repository root, or use its
successful `canonical-source-first-package` artifact as a local NuGet source.
Add that source to a NuGet.config beside this README, retaining nuget.org and
the repository's preview-runtime package sources. The centralized `global.json`
selects the same source-first SDK version. Both apps currently target net11.0,
as required by that SDK; the BCL-only launcher itself targets net10.0.

```sh
dotnet build Host/Host.csproj -c Release
dotnet build PortableApp/PortableApp.csproj -c Release
dotnet Host/bin/Release/net11.0/Host.dll \
  /absolute/path/PortableApp/bin/Release/net11.0/PortableApp.dll \
  /absolute/path/to/dotnet "Initial child text"
```

The child opens its own top-level window. Edit its text and choose Return text,
or close the window to return `Accepted=false`. The host receives only that
decision and text, and prints the retained result directory. Normal execution
waits for the user; no timeout is imposed on the GUI. The automated package gate
compiles these same two apps but runs a separate bounded control/ownership
fixture without claiming visible desktop qualification.
