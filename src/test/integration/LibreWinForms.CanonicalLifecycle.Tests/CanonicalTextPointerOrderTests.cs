// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Runtime.CompilerServices;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Fact]
    public void PortableTextPointerOrder_LegacyDownDefaultsPrecedeDerivedAndPublicHandlers()
        => CheckTextPointerDownOrder(native: false);

    [Fact]
    public void PortableTextPointerOrder_NativeDownDefaultsPrecedeDerivedAndPublicHandlers()
        => CheckTextPointerDownOrder(native: true);

    private static void CheckTextPointerDownOrder(bool native, [CallerMemberName] string method = "")
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true, method: method)) return;
        RunRetainedEditor((platform, owner, editor, probe) =>
        {
            Point point = PrepareTextPointerOrder(editor, probe, 2);
            List<string> events = [];
            editor.BeforeMouseDown = _ =>
            {
                Assert.Equal(2, editor.SelectionStart);
                Assert.Equal(0, editor.SelectionLength);
                Assert.True(editor.Capture);
                events.Add("derived");
            };
            editor.MouseDown += (_, _) =>
            {
                Assert.Equal(2, editor.SelectionStart);
                events.Add("public");
                editor.Select(1, 3);
                editor.Capture = false;
            };

            if (native)
            {
                using NativeClickBridge input = new(platform, owner);
                input.ClickAt(owner.PointToClient(editor.PointToScreen(point)), 1);
            }
            else editor.Press(point);

            Assert.Equal(new[] { "derived", "public" }, events);
            Assert.Equal(1, editor.SelectionStart);
            Assert.Equal(3, editor.SelectionLength);
            Assert.False(editor.Capture);
        });
    }

    [Fact]
    public void PortableTextPointerOrder_DragDefaultsPrecedeHandlersAndPreserveTheirOverride()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            editor.Press(PrepareTextPointerOrder(editor, probe, 2));
            Point end = TextPointerCaretPoint(probe.Layouts.Last(), 6);
            List<string> events = [];
            editor.BeforeMouseMove = _ =>
            {
                Assert.Equal(2, editor.SelectionStart);
                Assert.Equal(4, editor.SelectionLength);
                events.Add("derived");
            };
            editor.MouseMove += (_, _) =>
            {
                Assert.Equal(4, editor.SelectionLength);
                events.Add("public");
                editor.Select(0, 1);
                editor.Capture = false;
            };
            editor.Drag(end);
            Assert.Equal(new[] { "derived", "public" }, events);
            Assert.Equal(0, editor.SelectionStart);
            Assert.Equal(1, editor.SelectionLength);
            Assert.False(editor.Capture);
        });
    }

    [Fact]
    public void PortableTextPointerOrder_ProtectedNotificationsDoNotRunEditorDefaults()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            Point point = PrepareTextPointerOrder(editor, probe, 2);
            int notifications = 0;
            editor.MouseDown += (_, _) => notifications++;
            editor.MouseMove += (_, _) => notifications++;
            editor.RaiseDownNotification(point);
            editor.RaiseMoveNotification(TextPointerCaretPoint(probe.Layouts.Last(), 6));
            Assert.Equal(2, notifications);
            Assert.Equal(0, editor.SelectionStart);
            Assert.Equal(0, editor.SelectionLength);
            Assert.False(editor.Capture);
        });
    }

    [Fact]
    public void PortableTextPointerOrder_UserMouseKeepsSelectionUnderApplicationControl()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            Point point = PrepareTextPointerOrder(editor, probe, 2);
            editor.SetUserMouse(true);
            int notifications = 0;
            editor.MouseDown += (_, _) => notifications++;
            editor.MouseMove += (_, _) => notifications++;
            editor.Press(point);
            editor.Drag(TextPointerCaretPoint(probe.Layouts.Last(), 6));
            Assert.Equal(2, notifications);
            Assert.Equal(0, editor.SelectionStart);
            Assert.Equal(0, editor.SelectionLength);
        });
    }

    [Fact]
    public void PortableTextPointerOrder_ThrowingHandlerDoesNotUndoCompletedDefaultSelection()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            Point point = PrepareTextPointerOrder(editor, probe, 2);
            var error = new InvalidOperationException("mouse handler failed");
            editor.MouseDown += (_, _) => throw error;
            Assert.Same(error, Assert.Throws<InvalidOperationException>(() => editor.Press(point)));
            Assert.Equal(2, editor.SelectionStart);
            Assert.Equal(0, editor.SelectionLength);
            editor.CancelPointer();
            Assert.False(editor.Capture);
        });
    }

    [Fact]
    public void PortableTextPointerOrder_ReplacedTextRetiresPendingHit()
        => CheckRetiredTextPointerHit("text");

    [Fact]
    public void PortableTextPointerOrder_RecreatedHandleRetiresPendingHit()
        => CheckRetiredTextPointerHit("handle");

    [Fact]
    public void PortableTextPointerOrder_CancelRetiresPendingHit()
        => CheckRetiredTextPointerHit("cancel");

    [Fact]
    public void PortableTextPointerOrder_NestedPressRetiresPendingHit()
        => CheckRetiredTextPointerHit("nested");

    [Fact]
    public void PortableTextPointerOrder_ReplacedCaptureRetiresPendingHit()
        => CheckRetiredTextPointerHit("capture");

    private static void CheckRetiredTextPointerHit(string retirement, [CallerMemberName] string method = "")
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true, method: method)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            Point point = PrepareTextPointerOrder(editor, probe, 2);
            RetainedLayoutProbe layout = probe.Layouts.Last();
            Point nestedPoint = TextPointerCaretPoint(layout, 6);
            int notifications = 0;
            editor.MouseDown += (_, _) => notifications++;
            layout.AfterHitTest = () =>
            {
                layout.AfterHitTest = null;
                switch (retirement)
                {
                    case "text": editor.Text = "replacement"; break;
                    case "handle": editor.RecreateSourceHandle(); break;
                    case "cancel": editor.CancelPointer(); break;
                    case "nested": editor.Press(nestedPoint); return;
                    case "capture": editor.Capture = false; editor.Capture = true; break;
                    default: throw new InvalidOperationException(retirement);
                }

                editor.Select(1, 1);
            };
            editor.Press(point);
            Assert.Equal(retirement == "nested" ? 1 : 0, notifications);
            Assert.Equal(retirement == "nested" ? 6 : 1, editor.SelectionStart);
            Assert.Equal(retirement == "nested" ? 0 : 1, editor.SelectionLength);
        });
    }

    [Fact]
    public void PortableTextPointerOrder_SelectionCallbackCancelRetiresOldPress()
        => CheckTextPointerSelectionReentry(nested: false);

    [Fact]
    public void PortableTextPointerOrder_SelectionCallbackNestedPressSurvivesOldTail()
        => CheckTextPointerSelectionReentry(nested: true);

    private static void CheckTextPointerSelectionReentry(bool nested, [CallerMemberName] string method = "")
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true, method: method)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            Point point = PrepareTextPointerOrder(editor, probe, 2);
            Point nestedPoint = TextPointerCaretPoint(probe.Layouts.Last(), 6);
            bool entered = false;
            int notifications = 0;
            editor.MouseDown += (_, _) => notifications++;
            editor.Invalidated += (_, _) =>
            {
                if (entered || editor.SelectionStart != 2) return;
                entered = true;
                if (nested) editor.Press(nestedPoint);
                else editor.CancelPointer();
            };
            editor.Press(point);
            Assert.True(entered);
            Assert.Equal(nested ? 1 : 0, notifications);
            Assert.Equal(nested ? 6 : 2, editor.SelectionStart);
            Assert.Equal(nested, editor.Capture);
        });
    }

    [Fact]
    public void PortableTextPointerOrder_ShiftAndReadOnlySelectionUseTheOriginalAnchor()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            Point point = PrepareTextPointerOrder(editor, probe, 2);
            editor.ReadOnly = true;
            editor.Select(5, 0);
            int edits = 0;
            int notifications = 0;
            editor.TextChanged += (_, _) => edits++;
            editor.BeforeMouseDown = _ =>
            {
                notifications++;
                Assert.Equal(2, editor.SelectionStart);
                Assert.Equal(3, editor.SelectionLength);
            };
            editor.Press(point, LibreInputModifiers.Shift);
            Assert.Equal(1, notifications);
            Assert.Equal(2, editor.SelectionStart);
            Assert.Equal(3, editor.SelectionLength);
            Assert.Equal("wide text", editor.Text);
            Assert.Equal(0, edits);
        });
    }

    [Fact]
    public void PortableTextPointerOrder_LayoutCreationCannotPublishAcrossReplacedText()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            Point point = PrepareTextPointerOrder(editor, probe, 2);
            editor.Text = "new source";
            int notifications = 0;
            RetainedLayoutProbe? rejected = null;
            editor.MouseDown += (_, _) => notifications++;
            probe.AfterCreateLayout = () =>
            {
                probe.AfterCreateLayout = null;
                rejected = probe.Layouts.Last();
                editor.Text = "replacement";
                editor.Select(1, 1);
            };
            editor.Press(point);
            Assert.NotNull(rejected);
            Assert.True(rejected.Disposed);
            Assert.Equal(0, notifications);
            Assert.Equal("replacement", editor.Text);
            Assert.Equal(1, editor.SelectionStart);
            Assert.Equal(1, editor.SelectionLength);
        });
    }

    [Fact]
    public void PortableTextPointerOrder_PreviousLayoutDisposalCannotReturnRetiredReplacement()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            Point point = PrepareTextPointerOrder(editor, probe, 2);
            RetainedLayoutProbe previous = probe.Layouts.Last();
            int notifications = 0;
            bool disposed = false;
            editor.MouseDown += (_, _) => notifications++;
            previous.AfterDispose = () =>
            {
                previous.AfterDispose = null;
                disposed = true;
                editor.Text = "replacement";
                editor.Select(1, 1);
            };
            editor.Width += 20;
            editor.Press(point);
            Assert.True(disposed);
            Assert.Equal(0, notifications);
            Assert.Equal("replacement", editor.Text);
            Assert.Equal(1, editor.SelectionStart);
            Assert.Equal(1, editor.SelectionLength);
        });
    }

    private static Point PrepareTextPointerOrder(RetainedEditor editor, RetainedTextRendererProbe probe, int position)
    {
        editor.Text = "wide text";
        editor.Select(0, 0);
        editor.Record();
        return TextPointerCaretPoint(probe.Layouts.Last(), position);
    }

    private static Point TextPointerCaretPoint(RetainedLayoutProbe layout, int position)
    {
        Point point = Point.Round(layout.GetCaret(position).Position);
        point.Offset(1, 1);
        return point;
    }
}
