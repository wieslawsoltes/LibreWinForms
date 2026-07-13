using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using Forms = System.Windows.Forms;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class FormsDesignerEventBindingBehaviorTests
{
    public static void Run()
    {
        LocalEventBindingServiceOverridesParentProvider();
        EventPropertyChangesParticipateInTransactionsAndUndo();
        EventPropertyFailurePathsRestorePriorBinding();
        Console.WriteLine("LibreWinForms Forms Designer event-binding tests passed: precedence=3 changes=4 undo=4 reuse=2 failures=5.");
    }

    private static void LocalEventBindingServiceOverridesParentProvider()
    {
        using var parentServices = new ServiceContainer();
        var parentBinding = new ProbeEventBindingService(parentServices, "parent");
        parentServices.AddService(typeof(IEventBindingService), parentBinding);

        using var surface = new DesignSurface(parentServices);
        var host = (IDesignerHost)surface.GetService(typeof(IDesignerHost))!;
        Assert(ReferenceEquals(host.GetService(typeof(IEventBindingService)), parentBinding),
            "The configured parent event-binding service was not visible before a loader registration.");

        var localBinding = new ProbeEventBindingService(host, "local");
        ((IServiceContainer)host).AddService(typeof(IEventBindingService), localBinding);
        Assert(ReferenceEquals(host.GetService(typeof(IEventBindingService)), localBinding),
            "A loader-local event-binding service did not override the DesignSurface provider.");
        Assert(ReferenceEquals(surface.GetService(typeof(IEventBindingService)), localBinding),
            "DesignSurface did not expose the loader-local event-binding service.");

        ((IServiceContainer)host).RemoveService(typeof(IEventBindingService));
        Assert(ReferenceEquals(host.GetService(typeof(IEventBindingService)), parentBinding),
            "Removing the loader-local event-binding service did not restore provider fallback.");
    }

    private static void EventPropertyChangesParticipateInTransactionsAndUndo()
    {
        using var surface = new DesignSurface();
        var host = (IDesignerHost)surface.GetService(typeof(IDesignerHost))!;
        var localBinding = new ProbeEventBindingService(host, "designer");
        ((IServiceContainer)host).AddService(typeof(IEventBindingService), localBinding);

        var root = (Forms.Panel)host.CreateComponent(typeof(Forms.Panel), "rootPanel");
        var button = (Forms.Button)host.CreateComponent(typeof(Forms.Button), "eventButton");
        root.Controls.Add(button);
        EventDescriptor clickEvent = TypeDescriptor.GetEvents(button)[nameof(Forms.Control.Click)]
            ?? throw new InvalidOperationException("Button.Click is not available through TypeDescriptor.");
        IEventBindingService eventBindings = localBinding;
        PropertyDescriptor clickProperty = eventBindings.GetEventProperty(clickEvent);

        string firstName = eventBindings.CreateUniqueMethodName(button, clickEvent);
        string reusedName = eventBindings.CreateUniqueMethodName(button, clickEvent);
        Assert(firstName == "eventButton_Click" && reusedName == firstName,
            "Event handler name creation was not deterministic for the same sited component/event pair.");

        var changeSequence = new List<string>();
        var changeService = (IComponentChangeService)host.GetService(typeof(IComponentChangeService))!;
        ComponentChangingEventHandler changingHandler = (_, e) =>
        {
            if (ReferenceEquals(e.Component, button))
                changeSequence.Add((e.Member is EventDescriptor ? "changing:event:" : "changing:property:") + e.Member?.Name);
        };
        ComponentChangedEventHandler changedHandler = (_, e) =>
        {
            if (ReferenceEquals(e.Component, button))
            {
                changeSequence.Add((e.Member is EventDescriptor ? "changed:event:" : "changed:property:") + e.Member?.Name);
                if (e.Member is EventDescriptor)
                {
                    Assert(e.OldValue is null && e.NewValue is null,
                        "The native event-descriptor notification must not carry property values.");
                }
                else
                {
                    Assert(e.OldValue is null && string.Equals(e.NewValue as string, firstName, StringComparison.Ordinal),
                        "The event-property notification did not carry the old/new handler values.");
                }
            }
        };
        changeService.ComponentChanging += changingHandler;
        changeService.ComponentChanged += changedHandler;

        int openedTransactions = 0;
        int closedTransactions = 0;
        host.TransactionOpened += (_, _) => openedTransactions++;
        host.TransactionClosed += (_, e) =>
        {
            if (e.TransactionCommitted)
                closedTransactions++;
        };

        using var undo = new RecordingUndoEngine(host);
        clickProperty.SetValue(button, firstName);
        Assert((string?)clickProperty.GetValue(button) == firstName,
            "Setting the event property did not retain the handler name.");
        Assert(openedTransactions == 1 && closedTransactions == 1,
            "Setting the event property did not use one committed designer transaction.");
        Assert(changeSequence.Count == 4
            && changeSequence[0] == "changing:property:Click"
            && changeSequence[1] == "changing:event:Click"
            && changeSequence[2] == "changed:event:Click"
            && changeSequence[3] == "changed:property:Click",
            "Setting the event property did not publish the native event/property notification sequence.");
        changeService.ComponentChanging -= changingHandler;
        changeService.ComponentChanged -= changedHandler;
        Assert(undo.UndoCount == 1,
            "Setting the event property did not create one undo unit.");

        Assert(undo.UndoOnce() && clickProperty.GetValue(button) is null,
            "Undo did not clear the event handler binding.");
        Assert(undo.RedoOnce() && (string?)clickProperty.GetValue(button) == firstName,
            "Redo did not restore the event handler binding.");

        int undoCountBeforeReuse = undo.UndoCount;
        clickProperty.SetValue(button, firstName);
        Assert(undo.UndoCount == undoCountBeforeReuse,
            "Reusing an unchanged event handler name created a redundant undo unit.");

        clickProperty.ResetValue(button);
        Assert(clickProperty.GetValue(button) is null && undo.UndoCount == undoCountBeforeReuse + 1,
            "Resetting an event property did not clear it through one undoable transaction.");
        Assert(undo.UndoOnce() && (string?)clickProperty.GetValue(button) == firstName,
            "Undo did not restore the event handler after ResetValue.");
    }

    private static void EventPropertyFailurePathsRestorePriorBinding()
    {
        using (var fixture = new EventFixture())
        {
            ComponentChangingEventHandler cancel = (_, _) => throw CheckoutException.Canceled;
            fixture.ChangeService.ComponentChanging += cancel;
            fixture.Property.SetValue(fixture.Button, "replacementHandler");
            fixture.ChangeService.ComponentChanging -= cancel;
            Assert((string?)fixture.Property.GetValue(fixture.Button) == EventFixture.OriginalHandler,
                "Checkout cancellation changed the event handler binding.");
            Assert(fixture.CanceledTransactions == 1,
                "Checkout cancellation did not cancel its designer transaction exactly once.");
        }

        using (var fixture = new EventFixture())
        {
            ComponentChangingEventHandler failChanging = (_, _) => throw new InvalidOperationException("changing");
            fixture.ChangeService.ComponentChanging += failChanging;
            AssertThrows<InvalidOperationException>(() => fixture.Property.SetValue(fixture.Button, "replacementHandler"));
            fixture.ChangeService.ComponentChanging -= failChanging;
            Assert((string?)fixture.Property.GetValue(fixture.Button) == EventFixture.OriginalHandler,
                "A ComponentChanging failure changed the event handler binding.");
        }

        using (var fixture = new EventFixture())
        {
            fixture.Binding.ThrowOnUseMethodName = "replacementHandler";
            AssertThrows<InvalidOperationException>(() => fixture.Property.SetValue(fixture.Button, "replacementHandler"));
            Assert((string?)fixture.Property.GetValue(fixture.Button) == EventFixture.OriginalHandler,
                "A UseMethod failure changed the event handler binding.");
            Assert(!fixture.Binding.FreeCalls.Contains(EventFixture.OriginalHandler),
                "UseMethod failure released the existing method before the replacement was acquired.");
        }

        using (var fixture = new EventFixture())
        {
            fixture.Binding.ThrowOnFreeMethodName = EventFixture.OriginalHandler;
            AssertThrows<InvalidOperationException>(() => fixture.Property.SetValue(fixture.Button, "replacementHandler"));
            Assert((string?)fixture.Property.GetValue(fixture.Button) == EventFixture.OriginalHandler,
                "A FreeMethod failure changed the event handler binding.");
            Assert(fixture.Binding.UseCalls.Contains("replacementHandler")
                && fixture.Binding.FreeCalls.Contains("replacementHandler"),
                "FreeMethod failure did not release the replacement method acquired earlier in native ordering.");
        }

        using (var fixture = new EventFixture())
        {
            ComponentChangedEventHandler failChanged = (_, e) =>
            {
                if (e.Member is EventDescriptor)
                    throw new InvalidOperationException("changed");
            };
            fixture.ChangeService.ComponentChanged += failChanged;
            AssertThrows<InvalidOperationException>(() => fixture.Property.SetValue(fixture.Button, "replacementHandler"));
            fixture.ChangeService.ComponentChanged -= failChanged;
            Assert((string?)fixture.Property.GetValue(fixture.Button) == EventFixture.OriginalHandler,
                "A ComponentChanged failure did not restore the authoritative event handler mapping.");
            Assert(fixture.Binding.UseCalls.FindAll(name => name == EventFixture.OriginalHandler).Count == 2
                && fixture.Binding.FreeCalls.Contains("replacementHandler"),
                "A ComponentChanged failure did not restore old/new method usage bookkeeping.");
        }
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name} was not thrown.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class ProbeEventBindingService : EventBindingService
    {
        private readonly string _prefix;

        public ProbeEventBindingService(IServiceProvider provider, string prefix)
            : base(provider)
        {
            _prefix = prefix;
        }

        public string? ThrowOnUseMethodName { get; set; }

        public string? ThrowOnFreeMethodName { get; set; }

        public List<string> UseCalls { get; } = new();

        public List<string> FreeCalls { get; } = new();

        protected override string CreateUniqueMethodName(IComponent component, EventDescriptor e)
        {
            return component.Site?.Name + "_" + e.Name;
        }

        protected override ICollection GetCompatibleMethods(EventDescriptor e) => Array.Empty<string>();

        protected override void UseMethod(IComponent component, EventDescriptor e, string methodName)
        {
            UseCalls.Add(methodName);
            if (string.Equals(methodName, ThrowOnUseMethodName, StringComparison.Ordinal))
                throw new InvalidOperationException("UseMethod failure");
        }

        protected override void FreeMethod(IComponent component, EventDescriptor e, string methodName)
        {
            FreeCalls.Add(methodName);
            if (string.Equals(methodName, ThrowOnFreeMethodName, StringComparison.Ordinal))
                throw new InvalidOperationException("FreeMethod failure");
        }

        protected override bool ShowCode() => true;

        protected override bool ShowCode(int lineNumber) => lineNumber > 0;

        protected override bool ShowCode(IComponent component, EventDescriptor e, string methodName)
        {
            return methodName.StartsWith(_prefix, StringComparison.Ordinal)
                || !string.IsNullOrWhiteSpace(methodName);
        }
    }

    private sealed class EventFixture : IDisposable
    {
        public EventFixture()
        {
            Surface = new DesignSurface();
            Host = (IDesignerHost)Surface.GetService(typeof(IDesignerHost))!;
            Binding = new ProbeEventBindingService(Host, "fixture");
            ((IServiceContainer)Host).AddService(typeof(IEventBindingService), Binding);
            _ = (Forms.Panel)Host.CreateComponent(typeof(Forms.Panel), "rootPanel");
            Button = (Forms.Button)Host.CreateComponent(typeof(Forms.Button), "eventButton");
            EventDescriptor clickEvent = TypeDescriptor.GetEvents(Button)[nameof(Forms.Control.Click)]!;
            Property = ((IEventBindingService)Binding).GetEventProperty(clickEvent);
            ChangeService = (IComponentChangeService)Host.GetService(typeof(IComponentChangeService))!;
            Host.TransactionClosed += (_, e) =>
            {
                if (!e.TransactionCommitted)
                    CanceledTransactions++;
            };
            Property.SetValue(Button, OriginalHandler);
            CanceledTransactions = 0;
        }

        public const string OriginalHandler = "originalHandler";

        public DesignSurface Surface { get; }

        public IDesignerHost Host { get; }

        public ProbeEventBindingService Binding { get; }

        public Forms.Button Button { get; }

        public PropertyDescriptor Property { get; }

        public IComponentChangeService ChangeService { get; }

        public int CanceledTransactions { get; private set; }

        public void Dispose() => Surface.Dispose();
    }

    private sealed class RecordingUndoEngine : UndoEngine
    {
        private readonly Stack<UndoUnit> _undo = new();
        private readonly Stack<UndoUnit> _redo = new();

        public RecordingUndoEngine(IServiceProvider provider)
            : base(provider)
        {
        }

        public int UndoCount => _undo.Count;

        protected override void AddUndoUnit(UndoUnit unit)
        {
            _undo.Push(unit);
            _redo.Clear();
        }

        public bool UndoOnce()
        {
            if (_undo.Count == 0)
                return false;
            UndoUnit unit = _undo.Pop();
            unit.Undo();
            _redo.Push(unit);
            return true;
        }

        public bool RedoOnce()
        {
            if (_redo.Count == 0)
                return false;
            UndoUnit unit = _redo.Pop();
            unit.Undo();
            _undo.Push(unit);
            return true;
        }
    }
}
