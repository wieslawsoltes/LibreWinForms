"""Read-only admission for a group-zero X11 diagnostic keyboard route.

This does not inject input, change locks/maps, or qualify application behavior.
Callers retain their foreground, held-key, mapping, deadline and cleanup checks.
"""
import ctypes
import ctypes.util
import json
from pathlib import Path
import subprocess
import sys
import time

FIELDS = ('mods', 'baseMods', 'latchedMods', 'lockedMods', 'group', 'lockedGroup',
          'baseGroup', 'latchedGroup', 'compatState', 'grabMods', 'compatGrabMods',
          'lookupMods', 'compatLookupMods', 'ptrBtnState')
# Xorg ProcXkbGetState zero-initializes grab/lookup reply fields rather than
# copying the server's derived-state members. Do not invent Shift bits there.
SHIFT_FIELDS = frozenset(('mods', 'baseMods', 'compatState'))
NUM_LOCK = 0xFF7F
MOD2 = 16


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def validate_state(state, mapping, minimum, modifiers, owned_shift=False):
    """Admit zero state or the explicitly proven, otherwise idle Mod2 lock.

    The latter is only a state admission: every input route must independently
    prove the same desired symbol with and without NumLock before injection.
    No arbitrary modifier masking, held/latched state or nonzero group is valid.
    """
    require(set(state) == set(FIELDS) and all(type(x) is int for x in state.values()),
            'Malformed XKB state')
    lock = state['lockedMods']
    require(lock in (0, MOD2), 'Unsupported locked XKB modifiers; never clear them')
    for name in FIELDS:
        expected = int(owned_shift and name in SHIFT_FIELDS)
        if name in ('mods', 'lockedMods', 'compatState'):
            expected |= lock
        require(state[name] == expected, f'Unsupported XKB state {name}={state[name]}; never clear it')
    if lock:
        require(len(modifiers) == 8 and type(minimum) is int and
                8 <= minimum <= minimum + len(mapping) - 1 <= 255, 'Invalid core keymap')
        codes = [code for code in modifiers[4] if code]
        require(len(codes) == 1, 'Mod2 is not exclusively one NumLock key')
        code = codes[0]
        require(type(code) is int and minimum <= code < minimum + len(mapping), 'Invalid NumLock keycode')
        require(all(code not in group for index, group in enumerate(modifiers) if index != 4),
                'NumLock key is also bound to another modifier')
        symbols = {symbol for symbol in mapping[code - minimum] if symbol}
        require(symbols == {NUM_LOCK}, 'Mod2 key is not exclusively NumLock')
    return lock


def verify_symbols(symbol, shifted, lock, results):
    """Require actual XKB translation, including the lock-neutral control."""
    require(type(symbol) is int and 0 < symbol <= 0xFFFFFFFF and
            type(shifted) is bool and type(lock) is int and lock in (0, MOD2),
            'Invalid route request')
    masks = {int(shifted), int(shifted) | lock}
    require(set(results) == masks, 'Missing or extra XKB translation results')
    for mask in masks:
        require(type(results[mask]) is int and results[mask] == symbol,
                f'XKB route changes the requested symbol at modifier mask {mask}')


