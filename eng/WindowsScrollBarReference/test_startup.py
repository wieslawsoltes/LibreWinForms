import unittest
from types import SimpleNamespace
from unittest.mock import Mock
import startup


class Startup(unittest.TestCase):
    def fixture(self):
        owner = dict(hwnd=42, title='ScrollBarRepeatReference [5]', client=dict(x=1,y=2,width=620,height=400))
        native = SimpleNamespace(owner=owner, owner_now=Mock(return_value=owner), child=SimpleNamespace(pid=5), desktop=Mock())
        observer, caption, record = Mock(), Mock(), {}
        observer.foreground.return_value = 42
        def dismiss(observer, sender, claim, deadline, evidence, environment, authorized):
            self.assertEqual(deadline,16); self.assertTrue(authorized)
            claim('hosted-shell-escape'); claim('hosted-shell-escape')
        return native, observer, Mock(side_effect=dismiss), Mock(return_value=caption), record

    def call(self, args, **kwargs):
        opts=dict(authorized=True, environment=startup.HOST, clock=lambda:1);opts.update(kwargs)
        return startup.activate(*args[:4],60,args[4],**opts)

    def test_two_shell_actions_one_owned_activation_and_caption_share_deadline(self):
        a=self.fixture(); self.assertEqual(1,self.call(a));self.assertTrue(a[4]['completed'])
        a[0].desktop.activate.assert_called_once_with(a[0].owner)
        a[3].return_value.activate_ci_window.assert_called_once_with(42,5,16,a[4]['caption'],startup.HOST)
        self.assertEqual(a[4]['shellActions'],2)

    def test_unapproved_foreign_and_expired_scopes_stop_before_observation(self):
        for opts in ({'authorized':False},{'environment':{}},{'clock':lambda:60}):
            a=self.fixture()
            with self.assertRaises(RuntimeError): self.call(a,**opts)
            a[0].owner_now.assert_not_called();a[2].assert_not_called();a[3].assert_not_called()

    def test_no_second_startup(self):
        a=self.fixture();self.call(a)
        with self.assertRaises(RuntimeError):self.call(a)
        a[0].desktop.activate.assert_called_once()

    def test_third_shell_action_stops_before_owned_activation(self):
        a=self.fixture()
        def bad(o,s,claim,*args,**kwargs):
            for _ in range(3):claim('hosted-shell-escape')
        a[2].side_effect=bad
        with self.assertRaisesRegex(RuntimeError,'count'):self.call(a)
        a[0].desktop.activate.assert_not_called();a[3].assert_not_called()

    def test_owner_replacement_stops_before_caption(self):
        a=self.fixture();a[0].owner_now.side_effect=[a[0].owner,a[0].owner,dict(hwnd=43)]
        with self.assertRaisesRegex(RuntimeError,'owner changed'):self.call(a)
        a[3].assert_not_called()

    def test_caption_failure_is_preserved_without_retry(self):
        a=self.fixture();a[3].return_value.activate_ci_window.side_effect=RuntimeError('original caption failure')
        with self.assertRaisesRegex(RuntimeError,'original caption failure'):self.call(a)
        a[3].return_value.activate_ci_window.assert_called_once();self.assertNotIn('completed',a[4])

    def test_failed_cleanup_observation_does_not_replace_primary_error(self):
        a=self.fixture();a[3].return_value.activate_ci_window.side_effect=RuntimeError('original caption failure')
        a[1].foreground.side_effect=RuntimeError('observation failure')
        with self.assertRaisesRegex(RuntimeError,'original caption failure'):self.call(a)
        self.assertEqual(a[4]['observationError'],'observation failure')


if __name__=='__main__':unittest.main()
