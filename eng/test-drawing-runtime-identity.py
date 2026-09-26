#!/usr/bin/env python3
"""Executable selected-asset and bootstrap contracts; never edits a package cache."""

import argparse
import os
from pathlib import Path
import platform
import shutil
import subprocess
import tempfile
import unittest
import zipfile
from xml.sax.saxutils import escape


PARSER = argparse.ArgumentParser(description=__doc__)
PARSER.add_argument("--dotnet", default="dotnet")
PARSER.add_argument("--framework", default="net10.0")
PARSER.add_argument("--progpu-drawing", type=Path, required=True)
PARSER.add_argument("--microsoft-drawing", type=Path,
                    help="existing Microsoft DLL; otherwise restore pinned 10.0.12 into an isolated temporary cache")
PARSER.add_argument("--canonical-directory", type=Path)
PARSER.add_argument("--targets-directory", type=Path)
PARSER.add_argument("--package-feed", type=Path)
PARSER.add_argument("--canonical-version", default="0.1.0-source-first")
PARSER.add_argument("--backend-version", default="0.1.0-source-first")
PARSER.add_argument("--sdk-version", default="0.1.0-source-first")
ARGS = PARSER.parse_args()
REPO = Path(__file__).resolve().parents[1]
TARGETS = (ARGS.targets_directory or REPO / "src/LibreWinForms.Sdk/targets").resolve()
GOOD = ARGS.progpu_drawing.resolve(strict=True)
FOREIGN = ARGS.microsoft_drawing.resolve(strict=True) if ARGS.microsoft_drawing else None
DOTNET = shutil.which(ARGS.dotnet) or str(Path(ARGS.dotnet).resolve(strict=True))


class DrawingIdentityTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="librewinforms-drawing-contract-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()

    def run_command(self, *arguments, expected=0, timeout=90):
        environment = dict(os.environ, NUGET_PACKAGES=str(self.root / "packages"),
                           NUGET_HTTP_CACHE_PATH=str(self.root / "http-cache"),
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

    def test_common_output_directory_does_not_require_skipped_copy(self):
        project = self.fixture(f'''<PropertyGroup><UseCommonOutputDirectory>true</UseCommonOutputDirectory></PropertyGroup>
          <ItemGroup><ReferenceCopyLocalPaths Include="{escape(str(GOOD))}" /></ItemGroup>
          <Target Name="CopyFilesToOutputDirectory" />''')
        self.run_command("msbuild", project, "-nologo", "-t:CopyFilesToOutputDirectory")
        out = self.root / "out/System.Drawing.Common.dll"
        out.parent.mkdir()
        shutil.copyfile(FOREIGN, out)
        self.run_command("msbuild", project, "-nologo", "-t:CopyFilesToOutputDirectory", expected="LWFDRAW001")

    def test_real_rar_renamed_drawing_reference(self):
        (self.root / "Library.cs").write_text("public class Library { }")
        for kind, source, error in (("Good", GOOD, 0), ("Foreign", FOREIGN, "LWFDRAW001")):
            renamed = self.root / f"{kind}-vendor-drawing.dll"
            shutil.copyfile(source, renamed)
            project = self.root / "Renamed.csproj"
            project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
              <TargetFramework>{ARGS.framework}</TargetFramework></PropertyGroup><ItemGroup>
              <Reference Include="System.Drawing.Common"><HintPath>{escape(str(renamed))}</HintPath><Private>true</Private></Reference>
              </ItemGroup><Import Project="{escape(str(TARGETS / 'LibreWinForms.DrawingIdentity.targets'))}" />
              </Project>''')
            with self.subTest(kind=kind, stage="compiler"):
                self.run_command("build", project.name, "-c", kind, "-m:1", "-v:q", expected=error)
            with self.subTest(kind=kind, stage="copy"):
                self.run_command("msbuild", project.name, "-nologo", "-t:ResolveReferences;_CopyFilesMarkedCopyLocal",
                                 f"-p:Configuration={kind}", expected=error)

    def test_nested_publish_destination_identifies_renamed_source(self):
        renamed = self.root / "vendor-drawing.dll"
        shutil.copyfile(FOREIGN, renamed)
        for destination in ("payload/System.Drawing.Common.dll", "payload\\System.Drawing.Common.dll",
                            "payload\\SYSTEM.DRAWING.COMMON.DLL"):
            with self.subTest(destination=destination):
                project = self.fixture(f'''<Target Name="ComputeFilesToPublish"><ItemGroup>
                  <ResolvedFileToPublish Include="{escape(str(renamed))}"><RelativePath>{destination}</RelativePath>
                  <CopyToPublishDirectory>Always</CopyToPublishDirectory></ResolvedFileToPublish>
                  </ItemGroup></Target>''')
                self.run_command("msbuild", project, "-nologo", "-t:ComputeFilesToPublish", expected="LWFDRAW001")

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
                    self.assertIn(f"runtime=.NET {ARGS.framework[3:].split('.')[0]}.", output)
                    if order == "microsoft":
                        self.assertNotIn("consumer-passed", output)
                        if not original:
                            self.assertIn("An already-loaded Microsoft System.Drawing.Common cannot be replaced", output)
                            self.assertIn("RequireDrawingAbi", output)
                            self.assertNotIn("ProGpuPlatform.CreateServices", output)
                    else:
                        self.assertIn("control-font=", output)
                        self.assertIn("consumer-passed", output)

    @unittest.skipUnless(ARGS.package_feed, "requires a freshly packed canonical package closure")
    def test_real_package_build_and_publish_contracts(self):
        feed = ARGS.package_feed.resolve(strict=True)
        package = feed / f"LibreWinForms.System.Windows.Forms.{ARGS.canonical_version}.nupkg"
        with zipfile.ZipFile(package) as archive:
            self.assertEqual(archive.read("buildTransitive/LibreWinForms.System.Windows.Forms.targets"),
                             (TARGETS / "LibreWinForms.DrawingIdentity.targets").read_bytes())
        (self.root / "NuGet.config").write_text(f'''<configuration><packageSources><clear />
          <add key="fresh-canonical" value="{escape(str(feed))}" />
          <add key="nuget" value="https://api.nuget.org/v3/index.json" />
          </packageSources></configuration>''')
        project = self.root / "PackageApp.csproj"
        project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
          <TargetFramework>{ARGS.framework}</TargetFramework><OutputType>Exe</OutputType>
          <ImplicitUsings>enable</ImplicitUsings><UseAppHost>false</UseAppHost><EnableDefaultCompileItems>false</EnableDefaultCompileItems>
          </PropertyGroup><ItemGroup><Compile Include="Program.cs" />
          <PackageReference Include="LibreWinForms.System.Windows.Forms" Version="{ARGS.canonical_version}" />
          <PackageReference Include="LibreWinForms.ProGPU" Version="{ARGS.backend_version}" />
          <PackageReference Include="System.Drawing.Common" Version="10.0.12" ExcludeAssets="all" />
          </ItemGroup>
          <Target Name="SelectForeignCompiler" BeforeTargets="_LibreWinFormsValidateDrawingCompilerIdentity" DependsOnTargets="FindReferenceAssembliesForReferences" Condition="'$(ForeignCompiler)' == 'true'">
            <ItemGroup><ReferencePathWithRefAssemblies Remove="@(ReferencePathWithRefAssemblies)" Condition="'%(Filename)' == 'System.Drawing.Common'" />
            <ReferencePathWithRefAssemblies Include="{escape(str(FOREIGN))}" /></ItemGroup>
          </Target>
          <Target Name="SelectForeignPublish" BeforeTargets="_LibreWinFormsValidateDrawingPublishIdentity" Condition="'$(ForeignPublish)' == 'true'">
            <ItemGroup><ResolvedFileToPublish Remove="@(ResolvedFileToPublish)" Condition="'%(Filename)' == 'System.Drawing.Common'" />
              <FilesToBundle Remove="@(FilesToBundle)" Condition="'%(Filename)' == 'System.Drawing.Common'" />
              <ResolvedFileToPublish Include="{escape(str(FOREIGN))}"><RelativePath>System.Drawing.Common.dll</RelativePath>
              <CopyToPublishDirectory>Always</CopyToPublishDirectory></ResolvedFileToPublish>
            </ItemGroup>
          </Target>
          </Project>''')
        (self.root / "Program.cs").write_text('''LibreWinForms.ProGPU.ProGpuPlatform.Register();
using (var control = new System.Windows.Forms.Control()) {
  System.Console.WriteLine("package-control-font=" + control.Font.Name);
}
LibreWinForms.Platform.LibrePlatform.Current.Dispose();
System.Console.WriteLine("package-consumer-passed");
''')
        self.run_command("build", project.name, "-c", "IdentityContract", "-m:1", "-v:q", timeout=180)
        output = self.run_command(self.root / f"bin/IdentityContract/{ARGS.framework}/PackageApp.dll", timeout=30)
        self.assertIn("package-consumer-passed", output)
        restored = self.root / f"packages/librewinforms.system.windows.forms/{ARGS.canonical_version}/librewinforms.system.windows.forms.{ARGS.canonical_version}.nupkg"
        self.assertEqual(restored.read_bytes(), package.read_bytes())
        library = self.root / "Library"
        library.mkdir()
        (library / "Library.csproj").write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
          <TargetFramework>{ARGS.framework}</TargetFramework><CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>
          </PropertyGroup><ItemGroup><PackageReference Include="LibreWinForms.System.Windows.Forms" Version="{ARGS.canonical_version}" />
          </ItemGroup></Project>''')
        (library / "Factory.cs").write_text('''public static class Factory {
          public static System.Windows.Forms.Control Create() => new System.Windows.Forms.Control();
        }''')
        self.run_command("build", "Library/Library.csproj", "-c", "LibraryIdentityContract", "-m:1", "-v:q")
        self.assertFalse((library / f"bin/LibraryIdentityContract/{ARGS.framework}/System.Drawing.Common.dll").exists())
        self.run_command("build", project.name, "--no-restore", "-c", "ForeignCompiler", "-m:1", "-v:q",
                         "-p:ForeignCompiler=true", expected="LWFDRAW001")
        self.run_command("publish", project.name, "--no-restore", "-c", "IdentityContract", "-m:1", "-v:q",
                         "-o", self.root / "plain")
        output = self.run_command(self.root / "plain/PackageApp.dll", timeout=30)
        self.assertIn("package-consumer-passed", output)
        self.run_command("publish", project.name, "--no-restore", "-c", "IdentityContract", "-m:1", "-v:q",
                         "-o", self.root / "foreign-publish", "-p:ForeignPublish=true", expected="LWFDRAW001")
        self.assertFalse((self.root / "foreign-publish/System.Drawing.Common.dll").exists())
        architecture = {"x86_64": "x64", "AMD64": "x64", "arm64": "arm64", "aarch64": "arm64"}[platform.machine()]
        system = {"Darwin": "osx", "Linux": "linux", "Windows": "win"}[platform.system()]
        rid = f"{system}-{architecture}"
        single = ["publish", project.name, "-c", "SingleFileContract", "-m:1", "-v:q", "-r", rid,
                  "-p:PublishSingleFile=true", "-p:SelfContained=false", "-p:UseAppHost=true"]
        self.run_command(*single, "-o", self.root / "single", timeout=180)
        self.assertFalse((self.root / "single/System.Drawing.Common.dll").exists())
        executable = self.root / ("single/PackageApp.exe" if system == "win" else "single/PackageApp")
        environment = dict(os.environ, DOTNET_ROOT=str(Path(DOTNET).resolve().parent),
                           DOTNET_ROLL_FORWARD="Major", DOTNET_ROLL_FORWARD_TO_PRERELEASE="1")
        result = subprocess.run([str(executable)], cwd=self.root, env=environment, text=True,
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=30, check=False)
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertIn("package-consumer-passed", result.stdout)
        self.run_command(*single, "--no-restore", "-o", self.root / "foreign-single",
                         "-p:ForeignPublish=true", expected="LWFDRAW001")
        self.assertFalse((self.root / ("foreign-single/PackageApp.exe" if system == "win" else "foreign-single/PackageApp")).exists())

    @unittest.skipUnless(ARGS.package_feed and ARGS.framework == "net11.0", "requires fresh LibreWinForms.Sdk and net11.0")
    def test_real_sdk_package_bootstrap(self):
        feed = ARGS.package_feed.resolve(strict=True)
        (self.root / "NuGet.config").write_text(f'''<configuration><packageSources><clear />
          <add key="fresh" value="{escape(str(feed))}" /><add key="nuget" value="https://api.nuget.org/v3/index.json" />
          </packageSources></configuration>''')
        (self.root / "SdkApp.csproj").write_text(f'''<Project Sdk="LibreWinForms.Sdk/{ARGS.sdk_version}">
          <PropertyGroup><TargetFramework>net11.0</TargetFramework><OutputType>Exe</OutputType>
          <UseWindowsForms>true</UseWindowsForms><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
          <ItemGroup><PackageReference Include="System.Drawing.Common" Version="10.0.12" ExcludeAssets="all" /></ItemGroup>
          </Project>''')
        (self.root / "Program.cs").write_text('''if (!LibreWinForms.Platform.LibrePlatform.IsRegistered)
  throw new System.Exception("generated bootstrap did not register backend");
