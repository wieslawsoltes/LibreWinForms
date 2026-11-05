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
HELPER_COMMIT = "065e8cc0c027566f1316169063ba92f14fa715fb"


def drawing_package_ids():
    script = 'source "$1/eng/librewinforms-package-list.sh"; printf "%s\\n" "${librewinforms_preview_progpu_package_ids[@]}"'
    return subprocess.run(["bash", "-c", script, "drawing-closure", str(ROOT)],
                          check=True, capture_output=True, text=True).stdout.splitlines()


def verify_drawing_source_closure(package_ids):
    seen = set()
    for package_id in package_ids:
        project_name = {"ProGPU.System.Drawing.Common": "System.Drawing.Common",
                        "ProGPU.SkiaSharp": "SkiaSharp"}.get(package_id, package_id)
        progpu_root = ROOT / "external/ProGPU"
        source_root = ROOT / "src/ProGPU.Drawing" if package_id == "ProGPU.System.Drawing.Common" else progpu_root / "src"
        project = source_root / project_name / (project_name + ".csproj")
        for reference in ET.parse(project).findall("./ItemGroup/ProjectReference"):
            include = reference.attrib["Include"].replace("\\", "/").replace("$(ProGpuSourceRoot)", str(progpu_root))
            if "$(" in include:
                raise ValueError(f"Unresolved source dependency: {include}")
            dependency = (project.parent / include).resolve()
            dependency_id = ET.parse(dependency).findtext("./PropertyGroup/PackageId") or dependency.stem
            if dependency_id not in seen:
                raise ValueError(f"{package_id} requires earlier dependency {dependency_id}")
        if package_id in seen:
            raise ValueError(f"Duplicate package {package_id}")
        seen.add(package_id)


