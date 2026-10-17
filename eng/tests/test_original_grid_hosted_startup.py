"""Pure startup integration controls; no native libraries, launch or input."""
import importlib.util
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock

SPEC = importlib.util.spec_from_file_location('grid_startup', Path(__file__).parents[1] / 'OriginalGrid/startup.py')
STARTUP = importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(STARTUP)


class StartupTests(unittest.TestCase):
    def fixture(self, root, shell=True, changed=False, caption_error=False, shell_count=2):
        state = dict(Sequence=1, FormFocused=False, InEdit=False, EditorUtf16=None, FirstNameUtf16=None,
                     Client=[10, 20, 300, 200], Cell=[30, 50, 120, 21])
        owner = dict(hwnd=123, title='original', client=dict(x=10, y=20, width=300, height=200))
        session = SimpleNamespace(hosted_startup=True, allow_shell=shell, phase='startup',
            startup_attempted=False, state=None, deadline=60, started=0, evidence=root,
            child=mock.Mock(pid=99), desktop=mock.Mock(), record=mock.Mock())
        session.child.poll.return_value = None
        session.native = mock.Mock(return_value=[owner])
        native = mock.Mock(); native.foreground.return_value = 12; native.snapshot.return_value = {'hwnd':12}
        def dismiss(n, sender, claim, deadline, record, environment, authorized):
            self.assertEqual(deadline, 20)
            self.assertTrue(authorized)
            record['setupDeadline'] = 4
            for _ in range(shell_count): claim('hosted-shell-escape', nativeProof={})
        def caption(hwnd, pid, deadline, record, environment):
            self.assertEqual((hwnd,pid,deadline),(123,99,20))
            session.desktop.activate.assert_called_once_with(owner)
            record['clickCount'] = 1
            if caption_error: raise RuntimeError('original caption failure')
        reader = mock.Mock()
        reader.read_snapshot.side_effect = [state | dict(Sequence=i, FormFocused=True,
            FirstNameUtf16=[65] if changed else None) for i in range(2,10)]
        modules = {'prepare-desktop': SimpleNamespace(Desktop=mock.Mock(return_value=native)),
                   'shell-escape': SimpleNamespace(dismiss=mock.Mock(side_effect=dismiss)),
                   'caption': SimpleNamespace(Desktop=mock.Mock(return_value=SimpleNamespace(activate_ci_window=caption))),
                   '../librewinforms-original-grid-desktop':reader}
        return session,state,owner,modules

    def run_startup(self, session, state, owner, modules, **changes):
        options=dict(authorized=True, environment=STARTUP.ENVIRONMENT, clock=lambda:1, sleep=lambda _:None)
        options.update(changes)
        with mock.patch.object(STARTUP,'load',side_effect=lambda name:modules[name]) as loader:
            STARTUP.activate(session,state,owner,modules["../librewinforms-original-grid-desktop"].read_snapshot,**options)
        return loader

    def test_reference_two_shell_actions_and_one_caption_with_original_limits(self):
        with tempfile.TemporaryDirectory() as temporary:
            session,state,owner,modules=self.fixture(Path(temporary))
            self.run_startup(session,state,owner,modules)
            record=json.loads((Path(temporary)/'startup-input.json').read_text())
            self.assertEqual((record['shellActions'],record['captionClicks']),(2,1))
            self.assertEqual((record['startupDeadline'],record['applicationDeadline']),(20,60))
            self.assertTrue(record['completed']);self.assertFalse(record['qualified'])
            self.assertTrue(record['originalActivationRequested'])
            self.assertEqual(session.record.call_count,3)
            session.desktop.foreground.assert_called_once_with(99)

    def test_portable_never_receives_shell_escape(self):
        with tempfile.TemporaryDirectory() as temporary:
            session,state,owner,modules=self.fixture(Path(temporary),shell=False)
            self.run_startup(session,state,owner,modules)
            modules['shell-escape'].dismiss.assert_not_called()
            self.assertEqual(session.record.call_count,1)

    def test_unapproved_foreign_or_expired_calls_fail_before_native_queries(self):
        for change in ({'authorized':False},{'environment':{}},{'clock':lambda:20}):
            with self.subTest(change=change),tempfile.TemporaryDirectory() as temporary:
                session,state,owner,modules=self.fixture(Path(temporary))
                with self.assertRaises(RuntimeError):
                    self.run_startup(session,state,owner,modules,**change)
                modules['prepare-desktop'].Desktop.assert_not_called()
                modules['caption'].Desktop.assert_not_called()
                session.record.assert_not_called()
                session.desktop.activate.assert_not_called()
                self.assertFalse((Path(temporary)/'startup-input.json').exists())

    def test_repeated_or_nonstartup_activation_is_rejected(self):
        for key,value in [('startup_attempted',True),('phase','begin-edit'),('hosted_startup',False)]:
            with self.subTest(key=key),tempfile.TemporaryDirectory() as temporary:
                session,state,owner,modules=self.fixture(Path(temporary));setattr(session,key,value)
                with self.assertRaises(RuntimeError):self.run_startup(session,state,owner,modules)
                session.record.assert_not_called()

    def test_changed_editing_source_is_not_repaired_into_acceptance(self):
        with tempfile.TemporaryDirectory() as temporary:
            session,state,owner,modules=self.fixture(Path(temporary),changed=True)
            with self.assertRaisesRegex(RuntimeError,'changed original source'):self.run_startup(session,state,owner,modules)
            record=json.loads((Path(temporary)/'startup-input.json').read_text())
            self.assertNotIn('completed',record);self.assertEqual(state['FirstNameUtf16'],None)
            self.assertIn('foregroundAtFailure',record)

    def test_failed_caption_preserves_failure_and_inserted_click_evidence(self):
        with tempfile.TemporaryDirectory() as temporary:
            session,state,owner,modules=self.fixture(Path(temporary),caption_error=True)
            with self.assertRaisesRegex(RuntimeError,'original caption failure'):self.run_startup(session,state,owner,modules)
            record=json.loads((Path(temporary)/'startup-input.json').read_text())
            self.assertEqual(record['captionClicks'],1);self.assertNotIn('completed',record)
            modules['../librewinforms-original-grid-desktop'].read_snapshot.assert_not_called()

    def test_third_shell_action_stops_before_caption(self):
        with tempfile.TemporaryDirectory() as temporary:
            session,state,owner,modules=self.fixture(Path(temporary),shell_count=3)
            with self.assertRaisesRegex(RuntimeError,'budget'):self.run_startup(session,state,owner,modules)
            modules['caption'].Desktop.assert_not_called();self.assertEqual(session.record.call_count,2)
            session.desktop.activate.assert_not_called()

    def test_original_activation_failure_is_not_retried_or_replaced_with_input(self):
        with tempfile.TemporaryDirectory() as temporary:
            session,state,owner,modules=self.fixture(Path(temporary),shell=False)
            session.desktop.activate.side_effect=RuntimeError('original activation failure')
            with self.assertRaisesRegex(RuntimeError,'original activation failure'):
                self.run_startup(session,state,owner,modules)
            session.desktop.activate.assert_called_once_with(owner)
            modules['caption'].Desktop.assert_not_called()
            session.record.assert_not_called()

    def test_activation_cannot_change_original_native_owner(self):
        with tempfile.TemporaryDirectory() as temporary:
            session,state,owner,modules=self.fixture(Path(temporary),shell=False)
            session.native.side_effect=[[owner],[owner],[owner|{'hwnd':456}]]
            with self.assertRaisesRegex(RuntimeError,'owner changed during activation'):
                self.run_startup(session,state,owner,modules)
            modules['caption'].Desktop.assert_not_called()

    def test_foreground_change_is_only_permitted_source_difference(self):
        state={'Sequence':1,'FormFocused':False,'EditorFocused':False,'FirstNameUtf16':None}
        self.assertEqual(STARTUP.unchanged_source(state),STARTUP.unchanged_source(state|{'Sequence':2,'FormFocused':True}))
        for change in ({'EditorFocused':True},{'FirstNameUtf16':[65]}):
            self.assertNotEqual(STARTUP.unchanged_source(state),STARTUP.unchanged_source(state|change))


if __name__ == '__main__': unittest.main()
