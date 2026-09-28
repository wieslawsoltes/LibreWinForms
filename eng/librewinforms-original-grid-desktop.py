#!/usr/bin/env python3
"""Real Windows input on the original sample; never substitutes for package/pixel qualification."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time

ENG = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("grid_windows_desktop", ENG / "librewinforms-popup-desktop.py")
DESKTOP = importlib.util.module_from_spec(spec)
spec.loader.exec_module(DESKTOP)
require, digest = DESKTOP.require, DESKTOP.digest
TITLE = "DataGridViewCustomColumn Sample"
SOURCE_COMMIT = "acb39ceb13f910ae0f8f6298059c59102b749c41"
MAX_LOG_BYTES = 2 * 1024 * 1024


def units(text):
    payload = text.encode("utf-16-le")
    return [int.from_bytes(payload[i:i + 2], "little") for i in range(0, len(payload), 2)]


def save(path, value):
    with path.open("x", encoding="utf-8") as output:
        json.dump(value, output, indent=2)


def verify_files(directory, entries):
    require(0 < len(entries) <= 2000, "Payload manifest count is invalid")
    admitted = {}
    for entry in entries:
        relative = Path(entry["path"])
        require(not relative.is_absolute() and ".." not in relative.parts, "Unsafe payload path")
        require(str(relative) not in admitted, "Duplicate payload path")
        require(re.fullmatch(r"[0-9a-f]{64}", entry["sha256"]) is not None, "Invalid payload digest")
        path = directory / relative
        require(path.resolve().is_relative_to(directory.resolve()) and not path.is_symlink(), "Payload escaped its directory")
        require(digest(path) == entry["sha256"], "Payload differs from its build receipt")
        admitted[str(relative)] = entry["sha256"]
    actual = {str(path.relative_to(directory)) for path in directory.rglob("*") if path.is_file()}
    require(actual == set(admitted), "Payload manifest is not complete")
    return admitted


def validate_snapshot(state, pid):
    require(state.get("Schema") == "original-grid-observer-v1" and state.get("ProcessId") == pid,
            "Observer schema/process identity mismatch")
    require(type(state.get("Sequence")) is int and 1 <= state["Sequence"] <= 600, "Observer sequence exceeds its bound")
    require(state.get("FormTitle") == TITLE, "Unexpected original sample title")
    for field in ("Client", "GridClient", "Cell"):
        rect = state.get(field)
        require(isinstance(rect, list) and len(rect) == 4 and all(type(v) is int for v in rect), "Invalid source rectangle")
        require(0 < rect[2] <= 4096 and 0 < rect[3] <= 4096, "Source rectangle exceeds its bound")
    for field in ("EditorUtf16", "FirstNameUtf16"):
        value = state.get(field)
        require(value is None or (isinstance(value, list) and len(value) <= 4096
                and all(type(v) is int and 0 <= v <= 65535 for v in value)), "Invalid source UTF-16 snapshot")
    for field in ("FormFocused", "EditorFocused", "InEdit"):
        require(type(state.get(field)) is bool, "Invalid source editing/focus state")
    require(type(state.get("DeviceDpi")) is int and state["DeviceDpi"] > 0, "Missing source DPI")
    for field in ("CurrentColumn", "CurrentRow", "SelectionStart", "SelectionLength"):
        require(type(state.get(field)) is int, "Invalid source selection state")
    return state


def read_snapshot(log, pid):
    require(log.stat().st_size <= MAX_LOG_BYTES, "Application log exceeded its bound")
    lines = log.read_bytes().split(b"\n")[:-1]
    complete = [line for line in lines if line.startswith(b"GRID_EDITING ")]
    require(len(complete) <= 600, "Observer emitted too many snapshots")
    if not complete:
        raise FileNotFoundError("No original grid snapshot yet")
    return validate_snapshot(json.loads(complete[-1][len(b"GRID_EDITING "):].decode("utf-8")), pid)


def stable(state):
    return {key: value for key, value in state.items() if key != "Sequence"}


def layout_contract(state):
    client = state["Client"]
    def local(field):
        x, y, width, height = state[field]
        return [x - client[0], y - client[1], width, height]
    return dict(clientSize=client[2:], grid=local("GridClient"), cell=local("Cell"),
                metrics={key: state[key] for key in ("DeviceDpi", "FontName", "FontSize", "FontHeight",
                         "AutoScaleX", "AutoScaleY", "TemplateHeight", "ActualRowHeight", "HeaderHeight",
                         "ColumnWidths", "ColumnHeaders")})


class Session:
    def __init__(self, desktop, child, evidence, started, dpi=None):
        self.desktop, self.child, self.evidence = desktop, child, evidence
        self.started, self.deadline = started, started + 60
        self.dpi, self.state, self.phase = dpi, None, "startup"
        self.sequence = 0
        self.image_bytes = 0
        self.last_observed = None

    def native(self, state):
        windows = self.desktop.windows(self.child.pid)
        mains = [window for window in windows if window["title"] == TITLE]
        require(len(mains) == 1, "Expected one real original-sample window")
        x, y, width, height = state["Client"]
        require(mains[0]["client"] == dict(x=x, y=y, width=width, height=height), "Source/native client geometry mismatch")
        require(self.dpi is None or state["DeviceDpi"] == self.dpi, "Reference/portable DPI mismatch")
        return mains

    def wait(self, predicate, startup=False):
        limit = min(self.deadline, self.started + 20 if startup else time.monotonic() + 3)
        previous = None
        requested_activation = False
        while time.monotonic() < limit:
            require(self.child.poll() is None, f"Original sample exited during {self.phase}: {self.child.returncode}")
            try:
                state = read_snapshot(self.evidence / "stdout.log", self.child.pid)
            except FileNotFoundError:
                time.sleep(0.05)
                continue
            mains = self.native(state)
            self.last_observed = state
            if startup and not requested_activation:
                self.desktop.activate(mains[0])
                requested_activation = True
            if (state["Sequence"] > self.sequence and state["FormFocused"] and predicate(state)
                    and previous is not None and state["Sequence"] > previous["Sequence"]
                    and stable(state) == stable(previous)):
                self.desktop.foreground(self.child.pid)
                self.state, self.sequence = state, state["Sequence"]
                if self.dpi is None:
                    self.dpi = state["DeviceDpi"]
                return
            previous = state if predicate(state) and state["FormFocused"] else None
            time.sleep(0.05)
        raise TimeoutError(f"Original deadline expired during {self.phase}")

    def ready(self):
        require(time.monotonic() < self.deadline, "No input after the original 60-second deadline")
        require(self.child.poll() is None and self.state is not None, "No input for an unavailable application")
        self.native(self.state)
        self.desktop.foreground(self.child.pid)

    def key(self, value):
        self.ready()
        self.desktop.key(self.child.pid, value)
        self.record(dict(kind="key", virtualKey=value))

    def cell(self):
        self.ready()
        x, y, width, height = self.state["Cell"]
        self.desktop.pointer(self.child.pid, dict(x=x, y=y, width=width, height=height), "left")
        self.record(dict(kind="cell-click", rectangle=self.state["Cell"]))

    def record(self, action):
        action.update(phase=self.phase, processId=self.child.pid, elapsedSeconds=time.monotonic() - self.started)
        with (self.evidence / "input.jsonl").open("a", encoding="utf-8") as output:
            output.write(json.dumps(action) + "\n")

    def capture(self, name):
        self.ready()
        mains = self.native(self.state)
        path = self.evidence / (name + ".bmp")
        require(self.image_bytes + 16 * 1024 * 1024 + 54 <= 128 * 1024 * 1024, "Screenshot budget exhausted")
        image = self.desktop.screenshot(self.child.pid, mains, path)
        self.image_bytes += path.stat().st_size
        require(time.monotonic() < self.deadline, "Capture exceeded the original deadline")
        require(self.child.poll() is None, "Application exited during capture")
        require(self.native(self.state) == mains, "Native geometry changed during capture")
        latest = read_snapshot(self.evidence / "stdout.log", self.child.pid)
        require(stable(latest) == stable(self.state), "Source state changed during capture")
        save(self.evidence / (name + ".json"), dict(snapshot=self.state, nativeWindows=mains, screenshot=image, qualified=False))


def editing(state, text):
    return (state["InEdit"] and state["EditorFocused"] and state["CurrentColumn"] == 0
            and state["CurrentRow"] == 0 and state["EditorUtf16"] == units(text))


def committed(state, text):
    return not state["InEdit"] and state["FirstNameUtf16"] == units(text)


def scenario(session, reference=None):
    session.wait(lambda state: state["FirstNameUtf16"] in (None, []) and not state["InEdit"], startup=True)
    layout = layout_contract(session.state)
    require(reference is None or layout == reference, "Original Microsoft/portable source layout differs")
    session.capture("ready")
    session.phase = "begin-edit"
    session.cell()
    session.key(0x71)  # F2, handled by the original grid.
    session.wait(lambda state: editing(state, ""))
    session.phase = "type-name"
    for key in (0x41, 0x4C, 0x49, 0x43, 0x45):  # Real unmodified physical keys; never clipboard/managed dispatch.
        session.key(key)
    session.wait(lambda state: editing(state, "alice") and state["SelectionStart"] == 5 and state["SelectionLength"] == 0)
    session.capture("editing")
    session.phase = "enter-commit"
    session.key(0x0D)
    session.wait(lambda state: committed(state, "alice"))
    session.capture("committed")
    session.phase = "reopen-edit"
    session.cell()
    session.key(0x71)
    session.wait(lambda state: editing(state, "alice"))
    session.key(0x23)  # End
    session.wait(lambda state: editing(state, "alice") and state["SelectionStart"] == 5 and state["SelectionLength"] == 0)
    session.key(0x58)
    session.wait(lambda state: editing(state, "alicex"))
    session.phase = "escape-cancel"
    session.key(0x1B)
    session.wait(lambda state: committed(state, "alice"))
    session.capture("cancelled")
    return layout


def run_mode(root, build, mode, evidence, desktop, reference=None):
    compiled = build["builds"][mode]
    require(compiled["exitCode"] == 0, "Consumer did not build successfully")
    app = Path(compiled["app"]).resolve(strict=True)
    require(app.is_relative_to(root) and app.name == "CSWinFormDataGridView.exe", "Application escaped prepared source root")
    hashes = verify_files(app.parent, compiled["outputs"])
    sdk = Path(build["sdk"]).resolve(strict=True)
    require(digest(sdk) == build["sdkSha256"], "SDK host differs from build receipt")
    environment = {key: value for key, value in os.environ.items()
                   if not re.search(r"TOKEN|SECRET|PASSWORD|LICENSE|CREDENTIAL|API_KEY|ACCESS_KEY", key, re.I)}
    environment.pop("DOTNET_STARTUP_HOOKS", None)
    environment.update(DOTNET_ROOT=str(sdk.parent), DOTNET_ROOT_ARM64=str(sdk.parent),
                       DOTNET_CLI_TELEMETRY_OPTOUT="1", LIBREWINFORMS_GRID_OBSERVER="1")
    evidence.mkdir()
    receipt = dict(mode=mode, qualified=False, status="incomplete", sourceHead=build["sourceHead"],
                   observerSha256=build["observerSha256"], applicationDeadlineSeconds=60, startupDeadlineSeconds=20)
    child = session = None
    try:
        with (evidence / "stdout.log").open("xb") as stdout, (evidence / "stderr.log").open("xb") as stderr:
            started = time.monotonic()
            child = subprocess.Popen([str(app)], cwd=app.parent, env=environment, stdout=stdout, stderr=stderr)
            receipt["processId"] = child.pid
            dpi = None if reference is None else reference["metrics"]["DeviceDpi"]
            session = Session(desktop, child, evidence, started, dpi)
            layout = scenario(session, reference)
            for name in ("FormsAssembly", "DrawingAssembly"):
                loaded = Path(session.state[name]).resolve(strict=True)
                require(loaded.parent == app.parent if mode == "Portable" else
                        loaded.is_relative_to(sdk.parent / "shared/Microsoft.WindowsDesktop.App"),
                        "Unexpected loaded Forms/Drawing runtime identity")
            receipt.update(status="input-phases-passed", elapsedSeconds=time.monotonic() - started, dpi=session.dpi)
            return layout
    except BaseException as error:
        receipt.update(status="failed", error=type(error).__name__ + ": " + str(error), phase=session.phase if session else "launch")
        receipt["lastObserved"] = session.last_observed if session else None
        raise
    finally:
        if child is not None:
            if child.poll() is None:
                child.terminate()
            try:
                child.wait(timeout=2)
            except subprocess.TimeoutExpired:
                child.kill()
                child.wait(timeout=2)
            receipt["cleanupExitCode"] = child.returncode
        try:
            require(verify_files(app.parent, compiled["outputs"]) == hashes, "Application payload changed during the run")
            receipt["payloadUnchanged"] = True
        finally:
            save(evidence / "receipt.json", receipt)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--prepared-root", required=True, type=Path)
    parser.add_argument("--evidence", required=True, type=Path)
    args = parser.parse_args()
    require(sys.platform == "win32", "Use Windows for the paired Microsoft/portable original-grid check")
    root = args.prepared_root.resolve(strict=True)
    build = json.loads((root / "build-receipt.json").read_text())
    preparation = json.loads((root / "preparation.json").read_text())
    require("error" not in build and preparation["producerSuccess"] is True, "A complete successful producer/build is required")
    require(preparation["sourceCommit"] == SOURCE_COMMIT and build["sourceHead"] == preparation["sourceHead"], "Original source/producer mismatch")
    observer = digest(ENG / "OriginalGrid/OriginalGridObserver.cs")
    require(observer == build["observerSha256"] == preparation["observerSha256"] == digest(root / "OriginalGridObserver.cs"),
            "Prepared/compiled passive observer differs from the admitted source")
    for mode in ("Microsoft", "Portable"):
        for entry in preparation["files"][mode]:
            relative = Path(entry["path"])
            require(not relative.is_absolute() and ".." not in relative.parts, "Unsafe source manifest path")
            require(digest(root / mode / relative) == entry["sha256"], "Original sample source changed after preparation")
    args.evidence.mkdir()
    desktop = DESKTOP.WindowsDesktop()
    reference = run_mode(root, build, "Microsoft", args.evidence / "Microsoft", desktop)
    run_mode(root, build, "Portable", args.evidence / "Portable", desktop, reference)
    print("Both original-source input protocols passed; native pixels/other platforms and remaining editors stay separate.")


if __name__ == "__main__":
    main()
