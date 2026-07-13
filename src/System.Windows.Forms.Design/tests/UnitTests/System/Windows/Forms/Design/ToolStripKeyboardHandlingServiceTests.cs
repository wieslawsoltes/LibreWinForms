// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.Design;
using Moq;

namespace System.Windows.Forms.Design.Tests;

public class ToolStripKeyboardHandlingServiceTests
{
    [WinFormsFact]
    public void Constructor_RegistersPortableKeyboardContract()
    {
        Mock<IServiceProvider> provider = new();
        Mock<IDesignerHost> host = new();
        Mock<IComponentChangeService> componentChangeService = new();
        provider.Setup(serviceProvider => serviceProvider.GetService(typeof(IDesignerHost))).Returns(host.Object);
        host.Setup(designerHost => designerHost.GetService(typeof(IComponentChangeService))).Returns(componentChangeService.Object);

        ToolStripKeyboardHandlingService service = new(provider.Object);

        host.Verify(
            designerHost => designerHost.AddService(typeof(IPortableToolStripKeyboardHandlingService), service),
            Times.Once);
    }

    [WinFormsFact]
    public void PortableKeyboardContract_ExposesTemplateStateAndFailsClosedWithoutSelectionServices()
    {
        Mock<IServiceProvider> provider = new();
        Mock<IDesignerHost> host = new();
        Mock<IComponentChangeService> componentChangeService = new();
        provider.Setup(serviceProvider => serviceProvider.GetService(typeof(IDesignerHost))).Returns(host.Object);
        host.Setup(designerHost => designerHost.GetService(typeof(IComponentChangeService))).Returns(componentChangeService.Object);
        ToolStripKeyboardHandlingService service = new(provider.Object);
        IPortableToolStripKeyboardHandlingService portableService = service;

        Assert.False(portableService.TemplateNodeActive);
        service.TemplateNodeActive = true;
        Assert.True(portableService.TemplateNodeActive);

        portableService.ProcessUpDown(down: true);
        portableService.ProcessUpDown(down: false);
    }
}
