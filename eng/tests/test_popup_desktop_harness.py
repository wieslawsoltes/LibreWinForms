#!/usr/bin/env python3
"""Offline harness contracts: no SDK build, native API, application, or input."""

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import xml.etree.ElementTree as ET
import zipfile


ROOT = Path(__file__).resolve().parents[2]


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / "eng" / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


PREPARE = load("popup_prepare", "librewinforms-prepare-popup-desktop.py")
DRIVER = load("popup_driver", "librewinforms-popup-desktop.py")


class PopupDesktopContracts(unittest.TestCase):
    @staticmethod
    def desktop_with_held_key(key):
        class Native:
            def __init__(self):
                self.mutations = []

            def GetForegroundWindow(self):
                return 1

            def GetAsyncKeyState(self, requested):
                return 0x8000 if requested == key else 0

            def WindowFromPoint(self, point):
                return 1

            def SetCursorPos(self, x, y):
                self.mutations.append("move")
                return 1

            def SendInput(self, count, inputs, size):
                self.mutations.append("input")
                return count

        desktop = DRIVER.WindowsDesktop.__new__(DRIVER.WindowsDesktop)
        desktop.user = Native()
        desktop.pid = lambda _: 24
        return desktop

    def test_held_buttons_or_modifiers_prevent_hover_and_click_before_any_native_mutation(self):
        for key in (1, 2, 4, 5, 6, 0x10, 0x11, 0x12, 0x5B, 0x5C):
            for button in (None, "left", "right"):
                with self.subTest(key=key, button=button):
                    desktop = self.desktop_with_held_key(key)
                    with self.assertRaisesRegex(RuntimeError, "held"):
                        desktop.pointer(24, dict(x=10, y=20, width=30, height=40), button)
                    self.assertEqual(desktop.user.mutations, [])

    def test_requested_physical_key_already_held_prevents_pair_injection(self):
        for key in (0x79, 0x28, 0x0D, 0x1B, 0x12):
            with self.subTest(key=key):
                desktop = self.desktop_with_held_key(key)
                with self.assertRaisesRegex(RuntimeError, "physical key.*held"):
                    desktop.key(24, key)
                self.assertEqual(desktop.user.mutations, [])

    def test_pair_staging_preserves_exact_source_and_scoped_versions(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for name in ("LibreWinForms.Sdk", "LibreWinForms.System.Windows.Forms", "LibreWinForms.ProGPU"):
                with zipfile.ZipFile(root / f"{name}.1.2.3.nupkg", "w") as archive:
                    archive.writestr(f"{name}.nuspec", f"<package><metadata><id>{name}</id><version>1.2.3</version></metadata></package>")
            staged = root / "staged"
            receipt = PREPARE.prepare(staged, root, "1.2.3", "1.2.3", "1.2.3", "11.0.100-preview.5.26302.115")
            self.assertFalse(receipt["qualified"])
            for mode in ("Microsoft", "Portable"):
                self.assertEqual((staged / mode / "Program.cs").read_bytes(), (PREPARE.SOURCE / "Program.cs").read_bytes())
            self.assertIn("LibreWinForms.Sdk/1.2.3", (staged / "Portable/PopupInteractionApp.csproj").read_text())
            self.assertIn('Sdk="Microsoft.NET.Sdk"', (staged / "Microsoft/PopupInteractionApp.csproj").read_text())
            for mode, tfm in (("Microsoft", "net11.0-windows"), ("Portable", "net11.0")):
                project = ET.parse(staged / mode / "PopupInteractionApp.csproj")
                self.assertEqual(project.findtext("PropertyGroup/TargetFramework"), tfm)
            with self.assertRaisesRegex(ValueError, "must be new"):
                PREPARE.prepare(staged, root, "1.2.3", "1.2.3", "1.2.3", "11.0.100")

    def test_driver_accepts_only_current_consumer_framework_output_paths(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = (PREPARE.SOURCE / "Program.cs").read_bytes()
            manifest = dict(schema="popup-interaction-preparation-v1", sourceSha256=DRIVER.digest(PREPARE.SOURCE / "Program.cs"))
            (root / "preparation.json").write_text(json.dumps(manifest))
            current = []
            previous = []
            for mode, suffix in (("Microsoft", "-windows"), ("Portable", "")):
                project = root / mode
                project.mkdir()
                (project / "Program.cs").write_bytes(source)
                for version, outputs in (("11", current), ("10", previous)):
                    output = project / f"bin/Release/net{version}.0{suffix}/PopupInteractionApp.exe"
                    output.parent.mkdir(parents=True)
                    output.write_bytes(b"inert output-path fixture; never executed")
                    outputs.append(output.resolve())
            self.assertEqual(DRIVER.check_preparation(root, *current), manifest)
            for reference, portable in ((previous[0], current[1]), (current[0], previous[1])):
                with self.subTest(reference=reference, portable=portable):
                    with self.assertRaisesRegex(RuntimeError, "explicit prepared consumer output"):
                        DRIVER.check_preparation(root, reference, portable)

    def test_wrong_package_identity_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with zipfile.ZipFile(root / "LibreWinForms.Sdk.1.2.3.nupkg", "w") as archive:
                archive.writestr("wrong.nuspec", "<package><metadata><id>Other</id><version>1.2.3</version></metadata></package>")
            with self.assertRaisesRegex(ValueError, "ID/version"):
                PREPARE.package(root, "LibreWinForms.Sdk", "1.2.3")

    def test_stability_ignores_paint_frequency_but_not_geometry_or_commands(self):
        before = dict(sequence=1, elapsedMs=100, counts={"form-paint": 2, "command": 0}, client={"x": 10})
        after = dict(sequence=2, elapsedMs=200, counts={"form-paint": 3, "command": 0}, client={"x": 10})
        self.assertEqual(DRIVER.stable_state(before), DRIVER.stable_state(after))
        after["client"]["x"] = 11
        self.assertNotEqual(DRIVER.stable_state(before), DRIVER.stable_state(after))
        after["client"]["x"] = 10
        after["counts"]["command"] = 1
        self.assertNotEqual(DRIVER.stable_state(before), DRIVER.stable_state(after))

    def test_wrong_pid_snapshot_fails_before_native_calls(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            (directory / "app").mkdir()
            (directory / "app/snapshot-00000001.json").write_text(json.dumps(dict(pid=23, title="PopupInteractionApp [run]")))

            class Process:
                pid = 24
                returncode = None
                def poll(self):
                    return None

            session = DRIVER.Session(None, Process(), directory, "run")
            with self.assertRaisesRegex(RuntimeError, "identity"):
                session.wait(lambda _: True)

    def test_expired_deadline_does_not_start_more_input_or_native_queries(self):
        session = DRIVER.Session(None, None, Path("unused"), "run")
        session.deadline = 0
        with self.assertRaisesRegex(TimeoutError, "60-second"):
            session.wait(lambda _: True)
        with self.assertRaisesRegex(RuntimeError, "deadline"):
            session.key(0x79)

    def test_geometry_rejection_retains_actual_candidate_without_input_or_capture(self):
        source_client = dict(x=1516, y=835, width=560, height=250)
        native_client = dict(x=3032, y=1670, width=1120, height=500)
        for popup_failure in (False, True):
            with self.subTest(popup=popup_failure):
                process = mock.Mock(pid=24, returncode=None)
                process.poll.return_value = None
                desktop = mock.Mock()
                windows = [dict(hwnd=99, title="PopupInteractionApp [run]",
                                client=source_client if popup_failure else native_client)]
                desktop.windows.return_value = windows
                state = dict(pid=24, title="PopupInteractionApp [run]", sequence=1, counts={},
                             form=dict(client=source_client), popups={})
                if popup_failure:
                    state["popups"]["context"] = dict(visible=True, client=dict(x=10, y=20, width=80, height=60))
                later = dict(state, sequence=2)
                session = DRIVER.Session(desktop, process, Path("unused"), "run")
                expected = "Visible source popup lacks" if popup_failure else "Source/native main client geometry differs"
                with mock.patch.object(DRIVER, "read_snapshot", side_effect=[state, later]), mock.patch.object(DRIVER.time, "sleep"):
                    with self.assertRaisesRegex(RuntimeError, expected):
                        session.wait(lambda _: True)
                self.assertIsNone(session.state)
                self.assertEqual(session.failure_state["pid"], 24)
                self.assertEqual(session.failure_state["snapshot"], later)
                self.assertEqual(session.failure_state["nativeWindows"], windows)
                self.assertFalse(session.failure_state["qualified"])
                self.assertEqual(desktop.mock_calls, [mock.call.windows(24)])

    def test_geometry_failure_receipt_remains_incomplete_and_contains_rejected_candidate(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            executable = root / "PopupInteractionApp.exe"
            executable.write_bytes(b"inert fixture; never executed")
            executable.with_suffix(".dll").write_bytes(b"inert fixture")
            process = mock.Mock(pid=24, returncode=1)
            process.poll.return_value = 1
            state = dict(pid=24, form=dict(client=dict(x=10, y=20, width=30, height=40)))
            windows = [dict(hwnd=99, client=dict(x=20, y=40, width=60, height=80))]
            desktop = mock.Mock()
            with mock.patch.object(DRIVER.subprocess, "Popen", return_value=process), \
                    mock.patch.object(DRIVER, "scenario", side_effect=lambda session: session.reject_geometry("geometry mismatch", state, windows)):
                self.assertFalse(DRIVER.run_case(desktop, executable, root, "portable", "run"))
            receipt = json.loads((root / "portable/receipt.json").read_text())
            self.assertEqual(receipt["status"], "incomplete")
            self.assertEqual(receipt["error"], "RuntimeError: geometry mismatch")
            self.assertEqual(receipt["failureState"]["snapshot"], state)
            self.assertEqual(receipt["failureState"]["nativeWindows"], windows)
            self.assertFalse(receipt["qualified"])
            self.assertEqual(desktop.mock_calls, [])

    def test_oversized_geometry_evidence_fails_without_unbounded_receipt(self):
        session = DRIVER.Session(None, mock.Mock(pid=24), Path("unused"), "run")
        with self.assertRaisesRegex(RuntimeError, "original mismatch"):
            session.reject_geometry("original mismatch", dict(text="x" * (256 * 1024)), [])
        self.assertIn("256 KiB", session.failure_state["evidenceError"])
        self.assertLess(len(json.dumps(session.failure_state).encode("utf-8")), 1024)
        self.assertIsNone(session.state)

    def test_observer_does_not_drive_controls_or_change_validation_policy(self):
        source = (PREPARE.SOURCE / "Program.cs").read_text()
        for forbidden in (".Focus(", ".PerformClick(", ".ShowDropDown(", ".BeginInvoke(",
                          ".DataError +=", ".Validating +=", ".Validation +=", "ActiveControl =", "SendKeys", "SendMessage"):
            self.assertNotIn(forbidden, source)
        snapshot = source.split("private void Snapshot()", 1)[1].split("protected override void Dispose", 1)[0]
        self.assertNotIn(".Text =", snapshot)
        self.assertIn("control.IsHandleCreated && control.Visible", source)


if __name__ == "__main__":
    unittest.main()
