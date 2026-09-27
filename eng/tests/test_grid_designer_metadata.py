import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('grid_metadata',
    Path(__file__).resolve().parents[1] / 'librewinforms-grid-designer-metadata.py')
metadata = importlib.util.module_from_spec(spec)
spec.loader.exec_module(metadata)


class MetadataTests(unittest.TestCase):
    def synthetic(self, newline=b'\n'):
        # Synthetic declarations exercise transformation only; they cannot pass
        # the checked-in production SHA pins without this test-local override.
        data, rules = {}, {}
        for name, (_, additions) in metadata.RULES.items():
            source = b'\xef\xbb\xbf// fixture' + newline
            for declaration, _ in additions:
                source += b'        ' + declaration.encode() + newline + b'        { get; set; }' + newline
            data[name] = source
            rules[name] = (metadata.digest(source), additions)
        return data, rules

    def test_pins_and_counts(self):
        self.assertEqual(len(metadata.RULES), 2)
        self.assertEqual([len(x[1]) for x in metadata.RULES.values()], [5, 4])
        self.assertEqual(metadata.SOURCE_COMMIT, 'acb39ceb13f910ae0f8f6298059c59102b749c41')

    def test_only_nine_attributes_and_bom_newlines_preserved(self):
        for newline in (b'\n', b'\r\n'):
            data, rules = self.synthetic(newline)
            with patch.dict(metadata.RULES, rules, clear=True):
                for name, source in data.items():
                    result = metadata.annotate(name, source)
                    self.assertTrue(result.startswith(b'\xef\xbb\xbf'))
                    for _, attribute in rules[name][1]:
                        result = result.replace(b'        ' + attribute.encode() + newline, b'', 1)
                    self.assertEqual(result, source)

    def test_actual_field_defaults_not_maskedtextbox_documentation_defaults(self):
        additions = dict(metadata.RULES[metadata.PREFIX + 'MaskedTextBoxColumn.cs'][1])
        self.assertIn("'\\0'", additions['public virtual char PromptChar'])
        self.assertIn('false', additions['public virtual bool IncludeLiterals'])
        self.assertIn('null', additions['public virtual string Mask'])

    def test_runtime_editor_relationships_are_not_serialized(self):
        for _, attribute in metadata.RULES[metadata.PREFIX + 'MaskedTextBoxEditingControl.cs'][1]:
            self.assertIn('DesignerSerializationVisibility.Hidden', attribute)

    def test_production_pins_reject_synthetic_or_changed_source(self):
        data, _ = self.synthetic()
        for name, source in data.items():
            with self.assertRaisesRegex(ValueError, 'pinned upstream'):
                metadata.annotate(name, source)
        with self.assertRaisesRegex(ValueError, 'Unsupported'):
            metadata.annotate('../unexpected.cs', b'')

    def test_missing_or_duplicate_declaration_fails(self):
        data, rules = self.synthetic()
        name = next(iter(data))
        for source in (b'none\n', data[name] + data[name]):
            changed = {name: (metadata.digest(source), rules[name][1])}
            with patch.dict(metadata.RULES, changed), self.assertRaisesRegex(ValueError, 'Missing/duplicate'):
                metadata.annotate(name, source)

    def test_invalid_later_file_creates_no_destination(self):
        data, rules = self.synthetic()
        with tempfile.TemporaryDirectory() as directory, patch.dict(metadata.RULES, rules, clear=True):
            root = Path(directory)
            source = root / 'source'
            for name, value in data.items():
                path = source / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(value)
            (source / list(data)[1]).write_bytes(b'changed')
            with self.assertRaises(ValueError):
                metadata.prepare(source, root / 'destination')
            self.assertFalse((root / 'destination').exists())

    def test_valid_batch_and_no_overwrite(self):
        data, rules = self.synthetic()
        with tempfile.TemporaryDirectory() as directory, patch.dict(metadata.RULES, rules, clear=True):
            root = Path(directory)
            for name, value in data.items():
                path = root / 'source' / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(value)
            target = root / 'prepared'
            receipt = metadata.prepare(root / 'source', target)
            self.assertTrue(receipt['runtimeBodiesUnchanged'])
            self.assertFalse(receipt['analyzerSuppression'])
            self.assertEqual(sum(f['addedAttributes'] for f in receipt['files']), 9)
            with self.assertRaisesRegex(ValueError, 'must not exist'):
                metadata.prepare(root / 'source', target)


if __name__ == '__main__':
    unittest.main()
