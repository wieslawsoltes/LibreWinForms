#!/usr/bin/env python3
"""Separate real-input modal workload; no managed UI calls or geometry repair."""

import hashlib
import importlib.util
import json
from pathlib import Path
import re
import stat


SPEC = importlib.util.spec_from_file_location("popup_modal_shared", Path(__file__).with_name("librewinforms-popup-desktop.py"))
SHARED = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SHARED)
require = SHARED.require
LIMIT = 256 * 1024


def regular(path, *, directory=False):
    value = path.lstat()
    require(not stat.S_ISLNK(value.st_mode) and not (getattr(value, "st_file_attributes", 0) & 0x400),
            "Modal evidence cannot be a symbolic link/reparse point")
    require(stat.S_ISDIR(value.st_mode) if directory else stat.S_ISREG(value.st_mode), "Modal evidence has wrong file type")
    require(path.is_absolute() and path.resolve(strict=True) == path, "Modal evidence path is not canonical and owned")
    return value


def owned_child(owner, value):
    regular(owner, directory=True)
    require(isinstance(value, str), "Missing modal evidence path")
    child = Path(value)
    require(child.parent == owner and re.fullmatch(r"modal-[0-9a-f]{32}", child.name) is not None,
            "Modal evidence must be an exact GUID-named direct child")
    regular(child, directory=True)
    return child


def parse_json(raw):
    def unique(pairs):
        value = {}
        for key, item in pairs:
            require(key not in value, "Duplicate modal evidence member")
            value[key] = item
        return value
    def finite_only(value):
        raise RuntimeError(f"Nonfinite modal evidence: {value}")
    return json.loads(raw, object_pairs_hook=unique, parse_constant=finite_only)


def observation(directory, pid, title):
    regular(directory, directory=True)
    paths = list(directory.glob("snapshot-????????.json"))
    require(len(paths) <= 650, "Observer snapshot budget exceeded")
    if not paths:
        raise FileNotFoundError("No modal observer snapshot published")
    path = max(paths)
    require(re.fullmatch(r"snapshot-[0-9]{8}\.json", path.name) is not None, "Invalid modal snapshot name")
    require(regular(path).st_size <= LIMIT, "Modal snapshot exceeds existing receipt budget")
    raw = path.read_bytes()
    require(len(raw) <= LIMIT, "Modal snapshot exceeds existing receipt budget")
    regular(path)
    state = parse_json(raw)
    require(type(state.get("schema")) is int and state["schema"] == 1 and type(state.get("pid")) is int
            and state["pid"] == pid and state.get("title") == title, "Modal snapshot PID/title/schema mismatch")
    require(type(state.get("sequence")) is int and 0 < state["sequence"] <= 650
            and path.name == f"snapshot-{state['sequence']:08d}.json", "Modal snapshot sequence/file mismatch")
    return state, dict(path=str(path), sha256=hashlib.sha256(raw).hexdigest())


def events(directory):
    path = directory / "events.jsonl"
    require(regular(path).st_size <= LIMIT, "Modal event journal exceeds receipt budget")
    raw = path.read_bytes()
    require(len(raw) <= LIMIT, "Modal event journal exceeds receipt budget")
    regular(path)
    # Only complete immutable append records are evidence; a writer-owned tail
    # is never parsed as success or retried with altered event contents.
    return [parse_json(line) for line in raw.split(b"\n")[:-1] if line]


def event(directory, name):
    values = [value for value in events(directory) if value.get("name") == name]
    require(len(values) == 1, f"Expected one actual {name} event")
    detail = values[0].get("detail")
    return parse_json(detail) if detail is not None else None


def owner_input_state(state):
    ignored = {"form-activated", "form-deactivated", "editor-focus-gained", "editor-focus-lost"}
    return dict(text=state["editor"]["text"], combo=state["combo"]["selectedIndex"],
                droppedDown=state["combo"]["droppedDown"],
                popups={name: value["visible"] for name, value in state["popups"].items()},
                counts={name: count for name, count in state["counts"].items()
                        if not name.endswith("-paint") and not name.startswith("modal-") and name not in ignored})


