// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.ComponentModel.Design;
using Moq;

namespace System.Windows.Forms.Design.Tests;

public class ToolStripKeyboardHandlingServiceTests
{
    [Fact]
    public void Constructor_RegistersCanonicalService()
    {
        Mock<IServiceProvider> provider = new();
        Mock<IDesignerHost> host = new();
        Mock<IComponentChangeService> componentChangeService = new();
        provider.Setup(serviceProvider => serviceProvider.GetService(typeof(IDesignerHost))).Returns(host.Object);
        host.Setup(designerHost => designerHost.GetService(typeof(IComponentChangeService))).Returns(componentChangeService.Object);

        ToolStripKeyboardHandlingService service = new(provider.Object);

        host.Verify(
            designerHost => designerHost.AddService(typeof(ToolStripKeyboardHandlingService), service),
            Times.Once);
    }

    [Fact]
    public void ProcessUpDown_WithoutSelectionServices_FailsClosed()
    {
        Mock<IServiceProvider> provider = new();
        Mock<IDesignerHost> host = new();
        Mock<IComponentChangeService> componentChangeService = new();
        provider.Setup(serviceProvider => serviceProvider.GetService(typeof(IDesignerHost))).Returns(host.Object);
        host.Setup(designerHost => designerHost.GetService(typeof(IComponentChangeService))).Returns(componentChangeService.Object);
        ToolStripKeyboardHandlingService service = new(provider.Object);

        Assert.False(service.TemplateNodeActive);
        service.TemplateNodeActive = true;
        Assert.True(service.TemplateNodeActive);

        service.ProcessUpDown(down: true);
        service.ProcessUpDown(down: false);
    }

    [Fact]
    public void ProcessUpDown_WithOwnedDropDownItems_MovesSelectionAndOrphanFailsClosed()
    {
#if LIBREWINFORMS_PORTABLE
        if (!LibreWinForms.Platform.LibrePlatform.IsRegistered)
        {
            LibreWinForms.ProGPU.ProGpuPlatform.Register();
        }
#endif

        using KeyboardDesignSurface surface = new();
        IDesignerHost host = Assert.IsAssignableFrom<IDesignerHost>(surface.GetService(typeof(IDesignerHost)));
        ISelectionService selection = Assert.IsAssignableFrom<ISelectionService>(host.GetService(typeof(ISelectionService)));
        _ = host.CreateComponent(typeof(Panel), "typedServiceRoot");
        var dropDown = (ContextMenuStrip)host.CreateComponent(typeof(ContextMenuStrip), "typedDropDown");
        var first = (ToolStripButton)host.CreateComponent(typeof(ToolStripButton), "firstTypedButton");
        var second = (ToolStripButton)host.CreateComponent(typeof(ToolStripButton), "secondTypedButton");
        var orphan = (ToolStripButton)host.CreateComponent(typeof(ToolStripButton), "orphanTypedButton");
        dropDown.Items.Add(first);
        dropDown.Items.Add(second);
        dropDown.PerformLayout();

        ToolStripKeyboardHandlingService service = new(host);
        selection.SetSelectedComponents(new object[] { first }, SelectionTypes.Replace);
        service.ProcessUpDown(down: true);
        Assert.Same(second, selection.PrimarySelection);
        service.ProcessUpDown(down: false);
        Assert.Same(first, selection.PrimarySelection);

        selection.SetSelectedComponents(new object[] { orphan }, SelectionTypes.Replace);
        service.ProcessUpDown(down: true);
        service.ProcessUpDown(down: false);
        Assert.Same(orphan, selection.PrimarySelection);

        service.RemoveCommands();
    }

    private sealed class KeyboardDesignSurface : DesignSurface
    {
        protected internal override IDesigner? CreateDesigner(IComponent component, bool rootDesigner)
            => rootDesigner ? new KeyboardRootDesigner() : null;
    }

#pragma warning disable CS0618 // IRootDesigner requires the legacy ViewTechnology contract.
    private sealed class KeyboardRootDesigner : ComponentDesigner, IRootDesigner
    {
        public ViewTechnology[] SupportedTechnologies => [ViewTechnology.Default];

        public object GetView(ViewTechnology technology) => Component;
    }
#pragma warning restore CS0618
}
