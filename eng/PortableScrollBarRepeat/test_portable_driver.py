import copy
import hashlib
from unittest.mock import patch
import test_geometry
from pathlib import Path
from types import SimpleNamespace
import tempfile
import unittest
import portable_driver as driver

class PayloadIdentity(unittest.TestCase):
    def test_read_only_completion_retains_exact_output_identity(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "application.dll"
            path.write_bytes(b"original")
            helper = SimpleNamespace(digest=lambda p: hashlib.sha256(p.read_bytes()).hexdigest())
            identity = {str(path): helper.digest(path)}
            receipt = {}
            driver.verify_payload(helper, identity, receipt)
            self.assertEqual(identity, receipt['payloadAfter'])

    def test_changed_payload_retains_mismatch_and_rejects_completion(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "application.dll"
            path.write_bytes(b"original")
            helper = SimpleNamespace(digest=lambda p: hashlib.sha256(p.read_bytes()).hexdigest())
            identity = {str(path): helper.digest(path)}
            path.write_bytes(b"changed")
            receipt = {}
            with self.assertRaisesRegex(RuntimeError, 'payload changed'):
                driver.verify_payload(helper, identity, receipt)
            self.assertNotEqual(identity, receipt['payloadAfter'])


class CompleteNativeInventory(unittest.TestCase):
    def test_later_inventory_must_retain_every_original_window_and_metadata(self):
        original=test_geometry.PassiveShadowGuards().observation()
        native=object.__new__(driver.Native)
        native.desktop=object();native.child=SimpleNamespace(pid=123)
        native.source_ready=test_geometry.receipt();native.root_inventory=original
        with patch.object(driver,'native_root_inventory',return_value=copy.deepcopy(original)):
            self.assertEqual(native.owner_now()['hwnd'],900)
        changed=[]
        missing=copy.deepcopy(original);missing['windows'].pop();missing['metadata'].pop();changed.append(missing)
        style=copy.deepcopy(original);style['metadata'][0]['style']^=1;changed.append(style)
        moved=copy.deepcopy(original);moved['windows'][1]['bounds']['x']+=1;changed.append(moved)
        for observation in changed:
            with self.subTest(observation=observation),patch.object(driver,'native_root_inventory',return_value=observation):
                with self.assertRaisesRegex(RuntimeError,'Complete owned native inventory changed'):native.owner_now()

if __name__ == '__main__': unittest.main()
