// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;
using LibreWinForms.Platform;
using LibreWinForms.ProGPU;
using LibreWinForms.ProGPU.Tests;
using ProGPU.Backend;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void NativePointerProvider_ClickPairsReuseCanonicalEventOrder(int button)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false };
        using NativeClickControl control = new() { Bounds = new(10, 10, 100, 60) };
        form.Controls.Add(control);
        form.Show();
        List<string> events = RecordNativeClicks(control);
        using NativeClickBridge input = new(platform, form);

        for (int count = 1; count <= 4; count++)
        {
            input.Click(control, count, button);
            Assert.Equal(count, input.LastInput.NativePointer!.Value.ClickCount);
        }

        Assert.Equal(new[]
        {
            "down:1", "click:1", "mouse-click:1", "up:1",
            "down:2", "double:2", "mouse-double:2", "up:1",
            "down:1", "click:1", "mouse-click:1", "up:1",
            "down:2", "double:2", "mouse-double:2", "up:1"
        }, events);
        Assert.False(control.Capture);
        Assert.Equal(MouseButtons.None, Control.MouseButtons);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void NativePointerProvider_ClickStylesRemainAuthoritative(bool standardClick, bool standardDoubleClick)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false };
        using NativeClickControl control = new(standardClick, standardDoubleClick) { Bounds = new(10, 10, 100, 60) };
        form.Controls.Add(control);
        form.Show();
        List<string> events = RecordNativeClicks(control);
        using NativeClickBridge input = new(platform, form);
        input.Click(control, 1);
        input.Click(control, 2);

        Assert.Equal(new[] { "down:1", "down:2" }, events.Where(value => value.StartsWith("down:")));
        Assert.DoesNotContain(events, value => value.StartsWith("double:") || value.StartsWith("mouse-double:"));
        Assert.Equal(standardClick ? 2 : 0, events.Count(value => value == "click:1"));
        Assert.Equal(2, events.Count(value => value == "up:1"));
    }

    [Fact]
    public void NativePointerProvider_ClickHistoryBelongsToTheActualControlAndConsecutiveCounts()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false, ClientSize = new(300, 120) };
        using NativeClickControl first = new() { Bounds = new(10, 10, 100, 60) };
        using NativeClickControl second = new() { Bounds = new(150, 10, 100, 60) };
        form.Controls.Add(first);
        form.Controls.Add(second);
        form.Show();
        List<string> events = RecordNativeClicks(second);
        using NativeClickBridge input = new(platform, form);
        input.Click(first, 1);
        input.Click(second, 2); // Native view identity does not prove source control identity.
        input.Click(second, 3);
        input.Click(second, 6); // Missing provider counts restart the source pair.
        input.Click(second, 7);
        input.Click(second, 0); // An unclassified packet cannot seed a pair.
        input.Click(second, 8);
        input.Click(second, 9);

        Assert.Equal(new[] { "down:1", "down:2", "down:1", "down:2", "down:1", "down:1", "down:2" },
            events.Where(value => value.StartsWith("down:")));
        Assert.Equal(3, events.Count(value => value == "mouse-double:2"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativePointerProvider_DoubleClickStyleIsCapturedAfterDownCallbacksNotAtRelease(bool enabledAfterDown)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false };
        using NativeClickControl control = new(standardDoubleClick: !enabledAfterDown) { Bounds = new(10, 10, 100, 60) };
        form.Controls.Add(control);
        form.Show();
        List<string> events = RecordNativeClicks(control);
        using NativeClickBridge input = new(platform, form);
        input.Click(control, 1);
        control.MouseDown += (_, e) =>
        {
            if (e.Clicks == 2)
                control.SetDoubleClickStyle(enabledAfterDown);
        };
        input.Button(control, down: true, count: 2);
        control.SetDoubleClickStyle(!enabledAfterDown);
        input.Button(control, down: false, count: 2);

        Assert.Equal(enabledAfterDown ? 1 : 0, events.Count(value => value == "mouse-double:2"));
        Assert.Equal(enabledAfterDown ? 1 : 2, events.Count(value => value == "mouse-click:1"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativePointerProvider_ClickCancellationAndLeaveKeepTheirDistinctOwnership(bool cancel)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false };
        using NativeClickControl control = new() { Bounds = new(10, 10, 100, 60) };
        form.Controls.Add(control);
        form.Show();
        List<string> events = RecordNativeClicks(control);
        using NativeClickBridge input = new(platform, form);
        input.Click(control, 1);
        input.Button(control, down: true, count: 2);
        input.Retire(cancel);
        Assert.Equal(!cancel, control.Capture);
        input.Button(control, down: false, count: 2);
        input.Click(control, 3);

        Assert.Equal(cancel ? 0 : 1, events.Count(value => value == "mouse-double:2"));
        Assert.Equal(new[] { "down:1", "down:2", "down:1" }, events.Where(value => value.StartsWith("down:")));
        Assert.False(control.Capture);
        Assert.Equal(MouseButtons.None, Control.MouseButtons);
    }

    [Fact]
    public void NativePointerProvider_ClickHistoryCannotCrossARecreatedChildHandle()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false };
        using NativeClickControl control = new() { Bounds = new(10, 10, 100, 60) };
        form.Controls.Add(control);
        form.Show();
        List<string> events = RecordNativeClicks(control);
        using NativeClickBridge input = new(platform, form);
        input.Click(control, 1);
        nint oldHandle = control.Handle;
        control.RecreateSourceHandle();
        Assert.NotEqual(oldHandle, control.Handle);
        input.Click(control, 2);

        Assert.Equal(new[] { "down:1", "down:1" }, events.Where(value => value.StartsWith("down:")));
        Assert.DoesNotContain("mouse-double:2", events);
    }

    [Fact]
    public void NativePointerProvider_FailedClickReleaseCannotSeedAnotherPair()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false };
        using NativeClickControl control = new() { Bounds = new(10, 10, 100, 60) };
        form.Controls.Add(control);
        form.Show();
        List<string> events = RecordNativeClicks(control);
        var error = new InvalidOperationException("click callback failed");
        EventHandler fail = (_, _) => throw error;
        control.Click += fail;
        using NativeClickBridge input = new(platform, form);
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => input.Click(control, 1)));
        control.Click -= fail;
        input.Click(control, 2);

        Assert.Equal(new[] { "down:1", "down:1" }, events.Where(value => value.StartsWith("down:")));
        Assert.DoesNotContain("mouse-double:2", events);
        Assert.False(control.Capture);
    }

    [Fact]
    public void NativePointerProvider_DoubleClickCallbackCannotRetireANewerPress()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false };
        using NativeClickControl control = new() { Bounds = new(10, 10, 100, 60) };
        form.Controls.Add(control);
        form.Show();
        List<string> events = RecordNativeClicks(control);
        using NativeClickBridge input = new(platform, form);
        bool nested = false;
        control.DoubleClick += (_, _) =>
        {
            if (nested)
                return;
            nested = true;
            input.Button(control, down: true, count: 3);
        };

        input.Click(control, 1);
        input.Click(control, 2);
        Assert.True(control.Capture);
        Assert.Equal(MouseButtons.Left, Control.MouseButtons);
        Assert.DoesNotContain("mouse-double:2", events); // The old release stops after reentry.
        input.Button(control, down: false, count: 3);
        input.Click(control, 4);

        Assert.Equal(new[] { "down:1", "down:2", "down:1", "down:2" }, events.Where(value => value.StartsWith("down:")));
        Assert.Single(events, value => value == "mouse-double:2");
        Assert.False(control.Capture);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativePointerProvider_DataGridViewReceivesItsActualCellAndHeaderDoubleClick(bool header)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false, ClientSize = new(400, 250) };
        using DataGridView grid = new()
        {
            Bounds = new(10, 10, 360, 200),
            AllowUserToAddRows = false,
            EditMode = DataGridViewEditMode.EditProgrammatically
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name", Width = 140 });
        grid.Rows.Add("seed");
        form.Controls.Add(grid);
        form.Show();
        List<(int Column, int Row)> doubleClicks = new();
        List<(int Column, int Row, int Clicks)> mouseDoubleClicks = new();
        int headers = 0;
        grid.CellDoubleClick += (_, e) => doubleClicks.Add((e.ColumnIndex, e.RowIndex));
        grid.CellMouseDoubleClick += (_, e) => mouseDoubleClicks.Add((e.ColumnIndex, e.RowIndex, e.Clicks));
        grid.ColumnHeaderMouseDoubleClick += (_, _) => headers++;
        int row = header ? -1 : 0;
        Rectangle bounds = grid.GetCellDisplayRectangle(0, row, cutOverflow: true);
        Point point = form.PointToClient(grid.PointToScreen(new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2)));
        using NativeClickBridge input = new(platform, form);
        input.ClickAt(point, 1);
        input.ClickAt(point, 2);

        Assert.Equal(new[] { (0, row) }, doubleClicks);
        Assert.Equal(new[] { (0, row, 2) }, mouseDoubleClicks);
        Assert.Equal(header ? 1 : 0, headers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativePointerProvider_HostedTextBoxUsesItsOwnReleaseEventsWithoutLeakingClassification(bool failDoubleClick)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using ContextMenuStrip menu = new() { AutoClose = false };
        using ToolStripTextBox item = new() { Text = "alpha beta", AutoSize = false, Width = 140 };
        menu.Items.Add(item);
        owner.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        menu.Show(owner, Point.Empty);
        TextBox editor = item.TextBox;
        List<string> events = RecordNativeClicks(editor);
        var error = new InvalidOperationException("editor double-click callback failed");
        EventHandler fail = (_, _) => throw error;
        using NativeClickBridge input = new(platform, menu);
        input.Click(editor, 1);
        if (failDoubleClick)
            editor.DoubleClick += fail;
        if (failDoubleClick)
            Assert.Same(error, Assert.Throws<InvalidOperationException>(() => input.Click(editor, 2)));
        else
            input.Click(editor, 2);
        editor.DoubleClick -= fail;
        input.Click(editor, 3);

        Assert.Equal(new[] { "down:1", "down:2", "down:1" }, events.Where(value => value.StartsWith("down:")));
        Assert.Equal(2, events.Count(value => value == "click:1"));
        Assert.Single(events, value => value == "double:1"); // TextBox forwards its canonical MouseUp args.
        Assert.Equal(failDoubleClick ? 0 : 1, events.Count(value => value == "mouse-double:1"));
        Assert.Same(owner, Form.ActiveForm);
        Assert.True(menu.Visible);
        Assert.False(editor.Capture);
    }

    [Fact]
    public void NativePointerProvider_CancelledHostedTextBoxReleaseCannotInventClickNotifications()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using ContextMenuStrip menu = new() { AutoClose = false };
        using ToolStripTextBox item = new() { Text = "alpha beta", AutoSize = false, Width = 140 };
        menu.Items.Add(item);
        owner.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        menu.Show(owner, Point.Empty);
        List<string> events = RecordNativeClicks(item.TextBox);
        using NativeClickBridge input = new(platform, menu);
        input.Click(item.TextBox, 1);
        input.Button(item.TextBox, down: true, count: 2);
        input.Retire(cancel: true);
        input.Button(item.TextBox, down: false, count: 2);
        input.Click(item.TextBox, 3);

        Assert.Equal(2, events.Count(value => value == "click:1"));
        Assert.Equal(2, events.Count(value => value == "mouse-click:1"));
        Assert.DoesNotContain(events, value => value.StartsWith("double:") || value.StartsWith("mouse-double:"));
        Assert.Equal(MouseButtons.None, Control.MouseButtons);
        Assert.False(item.TextBox.Capture);
    }

    private static List<string> RecordNativeClicks(Control control)
    {
        List<string> events = new();
        control.MouseDown += (_, e) => events.Add($"down:{e.Clicks}");
        control.Click += (_, e) => events.Add($"click:{Assert.IsType<MouseEventArgs>(e).Clicks}");
        control.MouseClick += (_, e) => events.Add($"mouse-click:{e.Clicks}");
        control.DoubleClick += (_, e) => events.Add($"double:{Assert.IsType<MouseEventArgs>(e).Clicks}");
        control.MouseDoubleClick += (_, e) => events.Add($"mouse-double:{e.Clicks}");
        control.MouseUp += (_, e) => events.Add($"up:{e.Clicks}");
        return events;
    }

    private sealed class NativeClickControl : Control
    {
        internal NativeClickControl(bool standardClick = true, bool standardDoubleClick = true)
        {
            SetStyle(ControlStyles.StandardClick, standardClick);
            SetStyle(ControlStyles.StandardDoubleClick, standardDoubleClick);
        }

        internal void RecreateSourceHandle() => RecreateHandle();
        internal void SetDoubleClickStyle(bool enabled) => SetStyle(ControlStyles.StandardDoubleClick, enabled);
    }

    private sealed class NativeClickBridge : IDisposable
    {
        private readonly Control _source;
        private readonly NativePointerTestContext _provider = new();
        private readonly NativePointerInput _subscription;
        private readonly SourcePointerTarget _target;
        private double _timestamp;
        internal LibreInputEvent LastInput => _target.LastInput;

        internal NativeClickBridge(HeadlessPlatform platform, Control source)
        {
            _source = source;
            _target = new(source, input => platform.SendControlInput(source, input));
            _subscription = new(_provider, _target);
            _target.Subscription = _subscription;
        }

        internal void Click(Control target, int count, int button = 0)
            => ClickAt(Position(target), count, button);

        internal void ClickAt(Point position, int count, int button = 0)
        {
            ButtonAt(position, true, count, button);
            ButtonAt(position, false, count, button);
        }

        internal void Button(Control target, bool down, int count, int button = 0)
            => ButtonAt(Position(target), down, count, button);

        private Point Position(Control target) => _source.PointToClient(target.PointToScreen(
            new(Math.Max(1, target.ClientSize.Width / 2), Math.Max(1, target.ClientSize.Height / 2))));

        private void ButtonAt(Point position, bool down, int count, int button)
            => _provider.Emit(new(down ? NativePointerEventKind.Down : NativePointerEventKind.Up,
                position.X, position.Y, ++_timestamp, button, count, NativePointerModifiers.None));

        internal void Retire(bool cancel) => _provider.Emit(new(
            cancel ? NativePointerEventKind.Cancel : NativePointerEventKind.Leave,
            0, 0, ++_timestamp, -1, 0, NativePointerModifiers.None));

        public void Dispose()
        {
            try { Retire(cancel: true); }
            finally
            {
                _subscription.Dispose();
                _provider.Dispose();
            }
        }
    }
}
