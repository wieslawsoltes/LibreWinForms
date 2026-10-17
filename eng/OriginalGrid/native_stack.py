"""One read-only inherited handle, bounded MiniDumpNormal; never PID discovery."""
import ctypes
from ctypes import wintypes
from pathlib import Path
import struct
import subprocess
import time


def launch_owned(child, tool, destination, launch=subprocess.Popen, expected_pid=None):
    if child.poll() is not None:
        raise RuntimeError('Owned child already exited')
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.GetCurrentProcess.restype = wintypes.HANDLE
    kernel.DuplicateHandle.argtypes = [wintypes.HANDLE, wintypes.HANDLE, wintypes.HANDLE,
        ctypes.POINTER(wintypes.HANDLE), wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.DuplicateHandle.restype = wintypes.BOOL
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    current, duplicate = kernel.GetCurrentProcess(), wintypes.HANDLE()
    # Popen owns this handle for the complete call; no new PID lookup occurs.
    if not kernel.DuplicateHandle(current, int(child._handle), current, ctypes.byref(duplicate),
                                  0x100000 | 0x0400 | 0x0010, True, 0):
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        startup = subprocess.STARTUPINFO()
        startup.lpAttributeList = {'handle_list': [duplicate.value]}
        return launch([str(tool), str(duplicate.value), str(child.pid if expected_pid is None else expected_pid),
                       str(destination)], close_fds=True, startupinfo=startup,
                      stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    finally:
        kernel.CloseHandle(duplicate)


def validate_dump(path, pid):
    size = path.stat().st_size
    if not 32 <= size <= 32 * 1024 * 1024:
        raise ValueError('Normal dump outside size bound')
    with path.open('rb') as stream:
        header = stream.read(32)
        signature, version, count, table, _, _, flags = struct.unpack('<IIIIIIQ', header)
        if signature != 0x504D444D or version & 0xFFFF != 0xA793 or flags not in (0, 0x200000) or not 1 <= count <= 64 or table < 32 or table + count * 12 > size:
            raise ValueError('Invalid MiniDumpNormal header')
        stream.seek(table)
        entries = [struct.unpack('<III', stream.read(12)) for _ in range(count)]
        if len({kind for kind, _, _ in entries}) != len(entries):
            raise ValueError('Duplicate dump stream')
        machine = process = None
        for kind, length, offset in entries:
            if offset < table + count * 12 or offset + length > size or kind in (6, 9):
                raise ValueError('Dump stream outside file')
            if kind == 7:
                if length < 56: raise ValueError('Short system stream')
                stream.seek(offset); machine = struct.unpack('<H', stream.read(2))[0]
            if kind == 15:
                if length < 24: raise ValueError('Short process stream')
                stream.seek(offset); info_size, valid, process = struct.unpack('<III', stream.read(12))
                if not 24 <= info_size <= length: raise ValueError('Invalid process stream')
                if not valid & 1: raise ValueError('Missing process identity')
        if machine not in (9, 12) or process != pid or (flags == 0x200000 and machine != 9):
            raise ValueError('Dump is not the owned x64/ARM64 process')
    return dict(bytes=size, architecture=machine, processId=process, flags=flags)


def collect(child, tool, output, stop, launch, record):
    if stop.wait(12) or child.poll() is not None:
        record['skipped'] = 'owned child already finished'
        return
    record['pid'] = child.pid
    process = None
    started = time.monotonic()
    dump = output / 'native-stack.dmp'
    try:
        process = launch_owned(child, tool, dump, launch)
        while process.poll() is None:
            if (stop.is_set() or child.poll() is not None or time.monotonic() - started >= 5
                    or (dump.exists() and dump.stat().st_size > 32 * 1024 * 1024)):
                record['boundedStop'] = True
                process.kill()
                break
            stop.wait(.025)
        stdout, stderr = process.communicate(timeout=1)
        record['exitCode'] = process.returncode
        (output / 'native-stack-tool.txt').write_bytes(stdout + stderr)
        if process.returncode == 0:
            record['dump'] = validate_dump(dump, child.pid)
    except BaseException as error:
        record['error'] = repr(error)
    finally:
        if process is not None and process.poll() is None:
            process.kill(); process.wait(timeout=1)
        record['seconds'] = time.monotonic() - started
