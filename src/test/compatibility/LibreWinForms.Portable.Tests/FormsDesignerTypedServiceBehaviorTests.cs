using System;
using System.ComponentModel.Design;
using Forms = System.Windows.Forms;
using FormsDesign = System.Windows.Forms.Design;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class FormsDesignerTypedServiceBehaviorTests
{
    public static void Run()
    {
        CleanupServiceSurvivesPortableSurfaceUnloading();
        ToolStripKeyboardServiceMovesTypedSelection();
        ToolStripKeyboardServiceFailsClosedWithoutAnOwnedItem();
        Console.WriteLine("LibreWinForms Forms Designer typed-service tests passed: cleanup=2 keyboard=3 navigation=2 failClosed=1.");
    }

    private static void CleanupServiceSurvivesPortableSurfaceUnloading()
    {
        var surface = new DesignSurface();
        var host = (IDesignerHost)surface.GetService(typeof(IDesignerHost))!;
        var hostCleanup = host.GetService(typeof(IPortableDesignSurfaceServiceCleanup))
            as IPortableDesignSurfaceServiceCleanup
            ?? throw new InvalidOperationException("The portable designer host did not publish its typed cleanup service.");
        Assert(ReferenceEquals(hostCleanup, surface.GetService(typeof(IPortableDesignSurfaceServiceCleanup))),
            "The surface and host did not publish one stable cleanup service.");

        IPortableDesignSurfaceServiceCleanup? unloadedCleanup = null;
        surface.Unloaded += (_, _) =>
        {
            unloadedCleanup = surface.GetService(typeof(IPortableDesignSurfaceServiceCleanup))
                as IPortableDesignSurfaceServiceCleanup;
            unloadedCleanup?.RemoveDesignerHostServices();
            unloadedCleanup?.RemoveDesignerHostServices();
        };

        surface.Dispose();
        Assert(ReferenceEquals(hostCleanup, unloadedCleanup),
            "The typed cleanup service was unavailable during the post-host Unloaded callback.");

        Type[] hostAliases =
        {
            typeof(IDesignerHost),
            typeof(System.ComponentModel.Design.Serialization.IDesignerLoaderHost),
            typeof(System.ComponentModel.Design.Serialization.IDesignerLoaderHost2),
            typeof(System.ComponentModel.IContainer),
            typeof(IComponentChangeService)
        };
        foreach (Type hostAlias in hostAliases)
        {
            Assert(host.GetService(hostAlias) is null,
                $"The disposed portable designer host still returned its {hostAlias.Name} alias.");
        }

        hostCleanup.RemoveDesignerHostServices();
    }

    private static void ToolStripKeyboardServiceMovesTypedSelection()
    {
        using var surface = new DesignSurface();
        var host = (IDesignerHost)surface.GetService(typeof(IDesignerHost))!;
        var selection = (ISelectionService)host.GetService(typeof(ISelectionService))!;
        var keyboard = host.GetService(typeof(FormsDesign.IPortableToolStripKeyboardHandlingService))
            as FormsDesign.IPortableToolStripKeyboardHandlingService
            ?? throw new InvalidOperationException("The portable designer host did not publish its typed ToolStrip keyboard service.");
        Assert(!keyboard.TemplateNodeActive, "The portable designer reported a template node without an active template editor.");

        var toolStrip = (Forms.ToolStrip)host.CreateComponent(typeof(Forms.ToolStrip), "typedToolStrip");
        var first = (Forms.ToolStripButton)host.CreateComponent(typeof(Forms.ToolStripButton), "firstTypedButton");
        var second = (Forms.ToolStripButton)host.CreateComponent(typeof(Forms.ToolStripButton), "secondTypedButton");
        toolStrip.Items.Add(first);
        toolStrip.Items.Add(second);
        selection.SetSelectedComponents(new object[] { first }, SelectionTypes.Replace);

        keyboard.ProcessUpDown(down: true);
        Assert(ReferenceEquals(selection.PrimarySelection, second),
            "Down navigation did not move to the next ToolStrip item through the typed selection service.");
        keyboard.ProcessUpDown(down: false);
        Assert(ReferenceEquals(selection.PrimarySelection, first),
            "Up navigation did not move to the previous ToolStrip item through the typed selection service.");
    }

    private static void ToolStripKeyboardServiceFailsClosedWithoutAnOwnedItem()
    {
        using var surface = new DesignSurface();
        var host = (IDesignerHost)surface.GetService(typeof(IDesignerHost))!;
        var selection = (ISelectionService)host.GetService(typeof(ISelectionService))!;
        var keyboard = (FormsDesign.IPortableToolStripKeyboardHandlingService)host.GetService(
            typeof(FormsDesign.IPortableToolStripKeyboardHandlingService))!;
        _ = host.CreateComponent(typeof(Forms.Panel), "typedRootPanel");
        var orphan = (Forms.ToolStripButton)host.CreateComponent(typeof(Forms.ToolStripButton), "orphanTypedButton");
        selection.SetSelectedComponents(new object[] { orphan }, SelectionTypes.Replace);

        keyboard.ProcessUpDown(down: true);
        keyboard.ProcessUpDown(down: false);
        Assert(ReferenceEquals(selection.PrimarySelection, orphan),
            "Keyboard navigation changed selection for a ToolStrip item without an owning collection.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
