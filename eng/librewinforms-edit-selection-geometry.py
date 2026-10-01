#!/usr/bin/env python3
"""Verify bounded original EDIT geometry receipts; no expected caret/pixel model.

Geometry checks reuse ProGPU 60347a5's original receipt verifier. The Forms
observer adds four explicit Arial lam-alef inputs and captured base-font bytes.
"""
import base64
import hashlib
import json
from pathlib import Path

GEOMETRY_INPUTS = {
    "emoji-context": ("a\u2603\ufe0fb\U0001f469\u200d\U0001f4bbc ", [4, 7, 9]),
    "supplementary-emoji-zwj": ("x\U0001f469\u200d\U0001f4bby ", [1, 4, 6]),
    "surrogate-combining": ("go A\U0001f600 e\u0301 fin ", [7, 8, 9]),
    "bidi-ltr": ("abc \u05d0\u05d1\u05d2, \u0639\u0631\u0628\u0649 end ", [4, 5, 7]),
    "bidi-rtl": ("abc \u05d0\u05d1\u05d2, \u0639\u0631\u0628\u0649 end ", [4, 5, 7]),
    "lam-alef-ltr": ("\u0644\u0627", [0, 1, 2]),
    "lam-alef-rtl": ("\u0644\u0627", [0, 1, 2]),
    "mixed-lam-alef-ltr": ("alpha \u0644\u0627 beta ", [6, 7, 8]),
    "mixed-lam-alef-rtl": ("alpha \u0644\u0627 beta ", [6, 7, 8]),
}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def utf16(text):
    data = text.encode("utf-16-le", errors="surrogatepass")
    return [int.from_bytes(data[index:index + 2], "little") for index in range(0, len(data), 2)]


def verify(path, binary, process_id):
    if not path.is_file() or not 1 <= path.stat().st_size <= 128 * 1024 * 1024:
        raise ValueError("Missing/oversized geometry receipt")
    receipt = json.loads(path.read_text(encoding="utf-8"))
    if (receipt.get("schema") != "native-edit-selection-geometry-v1"
            or receipt.get("completed") is not True or receipt.get("error") is not None
            or receipt.get("hostShown") is not True or receipt.get("desktopQualified") is not False
            or receipt.get("physicalInputQualified") is not False or receipt.get("expectedCases") != 9
            or receipt.get("dpiMode") != "PerMonitorV2" or receipt.get("themeFlag") != "true"
            or not 0 <= receipt["elapsedMilliseconds"] <= 30000):
        raise ValueError("Incomplete or incorrectly qualified geometry receipt")
    identity = receipt["identity"]
    if (identity.get("processId") != process_id or identity.get("architecture") not in ("X64", "Arm64")
            or "Microsoft.WindowsDesktop.App" not in identity.get("formsPath", "")
            or identity.get("probeSha256") != sha(binary)
            or sha(Path(identity["formsPath"])) != identity.get("formsSha256")
            or not all(isinstance(identity.get(key), str) and identity[key]
                       for key in ("framework", "os", "osBuild", "locale", "uiLocale"))):
        raise ValueError("Original process/font/platform identity mismatch")
    modules = identity["nativeTextModules"]
    names = [item["name"].lower() for item in modules]
    if "usp10.dll" not in names or len(set(names)) != len(names):
        raise ValueError("Original native text modules are missing or duplicated")
    for module in modules:
        if (Path(module["path"]).name.lower() != module["name"].lower()
                or not module["fileVersion"] or sha(Path(module["path"])) != module["fileSha256"]):
            raise ValueError("Original loaded native module identity changed")
    for case in receipt["cases"]:
        verify_classification(case)
    verify_geometry(receipt)
    return {"receiptSha256": sha(path), "cases": len(receipt["cases"]),
            "geometryComplete": receipt["geometryComplete"], "qualified": False}