class CanonicalNativeStagingTests(unittest.TestCase):
    helper_checkout_path = "artifacts/checkouts/wpf-native-staging"

    def test_release_registry_covers_actual_source_dependencies(self):
        verify_drawing_source_closure(drawing_package_ids())

    def test_omitting_native_dependency_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "ProGPU.Backend.Native"):
            verify_drawing_source_closure([p for p in drawing_package_ids() if p != "ProGPU.Backend.Native"])

    def test_omitting_native_sdk_dependency_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "ProGPU.Native.Sdk"):
            verify_drawing_source_closure([p for p in drawing_package_ids() if p != "ProGPU.Native.Sdk"])

    def test_omitting_dawn_dependency_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "ProGPU.Backend.Dawn"):
            verify_drawing_source_closure([p for p in drawing_package_ids() if p != "ProGPU.Backend.Dawn"])

    def test_omitting_native_scene_bridge_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "ProGPU.System.Drawing.Common.*ProGPU.Scene.Native"):
            verify_drawing_source_closure([p for p in drawing_package_ids() if p != "ProGPU.Scene.Native"])

    def test_native_scene_before_managed_scene_is_rejected(self):
        packages = drawing_package_ids()
        packages.remove("ProGPU.Scene.Native")
        packages.insert(packages.index("ProGPU.Scene"), "ProGPU.Scene.Native")
        with self.assertRaisesRegex(ValueError, "ProGPU.Scene.Native.*ProGPU.Scene"):
            verify_drawing_source_closure(packages)

    def staging_step(self):
        workflow = (ROOT / ".github/workflows/librewinforms-ci.yml").read_text()
        package = workflow.split("\n  packages:\n", 1)[1]
        self.assertIn("      actions: read\n", package)
        self.assertIn("LIBREWINFORMS_NATIVE_STAGING_WPF_COMMIT: " + HELPER_COMMIT, package)
        checkout = package.split("      - name: Checkout immutable LibreWPF native staging helper\n", 1)[1].split("\n      - name:", 1)[0]
        for entry in ("ref: ${{ env.LIBREWINFORMS_NATIVE_STAGING_WPF_COMMIT }}",
                      "path: artifacts/checkouts/wpf-native-staging",
                      "eng/progpu-stage-ci-native-runtimes.sh",
                      "eng/progpu-stage-release-native-packages.py"):
            self.assertIn(entry, checkout)
        name = "      - name: Stage exact successful ProGPU native runtimes for canonical packaging\n"
        self.assertLess(package.index("      - name: Align canonical LibreWPF ProGPU checkout\n"), package.index(name))
        self.assertLess(package.index(name), package.index("      - name: Build canonical WindowsFormsIntegration from LibreWPF source\n"))
        step = package.split(name, 1)[1].split("\n      - name:", 1)[0]
        self.assertIn("GH_TOKEN: ${{ github.token }}", step)
        return "\n".join(line[10:] for line in step.split("        run: |\n", 1)[1].splitlines())

    def run_step(self, helper_status=0, git_status=0, helper_commit=HELPER_COMMIT):
        with tempfile.TemporaryDirectory(prefix="forms-canonical-staging-") as temporary:
            root = Path(temporary)
            helper = root / self.helper_checkout_path / "eng/progpu-stage-ci-native-runtimes.sh"
            helper.parent.mkdir(parents=True)
            # Recording stub only: no runtime bytes or package files are created.
            helper.write_text('#!/bin/bash\nprintf "%s\\n" "$@" > "$STAGING_RECEIPT"\nexit "$STAGING_STATUS"\n')
            helper.chmod(0o755)
            receipt = root / "arguments"
            environment = dict(os.environ, GITHUB_WORKSPACE=str(root), STAGING_RECEIPT=str(receipt),
                               STAGING_STATUS=str(helper_status), GIT_STATUS=str(git_status),
                               LIBREWINFORMS_NATIVE_STAGING_WPF_COMMIT=HELPER_COMMIT, HELPER_COMMIT=helper_commit)
            script = '''git() {
  if test "$*" = "-C artifacts/checkouts/wpf-native-staging rev-parse HEAD"; then
    printf '%s\\n' "$HELPER_COMMIT"
    return 0
  fi
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
        self.assertEqual(arguments, ["a" * 40, root + "/external/ProGPU/artifacts/progpu-native/package", root + "/external/ProGPU/artifacts/progpu-dx12/package", root + "/external/ProGPU/artifacts/progpu-xlib/package", root + "/external/ProGPU/artifacts/progpu-wgpu-windows/package", root + "/external/ProGPU"])

    def test_failed_qualification_stops_packaging(self):
        status, arguments, _ = self.run_step(helper_status=23)
        self.assertEqual(status, 23)
        self.assertEqual(arguments[0], "a" * 40)

    def test_unresolved_commit_never_stages(self):
        status, arguments, _ = self.run_step(git_status=27)
        self.assertEqual(status, 27)
        self.assertEqual(arguments, [])


class PackageNativeHelperIdentityTests(unittest.TestCase):
    def test_replaced_helper_checkout_never_stages(self):
        status, arguments, _ = CanonicalNativeStagingTests().run_step(helper_commit="b" * 40)
        self.assertNotEqual(status, 0)
        self.assertEqual(arguments, [])


class ReleaseNativeStagingTests(CanonicalNativeStagingTests):
    helper_checkout_path = "artifacts/checkouts/wpf-native-staging"

    def staging_step(self):
        workflow = (ROOT / ".github/workflows/librewinforms-release.yml").read_text()
        self.assertIn("permissions:\n  actions: read\n  contents: write\n", workflow)
        self.assertIn("LIBREWINFORMS_NATIVE_STAGING_WPF_COMMIT: " + HELPER_COMMIT, workflow)
        self.assertIn("Checkout immutable LibreWPF native staging helper", workflow)
        names = ["Align canonical LibreWPF ProGPU checkout",
                 "Stage exact successful ProGPU native runtimes for canonical packaging",
                 "Build canonical WindowsFormsIntegration from LibreWPF source",
                 "Build and pack"]
        positions = [workflow.index("      - name: " + name + "\n") for name in names]
        self.assertEqual(positions, sorted(positions),
                         "release canonical packaging must follow exact producer admission")
        step = workflow.split("      - name: " + names[1] + "\n", 1)[1].split("\n      - name:", 1)[0]
        self.assertIn("GH_TOKEN: ${{ github.token }}", step)
        self.assertNotIn("continue-on-error", step)
        return "\n".join(line[10:] for line in step.split("        run: |\n", 1)[1].splitlines())


class ReleaseRecoveryTests(unittest.TestCase):
    def test_release_uses_the_qualified_linux_display_environment(self):
        workflow = (ROOT / ".github/workflows/librewinforms-release.yml").read_text()
        install = workflow.split("      - name: Install Linux display for SDK initialization\n", 1)[1].split("\n      - name:", 1)[0]
        for dependency in ("xvfb", "libglfw3", "libvulkan1", "mesa-vulkan-drivers"):
            self.assertIn(dependency, install)
        for script in ("librewinforms-build-canonical-wfi.sh", "librewinforms-pack.sh",
                       "librewinforms-package-smoke.sh"):
            self.assertIn("xvfb-run -a ./eng/" + script, workflow)

    def test_existing_tag_recovery_preserves_exact_source_and_version(self):
        workflow = (ROOT / ".github/workflows/librewinforms-release.yml").read_text()
        step = workflow.split("      - name: Resolve release version\n", 1)[1].split("\n      - name:", 1)[0]
        script = "\n".join(line[10:] for line in step.split("        run: |\n", 1)[1].splitlines())
        script = re.sub(r"\$\{\{ inputs\.[a-z_]+ \}\}", "", script)
        with tempfile.TemporaryDirectory(prefix="forms-release-identity-") as temporary:
            root = Path(temporary)

            def git(*arguments):
                return subprocess.run(["git", *arguments], cwd=root, check=True,
                                      capture_output=True, text=True).stdout.strip()

            git("init", "--quiet")
            git("-c", "user.name=Release contract", "-c", "user.email=contract@example.invalid",
                "commit", "--quiet", "--allow-empty", "-m", "original source")
            source = git("rev-parse", "HEAD")
            tag = "librewinforms-v0.1.0-preview.67"
            git("tag", tag)
            git("branch", "not-a-tag")
            git("-c", "user.name=Release contract", "-c", "user.email=contract@example.invalid",
                "commit", "--quiet", "--allow-empty", "-m", "different source")
            replacement = git("rev-parse", "HEAD")
            cases = [
                ("tag push", source, "tag", tag, "", "", True),
                ("exact recovery", source, "branch", "recipe", tag, "0.1.0-preview.67", True),
                ("version mismatch", source, "branch", "recipe", tag, "0.1.0-preview.68", False),
                ("wrong checkout", replacement, "branch", "recipe", tag, "0.1.0-preview.67", False),
                ("missing tag", source, "branch", "recipe", "librewinforms-v0.1.0-preview.68", "", False),
                ("branch ref", source, "branch", "recipe", "not-a-tag", "", False),
                ("shell characters", source, "branch", "recipe", tag + ";touch injected", "", False),
            ]
            for name, commit, ref_type, ref_name, requested, version, success in cases:
                with self.subTest(name=name):
                    git("checkout", "--quiet", "--detach", commit)
                    output = root / "output"
                    output.write_text("")
                    environment = dict(os.environ, GITHUB_REF_TYPE=ref_type, GITHUB_REF_NAME=ref_name,
                                       REQUESTED_SOURCE_TAG=requested, REQUESTED_VERSION=version,
                                       GITHUB_ENV=str(root / "environment"), GITHUB_OUTPUT=str(output))
                    result = subprocess.run(["bash", "-c", script], cwd=root, env=environment,
                                            capture_output=True, text=True)
                    self.assertEqual(result.returncode == 0, success, result.stderr)
                    self.assertEqual(git("rev-parse", "HEAD"), commit)
                    self.assertEqual(git("rev-parse", "refs/tags/" + tag), source)
                    self.assertFalse((root / "injected").exists())
                    if success:
                        self.assertIn("tag=" + tag + "\n", output.read_text())
                        self.assertIn("bridge_ref=librewpf-v0.1.0-preview.67\n", output.read_text())


class SourceFirstNativeStagingTests(unittest.TestCase):
    helper_commit = HELPER_COMMIT

    def staging_step(self):
        workflow = (ROOT / ".github/workflows/librewinforms-ci.yml").read_text()
        job = workflow.split("\n  source-first-shadow:\n", 1)[1].split("\n  source-first-visible:\n", 1)[0]
        self.assertRegex(job, r"permissions:\n      contents: read\n      actions: read\n")
        self.assertIn("LIBREWINFORMS_NATIVE_STAGING_WPF_COMMIT: " + self.helper_commit, job)
        validate = "      - name: Validate canonical source and local ProGPU graph\n"
        checkout = "      - name: Checkout immutable LibreWPF native staging helper\n"
        stage = "      - name: Stage exact successful ProGPU native runtimes for source-first packaging\n"
        pack = "      - name: Pack and consume canonical source-first package\n"
        positions = [job.index(name) for name in (checkout, stage, validate, pack)]
        self.assertEqual(positions, sorted(positions))
        helper_checkout = job.split(checkout, 1)[1].split("\n      - name:", 1)[0]
        for setting in ("uses: actions/checkout@v4", "repository: wieslawsoltes/LibreWPF",
                        "ref: ${{ env.LIBREWINFORMS_NATIVE_STAGING_WPF_COMMIT }}",
                        "path: artifacts/checkouts/wpf-native-staging",
                        "sparse-checkout: |",
                        "eng/progpu-stage-ci-native-runtimes.sh",
                        "eng/progpu-stage-release-native-packages.py",
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
        self.assertEqual(arguments, ["a" * 40, root + "/external/ProGPU/artifacts/progpu-native/package", root + "/external/ProGPU/artifacts/progpu-dx12/package", root + "/external/ProGPU/artifacts/progpu-xlib/package", root + "/external/ProGPU/artifacts/progpu-wgpu-windows/package", root + "/external/ProGPU"])

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


class WindowsSourceNativeStagingTests(SourceFirstNativeStagingTests):
    def staging_step(self):
        workflow = (ROOT / '.github/workflows/librewinforms-ci.yml').read_text()
        job = workflow.split('\n  native-theme:\n', 1)[1].split('\n  automation-locale:\n', 1)[0]
        self.assertIn('      actions: read\n', job)
        self.assertIn('LIBREWINFORMS_NATIVE_STAGING_WPF_COMMIT: ' + self.helper_commit, job)
        checkout = job.split('      - name: Checkout immutable LibreWPF native staging helper\n', 1)[1].split('\n      - name:', 1)[0]
        name = '      - name: Stage exact successful ProGPU native runtimes for Windows source tests\n'
        self.assertLess(job.index(name), job.index('      - name: Build actual backend'))
        step = job.split(name, 1)[1].split('\n      - name:', 1)[0]
        for selected in (checkout, step):
            self.assertIn("if: hashFiles('external/ProGPU/eng/progpu-wgpu-windows-runtime.py') != ''", selected)
        self.assertIn('ref: ${{ env.LIBREWINFORMS_NATIVE_STAGING_WPF_COMMIT }}', checkout)
        self.assertIn('GH_TOKEN: ${{ github.token }}', step)
        self.assertNotIn('continue-on-error', step)
        return '\n'.join(line[10:] for line in step.split('        run: |\n', 1)[1].splitlines())


if __name__ == "__main__":
    unittest.main()
