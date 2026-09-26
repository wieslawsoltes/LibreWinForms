#!/usr/bin/env python3
"""Real Microsoft-first host / canonical installed-SDK child contracts; no shared cache mutation."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile


def run(command, cwd, environment, log, timeout=180):
    with log.open("xb") as output:
        process = subprocess.Popen(command, cwd=cwd, env=environment, stdout=output,
                                   stderr=subprocess.STDOUT, start_new_session=os.name != "nt")
        try:
            code = process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            # This test runner explicitly owns its timeout termination policy.
            if os.name == "nt":
                subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                               stdout=output, stderr=subprocess.STDOUT, timeout=10, check=False)
            else:
                os.killpg(process.pid, signal.SIGKILL)
            process.wait(timeout=10)
            raise RuntimeError(f"Timed out; retained log: {log}") from None
    if code:
        raise RuntimeError(f"Command exited {code}; retained log: {log}")


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--package-feed", type=Path, required=True)
    parser.add_argument("--package-version", default="0.1.0-source-first")
    parser.add_argument("--sdk-version", default="0.1.0-source-first")
    parser.add_argument("--progpu-version", default="0.1.0-source-first")
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[1]
    dotnet = str(Path(shutil.which(args.dotnet) or args.dotnet).resolve(strict=True))
    feed = args.package_feed.resolve(strict=True)
    artifact_parent = repo / "artifacts/application-isolation"
    artifact_parent.mkdir(parents=True, exist_ok=True)
    root = Path(tempfile.mkdtemp(prefix="package-contract-", dir=artifact_parent))
    print(f"Retained application isolation evidence: {root}", flush=True)
    environment = dict(os.environ, NUGET_PACKAGES=str(root / "packages"),
                       NUGET_HTTP_CACHE_PATH=str(root / "http-cache"),
                       TMPDIR=str(root), TMP=str(root), TEMP=str(root),
                       DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1",
                       DOTNET_ROLL_FORWARD="Major", DOTNET_ROLL_FORWARD_TO_PRERELEASE="1")
    package = feed / f"LibreWinForms.ApplicationIsolation.{args.package_version}.nupkg"
    with zipfile.ZipFile(package) as archive:
        isolation_bytes = archive.read("lib/net10.0/LibreWinForms.ApplicationIsolation.dll")
        nuspec = ET.fromstring(archive.read("LibreWinForms.ApplicationIsolation.nuspec"))
        if any(element.tag.rsplit("}", 1)[-1] == "dependency" for element in nuspec.iter()):
            raise RuntimeError("The BCL-only isolation package gained a package dependency.")
    source = repo / "samples/LibreWinForms.IsolatedApplication"
    for kind in ("sample", "contracts"):
        destination = root / kind
        shutil.copytree(source, destination)
        (destination / "global.json").write_text(json.dumps({"msbuild-sdks": {"LibreWinForms.Sdk": args.sdk_version}}))
        config = ET.parse(repo / "NuGet.config")
        sources = config.getroot().find("packageSources")
        if sources is None:
            raise RuntimeError("Repository package sources are missing.")
        ET.SubElement(sources, "add", key="ApplicationIsolationSource", value=str(feed))
        config.write(destination / "NuGet.config", encoding="utf-8", xml_declaration=True)
        if kind == "contracts":
            shutil.copyfile(repo / "eng/fixtures/application-isolation/Host.cs", destination / "Host/Program.cs")
            shutil.copyfile(repo / "eng/fixtures/application-isolation/Child.cs", destination / "PortableApp/Program.cs")
        for app in ("Host", "PortableApp"):
            run([dotnet, "build", str(destination / app / f"{app}.csproj"), "-c", "Release", "-m:1",
                 "-nodeReuse:false", "-p:UseSharedCompilation=false", f"-p:IsolationPackageVersion={args.package_version}"],
                destination, environment, root / f"{kind}-{app}-build.log")

    host_output = root / "contracts/Host/bin/Release/net11.0"
    child_output = root / "contracts/PortableApp/bin/Release/net11.0"
    with zipfile.ZipFile(feed / f"ProGPU.System.Drawing.Common.{args.progpu_version}.nupkg") as archive:
        canonical_drawing = archive.read("lib/net10.0/System.Drawing.Common.dll")
    if (child_output / "System.Drawing.Common.dll").read_bytes() != canonical_drawing:
        raise RuntimeError("Child Drawing is not the exact just-produced canonical package payload.")
    microsoft = root / "packages/system.drawing.common/10.0.12/lib/net10.0/System.Drawing.Common.dll"
    if (host_output / "System.Drawing.Common.dll").read_bytes() != microsoft.read_bytes():
        raise RuntimeError("Host Drawing is not the selected Microsoft package payload.")
    for output in (host_output, child_output):
        if (output / "LibreWinForms.ApplicationIsolation.dll").read_bytes() != isolation_bytes:
            raise RuntimeError("Consumer isolation DLL differs from its actual package.")
    host_libraries = json.loads((host_output / "Host.deps.json").read_text())["libraries"]
    child_libraries = json.loads((child_output / "PortableApp.deps.json").read_text())["libraries"]
    if any(name.startswith(("ProGPU.", "LibreWinForms.System.Windows.Forms/", "LibreWinForms.ProGPU/")) for name in host_libraries):
        raise RuntimeError("Canonical UI/Drawing dependencies leaked into the Microsoft host.")
    if any(name.startswith("System.Drawing.Common/") for name in child_libraries):
        raise RuntimeError("Microsoft Drawing leaked into the canonical child package closure.")
    run([dotnet, str(host_output / "Host.dll"), str(child_output / "PortableApp.dll"), dotnet],
        root, environment, root / "microsoft-first-contracts.log", timeout=120)
    receipt = {"success": True, "hostDrawingSha256": sha256(microsoft.read_bytes()),
               "childDrawingSha256": sha256(canonical_drawing), "isolationSha256": sha256(isolation_bytes),
               "contracts": 15, "guiExecuted": False, "sampleCompiled": True}
    with (root / "receipt.json").open("x", encoding="utf-8") as stream:
        json.dump(receipt, stream, indent=2)
    print(json.dumps(receipt), flush=True)


if __name__ == "__main__":
    main()
