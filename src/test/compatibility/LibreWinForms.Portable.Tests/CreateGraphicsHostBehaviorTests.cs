using System;
using System.Collections.Generic;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class CreateGraphicsHostBehaviorTests
{
    public static void Run()
    {
        var host = new FakeGraphicsApplicationHost();
        Forms.Application.RegisterPortableApplicationHost(host);

        var header = new Forms.Control { Name = "header", Size = new Size(160, 20) };
        var side = new Forms.Control { Name = "side", Size = new Size(60, 80) };
        var hex = new Forms.Control { Name = "hex", Size = new Size(240, 80) };
        var text = new Forms.Control { Name = "text", Size = new Size(160, 80) };
        Forms.Control[] views = { header, side, hex, text };

        var painters = new List<Graphics>();
        foreach (Forms.Control view in views)
        {
            Graphics graphics = view.CreateGraphics();
            painters.Add(graphics);
            using var brush = new SolidBrush(Color.FromArgb(255, 24, 72, 120));
            graphics.FillRectangle(brush, 0, 0, view.Width, view.Height);
        }

        Assert(host.RequestedControls.Count == views.Length, "The typed graphics host did not receive all HexEditor view controls.");
        for (int index = 0; index < views.Length; index++)
        {
            Assert(
                ReferenceEquals(host.RequestedControls[index], views[index]),
                "CreateGraphics changed the typed control identity.");
            Assert(
                ReferenceEquals(host.CreatedGraphics[index], painters[index]),
                "CreateGraphics did not return the host-owned graphics instance.");
            Assert(
                painters[index].DrawingContext.Commands.Count > 0,
                "A long-lived hosted graphics instance did not retain drawing commands.");
        }

        Console.WriteLine(
            "LibreWinForms CreateGraphics host tests passed: typedHost=1 views=4 longLivedCommands=1.");
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
        public List<Graphics> CreatedGraphics { get; } = new();

        public List<Forms.Control> RequestedControls { get; } = new();

        public bool TryCreateGraphics(Forms.Control control, out Graphics graphics)
        {
            RequestedControls.Add(control);
            graphics = Graphics.FromHwnd(control.Handle);
            CreatedGraphics.Add(graphics);
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
