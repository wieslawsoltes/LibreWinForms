// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;
using System.ComponentModel;

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
        VerifyHexEditorControlContracts();
        VerifyHexEditorMenuContracts();
        VerifyHexEditorToolStripContracts();
        VerifyHexEditorDialogAndConverterContracts();

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

    private static void VerifyHexEditorControlContracts()
    {
        if (!ReferenceEquals(Cursors.IBeam, Cursors.IBeam)
            || ReferenceEquals(Cursors.IBeam, Cursors.Default)
            || ReferenceEquals(Cursors.IBeam, Cursors.WaitCursor)
            || !ReferenceEquals(Cursors.SizeWE, Cursors.SizeWE)
            || !ReferenceEquals(Cursors.SizeNS, Cursors.SizeNS))
        {
            throw new InvalidOperationException("Canonical stock cursors no longer provide stable typed instances.");
        }

        using var buffered = new DoubleBufferedProbeControl();
        if (buffered.IsDoubleBuffered)
        {
            throw new InvalidOperationException("Canonical controls no longer default DoubleBuffered to false.");
        }

        buffered.IsDoubleBuffered = true;
        if (!buffered.IsDoubleBuffered
            || !buffered.HasStyle(ControlStyles.OptimizedDoubleBuffer)
            || !buffered.HasStyle(ControlStyles.AllPaintingInWmPaint))
        {
            throw new InvalidOperationException("DoubleBuffered=true no longer enables the canonical painting styles.");
        }

        buffered.IsDoubleBuffered = false;
        if (buffered.HasStyle(ControlStyles.OptimizedDoubleBuffer)
            || !buffered.HasStyle(ControlStyles.AllPaintingInWmPaint))
        {
            throw new InvalidOperationException("DoubleBuffered=false changed the canonical asymmetric style semantics.");
        }

        using var input = new HexEditorInputKeyProbeControl();
        var left = new Message { Msg = 0x0100, WParam = (nint)Keys.Left };
        if (input.PreProcessMessage(ref left) || input.CommandCount != 0)
        {
            throw new InvalidOperationException("HexEditor arrow input was consumed as a command.");
        }

        var delete = new Message { Msg = 0x0100, WParam = (nint)Keys.Delete };
        if (!input.PreProcessMessage(ref delete) || input.CommandCount != 1)
        {
            throw new InvalidOperationException("HexEditor Delete input did not reach ProcessCmdKey exactly once.");
        }

        using var userControl = new UserControl();
        if (userControl.BorderStyle != BorderStyle.None)
        {
            throw new InvalidOperationException("UserControl.BorderStyle no longer defaults to None.");
        }

        userControl.BorderStyle = BorderStyle.FixedSingle;
        userControl.BorderStyle = BorderStyle.Fixed3D;
        if (userControl.BorderStyle != BorderStyle.Fixed3D)
        {
            throw new InvalidOperationException("UserControl.BorderStyle did not retain the canonical value.");
        }

        AssertInvalidEnum(() => userControl.BorderStyle = (BorderStyle)(-1));
        AssertInvalidEnum(() => userControl.BorderStyle = (BorderStyle)3);
    }

    private static void VerifyHexEditorMenuContracts()
    {
        using var owner = new Control();
        using var first = new ContextMenuStrip();
        using var replacement = new ContextMenuStrip();
        int changes = 0;
        ContextMenuStrip? observed = null;
        owner.ContextMenuStripChanged += (_, _) =>
        {
            changes++;
            observed = owner.ContextMenuStrip;
        };

        owner.ContextMenuStrip = first;
        owner.ContextMenuStrip = first;
        owner.ContextMenuStrip = replacement;
        first.Dispose();
        if (changes != 2 || !ReferenceEquals(observed, replacement) || !ReferenceEquals(owner.ContextMenuStrip, replacement))
        {
            throw new InvalidOperationException("ContextMenuStrip replacement did not preserve canonical ownership semantics.");
        }

        replacement.Dispose();
        if (changes != 3 || owner.ContextMenuStrip is not null || observed is not null)
        {
            throw new InvalidOperationException("Disposing the current ContextMenuStrip did not clear its owner.");
        }

        using var menu = new DropDownEventProbe();
        int closed = 0;
        ToolStripDropDownCloseReason reason = default;
        menu.Closed += (_, e) =>
        {
            closed++;
            reason = e.CloseReason;
        };

        menu.RaiseClosed(ToolStripDropDownCloseReason.ItemClicked);
        if (closed != 1 || reason != ToolStripDropDownCloseReason.ItemClicked)
        {
            throw new InvalidOperationException("ToolStripDropDown lost its typed close reason.");
        }

        menu.RaiseClosed(ToolStripDropDownCloseReason.CloseCalled);
        if (closed != 2 || reason != ToolStripDropDownCloseReason.CloseCalled)
        {
            throw new InvalidOperationException("ToolStripDropDown did not report the canonical CloseCalled reason.");
        }
    }

    private static void VerifyHexEditorToolStripContracts()
    {
        using var combo = new ToolStripComboBox();
        object hexadecimal = "Hexadecimal";
        object octal = "Octal";
        combo.Items.AddRange([hexadecimal, octal, "Decimal"]);
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        int changes = 0;
        combo.SelectedIndexChanged += (_, _) => changes++;

        combo.SelectedItem = octal;
        combo.SelectedItem = new object();
        if (combo.SelectedIndex != 1 || !ReferenceEquals(combo.SelectedItem, octal) || changes != 1)
        {
            throw new InvalidOperationException("ToolStripComboBox did not preserve canonical selected-item forwarding.");
        }

        combo.SelectedItem = null;
        if (combo.SelectedIndex != -1 || combo.SelectedItem is not null || changes != 2
            || combo.DropDownStyle != ComboBoxStyle.DropDownList
            || combo.Items.Count != 3
            || !Equals(combo.Items[0], hexadecimal)
            || !Equals(combo.Items[2], "Decimal"))
        {
            throw new InvalidOperationException("ToolStripComboBox selection, style, or designer item ordering changed.");
        }

        using var menuItem = new ToolStripMenuItem("File");
        using var child = new ToolStripMenuItem("Open");
        menuItem.DropDownItems.Add(child);
        if (!ReferenceEquals(menuItem.DropDownItems, menuItem.DropDown.Items)
            || menuItem.DropDown.Items.Count != 1
            || !ReferenceEquals(menuItem.DropDown.Items[0], child))
        {
            throw new InvalidOperationException("ToolStripMenuItem exposed divergent drop-down collections.");
        }

        using var button = new ToolStripDropDownButton();
        using var buttonChild = new ToolStripButton();
        button.DropDownItems.Add(buttonChild);
        if (!ReferenceEquals(button.DropDownItems, button.DropDown.Items)
            || button.DropDown.Items.Count != 1
            || !ReferenceEquals(button.DropDown.Items[0], buttonChild))
        {
            throw new InvalidOperationException("ToolStripDropDownButton exposed divergent drop-down collections.");
        }

        using var progress = new ToolStripProgressBar();
        if (progress.Overflow != ToolStripItemOverflow.AsNeeded)
        {
            throw new InvalidOperationException("ToolStripItem.Overflow no longer defaults to AsNeeded.");
        }

        progress.Overflow = ToolStripItemOverflow.Never;
        AssertInvalidEnum(() => progress.Overflow = (ToolStripItemOverflow)(-1));
        AssertInvalidEnum(() => progress.Overflow = (ToolStripItemOverflow)3);
        if (progress.Overflow != ToolStripItemOverflow.Never)
        {
            throw new InvalidOperationException("Invalid ToolStripItem.Overflow input mutated canonical state.");
        }
    }

    private static void VerifyHexEditorDialogAndConverterContracts()
    {
        var converter = new CursorConverter();
        if (!converter.CanConvertFrom(typeof(string))
            || !converter.CanConvertTo(typeof(string))
            || !converter.GetStandardValuesSupported()
            || !ReferenceEquals(converter.ConvertFromInvariantString("IBeam"), Cursors.IBeam)
            || !Equals(converter.ConvertToInvariantString(Cursors.SizeWE), nameof(Cursors.SizeWE)))
        {
            throw new InvalidOperationException("CursorConverter no longer round-trips canonical stock cursors.");
        }

        using var open = new OpenFileDialog
        {
            AddExtension = true,
            CheckFileExists = true,
            DefaultExt = "bin",
            FileName = "sample.bin",
            Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*",
            Multiselect = false,
        };
        using var save = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "bin",
            FileName = "sample.bin",
            Filter = open.Filter,
            OverwritePrompt = true,
        };

        if (!open.CheckFileExists || open.Multiselect || open.DefaultExt != "bin"
            || !save.AddExtension || !save.OverwritePrompt || save.Filter != open.Filter)
        {
            throw new InvalidOperationException("Canonical file-dialog configuration no longer supports the HexEditor contract.");
        }
    }

    private static void AssertInvalidEnum(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidEnumArgumentException)
        {
            return;
        }

        throw new InvalidOperationException("Expected InvalidEnumArgumentException was not thrown.");
    }

    private sealed class DoubleBufferedProbeControl : Control
    {
        public bool IsDoubleBuffered
        {
            get => DoubleBuffered;
            set => DoubleBuffered = value;
        }

        public bool HasStyle(ControlStyles style) => GetStyle(style);
    }

    private sealed class HexEditorInputKeyProbeControl : Control
    {
        public int CommandCount { get; private set; }

        protected override bool IsInputKey(Keys keyData)
            => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Tab;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if ((keyData & Keys.KeyCode) == Keys.Delete)
            {
                CommandCount++;
                return true;
            }

            return false;
        }
    }

    private sealed class DropDownEventProbe : ContextMenuStrip
    {
        public void RaiseClosed(ToolStripDropDownCloseReason reason)
            => OnClosed(new ToolStripDropDownClosedEventArgs(reason));
    }
}
