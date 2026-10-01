#!/usr/bin/env python3
"""Synthetic geometry receipt corruption controls, never Windows measurements."""
import base64
import ast
import copy
import hashlib
import importlib.util
from pathlib import Path
import tempfile
import textwrap
from types import SimpleNamespace
import unittest

spec = importlib.util.spec_from_file_location("edit_geometry", Path(__file__).resolve().parents[1] / "librewinforms-edit-selection-geometry.py")
OBSERVER = importlib.util.module_from_spec(spec)
spec.loader.exec_module(OBSERVER)


class SelectionGeometryReceiptTests(unittest.TestCase):
    def base_receipt(self, source="text"):
        return {"cases": [{"requestedText": source, "requestedUtf16": OBSERVER.utf16(source),
                            "scriptBreak": {"flags": 0x40}}]}

    def receipt(self):
        value = self.base_receipt()
        value.update(schema="native-edit-selection-geometry-v1", hostShown=True, ownedWindowOnly=True,
                     fallbackFontIdentityQualified=False, expectedCases=len(OBSERVER.GEOMETRY_INPUTS), geometryComplete=True,
                     caretSamples=278, unavailableCaretSamples=0, cases=[])
        descriptor = dict(Height=-11, Width=0, Escapement=0, Orientation=0, Weight=400,
                          Italic=0, Underline=0, StrikeOut=0, CharSet=1, OutPrecision=0,
                          ClipPrecision=0, Quality=0, PitchAndFamily=0, FaceName="schema-only")
        for name, (source, seams) in OBSERVER.GEOMETRY_INPUTS.items():
            units = OBSERVER.utf16(source)
            length = len(units)
            case = self.base_receipt(source)["cases"][0]
            case.update(Name=name, RightToLeft=(name.endswith("-rtl")), requestedSeams=seams,
                        observations=[], metadata={"handle": 17, "nativeClass": "EDIT", "deviceDpi": 96,
                            "clientSize": {"Width": 2, "Height": 2}, "fallbackFontIdentityQualified": False,
                            "ownedCaretSamples": 38, "systemColors": [{"index": i, "colorRef": 0x112233}
                                for i in (5, 8, 13, 14, 15, 18)],
                            "baseFont": {"handle": 23, "borrowed": True, "fallbackIdentityQualified": False,
                                "descriptor": copy.deepcopy(descriptor), "byteCount": 16,
                                "bytes": base64.b64encode(bytes(16)).decode(), "sha256": hashlib.sha256(bytes(16)).hexdigest(),
                                "DeviceDpi": 96}})
            if "lam-alef" in name:
                case["metadata"]["ownedCaretSamples"] = 22
                case["metadata"]["requestedFont"] = {"family": "Arial", "size": 20, "unit": "Pixel", "style": "Regular"}
                case["metadata"]["baseFont"]["descriptor"]["FaceName"] = "Arial"
                case["metadata"]["baseFont"]["managed"] = {"Name": "Arial", "Size": 20, "unit": "Pixel", "style": "Regular"}
            if name.endswith("-rtl"):
                case["scriptBreak"]["flags"] |= 0x100
            a, b, c = seams
            requested = [(f"collapsed-{p}", p, p) for p in (0, a, b, c, length)]
            for start, end in ((a, b), (b, c), (a, c)):
                requested += [(f"forward-{start}-{end}", start, end), (f"reverse-{end}-{start}", end, start)]
            gestures = [] if "lam-alef" in name else [(label, 0, 0) for label in (
                    "first-down", "first-up", "double-down", "drag-last", "drag-first-reversal",
                    "drag-anchor-reversal", "drag-last-again", "final-up")]
            for index, (label, anchor, active) in enumerate(requested + gestures):
                start, end = sorted((anchor, active))
                text = b"".join(unit.to_bytes(2, "little") for unit in units[start:end]).decode("utf-16-le", "surrogatepass")
                state = {"Selection": {"Start": start, "End": end, "ManagedStart": start,
                            "ManagedLength": end - start, "SelectedText": text, "Focused": True},
                    "Caret": {"Available": True, "UnavailableReason": None, "GuiQuerySucceeded": True,
                        "GuiError": 0, "CaretOwner": 17, "FocusOwner": 17, "Flags": 0,
                        "Rectangle": {"Left": 0, "Top": 0, "Right": 1, "Bottom": 2},
                        "PointQuerySucceeded": True, "PointError": 0, "Point": {"X": 0, "Y": 0}},
                    "Client": {"Left": 0, "Top": 0, "Right": 2, "Bottom": 2},
                    "Format": {"Left": 0, "Top": 0, "Right": 2, "Bottom": 2}, "FirstVisibleRaw": 0,
                    "Positions": [{"Index": p, "Raw": 0 if p < length else -1,
                        "Point": {"X": 0, "Y": 0} if p < length else None} for p in range(length + 1)],
                    "Hits": [{"X": x, "Y": 0, "Raw": x, "Character": x, "Line": 0} for x in range(2)]}
                for key in ("HorizontalScroll", "VerticalScroll"):
                    state[key] = {"Available": False, "Error": 0, "Minimum": 0, "Maximum": 0,
                                  "Page": 0, "Position": 0, "TrackPosition": 0}
                request = {"kind": "set-selection", "requestedAnchor": anchor, "requestedActive": active, "result": 0}
                if index >= 11:
                    request = {"kind": "gesture", "message": [0x201, 0x202, 0x203, 0x200, 0x200, 0x200, 0x200, 0x202][index - 11],
                               "callbacks": [{"name": callback, "step": label} for callback in ("WndProc-enter", "WndProc-return")]}
                raw = bytes((0x12, 0x34, 0x56, 0xA5)) * 4
                pixels = {"Width": 2, "Height": 2, "Stride": 8, "Format": "BGRX8-top-down-original-bytes",
                          "Bytes": base64.b64encode(raw).decode(), "Sha256": hashlib.sha256(raw).hexdigest(),
                          "ClearControlBytes": base64.b64encode(raw).decode(), "ClearControlSha256": hashlib.sha256(raw).hexdigest(),
                          "RgbIndependentOfClear": True, "RgbClearDifferences": 0, "FirstPrintResult": 0, "SecondPrintResult": 0}
                case["observations"].append({"name": label, "request": request, "beforeScroll": copy.deepcopy(state),
                    "scrollCaretResult": 0, "afterScroll": copy.deepcopy(state), "pixels": pixels})
            value["cases"].append(case)
        return value


    def test_complete_nine_case_geometry_and_original_order(self):
        receipt = self.receipt()
        OBSERVER.verify_geometry(receipt)
        self.assertEqual([case["Name"] for case in receipt["cases"]][:5],
                         ["emoji-context", "supplementary-emoji-zwj", "surrogate-combining", "bidi-ltr", "bidi-rtl"])
        self.assertEqual(receipt["caretSamples"], 278)

    def test_missing_case_and_rewritten_source_fail(self):
        for mutation in (lambda value: value["cases"].pop(),
                         lambda value: value["cases"][-1].update(requestedText="other"),
                         lambda value: value["cases"][-1].update(RightToLeft=False),
                         lambda value: value["cases"][-1].update(requestedSeams=[6, 8, 9])):
            value = self.receipt()
            mutation(value)
            with self.assertRaises(ValueError):
                OBSERVER.verify_geometry(value)

    def test_font_bytes_and_exact_requested_font_cannot_be_substituted(self):
        for mutate in (lambda font: font.update(sha256="a" * 64),
                       lambda font: font.update(byteCount=15),
                       lambda font: font["descriptor"].update(FaceName="Other"),
                       lambda font: font["managed"].update(Size=21)):
            value = self.receipt()
            mutate(value["cases"][-1]["metadata"]["baseFont"])
            with self.assertRaises(ValueError):
                OBSERVER.verify_geometry(value)

    def test_original_directed_selection_request_and_public_source_are_exact(self):
        for key in ("requestedAnchor", "requestedActive"):
            value = self.receipt()
            value["cases"][-1]["observations"][5]["request"][key] = 0
            with self.assertRaises(ValueError):
                OBSERVER.verify_geometry(value)
        value = self.receipt()
        value["cases"][-1]["observations"][5]["afterScroll"]["Selection"]["SelectedText"] = "other"
        with self.assertRaises(ValueError):
            OBSERVER.verify_geometry(value)

    def test_unavailable_caret_stays_explicit(self):
        value = self.receipt()
        caret = value["cases"][-1]["observations"][0]["afterScroll"]["Caret"]
        caret.update(Available=False, UnavailableReason="No owned caret", Rectangle=None, Point=None, CaretOwner=0)
        value["unavailableCaretSamples"] = 1
        value["geometryComplete"] = False
        value["cases"][-1]["metadata"]["ownedCaretSamples"] -= 1
        OBSERVER.verify_geometry(value)
        value["geometryComplete"] = True
        with self.assertRaises(ValueError):
            OBSERVER.verify_geometry(value)

    def test_native_source_positions_and_full_hit_scan_cannot_be_filtered(self):
        for field in ("Positions", "Hits"):
            value = self.receipt()
            value["cases"][-1]["observations"][0]["afterScroll"][field].pop()
            with self.assertRaises(ValueError):
                OBSERVER.verify_geometry(value)

    def test_native_raw_position_and_hit_identity_cannot_be_changed(self):
        for field, replacement in (("Positions", {"Point": {"X": 1, "Y": 0}}),
                                   ("Hits", {"Character": 7})):
            value = self.receipt()
            value["cases"][-1]["observations"][0]["afterScroll"][field][0].update(replacement)
            with self.assertRaises(ValueError):
                OBSERVER.verify_geometry(value)

    def test_pixel_bytes_hash_and_clear_control_are_independent(self):
        for field in ("Bytes", "ClearControlBytes"):
            value = self.receipt()
            value["cases"][-1]["observations"][0]["pixels"][field] = base64.b64encode(bytes(16)).decode()
            with self.assertRaises(ValueError):
                OBSERVER.verify_geometry(value)
        value = self.receipt()
        pixels = value["cases"][-1]["observations"][0]["pixels"]
        raw = bytes(16)
        pixels.update(ClearControlBytes=base64.b64encode(raw).decode(),
                      ClearControlSha256=hashlib.sha256(raw).hexdigest())
        with self.assertRaises(ValueError):
            OBSERVER.verify_geometry(value)

    def test_geometry_does_not_assert_half_advance_or_trailing_snap(self):
        value = self.receipt()
        caret = value["cases"][-1]["observations"][2]["afterScroll"]["Caret"]
        caret["Point"]["X"] = 7
        caret["Rectangle"].update(Left=7, Right=8)
        OBSERVER.verify_geometry(value)

    def test_callback_delivery_and_caret_owner_are_required(self):
        value = self.receipt()
        value["cases"][0]["observations"][11]["request"]["callbacks"].pop()
        with self.assertRaises(ValueError):
            OBSERVER.verify_geometry(value)
        value = self.receipt()
        value["cases"][-1]["observations"][0]["afterScroll"]["Caret"]["CaretOwner"] = 19
        with self.assertRaises(ValueError):
            OBSERVER.verify_geometry(value)


