using System;
using System.Drawing;
using System.Linq;
using ProGPU.Scene;
using Forms = System.Windows.Forms;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class TextRendererBehaviorTests
{
    public static void Run()
    {
        MeasureAndDrawHexEditorTextThroughGraphics();
        DrawSelectionTextThroughHostedControl();
        Console.WriteLine(
            "LibreWinForms TextRenderer tests passed: measure=1 flags=1 prefix=1 graphics=1 hostedControl=1.");
    }

    private static void MeasureAndDrawHexEditorTextThroughGraphics()
    {
        using Graphics graphics = Graphics.FromHwnd(IntPtr.Zero);
        Font font = SystemFonts.DefaultFont;
        Size defaultSize = Forms.TextRenderer.MeasureText("_", font);
        Size measured = Forms.TextRenderer.MeasureText(
            graphics,
            "00 FF",
            font,
            new Size(short.MaxValue, short.MaxValue),
            Forms.TextFormatFlags.NoPadding
                | Forms.TextFormatFlags.NoPrefix
                | Forms.TextFormatFlags.PreserveGraphicsClipping);

        Assert(defaultSize.Width > 0 && defaultSize.Height > 0, "Default text measurement was empty.");
        Assert(measured.Width > 0 && measured.Height > 0, "HexEditor no-padding text measurement was empty.");
        Assert(
            measured.Width <= short.MaxValue && measured.Height <= short.MaxValue,
            "Text measurement ignored the proposed size.");

        Forms.TextRenderer.DrawText(
            graphics,
            "Offset && data",
            font,
            new Rectangle(1, 2, 180, 24),
            Color.DarkSlateBlue,
            Color.White,
            Forms.TextFormatFlags.Right);
        Forms.TextRenderer.DrawText(
            graphics,
            "FF",
            font,
            new Point(0, 30),
            Color.Black,
            Color.White);

        RenderCommand[] textCommands = graphics.DrawingContext.Commands
            .Where(static command => command.Type == RenderCommandType.DrawText)
            .ToArray();
        Assert(textCommands.Length == 2, "TextRenderer did not emit both graphics text commands.");
        Assert(textCommands[0].Text == "Offset & data", "Escaped accelerator text was not normalized.");
        Assert(textCommands[1].Text == "FF", "Point text did not reach the drawing context.");
        Assert(
            graphics.DrawingContext.Commands.Any(static command => command.Type == RenderCommandType.DrawRect),
            "TextRenderer did not emit the requested text background.");
    }

    private static void DrawSelectionTextThroughHostedControl()
    {
        var host = new FakeGraphicsApplicationHost();
        Forms.Application.RegisterPortableApplicationHost(host);
        var hexView = new Forms.Panel { Size = new Size(240, 80) };

        Forms.TextRenderer.DrawText(
            hexView,
            "41 42",
            SystemFonts.DefaultFont,
            new Rectangle(4, 5, 80, 20),
            Color.White,
            Color.Navy,
            Forms.TextFormatFlags.Left & Forms.TextFormatFlags.SingleLine);

        Assert(ReferenceEquals(host.RequestedControl, hexView), "Control text did not route through the typed graphics host.");
        Assert(
            host.Graphics?.DrawingContext.Commands.Any(
                static command => command.Type == RenderCommandType.DrawText && command.Text == "41 42") == true,
            "Hosted control text did not reach its presentation graphics.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FakeGraphicsApplicationHost :
        Forms.IWinFormsApplicationHost,
        Forms.IWinFormsGraphicsHost
    {
        public Graphics? Graphics { get; private set; }

        public Forms.Control? RequestedControl { get; private set; }

        public bool TryCreateGraphics(Forms.Control control, out Graphics graphics)
        {
            RequestedControl = control;
            graphics = System.Drawing.Graphics.FromHwnd(control.Handle);
            Graphics = graphics;
            return true;
        }

        public void Run(Forms.Form mainForm)
        {
            throw new NotSupportedException();
        }

        public Forms.DialogResult ShowDialog(Forms.Form form, Forms.IWin32Window? owner)
        {
            throw new NotSupportedException();
        }

        public void ExitThread()
        {
        }
    }
}
