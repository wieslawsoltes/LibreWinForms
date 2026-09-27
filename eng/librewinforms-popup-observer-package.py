#!/usr/bin/env python3
"""Compile the real optional popup observer from fresh SDK Package inputs; never launch it."""

import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("popup_prepare", ROOT / "eng/librewinforms-prepare-popup-desktop.py")
PREPARE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PREPARE)
ISOLATION_FILES = ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props")
ISOLATION_CONTENT = "<Project />\n"


def isolate_consumer(stage):
    # Evidence lives inside the repository: stop standard upward MSBuild searches
    # here, without importing Arcade or changing the actual package SDK itself.
    for name in ISOLATION_FILES:
        with (stage / name).open("x") as stream:
            stream.write(ISOLATION_CONTENT)


class CommandFailure(RuntimeError):
    def __init__(self, command, code):
        super().__init__(f"{command} failed with exit {code}; retained command output is authoritative")
        self.code = code if code > 0 else 128 - code


def run(command, cwd, environment, evidence, name):
    (evidence / f"{name}.command.json").write_text(json.dumps(command, indent=2))
    with (evidence / f"{name}.stdout.log").open("xb") as stdout, (evidence / f"{name}.stderr.log").open("xb") as stderr:
        result = subprocess.run(command, cwd=cwd, env=environment, stdout=stdout, stderr=stderr, timeout=300)
    if result.returncode:
        raise CommandFailure(name, result.returncode)
    return (evidence / f"{name}.stdout.log").read_text()


