// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;

namespace LibreWinForms.Sdk.SourceFirstSmoke;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        if (!LibrePlatform.IsRegistered)
        {
            throw new InvalidOperationException("The source-first SDK did not register the ProGPU platform backend.");
        }

        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        ApplicationConfiguration.Initialize();
        VerifyHexEditorInputScrollContracts();

        using Bitmap bitmap = new(64, 64);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (Pen pen = new(Color.Black))
        {
            graphics.DrawCurve(
                pen,
                [new Point(0, 0), new Point(16, 32), new Point(48, 16), new Point(63, 63)]);
        }

        using Form form = new()
        {
            ClientSize = new Size(320, 180),
            Text = "Canonical LibreWinForms SDK smoke"
        };
        form.Controls.Add(new Button { Text = "Source-built", AutoSize = true });

        LibrePlatform.Current.Dispose();
        return 0;
    }

    private static void VerifyHexEditorInputScrollContracts()
    {
        var input = new KeyEventArgs(Keys.Control | Keys.Shift | Keys.Alt | Keys.F);
        if (!input.Control
            || !input.Shift
            || !input.Alt
            || input.KeyValue != (int)Keys.F
            || input.KeyValue is <= 64 or >= 71)
        {
            throw new InvalidOperationException("Canonical KeyEventArgs no longer preserves HexEditor modifier/value checks.");
        }

        // The frozen compatibility vector used 0x5D, but canonical WinForms defines that
        // value as Keys.Apps. Use the upstream unit-test value for an undefined low word.
        var undefined = new KeyEventArgs(Keys.Control | Keys.Shift | Keys.Alt | (Keys)0xFF);
        if (undefined.KeyCode != Keys.None
            || undefined.KeyValue != 0xFF
            || undefined.Modifiers != (Keys.Control | Keys.Shift | Keys.Alt))
        {
            throw new InvalidOperationException("Canonical KeyEventArgs masking no longer matches upstream WinForms.");
        }

        var suppressed = new KeyEventArgs(Keys.A) { SuppressKeyPress = true };
        if (!suppressed.Handled)
        {
            throw new InvalidOperationException("SuppressKeyPress=true did not mark the key event handled.");
        }

        suppressed.SuppressKeyPress = false;
        if (suppressed.Handled)
        {
            throw new InvalidOperationException("SuppressKeyPress=false did not clear the handled state.");
        }

        var vertical = new ScrollEventArgs(
            ScrollEventType.SmallIncrement,
            oldValue: 12,
            newValue: 18,
            ScrollOrientation.VerticalScroll);
        if (vertical.Type != ScrollEventType.SmallIncrement
            || vertical.OldValue != 12
            || vertical.NewValue != 18
            || vertical.ScrollOrientation != ScrollOrientation.VerticalScroll)
        {
            throw new InvalidOperationException("Canonical four-argument ScrollEventArgs changed HexEditor state.");
        }

        var horizontal = new ScrollEventArgs(ScrollEventType.ThumbPosition, oldValue: 4, newValue: 7);
        if (horizontal.OldValue != 4
            || horizontal.NewValue != 7
            || horizontal.ScrollOrientation != ScrollOrientation.HorizontalScroll)
        {
            throw new InvalidOperationException("Canonical ScrollEventArgs no longer defaults to horizontal orientation.");
        }
    }
}
