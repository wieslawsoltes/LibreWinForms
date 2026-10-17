"""Diagnostic only: one bounded managed stack beside the unchanged original pair."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import runpy
import subprocess
import sys
import threading
import time


def collect(child, tool, output, stop, launch, record):
    # The original controller still owns the child and its 20/60-second limits.
    # Observe only this exact Popen object; never rediscover a process by name.
    if stop.wait(12) or child.poll() is not None:
        record['skipped'] = 'owned child already finished'
        return
    record['pid'] = child.pid
    process = None
    started = time.monotonic()
    try:
        with (output / 'managed-stacks.txt').open('xb') as stdout, (output / 'stack-errors.txt').open('xb') as stderr:
            process = launch([str(tool), 'report', '--process-id', str(child.pid)], stdout=stdout, stderr=stderr)
            while process.poll() is None:
                if (stop.is_set() or child.poll() is not None or time.monotonic() - started >= 5
                        or sum((output / n).stat().st_size for n in ('managed-stacks.txt', 'stack-errors.txt')) > 2 * 1024 * 1024):
                    record['boundedStop'] = True
                    process.kill()
                    break
                stop.wait(.025)
            record['exitCode'] = process.wait(timeout=1)
    except BaseException as error:
        record['error'] = repr(error)
    finally:
        if process is not None and process.poll() is None:
            process.kill()
            process.wait(timeout=1)
        record['seconds'] = time.monotonic() - started


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--prepared-root', type=Path, required=True)
    parser.add_argument('--evidence', type=Path, required=True)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument('--stack-tool', type=Path)
    group.add_argument('--native-tool', type=Path)
    args = parser.parse_args()
    if os.name != 'nt' or any(os.environ.get(k) != v for k, v in {
        'GITHUB_ACTIONS':'true', 'RUNNER_ENVIRONMENT':'github-hosted', 'RUNNER_OS':'Windows',
        'GITHUB_REPOSITORY':'wieslawsoltes/LibreWinForms'}.items()):
        raise RuntimeError('Requires this repository\'s disposable hosted Windows runner')
    root, tool = args.prepared_root.resolve(strict=True), (args.native_tool or args.stack_tool).resolve(strict=True)
    build = json.loads((root / 'build-receipt.json').read_text())
    app = str(Path(build['builds']['Portable']['app']).resolve(strict=True))
    if not Path(app).is_relative_to(root) or tool.is_symlink():
        raise RuntimeError('Exact prepared child and regular stack tool required')
    output = args.evidence.with_name(args.evidence.name + '-diagnostic')
    output.mkdir(exist_ok=False)
    driver = Path(__file__).resolve().parents[1] / 'librewinforms-original-grid-desktop.py'
    record = dict(diagnosticOnly=True, qualified=False, originalStartupSeconds=20, originalProcessSeconds=60,
                  originalDriverSha256=hashlib.sha256(driver.read_bytes()).hexdigest(),
                  toolSha256=hashlib.sha256(tool.read_bytes()).hexdigest(), toolVersion='owned-MiniDumpNormal-v1' if args.native_tool else '10.0.750501',
                  sourceHead=build['sourceHead'], observation={})
    collector = collect
    if args.native_tool:
        from native_stack import collect as collector
    original = subprocess.Popen
    stop, threads = threading.Event(), []

    def launch(*arguments, **kwargs):
        command = arguments[0] if arguments else kwargs.get('args')
        matched = isinstance(command, (list, tuple)) and list(command) == [app]
        if matched and threads:
            raise RuntimeError('No second portable launch is admitted')
        child = original(*arguments, **kwargs)
        if matched:
            thread = threading.Thread(target=collector,
                args=(child, tool, output, stop, original, record['observation']), daemon=True)
            threads.append(thread)
            thread.start()
        return child

    previous = sys.argv
    try:
        subprocess.Popen = launch
        sys.argv = [str(driver), '--prepared-root', str(root), '--evidence', str(args.evidence), '--hosted-startup']
        runpy.run_path(str(driver), run_name='__main__')
        record['originalControllerCompleted'] = True
    except BaseException as error:
        record['originalControllerError'] = repr(error)
        raise
    finally:
        subprocess.Popen, sys.argv = original, previous
        stop.set()
        for thread in threads:
            thread.join(2)
        record['observerStopped'] = all(not thread.is_alive() for thread in threads)
        (output / 'receipt.json').write_text(json.dumps(record, indent=2) + '\n')


if __name__ == '__main__':
    main()
