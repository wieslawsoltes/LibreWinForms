#!/usr/bin/env python3
"""Offline inventory controls. Never load AppKit, inspect UI, or launch Forms."""
import importlib.util
import json
from pathlib import Path
import sys
import unittest
from unittest import mock

PATH = Path(__file__).resolve().parents[1] / "librewinforms-popup-macos-inventory.py"
spec = importlib.util.spec_from_file_location("inventory", PATH)
M = importlib.util.module_from_spec(spec)
spec.loader.exec_module(M)


class NativeFixture:
    def __init__(self):
        self.ax = mock.Mock()
        self.cg = mock.Mock()
        self.cf = mock.Mock()
        self.ax.AXIsProcessTrusted.return_value = True
        self.cg.CGPreflightScreenCaptureAccess.return_value = True
        self.cg.CGPreflightPostEventAccess.return_value = False  # unused permission is recorded, never requested
        self.ax.AXUIElementCreateApplication.return_value = "app"
        self.cf.CFRetain.side_effect = lambda value: value
        self.cf.CFEqual.side_effect = lambda a, b: a == b
        self.values = {("app", "AXWindows"): ["window"], ("window", "AXTitle"): "exact-title",
                       ("window", "AXRole"): "AXWindow", ("window", "AXChildren"): ["child"],
                       ("child", "AXRole"): "AXGroup", ("child", "AXContents"): ["child"],
                       ("child", "AXPosition"): dict(x=10.0, y=20.0)}
        self.release = mock.Mock()
        self.windows = mock.Mock(return_value=[dict(pid=42, title="exact-title", windowNumber=123)])
        self.pid = mock.Mock(return_value=42)
        self.check_time = mock.Mock()

    def attribute(self, element, name):
        return (self.values[(element, name)], 0) if (element, name) in self.values else (None, -25205)

    @staticmethod
    def array(value, maximum):
        M.require(len(value) <= maximum, "array budget")
        return value

    @staticmethod
    def string(value):
        return value

    @staticmethod
    def scalar(value):
        return value


