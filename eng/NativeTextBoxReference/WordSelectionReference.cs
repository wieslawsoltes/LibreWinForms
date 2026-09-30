// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Forms;

// This is an observation of the original EDIT implementation, not an alternate
// word breaker. Coordinates come from EM_POSFROMCHAR; no text width is guessed.
internal static class WordSelectionReference
{
    private const uint MouseMove = 0x0200, LeftDown = 0x0201, LeftUp = 0x0202, LeftDouble = 0x0203;
    private const uint GetSelection = 0x00B0, PositionFromCharacter = 0x00D6, CharacterFromPosition = 0x00D7;
    private const uint GetWordBreakProcedure = 0x00D1, ScriptBreak = 0x40, ScriptRightToLeft = 0x100;
    private const int MaximumCallbacks = 128;

    private sealed record Case(string Name, string Text, int[] Indices,
        bool Multiline = false, bool Password = false, bool ReadOnly = false, bool RightToLeft = false, bool Wrap = false);

    private static readonly Case[] Cases =
    [
        new("spaces", "  alpha  beta  ", [0, 2, 4, 6, 7, 9, 12, 13]),
        new("punctuation", "alpha,beta.gamma! (tail) - end ", [0, 4, 5, 6, 10, 15, 17, 21, 23, 25, 27]),
        new("tabs-single", "one\t\ttwo \tthree ", [2, 3, 4, 5, 7, 9, 10, 14]),
        new("tabs-multiline", "one\t\ttwo \tthree ", [2, 3, 4, 5, 7, 9, 10, 14], Multiline: true),
        new("crlf-single", "alpha\r\nbeta\r\n\r\ngamma ", [0, 4, 5, 7, 10, 13, 15, 19]),
        new("crlf-multiline", "alpha\r\nbeta\r\n\r\ngamma ", [0, 4, 5, 7, 10, 13, 15, 19], Multiline: true),
        new("surrogate-combining", "go A\U0001F600 e\u0301 fin ", [0, 3, 4, 5, 7, 8, 10, 12]),
        new("bidi-ltr", "abc \u05D0\u05D1\u05D2, \u0639\u0631\u0628\u0649 end ", [0, 2, 4, 6, 7, 9, 12, 14]),
        new("bidi-rtl", "abc \u05D0\u05D1\u05D2, \u0639\u0631\u0628\u0649 end ", [0, 2, 4, 6, 7, 9, 12, 14], RightToLeft: true),
        new("password", "alpha, beta\tend ", [0, 4, 5, 6, 7, 10, 11, 13], Password: true),
        new("read-only", "  alpha,beta \ttail ", [0, 2, 6, 7, 8, 11, 12, 14, 16], ReadOnly: true),
        new("wrapped-multiline", "alpha beta gamma delta end ", [0, 4, 6, 9, 11, 15, 17, 21], Multiline: true, Wrap: true),
        new("unicode-spaces", "a\u00A0b\u2003c\u202Fd\u3000e ", [0, 1, 2, 3, 4, 5, 6, 7, 8]),
        new("symbols", "a\u00A9b\u2603c\U0001F600d ", [0, 1, 2, 3, 4, 5, 6, 7]),
        new("isolated-breaks-single", "ab\rcd\nef ", [1, 2, 3, 4, 5, 6, 7]),
        new("isolated-breaks-multiline", "ab\rcd\nef ", [1, 2, 3, 4, 5, 6, 7], Multiline: true),
        new("crcrlf-multiline", "ab\r\r\ncd ", [1, 2, 3, 4, 5, 6], Multiline: true),
        new("wrapped-longword", "abcdefghijklmnopqrstuvwxyz0123456789 ", [0, 4, 8, 12, 16, 20, 24, 28, 32, 35], Multiline: true, Wrap: true)
    ];