using (var control = new System.Windows.Forms.Control()) {
  System.Console.WriteLine("sdk-control-font=" + control.Font.Name);
}
LibreWinForms.Platform.LibrePlatform.Current.Dispose();
System.Console.WriteLine("sdk-consumer-passed");
''')
        self.run_command("build", "SdkApp.csproj", "-c", "SdkIdentityContract", "-m:1", "-v:q", timeout=180)
        output = self.run_command(self.root / "bin/SdkIdentityContract/net11.0/SdkApp.dll", timeout=30)
        self.assertIn("sdk-consumer-passed", output)
        generated = (self.root / "obj/SdkIdentityContract/net11.0/LibreWinForms.ApplicationBootstrap.g.cs").read_text()
        self.assertIn("MethodImplOptions.NoInlining", generated)
        self.assertIn("An already-loaded Microsoft System.Drawing.Common cannot be replaced", generated)


if __name__ == "__main__":
    with tempfile.TemporaryDirectory(prefix="librewinforms-foreign-drawing-") as foreign_root:
        if FOREIGN is None:
            root = Path(foreign_root).resolve()
            (root / "NuGet.config").write_text('<configuration><packageSources><clear /><add key="nuget" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>')
            (root / "Foreign.csproj").write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><PackageReference Include="System.Drawing.Common" Version="10.0.12" /></ItemGroup></Project>''')
            result = subprocess.run([DOTNET, "restore", "Foreign.csproj", "--configfile", "NuGet.config", "-v:q"],
                                    cwd=root, env=dict(os.environ, NUGET_PACKAGES=str(root / "packages"),
                                                       NUGET_HTTP_CACHE_PATH=str(root / "http-cache")),
                                    stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, timeout=90, check=False)
            if result.returncode:
                raise RuntimeError("Cannot stage real pinned Microsoft drawing control:\n" + result.stdout)
            FOREIGN = (root / "packages/system.drawing.common/10.0.12/lib/net10.0/System.Drawing.Common.dll").resolve(strict=True)
        unittest.main(argv=[__file__], verbosity=2)
