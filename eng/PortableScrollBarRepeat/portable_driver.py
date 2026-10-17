"""Prepared portable seven-hold counterpart; execution requires separate approval."""
import argparse
import ctypes as C
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import time
from reference_driver import HOST, NAMES, HeldInput, require, observe_desktop, reference_arguments, Native as OriginalNative
from geometry import inventory, painted_target, contains, bind_native_root, bind_portable_root


def native_root_inventory(desktop, pid):
    windows = desktop.windows(pid)
    require(len(windows) <= 128, 'Native inventory exceeds observation bound')
    user = desktop.user
    user.GetClassNameW.argtypes, user.GetClassNameW.restype = [C.c_void_p, C.c_wchar_p, C.c_int], C.c_int
    user.RealGetWindowClassW.argtypes, user.RealGetWindowClassW.restype = [C.c_void_p, C.c_wchar_p, C.c_uint32], C.c_uint32
    user.GetWindowLongPtrW.argtypes, user.GetWindowLongPtrW.restype = [C.c_void_p, C.c_int], C.c_ssize_t
    user.GetWindow.argtypes, user.GetWindow.restype = [C.c_void_p, C.c_uint32], C.c_void_p
    metadata = []
    for window in windows:
        registered = C.create_unicode_buffer(256); original = C.create_unicode_buffer(256)
        registered_count = int(user.GetClassNameW(window['hwnd'], registered, 256))
        original_count = int(user.RealGetWindowClassW(window['hwnd'], original, 256))
        metadata.append(dict(hwnd=window['hwnd'], pid=desktop.pid(window['hwnd']),
                             registeredClass=registered.value, registeredCount=registered_count,
                             realClass=original.value, realCount=original_count,
                             style=int(user.GetWindowLongPtrW(window['hwnd'], -16)) & 0xffffffff,
                             extendedStyle=int(user.GetWindowLongPtrW(window['hwnd'], -20)) & 0xffffffff,
                             owner=int(user.GetWindow(window['hwnd'], 4) or 0)))
    return dict(windows=windows, metadata=metadata)


class Native(OriginalNative):
    def __init__(self, helper, child, ready, record, snapshot_path):
        self.snapshot_path = snapshot_path
        self.source_ready = ready
        desktop = helper.WindowsDesktop()
        self.root_inventory = native_root_inventory(desktop, child.pid)
        record.append(dict(kind='native-root-inventory', inventory=self.root_inventory))
        observed = bind_portable_root(self.root_inventory, ready, child.pid)
        record.append(dict(kind='independent-native-root', sourceHandle=ready['sourceHandle'], native=observed))
        super().__init__(helper, child, dict(ready, hwnd=observed['hwnd']), record)
        self.kernel = C.WinDLL('kernel32')
        self.kernel.GetTickCount64.argtypes = []
        self.kernel.GetTickCount64.restype = C.c_uint64
        self.current_controls()

    def owner_now(self):
        current = native_root_inventory(self.desktop, self.child.pid)
        require(current == self.root_inventory, 'Complete owned native inventory changed')
        return bind_portable_root(current, self.source_ready, self.child.pid)

    def current_controls(self):
        snapshots = list(self.snapshot_path.glob('snapshot-??????.json'))
        require(0 < len(snapshots) <= 2200, 'Bounded source snapshots unavailable')
        data = max(snapshots, key=lambda p: p.name).read_bytes()
        require(len(data) <= 65536, 'Source snapshot exceeds its bound')
        return inventory(json.loads(data), self.source_ready, int(self.kernel.GetTickCount64()))

    def check(self, target, point, held):
        # Reuse all original foreground, cloak, foreign-input, process, real
        # WindowFromPoint and root geometry guards. Portable widgets are not HWNDs.
        root = dict(hwnd=self.owner['hwnd'], client=self.owner['client'])
        super().check(root, point, held)
        controls = self.current_controls()
        if 'name' not in target:
            require(target == root and held, 'Unowned blank-client movement')
            require(not any(contains(c['client'], point) for c in controls.values()), 'Leave point intersects another source control')
            return
        current = controls.get(target['name'])
        require(current is not None and current['sourceHandle'] == target['sourceHandle']
                and current['client'] == target['client'] and contains(current['client'], point),
                'Original owned source target changed')

    def target(self, name):
        current = self.current_controls()[name]
        point = painted_target(current)
        target = dict(hwnd=self.owner['hwnd'], name=name, sourceHandle=current['sourceHandle'], client=current['client'])
        self.record.append(dict(kind='source-painted-geometry', name=name, sourceHandle=current['sourceHandle'],
                                client=current['client'], paint=current['paint'], point=point))
        return target, point


def verify_payload(helper, identity, receipt):
    receipt['payloadAfter'] = {p: helper.digest(Path(p)) for p in identity}
    require(receipt['payloadAfter'] == identity, 'Reference payload changed')


