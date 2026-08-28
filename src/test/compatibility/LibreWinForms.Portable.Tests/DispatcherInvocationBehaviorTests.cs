using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;
using Forms = System.Windows.Forms;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class DispatcherInvocationBehaviorTests
{
    private delegate int AddValues(int left, int right);

    public static void Run()
    {
        using var dispatcherHost = new FakeDispatcherApplicationHost();
        Forms.Application.RegisterPortableApplicationHost(dispatcherHost);
        var control = new Forms.Control();

        Assert(control.InvokeRequired, "InvokeRequired was false away from the registered UI dispatcher.");

        int callerThread = Environment.CurrentManagedThreadId;
        int callbackThread = 0;
        object? invokeResult = control.Invoke(
            new Func<object?>(() =>
            {
                callbackThread = Environment.CurrentManagedThreadId;
                return "marshaled";
            }));
        Assert((string?)invokeResult == "marshaled", "Invoke did not return the callback result.");
        Assert(callbackThread == dispatcherHost.DispatcherThreadId, "Invoke did not marshal to the UI dispatcher.");
        Assert(callbackThread != callerThread, "The fake UI dispatcher unexpectedly used the caller thread.");

        dispatcherHost.Invoke(
            () =>
            {
                Assert(!control.InvokeRequired, "InvokeRequired was true on the registered UI dispatcher.");
                int postsBefore = dispatcherHost.BeginInvokeCount;
                int directThread = 0;
                control.Invoke((Action)(() => directThread = Environment.CurrentManagedThreadId));
                Assert(directThread == dispatcherHost.DispatcherThreadId, "Same-thread Invoke did not execute directly.");
                Assert(
                    dispatcherHost.BeginInvokeCount == postsBefore,
                    "Same-thread Invoke unnecessarily posted another dispatcher callback.");
            });

        using var releaseQueue = new ManualResetEventSlim(initialState: false);
        dispatcherHost.BeginInvoke(() => releaseQueue.Wait());
        int asyncCalls = 0;
        IAsyncResult asynchronous = control.BeginInvoke(
            new AddValues(
                (left, right) =>
                {
                    Interlocked.Increment(ref asyncCalls);
                    return left + right;
                }),
            3,
            4);
        Assert(!asynchronous.IsCompleted, "BeginInvoke completed inline instead of posting asynchronously.");
        Assert(asyncCalls == 0, "BeginInvoke ran before the dispatcher queue was released.");
        releaseQueue.Set();
        Assert((int?)control.EndInvoke(asynchronous) == 7, "EndInvoke did not wait for and return the delegate result.");
        Assert(asynchronous.IsCompleted && asyncCalls == 1, "The asynchronous callback did not complete exactly once.");

        string? typedArgument = null;
        IAsyncResult typedAsynchronous = control.BeginInvoke<string>(
            value => typedArgument = value,
            "SharpDevelop solution");
        _ = control.EndInvoke(typedAsynchronous);
        Assert(
            typedArgument == "SharpDevelop solution",
            "The typed single-argument BeginInvoke path lost its argument.");

        int typedInvokeValue = 0;
        control.Invoke<int>(value => typedInvokeValue = value, 42);
        Assert(typedInvokeValue == 42, "The typed single-argument Invoke path lost its argument.");

        IAsyncResult throwing = control.BeginInvoke(
            (Action)(() => throw new InvalidOperationException("portable invoke failure")));
        try
        {
            _ = control.EndInvoke(throwing);
            throw new InvalidOperationException("EndInvoke did not rethrow the callback exception.");
        }
        catch (InvalidOperationException exception) when (exception.Message == "portable invoke failure")
        {
        }

        dispatcherHost.Invoke(
            () =>
            {
                int sameThreadCalls = 0;
                IAsyncResult sameThread = control.BeginInvoke(
                    (Func<object?>)(() =>
                    {
                        sameThreadCalls++;
                        return 42;
                    }));
                Assert(!sameThread.IsCompleted, "Same-thread BeginInvoke completed synchronously.");
                Assert((int?)control.EndInvoke(sameThread) == 42, "Same-thread EndInvoke did not drain the posted callback.");
                Assert(sameThreadCalls == 1, "Same-thread BeginInvoke callback ran more than once.");
            });

        dispatcherHost.Invoke(() => { });
        Console.WriteLine(
            "LibreWinForms dispatcher invocation tests passed: invokeRequired=1 beginAsync=1 typedArg=1 endWait=1 exception=1 sameThread=1.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FakeDispatcherApplicationHost :
        Forms.IWinFormsApplicationHost,
        Forms.IWinFormsDispatcherHost,
        IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new();
        private readonly ManualResetEventSlim _started = new(initialState: false);
        private readonly Thread _thread;
        private int _beginInvokeCount;
        private int _disposed;

        public FakeDispatcherApplicationHost()
        {
            _thread = new Thread(RunDispatcher)
            {
                IsBackground = true,
                Name = "LibreWinForms fake UI dispatcher"
            };
            _thread.Start();
            _started.Wait();
        }

        public int BeginInvokeCount => Volatile.Read(ref _beginInvokeCount);

        public int DispatcherThreadId { get; private set; }

        public bool CheckAccess() => Environment.CurrentManagedThreadId == DispatcherThreadId;

        public void BeginInvoke(Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            Interlocked.Increment(ref _beginInvokeCount);
            _queue.Add(callback);
        }

        public void Invoke(Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            if (CheckAccess())
            {
                callback();
                return;
            }

            using var completed = new ManualResetEventSlim(initialState: false);
            ExceptionDispatchInfo? exception = null;
            BeginInvoke(
                () =>
                {
                    try
                    {
                        callback();
                    }
                    catch (Exception caught)
                    {
                        exception = ExceptionDispatchInfo.Capture(caught);
                    }
                    finally
                    {
                        completed.Set();
                    }
                });
            completed.Wait();
            exception?.Throw();
        }

        public void Run(Forms.Form mainForm)
        {
            throw new NotSupportedException();
        }

        public Forms.DialogResult ShowDialog(Forms.Form form, Forms.IWin32Window? owner)
        {
            throw new NotSupportedException();
        }

        public void ExitThread()
        {
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _queue.CompleteAdding();
            _thread.Join();
            _started.Dispose();
            _queue.Dispose();
        }

        private void RunDispatcher()
        {
            DispatcherThreadId = Environment.CurrentManagedThreadId;
            _started.Set();
            foreach (Action callback in _queue.GetConsumingEnumerable())
            {
                callback();
            }
        }
    }
}
