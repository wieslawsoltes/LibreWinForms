#!/usr/bin/env python3
"""Read-only comparison of paired source, typed Cocoa, and independent CG evidence.

Never injects input, captures pixels, guesses client decorations, or fits a scale
to rectangles. Passing recorded geometry is not desktop/package qualification.
"""

import argparse
import hashlib
import json
import math
from pathlib import Path


LIMIT = 256 * 1024
NAMES = {"main", "context", "context-child", "menu", "menu-child"}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def integer(value, name, nonzero=False, minimum=-(2 ** 63), maximum=2 ** 63 - 1):
    require(type(value) is int and minimum <= value <= maximum and (not nonzero or value != 0),
            f"Invalid {name}")
    return value


def finite(value):
    if type(value) not in (int, float):
        return False
    try:
        return math.isfinite(value)
    except OverflowError:
        return False


def scale(value, name):
    require(finite(value) and 0 < value <= 8,
            f"Invalid {name}")
    return value


def rectangle(value, name):
    require(isinstance(value, dict) and set(value) == {"x", "y", "width", "height"}, f"Invalid {name}")
    require(all(finite(v) for v in value.values()), f"Nonfinite {name}")
    require(value["width"] > 0 and value["height"] > 0
            and math.isfinite(value["x"] + value["width"])
            and math.isfinite(value["y"] + value["height"]), f"Empty/overflowed {name}")
    return value


def managed_rectangle(native, mode, dpi, framebuffer):
    # This is the declared LibreWindowCoordinates policy, not a scale inferred
    # from observed source/native size. Preserve its AwayFromZero integer rule.
    require(mode in ("Logical", "DevicePixels"), "Unknown source coordinate mode")
    factor = framebuffer / dpi if mode == "Logical" else framebuffer
    result = {}
    for key, value in native.items():
        number = value * factor
        require(math.isfinite(number), "Managed coordinate overflow")
        rounded = math.floor(number + 0.5) if number >= 0 else math.ceil(number - 0.5)
        require(-(2 ** 31) <= rounded < 2 ** 31, "Managed coordinate overflow")
        result[key] = rounded
    return result, factor


