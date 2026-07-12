using System;
using Forms = System.Windows.Forms;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class KeyboardRoutingBehaviorTests
{
    private const int WmKeyDown = 0x0100;

    public static void Run()
    {
        CommandKeysReachTheFocusedControlWithModifiers();
        UnhandledChildCommandsBubbleToTheParent();
        MessageFiltersObserveTypedKeyMessagesAndCanBeRemoved();
        Console.WriteLine("LibreWinForms keyboard routing tests passed: command=1 modifiers=1 parent=1 filter=1 removal=1.");
    }

    private static void UnhandledChildCommandsBubbleToTheParent()
    {
        var parent = new CommandProbeControl();
        var child = new Forms.Control();
        parent.Controls.Add(child);
        var message = new Forms.Message
        {
            HWnd = child.Handle,
            Msg = WmKeyDown,
            WParam = new IntPtr((int)Forms.Keys.F6)
        };

        Assert(child.PreProcessMessage(ref message), "Unhandled child command did not bubble to its parent.");
        Assert(parent.CommandCount == 1, "Parent ProcessCmdKey was not called exactly once.");
        Assert(parent.LastKeyData == Forms.Keys.F6, "Parent ProcessCmdKey received the wrong key.");
    }

    private static void CommandKeysReachTheFocusedControlWithModifiers()
    {
        var control = new CommandProbeControl();
        Forms.Keys previousModifiers = Forms.Control.ModifierKeys;
        Forms.Control.ModifierKeys = Forms.Keys.Control | Forms.Keys.Shift;
        try
        {
            var message = new Forms.Message
            {
                HWnd = control.Handle,
                Msg = WmKeyDown,
                WParam = new IntPtr((int)Forms.Keys.Delete)
            };

            bool handled = control.PreProcessMessage(ref message);
            Assert(handled, "PreProcessMessage did not preserve the ProcessCmdKey handled result.");
            Assert(control.CommandCount == 1, "ProcessCmdKey was not called exactly once.");
            Assert(
                control.LastKeyData == (Forms.Keys.Delete | Forms.Keys.Control | Forms.Keys.Shift),
                "ProcessCmdKey did not receive the current modifier state.");
            Assert(message.HWnd == control.Handle, "Command message lost the originating control handle.");
        }
        finally
        {
            Forms.Control.ModifierKeys = previousModifiers;
        }
    }

    private static void MessageFiltersObserveTypedKeyMessagesAndCanBeRemoved()
    {
        var control = new Forms.Control();
        var filter = new RecordingMessageFilter();
        var message = new Forms.Message
        {
            HWnd = control.Handle,
            Msg = WmKeyDown,
            WParam = new IntPtr((int)Forms.Keys.F2)
        };

        Forms.Application.AddMessageFilter(filter);
        try
        {
            Assert(Forms.Application.FilterMessage(ref message), "Registered message filter did not handle the key message.");
            Assert(filter.CallCount == 1, "Registered message filter was not called exactly once.");
            Assert(filter.LastHWnd == control.Handle, "Message filter observed the wrong originating handle.");
            Assert(filter.LastMessage == WmKeyDown, "Message filter observed the wrong message kind.");
            Assert(filter.LastKeyCode == Forms.Keys.F2, "Message filter observed the wrong key code.");
        }
        finally
        {
            Forms.Application.RemoveMessageFilter(filter);
        }

        Assert(!Forms.Application.FilterMessage(ref message), "Removed message filter still handled a message.");
        Assert(filter.CallCount == 1, "Removed message filter was invoked again.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class CommandProbeControl : Forms.Control
    {
        public int CommandCount { get; private set; }

        public Forms.Keys LastKeyData { get; private set; }

        protected override bool ProcessCmdKey(ref Forms.Message msg, Forms.Keys keyData)
        {
            CommandCount++;
            LastKeyData = keyData;
            return true;
        }
    }

    private sealed class RecordingMessageFilter : Forms.IMessageFilter
    {
        public int CallCount { get; private set; }

        public IntPtr LastHWnd { get; private set; }

        public Forms.Keys LastKeyCode { get; private set; }

        public int LastMessage { get; private set; }

        public bool PreFilterMessage(ref Forms.Message message)
        {
            CallCount++;
            LastHWnd = message.HWnd;
            LastMessage = message.Msg;
            LastKeyCode = (Forms.Keys)message.WParam.ToInt32();
            return true;
        }
    }
}
