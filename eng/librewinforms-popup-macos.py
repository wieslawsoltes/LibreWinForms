#!/usr/bin/env python3
"""Real macOS popup evidence through the unchanged shared fourteen-phase scenario."""

import argparse
from contextlib import contextmanager
import importlib.util
import json
import math
import os
from pathlib import Path
import signal
import struct
import subprocess
import sys
import tempfile
import time
import uuid


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(filename))
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


SHARED = module("popup_desktop", "librewinforms-popup-desktop.py")
GEOMETRY = module("popup_native_geometry", "librewinforms-popup-native-geometry.py")
require = SHARED.require
ENVIRONMENT = "LIBREWINFORMS_POPUP_NATIVE_GEOMETRY"
MACHO = {b"\xcf\xfa\xed\xfe", b"\xfe\xed\xfa\xcf", b"\xca\xfe\xba\xbe", b"\xbe\xba\xfe\xca",
         b"\xca\xfe\xba\xbf", b"\xbf\xba\xfe\xca"}


def native_executable(path):
    require(path.is_file() and os.access(path, os.X_OK), "Native executable is unavailable")
    with path.open("rb") as stream:
        require(stream.read(4) in MACHO, "An actual Mach-O executable is required")


def check_preparation(prepared, app):
    manifest, _ = GEOMETRY.read_bounded(prepared / "preparation.json")
    require(manifest.get("schema") == "popup-interaction-preparation-v1", "Unknown source preparation receipt")
    require(manifest["sourceSha256"] == SHARED.digest(Path(__file__).parent / "PopupInteractionApp/Program.cs") ==
            SHARED.digest(prepared / "Portable/Program.cs") == SHARED.digest(prepared / "Microsoft/Program.cs"),
            "Microsoft/portable shared scenario bytes differ")
    require(manifest["startupSourceSha256"] == SHARED.digest(Path(__file__).parent / "PopupInteractionApp/PopupInteractionStartup.cs") ==
            SHARED.digest(prepared / "Portable/PopupInteractionStartup.cs") == SHARED.digest(prepared / "Microsoft/PopupInteractionStartup.cs"),
            "Microsoft/portable shared startup bytes differ")
    native = manifest.get("nativeGeometry", {})
    require(native.get("enabled") is True and native.get("environmentVariable") == ENVIRONMENT
            and native.get("sourcePath") == "Portable/PortableNativeGeometryObserver.cs",
            "Prepare with --native-geometry; source-bound fallback is not allowed")
    require(native["sourceSha256"] == SHARED.digest(prepared / native["sourcePath"]) ==
            SHARED.digest(Path(__file__).parent / "PopupInteractionApp/PortableNativeGeometryObserver.cs"),
            "Prepared typed native observer bytes differ")
    require(app == (prepared / "Portable/bin/Release/net11.0/PopupInteractionApp").resolve(strict=True),
            "Executable is not the explicit prepared macOS consumer output")
    native_executable(app)
    return manifest


@contextmanager
def observer_environment():
    previous = os.environ.get(ENVIRONMENT)
    os.environ[ENVIRONMENT] = "1"
    try:
        yield
    finally:
        if previous is None:
            os.environ.pop(ENVIRONMENT, None)
        else:
            os.environ[ENVIRONMENT] = previous


def contains_rectangle(outer, inner):
    return (outer["x"] <= inner["x"] and outer["y"] <= inner["y"] and
            outer["x"] + outer["width"] >= inner["x"] + inner["width"] and
            outer["y"] + outer["height"] >= inner["y"] + inner["height"])


def native_point(rectangle, windows):
    """Invert ONLY the already-proven source coordinate policy, never fit bounds."""
    GEOMETRY.rectangle(rectangle, "input rectangle")
    x = rectangle["x"] + rectangle["width"] // 2
    y = rectangle["y"] + rectangle["height"] // 2
    points = set()
    for window in windows:
        if window["client"] is None or not contains_rectangle(window["client"], rectangle):
            continue
        factor = window["nativeToSourceScale"]
        require(GEOMETRY.finite(factor) and factor > 0, "Invalid declared coordinate factor")
        point = (x / factor, y / factor)
        require(all(math.isfinite(value) and abs(value) <= 1_000_000 for value in point), "Native point exceeds budget")
        if SHARED.contains(window["nativeClient"], *point):
            points.add(point)
    require(len(points) == 1, "No unambiguous typed native client/coordinate policy for input target")
    return list(points.pop())


