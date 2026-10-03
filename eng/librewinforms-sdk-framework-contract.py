#!/usr/bin/env python3
"""Check already-produced SDK/runtime TFMs and compile real consumers; never run UI.

Requires an installed .NET 11 SDK and complete, caller-selected restore feeds.
There is no implicit remote source, producer build, source-graph build, or runtime
execution. Every child command has the existing 300-second consumer deadline.
Project-mode controls evaluate the three real direct references, not their full
transitive build graph. This is not native/package application qualification.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import tempfile
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape, quoteattr
import zipfile


TFMS = ("net10.0", "net11.0")
VALIDATE_TARGET = "_LibreWinFormsValidateCanonicalSdkConfiguration"
VERSION_ASSET = "Sdk/LibreWinForms.Sdk.Versions.props"
BOOTSTRAP = "LibreWinForms.ApplicationBootstrap.g.cs"
CONFIGURATION = "LibreWinForms.ApplicationConfiguration.g.cs"
APP_SOURCE = """internal static class Program
{
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var form = new global::System.Windows.Forms.Form();
        using var bitmap = new global::System.Drawing.Bitmap(1, 1);
        _ = global::ProGPU.SystemDrawing.NativeFontInteropServices.IsRegistered;
    }
}
"""
EMPTY_APP = "internal static class Program { private static void Main() { } }\n"
LIBRARY = "public static class ConsumerLibrary { public static int Value => 1; }\n"
CALLER_CONFIGURATION = """internal static class ApplicationConfiguration
{
    internal static void Initialize() { }
}
"""

# These are observation targets in the consumer only. They do not replace any
# SDK validation, generation, compiler, or referenced-project target.
OBSERVATION_TARGETS = """<Project>
  <Target Name="FrameworkContractPolicy"
          DependsOnTargets="_LibreWinFormsValidateCanonicalSdkConfiguration">
    <WriteLinesToFile File="$(MSBuildProjectDirectory)/configuration-policy.txt"
                      Lines="forms=$(LibreWinFormsUseSystemWindowsForms);bootstrap=$(LibreWinFormsGenerateApplicationBootstrap);framework=$(TargetFramework);payload=$(LibreWinFormsPackagedRuntimeTargetFramework)"
                      Overwrite="true" />
  </Target>
  <Target Name="FrameworkContractCompilerInputs" BeforeTargets="CoreCompile"
          DependsOnTargets="_LibreWinFormsGenerateApplicationBootstrap;_LibreWinFormsGenerateApplicationConfiguration;FindReferenceAssembliesForReferences;FrameworkContractPolicy">
    <WriteLinesToFile File="$(MSBuildProjectDirectory)/compiler-inputs.txt"
                      Lines="@(Compile->'%(FullPath)')" Overwrite="true" />
    <WriteLinesToFile File="$(MSBuildProjectDirectory)/compiler-references.txt"
                      Lines="@(ReferencePathWithRefAssemblies->'%(FullPath)')" Overwrite="true" />
  </Target>
  <Target Name="FrameworkContractProjectReferences"
          DependsOnTargets="_LibreWinFormsValidateCanonicalSdkConfiguration">
    <ItemGroup>
      <_FrameworkContractRuntimeReference Include="@(ProjectReference)"
          Condition="'%(ProjectReference.ReferenceOutputAssembly)' != 'false'" />
      <_FrameworkContractAnalyzerReference Include="@(ProjectReference)"
          Condition="'%(ProjectReference.ReferenceOutputAssembly)' == 'false'" />
    </ItemGroup>
    <WriteLinesToFile File="$(MSBuildProjectDirectory)/project-properties.txt"
                      Lines="@(_FrameworkContractRuntimeReference->'%(FullPath)|%(AdditionalProperties)')" Overwrite="true" />
    <MSBuild Projects="@(_FrameworkContractRuntimeReference)" Targets="GetTargetFrameworks" BuildInParallel="false">
      <Output TaskParameter="TargetOutputs" ItemName="_FrameworkContractReference" />
    </MSBuild>
    <WriteLinesToFile File="$(MSBuildProjectDirectory)/project-frameworks.txt"
                      Lines="@(_FrameworkContractReference->'%(Identity)|%(TargetFrameworks)')" Overwrite="true" />
    <MSBuild Projects="@(_FrameworkContractAnalyzerReference)" Targets="GetTargetFrameworks" BuildInParallel="false">
      <Output TaskParameter="TargetOutputs" ItemName="_FrameworkContractAnalyzer" />
    </MSBuild>
    <WriteLinesToFile File="$(MSBuildProjectDirectory)/analyzer-frameworks.txt"
                      Lines="@(_FrameworkContractAnalyzer->'%(Identity)|%(TargetFrameworks)')" Overwrite="true" />
  </Target>
