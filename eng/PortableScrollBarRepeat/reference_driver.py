"""Prepared hosted-only reference. Execution requires explicit scoped approval."""
import argparse
import ctypes as C
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import time

HOST = dict(GITHUB_ACTIONS="true", RUNNER_ENVIRONMENT="github-hosted", RUNNER_OS="Windows",
            GITHUB_REPOSITORY="wieslawsoltes/LibreWinForms")
NAMES = ("h-decrement", "h-increment", "h-page", "h-leave", "v-decrement", "v-increment", "v-page")


def require(value, message):
    if not value:
        raise RuntimeError(message)


def observe_desktop(desktop):
    context = desktop.context()
    windows = desktop.windows()
    require(len(windows) <= 128, "Read-only startup inventory exceeds its bound")
    hwnd = desktop.foreground()
    return dict(context=context, windows=windows,
                foreground=None if not hwnd else desktop.snapshot(hwnd))


def native_scrollbar_class(user, hwnd, source):
    source_class = source.get('sourceClass')
    require(source.get('hwnd') == hwnd and isinstance(source_class, str) and source_class.casefold() == 'scrollbar',
            "Original source scrollbar class/handle differs")
    registered, original = C.create_unicode_buffer(256), C.create_unicode_buffer(256)
    registered_count = user.GetClassNameW(hwnd, registered, len(registered))
    original_count = user.RealGetWindowClassW(hwnd, original, len(original))
    observed = dict(registered=registered.value, original=original.value)
    require(0 < registered_count < 255 and registered_count == len(registered.value)
            and 0 < original_count < 255 and original_count == len(original.value)
            and registered.value == source.get('registeredClass')
            and original.value == source.get('realClass'),
            f"Target is not the original native scrollbar: {observed}")
    return observed


def reference_arguments(app, evidence, *, diagnose_startup=False, observe_startup_only=False):
    require(not diagnose_startup or observe_startup_only, "Startup phases require read-only diagnosis")
    return [str(app), str(evidence / 'app')] + (['--startup-phases'] if diagnose_startup else [])


class HeldInput:
    def __init__(self, backend, deadline, *, authorized=False, environment=None, clock=time.monotonic, sleep=time.sleep, input_started=None):
        environment = os.environ if environment is None else environment
        require(authorized and all(environment.get(k) == v for k, v in HOST.items()), "Explicit hosted reference approval required")
        self.backend, self.clock, self.sleep = backend, clock, sleep
        self.deadline = deadline
        require(input_started is None or 0 <= input_started <= clock(), 'Invalid original input start')
        self.input_deadline = None if input_started is None else min(deadline, input_started + 15)
        self.held, self.actions, self.presses = False, 0, 0

    def check(self, target, point):
        require(self.clock() < self.deadline and (self.input_deadline is None or self.clock() < self.input_deadline), "Original input deadline expired")
        self.backend.check(target, point, self.held)

    def claim(self):
        require(self.actions < 31, "Input action budget exhausted; last slot is cleanup only")
        if self.input_deadline is None:
            self.input_deadline = min(self.deadline, self.clock() + 15)
        self.actions += 1

    def move(self, target, point):
        self.check(target, point); self.claim()
        self.backend.move(point)
        self.check(target, point)
        require(self.backend.position() == point, "Native pointer did not reach the verified target")

    def down(self, target, point):
        require(not self.held and self.presses < 7, "Repeated or excessive owned press")
        self.check(target, point)
        require(self.backend.position() == point, "Pointer moved before the native press")
        self.claim()
        require(self.backend.button(True), "Native down was not accepted")
        self.held = True; self.presses += 1

    def observe(self, target, point, seconds):
        require(0 < seconds <= 1.2, "Hold segment exceeds its prepared bound")
        end = self.clock() + seconds
        require(end < self.deadline and self.input_deadline is not None and end < self.input_deadline, "Hold would exceed the original deadline")
        while self.clock() < end:
            self.sleep(min(.025, end - self.clock()))
            self.check(target, point)
            require(self.backend.position() == point, "Unexpected pointer movement during held observation")

    def up(self, target, point):
        require(self.held, "No owned press to release")
        self.check(target, point); self.claim()
        require(self.backend.button(False), "Native release was not accepted")
        self.held = False

    def cleanup(self):
        if self.held:
            require(self.actions < 32, "Cleanup action budget exhausted")
            self.actions += 1
            require(self.backend.button(False), "Owned cleanup release was not accepted")
            self.held = False


