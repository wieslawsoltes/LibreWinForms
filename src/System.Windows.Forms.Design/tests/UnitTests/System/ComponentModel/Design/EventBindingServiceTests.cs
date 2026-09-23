// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.ComponentModel.Design.Serialization;

namespace System.ComponentModel.Design.Tests;

public class EventBindingServiceTests
{
    [Fact]
    public void ServiceResolution_LocalRegistrationOverridesParentAndRemovalRestoresFallback()
    {
        using ServiceContainer parentServices = new();
        ProbeEventBindingService parentBinding = new(parentServices);
        parentServices.AddService<IEventBindingService>(parentBinding);

        using EventBindingDesignSurface surface = new(parentServices);
        IDesignerHost host = Assert.IsAssignableFrom<IDesignerHost>(surface.GetService(typeof(IDesignerHost)));
        IServiceContainer hostServices = host;

        Assert.Same(parentBinding, host.GetService(typeof(IEventBindingService)));

        ProbeEventBindingService localBinding = new(host);
        hostServices.AddService<IEventBindingService>(localBinding);
        Assert.Same(localBinding, host.GetService(typeof(IEventBindingService)));
        Assert.Same(localBinding, surface.GetService(typeof(IEventBindingService)));

        hostServices.RemoveService<IEventBindingService>();
        Assert.Same(parentBinding, host.GetService(typeof(IEventBindingService)));
    }

    [Fact]
    public void EventProperty_SetResetAndReuse_UseTransactionsNotificationsAndCodeDomUndo()
    {
        EnsurePortableBackend();

        using EventBindingFixture fixture = new();
        string firstName = fixture.Bindings.CreateUniqueMethodName(fixture.Button, fixture.ClickEvent);
        Assert.Equal("eventButton_Click", firstName);
        Assert.Equal(firstName, fixture.Bindings.CreateUniqueMethodName(fixture.Button, fixture.ClickEvent));

        List<string> changeSequence = [];
        fixture.ChangeService.ComponentChanging += OnChanging;
        fixture.ChangeService.ComponentChanged += OnChanged;

        int openedTransactions = 0;
        int committedTransactions = 0;
        fixture.Host.TransactionOpened += (_, _) => openedTransactions++;
        fixture.Host.TransactionClosed += (_, e) => committedTransactions += e.TransactionCommitted ? 1 : 0;

        using RecordingUndoEngine undo = new(fixture.Host);
        fixture.Property.SetValue(fixture.Button, firstName);

        Assert.Equal(firstName, fixture.Property.GetValue(fixture.Button));
        Assert.Equal(1, openedTransactions);
        Assert.Equal(1, committedTransactions);
        Assert.Equal(
            ["changing:property:Click", "changing:event:Click", "changed:event:Click", "changed:property:Click"],
            changeSequence);
        Assert.Equal(1, undo.UndoCount);

        fixture.ChangeService.ComponentChanging -= OnChanging;
        fixture.ChangeService.ComponentChanged -= OnChanged;

        Assert.True(undo.UndoOnce());
        Assert.Null(fixture.Property.GetValue(fixture.Button));
        Assert.True(undo.RedoOnce());
        Assert.Equal(firstName, fixture.Property.GetValue(fixture.Button));

        int undoCountBeforeReuse = undo.UndoCount;
        fixture.Property.SetValue(fixture.Button, firstName);
        Assert.Equal(undoCountBeforeReuse, undo.UndoCount);

        fixture.Property.ResetValue(fixture.Button);
        Assert.Null(fixture.Property.GetValue(fixture.Button));
        Assert.Equal(undoCountBeforeReuse + 1, undo.UndoCount);
        Assert.True(undo.UndoOnce());
        Assert.Equal(firstName, fixture.Property.GetValue(fixture.Button));

        void OnChanging(object? sender, ComponentChangingEventArgs e)
        {
            if (ReferenceEquals(e.Component, fixture.Button))
            {
                changeSequence.Add((e.Member is EventDescriptor ? "changing:event:" : "changing:property:") + e.Member?.Name);
            }
        }

        void OnChanged(object? sender, ComponentChangedEventArgs e)
        {
            if (!ReferenceEquals(e.Component, fixture.Button))
            {
                return;
            }

            changeSequence.Add((e.Member is EventDescriptor ? "changed:event:" : "changed:property:") + e.Member?.Name);
            if (e.Member is EventDescriptor)
            {
                Assert.Null(e.OldValue);
                Assert.Null(e.NewValue);
            }
            else
            {
                Assert.Null(e.OldValue);
                Assert.Equal(firstName, e.NewValue);
            }
        }
    }

