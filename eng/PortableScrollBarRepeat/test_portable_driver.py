import hashlib
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

if __name__ == '__main__': unittest.main()
