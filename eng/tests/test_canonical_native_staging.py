#!/usr/bin/env python3
"""Exercise the package lane's exact native staging invocation without downloads."""

import os
from pathlib import Path
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]


class CanonicalNativeStagingTests(unittest.TestCase):
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


if __name__ == "__main__":
    unittest.main()
