#!/usr/bin/env python3
"""Offline evidence-comparison controls; no native calls, input, capture or launch."""

import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("popup_native_geometry", ROOT / "eng/librewinforms-popup-native-geometry.py")
READER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(READER)


def fixture(mode="Logical", dpi=2, framebuffer=2):
    client = dict(x=-120, y=180, width=420, height=260)
    frame = dict(x=-120, y=152, width=420, height=288)
    factor = framebuffer / dpi if mode == "Logical" else framebuffer
    source = dict(schema=1, pid=24, sequence=7, title="PopupInteractionApp [test]",
                  form=dict(visible=True, client={k: v * factor for k, v in client.items()}),
                  popups={name: dict(visible=False, client=None) for name in READER.NAMES - {"main"}})
    main = dict(name="main", visible=True, handleCreated=True, available=True, cocoaComparisonAdmitted=True,
                sourceHandle=dict(value=-(2 ** 63) + 1, kind="Window"), coordinateMode=mode,
                dpiScale=dpi, framebufferScale=framebuffer,
                geometry=dict(window=dict(kind="Cocoa", handle=100, display=0, descriptor="Cocoa"),
                              contentView=200, cocoaWindowNumber=300, backingScale=framebuffer,
                              contentBounds=client, frameBounds=frame))
    sidecar = dict(schema="popup-native-geometry-v1", pid=24, sequence=7,
                   coordinateSpace="native-desktop-top-left-points",
                   windows=[main] + [dict(name=name, visible=False, handleCreated=False,
                                        available=False, geometry=None) for name in sorted(READER.NAMES - {"main"})])
    inventory = [dict(pid=24, windowNumber=300, title=source["title"], frameBounds=copy.deepcopy(frame))]
    return source, sidecar, inventory


def validate(values):
    return READER.validate_cocoa_geometry(*values, expected_pid=24)


