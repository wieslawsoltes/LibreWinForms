#!/usr/bin/env python3
"""Offline X11 driver contracts. No Xlib import, server, native input or app launch."""

import importlib.util
import json
from pathlib import Path
import struct
import tempfile
import time
from types import SimpleNamespace as NS
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("popup_x11", ROOT / "eng/librewinforms-popup-x11.py")
DRIVER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(DRIVER)


def desktop():
    value = DRIVER.X11Desktop.__new__(DRIVER.X11Desktop)
    value.deadline = time.monotonic() + 60
    value.X = NS(KeyPress=2, KeyRelease=3, ButtonPress=4, ButtonRelease=5, MotionNotify=6, IsViewable=2)
    value.display = mock.Mock()
    value.display.query_keymap.return_value = [0] * 32
    value.display.get_modifier_mapping.return_value = [[50], [], [37], [64], [], [], [133], []]
    value.display.get_pointer_mapping.return_value = [1, 2, 3, 4, 5]
    value.root = mock.Mock()
    value.root.query_pointer.return_value = NS(same_screen=True, mask=0, root_x=15, root_y=25)
    value.screen = NS(width_in_pixels=1920, height_in_pixels=1080)
    value.foreground = mock.Mock()
    value.point_owner = mock.Mock(return_value=24)
    value.errors = []
    return value