def verify(stage, cache, preparation, result, configuration, drawing_version, expected_hashes):
    for name in ISOLATION_FILES:
        if (stage / name).read_text() != ISOLATION_CONTENT:
            raise ValueError(f"Consumer isolation boundary changed: {name}")
    portable = stage / "Portable"
    properties = result["Properties"]
    if properties["LibreWinFormsReferenceMode"] != "Package" or properties["PopupNativeGeometryDiagnostics"] != "true":
        raise ValueError("The observer must compile unconditionally in Package mode")
    if result["TargetResults"]["Build"]["Result"] != "Success" or result["Items"]["ProjectReference"]:
        raise ValueError("Expected a successful Package-only Build, without source project references")
    sources = [Path(item["FullPath"]).resolve(strict=True) for item in result["Items"]["Compile"]]
    generated = portable / "obj" / configuration / "net11.0"
    required = [portable / "Program.cs", portable / "PortableNativeGeometryObserver.cs",
                generated / "LibreWinForms.ApplicationConfiguration.g.cs",
                generated / "LibreWinForms.ApplicationBootstrap.g.cs"]
    for path in required:
        if sources.count(path.resolve(strict=True)) != 1:
            raise ValueError(f"Missing or repeated actual Compile input: {path.name}")
    if PREPARE.sha256(portable / "Program.cs") != preparation["sourceSha256"] or PREPARE.sha256(stage / "Microsoft/Program.cs") != preparation["sourceSha256"]:
        raise ValueError("The shared Microsoft/Portable Program.cs bytes changed")
    observer = preparation["nativeGeometry"]
    if observer["enabled"] is not True or PREPARE.sha256(portable / "PortableNativeGeometryObserver.cs") != observer["sourceSha256"]:
        raise ValueError("The optional observer bytes changed or were not enabled")
    if (stage / "Microsoft/PortableNativeGeometryObserver.cs").exists():
        raise ValueError("The optional observer must not enter the Microsoft consumer")
    initialization = required[2].read_text()
    bootstrap = required[3].read_text()
    if "internal static partial class ApplicationConfiguration" not in initialization or "internal static void Initialize()" not in initialization:
        raise ValueError("Missing actual SDK-generated ApplicationConfiguration.Initialize")
    if "LibreWinForms.ProGPU.ProGpuPlatform.Register()" not in bootstrap or "WindowsFormsHost.EnableWindowsFormsInterop()" in bootstrap:
        raise ValueError("Missing canonical SDK bootstrap or unexpected compatibility bootstrap")
    output = portable / "bin" / configuration / "net11.0"
    assembly = output / "PopupInteractionApp.dll"
    if Path(properties["TargetPath"]).resolve(strict=True) != assembly.resolve(strict=True) or not assembly.stat().st_size:
        raise ValueError("Build TargetPath is not the fresh actual popup consumer")
    dependencies = json.loads((output / "PopupInteractionApp.deps.json").read_text())["libraries"]
    for package in preparation["packages"]:
        if PREPARE.sha256(Path(package["path"])) != package["sha256"]:
            raise ValueError("A producer archive changed during compilation")
        package_id, version = package["id"].lower(), package["version"].lower()
        restored = cache / package_id / version / f"{package_id}.{version}.nupkg"
        if PREPARE.sha256(restored) != package["sha256"]:
            raise ValueError("Restored package differs from the just-produced archive")
        if package["id"] != "LibreWinForms.Sdk" and f"{package['id']}/{package['version']}" not in dependencies:
            raise ValueError("Output dependency version differs from the exact producer")
    if f"ProGPU.System.Drawing.Common/{drawing_version}" not in dependencies or any(key.startswith("System.Drawing.Common/") for key in dependencies):
        raise ValueError("Output does not use the exact ProGPU drawing package")
    payloads = {}
    for name, expected in expected_hashes.items():
        actual = PREPARE.sha256(output / name)
        if actual != expected:
            raise ValueError(f"Output differs from the compiled producer payload: {name}")
        payloads[name] = actual
    return dict(compiledInputs={str(path.relative_to(portable)): PREPARE.sha256(path) for path in required},
                isolationSha256={name: PREPARE.sha256(stage / name) for name in ISOLATION_FILES},
                applicationSha256=PREPARE.sha256(assembly), payloadSha256=payloads)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--feed", type=Path, required=True)
    parser.add_argument("--configuration", default="Release")
    for name in ("sdk-version", "canonical-version", "backend-version", "drawing-version",
                 "forms-sha256", "backend-sha256", "drawing-sha256"):
        parser.add_argument("--" + name, required=True)
    args = parser.parse_args()
    executable = shutil.which(args.dotnet)
    if not executable:
        parser.error("The explicit dotnet executable is unavailable")
    dotnet = str(Path(executable).resolve(strict=True))
    parent = ROOT / "artifacts/popup-observer-package"
    parent.mkdir(parents=True, exist_ok=True)
    evidence = Path(tempfile.mkdtemp(prefix="run-", dir=parent)).resolve()
    receipt = dict(schema="popup-observer-package-build-v1", compiled=False, guiExecuted=False, qualified=False)
    exit_code = 1
    try:
        # New stage, intermediate/output directories and package cache: no warm
        # Compile result or ambient package payload can satisfy this consumer.
        stage, cache = evidence / "consumer", evidence / "packages"
        environment = dict(os.environ, NUGET_PACKAGES=str(cache))
        sdk = run([dotnet, "--version"], ROOT, environment, evidence, "sdk").strip()
        preparation = PREPARE.prepare(stage, args.feed, args.sdk_version, args.canonical_version,
                                      args.backend_version, sdk, native_geometry=True)
        isolate_consumer(stage)
        (evidence / "preparation.json").write_text(json.dumps(preparation, indent=2))
        project = stage / "Portable/PopupInteractionApp.csproj"
        properties = [f"-p:Configuration={args.configuration}", "-p:MicrosoftNETCoreAppRefPackageVersion=",
                      "-nodeReuse:false"]
        run([dotnet, "restore", str(project), "--configfile", str(stage / "NuGet.config"),
             "--force", "--no-cache", *properties], stage, environment, evidence, "restore")
        # Official MSBuild post-target queries report the actual generated Compile
        # items after Build, unlike evaluation-only -getItem (which omits them).
        result = json.loads(run([dotnet, "msbuild", str(project), "-getTargetResult:Build",
                                 "-getItem:Compile,ProjectReference",
                                 "-getProperty:TargetPath,LibreWinFormsReferenceMode,PopupNativeGeometryDiagnostics",
                                 "-nologo", "-verbosity:quiet", "-consoleLoggerParameters:ErrorsOnly", *properties],
                                stage, environment, evidence, "build"))
        proof = verify(stage, cache, preparation, result, args.configuration, args.drawing_version,
                       {"System.Windows.Forms.dll": args.forms_sha256,
                        "LibreWinForms.ProGPU.dll": args.backend_sha256,
                        "System.Drawing.Common.dll": args.drawing_sha256})
        receipt.update(proof, compiled=True, sourceCommit=preparation["sourceCommit"],
                       sourceDirty=preparation["sourceDirty"], sdkVersion=sdk)
        exit_code = 0
    except (OSError, ValueError, KeyError, subprocess.SubprocessError, CommandFailure) as error:
        receipt["error"] = str(error)
        exit_code = error.code if isinstance(error, CommandFailure) else 1
    finally:
        receipt["exitCode"] = exit_code
        (evidence / "receipt.json").write_text(json.dumps(receipt, indent=2))
        print(f"Popup observer compile-only evidence (no UI qualification): {evidence}", flush=True)
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