def combine_windows(source, sidecar, inventory, pid):
    comparison = GEOMETRY.validate_cocoa_geometry(source, sidecar, inventory, pid)
    require(all(isinstance(w.get("title"), str) and len(w["title"].encode("utf-8")) <= 4095 for w in inventory),
            "Invalid native window title")
    matches = {window["independentCGWindowNumber"]: window for window in comparison["windows"]}
    result = []
    for window in inventory:
        match = matches.get(window["windowNumber"])
        result.append(dict(pid=pid, windowNumber=window["windowNumber"], title=window["title"],
                           bounds=window["frameBounds"], client=match["sourceClient"] if match else None,
                           nativeClient=match["nativeContent"] if match else None,
                           nativeToSourceScale=match["declaredNativeToSourceScale"] if match else None,
                           sourceName=match["name"] if match else None,
                           clientGeometryVerified=match is not None,
                           frameCoordinateSpace="native-desktop-top-left-points"))
    return sorted(result, key=lambda window: window["windowNumber"])


def combine_observations(observations, inventory, pid):
    require(0 < len(observations) <= 2, "Only the exact owner and one modal child may supply geometry")
    result = None
    identities = {key: set() for key in ("sourceHandle", "nativeWindow", "contentView", "independentCGWindowNumber")}
    for source, sidecar in observations:
        comparison = GEOMETRY.validate_cocoa_geometry(source, sidecar, inventory, pid)
        for window in comparison["windows"]:
            for key, seen in identities.items():
                require(window[key] not in seen, "Aliased owner/child native geometry identity")
                seen.add(window[key])
        current = combine_windows(source, sidecar, inventory, pid)
        if result is None:
            result = current
        else:
            require([w["windowNumber"] for w in result] == [w["windowNumber"] for w in current],
                    "Native inventory changed during owner/child geometry merge")
            result = [new if new["clientGeometryVerified"] else old for old, new in zip(result, current)]
    return result


def overlaps(lhs, rhs):
    return (max(lhs["x"], rhs["x"]) < min(lhs["x"] + lhs["width"], rhs["x"] + rhs["width"]) and
            max(lhs["y"], rhs["y"]) < min(lhs["y"] + lhs["height"], rhs["y"] + rhs["height"]))


def check_capture_occlusion(value, windows):
    """Independently check both bounded native observations, not pixel visibility."""
    require(isinstance(value, dict) and value.get("policy") == "foreign-window-frame-intersection-v1"
            and value.get("coordinateSpace") == "native-desktop-top-left-points",
            "Missing/unknown native capture obstruction policy")
    require(isinstance(windows, list) and 0 < len(windows) <= 32, "Invalid owned capture windows")
    pid = GEOMETRY.integer(windows[0].get("pid"), "owned capture PID", minimum=1, maximum=2 ** 31 - 1)
    expected = {}
    for window in windows:
        require(type(window.get("pid")) is int and window["pid"] == pid, "Foreign owned capture PID")
        number = GEOMETRY.integer(window.get("windowNumber"), "owned capture number", minimum=1, maximum=2 ** 32 - 1)
        require(number not in expected, "Duplicate owned capture identity")
        expected[number] = GEOMETRY.rectangle(window.get("bounds"), "owned capture frame")
    samples = value.get("samples")
    require(isinstance(samples, list) and len(samples) == 2, "Capture needs before/after obstruction observations")
    previous_time = -1
    for phase, sample in zip(("before", "after"), samples):
        require(isinstance(sample, dict) and sample.get("phase") == phase, "Capture observation phase mismatch")
        observed = sample.get("observedUptimeSeconds")
        require(GEOMETRY.finite(observed) and observed >= 0 and observed >= previous_time,
                "Invalid/nonmonotonic capture observation time")
        previous_time = observed
        total = GEOMETRY.integer(sample.get("systemWindowCount"), "CG inventory count", minimum=1, maximum=4096)
        entries = sample.get("windows")
        require(isinstance(entries, list) and 0 < len(entries) <= min(64, total), "Capture observation budget exceeded")
        seen, own, previous_z = set(), {}, -1
        for entry in entries:
            require(isinstance(entry, dict) and set(entry) ==
                    {"pid", "windowNumber", "zIndex", "layer", "alpha", "frameBounds"},
                    "Missing/unexpected capture window metadata")
            owner = GEOMETRY.integer(entry["pid"], "CG owner PID", minimum=0, maximum=2 ** 31 - 1)
            number = GEOMETRY.integer(entry["windowNumber"], "CG window number", minimum=1, maximum=2 ** 32 - 1)
            z_index = GEOMETRY.integer(entry["zIndex"], "CG z-index", minimum=0, maximum=total - 1)
            GEOMETRY.integer(entry["layer"], "CG layer", minimum=-(2 ** 31), maximum=2 ** 31 - 1)
            require(number not in seen and z_index > previous_z, "Duplicate/reordered CG obstruction identity")
            seen.add(number)
            previous_z = z_index
            require(GEOMETRY.finite(entry["alpha"]) and 0 <= entry["alpha"] <= 1, "Invalid CG window alpha")
            frame = GEOMETRY.rectangle(entry["frameBounds"], "CG obstruction frame")
            require(any(overlaps(frame, bounds) for bounds in expected.values()), "Unrelated capture window metadata")
            if owner == pid:
                require(number in expected and frame == expected[number], "Owned capture identity/frame changed")
                own[number] = entry
        require(set(own) == set(expected), "Missing owned capture identity")
        for foreign in entries:
            if foreign["pid"] == pid or foreign["alpha"] == 0:
                continue
            for owned in own.values():
                require(not (foreign["zIndex"] < owned["zIndex"] and
                             overlaps(foreign["frameBounds"], owned["frameBounds"])),
                        f"Foreign window potentially occludes owned capture ({phase})")


