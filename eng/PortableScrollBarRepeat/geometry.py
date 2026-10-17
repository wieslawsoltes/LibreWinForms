"""Read-only validation of actual portable paint/source receipts; no input APIs."""
NAMES = ('h-decrement', 'h-increment', 'h-page', 'h-leave', 'v-decrement', 'v-increment', 'v-page')


def require(value, message):
    if not value:
        raise RuntimeError(message)


def rectangle(value):
    require(isinstance(value, dict) and set(value) == {'x', 'y', 'width', 'height'}, 'Rectangle fields differ')
    require(all(type(v) is int and -(2**31) <= v < 2**31 for v in value.values()), 'Rectangle integers invalid')
    require(value['width'] > 0 and value['height'] > 0
            and value['x'] + value['width'] < 2**31 and value['y'] + value['height'] < 2**31, 'Rectangle extent invalid')
    return value


def contains(rect, point):
    return rect['x'] <= point[0] < rect['x'] + rect['width'] and rect['y'] <= point[1] < rect['y'] + rect['height']


def nested(outer, inner):
    return contains(outer, (inner['x'], inner['y'])) and contains(outer, (inner['x'] + inner['width'] - 1, inner['y'] + inner['height'] - 1))


def inventory(snapshot, ready, now_ms):
    require(type(now_ms) is int and type(snapshot.get('tickMilliseconds')) is int
            and 0 <= now_ms - snapshot['tickMilliseconds'] <= 500, 'Source receipt stale or from future')
    require(snapshot.get('schema') == 2 and snapshot.get('qualified') is False
            and snapshot.get('dpi') == 96 and 'hwnd' not in snapshot, 'Source receipt policy differs')
    for field in ('pid', 'title', 'sourceHandle', 'forms', 'client', 'coordinateMode'):
        require(snapshot.get(field) == ready.get(field) and snapshot.get(field) is not None, 'Owned root identity changed: ' + field)
    owner = rectangle(snapshot['client'])
    controls = snapshot.get('controls')
    originals = ready.get('controls')
    require(isinstance(controls, list) and len(controls) == 7 and isinstance(originals, list) and len(originals) == 7,
            'Original seven-control inventory differs')
    require(tuple(c.get('name') for c in controls) == NAMES and tuple(c.get('name') for c in originals) == NAMES,
            'Original control order or identity differs')
    handles = set()
    for current, original in zip(controls, originals):
        handle = current.get('sourceHandle')
        require(type(handle) is int and handle != 0 and handle not in handles and handle != snapshot['sourceHandle'],
                'Original source handle missing or aliased')
        handles.add(handle)
        for field in ('name', 'sourceHandle', 'sourceClass', 'client', 'localClient', 'Minimum', 'Maximum', 'SmallChange', 'LargeChange'):
            require(current.get(field) == original.get(field) and current.get(field) is not None, 'Control identity changed: ' + field)
        require(current.get('enabled') is True and current.get('visible') is True, 'Control is hidden or disabled')
        screen, local = rectangle(current['client']), rectangle(current['localClient'])
        require(local['x'] == local['y'] == 0 and screen['width'] == local['width'] and screen['height'] == local['height']
                and nested(owner, screen), 'Source screen/client geometry differs')
        require(current['sourceClass'] == ('VScrollBar' if current['name'].startswith('v') else 'HScrollBar'), 'Source orientation differs')
    return {c['name']: c for c in controls}


def painted_target(control):
    require(control.get('paintCurrent') is True, 'Current source value has no completed paint')
    parts = control.get('paint'); local = rectangle(control['localClient']); screen = rectangle(control['client'])
    require(isinstance(parts, list) and 4 <= len(parts) <= 8, 'Actual paint inventory unavailable')
    vertical = control['name'].startswith('v')
    thumb_id, negative_state, positive_state = (3, 1, 5) if vertical else (2, 9, 13)
    # Track enum values follow the actual ScrollBarRenderer call. An optional
    # pressed track is admitted separately and never supplies arrow geometry.
    arrows = [p for p in parts if p.get('part') == 1]
    thumbs = [p for p in parts if p.get('part') == thumb_id]
    tracks = [p for p in parts if p.get('part') in ((6, 7) if vertical else (4, 5))]
    require(len(arrows) == 2 and len(thumbs) == 1 and tracks, 'Missing or duplicated original paint parts')
    require(len(arrows) + len(thumbs) + len(tracks) == len(parts), 'Foreign theme paint part')
    for part in parts:
        require(type(part.get('state')) is int and nested(local, rectangle(part.get('bounds'))), 'Paint frame exceeds control')
        require(part['part'] == 1 or part['state'] in (1, 2, 3), 'Noninteractive source paint state')
    minus = [p for p in arrows if negative_state <= p['state'] < negative_state + 4]
    plus = [p for p in arrows if positive_state <= p['state'] < positive_state + 4]
    require(len(minus) == len(plus) == 1, 'Arrow direction/state identity differs')
    require(minus[0]['state'] != negative_state + 3 and plus[0]['state'] != positive_state + 3,
            'Disabled source arrow paint')
    minus, plus, thumb = minus[0]['bounds'], plus[0]['bounds'], thumbs[0]['bounds']
    axis, extent, cross, breadth = ('y', 'height', 'x', 'width') if vertical else ('x', 'width', 'y', 'height')
    for part in (minus, plus, thumb):
        require(part[cross] == 0 and part[breadth] == local[breadth], 'Paint frame crosses source orientation')
    require(minus[axis] == 0 and plus[axis] + plus[extent] == local[extent]
            and minus[extent] < thumb[axis] < thumb[axis] + thumb[extent] < plus[axis], 'Original middle thumb inventory differs')
    name = control['name']
    offset = minus[extent] // 2 if name.endswith('decrement') else thumb[axis] + thumb[extent] + 3 if name.endswith('page') else plus[axis] + plus[extent] // 2
    require(0 < offset < local[extent] and (not name.endswith('page') or offset < plus[axis]), 'Target outside actual selected part')
    point = (screen['x'] + screen['width'] // 2, screen['y'] + offset) if vertical else (screen['x'] + offset, screen['y'] + screen['height'] // 2)
    require(contains(screen, point), 'Target outside original source client')
    return point


def bind_native_root(windows, source, child_pid):
    """Bind independent native observation; never publish an HWND as source data."""
    require(type(child_pid) is int and child_pid > 0 and source.get('pid') == child_pid,
            'Source process is not the launched child')
    require(source.get('schema') == 2 and source.get('qualified') is False
            and source.get('dpi') == 96 and source.get('coordinateMode') == 'DevicePixels'
            and 'hwnd' not in source, 'Source-only geometry policy differs')
    require(source.get('title') == f'PortableScrollBarRepeat [{child_pid}]', 'Owned source title differs')
    require(isinstance(windows, list) and len(windows) == 1, 'Native root inventory is not unique')
    owner = windows[0]
    require(type(owner.get('hwnd')) is int and owner['hwnd'] > 0
            and owner.get('title') == source['title'] and owner.get('enabled') is True,
            'Native root identity differs')
    require(rectangle(owner.get('client')) == rectangle(source.get('client')), 'Independent native/source root geometry differs')
    return dict(owner)
