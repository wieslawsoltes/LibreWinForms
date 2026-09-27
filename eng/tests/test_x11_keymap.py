"""Offline admission controls; no desktop or injected events."""
import importlib.util
from pathlib import Path
import subprocess
import types
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location('keymap', Path(__file__).parents[1] / 'librewinforms-x11-keymap.py')
KEYMAP = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(KEYMAP)


class KeymapTests(unittest.TestCase):
    def fixture(self, shift=False, lock=16):
        state = {name: int(shift and name in KEYMAP.SHIFT_FIELDS) for name in KEYMAP.FIELDS}
        for name in ('mods', 'lockedMods', 'compatState'):
            state[name] |= lock
        modifiers = [[0] for _ in range(8)]
        modifiers[4] = [77]
        return state, [(KEYMAP.NUM_LOCK, 0, KEYMAP.NUM_LOCK)], 77, modifiers

    def test_zero_state(self):
        self.assertEqual(KEYMAP.validate_state(*self.fixture(lock=0)), 0)

    def test_numlock_is_preserved(self):
        fixture = self.fixture()
        before = dict(fixture[0])
        self.assertEqual(KEYMAP.validate_state(*fixture), 16)
        self.assertEqual(fixture[0], before)

    def test_owned_shift_with_and_without_lock(self):
        for lock in (0, 16):
            self.assertEqual(KEYMAP.validate_state(*self.fixture(True, lock), owned_shift=True), lock)

    def test_xorg_unpopulated_reply_fields_stay_zero_under_shift(self):
        for field in ('grabMods', 'compatGrabMods', 'lookupMods', 'compatLookupMods'):
            fixture = self.fixture(True)
            self.assertEqual(fixture[0][field], 0)
            fixture[0][field] = 1
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                KEYMAP.validate_state(*fixture, owned_shift=True)

    def test_each_unexpected_state_component_rejected(self):
        for name in KEYMAP.FIELDS:
            fixture = self.fixture()
            fixture[0][name] ^= 2
            with self.subTest(name=name), self.assertRaises(RuntimeError):
                KEYMAP.validate_state(*fixture)

    def test_user_shift_is_not_owned_shift(self):
        with self.assertRaises(RuntimeError):
            KEYMAP.validate_state(*self.fixture(True))

    def test_missing_extra_boolean_state_rejected(self):
        for change in ('missing', 'extra', 'boolean'):
            fixture = self.fixture()
            if change == 'missing':
                fixture[0].pop('group')
            else:
                fixture[0]['unknown' if change == 'extra' else 'group'] = False
            with self.subTest(change=change), self.assertRaises(RuntimeError):
                KEYMAP.validate_state(*fixture)

    def test_mod2_requires_exclusive_numlock(self):
        for row in ((), (0,), (0xFFE5,), (KEYMAP.NUM_LOCK, 0xFFE5)):
            state, mapping, minimum, modifiers = self.fixture()
            with self.subTest(row=row), self.assertRaises(RuntimeError):
                KEYMAP.validate_state(state, [row], minimum, modifiers)

    def test_extra_missing_and_out_of_range_mod2_keys(self):
        for codes in ([], [77, 77], [77, 78], [76], [256], [True]):
            fixture = self.fixture()
            fixture[3][4] = codes
            with self.subTest(codes=codes), self.assertRaises(RuntimeError):
                KEYMAP.validate_state(*fixture)

    def test_same_key_in_another_modifier_rejected(self):
        fixture = self.fixture()
        fixture[3][3] = [77]
        with self.assertRaises(RuntimeError):
            KEYMAP.validate_state(*fixture)

    def test_actual_both_states_required(self):
        KEYMAP.verify_symbols(65, True, 16, {1: 65, 17: 65})
        KEYMAP.verify_symbols(97, False, 16, {0: 97, 16: 97})
        KEYMAP.verify_symbols(0xFF0D, False, 0, {0: 0xFF0D})
        for results in ({1: 65}, {17: 65}, {1: 65, 17: 97}, {1: 65, 17: 65, 0: 97}):
            with self.subTest(results=results), self.assertRaises(RuntimeError):
                KEYMAP.verify_symbols(65, True, 16, results)

    def test_numlock_sensitive_keypad_route_rejected(self):
        with self.assertRaises(RuntimeError):
            KEYMAP.verify_symbols(0xFFB1, False, 16, {0: 0xFF9C, 16: 0xFFB1})

    def test_unknown_mask_and_no_symbol_rejected(self):
        for symbol, lock in ((0, 16), (65, 2), (65, True)):
            with self.assertRaises(RuntimeError):
                KEYMAP.verify_symbols(symbol, False, lock, {0: symbol})

    def keyboard(self):
        state, mapping, minimum, modifiers = self.fixture()
        class Base:
            def route(self, symbol):
                return dict(code=38, shift=True, symbol=symbol)
            def send(self, pid, symbol):
                raise AssertionError('Offline tests must not inject input')
        guarded = KEYMAP.guarded_keyboard_type(Base)
        self.assertIs(guarded.send, Base.send)
        key = guarded()
        display = types.SimpleNamespace(
            display=types.SimpleNamespace(info=types.SimpleNamespace(min_keycode=minimum, max_keycode=minimum)),
            get_keyboard_mapping=lambda *args: mapping,
            get_modifier_mapping=lambda: modifiers,
            get_display_name=lambda: ':0')
        key.desktop = types.SimpleNamespace(display=display, root=types.SimpleNamespace(id=123), deadline=float('inf'))
        key.opcode = 135
        key.GetState = lambda **kwargs: types.SimpleNamespace(length=0, **state)
        return key, state

    def test_guard_reuses_sender_and_records_actual_translation(self):
        key, _ = self.keyboard()
        with patch.object(KEYMAP, 'lookup_symbols', return_value={1: 65, 17: 65}) as lookup:
            route = key.route(65)
        lookup.assert_called_once_with(':0', 123, 38, [1, 17], 3)
        self.assertEqual(route['unchangedLockMask'], 16)
        self.assertEqual(route['xkbSymbolsByMask'], {1: 65, 17: 65})

    def test_changed_lock_rejected_before_route(self):
        key, state = self.keyboard()
        key.state()
        state.update({name: 0 for name in state})
        with patch.object(KEYMAP, 'lookup_symbols') as lookup, self.assertRaisesRegex(RuntimeError, 'Lock state changed'):
            key.route(65)
        lookup.assert_not_called()

    def test_foreign_call_timeout_is_process_bounded(self):
        with patch.object(KEYMAP.subprocess, 'run', side_effect=subprocess.TimeoutExpired('lookup', 0.2)) as run:
            with self.assertRaises(subprocess.TimeoutExpired):
                KEYMAP.lookup_symbols(':0', 123, 38, [1, 17], 0.2)
        self.assertEqual(run.call_args.kwargs['timeout'], 0.2)
        self.assertTrue(run.call_args.kwargs['check'])

    def test_expired_or_extended_deadline_does_not_launch(self):
        for timeout in (0, -1, 4, float('inf'), float('nan')):
            with patch.object(KEYMAP.subprocess, 'run') as run, self.assertRaises(RuntimeError):
                KEYMAP.lookup_symbols(':0', 123, 38, [1, 17], timeout)
            run.assert_not_called()


if __name__ == '__main__':
    unittest.main()
