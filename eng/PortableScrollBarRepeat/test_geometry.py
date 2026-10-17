import copy
import unittest
from geometry import NAMES, inventory, painted_target


def rect(x, y, width, height):
    return dict(x=x, y=y, width=width, height=height)


def receipt():
    controls = []
    for i, name in enumerate(NAMES):
        vertical = name.startswith('v')
        local = rect(0, 0, 17, 300) if vertical else rect(0, 0, 350, 17)
        screen = rect(530 + (i - 4) * 60, 130, 17, 300) if vertical else rect(130, 140 + i * 80, 350, 17)
        def part(start, length):
            return rect(0, start, 17, length) if vertical else rect(start, 0, length, 17)
        length = 300 if vertical else 350
        paint = [dict(part=7 if vertical else 5, state=1, bounds=part(17, length - 34)),
                 dict(part=3 if vertical else 2, state=1, bounds=part(150, 20)),
                 dict(part=1, state=1 if vertical else 9, bounds=part(0, 17)),
                 dict(part=1, state=5 if vertical else 13, bounds=part(length - 17, 17))]
        controls.append(dict(name=name, sourceHandle=1000+i, sourceClass='VScrollBar' if vertical else 'HScrollBar',
                             client=screen, localClient=local, Minimum=0, Maximum=10000, SmallChange=1,
                             LargeChange=1000 if name.endswith('page') else 10, Value=5000,
                             visible=True, enabled=True, paintCurrent=True, paint=paint))
    return dict(schema=1, qualified=False, pid=123, title='PortableScrollBarRepeat [123]', hwnd=900,
                sourceHandle=901, forms='C:/owned/System.Windows.Forms.dll', dpi=96,
                client=rect(100,100,620,400), coordinateMode='DevicePixels', tickMilliseconds=1000, controls=controls)


class GeometryGuards(unittest.TestCase):
    def test_all_original_names_use_actual_painted_arrow_and_thumb_bounds(self):
        ready = receipt(); controls = inventory(copy.deepcopy(ready), ready, 1001)
        expected = [(138,148),(471,228),(303,308),(471,388),(538,138),(598,421),(658,303)]
        self.assertEqual([painted_target(controls[name]) for name in NAMES], expected)

    def test_stale_future_or_noninteger_source_time_fails(self):
        for tick in (499,1002,True,1000.5):
            with self.subTest(tick=tick), self.assertRaises(RuntimeError):
                current=receipt(); current['tickMilliseconds']=tick; inventory(current,receipt(),1001)

    def test_changed_root_identity_fails(self):
        for field in ('pid','title','hwnd','sourceHandle','forms','client','coordinateMode'):
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                current=receipt(); current[field]=None; inventory(current,receipt(),1001)

    def test_changed_source_identity_or_range_fails(self):
        for field in ('sourceHandle','sourceClass','client','localClient','Minimum','Maximum','SmallChange','LargeChange'):
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                current=receipt(); current['controls'][0][field]=None; inventory(current,receipt(),1001)

    def test_hidden_disabled_or_aliased_control_fails(self):
        for field in ('enabled','visible','sourceHandle'):
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                current=receipt(); current['controls'][0][field]=False if field!='sourceHandle' else 1001
                inventory(current,receipt(),1001)

    def test_duplicate_or_reordered_controls_fail(self):
        for changed in (list(reversed(receipt()['controls'])), [receipt()['controls'][0]]*7):
            with self.subTest(), self.assertRaises(RuntimeError):
                current=receipt(); current['controls']=changed; inventory(current,receipt(),1001)

    def test_live_value_movement_does_not_change_window_identity(self):
        ready=receipt(); current=copy.deepcopy(ready); current['controls'][0]['Value']=4999
        inventory(current,ready,1001)

    def test_pending_paint_cannot_select_a_target(self):
        control=receipt()['controls'][0]; control['paintCurrent']=False
        with self.assertRaises(RuntimeError):painted_target(control)

    def test_wrong_arrow_direction_fails(self):
        control=receipt()['controls'][0]; control['paint'][2]['state']=1
        with self.assertRaises(RuntimeError):painted_target(control)

    def test_duplicate_or_missing_thumb_fails(self):
        for delta in (-1,1):
            control=receipt()['controls'][0]
            if delta<0:control['paint'].pop(1)
            else:control['paint'].append(copy.deepcopy(control['paint'][1]))
            with self.subTest(delta=delta),self.assertRaises(RuntimeError):painted_target(control)

    def test_outside_or_overflowing_paint_rejects(self):
        for bounds in (rect(-1,0,17,17),rect(0,0,2**31-1,17),rect(0,0,0,17)):
            control=receipt()['controls'][0];control['paint'][2]['bounds']=bounds
            with self.subTest(bounds=bounds),self.assertRaises(RuntimeError):painted_target(control)

    def test_thumb_crossing_an_arrow_rejects(self):
        control=receipt()['controls'][0];control['paint'][1]['bounds']['x']=10
        with self.assertRaises(RuntimeError):painted_target(control)

    def test_wrong_cross_axis_extent_rejects(self):
        control=receipt()['controls'][0];control['paint'][1]['bounds']['height']=16
        with self.assertRaises(RuntimeError):painted_target(control)

    def test_new_foreign_paint_part_rejects(self):
        control=receipt()['controls'][0];control['paint'].append(dict(part=99,state=1,bounds=rect(0,0,1,1)))
        with self.assertRaises(RuntimeError):painted_target(control)


if __name__=='__main__':unittest.main()
