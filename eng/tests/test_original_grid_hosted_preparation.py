#!/usr/bin/env python3
"""Rejection controls for original-source package preparation; no desktop execution."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("hosted_grid", ROOT / "eng/OriginalGrid/prepare-hosted.py")
PREP = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PREP)
PIN = json.loads((ROOT / "eng/OriginalGrid/qualified-producer.json").read_text())


def producer():
    def run(key, workflow):
        return dict(id=PIN[key], head_sha=PIN["head"], path=".github/workflows/" + workflow,
                    status="completed", conclusion="success")
    jobs = dict(total_count=9, jobs=[dict(head_sha=PIN["head"], status="completed", conclusion="success") for _ in range(9)])
    artifact = dict(id=PIN["artifact"], name=PIN["artifactName"], size_in_bytes=PIN["bytes"], expired=False,
                    digest="sha256:" + PIN["sha256"], workflow_run=dict(id=PIN["build"], head_sha=PIN["head"]))
    return [run("build", "librewinforms-ci.yml"), run("docs", "librewinforms-docs.yml"), jobs, artifact]


class HostedPreparationTests(unittest.TestCase):
    def test_complete_exact_producer(self):
        PREP.validate_producer(PIN, *producer())

    def test_other_head_pending_failed_wrong_workflow_and_partial_jobs_rejected(self):
        for index, field, value in [(0, "head_sha", "0" * 40), (0, "status", "in_progress"),
                                    (0, "conclusion", "cancelled"), (1, "conclusion", "failure"),
                                    (1, "path", ".github/workflows/other.yml"), (2, "total_count", 10)]:
            with self.subTest(index=index, field=field):
                records = producer()
                records[index][field] = value
                with self.assertRaises(ValueError):
                    PREP.validate_producer(PIN, *records)
        for result in ("skipped", "failure", None):
            records = producer()
            records[2]["jobs"][0]["conclusion"] = result
            with self.assertRaises(ValueError):
                PREP.validate_producer(PIN, *records)

    def test_wrong_artifact_identity_hash_size_or_source_is_rejected(self):
        for field, value in [("id", 1), ("name", "other"), ("size_in_bytes", 1), ("expired", True),
                             ("digest", "sha256:" + "0" * 64), ("workflow_run", dict(id=1, head_sha=PIN["head"]))]:
            with self.subTest(field=field):
                records = producer()
                records[3][field] = value
                with self.assertRaises(ValueError):
                    PREP.validate_producer(PIN, *records)

    def test_project_projection_keeps_every_other_original_byte(self):
        source = b'<Project Sdk="Microsoft.NET.Sdk.WindowsDesktop">\r\n<TargetFramework>net6.0-windows</TargetFramework>\r\n<Original />\r\n</Project>'
        for mode, framework, sdk in [("Microsoft", b"net11.0-windows", b"Microsoft.NET.Sdk.WindowsDesktop"),
                                     ("Portable", b"net11.0", b"LibreWinForms.Sdk/0.1.0-source-first")]:
            actual = PREP.project_bytes(source, mode)
            restored = actual.replace(framework, b"net6.0-windows").replace(sdk, b"Microsoft.NET.Sdk.WindowsDesktop")
            self.assertEqual(restored, source)
        with self.assertRaises(ValueError):
            PREP.project_bytes(source.replace(b"net6.0-windows", b"net8.0-windows"), "Microsoft")

    def test_source_batch_rejects_mismatch_before_publishing_either_application(self):
        source = b"original source"
        blob = hashlib.sha1(b"blob 15\0" + source).hexdigest()
        contract = dict(files=[dict(path="Program.cs", bytes=len(source), gitBlobSha1=blob)],
            preparedFiles={mode: [dict(path="Program.cs", sha256=hashlib.sha256(source).hexdigest())]
                           for mode in ("Microsoft", "Portable")}, licenseSha256=hashlib.sha256(b"license").hexdigest())
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "pair"
            for bad in (b"different bytes", source + b"\n"):
                with self.assertRaises(ValueError):
                    PREP.stage_source(Path(temporary), output, contract, lambda _: bad, b"license")
                self.assertFalse(output.exists())
            wrong = copy.deepcopy(contract)
            wrong["preparedFiles"]["Portable"][0]["sha256"] = "0" * 64
            with self.assertRaises(ValueError):
                PREP.stage_source(Path(temporary), output, wrong, lambda _: source, b"license")
            self.assertFalse(output.exists())
            PREP.stage_source(Path(temporary), output, contract, lambda _: source, b"license")
            self.assertEqual((output / "Microsoft/Program.cs").read_bytes(), source)
            self.assertEqual((output / "Portable/Program.cs").read_bytes(), source)
            with self.assertRaises(ValueError):
                PREP.stage_source(Path(temporary), output, contract, lambda _: source, b"license")

    def test_feed_inventory_rejects_missing_or_unexpected_payloads(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with self.assertRaises(ValueError):
                PREP.validate_feed(root, PIN)
            (root / "unqualified.nupkg").write_bytes(b"other")
            with self.assertRaises(ValueError):
                PREP.validate_feed(root, PIN)

    def test_original_source_and_observer_contract_is_complete(self):
        contract = json.loads((ROOT / "eng/OriginalGrid/source-contract.json").read_text())
        self.assertEqual(contract["commit"], PREP.METADATA.SOURCE_COMMIT)
        self.assertEqual(len(contract["files"]), 26)
        self.assertEqual(len(contract["preparedFiles"]["Microsoft"]), 26)
        self.assertEqual(len(contract["preparedFiles"]["Portable"]), 26)
        self.assertEqual(PREP.digest(ROOT / "eng/OriginalGrid/OriginalGridObserver.cs"), contract["observerSha256"])
        self.assertEqual(contract["sdkVersion"], "11.0.100-preview.5.26302.115")


if __name__ == "__main__":
    unittest.main()