class X11PopupContracts(unittest.TestCase):
    def test_held_modifiers_requested_keys_and_pointer_buttons_reject_before_mutation(self):
        for kind, code in (("key", 50), ("key", 37), ("key", 64), ("key", 133), ("key", 77), ("button", 8), ("button", 12)):
            with self.subTest(kind=kind, code=code):
                value = desktop()
                if kind == "key":
                    keys = [0] * 32
                    keys[code // 8] |= 1 << (code % 8)
                    value.display.query_keymap.return_value = keys
                else:
                    value.root.query_pointer.return_value.mask = 1 << code
                with self.assertRaisesRegex(RuntimeError, "held"):
                    value.held(77)
                value.display.xtest_fake_input.assert_not_called()

    def test_reversed_button_mapping_is_rejected_not_reconfigured(self):
        value = desktop()
        value.display.get_pointer_mapping.return_value = [3, 2, 1]
        with self.assertRaisesRegex(RuntimeError, "mapping"):
            value.pointer(24, dict(x=10, y=20, width=10, height=10), "left")
        value.display.xtest_fake_input.assert_not_called()

    def test_covered_target_rejects_before_pointer_motion(self):
        value = desktop()
        value.point_owner.return_value = 99
        with self.assertRaisesRegex(RuntimeError, "covered"):
            value.pointer(24, dict(x=10, y=20, width=10, height=10), "left")
        value.display.xtest_fake_input.assert_not_called()

    def test_target_change_after_motion_prevents_button_pair(self):
        value = desktop()
        value.point_owner.side_effect = [24, 99]
        with self.assertRaisesRegex(RuntimeError, "Actual pointer"):
            value.pointer(24, dict(x=10, y=20, width=10, height=10), "left")
        self.assertEqual(value.display.xtest_fake_input.call_args_list,
                         [mock.call(6, root=value.root, x=15, y=25)])

    def test_successful_pointer_uses_observed_native_coordinates_without_scaling(self):
        value = desktop()
        value.pointer(24, dict(x=10, y=20, width=10, height=10), "right")
        self.assertEqual(value.display.xtest_fake_input.call_args_list,
                         [mock.call(6, root=value.root, x=15, y=25), mock.call(4, 3), mock.call(5, 3)])

    def test_keyboard_uses_current_keysym_mapping_and_complete_physical_pair(self):
        for key, name in DRIVER.X11Desktop.KEYS.items():
            with self.subTest(key=key):
                value = desktop()
                value.XK = mock.Mock()
                value.XK.string_to_keysym.return_value = 100
                value.display.keysym_to_keycode.return_value = 77
                value.key(24, key)
                value.XK.string_to_keysym.assert_called_once_with(name)
                self.assertEqual(value.display.xtest_fake_input.call_args_list, [mock.call(2, 77), mock.call(3, 77)])

    def test_pair_failure_releases_only_an_actually_queued_owned_down(self):
        for queued in (False, True):
            with self.subTest(queued=queued):
                value = desktop()
                failure = RuntimeError("original protocol failure")
                if queued:
                    value.display.sync.side_effect = [failure, None]
                else:
                    value.display.xtest_fake_input.side_effect = failure
                with self.assertRaisesRegex(RuntimeError, "original protocol failure"):
                    value.pair(2, 3, 77)
                expected = [mock.call(2, 77), mock.call(3, 77), mock.call(3, 77)] if queued else [mock.call(2, 77)]
                self.assertEqual(value.display.xtest_fake_input.call_args_list, expected)

    def test_expired_absolute_deadline_prevents_native_queries(self):
        value = desktop()
        value.deadline = 0
        with self.assertRaisesRegex(RuntimeError, "deadline"):
            value.pointer(24, dict(x=10, y=20, width=10, height=10))
        value.foreground.assert_not_called()
        self.assertEqual(value.display.mock_calls, [])

    def test_capture_layout_requires_actual_order_masks_stride_and_depth(self):
        visual = NS(visual_id=21, visual_class=4, red_mask=0xFF0000, green_mask=0xFF00, blue_mask=0xFF)
        pixel = NS(depth=24, bits_per_pixel=32, scanline_pad=32)
        screen = NS(root_depth=24, root_visual=21, allowed_depths=[NS(visuals=[visual])])
        info = NS(pixmap_formats=[pixel], image_byte_order=0)
        self.assertEqual(DRIVER.image_layout(screen, info)["visual"], 21)
        for target, field, value in ((visual, "red_mask", 0xFF), (visual, "visual_class", 3),
                                     (pixel, "bits_per_pixel", 24), (pixel, "scanline_pad", 64),
                                     (info, "image_byte_order", 1), (screen, "root_depth", 30)):
            with self.subTest(field=field):
                previous = getattr(target, field)
                setattr(target, field, value)
                try:
                    with self.assertRaises(RuntimeError):
                        DRIVER.image_layout(screen, info)
                finally:
                    setattr(target, field, previous)

    def test_bmp_preserves_native_bytes_and_rejects_partial_or_existing_artifacts(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            path = root / "pixels.bmp"
            pixels = bytes(range(24))
            DRIVER.write_bmp(path, 3, 2, pixels)
            image = path.read_bytes()
            self.assertEqual(image[54:], pixels)
            self.assertEqual(struct.unpack_from("<ii", image, 18), (3, -2))
            with self.assertRaises(FileExistsError):
                DRIVER.write_bmp(path, 3, 2, pixels)
            with self.assertRaisesRegex(RuntimeError, "stride/length"):
                DRIVER.write_bmp(root / "short.bmp", 3, 2, pixels[:-1])
            with self.assertRaisesRegex(RuntimeError, "budget"):
                DRIVER.write_bmp(root / "huge.bmp", 4096, 4096, b"")
            self.assertEqual(list(root.iterdir()), [path])

    def test_property_read_is_bounded_and_rejects_wrong_type_or_trailing_data(self):
        value = desktop()
        value.atoms = {"PID": 50}
        window = mock.Mock()
        for reply in (NS(property_type=7, format=32, bytes_after=0, value=[24]),
                      NS(property_type=6, format=8, bytes_after=0, value=[24]),
                      NS(property_type=6, format=32, bytes_after=4, value=[24])):
            window.get_property.return_value = reply
            with self.assertRaisesRegex(RuntimeError, "Malformed|budget"):
                value.property(window, "PID", 6, 32)
        window.get_property.assert_called_with(50, 6, 0, 1024)

    def test_window_geometry_comes_from_actual_client_translation_and_server_extent(self):
        value = desktop()
        value.atom_types = NS(CARDINAL=6, WINDOW=33, ATOM=4)
        value.atoms = {"UTF8_STRING": 31}
        value.root.id = 2
        owner = mock.Mock(id=100)
        popup = mock.Mock(id=200)
        value.root.query_tree.return_value.children = [owner, popup]
        for window, override in ((owner, False), (popup, True)):
            window.get_attributes.return_value = NS(map_state=2, override_redirect=override)
            window.query_tree.return_value.children = []
            window.get_geometry.return_value = NS(root=value.root, width=80, height=60, border_width=1)
        value.root.translate_coords.side_effect = lambda window, _x, _y: NS(same_screen=True, x=window.id + 3, y=17)
        value.pid = mock.Mock(return_value=24)
        value.window = mock.Mock(return_value=owner)
        value.scalar = mock.Mock(return_value=100)
        value.property = mock.Mock(side_effect=lambda window, name, kind, bits: b"Owned" if bits == 8 else [9])
        result = value.windows(24)
        self.assertEqual([w["xid"] for w in result], [100, 200])
        self.assertEqual(result[1]["client"], dict(x=203, y=17, width=80, height=60))
        self.assertEqual(result[1]["bounds"], dict(x=202, y=16, width=82, height=62))
        self.assertEqual(result[1]["transientFor"], 100)
        value.root.translate_coords.assert_any_call(popup, 0, 0)
        value.scalar.return_value = None
        with self.assertRaisesRegex(RuntimeError, "transient owner"):
            value.windows(24)

    def test_foreground_requires_both_ewmh_owner_and_actual_keyboard_focus(self):
        value = desktop()
        value.atom_types = NS(WINDOW=33)
        value.scalar = mock.Mock(return_value=100)
        value.window = mock.Mock(return_value=NS(id=100))
        value.pid = mock.Mock(side_effect=[24, 99])
        with self.assertRaisesRegex(RuntimeError, "keyboard focus"):
            DRIVER.X11Desktop.foreground.__wrapped__(value, 24)
        value.display.xtest_fake_input.assert_not_called()

    def test_protocol_wait_has_a_real_timeout_and_restores_the_owned_alarm(self):
        before = DRIVER.signal.getitimer(DRIVER.signal.ITIMER_REAL)
        with self.assertRaisesRegex(TimeoutError, "protocol operation"):
            with DRIVER.protocol_timeout(0.01):
                time.sleep(0.1)
        self.assertEqual(DRIVER.signal.getitimer(DRIVER.signal.ITIMER_REAL), before)

    def test_connection_cleanup_closes_only_the_owned_socket_even_if_flush_fails(self):
        value = desktop()
        connection = value.display
        failure = RuntimeError("original flush failure")
        connection.close.side_effect = failure
        with self.assertRaisesRegex(RuntimeError, "original flush failure"):
            value.close()
        connection.display.socket.close.assert_called_once_with()
        self.assertIsNone(value.display)
        value.close()
        connection.display.socket.close.assert_called_once_with()

    def test_term_cancellation_keeps_shared_owned_child_cleanup_and_incomplete_receipt(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            app = root / "PopupInteractionApp"
            app.write_bytes(b"inert app never executed")
            app.with_suffix(".dll").write_bytes(b"inert assembly")
            process = mock.Mock(pid=24, returncode=-15)
            process.poll.return_value = None
            with mock.patch.object(DRIVER.SHARED.subprocess, "Popen", return_value=process), \
                    mock.patch.object(DRIVER.SHARED, "scenario", side_effect=lambda _session: DRIVER.cancel(15, None)):
                with self.assertRaises(SystemExit) as stopped:
                    DRIVER.SHARED.run_case(None, app, root, "portable", "run")
            self.assertEqual(stopped.exception.code, 143)
            process.terminate.assert_called_once_with()
            process.wait.assert_called_once_with(timeout=2)
            receipt = json.loads((root / "portable/receipt.json").read_text())
            self.assertEqual(receipt["status"], "incomplete")
            self.assertFalse(receipt["qualified"])
    def test_linux_preparation_accepts_only_identical_source_and_exact_elf_apphost(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            source = ROOT / "eng/PopupInteractionApp/Program.cs"
            (root / "Portable/bin/Release/net11.0").mkdir(parents=True)
            (root / "Portable/Program.cs").write_bytes(source.read_bytes())
            receipt = dict(schema="popup-interaction-preparation-v1", sourceSha256=DRIVER.SHARED.digest(source))
            (root / "preparation.json").write_text(json.dumps(receipt))
            app = root / "Portable/bin/Release/net11.0/PopupInteractionApp"
            app.write_bytes(b"\x7fELF inert fixture never executed")
            self.assertEqual(DRIVER.check_preparation(root, app), receipt)
            app.write_bytes(b"MZ inert wrong-format fixture")
            with self.assertRaisesRegex(RuntimeError, "ELF"):
                DRIVER.check_preparation(root, app)
            app.write_bytes(b"\x7fELF")
            (root / "Portable/Program.cs").write_text("changed source")
            with self.assertRaisesRegex(RuntimeError, "shared scenario"):
                DRIVER.check_preparation(root, app)

    def test_shared_scenario_and_success_status_are_reused_without_a_linux_substitute(self):
        source = (ROOT / "eng/librewinforms-popup-x11.py").read_text()
        self.assertIn("SHARED.run_case(desktop, app, root", source)
        self.assertNotIn("def scenario(", source)
        self.assertNotIn("set_input_focus(", source)
        self.assertNotIn("XDG_SESSION_TYPE", source)
        shared = (ROOT / "eng/librewinforms-popup-desktop.py").read_text()
        self.assertIn('session.desktop.activate(main)', shared)
        self.assertEqual(shared.count('session.capture("'), 14)


if __name__ == "__main__":
    unittest.main()
