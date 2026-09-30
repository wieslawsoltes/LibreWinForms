// Native macOS adapter for the unchanged popup desktop scenario. No application
// callbacks, private API, permission requests, or source-coordinate inference.
import AppKit
import ApplicationServices
import CoreGraphics
import Foundation
import ImageIO
import ScreenCaptureKit
import UniformTypeIdentifiers

private struct Rejected: Error, CustomStringConvertible {
    let description: String
}

private func require(_ condition: Bool, _ message: String) throws {
    if !condition { throw Rejected(description: message) }
}

private struct Bounds: Codable, Equatable {
    let x: Double
    let y: Double
    let width: Double
    let height: Double

    init(_ rect: CGRect) {
        x = rect.minX; y = rect.minY; width = rect.width; height = rect.height
    }

    var rect: CGRect { CGRect(x: x, y: y, width: width, height: height) }
    var valid: Bool {
        [x, y, width, height, x + width, y + height].allSatisfy { $0.isFinite }
            && width > 0 && height > 0
    }
}

private struct Window: Codable, Equatable {
    let pid: Int32
    let windowNumber: UInt32
    let title: String
    let frameBounds: Bounds
}

// CG's on-screen list is front-to-back. Keep that order separately from the
// stable, window-number-sorted owned inventory. Foreign titles are not retained.
private struct CaptureWindow: Encodable {
    let pid: Int32
    let windowNumber: UInt32
    let zIndex: Int
    let layer: Int32
    let alpha: Double
    let frameBounds: Bounds
}

private struct CaptureObservation: Encodable {
    let phase: String
    let observedUptimeSeconds: Double
    let systemWindowCount: Int
    let windows: [CaptureWindow]
}

private struct CaptureOcclusion: Encodable {
    let policy = "foreign-window-frame-intersection-v1"
    let coordinateSpace = "native-desktop-top-left-points"
    let samples: [CaptureObservation]
}

private struct CaptureRejected: Error, CustomStringConvertible {
    let description: String
    let captureOcclusion: CaptureOcclusion
}

private struct Request: Decodable {
    let action: String
    let pid: Int32?
    let windowNumber: UInt32?
    let point: [Double]?
    let key: UInt16?
    let click: String?
    let windows: [Window]?
    let output: String?
}

private func object<T: Encodable>(_ value: T) throws -> Any {
    try JSONSerialization.jsonObject(with: JSONEncoder().encode(value))
}

@MainActor
private func screenEntries() throws -> [[String: Any]] {
    guard let entries = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements],
                                                  kCGNullWindowID) as? [[String: Any]] else {
        throw Rejected(description: "Independent CG inventory unavailable")
    }
    try require(entries.count <= 4096, "CG inventory budget exceeded")
    return entries
}

@MainActor
private func inventory(_ pid: Int32, entries: [[String: Any]]) throws -> [Window] {
    try require(pid > 0 && NSRunningApplication(processIdentifier: pid)?.isTerminated == false,
                "Owned process is unavailable")
    var result: [Window] = []
    for entry in entries {
        guard (entry[kCGWindowOwnerPID as String] as? NSNumber)?.int32Value == pid else { continue }
        guard let number = entry[kCGWindowNumber as String] as? NSNumber,
              let raw = entry[kCGWindowBounds as String] as? NSDictionary,
              let rect = CGRect(dictionaryRepresentation: raw as CFDictionary) else {
            throw Rejected(description: "Owned CG window lacks actual identity/frame")
        }
        let title = entry[kCGWindowName as String] as? String ?? ""
        let bounds = Bounds(rect)
        try require(number.uint32Value != 0 && bounds.valid && title.utf8.count <= 4095,
                    "Invalid actual CG identity/frame/title")
        result.append(Window(pid: pid, windowNumber: number.uint32Value, title: title, frameBounds: bounds))
    }
    try require(result.count <= 32 && Set(result.map { $0.windowNumber }).count == result.count,
                "CG owned window identity/count budget exceeded")
    return result.sorted { $0.windowNumber < $1.windowNumber }
}

@MainActor
private func inventory(_ pid: Int32) throws -> [Window] {
    try inventory(pid, entries: screenEntries())
}

private func overlaps(_ lhs: Bounds, _ rhs: Bounds) -> Bool {
    max(lhs.x, rhs.x) < min(lhs.x + lhs.width, rhs.x + rhs.width)
        && max(lhs.y, rhs.y) < min(lhs.y + lhs.height, rhs.y + rhs.height)
}

private func cgInteger(_ value: NSNumber, minimum: Int64, maximum: Int64) -> Bool {
    CFGetTypeID(value) != CFBooleanGetTypeID() && value.doubleValue.isFinite
        && value.int64Value >= minimum && value.int64Value <= maximum
        && value.doubleValue == Double(value.int64Value)
}

