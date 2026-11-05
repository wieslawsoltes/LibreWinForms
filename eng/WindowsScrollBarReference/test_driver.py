import unittest
from pathlib import Path
import driver


class Clock:
    value = 0
    def now(self): return self.value
    def sleep(self, amount): self.value += amount


class Backend:
    def __init__(self):
        self.point, self.events, self.fail, self.accepted = (1, 2), [], False, True
    def check(self, target, point, held):
        if self.fail: raise RuntimeError('ownership changed')
        if target != 'owned': raise RuntimeError('unowned target')
    def position(self): return self.point
    def move(self, point): self.events.append(('move', point)); self.point = point
    def button(self, down): self.events.append(('button', down)); return self.accepted


class Guards(unittest.TestCase):
    def test_phase_logging_is_only_admitted_for_read_only_diagnosis(self):
        with self.assertRaisesRegex(RuntimeError, 'read-only'):
            driver.reference_arguments('original.exe', Path('evidence'), diagnose_startup=True)
        self.assertEqual(['original.exe', str(Path('evidence') / 'app'), '--startup-phases'],
            driver.reference_arguments('original.exe', Path('evidence'), diagnose_startup=True, observe_startup_only=True))

    def test_ordinary_reference_arguments_stay_unchanged(self):
        self.assertEqual(['original.exe', str(Path('evidence') / 'app')],
            driver.reference_arguments('original.exe', Path('evidence')))

    def test_native_class_uses_exact_typed_source_and_both_native_receipts(self):
        alias = 'WindowsForms10.ScrollBar.app.0.2360855_r3_ad1'
        class User:
            def GetClassNameW(self, hwnd, buffer, count):
                self.assert_hwnd = hwnd
                buffer.value = alias
                return len(buffer.value)
            def RealGetWindowClassW(self, hwnd, buffer, count):
                assert hwnd == self.assert_hwnd == 42
                buffer.value = alias
                return len(buffer.value)
        source = dict(hwnd=42, sourceClass='ScrollBar', registeredClass=alias, realClass=alias)
        self.assertEqual(alias, driver.native_scrollbar_class(User(), 42, source)['original'])

    def test_registered_scrollbar_name_cannot_admit_a_different_native_type(self):
        class User:
            def GetClassNameW(self, hwnd, buffer, count):
                buffer.value = 'SCROLLBAR'; return len(buffer.value)
            def RealGetWindowClassW(self, hwnd, buffer, count):
                buffer.value = 'Button'; return len(buffer.value)
        with self.assertRaisesRegex(RuntimeError, 'original native scrollbar'):
            driver.native_scrollbar_class(User(), 42,
                dict(hwnd=42, sourceClass='ScrollBar', registeredClass='SCROLLBAR', realClass='ScrollBar'))

    def test_missing_or_different_original_source_class_cannot_reach_native_queries(self):
        for source in ({}, dict(hwnd=43, sourceClass='ScrollBar'), dict(hwnd=42, sourceClass='Button'), dict(hwnd=42, sourceClass=None)):
            with self.assertRaisesRegex(RuntimeError, 'source scrollbar'):
                driver.native_scrollbar_class(object(), 42, source)

    def test_missing_or_changed_native_class_receipt_rejects(self):
        class User:
            def GetClassNameW(self, hwnd, buffer, count):
                buffer.value = 'Original'; return len(buffer.value)
            def RealGetWindowClassW(self, hwnd, buffer, count):
                buffer.value = 'Original'; return len(buffer.value)
        for field in ('registeredClass', 'realClass'):
            for value in (None, 'Changed'):
                source = dict(hwnd=42, sourceClass='ScrollBar', registeredClass='Original', realClass='Original')
                source[field] = value
                with self.subTest(field=field, value=value), self.assertRaises(RuntimeError):
                    driver.native_scrollbar_class(User(), 42, source)

    def test_failed_or_truncated_class_queries_reject(self):
        for failed in ('registered', 'original'):
            for result in (0, 255, 3):
                class User:
                    def GetClassNameW(self, hwnd, buffer, count):
                        buffer.value = 'SCROLLBAR'
                        return result if failed == 'registered' else len(buffer.value)
                    def RealGetWindowClassW(self, hwnd, buffer, count):
                        buffer.value = 'SCROLLBAR'
                        return result if failed == 'original' else len(buffer.value)
                with self.subTest(failed=failed, result=result), self.assertRaises(RuntimeError):
                    driver.native_scrollbar_class(User(), 42,
                        dict(hwnd=42, sourceClass='ScrollBar', registeredClass='SCROLLBAR', realClass='SCROLLBAR'))

    def test_startup_input_time_cannot_be_reset_before_scroll_gestures(self):
        p,b,c=self.make(input_started=0);c.value=14.9
        p.down('owned',(1,2))
        with self.assertRaisesRegex(RuntimeError,'deadline'):p.observe('owned',(1,2),1.2)
        p.cleanup()

    def test_future_or_negative_input_origin_is_rejected(self):
        for start in (-1,1):
            with self.assertRaisesRegex(RuntimeError,'start'):self.make(input_started=start)

    def test_startup_observation_uses_only_read_operations(self):
        class ReadOnly:
            def context(self): return {'desktop': 'Default'}
            def windows(self): return [{'hwnd': 5}]
            def foreground(self): return 6
            def snapshot(self, hwnd): return {'hwnd': hwnd, 'title': 'foreign'}
        self.assertEqual(dict(context={'desktop': 'Default'}, windows=[{'hwnd': 5}],
                              foreground={'hwnd': 6, 'title': 'foreign'}), driver.observe_desktop(ReadOnly()))

    def test_startup_observation_bounds_inventory_before_foreground(self):
        class ReadOnly:
            def context(self): return {}
            def windows(self): return [{}] * 129
        with self.assertRaisesRegex(RuntimeError, 'bound'): driver.observe_desktop(ReadOnly())

    def make(self, **changes):
        b, c = Backend(), Clock()
        args = dict(authorized=True, environment=driver.HOST.copy(), clock=c.now, sleep=c.sleep)
        args.update(changes)
        return driver.HeldInput(b, 60, **args), b, c

    def test_no_approval_means_no_input(self):
        with self.assertRaisesRegex(RuntimeError, 'approval'): self.make(authorized=False)

    def test_every_host_identity_field_is_required(self):
        for key in driver.HOST:
            with self.subTest(key=key), self.assertRaisesRegex(RuntimeError, 'approval'):
                environment = driver.HOST.copy(); environment[key] = 'wrong'; self.make(environment=environment)

    def test_complete_original_hold_delivers_one_pair(self):
        p, b, c = self.make(); p.move('owned', (1, 2)); p.down('owned', (1, 2)); p.observe('owned', (1, 2), 1.2); p.up('owned', (1, 2)); p.cleanup()
        self.assertEqual([('move', (1, 2)), ('button', True), ('button', False)], b.events)
        self.assertFalse(p.held); self.assertEqual(1, p.presses); self.assertEqual(3, p.actions)

    def test_cleanup_only_releases_its_accepted_down(self):
        p, b, c = self.make(); p.cleanup(); self.assertEqual([], b.events)
        p.down('owned', (1, 2)); b.fail = True; c.value = 100; p.cleanup(); p.cleanup()
        self.assertEqual([('button', True), ('button', False)], b.events)

    def test_failed_down_does_not_acquire_release_ownership(self):
        p, b, c = self.make(); b.accepted = False
        with self.assertRaises(RuntimeError): p.down('owned', (1, 2))
        p.cleanup(); self.assertEqual([('button', True)], b.events)

    def test_failed_normal_release_retains_one_cleanup_release(self):
        p, b, c = self.make(); p.down('owned', (1, 2)); b.accepted = False
        with self.assertRaises(RuntimeError): p.up('owned', (1, 2))
        self.assertTrue(p.held); b.accepted = True; p.cleanup(); self.assertFalse(p.held)
        self.assertEqual([('button', True), ('button', False), ('button', False)], b.events)

    def test_failed_cleanup_retains_failure(self):
        p, b, c = self.make(); p.down('owned', (1, 2)); b.accepted = False
        with self.assertRaises(RuntimeError): p.cleanup()
        self.assertTrue(p.held)

    def test_foreign_geometry_blocks_down(self):
        p, b, c = self.make()
        with self.assertRaises(RuntimeError): p.down('foreign', (1, 2))
        self.assertEqual([], b.events)

    def test_movement_before_down_blocks_injection(self):
        p, b, c = self.make(); b.point = (4, 5)
        with self.assertRaises(RuntimeError): p.down('owned', (1, 2))
        self.assertEqual([], b.events)

    def test_unexpected_motion_during_hold_fails_without_new_input(self):
        p, b, c = self.make(); p.down('owned', (1, 2)); b.point = (4, 5)
        with self.assertRaises(RuntimeError): p.observe('owned', (1, 2), .1)
        self.assertEqual([('button', True)], b.events); p.cleanup()

    def test_ownership_change_during_hold_fails(self):
        p, b, c = self.make(); p.down('owned', (1, 2)); b.fail = True
        with self.assertRaises(RuntimeError): p.observe('owned', (1, 2), .1)
        p.cleanup(); self.assertEqual([('button', True), ('button', False)], b.events)

    def test_hold_cannot_extend_input_or_process_deadline(self):
        for deadline in (1, 60):
            p, b, c = self.make(); p.deadline = deadline; p.down('owned', (1, 2))
            if deadline == 60: c.value = 14.5
            with self.assertRaises(RuntimeError): p.observe('owned', (1, 2), 1.2)
            p.cleanup()

    def test_input_after_15_seconds_is_rejected(self):
        p, b, c = self.make(); p.move('owned', (1, 2)); c.value = 15
        with self.assertRaises(RuntimeError): p.down('owned', (1, 2))
        self.assertEqual([('move', (1, 2))], b.events)

    def test_oversized_hold_is_rejected(self):
        p, b, c = self.make(); p.down('owned', (1, 2))
        with self.assertRaises(RuntimeError): p.observe('owned', (1, 2), 2)
        p.cleanup()

    def test_second_down_cannot_replace_lease(self):
        p, b, c = self.make(); p.down('owned', (1, 2))
        with self.assertRaises(RuntimeError): p.down('owned', (1, 2))
        self.assertEqual([('button', True)], b.events); p.cleanup()

    def test_eighth_press_is_rejected(self):
        p, b, c = self.make()
        for _ in range(7): p.down('owned', (1, 2)); p.up('owned', (1, 2))
        with self.assertRaises(RuntimeError): p.down('owned', (1, 2))
        self.assertEqual(14, len(b.events))

    def test_budget_reserves_one_cleanup_release(self):
        p, b, c = self.make(); p.down('owned', (1, 2)); p.actions = 31
        with self.assertRaises(RuntimeError): p.move('owned', (1, 2))
        p.cleanup(); self.assertEqual(32, p.actions); self.assertFalse(p.held)


if __name__ == '__main__': unittest.main()
