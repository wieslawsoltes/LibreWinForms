#!/usr/bin/env python3
"""Read-only, PID/title-scoped macOS AX inventory; never input or screen capture."""

import argparse
import ctypes as C
import json
import math
from pathlib import Path
import selectors
import subprocess
import sys
import tempfile
import time

MAX_BYTES = 256 * 1024
MAX_NODES = 128
MAX_DEPTH = 8
SECONDS = 15


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


class Point(C.Structure):
    _fields_ = [("x", C.c_double), ("y", C.c_double)]


class Size(C.Structure):
    _fields_ = [("width", C.c_double), ("height", C.c_double)]


class Rect(C.Structure):
    _fields_ = [("origin", Point), ("size", Size)]


class Native:
    def __init__(self):
        require(sys.platform == "darwin", "Inventory requires macOS")
        self.deadline = time.monotonic() + 10
        self.cf = C.CDLL("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")
        self.ax = C.CDLL("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")
        self.cg = C.CDLL("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")
        p, i, long, b = C.c_void_p, C.c_int32, C.c_long, C.c_bool
        definitions = [
            (self.cf, "CFRelease", None, [p]), (self.cf, "CFRetain", p, [p]),
            (self.cf, "CFEqual", b, [p, p]), (self.cf, "CFGetTypeID", C.c_ulong, [p]),
            (self.cf, "CFStringGetTypeID", C.c_ulong, []),
            (self.cf, "CFStringCreateWithCString", p, [p, C.c_char_p, C.c_uint32]),
            (self.cf, "CFStringGetCString", b, [p, C.c_char_p, long, C.c_uint32]),
            (self.cf, "CFArrayGetTypeID", C.c_ulong, []),
            (self.cf, "CFArrayGetCount", long, [p]), (self.cf, "CFArrayGetValueAtIndex", p, [p, long]),
            (self.cf, "CFDictionaryGetValue", p, [p, p]),
            (self.cf, "CFNumberGetTypeID", C.c_ulong, []),
            (self.cf, "CFNumberGetValue", b, [p, i, p]),
            (self.ax, "AXIsProcessTrusted", b, []),
            (self.ax, "AXUIElementCreateApplication", p, [i]),
            (self.ax, "AXUIElementGetTypeID", C.c_ulong, []),
            (self.ax, "AXUIElementGetPid", i, [p, C.POINTER(i)]),
            (self.ax, "AXUIElementSetMessagingTimeout", i, [p, C.c_float]),
            (self.ax, "AXUIElementCopyAttributeValue", i, [p, p, C.POINTER(p)]),
            (self.ax, "AXValueGetTypeID", C.c_ulong, []),
            (self.ax, "AXValueGetType", i, [p]), (self.ax, "AXValueGetValue", b, [p, i, p]),
            (self.cg, "CGPreflightScreenCaptureAccess", b, []),
            (self.cg, "CGPreflightPostEventAccess", b, []),
            (self.cg, "CGWindowListCopyWindowInfo", p, [C.c_uint32, C.c_uint32]),
            (self.cg, "CGRectMakeWithDictionaryRepresentation", b, [p, C.POINTER(Rect)]),
        ]
        for library, name, result, arguments in definitions:
            function = getattr(library, name)
            function.restype, function.argtypes = result, arguments

    def check_time(self):
        require(time.monotonic() < self.deadline, "Native inventory deadline exceeded")

    def release(self, value):
        if value:
            self.cf.CFRelease(value)

    def string(self, value):
        require(value and self.cf.CFGetTypeID(value) == self.cf.CFStringGetTypeID(), "Expected native string")
        buffer = C.create_string_buffer(4096)
        require(self.cf.CFStringGetCString(value, buffer, len(buffer), 0x08000100), "Native string exceeds 4095 UTF-8 bytes")
        return buffer.value.decode("utf-8", errors="strict")

    def number(self, value):
        require(value and self.cf.CFGetTypeID(value) == self.cf.CFNumberGetTypeID(), "Expected native number")
        result = C.c_int64()
        require(self.cf.CFNumberGetValue(value, 4, C.byref(result)), "Native integer unavailable")
        return result.value

    def array(self, value, maximum):
        require(value and self.cf.CFGetTypeID(value) == self.cf.CFArrayGetTypeID(), "Expected native array")
        count = self.cf.CFArrayGetCount(value)
        require(0 <= count <= maximum, f"Native array exceeds {maximum}-element budget")
        return [self.cf.CFArrayGetValueAtIndex(value, index) for index in range(count)]

    def dictionary(self, value, key):
        name = self.cf.CFStringCreateWithCString(None, key.encode(), 0x08000100)
        require(name, "Native key allocation failed")
        try:
            return self.cf.CFDictionaryGetValue(value, name)
        finally:
            self.release(name)

    def attribute(self, element, name):
        self.check_time()
        # Applied to every queried element, not merely an unrelated AX root.
        budget = min(0.25, self.deadline - time.monotonic())
        require(budget > 0, "Native inventory deadline exceeded")
        require(self.ax.AXUIElementSetMessagingTimeout(element, budget) == 0, "AX messaging timeout unavailable")
        key = self.cf.CFStringCreateWithCString(None, name.encode(), 0x08000100)
        require(key, "Native key allocation failed")
        value = C.c_void_p()
        try:
            error = self.ax.AXUIElementCopyAttributeValue(element, key, C.byref(value))
        finally:
            self.release(key)
        try:
            self.check_time()
        except Exception:
            self.release(value.value)
            raise
        if error:
            self.release(value.value)
            # Unsupported/no-value is evidence, never a fabricated rectangle.
            require(error in (-25205, -25212), f"AX {name} failed: {error}")
            return None, error
        require(value.value, f"AX {name} returned no object on success")
        return value.value, 0

    def pid(self, element):
        require(self.cf.CFGetTypeID(element) == self.ax.AXUIElementGetTypeID(), "Expected AX element")
        result = C.c_int32()
        require(self.ax.AXUIElementGetPid(element, C.byref(result)) == 0, "AX PID unavailable")
        return result.value

    def scalar(self, value):
        kind = self.cf.CFGetTypeID(value)
        if kind == self.cf.CFStringGetTypeID():
            return self.string(value)
        require(kind == self.ax.AXValueGetTypeID(), "Unsupported AX scalar type")
        shape = self.ax.AXValueGetType(value)
        require(shape in (1, 2), "Expected native AX position or size")
        result = Point() if shape == 1 else Size()
        require(self.ax.AXValueGetValue(value, shape, C.byref(result)), "AX geometry unavailable")
        fields = {name: getattr(result, name) for name, _ in result._fields_}
        require(all(math.isfinite(v) for v in fields.values()), "Nonfinite native AX geometry")
        return fields

    def windows(self, pid):
        self.check_time()
        array = self.cg.CGWindowListCopyWindowInfo(1 | 16, 0)  # onscreen, excluding desktop elements
        require(array, "No current GUI window-server session")
        try:
            result = []
            for window in self.array(array, 4096):
                if self.number(self.dictionary(window, "kCGWindowOwnerPID")) != pid:
                    continue
                title = self.dictionary(window, "kCGWindowName")
                rect = Rect()
                require(self.cg.CGRectMakeWithDictionaryRepresentation(
                    self.dictionary(window, "kCGWindowBounds"), C.byref(rect)), "CG window bounds unavailable")
                bounds = dict(x=rect.origin.x, y=rect.origin.y, width=rect.size.width, height=rect.size.height)
                require(all(math.isfinite(v) for v in bounds.values()), "Nonfinite CG window bounds")
                result.append(dict(windowNumber=self.number(self.dictionary(window, "kCGWindowNumber")),
                                   pid=pid, title=self.string(title) if title else None, frameBounds=bounds))
            require(len(result) <= 32, "Target window count exceeds 32")
            self.check_time()
            return result
        finally:
            self.release(array)