private func captureObservation(_ entries: [[String: Any]], _ owned: [Window],
                                phase: String) throws -> CaptureObservation {
    var windows: [CaptureWindow] = []
    var identities = Set<UInt32>()
    for (index, entry) in entries.enumerated() {
        guard let pid = entry[kCGWindowOwnerPID as String] as? NSNumber,
              let number = entry[kCGWindowNumber as String] as? NSNumber,
              let layer = entry[kCGWindowLayer as String] as? NSNumber,
              let alpha = entry[kCGWindowAlpha as String] as? NSNumber,
              let raw = entry[kCGWindowBounds as String] as? NSDictionary,
              let rect = CGRect(dictionaryRepresentation: raw as CFDictionary) else {
            throw Rejected(description: "CG obstruction inventory lacks required metadata")
        }
        let bounds = Bounds(rect)
        try require(cgInteger(pid, minimum: 0, maximum: Int64(Int32.max))
                    && cgInteger(number, minimum: 1, maximum: Int64(UInt32.max))
                    && cgInteger(layer, minimum: Int64(Int32.min), maximum: Int64(Int32.max))
                    && CFGetTypeID(alpha) != CFBooleanGetTypeID()
                    && alpha.doubleValue.isFinite && (0...1).contains(alpha.doubleValue)
                    && [bounds.x, bounds.y, bounds.width, bounds.height,
                        bounds.x + bounds.width, bounds.y + bounds.height].allSatisfy { $0.isFinite }
                    && rect.size.width >= 0 && rect.size.height >= 0
                    && identities.insert(number.uint32Value).inserted,
                    "Invalid/duplicate CG obstruction inventory metadata")
        // Inspect every system entry, but retain only actual positive-area
        // intersections. Empty/nonintersecting foreign frames cannot cover an
        // owned frame. More evidence than the bounded receipt permits rejects.
        guard owned.contains(where: { overlaps($0.frameBounds, bounds) }) else { continue }
        windows.append(CaptureWindow(pid: pid.int32Value, windowNumber: number.uint32Value,
                                     zIndex: index, layer: layer.int32Value,
                                     alpha: alpha.doubleValue, frameBounds: bounds))
        try require(windows.count <= 64, "CG obstruction evidence budget exceeded")
    }
    return CaptureObservation(phase: phase, observedUptimeSeconds: ProcessInfo.processInfo.systemUptime,
                              systemWindowCount: entries.count, windows: windows)
}

private func requireUnobstructed(_ samples: [CaptureObservation], _ pid: Int32) throws {
    let sample = samples[samples.count - 1]
    for owned in sample.windows where owned.pid == pid {
        for foreign in sample.windows where foreign.pid != pid && foreign.alpha > 0 {
            if foreign.zIndex < owned.zIndex && overlaps(foreign.frameBounds, owned.frameBounds) {
                throw CaptureRejected(
                    description: "Foreign CG window \(foreign.windowNumber) PID \(foreign.pid) potentially occludes owned window \(owned.windowNumber) (\(sample.phase))",
                    captureOcclusion: CaptureOcclusion(samples: samples))
            }
        }
    }
}

@MainActor
private func foreground(_ pid: Int32) throws {
    try require(pid > 0 && NSRunningApplication(processIdentifier: pid)?.isTerminated == false
                && NSWorkspace.shared.frontmostApplication?.processIdentifier == pid,
                "Foreground belongs to another process; no input/capture permitted")
}

private func permissions() throws {
    // Preflight only: never request TCC authorization or dismiss an OS dialog.
    try require(AXIsProcessTrusted(), "Accessibility permission is unavailable")
    try require(CGPreflightPostEventAccess(), "CGEvent posting permission is unavailable")
    try require(CGPreflightListenEventAccess(), "Physical input-state permission is unavailable")
    try require(CGPreflightScreenCaptureAccess(), "Screen capture permission is unavailable")
}

@MainActor
private func held(_ key: CGKeyCode? = nil) throws {
    var keys: Set<CGKeyCode> = [54, 55, 56, 60, 58, 61, 59, 62]
    if let key { keys.insert(key) }
    try require(!keys.contains { CGEventSource.keyState(.hidSystemState, key: $0) },
                "Requested physical key/modifier held; refusing an injected release")
    // NSEvent exposes the complete current button bitmask, including additional
    // buttons that are not cases of the three-value CGMouseButton enum.
    try require(NSEvent.pressedMouseButtons == 0, "A mouse button is already held")
    for button in [CGMouseButton.left, .right, .center] {
        try require(!CGEventSource.buttonState(.hidSystemState, button: button),
                    "Physical mouse button held; refusing pointer input")
    }
}

