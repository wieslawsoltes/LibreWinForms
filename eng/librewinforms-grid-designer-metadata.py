#!/usr/bin/env python3
"""Add explicit designer metadata to two pinned dotnet/samples grid files.

This is sample preparation, not analyzer suppression or a runtime compatibility
shim. Keep original sources separately; only an absent destination is writable.
"""
import argparse
import hashlib
import json
from pathlib import Path

SOURCE_COMMIT = 'acb39ceb13f910ae0f8f6298059c59102b749c41'
PREFIX = 'CustomDataGridViewColumn/'
RULES = {
    PREFIX + 'MaskedTextBoxColumn.cs': (
        'f9d3028e3a2381987690c696e542753a2e7a418dfb683fbfaf920e45f46c79d0', (
            ('public virtual string Mask', '[System.ComponentModel.DefaultValue(null)]'),
            ('public virtual char PromptChar', "[System.ComponentModel.DefaultValue('\\0')]"),
            ('public virtual bool IncludePrompt', '[System.ComponentModel.DefaultValue(false)]'),
            ('public virtual bool IncludeLiterals', '[System.ComponentModel.DefaultValue(false)]'),
            ('public virtual Type ValidatingType', '[System.ComponentModel.DefaultValue(null)]'),
        )),
    PREFIX + 'MaskedTextBoxEditingControl.cs': (
        '1050d60a9adfb03d54480092339d7b6e7f2fe2fe0bc65ece6f5e47917b610474', tuple(
            (declaration, '[System.ComponentModel.DesignerSerializationVisibility('
             'System.ComponentModel.DesignerSerializationVisibility.Hidden)]')
            for declaration in ('public DataGridView EditingControlDataGridView',
                                'public object EditingControlFormattedValue',
                                'public int EditingControlRowIndex',
                                'public bool EditingControlValueChanged'))),
}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def annotate(relative, source):
    require(relative in RULES, 'Unsupported sample source')
    expected, additions = RULES[relative]
    require(digest(source) == expected, 'Original sample bytes differ from pinned upstream source')
    newline = b'\r\n' if b'\r\n' in source else b'\n'
    replacements = []
    result = source
    for declaration, attribute in additions:
        original = b'        ' + declaration.encode('ascii') + newline
        expanded = b'        ' + attribute.encode('ascii') + newline + original
        require(result.count(original) == 1, 'Missing/duplicate original property declaration')
        result = result.replace(original, expanded, 1)
        replacements.append((original, expanded))
    restored = result
    for original, expanded in reversed(replacements):
        require(restored.count(expanded) == 1, 'Ambiguous added metadata')
        restored = restored.replace(expanded, original, 1)
    require(restored == source, 'Preparation changed more than explicit serialization attributes')
    return result


def prepare(source_root, destination):
    source_root = source_root.resolve(strict=True)
    require(source_root.is_dir(), 'Source directory required')
    require(not destination.exists() and not destination.is_symlink(), 'Destination must not exist')
    prepared = []
    # Validate the complete two-file batch before creating any output.
    for relative, (_, additions) in RULES.items():
        path = source_root / relative
        require(path.is_file() and not path.is_symlink(), 'Original regular source file required')
        source = path.read_bytes()
        output = annotate(relative, source)
        prepared.append((relative, output, dict(path=relative, sourceSha256=digest(source),
                         preparedSha256=digest(output), addedAttributes=len(additions))))
    destination.mkdir()
    for relative, output, _ in prepared:
        path = destination / relative
        path.parent.mkdir(exist_ok=True)
        with path.open('xb') as stream:
            stream.write(output)
    receipt = dict(schema='grid-designer-metadata-v1', upstreamCommit=SOURCE_COMMIT,
                   changes='nine explicit designer serialization attributes',
                   runtimeBodiesUnchanged=True, analyzerSuppression=False,
                   compiled=False, desktopQualified=False,
                   files=[record for _, _, record in prepared])
    with (destination / 'metadata.json').open('x') as stream:
        json.dump(receipt, stream, indent=2)
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, required=True)
    parser.add_argument('--destination', type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(prepare(args.source_root, args.destination), indent=2))


if __name__ == '__main__':
    main()
