#!/usr/bin/env python3
"""Exercise both package producers' exact native staging calls without downloads."""

import os
from pathlib import Path
import re
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[2]


def drawing_package_ids():
    script = 'source "$1/eng/librewinforms-package-list.sh"; printf "%s\\n" "${librewinforms_preview_progpu_package_ids[@]}"'
    return subprocess.run(["bash", "-c", script, "drawing-closure", str(ROOT)],
                          check=True, capture_output=True, text=True).stdout.splitlines()


def verify_drawing_source_closure(package_ids):
    seen = set()
    for package_id in package_ids:
        project_name = {"ProGPU.System.Drawing.Common": "System.Drawing.Common",
                        "ProGPU.SkiaSharp": "SkiaSharp"}.get(package_id, package_id)
        project = ROOT / "external/ProGPU/src" / project_name / (project_name + ".csproj")
        for reference in ET.parse(project).findall("./ItemGroup/ProjectReference"):
            dependency = (project.parent / reference.attrib["Include"].replace("\\", "/")).resolve()
            dependency_id = ET.parse(dependency).findtext("./PropertyGroup/PackageId") or dependency.stem
            if dependency_id not in seen:
                raise ValueError(f"{package_id} requires earlier dependency {dependency_id}")
        if package_id in seen:
            raise ValueError(f"Duplicate package {package_id}")
        seen.add(package_id)


class CanonicalNativeStagingTests(unittest.TestCase):
    def test_release_registry_covers_actual_source_dependencies(self):
        verify_drawing_source_closure(drawing_package_ids())

    def test_omitting_native_dependency_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "ProGPU.Backend.Native"):
            verify_drawing_source_closure([p for p in drawing_package_ids() if p != "ProGPU.Backend.Native"])

    def test_omitting_dawn_dependency_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "ProGPU.Backend.Dawn"):
            verify_drawing_source_closure([p for p in drawing_package_ids() if p != "ProGPU.Backend.Dawn"])

    def staging_step(self):
        workflow = (ROOT / ".github/workflows/librewinforms-ci.yml").read_text()
        package = workflow.split("\n  packages:\n", 1)[1]
        self.assertIn("      actions: read\n", package)
        name = "      - name: Stage exact successful ProGPU native runtimes for canonical packaging\n"
        self.assertLess(package.index("      - name: Align canonical LibreWPF ProGPU checkout\n"), package.index(name))
        self.assertLess(package.index(name), package.index("      - name: Build canonical WindowsFormsIntegration from LibreWPF source\n"))
        step = package.split(name, 1)[1].split("\n      - name:", 1)[0]
        self.assertIn("GH_TOKEN: ${{ github.token }}", step)
        return "\n".join(line[10:] for line in step.split("        run: |\n", 1)[1].splitlines())

    def run_step(self, helper_status=0, git_status=0):
        with tempfile.TemporaryDirectory(prefix="forms-canonical-staging-") as temporary:
            root = Path(temporary)
            helper = root / "artifacts/checkouts/wpf-canonical/eng/progpu-stage-ci-native-runtimes.sh"
            helper.parent.mkdir(parents=True)
            # Recording stub only: no runtime bytes or package files are created.
            helper.write_text('#!/bin/bash\nprintf "%s\\n" "$@" > "$STAGING_RECEIPT"\nexit "$STAGING_STATUS"\n')
            helper.chmod(0o755)
            receipt = root / "arguments"
            environment = dict(os.environ, GITHUB_WORKSPACE=str(root), STAGING_RECEIPT=str(receipt),
                               STAGING_STATUS=str(helper_status), GIT_STATUS=str(git_status))
            script = '''git() {
  test "$*" = "-C external/ProGPU rev-parse HEAD" || return 91
  test "$GIT_STATUS" = 0 || return "$GIT_STATUS"
  printf '%s\\n' aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
}
''' + self.staging_step()
            result = subprocess.run(["bash", "-c", script], cwd=root, env=environment, capture_output=True, text=True)
            return result.returncode, receipt.read_text().splitlines() if receipt.exists() else [], str(root)

    def test_stages_exact_forms_progpu_checkout_not_wpf_prerequisite(self):
        status, arguments, root = self.run_step()
        self.assertEqual(status, 0)
        self.assertEqual(arguments, ["a" * 40, root + "/external/ProGPU/artifacts/progpu-native/package"])

    def test_failed_qualification_stops_packaging(self):
        status, arguments, _ = self.run_step(helper_status=23)
        self.assertEqual(status, 23)
        self.assertEqual(arguments[0], "a" * 40)

    def test_unresolved_commit_never_stages(self):
        status, arguments, _ = self.run_step(git_status=27)
        self.assertEqual(status, 27)
        self.assertEqual(arguments, [])