    internal static int Run(string path, string dpiMode, string themeFlag)
    {
        string output = Path.GetFullPath(path);
        // CreateNew preserves prior evidence, including failed attempts.
        using FileStream file = new(output, FileMode.CreateNew, FileAccess.Write);
        Stopwatch budget = Stopwatch.StartNew();
        var results = new List<object>();
        object? identity = null;
        string? failure = null;
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Original Windows EDIT is required.");
            string formsPath = typeof(Control).Assembly.Location;
            if (!formsPath.Contains("Microsoft.WindowsDesktop.App", StringComparison.OrdinalIgnoreCase)
                || typeof(Control).Assembly.GetName().Name != "System.Windows.Forms")
                throw new InvalidOperationException("Not the original Microsoft WindowsDesktop WinForms reference.");
            identity = new
            {
                framework = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                processId = Environment.ProcessId,
                forms = typeof(Control).Assembly.FullName,
                formsPath,
                formsSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(formsPath))).ToLowerInvariant(),
                probeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(WordSelectionReference).Assembly.Location))).ToLowerInvariant()
            };
            HighDpiMode mode = Enum.Parse<HighDpiMode>(dpiMode);
            bool themed = bool.Parse(themeFlag);
            if (!Application.SetHighDpiMode(mode)) throw new InvalidOperationException("DPI policy rejected.");
            if (themed) Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using Form form = new()
            {
                AutoScaleMode = AutoScaleMode.None,
                ClientSize = new Size(520, 180),
                ShowInTaskbar = false
            };
            _ = form.Handle;
            foreach (Case item in Cases)
            {
                var gestures = new List<object>();
                var scriptBreak = new Dictionary<string, object?>();
                int completedGestures = 0;
                // Publish the case before work so failures retain completed gestures.
                results.Add(new { item.Name, requestedText = item.Text, requestedUtf16 = Utf16(item.Text),
                    item.Multiline, item.Password, item.ReadOnly, item.RightToLeft, item.Wrap, item.Indices, scriptBreak, gestures });
                foreach (int index in item.Indices)
                foreach (int quarter in new[] { 1, 3 })
                {
                    CheckBudget(budget);
                    using Probe editor = new()
                    {
                        AutoSize = false,
                        BorderStyle = BorderStyle.FixedSingle,
                        Bounds = new Rectangle(10, 10, item.Wrap ? 96 : 480, 140),
                        Multiline = item.Multiline,
                        WordWrap = item.Wrap,
                        ReadOnly = item.ReadOnly,
                        PasswordChar = item.Password ? '*' : '\0',
                        RightToLeft = item.RightToLeft ? RightToLeft.Yes : RightToLeft.No,
                        Font = SystemFonts.DefaultFont,
                        Text = item.Text
                    };
                    form.Controls.Add(editor);
                    nint handle = editor.Handle;
                    RequireOwner(editor, handle);
                    string initialText = editor.Text;
                    if (scriptBreak.Count == 0)
                        ObserveScriptBreak(initialText, item.RightToLeft, scriptBreak);
                    else if (!Equals(scriptBreak["actualText"], initialText))
                        throw new InvalidOperationException("EDIT text changed between independent case observations.");
                    // Borrowed diagnostic address only: do not invoke an unknown callback.
                    nint wordBreakProcedure = SendMessageW(handle, GetWordBreakProcedure, 0, 0);
                    RequireOwner(editor, handle);
                    var positions = ReadPositions(handle, initialText.Length);
                    if (item.Name == "wrapped-longword"
                        && positions.Where(p => p.Index < initialText.Length && p.Point.HasValue)
                            .Select(p => p.Point!.Value.Y).Distinct().Count() < 2)
                        throw new InvalidOperationException("The native long word did not span multiple observed rows.");
                    Hit? anchor = ResolveHit(editor, handle, positions, index, quarter);
                    var steps = new List<object>();
                    gestures.Add(new
                    {
                        requestedIndex = index, quarter, actualText = initialText, actualUtf16 = Utf16(initialText),
                        editor.DeviceDpi, editor.ClientSize, editor.Multiline, editor.ReadOnly,
                        passwordCharacter = (int)editor.PasswordChar, rightToLeft = editor.RightToLeft.ToString(),
                        font = new { editor.Font.Name, editor.Font.Size, unit = editor.Font.Unit.ToString(), style = editor.Font.Style.ToString() },
                        wordBreakProcedure = new { message = GetWordBreakProcedure, address = wordBreakProcedure.ToInt64(),
                            available = wordBreakProcedure != 0, invoked = false },
                        handle = handle.ToInt64(), nativeClass = ClassName(handle), positions, anchor,
                        coordinateUnavailable = anchor is null,
                        unavailableReason = anchor is null ? "No distinct same-row in-client native coordinate span for this UTF-16 request." : null,
                        steps
                    });
                    // Delimiters, coincident UTF-16 cluster positions and clipped
                    // characters are retained as unavailable, never invented boxes.
                    if (anchor is null) continue;

                    editor.BeginObservation();
                    Send("first-down", LeftDown, anchor);
                    Send("first-up", LeftUp, anchor);
                    Send("double-down", LeftDouble, anchor);
                    Selection doubled = ReadSelection(editor, handle);
                    if (editor.DoubleDownCount != 1) throw new InvalidOperationException("The actual double-down callback was not delivered exactly once.");
                    if (!doubled.CaptureOwned) throw new InvalidOperationException("EDIT did not own capture for word drag.");
                    if (item.Name == "spaces" && index == 4 && doubled.Start == doubled.End)
                        throw new InvalidOperationException("The interior ASCII word control did not select any text.");
                    Position[] dragPositions = ReadPositions(handle, initialText.Length);
                    Hit[] available = Enumerable.Range(0, initialText.Length)
                        .Select(i => ResolveHit(editor, handle, dragPositions, i, 1))
                        .OfType<Hit>().ToArray();
                    if (available.Length == 0) throw new InvalidOperationException("No native drag positions are available.");
                    // Logical first/last are labels, not assumptions about bidi X.
                    // Coordinates and current native hit indices accompany every step.
                    Send("drag-last", MouseMove, available[^1]);
                    Send("drag-first-reversal", MouseMove, available[0]);
                    Send("drag-anchor-reversal", MouseMove, anchor);
                    Send("drag-last-again", MouseMove, available[^1]);
                    Send("final-up", LeftUp, available[^1]);
                    if (GetCapture() == handle) throw new InvalidOperationException("EDIT retained capture after release.");
                    completedGestures++;

                    void Send(string name, uint message, Hit planned)
                    {
                        CheckBudget(budget);
                        RequireOwner(editor, handle);
                        Hit current = ResolveHit(editor, handle, ReadPositions(handle, initialText.Length), planned.Index, planned.Quarter)
                            ?? throw new InvalidOperationException($"Native position retired before {name}.");
                        editor.Step = name;
                        int callbackStart = editor.Callbacks.Count;
                        Selection before = ReadSelection(editor, handle);
                        nint result = SendMessageW(handle, message, message == LeftUp ? 0 : 1, Pack(current.Point));
                        RequireOwner(editor, handle);
                        Selection after = ReadSelection(editor, handle);
                        steps.Add(new { name, message, wParam = message == LeftUp ? 0 : 1, hit = current,
                            result = result.ToInt64(), before, after, callbacks = editor.Callbacks.Skip(callbackStart).ToArray() });
                        if (editor.CallbackOverflow) throw new InvalidOperationException("Callback receipt budget exceeded.");
                        if (editor.Text != initialText) throw new InvalidOperationException("Pointer-only input changed source text.");
                        if (after.Start < 0 || after.End < after.Start || after.End > initialText.Length
                            || after.ManagedStart != after.Start || after.ManagedLength != after.End - after.Start
                            || after.SelectedText != initialText.Substring(after.Start, after.End - after.Start))
                            throw new InvalidOperationException("Native/public UTF-16 selection views disagree.");
                        if (message != LeftUp && !after.CaptureOwned)
                            throw new InvalidOperationException("Native selection input lost owned capture.");
                    }
                }
                if (gestures.Count != item.Indices.Length * 2)
                    throw new InvalidOperationException("Incomplete requested coordinate inventory.");
                if (completedGestures == 0)
                    throw new InvalidOperationException($"No native gesture could be observed for {item.Name}.");
            }
        }
        catch (Exception error)
        {
            failure = error.ToString();
            Console.Error.WriteLine(failure);
        }
        var receipt = new
        {
            schema = "native-edit-word-selection-v1",
            completed = failure is null,
            desktopQualified = false,
            physicalInputQualified = false,
            hostShown = false,
            transport = "synchronous owned-HWND native messages; no physical input or timing classification",
            dpiMode, themeFlag, identity, expectedCases = Cases.Length, cases = results,
            elapsedMilliseconds = budget.ElapsedMilliseconds, error = failure
        };
        JsonSerializer.Serialize(file, receipt, new JsonSerializerOptions { WriteIndented = true });
        file.Flush();
        return failure is null ? 0 : 1;
    }

    private static void CheckBudget(Stopwatch budget)
    {
        if (budget.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Native EDIT observation exceeded 30 seconds.");
    }

    private static int[] Utf16(string value) => value.Select(c => (int)c).ToArray();

    private static void ObserveScriptBreak(string text, bool rightToLeft, Dictionary<string, object?> result)
    {
        // Independent Uniscribe evidence, NOT a claim that EDIT uses these flags.
        // https://learn.microsoft.com/windows/win32/api/usp10/nf-usp10-scriptstringanalyse
        // https://learn.microsoft.com/windows/win32/api/usp10/ns-usp10-script_logattr
        // No clipping/hotkey transformation: each original UTF-16 unit has one
        // SCRIPT_LOGATTR byte. Never marshal native BYTE bitfields as C# bools.
        uint flags = ScriptBreak | (rightToLeft ? ScriptRightToLeft : 0);
        int glyphCapacity = checked((text.Length * 3 + 1) / 2 + 16);
        result["api"] = "ScriptStringAnalyse/ScriptString_pLogAttr/ScriptStringFree";
        result["independentOfEdit"] = true;
        result["actualText"] = text;
        result["actualUtf16"] = Utf16(text);
        result["flags"] = flags;
        result["hdc"] = 0;
        result["charset"] = -1;
        result["glyphCapacity"] = glyphCapacity;
        result["attributeByteSize"] = 1;
        result["completed"] = false;
        nint analysis = 0;
        bool ownsAnalysis = false;
        Exception? primaryError = null;
        try
        {
            if (text.Length == 0) throw new InvalidOperationException("Break analysis requires nonempty UTF-16 input.");
            int analyseResult = ScriptStringAnalyse(0, text, text.Length, glyphCapacity, -1, flags, 0,
                0, 0, 0, 0, 0, out analysis);
            result["analyseHResult"] = analyseResult;
            result["analyseHResultHex"] = $"0x{unchecked((uint)analyseResult):X8}";
            result["analysisAllocated"] = analysis != 0;
            if (analyseResult != 0 || analysis == 0)
                throw new InvalidOperationException($"ScriptStringAnalyse failed: 0x{unchecked((uint)analyseResult):X8}.");
            ownsAnalysis = true;
            nint attributes = ScriptString_pLogAttr(analysis);
            result["attributesAvailable"] = attributes != 0;
            if (attributes == 0) throw new InvalidOperationException("ScriptString_pLogAttr returned null.");
            byte[] bytes = new byte[text.Length];
            Marshal.Copy(attributes, bytes, 0, bytes.Length);
            // The copy, including reserved bits, survives ScriptStringFree.
            result["rawBytes"] = bytes.Select(b => (int)b).ToArray();
            result["attributes"] = bytes.Select((value, index) => new
            {
                index, utf16 = (int)text[index], raw = (int)value,
                softBreak = (value & 1) != 0, whiteSpace = (value & 2) != 0,
                charStop = (value & 4) != 0, wordStop = (value & 8) != 0,
                invalid = (value & 16) != 0, reserved = value >> 5
            }).ToArray();
        }
        catch (Exception error)
        {
            primaryError = error;
            result["error"] = error.ToString();
            throw;
        }
        finally
        {
            // A failed HRESULT does not transfer an analysis object to us.
            if (ownsAnalysis)
            {
                try
                {
                    int freeResult = ScriptStringFree(ref analysis);
                    result["freeHResult"] = freeResult;
                    result["freeHResultHex"] = $"0x{unchecked((uint)freeResult):X8}";
                    if (freeResult != 0)
                        throw new InvalidOperationException($"ScriptStringFree failed: 0x{unchecked((uint)freeResult):X8}.");
                }
                catch (Exception error)
                {
                    result["freeError"] = error.ToString();
                    if (primaryError is null) throw;
                }
            }
        }
        result["completed"] = true;
    }

    private sealed record Position(int Index, long Raw, Point? Point);
    private sealed record Hit(int Index, int Quarter, int FollowingIndex, Point Start, Point Following,
        Point Point, int NativeCharacter, int NativeLine, long RawHit);
    private sealed record Selection(int Start, int End, int ManagedStart, int ManagedLength,
        string SelectedText, bool Focused, bool CaptureOwned, long CaptureHandle, string PhysicalButtons);

    private static Position[] ReadPositions(nint handle, int length)
        => Enumerable.Range(0, length + 1).Select(index =>
        {
            long raw = SendMessageW(handle, PositionFromCharacter, index, 0).ToInt64();
            return new Position(index, raw, unchecked((int)raw) == -1 ? null
                : new Point(unchecked((short)raw), unchecked((short)(raw >> 16))));
        }).ToArray();

    private static Hit? ResolveHit(Probe editor, nint handle, Position[] positions, int index, int quarter)
    {
        if ((uint)index >= (uint)(positions.Length - 1) || positions[index].Point is not Point start) return null;
        int next = index + 1;
        while (next < positions.Length && positions[next].Point == start) next++;
        if (next == positions.Length || positions[next].Point is not Point following || following.Y != start.Y
            || Math.Abs(following.X - start.X) < 2) return null;
        Point point = new(start.X + (following.X - start.X) * quarter / 4, start.Y);
        if (!editor.ClientRectangle.Contains(point)) return null;
        long rawHit = SendMessageW(handle, CharacterFromPosition, 0, Pack(point)).ToInt64();
        if (unchecked((int)rawHit) == -1) return null;
        int character = (int)(rawHit & 0xffff), line = (int)((rawHit >> 16) & 0xffff);
        if (character >= editor.TextLength) return null;
        return new Hit(index, quarter, next, start, following, point, character, line, rawHit);
    }

    private static Selection ReadSelection(Probe editor, nint handle)
    {
        SendMessageSelection(handle, GetSelection, out uint start, out uint end);
        nint capture = GetCapture();
        return new(checked((int)start), checked((int)end), editor.SelectionStart, editor.SelectionLength,
            editor.SelectedText, editor.Focused, capture == handle, capture.ToInt64(), Control.MouseButtons.ToString());
    }

    private static void RequireOwner(Probe editor, nint handle)
    {
        if (!editor.IsHandleCreated || editor.Handle != handle || editor.IsDisposed
            || GetWindowThreadProcessId(handle, out uint process) == 0 || process != Environment.ProcessId)
            throw new InvalidOperationException("Owned EDIT handle retired.");
        string className = ClassName(handle);
        if (!className.Equals("EDIT", StringComparison.OrdinalIgnoreCase)
            && !className.StartsWith("WindowsForms10.EDIT.", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unexpected native class {className}.");
    }

    private static string ClassName(nint handle)
    {
        char[] value = new char[256];
        int count = GetClassNameW(handle, value, value.Length);
        if (count == 0) throw new System.ComponentModel.Win32Exception();
        if (count >= value.Length - 1) throw new InvalidOperationException("Native class name was truncated.");
        return new string(value, 0, count);
    }

    private sealed class Probe : TextBox
    {
        internal string Step = "setup";
        internal readonly List<object> Callbacks = [];
        internal bool CallbackOverflow;
        internal int DoubleDownCount;
        private bool _observing;

        internal void BeginObservation()
        {
            MouseDown += (_, e) => Record("MouseDown", e);
            MouseMove += (_, e) => Record("MouseMove", e);
            MouseUp += (_, e) => Record("MouseUp", e);
            Click += (_, _) => Record("Click");
            MouseClick += (_, e) => Record("MouseClick", e);
            DoubleClick += (_, _) => Record("DoubleClick");
            MouseDoubleClick += (_, e) => Record("MouseDoubleClick", e);
            MouseCaptureChanged += (_, _) => Record("MouseCaptureChanged");
            _observing = true;
        }

        private void Record(string name, MouseEventArgs? e = null)
        {
            if (!_observing || !IsHandleCreated) return;
            if (Callbacks.Count >= MaximumCallbacks) { CallbackOverflow = true; return; }
            if (name == "MouseDown" && e?.Clicks == 2) DoubleDownCount++;
            Callbacks.Add(new { sequence = Callbacks.Count, step = Step, name,
                mouse = e is null ? null : new { button = e.Button.ToString(), e.Clicks, e.X, e.Y },
                selection = ReadSelection(this, Handle) });
        }

        protected override void WndProc(ref Message m)
        {
            bool pointer = (uint)m.Msg is WordSelectionReference.MouseMove or WordSelectionReference.LeftDown
                or WordSelectionReference.LeftUp or WordSelectionReference.LeftDouble;
            if (pointer) Record("WndProc-enter");
            base.WndProc(ref m);
            if (pointer) Record("WndProc-return");
        }
    }

    private static nint Pack(Point point) => unchecked((nint)((uint)(ushort)point.X | (uint)(ushort)point.Y << 16));
    [DllImport("user32", EntryPoint = "SendMessageW")] private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32", EntryPoint = "SendMessageW")] private static extern nint SendMessageSelection(nint hwnd, uint message, out uint start, out uint end);
    [DllImport("user32")] private static extern nint GetCapture();
    [DllImport("user32", SetLastError = true)] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetClassNameW(nint hwnd, [Out] char[] name, int capacity);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("usp10.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int ScriptStringAnalyse(nint hdc, [MarshalAs(UnmanagedType.LPWStr)] string text,
        int characterCount, int glyphCapacity, int charset, uint flags, int requiredWidth,
        nint control, nint state, nint advances, nint tabs, nint inputClasses, out nint analysis);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("usp10.dll", ExactSpelling = true)] private static extern nint ScriptString_pLogAttr(nint analysis);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("usp10.dll", ExactSpelling = true)] private static extern int ScriptStringFree(ref nint analysis);
}