class Native:
    def __init__(self, helper, child, ready, record, *, desktop=None):
        self.h, self.child, self.ready, self.record = helper, child, ready, record
        self.desktop = helper.WindowsDesktop() if desktop is None else desktop
        self.u = self.desktop.user
        self.u.GetAncestor.argtypes, self.u.GetAncestor.restype = [C.c_void_p, C.c_uint32], C.c_void_p
        self.u.GetClassNameW.argtypes, self.u.GetClassNameW.restype = [C.c_void_p, C.c_wchar_p, C.c_int], C.c_int
        self.u.RealGetWindowClassW.argtypes, self.u.RealGetWindowClassW.restype = [C.c_void_p, C.c_wchar_p, C.c_uint32], C.c_uint32
        self.dwm = C.WinDLL("dwmapi")
        self.dwm.DwmGetWindowAttribute.argtypes = [C.c_void_p, C.c_uint32, C.c_void_p, C.c_uint32]
        self.dwm.DwmGetWindowAttribute.restype = C.c_int32
        self.owner = self.owner_now()
        require(self.owner["client"] == ready["client"] and self.owner["enabled"], "Source/native owner geometry differs")
        self.controls = {item["name"]: item for item in ready["controls"]}
        self.control_classes = {}
        require(tuple(self.controls) == NAMES and len(ready["controls"]) == 7, "Original control inventory changed")

    def owner_now(self):
        windows = self.desktop.windows(self.child.pid)
        require(len(windows) == 1 and windows[0]["hwnd"] == self.ready["hwnd"] and windows[0]["title"] == self.ready["title"], "Owned native window identity changed")
        return windows[0]

    def rectangle(self, hwnd):
        rect, origin = self.h.Rect(), self.h.Point()
        require(self.u.GetClientRect(hwnd, C.byref(rect)) and self.u.ClientToScreen(hwnd, C.byref(origin)), "Native child bounds unavailable")
        return dict(x=origin.x, y=origin.y, width=rect.right, height=rect.bottom)

    def check(self, target, point, held):
        require(self.child.poll() is None and self.owner_now() == self.owner, "Child ended or owner changed")
        self.desktop.foreground(self.child.pid)
        cloak = C.c_uint32()
        require(self.dwm.DwmGetWindowAttribute(self.owner["hwnd"], 14, C.byref(cloak), 4) == 0 and cloak.value == 0, "Owner cloak observation failed")
        require(not any(self.u.GetAsyncKeyState(k) & 0x8000 for k in ((2, 4, 5, 6, 0x10, 0x11, 0x12, 0x5B, 0x5C) if held else (1, 2, 4, 5, 6, 0x10, 0x11, 0x12, 0x5B, 0x5C))), "Foreign held input detected")
        hwnd = target["hwnd"]
        require(self.desktop.pid(hwnd) == self.child.pid and self.u.IsWindowVisible(hwnd) and self.u.IsWindowEnabled(hwnd), "Target native identity changed")
        require(self.rectangle(hwnd) == target["client"], "Target geometry changed")
        require(self.h.contains(target["client"], *point) and self.h.contains(self.owner["client"], *point), "Point outside original owned geometry")
        require(self.u.WindowFromPoint(self.h.Point(*point)) == hwnd, "Point is obstructed or targets another window")
        require(self.u.GetAncestor(hwnd, 2) == self.owner["hwnd"], "Target no longer belongs to the owned root")
        if hwnd != self.owner["hwnd"]:
            observed = native_scrollbar_class(self.u, hwnd, target)
            if hwnd not in self.control_classes:
                self.control_classes[hwnd] = observed
                self.record.append(dict(kind="native-class", hwnd=hwnd, **observed))
            require(self.control_classes[hwnd] == observed, "Original native scrollbar class changed")

    def position(self):
        p = self.h.Point(); require(self.u.GetCursorPos(C.byref(p)), "Pointer position unavailable")
        return p.x, p.y

    def move(self, point):
        require(self.u.SetCursorPos(*point), "Native pointer movement rejected")
        self.record.append(dict(kind="move", point=point, seconds=time.monotonic()))

    def button(self, down):
        item = self.h.Input(0, self.h.InputUnion(mouse=self.h.MouseInput(0, 0, 0, 2 if down else 4, 0, 0)))
        sent = self.u.SendInput(1, C.byref(item), C.sizeof(item))
        self.record.append(dict(kind="down" if down else "up", accepted=int(sent), seconds=time.monotonic()))
        return sent == 1

    def target(self, name):
        class ScrollInfo(C.Structure):
            _fields_ = [("size", C.c_uint32), ("rect", self.h.Rect), ("arrow", C.c_int32),
                        ("thumb_start", C.c_int32), ("thumb_end", C.c_int32), ("reserved", C.c_int32), ("states", C.c_uint32 * 6)]
        self.u.GetScrollBarInfo.argtypes = [C.c_void_p, C.c_int32, C.POINTER(ScrollInfo)]
        self.u.GetScrollBarInfo.restype = C.c_int32
        target = self.controls[name]; info = ScrollInfo(); info.size = C.sizeof(info)
        require(self.u.GetScrollBarInfo(target["hwnd"], -4, C.byref(info)), "Original native scrollbar geometry unavailable")
        rect = self.h.box(info.rect)
        require(rect == target["client"] and info.arrow > 0 and not any(s & 0x8001 for s in info.states), "Original scrollbar unavailable or geometry differs")
        vertical = name[0] == "v"; length = rect["height"] if vertical else rect["width"]
        require(info.arrow < info.thumb_start < info.thumb_end < length - info.arrow, "Original middle thumb geometry changed")
        offset = info.arrow // 2 if name.endswith("decrement") else info.thumb_end + 3 if name.endswith("page") else length - 1 - info.arrow // 2
        require(0 < offset < length, "Native target outside scrollbar")
        point = (rect["x"] + rect["width"] // 2, rect["y"] + offset) if vertical else (rect["x"] + offset, rect["y"] + rect["height"] // 2)
        self.record.append(dict(kind="geometry", name=name, rect=rect, arrow=info.arrow, thumb=[info.thumb_start, info.thumb_end], states=list(info.states), point=point))
        return target, point


def run(args):
    require(args.authorized and all(os.environ.get(k) == v for k, v in HOST.items()), "Explicit hosted reference approval required")
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
            require(ready['pid'] == child.pid and ready['title'] == f'ScrollBarRepeatReference [{child.pid}]', "Fresh child identity differs")
            native = Native(helper, child, ready, receipt['actions'])
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
            receipt['payloadAfter'] = {p: helper.digest(Path(p)) for p in identity}
            require(receipt['payloadAfter'] == identity, 'Reference payload changed')
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