class ShapingGeometryReceiptTests(unittest.TestCase):
    """Synthetic schema corruption tests, not a manufactured Windows oracle."""

    def receipt(self):
        primary = SelectionGeometryReceiptTests().receipt()
        cases = [case for case in primary["cases"] if "lam-alef" in case["Name"]]
        font = cases[0]["metadata"]["baseFont"]
        receipt = dict(Schema="native-edit-shaping-diagnostics-v1", Completed=True, Errors=[],
            IndependentOfEdit=True, EditRendererIdentityQualified=False, EditFallbackIdentityQualified=False,
            DesktopQualified=False, PhysicalInputQualified=False, HostShown=True, OwnedWindowOnly=True,
            FontPolicy="selected-HFONT-only; no fallback attempted", RequestedPixelSizes=[20, 21],
            FontBytesBySha256={font["sha256"]: font["bytes"]}, ElapsedMilliseconds=100, Cases=[])
        for size in (20, 21):
            for original in cases:
                source = original["requestedUtf16"]
                length = len(source)
                a, _, c = original["requestedSeams"]
                count = length - 1
                clusters = [cp if cp <= a else cp - 1 for cp in range(length)]
                advances = [1] * count
                advances[a] = 11 if size == 20 else 12
                width = sum(advances)
                analysis = dict(Flags=1024, State=1)
                run = dict(start=0, end=length, sourceUtf16=source, inputAnalysis=copy.deepcopy(analysis),
                    shapeAnalysis=copy.deepcopy(analysis), placeAnalysis=copy.deepcopy(analysis),
                    shapeHResult=0, placeHResult=0, logicalWidthsHResult=0, rawPropertiesFirst=1,
                    rawPropertiesSecond=8, glyphs=list(range(1, count + 1)), logClusters=clusters,
                    visualAttributes=[16] * count, missingGlyphIndices=[], advances=advances,
                    offsets=[dict(Du=0, Dv=0) for _ in range(count)], abc=dict(A=0, B=width, C=0),
                    advanceWidth=width, logicalWidths=[1] * length,
                    edges=[dict(cp=cp, trailing=trailing, sourceBoundary=cp + int(trailing), hResult=0, x=0)
                           for cp in range(length) for trailing in (False, True)],
                    hits=[dict(x=x, hResult=0, cp=0, trailing=0) for x in range(-1, width + 2)],
                    targetCluster=dict(sourceStart=a, sourceEnd=c, firstGlyph=a, glyphEnd=a + 1,
                                       advanceWidth=advances[a], parity=advances[a] % 2))
                observed_font = dict(borrowedHandle=23, descriptor=copy.deepcopy(font["descriptor"]),
                    selectedFace="Actual selected face", faceApi="GetTextFaceW", byteCount=font["byteCount"],
                    sha256=font["sha256"], metrics=dict(Height=size + 2, InternalLeading=2), mapMode=1,
                    realizedEmHeight=size, managed=dict(Name="Arial", Size=size, unit="Pixel", style="Regular"),
                    source="WM_GETFONT selected into GetDC(EDIT); GetFontData on that same HDC")
                observed_font["descriptor"]["Height"] = -size
                receipt["Cases"].append(dict(name=original["Name"], requestedPixelSize=size,
                    requestedText=original["requestedText"], requestedUtf16=source,
                    requestedSeams=original["requestedSeams"], rightToLeft=original["RightToLeft"],
                    primaryCase=original["Name"], samePrimaryWindow=False, handle=17, nativeClass="EDIT",
                    deviceDpi=96, clientSize=dict(Width=2, Height=2), systemColors=original["metadata"]["systemColors"],
                    completed=True, fontRestored=True, dcReleased=True, ownedCaretSamples=22,
                    fontPropertiesHResult=0, itemizeHResult=0, scriptPropertiesHResult=0, layoutHResult=0,
                    freeCacheHResult=0, control=0, initialState=int(original["RightToLeft"]), font=observed_font,
                    fontProperties=dict(ByteSize=16, Blank=0, Default=65535, Invalid=0, Kashida=1),
                    runs=[run], levels=[1], visualToLogical=[0], logicalToVisual=[0],
                    observations=copy.deepcopy(original["observations"])))
        return receipt

    def test_complete_independent_capture_and_actual_measured_parities(self):
        OBSERVER.verify_shaping(self.receipt())

    def test_original_renderer_and_fallback_identity_cannot_be_claimed(self):
        for key in ("IndependentOfEdit", "EditRendererIdentityQualified", "EditFallbackIdentityQualified"):
            value = self.receipt()
            value[key] = not value[key]
            with self.assertRaises(ValueError):
                OBSERVER.verify_shaping(value)

    def test_selected_font_is_not_inferred_from_requested_logfont(self):
        for mutate in (lambda font: font.update(faceApi="LOGFONT"), lambda font: font.update(selectedFace=""),
                       lambda font: font.update(sha256="a" * 64), lambda font: font.update(realizedEmHeight=0)):
            value = self.receipt()
            mutate(value["Cases"][0]["font"])
            with self.assertRaises(ValueError):
                OBSERVER.verify_shaping(value)

    def test_both_affinities_and_complete_integer_hit_scan_are_required(self):
        for field in ("edges", "hits", "logClusters", "advances"):
            value = self.receipt()
            value["Cases"][0]["runs"][0][field].pop()
            with self.assertRaises(ValueError):
                OBSERVER.verify_shaping(value)

    def test_missing_glyph_failed_hresult_and_cleanup_are_not_success(self):
        for mutation in (lambda value: value["Cases"][0]["runs"][0]["glyphs"].__setitem__(0, 65535),
                         lambda value: value["Cases"][0]["runs"][0].update(shapeHResult=-1),
                         lambda value: value["Cases"][0].update(fontRestored=False),
                         lambda value: value["Cases"][0].update(freeCacheHResult=-1)):
            value = self.receipt()
            mutation(value)
            with self.assertRaises(ValueError):
                OBSERVER.verify_shaping(value)

    def test_second_size_does_not_imply_odd_even_advance_coverage(self):
        value = self.receipt()
        for case in value["Cases"][4:]:
            run = case["runs"][0]
            target = run["targetCluster"]
            run["advances"][target["firstGlyph"]] += 1
            run["advanceWidth"] += 1
            run["hits"].append(dict(x=run["advanceWidth"] + 1, hResult=0, cp=0, trailing=0))
            target["advanceWidth"] += 1
            target["parity"] = 1
        with self.assertRaisesRegex(ValueError, "odd/even"):
            OBSERVER.verify_shaping(value)

    def test_additive_edit_pixels_still_use_original_strict_verifier(self):
        value = self.receipt()
        value["Cases"][0]["observations"][0]["pixels"]["Sha256"] = "a" * 64
        with self.assertRaises(ValueError):
            OBSERVER.verify_shaping(value)

    def test_raw_coordinates_are_not_an_asserted_fraction_or_snap_model(self):
        value = self.receipt()
        value["Cases"][0]["runs"][0]["edges"][1]["x"] = 7
        OBSERVER.verify_shaping(value)


