#!/usr/bin/env python3
"""Offline macOS adapter controls; never compile/run native helpers or applications."""

import copy
import importlib.util
import json
import os
from pathlib import Path
import struct
import subprocess
import tempfile
import time
from types import SimpleNamespace as NS
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("popup_macos", ROOT / "eng/librewinforms-popup-macos.py")
DRIVER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(DRIVER)
FIXTURE_SPEC = importlib.util.spec_from_file_location("geometry_fixture", Path(__file__).with_name("test_popup_native_geometry.py"))
FIXTURE = importlib.util.module_from_spec(FIXTURE_SPEC)
FIXTURE_SPEC.loader.exec_module(FIXTURE)


def desktop(root):
    value = DRIVER.MacDesktop.__new__(DRIVER.MacDesktop)
    value.helper, value.root = root / "native-helper", root
    value.deadline = time.monotonic() + 60
    value.calls = value.bytes = 0
    return value


def reply(action, **extra):
    return NS(returncode=0, stderr=b"", stdout=json.dumps(dict(schema="popup-macos-native-v1", success=True,
                                                               action=action, **extra)).encode())


def windows(mode="Logical", dpi=2, framebuffer=2):
    return DRIVER.combine_windows(*FIXTURE.fixture(mode, dpi, framebuffer), 24)