class ModalSession(SHARED.Session):
    def __init__(self, desktop, process, directory, run):
        super().__init__(desktop, process, directory, run)
        self.owner_directory = directory / "app"
        self.owner_run = run
        self.child_directory = None
        self.child_active = False
        self.owner_before = None
        self.owner_native_identity = None
        self.owner_guard_rectangle = None
        self.last_owner = self.last_child = None
        self.desktop.select_observations([(self.owner_directory, f"PopupInteractionApp [{run}]")])

    def read_state(self):
        owner, owner_receipt = observation(self.owner_directory, self.process.pid, f"PopupInteractionApp [{self.owner_run}]")
        self.last_owner = dict(snapshot=owner, **owner_receipt)
        if not self.child_active:
            return owner
        require(owned_child(self.owner_directory, owner["modal"]["evidenceDirectory"]) == self.child_directory,
                "Modal source generation changed")
        require(owner["modal"]["dialogVisible"] is True
                and owner_input_state(owner) == self.owner_before, "Disabled modal owner changed input state")
        require(all(owner["counts"].get(name) == 1 for name in
                    ("modal-button-pointer", "modal-button-click", "modal-open-request", "modal-shown")),
                "Disabled modal owner received another modal button action")
        child, child_receipt = observation(self.child_directory, self.process.pid, f"PopupInteractionApp [{self.owner_run} modal]")
        self.last_child = dict(snapshot=child, **child_receipt)
        return child

    def adopt_child(self):
        shown = event(self.owner_directory, "modal-shown")
        requested = event(self.owner_directory, "modal-open-request")
        child = owned_child(self.owner_directory, shown.get("evidenceDirectory"))
        require(self.child_directory is None and child == owned_child(self.owner_directory, self.state["modal"]["evidenceDirectory"]),
                "A modal action must own one new evidence generation")
        require(shown.get("ownerMatches") is True and requested.get("ownerEnabled") is True and type(shown.get("ownerHandle")) is int
                and shown["ownerHandle"] != 0 and shown["ownerHandle"] == requested.get("ownerHandle")
                and type(shown.get("dialogHandle")) is int and shown["dialogHandle"] != 0
                and shown["dialogHandle"] != shown["ownerHandle"], "Original modal owner/child identity or disable transition differs")
        require(all(self.state["counts"].get(name) == 1 for name in
                    ("modal-button-pointer", "modal-button-click", "modal-open-request", "modal-shown")),
                "Modal opening did not originate in one observed owner button action")
        self.child_directory, self.child_active = child, True
        self.run = self.owner_run + " modal"
        self.desktop.select_observations([(self.owner_directory, f"PopupInteractionApp [{self.owner_run}]"),
                                         (child, f"PopupInteractionApp [{self.run}]")])
        self.requested = requested
        self.owner_handle = shown["ownerHandle"]

    def assert_input_enabled(self, windows, state, expected):
        matches = [window for window in windows if window["title"] == state["title"]]
        require(len(matches) == 1 and self.desktop.window_input_enabled(matches[0], state) is expected,
                "Actual window modal input-enabled policy differs")

    def wait(self, predicate):
        windows = super().wait(predicate)
        if self.child_active:
            self.assert_input_enabled(windows, self.last_owner["snapshot"], False)
            self.assert_input_enabled(windows, self.state, True)
        return windows

    def return_to_owner(self):
        self.child_active = False
        self.run = self.owner_run
        self.desktop.select_observations([(self.owner_directory, f"PopupInteractionApp [{self.run}]")])

    def input_ready(self):
        super().input_ready()
        if self.child_active:
            self.read_state()  # Recheck this exact disabled owner before every physical action.
            self.assert_input_enabled(self.desktop.windows(self.process.pid), self.last_owner["snapshot"], False)

    def record_input(self, action):
        action.update(observerDirectory=str(self.child_directory if self.child_active else self.owner_directory),
                      observerTitle=f"PopupInteractionApp [{self.run}]",
                      ownerObservation=self.last_owner,
                      childObservation=self.last_child if self.child_active else None)
        require(len(json.dumps(action).encode()) <= LIMIT, "Modal input observation exceeds receipt budget")
        super().record_input(action)

    def owner_guard_input(self, *, blocked):
        self.input_ready()
        require(self.child_active is blocked, "Owner input phase does not match source modal lifetime")
        owner = self.last_owner["snapshot"]
        guard = owner["modal"].get("guard")
        require(isinstance(guard, dict) and guard.get("name") == "owner-input-guard"
                and guard.get("client") == self.owner_guard_rectangle, "Original source guard identity/geometry changed")
        windows = self.desktop.windows(self.process.pid)
        matches = [w for w in windows if w["title"] == owner["title"]
                   and self.desktop.window_identity(w) == self.owner_native_identity]
        require(len(matches) == 1 and matches[0]["client"] == owner["form"]["client"],
                "Exact owner native client identity is unavailable")
        self.assert_input_enabled(windows, owner, not blocked)
        require(all(owner["counts"].get(name, 0) == 0 for name in ("owner-guard-down", "owner-guard-up", "owner-guard-click")),
                "Owner guard already received input before its positive control")
        sequence = owner["sequence"]
        proof = self.desktop.blocked_owner_pointer(self.process.pid, matches[0], guard["client"])
        self.record_input(dict(kind="owner-guard-pointer", blocked=blocked, rectangle=guard["client"], button="left", nativeProof=proof))
        return sequence

    def capture(self, phase, predicate):
        super().capture(phase, predicate)
        evidence = dict(owner=self.last_owner, child=self.last_child if self.child_active else None, qualified=False)
        payload = json.dumps(evidence, indent=2).encode("utf-8")
        require(len(payload) <= LIMIT, "Modal phase observation exceeds receipt budget")
        with (self.directory / f"{phase}.owner.json").open("xb") as stream:
            stream.write(payload)


