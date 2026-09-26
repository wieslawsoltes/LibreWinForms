#!/usr/bin/env python3
"""Exercise actual CI SDK packing with default and fresh explicit NuGet roots."""

import argparse
import importlib.util
import json
import os
from pathlib import Path
import signal
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--evidence-directory", type=Path, required=True)
    args = parser.parse_args()
    repo = args.repo_root.resolve()
    evidence = args.evidence_directory.resolve()
    evidence.mkdir(parents=True, exist_ok=False)
    spec = importlib.util.spec_from_file_location("analyzer_contract", repo / "eng/librewinforms-analyzer-contract.py")
    contract = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(contract)
    results = []
    with tempfile.TemporaryDirectory(prefix="librewinforms-sdk-analyzer-pack.") as temporary:
        scratch = Path(temporary)
        for mode in ("default", "explicit"):
            environment = os.environ.copy()
            environment.pop("NUGET_PACKAGES", None)
            if mode == "explicit":
                environment["NUGET_PACKAGES"] = str(scratch / "packages")
            output = evidence / mode
            output.mkdir()
            # Diagnostic-only packages are isolated from the producer feed and
            # never consumed or published as the canonical runtime closure.
            version = "0.0.0-analyzer-pack-contract"
            command = [args.dotnet, "pack", str(repo / "src/LibreWinForms.Sdk/LibreWinForms.Sdk.csproj"),
                       "--configuration", args.configuration, "--nologo", "--disable-build-servers",
                       "--output", str(output), "-p:ContinuousIntegrationBuild=true",
                       "-p:Version=" + version, "-p:PackageVersion=" + version]
            log = output / "pack.log"
            with log.open("w", encoding="utf-8") as stream:
                process = subprocess.Popen(command, cwd=repo, env=environment, start_new_session=True,
                                           stdout=stream, stderr=subprocess.STDOUT)
                try:
                    exit_code = process.wait(timeout=300)
                except BaseException:
                    try:
                        os.killpg(process.pid, signal.SIGKILL)
                    except ProcessLookupError:
                        pass
                    process.wait()
                    raise
            if exit_code != 0:
                raise AssertionError(f"CI SDK packing failed with {mode} NuGet root; see {log}")
            package = output / f"LibreWinForms.Sdk.{version}.nupkg"
            payload = contract.verify_payload(repo, package, args.configuration)
            payload.update({"mode": mode, "command": command,
                            "NUGET_PACKAGES": environment.get("NUGET_PACKAGES"), "exitCode": exit_code})
            (output / "payload.json").write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
            results.append(payload)
            print(f"PASS CI SDK pack: {mode} root, {len(payload['files'])} exact analyzer files", flush=True)
    (evidence / "results.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