def collect(native, pid, title, report):
    report["permissions"] = dict(accessibility=bool(native.ax.AXIsProcessTrusted()),
                                 screenRecording=bool(native.cg.CGPreflightScreenCaptureAccess()),
                                 eventPosting=bool(native.cg.CGPreflightPostEventAccess()))
    require(report["permissions"]["accessibility"], "Accessibility permission denied; no prompt requested")
    require(report["permissions"]["screenRecording"], "Screen-recording permission denied; no prompt requested")
    report["nativeWindowsBefore"] = native.windows(pid)
    matches = [w for w in report["nativeWindowsBefore"] if w["title"] == title]
    require(len(matches) == 1, "Expected exactly one native window with explicit PID/title")
    application = native.ax.AXUIElementCreateApplication(pid)
    require(application, "AX application unavailable")
    kept = []
    try:
        require(native.pid(application) == pid, "AX application PID differs")
        windows, error = native.attribute(application, "AXWindows")
        require(not error, f"AXWindows unavailable: {error}")
        try:
            selected = []
            for element in native.array(windows, 32):
                require(native.pid(element) == pid, "AX window belongs to another process")
                value, error = native.attribute(element, "AXTitle")
                try:
                    if not error and native.string(value) == title:
                        retained = native.cf.CFRetain(element)
                        kept.append(retained)
                        selected.append(retained)
                finally:
                    native.release(value)
        finally:
            native.release(windows)
        require(len(selected) == 1, "Expected exactly one AX window with explicit PID/title")
        nodes = []
        report["axNodes"] = nodes

        def visit(element, depth):
            native.check_time()
            require(native.pid(element) == pid, "AX descendant belongs to another process")
            for index, existing in enumerate(kept):
                if native.cf.CFEqual(existing, element) and index < len(nodes):
                    return index
            require(depth <= MAX_DEPTH and len(nodes) < MAX_NODES, "AX tree depth/node budget exceeded")
            index = len(nodes)
            if index != 0:
                kept.append(native.cf.CFRetain(element))
            node = dict(index=index, depth=depth, pid=pid, attributes={})
            nodes.append(node)
            for name in ("AXRole", "AXSubrole", "AXIdentifier", "AXTitle", "AXPosition", "AXSize", "AXChildren", "AXContents"):
                value, error = native.attribute(element, name)
                try:
                    if error:
                        node["attributes"][name] = dict(error=error)
                    elif name in ("AXChildren", "AXContents"):
                        node["attributes"][name] = [visit(child, depth + 1) for child in native.array(value, 64)]
                    else:
                        node["attributes"][name] = native.scalar(value)
                finally:
                    native.release(value)
            return index

        visit(selected[0], 0)
        report["nativeWindowsAfter"] = native.windows(pid)
        require(report["nativeWindowsBefore"] == report["nativeWindowsAfter"], "Target native windows changed during inventory")
        report["status"] = "observed-not-client-geometry-qualified"
    finally:
        for element in kept:
            native.release(element)
        native.release(application)


