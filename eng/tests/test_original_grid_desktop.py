#!/usr/bin/env python3
"""Offline original-grid driver contracts; these tests do not qualify native UI."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("original_grid_driver", ROOT / "eng/librewinforms-original-grid-desktop.py")
DRIVER = importlib.util.module_from_spec(spec)
spec.loader.exec_module(DRIVER)


def snapshot():
    return dict(Schema="original-grid-observer-v1", ProcessId=24, Sequence=1,
                FormTitle=DRIVER.TITLE, Client=[10, 20, 600, 400], GridClient=[30, 80, 560, 300],
                Cell=[45, 95, 120, 21], DeviceDpi=192, FormFocused=True, EditorFocused=False,
                InEdit=False, EditorUtf16=None, FirstNameUtf16=None, CurrentColumn=0,
                CurrentRow=0, SelectionStart=-1, SelectionLength=-1,
                FontName="Segoe UI", FontSize=9, FontHeight=32, AutoScaleX=13, AutoScaleY=32,
                TemplateHeight=21, ActualRowHeight=21, HeaderHeight=46,
                ColumnWidths=[120, 75, 75, 150, 75, 40, 75, 75],
                ColumnHeaders=["Name", "Employee ID", "SSN", "Address", "City", "State", "Zip Code", "Department"])


class OriginalGridContracts(unittest.TestCase):
    def test_observer_installs_no_validation_input_focus_or_grid_handlers(self):
        source = (ROOT / "eng/OriginalGrid/OriginalGridObserver.cs").read_text()
        subscriptions = [line.strip().split(" += ")[0] for line in source.splitlines() if " += " in line]
        self.assertEqual(subscriptions, ["Application.Idle", "Samples.Tick", "Application.ApplicationExit"])
        for forbidden in (".Focus(", ".Activate(", ".BeginEdit(", ".EndEdit(",
                          "CellValidating +=", "RowValidating +=", "DataError +=", "KeyDown +="):
            self.assertNotIn(forbidden, source)
        self.assertIn('Environment.GetEnvironmentVariable("LIBREWINFORMS_GRID_OBSERVER") != "1"', source)
        self.assertIn("grid.VirtualMode", source)
        self.assertIn("++_sequence > 600", source)

    def test_utf16_preserves_surrogate_units(self):
        self.assertEqual(DRIVER.units("A\U0001f600"), [65, 0xD83D, 0xDE00])

    def test_partial_last_line_does_not_hide_last_complete_snapshot(self):
        with tempfile.TemporaryDirectory() as temporary:
            log = Path(temporary) / "stdout.log"
            log.write_bytes(("GRID_EDITING " + json.dumps(snapshot()) + "\r\n").encode() + b"partial \xf0\x9f")
            self.assertEqual(DRIVER.read_snapshot(log, 24), snapshot())

    def test_incomplete_first_snapshot_is_not_accepted(self):
        with tempfile.TemporaryDirectory() as temporary:
            log = Path(temporary) / "stdout.log"
            log.write_text("GRID_EDITING " + json.dumps(snapshot()))
            with self.assertRaises(FileNotFoundError):
                DRIVER.read_snapshot(log, 24)

    def test_wrong_process_schema_sequence_rectangles_and_units_are_rejected(self):
        bad = [("ProcessId", 25), ("Schema", "other"), ("Sequence", 601), ("Sequence", True),
               ("Client", [0, 0, 0, 20]), ("Cell", [0, 0, 99999, 20]),
               ("EditorUtf16", [65536]), ("FirstNameUtf16", [True]), ("InEdit", 1)]
        for key, value in bad:
            with self.subTest(key=key, value=value):
                state = snapshot() | {key: value}
                with self.assertRaises(RuntimeError):
                    DRIVER.validate_snapshot(state, 24)

    def test_layout_comparison_ignores_desktop_origin_but_not_size_or_scaling(self):
        first = snapshot()
        moved = snapshot()
        for key in ("Client", "GridClient", "Cell"):
            moved[key] = [moved[key][0] + 80, moved[key][1] - 10, *moved[key][2:]]
        self.assertEqual(DRIVER.layout_contract(first), DRIVER.layout_contract(moved))
        moved["Client"][2] += 1
        self.assertNotEqual(DRIVER.layout_contract(first), DRIVER.layout_contract(moved))
        for key in ("DeviceDpi", "FontHeight", "ActualRowHeight", "AutoScaleY"):
            changed = snapshot() | {key: first[key] + 1}
            self.assertNotEqual(DRIVER.layout_contract(first), DRIVER.layout_contract(changed))

    def test_complete_payload_manifest_rejects_changed_extra_duplicate_and_escaping_files(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            file = root / "app.dll"
            file.write_bytes(b"original")
            entry = dict(path="app.dll", sha256=DRIVER.digest(file))
            self.assertEqual(DRIVER.verify_files(root, [entry]), {"app.dll": entry["sha256"]})
            for entries in ([entry, entry], [entry | dict(path="../app.dll")], [entry | dict(sha256="bad")]):
                with self.assertRaises(RuntimeError):
                    DRIVER.verify_files(root, entries)
            (root / "unexpected.dll").write_bytes(b"extra")
            with self.assertRaisesRegex(RuntimeError, "not complete"):
                DRIVER.verify_files(root, [entry])
            file.write_bytes(b"changed")
            with self.assertRaisesRegex(RuntimeError, "differs"):
                DRIVER.verify_files(root, [entry])

    def test_expired_or_exited_child_never_receives_input(self):
        for now, exit_code in ((61, None), (1, 1)):
            with self.subTest(now=now, exit_code=exit_code), tempfile.TemporaryDirectory() as temporary:
                desktop = mock.Mock()
                child = mock.Mock(pid=24)
                child.poll.return_value = exit_code
                session = DRIVER.Session(desktop, child, Path(temporary), 0)
                session.state = snapshot()
                with mock.patch.object(DRIVER.time, "monotonic", return_value=now):
                    with self.assertRaises(RuntimeError):
                        session.key(0x0D)
                desktop.assert_not_called()
                self.assertEqual(desktop.mock_calls, [])

    def test_edit_and_commit_predicates_do_not_confuse_editor_text_with_cell_value(self):
        state = snapshot() | dict(InEdit=True, EditorFocused=True, EditorUtf16=DRIVER.units("alice"))
        self.assertTrue(DRIVER.editing(state, "alice"))
        self.assertFalse(DRIVER.committed(state, "alice"))
        state.update(InEdit=False, EditorFocused=False, EditorUtf16=None, FirstNameUtf16=DRIVER.units("alice"))
        self.assertFalse(DRIVER.editing(state, "alice"))
        self.assertTrue(DRIVER.committed(state, "alice"))

    def test_contract_keeps_original_deadlines_and_single_dpi_owner(self):
        source = (ROOT / "eng/librewinforms-original-grid-desktop.py").read_text()
        self.assertIn("started + 60", source)
        self.assertIn("self.started + 20 if startup", source)
        self.assertEqual(source.count("DESKTOP.WindowsDesktop()"), 1)
        self.assertNotIn("SetProcessDpiAwarenessContext", source)
        self.assertNotIn("CellValidating", source)


if __name__ == "__main__":
    unittest.main()