def validate_cocoa_geometry(snapshot, sidecar, native_windows, expected_pid):
    integer(expected_pid, "expected PID", minimum=1, maximum=2 ** 31 - 1)
    require(isinstance(snapshot, dict) and isinstance(sidecar, dict), "Invalid source/native document")
    require(expected_pid > 0 and type(snapshot.get("schema")) is int and snapshot["schema"] == 1,
            "Invalid source identity/schema")
    require(integer(snapshot.get("pid"), "source PID") == expected_pid
            and integer(sidecar.get("pid"), "native PID") == expected_pid, "PID mismatch")
    sequence = integer(snapshot.get("sequence"), "source sequence", nonzero=True)
    require(0 < sequence <= 650 and integer(sidecar.get("sequence"), "native sequence") == sequence, "Sequence mismatch")
    require(isinstance(snapshot.get("title"), str) and 0 < len(snapshot["title"]) <= 4095,
            "Missing/invalid main title")
    require(sidecar.get("schema") == "popup-native-geometry-v1"
            and sidecar.get("coordinateSpace") == "native-desktop-top-left-points", "Unknown native geometry schema/units")
    require(isinstance(snapshot.get("popups"), dict)
            and set(snapshot["popups"]) == NAMES - {"main"}, "Source popup set differs")
    require(isinstance(snapshot.get("form"), dict)
            and all(isinstance(value, dict) for value in snapshot["popups"].values()), "Invalid source window state")
    entries = sidecar.get("windows")
    require(isinstance(entries, list) and len(entries) == len(NAMES)
            and all(isinstance(entry, dict) for entry in entries), "Invalid native window entries")
    require({entry.get("name") for entry in entries} == NAMES, "Duplicate/missing native window names")
    require(isinstance(native_windows, list) and len(native_windows) <= 32, "Invalid CG inventory budget")
    require(all(isinstance(window, dict) and integer(window.get("pid"), "CG PID") == expected_pid for window in native_windows),
            "Foreign CG inventory PID")
    cg_numbers = [integer(window.get("windowNumber"), "CG window number", minimum=1, maximum=2 ** 32 - 1)
                  for window in native_windows]
    require(all(number > 0 for number in cg_numbers) and len(set(cg_numbers)) == len(cg_numbers),
            "Invalid/duplicate CG window identity")
    for window in native_windows:
        rectangle(window.get("frameBounds"), "CG frame")

    matched = []
    source_handles, native_handles, views, numbers = set(), set(), set(), set()
    for entry in entries:
        name = entry["name"]
        source = snapshot["form"] if name == "main" else snapshot["popups"][name]
        require(type(source.get("visible")) is bool and entry.get("visible") is source["visible"],
                f"Visibility mismatch: {name}")
        if not source["visible"]:
            continue
        require(entry.get("handleCreated") is True and entry.get("available") is True
                and entry.get("cocoaComparisonAdmitted") is True, f"Native geometry unavailable: {name}")
        handle = entry.get("sourceHandle")
        require(isinstance(handle, dict) and handle.get("kind") == "Window", f"Logical/non-window source handle: {name}")
        source_handle = integer(handle.get("value"), "source window handle", nonzero=True)
        geometry = entry.get("geometry")
        require(isinstance(geometry, dict), f"Missing geometry: {name}")
        native = geometry.get("window")
        require(isinstance(native, dict) and native.get("kind") == "Cocoa"
                and integer(native.get("display"), "native display") == 0,
                f"Not an owned Cocoa window: {name}")
        native_handle = integer(native.get("handle"), "native window handle", nonzero=True)
        view = integer(geometry.get("contentView"), "native content view", nonzero=True)
        number = integer(geometry.get("cocoaWindowNumber"), "Cocoa window number", nonzero=True)
        require(number > 0 and source_handle not in source_handles and native_handle not in native_handles
                and view not in views and number not in numbers, "Aliased native/source window identity")
        source_handles.add(source_handle)
        native_handles.add(native_handle)
        views.add(view)
        numbers.add(number)
        content = rectangle(geometry.get("contentBounds"), "native content")
        frame = rectangle(geometry.get("frameBounds"), "native frame")
        backing = scale(geometry.get("backingScale"), "native backing scale")
        dpi = scale(entry.get("dpiScale"), "source DPI scale")
        framebuffer = scale(entry.get("framebufferScale"), "source framebuffer scale")
        require(backing == framebuffer, "Native backing/source framebuffer scale mismatch")
        cg = [window for window in native_windows if window["windowNumber"] == number]
        require(len(cg) == 1 and cg[0]["frameBounds"] == frame, f"No exact independent CG frame match: {name}")
        if name == "main":
            require(cg[0].get("title") == snapshot.get("title"), "Main PID/title identity mismatch")
        managed, factor = managed_rectangle(content, entry.get("coordinateMode"), dpi, framebuffer)
        require(rectangle(source.get("client"), "source client") == managed,
                f"Source/native client mismatch under declared coordinate policy: {name}")
        matched.append(dict(name=name, sourceHandle=source_handle, nativeWindow=native_handle,
                            contentView=view, independentCGWindowNumber=cg[0]["windowNumber"],
                            nativeContent=content, nativeFrame=frame, sourceClient=source["client"],
                            declaredNativeToSourceScale=factor, backingScale=backing))
    require(any(item["name"] == "main" for item in matched), "No visible matched main window")
    return dict(schema="popup-native-geometry-comparison-v1", pid=expected_pid, sequence=sequence,
                status="recorded-geometry-matched", qualified=False, windows=matched,
                unmatchedCGWindows=[window for window in native_windows if window["windowNumber"] not in numbers],
                limitations=["No input or pixel validation", "No current-process/freshness proof from files alone",
                             "ComboBox/ToolTip private native surfaces are not source-client matched"])


def read_bounded(path):
    require(path.stat().st_size <= LIMIT, "Evidence exceeds 256 KiB")
    raw = path.read_bytes()
    require(len(raw) <= LIMIT, "Evidence exceeds 256 KiB")
    def reject_constant(value):
        raise ValueError(f"Nonfinite JSON value: {value}")
    def unique_members(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, f"Duplicate JSON member: {key}")
            result[key] = value
        return result
    try:
        value = json.loads(raw, parse_constant=reject_constant, object_pairs_hook=unique_members)
    except RecursionError as error:
        raise ValueError("JSON nesting exceeds the parser budget") from error
    return value, hashlib.sha256(raw).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--snapshot", type=Path, required=True)
    parser.add_argument("--native-geometry", type=Path, required=True)
    parser.add_argument("--cg-inventory", type=Path, required=True)
    parser.add_argument("--expected-pid", type=int, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    receipt = dict(status="rejected", qualified=False)
    try:
        source, source_hash = read_bounded(args.snapshot)
        native, native_hash = read_bounded(args.native_geometry)
        cg, cg_hash = read_bounded(args.cg_inventory)
        receipt = validate_cocoa_geometry(source, native, cg, args.expected_pid)
        receipt["inputSha256"] = dict(snapshot=source_hash, nativeGeometry=native_hash, cgInventory=cg_hash)
    except (ValueError, OSError, KeyError, TypeError) as error:
        receipt["error"] = f"{type(error).__name__}: {error}"
    with args.output.open("x") as stream:
        json.dump(receipt, stream, indent=2, allow_nan=False)
    return 0 if receipt["status"] == "recorded-geometry-matched" else 1


if __name__ == "__main__":
    raise SystemExit(main())
