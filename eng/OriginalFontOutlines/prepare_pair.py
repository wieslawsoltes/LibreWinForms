import hashlib
import json
import os
from pathlib import Path
import shutil
import urllib.request
import zipfile
import xml.etree.ElementTree as ET

source = Path(__file__).resolve().parent
if os.name != 'nt' or os.environ.get('GITHUB_ACTIONS') != 'true' or os.environ.get('RUNNER_ENVIRONMENT') != 'github-hosted':
    raise RuntimeError('Disposable hosted Windows required')
receipt = json.loads((source / 'producer-build.json').read_text())['structuredContent']
artifact = json.loads((source / 'producer-artifact.json').read_text())
head = 'aebdc1d6b4ba5a790dbdf64c9e4555c3d8f44c9f'
if receipt['headRevision'] != head or receipt['hasMore'] or len(receipt['jobs']) != 61 or not all(
    x['head_sha'] == head and x['status'] == 'completed' and x['conclusion'] == 'success' for x in receipt['jobs']):
    raise RuntimeError('Complete exact successful producer Build receipt required')
if artifact['workflow_run']['head_sha'] != head or artifact['workflow_run']['id'] != 39104271845 or artifact['expired']:
    raise RuntimeError('Artifact does not belong to the verified whole producer')
root = Path(os.environ['RUNNER_TEMP']) / 'font-outline-pair'
root.mkdir(exist_ok=False)
feed = root / 'feed'; feed.mkdir()
archive = root / 'producer.zip'
request = urllib.request.Request(artifact['archive_download_url'], headers={
    'Authorization': 'Bearer ' + os.environ['GH_TOKEN'], 'Accept': 'application/vnd.github+json'})
with urllib.request.urlopen(request, timeout=60) as response, archive.open('xb') as output:
    shutil.copyfileobj(response, output)
def sha256(path):
    with path.open('rb') as data:
        return hashlib.file_digest(data, 'sha256').hexdigest()

if archive.stat().st_size != artifact['size_in_bytes'] or 'sha256:' + sha256(archive) != artifact['digest']:
    raise RuntimeError('Whole package archive identity changed')
packages = []
seen = set()
with zipfile.ZipFile(archive) as bundle:
    for entry in bundle.infolist():
        if entry.is_dir(): continue
        name = Path(entry.filename).name
        if name != entry.filename or name.lower() in seen or not name.endswith(('.nupkg', '.snupkg')):
            raise RuntimeError('Invalid original package archive inventory')
        seen.add(name.lower())
        if name.endswith('.snupkg'): continue
        target = feed / name
        with target.open('xb') as output, bundle.open(entry) as data: shutil.copyfileobj(data, output)
        with zipfile.ZipFile(target) as package:
            specs = [n for n in package.namelist() if n.endswith('.nuspec')]
            if len(specs) != 1: raise RuntimeError('Invalid package manifest inventory')
            manifest = ET.fromstring(package.read(specs[0]))
            values = {e.tag.rsplit('}', 1)[-1]: e.text for e in manifest.iter() if e.tag.rsplit('}', 1)[-1] in ('id', 'version')}
        packages.append({'file': name, **values, 'sha256': sha256(target)})
native = [p for p in packages if p['id'] == 'ProGPU.Backend.Native']
if len(native) != 1: raise RuntimeError('Missing exact native producer package')
version = native[0]['version']
for package in packages:
    if package['id'].startswith('ProGPU.') and package['version'] != version:
        raise RuntimeError('Mixed producer package versions')
for folder in ('reference', 'native'):
    shutil.copytree(source / folder, root / (folder + '-source'))
(root / 'producer.json').write_text(json.dumps({'head': head, 'build': 39104271845, 'artifact': artifact,
    'packages': packages, 'version': version, 'limitation': 'Whole successful Build; independent Android companion failed. Diagnostic only, no pin/release admission.'}, indent=2))
(root / 'global.json').write_text('{"sdk":{"version":"10.0.201","rollForward":"disable"}}')
import xml.sax.saxutils
path = xml.sax.saxutils.escape(str(feed), {'"': '&quot;'})
(root / 'NuGet.config').write_text('<configuration><packageSources><clear/><add key="producer" value="'+path+'"/><add key="NuGet" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><packageSource key="producer"><package pattern="ProGPU.*"/></packageSource><packageSource key="NuGet"><package pattern="*"/></packageSource></packageSourceMapping></configuration>')
