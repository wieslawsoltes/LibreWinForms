import importlib.util
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

SPEC = importlib.util.spec_from_file_location('native_stack', Path(__file__).parents[1] / 'OriginalGrid/native_stack.py')
NATIVE = importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(NATIVE)
TOOL = None
if '--native-tool' in sys.argv:
    index = sys.argv.index('--native-tool'); TOOL = Path(sys.argv[index + 1]).resolve(strict=True)
    del sys.argv[index:index + 2]


class NativeStackTests(unittest.TestCase):
    def fixture(self, path, pid=123, flags=0):
        data = bytearray(160)
        struct.pack_into('<IIIIIIQ', data, 0, 0x504D444D, 0xA793, 4, 32, 0, 0, flags)
        struct.pack_into('<III', data, 32, 7, 56, 80)
        struct.pack_into('<III', data, 44, 15, 24, 136)
        struct.pack_into('<H', data, 80, 12)
        struct.pack_into('<III', data, 136, 24, 1, pid)
        path.write_bytes(data)

    def test_normal_dump_requires_exact_process_and_bounded_streams(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'test.dmp'
            self.fixture(path)
            self.assertEqual(NATIVE.validate_dump(path,123)['architecture'],12)
            with self.assertRaises(ValueError): NATIVE.validate_dump(path,124)
            self.fixture(path, flags=2)
            with self.assertRaises(ValueError): NATIVE.validate_dump(path,123)
            self.fixture(path); path.write_bytes(path.read_bytes()[:120])
            with self.assertRaises(ValueError): NATIVE.validate_dump(path,123)

    def test_nonempty_unused_or_duplicate_active_stream_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'test.dmp'
            for entry in ((0, 1, 80), (7, 56, 80)):
                self.fixture(path)
                data=bytearray(path.read_bytes());struct.pack_into('<III',data,56,*entry);path.write_bytes(data)
                with self.assertRaises(ValueError): NATIVE.validate_dump(path,123)

    def test_native_observer_bound_stops_only_helper(self):
        with tempfile.TemporaryDirectory() as directory:
            child, stop, helper, record = mock.Mock(pid=123), mock.Mock(), mock.Mock(), {}
            child.poll.return_value = None
            stop.wait.return_value = False; stop.is_set.return_value = False
            helper.poll.side_effect = [None, -9]
            helper.communicate.return_value = (b'', b''); helper.returncode = -9
            with mock.patch.object(NATIVE, 'launch_owned', return_value=helper), \
                 mock.patch.object(NATIVE.time, 'monotonic', side_effect=[0,5,5]):
                NATIVE.collect(child,Path('tool'),Path(directory),stop,mock.Mock(),record)
            helper.kill.assert_called_once(); child.kill.assert_not_called(); child.terminate.assert_not_called()
            self.assertTrue(record['boundedStop']); self.assertEqual(record['seconds'],5)

    def test_finished_native_child_is_not_observed(self):
        child, stop, record = mock.Mock(), mock.Mock(), {}
        stop.wait.return_value = False; child.poll.return_value = 0
        with mock.patch.object(NATIVE,'launch_owned') as launch:
            NATIVE.collect(child,Path('tool'),Path('unused'),stop,mock.Mock(),record)
        launch.assert_not_called()

    @unittest.skipUnless(TOOL, 'Requires native hosted Windows helper')
    def test_actual_owned_process_dump_and_wrong_identity_rejection(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            child = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(30)'])
            try:
                rejected = NATIVE.launch_owned(child, TOOL, root / 'rejected.dmp', expected_pid=0)
                rejected.communicate(timeout=7)
                self.assertNotEqual(rejected.returncode,0)
                self.assertFalse((root/'rejected.dmp').exists())
                probe = NATIVE.launch_owned(child, TOOL, root / 'owned.dmp')
                out, err = probe.communicate(timeout=7)
                self.assertEqual(probe.returncode,0,(out,err))
                self.assertEqual(NATIVE.validate_dump(root/'owned.dmp',child.pid)['processId'],child.pid)
                self.assertIsNone(child.poll(), 'Observer must leave owned child alive')
                # CREATE_NEW must not overwrite an existing capture.
                before=(root/'owned.dmp').read_bytes()
                repeated=NATIVE.launch_owned(child,TOOL,root/'owned.dmp')
                repeated.communicate(timeout=7)
                self.assertNotEqual(repeated.returncode,0)
                self.assertEqual((root/'owned.dmp').read_bytes(),before)
            finally:
                if child.poll() is None: child.terminate()
                child.wait(timeout=5)


if __name__ == '__main__': unittest.main()
