#!/usr/bin/env python3
"""Executable selected-asset and bootstrap contracts; never edits a package cache."""

import argparse
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape


PARSER = argparse.ArgumentParser(description=__doc__)
PARSER.add_argument("--dotnet", default="dotnet")
PARSER.add_argument("--framework", default="net10.0")
PARSER.add_argument("--progpu-drawing", type=Path, required=True)
PARSER.add_argument("--microsoft-drawing", type=Path, required=True)
PARSER.add_argument("--canonical-directory", type=Path)
PARSER.add_argument("--targets-directory", type=Path)
ARGS = PARSER.parse_args()
REPO = Path(__file__).resolve().parents[1]
TARGETS = (ARGS.targets_directory or REPO / "src/LibreWinForms.Sdk/targets").resolve()
GOOD = ARGS.progpu_drawing.resolve(strict=True)
FOREIGN = ARGS.microsoft_drawing.resolve(strict=True)
DOTNET = shutil.which(ARGS.dotnet) or str(Path(ARGS.dotnet).resolve(strict=True))


class DrawingIdentityTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="librewinforms-drawing-contract-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()

    def run_command(self, *arguments, expected=0, timeout=90):
        environment = dict(os.environ, NUGET_PACKAGES=str(self.root / "packages"),
                           DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1",
                           DOTNET_ROLL_FORWARD="Major", DOTNET_ROLL_FORWARD_TO_PRERELEASE="1")
        result = subprocess.run([DOTNET, *map(str, arguments)], cwd=self.root,
                                env=environment, text=True, stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT, timeout=timeout, check=False)
        if expected == 0:
            self.assertEqual(result.returncode, 0, result.stdout)
        else:
            self.assertNotEqual(result.returncode, 0, result.stdout)
            self.assertIn(expected, result.stdout)
        return result.stdout

    def fixture(self, body):
        path = self.root / "Contract.proj"
        path.write_text(f'''<Project>
  <PropertyGroup><OutDir>{escape(str(self.root / 'out'))}/</OutDir>
    <PublishDir>{escape(str(self.root / 'publish'))}/</PublishDir></PropertyGroup>
  <Import Project="{escape(str(TARGETS / 'LibreWinForms.DrawingIdentity.targets'))}" />
  {body}
</Project>''', encoding="utf-8")
        return path.name

    def test_selected_compiler_and_copy_files(self):
        for phase, item, target, dependencies in [
                ("compiler", "ReferencePathWithRefAssemblies", "CoreCompile",
                 '<Target Name="FindReferenceAssembliesForReferences" />'),
                ("copy", "ReferenceCopyLocalPaths", "_CopyFilesMarkedCopyLocal", "")]:
            for kind, file, error in [("good", GOOD, 0), ("foreign", FOREIGN, "LWFDRAW001"),
                                      ("missing", self.root / "System.Drawing.Common.dll", "LWFDRAW002")]:
                with self.subTest(phase=phase, kind=kind):
                    project = self.fixture(f'''<ItemGroup><{item} Include="{escape(str(file))}" /></ItemGroup>
                      {dependencies}<Target Name="{target}"><Message Importance="high" Text="selected-phase-ran" /></Target>''')
                    output = self.run_command("msbuild", project, "-nologo", f"-t:{target}", expected=error)
                    if error:
                        self.assertNotIn("selected-phase-ran", output)
                        self.assertIn(str(file), output)
                    else:
                        self.assertIn("selected-phase-ran", output)

    def test_empty_library_and_excluded_package_are_not_selected_assets(self):
        project = self.fixture('''<ItemGroup><PackageReference Include="System.Drawing.Common" Version="10.0.12" ExcludeAssets="all" /></ItemGroup>
          <PropertyGroup><OutputType>Library</OutputType><CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies></PropertyGroup>
          <Target Name="FindReferenceAssembliesForReferences" />
          <Target Name="CoreCompile" /><Target Name="_CopyFilesMarkedCopyLocal" />
          <Target Name="CopyFilesToOutputDirectory" /><Target Name="ComputeFilesToPublish" />
          <Target Name="CopyFilesToPublishDirectory" />
          <Target Name="Contract" DependsOnTargets="CoreCompile;_CopyFilesMarkedCopyLocal;CopyFilesToOutputDirectory;ComputeFilesToPublish;CopyFilesToPublishDirectory" />''')
        self.run_command("msbuild", project, "-nologo", "-t:Contract")

    def test_actual_output_and_missing_selected_destination(self):
        for kind, payload, error in [("good", GOOD, 0), ("foreign", FOREIGN, "LWFDRAW001"),
                                     ("missing", None, "LWFDRAW002")]:
            with self.subTest(kind=kind):
                out = self.root / "out/System.Drawing.Common.dll"
                out.parent.mkdir(exist_ok=True)
                if out.exists():
                    out.unlink()
                if payload:
                    shutil.copyfile(payload, out)
                project = self.fixture(f'''<ItemGroup><ReferenceCopyLocalPaths Include="{escape(str(GOOD))}" /></ItemGroup>
                  <Target Name="CopyFilesToOutputDirectory" />''')
                self.run_command("msbuild", project, "-nologo", "-t:CopyFilesToOutputDirectory", expected=error)

    def test_stale_output_rejected_without_copy_local_item(self):
        out = self.root / "out/System.Drawing.Common.dll"
        out.parent.mkdir()
        shutil.copyfile(FOREIGN, out)
        project = self.fixture('<Target Name="CopyFilesToOutputDirectory" />')
        self.run_command("msbuild", project, "-nologo", "-t:CopyFilesToOutputDirectory", expected="LWFDRAW001")

    def test_publish_checks_actual_bundle_inputs_before_bundle(self):
        for single in (False, True):
            for kind, file, error in [("good", GOOD, 0), ("foreign", FOREIGN, "LWFDRAW001")]:
                with self.subTest(single=single, kind=kind):
                    project = self.fixture(f'''<PropertyGroup><PublishSingleFile>{str(single).lower()}</PublishSingleFile></PropertyGroup>
                      <Target Name="SelectPublish"><ItemGroup><ResolvedFileToPublish Include="{escape(str(file))}">
                        <RelativePath>System.Drawing.Common.dll</RelativePath><CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
                      </ResolvedFileToPublish></ItemGroup></Target>
                      <Target Name="PrepareForBundle" DependsOnTargets="SelectPublish"><ItemGroup>
                        <FilesToBundle Include="@(ResolvedFileToPublish)" /><ResolvedFileToPublish Remove="@(ResolvedFileToPublish)" />
                      </ItemGroup></Target>
                      <Target Name="GenerateSingleFileBundle" Condition="'$(PublishSingleFile)' == 'true'" DependsOnTargets="PrepareForBundle">
                        <Message Importance="high" Text="bundle-was-written" />
                      </Target>
                      <Target Name="ComputeFilesToPublish" DependsOnTargets="SelectPublish;GenerateSingleFileBundle" />
                      <Target Name="CopyFilesToPublishDirectory" DependsOnTargets="ComputeFilesToPublish">
                        <Copy SourceFiles="@(ResolvedFileToPublish)" DestinationFiles="@(ResolvedFileToPublish->'$(PublishDir)%(RelativePath)')" />
                      </Target>''')
                    output = self.run_command("msbuild", project, "-nologo", "-t:CopyFilesToPublishDirectory", expected=error)
                    if single and error:
                        self.assertNotIn("bundle-was-written", output)
                    if single and not error:
                        self.assertIn("bundle-was-written", output)

    def test_publish_destination_overwrite_is_rejected(self):
        project = self.fixture(f'''<Target Name="ComputeFilesToPublish"><ItemGroup>
          <ResolvedFileToPublish Include="{escape(str(GOOD))}"><RelativePath>System.Drawing.Common.dll</RelativePath>
          <CopyToPublishDirectory>Always</CopyToPublishDirectory></ResolvedFileToPublish>
          </ItemGroup></Target>
          <Target Name="CopyFilesToPublishDirectory" DependsOnTargets="ComputeFilesToPublish">
            <Copy SourceFiles="{escape(str(FOREIGN))}" DestinationFiles="$(PublishDir)System.Drawing.Common.dll" />
          </Target>''')
        self.run_command("msbuild", project, "-nologo", "-t:CopyFilesToPublishDirectory", expected="LWFDRAW001")

    @unittest.skipUnless(ARGS.canonical_directory, "requires a freshly compiled canonical consumer closure")
    def test_generated_bootstrap_fresh_process_load_orders(self):
        canonical = ARGS.canonical_directory.resolve(strict=True)
        for required in ("System.Windows.Forms.dll", "LibreWinForms.ProGPU.dll", "System.Drawing.Common.dll"):
            self.assertTrue((canonical / required).is_file(), required)
        host = self.root / "Host"
        payload = self.root / "Payload"
        host.mkdir()
        payload.mkdir()
        (host / "Host.csproj").write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
          <TargetFramework>{ARGS.framework}</TargetFramework><OutputType>Exe</OutputType>
          <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>''')
        (host / "Program.cs").write_text('''using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