def check_capture(image, destination, windows):
    require(isinstance(image, dict) and image.get("captureProvider") == "ScreenCaptureKit.captureImage(in:)"
            and image.get("encoding") == "ImageIO BMP, no resizing", "Unknown native capture provider/encoding")
    check_capture_occlusion(image.get("captureOcclusion"), windows)
    rectangles = [window["bounds"] for window in windows]
    expected = dict(x=min(r["x"] for r in rectangles), y=min(r["y"] for r in rectangles))
    expected["width"] = max(r["x"] + r["width"] for r in rectangles) - expected["x"]
    expected["height"] = max(r["y"] + r["height"] for r in rectangles) - expected["y"]
    require(image.get("sourceRect") == expected, "Capture rectangle differs from actual owned native frames")
    width = GEOMETRY.integer(image.get("width"), "image width", minimum=1, maximum=4096)
    height = GEOMETRY.integer(image.get("height"), "image height", minimum=1, maximum=4096)
    require(width * height <= 4_194_304, "Actual returned image exceeds shared pixel budget")
    require(image.get("returnedPixelsPerPointX") == width / expected["width"] and
            image.get("returnedPixelsPerPointY") == height / expected["height"], "Returned image scale metadata differs")
    require(destination.is_file() and not destination.is_symlink(), "Missing regular native capture")
    length = destination.stat().st_size
    require(54 <= length <= 16 * 1024 * 1024 + 54 and image.get("encodedBytes") == length,
            "Encoded native capture exceeds/differs from its bounded receipt")
    with destination.open("rb") as stream:
        header = stream.read(54)
    require(header[:2] == b"BM" and struct.unpack_from("<I", header, 2)[0] == length,
            "Native encoder did not produce a complete BMP")
    require(struct.unpack_from("<I", header, 14)[0] >= 40, "Unsupported native BMP header")
    actual_width, actual_height = struct.unpack_from("<ii", header, 18)
    require(actual_width == width and abs(actual_height) == height, "Encoded/native image dimensions differ")
    return dict(image, sha256=SHARED.digest(destination), qualified=False, usableWindowPixelsVerified=False,
                fullChromeGeometryVerified=True,
                privateSurfaceClientGeometryVerified=False)


