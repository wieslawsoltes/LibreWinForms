#!/usr/bin/env python3
"""Compile real SDK consumers and verify its original analyzer payload; no UI runs."""

import argparse
import copy
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import tempfile
import warnings
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape
import zipfile


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def verify_payload(repo, package, configuration):
    expected = {}
    for suffix, language in (("", ""), (".CSharp", "cs/"), (".VisualBasic", "vb/")):
        name = "System.Windows.Forms.Analyzers" + suffix
        output = repo / "artifacts/bin" / name / configuration / "netstandard2.0"
        prefix = "analyzers/dotnet/" + language
        main = output / (name + ".dll")
        expected[prefix + main.name] = main
        cultures = repo / "src" / name / "src/Resources/xlf"
        for translated in sorted(cultures.glob("SR.*.xlf")):
            culture = translated.name[len("SR."):-len(".xlf")]
            resource = output / culture / (name + ".resources.dll")
            expected[prefix + culture + "/" + resource.name] = resource
    with zipfile.ZipFile(package) as archive:
        actual = [entry for entry in archive.namelist() if entry.startswith("analyzers/")]
        if len(actual) != len(set(actual)) or set(actual) != set(expected):
            raise AssertionError(f"Analyzer payload differs from original source outputs: {set(actual) ^ set(expected)}")
        hashes = {}
        for entry, original in expected.items():
            source = original.read_bytes()
            if archive.read(entry) != source:
                raise AssertionError(f"Analyzer payload is not the exact source assembly: {entry}")
            hashes[entry] = sha256(source)
        nuspec = ET.fromstring(archive.read("LibreWinForms.Sdk.nuspec"))
        for dependency in nuspec.iter():
            if dependency.tag.endswith("dependency") and "CodeAnalysis" in dependency.attrib.get("id", ""):
                raise AssertionError("The installed SDK must not add Roslyn runtime dependencies")
    return {"package": str(package), "packageSha256": sha256(package.read_bytes()), "files": hashes}