private func hitOwner(_ point: CGPoint, _ pid: Int32) throws {
    let system = AXUIElementCreateSystemWide()
    try require(AXUIElementSetMessagingTimeout(system, 0.25) == .success,
                "Could not bound system-wide accessibility hit testing")
    var element: AXUIElement?
    let status = AXUIElementCopyElementAtPosition(system, Float(point.x), Float(point.y), &element)
    try require(status == .success && element != nil,
                "Actual system-wide AX hit unavailable (\(status.rawValue)); no pointer input")
    var actual: pid_t = 0
    try require(AXUIElementGetPid(element!, &actual) == .success && actual == pid,
                "Observed point is covered by another process")
}

private func eventSource() throws -> CGEventSource {
    guard let source = CGEventSource(stateID: .hidSystemState) else {
        throw Rejected(description: "Native input source unavailable")
    }
    return source
}

@MainActor
private func pointer(_ request: Request, _ pid: Int32) throws {
    try foreground(pid)
    try held()
    guard let values = request.point, values.count == 2,
          values.allSatisfy({ $0.isFinite && abs($0) <= 1_000_000 }) else {
        throw Rejected(description: "Invalid declared native point")
    }
    try require(request.click == nil || request.click == "left" || request.click == "right",
                "Unknown mouse operation")
    let point = CGPoint(x: values[0], y: values[1])
    try hitOwner(point, pid)
    let source = try eventSource()
    guard let move = CGEvent(mouseEventSource: source, mouseType: .mouseMoved,
                             mouseCursorPosition: point, mouseButton: .left) else {
        throw Rejected(description: "Native mouse event creation failed")
    }
    move.post(tap: .cghidEventTap)
    // CGEventPost has no delivery result. Independently observe the pointer;
    // later unchanged scenario assertions, not posting, prove application input.
    var arrived = false
    for _ in 0..<20 {
        if CGEvent(source: nil)?.location == point { arrived = true; break }
        Thread.sleep(forTimeInterval: 0.005)
    }
    try require(arrived, "Native pointer did not reach the admitted point")
    try foreground(pid)
    try held()
    try hitOwner(point, pid)
    if let click = request.click {
        let button: CGMouseButton = click == "left" ? .left : .right
        let downType: CGEventType = click == "left" ? .leftMouseDown : .rightMouseDown
        let upType: CGEventType = click == "left" ? .leftMouseUp : .rightMouseUp
        guard let down = CGEvent(mouseEventSource: source, mouseType: downType,
                                 mouseCursorPosition: point, mouseButton: button),
              let up = CGEvent(mouseEventSource: source, mouseType: upType,
                               mouseCursorPosition: point, mouseButton: button) else {
            throw Rejected(description: "Native mouse pair creation failed")
        }
        down.setIntegerValueField(.mouseEventClickState, value: 1)
        up.setIntegerValueField(.mouseEventClickState, value: 1)
        down.post(tap: .cghidEventTap)
        // Both events exist before posting down. No throwing work can strand a
        // driver-owned press, and no unrelated modifier/button is released.
        up.post(tap: .cghidEventTap)
    }
}

@MainActor
private func key(_ request: Request, _ pid: Int32) throws {
    // Carbon Events.h kVK_F10/DownArrow/Escape/Option/Return, not text guessing.
    guard let code = request.key, [UInt16(109), 125, 53, 58, 36].contains(code) else {
        throw Rejected(description: "Unsupported shared-scenario key")
    }
    try foreground(pid)
    try held(code)
    let source = try eventSource()
    guard let down = CGEvent(keyboardEventSource: source, virtualKey: code, keyDown: true),
          let up = CGEvent(keyboardEventSource: source, virtualKey: code, keyDown: false) else {
        throw Rejected(description: "Native keyboard pair creation failed")
    }
    down.post(tap: .cghidEventTap)
    up.post(tap: .cghidEventTap)
}

