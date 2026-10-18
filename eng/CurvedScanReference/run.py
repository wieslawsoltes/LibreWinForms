"""Isolated pinned SDK bootstrap; original no-input app and deadlines unchanged."""
from pathlib import Path, PurePosixPath
import hashlib
import json
import os
import shutil
import subprocess
import urllib.request
import zipfile

if os.name != 'nt' or os.environ.get('GITHUB_ACTIONS') != 'true' or os.environ.get('RUNNER_ENVIRONMENT') != 'github-hosted':
    raise RuntimeError('Disposable hosted Windows required')
source = Path(__file__).resolve().parent
root = Path(os.environ['RUNNER_TEMP']).resolve() / 'authored-scan-reference'
root.mkdir(exist_ok=False)
sdk = root / 'owned-sdk'
archive = root / 'owned-sdk.zip'
pin = json.loads((source / 'sdk-pin.json').read_text())
assert pin['version'] == '10.0.301' and pin['file']['rid'] == 'win-arm64'
for name in ('Program.cs', 'NativeDriver.cs', 'GdiBitmapCapture.cs', 'GdiOutlineCapture.cs', 'ScanReference.csproj', 'global.json', 'sdk-pin.json', 'run.py', 'inputs.json', 'ProGPU-Authored-Scan-1.ttf', 'ProGPU-Authored-Scan-3.ttf'):
    shutil.copy2(source / name, root / name)
try:
    digest = hashlib.sha512()
    count = 0
    with urllib.request.urlopen(pin['file']['url'], timeout=30) as response, archive.open('xb') as output:
        while block := response.read(1024 * 1024):
            count += len(block)
            if count > 512 * 1024 * 1024:
                raise RuntimeError('SDK archive size bound exceeded')
            digest.update(block)
            output.write(block)
    if digest.hexdigest() != pin['file']['hash']:
        raise RuntimeError('Official SDK SHA512 mismatch')
    with zipfile.ZipFile(archive) as payload:
        names = payload.namelist()
        if len(names) != len(set(names)) or sum(i.file_size for i in payload.infolist()) > 2 * 1024**3:
            raise RuntimeError('SDK inventory bound exceeded')
        for name in names:
            parts = PurePosixPath(name.replace('\\', '/'))
            if parts.is_absolute() or '..' in parts.parts or ':' in name:
                raise RuntimeError('SDK path escapes owned root')
        payload.extractall(sdk)
    env = dict(os.environ, DOTNET_ROOT=str(sdk), DOTNET_MULTILEVEL_LOOKUP='0',
               DOTNET_CLI_HOME=str(root / 'cli-home'), DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_CLI_USE_MSBUILD_SERVER='0', MSBUILDDISABLENODEREUSE='1')
    dotnet = sdk / 'dotnet.exe'
    version = subprocess.check_output([str(dotnet), '--version'], cwd=root, env=env, timeout=20, text=True).strip()
    if version != pin['version']:
        raise RuntimeError('Exact SDK selection changed')
    (root / 'bootstrap.json').write_text(json.dumps(dict(version=version, bytes=count, sha512=digest.hexdigest(),
        isolatedRoot=True, authoredReference=True), indent=2) + '\n')
    with (root / 'build.log').open('w') as log:
        subprocess.run([str(dotnet), 'build', 'ScanReference.csproj', '-c', 'Release', '-r', 'win-arm64', '--nologo', '-nodeReuse:false', '-p:UseSharedCompilation=false'],
                       cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=120, check=True)
    output = root / 'evidence'
    app = root / 'bin/Release/net10.0-windows/win-arm64/ScanReference.dll'
    with (root / 'stdout.log').open('w') as stdout, (root / 'stderr.log').open('w') as stderr:
        child = subprocess.Popen([str(dotnet), str(app), str(root), str(output)], cwd=root, env=env, stdout=stdout, stderr=stderr)
        try:
            code = child.wait(timeout=60)
        except subprocess.TimeoutExpired:
            subprocess.run(['taskkill', '/PID', str(child.pid), '/T', '/F'], check=False, timeout=10)
            child.wait(timeout=10)
            raise RuntimeError('Original 60-second controller deadline exceeded')
        if code:
            raise RuntimeError(f'Authored scan reference exited {code}')
    result = json.loads((output / 'reference.json').read_text())
    if len(result['frames']) != 2640 or len(result['fonts']) != 2 or result['qualified'] or result['inputEvents'] or result['desktopWindows'] or result['elapsedSeconds'] > 45:
        raise RuntimeError('Incomplete authored bounded no-input inventory')
    (root / 'source-hashes.json').write_text(json.dumps({name:hashlib.sha256((root / name).read_bytes()).hexdigest()
        for name in ('Program.cs','NativeDriver.cs','GdiBitmapCapture.cs','GdiOutlineCapture.cs','ScanReference.csproj','global.json','sdk-pin.json','run.py','inputs.json','ProGPU-Authored-Scan-1.ttf','ProGPU-Authored-Scan-3.ttf','evidence/reference.json')}, indent=2) + '\n')
finally:
    if archive.exists():
        archive.unlink()
    if sdk.exists():
        shutil.rmtree(sdk)
