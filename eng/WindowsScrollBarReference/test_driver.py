import unittest
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
