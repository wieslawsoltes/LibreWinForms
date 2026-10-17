import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest import mock

SPEC = importlib.util.spec_from_file_location('grid_diagnostic', Path(__file__).parents[1] / 'OriginalGrid/diagnose-startup.py')
DIAG = importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(DIAG)


class StackObservationTests(unittest.TestCase):
    def test_finished_or_canceled_child_is_not_attached(self):
        for canceled in (False, True):
            child, stop, launch, record = mock.Mock(), mock.Mock(), mock.Mock(), {}
            child.poll.return_value = 0
            stop.wait.return_value = canceled
            DIAG.collect(child, Path('tool.exe'), Path('unused'), stop, launch, record)
            launch.assert_not_called(); child.kill.assert_not_called()
            self.assertIn('skipped',record)

    def test_five_second_bound_kills_only_diagnostic_tool(self):
        with tempfile.TemporaryDirectory() as directory:
            child, stop, process, record = mock.Mock(pid=123), mock.Mock(), mock.Mock(), {}
            child.poll.return_value = None
            stop.wait.return_value = False; stop.is_set.return_value = False
            process.poll.side_effect = [None, -9]; process.wait.return_value = -9
            launch = mock.Mock(return_value=process)
            with mock.patch.object(DIAG.time,'monotonic',side_effect=[0,5,5]):
                DIAG.collect(child,Path('tool.exe'),Path(directory),stop,launch,record)
            self.assertEqual(launch.call_args.args[0],['tool.exe','report','--process-id','123'])
            process.kill.assert_called_once(); child.kill.assert_not_called(); child.terminate.assert_not_called()
            self.assertTrue(record['boundedStop']); self.assertEqual(record['seconds'],5)

    def test_observation_error_does_not_replace_original_controller_failure(self):
        with tempfile.TemporaryDirectory() as directory:
            child, stop, record = mock.Mock(pid=123), mock.Mock(), {}
            child.poll.return_value = None; stop.wait.return_value = False
            launch=mock.Mock(side_effect=OSError('diagnostic unavailable'))
            DIAG.collect(child,Path('tool.exe'),Path(directory),stop,launch,record)
            self.assertIn('diagnostic unavailable',record['error']);child.kill.assert_not_called()


if __name__ == '__main__': unittest.main()
