#!/usr/bin/env python3
"""Offline harness contracts: no SDK build, native API, application, or input."""

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
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
            with self.assertRaisesRegex(ValueError, "must be new"):
                PREPARE.prepare(staged, root, "1.2.3", "1.2.3", "1.2.3", "11.0.100")

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