</Project>
"""


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def local_name(element):
    return element.tag.rsplit("}", 1)[-1]


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def package_path(feed, package_id, version):
    require(re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9.+-]*", version) is not None,
            f"Invalid literal package version: {version!r}")
    wanted = f"{package_id}.{version}.nupkg".lower()
    matches = [path for path in feed.iterdir() if path.is_file() and path.name.lower() == wanted]
    require(len(matches) == 1, f"Require one exact already-produced {wanted} in {feed}")
    return matches[0]


def archive_names(archive):
    names = archive.namelist()
    require(len(names) == len(set(names)), "Package contains duplicate ZIP entries")
    return names


def package_identity(archive, package_id, version):
    nuspecs = [name for name in archive_names(archive) if name.lower().endswith(".nuspec")]
    require(len(nuspecs) == 1, f"Require exactly one nuspec: {package_id}")
    root = ET.fromstring(archive.read(nuspecs[0]))
    metadata = [node for node in root if local_name(node) == "metadata"]
    require(len(metadata) == 1, f"Require one package metadata element: {package_id}")
    for key, expected in (("id", package_id), ("version", version)):
        values = [node.text for node in metadata[0] if local_name(node) == key]
        require(values == [expected], f"{package_id} nuspec {key} differs: {values!r}")
    return metadata[0]


def sdk_metadata(package, version):
    with zipfile.ZipFile(package) as archive:
        package_identity(archive, "LibreWinForms.Sdk", version)
        root = ET.fromstring(archive.read(VERSION_ASSET))
        result = {}
        for key in ("LibreWinFormsPackagedRuntimeTargetFramework",
                    "LibreWinFormsPackagedRuntimeVersion", "LibreWinFormsPackagedProGpuBackendVersion"):
            nodes = [node for node in root.iter() if local_name(node) == key]
            require(len(nodes) == 1 and not nodes[0].attrib and nodes[0].text,
                    f"SDK must declare one unconditional literal {key}")
            result[key] = nodes[0].text.strip()
        require(result["LibreWinFormsPackagedRuntimeTargetFramework"] in TFMS,
                "SDK declares missing/unknown packaged runtime TFM")
        return result


def verify_runtime_package(package, package_id, version, framework, assembly):
    with zipfile.ZipFile(package) as archive:
        metadata = package_identity(archive, package_id, version)
        names = archive_names(archive)
        assets = [name for name in names if name.startswith(("lib/", "ref/")) and not name.endswith("/")]
        require(f"lib/{framework}/{assembly}.dll" in assets,
                f"{package_id} lacks actual {framework} runtime assembly {assembly}")
        require(f"ref/{framework}/{assembly}.dll" in assets,
                f"{package_id} lacks actual {framework} reference assembly {assembly}")
        frameworks = {name.split("/")[1] for name in assets}
        require(frameworks == {framework},
                f"{package_id} lib/ref TFMs {frameworks!r} disagree with SDK metadata {framework}")
        groups = [node.attrib.get("targetFramework", "") for node in metadata.iter()
                  if local_name(node) == "group"]
        # NuGet's nuspec spelling and the short TFM spelling denote the same
        # exact non-platform framework; no prefix/platform-version matching.
        accepted = {framework, ".NETCoreApp" + framework.removeprefix("net")}
        require(bool(groups) and all(group in accepted for group in groups),
                f"{package_id} dependency groups disagree with {framework}: {groups!r}")
        return {"package": str(package), "sha256": sha256(package.read_bytes()),
                "framework": framework, "dependencyGroups": groups,
                "assets": {name: sha256(archive.read(name)) for name in assets}}


def run_command(command, cwd, environment, log):
    with log.open("w", encoding="utf-8") as output:
        process = subprocess.Popen(command, cwd=cwd, env=environment, stdout=output,
                                   stderr=subprocess.STDOUT, start_new_session=os.name == "posix")
        try:
            return process.wait(timeout=300)
        except BaseException:
            if os.name == "posix":
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
            elif process.poll() is None:
                # Kill only this owned command tree, including its compiler.
                subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                               stdout=output, stderr=subprocess.STDOUT, timeout=30, check=False)
            if process.poll() is None:
                process.kill()
            process.wait()
            raise


def write_project(case, version, framework, output_type, properties=None, source=EMPTY_APP):
    case.mkdir()
    project = case / "Consumer.csproj"
    group = {"OutputType": output_type, "TargetFramework": framework, **(properties or {})}
    project.write_text(f'<Project Sdk={quoteattr("LibreWinForms.Sdk/" + version)}>\n  <PropertyGroup>\n'
                       + "".join(f"    <{key}>{escape(value)}</{key}>\n" for key, value in group.items())
                       + "  </PropertyGroup>\n</Project>\n", encoding="utf-8")
    (case / "Program.cs").write_text(source, encoding="utf-8")
    (case / "Directory.Build.targets").write_text(OBSERVATION_TARGETS, encoding="utf-8")
    return project


def copy_text(source, destination):
    destination.write_text(source.read_text(encoding="utf-8-sig"), encoding="utf-8")


def build_consumer(args, scratch, environment, evidence, framework, name, output_type,
                   properties=None, forms=True, bootstrap=True, configuration=True, caller=False):
    case = scratch / (framework + "-" + name)
    source = APP_SOURCE if forms else LIBRARY if output_type == "Library" else EMPTY_APP
    project = write_project(case, args.sdk_version, framework, output_type, properties, source)
    if caller:
        (case / "Caller.cs").write_text(CALLER_CONFIGURATION, encoding="utf-8")
    record = evidence / case.name
    record.mkdir()
    for path in case.iterdir():
        copy_text(path, record / path.name)
    command = [args.dotnet, "build", str(project), "--configuration", args.configuration, "--nologo",
               "--disable-build-servers", "-p:EmitCompilerGeneratedFiles=true",
               "-p:UseAppHost=false", "-p:ErrorLog=" + str(case / "diagnostics.sarif")]
    exit_code = run_command(command, scratch, environment, record / "build.log")
    require(exit_code == 0, f"Consumer failed: {case.name}; see {record / 'build.log'}")
    diagnostics = json.loads((case / "diagnostics.sarif").read_text(encoding="utf-8"))
    faults = [item for run in diagnostics["runs"] for item in run.get("results", [])
              if item.get("level") in ("error", "warning")]
    require(not faults, f"Consumer/compiler/analyzer diagnostics in {case.name}: {faults!r}")
    copy_text(case / "diagnostics.sarif", record / "diagnostics.sarif")
    for filename in ("compiler-inputs.txt", "compiler-references.txt", "configuration-policy.txt"):
        copy_text(case / filename, record / filename)
    compiler_inputs = {Path(value).resolve() for value in
                       (case / "compiler-inputs.txt").read_text(encoding="utf-8-sig").splitlines()}
    for filename, expected in ((BOOTSTRAP, bootstrap), (CONFIGURATION, configuration)):
        generated = list(case.rglob(filename))
        require(len(generated) == int(expected), f"{case.name}: unexpected {filename} generation")
        if generated:
            require(generated[0].resolve() in compiler_inputs,
                    f"{case.name}: {filename} was not a real compiler input")
            text = generated[0].read_text(encoding="utf-8-sig")
            if filename == BOOTSTRAP:
                require("ModuleInitializer" in text and "LibreWinForms.ProGPU.ProGpuPlatform.Register()" in text,
                        f"{case.name}: missing real module initializer/backend registration")
            else:
                require("internal static void Initialize()" in text and "ConfigureHighDpiMode()" in text,
                        f"{case.name}: missing SDK-owned Initialize")
            copy_text(generated[0], record / filename)
    require(not list(case.rglob("ApplicationConfiguration.g.cs")),
            f"{case.name}: upstream full generator duplicated SDK/caller configuration")
    policy = dict(line.split("=", 1) for line in
                  (case / "configuration-policy.txt").read_text(encoding="utf-8-sig").splitlines())
    require(policy["forms"] == str(forms).lower() and policy["bootstrap"] == str(bootstrap).lower(),
            f"{case.name}: resolved SDK policy differs: {policy!r}")
    references = [Path(line) for line in
                  (case / "compiler-references.txt").read_text(encoding="utf-8-sig").splitlines()]
    if forms:
        drawing = [path for path in references if path.name == "System.Drawing.Common.dll"]
        require(len(drawing) == 1 and "progpu.system.drawing.common" in str(drawing[0]).lower(),
                f"{case.name}: compiler did not select the ProGPU Drawing replacement")
        require(any(path.name == "System.Windows.Forms.dll" for path in references),
                f"{case.name}: missing canonical Forms compiler reference")
    assets = json.loads((case / "obj/project.assets.json").read_text(encoding="utf-8"))
    for package_id, key in (("LibreWinForms.System.Windows.Forms", "LibreWinFormsPackagedRuntimeVersion"),
                            ("LibreWinForms.ProGPU", "LibreWinFormsPackagedProGpuBackendVersion")):
        require(f"{package_id}/{args.metadata[key]}" in assets["libraries"],
                f"{case.name}: wrong runtime/backend version selected")
    require(not any(key.lower().startswith("librewinforms.compatibility.") for key in assets["libraries"]),
            f"{case.name}: transitional compatibility dependency selected")
    assembly = case / "bin" / args.configuration / framework / "Consumer.dll"
    require(assembly.is_file(), f"{case.name}: no compiled consumer assembly")
    result = {"case": case.name, "command": command, "exitCode": exit_code,
              "forms": forms, "bootstrap": bootstrap, "configuration": configuration,
              "assemblySha256": sha256(assembly.read_bytes()), "executed": False}
    (record / "result.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return result


def reject_consumer(args, scratch, environment, evidence, name, framework, code, overrides=None):
    case = scratch / name
    project = write_project(case, args.sdk_version, framework, "Exe")
    command = [args.dotnet, "msbuild", str(project), "-nologo", "-nr:false",
               "-target:" + VALIDATE_TARGET]
    command.extend(f"-property:{key}={value}" for key, value in (overrides or {}).items())
    log = evidence / (name + ".log")
    exit_code = run_command(command, scratch, environment, log)
    text = log.read_text(encoding="utf-8")
    errors = set(re.findall(r"\berror\s+([A-Za-z]+[0-9]+)\s*:", text, re.IGNORECASE))
    require(exit_code != 0 and errors == {code},
            f"{name}: expected only SDK {code}, got exit {exit_code}, {errors!r}; see {log}")
    require(not list(case.rglob("*.dll")) and not list(case.rglob(BOOTSTRAP)),
            f"{name}: rejected policy reached compilation/generation")
    copy_text(project, evidence / (name + ".csproj"))
    return {"case": name, "command": command, "exitCode": exit_code, "error": code}


def project_references(args, scratch, environment, evidence, framework):
    case = scratch / (framework + "-project-references")
    project = write_project(case, args.sdk_version, framework, "Exe", {
        "LibreWinFormsReferenceMode": "Project", "LibreWinFormsSourceRoot": str(args.repo_root) + "/"})
    command = [args.dotnet, "msbuild", str(project), "-nologo", "-nr:false",
               "-target:FrameworkContractProjectReferences"]
    log = evidence / (case.name + ".log")
    require(run_command(command, scratch, environment, log) == 0,
            f"Real Project framework evaluation failed; no fallback or skip: {log}")
    expected = {(args.repo_root / path).resolve() for path in (
        "src/System.Windows.Forms/System.Windows.Forms.csproj",
        "src/System.Windows.Forms.Design/src/System.Windows.Forms.Design.csproj",
        "src/LibreWinForms.ProGPU/LibreWinForms.ProGPU.csproj")}
    records = (case / "project-properties.txt").read_text(encoding="utf-8-sig").splitlines()
    # Preserve complete property metadata whether WriteLinesToFile receives its
    # semicolons in one item or expands them into separate lines.
    references = {}
    current = None
    for line in records:
        if "|" in line:
            path, properties = line.split("|", 1)
            current = Path(path).resolve()
            require(current not in references, "Duplicate ProjectReference property receipt")
            references[current] = {}
        else:
            require(current is not None, "ProjectReference property receipt has no owner")
            properties = line
        references[current].update(dict(item.split("=", 1) for item in properties.split(";") if item))
    require(set(references) == expected, "Project mode changed its actual direct reference inventory")
    require(all(values.get("NetCurrent") == framework for values in references.values()),
            f"Project mode failed to carry NetCurrent={framework} through AdditionalProperties")
    outputs = (case / "project-frameworks.txt").read_text(encoding="utf-8-sig").splitlines()
    actual = [line.rsplit("|", 1) for line in outputs]
    require(len(actual) == 3 and {Path(path).resolve() for path, _ in actual} == expected
            and all(tfm == framework for _, tfm in actual),
            f"Real GetTargetFrameworks output differs from {framework}: {actual!r}")
    analyzer_outputs = (case / "analyzer-frameworks.txt").read_text(encoding="utf-8-sig").splitlines()
    analyzers = [line.rsplit("|", 1) for line in analyzer_outputs]
    expected_analyzers = {(args.repo_root / path).resolve() for path in (
        "src/System.Windows.Forms.Analyzers/src/System.Windows.Forms.Analyzers.csproj",
        "src/System.Windows.Forms.Analyzers.CSharp/src/System.Windows.Forms.Analyzers.CSharp.csproj",
        "src/System.Windows.Forms.Analyzers.VisualBasic/src/System.Windows.Forms.Analyzers.VisualBasic.vbproj")}
    require(len(analyzers) == 3 and {Path(path).resolve() for path, _ in analyzers} == expected_analyzers
            and all(tfm == "netstandard2.0" for _, tfm in analyzers),
            f"Original analyzer reference frameworks changed: {analyzers!r}")
    for filename in ("project-properties.txt", "project-frameworks.txt", "analyzer-frameworks.txt"):
        copy_text(case / filename, evidence / (case.name + "-" + filename))
    return {"case": case.name, "command": command, "directReferences": [str(path) for path in sorted(expected)],
            "framework": framework, "sourceBuildExecuted": False}


def late_forms_opt_out(args, scratch, environment, evidence, framework):
    case = scratch / "body-false-overrides-early-request"
    project = write_project(case, args.sdk_version, framework, "Exe", {"UseWindowsForms": "false"})
    # This is the SDK's retained early request; the real project body owns the
    # later explicit false. Evaluate actual SDK targets without source builds.
    command = [args.dotnet, "msbuild", str(project), "-nologo", "-nr:false",
               "-target:FrameworkContractPolicy", "-property:LibreWinFormsRequestedUseWindowsForms=true"]
    log = evidence / (case.name + ".log")
    require(run_command(command, scratch, environment, log) == 0,
            f"Body/early-request control failed; see {log}")
    policy = dict(line.split("=", 1) for line in
                  (case / "configuration-policy.txt").read_text(encoding="utf-8-sig").splitlines())
    require(policy["forms"] == "false" and policy["bootstrap"] == "false",
            f"Explicit body opt-out lost to retained early request: {policy!r}")
    copy_text(project, evidence / (case.name + ".csproj"))
    copy_text(case / "configuration-policy.txt", evidence / (case.name + ".txt"))
    return {"case": case.name, "command": command, "policy": policy}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package-source", type=Path, required=True)
    parser.add_argument("--sdk-version", required=True)
    parser.add_argument("--evidence-directory", type=Path, required=True)
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--dependency-source", action="append", default=[],
                        help="Explicit additional NuGet feed; repeatable, no implicit remote feed")
    args = parser.parse_args()
    args.package_source = args.package_source.resolve(strict=True)
    args.repo_root = args.repo_root.resolve(strict=True)
    evidence = args.evidence_directory.resolve()
    evidence.mkdir(parents=True, exist_ok=False)
    sdk = package_path(args.package_source, "LibreWinForms.Sdk", args.sdk_version)
    args.metadata = sdk_metadata(sdk, args.sdk_version)
    framework = args.metadata["LibreWinFormsPackagedRuntimeTargetFramework"]
    payloads = []
    for package_id, key, assembly in (
        ("LibreWinForms.System.Windows.Forms", "LibreWinFormsPackagedRuntimeVersion", "System.Windows.Forms"),
        ("LibreWinForms.ProGPU", "LibreWinFormsPackagedProGpuBackendVersion", "LibreWinForms.ProGPU")):
        version = args.metadata[key]
        payloads.append(verify_runtime_package(package_path(args.package_source, package_id, version),
                                              package_id, version, framework, assembly))
    manifest = {"sdk": str(sdk), "sdkSha256": sha256(sdk.read_bytes()),
                "metadata": args.metadata, "payloads": payloads}
    (evidence / "package-frameworks.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    results = []
    with tempfile.TemporaryDirectory(prefix="librewinforms-sdk-framework.") as temporary:
        scratch = Path(temporary)
        config = ET.Element("configuration")
        sources = ET.SubElement(config, "packageSources")
        ET.SubElement(sources, "clear")
        for index, feed in enumerate([str(args.package_source), *args.dependency_source]):
            ET.SubElement(sources, "add", key=f"Contract{index}", value=feed)
        ET.ElementTree(config).write(scratch / "NuGet.Config", encoding="utf-8", xml_declaration=True)
        copy_text(scratch / "NuGet.Config", evidence / "NuGet.Config")
        environment = os.environ.copy()
        environment.update({"NUGET_PACKAGES": str(scratch / "packages"), "MSBUILDDISABLENODEREUSE": "1",
                            "DOTNET_CLI_UI_LANGUAGE": "en-US", "DOTNET_CLI_TELEMETRY_OPTOUT": "1"})
        for tfm in TFMS:
            if tfm == "net10.0" and framework == "net11.0":
                results.append(reject_consumer(args, scratch, environment, evidence,
                                               "net10-actual-net11-payload", tfm, "LWFTFM003"))
                continue
            output_type = "WinExe" if tfm == "net10.0" else "Exe"
            results.append(build_consumer(args, scratch, environment, evidence, tfm,
                                           "one-line-" + output_type.lower(), output_type))
        # Keep the compiler matrix bounded at seven builds for a net10 payload.
        # Both consumer TFMs compile; policy opt-outs need not rebuild every TFM.
        results.append(build_consumer(args, scratch, environment, evidence, framework,
                                       "explicit-forms-template", "WinExe", {"UseWindowsForms": "true"}))
        for name, properties in (
            ("use-windows-forms-false", {"UseWindowsForms": "false"}),
            ("sdk-forms-false", {"LibreWinFormsUseSystemWindowsForms": "false"})):
            results.append(build_consumer(args, scratch, environment, evidence, framework, name, "Exe",
                                           properties, forms=False, bootstrap=False, configuration=False))
        results.append(build_consumer(args, scratch, environment, evidence, framework, "library-default", "Library",
                                       forms=False, bootstrap=False, configuration=False))
        results.append(build_consumer(args, scratch, environment, evidence, framework, "caller-generation", "Exe",
                                       {"LibreWinFormsGenerateApplicationConfiguration": "false",
                                        "LibreWinFormsGenerateApplicationBootstrap": "false"},
                                       bootstrap=False, configuration=False, caller=True))
        results.append(late_forms_opt_out(args, scratch, environment, evidence, framework))
        for name, tfm in (("unsupported-net9", "net9.0"), ("unsupported-windows-suffix", "net10.0-windows")):
            results.append(reject_consumer(args, scratch, environment, evidence, name, tfm, "LWFTFM001"))
        for name, declared, code in (("missing-payload-tfm", "", "LWFTFM002"),
                                     ("unknown-payload-tfm", "net12.0", "LWFTFM002"),
                                     ("incompatible-payload-tfm", "net11.0", "LWFTFM003")):
            results.append(reject_consumer(args, scratch, environment, evidence, name, "net10.0", code,
                                           {"LibreWinFormsPackagedRuntimeTargetFramework": declared}))
        installed = scratch / "packages/librewinforms.sdk" / args.sdk_version.lower() / sdk.name.lower()
        require(installed.is_file() and installed.read_bytes() == sdk.read_bytes(),
                "SDK resolver did not install the exact supplied producer archive")
        for tfm in TFMS:
            results.append(project_references(args, scratch, environment, evidence, tfm))
    (evidence / "results.json").write_text(json.dumps({"schemaVersion": 1, "results": results,
                                                      "applicationExecution": False}, indent=2) + "\n", encoding="utf-8")
    print(f"PASS SDK framework contract: {len(results)} cases; compiled/evaluated only, no application execution")


if __name__ == "__main__":
    main()
