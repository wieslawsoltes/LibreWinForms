using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;
using Forms = System.Windows.Forms;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class ApplicationThreadLifecycleBehaviorTests
{
    public static void Run()
    {
        var host = new FakeThreadApplicationHost();
        Forms.Application.RegisterPortableApplicationHost(host);

        var first = StartFormLoop("first", host);
        var second = StartFormLoop("second", host);

        Assert(first.Form.InvokeRequired, "A control owned by the first application thread used the caller dispatcher.");
        Assert(second.Form.InvokeRequired, "A control owned by the second application thread used the caller dispatcher.");

        first.Form.BeginInvoke(
            (Action)(() =>
            {
                first.CallbackThreadId = Environment.CurrentManagedThreadId;
                first.Form.Dispose();
                Forms.Application.ExitThread();
            }));

        Assert(first.Thread.Join(TimeSpan.FromSeconds(5)), "The first per-thread application loop did not exit.");
        Assert(second.Thread.IsAlive, "Exiting the first application thread terminated a separate application loop.");
        Assert(first.CallbackThreadId == first.Thread.ManagedThreadId, "Control.BeginInvoke did not use its owning application thread context.");
        Assert(first.Form.IsDisposed, "The owning-thread callback did not dispose its main form.");

        second.Form.BeginInvoke((Action)Forms.Application.ExitThread);
        Assert(second.Thread.Join(TimeSpan.FromSeconds(5)), "The second per-thread application loop did not exit.");

        var noFormThread = new Thread(Forms.Application.Run)
        {
            IsBackground = true,
            Name = "LibreWinForms no-form application loop test"
        };
        noFormThread.Start();
        FakeApplicationThreadContext noFormContext = host.WaitForContext("<none>");
        noFormContext.BeginInvoke(Forms.Application.ExitThread);
        Assert(noFormThread.Join(TimeSpan.FromSeconds(5)), "Application.Run() without a form did not honor ExitThread.");

        Assert(host.GlobalExitCount == 0, "Per-thread ExitThread fell through to the process-wide application host.");
        Assert(host.CreatedContextCount == 3, "The typed host did not create one context per Application.Run call.");
        Assert(host.DisposedContextCount == 3, "Application.Run did not dispose every completed thread context.");

        Console.WriteLine(
            "LibreWinForms application thread lifecycle tests passed: contexts=3 isolated=2 noForm=1 ownerDispatch=1 globalExit=0.");
    }

    private static RunningFormLoop StartFormLoop(string name, FakeThreadApplicationHost host)
    {
        var ready = new ManualResetEventSlim(initialState: false);
        Forms.Form? form = null;
        var state = new RunningFormLoop();
        state.Thread = new Thread(
            () =>
            {
                form = new Forms.Form { Name = name };
                state.Form = form;
                ready.Set();
                Forms.Application.Run(form);
            })
        {
            IsBackground = true,
            Name = "LibreWinForms " + name + " application loop test"
        };

        state.Thread.Start();
        Assert(ready.Wait(TimeSpan.FromSeconds(5)), "The " + name + " form loop did not initialize.");
        _ = host.WaitForContext(name);
        return state;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class RunningFormLoop
    {
        public Thread Thread { get; set; } = null!;

        public Forms.Form Form { get; set; } = null!;

        public int CallbackThreadId { get; set; }
    }

    private sealed class FakeThreadApplicationHost :
        Forms.IWinFormsApplicationHost,
        Forms.IWinFormsThreadApplicationHost
    {
        private readonly BlockingCollection<FakeApplicationThreadContext> _createdContexts = new();
        private int _createdContextCount;
        private int _disposedContextCount;
        private int _globalExitCount;

        public int CreatedContextCount => Volatile.Read(ref _createdContextCount);

        public int DisposedContextCount => Volatile.Read(ref _disposedContextCount);

        public int GlobalExitCount => Volatile.Read(ref _globalExitCount);

        public Forms.IWinFormsApplicationThreadContext CreateThreadContext(Forms.Form? mainForm)
        {
            Interlocked.Increment(ref _createdContextCount);
            var context = new FakeApplicationThreadContext(
                mainForm?.Name ?? "<none>",
                () => Interlocked.Increment(ref _disposedContextCount));
            _createdContexts.Add(context);
            return context;
        }

        public FakeApplicationThreadContext WaitForContext(string name)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                if (_createdContexts.TryTake(out FakeApplicationThreadContext? context, 100))
                {
                    if (string.Equals(context.Name, name, StringComparison.Ordinal))
                    {
                        Assert(context.Started.Wait(TimeSpan.FromSeconds(5)), "The " + name + " context did not enter Run.");
                        return context;
                    }

                    _createdContexts.Add(context);
                }
            }

            throw new InvalidOperationException("The " + name + " application context was not created.");
        }

        public void Run(Forms.Form mainForm) => throw new NotSupportedException();

        public Forms.DialogResult ShowDialog(Forms.Form form, Forms.IWin32Window? owner) =>
            throw new NotSupportedException();

        public void ExitThread()
        {
            Interlocked.Increment(ref _globalExitCount);
        }
    }

    private sealed class FakeApplicationThreadContext : Forms.IWinFormsApplicationThreadContext
    {
        private readonly BlockingCollection<Action> _queue = new();
        private readonly Action _disposedCallback;
        private int _ownerThreadId;
        private int _exitRequested;
        private int _disposed;

        public FakeApplicationThreadContext(string name, Action disposedCallback)
        {
            Name = name;
            _disposedCallback = disposedCallback;
        }

        public string Name { get; }

        public ManualResetEventSlim Started { get; } = new(initialState: false);

        public bool CheckAccess() => Environment.CurrentManagedThreadId == Volatile.Read(ref _ownerThreadId);

        public void BeginInvoke(Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
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

        public void Run()
        {
            Volatile.Write(ref _ownerThreadId, Environment.CurrentManagedThreadId);
            Started.Set();
            while (Volatile.Read(ref _exitRequested) == 0)
            {
                if (_queue.TryTake(out Action? callback, 100))
                    callback();
            }
        }

        public void ExitThread()
        {
            Interlocked.Exchange(ref _exitRequested, 1);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            Started.Dispose();
            _queue.Dispose();
            _disposedCallback();
        }
    }
}
