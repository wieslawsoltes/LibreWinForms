// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing;
using System.Drawing.Design;
using System.Windows.Forms.Design.Behavior;
using Moq;

namespace System.Windows.Forms.Design.Tests;

public class DesignerLayoutContractTests
{
    [Fact]
    public void CanonicalLayout_UsesUpstreamGridSelectionCommandToolboxAndAdornerOwners()
    {
        EnsurePortableBackend();

        using LayoutDesignSurface surface = new();
        IDesignerHost host = Assert.IsAssignableFrom<IDesignerHost>(surface.GetService(typeof(IDesignerHost)));
        Panel root = Assert.IsType<Panel>(host.CreateComponent(typeof(Panel), "rootPanel"));
        Button button = Assert.IsType<Button>(host.CreateComponent(typeof(Button), "moveButton"));
        root.Controls.Add(button);
        button.Bounds = new Rectangle(13, 17, 40, 30);

        using Control snapControl = new();
        using ParentControlDesigner parentDesigner = new();
        parentDesigner.Initialize(snapControl);
        parentDesigner.TestAccessor.Dynamic.GridSize = new Size(10, 10);
        parentDesigner.TestAccessor.Dynamic._checkSnapLineSetting = false;
        parentDesigner.TestAccessor.Dynamic._defaultUseSnapLines = false;
        parentDesigner.TestAccessor.Dynamic.SnapToGrid = true;

        Assert.Equal(
            new Rectangle(10, 10, 24, 24),
            parentDesigner.GetSnappedRect(
                new Rectangle(8, 8, 24, 24),
                new Rectangle(15, 15, 24, 24),
                updateSize: false));
        Assert.Equal(
            new Rectangle(20, 20, 24, 24),
            parentDesigner.GetSnappedRect(
                new Rectangle(8, 8, 24, 24),
                new Rectangle(16, 16, 24, 24),
                updateSize: false));
        Assert.Equal(
            new Rectangle(20, 20, 10, 10),
            parentDesigner.GetSnappedRect(
                new Rectangle(20, 20, 20, 20),
                new Rectangle(20, 20, 4, 4),
                updateSize: true));

        object[] components = [button];
        LayoutSelectionHandler handler = new(host, root, parentDesigner);
        Assert.True(handler.BeginDrag(components, SelectionRules.Moveable, 0, 0));
        handler.DragMoved(components, new Rectangle(6, 7, 0, 0));
        Assert.Equal(new Point(20, 20), button.Location);
        handler.EndDrag(components, cancel: true);
        Assert.Equal(new Point(13, 17), button.Location);

        Assert.True(handler.BeginDrag(components, SelectionRules.Moveable, 0, 0));
        handler.DragMoved(components, new Rectangle(6, 7, 0, 0));
        handler.EndDrag(components, cancel: false);
        Assert.Equal(new Point(20, 20), button.Location);

        ISelectionService selection = Assert.IsAssignableFrom<ISelectionService>(host.GetService(typeof(ISelectionService)));
        selection.SetSelectedComponents(new object[] { button }, SelectionTypes.Replace);
        Assert.Same(button, selection.PrimarySelection);
        Assert.Equal(1, selection.SelectionCount);

        ToolboxItem toolboxItem = new(typeof(Button));
        IComponent[]? created = toolboxItem.CreateComponents(host);
        Button toolboxButton = Assert.IsType<Button>(Assert.Single(Assert.IsAssignableFrom<IComponent[]>(created)));
        Assert.Same(host.Container, toolboxButton.Site?.Container);

        AssertCanonicalKeyboardCommandsAreRegistered(root);

        BehaviorServiceAdornerCollection adorners = new((BehaviorService?)null);
        Adorner first = new();
        Adorner second = new();
        ProbeGlyph glyph = new();
        first.Glyphs.Add(glyph);
        adorners.AddRange(first, second);
        first.Enabled = false;

        Assert.Equal(2, adorners.Count);
        Assert.Same(first, adorners[0]);
        Assert.Same(second, adorners[1]);
        Assert.True(adorners.Contains(first));
        Assert.False(first.Enabled);
        Assert.Same(glyph, Assert.Single(first.Glyphs.Cast<Glyph>()));
    }