def verify_rejected_payloads(args, package, evidence):
    """Damage only scratch copies, never a producer archive or NuGet cache."""
    target = "analyzers/dotnet/cs/System.Windows.Forms.Analyzers.CSharp.dll"
    results = []
    for fault in ("missing", "changed", "extra", "duplicate"):
        mutant = evidence / ("invalid-analyzer-" + fault + ".nupkg")
        with zipfile.ZipFile(package) as original, zipfile.ZipFile(mutant, "w") as output:
            for item in original.infolist():
                if fault == "missing" and item.filename == target:
                    continue
                data = original.read(item)
                if fault == "changed" and item.filename == target:
                    data = bytes([data[0] ^ 1]) + data[1:]
                output.writestr(copy.copy(item), data)
            if fault == "extra":
                output.writestr("analyzers/dotnet/Microsoft.CodeAnalysis.dll", b"unexpected dependency")
            if fault == "duplicate":
                with warnings.catch_warnings():
                    warnings.simplefilter("ignore", UserWarning)
                    output.writestr(target, original.read(target))
        try:
            verify_payload(args.repo_root, mutant, args.configuration)
        except AssertionError as error:
            results.append({"fault": fault, "rejection": str(error), "packageSha256": sha256(mutant.read_bytes())})
        else:
            raise AssertionError(f"The payload verifier admitted its {fault} negative control")
    (evidence / "rejected-payloads.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
    return results


CS_NEGATIVE = """using System.Windows.Forms;
public sealed class ProbeControl : Control
{
    public string Unconfigured { get; set; } = "";
}
"""
CS_POSITIVE = """using System.ComponentModel;
using System.Windows.Forms;
public sealed class ProbeControl : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Hidden { get; set; } = "";
    [DefaultValue(0)]
    public int WithDefault { get; set; }
    public int WithPolicy { get; set; }
    public bool ShouldSerializeWithPolicy() => false;
    public int PrivateSetter { get; private set; }
}
"""
VB_NEGATIVE = """Imports System.Windows.Forms
Public NotInheritable Class ProbeControl
    Inherits Control
    Public Property Unconfigured As String
End Class
"""
VB_POSITIVE = """Imports System.ComponentModel
Imports System.Windows.Forms
Public NotInheritable Class ProbeControl
    Inherits Control
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Hidden As String
    <DefaultValue(0)>
    Public Property WithDefault As Integer
    Public Property WithPolicy As Integer
    Public Function ShouldSerializeWithPolicy() As Boolean
        Return False
    End Function
End Class
"""
ENTRYPOINTS = {
    "top-level": "ApplicationConfiguration.Initialize();",
    "block-namespace": "namespace Example { internal static class Program { private static void Main() { ApplicationConfiguration.Initialize(); } } }",
    "file-namespace": "namespace Example; internal static class Program { private static void Main() { ApplicationConfiguration.Initialize(); } }",
}
CALLER_CONFIGURATION = "internal static class ApplicationConfiguration { internal static void Initialize() { } }"


def build_case(args, scratch, evidence, mode, name, source, *, vb=False,
               executable=False, caller_configuration=False, disable_configuration=False,
               expected_errors=(), ordinary_generator=False, sdk_directory=None,
               missing_analyzer=None):
    case = scratch / (mode.lower() + "-" + name)
    case.mkdir()
    extension = "vb" if vb else "cs"
    project = case / ("Consumer." + extension + "proj")
    (case / ("Consumer." + extension)).write_text(source, encoding="utf-8")
    if caller_configuration:
        (case / "Caller.cs").write_text(CALLER_CONFIGURATION, encoding="utf-8")
    sdk = "Microsoft.NET.Sdk" if ordinary_generator else "LibreWinForms.Sdk/" + args.sdk_version
    properties = [
        "<TargetFramework>net11.0</TargetFramework>",
        "<OutputType>Exe</OutputType>" if executable else "<OutputType>Library</OutputType>",
        f"<UseWindowsForms>{str(executable and not ordinary_generator).lower()}</UseWindowsForms>",
        "<RootNamespace></RootNamespace>",
        "<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>",
        "<CompilerGeneratedFilesOutputPath>$(IntermediateOutputPath)generated</CompilerGeneratedFilesOutputPath>",
        f"<LibreWinFormsReferenceMode>{mode}</LibreWinFormsReferenceMode>",
    ]
    if disable_configuration:
        properties.append("<LibreWinFormsGenerateApplicationConfiguration>false</LibreWinFormsGenerateApplicationConfiguration>")
    if mode == "Project":
        properties.append(f"<LibreWinFormsSourceRoot>{escape(str(args.repo_root))}/</LibreWinFormsSourceRoot>")
    if args.progpu_source_root:
        properties.append(f"<LibreWinFormsProGpuSourceRoot>{escape(str(args.progpu_source_root))}/</LibreWinFormsProGpuSourceRoot>")
    extra = ""
    if ordinary_generator:
        sdk_cache = scratch / "packages/librewinforms.sdk" / args.sdk_version
        extra = f"""<ItemGroup>
          <PackageReference Include="LibreWinForms.System.Windows.Forms" Version="{escape(args.runtime_version)}" />
          <Analyzer Include="{escape(str(sdk_cache / 'analyzers/dotnet/System.Windows.Forms.Analyzers.dll'))}" />
          <Analyzer Include="{escape(str(sdk_cache / 'analyzers/dotnet/cs/System.Windows.Forms.Analyzers.CSharp.dll'))}" />
        </ItemGroup>"""
    if sdk_directory is None:
        start = f'<Project Sdk="{sdk}">'
        end = "</Project>"
    else:
        # Import a private extracted copy of the real SDK for missing-file tests.
        # Its normal Microsoft.NET.Sdk imports and validation targets still run.
        start = f'<Project><Import Project="{escape(str(sdk_directory / "Sdk/Sdk.props"))}" />'
        end = f'<Import Project="{escape(str(sdk_directory / "Sdk/Sdk.targets"))}" /></Project>'
    recorder = """<Target Name="RecordContractAnalyzerInputs" BeforeTargets="CoreCompile">
      <WriteLinesToFile File="$(MSBuildProjectDirectory)/analyzer-inputs.txt"
                        Lines="@(Analyzer->'%(FullPath)')" Overwrite="true" />
    </Target>"""
    project.write_text(start + "<PropertyGroup>" + "".join(properties)
                       + "</PropertyGroup>" + extra + recorder + end, encoding="utf-8")
    sarif = case / "diagnostics.sarif"
    command = [args.dotnet, "build", str(project), "--configuration", args.configuration,
               "--nologo", "--disable-build-servers", "-p:ErrorLog=" + str(sarif)]
    if mode == "Project":
        # Match the existing source-first SDK consumer graph. Global properties
        # also reach the canonical runtime's real transitive project references.
        command.extend(["-p:LibreWinFormsUseCanonicalRuntime=true", "-p:LibreWinFormsUseProGpuSystemDrawing=true",
                        "-p:LibreWinFormsReferenceMode=Project", "-p:MicrosoftNETCoreAppRefPackageVersion="])
    if args.progpu_source_root:
        command.append("-p:LibreWinFormsProGpuSourceRoot=" + str(args.progpu_source_root) + "/")
    environment = os.environ.copy()
    environment["NUGET_PACKAGES"] = str(scratch / "packages")
    log = evidence / (case.name + ".log")
    with log.open("w", encoding="utf-8") as output:
        process = subprocess.Popen(command, cwd=scratch, env=environment, start_new_session=True,
                                   stdout=output, stderr=subprocess.STDOUT)
        try:
            exit_code = process.wait(timeout=300)
        except BaseException:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            process.wait()
            raise
    if missing_analyzer is not None:
        message = "The LibreWinForms SDK is missing an original WinForms analyzer: "
        missing_lines = [line for line in log.read_text(encoding="utf-8").splitlines() if message in line]
        if exit_code == 0 or not missing_lines or not all(missing_analyzer in line for line in missing_lines):
            raise AssertionError(f"Missing analyzer was not rejected by the SDK's explicit guard; see {log}")
        if sarif.exists():
            raise AssertionError("Missing analyzer reached compilation instead of failing in SDK validation")
        print(f"PASS {case.name}: missing payload rejected before compilation", flush=True)
        return {"case": case.name, "exitCode": exit_code, "missingAnalyzer": missing_analyzer}
    if not sarif.is_file():
        raise AssertionError(f"Compiler did not produce diagnostics for {case.name}; see {log}")
    report = json.loads(sarif.read_text(encoding="utf-8"))
    diagnostics = [item for run in report["runs"] for item in run.get("results", [])]
    errors = sorted(item["ruleId"] for item in diagnostics if item.get("level") == "error")
    if errors != sorted(expected_errors) or (exit_code == 0) != (not expected_errors):
        raise AssertionError(f"{case.name}: expected {expected_errors}, got {errors}, exit {exit_code}; see {log}")
    if any(item["ruleId"].startswith(("CS803", "CS878", "AD000")) for item in diagnostics):
        raise AssertionError(f"Analyzer/generator failed to load or execute: {case.name}")
    for diagnostic in diagnostics:
        if diagnostic["ruleId"] == "WFO1000":
            message = diagnostic.get("message", {})
            if "Unconfigured" not in (message.get("text", "") if isinstance(message, dict) else message):
                raise AssertionError(f"WFO1000 did not report the actual source property in {case.name}")
    generated = list(case.rglob("ApplicationConfiguration.g.cs"))
    sdk_generated = list(case.rglob("LibreWinForms.ApplicationConfiguration.g.cs"))
    if ordinary_generator:
        if len(generated) != 1:
            raise AssertionError("The ordinary upstream generator must remain present and active")
        text = generated[0].read_text(encoding="utf-8")
        for statement in ("Application.EnableVisualStyles();", "Application.SetCompatibleTextRenderingDefault(false);",
                          "Application.SetHighDpiMode(HighDpiMode.SystemAware);"):
            if statement not in text:
                raise AssertionError(f"Ordinary upstream default is missing: {statement}")
    elif generated:
        raise AssertionError(f"Upstream generator emitted SDK/caller-owned configuration in {case.name}")
    if executable and not ordinary_generator and bool(sdk_generated) != (not disable_configuration):
        raise AssertionError(f"The existing SDK configuration ownership switch changed: {case.name}")
    saved = evidence / case.name
    saved.mkdir()
    analyzer_inputs = [Path(line) for line in (case / "analyzer-inputs.txt").read_text(encoding="utf-8").splitlines()
                       if Path(line).name.startswith("System.Windows.Forms.Analyzers")]
    expected_names = {"System.Windows.Forms.Analyzers.dll",
                      "System.Windows.Forms.Analyzers.VisualBasic.dll" if vb else "System.Windows.Forms.Analyzers.CSharp.dll"}
    if len(analyzer_inputs) != 2 or {path.name for path in analyzer_inputs} != expected_names:
        raise AssertionError(f"Compiler did not receive exactly the shared and selected language analyzers: {analyzer_inputs}")
    selected_hashes = {}
    for path in analyzer_inputs:
        source_output = args.repo_root / "artifacts/bin" / path.stem / args.configuration / "netstandard2.0" / path.name
        if mode == "Project" and path.resolve() != source_output.resolve():
            raise AssertionError(f"Project-mode compiler selected a different analyzer source output: {path}")
        selected_hashes[str(path)] = sha256(path.read_bytes())
    (saved / "analyzer-inputs.json").write_text(json.dumps(selected_hashes, indent=2) + "\n", encoding="utf-8")
    shutil.copy2(project, saved / project.name)
    shutil.copy2(sarif, saved / sarif.name)
    for source_file in case.glob("*." + extension):
        shutil.copy2(source_file, saved / source_file.name)
    for index, generated_file in enumerate(generated + sdk_generated):
        shutil.copy2(generated_file, saved / f"{index}-{generated_file.name}")
    print(f"PASS {case.name}: expected errors={list(expected_errors)}", flush=True)
    return {"case": case.name, "exitCode": exit_code, "expectedErrors": list(expected_errors)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--package-source", type=Path, required=True)
    parser.add_argument("--sdk-version", required=True)
    parser.add_argument("--runtime-version", default="0.1.0-source-first")
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--reference-mode", choices=("Project", "Package", "Both"), default="Both")
    parser.add_argument("--progpu-source-root", type=Path)
    parser.add_argument("--scratch-parent", type=Path,
                        help="Parent owned by the calling gate's cleanup; omitted scratch is retained for local diagnosis")
    parser.add_argument("--evidence-directory", type=Path, required=True)
    args = parser.parse_args()
    args.repo_root = args.repo_root.resolve()
    args.package_source = args.package_source.resolve()
    evidence = args.evidence_directory.resolve()
    evidence.mkdir(parents=True, exist_ok=False)
    package = args.package_source / f"LibreWinForms.Sdk.{args.sdk_version}.nupkg"
    payload = verify_payload(args.repo_root, package, args.configuration)
    payload["sourceHead"] = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=args.repo_root, text=True).strip()
    payload["sourceChanges"] = subprocess.check_output(["git", "status", "--porcelain"], cwd=args.repo_root, text=True).splitlines()
    (evidence / "analyzer-payload.json").write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    rejected_payloads = verify_rejected_payloads(args, package, evidence)
    scratch = Path(tempfile.mkdtemp(prefix="librewinforms-analyzer-contract.", dir=args.scratch_parent))
    (evidence / "scratch-root.txt").write_text(str(scratch) + "\n", encoding="utf-8")
    # Outside the source tree: its Arcade build settings must not leak into consumers.
    config = ET.parse(args.repo_root / "NuGet.config")
    sources = config.getroot().find("packageSources")
    ET.SubElement(sources, "add", key="LibreWinFormsAnalyzerContract", value=str(args.package_source))
    config.write(scratch / "NuGet.config", encoding="utf-8", xml_declaration=True)
    modes = ("Project", "Package") if args.reference_mode == "Both" else (args.reference_mode,)
    results = []
    for mode in modes:
        for name, source, vb, errors in (("csharp-negative", CS_NEGATIVE, False, ("WFO1000",)),
                                         ("csharp-positive", CS_POSITIVE, False, ()),
                                         ("vb-negative", VB_NEGATIVE, True, ("WFO1000",)),
                                         ("vb-positive", VB_POSITIVE, True, ())):
            results.append(build_case(args, scratch, evidence, mode, name, source, vb=vb, expected_errors=errors))
        for name, source in ENTRYPOINTS.items():
            results.append(build_case(args, scratch, evidence, mode, name, source, executable=True))
        results.append(build_case(args, scratch, evidence, mode, "caller-owned", ENTRYPOINTS["top-level"],
                                  executable=True, caller_configuration=True, disable_configuration=True))
        results.append(build_case(args, scratch, evidence, mode, "caller-missing", ENTRYPOINTS["top-level"],
                                  executable=True, disable_configuration=True, expected_errors=("CS0103",)))
    results.append(build_case(args, scratch, evidence, "Package", "ordinary-upstream", ENTRYPOINTS["top-level"],
                              executable=True, ordinary_generator=True))
    for language, entry in (("shared", "analyzers/dotnet/System.Windows.Forms.Analyzers.dll"),
                            ("csharp", "analyzers/dotnet/cs/System.Windows.Forms.Analyzers.CSharp.dll"),
                            ("vb", "analyzers/dotnet/vb/System.Windows.Forms.Analyzers.VisualBasic.dll")):
        sdk_directory = scratch / ("sdk-missing-" + language)
        with zipfile.ZipFile(package) as archive:
            archive.extractall(sdk_directory)
        (sdk_directory / entry).unlink()
        results.append(build_case(args, scratch, evidence, "Package", "missing-" + language,
                                  VB_NEGATIVE if language == "vb" else CS_NEGATIVE, vb=language == "vb",
                                  sdk_directory=sdk_directory, missing_analyzer=Path(entry).name))
    (evidence / "results.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
    print(f"Verified {len(payload['files'])} exact analyzer files, {len(rejected_payloads)} rejected archive controls "
          f"and {len(results)} compile contracts; evidence: {evidence}")


if __name__ == "__main__":
    main()