class MacDesktop:
    # Existing shared semantic keys -> documented Carbon physical key codes.
    KEYS = {0x79: 109, 0x28: 125, 0x1B: 53, 0x12: 58, 0x0D: 36}

    def __init__(self, helper, root):
        require(sys.platform == "darwin", "This native driver requires macOS")
        self.helper, self.root = helper, root
        self.deadline = time.monotonic() + 10
        self.calls, self.bytes = 0, 0
        self.modal_observations = None
        native_executable(helper)
        self.provenance = dict(helperSha256=SHARED.digest(helper),
                               helperSourceSha256=SHARED.digest(Path(__file__).with_name("PopupDesktopNative.swift")),
                               helperSourceBuildIdentityVerified=False, qualified=False)
        self.provenance["preflight"] = self.call("preflight")

    def call(self, action, **arguments):
        remaining = self.deadline - time.monotonic()
        require(remaining > 0, "Original macOS scenario deadline expired")
        require(self.calls < 1024 and self.bytes < 8 * 1024 * 1024, "Native diagnostic reply budget exceeded")
        self.calls += 1
        request = dict(action=action, **arguments)
        payload = json.dumps(request, allow_nan=False).encode()
        require(len(payload) <= 32768, "Native request exceeds 32 KiB")
        prefix = self.root / f"native-call-{self.calls:04d}"
        with prefix.with_suffix(".request.json").open("xb") as stream:
            stream.write(payload)
        # The subprocess is the owned native helper only. Timeout never targets
        # arbitrary native windows; shared run_case retains its app cleanup.
        try:
            completed = subprocess.run([str(self.helper)], input=payload, capture_output=True,
                                       timeout=min(5 if action == "capture" else 3, remaining), check=False)
        except subprocess.TimeoutExpired as error:
            for suffix, data in ((".stdout.json", error.stdout), (".stderr.log", error.stderr)):
                with prefix.with_suffix(suffix).open("xb") as stream:
                    stream.write((data or b"")[:65536])
            raise
        self.bytes += len(payload) + len(completed.stdout) + len(completed.stderr)
        for suffix, data in ((".stdout.json", completed.stdout), (".stderr.log", completed.stderr)):
            with prefix.with_suffix(suffix).open("xb") as stream:
                stream.write(data[:65536])
        require(len(completed.stdout) <= 65536 and len(completed.stderr) <= 65536, "Native reply exceeds budget")
        reply, _ = GEOMETRY.read_bounded(prefix.with_suffix(".stdout.json"))
        require(isinstance(reply, dict) and reply.get("schema") == "popup-macos-native-v1"
                and reply.get("success") is True and completed.returncode == 0,
                f"Native {action} failed: {str(reply)[:2048]}")
        require(reply.get("action") == action, "Native reply operation mismatch")
        if "pid" in arguments:
            require(type(reply.get("pid")) is int and reply["pid"] == arguments["pid"], "Native reply PID mismatch")
        return reply

    def windows(self, pid):
        inventory = self.call("inventory", pid=pid)["windows"]
        if self.modal_observations is not None:
            pairs = []
            for directory, title in self.modal_observations:
                source, _ = self.modal_reader.observation(directory, pid, title)
                path = directory / f"native-geometry-{source['sequence']:08d}.json"
                self.modal_reader.regular(path)
                sidecar, _ = GEOMETRY.read_bounded(path)
                self.modal_reader.regular(path)
                pairs.append((source, sidecar))
            return combine_observations(pairs, inventory, pid)
        app = self.root / "portable/app"
        snapshots = list(app.glob("snapshot-????????.json"))
        require(0 < len(snapshots) <= 650, "Missing/over-budget source snapshots")
        source, _ = GEOMETRY.read_bounded(max(snapshots))
        sidecar, _ = GEOMETRY.read_bounded(app / f"native-geometry-{source['sequence']:08d}.json")
        # The typed observer writes this exact sidecar BEFORE the matching source
        # snapshot. Do not substitute latest/unpaired or source-derived bounds.
        return combine_windows(source, sidecar, inventory, pid)

    def select_observations(self, observations):
        require(0 < len(observations) <= 2, "Invalid owner/modal observer inventory")
        if self.modal_observations is None:
            self.modal_reader = module("popup_modal_observation", "librewinforms-popup-modal.py")
        for path, title in observations:
            self.modal_reader.regular(path, directory=True)
            require(isinstance(title, str) and title.startswith("PopupInteractionApp ["), "Invalid observed source title")
        self.modal_observations = tuple(observations)

    def window_input_enabled(self, window, state):
        value = state.get("modal", {}).get("inputEnabled")
        require(type(value) is bool, "Missing exact typed source modal input observation")
        return value  # Source policy, not ordinary Cocoa native blocking proof.

    def foreground(self, pid):
        self.call("foreground", pid=pid)

    @staticmethod
    def window_identity(window):
        return window["windowNumber"]

    def activate(self, window):
        require(window["sourceName"] == "main" and window["clientGeometryVerified"], "Unverified activation target")
        self.call("activate", pid=window["pid"], windowNumber=window["windowNumber"])

    def pointer(self, pid, rect, click=None):
        require(click in (None, "left", "right"), "Unsupported pointer button")
        point = native_point(rect, self.windows(pid))
        self.call("pointer", pid=pid, point=point, click=click)

    def key(self, pid, key):
        require(key in self.KEYS, "Unsupported shared-scenario key")
        self.call("key", pid=pid, key=self.KEYS[key])

    def blocked_owner_pointer(self, pid, owner_window, target_rectangle):
        fresh = [w for w in self.windows(pid) if w["windowNumber"] == owner_window["windowNumber"]]
        require(fresh == [owner_window] and owner_window["clientGeometryVerified"] and owner_window["pid"] == pid,
                "Actual Cocoa owner identity/client/frame changed")
        SHARED.observed_rectangle(target_rectangle)
        require(contains_rectangle(owner_window["client"], target_rectangle), "Owner target is not in actual source client")
        scale = owner_window["nativeToSourceScale"]
        require(GEOMETRY.finite(scale) and scale > 0, "Missing exact owner native coordinate policy")
        target = {key: value / scale for key, value in target_rectangle.items()}
        point = native_point(target_rectangle, [owner_window])
        native = dict(pid=pid, windowNumber=owner_window["windowNumber"], title=owner_window["title"], frameBounds=owner_window["bounds"])
        proof = self.call("blocked-owner-pointer", pid=pid, owner=native, ownerClient=owner_window["nativeClient"],
                          target=target, point=point, click="left")["proof"]
        require(proof.get("policy") == "exposed-owner-native-z-order-v1" and proof.get("qualified") is False
                and proof.get("owner") == native and proof.get("ownerClient") == owner_window["nativeClient"]
                and proof.get("target") == target and proof.get("point") == point,
                "Native owner input reply identity/geometry differs")
        require(isinstance(proof.get("samples"), list) and len(proof["samples"]) == 2, "Missing native owner input samples")
        owner = dict(id=native["windowNumber"], pid=pid, title=native["title"], bounds=native["frameBounds"], client=owner_window["nativeClient"])
        for sample, phase in zip(proof["samples"], ("before", "before-click")):
            require(sample.get("phase") == phase, "Native owner input phase differs")
            rows = [dict(id=w["windowNumber"], pid=w["pid"], bounds=w["frameBounds"],
                         **({"title": owner["title"], "client": owner["client"]} if w["windowNumber"] == owner["id"] else {}))
                    for w in sample["windows"]]
            require([w["zIndex"] for w in sample["windows"]] == sorted(w["zIndex"] for w in sample["windows"]),
                    "Native owner obstruction Z-order differs")
            SHARED.exposed_owner_target(pid, owner, target, rows)
        return proof

    def screenshot(self, pid, windows, destination):
        require(windows and all(window["pid"] == pid for window in windows), "Capture needs exact owned windows")
        expected = [dict(pid=pid, windowNumber=w["windowNumber"], title=w["title"], frameBounds=w["bounds"]) for w in windows]
        image = self.call("capture", pid=pid, windows=expected, output=str(destination))["image"]
        return check_capture(image, destination, windows)


