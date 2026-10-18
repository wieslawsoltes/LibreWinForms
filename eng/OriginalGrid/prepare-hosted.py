#!/usr/bin/env python3
"""Build the original grid pair from immutable source and whole-qualified packages; no UI input."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

HERE = Path(__file__).resolve().parent
REPOSITORY = "wieslawsoltes/LibreWinForms"
LEGACY_BUILD_JOBS = frozenset({
    "Package lane", "Canonical source and ProGPU submodule lane", "AppKit adapter lane",
    *(f"Automation thread locale ({os})" for os in ("ubuntu-24.04", "macos-15", "windows-latest")),
    *(f"Visible canonical package ({os})" for os in ("ubuntu-24.04", "macos-15", "windows-latest")),
})
NATIVE_THEME_BUILD_JOB = "Live Windows theme resource pixels"
SPEC = importlib.util.spec_from_file_location("grid_metadata", HERE.parent / "librewinforms-grid-designer-metadata.py")
METADATA = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(METADATA)


def require(value, message):
    if not value:
        raise ValueError(message)


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def save(path, value):
    with path.open("x", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2)


def api(path):
    request = urllib.request.Request("https://api.github.com/repos/" + REPOSITORY + "/" + path,
        headers={"Authorization": "Bearer " + os.environ["GH_TOKEN"],
                 "Accept": "application/vnd.github+json", "User-Agent": "LibreWinForms-original-grid"})
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)


def validate_producer(pin, build, docs, jobs, artifact):
    count = pin["expectedBuildJobs"]
    require(type(count) is int and count in (9, 10), "Original whole producer requires the complete nine- or ten-job Build layout")
    expected_jobs = LEGACY_BUILD_JOBS | ({NATIVE_THEME_BUILD_JOB} if count == 10 else set())
    for record, run, workflow in ((build, pin["build"], ".github/workflows/librewinforms-ci.yml"),
                                  (docs, pin["docs"], ".github/workflows/librewinforms-docs.yml")):
        require(record["id"] == run and record["head_sha"] == pin["head"]
                and record["path"] == workflow and record["status"] == "completed"
                and record["conclusion"] == "success", "Whole exact producer Build/Docs must succeed")
    require(jobs["total_count"] == len(jobs["jobs"]) == pin["expectedBuildJobs"]
            and {job.get("name") for job in jobs["jobs"]} == expected_jobs
            and all(job["head_sha"] == pin["head"] and job["status"] == "completed"
                    and job["conclusion"] == "success" for job in jobs["jobs"]),
            "Complete original producer job inventory must succeed")
    require(artifact["id"] == pin["artifact"] and artifact["name"] == pin["artifactName"]
            and artifact["size_in_bytes"] == pin["bytes"] and not artifact["expired"]
            and artifact["digest"] == "sha256:" + pin["sha256"]
            and artifact["workflow_run"]["id"] == pin["build"]
            and artifact["workflow_run"]["head_sha"] == pin["head"], "Wrong qualified package artifact")


def validate_feed(feed, pin):
    require(feed.is_dir() and not feed.is_symlink(), "A regular qualified package feed is required")
    require({path.name for path in feed.iterdir()} == {p["file"] for p in pin["packages"]},
            "Package closure differs from the exact producer")
    for package in pin["packages"]:
        path = feed / package["file"]
        require(Path(package["file"]).name == package["file"] and path.is_file() and not path.is_symlink()
                and path.stat().st_size == package["bytes"] and digest(path) == package["sha256"],
                "Qualified package bytes changed")
        with zipfile.ZipFile(path) as archive:
            names = [name for name in archive.namelist() if name.endswith(".nuspec")]
            require(len(names) == 1, "Exactly one package manifest is required")
            metadata = ET.fromstring(archive.read(names[0])).find("{*}metadata")
            repository = metadata.find("{*}repository")
            require(metadata.findtext("{*}id") == package["id"]
                    and metadata.findtext("{*}version") == package["version"]
                    and repository is not None and repository.attrib == package["repository"],
                    "Package source identity changed")


def project_bytes(source, mode):
    old = b"<TargetFramework>net6.0-windows</TargetFramework>"
    require(source.count(old) == 1, "Original project framework changed")
    target = b"net11.0-windows" if mode == "Microsoft" else b"net11.0"
    result = source.replace(old, b"<TargetFramework>" + target + b"</TargetFramework>")
    if mode == "Portable":
        old = b'Sdk="Microsoft.NET.Sdk.WindowsDesktop"'
        require(result.count(old) == 1, "Original SDK declaration changed")
        result = result.replace(old, b'Sdk="LibreWinForms.Sdk/0.1.0-source-first"')
    return result


def stage_source(checkout, destination, contract, read_blob=None, license_bytes=None):
    require(not destination.exists() and not destination.is_symlink(), "Prepared source must be fresh")
    if read_blob is None:
        head = subprocess.check_output(["git", "-C", str(checkout), "rev-parse", "HEAD"], text=True).strip()
        require(head == contract["commit"], "Original sample checkout changed")
        def read_blob(relative):
            return subprocess.check_output(["git", "-C", str(checkout), "show",
                                            head + ":" + contract["path"] + relative])
    if license_bytes is None:
        license_bytes = subprocess.check_output(["git", "-C", str(checkout), "show", contract["commit"] + ":LICENSE"])
    require(hashlib.sha256(license_bytes).hexdigest() == contract["licenseSha256"], "Original source license changed")
    originals = {}
    for entry in contract["files"]:
        relative = entry["path"]
        require(not Path(relative).is_absolute() and ".." not in Path(relative).parts
                and relative not in originals, "Invalid original source inventory")
        data = read_blob(relative)
        blob = b"blob " + str(len(data)).encode() + b"\0" + data
        require(len(data) == entry["bytes"] and hashlib.sha1(blob).hexdigest() == entry["gitBlobSha1"],
                "Original source bytes changed: " + relative)
        originals[relative] = data
    prepared = {}
    for mode in ("Microsoft", "Portable"):
        expected = {entry["path"]: entry["sha256"] for entry in contract["preparedFiles"][mode]}
        require(set(expected) == set(originals), "Prepared source inventory changed")
        for relative, original in originals.items():
            data = METADATA.annotate(relative, original) if relative in METADATA.RULES else original
            if relative == "CSWinFormDataGridView.csproj":
                data = project_bytes(data, mode)
            require(hashlib.sha256(data).hexdigest() == expected[relative],
                    "Prepared source differs from original retained pair: " + mode + "/" + relative)
            prepared[mode + "/" + relative] = data
    # Publish only after validating both complete original source batches.
    destination.mkdir()
    (destination / "samples-LICENSE").write_bytes(license_bytes)
    for relative, data in prepared.items():
        path = destination / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)


def stage_configuration(root, contract):
    observer = HERE / "OriginalGridObserver.cs"
    require(digest(observer) == contract["observerSha256"], "Original passive observer changed")
    shutil.copyfile(observer, root / observer.name)
    for mode in ("Microsoft", "Portable"):
        dependency = ' DependsOnTargets="_LibreWinFormsGenerateApplicationBootstrap"' if mode == "Portable" else ""
        (root / mode / "Directory.Build.targets").write_text(
            '<Project><Target Name="IncludePassiveGridReferenceObserver" BeforeTargets="CoreCompile"' + dependency +
            '><ItemGroup><Compile Include="../OriginalGridObserver.cs" Link="OriginalGridObserver.cs" />'
            '</ItemGroup></Target></Project>\n', encoding="utf-8")
    (root / "Directory.Build.props").write_text('<Project><PropertyGroup>\n'
        '  <LibreWinFormsReferenceMode>Package</LibreWinFormsReferenceMode>\n'
        '  <LibreWinFormsCanonicalPackageVersion>0.1.0-source-first</LibreWinFormsCanonicalPackageVersion>\n'
        '  <LibreWinFormsProGpuBackendPackageVersion>0.1.0-source-first</LibreWinFormsProGpuBackendPackageVersion>\n'
        '</PropertyGroup></Project>\n', encoding="utf-8")
    for name in ("Directory.Build.targets", "Directory.Packages.props"):
        (root / name).write_text("<Project />\n", encoding="utf-8")
    save(root / "global.json", {"sdk": {"version": contract["sdkVersion"], "allowPrerelease": True, "rollForward": "disable"}})
    config = ET.Element("configuration")
    sources = ET.SubElement(config, "packageSources")
    ET.SubElement(sources, "clear")
    ET.SubElement(sources, "add", key="ImmutableFormsClosure", value="./feed")
    ET.SubElement(sources, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
    mapping = ET.SubElement(config, "packageSourceMapping")
    local = ET.SubElement(mapping, "packageSource", key="ImmutableFormsClosure")
    for pattern in ("LibreWinForms.*", "ProGPU.*"):
        ET.SubElement(local, "package", pattern=pattern)
    ET.SubElement(ET.SubElement(mapping, "packageSource", key="nuget.org"), "package", pattern="*")
    ET.ElementTree(config).write(root / "NuGet.config", encoding="utf-8", xml_declaration=True)


def compiler_build_options(mode, compiler, pin):
    require(mode in ("Microsoft", "Portable") and compiler in ("Automatic", "Dxc"),
            "Unsupported original grid compiler configuration")
    if compiler == "Automatic" or mode == "Microsoft":
        return [], None
    packages = [item for item in pin["packages"] if item["id"] == "ProGPU.Backend.Dx12"]
    require(len(packages) == 1 and packages[0]["repository"].get("commit") == pin["progpuHead"],
            "DXC requires the exact whole-qualified producer package")
    return ["-p:LibreWinFormsDx12ShaderCompiler=Dxc", "-p:RuntimeIdentifier=win-arm64",
            "-p:LibreWinFormsProGpuPackageVersion=" + packages[0]["version"]], "win-arm64"


def verify_compiler_payload(output, feed, pin, rid):
    package = next(item for item in pin["packages"] if item["id"] == "ProGPU.Backend.Dx12")
    prefix = "buildTransitive/runtimes/" + rid + "/native/"
    with zipfile.ZipFile(feed / package["file"]) as archive:
        members = [name for name in archive.namelist() if name.startswith(prefix) and not name.endswith("/")]
        require(len(members) == len(set(members)) and len(members) <= 64,
                "Compiler payload inventory is invalid")
        names = {name.removeprefix(prefix) for name in members}
        require({"wgpu_native.dll", "dxcompiler.dll", "dxil.dll", "wgpu-native-build.json",
                 "dxc-compiler-package.json", "progpu-dx12-runtime.json", "LICENSE.MIT", "LICENSE.APACHE",
                 "LICENSE-MS.txt", "LICENSE-LLVM.txt", "LICENCE-MIT.txt", "Microsoft.Direct3D.DXC.package.xml"} <= names,
                "Compiler payload or notices are incomplete")
        require(all(Path(name).name == name for name in names), "Unexpected nested compiler payload")
        hashes = {}
        for member in members:
            name = member.removeprefix(prefix)
            target = output / name
            require(target.is_file() and not target.is_symlink(), "Missing compiler output: " + name)
            actual = digest(target)
            require(actual == hashlib.sha256(archive.read(member)).hexdigest(),
                    "Consumer substituted qualified compiler output: " + name)
            hashes[name] = actual
    return dict(requested="Dxc", runtimeIdentifier=rid, package=package["id"],
                packageSha256=package["sha256"], files=hashes)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sample-checkout", type=Path, required=True)
    parser.add_argument("--feed", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--dx12-compiler", choices=("Automatic", "Dxc"), default="Automatic")
    parser.add_argument("--producer-pin", type=Path, default=HERE / "qualified-producer.json")
    args = parser.parse_args()
    require(sys.platform == "win32" and os.environ.get("GITHUB_ACTIONS") == "true"
            and os.environ.get("RUNNER_ENVIRONMENT") == "github-hosted"
            and os.environ.get("GITHUB_REPOSITORY") == REPOSITORY,
            "Build preparation is restricted to this repository's disposable hosted Windows runner")
    pin = json.loads(args.producer_pin.read_text(encoding="utf-8-sig"))
    contract = json.loads((HERE / "source-contract.json").read_text())
    build, docs = (api("actions/runs/" + str(pin[key])) for key in ("build", "docs"))
    jobs = api("actions/runs/" + str(pin["build"]) + "/jobs?per_page=100")
    artifact = api("actions/artifacts/" + str(pin["artifact"]))
    validate_producer(pin, build, docs, jobs, artifact)
    feed = args.feed.resolve(strict=True)
    validate_feed(feed, pin)
    compiler_build_options("Portable", args.dx12_compiler, pin)
    root = args.output.resolve()
    stage_source(args.sample_checkout, root, contract)
    stage_configuration(root, contract)
    shutil.copytree(feed, root / "feed")
    save(root / "producer.json", dict(build=build, docs=docs, jobs=jobs, artifact=artifact))
    save(root / "selected-producer.json", pin)
    save(root / "preparation.json", dict(sourceCommit=contract["commit"], sourceHead=pin["head"],
        producerSuccess=True, files=contract["preparedFiles"], packages=pin["packages"],
        observerSha256=contract["observerSha256"], qualified=False))
    dotnet = Path(shutil.which(args.dotnet) or args.dotnet).resolve(strict=True)
    environment = {key: value for key, value in os.environ.items()
                   if not re.search(r"TOKEN|SECRET|PASSWORD|LICENSE|CREDENTIAL|API_KEY|ACCESS_KEY", key, re.I)}
    for key in ("DOTNET_STARTUP_HOOKS", "DOTNET_ADDITIONAL_DEPS", "DOTNET_SHARED_STORE", "DOTNET_ROLL_FORWARD"):
        environment.pop(key, None)
    environment.update(DOTNET_ROOT=str(dotnet.parent), NUGET_PACKAGES=str(root / "packages"),
                       DOTNET_CLI_TELEMETRY_OPTOUT="1")
    receipt = dict(sourceHead=pin["head"], sdk=str(dotnet), sdkSha256=digest(dotnet),
                   observerSha256=contract["observerSha256"], builds={}, guiExecuted=False, qualified=False)
    try:
        version = subprocess.check_output([str(dotnet), "--version"], cwd=root, env=environment,
                                          text=True, timeout=15).strip()
        require(version == contract["sdkVersion"], "Original grid SDK version changed")
        receipt["sdkVersion"] = version
        for mode, framework in (("Microsoft", "net11.0-windows"), ("Portable", "net11.0")):
            project = root / mode / "CSWinFormDataGridView.csproj"
            compiler_options, rid = compiler_build_options(mode, args.dx12_compiler, pin)
            with (root / (mode + "-build.log")).open("xb") as log:
                child = subprocess.Popen([str(dotnet), "build", str(project), "-c", "Release", "-m:1",
                    "-nodeReuse:false", "-p:UseSharedCompilation=false", "--configfile", str(root / "NuGet.config"),
                    *compiler_options],
                    cwd=root, env=environment, stdout=log, stderr=subprocess.STDOUT)
                try:
                    code = child.wait(timeout=300)
                except subprocess.TimeoutExpired:
                    subprocess.run(["taskkill", "/PID", str(child.pid), "/T", "/F"],
                                   stdout=log, stderr=subprocess.STDOUT, timeout=10, check=False)
                    child.wait(timeout=10)
                    raise
            entry = receipt["builds"][mode] = dict(exitCode=code)
            require(code == 0, mode + " original sample build failed; log retained")
            output = project.parent / "bin/Release" / framework
            if rid:
                output = output / rid
                entry["compiler"] = verify_compiler_payload(output, feed, pin, rid)
            entry["app"] = str(output / "CSWinFormDataGridView.exe")
            require(Path(entry["app"]).is_file(), "Original apphost missing")
            entry["outputs"] = [dict(path=str(p.relative_to(output)), sha256=digest(p))
                                for p in sorted(output.rglob("*")) if p.is_file()]
            if mode == "Portable":
                matches = []
                for package in pin["packages"]:
                    with zipfile.ZipFile(feed / package["file"]) as archive:
                        for member in archive.namelist():
                            if not member.startswith("lib/net10.0/") or not member.endswith(".dll"):
                                continue
                            target = output / member.removeprefix("lib/net10.0/")
                            if target.is_file():
                                require(digest(target) == hashlib.sha256(archive.read(member)).hexdigest(),
                                        "Consumer substituted a qualified package assembly")
                                matches.append(dict(package=package["id"], member=member, sha256=digest(target)))
                require({"lib/net10.0/System.Windows.Forms.dll", "lib/net10.0/LibreWinForms.ProGPU.dll",
                         "lib/net10.0/System.Drawing.Common.dll"} <= {item["member"] for item in matches},
                        "Original Forms/Drawing package matches are incomplete")
                entry["matchedPackages"] = matches
        print("Both unchanged original grid consumers built; no UI or input was executed.")
    except BaseException as error:
        receipt["error"] = repr(error)
        raise
    finally:
        save(root / "build-receipt.json", receipt)


if __name__ == "__main__":
    main()
