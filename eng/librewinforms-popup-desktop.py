#!/usr/bin/env python3
"""Windows-only, real-input paired PopupInteractionApp evidence; never pixel parity."""

import argparse
import ctypes as C
import hashlib
import json
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import time
import uuid


class Point(C.Structure):
    _fields_ = [("x", C.c_int32), ("y", C.c_int32)]


class Rect(C.Structure):
    _fields_ = [("left", C.c_int32), ("top", C.c_int32), ("right", C.c_int32), ("bottom", C.c_int32)]


class MouseInput(C.Structure):
    _fields_ = [("dx", C.c_int32), ("dy", C.c_int32), ("data", C.c_uint32),
                ("flags", C.c_uint32), ("time", C.c_uint32), ("extra", C.c_size_t)]


class KeyInput(C.Structure):
    _fields_ = [("key", C.c_uint16), ("scan", C.c_uint16), ("flags", C.c_uint32),
                ("time", C.c_uint32), ("extra", C.c_size_t)]


class InputUnion(C.Union):
    _fields_ = [("mouse", MouseInput), ("key", KeyInput)]


class Input(C.Structure):
    _fields_ = [("kind", C.c_uint32), ("value", InputUnion)]


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def box(rect):
    return dict(x=rect.left, y=rect.top, width=rect.right - rect.left, height=rect.bottom - rect.top)


def contains(rect, x, y):
    return rect["x"] <= x < rect["x"] + rect["width"] and rect["y"] <= y < rect["y"] + rect["height"]


def stable_state(state):
    # Sequence/time and incidental paint frequency are not geometry/state.
    result = {key: value for key, value in state.items() if key not in ("sequence", "elapsedMs", "counts")}
    result["counts"] = {key: value for key, value in state["counts"].items() if not key.endswith("-paint")}
    return result


def read_snapshot(directory):
    paths = list((directory / "app").glob("snapshot-????????.json"))
    require(len(paths) <= 650, "Observer snapshot budget exceeded")
    if not paths:
        raise FileNotFoundError("No observer snapshot published")
    return json.loads(max(paths).read_text())


