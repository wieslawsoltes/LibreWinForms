// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using LibreWinForms.ProGPU;
using LibreWinForms.ProGPU.Tests;
using ProGPU.Backend;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Fact]
    public void PortableTextWordSelection_PasswordCharDoubleDownAndReversedDragSelectAll()
        => CheckPasswordWordDrag(systemPassword: false, readOnly: false);

    [Fact]
    public void PortableTextWordSelection_SystemPasswordDoubleDownAndReversedDragSelectAll()
        => CheckPasswordWordDrag(systemPassword: true, readOnly: false);

    [Fact]
    public void PortableTextWordSelection_ReadOnlyPasswordDoubleDownAndDragSelectAll()
        => CheckPasswordWordDrag(systemPassword: true, readOnly: true);

    private static void CheckPasswordWordDrag(bool systemPassword, bool readOnly,
        [CallerMemberName] string method = "")
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true, method: method)) return;
        RunRetainedEditor((platform, owner, editor, probe) =>
        {
            Point point = PreparePasswordWordSelection(editor, probe, systemPassword);
            editor.ReadOnly = readOnly;
            using PasswordWordPointer input = new(platform, owner, editor);
            input.Click(point, 1);
            Assert.Equal(2, editor.SelectionStart);
            Assert.Equal(0, editor.SelectionLength);
            List<string> events = [];
            editor.BeforeMouseDown = e =>
            {
                Assert.Equal(2, e.Clicks);
                AssertPasswordRange(editor);
                events.Add("derived");
            };
            editor.MouseDown += (_, _) => { AssertPasswordRange(editor); events.Add("public"); };
            int selections = 0;
            editor.Invalidated += (_, _) => selections++;
            int edits = 0;
            editor.TextChanged += (_, _) => edits++;
            int layouts = probe.Layouts.Count;
            probe.Layouts.Last().AfterHitTest = () => throw new InvalidOperationException("Password words do not hit-test a mask.");

            input.Down(point, 2);
            foreach (Point drag in new[] { new Point(-20, point.Y), new Point(editor.Width + 20, point.Y), point })
            {
                input.Drag(drag);
                AssertPasswordRange(editor);
                Assert.True(editor.Capture);
            }

            Assert.Equal(new[] { "derived", "public" }, events);
            Assert.Equal(1, selections);
            Assert.Equal(0, edits);
            Assert.Equal(layouts, probe.Layouts.Count);
            Assert.Equal(new string(systemPassword ? '\u25CF' : '#', editor.TextLength), probe.LastText);
            input.Up(point, 2);
            Assert.False(editor.Capture);
            AssertPasswordRange(editor);
        });
    }

    [Fact]
    public void PortableTextWordSelection_DownHandlerSelectionWinsWhileCaptureRemains()
        => CheckPasswordWordHandlerOverride("selection");

    [Fact]
    public void PortableTextWordSelection_DownHandlerTextReplacementWinsWhileCaptureRemains()
        => CheckPasswordWordHandlerOverride("text");

    [Fact]
    public void PortableTextWordSelection_ThrowingDownHandlerKeepsItsSelection()
        => CheckPasswordWordHandlerOverride("throw");

    private static void CheckPasswordWordHandlerOverride(string change, [CallerMemberName] string method = "")
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true, method: method)) return;
        RunRetainedEditor((platform, owner, editor, probe) =>
        {
            Point point = PreparePasswordWordSelection(editor, probe, systemPassword: false);
            using PasswordWordPointer input = new(platform, owner, editor);
            input.Click(point, 1);
            InvalidOperationException failure = new("application mouse handler");
            int notifications = 0;
            editor.MouseDown += (_, e) =>
            {
                Assert.Equal(2, e.Clicks);
                AssertPasswordRange(editor);
                notifications++;
                if (change == "text") editor.Text = "replacement";
                editor.Select(1, 2);
                if (change == "throw") throw failure;
            };
            if (change == "throw") Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => input.Down(point, 2)));
            else input.Down(point, 2);
            Assert.Equal(1, notifications);
            Assert.True(editor.Capture);
            input.Drag(new(editor.Width + 20, point.Y));
            input.Drag(new(-20, point.Y));
            Assert.Equal(1, editor.SelectionStart);
            Assert.Equal(2, editor.SelectionLength);
            Assert.Equal(change == "text" ? "replacement" : "secret words", editor.Text);
            Assert.True(editor.Capture);
        });
    }

    [Fact]
    public void PortableTextWordSelection_MoveHandlerSelectionWinsOnReversal()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((platform, owner, editor, probe) =>
        {
            Point point = PreparePasswordWordSelection(editor, probe, systemPassword: true);
            using PasswordWordPointer input = new(platform, owner, editor);
            input.Click(point, 1);
            input.Down(point, 2);
            int moves = 0;
            editor.MouseMove += (_, _) =>
            {
                if (++moves != 1) return;
                AssertPasswordRange(editor);
                editor.Select(1, 2);
            };
            input.Drag(new(-20, point.Y));
            input.Drag(new(editor.Width + 20, point.Y));
            Assert.Equal(2, moves);
            Assert.Equal(1, editor.SelectionStart);
            Assert.Equal(2, editor.SelectionLength);
            Assert.True(editor.Capture);
        });
    }

    [Fact]
    public void PortableTextWordSelection_UpRetiresModeAndNextSinglePressUsesCharacterDrag()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((platform, owner, editor, probe) =>
        {
            Point point = PreparePasswordWordSelection(editor, probe, systemPassword: false);
            using PasswordWordPointer input = new(platform, owner, editor);
            input.Click(point, 1);
            input.Down(point, 2);
            AssertPasswordRange(editor);
            input.Up(point, 2);
            int clicks = 0;
            editor.MouseDown += (_, e) => clicks = e.Clicks;
            input.Down(point, 3);
            Assert.Equal(1, clicks);
            Assert.Equal(2, editor.SelectionStart);
            Assert.Equal(0, editor.SelectionLength);
            input.Drag(TextPointerCaretPoint(probe.Layouts.Last(), 6));
            Assert.Equal(2, editor.SelectionStart);
            Assert.Equal(4, editor.SelectionLength);
            input.Drag(TextPointerCaretPoint(probe.Layouts.Last(), 1));
            Assert.Equal(1, editor.SelectionStart);
            Assert.Equal(1, editor.SelectionLength);
        });
    }

    [Fact]
    public void PortableTextWordSelection_CancelRetiresMode()
        => CheckPasswordWordRetirement("cancel");

    [Fact]
    public void PortableTextWordSelection_CaptureReplacementRetiresMode()
        => CheckPasswordWordRetirement("capture");

    [Fact]
    public void PortableTextWordSelection_HandleReplacementRetiresMode()
        => CheckPasswordWordRetirement("handle");

    private static void CheckPasswordWordRetirement(string change, [CallerMemberName] string method = "")
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true, method: method)) return;
        RunRetainedEditor((platform, owner, editor, probe) =>
        {
            Point point = PreparePasswordWordSelection(editor, probe, systemPassword: false);
            using PasswordWordPointer input = new(platform, owner, editor);
            input.Click(point, 1);
            input.Down(point, 2);
            AssertPasswordRange(editor);
            switch (change)
            {
                case "cancel": input.Cancel(); break;
                case "capture": editor.Capture = false; editor.Capture = true; break;
                case "handle": editor.RecreateSourceHandle(); Assert.True(editor.Focus()); break;
                default: throw new InvalidOperationException(change);
            }

            editor.Select(1, 2);
            input.Drag(new(editor.Width + 20, point.Y));
            Assert.Equal(1, editor.SelectionStart);
            Assert.Equal(2, editor.SelectionLength);
        });
    }

    [Fact]
    public void PortableTextWordSelection_SelectionCallbackCancelSuppressesOldDown()
        => CheckPasswordWordSelectionReentry(nested: false);

    [Fact]
    public void PortableTextWordSelection_SelectionCallbackNestedPressKeepsNewCharacterDrag()
        => CheckPasswordWordSelectionReentry(nested: true);

    private static void CheckPasswordWordSelectionReentry(bool nested, [CallerMemberName] string method = "")
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true, method: method)) return;
        RunRetainedEditor((platform, owner, editor, probe) =>
        {
            Point point = PreparePasswordWordSelection(editor, probe, systemPassword: true);
            Point replacement = TextPointerCaretPoint(probe.Layouts.Last(), 4);
            using PasswordWordPointer input = new(platform, owner, editor);
            input.Click(point, 1);
            bool entered = false;
            int notifications = 0;
            editor.MouseDown += (_, _) => notifications++;
            editor.Invalidated += (_, _) =>
            {
                if (entered || editor.SelectionLength != editor.TextLength) return;
                entered = true;
                if (nested) input.Down(replacement, 0);
                else { input.Cancel(); editor.Select(1, 2); }
            };
            input.Down(point, 2);
            Assert.True(entered);
            Assert.Equal(nested ? 1 : 0, notifications);
            Assert.Equal(nested, editor.Capture);
            input.Drag(TextPointerCaretPoint(probe.Layouts.Last(), 6));
            Assert.Equal(nested ? 4 : 1, editor.SelectionStart);
            Assert.Equal(2, editor.SelectionLength);
        });
    }

    private static Point PreparePasswordWordSelection(RetainedEditor editor, RetainedTextRendererProbe probe,
        bool systemPassword)
    {
        editor.Multiline = false;
        if (systemPassword) editor.UseSystemPasswordChar = true;
        else editor.PasswordChar = '#';
        editor.Text = "secret words";
        editor.Select(0, 0);
        Assert.True(editor.Focus());
        probe.AfterCreateLayout = () => Assert.Equal(
            new string(systemPassword ? '\u25CF' : '#', editor.TextLength), probe.LastText);
        editor.Record();
        Assert.Equal(new string(systemPassword ? '\u25CF' : '#', editor.TextLength), probe.LastText);
        return TextPointerCaretPoint(probe.Layouts.Last(), 2);
    }

    private static void AssertPasswordRange(TextBox editor)
    {
        Assert.Equal(0, editor.SelectionStart);
        Assert.Equal(editor.TextLength, editor.SelectionLength);
        Assert.Equal("secret words", editor.Text);
    }

    // Actual provider -> NativePointerInput -> canonical source dispatch, with
    // points mapped through the live Form/editor rather than a synthetic OnMouse.
    private sealed class PasswordWordPointer : IDisposable
    {
        private readonly Control _source;
        private readonly Control _editor;
        private readonly NativePointerTestContext _provider = new();
        private readonly NativePointerInput _subscription;
        private double _timestamp;

        internal PasswordWordPointer(HeadlessPlatform platform, Control source, Control editor)
        {
            _source = source;
            _editor = editor;
            SourcePointerTarget target = new(source, input => platform.SendControlInput(source, input));
            _subscription = new(_provider, target);
            target.Subscription = _subscription;
        }

        internal void Click(Point point, int count) { Down(point, count); Up(point, count); }
        internal void Down(Point point, int count) => Send(NativePointerEventKind.Down, point, count);
        internal void Up(Point point, int count) => Send(NativePointerEventKind.Up, point, count);
        internal void Drag(Point point) => Send(NativePointerEventKind.Drag, point, 0);
        internal void Cancel() => _provider.Emit(new(NativePointerEventKind.Cancel,
            0, 0, ++_timestamp, -1, 0, NativePointerModifiers.None));

        private void Send(NativePointerEventKind kind, Point point, int count)
        {
            Point sourcePoint = _source.PointToClient(_editor.PointToScreen(point));
            _provider.Emit(new(kind, sourcePoint.X, sourcePoint.Y, ++_timestamp, 0, count, NativePointerModifiers.None));
        }

        public void Dispose()
        {
            try { Cancel(); }
            finally { _subscription.Dispose(); _provider.Dispose(); }
        }
    }
}
