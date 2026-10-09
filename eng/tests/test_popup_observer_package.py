#!/usr/bin/env python3
"""Offline verifier controls with synthetic files, never an SDK/UI qualification."""

import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("observer_package", ROOT / "eng/librewinforms-popup-observer-package.py")
GATE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(GATE)


class ObserverPackageContracts(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        self.stage, self.cache = self.root / "consumer", self.root / "packages"
        self.portable = self.stage / "Portable"
        self.output = self.portable / "bin/Release/net11.0"
        generated = self.portable / "obj/Release/net11.0"
        self.sources = [self.portable / "Program.cs", self.portable / "PortableNativeGeometryObserver.cs",
                        generated / "LibreWinForms.ApplicationConfiguration.g.cs",
                        generated / "LibreWinForms.ApplicationBootstrap.g.cs",
                        self.portable / "PopupInteractionStartup.cs"]
        for path, content in zip(self.sources, ["original shared source", "optional source",
                "internal static partial class ApplicationConfiguration { internal static void Initialize() {} }",
                "LibreWinForms.ProGPU.ProGpuPlatform.Register()", "shared argument source"]):
            self.write(path, content)
        self.write(self.stage / "Microsoft/Program.cs", self.sources[0].read_text())
        self.write(self.stage / "Microsoft/PopupInteractionStartup.cs", self.sources[4].read_text())
        GATE.isolate_consumer(self.stage)
        self.preparation = dict(sourceSha256=GATE.PREPARE.sha256(self.sources[0]), packages=[],
                                startupSourceSha256=GATE.PREPARE.sha256(self.sources[4]),
                                nativeGeometry=dict(enabled=True, sourceSha256=GATE.PREPARE.sha256(self.sources[1])))
        for name in ["LibreWinForms.Sdk", "LibreWinForms.System.Windows.Forms", "LibreWinForms.ProGPU"]:
            archive = self.root / f"{name}.1.2.3.nupkg"
            self.write(archive, f"synthetic {name}")
            self.write(self.cache / name.lower() / "1.2.3" / f"{name.lower()}.1.2.3.nupkg", archive.read_text())
            self.preparation["packages"].append(dict(id=name, version="1.2.3", path=str(archive), sha256=GATE.PREPARE.sha256(archive)))
        self.write(self.output / "PopupInteractionApp.dll", "synthetic consumer")
        self.write(self.output / "PopupInteractionApp.deps.json", json.dumps(dict(libraries={
            "LibreWinForms.System.Windows.Forms/1.2.3": {}, "LibreWinForms.ProGPU/1.2.3": {},
            "ProGPU.System.Drawing.Common/1.2.3": {}})))
        self.payloads = {}
        for name in ["System.Windows.Forms.dll", "LibreWinForms.ProGPU.dll", "System.Drawing.Common.dll"]:
            self.write(self.output / name, f"synthetic payload {name}")
            self.payloads[name] = GATE.PREPARE.sha256(self.output / name)
        self.result = dict(Properties=dict(LibreWinFormsReferenceMode="Package", PopupNativeGeometryDiagnostics="true",
                                          TargetPath=str(self.output / "PopupInteractionApp.dll")),
                           Items=dict(Compile=[dict(FullPath=str(path)) for path in self.sources], ProjectReference=[]),
                           TargetResults=dict(Build=dict(Result="Success")))

    @staticmethod
    def write(path, text):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)

    def verify(self, result=None):
        return GATE.verify(self.stage, self.cache, self.preparation, self.result if result is None else result,
                           "Release", "1.2.3", self.payloads)

    def test_complete_synthetic_receipt_reports_all_five_compiler_inputs(self):
        proof = self.verify()
        self.assertEqual(len(proof["compiledInputs"]), 5)
        self.assertEqual(proof["payloadSha256"], self.payloads)

    def test_all_three_boundaries_stop_hostile_ancestor_search_without_source_changes(self):
        original = {path: path.read_bytes() for path in self.sources}
        for name in GATE.ISOLATION_FILES:
            self.write(self.root / name, '<Project><Import Project="hostile-ancestor" /></Project>')
            self.assertEqual((self.stage / name).read_text(), "<Project />\n")
        self.assertEqual(set(self.verify()["isolationSha256"]), set(GATE.ISOLATION_FILES))
        self.assertEqual({path: path.read_bytes() for path in self.sources}, original)

    def test_changed_or_missing_isolation_boundary_fails(self):
        for name in GATE.ISOLATION_FILES:
            with self.subTest(name=name):
                path = self.stage / name
                path.write_text('<Project><Import Project="hostile" /></Project>')
                with self.assertRaisesRegex(ValueError, "isolation boundary"):
                    self.verify()
                path.unlink()
                with self.assertRaises(FileNotFoundError):
                    self.verify()
                path.write_text(GATE.ISOLATION_CONTENT)

    def test_isolation_never_overwrites_existing_caller_file(self):
        sentinel = self.stage / GATE.ISOLATION_FILES[0]
        sentinel.write_text("caller-owned")
        with self.assertRaises(FileExistsError):
            GATE.isolate_consumer(self.stage)
        self.assertEqual(sentinel.read_text(), "caller-owned")

    def test_omitted_or_duplicated_required_compile_input_fails(self):
        for index in range(5):
            for duplicate in (False, True):
                with self.subTest(index=index, duplicate=duplicate):
                    result = copy.deepcopy(self.result)
                    if duplicate:
                        result["Items"]["Compile"].append(result["Items"]["Compile"][index])
                    else:
                        result["Items"]["Compile"].pop(index)
                    with self.assertRaisesRegex(ValueError, "Compile input"):
                        self.verify(result)

    def test_project_mode_or_disabled_observer_fails(self):
        for name, value in [("LibreWinFormsReferenceMode", "Project"), ("PopupNativeGeometryDiagnostics", "false")]:
            result = copy.deepcopy(self.result)
            result["Properties"][name] = value
            with self.assertRaisesRegex(ValueError, "Package mode"):
                self.verify(result)

    def test_failed_build_or_project_reference_fails(self):
        for reference in (False, True):
            result = copy.deepcopy(self.result)
            if reference:
                result["Items"]["ProjectReference"] = [dict(Identity="source.csproj")]
            else:
                result["TargetResults"]["Build"]["Result"] = "Failure"
            with self.assertRaisesRegex(ValueError, "Package-only Build"):
                self.verify(result)

    def test_changed_shared_source_or_observer_fails(self):
        for path in [self.sources[0], self.stage / "Microsoft/Program.cs", self.sources[1],
                     self.sources[4], self.stage / "Microsoft/PopupInteractionStartup.cs"]:
            with self.subTest(path=path):
                original = path.read_text()
                path.write_text("changed")
                with self.assertRaises(ValueError):
                    self.verify()
                path.write_text(original)

    def test_observer_in_microsoft_consumer_fails(self):
        self.write(self.stage / "Microsoft/PortableNativeGeometryObserver.cs", "unexpected")
        with self.assertRaisesRegex(ValueError, "Microsoft consumer"):
            self.verify()

    def test_missing_sdk_configuration_or_compatibility_bootstrap_fails(self):
        for path, content in [(self.sources[2], "caller configuration"),
                              (self.sources[3], "WindowsFormsHost.EnableWindowsFormsInterop()")]:
            original = path.read_text()
            path.write_text(content)
            with self.assertRaises(ValueError):
                self.verify()
            path.write_text(original)

    def test_producer_or_restored_archive_replacement_fails(self):
        archive = Path(self.preparation["packages"][0]["path"])
        restored = self.cache / "librewinforms.sdk/1.2.3/librewinforms.sdk.1.2.3.nupkg"
        for path in (archive, restored):
            original = path.read_text()
            path.write_text("replacement")
            with self.assertRaisesRegex(ValueError, "archive"):
                self.verify()
            path.write_text(original)

    def test_wrong_dependency_or_official_drawing_fails(self):
        path = self.output / "PopupInteractionApp.deps.json"
        original = path.read_text()
        for wrong in ("missing", "official"):
            data = json.loads(original)
            if wrong == "missing":
                data["libraries"].pop("LibreWinForms.ProGPU/1.2.3")
            else:
                data["libraries"]["System.Drawing.Common/11.0.0"] = {}
            path.write_text(json.dumps(data))
            with self.assertRaises(ValueError):
                self.verify()

    def test_wrong_producer_output_fails(self):
        self.write(self.output / "LibreWinForms.ProGPU.dll", "other payload")
        with self.assertRaisesRegex(ValueError, "producer payload"):
            self.verify()

    def test_command_nonzero_is_not_qualified_or_retried(self):
        with mock.patch.object(GATE.subprocess, "run", return_value=subprocess.CompletedProcess([], 7)) as run:
            with self.assertRaises(GATE.CommandFailure) as error:
                GATE.run(["explicit-dotnet", "msbuild"], self.root, {}, self.root, "failed")
        self.assertEqual(error.exception.code, 7)
        self.assertEqual(run.call_count, 1)
        self.assertEqual(run.call_args.kwargs["timeout"], 300)
        self.assertTrue((self.root / "failed.command.json").is_file())
        self.assertTrue((self.root / "failed.stderr.log").is_file())

    def test_command_timeout_is_not_retried(self):
        with mock.patch.object(GATE.subprocess, "run", side_effect=subprocess.TimeoutExpired([], 300)) as run:
            with self.assertRaises(subprocess.TimeoutExpired):
                GATE.run(["explicit-dotnet", "restore"], self.root, {}, self.root, "timeout")
        self.assertEqual(run.call_count, 1)

    def test_mandatory_pack_hook_and_always_evidence_preserve_existing_gates(self):
        pack = (ROOT / "eng/librewinforms-pack-source-first.sh").read_text()
        self.assertLess(pack.index('"${dotnet}" run \\\n  --project "${sdk_package_smoke_project}"'), pack.index("librewinforms-popup-observer-package.py"))
        self.assertLess(pack.index("librewinforms-popup-observer-package.py"), pack.index("test-drawing-runtime-identity.py"))
        for gate in ["test-application-isolation.py", "librewinforms-analyzer-contract.py"]:
            self.assertIn(gate, pack)
        workflow = (ROOT / ".github/workflows/librewinforms-ci.yml").read_text()
        self.assertIn("name: Retain popup observer compile-only package evidence\n        if: always()", workflow)
        packages = workflow.split("  packages:\n", 1)[1]
        self.assertIn("name: Retain full Package lane popup observer compile evidence\n        if: always()", packages)
        self.assertIn("name: popup-observer-full-package-build\n", packages)
        for retained in ("artifacts/popup-observer-package/*/*.json", "artifacts/popup-observer-package/*/*.log"):
            self.assertIn(retained, packages)
        self.assertIn("name: popup-observer-package-build\n", workflow.split("  packages:\n", 1)[0])


if __name__ == "__main__":
    unittest.main()
