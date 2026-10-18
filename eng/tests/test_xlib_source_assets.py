#!/usr/bin/env python3
"""Evaluate source-app assets using real staged producer files; never execute them."""
import argparse
import hashlib
import json
from itertools import product
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--progpu-root', type=Path, default=ROOT / 'external/ProGPU')
    parser.add_argument('--runtime-root', required=True, type=Path)
    args = parser.parse_args()
    payload = args.runtime_root.resolve(strict=True)
    target = args.progpu_root.resolve(strict=True) / 'src/ProGPU.Backend/buildTransitive/ProGPU.Backend.targets'
    files = {}
    for rid in ('linux-x64', 'linux-arm64'):
        native = payload / rid / 'native'
        receipt = json.loads((native / 'wgpu-native-linux.json').read_text())
        assert receipt['rid'] == rid and receipt['abiTestsPassed'] is True
        for name, digest in receipt['files'].items():
            path = native / name
            assert hashlib.sha256(path.read_bytes()).hexdigest() == digest, path
        for path in native.iterdir():
            assert path.is_file() and not path.is_symlink(), path
            files[path.relative_to(payload).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    count = 0
    with tempfile.TemporaryDirectory(prefix='forms-xlib-source-assets-') as temporary:
        root = Path(temporary)
        # Only the original target and original staged payload are selected.
        # This private layout tests discovery; it is not a native build or package.
        source = root / 'source'
        selected = source / 'src/ProGPU.Backend/buildTransitive/ProGPU.Backend.targets'
        selected.parent.mkdir(parents=True)
        selected.write_bytes(target.read_bytes())
        (source / 'src/ProGPU.Backend/ProGPU.Backend.csproj').write_bytes(
            (args.progpu_root / 'src/ProGPU.Backend/ProGPU.Backend.csproj').read_bytes())
        stage = source / 'artifacts/progpu-xlib/package'
        stage.parent.mkdir(parents=True)
        stage.symlink_to(payload, target_is_directory=True)
        for boundary, suffix in product(('repository', 'sdk'), ('/', '')):
            for rid, mode, output, enabled, explicit, source_test in (
                    ('', 'Project', 'Exe', True, False, False),
                    ('linux-x64', 'Project', 'Exe', True, False, False),
                    ('linux-arm64', 'Project', 'WinExe', True, True, False),
                    ('win-x64', 'Project', 'Exe', True, False, False),
                    ('osx-arm64', 'Project', 'Exe', True, False, False),
                    ('', 'Package', 'Exe', True, True, False),
                    ('', 'Project', 'Library', True, True, False),
                    ('', 'Project', 'Exe', False, True, False),
                    ('', '', 'Exe', False, False, True),
                    ('linux-arm64', '', 'Library', False, True, True)):
                document = ET.Element('Project')
                properties = ET.SubElement(document, 'PropertyGroup')
                values = dict(OutputType=output, TargetFramework='net10.0', RuntimeIdentifier=rid,
                              LibreWinFormsReferenceMode=mode,
                              LibreWinFormsUseProGpuSystemDrawing=str(enabled).lower(),
                              LibreWinFormsUsePortableFrameworkReferences='true',
                              LibreWinFormsUseSourceXlibRuntime=str(source_test).lower(),
                              IsTestProject=str(source_test).lower(),
                              LibreWinFormsSourceRoot=str(ROOT) + '/',
                              LibreWinFormsDrawingRoot=str(ROOT / 'src/ProGPU.Drawing') + '/',
                              LibreWinFormsProGpuSourceRoot=str(source) + suffix)
                if explicit:
                    values['ProGpuXlibSourceRuntimeRoot'] = str(payload)
                for name, value in values.items():
                    ET.SubElement(properties, name).text = value
                imported = ROOT / ('eng/LibreWinForms.ProGPU.targets' if boundary == 'repository'
                                   else 'src/LibreWinForms.Sdk/targets/LibreWinForms.Sdk.targets')
                ET.SubElement(document, 'Import', Project=str(imported))
                # The repository and SDK can both reach this shared helper.
                ET.SubElement(document, 'Import', Project=str(ROOT / 'eng/LibreWinForms.XlibRuntime.targets'))
                project = root / 'control.proj'
                ET.ElementTree(document).write(project, encoding='unicode')
                command = [args.dotnet, 'msbuild', str(project), '-nologo', '-getItem:Content',
                           '-p:LibreWinFormsProGpuSourceRoot=' + str(source) + suffix]
                if boundary == 'repository':
                    command.append('-target:ValidateLibreWinFormsProGpuProjectReference')
                result = subprocess.run(command,
                                        capture_output=True, text=True, timeout=30)
                assert result.returncode == 0, result.stdout + result.stderr
                items = json.loads(result.stdout)['Items']['Content']
                expected = {name: digest for name, digest in files.items()
                            if ((mode == 'Project' and output != 'Library' and enabled) or source_test)
                            and (not rid or name.startswith(rid + '/'))}
                actual = {}
                for item in items:
                    link = item['Link']
                    assert link.startswith('runtimes/'), item
                    name = link.removeprefix('runtimes/')
                    assert name not in actual, 'Duplicate runtime asset'
                    assert item['CopyToOutputDirectory'] == item['CopyToPublishDirectory'] == 'PreserveNewest'
                    actual[name] = hashlib.sha256(Path(item['FullPath']).read_bytes()).hexdigest()
                assert actual == expected, (boundary, suffix, rid, mode, output, enabled, actual, expected)
                count += 1
    print(f'{count} real-payload source asset evaluations passed; no native application executed.')


if __name__ == '__main__':
    main()