def scenario(session):
    closed = lambda s: not any(p["visible"] for p in s["popups"].values())
    opened = lambda name: lambda s: s["popups"][name]["visible"] and s["counts"].get(name + "-paint", 0) > 0
    windows = session.wait(lambda s: s["form"]["visible"] and s["counts"].get("form-paint", 0) > 0)
    session.desktop.activate(next(w for w in windows if w["title"] == session.state["title"]))
    session.wait(lambda s: s["form"]["active"])
    session.point(session.state["editor"]["client"])
    session.capture("m01-owner", lambda s: s["form"]["active"] and s["modal"]["enabled"] is True
                    and s["modal"]["buttonName"] == "open-modal-dialog" and closed(s))
    owner_window = next(w for w in session.desktop.windows(session.process.pid) if w["title"] == session.state["title"])
    session.owner_native_identity = session.desktop.window_identity(owner_window)
    session.owner_guard_rectangle = session.state["modal"]["guard"]["client"]
    session.owner_before = owner_input_state(session.state)
    session.assert_input_enabled(session.desktop.windows(session.process.pid), session.state, True)
    session.point(session.state["modal"]["button"], "left")
    session.wait(lambda s: s["counts"].get("modal-shown") == 1 and s["modal"]["dialogVisible"] is True)
    session.assert_input_enabled(session.desktop.windows(session.process.pid), session.state, False)
    session.adopt_child()
    session.capture("m02-dialog", lambda s: s["form"]["active"] and s["modal"]["enabled"] is True
                    and s["modal"]["buttonName"] == "close-modal-dialog" and s["modal"]["ownerHandle"] == session.owner_handle)
    blocked_sequence = session.owner_guard_input(blocked=True)
    session.capture("m02-owner-blocked", lambda s: s["form"]["active"] and closed(s)
                    and session.last_owner["snapshot"]["sequence"] > blocked_sequence
                    and owner_input_state(session.last_owner["snapshot"]) == session.owner_before)
    session.point(session.state["contextTarget"], "right")
    session.capture("m03-context", opened("context"))
    session.point(session.state["items"]["context-more"]["client"])
    session.capture("m04-context-child", opened("context-child"))
    session.key(0x1B); session.key(0x1B)
    session.capture("m05-context-escape", lambda s: closed(s) and s["counts"].get("context-command", 0) == 0)
    session.point(session.state["contextTarget"], "right")
    session.wait(opened("context"))
    session.point(session.state["items"]["context-more"]["client"])
    session.wait(opened("context-child"))
    session.point(session.state["items"]["context-command"]["client"], "left")
    session.capture("m06-context-command", lambda s: closed(s) and s["counts"].get("context-command") == 1)
    session.point(session.state["items"]["menu-file"]["client"], "left")
    session.wait(opened("menu"))
    session.point(session.state["items"]["menu-more"]["client"])
    session.capture("m07-menu-child", opened("menu-child"))
    session.key(0x1B); session.key(0x1B)
    session.capture("m08-menu-escape", lambda s: closed(s) and s["counts"].get("menu-command", 0) == 0)
    session.point(session.state["items"]["menu-file"]["client"], "left")
    session.wait(opened("menu"))
    session.point(session.state["items"]["menu-more"]["client"])
    session.wait(opened("menu-child"))
    session.point(session.state["items"]["menu-command"]["client"], "left")
    session.capture("m09-menu-command", lambda s: closed(s) and s["counts"].get("menu-command") == 1)
    session.point(session.state["combo"]["client"], "left")
    session.capture("m10-combo", lambda s: s["combo"]["droppedDown"] and s["counts"].get("combo-opened") == 1)
    session.key(0x1B)
    session.capture("m11-combo-escape", lambda s: not s["combo"]["droppedDown"] and s["combo"]["selectedIndex"] == 0
                    and s["counts"].get("combo-committed", 0) == 0)
    session.point(session.state["combo"]["client"], "left")
    session.wait(lambda s: s["combo"]["droppedDown"])
    session.key(0x28); session.key(0x0D)
    session.capture("m12-combo-committed", lambda s: not s["combo"]["droppedDown"] and s["combo"]["selectedIndex"] == 1
                    and s["counts"].get("combo-committed") == 1)
    existing = {session.desktop.window_identity(w) for w in session.desktop.windows(session.process.pid)}
    tooltip_count = session.state["counts"].get("tooltip-popup", 0)
    session.point(session.state["tooltipTarget"])
    session.capture("m13-tooltip", lambda s: s["counts"].get("tooltip-popup", 0) > tooltip_count and
                    any(session.desktop.window_identity(w) not in existing for w in session.desktop.windows(session.process.pid)))
    session.point(session.state["modal"]["button"], "left")
    session.return_to_owner()
    session.capture("m14-owner-return", lambda s: s["counts"].get("modal-return") == 1 and s["counts"].get("modal-closed") == 1
                    and s["modal"]["enabled"] is True and not s["modal"]["dialogVisible"] and s["form"]["active"]
                    and s["modal"]["activeControl"] == session.requested["activeControl"])
    returned = event(session.owner_directory, "modal-return")
    session.assert_input_enabled(session.desktop.windows(session.process.pid), session.state, True)
    require(returned.get("result") == "OK" and returned.get("ownerEnabled") is True and returned.get("dialogVisible") is False
            and returned.get("activeControl") == session.requested["activeControl"]
            and returned.get("editorFocused") == session.requested["editorFocused"], "Original modal return/focus contract differs")
    require(owner_input_state(session.state) == session.owner_before, "Modal child changed owner input state")
    for name in ("modal-button-pointer", "modal-button-click"):
        event(session.owner_directory, name)
    require(not any(w["title"] == f"PopupInteractionApp [{session.owner_run} modal]"
                    for w in session.desktop.windows(session.process.pid)), "Closed modal child remains natively visible")
    for name in ("modal-button-pointer", "modal-button-click", "form-closed"):
        event(session.child_directory, name)
    before = session.state["counts"].get("editor-pointer", 0)
    session.point(session.state["editor"]["client"], "left")
    session.capture("m15-owner-enabled", lambda s: s["editor"]["focused"] and s["counts"].get("editor-pointer") == before + 1
                    and s["modal"]["enabled"] is True)
    enabled_sequence = session.owner_guard_input(blocked=False)
    session.capture("m16-owner-guard", lambda s: s["sequence"] > enabled_sequence and s["form"]["active"]
                    and s["modal"]["enabled"] is True and all(s["counts"].get(name) == 1
                    for name in ("owner-guard-down", "owner-guard-up", "owner-guard-click")))
    for name in ("owner-guard-down", "owner-guard-up", "owner-guard-click"):
        event(session.owner_directory, name)
