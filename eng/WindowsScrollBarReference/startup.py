"""Prepared extension; not installed or executed before scoped approval."""
import os
import time

HOST = dict(GITHUB_ACTIONS='true', RUNNER_ENVIRONMENT='github-hosted',
            RUNNER_OS='Windows', GITHUB_REPOSITORY='wieslawsoltes/LibreWinForms')


def require(condition, message):
    if not condition: raise RuntimeError(message)


def activate(native, observer, dismiss, caption_factory, deadline, record, *,
             authorized=False, environment=None, clock=time.monotonic):
    environment = os.environ if environment is None else environment
    require(authorized and all(environment.get(k) == v for k, v in HOST.items()),
            'Explicit hosted startup extension required')
    require(not record and clock() < deadline, 'Repeated or expired startup extension')
    # The existing 15-second input interval begins before even shell input.
    input_start = clock()
    input_deadline = min(deadline, input_start + 15)
    owner = native.owner
    require(native.owner_now() == owner, 'Original owner changed before startup')
    record.update(inputStarted=input_start, inputDeadline=input_deadline,
                  shell={}, shellActions=0, activationRequested=False, caption={})

    def claim(kind, **metadata):
        require(kind == 'hosted-shell-escape' and record['shellActions'] < 2
                and clock() < input_deadline, 'Startup shell count/deadline exceeded')
        record['shellActions'] += 1

    try:
        dismiss(observer, native.desktop, claim, input_deadline, record['shell'],
                environment, authorized=True)
        require(clock() < input_deadline and native.owner_now() == owner,
                'Original owner changed during startup')
        record['activationRequested'] = True
        native.desktop.activate(owner)  # The one already-approved owned activation.
        require(native.owner_now() == owner, 'Original owner changed during activation')
        caption = caption_factory(adopt_physical_coordinates=True)
        caption.activate_ci_window(owner['hwnd'], native.child.pid, input_deadline,
                                   record['caption'], environment)
        require(clock() < input_deadline and native.owner_now() == owner,
                'Original owner changed during caption activation')
        native.desktop.foreground(native.child.pid)
        record['completed'] = True
        return input_start
    except BaseException as error:
        record['error'] = type(error).__name__ + ': ' + str(error)
        raise
    finally:
        try:
            record['foregroundAfter'] = observer.foreground()
        except BaseException as error:
            record['observationError'] = str(error)
            if 'error' not in record: raise