class ReferencePhaseExecutionTests(unittest.TestCase):
    """Execute the actual inline phase runner around its classifier-loop binding."""

    def phase_dispatch(self, legacy_collision=False):
        workflow = (Path(__file__).resolve().parents[2] / ".github/workflows/librewinforms-ci.yml").read_text()
        lines = workflow.splitlines()
        step = lines.index("      - name: Observe original Windows EDIT word selection")
        start = lines.index("        run: |", step) + 1
        inline = []
        for line in lines[start:]:
            if line and not line.startswith("          "):
                break
            inline.append(line)
        tree = ast.parse(textwrap.dedent("\n".join(inline)))
        runner = next(node for node in tree.body if isinstance(node, ast.FunctionDef)
                      and [argument.arg for argument in node.args.args] == ["name", "command", "timeout"])
        calls = sorted((node for node in ast.walk(tree) if isinstance(node, ast.Expr)
                        and isinstance(node.value, ast.Call) and isinstance(node.value.func, ast.Name)
                        and node.value.func.id == runner.name), key=lambda node: node.lineno)
        self.assertEqual([node.value.args[0].value for node in calls], ["build", "reference", "selection-geometry"])
        binding = copy.deepcopy(next(node for node in ast.walk(tree) if isinstance(node, ast.For)
                                    and isinstance(node.iter, ast.Name) and node.iter.id == "runs"))
        # Execute the actual module-scope target assignment, without asserting
        # fabricated Windows classification or running a native subprocess.
        binding.body, binding.orelse = [ast.Pass()], []
        if legacy_collision:
            runner.name = binding.target.id
            for call in calls:
                call.value.func.id = runner.name
        selected = ast.fix_missing_locations(ast.Module(body=[runner, calls[0], calls[1], binding, calls[2]], type_ignores=[]))
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            waits = []

            def launch(command, **options):
                self.assertEqual(options["cwd"], root)
                self.assertFalse(options["stdout"].closed)
                return SimpleNamespace(pid=17, wait=lambda timeout: waits.append(timeout) or 0)

            scope = dict(root=root, evidence=root, project=root / "reference.csproj", output=root / "bin",
                         receipt=root / "receipt.json", geometry_receipt=root / "geometry.json",
                         environment={}, phases=[], runs=[{"start": 0, "end": 2}],
                         os=SimpleNamespace(sep="/"), subprocess=SimpleNamespace(Popen=launch, STDOUT=-2))
            exec(compile(selected, "actual-reference-workflow", "exec"), scope)
            return scope["phases"], waits

    def test_geometry_phase_survives_actual_script_run_binding(self):
        phases, waits = self.phase_dispatch()
        self.assertEqual([phase["name"] for phase in phases], ["build", "reference", "selection-geometry"])
        self.assertEqual([phase["timeoutSeconds"] for phase in phases], [180, 60, 60])
        self.assertEqual(waits, [180, 60, 60])
        self.assertTrue(all(phase["exitCode"] == 0 for phase in phases))

    def test_previous_runner_name_reproduces_the_real_collision(self):
        with self.assertRaisesRegex(TypeError, "'dict' object is not callable"):
            self.phase_dispatch(legacy_collision=True)


if __name__ == "__main__":
    unittest.main()
