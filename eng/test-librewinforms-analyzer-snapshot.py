#!/usr/bin/env python3
"""Transport-only snapshot tests; real compiler/assembly controls remain separate."""

import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
import zipfile


REPO = Path(__file__).resolve().parent.parent
SPEC = importlib.util.spec_from_file_location("analyzer_contract", REPO / "eng/librewinforms-analyzer-contract.py")
CONTRACT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CONTRACT)


class ProducerSnapshotTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="analyzer-snapshot-test.")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.repo = self.root / "source"
        self.repo.mkdir()
        # Copy only resource inventory names, not built assemblies. These bytes
        # intentionally are not DLLs and qualify only the snapshot transport.
        for path in (REPO / "src").glob("System.Windows.Forms.Analyzers*/src/Resources/xlf/SR.*.xlf"):
            fixture = self.repo / path.relative_to(REPO)
            fixture.parent.mkdir(parents=True, exist_ok=True)
            fixture.write_text("inventory fixture\n", encoding="utf-8")
        (self.repo / ".gitignore").write_text("artifacts/\n", encoding="utf-8")
        self.git(self.repo, "init", "--quiet")
        self.commit(self.repo)
        self.files = CONTRACT.producer_files(self.repo, "Release")
        self.assertEqual(42, len(self.files))
        for entry, path in self.files.items():
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"transport-only producer fixture: " + entry.encode())
        self.package = self.root / "LibreWinForms.Sdk.fixture.nupkg"
        self.write_package()
        self.snapshot = self.root / "snapshot"
        self.snapshot.mkdir()
        self.digest = CONTRACT.capture_producer(self.repo, self.package, "Release", self.snapshot)

    def git(self, repo, *arguments):
        return subprocess.check_output(["git", "-c", "core.hooksPath=/dev/null", *arguments],
                                       cwd=repo, text=True, stderr=subprocess.STDOUT).strip()

    def commit(self, repo):
        self.git(repo, "add", ".")
        self.git(repo, "-c", "user.name=Snapshot fixture", "-c", "user.email=fixture@example.invalid",
                 "commit", "--quiet", "--no-gpg-sign", "-m", "Snapshot fixture")

    def write_package(self, extra=False):
        with zipfile.ZipFile(self.package, "w") as archive:
            for entry, path in self.files.items():
                archive.writestr(entry, path.read_bytes())
            archive.writestr("LibreWinForms.Sdk.nuspec", "<package><metadata /></package>")
            if extra:
                archive.writestr("Sdk/Unrelated.txt", "Changed original archive")

    def verify(self, snapshot=None, digest=None, engine=None):
        return CONTRACT.verify_snapshot(self.repo, self.package, "Release", snapshot or self.snapshot,
                                        digest or self.digest, engine)

    def test_original_generation_survives_legitimate_consumer_rebuild(self):
        for original in self.files.values():
            original.write_bytes(b"different consumer generation")
        self.assertEqual(42, len(self.verify()[0]["files"]))
        with self.assertRaisesRegex(AssertionError, "not the exact source assembly"):
            CONTRACT.verify_payload(self.repo, self.package, "Release")

    def test_original_generation_is_verified_before_and_after_consumer(self):
        self.verify()
        target = self.snapshot / next(iter(self.files))
        target.chmod(0o644)
        target.write_bytes(b"changed after consumers")
        target.chmod(0o444)
        with self.assertRaisesRegex(AssertionError, "not the exact source assembly"):
            self.verify()

    def test_snapshot_missing_extra_symlink_writable_and_directory_rejected(self):
        for fault in ("missing", "extra", "symlink", "writable", "directory"):
            with self.subTest(fault=fault):
                mutant = self.root / fault
                shutil.copytree(self.snapshot, mutant)
                target = mutant / next(iter(self.files))
                if fault == "missing":
                    target.unlink()
                elif fault == "extra":
                    extra = mutant / "unexpected.dll"
                    extra.write_bytes(b"extra")
                    extra.chmod(0o444)
                elif fault == "symlink":
                    target.unlink()
                    target.symlink_to(self.snapshot / next(iter(self.files)))
                elif fault == "writable":
                    target.chmod(0o644)
                else:
                    (mutant / "unexpected-directory").mkdir()
                with self.assertRaises(AssertionError):
                    self.verify(mutant)

    def test_outer_digest_rejects_rewritten_manifest_even_with_forged_file_hash(self):
        manifest = self.snapshot / "manifest.json"
        data = json.loads(manifest.read_text(encoding="utf-8"))
        data["files"][next(iter(self.files))] = "0" * 64
        manifest.chmod(0o644)
        manifest.write_text(json.dumps(data), encoding="utf-8")
        manifest.chmod(0o444)
        with self.assertRaisesRegex(AssertionError, "capture-phase SHA256"):
            self.verify()
        # Even an independently supplied digest of a forged manifest does not
        # bypass its source/package/actual-byte fields.
        with self.assertRaisesRegex(AssertionError, "captured bytes changed"):
            self.verify(digest=CONTRACT.sha256(manifest.read_bytes()))

    def test_original_archive_digest_includes_non_analyzer_entries(self):
        self.write_package(extra=True)
        with self.assertRaisesRegex(AssertionError, "package or captured bytes changed"):
            self.verify()

    def test_source_revision_and_dirty_state_rejected(self):
        (self.repo / "source-change.txt").write_text("changed source\n", encoding="utf-8")
        with self.assertRaisesRegex(AssertionError, "unchanged clean source"):
            self.verify()
        self.commit(self.repo)
        with self.assertRaisesRegex(AssertionError, "source, package or captured bytes changed"):
            self.verify()

    def test_engine_identity_is_bound_even_when_outside_source(self):
        engine = self.root / "engine"
        engine.mkdir()
        self.git(engine, "init", "--quiet")
        (engine / "engine.txt").write_text("pinned engine\n", encoding="utf-8")
        self.commit(engine)
        snapshot = self.root / "with-engine"
        snapshot.mkdir()
        digest = CONTRACT.capture_producer(self.repo, self.package, "Release", snapshot, engine)
        self.verify(snapshot, digest, engine)
        (engine / "engine.txt").write_text("different engine\n", encoding="utf-8")
        self.commit(engine)
        with self.assertRaisesRegex(AssertionError, "source, package or captured bytes changed"):
            self.verify(snapshot, digest, engine)

    def test_checked_in_submodule_generation_is_bound(self):
        origin = self.root / "engine-origin"
        origin.mkdir()
        self.git(origin, "init", "--quiet")
        (origin / "engine.txt").write_text("pinned engine\n", encoding="utf-8")
        self.commit(origin)
        self.git(self.repo, "-c", "protocol.file.allow=always", "submodule", "add", "--quiet",
                 str(origin), "external/ProGPU")
        self.commit(self.repo)
        snapshot = self.root / "with-submodule"
        snapshot.mkdir()
        digest = CONTRACT.capture_producer(self.repo, self.package, "Release", snapshot)
        self.verify(snapshot, digest)
        manifest = json.loads((snapshot / "manifest.json").read_bytes())
        self.assertIn("external/ProGPU", manifest["sourceIdentity"]["forms"]["submodules"][0])
        engine = self.repo / "external/ProGPU"
        (engine / "engine.txt").write_text("changed actual submodule source\n", encoding="utf-8")
        with self.assertRaisesRegex(AssertionError, "unchanged clean source"):
            self.verify(snapshot, digest)
        self.commit(engine)
        with self.assertRaisesRegex(AssertionError, "unchanged clean source"):
            self.verify(snapshot, digest)

    def test_configuration_and_original_package_path_are_bound(self):
        with self.assertRaisesRegex(AssertionError, "source, package or captured bytes changed"):
            CONTRACT.verify_snapshot(self.repo, self.package, "Debug", self.snapshot, self.digest)
        copied = self.root / "same-bytes-different-producer.nupkg"
        shutil.copy2(self.package, copied)
        with self.assertRaisesRegex(AssertionError, "source, package or captured bytes changed"):
            CONTRACT.verify_snapshot(self.repo, copied, "Release", self.snapshot, self.digest)

    def test_archive_mutants_still_reach_exact_entry_comparer_after_rebuild(self):
        _, captured = self.verify()
        for original in self.files.values():
            original.write_bytes(b"different consumer generation")
        evidence = self.root / "archive-mutants"
        evidence.mkdir()
        results = CONTRACT.verify_rejected_payloads(SimpleNamespace(repo_root=self.repo, configuration="Release"),
                                                    self.package, evidence, captured)
        self.assertEqual(["missing", "changed", "extra", "duplicate"], [item["fault"] for item in results])
        self.assertIn("not the exact source assembly", results[1]["rejection"])
        self.assertTrue(all("SHA256" not in item["rejection"] for item in results))


if __name__ == "__main__":
    unittest.main(verbosity=2)
