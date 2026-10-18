#!/usr/bin/env python3
"""Evaluate source native deployment with real qualified payloads, without execution."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--runtime-root', type=Path, required=True)
    args = parser.parse_args()
    payload = args.runtime_root.resolve(strict=True)
    files = {p.relative_to(payload).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
             for p in (payload / 'runtimes').glob('*/native/**/*')
             if p.is_file() and 'sdk' not in p.relative_to(payload).parts}
    assert files and any('progpu_native' in name for name in files)
    rids = sorted({name.split('/')[1] for name in files})
    count = 0
    with tempfile.TemporaryDirectory(prefix='forms-native-source-assets-') as temporary:
        root = Path(temporary)
        source = root / 'producer'
        stage = source / 'artifacts/progpu-native/package'
        stage.parent.mkdir(parents=True)
        stage.symlink_to(payload, target_is_directory=True)
        for suffix in ('', '/'):
            for rid in ['', *rids, 'unsupported-test-rid']:
                for mode, output, test, enabled, explicit in (
                        ('Project', 'Exe', False, True, False),
                        ('Project', 'WinExe', False, True, True),
                        ('Project', 'Library', False, True, True),
                        ('Project', 'Exe', False, False, True),
                        ('Package', 'Exe', False, True, True),
                        ('', 'Library', True, True, False)):
                    project = ET.Element('Project')
                    properties = ET.SubElement(project, 'PropertyGroup')
                    values = dict(OutputType=output, RuntimeIdentifier=rid,
                                  IsTestProject=str(test).lower(),
                                  LibreWinFormsReferenceMode=mode,
                                  LibreWinFormsUseProGpuSystemDrawing=str(enabled).lower(),
                                  LibreWinFormsUseSourceNativeRuntime=str(test).lower(),
                                  LibreWinFormsProGpuSourceRoot=str(source) + suffix)
                    if explicit:
                        values['ProGpuNativeRuntimeRoot'] = str(payload) + suffix
                    for name, value in values.items():
                        ET.SubElement(properties, name).text = value
                    ET.SubElement(project, 'Import', Project=str(ROOT / 'eng/LibreWinForms.NativeRuntime.targets'))
                    path = root / 'control.proj'
                    ET.ElementTree(project).write(path, encoding='unicode')
                    result = subprocess.run([args.dotnet, 'msbuild', str(path), '-nologo',
                                             '-getItem:Content',
                                             '-p:LibreWinFormsProGpuSourceRoot=' + str(source) + suffix],
                                            capture_output=True, text=True, timeout=30)
                    assert result.returncode == 0, result.stdout + result.stderr
                    active = test or (mode == 'Project' and output != 'Library' and enabled)
                    expected = {}
                    for name, digest in files.items():
                        if not active or (rid and name.split('/')[1] != rid):
                            continue
                        expected[name if not rid else name.split('/native/', 1)[1]] = digest
                    actual = {}
                    for item in json.loads(result.stdout)['Items']['Content']:
                        link = item['Link'].replace('\\', '/')
                        assert link not in actual, 'Duplicate runtime asset'
                        assert item['CopyToOutputDirectory'] == item['CopyToPublishDirectory'] == 'PreserveNewest'
                        assert item['ExcludeFromSingleFile'] == 'true'
                        actual[link] = hashlib.sha256(Path(item['FullPath']).read_bytes()).hexdigest()
                    assert actual == expected, (suffix, rid, mode, output, test, actual, expected)
                    count += 1
    print(f'{count} real-payload native source deployment evaluations passed; no application executed.')


if __name__ == '__main__':
    main()
