"""Evaluate the actual SDK targets: compiler policy, generated code and exact package edge."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[2]
DOTNET='dotnet'


class CompilerSdkTests(unittest.TestCase):
    def evaluate(self, **changes):
        settings=dict(LibreWinFormsDx12ShaderCompiler='Automatic',RuntimeIdentifier='win-arm64',
            LibreWinFormsReferenceMode='Package',LibreWinFormsGenerateApplicationBootstrap='true',
            LibreWinFormsUsePortableFrameworkReferences='true',OutputType='WinExe',
            LibreWinFormsProGpuPackageVersion='1.2.3',LibreWinFormsCanonicalPackageVersion='1.2.3',
            LibreWinFormsProGpuBackendPackageVersion='1.2.3',LibreWinFormsPlatform='SilkNet',LibreWinFormsRenderingBackend='ProGPU')
        settings.update(changes)
        with tempfile.TemporaryDirectory(prefix='forms-dx12-sdk-') as directory:
            root=Path(directory);project=ET.Element('Project');properties=ET.SubElement(project,'PropertyGroup')
            settings['IntermediateOutputPath']=str(root/'obj')+'/'
            for key,value in settings.items():ET.SubElement(properties,key).text=value
            ET.SubElement(project,'Import',Project=str(ROOT/'src/LibreWinForms.Sdk/targets/LibreWinForms.Sdk.targets'))
            path=root/'contract.proj';ET.ElementTree(project).write(path)
            result_file=root/'evaluation.json'
            result=subprocess.run([DOTNET,'msbuild',str(path),'-nologo',
                '-t:_LibreWinFormsValidateDx12Compiler;_LibreWinFormsGenerateApplicationBootstrap',
                '-getItem:PackageReference','-getResultOutputFile:'+str(result_file)],capture_output=True,text=True)
            diagnostics=result.stdout+result.stderr
            if result.returncode == 0:
                self.assertTrue(result_file.is_file(),diagnostics)
                output=result_file.read_text(encoding='utf-8-sig')
            else:
                output=diagnostics
            code=root/'obj/LibreWinForms.ApplicationBootstrap.g.cs'
            return result.returncode,output,code.read_text() if code.exists() else ''

    def test_default_preserves_registration_and_has_no_compiler_package(self):
        rc,output,code=self.evaluate();self.assertEqual(rc,0,output)
        self.assertIn('ProGpuPlatform.Register();',code)
        self.assertNotIn('WgpuDx12CompilerOptions',code)
        self.assertFalse(any(item['Identity']=='ProGPU.Backend.Dx12' for item in json.loads(output)['Items']['PackageReference']))

    def test_explicit_compilers_generate_typed_options_and_exact_dxc_dependency(self):
        for compiler in ('Dxc','Fxc','dxc','fxc'):
            for rid in ('win-x64','win-arm64'):
                for central in ('true','false'):
                    with self.subTest(compiler=compiler,rid=rid,central=central):
                        rc,output,code=self.evaluate(LibreWinFormsDx12ShaderCompiler=compiler,RuntimeIdentifier=rid,ManagePackageVersionsCentrally=central)
                        self.assertEqual(rc,0,output)
                        self.assertIn('WgpuDx12ShaderCompiler.'+compiler.capitalize(),code)
                        self.assertNotIn('ProGpuPlatform.Register();',code)
                        items=[item for item in json.loads(output)['Items']['PackageReference'] if item['Identity']=='ProGPU.Backend.Dx12']
                        self.assertEqual(len(items),1 if compiler.lower()=='dxc' else 0)
                        if items:self.assertEqual(items[0]['VersionOverride' if central=='true' else 'Version'],'1.2.3')

    def test_invalid_and_unusable_choices_fail_before_bootstrap(self):
        for changes in (dict(LibreWinFormsDx12ShaderCompiler='Other'),dict(RuntimeIdentifier='osx-arm64'),
                        dict(RuntimeIdentifier=''),dict(OutputType='Library'),dict(LibreWinFormsReferenceMode='Project'),
                        dict(LibreWinFormsPlatform='Other'),dict(LibreWinFormsRenderingBackend='Other'),dict(LibreWinFormsGenerateApplicationBootstrap='false'),dict(LibreWinFormsUsePortableFrameworkReferences='false')):
            settings=dict(LibreWinFormsDx12ShaderCompiler='Dxc');settings.update(changes)
            with self.subTest(settings=settings):
                rc,output,code=self.evaluate(**settings)
                self.assertNotEqual(rc,0,output);self.assertEqual(code,'')


if __name__=='__main__':
    import sys
    if '--dotnet' in sys.argv:
        i=sys.argv.index('--dotnet');DOTNET=sys.argv[i+1];del sys.argv[i:i+2]
    unittest.main()
