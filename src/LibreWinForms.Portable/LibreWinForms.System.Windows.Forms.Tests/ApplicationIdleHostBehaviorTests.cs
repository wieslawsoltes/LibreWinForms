using System;
using Forms = System.Windows.Forms;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class ApplicationIdleHostBehaviorTests
{
    public static void Run()
    {
        var host = new FakeIdleApplicationHost();
        Forms.Application.RegisterPortableApplicationHost(host);

        int firstCalls = 0;
        int secondCalls = 0;
        EventHandler first = (_, _) => firstCalls++;
        EventHandler second = (_, _) => secondCalls++;
        Forms.Application.Idle += first;
        Forms.Application.Idle += second;

        Assert(host.RequestCount == 1, "Application did not coalesce idle requests for the same dispatcher turn.");
        host.DispatchIdle();
        Assert(firstCalls == 1 && secondCalls == 1, "Application did not invoke all typed idle subscribers.");

        Forms.Application.Idle -= first;
        Forms.Application.Idle -= second;
        Forms.Application.Idle += first;
        Assert(host.RequestCount == 2, "A later idle subscription did not schedule a new dispatcher-idle turn.");
        host.DispatchIdle();
        Forms.Application.Idle -= first;
        Assert(firstCalls == 2 && secondCalls == 1, "Removed idle subscribers were invoked again.");

        Console.WriteLine("LibreWinForms application idle host tests passed: typedHost=1 coalesced=1 subscribers=2 turns=2.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class FakeIdleApplicationHost :
        Forms.IWinFormsApplicationHost,
        Forms.IWinFormsIdleHost
    {
        private Action? _pendingIdle;

        public int RequestCount { get; private set; }

        public bool TryBeginInvokeIdle(Action callback)
        {
            if (_pendingIdle is not null)
                throw new InvalidOperationException("The idle host received overlapping requests.");

            RequestCount++;
            _pendingIdle = callback;
            return true;
        }

        public void DispatchIdle()
        {
            Action callback = _pendingIdle
                ?? throw new InvalidOperationException("No dispatcher-idle callback is pending.");
            _pendingIdle = null;
            callback();
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
    }
}