def run(args):
    require((args.authorized or args.observe_startup_only) and all(os.environ.get(k) == v for k, v in HOST.items()), "Explicit hosted portable input approval required")
    app, helper_path, evidence = args.app.resolve(strict=True), args.helper.resolve(strict=True), args.evidence.resolve()
    require(not evidence.exists(), "Evidence must be new"); evidence.mkdir(parents=True)
    spec = importlib.util.spec_from_file_location("repeat_native_helper", helper_path); helper = importlib.util.module_from_spec(spec); spec.loader.exec_module(helper)
    identity = {str(p): helper.digest(p) for p in app.parent.glob('*') if p.is_file()}
    command = reference_arguments(app, evidence, diagnose_startup=args.diagnose_startup,
                                  observe_startup_only=args.observe_startup_only)
    receipt = dict(qualified=False, helperSha256=helper.digest(helper_path), payloadBefore=identity, actions=[], completed=False)
    started = time.monotonic(); deadline = started + 60; lease = None; child = None
    try:
        with (evidence / "stdout.log").open('xb') as out, (evidence / "stderr.log").open('xb') as err:
            child = subprocess.Popen(command, stdin=subprocess.DEVNULL, stdout=out, stderr=err, cwd=app.parent)
            while not (evidence / 'app/ready.json').exists():
                require(time.monotonic() < started + 15 and child.poll() is None, "Original startup bound expired or app ended")
                time.sleep(.025)
            ready = json.loads((evidence / 'app/ready.json').read_text()); receipt['ready'] = ready
            require(ready['pid'] == child.pid and ready['title'] == f'PortableScrollBarRepeat [{child.pid}]', "Fresh child identity differs")
            native = Native(helper, child, ready, receipt['actions'], evidence / 'app')
            observer_path = helper_path.parent / 'OriginalGrid' / 'prepare-desktop.py'
            spec = importlib.util.spec_from_file_location('repeat_startup_observer', observer_path)
            observer = importlib.util.module_from_spec(spec); spec.loader.exec_module(observer)
            desktop_observer = observer.Desktop()
            receipt['startupObserverSha256'] = helper.digest(observer_path)
            receipt['startupBeforeActivation'] = observe_desktop(desktop_observer)
            if args.observe_startup_only:
                # Read-only diagnosis never reaches activation or HeldInput.
                receipt['diagnosticOnly'] = True
                (evidence / 'app/complete').touch(exist_ok=False)
                child.wait(timeout=max(0, deadline - time.monotonic()))
                require(child.returncode == 0, 'Observed reference did not close normally')
                verify_payload(helper, identity, receipt)
                receipt['observationCompleted'] = True
                return
            input_started = None
            if args.hosted_startup:
                import startup
                def load_startup(name):
                    path = helper_path.parent / 'OriginalGrid' / (name + '.py')
                    spec = importlib.util.spec_from_file_location('repeat_' + name, path)
                    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
                    receipt.setdefault('startupSourceHashes', {})[name] = helper.digest(path)
                    return module
                shell = load_startup('shell-escape'); caption = load_startup('caption')
                receipt['hostedStartup'] = {}
                input_started = startup.activate(native, desktop_observer, shell.dismiss,
                    caption.Desktop, deadline, receipt['hostedStartup'], authorized=args.authorized)
                receipt['activationRequested'] = receipt['hostedStartup']['activationRequested']
            else:
                receipt['activationRequested'] = True
                native.desktop.activate(native.owner)
                receipt['startupAfterActivation'] = observe_desktop(desktop_observer)
                native.desktop.foreground(child.pid)
            receipt['beforeCapture'] = native.desktop.screenshot(child.pid, [native.owner], evidence / 'before.bmp')
            lease = HeldInput(native, deadline, authorized=True, input_started=input_started)
            for name in NAMES:
                target, point = native.target(name)
                lease.move(target, point); lease.down(target, point)
                if name == 'h-leave':
                    lease.observe(target, point, .6)
                    outside = (target['client']['x'] + target['client']['width'] + 12, point[1])
                    owner_target = dict(hwnd=native.owner['hwnd'], client=native.owner['client'])
                    lease.move(owner_target, outside); lease.observe(owner_target, outside, .3)
                    lease.move(target, point); lease.observe(target, point, .6)
                else:
                    lease.observe(target, point, 1.2)
                lease.up(target, point)
                time.sleep(.15)
            receipt['afterCapture'] = native.desktop.screenshot(child.pid, [native.owner], evidence / 'after.bmp')
            (evidence / 'app/complete').touch(exist_ok=False)
            child.wait(timeout=max(0, deadline - time.monotonic()))
            require(child.returncode == 0, 'Reference did not close normally')
            verify_payload(helper, identity, receipt)
            receipt['completed'] = True
    except BaseException as error:
        receipt['error'] = type(error).__name__ + ': ' + str(error)
        raise
    finally:
        receipt['cleanupErrors'] = []
        try:
            if lease is not None: lease.cleanup()
        except BaseException as error:
            receipt['cleanupErrors'].append('release: ' + str(error))
        finally:
            try:
                if child is not None and child.poll() is None:
                    child.kill(); child.wait(timeout=5)
            except BaseException as error:
                receipt['cleanupErrors'].append('owned-child: ' + str(error))
            receipt['elapsedSeconds'] = time.monotonic() - started
            receipt['childExit'] = None if child is None else child.returncode
            receipt['pointerActionsCount'] = 0 if lease is None else lease.actions
            setup = receipt.get('hostedStartup', {})
            receipt['activationRequested'] = receipt.get('activationRequested', False) or setup.get('activationRequested', False)
            receipt['startupNativeAttemptMaximum'] = 3 * setup.get('shellActions', 0) + (4 if 'moveInserted' in setup.get('caption', {}) else 0)
            receipt['actionsCount'] = receipt['pointerActionsCount'] + int(receipt['activationRequested']) + receipt['startupNativeAttemptMaximum']
            receipt['presses'] = 0 if lease is None else lease.presses
            (evidence / 'driver.json').write_text(json.dumps(receipt, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--app', type=Path, required=True)
    parser.add_argument('--helper', type=Path, required=True); parser.add_argument('--evidence', type=Path, required=True)
    parser.add_argument('--observe-startup-only', action='store_true')
    parser.add_argument('--diagnose-startup', action='store_true')
    parser.add_argument('--hosted-startup', action='store_true')
    parser.add_argument('--authorized', action='store_true'); run(parser.parse_args())
