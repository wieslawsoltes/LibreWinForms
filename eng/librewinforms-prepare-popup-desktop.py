#!/usr/bin/env python3
"""Stage identical public Forms source into two isolated consumer projects; do not build."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "eng/PopupInteractionApp"


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def package(feed, name, version):
    if not re.fullmatch(r"[0-9A-Za-z][0-9A-Za-z.+-]*", version):
        raise ValueError("Explicit package versions must be simple NuGet versions")
    path = feed / f"{name}.{version}.nupkg"
    with zipfile.ZipFile(path) as archive:
        manifests = [item for item in archive.namelist() if item.endswith(".nuspec")]
        if len(manifests) != 1:
            raise ValueError("Expected exactly one package manifest")
        metadata = ET.fromstring(archive.read(manifests[0]))
        values = {element.tag.split("}")[-1]: element.text for element in metadata.iter()}
        if values.get("id") != name or values.get("version") != version:
            raise ValueError("Package ID/version differs from the requested producer payload")
    return dict(path=str(path), sha256=sha256(path), id=name, version=version)


def prepare(destination, feed, sdk_version, canonical_version, backend_version, dotnet_sdk, native_geometry=False):
    if destination.exists():
        raise ValueError("Destination must be new; no existing source or evidence is overwritten")
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+[0-9A-Za-z.+-]*", dotnet_sdk):
        raise ValueError("Supply an explicit .NET SDK version")
    feed = feed.resolve(strict=True)
    packages = [package(feed, "LibreWinForms.Sdk", sdk_version),
                package(feed, "LibreWinForms.System.Windows.Forms", canonical_version),
                package(feed, "LibreWinForms.ProGPU", backend_version)]
    commit = subprocess.run(["git", "-C", str(ROOT), "rev-parse", "HEAD"], check=True,
                            capture_output=True, text=True, timeout=10).stdout.strip()
    dirty = subprocess.run(["git", "-C", str(ROOT), "status", "--porcelain", "--untracked-files=all"], check=True,
                           capture_output=True, text=True, timeout=10).stdout.strip()
    destination.mkdir(parents=False)
    source_hash = sha256(SOURCE / "Program.cs")
    startup_hash = sha256(SOURCE / "PopupInteractionStartup.cs")
    observer_name = "PortableNativeGeometryObserver.cs"
    native_observer = dict(enabled=bool(native_geometry), sourcePath=None, sourceSha256=None,
                           environmentVariable="LIBREWINFORMS_POPUP_NATIVE_GEOMETRY")
    for mode in ("Microsoft", "Portable"):
        target = destination / mode
        target.mkdir()
        shutil.copyfile(SOURCE / "Program.cs", target / "Program.cs")
        shutil.copyfile(SOURCE / "PopupInteractionStartup.cs", target / "PopupInteractionStartup.cs")
        project = (SOURCE / f"{mode}.csproj").read_text()
        if mode == "Portable":
            project = project.replace("LibreWinForms.Sdk/0.1.0-source-first-sdk", f"LibreWinForms.Sdk/{sdk_version}")
            project = project.replace("<LibreWinFormsCanonicalPackageVersion>0.1.0-source-first<", f"<LibreWinFormsCanonicalPackageVersion>{canonical_version}<")
            project = project.replace("<LibreWinFormsProGpuBackendPackageVersion>0.1.0-source-first-backend<", f"<LibreWinFormsProGpuBackendPackageVersion>{backend_version}<")
            if native_geometry:
                shutil.copyfile(SOURCE / observer_name, target / observer_name)
                observer_hash = sha256(SOURCE / observer_name)
                if sha256(target / observer_name) != observer_hash:
                    raise ValueError("Portable native geometry observer copy changed")
                project = project.replace("</PropertyGroup>", "  <PopupNativeGeometryDiagnostics>true</PopupNativeGeometryDiagnostics>\n  </PropertyGroup>", 1)
                native_observer.update(sourcePath=f"Portable/{observer_name}", sourceSha256=observer_hash)
        (target / "PopupInteractionApp.csproj").write_text(project)
        if sha256(target / "Program.cs") != source_hash:
            raise ValueError("Shared source copy changed")
        if sha256(target / "PopupInteractionStartup.cs") != startup_hash:
            raise ValueError("Shared startup source copy changed")
    config = ET.Element("configuration")
    sources = ET.SubElement(config, "packageSources")
    ET.SubElement(sources, "clear")
    ET.SubElement(sources, "add", key="qualified-private-feed", value=str(feed))
    ET.SubElement(sources, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
    ET.ElementTree(config).write(destination / "NuGet.config", encoding="unicode")
    (destination / "global.json").write_text(json.dumps(dict(sdk=dict(version=dotnet_sdk, allowPrerelease=True, rollForward="disable")), indent=2))
    receipt = dict(schema="popup-interaction-preparation-v1", sourceCommit=commit, sourceDirty=bool(dirty),
                   sourceSha256=source_hash, startupSourceSha256=startup_hash,
                   sdkVersion=dotnet_sdk, packages=packages, qualified=False,
                   nativeGeometry=native_observer,
                   limitation="Source staging only; producer admission, compilation, loaded identity and desktop phases remain separate.")
    (destination / "preparation.json").write_text(json.dumps(receipt, indent=2))
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--destination", type=Path, required=True)
    parser.add_argument("--feed", type=Path, required=True)
    parser.add_argument("--native-geometry", action="store_true",
                        help="Compile the optional portable-only native observer; requires the typed geometry package API and LIBREWINFORMS_POPUP_NATIVE_GEOMETRY=1 at runtime")
    for name in ("sdk-version", "canonical-version", "backend-version", "dotnet-sdk"):
        parser.add_argument("--" + name, required=True)
    args = parser.parse_args()
    prepare(args.destination.resolve(), args.feed, args.sdk_version, args.canonical_version, args.backend_version, args.dotnet_sdk, args.native_geometry)
    print(f"Prepared identical source (not built/qualified): {args.destination}")


if __name__ == "__main__":
    main()
