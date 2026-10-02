"""Source contracts only; these controls do not run Drawing tests or a collector."""

from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]


class DrawingTraceGateTests(unittest.TestCase):
    def setUp(self):
        self.source = (ROOT / 'eng/librewinforms-source-first.sh').read_text()
        start = self.source.index('echo "Verifying ProGPU System.Drawing API debt')
        end = self.source.index('echo "Verifying the retired Portable', start)
        self.gate = self.source[start:end]
        workflow = (ROOT / '.github/workflows/librewinforms-ci.yml').read_text()
        self.job = workflow.split('  source-first-shadow:\n', 1)[1].split('  source-first-visible:\n', 1)[0]

    def test_trace_is_explicit_release_only_and_keeps_api_gate_first(self):
        self.assertIn('configuration="${CONFIGURATION:-Release}"', self.source)
        self.assertIn('"${LIBREWINFORMS_DRAWING_TRACE:-0}" == "1" && "${configuration}" == "Release"', self.gate)
        self.assertLess(self.gate.index('./eng/progpu-verify-system-drawing-api.sh'),
                        self.gate.index('python3 eng/progpu-test-system-drawing.py'))
        self.assertEqual(self.gate.count('python3 eng/progpu-test-system-drawing.py'), 1)

    def test_other_configurations_keep_original_command_and_explicit_no_trace_notice(self):
        fallback = self.gate.split('\n  else\n', 1)[1]
        self.assertIn('Drawing trace disabled: the pinned collector supports Release; preserving ${configuration} tests.', fallback)
        self.assertIn('dotnet test src/System.Drawing.Common.Tests/System.Drawing.Common.Tests.csproj \\\n'
                      '      --configuration "${configuration}" \\\n'
                      '      --nologo', fallback)
        self.assertNotIn('progpu-test-system-drawing.py', fallback)

    def test_exact_required_metadata_and_output_are_not_test_filters(self):
        self.assertIn('--require-method System.Drawing.Common.Tests.MetafileParserTests \\\n'
                      '      WarmedEnumerationDoesNotAllocatePerRecordPayloads', self.gate)
        self.assertIn('--output "${repo_root}/artifacts/system-drawing-quality"', self.gate)
        self.assertEqual(self.gate.count('dotnet test '), 1)
        for change in ('--filter', '--no-build', '|| true', 'DOTNET_GC', 'COMPlus_', 'Retry', 'warmup'):
            self.assertNotIn(change, self.gate)

    def test_ci_opt_in_and_original_outer_deadline_remain(self):
        self.assertIn('timeout-minutes: 45', self.job)
        step = self.job.split('      - name: Validate canonical source and local ProGPU graph\n', 1)[1].split('\n      - name:', 1)[0]
        self.assertIn('LIBREWINFORMS_DRAWING_TRACE: "1"', step)
        self.assertIn('run: ./eng/librewinforms-source-first.sh', step)
        self.assertNotIn('continue-on-error', step)
        self.assertIn('run: python3 eng/tests/test_drawing_trace_gate.py', self.job)

    def test_exact_owned_evidence_upload_is_always_and_separate_from_packages(self):
        step = self.job.split('      - name: Retain Drawing test and allocation diagnostic evidence\n', 1)[1].split('\n      - name:', 1)[0]
        self.assertIn('if: always()', step)
        self.assertIn('name: source-system-drawing-quality', step)
        self.assertIn('path: artifacts/system-drawing-quality/**', step)
        self.assertIn('retention-days: 7', step)
        self.assertIn('path: artifacts/packages/source-first/*.nupkg', self.job)


if __name__ == '__main__':
    unittest.main()
