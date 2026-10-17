"""Explicitly approved hosted startup only; original grid scenarios stay unchanged."""
import importlib.util
import hashlib
import json
import os
from pathlib import Path
import time

HERE = Path(__file__).resolve().parent
ENVIRONMENT = dict(GITHUB_ACTIONS="true", RUNNER_ENVIRONMENT="github-hosted", RUNNER_OS="Windows",
                   GITHUB_REPOSITORY="wieslawsoltes/LibreWinForms")


def require(value, message):
    if not value:
        raise RuntimeError(message)


def load(name):
    spec = importlib.util.spec_from_file_location("grid_startup_" + name.replace("-", "_"), HERE / (name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def unchanged_source(state):
    # Activation may change Form focus, but cannot change editor/content/layout.
    return {key: value for key, value in state.items() if key not in ("Sequence", "FormFocused")}


def activate(session, state, owner, read_snapshot, *, authorized=False, environment=None, clock=time.monotonic, sleep=time.sleep):
    environment = os.environ if environment is None else environment
    require(authorized and session.hosted_startup, "Hosted startup input requires explicit approval")
    require(all(environment.get(k) == v for k, v in ENVIRONMENT.items()), "Disposable Forms hosted Windows only")
    require(session.phase == "startup" and not session.startup_attempted and session.state is None,
            "Hosted activation is first-startup-only and cannot be retried")
    limit = min(session.deadline, session.started + 20)
    require(clock() < limit and session.child.poll() is None, "Original startup deadline or process ended")
    require(session.native(state) == [owner], "Original native owner changed before startup")
    session.startup_attempted = True
    record = dict(qualified=False, authorized=True, startupDeadline=limit,
                  applicationDeadline=session.deadline, maximumShellKeys=2 if session.allow_shell else 0,
                  maximumCaptionClicks=1, shellActions=0, captionClicks=0,
                  sourceBefore=state, shell={}, caption={}, helpers={})
    modules = {}
    try:
        for name in ("prepare-desktop", "shell-escape", "caption"):
            modules[name] = load(name)
            record["helpers"][name] = hashlib.sha256((HERE / (name + ".py")).read_bytes()).hexdigest()
        if session.allow_shell:
            def claim(kind, **details):
                require(kind == "hosted-shell-escape" and record["shellActions"] < 2 and clock() < limit,
                        "Shell startup action budget exceeded")
                record["shellActions"] += 1
                session.record(dict(kind=kind, **details))
            modules["shell-escape"].dismiss(modules["prepare-desktop"].Desktop(), session.desktop, claim,
                                            limit, record["shell"], environment, authorized=True)
        require(clock() < limit and session.child.poll() is None, "Original startup budget ended before caption")
        require(session.native(state) == [owner], "Original native owner changed before caption")
        try:
            modules["caption"].Desktop(adopt_physical_coordinates=True).activate_ci_window(
                owner["hwnd"], session.child.pid, limit, record["caption"], environment)
        finally:
            record["captionClicks"] = record["caption"].get("clickCount", 0)
            if record["captionClicks"]:
                session.record(dict(kind="hosted-caption", nativeProof=record["caption"]))
        require(record["captionClicks"] <= 1, "Caption startup action budget exceeded")
        # Advancing observations must still contain the original source state.
        # No setter or managed event is used to repair a leaked key or layout.
        previous = None
        sequence = read_snapshot(session.evidence / "stdout.log", session.child.pid)["Sequence"]
        until = min(limit, record["shell"].get("setupDeadline", limit)) if record["shellActions"] else limit
        while clock() < until:
            require(session.child.poll() is None, "Original application exited during startup verification")
            latest = read_snapshot(session.evidence / "stdout.log", session.child.pid)
            require(unchanged_source(latest) == unchanged_source(state), "Startup input changed original source state")
            require(session.native(latest) == [owner], "Original native geometry changed during startup input")
            if (previous is not None and previous["Sequence"] > sequence
                    and latest["Sequence"] > previous["Sequence"] and latest["FormFocused"]):
                session.desktop.foreground(session.child.pid)
                record["sourceAfter"] = latest
                record["completed"] = True
                return
            previous = latest
            sleep(.025)
        raise TimeoutError("Original startup bound expired verifying input")
    except BaseException as error:
        record["error"] = type(error).__name__ + ": " + str(error)
        try:
            native = modules.get("prepare-desktop")
            if native is not None:
                native = native.Desktop()
                hwnd = native.foreground()
                record["foregroundAtFailure"] = native.snapshot(hwnd) if hwnd else None
        except BaseException as observation_error:
            record["failureObservationError"] = str(observation_error)
        raise
    finally:
        record["elapsedSeconds"] = clock() - session.started
        with (session.evidence / "startup-input.json").open("x", encoding="utf-8") as output:
            json.dump(record, output, indent=2)