class WindowsDesktop:
    def __init__(self):
        require(sys.platform == "win32", "This driver admits Windows reference/portable pairs only")
        self.user = C.WinDLL("user32", use_last_error=True)
        self.gdi = C.WinDLL("gdi32", use_last_error=True)
        self.callback_type = C.WINFUNCTYPE(C.c_int32, C.c_void_p, C.c_ssize_t)
        definitions = [
            (self.user, "SetProcessDpiAwarenessContext", C.c_int32, [C.c_void_p]),
            (self.user, "EnumWindows", C.c_int32, [self.callback_type, C.c_ssize_t]),
            (self.user, "GetWindowThreadProcessId", C.c_uint32, [C.c_void_p, C.POINTER(C.c_uint32)]),
            (self.user, "IsWindowVisible", C.c_int32, [C.c_void_p]),
            (self.user, "GetWindowTextW", C.c_int32, [C.c_void_p, C.c_wchar_p, C.c_int32]),
            (self.user, "GetWindowRect", C.c_int32, [C.c_void_p, C.POINTER(Rect)]),
            (self.user, "GetClientRect", C.c_int32, [C.c_void_p, C.POINTER(Rect)]),
            (self.user, "ClientToScreen", C.c_int32, [C.c_void_p, C.POINTER(Point)]),
            (self.user, "GetForegroundWindow", C.c_void_p, []),
            (self.user, "SetForegroundWindow", C.c_int32, [C.c_void_p]),
            (self.user, "WindowFromPoint", C.c_void_p, [Point]),
            (self.user, "GetAsyncKeyState", C.c_int16, [C.c_int32]),
            (self.user, "SetCursorPos", C.c_int32, [C.c_int32, C.c_int32]),
            (self.user, "SendInput", C.c_uint32, [C.c_uint32, C.POINTER(Input), C.c_int32]),
            (self.user, "GetDC", C.c_void_p, [C.c_void_p]),
            (self.user, "ReleaseDC", C.c_int32, [C.c_void_p, C.c_void_p]),
            (self.gdi, "CreateCompatibleDC", C.c_void_p, [C.c_void_p]),
            (self.gdi, "CreateCompatibleBitmap", C.c_void_p, [C.c_void_p, C.c_int32, C.c_int32]),
            (self.gdi, "SelectObject", C.c_void_p, [C.c_void_p, C.c_void_p]),
            (self.gdi, "BitBlt", C.c_int32, [C.c_void_p, C.c_int32, C.c_int32, C.c_int32, C.c_int32,
                                           C.c_void_p, C.c_int32, C.c_int32, C.c_uint32]),
            (self.gdi, "GetDIBits", C.c_int32, [C.c_void_p, C.c_void_p, C.c_uint32, C.c_uint32,
                                              C.c_void_p, C.c_void_p, C.c_uint32]),
            (self.gdi, "DeleteObject", C.c_int32, [C.c_void_p]),
            (self.gdi, "DeleteDC", C.c_int32, [C.c_void_p]),
        ]
        for library, name, result, arguments in definitions:
            function = getattr(library, name)
            function.restype, function.argtypes = result, arguments
        require(self.user.SetProcessDpiAwarenessContext(C.c_void_p(-4)), "Could not establish physical desktop coordinates")

    def pid(self, window):
        value = C.c_uint32()
        require(window and self.user.GetWindowThreadProcessId(window, C.byref(value)), "Native window identity unavailable")
        return value.value

    def foreground(self, pid):
        require(self.pid(self.user.GetForegroundWindow()) == pid, "Foreground belongs to another process; no input permitted")

    def windows(self, pid):
        result = []

        @self.callback_type
        def visit(window, _):
            owner = C.c_uint32()
            self.user.GetWindowThreadProcessId(window, C.byref(owner))
            if owner.value == pid and self.user.IsWindowVisible(window):
                bounds, client, origin = Rect(), Rect(), Point()
                title = C.create_unicode_buffer(1024)
                self.user.GetWindowTextW(window, title, len(title))
                if (self.user.GetWindowRect(window, C.byref(bounds)) and
                        self.user.GetClientRect(window, C.byref(client)) and
                        self.user.ClientToScreen(window, C.byref(origin))):
                    result.append(dict(hwnd=int(window), title=title.value, bounds=box(bounds),
                                       client=dict(x=origin.x, y=origin.y, width=client.right, height=client.bottom)))
            return 1

        require(self.user.EnumWindows(visit, 0), "Could not enumerate native windows")
        return result

    def key(self, pid, key):
        self.foreground(pid)
        # Never release user-held modifiers or silently work around an interactive desktop.
        require(not any(self.user.GetAsyncKeyState(k) & 0x8000 for k in (0x10, 0x11, 0x12, 0x5B, 0x5C)),
                "A physical modifier is held; refusing keyboard injection")
        inputs = (Input * 2)(Input(1, InputUnion(key=KeyInput(key, 0, 0, 0, 0))),
                             Input(1, InputUnion(key=KeyInput(key, 0, 2, 0, 0))))
        self.send_pair(inputs)

    def send_pair(self, inputs):
        sent = self.user.SendInput(2, inputs, C.sizeof(Input))
        if sent == 1:
            # Retire only our just-injected down event if Windows admitted a
            # partial pair. Never release unrelated/user-held keys or buttons.
            self.user.SendInput(1, C.cast(C.byref(inputs, C.sizeof(Input)), C.POINTER(Input)), C.sizeof(Input))
        require(sent == 2, "SendInput did not deliver the complete input pair")

    def pointer(self, pid, rect, click=None):
        self.foreground(pid)
        require(rect and rect["width"] > 0 and rect["height"] > 0, "Missing observed target geometry")
        x, y = rect["x"] + rect["width"] // 2, rect["y"] + rect["height"] // 2
        require(self.pid(self.user.WindowFromPoint(Point(x, y))) == pid, "Observed point is covered by another process")
        require(self.user.SetCursorPos(x, y), "Could not move the native pointer")
        if click:
            self.foreground(pid)
            require(not any(self.user.GetAsyncKeyState(k) & 0x8000 for k in (1, 2, 4)),
                    "A physical pointer button is held; refusing click injection")
            require(self.pid(self.user.WindowFromPoint(Point(x, y))) == pid, "Pointer owner changed before click")
            down, up = (0x2, 0x4) if click == "left" else (0x8, 0x10)
            inputs = (Input * 2)(Input(0, InputUnion(mouse=MouseInput(0, 0, 0, down, 0, 0))),
                                 Input(0, InputUnion(mouse=MouseInput(0, 0, 0, up, 0, 0))))
            self.send_pair(inputs)

    def screenshot(self, pid, windows, destination):
        self.foreground(pid)
        rectangles = [window["bounds"] for window in windows]
        x, y = min(r["x"] for r in rectangles), min(r["y"] for r in rectangles)
        width = max(r["x"] + r["width"] for r in rectangles) - x
        height = max(r["y"] + r["height"] for r in rectangles) - y
        require(0 < width <= 4096 and 0 < height <= 4096 and width * height <= 4_194_304,
                "Owned screenshot extent exceeds 4096 pixels per edge or 16 MiB pixel budget")
        screen = self.user.GetDC(None)
        memory = bitmap = previous = None
        try:
            require(screen, "Desktop capture DC unavailable")
            memory = self.gdi.CreateCompatibleDC(screen)
            bitmap = self.gdi.CreateCompatibleBitmap(screen, width, height)
            require(memory and bitmap, "Capture allocation failed")
            previous = self.gdi.SelectObject(memory, bitmap)
            require(previous, "Could not select capture bitmap")
            require(self.gdi.BitBlt(memory, 0, 0, width, height, screen, x, y, 0x40CC0020), "Desktop BitBlt failed")
            self.gdi.SelectObject(memory, previous)
            previous = None  # GetDIBits requires the bitmap deselected.
            header = C.create_string_buffer(struct.pack("<IiiHHIIiiII", 40, width, -height, 1, 32, 0, 0, 0, 0, 0, 0))
            pixels = C.create_string_buffer(width * height * 4)
            require(self.gdi.GetDIBits(memory, bitmap, 0, height, pixels, header, 0) == height, "Incomplete native screenshot")
            self.foreground(pid)
            # Raw top-down32-bit BMP preserves actual BGRX bytes, with no image processing.
            with destination.open("xb") as stream:
                stream.write(struct.pack("<2sIHHI", b"BM", 54 + len(pixels), 0, 0, 54))
                stream.write(header.raw[:40])
                stream.write(pixels.raw)
            return dict(x=x, y=y, width=width, height=height, sha256=digest(destination))
        finally:
            if previous:
                self.gdi.SelectObject(memory, previous)
            if bitmap:
                self.gdi.DeleteObject(bitmap)
            if memory:
                self.gdi.DeleteDC(memory)
            if screen:
                self.user.ReleaseDC(None, screen)


