"""Build and execute only Microsoft reference on a disposable Windows runner."""
import hashlib,json,os,pathlib,shutil,subprocess,sys,tempfile
from owned_process import run
from verify import validate
ROOT=pathlib.Path(__file__).resolve().parent
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    assert sys.platform=='win32' and os.environ.get('RUNNER_ENVIRONMENT')=='github-hosted'
    assert os.environ.get('GITHUB_REPOSITORY')=='wieslawsoltes/LibreWinForms' and os.environ.get('GITHUB_RUN_ATTEMPT')=='1'
    assert os.environ.get('SEGOE_RUNNER_LABEL') in ('windows-2022','windows-2025')
    assert os.environ.get('RUNNER_ARCH')=='X64'
    manifest=json.loads((ROOT/'source-manifest.json').read_text())
    for name,digest in manifest['files'].items():assert sha(ROOT/name)==digest
    evidence=pathlib.Path(os.environ['SEGOE_METADATA_EVIDENCE']).resolve();assert not evidence.exists();evidence.mkdir(parents=True)
    workspace=pathlib.Path(tempfile.mkdtemp(prefix='original-segoe-metadata-',dir=os.environ['RUNNER_TEMP']))
    observation=workspace/'observation';result={'qualified':False,'applicationExecuted':False,'completed':False,'cleanupErrors':[]}
    shutil.copy2(ROOT/'source-manifest.json',evidence/'source-manifest.json')
    try:
        provenance={key:os.environ.get(key) for key in ('GITHUB_REPOSITORY','GITHUB_RUN_ID','GITHUB_RUN_ATTEMPT','GITHUB_SHA','SEGOE_EXPECTED_HEAD','SEGOE_RUNNER_LABEL','RUNNER_ARCH','RUNNER_OS','ImageOS','ImageVersion','ImageVersionFull')}
        provenance['checkoutHead']=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True,timeout=30).strip()
        (evidence/'host-provenance.json').write_text(json.dumps(provenance,indent=2)+'\n')
        assert provenance['checkoutHead']==provenance['SEGOE_EXPECTED_HEAD']
        shutil.copytree(ROOT/'reference',workspace/'reference',ignore=shutil.ignore_patterns('bin','obj'))
        shutil.copy2(ROOT/'global.json',workspace/'global.json')
        (workspace/'NuGet.config').write_text('<configuration><packageSources><clear/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>')
        with (evidence/'build.log').open('w') as log:
            build=subprocess.run(['dotnet','build','reference/SegoeMetadata.csproj','-c','Release','-r','win-x64','-p:ImportDirectoryBuildProps=false','-p:ImportDirectoryBuildTargets=false','--nologo'],cwd=workspace,stdout=log,stderr=subprocess.STDOUT,timeout=600)
        assert build.returncode==0
        app=workspace/'reference/bin/Release/net10.0-windows/win-x64/SegoeMetadata.exe'
        with app.open('rb') as binary:
            header=binary.read(64);assert header[:2]==b'MZ'
            binary.seek(int.from_bytes(header[60:64],'little'));pe=binary.read(6)
            assert pe[:4]==b'PE\0\0' and int.from_bytes(pe[4:6],'little')==0x8664
        output={str(p.relative_to(app.parent)):sha(p) for p in app.parent.rglob('*') if p.is_file()}
        (evidence/'build-output.json').write_text(json.dumps(output,indent=2)+'\n')
        (evidence/'dotnet-info.txt').write_text(subprocess.check_output(['dotnet','--info'],cwd=workspace,text=True,timeout=30))
        process=run([str(app),str(observation)],evidence/'application.log',evidence/'process.json',30)
        result['process']=process
        assert not process['timedOut'] and process['exitUnsigned']==0
        result['inventory']=validate(observation)
        assert output=={str(p.relative_to(app.parent)):sha(p) for p in app.parent.rglob('*') if p.is_file()}
        result['completed']=True
    except BaseException as error:
        result['error']=repr(error);raise
    finally:
        try:
            process_path=evidence/'process.json'
            if process_path.exists():result['applicationExecuted']='pid' in json.loads(process_path.read_text())
            if observation.exists():shutil.copytree(observation,evidence/'observation')
        except BaseException as error:result['cleanupErrors'].append(repr(error));result['completed']=False
        finally:
            # Keep the owned workspace when evidence could not be copied.
            if not result['cleanupErrors']:
                try:shutil.rmtree(workspace)
                except BaseException as error:result['cleanupErrors'].append(repr(error));result['completed']=False
            result['retainedWorkspace']=str(workspace) if workspace.exists() else None
            (evidence/'result.json').write_text(json.dumps(result,indent=2)+'\n')
    assert result['completed'] and not result['cleanupErrors']
if __name__=='__main__':main()