Console.WriteLine("runtime=" + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
string canonical = args[0];
AssemblyLoadContext.Default.Resolving += (_, name) => {
    string path = Path.Combine(canonical, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
string first = args[1] == "microsoft" ? args[2] : Path.Combine(canonical, "System.Drawing.Common.dll");
Console.WriteLine("first-identity=" + AssemblyName.GetAssemblyName(first));
AssemblyLoadContext.Default.LoadFromAssemblyPath(first);
try {
    Assembly payload = AssemblyLoadContext.Default.LoadFromAssemblyPath(args[3]);
    RuntimeHelpers.RunModuleConstructor(payload.ManifestModule.ModuleHandle);
    payload.GetType("Smoke", true)!.GetMethod("Run")!.Invoke(null, null);
    return 0;
} catch (Exception error) { Console.WriteLine(error); return 23; }
''')
        references = "".join(f'<Reference Include="{name}"><HintPath>{escape(str(canonical / (name + ".dll")))}</HintPath><Private>false</Private></Reference>'
                             for name in ("System.Drawing.Common", "System.Windows.Forms", "System.Windows.Forms.Primitives",
                                          "System.Private.Windows.Core", "LibreWinForms.Platform", "LibreWinForms.ProGPU"))
        (payload / "Payload.csproj").write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
          <TargetFramework>{ARGS.framework}</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
          <EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>CA2255</NoWarn>
          <LibreWinFormsGenerateApplicationBootstrap>true</LibreWinFormsGenerateApplicationBootstrap>
          </PropertyGroup><ItemGroup>{references}<Compile Include="Smoke.cs" /></ItemGroup>
          <Import Project="{escape(str(TARGETS / 'LibreWinForms.Sdk.targets'))}" />
          <Target Name="_LibreWinFormsValidateCanonicalSdkConfiguration" />
          <Target Name="UseOriginalBootstrap" BeforeTargets="CoreCompile" Condition="'$(Original)' == 'true'">
            <ItemGroup><Compile Include="Original.cs" /></ItemGroup>
          </Target>
          </Project>''')
        (payload / "Original.cs").write_text('''internal static class OriginalBootstrap {
  [System.Runtime.CompilerServices.ModuleInitializer]
  internal static void Initialize() => LibreWinForms.ProGPU.ProGpuPlatform.Register();
}''')
        (payload / "Smoke.cs").write_text('''public static class Smoke {
  public static void Run() {
    if (!LibreWinForms.Platform.LibrePlatform.IsRegistered) throw new System.Exception("backend not registered");
    using (var control = new System.Windows.Forms.Control()) {
      System.Console.WriteLine("control-font=" + control.Font.Name);
    }
    LibreWinForms.Platform.LibrePlatform.Current.Dispose();
    System.Console.WriteLine("consumer-passed");
  }
}''')
        self.run_command("build", "Host/Host.csproj", "-c", "Release", "-m:1", "-v:q")
        for original in (False, True):
            configuration = "Original" if original else "Guarded"
            self.run_command("build", "Payload/Payload.csproj", "-c", configuration, "-m:1", "-v:q",
                             f"-p:Original={str(original).lower()}",
                             f"-p:LibreWinFormsGenerateApplicationBootstrap={str(not original).lower()}")
            for order in ("microsoft", "progpu"):
                with self.subTest(original=original, order=order):
                    output = self.run_command(host / f"bin/Release/{ARGS.framework}/Host.dll", canonical, order, FOREIGN,
                                              payload / f"bin/{configuration}/{ARGS.framework}/Payload.dll",
                                              expected="TypeLoadException" if order == "microsoft" else 0, timeout=30)
                    if order == "microsoft":
                        self.assertNotIn("consumer-passed", output)
                        if not original:
                            self.assertIn("An already-loaded Microsoft System.Drawing.Common cannot be replaced", output)
                            self.assertIn("RequireDrawingAbi", output)
                            self.assertNotIn("ProGpuPlatform.CreateServices", output)
                    else:
                        self.assertIn("control-font=", output)
                        self.assertIn("consumer-passed", output)


if __name__ == "__main__":
    unittest.main(argv=[__file__], verbosity=2)