class Session:
    def __init__(self, desktop, process, directory, run):
        self.desktop, self.process, self.directory, self.run = desktop, process, directory, run
        self.deadline = time.monotonic() + 60
        self.state = None
        self.phase = "startup"
        self.image_bytes = 0

    def wait(self, predicate):
        previous = None
        while time.monotonic() < self.deadline:
            require(self.process.poll() is None, f"Application exited at {self.phase}: {self.process.returncode}")
            try:
                state = read_snapshot(self.directory)
            except (FileNotFoundError, json.JSONDecodeError):
                time.sleep(0.05)
                continue
            require(state["pid"] == self.process.pid and state["title"] == f"PopupInteractionApp [{self.run}]", "Snapshot identity mismatch")
            if predicate(state) and previous is not None and state["sequence"] > previous["sequence"] and stable_state(state) == stable_state(previous):
                windows = self.desktop.windows(self.process.pid)
                mains = [w for w in windows if w["title"] == state["title"]]
                require(len(mains) == 1 and mains[0]["client"] == state["form"]["client"], "Source/native main client geometry differs")
                for popup in state["popups"].values():
                    if popup["visible"]:
                        require(any(w["client"] == popup["client"] for w in windows), "Visible source popup lacks matching native client geometry")
                self.state = state
                return windows
            previous = state if predicate(state) else None
            time.sleep(0.05)
        raise TimeoutError(f"Original 60-second application deadline expired during {self.phase}")

    def capture(self, phase, predicate):
        self.phase = phase
        windows = self.wait(predicate)
        self.desktop.foreground(self.process.pid)
        snapshot = self.state
        require(self.image_bytes + 16 * 1024 * 1024 + 54 <= 128 * 1024 * 1024,
                "Per-process screenshot evidence would exceed 128 MiB")
        image = self.desktop.screenshot(self.process.pid, windows, self.directory / f"{phase}.bmp")
        self.image_bytes += (self.directory / f"{phase}.bmp").stat().st_size
        require(time.monotonic() < self.deadline, "Screenshot exceeded original application deadline")
        require(self.process.poll() is None, "Application exited during screenshot")
        require(self.desktop.windows(self.process.pid) == windows, "Native window state changed during screenshot")
        require(stable_state(read_snapshot(self.directory)) == stable_state(snapshot), "Observed source state changed during screenshot")
        with (self.directory / f"{phase}.json").open("x") as stream:
            json.dump(dict(snapshot=snapshot, nativeWindows=windows, screenshot=image, qualified=False), stream, indent=2)

    def point(self, rect, click=None):
        self.input_ready()
        self.desktop.pointer(self.process.pid, rect, click)
        self.record_input(dict(kind="pointer", rectangle=rect, button=click))

    def key(self, key):
        self.input_ready()
        self.desktop.key(self.process.pid, key)
        self.record_input(dict(kind="key-pair", virtualKey=key))

    def record_input(self, action):
        action.update(phase=self.phase, monotonicSeconds=time.monotonic(), pid=self.process.pid)
        with (self.directory / "driver-input.jsonl").open("a") as stream:
            stream.write(json.dumps(action) + "\n")

    def input_ready(self):
        require(time.monotonic() < self.deadline, "No input permitted after the original application deadline")
        require(self.process.poll() is None, "No input permitted after application exit")


