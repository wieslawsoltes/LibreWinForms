import ctypes as C
import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch
spec=importlib.util.spec_from_file_location("grid_caption",Path(__file__).parents[1]/"OriginalGrid/caption.py")
probe=importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)
class ForegroundTests(unittest.TestCase):
    class NativeWindow:
        def __init__(self, foreground=(9, 7), accepted=True):
            self.foreground = iter(foreground)
            self.last_foreground = 9
            self.accepted = accepted
            self.owner = 12
            self.live = True
            self.visible = True
            self.iconic = False
            self.requests = []

        def IsWindow(self, hwnd): return self.live and hwnd == 7
        def IsWindowVisible(self, hwnd): return self.visible
        def IsIconic(self, hwnd): return self.iconic
        def GetWindowThreadProcessId(self, hwnd, owner):
            owner._obj.value = self.owner
            return 1
        def GetForegroundWindow(self):
            self.last_foreground = next(self.foreground, self.last_foreground)
            return self.last_foreground
        def SetForegroundWindow(self, hwnd):
            self.requests.append(hwnd)
            return self.accepted

    def desktop(self, **options):
        desktop = probe.Desktop.__new__(probe.Desktop)
        desktop.u = self.NativeWindow(**options)
        return desktop

    def prepare(self, desktop, record, snapshot=None, deadline=100):
        desktop.prepare_foreground(snapshot or {"hwnd": 7, "pid": 12}, 12, deadline, record)

    def host_activation(self):
        return {"pid": 12, "hwnd": 7, "before": 9, "requested": True,
                "accepted": True, "after": 7, "foregroundPid": 12, "requestCount": 1}

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_host_activation_never_adds_a_driver_request(self, _):
        desktop = self.desktop(foreground=(7, 7)); record = {}
        desktop.prepare_foreground({"hwnd": 7, "pid": 12}, 12, 100, record, self.host_activation())
        self.assertEqual([], desktop.u.requests)
        self.assertEqual(7, record["after"])

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_rejected_or_foreign_host_receipt_cannot_use_coincident_foreground(self, _):
        for key, value in (("pid", 13), ("hwnd", 8), ("accepted", False), ("after", 9),
                           ("foregroundPid", 13), ("requestCount", 2), ("requested", False)):
            receipt = self.host_activation(); receipt[key] = value
            desktop = self.desktop(foreground=(7, 7))
            with self.subTest(key=key), self.assertRaises(RuntimeError):
                desktop.prepare_foreground({"hwnd": 7, "pid": 12}, 12, 100, {}, receipt)
            self.assertEqual([], desktop.u.requests)

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_foreground_lost_after_host_receipt_does_not_trigger_a_second_request(self, _):
        desktop = self.desktop(foreground=(9, 9))
        with self.assertRaisesRegex(RuntimeError, "lost foreground"):
            desktop.prepare_foreground({"hwnd": 7, "pid": 12}, 12, 100, {}, self.host_activation())
        self.assertEqual([], desktop.u.requests)

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_already_foreground_does_not_request_activation(self, _):
        desktop = self.desktop(foreground=(7, 7)); record = {}
        self.prepare(desktop, record)
        self.assertEqual([], desktop.u.requests)
        self.assertEqual(7, record["after"])
        self.assertFalse(record["requested"])

    @patch.object(probe.time, "sleep")
    @patch.object(probe.time, "monotonic", return_value=20)
    def test_one_request_waits_for_actual_cross_thread_foreground(self, _, sleep):
        desktop = self.desktop(foreground=(9, 9, 7)); record = {}
        self.prepare(desktop, record)
        self.assertEqual([7], desktop.u.requests)
        self.assertTrue(record["accepted"])
        self.assertEqual(7, record["after"])
        sleep.assert_called_once_with(0.025)

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_foreign_dead_hidden_or_minimized_window_never_receives_request(self, _):
        for name, value in (("owner", 13), ("live", False), ("visible", False), ("iconic", True)):
            desktop = self.desktop(); setattr(desktop.u, name, value)
            with self.subTest(name=name), self.assertRaises(RuntimeError):
                self.prepare(desktop, {})
            self.assertEqual([], desktop.u.requests)
        desktop = self.desktop()
        with self.assertRaisesRegex(RuntimeError, "snapshot PID"):
            self.prepare(desktop, {}, {"hwnd": 7, "pid": 13})
        self.assertEqual([], desktop.u.requests)

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_rejected_request_is_retained_and_never_retried(self, _):
        desktop = self.desktop(accepted=False); record = {}
        with self.assertRaisesRegex(RuntimeError, "rejected"):
            self.prepare(desktop, record)
        self.assertEqual([7], desktop.u.requests)
        self.assertFalse(record["accepted"])
        self.assertEqual(7, record["afterRejectedRequest"])
        # Even a coincident foreground change cannot turn a denied request into
        # success. Keep the original failure and its diagnostic readback.
        self.assertEqual(12, record["foregroundPidAfterRejectedRequest"])

    @patch.object(probe.time, "sleep")
    @patch.object(probe.time, "monotonic", side_effect=(20, 20, 20.5, 21))
    def test_accepted_request_without_actual_foreground_stays_failure(self, _, sleep):
        desktop = self.desktop(foreground=(9,)); record = {}
        with self.assertRaisesRegex(RuntimeError, "budget"):
            self.prepare(desktop, record)
        self.assertEqual([7], desktop.u.requests)
        self.assertEqual(9, record["after"])
        sleep.assert_called_once_with(0.025)

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_ownership_change_after_accepted_request_fails(self, _):
        desktop = self.desktop()
        def change_owner(hwnd):
            desktop.u.requests.append(hwnd); desktop.u.owner = 13
            return True
        desktop.u.SetForegroundWindow = change_owner
        with self.assertRaisesRegex(RuntimeError, "PID mismatch"):
            self.prepare(desktop, {})
        self.assertEqual([7], desktop.u.requests)

    @patch.object(probe.time, "monotonic", return_value=98)
    def test_outer_deadline_cannot_be_extended_by_activation(self, _):
        desktop = self.desktop()
        with self.assertRaisesRegex(RuntimeError, "original60-second"):
            self.prepare(desktop, {})
        self.assertEqual([], desktop.u.requests)

    def test_later_capture_does_not_reactivate_a_background_window(self):
        desktop = self.desktop(foreground=(9,))
        with self.assertRaisesRegex(RuntimeError, "not foreground"):
            desktop.geometry(7, 12)
        self.assertEqual([], desktop.u.requests)


