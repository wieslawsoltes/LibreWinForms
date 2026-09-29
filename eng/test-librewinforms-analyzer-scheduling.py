#!/usr/bin/env python3
"""Bounded scheduling controls; the real 88 compiler cases remain mandatory."""

import ast
from concurrent.futures import ThreadPoolExecutor
import importlib.util
import json
from pathlib import Path
import signal
import subprocess
import tempfile
import threading
from types import SimpleNamespace
import unittest
from unittest import mock


REPO = Path(__file__).resolve().parent.parent
SPEC = importlib.util.spec_from_file_location("analyzer_contract", REPO / "eng/librewinforms-analyzer-contract.py")
CONTRACT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CONTRACT)

MODE_CASES = (
    "csharp-negative", "csharp-positive", "vb-negative", "vb-positive",
    "top-level", "block-namespace", "file-namespace", "caller-owned", "caller-missing",
    "font-family-discovery", "font-top-level", "font-block-namespace", "font-file-namespace",
    "font-sanitization", "font-absent", "font-empty", "font-whitespace", "font-invalid",
    "font-caller-owned", "font-library-without-forms", "font-explicit-forms-library",
    "font-late-caller-owned", "font-late-sdk-owned", "font-late-disable-forms", "font-late-disable-portable-references",
    "dpi-absent", "dpi-empty", "dpi-dpiunaware", "dpi-systemaware", "dpi-permonitor", "dpi-permonitorv2",
    "dpi-dpiunawaregdiscaled", "dpi-case-insensitive", "dpi-numeric", "dpi-invalid-name", "dpi-invalid-number",
    "dpi-invalid-escaping", "dpi-caller-owned", "dpi-late-policy", "dpi-late-caller-owned",
    "dpi-prior-permonitorv2", "dpi-prior-dpiunaware",
)


class OwnedProcess:
    """A process double whose wait finishes only after the owner's kill."""

    def __init__(self):
        self.pid = 12345
        self.killed = threading.Event()
        self.waits = []

    def poll(self):
        return -9 if self.killed.is_set() else None

    def wait(self, timeout=None):
        self.waits.append(timeout)
        if not self.killed.wait(5):
            raise AssertionError("Owned process was not cancelled")
        return -9


class AnalyzerSchedulingTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="analyzer-scheduling-test.")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.args = SimpleNamespace(parallel_modes=True, reference_mode="Both",
                                    project_packages=self.root / "sdk-packages", producer_snapshot=self.root / "producer")

    def test_parallel_modes_overlap_but_each_complete_case_list_stays_serial(self):
        barrier = threading.Barrier(2)
        lock = threading.Lock()
        active = {"Project": 0, "Package": 0}
        maximum = {"Project": 0, "Package": 0}
        observed = {"Project": [], "Package": []}
        peak = 0

        def case(args, scratch, evidence, mode, name, source, **options):
            nonlocal peak
            with lock:
                active[mode] += 1
                maximum[mode] = max(maximum[mode], active[mode])
                peak = max(peak, sum(active.values()))
                observed[mode].append((name, source, options))
            try:
                if name == "csharp-negative":
                    barrier.wait(timeout=5)
                return {"case": mode.lower() + "-" + name, "fontFamily": "Fixture Sans"}
            finally:
                with lock:
                    active[mode] -= 1

        with mock.patch.object(CONTRACT, "build_case", side_effect=case):
            results = CONTRACT.build_modes(self.args, self.root / "consumer", self.root)
        self.assertEqual({"Project": 1, "Package": 1}, maximum)
        self.assertEqual(2, peak)
        self.assertEqual(42, len(MODE_CASES))
        for mode in active:
            self.assertEqual(list(MODE_CASES), [value[0] for value in observed[mode]])
        # Both modes retain every actual source/options input, including the
        # discovered font's dependent cases and all expected negative diagnostics.
        self.assertEqual(observed["Project"], observed["Package"])
        self.assertEqual([mode.lower() + "-" + name for mode in ("Project", "Package") for name in MODE_CASES],
                         [value["case"] for value in results])
        timing = json.loads((self.root / "mode-timing.json").read_text())
        self.assertTrue(timing["parallel"])
        self.assertEqual([42, 42], [mode["caseCount"] for mode in timing["modes"]])
        self.assertEqual([str(self.args.project_packages), str(self.root / "consumer/packages")],
                         [mode["packageCache"] for mode in timing["modes"]])
        self.assertIsNone(self.args._compiler_processes)

    def test_serial_and_single_mode_callers_remain_on_the_calling_thread(self):
        for selection in ("Both", "Project", "Package"):
            with self.subTest(selection=selection):
                self.args.parallel_modes = False
                self.args.reference_mode = selection
                self.args.project_packages = None
                self.args.producer_snapshot = None
                seen = []

                def mode(args, scratch, evidence, name):
                    seen.append((name, threading.get_ident()))
                    return [{"case": name}]

                with mock.patch.object(CONTRACT, "build_mode_cases", side_effect=mode):
                    results = CONTRACT.build_modes(self.args, self.root, self.root)
                expected = ["Project", "Package"] if selection == "Both" else [selection]
                self.assertEqual([(name, threading.get_ident()) for name in expected], seen)
                self.assertEqual(expected, [value["case"] for value in results])

    def test_parallel_admission_requires_the_complete_private_handoff(self):
        for field, value in (("reference_mode", "Project"), ("reference_mode", "Package"),
                             ("project_packages", None), ("producer_snapshot", None)):
            with self.subTest(field=field, value=value):
                original = getattr(self.args, field)
                setattr(self.args, field, value)
                with self.assertRaisesRegex(AssertionError, "verified caller-owned"):
                    CONTRACT.validate_parallel_modes(self.args)
                setattr(self.args, field, original)
        with mock.patch.object(CONTRACT.os, "name", "nt"):
            with self.assertRaisesRegex(AssertionError, "POSIX process groups"):
                CONTRACT.validate_parallel_modes(self.args)

    def test_failure_or_interrupt_cancels_the_sibling_without_losing_the_original_error(self):
        for failure in (AssertionError("original package failure"), KeyboardInterrupt(), SystemExit(143)):
            with self.subTest(failure=type(failure).__name__):
                process = OwnedProcess()
                started = threading.Event()
                controllers = []

                def create(*arguments, **options):
                    started.set()
                    self.assertTrue(options["start_new_session"])
                    return process

                def mode(args, scratch, evidence, name):
                    if name == "Package":
                        self.assertTrue(started.wait(5))
                        raise failure
                    controllers.append(args._compiler_processes)
                    CONTRACT.run_consumer(args, ["fixture compiler"], cwd=scratch, env={}, output=None, timeout=300)
                    raise AssertionError("killed sibling diagnostics are not the original failure")

                with mock.patch.object(CONTRACT.subprocess, "Popen", side_effect=create), \
                     mock.patch.object(CONTRACT.CompilerProcesses, "kill", side_effect=lambda value: value.killed.set()), \
                     mock.patch.object(CONTRACT, "build_mode_cases", side_effect=mode):
                    with self.assertRaises(type(failure)) as caught:
                        CONTRACT.build_modes(self.args, self.root, self.root)
                self.assertIs(failure, caught.exception)
                self.assertTrue(process.killed.is_set())
                self.assertEqual([300], process.waits)
                self.assertEqual(set(), controllers[0].processes)
                self.assertIsNone(self.args._compiler_processes)
                self.assertFalse((self.root / "mode-timing.json").exists())

    def test_cancelled_run_cannot_start_a_later_case_or_process(self):
        controller = CONTRACT.CompilerProcesses()
        controller.cancel()
        with mock.patch.object(CONTRACT.subprocess, "Popen") as create:
            with self.assertRaisesRegex(RuntimeError, "cancelled"):
                controller.check()
            with self.assertRaisesRegex(RuntimeError, "cancelled"):
                controller.run(["not started"], cwd=self.root, env={}, output=None, timeout=300)
        create.assert_not_called()

    def test_timeout_reaps_the_owned_group_without_extending_the_case_deadline(self):
        for timeout in (300, 60):
            with self.subTest(timeout=timeout):
                controller = CONTRACT.CompilerProcesses()
                process = mock.Mock(pid=12345)
                failure = subprocess.TimeoutExpired("fixture", timeout)
                process.wait.side_effect = [failure, -9]
                with mock.patch.object(CONTRACT.subprocess, "Popen", return_value=process), \
                     mock.patch.object(CONTRACT.os, "killpg") as kill:
                    with self.assertRaises(subprocess.TimeoutExpired) as caught:
                        controller.run(["fixture"], cwd=self.root, env={}, output=None, timeout=timeout)
                self.assertIs(failure, caught.exception)
                self.assertEqual([mock.call(timeout=timeout), mock.call()], process.wait.call_args_list)
                kill.assert_called_once_with(process.pid, signal.SIGKILL)
                self.assertEqual(set(), controller.processes)
        tree = ast.parse(Path(CONTRACT.__file__).read_text())
        bounds = [keyword.value.value for node in ast.walk(tree) if isinstance(node, ast.Call) and
                  isinstance(node.func, ast.Name) and node.func.id == "run_consumer"
                  for keyword in node.keywords if keyword.arg == "timeout"]
        self.assertEqual([300, 60], bounds)

    def test_cancellation_cannot_miss_a_process_still_being_created(self):
        controller = CONTRACT.CompilerProcesses()
        process = OwnedProcess()
        creating, release, cancelling = threading.Event(), threading.Event(), threading.Event()

        def create(*arguments, **options):
            creating.set()
            if not release.wait(5):
                raise AssertionError("Creation barrier was not released")
            return process

        def cancel():
            cancelling.set()
            controller.cancel()

        with mock.patch.object(CONTRACT.subprocess, "Popen", side_effect=create), \
             mock.patch.object(CONTRACT.CompilerProcesses, "kill", side_effect=lambda value: value.killed.set()), \
             ThreadPoolExecutor(max_workers=2) as executor:
            running = executor.submit(controller.run, ["fixture"], cwd=self.root, env={}, output=None, timeout=300)
            self.assertTrue(creating.wait(5))
            cancelled = executor.submit(cancel)
            self.assertTrue(cancelling.wait(5))
            release.set()
            self.assertEqual(-9, running.result(timeout=5))
            cancelled.result(timeout=5)
        self.assertTrue(process.killed.is_set())
        self.assertEqual(set(), controller.processes)

    def test_termination_retains_signal_exit_status(self):
        with self.assertRaises(SystemExit) as caught:
            CONTRACT.terminate_modes(signal.SIGTERM, None)
        self.assertEqual(128 + signal.SIGTERM, caught.exception.code)

    def test_full_packing_gate_retains_snapshot_and_all_original_controls(self):
        pack = (REPO / "eng/librewinforms-pack-source-first.sh").read_text()
        self.assertLess(pack.index("test-librewinforms-analyzer-scheduling.py"), pack.index("Capturing original SDK"))
        invocation = pack[pack.index('echo "Verifying original SDK analyzer payload'):]
        for argument in ("--parallel-modes", "--project-packages", "--producer-snapshot", "--producer-manifest-sha256"):
            self.assertIn(argument, invocation)
        source = Path(CONTRACT.__file__).read_text()
        self.assertIn('"ordinary-upstream"', source)
        self.assertIn('"missing-" + language', source)
        self.assertIn("verify_rejected_payloads(args, package, evidence, expected)", source)
        self.assertIn("verify_snapshot(args.repo_root, package, args.configuration,", source)


if __name__ == "__main__":
    unittest.main()