@MainActor
private func capture(_ request: Request, _ pid: Int32) async throws -> [String: Any] {
    guard #available(macOS 15.2, *) else {
        throw Rejected(description: "ScreenCaptureKit rectangle capture requires macOS 15.2+")
    }
    try foreground(pid)
    let beforeEntries = try screenEntries()
    let before = try inventory(pid, entries: beforeEntries)
    try require(!before.isEmpty && request.windows == before, "Capture native ownership/geometry changed")
    guard let output = request.output, output.hasSuffix(".bmp"),
          !FileManager.default.fileExists(atPath: output) else {
        throw Rejected(description: "Capture output must be a fresh explicit BMP path")
    }
    var rectangle = before[0].frameBounds.rect
    for window in before.dropFirst() { rectangle = rectangle.union(window.frameBounds.rect) }
    try require(rectangle.width <= 4096 && rectangle.height <= 4096
                && rectangle.width * rectangle.height <= 4_194_304, "Native capture extent exceeds budget")
    var samples = [try captureObservation(beforeEntries, before, phase: "before")]
    try requireUnobstructed(samples, pid)
    // ScreenCaptureKit determines returned resolution. No width/height override,
    // fitted scale, drawing context, window compositing, or image resizing.
    let image = try await SCScreenshotManager.captureImage(in: rectangle)
    try foreground(pid)
    let afterEntries = try screenEntries()
    try require(try inventory(pid, entries: afterEntries) == before, "Native window state changed during capture")
    samples.append(try captureObservation(afterEntries, before, phase: "after"))
    try requireUnobstructed(samples, pid)
    try require(image.width > 0 && image.width <= 4096 && image.height > 0 && image.height <= 4096
                && image.width * image.height <= 4_194_304 && image.bitsPerPixel <= 32,
                "Actual returned native image exceeds shared pixel budget")
    let encoded = NSMutableData()
    guard let destination = CGImageDestinationCreateWithData(encoded, UTType.bmp.identifier as CFString, 1, nil) else {
        throw Rejected(description: "Native BMP encoder unavailable; no capture fallback")
    }
    CGImageDestinationAddImage(destination, image, nil)
    try require(CGImageDestinationFinalize(destination), "Native BMP encoding failed")
    try require(encoded.length <= 16 * 1024 * 1024 + 54, "Encoded capture exceeds shared byte budget")
    try (encoded as Data).write(to: URL(fileURLWithPath: output), options: .withoutOverwriting)
    return ["captureProvider": "ScreenCaptureKit.captureImage(in:)", "sourceRect": try object(Bounds(rectangle)),
            "width": image.width, "height": image.height, "bytesPerRow": image.bytesPerRow,
            "bitsPerPixel": image.bitsPerPixel, "bitsPerComponent": image.bitsPerComponent,
            "bitmapInfo": image.bitmapInfo.rawValue, "alphaInfo": image.alphaInfo.rawValue,
            "colorSpace": image.colorSpace?.name as String? ?? "unnamed",
            "returnedPixelsPerPointX": Double(image.width) / rectangle.width,
            "returnedPixelsPerPointY": Double(image.height) / rectangle.height,
            "encodedBytes": encoded.length, "encoding": "ImageIO BMP, no resizing",
            "captureOcclusion": try object(CaptureOcclusion(samples: samples)),
            "qualified": false, "usableWindowPixelsVerified": false]
}

@main
private struct PopupDesktopNative {
    @MainActor static func main() async {
        do {
            guard #available(macOS 15.2, *) else {
                throw Rejected(description: "This driver requires macOS 15.2+ ScreenCaptureKit")
            }
            let bytes = try FileHandle.standardInput.read(upToCount: 32769) ?? Data()
            try require(!bytes.isEmpty && bytes.count <= 32768, "Native request exceeds 32 KiB")
            let request = try JSONDecoder().decode(Request.self, from: bytes)
            try permissions()
            var result: [String: Any] = ["schema": "popup-macos-native-v1", "success": true,
                                         "action": request.action, "qualified": false]
            if request.action == "preflight" {
                result["os"] = ProcessInfo.processInfo.operatingSystemVersionString
                result["captureProvider"] = "ScreenCaptureKit.captureImage(in:)"
            } else {
                guard let pid = request.pid, pid > 0 else { throw Rejected(description: "Missing owned PID") }
                result["pid"] = pid
                switch request.action {
                case "inventory": result["windows"] = try object(inventory(pid))
                case "foreground": try foreground(pid)
                case "activate":
                    try require(try inventory(pid).contains { $0.windowNumber == request.windowNumber },
                                "Activation target is not an actual owned window")
                    try require(NSRunningApplication(processIdentifier: pid)?.activate(options: []) == true,
                                "Owned application activation request failed")
                case "pointer": try pointer(request, pid)
                case "key": try key(request, pid)
                case "capture": result["image"] = try await capture(request, pid)
                default: throw Rejected(description: "Unknown native operation")
                }
            }
            let data = try JSONSerialization.data(withJSONObject: result, options: [.sortedKeys])
            try require(data.count <= 65536, "Native reply budget exceeded")
            FileHandle.standardOutput.write(data)
        } catch {
            var reply: [String: Any] = ["schema": "popup-macos-native-v1", "success": false,
                                       "qualified": false, "error": String(describing: error).prefix(2048).description]
            if let rejected = error as? CaptureRejected {
                reply["captureOcclusion"] = try? object(rejected.captureOcclusion)
            }
            if let data = try? JSONSerialization.data(withJSONObject: reply, options: [.sortedKeys]), data.count <= 65536 {
                FileHandle.standardOutput.write(data)
            }
            exit(1)
        }
    }
}