def worker(pid, title):
    report = dict(schema="popup-macos-ax-inventory-v1", pid=pid, title=title, qualified=False,
                  status="incomplete", inputExecuted=False, captureExecuted=False,
                  limitation="AX contents are not assumed to be NSWindow.contentView; no client geometry admission.")
    try:
        collect(Native(), pid, title, report)
    except Exception as error:
        report["error"] = f"{type(error).__name__}: {error}"
    encoded = json.dumps(report, indent=2, allow_nan=False).encode()
    if len(encoded) > MAX_BYTES:
        report = dict(schema=report["schema"], pid=pid, title=title, qualified=False,
                      status="incomplete", error="Inventory exceeds 256 KiB output budget")
        encoded = json.dumps(report).encode()
    sys.stdout.buffer.write(encoded)
    return 0 if report["status"] == "observed-not-client-geometry-qualified" else 1


def bounded_worker(command):
    # Only this fresh worker can be stopped; the observed application is never
    # started, activated, signaled, or otherwise changed by this inventory.
    child = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    streams = {child.stdout: bytearray(), child.stderr: bytearray()}
    deadline = time.monotonic() + SECONDS
    try:
        with selectors.DefaultSelector() as selector:
            for stream in streams:
                selector.register(stream, selectors.EVENT_READ)
            while selector.get_map():
                require(time.monotonic() < deadline, "Inventory worker exceeded 15-second deadline")
                for key, _ in selector.select(min(0.1, max(0, deadline - time.monotonic()))):
                    block = key.fileobj.read1(8192)
                    if not block:
                        selector.unregister(key.fileobj)
                    else:
                        require(sum(map(len, streams.values())) + len(block) <= MAX_BYTES, "Inventory worker output budget exceeded")
                        streams[key.fileobj].extend(block)
            child.wait(timeout=max(0.001, deadline - time.monotonic()))
        return child.returncode, bytes(streams[child.stdout]), bytes(streams[child.stderr]), None
    except Exception as error:
        return None, bytes(streams[child.stdout]), bytes(streams[child.stderr]), f"{type(error).__name__}: {error}"
    finally:
        if child.poll() is None:
            child.terminate()
            try:
                child.wait(timeout=1)
            except subprocess.TimeoutExpired:
                child.kill()
                child.wait(timeout=1)
        child.stdout.close()
        child.stderr.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--title", required=True)
    parser.add_argument("--evidence-parent", type=Path)
    parser.add_argument("--worker", action="store_true", help=argparse.SUPPRESS)
    args = parser.parse_args()
    require(args.pid > 0 and 0 < len(args.title.encode()) <= 1024, "Supply a positive PID and exact nonempty title (at most 1024 UTF-8 bytes)")
    if args.worker:
        return worker(args.pid, args.title)
    require(args.evidence_parent and args.evidence_parent.is_dir(), "Evidence parent must already exist")
    root = Path(tempfile.mkdtemp(prefix="popup-macos-ax-", dir=args.evidence_parent.resolve()))
    code, stdout, stderr, error = bounded_worker([sys.executable, str(Path(__file__).resolve()),
                                                "--worker", "--pid", str(args.pid), "--title", args.title])
    for name, content in (("worker.stdout.json", stdout), ("worker.stderr.log", stderr)):
        with (root / name).open("xb") as stream:
            stream.write(content)
    receipt = dict(schema="popup-macos-ax-run-v1", pid=args.pid, title=args.title,
                   qualified=False, workerExitCode=code, error=error, status="incomplete")
    if code == 0 and error is None:
        try:
            report = json.loads(stdout)
            require(report.get("schema") == "popup-macos-ax-inventory-v1" and report.get("pid") == args.pid
                    and report.get("title") == args.title and report.get("qualified") is False
                    and report.get("status") == "observed-not-client-geometry-qualified", "Worker receipt identity/status differs")
            receipt["status"] = report["status"]
        except (ValueError, RuntimeError) as failure:
            receipt["error"] = str(failure)
    with (root / "receipt.json").open("x") as stream:
        json.dump(receipt, stream, indent=2)
    print(f"Read-only AX inventory: {root}; {receipt['status']}")
    return 0 if receipt["status"] != "incomplete" else 1


if __name__ == "__main__":
    raise SystemExit(main())
