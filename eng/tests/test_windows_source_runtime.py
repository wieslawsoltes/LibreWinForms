"""Forms import/ownership controls with a metadata-only producer boundary recorder."""
from pathlib import Path
import json
import os
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
OWNED = ('wgpu_native.dll', 'wgpu-native-build.json', 'LICENSE.MIT', 'LICENSE.APACHE',
         'wgpu-LICENSE.MIT', 'wgpu-LICENSE.APACHE', 'webgpu-headers-LICENSE', 'wgpu-native-windows.json')

class WindowsSourceRuntimeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='forms-windows-source-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.producer = self.root / 'producer'
        self.targets = self.producer / 'src/ProGPU.Backend/buildTransitive'
        self.targets.mkdir(parents=True)
        (self.producer / 'eng').mkdir()
        (self.targets.parent / 'ProGPU.Backend.csproj').write_text('<Project />')
        (self.producer / 'artifacts/progpu-xlib/package').mkdir(parents=True)
        self.native = self.producer / 'artifacts/progpu-native/package'
        for rid in ('win-x64', 'win-arm64'):
            directory = self.native / 'runtimes' / rid / 'native'
            directory.mkdir(parents=True)
            for name in OWNED + ('progpu_native.dll', 'private-renderer.json'):
                (directory / name).write_bytes(b'synthetic ownership control; never loaded')
        # Records only the imported root, with no renderer or copied implementation.
        (self.targets / 'ProGPU.Backend.Windows.targets').write_text(
            '<Project><ItemGroup><ObservedWindowsRoot Include="$(ProGpuWgpuWindowsSourceRuntimeRoot)" />'
            '</ItemGroup></Project>')
        (self.targets / 'ProGPU.Backend.targets').write_text(
            '<Project><Import Project="$(MSBuildThisFileDirectory)ProGPU.Backend.Windows.targets" /></Project>')
        (self.producer / 'eng/progpu-wgpu-windows-runtime.py').write_text('# metadata capability recorder only\n')

    def evaluate(self, boundary='repository', mode='Project', output='Exe', rid='win-x64',
                 enabled=True, test=False, explicit=False, suffix='', legacy=False, missing_target=False, validate=False):
        if legacy:
            (self.producer / 'eng/progpu-wgpu-windows-runtime.py').unlink()
            (self.targets / 'ProGPU.Backend.targets').write_text('<Project />')
        if missing_target:
            (self.targets / 'ProGPU.Backend.Windows.targets').unlink()
        project = ET.Element('Project')
        group = ET.SubElement(project, 'PropertyGroup')
        values = dict(OutputType=output, RuntimeIdentifier=rid, TargetFramework='net10.0',
                      IsTestProject=str(test).lower(), LibreWinFormsReferenceMode=mode,
                      LibreWinFormsUseProGpuSystemDrawing=str(enabled).lower(),
                      LibreWinFormsUsePortableFrameworkReferences='true',
                      LibreWinFormsUseSourceNativeRuntime=str(test).lower(),
                      LibreWinFormsProGpuSourceRoot=str(self.producer) + suffix,
                      LibreWinFormsSourceRoot=str(ROOT) + '/',
                      LibreWinFormsDrawingRoot=str(ROOT / 'src/ProGPU.Drawing') + '/')
        selected = self.root / 'explicit-windows' if explicit else self.producer / 'artifacts/progpu-wgpu-windows/package'
        if explicit: values['ProGpuWgpuWindowsSourceRuntimeRoot'] = str(selected)
        for name, value in values.items(): ET.SubElement(group, name).text = value
        target = {'repository': ROOT / 'eng/LibreWinForms.ProGPU.targets',
                  'sdk': ROOT / 'src/LibreWinForms.Sdk/targets/LibreWinForms.Sdk.targets',
                  'native': ROOT / 'eng/LibreWinForms.NativeRuntime.targets'}[boundary]
        ET.SubElement(project, 'Import', Project=str(target))
        path = self.root / 'control.proj'
        ET.ElementTree(project).write(path, encoding='unicode')
        command = [os.environ.get('DOTNET_HOST_PATH') or shutil.which('dotnet'),
            'msbuild', str(path), '-nologo', '-getItem:Content,ObservedWindowsRoot']
        if validate: command.append('-target:ValidateLibreWinFormsOwnedWindowsSourceRuntime')
        result = subprocess.run(command, cwd=ROOT,
            text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=30)
        if result.returncode: return result, None, selected
        return result, json.loads(result.stdout[result.stdout.index('{'):])['Items'], selected

    def check_selected(self, **kwargs):
        result, items, selected = self.evaluate(**kwargs)
        self.assertEqual(0, result.returncode, result.stdout)
        self.assertEqual([selected.resolve()], [Path(x['Identity']).resolve() for x in items['ObservedWindowsRoot']])
        identities = [Path(x['Identity']).name for x in items['Content']]
        self.assertEqual({'progpu_native.dll', 'private-renderer.json'}, set(identities))
        return items

    def test_repository_prepares_root_before_shared_backend_import(self):
        self.check_selected()

    def test_sdk_prepares_root_before_shared_backend_import(self):
        self.check_selected(boundary='sdk')

    def test_drawing_source_test_imports_directly_without_xlib(self):
        self.check_selected(boundary='native', mode='', output='Library', test=True)

    def test_explicit_root_is_preserved(self):
        self.check_selected(explicit=True)

    def test_separated_root_selects_same_owner(self):
        self.check_selected(suffix='/')

    def test_portable_native_payload_has_one_default_owner(self):
        items = self.check_selected(rid='')
        self.assertEqual(4, len(items['Content']))
        self.assertTrue(all(x['Link'].replace('\\', '/').startswith('runtimes/') for x in items['Content']))

    def test_arm64_specific_native_payload_keeps_loader_root(self):
        items = self.check_selected(rid='win-arm64')
        self.assertEqual({'progpu_native.dll', 'private-renderer.json'}, {x['Link'] for x in items['Content']})

    def test_legacy_qualified_producer_preserves_original_native_inventory(self):
        result, items, _ = self.evaluate(legacy=True, validate=True)
        self.assertEqual(0, result.returncode, result.stdout)
        self.assertEqual([], items['ObservedWindowsRoot'])
        self.assertEqual(set(OWNED) | {'progpu_native.dll', 'private-renderer.json'},
                         {Path(x['Identity']).name for x in items['Content']})

    def test_package_mode_never_imports_source_windows_owner(self):
        result, items, _ = self.evaluate(mode='Package', explicit=True, validate=True)
        self.assertEqual(0, result.returncode, result.stdout)
        self.assertEqual([], items['ObservedWindowsRoot'])
        self.assertEqual([], items['Content'])

    def test_ordinary_library_never_imports_source_windows_owner(self):
        result, items, _ = self.evaluate(output='Library', explicit=True, validate=True)
        self.assertEqual(0, result.returncode, result.stdout)
        self.assertEqual([], items['ObservedWindowsRoot'])
        self.assertEqual([], items['Content'])

    def test_disabled_source_drawing_never_imports_windows_owner(self):
        result, items, _ = self.evaluate(enabled=False, validate=True)
        self.assertEqual(0, result.returncode, result.stdout)
        self.assertEqual([], items['ObservedWindowsRoot'])
        self.assertEqual([], items['Content'])

    def test_selected_windows_payload_readiness_preserves_exact_owner(self):
        for rid in ('win-x64', 'win-arm64'):
            directory = self.producer / 'artifacts/progpu-wgpu-windows/package' / rid / 'native'
            directory.mkdir(parents=True)
            for name in OWNED:
                (directory / name).write_bytes(b'synthetic metadata only; never loaded')
        for rid in ('win-x64', 'win-arm64'):
            with self.subTest(rid=rid):
                self.check_selected(rid=rid, validate=True)

    def test_missing_explicit_windows_payload_rejects_before_build(self):
        for rid in ('win-x64', 'win-arm64'):
            with self.subTest(rid=rid):
                result, _, _ = self.evaluate(rid=rid, validate=True)
                self.assertNotEqual(0, result.returncode)
                self.assertIn('Missing whole-qualified owned ' + rid, result.stdout)

    def test_missing_receipt_rejects_even_when_owned_library_exists(self):
        for rid in ('win-x64', 'win-arm64'):
            directory = self.producer / 'artifacts/progpu-wgpu-windows/package' / rid / 'native'
            directory.mkdir(parents=True)
            (directory / 'wgpu_native.dll').write_bytes(b'never loaded')
            with self.subTest(rid=rid):
                result, _, _ = self.evaluate(rid=rid, validate=True)
                self.assertNotEqual(0, result.returncode)
                self.assertIn('Missing whole-qualified owned ' + rid, result.stdout)

    def test_portable_windows_requires_each_complete_rid_pair(self):
        for rid in ('win-x64', 'win-arm64'):
            directory = self.producer / 'artifacts/progpu-wgpu-windows/package' / rid / 'native'
            directory.mkdir(parents=True)
            for name in ('wgpu_native.dll', 'wgpu-native-windows.json'):
                (directory / name).write_bytes(b'never loaded')
        result, _, _ = self.evaluate(rid='', validate=True)
        self.assertEqual(0, result.returncode, result.stdout)
        for rid in ('win-x64', 'win-arm64'):
            for name in ('wgpu_native.dll', 'wgpu-native-windows.json'):
                with self.subTest(rid=rid, missing=name):
                    path = self.producer / 'artifacts/progpu-wgpu-windows/package' / rid / 'native' / name
                    original = path.read_bytes()
                    path.unlink()
                    try:
                        result, _, _ = self.evaluate(rid='', validate=True)
                        self.assertEqual(os.name != 'nt', result.returncode == 0, result.stdout)
                    finally:
                        path.write_bytes(original)

    def test_portable_missing_payload_guard_uses_actual_host(self):
        result, _, _ = self.evaluate(rid='', validate=True)
        self.assertEqual(os.name != 'nt', result.returncode == 0, result.stdout)

    def test_declared_capability_missing_target_fails(self):
        result, _, _ = self.evaluate(missing_target=True)
        self.assertNotEqual(0, result.returncode)
        self.assertIn('ProGPU.Backend.Windows.targets', result.stdout)

if __name__ == '__main__':
    unittest.main()