def verify_classification(case):
    source = case["requestedUtf16"]
    observed = case["scriptBreak"]
    if (observed.get("completed") is not True or observed.get("independentOfEdit") is not True
            or observed.get("actualUtf16") != source or observed.get("actualText") != case["requestedText"]
            or observed.get("flags") != (0x140 if case["RightToLeft"] else 0x40)
            or observed.get("hdc") != 0 or observed.get("charset") != -1
            or observed.get("attributeByteSize") != 1 or observed.get("analyseHResult") != 0
            or observed.get("freeHResult") != 0 or observed.get("analysisAllocated") is not True
            or observed.get("attributesAvailable") is not True):
        raise ValueError("Complete original classifier source/API/cleanup mismatch")
    raw, attributes = observed["rawBytes"], observed["attributes"]
    if len(raw) != len(source) or len(attributes) != len(source):
        raise ValueError("Incomplete original UTF16 classification")
    for index, (byte, attribute) in enumerate(zip(raw, attributes)):
        if (type(byte) is not int or not 0 <= byte <= 255 or attribute["index"] != index
                or attribute["utf16"] != source[index] or attribute["raw"] != byte
                or attribute["reserved"] != byte >> 5):
            raise ValueError("Original raw classification identity changed")
        for name, bit in (("softBreak", 1), ("whiteSpace", 2), ("charStop", 4), ("wordStop", 8), ("invalid", 16)):
            if attribute[name] is not bool(byte & bit):
                raise ValueError("Original classification bit decoding changed")
    classification = observed["classification"]
    if (classification.get("completed") is not True or classification["terminalPosition"] != len(source)
            or classification.get("itemByteSize") != 8 or classification.get("characterTypesSucceeded") is not True):
        raise ValueError("Incomplete original script partition")
    types = classification["ctype1"]
    if len(types) != len(source):
        raise ValueError("Incomplete original CTYPE1 source coverage")
    for index, value in enumerate(types):
        if (value["index"] != index or value["utf16"] != source[index]
                or type(value["raw"]) is not int or not 0 <= value["raw"] <= 65535):
            raise ValueError("Original CTYPE1 source identity changed")
        for name, bit in (("space", 8), ("punctuation", 16), ("control", 32), ("blank", 64), ("alphabetic", 256)):
            if value[name] is not bool(value["raw"] & bit):
                raise ValueError("Original CTYPE1 bit decoding changed")
    end = 0
    for run in classification["runs"]:
        if (run["start"] != end or not end < run["end"] <= len(source)
                or run["script"] != run["rawAnalysis"] & 0x3FF
                or run["needsWordBreaking"] is not bool(run["rawPropertiesFirst"] & (1 << 18))):
            raise ValueError("Original script partition or copied flags changed")
        end = run["end"]
    if end != len(source):
        raise ValueError("Missing original script terminal boundary")