def scenario(session):
    closed = lambda s: not any(p["visible"] for p in s["popups"].values())
    opened = lambda name: lambda s: s["popups"][name]["visible"] and s["counts"].get(name + "-paint", 0) > 0
    session.phase = "startup"
    windows = session.wait(lambda s: s["form"]["visible"] and s["counts"].get("form-paint", 0) > 0)
    main = next(w for w in windows if w["title"] == session.state["title"])
    # Activate only this freshly launched, PID-verified application.
    session.desktop.user.SetForegroundWindow(main["hwnd"])
    session.capture("01-baseline", lambda s: s["form"]["active"])
    session.point(session.state["contextTarget"], "right")
    session.capture("02-context", opened("context"))
    session.point(session.state["items"]["context-more"]["client"])
    session.capture("03-context-child", opened("context-child"))
    session.point(session.state["items"]["context-command"]["client"], "left")
    session.capture("04-context-command", lambda s: closed(s) and s["counts"].get("context-command") == 1)
    session.point(session.state["contextTarget"], "right")
    session.wait(opened("context"))
    session.point(session.state["editor"]["client"], "left")
    session.capture("05-outside-dismiss", lambda s: closed(s) and s["counts"].get("editor-pointer", 0) > 0 and s["editor"]["focused"])
    session.point(session.state["items"]["menu-file"]["client"], "left")
    session.capture("06-menu", opened("menu"))
    session.point(session.state["items"]["menu-more"]["client"])
    session.capture("07-menu-child", opened("menu-child"))
    session.point(session.state["items"]["menu-command"]["client"], "left")
    session.capture("08-menu-command", lambda s: closed(s) and s["counts"].get("menu-command") == 1)
    session.key(0x79)  # F10, down+up
    session.capture("09-f10-selected", lambda s: closed(s) and s["items"]["menu-file"]["selected"])
    session.key(0x28)  # Down
    session.capture("10-keyboard-menu", opened("menu"))
    session.key(0x1B)
    session.key(0x1B)
    session.wait(lambda s: closed(s) and not s["items"]["menu-file"]["selected"])
    session.key(0x12)  # bare Alt, down+up
    session.capture("11-alt-selected", lambda s: closed(s) and s["items"]["menu-file"]["selected"])
    session.key(0x1B)
    session.wait(lambda s: closed(s) and not s["items"]["menu-file"]["selected"])
    session.point(session.state["combo"]["client"], "left")
    session.capture("12-combo", lambda s: s["combo"]["droppedDown"] and s["counts"].get("combo-opened", 0) > 0
                    and len(session.desktop.windows(session.process.pid)) > 1)
    session.key(0x28)
    session.key(0x0D)
    session.capture("13-combo-committed", lambda s: not s["combo"]["droppedDown"] and s["combo"]["selectedIndex"] == 1 and s["counts"].get("combo-committed") == 1)
    session.point(session.state["tooltipTarget"])
    session.capture("14-tooltip", lambda s: s["counts"].get("tooltip-popup", 0) > 0 and len(session.desktop.windows(session.process.pid)) > 1)