class SourceFirstNativeStagingTests(unittest.TestCase):
    helper_commit = "d8a3a153bfd3d180023d119001e94da50c2eeca1"

    def staging_step(self):
        workflow = (ROOT / ".github/workflows/librewinforms-ci.yml").read_text()
        job = workflow.split("\n  source-first-shadow:\n", 1)[1].split("\n  source-first-visible:\n", 1)[0]
        self.assertRegex(job, r"permissions:\n      contents: read\n      actions: read\n")
        self.assertIn("LIBREWINFORMS_NATIVE_STAGING_WPF_COMMIT: " + self.helper_commit, job)
        validate = "      - name: Validate canonical source and local ProGPU graph\n"
        checkout = "      - name: Checkout immutable LibreWPF native staging helper\n"
        stage = "      - name: Stage exact successful ProGPU native runtimes for source-first packaging\n"
        pack = "      - name: Pack and consume canonical source-first package\n"
        positions = [job.index(name) for name in (validate, checkout, stage, pack)]
        self.assertEqual(positions, sorted(positions))
        helper_checkout = job.split(checkout, 1)[1].split("\n      - name:", 1)[0]
        for setting in ("uses: actions/checkout@v4", "repository: wieslawsoltes/LibreWPF",
                        "ref: ${{ env.LIBREWINFORMS_NATIVE_STAGING_WPF_COMMIT }}",
                        "path: artifacts/checkouts/wpf-native-staging",
                        "sparse-checkout: eng/progpu-stage-ci-native-runtimes.sh",
                        "sparse-checkout-cone-mode: false"):
            self.assertIn(setting, helper_checkout)
        step = job.split(stage, 1)[1].split("\n      - name:", 1)[0]
        self.assertIn("GH_TOKEN: ${{ github.token }}", step)
        self.assertNotIn("continue-on-error:", step)
        return "\n".join(line[10:] for line in step.split("        run: |\n", 1)[1].splitlines())

    def run_step(self, helper_status=0, git_failure="", git_status=27,
                 helper_commit=None, pinned_commit="a" * 40, checkout_commit="a" * 40):
        with tempfile.TemporaryDirectory(prefix="forms source staging ") as temporary:
            root = Path(temporary)
            helper = root / "artifacts/checkouts/wpf-native-staging/eng/progpu-stage-ci-native-runtimes.sh"
            helper.parent.mkdir(parents=True)
            # Records invocation only. This is not an imitation native artifact.
            helper.write_text('#!/bin/bash\nprintf "%s\\n" "$@" > "$STAGING_RECEIPT"\nexit "$STAGING_STATUS"\n')
            helper.chmod(0o755)
            receipt = root / "arguments"
            environment = dict(os.environ, GITHUB_WORKSPACE=str(root), STAGING_RECEIPT=str(receipt),
                               STAGING_STATUS=str(helper_status), GIT_STATUS=str(git_status),
                               GIT_FAILURE=git_failure, HELPER_COMMIT=helper_commit or self.helper_commit,
                               PINNED_COMMIT=pinned_commit, CHECKOUT_COMMIT=checkout_commit,
                               LIBREWINFORMS_NATIVE_STAGING_WPF_COMMIT=self.helper_commit)
            script = '''git() {
  case "$*" in
    "-C artifacts/checkouts/wpf-native-staging rev-parse HEAD") key=helper; value="$HELPER_COMMIT" ;;
    "rev-parse HEAD:external/ProGPU") key=pin; value="$PINNED_COMMIT" ;;
    "-C external/ProGPU rev-parse HEAD") key=checkout; value="$CHECKOUT_COMMIT" ;;
    *) return 91 ;;
  esac
  test "$GIT_FAILURE" != "$key" || return "$GIT_STATUS"
  printf '%s\\n' "$value"
}
''' + self.staging_step()
            result = subprocess.run(["bash", "-c", script], cwd=root, env=environment, capture_output=True, text=True)
            return result.returncode, receipt.read_text().splitlines() if receipt.exists() else [], str(root)

    def test_stages_exact_gitlink_into_actual_forms_progpu_checkout(self):
        status, arguments, root = self.run_step()
        self.assertEqual(status, 0)
        self.assertEqual(arguments, ["a" * 40, root + "/external/ProGPU/artifacts/progpu-native/package"])

    def test_failed_producer_qualification_stops_packaging(self):
        status, arguments, _ = self.run_step(helper_status=23)
        self.assertEqual(status, 23)
        self.assertEqual(arguments[0], "a" * 40)

    def test_replaced_helper_checkout_never_stages(self):
        status, arguments, _ = self.run_step(helper_commit="b" * 40)
        self.assertNotEqual(status, 0)
        self.assertEqual(arguments, [])

    def test_replaced_progpu_checkout_never_stages(self):
        status, arguments, _ = self.run_step(checkout_commit="b" * 40)
        self.assertNotEqual(status, 0)
        self.assertEqual(arguments, [])

    def test_unresolved_identity_never_stages(self):
        for identity in ("helper", "pin", "checkout"):
            with self.subTest(identity=identity):
                status, arguments, _ = self.run_step(git_failure=identity)
                self.assertNotEqual(status, 0)
                self.assertEqual(arguments, [])

    def test_non_commit_gitlink_never_stages(self):
        for identity in ("", "a" * 39, "not-a-commit"):
            with self.subTest(identity=identity):
                status, arguments, _ = self.run_step(pinned_commit=identity, checkout_commit=identity)
                self.assertNotEqual(status, 0)
                self.assertEqual(arguments, [])

    def test_all_workflow_drawing_pack_producers_are_staged(self):
        workflow = (ROOT / ".github/workflows/librewinforms-ci.yml").read_text()
        producers = []
        for match in re.finditer(r"^  ([a-z-]+):\n(.*?)(?=^  [a-z-]+:\n|\Z)", workflow,
                                 flags=re.MULTILINE | re.DOTALL):
            job, body = match.groups()
            if re.search(r"\./eng/librewinforms-pack(?:-source-first)?\.sh", body):
                producers.append(job)
                self.assertIn("eng/progpu-stage-ci-native-runtimes.sh", body)
        self.assertEqual(producers, ["source-first-shadow", "packages"])
        packer = (ROOT / "eng/librewinforms-pack-source-first.sh").read_text()
        self.assertIn("PROGPU_PACKAGE_GROUP=drawing-runtime", packer)
        self.assertIn('"${repo_root}/external/ProGPU/eng/progpu-pack.sh"', packer)


if __name__ == "__main__":
    unittest.main()