def verify_geometry(receipt):
    """Validate original evidence structure/bytes; never manufacture geometry."""
    cases = receipt["cases"]
    if ([case["Name"] for case in cases] != list(GEOMETRY_INPUTS)
            or receipt.get("ownedWindowOnly") is not True
            or receipt.get("fallbackFontIdentityQualified") is not False):
        raise ValueError("Owned geometry case/identity inventory changed")
    total, unavailable = 0, 0
    for case in cases:
        source, seams = GEOMETRY_INPUTS[case["Name"]]
        length = len(utf16(source))
        if (case["requestedText"] != source or case["requestedUtf16"] != utf16(source)
                or case["requestedSeams"] != seams or case["RightToLeft"] is not case["Name"].endswith("-rtl")):
            raise ValueError("Geometry original source/seams/direction changed")
        metadata = case["metadata"]
        handle = metadata["handle"]
        font = metadata["baseFont"]
        descriptor = font["descriptor"]
        signed_fields = ("Height", "Width", "Escapement", "Orientation", "Weight")
        byte_fields = ("Italic", "Underline", "StrikeOut", "CharSet", "OutPrecision", "ClipPrecision", "Quality", "PitchAndFamily")
        if (type(handle) is not int or handle == 0 or metadata.get("fallbackFontIdentityQualified") is not False
                or font.get("borrowed") is not True or font.get("fallbackIdentityQualified") is not False
                or type(font["handle"]) is not int or font["handle"] == 0
                or not 1 <= font["byteCount"] <= 32 * 1024 * 1024 or not hash_text(font["sha256"])
                or not isinstance(descriptor.get("FaceName"), str) or not 1 <= len(descriptor["FaceName"]) <= 31
                or any(type(descriptor.get(key)) is not int or not -(2 ** 31) <= descriptor[key] < 2 ** 31 for key in signed_fields)
                or any(type(descriptor.get(key)) is not int or not 0 <= descriptor[key] <= 255 for key in byte_fields)
                or not 1 <= metadata["deviceDpi"] <= 960
                or font["DeviceDpi"] != metadata["deviceDpi"]):
            raise ValueError("Original owned base-font/DPI metadata incomplete")
        font_bytes = base64.b64decode(font["bytes"], validate=True)
        if len(font_bytes) != font["byteCount"] or hashlib.sha256(font_bytes).hexdigest() != font["sha256"]:
            raise ValueError("Original borrowed base-font bytes changed")
        if "lam-alef" in case["Name"]:
            if (metadata.get("requestedFont") != {"family": "Arial", "size": 20, "unit": "Pixel", "style": "Regular"}
                    or descriptor["FaceName"].lower() != "arial"
                    or font.get("managed") != {"Name": "Arial", "Size": 20, "unit": "Pixel", "style": "Regular"}):
                raise ValueError("Requested Arial identity was substituted")
        elif metadata.get("requestedFont") is not None:
            raise ValueError("Original system-font request changed")
        if ([color["index"] for color in metadata["systemColors"]] != [5, 8, 13, 14, 15, 18]
                or any(type(color.get("colorRef")) is not int or not 0 <= color["colorRef"] <= 0xFFFFFF
                       for color in metadata["systemColors"])):
            raise ValueError("Original system-color inventory changed")
        a, b, c = seams
        requested = [(f"collapsed-{position}", position, position) for position in [0, a, b, c, length]]
        for start, end in ((a, b), (b, c), (a, c)):
            requested += [(f"forward-{start}-{end}", start, end), (f"reverse-{end}-{start}", end, start)]
        gestures = [] if "lam-alef" in case["Name"] else [
            "first-down", "first-up", "double-down", "drag-last", "drag-first-reversal",
            "drag-anchor-reversal", "drag-last-again", "final-up"]
        observations = case["observations"]
        if [item["name"] for item in observations] != [item[0] for item in requested] + gestures:
            raise ValueError("Directed selection/gesture geometry inventory changed")
        caret_count = 0
        for index, observation in enumerate(observations):
            request = observation["request"]
            if index < len(requested):
                _, anchor, active = requested[index]
                if request != {"kind": "set-selection", "requestedAnchor": anchor,
                               "requestedActive": active, "result": request.get("result")}:
                    raise ValueError("Original directed selection identity changed")
            else:
                messages = [0x201, 0x202, 0x203, 0x200, 0x200, 0x200, 0x200, 0x202]
                callbacks = request.get("callbacks", [])
                if (request.get("kind") != "gesture" or request.get("message") != messages[index - len(requested)]
                        or len(callbacks) < 2 or callbacks[0].get("name") != "WndProc-enter"
                        or callbacks[-1].get("name") != "WndProc-return"
                        or any(callback.get("step") != observation["name"] for callback in callbacks)):
                    raise ValueError("Original native gesture delivery is missing")
            if type(observation.get("scrollCaretResult")) is not int:
                raise ValueError("Original EM_SCROLLCARET result is missing")
            for state_name in ("beforeScroll", "afterScroll"):
                state = observation[state_name]
                selection = state["Selection"]
                start, end = selection["Start"], selection["End"]
                if (not 0 <= start <= end <= length or selection["ManagedStart"] != start
                        or selection["ManagedLength"] != end - start or selection.get("Focused") is not True
                        or utf16(selection["SelectedText"]) != utf16(source)[start:end]):
                    raise ValueError("Original selected UTF16/focus identity changed")
                caret = state["Caret"]
                total += 1
                if caret["Available"] is True:
                    if (caret.get("UnavailableReason") is not None or caret.get("GuiQuerySucceeded") is not True
                            or caret.get("PointQuerySucceeded") is not True or caret["GuiError"] != 0 or caret["PointError"] != 0
                            or caret["CaretOwner"] != handle or caret["FocusOwner"] != handle
                            or not positive_rect(caret["Rectangle"]) or not native_point(caret["Point"])):
                        raise ValueError("Owned native caret evidence is invalid")
                    caret_count += 1
                elif caret["Available"] is False:
                    unavailable += 1
                    if not caret["UnavailableReason"] or caret["Point"] is not None or caret["Rectangle"] is not None:
                        raise ValueError("Unavailable native caret acquired invented coordinates")
                else:
                    raise ValueError("Native caret availability is not explicit")
                client = state["Client"]
                if (not positive_rect(client) or client["Left"] != 0 or client["Top"] != 0
                        or client["Right"] > 512 or client["Bottom"] > 160 or not positive_rect(state["Format"])):
                    raise ValueError("Original bounded client/format frame is invalid")
                if type(state.get("FirstVisibleRaw")) is not int:
                    raise ValueError("Original first-visible position is missing")
                for key in ("HorizontalScroll", "VerticalScroll"):
                    scroll = state[key]
                    if (type(scroll.get("Available")) is not bool
                            or any(type(scroll.get(field)) is not int for field in
                                   ("Error", "Minimum", "Maximum", "Page", "Position", "TrackPosition"))
                            or not 0 <= scroll["Page"] <= 0xFFFFFFFF
                            or (scroll["Available"] and scroll["Error"] != 0)):
                        raise ValueError("Original scroll status/metrics are incomplete")
                positions = state["Positions"]
                if [p["Index"] for p in positions] != list(range(length + 1)):
                    raise ValueError("Original EM_POSFROMCHAR inventory was filtered")
                rows = set()
                for position in positions:
                    raw = position["Raw"] & 0xFFFFFFFF
                    point = position["Point"]
                    if raw == 0xFFFFFFFF:
                        if point is not None: raise ValueError("Unavailable source position was invented")
                    else:
                        x, y = signed_short(raw), signed_short(raw >> 16)
                        if point is None or (point["X"], point["Y"]) != (x, y):
                            raise ValueError("Raw EM_POSFROMCHAR decoding changed")
                        if 0 <= y < client["Bottom"]: rows.add(y)
                expected_hits = [(x, y) for y in sorted(rows) for x in range(client["Right"])]
                hits = state["Hits"]
                if not 1 <= len(rows) <= 8 or [(hit["X"], hit["Y"]) for hit in hits] != expected_hits:
                    raise ValueError("Integer EM_CHARFROMPOS scan is incomplete")
                for hit in hits:
                    raw = hit["Raw"] & 0xFFFFFFFF
                    decoded = (None, None) if raw == 0xFFFFFFFF else (raw & 0xFFFF, raw >> 16)
                    if (hit["Character"], hit["Line"]) != decoded:
                        raise ValueError("Original raw hit identity changed")
            pixels = observation["pixels"]
            width, height, stride = pixels["Width"], pixels["Height"], pixels["Stride"]
            if (pixels.get("Format") != "BGRX8-top-down-original-bytes" or not 1 <= width <= 512
                    or not 1 <= height <= 160 or stride != width * 4
                    or (width, height) != (observation["afterScroll"]["Client"]["Right"], observation["afterScroll"]["Client"]["Bottom"])
                    or pixels.get("RgbIndependentOfClear") is not True or pixels["RgbClearDifferences"] != 0
                    or not hash_text(pixels["ClearControlSha256"])):
                raise ValueError("Original WM_PRINTCLIENT RGB evidence is incomplete")
            buffers = []
            for data_key, hash_key in (("Bytes", "Sha256"), ("ClearControlBytes", "ClearControlSha256")):
                raw = base64.b64decode(pixels[data_key], validate=True)
                if len(raw) != stride * height or hashlib.sha256(raw).hexdigest() != pixels[hash_key]:
                    raise ValueError("Original print pixels changed")
                buffers.append(raw)
            if any(buffers[0][channel::4] != buffers[1][channel::4] for channel in range(3)):
                raise ValueError("Original print RGB depends on unpainted clear bytes")
            if any(type(pixels.get(key)) is not int for key in ("FirstPrintResult", "SecondPrintResult")):
                raise ValueError("Original print message results are missing")
        if caret_count == 0 or metadata["ownedCaretSamples"] != caret_count:
            raise ValueError("Actual owned caret evidence is missing")
    if (receipt["caretSamples"] != total or receipt["unavailableCaretSamples"] != unavailable
            or receipt["geometryComplete"] is not (unavailable == 0)):
        raise ValueError("Caret availability/geometry completeness changed")


def hash_text(value):
    return isinstance(value, str) and len(value) == 64 and all(c in "0123456789abcdef" for c in value)


def positive_rect(value):
    return (isinstance(value, dict) and all(type(value.get(key)) is int for key in ("Left", "Top", "Right", "Bottom"))
            and value["Right"] > value["Left"] and value["Bottom"] > value["Top"])


def native_point(value):
    return isinstance(value, dict) and all(type(value.get(key)) is int for key in ("X", "Y"))


def signed_short(value):
    value &= 0xFFFF
    return value - 0x10000 if value >= 0x8000 else value
