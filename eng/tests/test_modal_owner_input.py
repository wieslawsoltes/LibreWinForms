#!/usr/bin/env python3
"""Authored offline owner-input geometry/identity controls; no native calls."""
import copy
import ctypes as C
import importlib.util
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]


def load(name, file):
    spec = importlib.util.spec_from_file_location(name, ROOT / "eng" / file)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


SHARED = load("owner_input_shared", "librewinforms-popup-desktop.py")
MODAL = load("owner_input_modal", "librewinforms-popup-modal.py")
MAC = load("owner_input_mac", "librewinforms-popup-macos.py")
X11 = load("owner_input_x11", "librewinforms-popup-x11.py")


def fixture():
    owner = dict(id=101, pid=24, title="Exact owner [GUID]", bounds=dict(x=0, y=0, width=400, height=400),
                 client=dict(x=10, y=20, width=380, height=370))
    target = dict(x=20, y=330, width=100, height=30)
    child = dict(id=102, pid=24, bounds=dict(x=80, y=60, width=240, height=220))
    return owner, target, [child, copy.deepcopy(owner)]


class ExposedOwnerGeometry(unittest.TestCase):
    def test_exposure_uses_exact_native_owner_client_and_actual_front_order(self):
        owner, target, inventory = fixture()
        result = SHARED.exposed_owner_target(24, owner, target, inventory)
        self.assertFalse(result["qualified"])
        self.assertEqual(result["point"], [70, 345])
        self.assertEqual(result["frontToOwner"], inventory)

    def test_same_process_dialog_and_foreign_window_are_both_obstructions(self):
        for pid in (24, 99, 0):
            owner, target, inventory = fixture()
            inventory[0].update(pid=pid, bounds=dict(x=119, y=330, width=20, height=30))
            with self.subTest(pid=pid), self.assertRaisesRegex(RuntimeError, "obscured"):
                SHARED.exposed_owner_target(24, owner, target, inventory)

    def test_full_target_not_only_center_must_be_visible_with_half_open_edges(self):
        owner, target, inventory = fixture()
        inventory[0]["bounds"] = dict(x=120, y=330, width=20, height=30)
        SHARED.exposed_owner_target(24, owner, target, inventory)
        inventory[0]["bounds"]["x"] = 119
        with self.assertRaisesRegex(RuntimeError, "obscured"):
            SHARED.exposed_owner_target(24, owner, target, inventory)

    def test_stale_owner_missing_owner_alias_unknown_bounds_and_nonfinite_target_reject(self):
        for mode in range(10):
            owner, target, inventory = fixture()
            if mode == 0: inventory[-1]["title"] = "Foreign title"
            if mode == 1: inventory[-1]["pid"] = 99
            if mode == 2: inventory.pop()
            if mode == 3: inventory.insert(0, copy.deepcopy(inventory[0]))
            if mode == 4: inventory[0]["bounds"] = None
            if mode == 5: target["x"] = float("nan")
            if mode == 6: target["y"] = 389
            if mode == 7: target["width"] = True
            if mode == 8: owner["id"] = True
            if mode == 9: owner["client"]["width"] = 900
            with self.subTest(mode=mode), self.assertRaises(RuntimeError):
                SHARED.exposed_owner_target(24, owner, target, inventory)

    @staticmethod
    def windows_driver(inventories, held=False):
        value = SHARED.WindowsDesktop.__new__(SHARED.WindowsDesktop)
        value.foreground = mock.Mock()
        value.user = mock.Mock()
        value.user.GetAsyncKeyState.return_value = 0x8000 if held else 0
        value.user.SetCursorPos.return_value = 1
        def cursor(pointer):
            point = C.cast(pointer, C.POINTER(SHARED.Point)).contents
            point.x, point.y = 70, 345
            return 1
        value.user.GetCursorPos.side_effect = cursor
        value.owner_obstruction_inventory = mock.Mock(side_effect=inventories)
        value.send_pair = mock.Mock()
        return value

    def test_disabled_win32_owner_does_not_use_window_from_point_or_force_focus(self):
        owner, target, inventory = fixture()
        native = dict(hwnd=owner["id"], title=owner["title"], bounds=owner["bounds"], client=owner["client"], enabled=False)
        value = self.windows_driver([inventory, inventory])
        proof = value.blocked_owner_pointer(24, native, target)
        self.assertFalse(proof["qualified"])
        value.user.WindowFromPoint.assert_not_called()
        value.user.SetForegroundWindow.assert_not_called()
        pair = value.send_pair.call_args.args[0]
        self.assertEqual([item.value.mouse.flags for item in pair], [2, 4])

    def test_win32_new_obstruction_after_movement_prevents_pair(self):
        owner, target, inventory = fixture(); changed = copy.deepcopy(inventory)
        changed[0]["bounds"] = target
        value = self.windows_driver([inventory, changed])
        native = dict(hwnd=owner["id"], title=owner["title"], bounds=owner["bounds"], client=owner["client"])
        with self.assertRaisesRegex(RuntimeError, "obscured"):
            value.blocked_owner_pointer(24, native, target)
        value.user.SetCursorPos.assert_called_once_with(70, 345)
        value.send_pair.assert_not_called()

    def test_win32_held_input_rejects_before_movement_or_observation(self):
        owner, target, inventory = fixture(); value = self.windows_driver([inventory], held=True)
        native = dict(hwnd=owner["id"], title=owner["title"], bounds=owner["bounds"], client=owner["client"])
        with self.assertRaisesRegex(RuntimeError, "held"):
            value.blocked_owner_pointer(24, native, target)
        value.user.SetCursorPos.assert_not_called(); value.send_pair.assert_not_called()
        value.owner_obstruction_inventory.assert_not_called()

    def test_cocoa_stale_or_unverified_owner_never_posts_to_native_helper(self):
        for mode in ("stale", "unverified"):
            owner = dict(windowNumber=101, pid=24, clientGeometryVerified=mode != "unverified")
            value = MAC.MacDesktop.__new__(MAC.MacDesktop)
            value.windows = mock.Mock(return_value=[] if mode == "stale" else [owner])
            value.call = mock.Mock()
            with self.subTest(mode=mode), self.assertRaisesRegex(RuntimeError, "Cocoa owner"):
                value.blocked_owner_pointer(24, owner, dict(x=1, y=2, width=3, height=4))
            value.call.assert_not_called()

    def test_x11_blocking_owner_uses_actual_root_stacking_not_owned_inventory_sort(self):
        owner, _, inventory = fixture()
        source = dict(xid=101, outerFrameXid=201, pid=24, title=owner["title"], bounds=owner["bounds"], client=owner["client"])
        value = X11.X11Desktop.__new__(X11.X11Desktop)
        value.X = SimpleNamespace(IsViewable=2); value.root = mock.Mock(); value.windows = mock.Mock(return_value=[source])
        frame = mock.Mock(id=201); child = mock.Mock(id=102)
        value.root.query_tree.return_value.children = [frame, child]
        for window in (frame, child):
            window.get_attributes.return_value.map_state = 2
            bounds = owner["bounds"] if window is frame else inventory[0]["bounds"]
            window.get_geometry.return_value = SimpleNamespace(root=value.root, border_width=0, width=bounds["width"], height=bounds["height"])
        value.root.translate_coords.side_effect = lambda window, x, y: SimpleNamespace(
            same_screen=True, x=owner["bounds"]["x"] if window is frame else inventory[0]["bounds"]["x"],
            y=owner["bounds"]["y"] if window is frame else inventory[0]["bounds"]["y"])
        value.pid = mock.Mock(return_value=24)
        expected, actual = value.owner_obstruction_inventory(24, source)
        self.assertEqual([entry["id"] for entry in actual], [102, 201])
        self.assertEqual(expected["client"], owner["client"])


if __name__ == "__main__":
    unittest.main()