def run_case(desktop, executable, root, label, run):
    directory = root / label
    directory.mkdir()
    app = directory / "app"
    app.mkdir()
    receipt = dict(label=label, executable=str(executable), executableSha256=digest(executable),
                   assemblySha256=digest(executable.with_suffix(".dll")), qualified=False, status="incomplete")
    with (directory / "stdout.log").open("xb") as stdout, (directory / "stderr.log").open("xb") as stderr:
        process = subprocess.Popen([str(executable), str(app), run], cwd=executable.parent, stdout=stdout, stderr=stderr)
        session = Session(desktop, process, directory, run)
        receipt["pid"] = process.pid
        try:
            scenario(session)
            receipt["status"] = "phases-captured-not-pixel-qualified"
        except Exception as error:
            receipt.update(error=f"{type(error).__name__}: {error}", failedPhase=session.phase)
        finally:
            # No input/close commands sent to any other window. Stop only the
            # process we started; application watchdog is independent.
            if process.poll() is None:
                process.terminate()
            try:
                process.wait(timeout=2)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=2)
            receipt["processExitCode"] = process.returncode
            with (directory / "receipt.json").open("x") as stream:
                json.dump(receipt, stream, indent=2)
    return receipt["status"] != "incomplete"


def check_preparation(prepared, reference, portable):
    manifest = json.loads((prepared / "preparation.json").read_text())
    require(manifest.get("schema") == "popup-interaction-preparation-v1", "Unknown source preparation receipt")
    require(manifest["sourceSha256"] == digest(Path(__file__).resolve().parent / "PopupInteractionApp/Program.cs"),
            "Prepared source differs from this checked-in scenario")
    for mode, executable, tfm in (("Microsoft", reference, "net10.0-windows"), ("Portable", portable, "net10.0")):
        require(digest(prepared / mode / "Program.cs") == manifest["sourceSha256"], "Paired source bytes changed")
        require(executable == (prepared / mode / "bin/Release" / tfm / "PopupInteractionApp.exe").resolve(strict=True),
                "Executable is not the explicit prepared consumer output")
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reference-app", type=Path, required=True)
    parser.add_argument("--portable-app", type=Path, required=True)
    parser.add_argument("--evidence-parent", type=Path, required=True)
    parser.add_argument("--prepared-root", type=Path, required=True)
    args = parser.parse_args()
    reference, portable = args.reference_app.resolve(strict=True), args.portable_app.resolve(strict=True)
    require(reference != portable, "Reference and portable executables must be separate builds")
    require(args.evidence_parent.is_dir(), "Evidence parent must already exist")
    preparation = check_preparation(args.prepared_root.resolve(strict=True), reference, portable)
    desktop = WindowsDesktop()
    root = Path(tempfile.mkdtemp(prefix="popup-interaction-", dir=args.evidence_parent.resolve()))
    run = uuid.uuid4().hex
    with (root / "preparation.json").open("x") as stream:
        json.dump(preparation, stream, indent=2)
    print(f"Paired raw evidence: {root}", flush=True)
    first = run_case(desktop, reference, root, "microsoft", run)
    second = run_case(desktop, portable, root, "portable", run)
    print("Raw phases captured; manual image/event comparison remains required." if first and second else "Incomplete paired evidence; inspect receipts.")
    return 0 if first and second else 1


if __name__ == "__main__":
    raise SystemExit(main())
