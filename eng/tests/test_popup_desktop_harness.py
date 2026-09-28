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

    def test_navigation_pairs_preserve_extended_physical_key_identity(self):
        # Win32 extended scan identities distinguish the dedicated navigation
        # cluster from numeric keypad keys. Native callbacks need both bytes.
        cases = ((0x21, 0xE049), (0x22, 0xE051), (0x23, 0xE04F), (0x24, 0xE047),
                 (0x25, 0xE04B), (0x26, 0xE048), (0x27, 0xE04D), (0x28, 0xE050),
                 (0x2D, 0xE052), (0x2E, 0xE053), (0x79, 0x44), (0x1B, 0x01),
                 (0x12, 0x38), (0x0D, 0x1C), (0x62, 0x50))
        for key, scan in cases:
            with self.subTest(key=key):
                desktop = self.desktop_with_held_key(None)
                desktop.user.MapVirtualKeyW = mock.Mock(return_value=scan)
                desktop.send_pair = mock.Mock()
                desktop.key(24, key)
                desktop.user.MapVirtualKeyW.assert_called_once_with(key, 4)
                pair = desktop.send_pair.call_args.args[0]
                extended = 1 if scan >> 8 == 0xE0 else 0
                self.assertEqual([(p.kind, p.value.key.key, p.value.key.scan, p.value.key.flags) for p in pair],
                                 [(1, key, scan & 0xFF, extended), (1, key, scan & 0xFF, extended | 2)])

    def test_unmapped_or_unsupported_scan_prefix_does_not_inject_input(self):
        for scan in (0, 0xE11D, 0xE250):
            with self.subTest(scan=scan):
                desktop = self.desktop_with_held_key(None)
                desktop.user.MapVirtualKeyW = mock.Mock(return_value=scan)
                with self.assertRaisesRegex(RuntimeError, "scan"):
                    desktop.key(24, 0x28)
                self.assertEqual(desktop.user.mutations, [])

    def test_navigation_cluster_remains_extended_when_windows_returns_a_keypad_scan_alias(self):
        # Observed MAPVK_VK_TO_VSC_EX results on the Windows ARM64 guest's US
        # layout omit E0 for all ten dedicated navigation virtual keys.
        cases = ((0x21, 0x49), (0x22, 0x51), (0x23, 0x4F), (0x24, 0x47),
                 (0x25, 0x4B), (0x26, 0x48), (0x27, 0x4D), (0x28, 0x50),
                 (0x2D, 0x52), (0x2E, 0x53))
        for key, scan in cases:
            with self.subTest(key=key):
                desktop = self.desktop_with_held_key(None)
                desktop.user.MapVirtualKeyW = mock.Mock(return_value=scan)
                desktop.send_pair = mock.Mock()
                desktop.key(24, key)
                pair = desktop.send_pair.call_args.args[0]
                self.assertEqual([(p.value.key.key, p.value.key.scan, p.value.key.flags) for p in pair],
                                 [(key, scan, 1), (key, scan, 3)])

    def test_ordinary_and_keypad_keys_do_not_inherit_navigation_cluster_flags(self):
        for key, scan in ((0x62, 0x50), (0x68, 0x48), (0x0D, 0x1C), (0x12, 0x38),
                          (0x79, 0x44), (0x1B, 0x01), (0x41, 0x1E)):
            with self.subTest(key=key):
                desktop = self.desktop_with_held_key(None)
                desktop.user.MapVirtualKeyW = mock.Mock(return_value=scan)
                desktop.send_pair = mock.Mock()
                desktop.key(24, key)
                pair = desktop.send_pair.call_args.args[0]
                self.assertEqual([(p.value.key.key, p.value.key.scan, p.value.key.flags) for p in pair],
                                 [(key, scan, 0), (key, scan, 2)])

    def test_partial_extended_pair_releases_only_the_same_injected_key(self):
        desktop = self.desktop_with_held_key(None)
        desktop.user.MapVirtualKeyW = mock.Mock(return_value=0xE050)
        submitted = []
        def send(count, inputs, size):
            self.assertEqual(size, DRIVER.C.sizeof(DRIVER.Input))
            submitted.append([(inputs[i].value.key.key, inputs[i].value.key.scan,
                               inputs[i].value.key.flags) for i in range(count)])
            return 1
        desktop.user.SendInput = send
        with self.assertRaisesRegex(RuntimeError, "complete input pair"):
            desktop.key(24, 0x28)
        self.assertEqual(submitted, [[(0x28, 0x50, 1), (0x28, 0x50, 3)], [(0x28, 0x50, 3)]])

    def test_pair_staging_preserves_exact_source_and_scoped_versions(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for name in ("LibreWinForms.Sdk", "LibreWinForms.System.Windows.Forms", "LibreWinForms.ProGPU"):
                with zipfile.ZipFile(root / f"{name}.1.2.3.nupkg", "w") as archive:
                    archive.writestr(f"{name}.nuspec", f"<package><metadata><id>{name}</id><version>1.2.3</version></metadata></package>")
            staged = root / "staged"
            receipt = PREPARE.prepare(staged, root, "1.2.3", "1.2.3", "1.2.3", "11.0.100-preview.5.26302.115")
            self.assertFalse(receipt["qualified"])
            self.assertFalse(receipt["nativeGeometry"]["enabled"])
            self.assertIsNone(receipt["nativeGeometry"]["sourceSha256"])
            self.assertFalse((staged / "Portable/PortableNativeGeometryObserver.cs").exists())
            for mode in ("Microsoft", "Portable"):
                self.assertEqual((staged / mode / "Program.cs").read_bytes(), (PREPARE.SOURCE / "Program.cs").read_bytes())
            self.assertIn("LibreWinForms.Sdk/1.2.3", (staged / "Portable/PopupInteractionApp.csproj").read_text())
            self.assertIn('Sdk="Microsoft.NET.Sdk"', (staged / "Microsoft/PopupInteractionApp.csproj").read_text())
            for mode, tfm in (("Microsoft", "net11.0-windows"), ("Portable", "net11.0")):
                project = ET.parse(staged / mode / "PopupInteractionApp.csproj")
                self.assertEqual(project.findtext("PropertyGroup/TargetFramework"), tfm)
            with self.assertRaisesRegex(ValueError, "must be new"):
                PREPARE.prepare(staged, root, "1.2.3", "1.2.3", "1.2.3", "11.0.100")

    def test_native_geometry_is_explicit_portable_only_with_exact_source_receipt(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for name in ("LibreWinForms.Sdk", "LibreWinForms.System.Windows.Forms", "LibreWinForms.ProGPU"):
                with zipfile.ZipFile(root / f"{name}.1.2.3.nupkg", "w") as archive:
                    archive.writestr(f"{name}.nuspec", f"<package><metadata><id>{name}</id><version>1.2.3</version></metadata></package>")
            staged = root / "staged"
            receipt = PREPARE.prepare(staged, root, "1.2.3", "1.2.3", "1.2.3", "11.0.100", native_geometry=True)
            observer = receipt["nativeGeometry"]
            self.assertTrue(observer["enabled"])
            self.assertEqual(observer["environmentVariable"], "LIBREWINFORMS_POPUP_NATIVE_GEOMETRY")
            self.assertEqual(observer["sourcePath"], "Portable/PortableNativeGeometryObserver.cs")
            self.assertEqual(observer["sourceSha256"], PREPARE.sha256(PREPARE.SOURCE / "PortableNativeGeometryObserver.cs"))
            self.assertEqual(observer["sourceSha256"], PREPARE.sha256(staged / observer["sourcePath"]))
            self.assertFalse((staged / "Microsoft/PortableNativeGeometryObserver.cs").exists())
            for mode in ("Microsoft", "Portable"):
                self.assertEqual((staged / mode / "Program.cs").read_bytes(), (PREPARE.SOURCE / "Program.cs").read_bytes())
                project = ET.parse(staged / mode / "PopupInteractionApp.csproj")
                self.assertEqual(project.findtext("PropertyGroup/PopupNativeGeometryDiagnostics"),
                                 "true" if mode == "Portable" else None)
            self.assertFalse(receipt["qualified"])
            self.assertEqual(len(receipt["packages"]), 3)

    def test_native_observer_copy_corruption_is_not_published_as_preparation(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for name in ("LibreWinForms.Sdk", "LibreWinForms.System.Windows.Forms", "LibreWinForms.ProGPU"):
                with zipfile.ZipFile(root / f"{name}.1.2.3.nupkg", "w") as archive:
                    archive.writestr(f"{name}.nuspec", f"<package><metadata><id>{name}</id><version>1.2.3</version></metadata></package>")
            staged = root / "staged"
            copy = PREPARE.shutil.copyfile
            def corrupt_observer(source, target):
                copy(source, target)
                if source.name == "PortableNativeGeometryObserver.cs":
                    target.write_bytes(b"changed observer fixture")
            with mock.patch.object(PREPARE.shutil, "copyfile", side_effect=corrupt_observer):
                with self.assertRaisesRegex(ValueError, "observer copy changed"):
                    PREPARE.prepare(staged, root, "1.2.3", "1.2.3", "1.2.3", "11.0.100", native_geometry=True)
            self.assertFalse((staged / "preparation.json").exists())

    def test_native_observer_is_read_only_optional_and_precedes_same_sequence_publication(self):
        shared = (PREPARE.SOURCE / "Program.cs").read_text()
        observer = (PREPARE.SOURCE / "PortableNativeGeometryObserver.cs").read_text()
        self.assertIn("internal sealed partial class InteractionForm", shared)
        self.assertIn("partial void RecordNativeGeometry(long sequence);", shared)
        self.assertEqual(shared.count("RecordNativeGeometry(_sequence);"), 1)
        self.assertLess(shared.index("RecordNativeGeometry(_sequence);"), shared.index('File.Move(pending, Path.Combine(_directory, $"snapshot-'))
        self.assertIn('Environment.GetEnvironmentVariable("LIBREWINFORMS_POPUP_NATIVE_GEOMETRY") != "1"', observer)
        self.assertIn('FileMode.CreateNew', observer)
        self.assertIn('native-geometry-{sequence:D8}.json', observer)
        self.assertIn('sequence is < 1 or > 650', observer)
        self.assertIn('bytes.Length > 32 * 1024', observer)
        for name in ("main", "context", "context-child", "menu", "menu-child"):
            self.assertIn(f'ObserveNativeGeometry("{name}",', observer)
        for forbidden in (".Focus(", ".Show(", ".Hide(", ".Activate(", "new Timer", "Thread.Sleep",
                          "BeginInvoke", "GetType(", "GetMethod(", "CreateHandle(", "_combo", "_tip",
                          ".PointToScreen(", ".Apply(", ".Attach("):
            self.assertNotIn(forbidden, observer)
        project = ET.parse(PREPARE.SOURCE / "Portable.csproj")
        entries = [group for group in project.findall("ItemGroup")
                   if group.find("Compile[@Include='PortableNativeGeometryObserver.cs']") is not None]
        self.assertEqual(len(entries), 1)
        self.assertEqual(entries[0].get("Condition"), "'$(PopupNativeGeometryDiagnostics)' == 'true'")
        self.assertNotIn("PortableNativeGeometryObserver", (PREPARE.SOURCE / "Microsoft.csproj").read_text())

    def test_native_observer_keeps_raw_geometry_and_checks_exact_typed_policy_identity(self):
        observer = (PREPARE.SOURCE / "PortableNativeGeometryObserver.cs").read_text()
        self.assertLess(observer.index("if (!handleCreated || control.IsDisposed || control.Disposing)"),
                        observer.index("nint handle = control.Handle;"))
        for contract in ("platform.Windows is not SilkWindowService service",
                         "platform.Handles.TryGet(token, out ILibreWindow? window)",
                         "service.TryGetNativeGeometrySnapshot(token, out NativeWindowGeometrySnapshot snapshot)",
                         "control.Handle != handle", "!ReferenceEquals(current, window)",
                         "current.Handle != token", "currentMode != mode",
                         "currentDpi != dpi", "currentFramebuffer != framebuffer",
                         "snapshot.Window.Kind == NativeWindowKind.Cocoa && snapshot.BackingScale == framebuffer",
                         "contentBounds = NativeBounds(snapshot.ContentBounds)",
                         "frameBounds = NativeBounds(snapshot.FrameBounds)",
                         "backingScale = snapshot.BackingScale",
                         "new { x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height }"):
            self.assertIn(contract, observer)
        self.assertNotIn("Math.Round", observer)
        self.assertNotIn(" / framebuffer", observer)
        self.assertNotIn(" * framebuffer", observer)

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

    def test_design_sized_children_are_attached_before_canonical_autoscale_resumes(self):
        source = (PREPARE.SOURCE / "Program.cs").read_text()
        constructor = source.split("internal InteractionForm(string directory, string run)", 1)[1].split("private void RegisterPopup", 1)[0]
        ordered = ("SuspendLayout();", "AutoScaleDimensions = new(96, 96);",
                   "AutoScaleMode = AutoScaleMode.Dpi;", "ClientSize = new(560, 250);",
                   "Controls.AddRange([_editor, _contextTarget, _combo, _tipTarget, _menu]);",
                   "ResumeLayout(false);", "PerformLayout();", "_observer.Start();")
        positions = [constructor.index(statement) for statement in ordered]
        self.assertEqual(positions, sorted(positions))
        for statement in ordered:
            self.assertEqual(constructor.count(statement), 1)
        for manual_scaling in ("DeviceDpi", ".Scale(", "PerformAutoScale(", "LogicalToDeviceUnits("):
            self.assertNotIn(manual_scaling, constructor)
        self.assertIn('Text = "Right-click for context menu", Location = new(24, 108), Size = new(240, 36)', source)


if __name__ == "__main__":
    unittest.main()