class MacPopupContracts(unittest.TestCase):
    def test_native_frame_and_typed_client_stay_distinct(self):
        values = FIXTURE.fixture()
        original = copy.deepcopy(values)
        result = DRIVER.combine_windows(*values, 24)
        self.assertEqual(result[0]["bounds"]["y"], 152)
        self.assertEqual(result[0]["nativeClient"]["y"], 180)
        self.assertEqual(result[0]["client"]["y"], 180)
        self.assertTrue(result[0]["clientGeometryVerified"])
        self.assertEqual(values, original)

    def test_private_native_surface_never_gets_fabricated_client(self):
        values = FIXTURE.fixture()
        values[2].append(dict(pid=24, windowNumber=301, title="", frameBounds=dict(x=3, y=5, width=80, height=100)))
        result = DRIVER.combine_windows(*values, 24)
        self.assertIsNone(result[1]["client"])
        self.assertIsNone(result[1]["nativeToSourceScale"])
        self.assertIsNone(result[1]["nativeClient"])
        self.assertFalse(result[1]["clientGeometryVerified"])

    def test_independent_pid_number_frame_and_source_mismatch_reject(self):
        for field, value in (("pid", 99), ("windowNumber", 999), ("frameBounds", dict(x=0, y=0, width=1, height=1))):
            with self.subTest(field=field):
                data = FIXTURE.fixture()
                data[2][0][field] = value
                with self.assertRaises(ValueError): DRIVER.combine_windows(*data, 24)
        data = FIXTURE.fixture()
        data[0]["form"]["client"]["x"] += 1
        with self.assertRaisesRegex(ValueError, "client mismatch"): DRIVER.combine_windows(*data, 24)

    def test_point_uses_declared_device_scale_not_frame_content_ratio(self):
        native = windows("DevicePixels", 2, 2)
        point = DRIVER.native_point(dict(x=-220, y=380, width=40, height=20), native)
        self.assertEqual(point, [-100, 195])
        self.assertNotEqual(native[0]["bounds"]["height"], native[0]["nativeClient"]["height"])

    def test_logical_input_policy_keeps_both_native_scales(self):
        native = windows("Logical", 2, 2)
        self.assertEqual(DRIVER.native_point(dict(x=-110, y=190, width=20, height=10), native), [-100, 195])

    def test_unknown_or_ambiguous_input_mapping_fails(self):
        with self.assertRaisesRegex(RuntimeError, "unambiguous"):
            DRIVER.native_point(dict(x=4000, y=5000, width=20, height=10), windows())
        native = windows()
        alternate = copy.deepcopy(native[0])
        alternate["nativeToSourceScale"] = 2
        alternate["nativeClient"] = dict(x=-1000, y=-1000, width=2000, height=2000)
        with self.assertRaisesRegex(RuntimeError, "unambiguous"):
            DRIVER.native_point(dict(x=-110, y=190, width=20, height=10), native + [alternate])

    def test_native_pointer_and_key_adapter_send_only_shared_semantics(self):
        with tempfile.TemporaryDirectory() as temporary:
            value = desktop(Path(temporary))
            value.windows = mock.Mock(return_value=windows("DevicePixels", 2, 2))
            value.call = mock.Mock()
            value.pointer(24, dict(x=-220, y=380, width=40, height=20), "right")
            value.call.assert_called_once_with("pointer", pid=24, point=[-100, 195], click="right")
            for shared, native in DRIVER.MacDesktop.KEYS.items():
                value.key(24, shared)
                value.call.assert_called_with("key", pid=24, key=native)
            with self.assertRaises(RuntimeError): value.key(24, 0x41)

    def test_only_typed_main_can_request_activation(self):
        with tempfile.TemporaryDirectory() as temporary:
            value = desktop(Path(temporary))
            value.call = mock.Mock()
            native = windows()[0]
            value.activate(native)
            value.call.assert_called_once_with("activate", pid=24, windowNumber=300)
            native["sourceName"] = None
            with self.assertRaises(RuntimeError): value.activate(native)

    def test_expired_deadline_rejects_before_native_process(self):
        with tempfile.TemporaryDirectory() as temporary, mock.patch.object(DRIVER.subprocess, "run") as run:
            value = desktop(Path(temporary))
            value.deadline = 0
            with self.assertRaisesRegex(RuntimeError, "deadline"): value.call("inventory", pid=24)
            run.assert_not_called()

    def test_failed_native_preflight_is_retained_and_not_promoted(self):
        with tempfile.TemporaryDirectory() as temporary, mock.patch.object(DRIVER.subprocess, "run") as run:
            value = desktop(Path(temporary))
            run.return_value = NS(returncode=1, stdout=b'{"schema":"popup-macos-native-v1","success":false,"error":"AX -25204"}', stderr=b"original")
            with self.assertRaisesRegex(RuntimeError, "AX -25204"): value.call("preflight")
            self.assertEqual((value.root / "native-call-0001.stderr.log").read_bytes(), b"original")
            self.assertFalse(json.loads((value.root / "native-call-0001.stdout.json").read_text())["success"])

    def test_native_reply_requires_exact_action_pid_and_exit(self):
        for result in (reply("key", pid=24), reply("inventory", pid=99), reply("inventory", pid=True)):
            with self.subTest(result=result), tempfile.TemporaryDirectory() as temporary:
                value = desktop(Path(temporary))
                with mock.patch.object(DRIVER.subprocess, "run", return_value=result):
                    with self.assertRaises(RuntimeError): value.call("inventory", pid=24)
        with tempfile.TemporaryDirectory() as temporary:
            value = desktop(Path(temporary))
            result = reply("inventory", pid=24)
            result.returncode = 9
            with mock.patch.object(DRIVER.subprocess, "run", return_value=result):
                with self.assertRaises(RuntimeError): value.call("inventory", pid=24)

    def test_duplicate_native_reply_keys_reject(self):
        with tempfile.TemporaryDirectory() as temporary:
            value = desktop(Path(temporary))
            result = NS(returncode=0, stderr=b"", stdout=b'{"schema":"popup-macos-native-v1","success":false,"success":true,"action":"preflight"}')
            with mock.patch.object(DRIVER.subprocess, "run", return_value=result):
                with self.assertRaisesRegex(ValueError, "Duplicate"): value.call("preflight")

    def test_helper_timeout_retains_partial_output_with_original_deadline(self):
        with tempfile.TemporaryDirectory() as temporary:
            value = desktop(Path(temporary))
            value.deadline = time.monotonic() + 0.2
            failure = subprocess.TimeoutExpired(["helper"], 0.2, output=b"partial", stderr=b"diagnostic")
            with mock.patch.object(DRIVER.subprocess, "run", side_effect=failure) as run:
                with self.assertRaises(subprocess.TimeoutExpired): value.call("capture", pid=24)
                self.assertLessEqual(run.call_args.kwargs["timeout"], 0.2)
            self.assertEqual((value.root / "native-call-0001.stdout.json").read_bytes(), b"partial")

    def test_helper_request_and_receipt_files_are_never_overwritten(self):
        with tempfile.TemporaryDirectory() as temporary, mock.patch.object(DRIVER.subprocess, "run") as run:
            value = desktop(Path(temporary))
            path = value.root / "native-call-0001.request.json"
            path.write_text("retain")
            with self.assertRaises(FileExistsError): value.call("preflight")
            run.assert_not_called()
            self.assertEqual(path.read_text(), "retain")

    def test_source_and_native_snapshots_use_exact_matching_sequence(self):
        with tempfile.TemporaryDirectory() as temporary:
            value = desktop(Path(temporary))
            app = value.root / "portable/app"
            app.mkdir(parents=True)
            source, sidecar, inventory = FIXTURE.fixture()
            (app / "snapshot-00000007.json").write_text(json.dumps(source))
            (app / "native-geometry-00000007.json").write_text(json.dumps(sidecar))
            value.call = mock.Mock(return_value=dict(windows=inventory))
            self.assertEqual(value.windows(24), windows())
            sidecar["sequence"] = 8
            (app / "native-geometry-00000007.json").write_text(json.dumps(sidecar))
            with self.assertRaisesRegex(ValueError, "Sequence"): value.windows(24)

    def test_capture_retains_actual_returned_retina_dimensions_and_raw_native_frame(self):
        native = windows()
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "native.bmp"
            width, height = 840, 576
            data = bytes(width * height * 4)
            path.write_bytes(struct.pack("<2sIHHI", b"BM", len(data) + 54, 0, 0, 54) +
                             struct.pack("<IiiHHIIiiII", 40, width, height, 1, 32, 0, len(data), 0, 0, 0, 0) + data)
            image = dict(captureProvider="ScreenCaptureKit.captureImage(in:)", encoding="ImageIO BMP, no resizing",
                         sourceRect=native[0]["bounds"], width=width, height=height,
                         returnedPixelsPerPointX=2, returnedPixelsPerPointY=2, encodedBytes=path.stat().st_size)
            result = DRIVER.check_capture(image, path, native)
            self.assertEqual((result["width"], result["height"]), (840, 576))
            self.assertFalse(result["qualified"])
            self.assertFalse(result["usableWindowPixelsVerified"])
            self.assertFalse(result["privateSurfaceClientGeometryVerified"])
            for key, bad in (("width", 420), ("returnedPixelsPerPointX", 1), ("encodedBytes", 54),
                             ("captureProvider", "CGWindowListCreateImage")):
                with self.subTest(key=key), self.assertRaises(RuntimeError):
                    DRIVER.check_capture(dict(image, **{key: bad}), path, native)

    def test_runtime_observer_optin_restores_callers_environment_on_failure(self):
        for initial in (None, "caller"):
            with mock.patch.dict(os.environ, {}, clear=True):
                if initial is not None: os.environ[DRIVER.ENVIRONMENT] = initial
                with self.assertRaises(ValueError):
                    with DRIVER.observer_environment():
                        self.assertEqual(os.environ[DRIVER.ENVIRONMENT], "1")
                        raise ValueError("original")
                self.assertEqual(os.environ.get(DRIVER.ENVIRONMENT), initial)

    def test_preflight_failure_leaves_structured_no_launch_receipt(self):
        with tempfile.TemporaryDirectory() as temporary:
            parent = Path(temporary)
            app = parent / "app"
            app.touch()
            helper = parent / "helper"
            helper.touch()
            arguments = ["driver", "--portable-app", str(app), "--prepared-root", str(parent),
                         "--native-helper", str(helper), "--evidence-parent", str(parent)]
            with mock.patch.object(DRIVER.sys, "argv", arguments), mock.patch.object(DRIVER, "check_preparation", return_value={}), \
                 mock.patch.object(DRIVER, "MacDesktop", side_effect=RuntimeError("permission unavailable")), \
                 mock.patch.object(DRIVER.SHARED, "run_case") as run:
                with self.assertRaisesRegex(RuntimeError, "permission"): DRIVER.main()
                run.assert_not_called()
            roots = list(parent.glob("popup-macos-interaction-*"))
            self.assertEqual(len(roots), 1)
            receipt = json.loads((roots[0] / "driver-failure.json").read_text())
            self.assertTrue(receipt["appNotLaunched"])
            self.assertFalse(receipt["qualified"])

    def test_native_source_guards_no_permission_prompt_focus_or_obsolete_capture(self):
        source = (ROOT / "eng/PopupDesktopNative.swift").read_text()
        for forbidden in ("CGRequest", "AXIsProcessTrustedWithOptions", "CGWindowListCreateImage", "AXUIElementSetAttributeValue",
                          "CGWarpMouseCursorPosition", "image.width =", "configuration.width"):
            self.assertNotIn(forbidden, source)
        for required in ("AXUIElementCreateSystemWide()", "AXUIElementSetMessagingTimeout(system, 0.25)",
                         "CGEventSource.keyState(.hidSystemState", "try hitOwner(point, pid)",
                         "SCScreenshotManager.captureImage(in: rectangle)", ".withoutOverwriting",
                         "try inventory(pid) == before", "NSWorkspace.shared.frontmostApplication"):
            self.assertIn(required, source)

    def test_adapter_reuses_shared_scenario_and_child_cleanup_unchanged(self):
        source = (ROOT / "eng/librewinforms-popup-macos.py").read_text()
        self.assertIn('SHARED.run_case(desktop, app, root, "portable", uuid.uuid4().hex)', source)
        self.assertNotIn("def scenario", source)
        self.assertNotIn("Popen(", source)
        self.assertEqual(DRIVER.SHARED.scenario.__code__.co_filename,
                         str(ROOT / "eng/librewinforms-popup-desktop.py"))


if __name__ == "__main__":
    unittest.main()