    private static void AssertCanonicalKeyboardCommandsAreRegistered(Control root)
    {
        Mock<ISite> site = new();
        Mock<IDesignerHost> host = new();
        Mock<IMenuCommandService> menu = new();
        Mock<IEventHandlerService> events = new();
        Mock<ISelectionService> selection = new();
        Mock<IDictionaryService> dictionary = new();
        List<CommandID> registered = [];

        site.Setup(value => value.GetService(typeof(IDesignerHost))).Returns(host.Object);
        site.Setup(value => value.GetService(typeof(IMenuCommandService))).Returns(menu.Object);
        site.Setup(value => value.GetService(typeof(IEventHandlerService))).Returns(events.Object);
        site.Setup(value => value.GetService(typeof(ISelectionService))).Returns(selection.Object);
        site.Setup(value => value.GetService(typeof(IDictionaryService))).Returns(dictionary.Object);
        host.Setup(value => value.RootComponent).Returns(root);
        menu.Setup(value => value.AddCommand(It.IsAny<MenuCommand>()))
            .Callback<MenuCommand>(command => registered.Add(command.CommandID!));

        using ControlCommandSet commandSet = new(site.Object);

        CommandID[] expected =
        [
            MenuCommands.KeyMoveUp,
            MenuCommands.KeyMoveDown,
            MenuCommands.KeyMoveLeft,
            MenuCommands.KeyMoveRight,
            MenuCommands.KeyNudgeUp,
            MenuCommands.KeyNudgeDown,
            MenuCommands.KeyNudgeLeft,
            MenuCommands.KeyNudgeRight,
            MenuCommands.KeySizeWidthIncrease,
            MenuCommands.KeySizeWidthDecrease,
            MenuCommands.KeySizeHeightIncrease,
            MenuCommands.KeySizeHeightDecrease,
            MenuCommands.KeyNudgeWidthIncrease,
            MenuCommands.KeyNudgeWidthDecrease,
            MenuCommands.KeyNudgeHeightIncrease,
            MenuCommands.KeyNudgeHeightDecrease,
        ];

        Assert.All(expected, command => Assert.Contains(command, registered));
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

    private sealed class LayoutSelectionHandler : SelectionUIHandler
    {
        private readonly IDesignerHost _host;
        private readonly Control _root;
        private readonly ParentControlDesigner _parentDesigner;

        public LayoutSelectionHandler(IDesignerHost host, Control root, ParentControlDesigner parentDesigner)
        {
            _host = host;
            _root = root;
            _parentDesigner = parentDesigner;
        }

        protected override IComponent GetComponent() => _root;

        protected override Control GetControl() => _root;

        protected override Control GetControl(IComponent component) => Assert.IsAssignableFrom<Control>(component);

        protected override Size GetCurrentSnapSize() => new(10, 10);

        protected override object? GetService(Type serviceType) => _host.GetService(serviceType);

        protected override bool GetShouldSnapToGrid() => true;

        public override Rectangle GetUpdatedRect(Rectangle orignalRect, Rectangle dragRect, bool updateSize)
            => _parentDesigner.GetSnappedRect(orignalRect, dragRect, updateSize);

        public override void SetCursor()
        {
        }
    }

    private sealed class LayoutDesignSurface : DesignSurface
    {
        protected internal override IDesigner? CreateDesigner(IComponent component, bool rootDesigner)
            => rootDesigner ? new LayoutRootDesigner() : null;
    }

#pragma warning disable CS0618 // IRootDesigner requires the legacy ViewTechnology contract.
    private sealed class LayoutRootDesigner : ComponentDesigner, IRootDesigner
    {
        public ViewTechnology[] SupportedTechnologies => [ViewTechnology.Default];

        public object GetView(ViewTechnology technology) => Component;
    }
#pragma warning restore CS0618

    private sealed class ProbeGlyph : Glyph
    {
        public ProbeGlyph()
            : base(null)
        {
        }

        public override Cursor? GetHitTest(Point p) => null;

        public override void Paint(PaintEventArgs pe)
        {
        }
    }
}