def cancel(signum, _frame):
    raise SystemExit(128 + signum)


def main():
    signal.signal(signal.SIGTERM, cancel)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--portable-app", type=Path, required=True)
    parser.add_argument("--prepared-root", type=Path, required=True)
    parser.add_argument("--native-helper", type=Path, required=True)
    parser.add_argument("--evidence-parent", type=Path, required=True)
    parser.add_argument("--modal", action="store_true", help="Actual source ShowDialog with explicit native modal startup")
    args = parser.parse_args()
    require(args.evidence_parent.is_dir(), "Evidence parent must already exist")
    root = Path(tempfile.mkdtemp(prefix="popup-macos-interaction-", dir=args.evidence_parent.resolve()))
    launch_attempted = False
    try:
        app = args.portable_app.resolve(strict=True)
        preparation = check_preparation(args.prepared_root.resolve(strict=True), app)
        with (root / "preparation.json").open("x") as stream:
            json.dump(preparation, stream, indent=2)
        print(f"Unqualified real macOS evidence: {root}", flush=True)
        desktop = MacDesktop(args.native_helper.resolve(strict=True), root)
        with (root / "macos.json").open("x") as stream:
            json.dump(desktop.provenance, stream, indent=2)
        desktop.deadline = time.monotonic() + 60
        launch_attempted = True
        with observer_environment():
            if args.modal:
                complete = SHARED.run_case(desktop, app, root, "portable", uuid.uuid4().hex, modal=True, native_modal=True)
            else:
                complete = SHARED.run_case(desktop, app, root, "portable", uuid.uuid4().hex)
        print("Raw phases captured; native pixel/reference review remains required." if complete else
              "Incomplete evidence; inspect retained native calls and shared receipt.")
        return 0 if complete else 1
    except BaseException as error:
        with (root / "driver-failure.json").open("x") as stream:
            json.dump(dict(error=f"{type(error).__name__}: {error}"[:2048], qualified=False,
                           appNotLaunched=not launch_attempted,
                           status="driver-failed" if launch_attempted else "preflight-failed"), stream, indent=2)
        raise


if __name__ == "__main__":
    raise SystemExit(main())