def _native_lookup_symbols(display_name, root_xid, code, masks):
    """Use a fresh native connection, avoiding a retained stale Xlib keymap.

    The caller supplies the exact Python-Xlib display name/root. The public
    wrapper enforces the timeout in a separate process. There is no global Xlib error
    handler, server grab, map replacement or lock mutation.
    """
    require(sys.platform.startswith('linux'), 'Native keymap lookup requires Linux')
    require(isinstance(display_name, str) and display_name and '\0' not in display_name,
            'An explicit live display name is required')
    require(type(root_xid) is int and root_xid > 0 and type(code) is int and 8 <= code <= 255,
            'Invalid native route identity')
    masks = tuple(masks)
    require(masks and all(type(mask) is int and mask in (0, 1, 16, 17) for mask in masks),
            'Unsupported native modifier mask')
    library = ctypes.util.find_library('X11')
    require(library is not None, 'System libX11 is unavailable')
    lib = ctypes.CDLL(library)
    lib.XOpenDisplay.argtypes, lib.XOpenDisplay.restype = [ctypes.c_char_p], ctypes.c_void_p
    lib.XCloseDisplay.argtypes, lib.XCloseDisplay.restype = [ctypes.c_void_p], ctypes.c_int
    lib.XDefaultRootWindow.argtypes, lib.XDefaultRootWindow.restype = [ctypes.c_void_p], ctypes.c_ulong
    integer_pointer = ctypes.POINTER(ctypes.c_int)
    lib.XkbQueryExtension.argtypes = [ctypes.c_void_p] + [integer_pointer] * 5
    lib.XkbQueryExtension.restype = ctypes.c_int
    lib.XkbLookupKeySym.argtypes = [ctypes.c_void_p, ctypes.c_ubyte, ctypes.c_uint,
                                  ctypes.POINTER(ctypes.c_uint), ctypes.POINTER(ctypes.c_ulong)]
    lib.XkbLookupKeySym.restype = ctypes.c_int
    connection = lib.XOpenDisplay(display_name.encode())
    require(connection, 'Cannot open the admitted X11 display')
    try:
        require(lib.XDefaultRootWindow(connection) == root_xid, 'Native display root differs')
        opcode, event, error, major, minor = (ctypes.c_int(x) for x in (0, 0, 0, 1, 0))
        require(lib.XkbQueryExtension(connection, *(ctypes.byref(x) for x in
                    (opcode, event, error, major, minor))) and major.value == 1,
                'Native connection lacks XKB 1.x')
        result = {}
        for mask in masks:
            modifiers, symbol = ctypes.c_uint(), ctypes.c_ulong()
            require(lib.XkbLookupKeySym(connection, code, mask, ctypes.byref(modifiers),
                                       ctypes.byref(symbol)), 'XKB translation failed')
            result[mask] = symbol.value
        return result
    finally:
        lib.XCloseDisplay(connection)


def lookup_symbols(display_name, root_xid, code, masks, timeout):
    # A Python signal alone cannot bound a blocking foreign-library call.
    require(0 < timeout <= 3, 'Native lookup must fit the existing protocol deadline')
    result = subprocess.run([sys.executable, '-B', str(Path(__file__).resolve()), '--lookup',
                             display_name, str(root_xid), str(code), *(str(mask) for mask in masks)],
                            capture_output=True, text=True, timeout=timeout, check=True)
    return {int(mask): symbol for mask, symbol in json.loads(result.stdout).items()}


def guarded_keyboard_type(base):
    """Extend the original grid probe without replacing its input/cleanup path."""
    class GuardedKeyboard(base):
        def state(self, shift=False):
            display = self.desktop.display
            reply = self.GetState(display=display.display, opcode=self.opcode, deviceSpec=0x100)
            require(reply.length == 0, 'Unexpected XKB state reply length')
            state = {name: getattr(reply, name) for name in FIELDS}
            minimum, maximum = display.display.info.min_keycode, display.display.info.max_keycode
            require(8 <= minimum <= maximum <= 255, 'Invalid core keycode range')
            mapping = display.get_keyboard_mapping(minimum, maximum - minimum + 1)
            lock = validate_state(state, mapping, minimum, display.get_modifier_mapping(), shift)
            if hasattr(self, 'initial_lock'):
                require(lock == self.initial_lock, 'Lock state changed during the probe; no input permitted')
            else:
                self.initial_lock = lock
            return state

        def route(self, symbol):
            self.state()
            route = super().route(symbol)
            masks = sorted({int(route['shift']), int(route['shift']) | self.initial_lock})
            symbols = lookup_symbols(self.desktop.display.get_display_name(), self.desktop.root.id,
                                     route['code'], masks, min(3, self.desktop.deadline - time.monotonic()))
            verify_symbols(symbol, route['shift'], self.initial_lock, symbols)
            self.state()
            route['xkbSymbolsByMask'] = symbols
            route['unchangedLockMask'] = self.initial_lock
            return route
    return GuardedKeyboard


if __name__ == '__main__':
    require(len(sys.argv) >= 6 and sys.argv[1] == '--lookup', 'Expected read-only lookup arguments')
    print(json.dumps(_native_lookup_symbols(sys.argv[2], int(sys.argv[3]), int(sys.argv[4]),
                                           [int(mask) for mask in sys.argv[5:]])))
