// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing;
using System.Windows.Forms;
using FluentAssertions;
using FluentAssertions.Execution;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Fact]
    public void PortablePropertyGridMetricsUseReference192()
    {
        if (RunDpiCaseInNewProcess()) return;
        VerifyPropertyGridMetrics(2, HighDpiMode.SystemAware, GraphicsUnit.Point, 192);
    }

    [Fact]
    public void PortablePropertyGridMetricsUseFractionalReference144()
    {
        if (RunDpiCaseInNewProcess()) return;
        VerifyPropertyGridMetrics(1.5, HighDpiMode.SystemAware, GraphicsUnit.Point, 144);
    }

    [Fact]
    public void PortablePropertyGridMetricsKeepLogical96()
    {
        if (RunDpiCaseInNewProcess()) return;
        VerifyPropertyGridMetrics(2, HighDpiMode.DpiUnaware, GraphicsUnit.Point, 96);
    }

    [Fact]
    public void PortablePropertyGridMetricsKeepPixelFontUnits()
    {
        if (RunDpiCaseInNewProcess()) return;
        VerifyPropertyGridMetrics(2, HighDpiMode.SystemAware, GraphicsUnit.Pixel, 192);
    }

    [Fact]
    public void PortablePropertyGridMetricsRefreshOnResizeAfterFontChange()
    {
        if (RunDpiCaseInNewProcess()) return;
        VerifyPropertyGridMetrics(2, HighDpiMode.SystemAware, GraphicsUnit.Point, 192, changeFont: true);
    }

    private static void VerifyPropertyGridMetrics(double scale, HighDpiMode mode, GraphicsUnit unit,
        float dpi, bool changeFont = false)
    {
        HeadlessPlatform platform = PrepareFontAutoScalePlatform(scale, mode);
        using Font font = new(FontFamily.GenericSansSerif, 9.25f, FontStyle.Regular, unit);
        using Font replacement = new(FontFamily.GenericSansSerif, 12.25f, FontStyle.Regular, unit);
        using Form form = new() { AutoScaleMode = AutoScaleMode.None, ClientSize = new Size(700, 1000), ShowIcon = false };
        using PropertyGrid grid = new()
        {
            Font = font, Bounds = new Rectangle(0, 0, 700, 1000),
            ToolbarVisible = false, HelpVisible = true, PropertySort = PropertySort.Alphabetical,
        };
        using PropertyGridDpiComponent component = new();
        component.Site = new PropertyGridDpiSite(component);
        form.Controls.Add(grid);
        form.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        grid.SelectedObject = component;
        grid.Focus();
        // PropertyGrid forwards focus to its source view/editor, not itself.
        grid.ContainsFocus.Should().BeTrue();
        grid.SelectedGridItem.Should().NotBeNull();
        grid.SelectedGridItem!.Label.Should().Be(nameof(PropertyGridDpiComponent.Caption));
        grid.SelectedGridItem.Select().Should().BeTrue();
        AssertMetrics();
        if (changeFont)
        {
            grid.Font = replacement;
            grid.SelectedGridItem.Select().Should().BeTrue();
            // Canonical PropertyGrid arranges its own panes on resize, not
            // on ordinary child layout. Reflow using the invalidated metrics.
            grid.Width += 1;
            AssertMetrics();
        }

        void AssertMetrics()
        {
            using AssertionScope scope = new();
            int height = (int)Math.Ceiling(grid.Font.GetHeight(dpi));
            Control[] children = Descendants(grid).ToArray();
            TextBox editor = children.OfType<TextBox>().Single(control => control.Visible);
            editor.Text.Should().Be("Alpha");
            editor.Height.Should().Be(height + 1,
                "the source editor uses row font height +2, less its existing one-pixel inset");

            Label title = children.OfType<Label>().Single(control => control.Text == nameof(PropertyGridDpiComponent.Caption));
            Label description = children.OfType<Label>().Single(control => control.Text == PropertyGridDpiComponent.Help);
            title.Parent.Should().BeSameAs(description.Parent);
            Control help = title.Parent!;
            help.Width += 1; // Exercise the actual source OnResize metric path.
            int padding = (int)Math.Round(dpi / 96f * 2);
            title.Height.Should().Be(height + padding);
            (description.Top - title.Top).Should().Be(height + padding);

            grid.CommandsVisible.Should().BeTrue();
            LinkLabel commands = children.OfType<LinkLabel>().Single();
            commands.Links.Count.Should().Be(2);
            commands.Parent!.Height.Should().Be(2 * (int)(1.5 * height) + 8);
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class PropertyGridDpiComponent : Component
    {
        internal const string Help = "A description for the selected DPI fixture property.";

        [Description(Help)]
        [DefaultValue("Alpha")]
        public string Caption { get; set; } = "Alpha";
    }

    private sealed class PropertyGridDpiSite(IComponent component) : ISite
    {
        private readonly PropertyGridDpiCommands _commands = new();
        public IComponent Component => component;
        public IContainer? Container => null;
        public bool DesignMode => false;
        public string? Name { get; set; }
        public object? GetService(Type serviceType) => serviceType == typeof(IMenuCommandService) ? _commands : null;
    }

    private sealed class PropertyGridDpiCommands : IMenuCommandService
    {
        public DesignerVerbCollection Verbs { get; } = new([
            new DesignerVerb("First action", (_, _) => { }),
            new DesignerVerb("Second action", (_, _) => { })]);
        public void AddCommand(MenuCommand command) => throw new NotSupportedException();
        public void AddVerb(DesignerVerb verb) => throw new NotSupportedException();
        public MenuCommand? FindCommand(CommandID commandID) => null;
        public bool GlobalInvoke(CommandID commandID) => false;
        public void RemoveCommand(MenuCommand command) => throw new NotSupportedException();
        public void RemoveVerb(DesignerVerb verb) => throw new NotSupportedException();
        public void ShowContextMenu(CommandID menuID, int x, int y) => throw new NotSupportedException();
    }
}
