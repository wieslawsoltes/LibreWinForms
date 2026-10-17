"""Compile the prepared observer from a complete exact producer; no desktop input."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile


def require(value, message):
    if not value:
        raise ValueError(message)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    for name in ('repository', 'source', 'feed', 'producer-pin', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    require(sys.platform == 'win32' and os.environ.get('GITHUB_ACTIONS') == 'true'
            and os.environ.get('RUNNER_ENVIRONMENT') == 'github-hosted'
            and os.environ.get('GITHUB_REPOSITORY') == 'wieslawsoltes/LibreWinForms', 'Disposable hosted Windows only')
    spec = importlib.util.spec_from_file_location('original_producer', args.repository / 'eng/OriginalGrid/prepare-hosted.py')
    original = importlib.util.module_from_spec(spec); spec.loader.exec_module(original)
    pin = json.loads(args.producer_pin.read_text(encoding='utf-8-sig'))
    build, docs = (original.api('actions/runs/' + str(pin[k])) for k in ('build', 'docs'))
    jobs = original.api('actions/runs/' + str(pin['build']) + '/jobs?per_page=100')
    artifact = original.api('actions/artifacts/' + str(pin['artifact']))
    original.validate_producer(pin, build, docs, jobs, artifact)
    feed = args.feed.resolve(strict=True); original.validate_feed(feed, pin)
    original.compiler_build_options('Portable', 'Dxc', pin)
    require(all(p['version'] == '0.1.0-source-first' for p in pin['packages']), 'Prepared package versions changed')
    root = args.output.resolve(); require(not root.exists(), 'Fresh application output required'); root.mkdir()
    source = root / 'source'; source.mkdir()
    selected = ('Program.cs', 'ObservedTheme.cs', 'PaintObservation.cs', 'PortableSetup.cs', 'Portable.csproj')
    sources = {}
    for name in selected:
        path = args.source / name
        require(path.is_file() and not path.is_symlink(), 'Original prepared source unavailable')
        shutil.copyfile(path, source / name); sources[name] = digest(path)
    for name in ('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props'):
        (root / name).write_text('<Project />\n')
    sdk = json.loads((args.repository / 'eng/OriginalGrid/source-contract.json').read_text())['sdkVersion']
    (root / 'global.json').write_text(json.dumps(dict(sdk=dict(version=sdk, allowPrerelease=True, rollForward='disable'))))
    config = ET.Element('configuration'); sources_xml = ET.SubElement(config, 'packageSources'); ET.SubElement(sources_xml, 'clear')
    ET.SubElement(sources_xml, 'add', key='ImmutableFormsClosure', value=str(feed))
    ET.SubElement(sources_xml, 'add', key='nuget.org', value='https://api.nuget.org/v3/index.json')
    mapping = ET.SubElement(config, 'packageSourceMapping'); local = ET.SubElement(mapping, 'packageSource', key='ImmutableFormsClosure')
    for pattern in ('LibreWinForms.*', 'ProGPU.*', 'System.Drawing.Common'):
        ET.SubElement(local, 'package', pattern=pattern)
    ET.SubElement(ET.SubElement(mapping, 'packageSource', key='nuget.org'), 'package', pattern='*')
    ET.ElementTree(config).write(root / 'NuGet.config', encoding='utf-8', xml_declaration=True)
    environment = {k:v for k,v in os.environ.items() if not re.search(r'TOKEN|SECRET|PASSWORD|LICENSE|CREDENTIAL|API_KEY|ACCESS_KEY',k,re.I)}
    for key in ('DOTNET_STARTUP_HOOKS','DOTNET_ADDITIONAL_DEPS','DOTNET_SHARED_STORE','DOTNET_ROLL_FORWARD'):
        environment.pop(key,None)
    dotnet = Path(shutil.which('dotnet')).resolve(strict=True)
    environment.update(DOTNET_ROOT=str(dotnet.parent), NUGET_PACKAGES=str(root/'packages'), DOTNET_CLI_TELEMETRY_OPTOUT='1')
    receipt = dict(producerHead=pin['head'], sourceHashes=sources, producerQualified=True, qualified=False,
                   inputExecuted=False, sdk=str(dotnet), sdkSha256=digest(dotnet))
    try:
        version = subprocess.check_output([str(dotnet),'--version'],cwd=root,env=environment,text=True,timeout=15).strip()
        require(version==sdk,'Original selected SDK changed');receipt['sdkVersion']=version
        with (root/'application-build.log').open('xb') as log:
            child=subprocess.Popen([str(dotnet),'build',str(source/'Portable.csproj'),'-c','Release','-m:1',
                '-nodeReuse:false','-p:UseSharedCompilation=false','--configfile',str(root/'NuGet.config')],
                cwd=root,env=environment,stdout=log,stderr=subprocess.STDOUT)
            try:code=child.wait(timeout=300)
            except subprocess.TimeoutExpired:
                subprocess.run(['taskkill','/PID',str(child.pid),'/T','/F'],stdout=log,stderr=subprocess.STDOUT,timeout=10,check=False)
                child.wait(timeout=10);raise
        receipt['buildExit']=code;require(code==0,'Prepared package application failed to compile')
        output=source/'bin/Release/net11.0/win-arm64'
        receipt['compiler']=original.verify_compiler_payload(output,feed,pin,'win-arm64')
        matches=[]
        for package in pin['packages']:
            with zipfile.ZipFile(feed/package['file']) as archive:
                for member in archive.namelist():
                    if not member.startswith('lib/net10.0/') or not member.endswith('.dll'):continue
                    target=output/Path(member).name
                    if not target.is_file():continue
                    require(digest(target)==hashlib.sha256(archive.read(member)).hexdigest(),'Package assembly substituted: '+member)
                    matches.append(dict(package=package['id'],member=member,sha256=digest(target)))
        require({'System.Windows.Forms.dll','System.Drawing.Common.dll','LibreWinForms.Platform.dll','LibreWinForms.ProGPU.dll'}
                <= {Path(m['member']).name for m in matches}, 'Required source package assembly matches incomplete')
        receipt['packageMatches']=matches
        receipt['app']=str(output/'PortableScrollBarRepeat.exe');require(Path(receipt['app']).is_file(),'Original apphost missing')
        receipt['outputs']=[dict(path=str(p.relative_to(output)),sha256=digest(p)) for p in sorted(output.rglob('*')) if p.is_file()]
        require(all(digest(args.source/n)==v for n,v in sources.items()),'Prepared source changed during build')
    finally:
        (root/'preparation-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')


if __name__=='__main__':main()
