using System;
using System.ComponentModel;
using System.ComponentModel.Design;
using Forms = System.Windows.Forms;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class FormsDesignerMenuCommandBehaviorTests
{
    public static void Run()
    {
        RootAndSelectionVerbsMergeWithTypedDesignerPrecedence();
        SelectionAndTypeRefreshInvalidateTheCachedVerbSet();
        ExactAndVirtualVerbCommandsResolveAndInvoke();
        Console.WriteLine("LibreWinForms Forms Designer menu-command tests passed: selection=4 merge=4 cache=5 commands=8 dispose=1.");
    }

    private static void RootAndSelectionVerbsMergeWithTypedDesignerPrecedence()
    {
        using var fixture = new VerbFixture();
        var globalOnly = new DesignerVerb("Global only", (_, _) => { });
        var shadowedGlobal = new DesignerVerb("SHARED ACTION", (_, _) => { });
        var localWinner = new DesignerVerb("shared action", (_, _) => { });
        var rootLocal = new DesignerVerb("Root local", (_, _) => { });
        var childLocal = new DesignerVerb("Child local", (_, _) => { });
        var inheritedLocal = new DesignerVerb("Inherited local", (_, _) => { });

        fixture.RootDesigner.Verbs.Add(localWinner);
        fixture.RootDesigner.Verbs.Add(rootLocal);
        fixture.ChildDesigner.Verbs.Add(childLocal);
        fixture.InheritedDesigner.Verbs.Add(inheritedLocal);
        fixture.Commands.AddVerb(globalOnly);
        fixture.Commands.AddVerb(shadowedGlobal);

        fixture.Select(fixture.Root);
        DesignerVerbCollection rootVerbs = fixture.Commands.Verbs;
        Assert(rootVerbs.Count == 3, "The root verb set did not merge global and local verbs.");
        Assert(rootVerbs.Contains(globalOnly), "The root selection did not expose its global verb.");
        Assert(rootVerbs.Contains(localWinner) && !rootVerbs.Contains(shadowedGlobal),
            "A root designer verb did not win a case-insensitive caption collision.");
        Assert(rootVerbs.Contains(rootLocal), "The root designer local verb was omitted.");

        fixture.Select(fixture.Child);
        DesignerVerbCollection childVerbs = fixture.Commands.Verbs;
        Assert(childVerbs.Count == 1 && childVerbs.Contains(childLocal),
            "A non-root selection did not expose only its selected designer verbs.");
        Assert(!childVerbs.Contains(globalOnly), "A global verb leaked into a non-root selection.");

        fixture.Selection.SetSelectedComponents(
            new object[] { fixture.Root, fixture.Child },
            SelectionTypes.Replace);
        Assert(fixture.Commands.Verbs.Count == 0, "Designer verbs were exposed for a multi-selection.");

        fixture.Select(fixture.InheritedReadOnly);
        Assert(fixture.Commands.Verbs.Count == 0,
            "Designer verbs were exposed for an inherited read-only component.");
    }

    private static void SelectionAndTypeRefreshInvalidateTheCachedVerbSet()
    {
        using var fixture = new VerbFixture();
        var initial = new DesignerVerb("Initial", (_, _) => { });
        var refreshed = new DesignerVerb("Refreshed", (_, _) => { });
        fixture.ChildDesigner.Verbs.Add(initial);
        fixture.Select(fixture.Child);

        DesignerVerbCollection initialCache = fixture.Commands.Verbs;
        Assert(ReferenceEquals(initialCache, fixture.Commands.Verbs),
            "Repeated verb reads did not reuse the current selection cache.");
        fixture.ChildDesigner.Verbs.Add(refreshed);
        Assert(fixture.Commands.Verbs.Count == 1,
            "The selected designer verb cache changed without an invalidation signal.");

        TypeDescriptor.Refresh(typeof(VerbComponent));
        DesignerVerbCollection refreshedCache = fixture.Commands.Verbs;
        Assert(!ReferenceEquals(initialCache, refreshedCache),
            "TypeDescriptor.Refresh did not invalidate the selected designer verb cache.");
        Assert(refreshedCache.Count == 2 && refreshedCache.Contains(refreshed),
            "The selected designer verbs were not rebuilt after TypeDescriptor.Refresh.");

        fixture.Select(fixture.Root);
        DesignerVerbCollection emptyRootCache = fixture.Commands.Verbs;
        var global = new DesignerVerb("Late global", (_, _) => { });
        fixture.Commands.AddVerb(global);
        DesignerVerbCollection addedGlobalCache = fixture.Commands.Verbs;
        Assert(!ReferenceEquals(emptyRootCache, addedGlobalCache) && addedGlobalCache.Contains(global),
            "Adding a global verb did not invalidate the merged root verb cache.");
        fixture.Commands.RemoveVerb(global);
        Assert(!fixture.Commands.Verbs.Contains(global),
            "Removing a global verb did not invalidate the merged root verb cache.");

        fixture.Select(fixture.Child);
        Assert(!ReferenceEquals(refreshedCache, fixture.Commands.Verbs),
            "SelectionChanging did not invalidate the current designer verb cache.");
    }

    private static void ExactAndVirtualVerbCommandsResolveAndInvoke()
    {
        using var fixture = new VerbFixture();
        int firstInvocations = 0;
        int secondInvocations = 0;
        int exactInvocations = 0;
        int registeredInvocations = 0;
        var first = new DesignerVerb("First virtual", (_, _) => firstInvocations++);
        var exactId = new CommandID(new Guid("30F7C67B-A1D7-4265-B966-AE155096F762"), 73);
        var exact = new DesignerVerb("Exact", (_, _) => exactInvocations++, exactId);
        var second = new DesignerVerb("Second virtual", (_, _) => secondInvocations++);
        var registeredId = new CommandID(new Guid("9A5265D4-BE79-48EC-8B4E-74D130D2AE59"), 91);
        var registered = new MenuCommand((_, _) => registeredInvocations++, registeredId);

        fixture.ChildDesigner.Verbs.Add(first);
        fixture.ChildDesigner.Verbs.Add(exact);
        fixture.ChildDesigner.Verbs.Add(second);
        fixture.Select(fixture.Child);
        fixture.Commands.AddCommand(registered);

        Assert(ReferenceEquals(fixture.Commands.FindCommand(exactId), exact),
            "The selected designer's exact verb command was not resolved.");
        Assert(ReferenceEquals(fixture.Commands.FindCommand(StandardCommands.VerbFirst), first),
            "The first default designer verb did not resolve at StandardCommands.VerbFirst.");
        var secondVirtualId = new CommandID(
            StandardCommands.VerbFirst.Guid,
            StandardCommands.VerbFirst.ID + 1);
        Assert(ReferenceEquals(fixture.Commands.FindCommand(secondVirtualId), second),
            "Default designer verbs did not receive contiguous virtual command IDs.");
        Assert(ReferenceEquals(fixture.Commands.FindCommand(registeredId), registered),
            "An explicitly registered menu command was not resolved exactly.");

        Assert(fixture.Commands.GlobalInvoke(exactId) && exactInvocations == 1,
            "GlobalInvoke did not invoke an exact selected-designer verb.");
        Assert(fixture.Commands.GlobalInvoke(secondVirtualId) && secondInvocations == 1,
            "GlobalInvoke did not invoke a virtual selected-designer verb.");
        Assert(fixture.Commands.GlobalInvoke(registeredId) && registeredInvocations == 1,
            "GlobalInvoke did not invoke an explicitly registered command.");
        Assert(firstInvocations == 0, "Resolving another virtual verb invoked the first verb unexpectedly.");
    }

    private sealed class VerbFixture : IDisposable
    {
        private readonly VerbDesignSurface _surface = new();
        private readonly TypeDescriptionProvider _inheritanceProvider;

        public VerbFixture()
        {
            Host = (IDesignerHost)_surface.GetService(typeof(IDesignerHost))!;
            Selection = (ISelectionService)Host.GetService(typeof(ISelectionService))!;
            Root = (RootVerbControl)Host.CreateComponent(typeof(RootVerbControl), "verbRoot");
            Child = (VerbComponent)Host.CreateComponent(typeof(VerbComponent), "verbChild");
            InheritedReadOnly = (InheritedReadOnlyVerbComponent)Host.CreateComponent(
                typeof(InheritedReadOnlyVerbComponent),
                "inheritedVerbChild");
            _inheritanceProvider = TypeDescriptor.AddAttributes(
                InheritedReadOnly,
                InheritanceAttribute.InheritedReadOnly);
            RootDesigner = (RootVerbDesigner)Host.GetDesigner(Root)!;
            ChildDesigner = (VerbDesigner)Host.GetDesigner(Child)!;
            InheritedDesigner = (VerbDesigner)Host.GetDesigner(InheritedReadOnly)!;
            Commands = new MenuCommandService(Host);
        }

        public IDesignerHost Host { get; }

        public ISelectionService Selection { get; }

        public RootVerbControl Root { get; }

        public VerbComponent Child { get; }

        public InheritedReadOnlyVerbComponent InheritedReadOnly { get; }

        public RootVerbDesigner RootDesigner { get; }

        public VerbDesigner ChildDesigner { get; }

        public VerbDesigner InheritedDesigner { get; }

        public MenuCommandService Commands { get; }

        public void Select(object component)
        {
            Selection.SetSelectedComponents(new object[] { component }, SelectionTypes.Replace);
        }

        public void Dispose()
        {
            Commands.Dispose();
            TypeDescriptor.RemoveProvider(_inheritanceProvider, InheritedReadOnly);
            _surface.Dispose();
        }
    }

    private sealed class VerbDesignSurface : DesignSurface
    {
        protected override IDesigner? CreateDesigner(IComponent component, bool rootDesigner)
        {
            if (rootDesigner && component is RootVerbControl)
                return new RootVerbDesigner();
            if (component is VerbComponent or InheritedReadOnlyVerbComponent)
                return new VerbDesigner();

            return base.CreateDesigner(component, rootDesigner);
        }
    }

    private sealed class RootVerbControl : Forms.Panel
    {
    }

    private sealed class VerbComponent : Component
    {
    }

    private sealed class InheritedReadOnlyVerbComponent : Component
    {
    }

    private sealed class VerbDesigner : ComponentDesigner
    {
    }

    private sealed class RootVerbDesigner : ComponentDesigner, IRootDesigner
    {
        public ViewTechnology[] SupportedTechnologies => new[] { ViewTechnology.Default };

        public object GetView(ViewTechnology technology) => Component;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