    [Fact]
    public void EventProperty_PreMutationFailures_PreserveExistingBindingAndCancelTransaction()
    {
        EnsurePortableBackend();

        using EventBindingFixture fixture = new();
        fixture.Property.SetValue(fixture.Button, EventBindingFixture.OriginalHandler);
        fixture.ResetTransactionCounts();

        ComponentChangingEventHandler cancel = (_, _) => throw CheckoutException.Canceled;
        fixture.ChangeService.ComponentChanging += cancel;
        fixture.Property.SetValue(fixture.Button, "replacementHandler");
        fixture.ChangeService.ComponentChanging -= cancel;
        Assert.Equal(EventBindingFixture.OriginalHandler, fixture.Property.GetValue(fixture.Button));
        Assert.Equal(1, fixture.CanceledTransactions);

        ComponentChangingEventHandler failChanging = (_, _) => throw new InvalidOperationException("changing");
        fixture.ChangeService.ComponentChanging += failChanging;
        Assert.Throws<InvalidOperationException>(
            () => fixture.Property.SetValue(fixture.Button, "replacementHandler"));
        fixture.ChangeService.ComponentChanging -= failChanging;
        Assert.Equal(EventBindingFixture.OriginalHandler, fixture.Property.GetValue(fixture.Button));

        fixture.Binding.ThrowOnUseMethodName = "replacementHandler";
        Assert.Throws<InvalidOperationException>(
            () => fixture.Property.SetValue(fixture.Button, "replacementHandler"));
        Assert.Equal(EventBindingFixture.OriginalHandler, fixture.Property.GetValue(fixture.Button));
        Assert.DoesNotContain(EventBindingFixture.OriginalHandler, fixture.Binding.FreeCalls);
    }

    private static void EnsurePortableBackend()
    {
#if LIBREWINFORMS_PORTABLE
        if (!LibreWinForms.Platform.LibrePlatform.IsRegistered)
        {
            LibreWinForms.ProGPU.ProGpuPlatform.Register();
        }
#endif
    }

    private sealed class EventBindingFixture : IDisposable
    {
        public const string OriginalHandler = "originalHandler";

        public EventBindingFixture()
        {
            Surface = new EventBindingDesignSurface();
            Host = Assert.IsAssignableFrom<IDesignerHost>(Surface.GetService(typeof(IDesignerHost)));
            IServiceContainer hostServices = Host;
            Binding = new ProbeEventBindingService(Host);
            hostServices.AddService<IEventBindingService>(Binding);
            hostServices.AddService<ComponentSerializationService>(new CodeDomComponentSerializationService(Host));
            _ = Host.CreateComponent(typeof(Panel), "rootPanel");
            Button = (Button)Host.CreateComponent(typeof(Button), "eventButton");
            ClickEvent = TypeDescriptor.GetEvents(Button)[nameof(Control.Click)]!;
            Property = ((IEventBindingService)Binding).GetEventProperty(ClickEvent);
            ChangeService = Assert.IsAssignableFrom<IComponentChangeService>(Host.GetService(typeof(IComponentChangeService)));
            Host.TransactionClosed += OnTransactionClosed;
        }

        public EventBindingDesignSurface Surface { get; }

        public IDesignerHost Host { get; }

        public ProbeEventBindingService Binding { get; }

        public IEventBindingService Bindings => Binding;

        public Button Button { get; }

        public EventDescriptor ClickEvent { get; }

        public PropertyDescriptor Property { get; }

        public IComponentChangeService ChangeService { get; }

        public int CanceledTransactions { get; private set; }

        public void ResetTransactionCounts() => CanceledTransactions = 0;

        public void Dispose() => Surface.Dispose();

        private void OnTransactionClosed(object? sender, DesignerTransactionCloseEventArgs e)
            => CanceledTransactions += e.TransactionCommitted ? 0 : 1;
    }

    private sealed class ProbeEventBindingService : EventBindingService
    {
        public ProbeEventBindingService(IServiceProvider provider)
            : base(provider)
        {
        }

        public string? ThrowOnUseMethodName { get; set; }

        public List<string> UseCalls { get; } = [];

        public List<string> FreeCalls { get; } = [];

        protected override string CreateUniqueMethodName(IComponent component, EventDescriptor e)
            => component.Site?.Name + "_" + e.Name;

        protected override ICollection GetCompatibleMethods(EventDescriptor e) => Array.Empty<string>();

        protected override void UseMethod(IComponent component, EventDescriptor e, string methodName)
        {
            UseCalls.Add(methodName);
            if (string.Equals(methodName, ThrowOnUseMethodName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("UseMethod failure");
            }
        }

        protected override void FreeMethod(IComponent component, EventDescriptor e, string methodName)
            => FreeCalls.Add(methodName);

        protected override bool ShowCode() => true;

        protected override bool ShowCode(int lineNumber) => lineNumber > 0;

        protected override bool ShowCode(IComponent component, EventDescriptor e, string methodName) => true;
    }

    private sealed class RecordingUndoEngine : UndoEngine
    {
        private readonly Stack<UndoUnit> _undo = [];
        private readonly Stack<UndoUnit> _redo = [];

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
            if (!_undo.TryPop(out UndoUnit? unit))
            {
                return false;
            }

            unit.Undo();
            _redo.Push(unit);
            return true;
        }

        public bool RedoOnce()
        {
            if (!_redo.TryPop(out UndoUnit? unit))
            {
                return false;
            }

            unit.Undo();
            _undo.Push(unit);
            return true;
        }
    }

    private sealed class EventBindingDesignSurface : DesignSurface
    {
        public EventBindingDesignSurface(IServiceProvider? parentProvider = null)
            : base(parentProvider)
        {
        }

        protected internal override IDesigner? CreateDesigner(IComponent component, bool rootDesigner)
            => rootDesigner ? new EventBindingRootDesigner() : null;
    }

#pragma warning disable CS0618 // IRootDesigner requires the legacy ViewTechnology contract.
    private sealed class EventBindingRootDesigner : ComponentDesigner, IRootDesigner
    {
        public ViewTechnology[] SupportedTechnologies => [ViewTechnology.Default];

        public object GetView(ViewTechnology technology) => Component;
    }
#pragma warning restore CS0618
}