class NativeGeometryContracts(unittest.TestCase):
    def test_raw_content_and_frame_stay_distinct_with_negative_desktop_origin(self):
        values = fixture()
        before = copy.deepcopy(values)
        result = validate(values)
        self.assertEqual(result["status"], "recorded-geometry-matched")
        self.assertFalse(result["qualified"])
        self.assertEqual(result["windows"][0]["nativeContent"]["y"], 180)
        self.assertEqual(result["windows"][0]["nativeFrame"]["y"], 152)
        self.assertEqual(values, before)

    def test_device_pixel_policy_uses_declared_scale_without_rewriting_native_coordinates(self):
        result = validate(fixture("DevicePixels", dpi=2, framebuffer=2))
        record = result["windows"][0]
        self.assertEqual(record["sourceClient"]["width"], 840)
        self.assertEqual(record["nativeContent"]["width"], 420)
        self.assertEqual(record["declaredNativeToSourceScale"], 2)

    def test_logical_policy_uses_both_actual_scales(self):
        result = validate(fixture("Logical", dpi=2, framebuffer=1))
        self.assertEqual(result["windows"][0]["sourceClient"]["width"], 210)
        self.assertEqual(result["windows"][0]["declaredNativeToSourceScale"], 0.5)

    def test_midpoints_follow_existing_away_from_zero_policy(self):
        mapped, factor = READER.managed_rectangle(dict(x=-1, y=1, width=3, height=5), "Logical", 2, 1)
        self.assertEqual(mapped, dict(x=-1, y=1, width=2, height=3))
        self.assertEqual(factor, 0.5)

    def test_pid_and_sequence_must_match_same_snapshot(self):
        for index, key, value in ((0, "pid", 99), (1, "pid", 99), (1, "sequence", 6),
                                  (0, "sequence", 0), (1, "sequence", True), (0, "pid", True)):
            with self.subTest(index=index, key=key):
                values = fixture()
                values[index][key] = value
                with self.assertRaises(ValueError): validate(values)

    def test_unknown_schema_or_coordinate_units_reject(self):
        for index, key, value in ((0, "schema", True), (0, "schema", 2), (1, "schema", "future"),
                                  (1, "coordinateSpace", "framebuffer-pixels")):
            with self.subTest(index=index, key=key):
                values = fixture()
                values[index][key] = value
                with self.assertRaises(ValueError): validate(values)

    def test_duplicate_or_missing_window_names_reject(self):
        for action in ("duplicate", "missing", "unknown"):
            with self.subTest(action=action):
                values = fixture()
                if action == "missing": values[1]["windows"].pop()
                else: values[1]["windows"][-1]["name"] = "main" if action == "duplicate" else "combo"
                with self.assertRaises(ValueError): validate(values)

    def test_visible_unavailable_or_uncreated_window_cannot_be_qualified(self):
        for key in ("available", "handleCreated", "cocoaComparisonAdmitted"):
            with self.subTest(key=key):
                values = fixture()
                values[1]["windows"][0][key] = False
                with self.assertRaisesRegex(ValueError, "unavailable"): validate(values)

    def test_visibility_cannot_change_between_source_and_sidecar(self):
        values = fixture()
        values[1]["windows"][0]["visible"] = False
        with self.assertRaisesRegex(ValueError, "Visibility"): validate(values)

    def test_logical_null_and_boolean_handles_reject(self):
        for key, value in (("kind", "LogicalControl"), ("value", 0), ("value", True)):
            with self.subTest(key=key, value=value):
                values = fixture()
                values[1]["windows"][0]["sourceHandle"][key] = value
                with self.assertRaises(ValueError): validate(values)

    def test_wrong_native_kind_display_view_or_number_reject(self):
        for field, value in (("kind", "Win32"), ("display", 5), ("handle", 0),
                             ("contentView", 0), ("cocoaWindowNumber", -1)):
            with self.subTest(field=field):
                values = fixture()
                geometry = values[1]["windows"][0]["geometry"]
                (geometry["window"] if field in ("kind", "display", "handle") else geometry)[field] = value
                with self.assertRaises(ValueError): validate(values)

    def test_invalid_policy_values_reject_without_fitting_observed_rectangles(self):
        for field, value in (("coordinateMode", "Unknown"), ("dpiScale", 0), ("dpiScale", float("nan")),
                             ("framebufferScale", 9), ("framebufferScale", True)):
            with self.subTest(field=field):
                values = fixture()
                values[1]["windows"][0][field] = value
                with self.assertRaises(ValueError): validate(values)

    def test_backing_scale_is_independent_and_must_match_current_framebuffer_scale(self):
        values = fixture()
        values[1]["windows"][0]["geometry"]["backingScale"] = 1
        with self.assertRaisesRegex(ValueError, "scale mismatch"): validate(values)

    def test_foreign_duplicate_missing_or_negative_cg_identity_reject(self):
        for action in ("foreign", "duplicate", "missing", "negative"):
            with self.subTest(action=action):
                values = fixture()
                if action == "foreign": values[2][0]["pid"] = 99
                elif action == "duplicate": values[2].append(copy.deepcopy(values[2][0]))
                elif action == "missing": values[2].clear()
                else: values[2][0]["windowNumber"] = -1
                with self.assertRaises(ValueError): validate(values)

    def test_title_match_does_not_substitute_for_exact_independent_frame(self):
        values = fixture()
        values[2][0]["frameBounds"]["width"] += 1
        with self.assertRaisesRegex(ValueError, "exact independent CG frame"): validate(values)

    def test_main_title_identity_must_match(self):
        values = fixture()
        values[2][0]["title"] = "another window"
        with self.assertRaisesRegex(ValueError, "title identity"): validate(values)

    def test_content_cannot_be_replaced_by_frame_or_adjusted_to_source(self):
        values = fixture()
        values[1]["windows"][0]["geometry"]["contentBounds"] = copy.deepcopy(values[2][0]["frameBounds"])
        with self.assertRaisesRegex(ValueError, "Source/native client mismatch"): validate(values)

    def test_observed_source_scale_error_is_rejected_not_calibrated(self):
        values = fixture()
        values[0]["form"]["client"] = {key: value * 2 for key, value in values[0]["form"]["client"].items()}
        with self.assertRaisesRegex(ValueError, "Source/native client mismatch"): validate(values)

    def test_nonfinite_negative_boolean_and_overflowed_rectangles_reject(self):
        for key, value in (("x", float("inf")), ("y", True), ("width", -1), ("height", 0), ("x", 2 ** 40)):
            with self.subTest(key=key):
                values = fixture()
                values[1]["windows"][0]["geometry"]["contentBounds"][key] = value
                with self.assertRaises(ValueError): validate(values)

    def test_extra_native_surfaces_remain_unmatched_not_fabricated(self):
        values = fixture()
        unknown = dict(pid=24, windowNumber=301, title=None,
                       frameBounds=dict(x=10, y=20, width=50, height=60))
        values[2].append(unknown)
        result = validate(values)
        self.assertEqual(result["unmatchedCGWindows"], [unknown])
        self.assertEqual(len(result["windows"]), 1)
        self.assertFalse(result["qualified"])

    def test_visible_popup_needs_separate_source_and_native_identities(self):
        values = fixture()
        source, sidecar, inventory = values
        record = next(item for item in sidecar["windows"] if item["name"] == "context")
        record.update(copy.deepcopy(sidecar["windows"][0]))
        record["name"] = "context"
        source["popups"]["context"] = copy.deepcopy(source["form"])
        with self.assertRaisesRegex(ValueError, "Aliased"): validate(values)
        record["sourceHandle"]["value"] += 1
        record["geometry"]["window"]["handle"] += 1
        record["geometry"]["contentView"] += 1
        record["geometry"]["cocoaWindowNumber"] += 1
        inventory.append(dict(pid=24, windowNumber=301, title=None,
                              frameBounds=copy.deepcopy(record["geometry"]["frameBounds"])))
        self.assertEqual(len(validate(values)["windows"]), 2)

    def test_bounded_json_rejects_nonfinite_and_oversized_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "evidence.json"
            path.write_text('{"x":NaN}')
            with self.assertRaisesRegex(ValueError, "Nonfinite"): READER.read_bounded(path)
            path.write_text(" " * (READER.LIMIT + 1))
            with self.assertRaisesRegex(ValueError, "256 KiB"): READER.read_bounded(path)
            path.write_text(json.dumps({"ok": True}))
            value, digest = READER.read_bounded(path)
            self.assertEqual(value, {"ok": True})
            self.assertEqual(len(digest), 64)


if __name__ == "__main__":
    unittest.main()