class CiCaptionTests(unittest.TestCase):
    environment = {"GITHUB_ACTIONS": "true", "RUNNER_ENVIRONMENT": "github-hosted",
                   "RUNNER_OS": "Windows", "GITHUB_REPOSITORY": "wieslawsoltes/LibreWinForms"}

    class NativeWindow(ForegroundTests.NativeWindow):
        def __init__(self):
            super().__init__(foreground=(9,))
            self.inputs = []
            self.hit = 2
            self.point_owner = 7
            self.held = False
            self.capture = False
            self.button_count = 2
            self.activate = True
            self.cursor = (0, 0)

        def GetGUIThreadInfo(self, thread, info):
            info._obj.hwndCapture = 8 if self.capture else 0
            return 1
        def GetAsyncKeyState(self, key): return 0x8000 if self.held else 0
        def GetWindowRect(self, hwnd, rect):
            rect._obj.left, rect._obj.top, rect._obj.right, rect._obj.bottom = -500, 0, 400, 930
            return 1
        def ClientToScreen(self, hwnd, point):
            point._obj.x, point._obj.y = -492, 40
            return 1
        def WindowFromPoint(self, point): return self.point_owner
        def GetClassNameW(self, hwnd, value, length):
            value.value = "native-caption-control"
            return len(value.value)
        def GetWindowTextW(self, hwnd, value, length):
            value.value = "owned" if hwnd == 7 else "obstruction"
            return len(value.value)
        def SendMessageTimeoutW(self, hwnd, message, wparam, lparam, flags, timeout, result):
            result._obj.value = self.hit
            return 1
        def GetSystemMetrics(self, index): return {76: -500, 77: 0, 78: 1500, 79: 1000}[index]
        def GetCursorPos(self, point):
            point._obj.x, point._obj.y = self.cursor
            return 1
        def SendInput(self, count, values, size):
            array = probe.C.cast(values, probe.C.POINTER(probe.Input))
            self.inputs.append([array[i].mi.dwFlags for i in range(count)])
            if array[0].mi.dwFlags == 0xc001:
                self.cursor = (-500 + array[0].mi.dx * 1500 // 65536,
                               array[0].mi.dy * 1000 // 65536)
            if count == 2:
                if self.activate:
                    self.foreground = iter((7,))
                return self.button_count
            return count

    def desktop(self):
        desktop = probe.Desktop.__new__(probe.Desktop)
        desktop.u = self.NativeWindow()
        desktop.monitors = lambda crop: probe.require_monitor_coverage(crop, [(-500, 0, 1000, 1000)])
        return desktop

    def run_case(self, desktop, record=None, snapshot=None, environment=None):
        record = {} if record is None else record
        probe.Desktop.activate_ci_caption(desktop, snapshot or {"pid": 12, "hwnd": 7, "frame": 1}, 12, 100,
                                          record, self.environment if environment is None else environment)
        return record

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_one_caption_click_uses_exact_negative_physical_point_and_foreground_readback(self, _):
        desktop = self.desktop()
        record = self.run_case(desktop)
        self.assertEqual([[0xc001], [2, 4]], desktop.u.inputs)
        self.assertEqual([-50, 20], record["point"])
        self.assertEqual((-50, 20), desktop.u.cursor)
        self.assertEqual((1, 7), (record["clickCount"], record["after"]))
        self.assertEqual(2, record["captionCandidates"][0]["nativeHit"])
        self.assertEqual(7, record["captionCandidates"][0]["pointWindow"]["hwnd"])
        self.assertEqual("obstruction", record["foreground"]["title"])
        self.assertEqual([], desktop.u.requests)

    def test_local_and_self_hosted_input_rejected_before_any_native_query(self):
        for environment in ({}, dict(self.environment, RUNNER_ENVIRONMENT="self-hosted")):
            with self.subTest(environment=environment), self.assertRaisesRegex(RuntimeError, "GitHub-hosted"):
                self.run_case(None, environment=environment)

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_foreign_unpresented_hidden_and_dead_targets_never_receive_input(self, _):
        for snapshot in ({"pid": 13, "hwnd": 7, "frame": 1}, {"pid": 12, "hwnd": 7, "frame": 0}):
            desktop = self.desktop()
            with self.subTest(snapshot=snapshot), self.assertRaises(RuntimeError):
                self.run_case(desktop, snapshot=snapshot)
            self.assertEqual([], desktop.u.inputs)
        for name, value in (("owner", 13), ("live", False), ("visible", False), ("iconic", True)):
            desktop = self.desktop(); setattr(desktop.u, name, value)
            with self.subTest(name=name), self.assertRaises(RuntimeError):
                self.run_case(desktop)
            self.assertEqual([], desktop.u.inputs)

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_buttons_resize_client_obstruction_held_input_and_capture_rejected(self, _):
        for name, value in (("hit", 20), ("hit", 9), ("hit", 12), ("hit", 1),
                            ("point_owner", 8), ("held", True), ("capture", True)):
            desktop = self.desktop(); setattr(desktop.u, name, value)
            record = {}
            with self.subTest(name=name, value=value), self.assertRaises(RuntimeError):
                self.run_case(desktop, record)
            self.assertEqual([], desktop.u.inputs)
            if name == "point_owner":
                self.assertEqual([8, 8, 8], [entry["pointWindow"]["hwnd"] for entry in record["captionCandidates"]])
                self.assertTrue(all("nativeHit" not in entry for entry in record["captionCandidates"]))

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_caption_covered_after_move_never_receives_press(self, _):
        desktop = self.desktop()
        move = desktop.u.SendInput
        def obstruct(*args):
            result = move(*args)
            desktop.u.point_owner = 8
            return result
        desktop.u.SendInput = obstruct
        with self.assertRaisesRegex(RuntimeError, "caption ownership|caption.*changed"):
            self.run_case(desktop)
        self.assertEqual([[0xc001]], desktop.u.inputs)

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_partial_press_is_released_and_never_retried_or_accepted(self, _):
        desktop = self.desktop(); desktop.u.button_count = 1
        record = {}
        with self.assertRaisesRegex(RuntimeError, "not inserted completely"):
            self.run_case(desktop, record)
        self.assertEqual([[0xc001], [2, 4], [4]], desktop.u.inputs)
        self.assertEqual(1, record["partialPressReleaseInserted"])
        self.assertEqual(1, record["clickCount"])

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_already_foreground_does_not_inject_or_request(self, _):
        desktop = self.desktop(); desktop.u.foreground = iter((7,))
        record = self.run_case(desktop)
        self.assertEqual([], desktop.u.inputs)
        self.assertEqual(0, record["clickCount"])

    @patch.object(probe.time, "monotonic", return_value=98)
    def test_expired_original_deadline_never_receives_input(self, _):
        desktop = self.desktop()
        with self.assertRaisesRegex(RuntimeError, "budget"):
            self.run_case(desktop)
        self.assertEqual([], desktop.u.inputs)

    @patch.object(probe.time, "sleep")
    @patch.object(probe.time, "monotonic", side_effect=(20, 20, 20, 20, 20, 21))
    def test_successfully_inserted_click_without_foreground_is_failure(self, _, sleep):
        desktop = self.desktop(); desktop.u.activate = False
        with self.assertRaisesRegex(RuntimeError, "did not activate"):
            self.run_case(desktop)
        self.assertEqual([[0xc001], [2, 4]], desktop.u.inputs)



if __name__ == "__main__": unittest.main()
