#!/usr/bin/env python3
"""Cache routing/ownership controls, not a substitute for real compiler cases."""

import importlib.util
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest


REPO = Path(__file__).resolve().parent.parent
SPEC = importlib.util.spec_from_file_location("analyzer_contract", REPO / "eng/librewinforms-analyzer-contract.py")
CONTRACT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CONTRACT)


class ProjectCacheTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="analyzer-cache-test.")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name).resolve()
        self.cache = self.root / "sdk-packages"
        self.package = self.root / "producer.nupkg"
        self.package.write_bytes(b"cache-routing fixture, not a real SDK")
        self.installed = self.cache / "librewinforms.sdk/fixture/librewinforms.sdk.fixture.nupkg"
        self.installed.parent.mkdir(parents=True)
        self.installed.write_bytes(self.package.read_bytes())
        self.args = SimpleNamespace(project_packages=self.cache, scratch_parent=self.root,
                                    producer_snapshot=self.root / "producer", sdk_version="fixture", reference_mode="Both")

    def verify(self):
        CONTRACT.validate_project_packages(self.args, self.package)

    def test_project_reuses_only_the_exact_existing_cache(self):
        self.verify()
        self.assertEqual(self.cache, CONTRACT.case_packages(self.args, self.root / "consumer", "Project"))

    def test_package_and_mutant_cases_keep_fresh_cache(self):
        self.verify()
        scratch = self.root / "consumer"
        self.assertEqual(scratch / "packages", CONTRACT.case_packages(self.args, scratch, "Package"))
        self.assertNotEqual(self.cache, CONTRACT.case_packages(self.args, scratch, "Package"))

    def test_standalone_callers_still_build_cold(self):
        self.args.project_packages = None
        self.verify()
        scratch = self.root / "consumer"
        for mode in ("Project", "Package"):
            self.args.reference_mode = mode
            self.verify()
            self.assertEqual(scratch / "packages", CONTRACT.case_packages(self.args, scratch, mode))

    def test_handoff_requires_the_complete_both_matrix(self):
        for mode in ("Project", "Package"):
            with self.subTest(reference_mode=mode):
                self.args.reference_mode = mode
                with self.assertRaisesRegex(AssertionError, "complete Both reference-mode matrix"):
                    self.verify()

    def test_parent_alias_preserves_original_reference_path_spelling(self):
        alias = self.root / "parent-alias"
        alias.symlink_to(self.root, target_is_directory=True)
        self.args.scratch_parent = alias
        self.args.project_packages = alias / "sdk-packages"
        self.verify()
        self.assertEqual(alias / "sdk-packages", self.args.project_packages)

    def test_reuse_requires_both_ownership_and_snapshot(self):
        for field in ("scratch_parent", "producer_snapshot"):
            original = getattr(self.args, field)
            setattr(self.args, field, None)
            with self.assertRaisesRegex(AssertionError, "requires caller-owned scratch"):
                self.verify()
            setattr(self.args, field, original)

    def test_missing_or_different_archive_is_rejected(self):
        self.installed.write_bytes(b"another generation")
        with self.assertRaisesRegex(AssertionError, "exact current producer"):
            self.verify()
        self.installed.unlink()
        with self.assertRaisesRegex(AssertionError, "exact current producer"):
            self.verify()

    def test_shared_external_or_parent_directory_is_rejected(self):
        for cache in (self.root.parent, self.root, self.root / "missing", self.root / ".." / self.root.name / "sdk-packages"):
            with self.subTest(cache=cache):
                self.args.project_packages = cache
                with self.assertRaises(AssertionError):
                    self.verify()

    def test_symlink_directory_or_archive_is_rejected(self):
        alias = self.root / "alias"
        alias.symlink_to(self.cache, target_is_directory=True)
        self.args.project_packages = alias
        with self.assertRaisesRegex(AssertionError, "symlink traversal"):
            self.verify()
        self.args.project_packages = self.cache
        self.installed.unlink()
        self.installed.symlink_to(self.package)
        with self.assertRaisesRegex(AssertionError, "exact current producer"):
            self.verify()


if __name__ == "__main__":
    unittest.main()