class InventoryContracts(unittest.TestCase):
    def test_exact_target_optional_contents_and_cycles_remain_unqualified(self):
        native, report = NativeFixture(), {}
        M.collect(native, 42, "exact-title", report)
        self.assertEqual(report["status"], "observed-not-client-geometry-qualified")
        self.assertEqual(len(report["axNodes"]), 2)
        self.assertEqual(report["axNodes"][0]["attributes"]["AXContents"], dict(error=-25205))
        self.assertEqual(report["axNodes"][1]["attributes"]["AXContents"], [1])
        self.assertFalse(report["permissions"]["eventPosting"])
        native.release.assert_any_call("app")
        native.release.assert_any_call("window")
        native.release.assert_any_call("child")

    def test_permissions_rejected_before_any_window_query(self):
        for permission in ("accessibility", "screenRecording"):
            with self.subTest(permission=permission):
                native, report = NativeFixture(), {}
                if permission == "accessibility":
                    native.ax.AXIsProcessTrusted.return_value = False
                else:
                    native.cg.CGPreflightScreenCaptureAccess.return_value = False
                with self.assertRaisesRegex(RuntimeError, "denied.*no prompt"):
                    M.collect(native, 42, "exact-title", report)
                native.windows.assert_not_called()
                native.ax.AXUIElementCreateApplication.assert_not_called()
                self.assertFalse(report["permissions"][permission])

    def test_wrong_or_ambiguous_native_title_fails_before_ax(self):
        for windows in ([], [dict(title="other")], [dict(title="exact-title")] * 2):
            native = NativeFixture()
            native.windows.return_value = windows
            with self.assertRaisesRegex(RuntimeError, "exactly one native"):
                M.collect(native, 42, "exact-title", {})
            native.ax.AXUIElementCreateApplication.assert_not_called()

    def test_foreign_ax_pid_is_rejected_and_application_released(self):
        native = NativeFixture()
        native.pid.return_value = 43
        with self.assertRaisesRegex(RuntimeError, "PID differs"):
            M.collect(native, 42, "exact-title", {})
        native.release.assert_called_once_with("app")

    def test_later_title_failure_releases_already_retained_window(self):
        native = NativeFixture()
        native.values[("app", "AXWindows")] = ["window", "later-window"]
        original = native.attribute

        def failing_attribute(element, name):
            if element == "later-window" and name == "AXTitle":
                raise RuntimeError("later title failure")
            return original(element, name)

        native.attribute = failing_attribute
        with self.assertRaisesRegex(RuntimeError, "later title failure"):
            M.collect(native, 42, "exact-title", {})
        self.assertEqual(native.release.call_args_list.count(mock.call("window")), 1)
        native.release.assert_any_call("app")

    def test_changed_native_window_state_fails_and_retains_both_inventories(self):
        native, report = NativeFixture(), {}
        native.windows.side_effect = [[dict(title="exact-title", windowNumber=1)], [dict(title="exact-title", windowNumber=2)]]
        with self.assertRaisesRegex(RuntimeError, "changed during"):
            M.collect(native, 42, "exact-title", report)
        self.assertNotEqual(report["nativeWindowsBefore"], report["nativeWindowsAfter"])

    def test_tree_budget_fails_instead_of_silently_truncating(self):
        native = NativeFixture()
        with mock.patch.object(M, "MAX_NODES", 1), self.assertRaisesRegex(RuntimeError, "budget"):
            M.collect(native, 42, "exact-title", {})
        native.release.assert_any_call("window")

    def test_depth_budget_fails_instead_of_silently_truncating(self):
        native = NativeFixture()
        with mock.patch.object(M, "MAX_DEPTH", 0), self.assertRaisesRegex(RuntimeError, "budget"):
            M.collect(native, 42, "exact-title", {})

    def test_platform_guard_precedes_framework_loading(self):
        with mock.patch.object(M.sys, "platform", "linux"), mock.patch.object(M.C, "CDLL") as library:
            with self.assertRaisesRegex(RuntimeError, "requires macOS"):
                M.Native()
            library.assert_not_called()

    def test_readonly_surface_never_contains_input_capture_or_permission_request(self):
        source = PATH.read_text()
        for forbidden in ("CGEventPost", "CGEventCreate", "CGRequest", "AXIsProcessTrustedWithOptions",
                          "AXUIElementSetAttributeValue", "AXUIElementPerformAction", "CGWindowListCreateImage",
                          "screencapture", "osascript", "os.kill", "NSRunningApplication"):
            self.assertNotIn(forbidden, source)
        self.assertIn("child.terminate()", source)
        self.assertNotIn("process.terminate()", source)

    def test_owned_inert_child_exit_and_stderr_preserved(self):
        code, output, error, problem = M.bounded_worker([sys.executable, "-c", "import sys; print('fixture'); print('error',file=sys.stderr);sys.exit(7)"])
        self.assertEqual(code, 7)
        self.assertEqual(output, b"fixture\n")
        self.assertEqual(error, b"error\n")
        self.assertIsNone(problem)

    def test_owned_inert_child_timeout_is_bounded(self):
        with mock.patch.object(M, "SECONDS", 0.05):
            code, _, _, problem = M.bounded_worker([sys.executable, "-c", "import time;time.sleep(10)"])
        self.assertIsNone(code)
        self.assertIn("deadline", problem)

    def test_owned_inert_child_output_limit_is_fail_closed(self):
        with mock.patch.object(M, "MAX_BYTES", 8192):
            code, stdout, stderr, problem = M.bounded_worker([sys.executable, "-c", "import sys;sys.stdout.write('x'*32768)"])
        self.assertIsNone(code)
        self.assertIn("output budget", problem)
        self.assertLessEqual(len(stdout) + len(stderr), 8192)


if __name__ == "__main__":
    unittest.main()
