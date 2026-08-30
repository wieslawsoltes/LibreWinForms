// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using FluentAssertions;
using LibreWinForms.Platform;
using ProGPU.Scene;
using ProGpuSolidColorBrush = ProGPU.Vector.SolidColorBrush;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public class CanonicalLifecycleTests
{
    private delegate int AddValues(int left, int right);

    [Fact]
    public void ApplicationIdle_CoalescesDispatcherPostAndHonorsSubscriberRemoval()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        int firstCalls = 0;
        int secondCalls = 0;
        EventHandler first = (_, _) => firstCalls++;
        EventHandler second = (_, _) => secondCalls++;

        try
        {
            Application.Idle += first;
            Application.Idle += second;

            platform.DispatcherPostCount.Should().Be(1);
            platform.PumpOnce();
            firstCalls.Should().Be(1);
            secondCalls.Should().Be(1);

            Application.Idle -= first;
            Application.Idle -= second;
            Application.Idle += first;

            platform.DispatcherPostCount.Should().Be(2);
            platform.PumpOnce();
            firstCalls.Should().Be(2);
            secondCalls.Should().Be(1);
        }
        finally
        {
            Application.Idle -= first;
            Application.Idle -= second;
        }
    }

    [Fact]
    public void ApplicationThreads_OwnIndependentDispatchersAndExitContexts()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        ConcurrentQueue<ExceptionDispatchInfo> failures = new();
        int threadExitCount = 0;
        int applicationExitCount = 0;
        EventHandler onThreadExit = (_, _) => Interlocked.Increment(ref threadExitCount);
        EventHandler onApplicationExit = (_, _) => Interlocked.Increment(ref applicationExitCount);
        Application.ThreadExit += onThreadExit;
        Application.ApplicationExit += onApplicationExit;

        try
        {
            ThreadLoopState first = StartThreadLoop("first");
            ThreadLoopState second = StartThreadLoop("second");

            first.Control.InvokeRequired.Should().BeTrue();
            second.Control.InvokeRequired.Should().BeTrue();

            first.Control.BeginInvoke((Action)(() =>
            {
                first.CallbackThreadId = Environment.CurrentManagedThreadId;
                first.Control.Dispose();
                Application.ExitThread();
            }));

            first.Thread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
            second.Thread.IsAlive.Should().BeTrue();
            first.CallbackThreadId.Should().Be(first.Thread.ManagedThreadId);
            first.Control.IsDisposed.Should().BeTrue();
            first._contextDisposeCount.Should().Be(1);

            second.Control.BeginInvoke((Action)Application.ExitThread);
            second.Thread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
            second._contextDisposeCount.Should().Be(1);

            using ManualResetEventSlim noFormStarted = new();
            Thread noFormThread = new(() =>
            {
                try
                {
                    Application.Run();
                }
                catch (Exception exception)
                {
                    failures.Enqueue(ExceptionDispatchInfo.Capture(exception));
                    noFormStarted.Set();
                }
            })
            {
                IsBackground = true,
                Name = "Canonical no-form application loop test",
            };
            noFormThread.Start();
            ILibreDispatcher noFormDispatcher = platform.WaitForThreadDispatcher(noFormThread.ManagedThreadId);
            noFormDispatcher.Post(noFormStarted.Set);
            noFormStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).Should().BeTrue();
            noFormDispatcher.Post(Application.ExitThread);
            noFormThread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();

            failures.Should().BeEmpty();
            threadExitCount.Should().Be(3);
            applicationExitCount.Should().Be(2);

            ThreadLoopState StartThreadLoop(string name)
            {
                ThreadLoopState state = new();
                state.Thread = new Thread(() =>
                {
                    try
                    {
                        using Control control = new();
                        TrackingApplicationContext context = new(
                            () => Interlocked.Increment(ref state._contextDisposeCount));
                        state.Control = control;
                        control.CreateControl();
                        state.Ready.Set();
                        Application.Run(context);
                    }
                    catch (Exception exception)
                    {
                        failures.Enqueue(ExceptionDispatchInfo.Capture(exception));
                        state.Ready.Set();
                    }
                })
                {
                    IsBackground = true,
                    Name = $"Canonical {name} application loop test",
                };
                state.Thread.Start();
                state.Ready.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).Should().BeTrue();
                failures.Should().BeEmpty();
                state.Control.BeginInvoke((Action)state.LoopStarted.Set);
                state.LoopStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).Should().BeTrue();
                return state;
            }
        }
        finally
        {
            Application.ThreadExit -= onThreadExit;
            Application.ApplicationExit -= onApplicationExit;
        }
    }

    [Fact]
    public void DispatcherInvocation_UsesCanonicalControlAndTypedDispatcher()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new();
        using Control control = new();
        form.Controls.Add(control);
        form.Show();

        int dispatcherThreadId = Environment.CurrentManagedThreadId;
        control.InvokeRequired.Should().BeFalse();

        int postsBeforeDirectInvoke = platform.DispatcherPostCount;
        int directThreadId = 0;
        control.Invoke((Action)(() => directThreadId = Environment.CurrentManagedThreadId));
        directThreadId.Should().Be(dispatcherThreadId);
        platform.DispatcherPostCount.Should().Be(postsBeforeDirectInvoke);

        using ManualResetEventSlim synchronousCompleted = new(initialState: false);
        bool invokeRequired = false;
        int callbackThreadId = 0;
        string? result = null;
        Exception? synchronousFailure = null;
        Thread synchronousWorker = new(
            () =>
            {
                try
                {
                    invokeRequired = control.InvokeRequired;
                    result = (string?)control.Invoke(
                        (Func<string>)(() =>
                        {
                            callbackThreadId = Environment.CurrentManagedThreadId;
                            return "marshaled";
                        }));
                }
                catch (Exception exception)
                {
                    synchronousFailure = exception;
                }
                finally
                {
                    synchronousCompleted.Set();
                }
            })
        {
            IsBackground = true,
            Name = "Canonical synchronous Control.Invoke worker"
        };
        synchronousWorker.Start();

        PumpUntilSignaled(platform, synchronousCompleted);
        synchronousWorker.Join(10_000).Should().BeTrue();
        synchronousFailure.Should().BeNull();
        invokeRequired.Should().BeTrue();
        callbackThreadId.Should().Be(dispatcherThreadId);
        result.Should().Be("marshaled");

        using ManualResetEventSlim asynchronousQueued = new(initialState: false);
        using ManualResetEventSlim asynchronousCompleted = new(initialState: false);
        IAsyncResult? asynchronousResult = null;
        object? asynchronousValue = null;
        Exception? asynchronousFailure = null;
        Thread asynchronousWorker = new(
            () =>
            {
                try
                {
                    asynchronousResult = control.BeginInvoke(new AddValues((left, right) => left + right), 3, 4);
                    asynchronousQueued.Set();
                    asynchronousValue = control.EndInvoke(asynchronousResult);
                }
                catch (Exception exception)
                {
                    asynchronousFailure = exception;
                }
                finally
                {
                    asynchronousCompleted.Set();
                }
            })
        {
            IsBackground = true,
            Name = "Canonical asynchronous Control.EndInvoke worker"
        };
        asynchronousWorker.Start();

        SpinWait.SpinUntil(() => asynchronousQueued.IsSet, TimeSpan.FromSeconds(10)).Should().BeTrue();
        asynchronousResult.Should().NotBeNull();
        asynchronousResult!.IsCompleted.Should().BeFalse();
        PumpUntilSignaled(platform, asynchronousCompleted);
        asynchronousWorker.Join(10_000).Should().BeTrue();
        asynchronousFailure.Should().BeNull();
        asynchronousValue.Should().Be(7);
        asynchronousResult.IsCompleted.Should().BeTrue();

        control.Invoke(() => 42).Should().Be(42);

        int sameThreadCalls = 0;
        IAsyncResult sameThread = control.BeginInvoke(
            (Func<int>)(() =>
            {
                sameThreadCalls++;
                return 42;
            }));
        sameThread.IsCompleted.Should().BeFalse();
        control.EndInvoke(sameThread).Should().Be(42);
        sameThreadCalls.Should().Be(1);
        platform.PumpOnce();

        ThreadExceptionEventHandler swallowThreadException = (_, _) => { };
        Application.ThreadException += swallowThreadException;
        try
        {
            IAsyncResult throwing = control.BeginInvoke(
                (Action)(() => throw new InvalidOperationException("canonical invoke failure")));
            Action endThrowingInvoke = () => control.EndInvoke(throwing);
            endThrowingInvoke.Should().Throw<InvalidOperationException>()
                .WithMessage("canonical invoke failure");
            platform.PumpOnce();
        }
        finally
        {
            Application.ThreadException -= swallowThreadException;
        }

        using Control disposingControl = new();
        form.Controls.Add(disposingControl);
        _ = disposingControl.Handle;
        int postsBeforeDisposedInvoke = platform.DispatcherPostCount;
        using ManualResetEventSlim disposedInvokeCompleted = new(initialState: false);
        Exception? disposedInvokeFailure = null;
        Thread disposedInvokeWorker = new(
            () =>
            {
                try
                {
                    disposingControl.Invoke((Action)(() => { }));
                }
                catch (Exception exception)
                {
                    disposedInvokeFailure = exception;
                }
                finally
                {
                    disposedInvokeCompleted.Set();
                }
            })
        {
            IsBackground = true,
            Name = "Canonical disposed Control.Invoke worker"
        };
        disposedInvokeWorker.Start();

        SpinWait.SpinUntil(
            () => platform.DispatcherPostCount > postsBeforeDisposedInvoke,
            TimeSpan.FromSeconds(10)).Should().BeTrue();
        disposingControl.Dispose();
        SpinWait.SpinUntil(() => disposedInvokeCompleted.IsSet, TimeSpan.FromSeconds(10)).Should().BeTrue();
        disposedInvokeWorker.Join(10_000).Should().BeTrue();
        disposedInvokeFailure.Should().BeOfType<ObjectDisposedException>();
        platform.PumpOnce();
    }

    [Fact]
    public void DragDrop_UsesCanonicalEventsAndTypedPlatformSession()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new()
        {
            Bounds = new Rectangle(300, 400, 240, 180),
            StartPosition = FormStartPosition.Manual
        };
        using Control source = new() { Name = "source", Location = new Point(7, 9) };
        using Control parent = new() { Name = "parent", Location = new Point(20, 30), AllowDrop = true };
        using Control child = new() { Name = "child", Location = new Point(4, 5) };
        using Control second = new() { Name = "second", Location = new Point(70, 80), AllowDrop = true };
        parent.Controls.Add(child);
        form.Controls.AddRange([source, parent, second]);
        form.Show();

        LibreHandle sourceHandle = new(source.Handle, LibreHandleKind.LogicalControl);
        LibreHandle parentHandle = new(parent.Handle, LibreHandleKind.LogicalControl);
        LibreHandle childHandle = new(child.Handle, LibreHandleKind.LogicalControl);
        LibreHandle secondHandle = new(second.Handle, LibreHandleKind.LogicalControl);
        platform.DragDropTargets.Should().Contain(parentHandle).And.Contain(secondHandle);
        platform.DragDropTargets.Should().NotContain(childHandle);

        DataObject data = new();
        data.SetData(DataFormats.FileDrop, new[] { "/tmp/Project.csproj", "/tmp/Readme.txt" });
        data.SetData(DataFormats.UnicodeText, "canonical drag text");
        List<string> sequence = [];
        List<int> enterKeyStates = [];
        int childEnterCalls = 0;
        child.DragEnter += (_, _) => childEnterCalls++;
        parent.DragEnter += (_, e) =>
        {
            sequence.Add("parent.enter");
            e.Data.Should().BeSameAs(data);
            enterKeyStates.Add(e.KeyState);
            new Point(e.X, e.Y).Should().Be(new Point(640, 480));
            e.Effect = DragDropEffects.Move;
        };
        parent.DragOver += (_, e) =>
        {
            sequence.Add("parent.over");
            e.Effect.Should().Be(DragDropEffects.Move);
            e.Effect = DragDropEffects.Copy;
        };
        parent.DragLeave += (_, _) => sequence.Add("parent.leave");
        second.DragEnter += (_, e) =>
        {
            sequence.Add("second.enter");
            e.Effect = DragDropEffects.Copy;
        };
        second.DragOver += (_, e) =>
        {
            sequence.Add("second.over");
            e.Effect = DragDropEffects.Link;
        };
        second.DragDrop += (_, e) =>
        {
            sequence.Add("second.drop");
            e.Effect.Should().Be(DragDropEffects.None);
            e.Effect = DragDropEffects.Copy;
        };

        int queryContinueCalls = 0;
        source.QueryContinueDrag += (_, e) =>
        {
            queryContinueCalls++;
            e.KeyState.Should().Be(8);
            e.Action = DragAction.Continue;
        };
        int feedbackCalls = 0;
        source.GiveFeedback += (_, e) =>
        {
            feedbackCalls++;
            e.Effect.Should().Be(DragDropEffects.Copy);
            e.UseDefaultCursors = false;
        };

        platform.DragDropHandler = (request, session) =>
        {
            request.Source.Should().Be(sourceHandle);
            request.AllowedEffects.Should().Be(LibreDragDropEffects.Copy | LibreDragDropEffects.Move);
            request.Data.Formats.Should().BeEquivalentTo([DataFormats.FileDrop, DataFormats.UnicodeText]);
            request.Data.Contains(DataFormats.UnicodeText, autoConvert: false).Should().BeTrue();
            request.Data.GetData(DataFormats.UnicodeText, autoConvert: false).Should().Be("canonical drag text");
            session.QueryContinue(keyState: 8, escapePressed: false).Should().Be(LibreDragAction.Continue);
            session.GiveFeedback(LibreDragDropEffects.Copy).Should().BeFalse();

            LibreDragTransition first = session.Enter(
                childHandle,
                keyState: 8,
                new LibrePoint(640, 480),
                LibreDragDropEffects.Copy);
            first.Target.Should().Be(parentHandle);
            first.Effect.Should().Be(LibreDragDropEffects.Move);
            session.Over(parentHandle, 8, new LibrePoint(640, 480), first.Effect)
                .Should().Be(LibreDragDropEffects.Copy);
            session.Leave(parentHandle);

            LibreDragTransition next = session.Enter(
                secondHandle,
                keyState: 8,
                new LibrePoint(640, 480),
                LibreDragDropEffects.Copy);
            next.Target.Should().Be(secondHandle);
            LibreDragDropEffects over = session.Over(
                secondHandle,
                keyState: 8,
                new LibrePoint(640, 480),
                next.Effect);
            over.Should().Be(LibreDragDropEffects.None);
            return session.Drop(secondHandle, 8, new LibrePoint(640, 480), over);
        };

        source.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move)
            .Should().Be(DragDropEffects.Copy);
        sequence.Should().Equal(
            "parent.enter",
            "parent.over",
            "parent.leave",
            "second.enter",
            "second.over",
            "second.drop");
        childEnterCalls.Should().Be(0);
        queryContinueCalls.Should().Be(1);
        feedbackCalls.Should().Be(1);
        enterKeyStates.Should().Equal(8);

        child.PointToScreen(new Point(3, 4)).Should().Be(new Point(327, 439));
        child.PointToClient(new Point(327, 439)).Should().Be(new Point(3, 4));

        sequence.Clear();
        platform.DragDropHandler = (_, session) =>
        {
            LibreDragTransition entered = session.Enter(
                childHandle,
                keyState: 0,
                new LibrePoint(640, 480),
                LibreDragDropEffects.Copy);
            session.Leave(entered.Target);
            return LibreDragDropEffects.None;
        };
        source.DoDragDrop(data, DragDropEffects.Copy).Should().Be(DragDropEffects.None);
        sequence.Should().Equal("parent.enter", "parent.leave");
        enterKeyStates.Should().Equal(8, 0);

        platform.DragDropHandler = (request, _) =>
        {
            request.AllowedEffects.Should().Be(LibreDragDropEffects.None);
            return (LibreDragDropEffects)0x100;
        };
        source.DoDragDrop("unhosted", (DragDropEffects)0x100).Should().Be(DragDropEffects.None);
    }

    [Fact]
    public void KeyboardRouting_PreservesModifiersParentBubblingAndMessageFilters()
    {
        const int wmKeyDown = 0x0100;
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new();
        using CommandProbeControl parent = new() { Bounds = new Rectangle(4, 5, 100, 80) };
        using Control child = new() { Bounds = new Rectangle(40, 30, 40, 30) };
        parent.Controls.Add(child);
        form.Controls.Add(parent);
        form.Show();

        Message childCommand = Message.Create(child.Handle, wmKeyDown, (nint)Keys.F6, 0);
        child.PreProcessMessage(ref childCommand).Should().BeTrue();
        parent.CommandCount.Should().Be(1);
        parent.LastKeyData.Should().Be(Keys.F6);

        parent.ResetCommands();
        platform.SendInput(LibreInputEventKind.FocusGained);
        platform.SendInput(
            LibreInputEventKind.PointerDown,
            position: new LibrePoint(10, 10),
            button: LibrePointerButton.Primary);
        platform.SendInput(
            LibreInputEventKind.PointerUp,
            position: new LibrePoint(10, 10),
            button: LibrePointerButton.Primary);
        platform.SendInput(
            LibreInputEventKind.KeyDown,
            modifiers: LibreInputModifiers.Control | LibreInputModifiers.Shift,
            key: LibreKey.Delete);

        parent.CommandCount.Should().Be(1);
        parent.LastKeyData.Should().Be(Keys.Delete | Keys.Control | Keys.Shift);

        var filter = new RecordingMessageFilter();
        Message filtered = Message.Create(child.Handle, wmKeyDown, (nint)Keys.F2, 0);
        Application.AddMessageFilter(filter);
        try
        {
            Application.FilterMessage(ref filtered).Should().BeTrue();
            filter.CallCount.Should().Be(1);
            filter.LastHWnd.Should().Be(child.Handle);
            filter.LastMessage.Should().Be(wmKeyDown);
            filter.LastKeyCode.Should().Be(Keys.F2);
        }
        finally
        {
            Application.RemoveMessageFilter(filter);
        }

        Application.FilterMessage(ref filtered).Should().BeFalse();
        filter.CallCount.Should().Be(1);
        form.Close();
    }

    [Fact]
    public void InputLanguageUsesTypedPortableInventoryAndActivation()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        InputLanguageCollection installed = InputLanguage.InstalledInputLanguages;
        installed.Cast<InputLanguage>().Select(language => language.Culture.Name)
            .Should().Equal("en-US", "de-DE");
        InputLanguage.DefaultInputLanguage.Culture.Name.Should().Be("en-US");
        InputLanguage.CurrentInputLanguage.Culture.Name.Should().Be("en-US");
        InputLanguage.CurrentInputLanguage.LayoutName.Should().Be("US");
        InputLanguage.CurrentInputLanguage.Handle.Should().Be((nint)0x0409);

        InputLanguage german = InputLanguage.FromCulture(CultureInfo.GetCultureInfo("de-DE"))!;
        german.Should().NotBeNull();
        german.LayoutName.Should().Be("German");
        german.Handle.Should().Be((nint)0x0407);
        InputLanguage.CurrentInputLanguage = german;

        InputLanguage.CurrentInputLanguage.Should().Be(german);
        platform.InputLanguageActivationCount.Should().Be(1);
        InputLanguage.CurrentInputLanguage = null;
        InputLanguage.CurrentInputLanguage.Should().Be(InputLanguage.DefaultInputLanguage);
        platform.InputLanguageActivationCount.Should().Be(2);

        InputLanguage invalid = (InputLanguage)Activator.CreateInstance(
            typeof(InputLanguage),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            args: [(IntPtr)0x7777],
            culture: null)!;
        Action activateInvalid = () => InputLanguage.CurrentInputLanguage = invalid;
        activateInvalid.Should().Throw<ArgumentException>().WithParameterName("value");
        platform.InputLanguageActivationCount.Should().Be(2);
    }

    [Fact]
    public void KeyboardAndFocusCues_UseManagedPortableState()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.MenuAccessKeysUnderlinedValue = false;
        using Form form = new();
        using CueProbeButton button = new()
        {
            AutoSize = true,
            Text = "&Run"
        };
        form.Controls.Add(button);

        form.Show();

        button.KeyboardCues.Should().BeFalse();
        button.FocusCues.Should().BeFalse();
        button.GetPreferredSize(Size.Empty).Width.Should().BeGreaterThan(0);
        Action paint = () =>
        {
            form.Invalidate();
            form.Update();
        };
        paint.Should().NotThrow();

        Message showKeyboard = Message.Create(button.Handle, 0x0104, (nint)Keys.Menu, 0);
        button.PreProcessMessage(ref showKeyboard);
        button.KeyboardCues.Should().BeTrue();
        button.FocusCues.Should().BeFalse();

        Message showFocus = Message.Create(button.Handle, 0x0100, (nint)Keys.Tab, 0);
        button.PreProcessMessage(ref showFocus);
        button.KeyboardCues.Should().BeTrue();
        button.FocusCues.Should().BeTrue();

        form.Close();
    }

    [Fact]
    public void FormSizeConstraints_UseTypedInitialAndLivePlatformState()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new()
        {
            MinimumSize = new Size(200, 150),
            MaximumSize = new Size(900, 700),
        };

        nint handle = form.Handle;

        platform.LastWindowMinimumSize.Should().Be(new LibreSize(200, 150));
        platform.LastWindowMaximumSize.Should().Be(new LibreSize(900, 700));

        form.MinimumSize = new Size(300, 240);
        platform.LastWindowMinimumSize.Should().Be(new LibreSize(300, 240));
        platform.LastWindowMaximumSize.Should().Be(new LibreSize(900, 700));
        form.Handle.Should().Be(handle);

        form.MaximumSize = new Size(640, 480);
        platform.LastWindowMinimumSize.Should().Be(new LibreSize(300, 240));
        platform.LastWindowMaximumSize.Should().Be(new LibreSize(640, 480));
        form.Handle.Should().Be(handle);

        form.MaximumSize = Size.Empty;
        platform.LastWindowMaximumSize.Should().Be(new LibreSize(0, 0));
        form.Handle.Should().Be(handle);
    }

    [Fact]
    public void MinimizeAndMaximizeBoxes_UseTypedInitialAndLiveChromeState()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { MinimizeBox = false, MaximizeBox = false };

        nint handle = form.Handle;

        platform.LastWindowCanMinimize.Should().BeFalse();
        platform.LastWindowCanMaximize.Should().BeFalse();

        form.MinimizeBox = true;
        platform.LastWindowCanMinimize.Should().BeTrue();
        form.Handle.Should().Be(handle);

        form.MaximizeBox = true;
        platform.LastWindowCanMaximize.Should().BeTrue();
        form.Handle.Should().Be(handle);

        form.MinimizeBox = false;
        form.MaximizeBox = false;
        platform.LastWindowCanMinimize.Should().BeFalse();
        platform.LastWindowCanMaximize.Should().BeFalse();
        form.Handle.Should().Be(handle);
    }

    [Fact]
    public void ControlBox_UsesTypedInitialAndLiveChromeState()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ControlBox = false };

        nint handle = form.Handle;

        platform.LastWindowCanClose.Should().BeFalse();
        platform.LastWindowCanMinimize.Should().BeFalse();
        platform.LastWindowCanMaximize.Should().BeFalse();

        form.ControlBox = true;
        platform.LastWindowCanClose.Should().BeTrue();
        platform.LastWindowCanMinimize.Should().BeTrue();
        platform.LastWindowCanMaximize.Should().BeTrue();
        form.Handle.Should().Be(handle);

        form.ControlBox = false;
        platform.LastWindowCanClose.Should().BeFalse();
        platform.LastWindowCanMinimize.Should().BeFalse();
        platform.LastWindowCanMaximize.Should().BeFalse();
        form.Handle.Should().Be(handle);
    }

    [Fact]
    public void Opacity_UsesTypedInitialAndLiveWholeWindowState()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { Opacity = 0.35d };

        nint handle = form.Handle;

        platform.LastWindowOpacity.Should().Be(0.35d);

        form.Opacity = 0.72d;
        platform.LastWindowOpacity.Should().Be(0.72d);
        form.Handle.Should().Be(handle);

        form.Opacity = 2d;
        form.Opacity.Should().Be(1d);
        platform.LastWindowOpacity.Should().Be(1d);
        form.Handle.Should().Be(handle);

        form.Opacity = -1d;
        form.Opacity.Should().Be(0d);
        platform.LastWindowOpacity.Should().Be(0d);
        form.Handle.Should().Be(handle);

        form.Opacity = double.NaN;
        double.IsNaN(form.Opacity).Should().BeTrue();
        platform.LastWindowOpacity.Should().Be(0d);
        form.Handle.Should().Be(handle);

        form.AllowTransparency = false;
        form.Opacity.Should().Be(1d);
        platform.LastWindowOpacity.Should().Be(1d);
        form.Handle.Should().Be(handle);
    }

    [Fact]
    public void ShowInTaskbar_UsesTypedInitialAndLivePlatformState()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowInTaskbar = false };

        nint handle = form.Handle;

        platform.LastWindowShowInTaskbar.Should().BeFalse();

        form.ShowInTaskbar = true;
        platform.LastWindowShowInTaskbar.Should().BeTrue();
        form.Handle.Should().Be(handle);

        form.FormBorderStyle = FormBorderStyle.FixedToolWindow;
        platform.LastWindowShowInTaskbar.Should().BeFalse();

        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        platform.LastWindowShowInTaskbar.Should().BeTrue();

        form.ShowInTaskbar = false;
        platform.LastWindowShowInTaskbar.Should().BeFalse();
        form.Handle.Should().Be(handle);
    }

    [Fact]
    public void FormBorderStyle_UsesTypedInitialAndLiveWindowBorder()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { FormBorderStyle = FormBorderStyle.None };

        _ = form.Handle;

        platform.LastWindowBorder.Should().Be(LibreWindowBorder.Hidden);

        form.FormBorderStyle = FormBorderStyle.Sizable;
        platform.LastWindowBorder.Should().Be(LibreWindowBorder.Resizable);

        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        platform.LastWindowBorder.Should().Be(LibreWindowBorder.Fixed);

        form.FormBorderStyle = FormBorderStyle.None;
        platform.LastWindowBorder.Should().Be(LibreWindowBorder.Hidden);
    }

    [Fact]
    public void FormTopMost_UsesTypedInitialAndLiveWindowTopMost()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { TopMost = true };

        _ = form.Handle;

        platform.LastWindowTopMost.Should().BeTrue();

        form.TopMost = false;
        platform.LastWindowTopMost.Should().BeFalse();

        form.TopMost = true;
        platform.LastWindowTopMost.Should().BeTrue();
    }

    [Fact]
    public void FormOwnershipAndKeysConverter_PreserveCanonicalWinFormsContracts()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { Name = "owner" };
        using Form child = new() { Name = "child", TopMost = true };
        int shownCount = 0;
        int closedCount = 0;
        CloseReason closedReason = CloseReason.None;
        child.Shown += (_, _) => shownCount++;
        child.FormClosed += (_, e) =>
        {
            closedCount++;
            closedReason = e.CloseReason;
        };

        owner.Show();
        child.Show(owner);
        platform.PumpOnce();
        platform.PumpOnce();

        child.Owner.Should().BeSameAs(owner);
        owner.OwnedForms.Should().ContainSingle().Which.Should().BeSameAs(child);
        platform.GetWindowOwner(child).Should().Be(new LibreHandle(owner.Handle, LibreHandleKind.Window));
        child.Visible.Should().BeTrue();
        shownCount.Should().Be(1);
        platform.LastWindowTopMost.Should().BeTrue();

        child.TopMost = false;
        platform.LastWindowTopMost.Should().BeFalse();
        child.TopMost = true;
        platform.LastWindowTopMost.Should().BeTrue();

        Action showWithSelfOwner = () => child.Show(child);
        showWithSelfOwner.Should().Throw<InvalidOperationException>();

        owner.Close();
        closedCount.Should().Be(1);
        closedReason.Should().Be(CloseReason.FormOwnerClosing);

        var converter = new KeysConverter();
        TypeDescriptor.GetConverter(typeof(Keys)).Should().BeOfType<KeysConverter>();
        converter.ConvertFromInvariantString("Control+Shift+F").Should().Be(Keys.Control | Keys.Shift | Keys.F);
        converter.ConvertFromInvariantString("Ctrl+Alt+H").Should().Be(Keys.Control | Keys.Alt | Keys.H);
        converter.ConvertFromInvariantString("Control + H").Should().Be(Keys.Control | Keys.H);
        converter.ConvertFromInvariantString("F3").Should().Be(Keys.F3);
        converter.ConvertFromInvariantString("0").Should().Be(Keys.D0);
        converter.ConvertFromInvariantString("None").Should().Be(Keys.None);
        converter.ConvertFromInvariantString("(none)").Should().Be(Keys.None);
        converter.ConvertToInvariantString(Keys.Control | Keys.Alt | Keys.Shift | Keys.F1)
            .Should().Be("Ctrl+Alt+Shift+F1");
        converter.ConvertToInvariantString(Keys.Control | Keys.H).Should().Be("Ctrl+H");
        converter.ConvertToInvariantString(Keys.None).Should().Be("(none)");
        converter.ConvertFrom(
                context: null,
                CultureInfo.InvariantCulture,
                new Enum[] { Keys.Control, Keys.Shift, Keys.F })
            .Should().Be(Keys.Control | Keys.Shift | Keys.F);
        converter.ConvertFromInvariantString("   ").Should().BeNull();

        Action unknownKey = () => converter.ConvertFromInvariantString("Control+DefinitelyNotAKey");
        unknownKey.Should().Throw<ArgumentException>();
        Action multipleKeys = () => converter.ConvertFromInvariantString("A+B");
        multipleKeys.Should().Throw<FormatException>();
    }

    [Fact]
    public void ControlCollections_PreserveCanonicalParentingEventsAndOrdering()
    {
        using Control owner = new();
        using Control first = new() { Name = "first" };
        using Control second = new() { Name = "second" };
        using Control replacement = new() { Name = "replacement" };
        List<string> events = [];
        owner.ControlAdded += (_, e) => events.Add("add:" + e.Control!.Name);
        owner.ControlRemoved += (_, e) => events.Add("remove:" + e.Control!.Name);

        owner.Controls.Add(first);
        owner.Controls.Add(second);
        owner.Controls.Remove(first);
        owner.Controls.Add(replacement);
        owner.Controls.Remove(second);
        owner.Controls.Clear();

        events.Should().Equal(
            "add:first",
            "add:second",
            "remove:first",
            "add:replacement",
            "remove:second",
            "remove:replacement");
        first.Parent.Should().BeNull();
        second.Parent.Should().BeNull();
        replacement.Parent.Should().BeNull();

        using Control oldParent = new();
        using Control newParent = new();
        using Control child = new() { Name = "child" };
        events.Clear();
        oldParent.ControlRemoved += (_, e) =>
        {
            events.Add("remove:" + e.Control!.Name);
            e.Control.Parent.Should().BeNull();
            oldParent.Controls.Contains(e.Control).Should().BeFalse();
        };
        newParent.ControlAdded += (_, e) =>
        {
            events.Add("add:" + e.Control!.Name);
            e.Control.Parent.Should().BeSameAs(newParent);
            newParent.Controls.Contains(e.Control).Should().BeTrue();
        };

        oldParent.Controls.Add(child);
        newParent.Controls.Add(child);
        oldParent.Controls.Count.Should().Be(0);
        newParent.Controls.Count.Should().Be(1);
        newParent.Controls[0].Should().BeSameAs(child);
        child.Parent.Should().BeSameAs(newParent);
        events.Should().Equal("remove:child", "add:child");

        child.Parent = oldParent;
        child.Parent = oldParent;
        oldParent.Controls.Count.Should().Be(1);
        oldParent.Controls[0].Should().BeSameAs(child);
        child.Parent = newParent;
        oldParent.Controls.Count.Should().Be(0);
        newParent.Controls.Count.Should().Be(1);
        newParent.Controls[0].Should().BeSameAs(child);
        child.Parent = null;
        newParent.Controls.Count.Should().Be(0);

        using Control orderParent = new();
        using Control orderFirst = new();
        using Control orderSecond = new();
        int added = 0;
        int removed = 0;
        orderParent.ControlAdded += (_, _) => added++;
        orderParent.ControlRemoved += (_, _) => removed++;
        orderParent.Controls.Add(orderFirst);
        orderParent.Controls.Add(orderSecond);
        orderParent.Controls.Add(orderFirst);
        orderParent.Controls.Count.Should().Be(2);
        orderParent.Controls[1].Should().BeSameAs(orderFirst);
        orderParent.Controls.SetChildIndex(orderFirst, 0);
        orderParent.Controls[0].Should().BeSameAs(orderFirst);
        added.Should().Be(2);
        removed.Should().Be(0);

        using Control listParent = new();
        using Control listChild = new();
        ((System.Collections.IList)listParent.Controls).Add(listChild);
        listParent.Controls.Count.Should().Be(1);
        listParent.Controls[0].Should().BeSameAs(listChild);
        listChild.Parent.Should().BeSameAs(listParent);

        using TabControl oldTabs = new();
        using TabControl newTabs = new();
        using TabPage page = new();
        oldTabs.TabPages.Add(page);
        newTabs.Controls.Add(page);
        oldTabs.Controls.Count.Should().Be(0);
        oldTabs.TabPages.Count.Should().Be(0);
        newTabs.Controls.Count.Should().Be(1);
        newTabs.Controls[0].Should().BeSameAs(page);
        newTabs.TabPages.Count.Should().Be(1);
        newTabs.TabPages[0].Should().BeSameAs(page);
        page.Parent.Should().BeSameAs(newTabs);

        using Control cycleRoot = new();
        using Control cycleChild = new();
        cycleRoot.Controls.Add(cycleChild);
        Action createCycle = () => cycleChild.Controls.Add(cycleRoot);
        createCycle.Should().Throw<ArgumentException>();
        cycleRoot.Parent.Should().BeNull();
        cycleChild.Parent.Should().BeSameAs(cycleRoot);
        cycleRoot.Controls.Count.Should().Be(1);
        cycleRoot.Controls[0].Should().BeSameAs(cycleChild);
        cycleChild.Controls.Count.Should().Be(0);
    }

    [Fact]
    public void ControlTree_PreservesCanonicalSizingInvalidationAndHandleCleanup()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using Control sizing = new() { Size = new Size(-20, -30) };
        sizing.Size.Should().Be(new Size(-20, -30));
        sizing.SetBounds(4, 5, -6, 7);
        sizing.Bounds.Should().Be(new Rectangle(4, 5, -6, 7));

        using Control root = new();
        using Control child = new();
        using Control grandchild = new();
        root.Controls.Add(child);
        child.Controls.Add(grandchild);
        root.CreateControl();
        int rootInvalidated = 0;
        int childInvalidated = 0;
        int grandchildInvalidated = 0;
        root.Invalidated += (_, _) => rootInvalidated++;
        child.Invalidated += (_, _) => childInvalidated++;
        grandchild.Invalidated += (_, _) => grandchildInvalidated++;

        root.Invalidate(invalidateChildren: false);
        (rootInvalidated, childInvalidated, grandchildInvalidated).Should().Be((1, 0, 0));
        root.Invalidate(invalidateChildren: true);
        // Upstream invalidates descendant native windows without raising a managed
        // Invalidated event for every child; portable retained painting follows the
        // root dirty region while preserving that public event behavior.
        (rootInvalidated, childInvalidated, grandchildInvalidated).Should().Be((2, 0, 0));

        using Control visual = new();
        visual.CreateControl();
        int visualInvalidated = 0;
        int textChanged = 0;
        visual.Invalidated += (_, _) => visualInvalidated++;
        visual.TextChanged += (_, _) => textChanged++;
        visual.Text = "Updated";
        visual.BackColor = Color.AliceBlue;
        visual.ForeColor = Color.DarkSlateGray;
        // Upstream Control.Text raises TextChanged without a managed Invalidated event;
        // BackColor and ForeColor each invalidate the created control once.
        visualInvalidated.Should().Be(2);
        textChanged.Should().Be(1);
        visual.Text = "Updated";
        visual.BackColor = Color.AliceBlue;
        visual.ForeColor = Color.DarkSlateGray;
        visualInvalidated.Should().Be(2);
        textChanged.Should().Be(1);

        nint childHandle = child.Handle;
        nint grandchildHandle = grandchild.Handle;
        root.Dispose();
        root.IsDisposed.Should().BeTrue();
        child.IsDisposed.Should().BeTrue();
        grandchild.IsDisposed.Should().BeTrue();
        root.Controls.Count.Should().Be(0);
        child.Controls.Count.Should().Be(0);
        child.Parent.Should().BeNull();
        grandchild.Parent.Should().BeNull();
        Control.FromChildHandle(childHandle).Should().BeNull();
        Control.FromChildHandle(grandchildHandle).Should().BeNull();

        using Control separateParent = new();
        using Control separateChild = new();
        separateParent.Controls.Add(separateChild);
        separateChild.Dispose();
        separateParent.Controls.Count.Should().Be(0);
        separateChild.Parent.Should().BeNull();
    }

    [Fact]
    public void DesignerInitializableControls_PreserveCanonicalSplitContainerContracts()
    {
        using SplitContainer split = new();
        split.Should().BeAssignableTo<ISupportInitialize>();
        split.Orientation.Should().Be(Orientation.Vertical);
        split.SplitterDistance.Should().Be(50);
        split.SplitterWidth.Should().Be(4);
        split.Panel1MinSize.Should().Be(25);
        split.Panel2MinSize.Should().Be(25);
        split.Controls.Count.Should().Be(2);
        split.Controls[0].Should().BeSameAs(split.Panel1);
        split.Controls[1].Should().BeSameAs(split.Panel2);

        ISupportInitialize initialization = split;
        initialization.BeginInit();
        split.Orientation = Orientation.Horizontal;
        split.SplitterDistance = 42;
        split.Panel1MinSize = 30;
        split.Panel2MinSize = 35;
        split.SplitterWidth = 6;
        split.Orientation.Should().Be(Orientation.Horizontal);
        split.SplitterDistance.Should().Be(42);
        split.SplitterWidth.Should().Be(4);
        split.Panel1MinSize.Should().Be(25);
        split.Panel2MinSize.Should().Be(25);
        initialization.EndInit();
        split.SplitterWidth.Should().Be(6);
        split.Panel1MinSize.Should().Be(30);
        split.Panel2MinSize.Should().Be(35);

        initialization.BeginInit();
        split.SplitterWidth = 0;
        split.SplitterWidth.Should().Be(6);
        Action finishInvalidInitialization = initialization.EndInit;
        finishInvalidInitialization.Should().Throw<ArgumentOutOfRangeException>();
        split.SplitterWidth.Should().Be(6);
        Action setInvalidWidth = () => split.SplitterWidth = 0;
        setInvalidWidth.Should().Throw<ArgumentOutOfRangeException>();
        split.SplitterWidth.Should().Be(6);

        using NumericUpDown numeric = new();
        using TrackBar track = new();
        numeric.Should().BeAssignableTo<ISupportInitialize>();
        track.Should().BeAssignableTo<ISupportInitialize>();
    }

    [Fact]
    public void FormWindowState_UsesTypedInitialLiveAndPlatformDrivenTransitions()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { WindowState = FormWindowState.Maximized };

        _ = form.Handle;

        platform.LastWindowState.Should().Be(LibreWindowState.Maximized);

        form.WindowState = FormWindowState.Normal;
        platform.LastWindowState.Should().Be(LibreWindowState.Normal);

        form.Show();
        form.WindowState = FormWindowState.Minimized;
        platform.LastWindowState.Should().Be(LibreWindowState.Minimized);
        form.WindowState.Should().Be(FormWindowState.Minimized);

        platform.ChangeLastWindowState(LibreWindowState.Maximized);
        form.WindowState.Should().Be(FormWindowState.Maximized);

        platform.ChangeLastWindowState(LibreWindowState.FullScreen);
        form.WindowState.Should().Be(FormWindowState.Maximized);

        platform.ChangeLastWindowState(LibreWindowState.Normal);
        form.WindowState.Should().Be(FormWindowState.Normal);
    }

    [Fact]
    public void FormText_UsesTypedLiveWindowTitleWithoutUser32StyleRefresh()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { Text = "Initial title" };

        _ = form.Handle;

        platform.LastWindowTitle.Should().Be("Initial title");

        form.Text = "Updated title";
        platform.LastWindowTitle.Should().Be("Updated title");

        form.Text = string.Empty;
        platform.LastWindowTitle.Should().BeEmpty();

        form.Text = "Restored title";
        platform.LastWindowTitle.Should().Be("Restored title");
    }

    [Fact]
    public void FormIcon_UsesTypedRgbaWindowIconTransportAndShowIconClearsIt()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Bitmap bitmap = new(2, 1);
        bitmap.SetPixel(0, 0, Color.FromArgb(255, 10, 20, 30));
        bitmap.SetPixel(1, 0, Color.FromArgb(255, 200, 150, 100));
        using MemoryStream stream = new();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;
        using Icon icon = new(stream);
        using Form form = new() { Icon = icon };

        _ = form.Handle;

        platform.LastWindowIcons.Should().HaveCount(2);
        LibreWindowIcon original = platform.LastWindowIcons[0];
        byte[] pixels = new byte[original.PixelByteLength];
        original.CopyPixelsTo(pixels);
        original.Width.Should().Be(2);
        original.Height.Should().Be(1);
        pixels.Should().Equal(10, 20, 30, 255, 200, 150, 100, 255);

        form.ShowIcon = false;
        platform.LastWindowIcons.Should().BeEmpty();

        form.ShowIcon = true;
        platform.LastWindowIcons.Should().HaveCount(2);
    }

    [Fact]
    public void ImageList_UsesManagedImagesWithoutHdcOrFakeNativeHandles()
    {
        using var images = new ImageList { ImageSize = new Size(4, 4) };
        using Bitmap red = CreateSolidBitmap(4, 4, Color.Red);
        using Bitmap strip = new(8, 4, PixelFormat.Format32bppArgb);
        for (int y = 0; y < strip.Height; y++)
        {
            for (int x = 0; x < strip.Width; x++)
            {
                strip.SetPixel(x, y, x < 4 ? Color.Blue : Color.Green);
            }
        }

        images.Images.Add("red", red);
        images.Images.AddStrip(strip);

        images.Images.Count.Should().Be(3);
        images.HandleCreated.Should().BeFalse();
        Action nativeHandle = () => _ = images.Handle;
        nativeHandle.Should().Throw<PlatformNotSupportedException>()
            .WithMessage("*Windows common-controls adapter*");

        using (Image first = images.Images[0])
        using (Image second = images.Images[1])
        using (Image third = images.Images[2])
        {
            ((Bitmap)first).GetPixel(2, 2).ToArgb().Should().Be(Color.Red.ToArgb());
            ((Bitmap)second).GetPixel(2, 2).ToArgb().Should().Be(Color.Blue.ToArgb());
            ((Bitmap)third).GetPixel(2, 2).ToArgb().Should().Be(Color.Green.ToArgb());
        }

        using var target = new Bitmap(12, 4, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(target))
        {
            graphics.Clear(Color.Transparent);
            images.Draw(graphics, 0, 0, 0);
            images.Draw(graphics, 4, 0, 1);
            images.Draw(graphics, 8, 0, 2);
        }

        target.GetPixel(2, 2).ToArgb().Should().Be(Color.Red.ToArgb());
        target.GetPixel(6, 2).ToArgb().Should().Be(Color.Blue.ToArgb());
        target.GetPixel(10, 2).ToArgb().Should().Be(Color.Green.ToArgb());

        using Bitmap yellow = CreateSolidBitmap(4, 4, Color.Yellow);
        images.Images[0] = yellow;
        images.Images.RemoveAt(1);
        images.Images.Count.Should().Be(2);
        using (Image replacement = images.Images[0])
        using (Image remainingStripFrame = images.Images[1])
        {
            ((Bitmap)replacement).GetPixel(2, 2).ToArgb().Should().Be(Color.Yellow.ToArgb());
            ((Bitmap)remainingStripFrame).GetPixel(2, 2).ToArgb().Should().Be(Color.Green.ToArgb());
        }

        images.Images.Clear();
        images.Images.Count.Should().Be(0);

        static Bitmap CreateSolidBitmap(int width, int height, Color color)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(color);
            return bitmap;
        }
    }

    [Fact]
    public void VisualStyleBackgroundAndRegionUseTypedManagedService()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        Application.EnableVisualStyles();
        VisualStyleRenderer.IsSupported.Should().BeTrue();
        var renderer = new VisualStyleRenderer(VisualStyleElement.Button.PushButton.Normal);
        using var target = new Bitmap(8, 8, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(target))
        {
            graphics.Clear(Color.Transparent);
            renderer.DrawBackground(graphics, new Rectangle(1, 1, 6, 6), new Rectangle(4, 0, 4, 8));
            using Region? region = renderer.GetBackgroundRegion(graphics, new Rectangle(1, 2, 4, 5));
            region.Should().NotBeNull();
            region!.IsVisible(2, 3).Should().BeTrue();
            region.IsVisible(0, 0).Should().BeFalse();
            renderer.GetBackgroundContentRectangle(graphics, new Rectangle(0, 0, 20, 12))
                .Should().Be(new Rectangle(2, 2, 16, 8));
            renderer.GetBackgroundExtent(graphics, new Rectangle(1, 2, 30, 12))
                .Should().Be(new Rectangle(8, 9, 40, 22));
            renderer.GetPartSize(graphics, ThemeSizeType.True).Should().Be(new Size(21, 22));
            renderer.DrawEdge(
                graphics,
                new Rectangle(0, 0, 8, 8),
                Edges.Left | Edges.Top,
                EdgeStyle.Raised,
                EdgeEffects.None).Should().Be(new Rectangle(1, 1, 7, 7));
            renderer.DrawText(
                graphics,
                new Rectangle(0, 0, 8, 8),
                "text",
                drawDisabled: false,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            renderer.GetMargins(graphics, MarginProperty.ContentMargins).Should().Be(new Padding(4, 5, 6, 7));
            using Font? themeFont = renderer.GetFont(graphics, FontProperty.TextFont);
            themeFont.Should().NotBeNull();
            themeFont!.Size.Should().Be(10f);
            renderer.GetTextExtent(
                graphics,
                new Rectangle(1, 2, 30, 12),
                "measure",
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter)
                .Should().Be(new Rectangle(6, 7, 8, 9));
            renderer.HitTestBackground(
                graphics,
                new Rectangle(1, 2, 30, 12),
                new Point(2, 3),
                HitTestOptions.ResizingBorderLeft)
                .Should().Be(HitTestCode.Left);
            using var hitRegion = new Region(new Rectangle(1, 2, 4, 4));
            renderer.HitTestBackground(
                graphics,
                new Rectangle(1, 2, 30, 12),
                hitRegion,
                new Point(2, 3),
                HitTestOptions.ResizingBorderRight)
                .Should().Be(HitTestCode.Right);
            Action nativeRegionHitTest = () => renderer.HitTestBackground(
                graphics,
                new Rectangle(1, 2, 30, 12),
                new IntPtr(1),
                new Point(2, 3),
                HitTestOptions.BackgroundSegment);
            nativeRegionHitTest.Should().Throw<PlatformNotSupportedException>();
            TextMetrics metrics = renderer.GetTextMetrics(graphics);
            metrics.Height.Should().Be(20);
            metrics.Ascent.Should().Be(14);
            metrics.Descent.Should().Be(4);
            metrics.AverageCharWidth.Should().Be(7);
            metrics.MaxCharWidth.Should().Be(12);
            metrics.Weight.Should().Be(600);
            metrics.Italic.Should().BeTrue();
            metrics.Underlined.Should().BeTrue();
            metrics.StruckOut.Should().BeFalse();
            metrics.PitchAndFamily.Should().Be(
                TextMetricsPitchAndFamilyValues.FixedPitch | TextMetricsPitchAndFamilyValues.TrueType);
            metrics.CharSet.Should().Be(TextMetricsCharacterSet.Baltic);
        }

        target.GetPixel(2, 3).ToArgb().Should().Be(0);
        target.GetPixel(5, 3).ToArgb().Should().Be(Color.Purple.ToArgb());
        renderer.GetColor(ColorProperty.TextColor).ToArgb().Should().Be(Color.Orange.ToArgb());
        renderer.GetInteger(IntegerProperty.ProgressChunkSize).Should().Be(7);
        renderer.GetBoolean(BooleanProperty.BackgroundFill).Should().BeTrue();
        renderer.GetEnumValue(EnumProperty.BackgroundType).Should().Be(1);
        renderer.GetFilename(FilenameProperty.ImageFile).Should().Be("managed-theme-image");
        renderer.GetString(StringProperty.Text).Should().Be("managed-theme-text");
        renderer.GetPoint(PointProperty.TextShadowOffset).Should().Be(new Point(2, 3));
        renderer.IsBackgroundPartiallyTransparent().Should().BeFalse();
        platform.VisualStyleDrawCount.Should().Be(1);
        platform.VisualStyleEdgeDrawCount.Should().Be(1);
        platform.VisualStyleTextDrawCount.Should().Be(1);
        Action nativeHandle = () => _ = renderer.Handle;
        nativeHandle.Should().Throw<PlatformNotSupportedException>()
            .WithMessage("*Windows UxTheme adapter*");
    }

    [Fact]
    public void VisualStyleParentBackgroundUsesManagedControlPaintingWithoutHandles()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        Application.EnableVisualStyles();
        var renderer = new VisualStyleRenderer(VisualStyleElement.Button.PushButton.Normal);
        using var parent = new ParentPaintingControl { Size = new Size(20, 20) };
        using var child = new Control { Location = new Point(4, 5), Size = new Size(6, 6) };
        parent.Controls.Add(child);
        using var target = new Bitmap(6, 6, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(target))
        {
            graphics.Clear(Color.Transparent);
            renderer.DrawParentBackground(graphics, new Rectangle(0, 0, 6, 6), child);
        }

        child.IsHandleCreated.Should().BeFalse();
        parent.BackgroundPaintCount.Should().Be(1);
        parent.ForegroundPaintCount.Should().Be(1);
        target.GetPixel(2, 2).ToArgb().Should().Be(Color.Orange.ToArgb());
        target.GetPixel(3, 3).ToArgb().Should().Be(Color.CornflowerBlue.ToArgb());
    }

    [Fact]
    public void TextRendererUsesTypedManagedServiceWithoutHdc()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using var target = new Bitmap(80, 30, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(target);
        graphics.Clear(Color.Transparent);

        TextRenderer.DrawText(
            graphics,
            "portable",
            SystemFonts.DefaultFont,
            new Rectangle(4, 5, 60, 18),
            Color.Navy,
            Color.Beige,
            TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPadding
                | TextFormatFlags.TextBoxControl);
        Size headless = TextRenderer.MeasureText(
            "headless",
            SystemFonts.DefaultFont,
            new Size(70, 30),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        Size managed = TextRenderer.MeasureText(
            graphics,
            "managed",
            SystemFonts.DefaultFont,
            new Size(80, 40),
            TextFormatFlags.WordBreak | TextFormatFlags.LeftAndRightPadding);
        var nativeContext = new TrackingDeviceContext();
        Action nativeMeasure = () => TextRenderer.MeasureText(nativeContext, "native", SystemFonts.DefaultFont);

        platform.TextDrawCount.Should().Be(1);
        platform.TextMeasureCount.Should().Be(2);
        platform.LastTextBounds.Should().Be(new Rectangle(4, 5, 60, 18));
        platform.LastTextFormat.Should().Be(
            LibreTextFormat.WordBreak | LibreTextFormat.LeftAndRightPadding);
        target.GetPixel(4, 5).ToArgb().Should().Be(Color.Navy.ToArgb());
        headless.Should().Be(new Size(31, 17));
        managed.Should().Be(new Size(37, 19));
        nativeMeasure.Should().Throw<PlatformNotSupportedException>()
            .WithMessage("*managed Graphics*platform adapter*");
        nativeContext.GetHdcCalled.Should().BeFalse();
    }

    [Fact]
    public void ControlPaintDisabledTextUsesTypedManagedServiceWithoutHdc()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using var target = new Bitmap(80, 30, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(target);
        graphics.Clear(Color.Transparent);

        ControlPaint.DrawStringDisabled(
            graphics,
            "disabled",
            SystemFonts.DefaultFont,
            Color.Navy,
            new Rectangle(4, 5, 60, 18),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

        platform.TextDrawCount.Should().Be(2);
        platform.LastTextBounds.Should().Be(new Rectangle(4, 5, 60, 18));
        platform.LastTextFormat.Should().Be(LibreTextFormat.SingleLine | LibreTextFormat.NoPadding);

        var nativeContext = new TrackingDeviceContext();
        Action nativeDraw = () => ControlPaint.DrawStringDisabled(
            nativeContext,
            "disabled",
            SystemFonts.DefaultFont,
            Color.Navy,
            new Rectangle(4, 5, 60, 18),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

        nativeDraw.Should().Throw<PlatformNotSupportedException>()
            .WithMessage("*managed Graphics*platform adapter*");
        nativeContext.GetHdcCalled.Should().BeFalse();
    }

    [Fact]
    public void FontAutoScaleDimensionsUseManagedTextMetricsWithoutHfont()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using var container = new ContainerControl { AutoScaleMode = AutoScaleMode.Font };

        SizeF dimensions = container.CurrentAutoScaleDimensions;

        dimensions.Should().Be(new SizeF(8, container.Font.Height));
        platform.TextMeasureCount.Should().Be(1);
        platform.LastTextFormat.Should().Be(LibreTextFormat.SingleLine | LibreTextFormat.NoPadding);
        container.IsHandleCreated.Should().BeFalse();
    }

    [Fact]
    public void EmptyLabelPreferredSizeUsesManagedTextMetricsWithoutHfont()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using var label = new Label();

        Size preferredSize = label.GetPreferredSize(Size.Empty);

        preferredSize.Should().Be(new Size(0, label.Font.Height + 3));
        platform.TextMeasureCount.Should().Be(1);
        platform.LastTextFormat.Should().Be(LibreTextFormat.SingleLine | LibreTextFormat.NoPadding);
        label.IsHandleCreated.Should().BeFalse();
    }

    [Fact]
    public void CompatibleLabelPreferredSizeUsesManagedLayoutSurfaceWithoutScreenHdc()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using var label = new Label
        {
            Text = "compatible label",
            UseCompatibleTextRendering = true,
        };

        Size preferredSize = label.GetPreferredSize(Size.Empty);

        preferredSize.Should().NotBe(Size.Empty);
        platform.TextMeasureCount.Should().Be(0);
        label.IsHandleCreated.Should().BeFalse();
    }

    [Fact]
    public void ComboBoxPreferredHeightUsesManagedTextMetricsWithoutHfont()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using var comboBox = new ComboBox { FormattingEnabled = true };
        comboBox.ItemHeight = comboBox.ItemHeight;
        int measurementsBefore = platform.TextMeasureCount;

        int preferredHeight = comboBox.PreferredHeight;

        preferredHeight.Should().Be(
            comboBox.Font.Height
                + SystemInformation.Border3DSize.Height
                + (2 * SystemInformation.FixedFrameBorderSize.Height));
        platform.TextMeasureCount.Should().Be(measurementsBefore + 2);
        platform.LastTextFormat.Should().Be(LibreTextFormat.SingleLine | LibreTextFormat.NoPadding);
        comboBox.IsHandleCreated.Should().BeFalse();
    }

    [Fact]
    public void ComboBoxOwnerDrawMeasureItemSubscriptionStaysManaged()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var comboBox = new ComboBox { DrawMode = DrawMode.OwnerDrawFixed };
        MeasureItemEventHandler handler = (_, _) => { };

        comboBox.MeasureItem += handler;
        comboBox.IsHandleCreated.Should().BeFalse();

        comboBox.MeasureItem -= handler;
        comboBox.IsHandleCreated.Should().BeFalse();
    }

    [Fact]
    public void ComboBoxVariableItemHeightUsesManagedMeasureItemContract()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var comboBox = new ComboBox { DrawMode = DrawMode.OwnerDrawVariable };
        comboBox.Items.Add("measured");
        int measureCount = 0;
        comboBox.MeasureItem += (_, e) =>
        {
            measureCount++;
            e.ItemHeight = 41;
        };

        comboBox.GetItemHeight(0).Should().Be(41);
        measureCount.Should().Be(1);
    }

    [Fact]
    public void MonthCalendarDefaultSizeUsesManagedTextMetricsWithoutHfont()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        string todayText = DateTime.Now.ToShortDateString();

        using var calendar = new MonthCalendar();

        calendar.Size.Should().Be(calendar.SingleMonthSize + new Size(2, 2));
        platform.TextMeasureCount.Should().Be(1);
        platform.LastMeasuredText.Should().Be(todayText);
        platform.LastTextFormat.Should().Be(LibreTextFormat.SingleLine | LibreTextFormat.NoPadding);
        calendar.IsHandleCreated.Should().BeFalse();
    }

    [Fact]
    public void ButtonPreferredSizesUseManagedLayoutSurfacesWithoutScreenHdc()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using var button = new Button { Text = "button", UseCompatibleTextRendering = false };
        using var checkBox = new CheckBox { Text = "check", UseCompatibleTextRendering = false };
        using var radioButton = new RadioButton { Text = "radio", UseCompatibleTextRendering = false };
        using var compatibleButton = new Button
        {
            Text = "compatible",
            UseCompatibleTextRendering = true,
        };

        Size buttonSize = button.GetPreferredSize(Size.Empty);
        Size checkBoxSize = checkBox.GetPreferredSize(Size.Empty);
        Size radioButtonSize = radioButton.GetPreferredSize(Size.Empty);
        Size compatibleSize = compatibleButton.GetPreferredSize(Size.Empty);

        buttonSize.Should().NotBe(Size.Empty);
        checkBoxSize.Should().NotBe(Size.Empty);
        radioButtonSize.Should().NotBe(Size.Empty);
        compatibleSize.Should().NotBe(Size.Empty);
        platform.TextMeasureCount.Should().Be(3);
        platform.LastMeasuredText.Should().Be("radio");
        button.IsHandleCreated.Should().BeFalse();
        checkBox.IsHandleCreated.Should().BeFalse();
        radioButton.IsHandleCreated.Should().BeFalse();
        compatibleButton.IsHandleCreated.Should().BeFalse();
    }

    [Fact]
    public void DataGridViewLayoutUsesManagedGraphicsWithoutScreenHdc()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using var grid = new DataGridView
        {
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            ColumnHeadersVisible = false,
        };

        var textColumn = new DataGridViewTextBoxColumn
        {
            Width = 72,
            DefaultCellStyle = { WrapMode = DataGridViewTriState.True },
        };
        var comboColumn = new DataGridViewComboBoxColumn { Width = 72 };
        comboColumn.Items.AddRange("first", "second");
        grid.Columns.AddRange(textColumn, comboColumn);
        int rowIndex = grid.Rows.Add("wrapped DataGridView text", "second");
        int measurementsBefore = platform.TextMeasureCount;

        SystemInformation.DragSize.Should().Be(new Size(4, 4));
        grid.AutoResizeColumn(0, DataGridViewAutoSizeColumnMode.AllCellsExceptHeader);
        grid.AutoResizeRow(rowIndex, DataGridViewAutoSizeRowMode.AllCellsExceptHeader);
        Rectangle textBounds = grid.Rows[rowIndex].Cells[0].GetContentBounds(rowIndex);
        Rectangle comboBounds = grid.Rows[rowIndex].Cells[1].GetContentBounds(rowIndex);

        grid.Columns[0].Width.Should().BeGreaterThan(0);
        grid.Rows[rowIndex].Height.Should().BeGreaterThan(0);
        textBounds.Should().NotBe(Rectangle.Empty);
        comboBounds.Should().NotBe(Rectangle.Empty);
        platform.TextMeasureCount.Should().BeGreaterThan(measurementsBefore);
        grid.IsHandleCreated.Should().BeFalse();
    }

    [Fact]
    public void MaskedTextBoxPreservesCanonicalMaskAndValidationContracts()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var textBox = new MaskedTextBox("000-00")
        {
            TextMaskFormat = MaskFormat.IncludeLiterals,
            Text = "12345",
        };

        textBox.Text.Should().Be("123-45");
        textBox.MaskCompleted.Should().BeTrue();
        textBox.MaskFull.Should().BeTrue();

        textBox.TextMaskFormat = MaskFormat.ExcludePromptAndLiterals;
        textBox.Text.Should().Be("12345");
        textBox.ValidatingType = typeof(int);
        textBox.ValidateText().Should().Be(12345);

        textBox.Mask = string.Empty;
        textBox.Text = "canonical";
        textBox.Text.Should().Be("canonical");
        textBox.MaskCompleted.Should().BeTrue();
        textBox.IsHandleCreated.Should().BeFalse();
    }

    [Fact]
    public void DataGridViewCustomCellCreatesCanonicalEditingControl()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var grid = new DataGridView { AllowUserToAddRows = false };
        var column = new CanonicalEditingColumn();
        grid.Columns.Add(column);
        grid.Rows.Add("value");

        DataGridViewCell cell = grid.Rows.SharedRow(0).Cells[0];
        cell.Should().BeOfType<CanonicalEditingCell>();
        cell.OwningColumn.Should().BeSameAs(column);
        column.ValueType.Should().Be(typeof(string));

        grid.CurrentCell = cell;
        grid.BeginEdit(selectAll: false).Should().BeTrue();
        grid.EditingControl.Should().BeOfType<CanonicalEditingControl>();
        ((CanonicalEditingCell)cell).Initialized.Should().BeTrue();
        grid.EndEdit().Should().BeTrue();
    }

    [Fact]
    public void DataGridViewDataTableBindingPreservesCanonicalMetadataAndValues()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        var table = new DataTable();
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Count", typeof(int));
        table.Rows.Add("alpha", 3);
        table.Rows.Add("beta", 7);

        using var grid = new DataGridView
        {
            AllowUserToAddRows = false,
            BindingContext = new BindingContext(),
            DataSource = table,
        };

        grid.ColumnCount.Should().Be(2);
        grid.RowCount.Should().Be(2);
        grid.Columns[0].Name.Should().Be("Name");
        grid.Columns[1].ValueType.Should().Be(typeof(int));
        grid.Rows[0].Cells[0].Value.Should().Be("alpha");
        grid.Rows[1].Cells[1].Value.Should().Be(7);
        grid.Columns.Add("Extra", "Extra header").Should().Be(2);
        grid.Columns["Extra"]!.HeaderText.Should().Be("Extra header");
    }

    [Fact]
    public void DataGridViewCellPaintingReplaysThroughManagedSystemDrawing()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var grid = new DataGridView
        {
            Size = new Size(160, 80),
            AllowUserToAddRows = false,
        };
        grid.Columns.Add("Value", "Value");
        grid.Rows.Add("painted");

        using var bitmap = new Bitmap(160, 80);
        using Graphics graphics = Graphics.FromImage(bitmap);
        var args = new DataGridViewCellPaintingEventArgs(
            grid,
            graphics,
            new Rectangle(0, 0, 160, 80),
            grid.GetCellDisplayRectangle(0, 0, false),
            0,
            0,
            DataGridViewElementStates.Visible,
            "painted",
            "painted",
            errorText: null,
            grid.DefaultCellStyle,
            grid.AdvancedCellBorderStyle,
            DataGridViewPaintParts.All);

        args.Paint(args.CellBounds, DataGridViewPaintParts.All);

        args.Graphics.Should().BeSameAs(graphics);
        args.Value.Should().Be("painted");
    }

    [Fact]
    public void DataGridViewGeometryHitTestingAndCurrentCellUseCanonicalContracts()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using DataGridView grid = CreateCanonicalDataGridView();
        _ = grid.Handle;
        grid.PerformLayout();

        Rectangle topLeft = grid.GetCellDisplayRectangle(-1, -1, cutOverflow: false);
        Rectangle columnHeader = grid.GetCellDisplayRectangle(0, -1, cutOverflow: false);
        Rectangle rowHeader = grid.GetCellDisplayRectangle(-1, 0, cutOverflow: false);
        Rectangle firstCell = grid.GetCellDisplayRectangle(0, 0, cutOverflow: false);
        Rectangle secondCell = grid.GetCellDisplayRectangle(1, 1, cutOverflow: false);

        topLeft.Size.Should().Be(new Size(grid.RowHeadersWidth, grid.ColumnHeadersHeight));
        columnHeader.Left.Should().Be(topLeft.Right);
        columnHeader.Width.Should().Be(grid.Columns[0].Width);
        rowHeader.Top.Should().Be(topLeft.Bottom);
        rowHeader.Height.Should().Be(grid.Rows[0].Height);
        firstCell.Location.Should().Be(new Point(columnHeader.Left, rowHeader.Top));
        secondCell.Size.Should().Be(new Size(grid.Columns[1].Width, grid.Rows[1].Height));

        DataGridView.HitTestInfo topLeftHit = grid.HitTest(rowHeader.Left + 1, columnHeader.Top + 1);
        topLeftHit.Type.Should().Be(DataGridViewHitTestType.TopLeftHeader);
        topLeftHit.ColumnIndex.Should().Be(-1);
        topLeftHit.RowIndex.Should().Be(-1);

        DataGridView.HitTestInfo columnHeaderHit = grid.HitTest(columnHeader.Left + 1, columnHeader.Top + 1);
        columnHeaderHit.Type.Should().Be(DataGridViewHitTestType.ColumnHeader);
        columnHeaderHit.ColumnIndex.Should().Be(0);

        DataGridView.HitTestInfo rowHeaderHit = grid.HitTest(rowHeader.Left + 1, rowHeader.Top + 1);
        rowHeaderHit.Type.Should().Be(DataGridViewHitTestType.RowHeader);
        rowHeaderHit.RowIndex.Should().Be(0);

        DataGridView.HitTestInfo cellHit = grid.HitTest(secondCell.Left + 1, secondCell.Top + 1);
        cellHit.Type.Should().Be(DataGridViewHitTestType.Cell);
        cellHit.ColumnIndex.Should().Be(1);
        cellHit.RowIndex.Should().Be(1);
        cellHit.ColumnX.Should().Be(secondCell.Left);
        cellHit.RowY.Should().Be(secondCell.Top);
        cellHit.Should().Be(grid.HitTest(secondCell.Right - 1, secondCell.Bottom - 1));
        cellHit.ToString().Should().Be("{ Type:Cell, Column:1, Row:1 }");
        grid.HitTest(grid.ClientSize.Width - 1, grid.ClientSize.Height - 1)
            .Should().Be(DataGridView.HitTestInfo.Nowhere);
        ((int)DataGridViewHitTestType.VerticalScrollBar).Should().Be(6);

        grid.Width = secondCell.Right - 5;
        grid.PerformLayout();
        Rectangle clippedSecondCell = grid.GetCellDisplayRectangle(1, 1, cutOverflow: true);
        clippedSecondCell.Left.Should().Be(secondCell.Left);
        clippedSecondCell.Width.Should().BePositive().And.BeLessThan(secondCell.Width);
        clippedSecondCell.Height.Should().Be(secondCell.Height);
        grid.GetCellDisplayRectangle(1, 1, cutOverflow: false).Should().Be(secondCell);
        FluentActions.Invoking(() => grid.GetCellDisplayRectangle(-2, 0, false))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => grid.GetCellDisplayRectangle(0, grid.Rows.Count, false))
            .Should().Throw<ArgumentOutOfRangeException>();

        grid.CurrentCell = null;
        int changed = 0;
        grid.CurrentCellChanged += (_, _) => changed++;
        DataGridViewCell first = grid.Rows[0].Cells[0];
        DataGridViewCell second = grid.Rows[1].Cells[1];
        grid.CurrentCell = first;
        grid.CurrentCell.Should().BeSameAs(first);
        grid.CurrentRow.Should().BeSameAs(grid.Rows[0]);
        changed.Should().Be(1);
        grid.CurrentCell = first;
        changed.Should().Be(1);

        using DataGridView foreignGrid = CreateCanonicalDataGridView();
        grid.CurrentCell = foreignGrid.Rows[0].Cells[0];
        grid.CurrentCell.Should().BeSameAs(first);
        changed.Should().Be(1);
        FluentActions.Invoking(() => grid.CurrentCell = foreignGrid.Rows[1].Cells[1])
            .Should().Throw<ArgumentException>();
        grid.CurrentCell.Should().BeSameAs(first);

        grid.CurrentCell = second;
        changed.Should().Be(2);
        grid.Rows.RemoveAt(1);
        grid.CurrentCell.Should().BeNull();
        grid.CurrentRow.Should().BeNull();
        changed.Should().Be(3);
        grid.CurrentCell = null;
        changed.Should().Be(3);
    }

    [Fact]
    public void DataGridViewTextEditingUsesCanonicalEditingPanelAndCommitLifecycle()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using DataGridView grid = CreateCanonicalDataGridView();
        DataGridViewCell cell = grid.Rows[0].Cells[0];
        grid.CurrentCell = cell;

        cell.ReadOnly = true;
        grid.BeginEdit(selectAll: false).Should().BeFalse();
        cell.ReadOnly = false;
        grid.Rows[0].ReadOnly = true;
        grid.BeginEdit(selectAll: false).Should().BeFalse();
        grid.Rows[0].ReadOnly = false;
        grid.Columns[0].ReadOnly = true;
        grid.BeginEdit(selectAll: false).Should().BeFalse();
        grid.Columns[0].ReadOnly = false;
        grid.ReadOnly = true;
        grid.BeginEdit(selectAll: false).Should().BeFalse();
        grid.ReadOnly = false;

        int showing = 0;
        int changed = 0;
        grid.EditingControlShowing += (_, e) =>
        {
            showing++;
            e.Control.Should().BeSameAs(grid.EditingControl);
        };
        grid.CellValueChanged += (_, e) =>
        {
            changed++;
            e.ColumnIndex.Should().Be(0);
            e.RowIndex.Should().Be(0);
        };

        grid.BeginEdit(selectAll: true).Should().BeTrue();
        grid.IsCurrentCellInEditMode.Should().BeTrue();
        DataGridViewTextBoxEditingControl editor = grid.EditingControl
            .Should().BeOfType<DataGridViewTextBoxEditingControl>().Subject;
        editor.Parent.Should().BeSameAs(grid.EditingPanel);
        grid.EditingPanel.Parent.Should().BeSameAs(grid);
        editor.SelectionStart.Should().Be(0);
        editor.SelectionLength.Should().Be(editor.TextLength);
        showing.Should().Be(1);
        grid.BeginEdit(selectAll: false).Should().BeTrue();
        showing.Should().Be(1);

        editor.Text = "committed";
        grid.EndEdit().Should().BeTrue();
        cell.Value.Should().Be("committed");
        changed.Should().Be(1);
        grid.IsCurrentCellInEditMode.Should().BeFalse();
        grid.EditingControl.Should().BeNull();

        grid.BeginEdit(selectAll: false).Should().BeTrue();
        ((TextBox)grid.EditingControl!).Text = "discarded";
        grid.CancelEdit().Should().BeTrue();
        cell.Value.Should().Be("committed");
        changed.Should().Be(1);

        grid.BeginEdit(selectAll: false).Should().BeTrue();
        ((TextBox)grid.EditingControl!).Text = "committed by read-only";
        grid.Columns[0].ReadOnly = true;
        grid.IsCurrentCellInEditMode.Should().BeFalse();
        cell.Value.Should().Be("committed by read-only");
        changed.Should().Be(2);
        grid.Columns[0].ReadOnly = false;

        grid.BeginEdit(selectAll: false).Should().BeTrue();
        ((TextBox)grid.EditingControl!).Text = "committed by move";
        grid.CurrentCell = grid.Rows[1].Cells[0];
        cell.Value.Should().Be("committed by move");
        changed.Should().Be(3);
    }

    [Fact]
    public void DataGridViewComboBoxEditingPreservesTypedItemsAndSelection()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var grid = new DataGridView
        {
            Size = new Size(240, 100),
            AllowUserToAddRows = false,
        };
        var column = new DataGridViewComboBoxColumn { Width = 100 };
        column.Items.Add("one");
        column.Items.Add("two");
        grid.Columns.Add(column);
        grid.Rows.Add("one");
        grid.DataError += (_, e) => e.ThrowException = true;

        DataGridViewComboBoxCell cell = grid.Rows[0].Cells[0]
            .Should().BeOfType<DataGridViewComboBoxCell>().Subject;
        cell.Items.Count.Should().Be(2);
        cell.Items[0].Should().Be("one");
        cell.Items[1].Should().Be("two");
        grid.CurrentCell = cell;
        int itemsAtShowing = -1;
        grid.EditingControlShowing += (_, e) =>
            itemsAtShowing = ((DataGridViewComboBoxEditingControl)e.Control).Items.Count;
        grid.BeginEdit(selectAll: true).Should().BeTrue();
        itemsAtShowing.Should().Be(2);

        DataGridViewComboBoxEditingControl editor = grid.EditingControl
            .Should().BeOfType<DataGridViewComboBoxEditingControl>().Subject;
        editor.Parent.Should().BeSameAs(grid.EditingPanel);
        editor.Items.Count.Should().Be(2);
        editor.Items[0].Should().Be("one");
        editor.Items[1].Should().Be("two");
        editor.SelectedIndex.Should().Be(0);
        editor.SelectedItem.Should().Be("one");
        editor.SelectedIndex = 1;
        grid.EndEdit().Should().BeTrue();
        cell.Value.Should().Be("two");
    }

    [Fact]
    public void DataGridViewReceivesTypedPortablePointerSelectionWithoutAHostShim()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ClientSize = new Size(360, 180) };
        using DataGridView grid = CreateCanonicalDataGridView();
        grid.Bounds = new Rectangle(12, 15, 300, 120);
        grid.EditMode = DataGridViewEditMode.EditOnEnter;
        form.Controls.Add(grid);
        form.Show();
        grid.CurrentCell = null;

        Cursor.Clip = Rectangle.Empty;
        Cursor.Clip.Should().Be(Rectangle.Empty);
        FluentActions.Invoking(() => Cursor.Clip = new Rectangle(1, 2, 3, 4))
            .Should().Throw<PlatformNotSupportedException>();

        int mouseDown = 0;
        grid.CellMouseDown += (_, e) =>
        {
            mouseDown++;
            e.ColumnIndex.Should().Be(1);
            e.RowIndex.Should().Be(1);
        };
        Rectangle cell = grid.GetCellDisplayRectangle(1, 1, cutOverflow: false);
        var position = new LibrePoint(
            grid.Left + cell.Left + (cell.Width / 2),
            grid.Top + cell.Top + (cell.Height / 2));

        platform.SendInput(LibreInputEventKind.PointerDown, position: position, button: LibrePointerButton.Primary);
        platform.SendInput(LibreInputEventKind.PointerUp, position: position, button: LibrePointerButton.Primary);

        mouseDown.Should().Be(1);
        grid.CurrentCell.Should().BeSameAs(grid.Rows[1].Cells[1]);
        grid.BeginEdit(selectAll: true).Should().BeTrue();
        grid.EditingControl.Should().BeAssignableTo<IDataGridViewEditingControl>();
        grid.EndEdit().Should().BeTrue();
    }

    [Fact]
    public void DataGridViewColumnLookupUsesCanonicalNamesAndTypedIndexes()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var grid = new DataGridView { AllowUserToAddRows = false };
        var nameColumn = new DataGridViewTextBoxColumn { Name = "nameColumn" };
        var valueColumn = new DataGridViewTextBoxColumn { Name = "valueColumn" };
        grid.Columns.AddRange(nameColumn, valueColumn);

        grid.Columns["nameColumn"].Should().BeSameAs(nameColumn);
        grid.Columns["VALUECOLUMN"].Should().BeSameAs(valueColumn);
        grid.Columns["missing"].Should().BeNull();
        grid.Columns.Contains("NAMECOLUMN").Should().BeTrue();
        grid.Columns.IndexOf(valueColumn).Should().Be(1);
        nameColumn.DataGridView.Should().BeSameAs(grid);
        valueColumn.DataGridView.Should().BeSameAs(grid);
    }

    [Fact]
    public void DataGridViewSortPreservesStableRowsAndNewRowPlaceholder()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var grid = new DataGridView { Size = new Size(240, 120) };
        var nameColumn = new DataGridViewTextBoxColumn
        {
            Name = "name",
            SortMode = DataGridViewColumnSortMode.Programmatic,
        };
        grid.Columns.Add(nameColumn);
        int betaIndex = grid.Rows.Add("beta");
        int nullIndex = grid.Rows.Add();
        int alphaIndex = grid.Rows.Add("Alpha");
        int secondAlphaIndex = grid.Rows.Add("Alpha");
        DataGridViewRow beta = grid.Rows[betaIndex];
        DataGridViewRow nullRow = grid.Rows[nullIndex];
        DataGridViewRow alpha = grid.Rows[alphaIndex];
        DataGridViewRow secondAlpha = grid.Rows[secondAlphaIndex];
        DataGridViewRow placeholder = grid.Rows[grid.NewRowIndex];
        int rowsAdded = 0;
        int rowsRemoved = 0;
        int sorted = 0;
        grid.RowsAdded += (_, _) => rowsAdded++;
        grid.RowsRemoved += (_, _) => rowsRemoved++;
        grid.Sorted += (_, _) => sorted++;

        grid.Sort(nameColumn, ListSortDirection.Ascending);

        grid.Rows[0].Should().BeSameAs(nullRow);
        grid.Rows[1].Should().BeSameAs(alpha);
        grid.Rows[2].Should().BeSameAs(secondAlpha);
        grid.Rows[3].Should().BeSameAs(beta);
        grid.NewRowIndex.Should().Be(4);
        grid.Rows[4].Should().BeSameAs(placeholder);
        placeholder.IsNewRow.Should().BeTrue();
        grid.Rows.Cast<DataGridViewRow>().Select((row, index) => row.Index == index).Should().OnlyContain(value => value);
        grid.SortedColumn.Should().BeSameAs(nameColumn);
        grid.SortOrder.Should().Be(SortOrder.Ascending);

        grid.Sort(nameColumn, ListSortDirection.Descending);

        grid.Rows[0].Should().BeSameAs(beta);
        grid.Rows[1].Should().BeSameAs(alpha);
        grid.Rows[2].Should().BeSameAs(secondAlpha);
        grid.Rows[3].Should().BeSameAs(nullRow);
        grid.NewRowIndex.Should().Be(4);
        grid.Rows[4].Should().BeSameAs(placeholder);
        grid.SortedColumn.Should().BeSameAs(nameColumn);
        grid.SortOrder.Should().Be(SortOrder.Descending);
        rowsAdded.Should().Be(0);
        rowsRemoved.Should().Be(0);
        sorted.Should().Be(2);
    }

    [Fact]
    public void DataGridViewSortRejectsForeignColumnsAndInvalidDirections()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var grid = new DataGridView { AllowUserToAddRows = false };
        var ownColumn = new DataGridViewTextBoxColumn();
        grid.Columns.Add(ownColumn);
        grid.Rows.Add("value");

        FluentActions.Invoking(() => grid.Sort(new DataGridViewTextBoxColumn(), ListSortDirection.Ascending))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => grid.Sort(ownColumn, (ListSortDirection)42))
            .Should().Throw<InvalidEnumArgumentException>();
    }

    [Fact]
    public void DataGridViewNewRowPlaceholderTracksColumnsAndAllowUserToAddRows()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var grid = new DataGridView();
        int changed = 0;
        grid.AllowUserToAddRowsChanged += (_, _) =>
        {
            changed++;
            AssertCanonicalNewRowInvariant(grid);
        };

        grid.AllowUserToAddRows.Should().BeTrue();
        AssertCanonicalNewRowInvariant(grid);
        grid.Columns.Add(new DataGridViewTextBoxColumn());
        grid.NewRowIndex.Should().Be(0);
        grid.Rows[0].Cells[0].Should().BeOfType<DataGridViewTextBoxCell>();
        AssertCanonicalNewRowInvariant(grid);

        grid.AllowUserToAddRows = true;
        changed.Should().Be(0);
        DataGridViewRow oldPlaceholder = grid.Rows[grid.NewRowIndex];
        grid.AllowUserToAddRows = false;
        changed.Should().Be(1);
        grid.Rows.Count.Should().Be(0);
        grid.NewRowIndex.Should().Be(-1);
        oldPlaceholder.DataGridView.Should().BeNull();
        oldPlaceholder.Index.Should().Be(-1);
        oldPlaceholder.IsNewRow.Should().BeFalse();

        grid.AllowUserToAddRows = false;
        changed.Should().Be(1);
        grid.AllowUserToAddRows = true;
        changed.Should().Be(2);
        grid.Rows[grid.NewRowIndex].Should().NotBeSameAs(oldPlaceholder);
        AssertCanonicalNewRowInvariant(grid);
    }

    [Fact]
    public void DataGridViewRowAddPathsInsertBeforeTheCanonicalPlaceholder()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var grid = new DataGridView();
        grid.Columns.Add(new DataGridViewTextBoxColumn());
        grid.Columns.Add(new DataGridViewComboBoxColumn());

        int valuesIndex = grid.Rows.Add("alpha", "one");
        int emptyIndex = grid.Rows.Add();
        var explicitRow = new DataGridViewRow();
        int explicitIndex = grid.Rows.Add(explicitRow);
        var insertedAtEnd = new DataGridViewRow();
        grid.Rows.Insert(grid.NewRowIndex, insertedAtEnd);

        valuesIndex.Should().Be(0);
        emptyIndex.Should().Be(1);
        explicitIndex.Should().Be(2);
        grid.NewRowIndex.Should().Be(4);
        grid.Rows.Count.Should().Be(5);
        grid.Rows[2].Should().BeSameAs(explicitRow);
        grid.Rows[3].Should().BeSameAs(insertedAtEnd);
        explicitRow.Index.Should().Be(2);
        insertedAtEnd.Index.Should().Be(3);
        grid.Rows[0].Cells[0].Value.Should().Be("alpha");
        grid.Rows[0].Cells[1].Value.Should().Be("one");
        grid.Rows[0].Cells[1].Should().BeOfType<DataGridViewComboBoxCell>();
        AssertCanonicalNewRowInvariant(grid);
    }

    [Fact]
    public void DataGridViewRowsClearDetachesRowsAndRecreatesThePlaceholder()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using DataGridView grid = CreateCanonicalNewRowGrid();
        var first = new DataGridViewRow();
        var second = new DataGridViewRow();
        grid.Rows.Add(first);
        grid.Rows.Add(second);
        DataGridViewRow oldPlaceholder = grid.Rows[grid.NewRowIndex];

        grid.Rows.Clear();

        grid.Rows.Count.Should().Be(1);
        grid.NewRowIndex.Should().Be(0);
        grid.Rows[0].Should().NotBeSameAs(oldPlaceholder);
        first.DataGridView.Should().BeNull();
        first.Index.Should().Be(-1);
        second.DataGridView.Should().BeNull();
        second.Index.Should().Be(-1);
        oldPlaceholder.DataGridView.Should().BeNull();
        oldPlaceholder.Index.Should().Be(-1);
        oldPlaceholder.IsNewRow.Should().BeFalse();
        AssertCanonicalNewRowInvariant(grid);
    }

    [Fact]
    public void DataGridViewColumnMutationsSynchronizeRealAndPlaceholderRows()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var grid = new DataGridView();
        var firstColumn = new DataGridViewTextBoxColumn { Name = "first" };
        var lastColumn = new DataGridViewComboBoxColumn { Name = "last" };
        grid.Columns.Add(firstColumn);
        grid.Columns.Add(lastColumn);
        int rowIndex = grid.Rows.Add("left", "right");
        DataGridViewRow row = grid.Rows[rowIndex];
        DataGridViewCell firstCell = row.Cells[0];
        DataGridViewCell lastCell = row.Cells[1];

        grid.Columns.Insert(1, new DataGridViewTextBoxColumn { Name = "middle" });
        row.Cells.Count.Should().Be(3);
        row.Cells[0].Should().BeSameAs(firstCell);
        row.Cells[1].Should().BeOfType<DataGridViewTextBoxCell>();
        row.Cells[1].Value.Should().BeNull();
        row.Cells[2].Should().BeSameAs(lastCell);
        row.Cells[0].Value.Should().Be("left");
        row.Cells[2].Value.Should().Be("right");
        AssertCanonicalNewRowInvariant(grid);

        grid.Columns.RemoveAt(0);
        row.Cells.Count.Should().Be(2);
        row.Cells[1].Should().BeSameAs(lastCell);
        row.Cells[1].Value.Should().Be("right");
        firstColumn.DataGridView.Should().BeNull();
        firstColumn.Index.Should().Be(0);
        grid.Columns[0].Index.Should().Be(0);
        grid.Columns[1].Index.Should().Be(1);
        AssertCanonicalNewRowInvariant(grid);

        DataGridViewRow oldPlaceholder = grid.Rows[grid.NewRowIndex];
        grid.Columns.Clear();
        grid.Columns.Count.Should().Be(0);
        grid.NewRowIndex.Should().Be(-1);
        grid.Rows.Count.Should().Be(0);
        row.DataGridView.Should().BeNull();
        row.Index.Should().Be(-1);
        row.Cells.Count.Should().Be(2);
        row.Cells[0].Should().BeOfType<DataGridViewTextBoxCell>();
        row.Cells[0].Value.Should().BeNull();
        row.Cells[1].Should().BeSameAs(lastCell);
        row.Cells[1].Value.Should().Be("right");
        oldPlaceholder.DataGridView.Should().BeNull();
        oldPlaceholder.IsNewRow.Should().BeFalse();
        AssertCanonicalNewRowInvariant(grid);

        grid.Columns.Add(new DataGridViewComboBoxColumn());
        row.Cells.Count.Should().Be(2);
        grid.Rows.Count.Should().Be(1);
        grid.NewRowIndex.Should().Be(0);
        grid.Rows[0].Cells[0].Should().BeOfType<DataGridViewComboBoxCell>();
        AssertCanonicalNewRowInvariant(grid);
    }

    [Fact]
    public void DataGridViewNewRowPlaceholderCannotBeRemovedThroughPublicRowsApi()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using DataGridView grid = CreateCanonicalNewRowGrid();
        grid.Rows.Add("real");
        DataGridViewRow placeholder = grid.Rows[grid.NewRowIndex];

        FluentActions.Invoking(() => grid.Rows.RemoveAt(grid.NewRowIndex))
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => grid.Rows.Remove(placeholder))
            .Should().Throw<InvalidOperationException>();
        grid.Rows.Count.Should().Be(2);
        grid.NewRowIndex.Should().Be(1);
        grid.Rows[1].Should().BeSameAs(placeholder);
        AssertCanonicalNewRowInvariant(grid);
    }

    [Fact]
    public void ScrollableControlPublishesCanonicalAutoScrollMetricsAndOffsets()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var panel = new Panel
        {
            AutoScroll = true,
            Size = new Size(100, 80),
        };
        using var canvas = new PictureBox { Bounds = new Rectangle(80, 70, 120, 90) };
        panel.Controls.Add(canvas);
        _ = panel.Handle;
        _ = canvas.Handle;
        panel.CreateControl();
        panel.PerformLayout();

        panel.HorizontalScroll.Visible.Should().BeTrue();
        panel.VerticalScroll.Visible.Should().BeTrue();
        panel.HorizontalScroll.Maximum.Should().Be(199);
        panel.HorizontalScroll.LargeChange.Should().Be(100);
        panel.VerticalScroll.Maximum.Should().Be(159);
        panel.VerticalScroll.LargeChange.Should().Be(80);

        int scrollEvents = 0;
        panel.Scroll += (_, _) => scrollEvents++;
        panel.HorizontalScroll.Value = 100;
        panel.VerticalScroll.Value = 80;
        panel.AutoScrollPosition.Should().Be(new Point(-100, -80));
        panel.DisplayRectangle.Should().Be(new Rectangle(-100, -80, 200, 160));
        scrollEvents.Should().Be(0);
        FluentActions.Invoking(() => panel.HorizontalScroll.Value = 500)
            .Should().Throw<ArgumentOutOfRangeException>();

        panel.AutoScroll = false;
        panel.PerformLayout();
        panel.HorizontalScroll.Value.Should().Be(0);
        panel.VerticalScroll.Value.Should().Be(0);
        panel.HorizontalScroll.Visible.Should().BeFalse();
        panel.VerticalScroll.Visible.Should().BeFalse();
    }

    [Fact]
    public void ScrollableControlDisplayRectangleDeflatesPaddingAfterScrolling()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var panel = new Panel
        {
            AutoScroll = true,
            Padding = new Padding(5, 6, 7, 8),
            Size = new Size(100, 80),
        };
        using var child = new Control { Bounds = new Rectangle(80, 70, 120, 90) };
        panel.Controls.Add(child);
        _ = panel.Handle;
        _ = child.Handle;
        panel.CreateControl();
        panel.PerformLayout();

        panel.AutoScrollPosition = new Point(30, 25);

        panel.DisplayRectangle.Should().Be(new Rectangle(-25, -19, 188, 146));
    }

    [Fact]
    public void ScrollableControlCoordinateConversionIncludesManagedDisplayOffset()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var root = new Panel
        {
            AutoScroll = true,
            Location = new Point(10, 20),
            Size = new Size(100, 80),
        };
        using var child = new Control { Bounds = new Rectangle(80, 70, 120, 90) };
        root.Controls.Add(child);
        _ = root.Handle;
        _ = child.Handle;
        root.CreateControl();
        root.PerformLayout();
        root.HorizontalScroll.Value = 30;
        root.VerticalScroll.Value = 25;

        Point screen = child.PointToScreen(new Point(4, 5));
        screen.Should().Be(new Point(64, 70));
        child.PointToClient(screen).Should().Be(new Point(4, 5));
        child.Location.Should().Be(new Point(50, 45));
    }

    [Fact]
    public void ControlScalePreservesCanonicalBoundsSelectionAndDescendants()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var root = new CanonicalScaleProbeControl { Bounds = new Rectangle(3, 5, 100, 80) };
        using var child = new Control { Bounds = new Rectangle(7, 9, 20, 14) };
        root.Controls.Add(child);

        root.Scale(new SizeF(1.5f, 2f));
        root.Bounds.Should().Be(new Rectangle(4, 10, 150, 160));
        child.Bounds.Should().Be(new Rectangle(10, 18, 30, 28));

        root.ScaleSelected(new SizeF(2f, 0.5f), BoundsSpecified.Size);
        root.Bounds.Should().Be(new Rectangle(4, 10, 300, 80));
    }

    [Fact]
    public void ScrollableControlKeepsEmbeddedEditorAlignedWithScrollAndScale()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var panel = new Panel
        {
            AutoScroll = true,
            Size = new Size(240, 160),
        };
        using var pictureBox = new PictureBox { Size = new Size(640, 480) };
        panel.Controls.Add(pictureBox);
        _ = panel.Handle;
        _ = pictureBox.Handle;
        panel.CreateControl();
        panel.PerformLayout();
        panel.AutoScrollPosition = new Point(75, 45);

        using var editor = new TextBox { Bounds = new Rectangle(90, 70, 120, 24) };
        editor.Scale(new SizeF(1.5f, 1.5f));
        editor.Top -= panel.VerticalScroll.Value;
        editor.Left -= panel.HorizontalScroll.Value;
        panel.Controls.Add(editor);
        panel.Controls.SetChildIndex(editor, 0);

        editor.Bounds.Should().Be(new Rectangle(60, 60, 180, editor.PreferredHeight));
        panel.Controls[0].Should().BeSameAs(editor);
    }

    [Fact]
    public void TreeViewUsesCanonicalImageMetricsBoundsAndHitTesting()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var bitmap = new Bitmap(20, 20);
        using var imageList = new ImageList { ImageSize = new Size(20, 20) };
        imageList.Images.Add(bitmap);
        using var treeView = new CanonicalTreeView
        {
            ImageIndex = 0,
            ImageList = imageList,
            Size = new Size(180, 80),
        };
        TreeNode root = treeView.Nodes.Add("Root");
        TreeNode child = root.Nodes.Add("Child");
        root.Expand();
        _ = treeView.Handle;
        treeView.CreateControl();

        root.Bounds.Should().NotBe(Rectangle.Empty);
        root.Bounds.Height.Should().BeGreaterThanOrEqualTo(imageList.ImageSize.Height);
        child.Bounds.Left.Should().BeGreaterThan(root.Bounds.Left);
        int rootCenterY = root.Bounds.Top + (root.Bounds.Height / 2);

        TreeViewHitTestInfo labelHit = treeView.HitTest(root.Bounds.Left + 1, rootCenterY);
        labelHit.Node.Should().BeSameAs(root);
        labelHit.Location.Should().HaveFlag(TreeViewHitTestLocations.Label);

        int imageCenterX = root.Bounds.Left - 3 - (imageList.ImageSize.Width / 2);
        TreeViewHitTestInfo imageHit = treeView.HitTest(imageCenterX, rootCenterY);
        imageHit.Node.Should().BeSameAs(root);
        imageHit.Location.Should().HaveFlag(TreeViewHitTestLocations.Image);

        treeView.GetNodeAt(treeView.ClientSize.Width - 2, rootCenterY).Should().BeSameAs(root);
    }

    [Fact]
    public void TreeViewExpansionUsesCanonicalCancellationStateAndActions()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var treeView = new CanonicalTreeView { Size = new Size(180, 80) };
        TreeNode root = treeView.Nodes.Add("Root");
        root.Nodes.Add("Child");
        _ = treeView.Handle;
        treeView.CreateControl();

        bool cancelExpansion = true;
        int afterExpand = 0;
        int afterCollapse = 0;
        TreeViewAction beforeExpandAction = TreeViewAction.Unknown;
        TreeViewAction afterExpandAction = TreeViewAction.Unknown;
        TreeViewAction beforeCollapseAction = TreeViewAction.Unknown;
        TreeViewAction afterCollapseAction = TreeViewAction.Unknown;
        treeView.BeforeExpand += (_, e) =>
        {
            beforeExpandAction = e.Action;
            e.Cancel = cancelExpansion;
        };
        treeView.AfterExpand += (_, e) =>
        {
            afterExpand++;
            afterExpandAction = e.Action;
        };
        treeView.BeforeCollapse += (_, e) => beforeCollapseAction = e.Action;
        treeView.AfterCollapse += (_, e) =>
        {
            afterCollapse++;
            afterCollapseAction = e.Action;
        };

        root.Expand();
        root.IsExpanded.Should().BeFalse();
        afterExpand.Should().Be(0);
        beforeExpandAction.Should().Be(TreeViewAction.Expand);

        cancelExpansion = false;
        root.Expand();
        root.IsExpanded.Should().BeTrue();
        afterExpand.Should().Be(1);
        afterExpandAction.Should().Be(TreeViewAction.Expand);

        root.Collapse();
        root.IsExpanded.Should().BeFalse();
        afterCollapse.Should().Be(1);
        beforeCollapseAction.Should().Be(TreeViewAction.Collapse);
        afterCollapseAction.Should().Be(TreeViewAction.Collapse);
    }

    [Fact]
    public void TreeViewSelectionExpandsAncestorsAndScrollsCanonicalBoundsIntoView()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var treeView = new CanonicalTreeView { Size = new Size(180, 45) };
        TreeNode root = treeView.Nodes.Add("Root");
        TreeNode branch = root.Nodes.Add("Branch");
        TreeNode deepNode = branch.Nodes.Add("Deep node");
        for (int index = 0; index < 7; index++)
        {
            treeView.Nodes.Add($"Root {index}");
        }

        _ = treeView.Handle;
        treeView.CreateControl();
        treeView.SelectedNode = deepNode;

        root.IsExpanded.Should().BeTrue();
        branch.IsExpanded.Should().BeTrue();
        treeView.SelectedNode.Should().BeSameAs(deepNode);
        deepNode.IsSelected.Should().BeTrue();
        deepNode.IsVisible.Should().BeTrue();
        deepNode.Bounds.Top.Should().BeGreaterThanOrEqualTo(1);
        deepNode.Bounds.Bottom.Should().BeLessThanOrEqualTo(treeView.ClientSize.Height - 1);
        treeView.TopNode.Should().NotBeNull();

        TreeNode finalRoot = treeView.Nodes[^1];
        finalRoot.EnsureVisible();
        finalRoot.IsVisible.Should().BeTrue();
        finalRoot.Bounds.Bottom.Should().BeLessThanOrEqualTo(treeView.ClientSize.Height - 1);
    }

    [Fact]
    public void TreeViewKeyboardNavigationUsesCanonicalVisibleOrderAndAction()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var treeView = new CanonicalTreeView { Size = new Size(180, 90) };
        TreeNode root = treeView.Nodes.Add("Root");
        TreeNode child = root.Nodes.Add("Child");
        TreeNode sibling = root.Nodes.Add("Sibling");
        TreeNode secondRoot = treeView.Nodes.Add("Second root");
        TreeViewAction lastAction = TreeViewAction.Unknown;
        treeView.AfterSelect += (_, e) => lastAction = e.Action;
        _ = treeView.Handle;
        treeView.CreateControl();
        treeView.SelectedNode = root;

        treeView.RaiseKey(Keys.Right).Handled.Should().BeTrue();
        root.IsExpanded.Should().BeTrue();
        treeView.RaiseKey(Keys.Right).Handled.Should().BeTrue();
        treeView.SelectedNode.Should().BeSameAs(child);
        lastAction.Should().Be(TreeViewAction.ByKeyboard);

        treeView.RaiseKey(Keys.Down).Handled.Should().BeTrue();
        treeView.SelectedNode.Should().BeSameAs(sibling);
        treeView.RaiseKey(Keys.End).Handled.Should().BeTrue();
        treeView.SelectedNode.Should().BeSameAs(secondRoot);
        treeView.RaiseKey(Keys.Home).Handled.Should().BeTrue();
        treeView.SelectedNode.Should().BeSameAs(root);
    }

    [Fact]
    public void TreeViewCheckEventsPreserveCanonicalCancellationAndProgrammaticAction()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var treeView = new CanonicalTreeView
        {
            CheckBoxes = true,
            Size = new Size(180, 80),
        };
        TreeNode node = treeView.Nodes.Add("Node");
        _ = treeView.Handle;
        treeView.CreateControl();

        bool cancel = true;
        int beforeCheck = 0;
        int afterCheck = 0;
        TreeViewAction afterAction = TreeViewAction.ByMouse;
        treeView.BeforeCheck += (_, e) =>
        {
            beforeCheck++;
            e.Cancel = cancel;
        };
        treeView.AfterCheck += (_, e) =>
        {
            afterCheck++;
            afterAction = e.Action;
        };

        node.Checked = true;
        node.Checked.Should().BeFalse();
        beforeCheck.Should().Be(1);
        afterCheck.Should().Be(0);

        cancel = false;
        node.Checked = true;
        node.Checked.Should().BeTrue();
        beforeCheck.Should().Be(2);
        afterCheck.Should().Be(1);
        afterAction.Should().Be(TreeViewAction.Unknown);
    }

    [Fact]
    public void ProfessionalColorsUseManagedLayoutGraphicsWithoutScreenHdc()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        var colors = new ProfessionalColorTable();

        colors.ButtonPressedHighlight.Should().NotBe(Color.Empty);
        colors.ButtonCheckedHighlight.Should().NotBe(Color.Empty);
    }

    [Fact]
    public void VisualStyleInformationUsesTypedPortableMetadata()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        Application.EnableVisualStyles();

        VisualStyleInformation.IsEnabledByUser.Should().BeTrue();
        VisualStyleInformation.ColorScheme.Should().Be("ManagedColor");
        VisualStyleInformation.Size.Should().Be("ManagedSize");
        VisualStyleInformation.DisplayName.Should().Be("Managed theme");
        VisualStyleInformation.Company.Should().Be("Managed company");
        VisualStyleInformation.Author.Should().Be("Managed author");
        VisualStyleInformation.Copyright.Should().Be("Managed copyright");
        VisualStyleInformation.Url.Should().Be("https://managed.test");
        VisualStyleInformation.Version.Should().Be("Managed version");
        VisualStyleInformation.Description.Should().Be("Managed description");
        VisualStyleInformation.SupportsFlatMenus.Should().BeTrue();
        VisualStyleInformation.MinimumColorDepth.Should().Be(30);
    }

    [Fact]
    public void ToolStripUsesCategorizedPortableSystemSettingsNotifications()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using var toolStrip = new SettingsAwareToolStrip();
        toolStrip.Visible = false;
        toolStrip.Visible = true;
        int initialFontChanges = toolStrip.FontChangeCount;

        platform.RaiseSettingsChanged(LibreSystemSettingsChangeKind.Color);
        toolStrip.FontChangeCount.Should().Be(initialFontChanges);

        platform.RaiseSettingsChanged(LibreSystemSettingsChangeKind.Window);
        toolStrip.FontChangeCount.Should().Be(initialFontChanges + 1);

        toolStrip.Visible = false;
        platform.RaiseSettingsChanged(LibreSystemSettingsChangeKind.Window);
        toolStrip.FontChangeCount.Should().Be(initialFontChanges + 1);
    }

    [Fact]
    public void SystemInformationUsesTypedPortableInputSettings()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.MouseWheelScrollLines.Should().Be(7);
        SystemInformation.MenuAccessKeysUnderlined.Should().BeTrue();
        SystemInformation.KeyboardDelay.Should().Be(2);
        SystemInformation.IsKeyboardPreferred.Should().BeTrue();
        SystemInformation.KeyboardSpeed.Should().Be(23);
        SystemInformation.MouseHoverSize.Should().Be(new Size(13, 15));
        SystemInformation.MouseHoverTime.Should().Be(640);
        SystemInformation.MouseSpeed.Should().Be(14);
        SystemInformation.IsSnapToDefaultEnabled.Should().BeTrue();
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationUsesTypedPortableUiEffectSettings()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.DragFullWindows.Should().BeFalse();
        SystemInformation.IsDropShadowEnabled.Should().BeFalse();
        SystemInformation.IsFlatMenuEnabled.Should().BeTrue();
        SystemInformation.PopupMenuAlignment.Should().Be(LeftRightAlignment.Right);
        SystemInformation.IsMenuFadeEnabled.Should().BeFalse();
        SystemInformation.MenuShowDelay.Should().Be(275);
        SystemInformation.IsComboBoxAnimationEnabled.Should().BeTrue();
        SystemInformation.IsTitleBarGradientEnabled.Should().BeFalse();
        SystemInformation.IsHotTrackingEnabled.Should().BeTrue();
        SystemInformation.IsListBoxSmoothScrollingEnabled.Should().BeFalse();
        SystemInformation.IsMenuAnimationEnabled.Should().BeTrue();
        SystemInformation.IsSelectionFadeEnabled.Should().BeFalse();
        SystemInformation.IsToolTipAnimationEnabled.Should().BeTrue();
        SystemInformation.UIEffectsEnabled.Should().BeFalse();
        SystemInformation.IsMinimizeRestoreAnimationEnabled.Should().BeTrue();
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationUsesTypedPortableRenderingAndIconSettings()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.IsFontSmoothingEnabled.Should().BeFalse();
        SystemInformation.FontSmoothingContrast.Should().Be(1700);
        SystemInformation.FontSmoothingType.Should().Be(1);
        SystemInformation.IconHorizontalSpacing.Should().Be(81);
        SystemInformation.IconVerticalSpacing.Should().Be(83);
        SystemInformation.IconSpacingSize.Should().Be(new Size(81, 83));
        SystemInformation.IsIconTitleWrappingEnabled.Should().BeFalse();
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationUsesTypedPortableWindowTrackingAndCaretSettings()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.IsActiveWindowTrackingEnabled.Should().BeTrue();
        SystemInformation.ActiveWindowTrackingDelay.Should().Be(525);
        SystemInformation.BorderMultiplierFactor.Should().Be(3);
        SystemInformation.CaretWidth.Should().Be(5);
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationAndPrintPreviewUseTypedPortableFocusAndResizeMetrics()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.VerticalFocusThickness.Should().Be(6);
        SystemInformation.HorizontalFocusThickness.Should().Be(7);
        SystemInformation.VerticalResizeBorderThickness.Should().Be(8);
        SystemInformation.HorizontalResizeBorderThickness.Should().Be(9);

        using var preview = new PrintPreviewControl();
        preview.Controls[0].Should().BeAssignableTo<HScrollBar>().Which.Left.Should().Be(7);
        preview.Controls[1].Should().BeAssignableTo<VScrollBar>().Which.Top.Should().Be(6);
        preview.IsHandleCreated.Should().BeFalse();
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationUsesTypedPortablePointerAndTimingSettings()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.MousePresent.Should().BeTrue();
        SystemInformation.MouseButtonsSwapped.Should().BeTrue();
        SystemInformation.MouseButtons.Should().Be(5);
        SystemInformation.DoubleClickSize.Should().Be(new Size(12, 14));
        SystemInformation.DoubleClickTime.Should().Be(650);
        SystemInformation.NativeMouseWheelSupport.Should().BeFalse();
        SystemInformation.MouseWheelPresent.Should().BeFalse();
        SystemInformation.CaretBlinkTime.Should().Be(725);
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationAndComponentEditorUseTypedPortableNonClientMetrics()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.CaptionHeight.Should().Be(29);
        SystemInformation.MenuHeight.Should().Be(31);
        SystemInformation.MinWindowTrackSize.Should().Be(new Size(140, 52));

        using var component = new System.ComponentModel.Component();
        using var editor = new System.Windows.Forms.Design.ComponentEditorForm(component, []);
        Size initialSize = editor.Size;

        platform.CaptionHeightValue = 39;
        using var tallerEditor = new System.Windows.Forms.Design.ComponentEditorForm(component, []);
        SystemInformation.CaptionHeight.Should().Be(39);
        tallerEditor.Width.Should().Be(initialSize.Width);
        tallerEditor.Height.Should().BeGreaterThan(initialSize.Height);
        editor.IsHandleCreated.Should().BeFalse();
        tallerEditor.IsHandleCreated.Should().BeFalse();
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationUsesTypedPortableCursorAndIconMetrics()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.IconSize.Should().Be(new Size(33, 35));
        SystemInformation.CursorSize.Should().Be(new Size(37, 39));
        SystemInformation.SmallIconSize.Should().Be(new Size(17, 19));
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationUsesTypedPortableWindowGeometryMetrics()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.MinimumWindowSize.Should().Be(new Size(101, 102));
        SystemInformation.CaptionButtonSize.Should().Be(new Size(33, 34));
        SystemInformation.FrameBorderSize.Should().Be(new Size(7, 8));
        SystemInformation.MaxWindowTrackSize.Should().Be(new Size(1600, 1200));
        SystemInformation.PrimaryMonitorMaximizedWindowSize.Should().Be(new Size(1500, 1100));
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationUsesTypedPortableWindowChromeAndMinimizedMetrics()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.MinimizedWindowSpacingSize.Should().Be(new Size(201, 202));
        SystemInformation.ToolWindowCaptionHeight.Should().Be(43);
        SystemInformation.ToolWindowCaptionButtonSize.Should().Be(new Size(45, 46));
        SystemInformation.MenuButtonSize.Should().Be(new Size(47, 48));
        SystemInformation.MinimizedWindowSize.Should().Be(new Size(203, 204));
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationUsesTypedPortableCapabilityMetrics()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.KanjiWindowHeight.Should().Be(41);
        SystemInformation.DebugOS.Should().BeTrue();
        SystemInformation.RightAlignedMenus.Should().BeTrue();
        SystemInformation.PenWindows.Should().BeTrue();
        SystemInformation.DbcsEnabled.Should().BeTrue();
        SystemInformation.Secure.Should().BeTrue();
        SystemInformation.Network.Should().BeFalse();
        SystemInformation.TerminalServerSession.Should().BeTrue();
        SystemInformation.BootMode.Should().Be(BootMode.FailSafeWithNetwork);
        SystemInformation.ShowSounds.Should().BeTrue();
        SystemInformation.MenuCheckSize.Should().Be(new Size(27, 29));
        SystemInformation.MidEastEnabled.Should().BeTrue();
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationUsesTypedPortableMinimizedWindowArrangement()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.ArrangeStartingPosition.Should().Be(
            ArrangeStartingPosition.TopRight | ArrangeStartingPosition.Hide);
        SystemInformation.ArrangeDirection.Should().Be(ArrangeDirection.Up);
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationUsesTypedPortableLateDisplayMetrics()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.GetBorderSizeForDpi(192).Should().Be(new Size(22, 26));
        SystemInformation.ScreenOrientation.Should().Be(ScreenOrientation.Angle270);
        SystemInformation.SizingBorderWidth.Should().Be(7);
        SystemInformation.SmallCaptionButtonSize.Should().Be(new Size(31, 33));
        SystemInformation.MenuBarButtonSize.Should().Be(new Size(35, 37));
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void PowerStatusUsesTypedPortableService()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        PowerStatus power = SystemInformation.PowerStatus;

        power.PowerLineStatus.Should().Be(PowerLineStatus.Online);
        power.BatteryChargeStatus.Should().Be(BatteryChargeStatus.Low | BatteryChargeStatus.Charging);
        power.BatteryFullLifetime.Should().Be(7200);
        power.BatteryLifePercent.Should().Be(0.42f);
        power.BatteryLifeRemaining.Should().Be(1800);
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationUsesTypedPortableMenuFonts()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        using Font menuFont = SystemInformation.MenuFont;
        using Font dpiMenuFont = SystemInformation.GetMenuFontForDpi(192);
        menuFont.Size.Should().Be(11f);
        dpiMenuFont.Size.Should().Be(17f);
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void TimerUsesTypedPortableTimerService()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        int ticks = 0;
        using var timer = new System.Windows.Forms.Timer { Interval = 25 };
        timer.Tick += (_, _) => ticks++;

        timer.Start();
        timer.Enabled.Should().BeTrue();
        platform.TimerStartCount.Should().Be(1);
        platform.LastTimerInterval.Should().Be(TimeSpan.FromMilliseconds(25));
        platform.LastTimerRepeating.Should().BeTrue();
        platform.FireTimer();
        ticks.Should().Be(1);

        timer.Interval = 40;
        platform.TimerStartCount.Should().Be(2);
        platform.TimerStopCount.Should().Be(1);
        platform.LastTimerInterval.Should().Be(TimeSpan.FromMilliseconds(40));
        platform.FireTimer();
        ticks.Should().Be(2);

        timer.Stop();
        timer.Enabled.Should().BeFalse();
        platform.TimerStopCount.Should().Be(2);
        platform.HasActiveTimer.Should().BeFalse();
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void GroupBoxAndDisabledLinkLabelPaintWithoutNativeDeviceContexts()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using var target = new Bitmap(180, 60);
        using Graphics graphics = Graphics.FromImage(target);
        using var groupBox = new PaintingGroupBox
        {
            Text = "group",
            Size = new Size(160, 9),
            UseCompatibleTextRendering = false,
        };
        using var linkLabel = new PaintingLinkLabel
        {
            Text = "link",
            Enabled = false,
            Size = new Size(160, 30),
            UseCompatibleTextRendering = false,
        };

        groupBox.PaintTo(graphics);
        linkLabel.PaintTo(graphics);

        platform.TextMeasureCount.Should().BeGreaterThan(0);
        platform.TextDrawCount.Should().BeGreaterThan(1);
        groupBox.IsHandleCreated.Should().BeFalse();
        linkLabel.IsHandleCreated.Should().BeFalse();
    }

    [Fact]
    public void ScrollBarDefaultSizesUseTypedPortableSystemMetrics()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var vertical = new VScrollBar();
        using var horizontal = new HScrollBar();

        SystemInformation.VerticalScrollBarWidth.Should().Be(17);
        SystemInformation.HorizontalScrollBarHeight.Should().Be(17);
        SystemInformation.VerticalScrollBarArrowHeight.Should().Be(17);
        SystemInformation.HorizontalScrollBarArrowWidth.Should().Be(17);
        SystemInformation.VerticalScrollBarThumbHeight.Should().Be(17);
        SystemInformation.HorizontalScrollBarThumbWidth.Should().Be(17);
        SystemInformation.GetVerticalScrollBarWidthForDpi(192).Should().Be(34);
        SystemInformation.GetHorizontalScrollBarHeightForDpi(192).Should().Be(34);
        SystemInformation.VerticalScrollBarArrowHeightForDpi(192).Should().Be(34);
        SystemInformation.GetHorizontalScrollBarArrowWidthForDpi(192).Should().Be(34);
        vertical.Size.Should().Be(new Size(17, 80));
        horizontal.Size.Should().Be(new Size(80, 17));
        vertical.IsHandleCreated.Should().BeFalse();
        horizontal.IsHandleCreated.Should().BeFalse();
    }

    [Fact]
    public void CanonicalManagedRenderersUsePortableVisualStylesWithoutComCtl32()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        Application.EnableVisualStyles();

        Application.RenderWithVisualStyles.Should().BeTrue();
        using var target = new Bitmap(96, 32, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(target);
        graphics.Clear(Color.Transparent);
        ButtonRenderer.DrawButton(graphics, new Rectangle(0, 0, 20, 20), PushButtonState.Normal);
        CheckBoxRenderer.DrawCheckBox(graphics, new Point(22, 3), CheckBoxState.CheckedNormal);
        RadioButtonRenderer.DrawRadioButton(graphics, new Point(40, 3), RadioButtonState.CheckedNormal);
        ComboBoxRenderer.DrawDropDownButton(graphics, new Rectangle(58, 0, 20, 20), ComboBoxState.Normal);
        TrackBarRenderer.DrawHorizontalTrack(graphics, new Rectangle(80, 0, 4, 20));

        platform.VisualStyleDrawCount.Should().BeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public void CursorFiles_DecodeManagedPngAndDibPayloadsAndFailClosed()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        string path = Path.Combine(Path.GetTempPath(), $"librewinforms-cursor-{Guid.NewGuid():N}.cur");

        try
        {
            byte[] png;
            using (Bitmap source = new(2, 2))
            {
                source.SetPixel(0, 0, Color.FromArgb(255, 220, 10, 20));
                source.SetPixel(1, 0, Color.FromArgb(255, 20, 210, 30));
                source.SetPixel(0, 1, Color.FromArgb(255, 30, 40, 200));
                source.SetPixel(1, 1, Color.Transparent);
                using MemoryStream stream = new();
                source.Save(stream, ImageFormat.Png);
                png = stream.ToArray();
            }

            File.WriteAllBytes(path, BuildCursorContainer(png, 2, 2));
            using (Cursor cursor = new(path))
            using (Bitmap target = new(4, 4))
            {
                cursor.Size.Should().Be(new Size(2, 2));
                using (Graphics graphics = Graphics.FromImage(target))
                {
                    cursor.Draw(graphics, new Rectangle(1, 1, 2, 2));
                }

                target.GetPixel(1, 1).ToArgb().Should().Be(Color.FromArgb(255, 220, 10, 20).ToArgb());
                target.GetPixel(2, 1).ToArgb().Should().Be(Color.FromArgb(255, 20, 210, 30).ToArgb());
                target.GetPixel(2, 2).A.Should().Be(0);
                target.GetPixel(0, 0).A.Should().Be(0);
            }

            Color[] alphaPixels =
            [
                Color.FromArgb(255, 255, 0, 0),
                Color.FromArgb(128, 0, 255, 0),
                Color.FromArgb(255, 0, 0, 255),
                Color.FromArgb(255, 255, 255, 255),
            ];
            byte[] dib = BuildDibPayload(2, 2, alphaPixels, [false, false, false, true]);
            using (MemoryStream stream = new(BuildCursorContainer(dib, 2, 2), writable: false))
            using (Cursor cursor = new(stream))
            using (Bitmap target = new(2, 2))
            {
                using (Graphics graphics = Graphics.FromImage(target))
                {
                    cursor.Draw(graphics, new Rectangle(0, 0, 2, 2));
                }

                target.GetPixel(0, 0).ToArgb().Should().Be(Color.FromArgb(255, 255, 0, 0).ToArgb());
                target.GetPixel(1, 0).ToArgb().Should().Be(Color.FromArgb(128, 0, 255, 0).ToArgb());
                target.GetPixel(0, 1).ToArgb().Should().Be(Color.FromArgb(255, 0, 0, 255).ToArgb());
                target.GetPixel(1, 1).A.Should().Be(0);
            }

            byte[] unusedAlphaDib = BuildDibPayload(
                2,
                1,
                [Color.FromArgb(0, 180, 30, 20), Color.FromArgb(0, 20, 180, 30)],
                [false, true]);
            using (MemoryStream stream = new(BuildCursorContainer(unusedAlphaDib, 2, 1), writable: false))
            using (Cursor cursor = new(stream))
            using (Bitmap target = new(2, 1))
            {
                using (Graphics graphics = Graphics.FromImage(target))
                {
                    cursor.Draw(graphics, new Rectangle(0, 0, 2, 1));
                }

                target.GetPixel(0, 0).ToArgb().Should().Be(Color.FromArgb(255, 180, 30, 20).ToArgb());
                target.GetPixel(1, 0).A.Should().Be(0);
            }

            byte[] malformedBounds = BuildCursorContainer(new byte[40], 1, 1);
            BinaryPrimitives.WriteUInt32LittleEndian(malformedBounds.AsSpan(18, 4), (uint)(malformedBounds.Length + 1));
            Action malformedAction = () => new Cursor(new MemoryStream(malformedBounds, writable: false));
            malformedAction.Should().Throw<InvalidDataException>().WithMessage("*outside the data bounds*");

            byte[] truncatedDib = BuildDibPayload(1, 1, [Color.FromArgb(255, 1, 2, 3)], [false]);
            Array.Resize(ref truncatedDib, truncatedDib.Length - 1);
            Action truncatedAction = () => new Cursor(
                new MemoryStream(BuildCursorContainer(truncatedDib, 1, 1), writable: false));
            truncatedAction.Should().Throw<InvalidDataException>().WithMessage("*truncated*");

            byte[] unsupportedBitDepth = BuildDibPayload(1, 1, [Color.FromArgb(255, 1, 2, 3)], [false]);
            BinaryPrimitives.WriteUInt16LittleEndian(unsupportedBitDepth.AsSpan(14, 2), 24);
            Action bitDepthAction = () => new Cursor(
                new MemoryStream(BuildCursorContainer(unsupportedBitDepth, 1, 1), writable: false));
            bitDepthAction.Should().Throw<NotSupportedException>().WithMessage("*bit depth 24*");

            byte[] unsupportedCompression = BuildDibPayload(1, 1, [Color.FromArgb(255, 1, 2, 3)], [false]);
            BinaryPrimitives.WriteUInt32LittleEndian(unsupportedCompression.AsSpan(16, 4), 3);
            Action compressionAction = () => new Cursor(
                new MemoryStream(BuildCursorContainer(unsupportedCompression, 1, 1), writable: false));
            compressionAction.Should().Throw<NotSupportedException>().WithMessage("*compression mode 3*");

            Cursor shared = Cursors.Default;
            Size sharedSize = shared.Size;
            shared.Dispose();
            shared.Size.Should().Be(sharedSize);
            sharedSize.Should().Be(SystemInformation.CursorSize);
            Cursors.Default.Should().BeSameAs(shared);
        }
        finally
        {
            File.Delete(path);
        }

        static byte[] BuildCursorContainer(byte[] payload, int width, int height)
        {
            const int payloadOffset = 6 + 16;
            byte[] container = new byte[payloadOffset + payload.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(container.AsSpan(2, 2), 2);
            BinaryPrimitives.WriteUInt16LittleEndian(container.AsSpan(4, 2), 1);
            container[6] = width == 256 ? (byte)0 : checked((byte)width);
            container[7] = height == 256 ? (byte)0 : checked((byte)height);
            BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(14, 4), checked((uint)payload.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(18, 4), payloadOffset);
            payload.CopyTo(container, payloadOffset);
            return container;
        }

        static byte[] BuildDibPayload(int width, int height, Color[] pixels, bool[] mask)
        {
            const int headerSize = 40;
            int colorStride = checked(width * 4);
            int maskStride = checked(((width + 31) / 32) * 4);
            int colorByteCount = checked(colorStride * height);
            byte[] payload = new byte[checked(headerSize + colorByteCount + maskStride * height)];
            BinaryPrimitives.WriteUInt32LittleEndian(payload, headerSize);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), width);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), checked(height * 2));
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(12, 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(14, 2), 32);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20, 4), checked((uint)(colorByteCount + maskStride * height)));

            int maskOffset = headerSize + colorByteCount;
            for (int encodedRow = 0; encodedRow < height; encodedRow++)
            {
                int sourceY = height - 1 - encodedRow;
                for (int x = 0; x < width; x++)
                {
                    Color color = pixels[sourceY * width + x];
                    int colorOffset = headerSize + encodedRow * colorStride + x * 4;
                    payload[colorOffset] = color.B;
                    payload[colorOffset + 1] = color.G;
                    payload[colorOffset + 2] = color.R;
                    payload[colorOffset + 3] = color.A;
                    if (mask[sourceY * width + x])
                    {
                        payload[maskOffset + encodedRow * maskStride + x / 8] |= (byte)(0x80 >> (x & 7));
                    }
                }
            }

            return payload;
        }
    }

    [Fact]
    public void ApplicationRun_CanonicalForm_UsesTypedPortableLifecycle()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: true);
        using Form form = new() { Text = "Canonical portable lifecycle" };
        using InputProbeControl child = new() { Bounds = new Rectangle(12, 18, 120, 60) };
        form.Controls.Add(child);

        List<string> events = [];
        int closeAttempts = 0;
        int paintCallbacks = 0;
        Rectangle formPaintClip = default;
        Rectangle childPaintClip = default;
        RectangleF visibleClip = default;
        RectangleF createGraphicsVisibleClip = default;
        int paintCallbacksBeforeUpdate = -1;
        int paintCallbacksAfterUpdate = -1;
        int paintCallbacksAfterCleanUpdate = -1;
        List<string> inputEvents = [];
        Point mouseLocation = default;
        Point mousePosition = default;
        bool focusedDuringGotFocus = false;
        bool containsFocusDuringKeyDown = false;
        bool shiftSeenDuringKeyDown = false;
        bool leftButtonSeenDuringMouseDown = false;
        bool captureSeenDuringMouseDown = false;
        bool noButtonSeenDuringMouseUp = false;
        Keys keyCode = Keys.None;
        char keyChar = default;
        int wheelDelta = 0;
        Exception? inputException = null;
        form.Paint += (_, e) =>
        {
            paintCallbacks++;
            formPaintClip = e.ClipRectangle;
            visibleClip = e.Graphics.VisibleClipBounds;
            e.Graphics.FillRectangle(Brushes.CornflowerBlue, new Rectangle(4, 5, 24, 16));
        };
        child.Paint += (_, e) =>
        {
            paintCallbacks++;
            childPaintClip = e.ClipRectangle;
            e.Graphics.FillRectangle(Brushes.OrangeRed, new Rectangle(2, 3, 10, 8));
        };
        child.GotFocus += (_, _) =>
        {
            inputEvents.Add(nameof(child.GotFocus));
            focusedDuringGotFocus = child.Focused;
        };
        child.LostFocus += (_, _) => inputEvents.Add(nameof(child.LostFocus));
        child.MouseEnter += (_, _) => inputEvents.Add(nameof(child.MouseEnter));
        child.MouseMove += (_, e) =>
        {
            inputEvents.Add(nameof(child.MouseMove));
            mouseLocation = e.Location;
            mousePosition = Control.MousePosition;
        };
        child.MouseDown += (_, _) =>
        {
            inputEvents.Add(nameof(child.MouseDown));
            leftButtonSeenDuringMouseDown = Control.MouseButtons == MouseButtons.Left;
            captureSeenDuringMouseDown = child.Capture;
        };
        child.Click += (_, _) => inputEvents.Add(nameof(child.Click));
        child.MouseUp += (_, _) =>
        {
            inputEvents.Add(nameof(child.MouseUp));
            noButtonSeenDuringMouseUp = Control.MouseButtons == MouseButtons.None;
        };
        child.MouseWheel += (_, e) =>
        {
            inputEvents.Add(nameof(child.MouseWheel));
            wheelDelta = e.Delta;
        };
        child.KeyDown += (_, e) =>
        {
            inputEvents.Add(nameof(child.KeyDown));
            keyCode = e.KeyCode;
            shiftSeenDuringKeyDown = Control.ModifierKeys == Keys.Shift;
            containsFocusDuringKeyDown = form.ContainsFocus && child.ContainsFocus;
        };
        child.KeyPress += (_, e) =>
        {
            inputEvents.Add(nameof(child.KeyPress));
            keyChar = e.KeyChar;
        };
        child.KeyUp += (_, _) => inputEvents.Add(nameof(child.KeyUp));
        form.HandleCreated += (_, _) => events.Add(nameof(form.HandleCreated));
        form.VisibleChanged += (_, _) => events.Add(nameof(form.VisibleChanged));
        form.Shown += (_, _) => events.Add(nameof(form.Shown));
        form.Shown += (_, _) =>
        {
            form.Bounds = new(40, 50, 640, 480);
            form.Invalidate();
            paintCallbacksBeforeUpdate = paintCallbacks;
            form.Update();
            paintCallbacksAfterUpdate = paintCallbacks;
            form.Update();
            paintCallbacksAfterCleanUpdate = paintCallbacks;
            using (Graphics graphics = child.CreateGraphics())
            {
                createGraphicsVisibleClip = graphics.VisibleClipBounds;
                graphics.FillRectangle(Brushes.MediumPurple, new Rectangle(2, 3, 10, 8));
            }

            using (child.CreateGraphics())
            {
                // A recorder with no application drawing must not queue a presentation.
            }

            try
            {
                platform.SendInput(LibreInputEventKind.FocusGained);
                platform.SendInput(LibreInputEventKind.PointerMove, position: new(17, 24));
                platform.SendInput(LibreInputEventKind.PointerDown, position: new(17, 24), button: LibrePointerButton.Primary);
                platform.SendInput(LibreInputEventKind.PointerUp, position: new(17, 24), button: LibrePointerButton.Primary);
                platform.SendInput(LibreInputEventKind.PointerWheel, position: new(17, 24), delta: new(0, 120));
                platform.SendInput(LibreInputEventKind.KeyDown, modifiers: LibreInputModifiers.Shift, key: LibreKey.A);
                platform.SendInput(LibreInputEventKind.TextInput, modifiers: LibreInputModifiers.Shift, text: "a");
                platform.SendInput(LibreInputEventKind.KeyUp, key: LibreKey.A);
                platform.SendInput(LibreInputEventKind.FocusLost);
            }
            catch (Exception exception)
            {
                inputException = exception;
            }
        };
        form.FormClosing += (_, e) =>
        {
            events.Add(nameof(form.FormClosing));
            e.Cancel = ++closeAttempts == 1;
        };
        form.FormClosed += (_, _) => events.Add(nameof(form.FormClosed));
        form.HandleDestroyed += (_, _) => events.Add(nameof(form.HandleDestroyed));

        Application.Run(form);

        platform.WindowsCreated.Should().Be(1);
        events.Should().ContainInOrder(
            nameof(form.HandleCreated),
            nameof(form.VisibleChanged),
            nameof(form.Shown),
            nameof(form.FormClosing),
            nameof(form.FormClosing),
            nameof(form.FormClosed),
            nameof(form.HandleDestroyed));
        closeAttempts.Should().Be(2);
        platform.LastWindowBounds.Should().Be(new LibreRectangle(40, 50, 640, 480));
        platform.LastDirtyRectangle.Should().Be(new LibreRectangle(0, 0, 640, 480));
        platform.PresentCount.Should().Be(2);
        paintCallbacksAfterUpdate.Should().Be(paintCallbacksBeforeUpdate + 2);
        paintCallbacksAfterCleanUpdate.Should().Be(paintCallbacksAfterUpdate);
        paintCallbacks.Should().Be(2);
        formPaintClip.Should().Be(new Rectangle(0, 0, 640, 480));
        childPaintClip.Should().Be(new Rectangle(0, 0, 120, 60));
        visibleClip.Should().Be(new RectangleF(0, 0, 640, 480));
        createGraphicsVisibleClip.Should().Be(new RectangleF(0, 0, 120, 60));
        platform.LastPaintCommandCount.Should().BeGreaterThan(0);
        platform.SawFormPaintFill.Should().BeTrue();
        platform.SawTranslatedChildPaintFill.Should().BeTrue();
        platform.CreateGraphicsCommitCount.Should().Be(1);
        platform.SawCreateGraphicsTranslatedFill.Should().BeTrue();
        inputException.Should().BeNull();
        inputEvents.Should().ContainInOrder(
            nameof(child.MouseEnter),
            nameof(child.MouseMove),
            nameof(child.GotFocus),
            nameof(child.MouseDown),
            nameof(child.Click),
            nameof(child.MouseUp),
            nameof(child.MouseWheel),
            nameof(child.KeyDown),
            nameof(child.KeyPress),
            nameof(child.KeyUp),
            nameof(child.LostFocus));
        mouseLocation.Should().Be(new Point(5, 6));
        mousePosition.Should().Be(new Point(57, 74));
        focusedDuringGotFocus.Should().BeTrue();
        containsFocusDuringKeyDown.Should().BeTrue();
        shiftSeenDuringKeyDown.Should().BeTrue();
        leftButtonSeenDuringMouseDown.Should().BeTrue();
        captureSeenDuringMouseDown.Should().BeTrue();
        noButtonSeenDuringMouseUp.Should().BeTrue();
        keyCode.Should().Be(Keys.A);
        keyChar.Should().Be('a');
        wheelDelta.Should().Be(120);
        form.IsDisposed.Should().BeTrue();
        form.IsHandleCreated.Should().BeFalse();
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void UserControlMouseDown_PreservesFocusedDescendantWithoutUser32FocusQuery()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { Bounds = new Rectangle(20, 30, 240, 160) };
        using MouseDownProbeUserControl userControl = new() { Bounds = new Rectangle(10, 12, 120, 80) };
        using InputProbeControl child = new() { Bounds = new Rectangle(5, 6, 40, 24) };
        userControl.Controls.Add(child);
        form.Controls.Add(userControl);
        form.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        child.Focus().Should().BeTrue();
        child.Focused.Should().BeTrue();
        userControl.ContainsFocus.Should().BeTrue();

        int userControlGotFocus = 0;
        userControl.GotFocus += (_, _) => userControlGotFocus++;
        userControl.RaiseMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 3, 4, 0));

        child.Focused.Should().BeTrue();
        userControl.Focused.Should().BeFalse();
        userControlGotFocus.Should().Be(0);
    }

    [Fact]
    public void MessageBox_UsesTypedModalServiceWithCanonicalOwnerOptionsAndResult()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { Text = "Owner", Bounds = new Rectangle(40, 50, 320, 220) };
        form.Show();
        nint ownerHandle = form.Handle;
        platform.NextMessageBoxResult = LibreMessageBoxResult.TryAgain;

        DialogResult result = MessageBox.Show(
            form,
            "Choose the next action.",
            "Portable message",
            MessageBoxButtons.CancelTryContinue,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2,
            MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);

        result.Should().Be(DialogResult.TryAgain);
        platform.MessageBoxShowCount.Should().Be(1);
        platform.LastMessageBoxRequest.Should().Be(new LibreMessageBoxRequest(
            "Choose the next action.",
            "Portable message",
            LibreMessageBoxButtons.CancelTryContinue,
            LibreMessageBoxIcon.Warning,
            LibreMessageBoxDefaultButton.Button2,
            LibreMessageBoxOptions.RightAlign | LibreMessageBoxOptions.RightToLeftReading,
            ShowHelp: false,
            new LibreHandle(ownerHandle, LibreHandleKind.Window)));
        platform.MessageBoxOwnerDisabledDuringShow.Should().BeTrue();
        form.Enabled.Should().BeTrue();
        form.Handle.Should().Be(ownerHandle);
        platform.WindowsCreated.Should().Be(1);
    }

    [Fact]
    public void ColorDialog_UsesTypedCommonDialogPathWithOwnerHelpAndOwnedResult()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { Text = "Owner", Bounds = new Rectangle(40, 50, 320, 220) };
        form.Show();
        nint ownerHandle = form.Handle;
        int helpRequests = 0;
        platform.NextColorDialogResult = new LibreColorDialogResult(
            Accepted: true,
            Color.MediumPurple,
            [Color.DarkCyan, Color.Goldenrod]);
        platform.InvokeColorDialogHelp = true;
        using var dialog = new ColorDialog
        {
            Color = Color.Orange,
            CustomColors = [ColorTranslator.ToWin32(Color.CadetBlue)],
            AllowFullOpen = true,
            AnyColor = true,
            FullOpen = true,
            ShowHelp = true,
            SolidColorOnly = true,
        };
        dialog.HelpRequest += (_, _) => helpRequests++;

        DialogResult result = dialog.ShowDialog(form);

        result.Should().Be(DialogResult.OK);
        platform.ColorDialogShowCount.Should().Be(1);
        platform.LastColorDialogRequest.Should().NotBeNull();
        LibreColorDialogRequest request = platform.LastColorDialogRequest!.Value;
        request.Color.Should().Be(Color.Orange);
        request.CustomColors.Should().HaveCount(16);
        request.CustomColors[0].ToArgb().Should().Be(Color.CadetBlue.ToArgb());
        request.Options.Should().Be(
            LibreColorDialogOptions.AllowFullOpen
            | LibreColorDialogOptions.AnyColor
            | LibreColorDialogOptions.FullOpen
            | LibreColorDialogOptions.ShowHelp
            | LibreColorDialogOptions.SolidColorOnly);
        request.Owner.Should().Be(new LibreHandle(ownerHandle, LibreHandleKind.Window));
        platform.ColorDialogOwnerDisabledDuringShow.Should().BeTrue();
        helpRequests.Should().Be(1);
        dialog.Color.ToArgb().Should().Be(Color.MediumPurple.ToArgb());
        dialog.CustomColors[0].Should().Be(ColorTranslator.ToWin32(Color.DarkCyan));
        dialog.CustomColors[1].Should().Be(ColorTranslator.ToWin32(Color.Goldenrod));
        form.Enabled.Should().BeTrue();
        form.Handle.Should().Be(ownerHandle);
        platform.WindowsCreated.Should().Be(1);
    }

    [Fact]
    public void ColorDialog_CancelKeepsCanonicalStateAndCreatesNoFallbackOwnerWindow()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        int initialCustom = ColorTranslator.ToWin32(Color.Goldenrod);
        platform.NextColorDialogResult = new LibreColorDialogResult(
            Accepted: false,
            Color.Red,
            [Color.Magenta]);
        using var dialog = new ColorDialog
        {
            Color = Color.CadetBlue,
            CustomColors = [initialCustom],
        };

        DialogResult result = dialog.ShowDialog();

        result.Should().Be(DialogResult.Cancel);
        platform.ColorDialogShowCount.Should().Be(1);
        platform.LastColorDialogRequest.Should().NotBeNull();
        platform.LastColorDialogRequest!.Value.Owner.Should().Be(default(LibreHandle));
        dialog.Color.Should().Be(Color.CadetBlue);
        dialog.CustomColors[0].Should().Be(initialCustom);
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void FontDialog_UsesTypedCommonDialogPathWithApplyHelpOwnerAndFinalResult()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { Text = "Owner", Bounds = new Rectangle(40, 50, 320, 220) };
        form.Show();
        nint ownerHandle = form.Handle;
        using Font initialFont = new(FontFamily.GenericSansSerif, 12, FontStyle.Regular, GraphicsUnit.Point);
        LibreFontDialogSelection applied = new(
            FontFamily.GenericSerif.Name,
            14,
            FontStyle.Bold | FontStyle.Underline,
            1,
            false,
            Color.DarkCyan);
        LibreFontDialogSelection selected = new(
            FontFamily.GenericMonospace.Name,
            18,
            FontStyle.Italic | FontStyle.Strikeout,
            1,
            false,
            Color.MediumPurple);
        platform.AppliedFontDialogSelection = applied;
        platform.NextFontDialogResult = new LibreFontDialogResult(true, selected);
        platform.InvokeFontDialogApply = true;
        platform.InvokeFontDialogHelp = true;
        int applyRequests = 0;
        int helpRequests = 0;
        using var dialog = new FontDialog
        {
            Font = initialFont,
            Color = Color.Orange,
            MinSize = 9,
            MaxSize = 28,
            AllowSimulations = false,
            AllowVectorFonts = true,
            AllowVerticalFonts = false,
            AllowScriptChange = false,
            FixedPitchOnly = true,
            FontMustExist = true,
            ScriptsOnly = true,
            ShowApply = true,
            ShowColor = true,
            ShowEffects = true,
            ShowHelp = true,
        };
        dialog.Apply += (_, _) =>
        {
            applyRequests++;
            dialog.Font.Name.Should().Be(applied.FamilyName);
            dialog.Font.Style.Should().Be(applied.Style);
            dialog.Color.ToArgb().Should().Be(applied.Color.ToArgb());
        };
        dialog.HelpRequest += (_, _) => helpRequests++;

        DialogResult result = dialog.ShowDialog(form);

        result.Should().Be(DialogResult.OK);
        platform.FontDialogShowCount.Should().Be(1);
        platform.LastFontDialogRequest.Should().NotBeNull();
        LibreFontDialogRequest request = platform.LastFontDialogRequest!.Value;
        request.Selection.FamilyName.Should().Be(initialFont.Name);
        request.Selection.SizeInPoints.Should().Be(12);
        request.Selection.Style.Should().Be(FontStyle.Regular);
        request.Selection.Color.ToArgb().Should().Be(Color.Orange.ToArgb());
        request.MinimumSize.Should().Be(9);
        request.MaximumSize.Should().Be(28);
        request.Options.Should().Be(
            LibreFontDialogOptions.AllowVectorFonts
            | LibreFontDialogOptions.FixedPitchOnly
            | LibreFontDialogOptions.FontMustExist
            | LibreFontDialogOptions.ScriptsOnly
            | LibreFontDialogOptions.ShowApply
            | LibreFontDialogOptions.ShowColor
            | LibreFontDialogOptions.ShowEffects
            | LibreFontDialogOptions.ShowHelp);
        request.Owner.Should().Be(new LibreHandle(ownerHandle, LibreHandleKind.Window));
        platform.FontDialogOwnerDisabledDuringShow.Should().BeTrue();
        applyRequests.Should().Be(1);
        helpRequests.Should().Be(1);
        dialog.Font.Name.Should().Be(selected.FamilyName);
        dialog.Font.SizeInPoints.Should().Be(18);
        dialog.Font.Style.Should().Be(selected.Style);
        dialog.Color.ToArgb().Should().Be(selected.Color.ToArgb());
        form.Enabled.Should().BeTrue();
        form.Handle.Should().Be(ownerHandle);
        platform.WindowsCreated.Should().Be(1);
    }

    [Fact]
    public void FontDialog_CancelKeepsLastAppliedStateAndCreatesNoFallbackOwnerWindow()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Font initialFont = new(FontFamily.GenericSansSerif, 11, FontStyle.Regular, GraphicsUnit.Point);
        LibreFontDialogSelection applied = new(
            FontFamily.GenericMonospace.Name,
            16,
            FontStyle.Bold | FontStyle.Italic,
            1,
            false,
            Color.Teal);
        platform.AppliedFontDialogSelection = applied;
        platform.NextFontDialogResult = new LibreFontDialogResult(
            false,
            new(FontFamily.GenericSerif.Name, 20, FontStyle.Regular, 1, false, Color.Red));
        platform.InvokeFontDialogApply = true;
        int applyRequests = 0;
        using var dialog = new FontDialog
        {
            Font = initialFont,
            Color = Color.Black,
            ShowApply = true,
        };
        dialog.Apply += (_, _) => applyRequests++;

        DialogResult result = dialog.ShowDialog();

        result.Should().Be(DialogResult.Cancel);
        platform.FontDialogShowCount.Should().Be(1);
        platform.LastFontDialogRequest.Should().NotBeNull();
        platform.LastFontDialogRequest!.Value.Owner.Should().Be(default(LibreHandle));
        applyRequests.Should().Be(1);
        dialog.Font.Name.Should().Be(applied.FamilyName);
        dialog.Font.SizeInPoints.Should().Be(16);
        dialog.Font.Style.Should().Be(applied.Style);
        dialog.Color.ToArgb().Should().Be(applied.Color.ToArgb());
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void OpenFileDialog_UsesTypedDesktopPathWithOwnerHelpFiltersAndOptions()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        string directory = Directory.CreateTempSubdirectory("librewinforms-open-").FullName;
        string first = Path.Join(directory, "first.txt");
        string second = Path.Join(directory, "second.txt");
        File.WriteAllText(first, "first");
        File.WriteAllText(second, "second");
        try
        {
            using Form owner = new() { Text = "Owner" };
            owner.Show();
            nint ownerHandle = owner.Handle;
            platform.QueueFileDialogResult(new(true, [first, second], 1, true));
            platform.InvokeFileDialogHelp = true;
            int fileOk = 0;
            int help = 0;
            Guid clientGuid = Guid.NewGuid();
            Guid knownFolder = Guid.NewGuid();
            using var dialog = new OpenFileDialog
            {
                AddToRecent = false,
                AutoUpgradeEnabled = false,
                ClientGuid = clientGuid,
                DefaultExt = ".txt",
                DereferenceLinks = false,
                FileName = "seed.txt",
                Filter = "Text files|*.txt|All files|*.*",
                FilterIndex = 2,
                InitialDirectory = directory,
                Multiselect = true,
                OkRequiresInteraction = true,
                ReadOnlyChecked = true,
                RestoreDirectory = true,
                SelectReadOnly = false,
                ShowHelp = true,
                ShowHiddenFiles = true,
                ShowPinnedPlaces = false,
                ShowPreview = true,
                ShowReadOnly = true,
                SupportMultiDottedExtensions = true,
                Title = "Choose source files",
            };
            dialog.CustomPlaces.Add(new FileDialogCustomPlace(directory));
            dialog.CustomPlaces.Add(new FileDialogCustomPlace(knownFolder));
            dialog.FileOk += (_, _) => fileOk++;
            dialog.HelpRequest += (_, _) => help++;

            DialogResult result = dialog.ShowDialog(owner);

            result.Should().Be(DialogResult.OK);
            platform.FileDialogShowCount.Should().Be(1);
            platform.LastFileDialogRequest.Should().NotBeNull();
            LibreFileDialogRequest request = platform.LastFileDialogRequest!.Value;
            request.Kind.Should().Be(LibreFileDialogKind.OpenFile);
            request.Title.Should().Be("Choose source files");
            request.InitialDirectory.Should().Be(directory);
            request.SelectedPaths.Should().Equal("seed.txt");
            request.DefaultExtension.Should().Be("txt");
            request.FilterIndex.Should().Be(2);
            request.Filters.Should().HaveCount(2);
            request.Filters[0].Name.Should().Be("Text files");
            request.Filters[0].Patterns.Should().Equal("*.txt");
            request.Options.Should().Be(
                LibreFileDialogOptions.AddExtension
                | LibreFileDialogOptions.CheckFileExists
                | LibreFileDialogOptions.CheckPathExists
                | LibreFileDialogOptions.RestoreDirectory
                | LibreFileDialogOptions.ShowHelp
                | LibreFileDialogOptions.ShowHiddenFiles
                | LibreFileDialogOptions.SupportMultiDottedExtensions
                | LibreFileDialogOptions.ValidateNames
                | LibreFileDialogOptions.MultiSelect
                | LibreFileDialogOptions.ReadOnlyChecked
                | LibreFileDialogOptions.ShowPreview
                | LibreFileDialogOptions.ShowReadOnly
                | LibreFileDialogOptions.OkRequiresInteraction);
            request.ClientGuid.Should().Be(clientGuid);
            request.CustomPlaces.Should().Equal(
                new LibreFileDialogPlace(directory, null),
                new LibreFileDialogPlace(string.Empty, knownFolder));
            request.Owner.Should().Be(new LibreHandle(ownerHandle, LibreHandleKind.Window));
            platform.FileDialogOwnerDisabledDuringShow.Should().BeTrue();
            fileOk.Should().Be(1);
            help.Should().Be(1);
            dialog.FileNames.Should().Equal(first, second);
            dialog.FilterIndex.Should().Be(1);
            dialog.ReadOnlyChecked.Should().BeTrue();
            owner.Enabled.Should().BeTrue();
            owner.Handle.Should().Be(ownerHandle);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OpenFileDialog_CancelledFileOkReopensAndCommitsOnlyAcceptedCandidate()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        string first = Path.GetTempFileName();
        string second = Path.GetTempFileName();
        try
        {
            platform.QueueFileDialogResult(new(true, [first], 0, false));
            platform.QueueFileDialogResult(new(true, [second], 0, false));
            using var dialog = new OpenFileDialog { FilterIndex = 0 };
            int notifications = 0;
            dialog.FileOk += (_, e) =>
            {
                notifications++;
                e.Cancel = notifications == 1;
            };

            DialogResult result = dialog.ShowDialog();

            result.Should().Be(DialogResult.OK);
            platform.FileDialogShowCount.Should().Be(2);
            notifications.Should().Be(2);
            platform.LastFileDialogRequest!.Value.SelectedPaths.Should().Equal(first);
            dialog.FileName.Should().Be(second);
            platform.WindowsCreated.Should().Be(0);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Fact]
    public void SaveFileDialog_UsesCanonicalExtensionAndPreservesStateOnCancel()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        string directory = Directory.CreateTempSubdirectory("librewinforms-save-").FullName;
        string candidate = Path.Join(directory, "document");
        try
        {
            platform.QueueFileDialogResult(new(true, [candidate], 1, false));
            using var dialog = new SaveFileDialog
            {
                DefaultExt = "txt",
                Filter = "Text files|*.txt",
                InitialDirectory = directory,
                Title = "Save document",
            };
            string? fileOkName = null;
            dialog.FileOk += (_, _) => fileOkName = dialog.FileName;

            dialog.ShowDialog().Should().Be(DialogResult.OK);

            dialog.FileName.Should().Be(candidate + ".txt");
            fileOkName.Should().Be(candidate + ".txt");
            LibreFileDialogRequest request = platform.LastFileDialogRequest!.Value;
            request.Kind.Should().Be(LibreFileDialogKind.SaveFile);
            request.Options.Should().HaveFlag(LibreFileDialogOptions.CheckWriteAccess);
            request.Options.Should().HaveFlag(LibreFileDialogOptions.ExpandedMode);
            request.Options.Should().HaveFlag(LibreFileDialogOptions.OverwritePrompt);

            platform.QueueFileDialogResult(new(false, [Path.Join(directory, "ignored.txt")], 1, false));
            dialog.ShowDialog().Should().Be(DialogResult.Cancel);
            dialog.FileName.Should().Be(candidate + ".txt");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FolderBrowserDialog_UsesTypedDesktopPathAndRetainsSelectionOnCancel()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        string first = Directory.CreateTempSubdirectory("librewinforms-folder-a-").FullName;
        string second = Directory.CreateTempSubdirectory("librewinforms-folder-b-").FullName;
        try
        {
            using Form owner = new();
            owner.Show();
            platform.QueueFileDialogResult(new(true, [first, second], 0, false));
            using var dialog = new FolderBrowserDialog
            {
                AddToRecent = false,
                AutoUpgradeEnabled = false,
                Description = "Choose output folders",
                InitialDirectory = first,
                Multiselect = true,
                OkRequiresInteraction = true,
                SelectedPath = first,
                ShowHiddenFiles = true,
                ShowNewFolderButton = false,
                ShowPinnedPlaces = false,
                UseDescriptionForTitle = true,
            };

            dialog.ShowDialog(owner).Should().Be(DialogResult.OK);

            dialog.SelectedPaths.Should().Equal(first, second);
            LibreFileDialogRequest request = platform.LastFileDialogRequest!.Value;
            request.Kind.Should().Be(LibreFileDialogKind.SelectFolder);
            request.Title.Should().Be("Choose output folders");
            request.Description.Should().Be("Choose output folders");
            request.InitialDirectory.Should().Be(first);
            request.Options.Should().Be(
                LibreFileDialogOptions.MultiSelect
                | LibreFileDialogOptions.OkRequiresInteraction
                | LibreFileDialogOptions.ShowHiddenFiles
                | LibreFileDialogOptions.UseDescriptionForTitle);
            request.Owner.IsNull.Should().BeFalse();
            platform.FileDialogOwnerDisabledDuringShow.Should().BeTrue();

            platform.QueueFileDialogResult(new(false, [first], 0, false));
            dialog.ShowDialog().Should().Be(DialogResult.Cancel);
            dialog.SelectedPaths.Should().Equal(first, second);
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }

    [Fact]
    public void ControlCreateGraphics_UsesAncestorClipWithoutNativeHwndGraphics()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ClientSize = new Size(100, 50) };
        using Panel parent = new() { Bounds = new Rectangle(10, 5, 30, 20) };
        using Control child = new() { Bounds = new Rectangle(20, 0, 30, 20) };
        parent.Controls.Add(child);
        form.Controls.Add(parent);

        using (Graphics graphics = child.CreateGraphics())
        {
            graphics.VisibleClipBounds.Should().Be(new RectangleF(0, 0, 10, 20));
        }

        platform.CreateGraphicsCommitCount.Should().Be(0);
    }

    [Fact]
    public void ControlCreateGraphics_FlushCommitsBatchesAndDrawingContinues()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ClientSize = new Size(100, 50) };
        using Control child = new() { Bounds = new Rectangle(10, 5, 30, 20) };
        form.Controls.Add(child);
        _ = form.Handle;

        using (Graphics graphics = child.CreateGraphics())
        {
            graphics.FillRectangle(Brushes.Red, 0, 0, 4, 3);
            graphics.Flush();

            platform.CreateGraphicsCommitCount.Should().Be(1);
            platform.CreateGraphicsFlushCount.Should().Be(1);
            platform.LastCreateGraphicsFlushIntention.Should().Be(FlushIntention.Flush);

            graphics.FillRectangle(Brushes.Blue, 4, 0, 4, 3);
            graphics.Flush(FlushIntention.Sync);

            platform.CreateGraphicsCommitCount.Should().Be(2);
            platform.CreateGraphicsFlushCount.Should().Be(2);
            platform.LastCreateGraphicsFlushIntention.Should().Be(FlushIntention.Sync);
        }

        platform.CreateGraphicsCommitCount.Should().Be(2);
    }

    [Fact]
    public void RetainedPaintFrame_RepaintsDirtyLayersAndPreservesCleanSiblings()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ClientSize = new Size(320, 100) };
        using Control dirtyChild = new() { Bounds = new Rectangle(10, 10, 80, 40) };
        using Control cleanChild = new() { Bounds = new Rectangle(200, 10, 80, 40) };
        form.Controls.Add(dirtyChild);
        form.Controls.Add(cleanChild);

        int formPaints = 0;
        int dirtyChildPaints = 0;
        int cleanChildPaints = 0;
        form.Paint += (_, _) => formPaints++;
        dirtyChild.Paint += (_, _) => dirtyChildPaints++;
        cleanChild.Paint += (_, _) => cleanChildPaints++;

        form.Show();
        form.Invalidate();
        form.Update();
        formPaints.Should().Be(1);
        dirtyChildPaints.Should().Be(1);
        cleanChildPaints.Should().Be(1);
        platform.LastRetainedLayerCount.Should().Be(3);
        platform.LastRetainedLayerRepaintCount.Should().Be(3);

        formPaints = 0;
        dirtyChildPaints = 0;
        cleanChildPaints = 0;
        dirtyChild.Invalidate();
        dirtyChild.Update();

        formPaints.Should().Be(1);
        dirtyChildPaints.Should().Be(1);
        cleanChildPaints.Should().Be(0);
        platform.LastRetainedLayerCount.Should().Be(3);
        platform.LastRetainedLayerRepaintCount.Should().Be(2);
    }

    [Fact]
    public void FormShow_AcceptsTypedExternalTopLevelOwner()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        nint ownerHandle = (nint)0x505721;
        platform.RegisterExternalWindowOwner(ownerHandle);
        var owner = new ExternalWindowOwner(ownerHandle);
        using Form form = new() { Text = "Externally owned" };

        form.Show(owner);

        form.Owner.Should().BeNull();
        platform.GetWindowOwner(form).Should().Be(
            new LibreHandle(ownerHandle, LibreHandleKind.Window));
        platform.ExternalOwnerDisableCount.Should().Be(0);

        form.Close();
    }

    [Fact]
    public void FormShowDialog_DisablesRestoresAndActivatesTypedExternalOwner()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        nint ownerHandle = (nint)0x505722;
        platform.RegisterExternalWindowOwner(ownerHandle);
        var owner = new ExternalWindowOwner(ownerHandle);
        using Form dialog = new() { Text = "Externally owned dialog" };
        dialog.Shown += (_, _) => dialog.DialogResult = DialogResult.OK;

        DialogResult result = dialog.ShowDialog(owner);

        result.Should().Be(DialogResult.OK);
        dialog.Owner.Should().BeNull();
        platform.LastWindowOwner.Should().Be(
            new LibreHandle(ownerHandle, LibreHandleKind.Window));
        platform.ExternalOwnerDisableCount.Should().Be(1);
        platform.ExternalOwnerEnableCount.Should().Be(1);
        platform.ExternalOwnerActivateCount.Should().Be(1);
        platform.GetExternalOwnerState(ownerHandle).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void FormShow_RejectsUnregisteredExternalOwner()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new();
        var owner = new ExternalWindowOwner((nint)0x505723);

        Action show = () => form.Show(owner);

        show.Should().Throw<ArgumentException>()
            .WithParameterName("owner")
            .WithMessage("*live top-level window*");
    }

    [Fact]
    public void ApplicationRun_OwnedAndNestedModalForms_PreserveCanonicalState()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { Text = "Owner" };
        using Control ownerChild = new();
        using Form tool = new() { Text = "Owned tool" };
        using Form firstDialog = new() { Text = "First dialog" };
        using Form nestedDialog = new() { Text = "Nested dialog" };

        DialogResult firstResult = DialogResult.None;
        DialogResult nestedResult = DialogResult.None;
        bool ownerPublicEnabledDuringFirst = false;
        bool ownerPlatformEnabledAfterChildDisable = false;
        bool ownerPlatformDisabledDuringFirst = false;
        bool toolPlatformDisabledDuringFirst = false;
        bool firstPlatformDisabledDuringNested = false;
        bool ownerStillDisabledAfterNested = false;
        bool firstRestoredAfterNested = false;
        bool ownerRestoredAfterFirst = false;
        bool toolRestoredAfterFirst = false;
        LibreHandle toolOwner = default;
        LibreHandle firstOwner = default;
        LibreHandle nestedOwner = default;
        Exception? modalException = null;
        List<string> events = [];
        owner.Controls.Add(ownerChild);

        nestedDialog.Shown += (_, _) =>
        {
            try
            {
                events.Add("nested-shown");
                platform.TrackForm(nestedDialog);
                firstPlatformDisabledDuringNested = !platform.IsWindowEnabled(firstDialog);
                nestedOwner = platform.GetWindowOwner(nestedDialog);
                nestedDialog.Modal.Should().BeTrue();
                nestedDialog.Owner.Should().BeNull();
                nestedDialog.DialogResult = DialogResult.Retry;
            }
            catch (Exception exception)
            {
                modalException = exception;
                nestedDialog.DialogResult = DialogResult.Abort;
            }
        };
        firstDialog.Shown += (_, _) =>
        {
            try
            {
                events.Add("first-shown");
                platform.TrackForm(firstDialog);
                ownerPublicEnabledDuringFirst = owner.Enabled;
                ownerPlatformDisabledDuringFirst = !platform.IsWindowEnabled(owner);
                toolPlatformDisabledDuringFirst = !platform.IsWindowEnabled(tool);
                firstOwner = platform.GetWindowOwner(firstDialog);
                firstDialog.Modal.Should().BeTrue();
                firstDialog.Owner.Should().Be(owner);

                firstDialog.Activate();
                nestedResult = nestedDialog.ShowDialog();
                events.Add("nested-returned");
                firstRestoredAfterNested = platform.IsWindowEnabled(firstDialog);
                ownerStillDisabledAfterNested = !platform.IsWindowEnabled(owner);
                firstDialog.DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                modalException = exception;
                firstDialog.DialogResult = DialogResult.Abort;
            }
        };
        owner.Shown += (_, _) =>
        {
            try
            {
                events.Add("owner-shown");
                platform.TrackForm(owner);
                owner.Activate();
                ownerChild.Enabled = false;
                ownerPlatformEnabledAfterChildDisable = platform.IsWindowEnabled(owner);
                ownerChild.Enabled = true;
                tool.Owner = owner;
                tool.Show();
                platform.TrackForm(tool);
                toolOwner = platform.GetWindowOwner(tool);

                firstResult = firstDialog.ShowDialog(owner);
                events.Add("first-returned");
                ownerRestoredAfterFirst = platform.IsWindowEnabled(owner);
                toolRestoredAfterFirst = platform.IsWindowEnabled(tool);
            }
            catch (Exception exception)
            {
                modalException = exception;
            }
            finally
            {
                tool.Close();
                owner.Close();
            }
        };

        Application.Run(owner);

        modalException.Should().BeNull();
        firstResult.Should().Be(DialogResult.OK);
        nestedResult.Should().Be(DialogResult.Retry);
        events.Should().ContainInOrder(
            "owner-shown",
            "first-shown",
            "nested-shown",
            "nested-returned",
            "first-returned");
        ownerPublicEnabledDuringFirst.Should().BeTrue();
        ownerPlatformEnabledAfterChildDisable.Should().BeTrue();
        ownerPlatformDisabledDuringFirst.Should().BeTrue();
        toolPlatformDisabledDuringFirst.Should().BeTrue();
        firstPlatformDisabledDuringNested.Should().BeTrue();
        firstRestoredAfterNested.Should().BeTrue();
        ownerStillDisabledAfterNested.Should().BeTrue();
        ownerRestoredAfterFirst.Should().BeTrue();
        toolRestoredAfterFirst.Should().BeTrue();
        toolOwner.Should().Be(platform.GetFormerWindowHandle(owner));
        firstOwner.Should().Be(platform.GetFormerWindowHandle(owner));
        nestedOwner.Should().Be(platform.GetFormerWindowHandle(firstDialog));
        firstDialog.Owner.Should().BeNull();
        nestedDialog.Owner.Should().BeNull();
        platform.LastActivatedWindow.Should().Be(platform.GetFormerWindowHandle(owner));
        platform.WindowsCreated.Should().Be(4);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void ScreenAndSystemInformation_UseTypedPortableMonitorInventory()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(
            new LibreMonitor(
                "primary",
                new(0, 0, 1920, 1080),
                new(0, 0, 1920, 1040),
                1,
                true,
                32,
                "Primary display"),
            new LibreMonitor(
                "secondary",
                new(-1280, 0, 1280, 1024),
                new(-1280, 0, 1280, 984),
                1.5,
                false,
                30,
                "Secondary display"));

        Screen[] screens = Screen.AllScreens;
        screens.Should().HaveCount(2);
        Screen.PrimaryScreen.Should().NotBeNull();
        Screen.PrimaryScreen!.DeviceName.Should().Be("Primary display");
        Screen.PrimaryScreen.Bounds.Should().Be(new Rectangle(0, 0, 1920, 1080));
        Screen.PrimaryScreen.WorkingArea.Should().Be(new Rectangle(0, 0, 1920, 1040));
        Screen.FromPoint(new Point(-100, 400)).DeviceName.Should().Be("Secondary display");
        Screen.FromRectangle(new Rectangle(-100, 100, 300, 500)).Primary.Should().BeTrue();
        SystemInformation.PrimaryMonitorSize.Should().Be(new Size(1920, 1080));
        SystemInformation.WorkingArea.Should().Be(new Rectangle(0, 0, 1920, 1040));
        SystemInformation.VirtualScreen.Should().Be(new Rectangle(-1280, 0, 3200, 1080));
        SystemInformation.MonitorCount.Should().Be(2);
        SystemInformation.MonitorsSameDisplayFormat.Should().BeFalse();

        using Form owner = new() { Bounds = new Rectangle(-1000, 100, 600, 500) };
        using CenteringForm child = new() { Size = new Size(200, 100), Owner = owner };
        Screen.FromControl(owner).DeviceName.Should().Be("Secondary display");
        child.CenterOnParent();
        child.Location.Should().Be(new Point(-800, 300));
        child.CenterOnScreen();
        child.Location.Should().Be(new Point(-740, 442));
    }

    [Fact]
    public void PresentationScaleChange_InvalidatesLogicalSurfaceWithoutDoubleScalingControls()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { Size = new Size(400, 300) };
        int deviceDpiBefore = 0;
        int deviceDpiAfter = 0;

        form.Shown += (_, _) =>
        {
            deviceDpiBefore = form.DeviceDpi;
            platform.SetPresentationScale(2.0);
            deviceDpiAfter = form.DeviceDpi;
            platform.Post(form.Close);
        };

        Application.Run(form);

        platform.LastPresentationScale.Should().Be(2.0);
        platform.PresentationInvalidationCount.Should().Be(1);
        deviceDpiBefore.Should().Be(96);
        deviceDpiAfter.Should().Be(96);
        form.Size.Should().Be(new Size(400, 300));
    }

    [Fact]
    public void LogicalPresentation_SeparatesWindowsDpiFromFramebufferScale()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetInitialPresentationScales(dpiScale: 2.0, framebufferScale: 1.0);
        using Form form = new()
        {
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(10, 20, 400, 300),
        };
        LibreRectangle initialNativeBounds = default;
        Rectangle initialManagedBounds = default;
        int initialDeviceDpi = 0;

        form.Shown += (_, _) =>
        {
            initialNativeBounds = platform.LastNativeWindowBounds;
            initialManagedBounds = form.Bounds;
            initialDeviceDpi = form.DeviceDpi;
            platform.SetPresentationScales(dpiScale: 1.0, framebufferScale: 1.0);
            platform.Post(form.Close);
        };

        Application.Run(form);

        platform.LastCoordinateMode.Should().Be(LibreWindowCoordinateMode.Logical);
        initialNativeBounds.Should().Be(new LibreRectangle(20, 40, 800, 600));
        initialManagedBounds.Should().Be(new Rectangle(10, 20, 400, 300));
        initialDeviceDpi.Should().Be(96);
        platform.LastNativeWindowBounds.Should().Be(new LibreRectangle(10, 20, 400, 300));
        form.Bounds.Should().Be(new Rectangle(10, 20, 400, 300));
        form.DeviceDpi.Should().Be(96);
    }

    [Fact]
    public void PerMonitorV2_UsesDevicePixelCoordinatesAndRaisesCanonicalDpiEvents()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(new LibreMonitor(
            "primary",
            new(0, 0, 1920, 1080),
            new(0, 0, 1920, 1040),
            2.0,
            true));
        platform.SetInitialPresentationScales(dpiScale: 2.0, framebufferScale: 2.0);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2).Should().BeTrue();

        using Form form = new()
        {
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96, 96),
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(10, 20, 400, 300),
        };
        using Control child = new() { Bounds = new Rectangle(20, 30, 100, 40) };
        form.Controls.Add(child);

        int initialFormDpi = 0;
        int initialChildDpi = 0;
        Rectangle initialFormBounds = default;
        Rectangle initialChildBounds = default;
        int changedFormDpi = 0;
        int changedChildDpi = 0;
        Rectangle changedFormBounds = default;
        Rectangle changedChildBounds = default;
        DpiChangedEventArgs? changed = null;
        Exception? callbackException = null;
        List<string> dpiEvents = [];

        child.DpiChangedBeforeParent += (_, _) => dpiEvents.Add("child-before");
        form.DpiChanged += (_, e) =>
        {
            dpiEvents.Add("form");
            changed = e;
        };
        child.DpiChangedAfterParent += (_, _) => dpiEvents.Add("child-after");
        form.Shown += (_, _) =>
        {
            try
            {
                initialFormDpi = form.DeviceDpi;
                initialChildDpi = child.DeviceDpi;
                initialFormBounds = form.Bounds;
                initialChildBounds = child.Bounds;

                platform.SetPresentationScale(1.0);

                changedFormDpi = form.DeviceDpi;
                changedChildDpi = child.DeviceDpi;
                changedFormBounds = form.Bounds;
                changedChildBounds = child.Bounds;
            }
            catch (Exception exception)
            {
                callbackException = exception;
            }
            finally
            {
                platform.Post(form.Close);
            }
        };

        try
        {
            Application.Run(form);
        }
        finally
        {
            Application.SetHighDpiMode(HighDpiMode.DpiUnaware).Should().BeTrue();
        }

        platform.LastCoordinateMode.Should().Be(LibreWindowCoordinateMode.DevicePixels);
        callbackException.Should().BeNull();
        initialFormDpi.Should().Be(192);
        initialChildDpi.Should().Be(192);
        initialFormBounds.Should().Be(new Rectangle(10, 20, 800, 600));
        initialChildBounds.Should().Be(new Rectangle(40, 60, 200, 80));
        changedFormDpi.Should().Be(96);
        changedChildDpi.Should().Be(96);
        changedFormBounds.Should().Be(new Rectangle(5, 10, 400, 300));
        changedChildBounds.Should().Be(new Rectangle(20, 30, 100, 40));
        changed.Should().NotBeNull();
        changed!.DeviceDpiOld.Should().Be(192);
        changed.DeviceDpiNew.Should().Be(96);
        changed.SuggestedRectangle.Should().Be(new Rectangle(5, 10, 400, 300));
        dpiEvents.Should().ContainInOrder("child-before", "form", "child-after");
        platform.PresentationInvalidationCount.Should().Be(1);
    }

    [Fact]
    public void PerMonitorV2_SeparatesWindowsDpiFromFramebufferScale()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(new LibreMonitor(
            "primary",
            new(0, 0, 1920, 1080),
            new(0, 0, 1920, 1040),
            2.0,
            true));
        platform.SetInitialPresentationScales(dpiScale: 2.0, framebufferScale: 1.0);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2).Should().BeTrue();

        using Form form = new()
        {
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96, 96),
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(10, 20, 400, 300),
        };
        Rectangle initialManagedBounds = default;
        LibreRectangle initialNativeBounds = default;

        form.Shown += (_, _) =>
        {
            initialManagedBounds = form.Bounds;
            initialNativeBounds = platform.LastNativeWindowBounds;
            platform.SetPresentationScales(dpiScale: 1.0, framebufferScale: 1.0);
            platform.Post(form.Close);
        };

        try
        {
            Application.Run(form);
        }
        finally
        {
            Application.SetHighDpiMode(HighDpiMode.DpiUnaware).Should().BeTrue();
        }

        initialManagedBounds.Should().Be(new Rectangle(10, 20, 800, 600));
        initialNativeBounds.Should().Be(new LibreRectangle(10, 20, 800, 600));
        form.Bounds.Should().Be(new Rectangle(10, 20, 400, 300));
        platform.LastNativeWindowBounds.Should().Be(new LibreRectangle(10, 20, 400, 300));
        form.DeviceDpi.Should().Be(96);
    }

    [Fact]
    public void BringToFrontAndSendToBack_PreserveCanonicalChildAndTopLevelSemantics()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new();
        using Control first = new();
        using Control second = new();
        form.Controls.Add(first);
        form.Controls.Add(second);

        _ = form.Handle;
        _ = first.Handle;
        _ = second.Handle;
        nint formHandle = form.Handle;

        first.BringToFront();
        form.Controls.GetChildIndex(first).Should().Be(0);
        platform.WindowZOrderChangeCount.Should().Be(0);

        first.SendToBack();
        form.Controls.GetChildIndex(first).Should().Be(form.Controls.Count - 1);
        platform.WindowZOrderChangeCount.Should().Be(0);

        form.BringToFront();
        platform.LastWindowZOrder.Should().Be(LibreWindowZOrder.Front);
        platform.WindowZOrderChangeCount.Should().Be(1);
        form.Handle.Should().Be(formHandle);

        form.SendToBack();
        platform.LastWindowZOrder.Should().Be(LibreWindowZOrder.Back);
        platform.WindowZOrderChangeCount.Should().Be(2);
        form.Handle.Should().Be(formHandle);
    }

    [Fact]
    public void StockCursors_UseTypedPortableTransportWithHoverInheritanceAndCapture()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new()
        {
            Bounds = new Rectangle(20, 30, 240, 160),
            Cursor = Cursors.Cross,
        };
        using Control child = new()
        {
            Bounds = new Rectangle(10, 12, 80, 50),
            Cursor = Cursors.Hand,
        };
        form.Controls.Add(child);
        int cursorChanged = 0;
        child.CursorChanged += (_, _) => cursorChanged++;

        _ = form.Handle;
        form.Show();
        Cursors.Default.Should().Be(Cursors.Arrow);

        platform.SendInput(LibreInputEventKind.PointerMove, position: new LibrePoint(15, 18));
        platform.LastCursorShape.Should().Be(LibreCursorShape.Hand);

        child.Cursor = Cursors.IBeam;
        platform.LastCursorShape.Should().Be(LibreCursorShape.IBeam);
        cursorChanged.Should().Be(1);

        child.Capture = true;
        platform.SendInput(LibreInputEventKind.PointerMove, position: new LibrePoint(160, 100));
        (Cursor Cursor, LibreCursorShape Shape)[] stockCursors =
        [
            (Cursors.AppStarting, LibreCursorShape.AppStarting),
            (Cursors.Arrow, LibreCursorShape.Arrow),
            (Cursors.Cross, LibreCursorShape.Cross),
            (Cursors.Default, LibreCursorShape.Arrow),
            (Cursors.IBeam, LibreCursorShape.IBeam),
            (Cursors.No, LibreCursorShape.No),
            (Cursors.SizeAll, LibreCursorShape.SizeAll),
            (Cursors.SizeNESW, LibreCursorShape.SizeNESW),
            (Cursors.SizeNS, LibreCursorShape.SizeNS),
            (Cursors.SizeNWSE, LibreCursorShape.SizeNWSE),
            (Cursors.SizeWE, LibreCursorShape.SizeWE),
            (Cursors.UpArrow, LibreCursorShape.UpArrow),
            (Cursors.WaitCursor, LibreCursorShape.Wait),
            (Cursors.Help, LibreCursorShape.Help),
            (Cursors.Hand, LibreCursorShape.Hand),
            (Cursors.HSplit, LibreCursorShape.HSplit),
            (Cursors.VSplit, LibreCursorShape.VSplit),
            (Cursors.NoMove2D, LibreCursorShape.NoMove2D),
            (Cursors.NoMoveHoriz, LibreCursorShape.NoMoveHoriz),
            (Cursors.NoMoveVert, LibreCursorShape.NoMoveVert),
            (Cursors.PanEast, LibreCursorShape.PanEast),
            (Cursors.PanNE, LibreCursorShape.PanNE),
            (Cursors.PanNorth, LibreCursorShape.PanNorth),
            (Cursors.PanNW, LibreCursorShape.PanNW),
            (Cursors.PanSE, LibreCursorShape.PanSE),
            (Cursors.PanSouth, LibreCursorShape.PanSouth),
            (Cursors.PanSW, LibreCursorShape.PanSW),
            (Cursors.PanWest, LibreCursorShape.PanWest),
        ];
        foreach ((Cursor cursor, LibreCursorShape shape) in stockCursors)
        {
            child.Cursor = cursor;
            platform.LastCursorShape.Should().Be(shape);
        }

        child.Capture = false;
        platform.LastCursorShape.Should().Be(LibreCursorShape.Cross);

        child.Cursor = null!;
        platform.SendInput(LibreInputEventKind.PointerMove, position: new LibrePoint(15, 18));
        platform.LastCursorShape.Should().Be(LibreCursorShape.Cross);

        child.UseWaitCursor = true;
        platform.LastCursorShape.Should().Be(LibreCursorShape.Wait);
        cursorChanged.Should().BeGreaterThan(20);
        platform.CursorChangeCount.Should().BeGreaterThan(20);
    }

    [Fact]
    public void ContextMenuStripUsesCanonicalPortablePopupLifecycleAndTypedCloseReasons()
    {
        _ = UseHeadlessPlatform(autoCloseWindows: false);
        using Control owner = new();
        using ContextMenuStrip menu = new();
        int opening = 0;
        int opened = 0;
        int closing = 0;
        int closed = 0;
        ToolStripDropDownCloseReason reason = default;
        menu.Opening += (_, _) => opening++;
        menu.Opened += (_, _) => opened++;
        menu.Closing += (_, e) =>
        {
            closing++;
            reason = e.CloseReason;
        };
        menu.Closed += (_, e) =>
        {
            closed++;
            reason = e.CloseReason;
        };

        menu.Show(owner, Point.Empty);
        menu.Visible.Should().BeFalse();
        opening.Should().Be(1);
        opened.Should().Be(0);

        menu.Items.Add("Open");
        menu.Show(owner, Point.Empty);
        menu.Visible.Should().BeTrue();
        opening.Should().Be(2);
        opened.Should().Be(1);

        menu.Close(ToolStripDropDownCloseReason.ItemClicked);
        menu.Close(ToolStripDropDownCloseReason.AppClicked);
        menu.Visible.Should().BeFalse();
        closing.Should().Be(1);
        closed.Should().Be(1);
        reason.Should().Be(ToolStripDropDownCloseReason.ItemClicked);

        menu.Show(owner, Point.Empty);
        menu.Close();
        menu.Visible.Should().BeFalse();
        opened.Should().Be(2);
        closing.Should().Be(2);
        closed.Should().Be(2);
        reason.Should().Be(ToolStripDropDownCloseReason.CloseCalled);
    }

    [Fact]
    public void PreCreatedChildHandle_ReparentsThroughCanonicalManagedTreeWithoutNativeParenting()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new()
        {
            Bounds = new Rectangle(20, 30, 280, 180),
        };
        using Panel left = new()
        {
            Bounds = new Rectangle(0, 0, 100, 100),
            Cursor = Cursors.Cross,
        };
        using Panel right = new()
        {
            Bounds = new Rectangle(120, 0, 100, 100),
            Cursor = Cursors.IBeam,
        };
        using Control child = new()
        {
            Bounds = new Rectangle(10, 10, 40, 40),
            Cursor = Cursors.Hand,
        };
        int parentChanged = 0;
        child.ParentChanged += (_, _) => parentChanged++;
        form.Controls.Add(left);
        form.Controls.Add(right);
        left.Controls.Add(child);

        nint childHandle = child.Handle;
        child.IsHandleCreated.Should().BeTrue();
        form.IsHandleCreated.Should().BeFalse();

        form.Show();
        child.Handle.Should().Be(childHandle);
        child.Parent.Should().BeSameAs(left);
        platform.SendInput(LibreInputEventKind.PointerMove, position: new LibrePoint(15, 15));
        platform.LastCursorShape.Should().Be(LibreCursorShape.Hand);

        right.Controls.Add(child);
        child.Handle.Should().Be(childHandle);
        child.Parent.Should().BeSameAs(right);
        left.Controls.Count.Should().Be(0);
        right.Controls.Count.Should().Be(1);
        right.Controls[0].Should().BeSameAs(child);
        platform.SendInput(LibreInputEventKind.PointerMove, position: new LibrePoint(15, 15));
        platform.LastCursorShape.Should().Be(LibreCursorShape.Cross);
        platform.SendInput(LibreInputEventKind.PointerMove, position: new LibrePoint(135, 15));
        platform.LastCursorShape.Should().Be(LibreCursorShape.Hand);

        right.Controls.Remove(child);
        child.Handle.Should().Be(childHandle);
        child.Parent.Should().BeNull();
        left.Controls.Add(child);
        child.Handle.Should().Be(childHandle);
        child.Parent.Should().BeSameAs(left);
        parentChanged.Should().Be(5);
        platform.SendInput(LibreInputEventKind.PointerMove, position: new LibrePoint(15, 15));
        platform.LastCursorShape.Should().Be(LibreCursorShape.Hand);
    }

    [Fact]
    public void BaseAndFormHandleRecreation_UseLogicalAndTypedPortableLifecycles()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using RecreatingForm form = new()
        {
            Bounds = new Rectangle(20, 30, 280, 180),
            StartPosition = FormStartPosition.CenterScreen,
        };
        using RecreatingControl child = new()
        {
            Bounds = new Rectangle(10, 12, 80, 50),
        };
        using Control descendant = new()
        {
            Bounds = new Rectangle(2, 3, 20, 15),
        };
        child.Controls.Add(descendant);
        form.Controls.Add(child);
        form.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        child.Focus().Should().BeTrue();

        nint originalFormHandle = form.Handle;
        nint originalChildHandle = child.Handle;
        nint descendantHandle = descendant.Handle;
        int childHandleCreated = 0;
        int childHandleDestroyed = 0;
        bool childCreatedWhileRecreating = false;
        bool childDestroyedWhileRecreating = false;
        child.HandleCreated += (_, _) =>
        {
            childHandleCreated++;
            childCreatedWhileRecreating = child.RecreatingHandle;
        };
        child.HandleDestroyed += (_, _) =>
        {
            childHandleDestroyed++;
            childDestroyedWhileRecreating = child.RecreatingHandle;
        };

        child.RecreatePortableHandle();

        child.Handle.Should().NotBe(originalChildHandle);
        child.IsHandleCreated.Should().BeTrue();
        child.Created.Should().BeTrue();
        child.Parent.Should().BeSameAs(form);
        descendant.Handle.Should().Be(descendantHandle);
        form.Handle.Should().Be(originalFormHandle);
        child.ContainsFocus.Should().BeTrue();
        childHandleCreated.Should().Be(1);
        childHandleDestroyed.Should().Be(1);
        childCreatedWhileRecreating.Should().BeTrue();
        childDestroyedWhileRecreating.Should().BeTrue();
        child.RecreatingHandle.Should().BeFalse();

        nint recreatedChildHandle = child.Handle;
        int formHandleCreated = 0;
        int formHandleDestroyed = 0;
        bool formCreatedWhileRecreating = false;
        bool formDestroyedWhileRecreating = false;
        form.HandleCreated += (_, _) =>
        {
            formHandleCreated++;
            formCreatedWhileRecreating = form.RecreatingHandle;
        };
        form.HandleDestroyed += (_, _) =>
        {
            formHandleDestroyed++;
            formDestroyedWhileRecreating = form.RecreatingHandle;
        };

        form.RecreatePortableHandle();

        form.Handle.Should().NotBe(originalFormHandle);
        form.IsHandleCreated.Should().BeTrue();
        form.Created.Should().BeTrue();
        form.Visible.Should().BeTrue();
        form.Bounds.Should().Be(new Rectangle(20, 30, 280, 180));
        form.StartPosition.Should().Be(FormStartPosition.CenterScreen);
        child.Handle.Should().Be(recreatedChildHandle);
        descendant.Handle.Should().Be(descendantHandle);
        platform.WindowsCreated.Should().Be(2);
        formHandleCreated.Should().Be(1);
        formHandleDestroyed.Should().Be(1);
        formCreatedWhileRecreating.Should().BeTrue();
        formDestroyedWhileRecreating.Should().BeTrue();
        form.RecreatingHandle.Should().BeFalse();
    }

    private static HeadlessPlatform UseHeadlessPlatform(bool autoCloseWindows)
    {
        HeadlessPlatform platform;
        if (LibrePlatform.IsRegistered)
        {
            platform = LibrePlatform.Current.Dispatcher.Should().BeOfType<HeadlessPlatform>().Subject;
            platform.Reset(autoCloseWindows);
        }
        else
        {
            platform = new HeadlessPlatform(autoCloseWindows);
            LibrePlatform.Register(platform.Services);
        }

        return platform;
    }

    private static void PumpUntilSignaled(HeadlessPlatform platform, ManualResetEventSlim completed)
    {
        SpinWait.SpinUntil(
            () =>
            {
                platform.PumpOnce();
                return completed.IsSet;
            },
            TimeSpan.FromSeconds(10)).Should().BeTrue();
    }

    private sealed class CanonicalEditingColumn : DataGridViewColumn
    {
        internal CanonicalEditingColumn()
            : base(new CanonicalEditingCell())
        {
            ValueType = typeof(string);
        }
    }

    private sealed class CanonicalEditingCell : DataGridViewTextBoxCell
    {
        internal bool Initialized { get; private set; }

        public override Type EditType => typeof(CanonicalEditingControl);

        public override void InitializeEditingControl(
            int rowIndex,
            object? initialFormattedValue,
            DataGridViewCellStyle dataGridViewCellStyle)
        {
            base.InitializeEditingControl(rowIndex, initialFormattedValue, dataGridViewCellStyle);
            Initialized = DataGridView?.EditingControl is CanonicalEditingControl;
        }
    }

    private sealed class CanonicalEditingControl : DataGridViewTextBoxEditingControl
    {
    }

    private sealed class CanonicalScaleProbeControl : Control
    {
        internal void ScaleSelected(SizeF factor, BoundsSpecified specified)
            => ScaleControl(factor, specified);
    }

    private sealed class CanonicalTreeView : TreeView
    {
        internal KeyEventArgs RaiseKey(Keys key)
        {
            KeyEventArgs eventArgs = new(key);
            OnKeyDown(eventArgs);
            return eventArgs;
        }
    }

    private static DataGridView CreateCanonicalDataGridView()
    {
        var grid = new DataGridView
        {
            Size = new Size(300, 120),
            RowHeadersWidth = 40,
            ColumnHeadersHeight = 22,
            AllowUserToAddRows = false,
        };
        grid.RowTemplate.Height = 20;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "first", Width = 80 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "second", Width = 90 });
        grid.Rows.Add("alpha", "one");
        grid.Rows.Add("beta", "two");
        grid.CurrentCell = null;
        return grid;
    }

    private static DataGridView CreateCanonicalNewRowGrid()
    {
        var grid = new DataGridView();
        grid.Columns.Add(new DataGridViewTextBoxColumn());
        return grid;
    }

    private static void AssertCanonicalNewRowInvariant(DataGridView grid)
    {
        bool shouldHavePlaceholder = grid.AllowUserToAddRows && grid.Columns.Count > 0;
        DataGridViewRow[] rows = grid.Rows.Cast<DataGridViewRow>().ToArray();

        rows.Select((row, index) => row.Index == index).Should().OnlyContain(value => value);
        rows.Should().OnlyContain(row => row.DataGridView == grid);
        rows.Should().OnlyContain(row => row.Cells.Count == grid.Columns.Count);
        if (shouldHavePlaceholder)
        {
            grid.NewRowIndex.Should().Be(grid.Rows.Count - 1);
            rows.Count(row => row.IsNewRow).Should().Be(1);
            grid.Rows[grid.NewRowIndex].IsNewRow.Should().BeTrue();
        }
        else
        {
            grid.NewRowIndex.Should().Be(-1);
            rows.Should().NotContain(row => row.IsNewRow);
        }
    }

    private sealed class InputProbeControl : Control
    {
        internal InputProbeControl()
            => SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick | ControlStyles.UserPaint, true);
    }

    private sealed class CommandProbeControl : Control
    {
        internal CommandProbeControl()
            => SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick | ControlStyles.UserPaint, true);

        internal int CommandCount { get; private set; }

        internal Keys LastKeyData { get; private set; }

        internal void ResetCommands()
        {
            CommandCount = 0;
            LastKeyData = Keys.None;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            CommandCount++;
            LastKeyData = keyData;
            return true;
        }
    }

    private sealed class RecordingMessageFilter : IMessageFilter
    {
        internal int CallCount { get; private set; }

        internal nint LastHWnd { get; private set; }

        internal Keys LastKeyCode { get; private set; }

        internal int LastMessage { get; private set; }

        public bool PreFilterMessage(ref Message message)
        {
            CallCount++;
            LastHWnd = message.HWnd;
            LastMessage = message.Msg;
            LastKeyCode = (Keys)message.WParam.ToInt32();
            return true;
        }
    }

    private sealed class CueProbeButton : Button
    {
        internal bool KeyboardCues => ShowKeyboardCues;
        internal bool FocusCues => ShowFocusCues;

        protected override bool IsInputKey(Keys keyData) => true;
    }

    private sealed class MouseDownProbeUserControl : UserControl
    {
        internal void RaiseMouseDown(MouseEventArgs e) => OnMouseDown(e);
    }

    private sealed class CenteringForm : Form
    {
        internal void CenterOnParent() => CenterToParent();

        internal void CenterOnScreen() => CenterToScreen();
    }

    private sealed class RecreatingControl : Control
    {
        internal RecreatingControl()
            => SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick | ControlStyles.UserPaint, true);

        internal void RecreatePortableHandle() => RecreateHandle();
    }

    private sealed class RecreatingForm : Form
    {
        internal void RecreatePortableHandle() => RecreateHandle();
    }

    private sealed class PaintingGroupBox : GroupBox
    {
        internal void PaintTo(Graphics graphics)
        {
            using var e = new PaintEventArgs(graphics, ClientRectangle);
            OnPaint(e);
        }
    }

    private sealed class SettingsAwareToolStrip : ToolStrip
    {
        internal int FontChangeCount { get; private set; }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            FontChangeCount++;
        }
    }

    private sealed class PaintingLinkLabel : LinkLabel
    {
        internal void PaintTo(Graphics graphics)
        {
            using var e = new PaintEventArgs(graphics, ClientRectangle);
            OnPaint(e);
        }
    }

    private sealed class TrackingDeviceContext : IDeviceContext
    {
        internal bool GetHdcCalled { get; private set; }

        public IntPtr GetHdc()
        {
            GetHdcCalled = true;
            throw new InvalidOperationException("Portable canonical text must not acquire this HDC.");
        }

        public void ReleaseHdc()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class ParentPaintingControl : Control
    {
        internal int BackgroundPaintCount { get; private set; }
        internal int ForegroundPaintCount { get; private set; }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            BackgroundPaintCount++;
            using var background = new SolidBrush(Color.CornflowerBlue);
            using var marker = new SolidBrush(Color.Orange);
            pevent.Graphics.FillRectangle(background, ClientRectangle);
            pevent.Graphics.FillRectangle(marker, new Rectangle(6, 7, 1, 1));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ForegroundPaintCount++;
        }
    }

    private sealed class ThreadLoopState
    {
        internal Thread Thread { get; set; } = null!;

        internal Control Control { get; set; } = null!;

        internal ManualResetEventSlim Ready { get; } = new(initialState: false);

        internal ManualResetEventSlim LoopStarted { get; } = new(initialState: false);

        internal int CallbackThreadId { get; set; }

        internal int _contextDisposeCount;
    }

    private sealed class TrackingApplicationContext(Action disposed) : ApplicationContext
    {
        private int _disposed;

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                disposed();
            }
        }
    }

    private sealed class ExternalWindowOwner(nint handle) : IWin32Window
    {
        public nint Handle { get; } = handle;
    }

    private sealed class HeadlessPlatform :
        ILibreDispatcher,
        ILibreThreadDispatcherProvider,
        ILibreTimerService,
        ILibreWindowService,
        ILibreExternalWindowOwnerService,
        ILibreMonitorService,
        ILibrePaintService,
        ILibreVisualStyleService,
        ILibreSystemSettingsService,
        ILibreTextRendererService,
        ILibrePowerStatusService,
        ILibreMessageBoxService,
        ILibreColorDialogService,
        ILibreFontDialogService,
        ILibreFileDialogService,
        ILibreInputLanguageService,
        ILibreDragDropService
    {
        private static readonly LibreInputLanguageDescriptor[] s_inputLanguages =
        [
            new(0x0409, "en-US", "00000409", "US"),
            new(0x0407, "de-DE", "00000407", "German"),
        ];

        private readonly ConcurrentQueue<Action> _queue = new();
        private readonly ConcurrentDictionary<int, HeadlessThreadDispatcher> _threadDispatchers = new();
        private bool _autoCloseWindows;
        private readonly Dictionary<Form, LibreHandle> _formHandles = [];
        private readonly Dictionary<nint, LibreExternalWindowOwnerState> _externalWindowOwners = [];
        private bool _exitRequested;
        private double? _initialDpiScale;
        private double? _initialFramebufferScale;
        private HeadlessWindow? _lastWindow;
        private IReadOnlyList<LibreMonitor> _monitors = CreateDefaultMonitorInventory();
        private int _dispatcherPostCount;
        private int _managedThreadId;
        private Action? _timerCallback;
        private int _timerGeneration;

        internal HeadlessPlatform(bool autoCloseWindows = true)
        {
            _autoCloseWindows = autoCloseWindows;
            _managedThreadId = Environment.CurrentManagedThreadId;
            Handles = new ManagedLibreHandleRegistry();
            Services = new LibrePlatformServices(
                this,
                this,
                Handles,
                this,
                this,
                this,
                UnsupportedLibreDesktopCaptureService.Instance,
                UnsupportedLibreNativeFontInteropService.Instance,
                UnsupportedLibreNativeGraphicsInteropService.Instance,
                this,
                this,
                this,
                this,
                this,
                this,
                this,
                this,
                this,
                this);
        }

        internal void Reset(bool autoCloseWindows)
        {
            Handles.Count.Should().Be(0);
            foreach (HeadlessThreadDispatcher dispatcher in _threadDispatchers.Values)
            {
                dispatcher.Release();
            }

            _threadDispatchers.Clear();
            _autoCloseWindows = autoCloseWindows;
            _managedThreadId = Environment.CurrentManagedThreadId;
            _exitRequested = false;
            _initialDpiScale = null;
            _initialFramebufferScale = null;
            _lastWindow = null;
            _monitors = CreateDefaultMonitorInventory();
            CaptionHeightValue = 29;
            MenuAccessKeysUnderlinedValue = true;
            _formHandles.Clear();
            _externalWindowOwners.Clear();
            DragDropHandler = null;
            DragDropTargets.Clear();
            while (_queue.TryDequeue(out _))
            {
            }

            WindowsCreated = 0;
            LastWindowBounds = default;
            LastNativeWindowBounds = default;
            LastDirtyRectangle = default;
            PresentCount = 0;
            PresentationInvalidationCount = 0;
            LastPresentationScale = 1.0;
            LastCoordinateMode = LibreWindowCoordinateMode.Logical;
            LastPaintCommandCount = 0;
            LastRetainedLayerCount = 0;
            LastRetainedLayerRepaintCount = 0;
            SawFormPaintFill = false;
            SawTranslatedChildPaintFill = false;
            CreateGraphicsCommitCount = 0;
            CreateGraphicsFlushCount = 0;
            LastCreateGraphicsFlushIntention = null;
            SawCreateGraphicsTranslatedFill = false;
            LastActivatedWindow = default;
            LastWindowOwner = default;
            ExternalOwnerDisableCount = 0;
            ExternalOwnerEnableCount = 0;
            ExternalOwnerActivateCount = 0;
            LastWindowTitle = string.Empty;
            LastWindowState = LibreWindowState.Normal;
            LastWindowTopMost = false;
            LastWindowBorder = LibreWindowBorder.Hidden;
            LastWindowShowInTaskbar = true;
            LastWindowCanClose = true;
            LastWindowCanMinimize = true;
            LastWindowCanMaximize = true;
            LastWindowOpacity = 1d;
            LastWindowZOrder = null;
            WindowZOrderChangeCount = 0;
            LastCursorShape = null;
            CursorChangeCount = 0;
            LastWindowIcons = [];
            VisualStyleDrawCount = 0;
            VisualStyleEdgeDrawCount = 0;
            VisualStyleTextDrawCount = 0;
            TextDrawCount = 0;
            TextMeasureCount = 0;
            LastTextBounds = default;
            LastTextFormat = default;
            LastMeasuredText = string.Empty;
            _timerCallback = null;
            _timerGeneration = 0;
            TimerStartCount = 0;
            TimerStopCount = 0;
            LastTimerInterval = default;
            LastTimerRepeating = false;
            NextMessageBoxResult = LibreMessageBoxResult.OK;
            LastMessageBoxRequest = null;
            MessageBoxShowCount = 0;
            MessageBoxOwnerDisabledDuringShow = false;
            NextColorDialogResult = new LibreColorDialogResult(true, Color.Black, []);
            LastColorDialogRequest = null;
            ColorDialogShowCount = 0;
            ColorDialogOwnerDisabledDuringShow = false;
            InvokeColorDialogHelp = false;
            NextFontDialogResult = new LibreFontDialogResult(
                true,
                new(FontFamily.GenericSansSerif.Name, 9, FontStyle.Regular, 1, false, Color.Black));
            AppliedFontDialogSelection = null;
            LastFontDialogRequest = null;
            FontDialogShowCount = 0;
            FontDialogOwnerDisabledDuringShow = false;
            InvokeFontDialogApply = false;
            InvokeFontDialogHelp = false;
            _fileDialogResults.Clear();
            LastFileDialogRequest = null;
            FileDialogShowCount = 0;
            FileDialogOwnerDisabledDuringShow = false;
            InvokeFileDialogHelp = false;
            _currentInputLanguageToken = s_inputLanguages[0].Token;
            InputLanguageActivationCount = 0;
            Volatile.Write(ref _dispatcherPostCount, 0);
        }

        internal ManagedLibreHandleRegistry Handles { get; }

        internal Func<LibreDragDropRequest, ILibreDragDropSession, LibreDragDropEffects>? DragDropHandler { get; set; }

        internal HashSet<LibreHandle> DragDropTargets { get; } = [];

        public event EventHandler<LibreSystemSettingsChangedEventArgs>? SettingsChanged;

        internal void RaiseSettingsChanged(LibreSystemSettingsChangeKind kind)
            => SettingsChanged?.Invoke(this, new(kind));

        internal LibrePlatformServices Services { get; }

        private nint _currentInputLanguageToken = s_inputLanguages[0].Token;

        public LibreInputLanguageDescriptor Current
            => s_inputLanguages.Single(language => language.Token == _currentInputLanguageToken);

        public LibreInputLanguageDescriptor Default => s_inputLanguages[0];

        public IReadOnlyList<LibreInputLanguageDescriptor> Installed => s_inputLanguages;

        internal int InputLanguageActivationCount { get; private set; }

        public bool TryGet(nint token, out LibreInputLanguageDescriptor descriptor)
        {
            foreach (LibreInputLanguageDescriptor language in s_inputLanguages)
            {
                if (language.Token == token)
                {
                    descriptor = language;
                    return true;
                }
            }

            descriptor = null!;
            return false;
        }

        public bool TryActivate(nint token)
        {
            if (!TryGet(token, out _))
            {
                return false;
            }

            _currentInputLanguageToken = token;
            InputLanguageActivationCount++;
            return true;
        }

        internal int WindowsCreated { get; private set; }

        internal int CaptionHeightValue { get; set; } = 29;

        internal LibreRectangle LastWindowBounds { get; private set; }

        internal LibreRectangle LastNativeWindowBounds { get; private set; }

        internal LibreRectangle LastDirtyRectangle { get; private set; }

        internal int PresentCount { get; private set; }

        internal int TimerStartCount { get; private set; }

        internal int TimerStopCount { get; private set; }

        internal TimeSpan LastTimerInterval { get; private set; }

        internal bool LastTimerRepeating { get; private set; }

        internal bool HasActiveTimer => _timerCallback is not null;

        internal LibreMessageBoxResult NextMessageBoxResult { get; set; } = LibreMessageBoxResult.OK;

        internal LibreMessageBoxRequest? LastMessageBoxRequest { get; private set; }

        internal int MessageBoxShowCount { get; private set; }

        internal bool MessageBoxOwnerDisabledDuringShow { get; private set; }

        internal LibreColorDialogResult NextColorDialogResult { get; set; }
            = new(true, Color.Black, []);

        internal LibreColorDialogRequest? LastColorDialogRequest { get; private set; }

        internal int ColorDialogShowCount { get; private set; }

        internal bool ColorDialogOwnerDisabledDuringShow { get; private set; }

        internal bool InvokeColorDialogHelp { get; set; }

        internal LibreFontDialogResult NextFontDialogResult { get; set; }

        internal LibreFontDialogSelection? AppliedFontDialogSelection { get; set; }

        internal LibreFontDialogRequest? LastFontDialogRequest { get; private set; }

        internal int FontDialogShowCount { get; private set; }

        internal bool FontDialogOwnerDisabledDuringShow { get; private set; }

        internal bool InvokeFontDialogApply { get; set; }

        internal bool InvokeFontDialogHelp { get; set; }

        private readonly Queue<LibreFileDialogResult> _fileDialogResults = new();

        internal LibreFileDialogRequest? LastFileDialogRequest { get; private set; }

        internal int FileDialogShowCount { get; private set; }

        internal bool FileDialogOwnerDisabledDuringShow { get; private set; }

        internal bool InvokeFileDialogHelp { get; set; }

        internal void QueueFileDialogResult(LibreFileDialogResult result)
            => _fileDialogResults.Enqueue(result);

        internal int PresentationInvalidationCount { get; private set; }

        internal int VisualStyleDrawCount { get; private set; }
        internal int VisualStyleEdgeDrawCount { get; private set; }
        internal int VisualStyleTextDrawCount { get; private set; }
        internal int TextDrawCount { get; private set; }
        internal int TextMeasureCount { get; private set; }
        internal Rectangle LastTextBounds { get; private set; }
        internal LibreTextFormat LastTextFormat { get; private set; }
        internal string LastMeasuredText { get; private set; } = string.Empty;

        public bool HighContrast => false;
        public Font GetMenuFont(int dpi)
            => new(FontFamily.GenericMonospace, dpi == 0 ? 11f : 17f);
        public LibreSize BorderSize => new(11, 13);
        public LibreSize FixedFrameBorderSize => new(3, 3);
        public LibreSize Border3DSize => new(2, 2);
        public int VerticalScrollBarWidth => 17;
        public int HorizontalScrollBarHeight => 17;
        public int CaptionHeight => CaptionHeightValue;
        public int MenuHeight => 31;
        public LibreSize MinWindowTrackSize => new(140, 52);
        public LibreSize IconSize => new(33, 35);
        public LibreSize CursorSize => new(37, 39);
        public LibreSize SmallIconSize => new(17, 19);
        public LibreSize MinimumWindowSize => new(101, 102);
        public LibreSize CaptionButtonSize => new(33, 34);
        public LibreSize FrameBorderSize => new(7, 8);
        public LibreSize MaxWindowTrackSize => new(1600, 1200);
        public LibreSize PrimaryMonitorMaximizedWindowSize => new(1500, 1100);
        public LibreSize MinimizedWindowSpacingSize => new(201, 202);
        public int ToolWindowCaptionHeight => 43;
        public LibreSize ToolWindowCaptionButtonSize => new(45, 46);
        public LibreSize MenuButtonSize => new(47, 48);
        public LibreSize MinimizedWindowSize => new(203, 204);
        public int KanjiWindowHeight => 41;
        public bool DebugOperatingSystem => true;
        public bool RightAlignedMenus => true;
        public bool PenWindows => true;
        public bool DbcsEnabled => true;
        public bool Secure => true;
        public bool Network => false;
        public bool TerminalServerSession => true;
        public LibreBootMode BootMode => LibreBootMode.FailSafeWithNetwork;
        public bool ShowSounds => true;
        public LibreSize MenuCheckSize => new(27, 29);
        public bool MidEastEnabled => true;
        public LibreMinimizedWindowStartPosition MinimizedWindowStartPosition
            => LibreMinimizedWindowStartPosition.TopRight;
        public LibreMinimizedWindowDirection MinimizedWindowDirection => LibreMinimizedWindowDirection.Up;
        public bool HideMinimizedWindows => true;
        public LibreScreenOrientation ScreenOrientation => LibreScreenOrientation.Angle270;
        public int SizingBorderWidth => 7;
        public LibreSize SmallCaptionButtonSize => new(31, 33);
        public LibreSize MenuBarButtonSize => new(35, 37);
        public bool LockedTerminalSession => true;
        public LibrePowerStatusSnapshot GetCurrentStatus()
            => new(
                LibrePowerLineStatus.Online,
                LibreBatteryChargeStatus.Low | LibreBatteryChargeStatus.Charging,
                7200,
                0.42f,
                1800);

        public LibreMessageBoxResult Show(in LibreMessageBoxRequest request)
        {
            LastMessageBoxRequest = request;
            MessageBoxShowCount++;
            MessageBoxOwnerDisabledDuringShow = request.Owner.IsNull
                || (Handles.TryGet(request.Owner, out ILibreWindow? owner) && !owner.Enabled);
            return NextMessageBoxResult;
        }

        public LibreColorDialogResult Show(in LibreColorDialogRequest request)
        {
            LastColorDialogRequest = request;
            ColorDialogShowCount++;
            ColorDialogOwnerDisabledDuringShow = request.Owner.IsNull
                || (Handles.TryGet(request.Owner, out ILibreWindow? owner) && !owner.Enabled);
            if (InvokeColorDialogHelp)
            {
                request.HelpRequested?.Invoke();
            }

            return NextColorDialogResult;
        }

        public LibreFontDialogResult Show(in LibreFontDialogRequest request)
        {
            LastFontDialogRequest = request;
            FontDialogShowCount++;
            FontDialogOwnerDisabledDuringShow = request.Owner.IsNull
                || (Handles.TryGet(request.Owner, out ILibreWindow? owner) && !owner.Enabled);
            if (InvokeFontDialogApply && AppliedFontDialogSelection is { } selection)
            {
                request.ApplyRequested?.Invoke(selection);
            }

            if (InvokeFontDialogHelp)
            {
                request.HelpRequested?.Invoke();
            }

            return NextFontDialogResult;
        }

        public LibreFileDialogResult Show(in LibreFileDialogRequest request)
        {
            LastFileDialogRequest = request;
            FileDialogShowCount++;
            FileDialogOwnerDisabledDuringShow = request.Owner.IsNull
                || (Handles.TryGet(request.Owner, out ILibreWindow? owner) && !owner.Enabled);
            if (InvokeFileDialogHelp)
            {
                request.HelpRequested?.Invoke();
            }

            return _fileDialogResults.Count == 0
                ? new LibreFileDialogResult(false, request.SelectedPaths.ToArray(), request.FilterIndex, false)
                : _fileDialogResults.Dequeue();
        }

        public int VerticalScrollBarArrowHeight => 17;
        public int HorizontalScrollBarArrowWidth => 17;
        public int VerticalScrollBarThumbHeight => 17;
        public int HorizontalScrollBarThumbWidth => 17;
        public LibreSize DragSize => new(4, 4);
        public bool MousePresent => true;
        public bool MouseButtonsSwapped => true;
        public int MouseButtons => 5;
        public LibreSize DoubleClickSize => new(12, 14);
        public int DoubleClickTime => 650;
        public bool MouseWheelPresent => false;
        public int CaretBlinkTime => 725;
        public int MouseWheelScrollLines => 7;
        internal bool MenuAccessKeysUnderlinedValue { get; set; } = true;
        public bool MenuAccessKeysUnderlined => MenuAccessKeysUnderlinedValue;
        public int KeyboardDelay => 2;
        public bool KeyboardPreferred => true;
        public int KeyboardSpeed => 23;
        public LibreSize MouseHoverSize => new(13, 15);
        public int MouseHoverTime => 640;
        public int MouseSpeed => 14;
        public bool SnapToDefaultButton => true;
        public bool DragFullWindows => false;
        public bool DropShadowEnabled => false;
        public bool FlatMenuEnabled => true;
        public bool PopupMenusLeftAligned => false;
        public bool MenuFadeEnabled => false;
        public int MenuShowDelay => 275;
        public bool ComboBoxAnimationEnabled => true;
        public bool TitleBarGradientEnabled => false;
        public bool HotTrackingEnabled => true;
        public bool ListBoxSmoothScrollingEnabled => false;
        public bool MenuAnimationEnabled => true;
        public bool SelectionFadeEnabled => false;
        public bool ToolTipAnimationEnabled => true;
        public bool UIEffectsEnabled => false;
        public bool ActiveWindowTrackingEnabled => true;
        public int ActiveWindowTrackingDelay => 525;
        public bool MinimizeRestoreAnimationEnabled => true;
        public int BorderMultiplierFactor => 3;
        public int CaretWidth => 5;
        public int VerticalFocusThickness => 6;
        public int HorizontalFocusThickness => 7;
        public int VerticalResizeBorderThickness => 8;
        public int HorizontalResizeBorderThickness => 9;
        public bool FontSmoothingEnabled => false;
        public int FontSmoothingContrast => 1700;
        public int FontSmoothingType => 1;
        public int IconHorizontalSpacing => 81;
        public int IconVerticalSpacing => 83;
        public bool IconTitleWrappingEnabled => false;

        public string ThemeFilename => "managed.theme";
        public string ColorScheme => "ManagedColor";
        public string ThemeSize => "ManagedSize";
        public string DisplayName => "Managed theme";
        public string Company => "Managed company";
        public string Author => "Managed author";
        public string Copyright => "Managed copyright";
        public string Url => "https://managed.test";
        public string Version => "Managed version";
        public string Description => "Managed description";
        public bool SupportsFlatMenus => true;
        public int MinimumColorDepth => 30;

        internal double LastPresentationScale { get; private set; } = 1.0;

        internal LibreWindowCoordinateMode LastCoordinateMode { get; private set; }

        internal int LastPaintCommandCount { get; private set; }

        internal int LastRetainedLayerCount { get; private set; }

        internal int LastRetainedLayerRepaintCount { get; private set; }

        internal bool SawFormPaintFill { get; private set; }

        internal bool SawTranslatedChildPaintFill { get; private set; }

        internal int CreateGraphicsCommitCount { get; private set; }

        internal int CreateGraphicsFlushCount { get; private set; }

        internal FlushIntention? LastCreateGraphicsFlushIntention { get; private set; }

        internal bool SawCreateGraphicsTranslatedFill { get; private set; }

        internal LibreHandle LastActivatedWindow { get; private set; }

        internal LibreHandle LastWindowOwner { get; private set; }

        internal int ExternalOwnerDisableCount { get; private set; }

        internal int ExternalOwnerEnableCount { get; private set; }

        internal int ExternalOwnerActivateCount { get; private set; }

        internal string LastWindowTitle { get; private set; } = string.Empty;

        internal LibreWindowState LastWindowState { get; private set; }

        internal bool LastWindowTopMost { get; private set; }

        internal LibreWindowBorder LastWindowBorder { get; private set; }

        internal bool LastWindowShowInTaskbar { get; private set; }

        internal bool LastWindowCanClose { get; private set; }

        internal bool LastWindowCanMinimize { get; private set; }

        internal bool LastWindowCanMaximize { get; private set; }

        internal double LastWindowOpacity { get; private set; }

        internal LibreWindowZOrder? LastWindowZOrder { get; private set; }

        internal int WindowZOrderChangeCount { get; private set; }

        internal LibreCursorShape? LastCursorShape { get; private set; }

        internal int CursorChangeCount { get; private set; }

        internal LibreSize LastWindowMinimumSize { get; private set; }

        internal LibreSize LastWindowMaximumSize { get; private set; }

        internal IReadOnlyList<LibreWindowIcon> LastWindowIcons { get; private set; } = [];

        internal void ChangeLastWindowState(LibreWindowState state)
        {
            _lastWindow.Should().NotBeNull();
            _lastWindow!.State = state;
        }

        internal void SetMonitors(params LibreMonitor[] monitors)
        {
            monitors.Should().NotBeEmpty();
            _monitors = monitors;
        }

        internal void SetInitialPresentationScales(double dpiScale, double framebufferScale)
        {
            _initialDpiScale = dpiScale;
            _initialFramebufferScale = framebufferScale;
        }

        public int ManagedThreadId => _managedThreadId;

        public bool CheckAccess() => Environment.CurrentManagedThreadId == _managedThreadId;

        public ILibreDispatcher GetForCurrentThread()
        {
            int threadId = Environment.CurrentManagedThreadId;
            return threadId == _managedThreadId
                ? this
                : _threadDispatchers.GetOrAdd(
                    threadId,
                    id => new HeadlessThreadDispatcher(
                        id,
                        () => Interlocked.Increment(ref _dispatcherPostCount)));
        }

        public void Release(ILibreDispatcher dispatcher)
        {
            if (ReferenceEquals(dispatcher, this))
            {
                return;
            }

            if (dispatcher is not HeadlessThreadDispatcher threadDispatcher
                || !_threadDispatchers.TryRemove(threadDispatcher.ManagedThreadId, out HeadlessThreadDispatcher? removed)
                || !ReferenceEquals(threadDispatcher, removed))
            {
                throw new ArgumentException("The dispatcher was not created by this provider.", nameof(dispatcher));
            }

            threadDispatcher.Release();
        }

        internal ILibreDispatcher WaitForThreadDispatcher(int threadId)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                if (_threadDispatchers.TryGetValue(threadId, out HeadlessThreadDispatcher? dispatcher))
                {
                    return dispatcher;
                }

                Thread.Yield();
            }

            throw new TimeoutException($"Thread dispatcher {threadId} was not created.");
        }

        public void Post(Action callback)
        {
            Interlocked.Increment(ref _dispatcherPostCount);
            _queue.Enqueue(callback);
        }

        public void Send(Action callback) => callback();

        public void PumpOnce()
        {
            if (_queue.TryDequeue(out Action? callback))
            {
                callback();
            }
        }

        public void Run(CancellationToken cancellationToken)
        {
            for (int iterations = 0; !_exitRequested && !cancellationToken.IsCancellationRequested; iterations++)
            {
                if (iterations >= 100)
                {
                    throw new InvalidOperationException("The canonical lifecycle did not terminate its dispatcher loop.");
                }

                PumpOnce();
            }
        }

        public void RunNested(Func<bool> continueCondition, CancellationToken cancellationToken)
        {
            for (int iterations = 0; continueCondition() && !cancellationToken.IsCancellationRequested; iterations++)
            {
                if (iterations >= 100)
                {
                    throw new InvalidOperationException("The canonical nested modal loop did not terminate.");
                }

                PumpOnce();
            }
        }

        public void RequestExit() => _exitRequested = true;

        public bool IsSupported => true;

        public void SetTargetEnabled(LibreHandle target, bool enabled)
        {
            if (enabled)
            {
                DragDropTargets.Add(target);
            }
            else
            {
                DragDropTargets.Remove(target);
            }
        }

        public LibreDragDropEffects DoDragDrop(
            LibreDragDropRequest request,
            ILibreDragDropSession session)
            => DragDropHandler?.Invoke(request, session) ?? LibreDragDropEffects.None;

        internal int DispatcherPostCount => Volatile.Read(ref _dispatcherPostCount);

        public IDisposable Start(TimeSpan interval, bool repeating, Action callback)
        {
            TimerStartCount++;
            LastTimerInterval = interval;
            LastTimerRepeating = repeating;
            _timerCallback = callback;
            int generation = ++_timerGeneration;
            return new HeadlessTimerRegistration(this, generation);
        }

        internal void FireTimer()
            => (_timerCallback ?? throw new InvalidOperationException("No headless timer is active."))();

        private void StopTimer(int generation)
        {
            TimerStopCount++;
            if (generation != _timerGeneration)
            {
                return;
            }

            _timerCallback = null;
        }

        public ILibreWindow Create(in LibreWindowCreateOptions options, ILibreWindowEvents events)
        {
            WindowsCreated++;
            LastCoordinateMode = options.CoordinateMode;
            LastWindowOwner = options.Owner;
            _lastWindow = new HeadlessWindow(this, options, events);
            return _lastWindow;
        }

        public bool IsLive(LibreHandle owner)
            => owner.Kind == LibreHandleKind.Window
                && _externalWindowOwners.ContainsKey(owner.Value);

        public bool TryGetState(
            LibreHandle owner,
            out LibreExternalWindowOwnerState state)
        {
            state = default;
            return owner.Kind == LibreHandleKind.Window
                && _externalWindowOwners.TryGetValue(owner.Value, out state);
        }

        public bool TrySetEnabled(LibreHandle owner, bool enabled)
        {
            if (!TryGetState(owner, out LibreExternalWindowOwnerState state))
            {
                return false;
            }

            _externalWindowOwners[owner.Value] = state with { IsEnabled = enabled };
            if (enabled)
            {
                ExternalOwnerEnableCount++;
            }
            else
            {
                ExternalOwnerDisableCount++;
            }

            return true;
        }

        public bool TryActivate(LibreHandle owner)
        {
            if (!TryGetState(owner, out LibreExternalWindowOwnerState state)
                || !state.IsVisible)
            {
                return false;
            }

            ExternalOwnerActivateCount++;
            return true;
        }

        internal void RegisterExternalWindowOwner(nint handle)
            => _externalWindowOwners.Add(
                handle,
                new LibreExternalWindowOwnerState(IsVisible: true, IsEnabled: true));

        internal LibreExternalWindowOwnerState GetExternalOwnerState(nint handle)
            => _externalWindowOwners[handle];

        internal void TrackForm(Form form)
            => _formHandles[form] = GetWindowHandle(form);

        internal LibreHandle GetWindowHandle(Form form)
            => new(form.Handle, LibreHandleKind.Window);

        internal LibreHandle GetFormerWindowHandle(Form form)
            => _formHandles[form];

        internal bool IsWindowEnabled(Form form)
        {
            Handles.TryGet(GetWindowHandle(form), out HeadlessWindow? window).Should().BeTrue();
            return window!.Enabled;
        }

        internal LibreHandle GetWindowOwner(Form form)
        {
            Handles.TryGet(GetWindowHandle(form), out HeadlessWindow? window).Should().BeTrue();
            return window!.Owner;
        }

        internal void SendInput(
            LibreInputEventKind kind,
            LibreInputModifiers modifiers = LibreInputModifiers.None,
            LibreKey key = LibreKey.Unknown,
            string? text = null,
            LibrePoint position = default,
            LibrePoint delta = default,
            LibrePointerButton button = LibrePointerButton.None)
        {
            _lastWindow.Should().NotBeNull();
            _lastWindow!.SendInput(new LibreInputEvent(kind, 1, modifiers, key, text, position, delta, button));
        }

        internal void SetPresentationScale(double scale)
            => SetPresentationScales(scale, scale);

        internal void SetPresentationScales(double dpiScale, double framebufferScale)
        {
            _lastWindow.Should().NotBeNull();
            _lastWindow!.SetPresentationScales(dpiScale, framebufferScale);
            LastPresentationScale = dpiScale;
        }

        public IReadOnlyList<LibreMonitor> GetMonitors()
            => _monitors;

        public LibreMonitor GetNearest(LibreRectangle bounds)
            => LibreMonitorSelection.GetNearest(_monitors, bounds);

        public Graphics CreateGraphics(
            LibreHandle target,
            LibrePoint origin,
            LibreRectangle clipRectangle)
        {
            if (Handles.TryGet(target, out HeadlessWindow? window))
            {
                return window.CreateGraphics(origin, clipRectangle);
            }

            Handles.TryGet<object>(target, out _).Should().BeTrue();
            DrawingContext recording = new();
            Graphics graphics = Graphics.FromProGpuDrawingContext(
                recording,
                new RectangleF(
                    clipRectangle.X,
                    clipRectangle.Y,
                    clipRectangle.Width,
                    clipRectangle.Height),
                Matrix4x4.CreateTranslation(origin.X, origin.Y, 0f),
                () => recording.Clear());
            graphics.SetClip(new RectangleF(
                clipRectangle.X - origin.X,
                clipRectangle.Y - origin.Y,
                clipRectangle.Width,
                clipRectangle.Height));
            return graphics;
        }

        private static IReadOnlyList<LibreMonitor> CreateDefaultMonitorInventory()
            => [new("headless", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 1, true)];

        public void Invalidate(LibreHandle target, LibreRectangle dirtyRectangle)
        {
            Handles.TryGet(target, out HeadlessWindow? window).Should().BeTrue();
            LastDirtyRectangle = dirtyRectangle;
            window!.RequestPaint(dirtyRectangle);
        }

        public void InvalidateAll(LibreHandle target)
        {
            Handles.TryGet(target, out HeadlessWindow? window).Should().BeTrue();
            PresentationInvalidationCount++;
            LibreRectangle bounds = window!.Bounds;
            window.RequestPaint(new LibreRectangle(0, 0, bounds.Width, bounds.Height));
        }

        public void Present(LibreHandle target)
        {
            Handles.TryGet(target, out HeadlessWindow? window).Should().BeTrue();
            PresentCount++;
            window!.PresentPendingPaint();
        }

        public bool IsEnabled => true;

        public bool IsElementDefined(string className, int part)
            => !string.IsNullOrWhiteSpace(className) && part >= 0;

        public void DrawBackground(
            Graphics graphics,
            string className,
            int part,
            int state,
            Rectangle bounds,
            Rectangle? clipRectangle)
        {
            VisualStyleDrawCount++;
            GraphicsState saved = graphics.Save();
            try
            {
                if (clipRectangle is Rectangle clip)
                {
                    graphics.SetClip(clip, CombineMode.Intersect);
                }

                using var brush = new SolidBrush(Color.Purple);
                graphics.FillRectangle(brush, bounds);
            }
            finally
            {
                graphics.Restore(saved);
            }
        }

        public Region? GetBackgroundRegion(string className, int part, int state, Rectangle bounds)
            => new(bounds);

        public Rectangle GetBackgroundContentRectangle(string className, int part, int state, Rectangle bounds)
            => Rectangle.Inflate(bounds, -2, -2);

        public Rectangle GetBackgroundExtent(string className, int part, int state, Rectangle contentBounds)
        {
            contentBounds.Should().Be(new Rectangle(1, 2, 30, 12));
            return new Rectangle(8, 9, 40, 22);
        }

        public Size GetPartSize(
            string className,
            int part,
            int state,
            Rectangle? bounds,
            LibreVisualStyleSizeType type)
            => new(21, 22);

        public Color GetColor(
            string className,
            int part,
            int state,
            LibreVisualStyleColorProperty property)
            => Color.Orange;

        public int GetInteger(
            string className,
            int part,
            int state,
            LibreVisualStyleIntegerProperty property)
            => property == LibreVisualStyleIntegerProperty.ProgressChunkSize ? 7 : 3;

        public bool GetBoolean(
            string className,
            int part,
            int state,
            LibreVisualStyleBooleanProperty property)
        {
            property.Should().Be(LibreVisualStyleBooleanProperty.BackgroundFill);
            return true;
        }

        public int GetEnumValue(
            string className,
            int part,
            int state,
            LibreVisualStyleEnumProperty property)
        {
            property.Should().Be(LibreVisualStyleEnumProperty.BackgroundType);
            return 1;
        }

        public string GetFilename(
            string className,
            int part,
            int state,
            LibreVisualStyleFilenameProperty property)
        {
            property.Should().Be(LibreVisualStyleFilenameProperty.ImageFile);
            return "managed-theme-image";
        }

        public string GetString(
            string className,
            int part,
            int state,
            LibreVisualStyleStringProperty property)
        {
            property.Should().Be(LibreVisualStyleStringProperty.Text);
            return "managed-theme-text";
        }

        public Font? GetFont(
            string className,
            int part,
            int state,
            LibreVisualStyleFontProperty property)
        {
            property.Should().Be(LibreVisualStyleFontProperty.Text);
            return new Font(SystemFonts.DefaultFont.FontFamily, 10f);
        }

        public Rectangle MeasureText(
            Graphics graphics,
            string className,
            int part,
            int state,
            Rectangle? bounds,
            string text,
            LibreVisualStyleTextFormat format)
        {
            bounds.Should().Be(new Rectangle(1, 2, 30, 12));
            text.Should().Be("measure");
            format.Should().Be(LibreVisualStyleTextFormat.Right | LibreVisualStyleTextFormat.VerticalCenter);
            return new Rectangle(6, 7, 8, 9);
        }

        public LibreVisualStyleHitTestCode HitTestBackground(
            Graphics graphics,
            string className,
            int part,
            int state,
            Rectangle bounds,
            Region? region,
            Point point,
            LibreVisualStyleHitTestOptions options)
        {
            bounds.Should().Be(new Rectangle(1, 2, 30, 12));
            point.Should().Be(new Point(2, 3));
            if (region is null)
            {
                options.Should().Be(LibreVisualStyleHitTestOptions.ResizingBorderLeft);
                return LibreVisualStyleHitTestCode.Left;
            }

            options.Should().Be(LibreVisualStyleHitTestOptions.ResizingBorderRight);
            region.IsVisible(point, graphics).Should().BeTrue();
            return LibreVisualStyleHitTestCode.Right;
        }

        public LibreVisualStyleTextMetrics GetTextMetrics(
            Graphics graphics,
            string className,
            int part,
            int state)
            => new(
                Height: 20,
                Ascent: 14,
                Descent: 4,
                InternalLeading: 1,
                ExternalLeading: 1,
                AverageCharWidth: 7,
                MaxCharWidth: 12,
                Weight: 600,
                Overhang: 0,
                DigitizedAspectX: 96,
                DigitizedAspectY: 96,
                FirstChar: ' ',
                LastChar: '~',
                DefaultChar: '?',
                BreakChar: ' ',
                Italic: true,
                Underlined: true,
                StruckOut: false,
                PitchAndFamily: LibreVisualStyleTextPitchAndFamily.FixedPitch
                    | LibreVisualStyleTextPitchAndFamily.TrueType,
                CharacterSet: LibreVisualStyleTextCharacterSet.Baltic);

        public LibreVisualStyleMargins GetMargins(
            string className,
            int part,
            int state,
            LibreVisualStyleMarginProperty property)
        {
            property.Should().Be(LibreVisualStyleMarginProperty.Content);
            return new LibreVisualStyleMargins(4, 5, 6, 7);
        }

        public Point GetPoint(
            string className,
            int part,
            int state,
            LibreVisualStylePointProperty property)
        {
            property.Should().Be(LibreVisualStylePointProperty.TextShadowOffset);
            return new Point(2, 3);
        }

        public bool IsBackgroundPartiallyTransparent(string className, int part, int state)
            => false;

        public Rectangle DrawEdge(
            Graphics graphics,
            string className,
            int part,
            int state,
            Rectangle bounds,
            LibreVisualStyleEdges edges,
            LibreVisualStyleEdgeStyle style,
            LibreVisualStyleEdgeEffects effects)
        {
            VisualStyleEdgeDrawCount++;
            return Rectangle.FromLTRB(
                bounds.Left + (edges.HasFlag(LibreVisualStyleEdges.Left) ? 1 : 0),
                bounds.Top + (edges.HasFlag(LibreVisualStyleEdges.Top) ? 1 : 0),
                bounds.Right - (edges.HasFlag(LibreVisualStyleEdges.Right) ? 1 : 0),
                bounds.Bottom - (edges.HasFlag(LibreVisualStyleEdges.Bottom) ? 1 : 0));
        }

        public void DrawText(
            Graphics graphics,
            string className,
            int part,
            int state,
            Rectangle bounds,
            string text,
            bool disabled,
            LibreVisualStyleTextFormat format)
        {
            VisualStyleTextDrawCount++;
            text.Should().Be("text");
            format.Should().Be(
                LibreVisualStyleTextFormat.HorizontalCenter | LibreVisualStyleTextFormat.VerticalCenter);
        }

        public void DrawText(
            Graphics graphics,
            string text,
            Font? font,
            Rectangle bounds,
            Color foreColor,
            Color backColor,
            LibreTextFormat format)
        {
            TextDrawCount++;
            font.Should().NotBeNull();
            if (text == "portable")
            {
                bounds.Should().Be(new Rectangle(4, 5, 60, 18));
                foreColor.Should().Be(Color.Navy);
                backColor.Should().Be(Color.Beige);
                format.Should().Be(
                    LibreTextFormat.HorizontalCenter
                        | LibreTextFormat.VerticalCenter
                        | LibreTextFormat.SingleLine
                        | LibreTextFormat.NoPadding
                        | LibreTextFormat.TextBoxControl);
            }
            else if (text == "disabled")
            {
                bounds.Should().BeOneOf(new Rectangle(5, 6, 60, 18), new Rectangle(4, 5, 60, 18));
                backColor.Should().Be(Color.Empty);
                format.Should().Be(LibreTextFormat.SingleLine | LibreTextFormat.NoPadding);
            }
            else
            {
                text.Should().BeOneOf("group", "link");
                bounds.Width.Should().BeGreaterThan(0);
                bounds.Height.Should().BeGreaterThan(0);
            }

            LastTextBounds = bounds;
            LastTextFormat = format;
            using var marker = new SolidBrush(foreColor);
            graphics.FillRectangle(marker, bounds.X, bounds.Y, 1, 1);
        }

        public Size MeasureText(
            Graphics? graphics,
            string text,
            Font? font,
            Size proposedSize,
            LibreTextFormat format)
        {
            TextMeasureCount++;
            font.Should().NotBeNull();
            LastTextFormat = format;
            LastMeasuredText = text;
            if (text == "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ")
            {
                graphics.Should().BeNull();
                proposedSize.Should().Be(new Size(int.MaxValue, int.MaxValue));
                format.Should().Be(LibreTextFormat.SingleLine | LibreTextFormat.NoPadding);
                return new Size(416, font!.Height);
            }

            if (text == "0")
            {
                graphics.Should().BeNull();
                proposedSize.Should().Be(new Size(int.MaxValue, int.MaxValue));
                format.Should().Be(LibreTextFormat.SingleLine | LibreTextFormat.NoPadding);
                return new Size(8, font!.Height);
            }

            if (text == "j^")
            {
                graphics.Should().BeNull();
                proposedSize.Should().Be(new Size(short.MaxValue, (int)(font!.Height * 1.25)));
                format.Should().Be(LibreTextFormat.SingleLine);
                return new Size(12, font.Height);
            }

            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out _))
            {
                graphics.Should().BeNull();
                proposedSize.Should().Be(new Size(int.MaxValue, int.MaxValue));
                format.Should().Be(LibreTextFormat.SingleLine | LibreTextFormat.NoPadding);
                return new Size(72, font!.Height);
            }

            if (text is "button" or "check" or "radio")
            {
                graphics.Should().BeNull();
                format.Should().HaveFlag(LibreTextFormat.TextBoxControl);
                return new Size(text.Length * 7, font!.Height);
            }

            if (text is "wrapped DataGridView text" or "first" or "second" or "alpha" or "one" or "beta" or "two" or " ")
            {
                int availableWidth = proposedSize.Width is > 0 and < int.MaxValue
                    ? proposedSize.Width
                    : text.Length * 7;
                int lineCount = Math.Max(1, (text.Length * 7 + availableWidth - 1) / availableWidth);
                return new Size(Math.Min(text.Length * 7, availableWidth), font!.Height * lineCount);
            }

            if (text is "group" or "link")
            {
                proposedSize.Width.Should().BeGreaterThan(0);
                return new Size(text.Length * 7, font!.Height);
            }

            if (graphics is null
                && proposedSize == Size.Empty
                && format.HasFlag(LibreTextFormat.NoPadding)
                && format.HasFlag(LibreTextFormat.NoPrefix))
            {
                return new Size(Math.Max(1, text.Length * 7), font!.Height);
            }

            if (graphics is null && text != "headless")
            {
                int availableWidth = proposedSize.Width is > 0 and < int.MaxValue
                    ? proposedSize.Width
                    : Math.Max(1, text.Length * 7);
                int width = Math.Min(Math.Max(1, text.Length * 7), availableWidth);
                int lineCount = Math.Max(1, (Math.Max(1, text.Length * 7) + availableWidth - 1) / availableWidth);
                return new Size(width, font!.Height * lineCount);
            }

            if (graphics is null)
            {
                text.Should().Be("headless");
                proposedSize.Should().Be(new Size(70, 30));
                format.Should().Be(LibreTextFormat.SingleLine | LibreTextFormat.NoPadding);
                return new Size(31, 17);
            }

            text.Should().Be("managed");
            proposedSize.Should().Be(new Size(80, 40));
            format.Should().Be(LibreTextFormat.WordBreak | LibreTextFormat.LeftAndRightPadding);
            return new Size(37, 19);
        }

        private sealed class HeadlessWindow : ILibreWindow
        {
            private readonly HeadlessPlatform _platform;
            private readonly ILibreWindowEvents _events;
            private readonly LibreWindowCoordinateMode _coordinateMode;
            private readonly DrawingContext _retainedContext = new();
            private readonly Dictionary<LibreHandle, HeadlessRetainedLayer> _retainedLayers = [];
            private bool _disposed;
            private bool _paintQueued;
            private LibreRectangle _dirtyRectangle;
            private double _dpiScale;
            private double _framebufferScale;
            private LibreRectangle _nativeBounds;
            private string _title = string.Empty;
            private LibreWindowState _state;
            private bool _topMost;
            private LibreWindowBorder _border;
            private bool _showInTaskbar;
            private bool _canClose;
            private bool _canMinimize;
            private bool _canMaximize;
            private double _opacity = 1d;

            internal HeadlessWindow(
                HeadlessPlatform platform,
                in LibreWindowCreateOptions options,
                ILibreWindowEvents events)
            {
                _platform = platform;
                _events = events;
                _coordinateMode = options.CoordinateMode;
                _dpiScale = platform._initialDpiScale ?? options.InitialDpiScale;
                _framebufferScale = platform._initialFramebufferScale ?? options.InitialDpiScale;
                _nativeBounds = LibreWindowCoordinates.ToNative(
                    options.Bounds,
                    _coordinateMode,
                    _dpiScale,
                    _framebufferScale);
                _platform.LastWindowBounds = options.Bounds;
                _platform.LastNativeWindowBounds = _nativeBounds;
                Title = options.Title;
                _state = options.InitialState;
                _platform.LastWindowState = _state;
                TopMost = options.Options.HasFlag(LibreWindowOptions.TopMost);
                Border = !options.Options.HasFlag(LibreWindowOptions.Decorated)
                    ? LibreWindowBorder.Hidden
                    : options.Options.HasFlag(LibreWindowOptions.Resizable)
                        ? LibreWindowBorder.Resizable
                        : LibreWindowBorder.Fixed;
                ShowInTaskbar = options.ShowInTaskbar;
                CanClose = options.CanClose;
                CanMinimize = options.CanMinimize;
                CanMaximize = options.CanMaximize;
                Opacity = options.Opacity;
                SetSizeConstraints(options.MinimumSize, options.MaximumSize);
                Owner = options.Owner;
                Visible = options.Options.HasFlag(LibreWindowOptions.Visible);
                Handle = platform.Handles.Allocate(this, LibreHandleKind.Window);
            }

            public LibreHandle Handle { get; }

            public string Title
            {
                get => _title;
                set
                {
                    ArgumentNullException.ThrowIfNull(value);
                    _title = value;
                    _platform.LastWindowTitle = value;
                }
            }

            public LibreHandle Owner { get; set; }

            public LibreRectangle Bounds
            {
                get => LibreWindowCoordinates.ToManaged(
                    _nativeBounds,
                    _coordinateMode,
                    _dpiScale,
                    _framebufferScale);
                set
                {
                    _nativeBounds = LibreWindowCoordinates.ToNative(
                        value,
                        _coordinateMode,
                        _dpiScale,
                        _framebufferScale);
                    _platform.LastWindowBounds = value;
                    _platform.LastNativeWindowBounds = _nativeBounds;
                    _events.BoundsChanged(value);
                }
            }

            public LibreWindowState State
            {
                get => _state;
                set
                {
                    _state = value;
                    _platform.LastWindowState = value;
                    _events.StateChanged(value);
                }
            }

            public bool Visible { get; private set; }

            public bool Enabled { get; set; } = true;

            public bool TopMost
            {
                get => _topMost;
                set
                {
                    _topMost = value;
                    _platform.LastWindowTopMost = value;
                }
            }

            public LibreWindowBorder Border
            {
                get => _border;
                set
                {
                    _border = value;
                    _platform.LastWindowBorder = value;
                }
            }

            public bool ShowInTaskbar
            {
                get => _showInTaskbar;
                set
                {
                    _showInTaskbar = value;
                    _platform.LastWindowShowInTaskbar = value;
                }
            }

            public bool CanMinimize
            {
                get => _canMinimize;
                set
                {
                    _canMinimize = value;
                    _platform.LastWindowCanMinimize = value;
                }
            }

            public bool CanClose
            {
                get => _canClose;
                set
                {
                    _canClose = value;
                    _platform.LastWindowCanClose = value;
                }
            }

            public bool CanMaximize
            {
                get => _canMaximize;
                set
                {
                    _canMaximize = value;
                    _platform.LastWindowCanMaximize = value;
                }
            }

            public double Opacity
            {
                get => _opacity;
                set
                {
                    _opacity = value;
                    _platform.LastWindowOpacity = value;
                }
            }

            public void SetZOrder(LibreWindowZOrder value)
            {
                _platform.LastWindowZOrder = value;
                _platform.WindowZOrderChangeCount++;
            }

            public void SetCursor(LibreCursorShape shape)
            {
                _platform.LastCursorShape = shape;
                _platform.CursorChangeCount++;
            }

            public void SetSizeConstraints(LibreSize minimum, LibreSize maximum)
            {
                _platform.LastWindowMinimumSize = minimum;
                _platform.LastWindowMaximumSize = maximum;
            }

            public LibreWindowCoordinateMode CoordinateMode => _coordinateMode;

            public double FramebufferScale => _framebufferScale;

            public double DpiScale => _dpiScale;

            public void SetIcons(IReadOnlyList<LibreWindowIcon> icons)
                => _platform.LastWindowIcons = icons.ToArray();

            public void Show()
            {
                Visible = true;
                if (_platform._autoCloseWindows)
                {
                    _platform.Post(Close);
                }
            }

            public void Hide() => Visible = false;

            public void Activate() => _platform.LastActivatedWindow = Handle;

            public void Close()
            {
                if (_disposed)
                {
                    return;
                }

                if (_events.Closing())
                {
                    Dispose();
                }
                else
                {
                    _platform.Post(Close);
                }
            }

            internal Graphics CreateGraphics(
                LibrePoint origin,
                LibreRectangle clipRectangle)
            {
                DrawingContext recording = new();
                int infrastructureCommandCount = 0;
                Graphics graphics = Graphics.FromProGpuDrawingContext(
                    recording,
                    new RectangleF(
                        clipRectangle.X,
                        clipRectangle.Y,
                        clipRectangle.Width,
                        clipRectangle.Height),
                    Matrix4x4.CreateTranslation(origin.X, origin.Y, 0f),
                    intention => FlushGraphics(recording, infrastructureCommandCount, intention),
                    () => CompleteGraphics(recording, infrastructureCommandCount));
                graphics.SetClip(new RectangleF(
                    clipRectangle.X - origin.X,
                    clipRectangle.Y - origin.Y,
                    clipRectangle.Width,
                    clipRectangle.Height));
                infrastructureCommandCount = checked(recording.Commands.Count + 1);
                return graphics;
            }

            private void FlushGraphics(
                DrawingContext recording,
                int infrastructureCommandCount,
                FlushIntention intention)
            {
                _platform.CreateGraphicsFlushCount++;
                _platform.LastCreateGraphicsFlushIntention = intention;
                CompleteGraphics(recording, infrastructureCommandCount);
            }

            private void CompleteGraphics(
                DrawingContext recording,
                int infrastructureCommandCount)
            {
                try
                {
                    if (_disposed || recording.Commands.Count <= infrastructureCommandCount)
                    {
                        return;
                    }

                    _retainedContext.Append(recording);
                    _platform.CreateGraphicsCommitCount++;
                    _platform.SawCreateGraphicsTranslatedFill = ContainsSolidFill(
                        recording,
                        new RectangleF(14, 21, 10, 8),
                        Color.MediumPurple);
                }
                finally
                {
                    recording.Clear();
                }
            }

            internal void RequestPaint(LibreRectangle dirtyRectangle)
            {
                if (_paintQueued)
                {
                    _dirtyRectangle = Union(_dirtyRectangle, dirtyRectangle);
                }
                else
                {
                    _paintQueued = true;
                    _dirtyRectangle = dirtyRectangle;
                    _platform.Post(PresentPendingPaint);
                }
            }

            public void PresentPendingPaint()
            {
                if (_disposed || !_paintQueued)
                {
                    return;
                }

                LibreRectangle dirtyRectangle = _dirtyRectangle;
                _paintQueued = false;
                _dirtyRectangle = default;
                LibreRectangle surfaceBounds = new(0, 0, Bounds.Width, Bounds.Height);
                HeadlessRetainedPaintFrame frame = new(
                    _platform,
                    _retainedContext,
                    _retainedLayers,
                    surfaceBounds,
                    dirtyRectangle);
                try
                {
                    _events.PaintRequested(frame);
                }
                finally
                {
                    frame.Complete();
                }

                _platform.LastPaintCommandCount = _retainedContext.Commands.Count;
                _platform.SawFormPaintFill = ContainsSolidFill(
                    _retainedContext,
                    new RectangleF(4, 5, 24, 16),
                    Color.CornflowerBlue);
                _platform.SawTranslatedChildPaintFill = ContainsSolidFill(
                    _retainedContext,
                    new RectangleF(14, 21, 10, 8),
                    Color.OrangeRed);
            }

            internal void SendInput(in LibreInputEvent inputEvent)
            {
                if (Enabled || inputEvent.Kind == LibreInputEventKind.FocusLost)
                {
                    _events.Input(inputEvent);
                }
            }

            internal void SetPresentationScales(double dpiScale, double framebufferScale)
            {
                LibreRectangle oldManagedBounds = Bounds;
                double oldDpiScale = _dpiScale;
                _dpiScale = dpiScale;
                _framebufferScale = framebufferScale;
                int desiredWidth = _coordinateMode == LibreWindowCoordinateMode.DevicePixels
                    ? ScaleForDpi(oldManagedBounds.Width, dpiScale, oldDpiScale)
                    : oldManagedBounds.Width;
                int desiredHeight = _coordinateMode == LibreWindowCoordinateMode.DevicePixels
                    ? ScaleForDpi(oldManagedBounds.Height, dpiScale, oldDpiScale)
                    : oldManagedBounds.Height;
                if (_coordinateMode == LibreWindowCoordinateMode.Logical)
                {
                    _nativeBounds = LibreWindowCoordinates.ToNative(
                        oldManagedBounds,
                        _coordinateMode,
                        _dpiScale,
                        _framebufferScale);
                }
                else
                {
                    LibreRectangle nativeSize = LibreWindowCoordinates.ToNative(
                        new LibreRectangle(0, 0, desiredWidth, desiredHeight),
                        _coordinateMode,
                        _dpiScale,
                        _framebufferScale);
                    _nativeBounds = new LibreRectangle(
                        _nativeBounds.X,
                        _nativeBounds.Y,
                        nativeSize.Width,
                        nativeSize.Height);
                }

                _platform.LastNativeWindowBounds = _nativeBounds;
                _events.BoundsChanged(Bounds);
                _events.PresentationScaleChanged(dpiScale);
            }

            private static int ScaleForDpi(int value, double newDpiScale, double oldDpiScale)
                => checked((int)Math.Round(value * newDpiScale / oldDpiScale, MidpointRounding.AwayFromZero));

            private static LibreRectangle Union(LibreRectangle left, LibreRectangle right)
            {
                int x = Math.Min(left.X, right.X);
                int y = Math.Min(left.Y, right.Y);
                int rightEdge = Math.Max(
                    checked(left.X + left.Width),
                    checked(right.X + right.Width));
                int bottomEdge = Math.Max(
                    checked(left.Y + left.Height),
                    checked(right.Y + right.Height));
                return new LibreRectangle(x, y, checked(rightEdge - x), checked(bottomEdge - y));
            }

            private static bool ContainsSolidFill(
                DrawingContext context,
                RectangleF expectedRectangle,
                Color expectedColor)
            {
                Vector4 expected = new(
                    expectedColor.R / 255f,
                    expectedColor.G / 255f,
                    expectedColor.B / 255f,
                    expectedColor.A / 255f);

                foreach (RenderCommand command in context.Commands)
                {
                    if (command.Type == RenderCommandType.DrawRect &&
                        command.Pen is null &&
                        command.Brush is ProGpuSolidColorBrush brush &&
                        command.Rect.X == expectedRectangle.X &&
                        command.Rect.Y == expectedRectangle.Y &&
                        command.Rect.Width == expectedRectangle.Width &&
                        command.Rect.Height == expectedRectangle.Height &&
                        brush.Color == expected)
                    {
                        return true;
                    }
                }

                return false;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                Visible = false;
                _retainedContext.Clear();
                foreach (HeadlessRetainedLayer layer in _retainedLayers.Values)
                {
                    layer.Context.Clear();
                }

                _retainedLayers.Clear();
                _platform.Handles.Release(Handle);
                _events.Closed();
            }

            private sealed class HeadlessRetainedPaintFrame : ILibreRetainedPaintFrame
            {
                private readonly HeadlessPlatform _platform;
                private readonly DrawingContext _output;
                private readonly Dictionary<LibreHandle, HeadlessRetainedLayer> _layers;
                private readonly DrawingContext _fallback = new();
                private readonly List<HeadlessRetainedLayer> _ordered = [];
                private readonly HashSet<LibreHandle> _visited = [];
                private int _repaintCount;
                private bool _completed;

                internal HeadlessRetainedPaintFrame(
                    HeadlessPlatform platform,
                    DrawingContext output,
                    Dictionary<LibreHandle, HeadlessRetainedLayer> layers,
                    LibreRectangle surfaceBounds,
                    LibreRectangle dirtyRectangle)
                {
                    _platform = platform;
                    _output = output;
                    _layers = layers;
                    SurfaceBounds = surfaceBounds;
                    DirtyRectangle = dirtyRectangle;
                    Graphics = Graphics.FromProGpuDrawingContext(
                        _fallback,
                        new RectangleF(0, 0, surfaceBounds.Width, surfaceBounds.Height));
                }

                public Graphics Graphics { get; }

                public LibreRectangle SurfaceBounds { get; }

                public LibreRectangle DirtyRectangle { get; }

                public ILibrePaintLayer OpenLayer(
                    LibreHandle target,
                    LibreRectangle bounds,
                    LibreRectangle clipRectangle)
                {
                    _visited.Add(target).Should().BeTrue();
                    bool isNew = !_layers.TryGetValue(target, out HeadlessRetainedLayer? layer);
                    if (isNew)
                    {
                        layer = new HeadlessRetainedLayer();
                        _layers.Add(target, layer);
                    }

                    layer!.Bounds = bounds;
                    _ordered.Add(layer);
                    if (!isNew && !Intersects(bounds, DirtyRectangle))
                    {
                        return EmptyPaintLayer.Instance;
                    }

                    _repaintCount++;
                    layer.Context.Clear();
                    Graphics graphics = Graphics.FromProGpuDrawingContext(
                        layer.Context,
                        new RectangleF(0, 0, bounds.Width, bounds.Height));
                    graphics.SetClip(new RectangleF(
                        clipRectangle.X - bounds.X,
                        clipRectangle.Y - bounds.Y,
                        clipRectangle.Width,
                        clipRectangle.Height));
                    return new RecordingPaintLayer(graphics);
                }

                internal void Complete()
                {
                    if (_completed)
                    {
                        return;
                    }

                    _completed = true;
                    Graphics.Dispose();
                    foreach ((LibreHandle target, HeadlessRetainedLayer layer) in _layers.ToArray())
                    {
                        if (_visited.Contains(target))
                        {
                            continue;
                        }

                        layer.Context.Clear();
                        _layers.Remove(target);
                    }

                    _output.Clear();
                    _output.Append(_fallback);
                    foreach (HeadlessRetainedLayer layer in _ordered)
                    {
                        _output.Append(layer.Context, new Vector2(layer.Bounds.X, layer.Bounds.Y));
                    }

                    _fallback.Clear();
                    _platform.LastRetainedLayerCount = _layers.Count;
                    _platform.LastRetainedLayerRepaintCount = _repaintCount;
                }

                private static bool Intersects(LibreRectangle left, LibreRectangle right)
                    => left.Width > 0
                        && left.Height > 0
                        && right.Width > 0
                        && right.Height > 0
                        && left.X < right.Right
                        && right.X < left.Right
                        && left.Y < right.Bottom
                        && right.Y < left.Bottom;

                private sealed class RecordingPaintLayer(Graphics graphics) : ILibrePaintLayer
                {
                    public Graphics? Graphics { get; private set; } = graphics;

                    public void Dispose()
                    {
                        Graphics?.Dispose();
                        Graphics = null;
                    }
                }

                private sealed class EmptyPaintLayer : ILibrePaintLayer
                {
                    internal static EmptyPaintLayer Instance { get; } = new();

                    public Graphics? Graphics => null;

                    public void Dispose()
                    {
                    }
                }
            }

            private sealed class HeadlessRetainedLayer
            {
                internal DrawingContext Context { get; } = new();

                internal LibreRectangle Bounds { get; set; }
            }
        }

        private sealed class EmptyDisposable : IDisposable
        {
            public void Dispose() { }
        }

        private sealed class HeadlessThreadDispatcher(
            int managedThreadId,
            Action posted) : ILibreDispatcher, IDisposable
        {
            private readonly ConcurrentQueue<Action> _work = new();
            private readonly AutoResetEvent _wake = new(initialState: false);
            private volatile bool _exitRequested;
            private volatile bool _releaseRequested;
            private volatile bool _running;
            private bool _disposed;

            public int ManagedThreadId { get; } = managedThreadId;

            public bool CheckAccess() => Environment.CurrentManagedThreadId == ManagedThreadId;

            public void Post(Action callback)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                ArgumentNullException.ThrowIfNull(callback);
                posted();
                _work.Enqueue(callback);
                _wake.Set();
            }

            public void Send(Action callback)
            {
                ArgumentNullException.ThrowIfNull(callback);
                if (CheckAccess())
                {
                    callback();
                    return;
                }

                ExceptionDispatchInfo? error = null;
                using ManualResetEventSlim completed = new();
                Post(() =>
                {
                    try
                    {
                        callback();
                    }
                    catch (Exception exception)
                    {
                        error = ExceptionDispatchInfo.Capture(exception);
                    }
                    finally
                    {
                        completed.Set();
                    }
                });

                if (!completed.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("The headless dispatcher did not complete synchronous work.");
                }

                error?.Throw();
            }

            public void PumpOnce()
            {
                VerifyAccess();
                if (_work.TryDequeue(out Action? callback))
                {
                    callback();
                    return;
                }

                _wake.WaitOne(TimeSpan.FromMilliseconds(10));
            }

            public void Run(CancellationToken cancellationToken)
            {
                VerifyAccess();
                _exitRequested = false;
                _running = true;
                try
                {
                    while (!_exitRequested)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        PumpOnce();
                    }
                }
                finally
                {
                    _running = false;
                    if (_releaseRequested)
                    {
                        Dispose();
                    }
                }
            }

            public void RunNested(Func<bool> continueCondition, CancellationToken cancellationToken)
            {
                VerifyAccess();
                ArgumentNullException.ThrowIfNull(continueCondition);
                while (continueCondition())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PumpOnce();
                }
            }

            public void RequestExit()
            {
                _exitRequested = true;
                _wake.Set();
            }

            internal void Release()
            {
                _releaseRequested = true;
                RequestExit();
                if (!_running)
                {
                    Dispose();
                }
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _wake.Dispose();
            }

            private void VerifyAccess()
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!CheckAccess())
                {
                    throw new InvalidOperationException("The headless dispatcher must be used from its owning thread.");
                }
            }
        }

        private sealed class HeadlessTimerRegistration(HeadlessPlatform owner, int generation) : IDisposable
        {
            private HeadlessPlatform? _owner = owner;

            public void Dispose()
            {
                HeadlessPlatform? current = Interlocked.Exchange(ref _owner, null);
                current?.StopTimer(generation);
            }
        }
    }
}
