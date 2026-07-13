using System;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing;
using FormsDesign = System.Windows.Forms.Design;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class FormsDesignerOptionsBehaviorTests
{
    public static void Run()
    {
        DesignerOptionsExposeNativeGridDefaults();
        WindowsFormsDesignerOptionsSupportSharpStylePropertySetting();
        Console.WriteLine("LibreWinForms Forms Designer option contracts passed: defaults=8 clamp=2 setting=7.");
    }

    private static void DesignerOptionsExposeNativeGridDefaults()
    {
        var options = new FormsDesign.DesignerOptions();
        Assert(options.GridSize == new Size(8, 8), "DesignerOptions did not expose the native 8x8 grid default.");
        Assert(options.ShowGrid, "DesignerOptions did not enable the design grid by default.");
        Assert(options.SnapToGrid, "DesignerOptions did not enable grid snapping by default.");
        Assert(!options.UseSnapLines, "DesignerOptions enabled snap lines by default.");

        var sharpOptions = new GetterOnlyDesignerOptions(new Size(12, 14), showGrid: false, snapToGrid: false, useSnapLines: true);
        Assert(sharpOptions.GridSize == new Size(12, 14), "Getter-only DesignerOptions override lost its grid value.");
        Assert(!sharpOptions.ShowGrid && !sharpOptions.SnapToGrid && sharpOptions.UseSnapLines,
            "Getter-only DesignerOptions overrides lost their SharpDevelop-compatible values.");
    }

    private static void WindowsFormsDesignerOptionsSupportSharpStylePropertySetting()
    {
        var service = new FormsDesign.WindowsFormsDesignerOptionService();
        DesignerOptionService.DesignerOptionCollection root = service.Options;
        DesignerOptionService.DesignerOptionCollection? page = root["WindowsFormsDesigner"];
        Assert(page is not null, "WindowsFormsDesigner option page was not populated.");
        Assert(root["DesignerOptions"] is null, "Portable options changed away from the WindowsFormsDesigner page.");

        PropertyDescriptor gridSize = RequireProperty(root.Properties, nameof(service.GridSize));
        PropertyDescriptor showGrid = RequireProperty(root.Properties, nameof(service.ShowGrid));
        PropertyDescriptor snapToGrid = RequireProperty(root.Properties, nameof(service.SnapToGrid));
        PropertyDescriptor useSnapLines = RequireProperty(root.Properties, nameof(service.UseSnapLines));

        Assert((Size)gridSize.GetValue(service)! == new Size(8, 8), "WindowsFormsDesigner service grid default changed.");
        Assert((bool)showGrid.GetValue(service)!, "WindowsFormsDesigner service ShowGrid default changed.");
        Assert((bool)snapToGrid.GetValue(service)!, "WindowsFormsDesigner service SnapToGrid default changed.");
        Assert(!(bool)useSnapLines.GetValue(service)!, "WindowsFormsDesigner service UseSnapLines default changed.");

        gridSize.SetValue(service, new Size(1, 201));
        Assert(service.GridSize == new Size(2, 200), "WindowsFormsDesigner did not clamp both grid dimensions to 2..200.");
        gridSize.SetValue(service, new Size(32, 24));
        showGrid.SetValue(service, false);
        snapToGrid.SetValue(service, false);
        useSnapLines.SetValue(service, true);
        Assert(service.GridSize == new Size(32, 24), "Sharp-style GridSize property setting did not reach the service.");
        Assert(!service.ShowGrid && !service.SnapToGrid && service.UseSnapLines,
            "Sharp-style boolean property setting did not reach the service.");
    }

    private static PropertyDescriptor RequireProperty(PropertyDescriptorCollection properties, string name)
    {
        return properties.Find(name, ignoreCase: false)
            ?? throw new InvalidOperationException($"Designer option property '{name}' is missing.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class GetterOnlyDesignerOptions : FormsDesign.DesignerOptions
    {
        private readonly Size _gridSize;
        private readonly bool _showGrid;
        private readonly bool _snapToGrid;
        private readonly bool _useSnapLines;

        public GetterOnlyDesignerOptions(Size gridSize, bool showGrid, bool snapToGrid, bool useSnapLines)
        {
            _gridSize = gridSize;
            _showGrid = showGrid;
            _snapToGrid = snapToGrid;
            _useSnapLines = useSnapLines;
        }

        public override Size GridSize => _gridSize;

        public override bool ShowGrid => _showGrid;

        public override bool SnapToGrid => _snapToGrid;

        public override bool UseSnapLines => _useSnapLines;
    }
}
