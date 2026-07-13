using System;
using System.ComponentModel;
using Forms = System.Windows.Forms;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class HexEditorContractBehaviorTests
{
    private const int WmKeyDown = 0x0100;

    public static void Run()
    {
        CursorKindsAreStableTypedSingletons();
        DoubleBufferedMatchesAsymmetricStyleSemantics();
        InputKeysBypassCommandPreprocessing();
        ContextMenuStripOwnershipTracksReplacementAndDisposal();
        DropDownCloseReasonsAreTypedAndIdempotent();
        UserControlBorderStyleValidatesAndInvalidates();
        ToolStripComboBoxForwardsSelectionAndStyle();
        ComboBoxItemRangesPreserveDesignerOrder();
        ToolStripOverflowStateValidates();
        Console.WriteLine(
            "LibreWinForms HexEditor contracts passed: cursor=5 doubleBuffer=1 inputKey=2 menu=4 close=2 border=3 combo=3 range=3 overflow=3.");
    }

    private static void CursorKindsAreStableTypedSingletons()
    {
        Assert(ReferenceEquals(Forms.Cursors.IBeam, Forms.Cursors.IBeam), "Cursors.IBeam is not a stable singleton.");
        Assert(!ReferenceEquals(Forms.Cursors.IBeam, Forms.Cursors.Default), "IBeam and Default cursors share identity.");
        Assert(!ReferenceEquals(Forms.Cursors.IBeam, Forms.Cursors.WaitCursor), "IBeam and Wait cursors share identity.");
        Assert(ReferenceEquals(Forms.Cursor.Current, Forms.Cursors.Default), "Cursor.Current did not default to the stable default cursor.");
        Assert(Forms.Cursors.Default.PortableKind == Forms.PortableCursorKind.Default, "Default cursor kind changed.");
        Assert(Forms.Cursors.WaitCursor.PortableKind == Forms.PortableCursorKind.Wait, "Wait cursor kind changed.");
        Assert(Forms.Cursors.IBeam.PortableKind == Forms.PortableCursorKind.IBeam, "IBeam cursor kind changed.");
        Assert(ReferenceEquals(Forms.Cursors.SizeWE, Forms.Cursors.SizeWE), "Cursors.SizeWE is not a stable singleton.");
        Assert(ReferenceEquals(Forms.Cursors.SizeNS, Forms.Cursors.SizeNS), "Cursors.SizeNS is not a stable singleton.");
        Assert(Forms.Cursors.SizeWE.PortableKind == Forms.PortableCursorKind.SizeWE, "SizeWE cursor kind changed.");
        Assert(Forms.Cursors.SizeNS.PortableKind == Forms.PortableCursorKind.SizeNS, "SizeNS cursor kind changed.");
    }

    private static void DoubleBufferedMatchesAsymmetricStyleSemantics()
    {
        var control = new DoubleBufferedProbeControl();
        int invalidated = 0;
        control.Invalidated += (_, _) => invalidated++;

        Assert(!control.IsDoubleBuffered, "DoubleBuffered did not default to false.");
        control.IsDoubleBuffered = true;
        Assert(control.IsDoubleBuffered, "DoubleBuffered=true was not retained.");
        Assert(control.HasStyle(Forms.ControlStyles.OptimizedDoubleBuffer), "OptimizedDoubleBuffer was not enabled.");
        Assert(control.HasStyle(Forms.ControlStyles.AllPaintingInWmPaint), "AllPaintingInWmPaint was not enabled.");
        control.IsDoubleBuffered = true;
        control.IsDoubleBuffered = false;
        Assert(!control.HasStyle(Forms.ControlStyles.OptimizedDoubleBuffer), "OptimizedDoubleBuffer was not disabled.");
        Assert(control.HasStyle(Forms.ControlStyles.AllPaintingInWmPaint), "Disabling buffering cleared AllPaintingInWmPaint.");
        Assert(invalidated == 0, "DoubleBuffered style changes emitted portable invalidation events.");
    }

    private static void InputKeysBypassCommandPreprocessing()
    {
        var control = new HexEditorInputKeyProbeControl();
        var left = new Forms.Message { Msg = WmKeyDown, WParam = new IntPtr((int)Forms.Keys.Left) };
        Assert(!control.PreProcessMessage(ref left), "HexEditor arrow key was consumed as a command.");
        Assert(control.CommandCount == 0, "HexEditor arrow key reached ProcessCmdKey.");

        var delete = new Forms.Message { Msg = WmKeyDown, WParam = new IntPtr((int)Forms.Keys.Delete) };
        Assert(control.PreProcessMessage(ref delete), "HexEditor Delete command was not handled.");
        Assert(control.CommandCount == 1, "HexEditor Delete command did not reach ProcessCmdKey exactly once.");
    }

    private static void ContextMenuStripOwnershipTracksReplacementAndDisposal()
    {
        using var control = new Forms.Control();
        using var first = new Forms.ContextMenuStrip();
        using var replacement = new Forms.ContextMenuStrip();
        int changes = 0;
        Forms.ContextMenuStrip? observed = null;
        control.ContextMenuStripChanged += (_, _) =>
        {
            changes++;
            observed = control.ContextMenuStrip;
        };

        control.ContextMenuStrip = first;
        Assert(changes == 1 && ReferenceEquals(observed, first), "Initial context-menu change observed stale state.");
        control.ContextMenuStrip = first;
        Assert(changes == 1, "Same context-menu assignment raised a change.");
        control.ContextMenuStrip = replacement;
        Assert(changes == 2 && ReferenceEquals(observed, replacement), "Replacement context-menu change observed stale state.");
        first.Dispose();
        Assert(changes == 2 && ReferenceEquals(control.ContextMenuStrip, replacement), "Disposed old menu cleared its replacement.");
        replacement.Dispose();
        Assert(changes == 3 && control.ContextMenuStrip == null && observed == null, "Disposed current menu did not clear ownership.");
    }

    private static void DropDownCloseReasonsAreTypedAndIdempotent()
    {
        using var owner = new Forms.Control();
        using var menu = new Forms.ContextMenuStrip();
        int closed = 0;
        Forms.ToolStripDropDownCloseReason reason = default;
        menu.Closed += (_, e) =>
        {
            closed++;
            reason = e.CloseReason;
        };

        Assert(!menu.Visible, "Context menus must start hidden.");
        menu.Show(owner, System.Drawing.Point.Empty);
        menu.Close(Forms.ToolStripDropDownCloseReason.ItemClicked);
        menu.Close(Forms.ToolStripDropDownCloseReason.AppClicked);
        Assert(closed == 1 && reason == Forms.ToolStripDropDownCloseReason.ItemClicked, "Typed close reason was lost or duplicated.");

        menu.Show(owner, System.Drawing.Point.Empty);
        menu.Close();
        Assert(closed == 2 && reason == Forms.ToolStripDropDownCloseReason.CloseCalled, "Close() did not report CloseCalled.");
    }

    private static void UserControlBorderStyleValidatesAndInvalidates()
    {
        using var control = new Forms.UserControl();
        int invalidated = 0;
        control.Invalidated += (_, _) => invalidated++;
        Assert(control.BorderStyle == Forms.BorderStyle.None, "UserControl BorderStyle did not default to None.");
        control.BorderStyle = Forms.BorderStyle.FixedSingle;
        control.BorderStyle = Forms.BorderStyle.FixedSingle;
        control.BorderStyle = Forms.BorderStyle.Fixed3D;
        Assert(invalidated == 2, "UserControl BorderStyle did not invalidate once per changed value.");

        AssertThrowsInvalidEnum(() => control.BorderStyle = (Forms.BorderStyle)(-1));
        AssertThrowsInvalidEnum(() => control.BorderStyle = (Forms.BorderStyle)3);
        Assert(control.BorderStyle == Forms.BorderStyle.Fixed3D, "Invalid BorderStyle assignment mutated state.");
    }

    private static void ToolStripComboBoxForwardsSelectionAndStyle()
    {
        using var combo = new Forms.ToolStripComboBox();
        object hexadecimal = "Hexadecimal";
        object octal = "Octal";
        combo.Items.Add(hexadecimal);
        combo.Items.Add(octal);
        combo.DropDownStyle = Forms.ComboBoxStyle.DropDownList;
        int changes = 0;
        combo.SelectedIndexChanged += (_, _) => changes++;

        combo.SelectedItem = octal;
        Assert(combo.SelectedIndex == 1 && ReferenceEquals(combo.SelectedItem, octal), "SelectedItem did not select a present item.");
        combo.SelectedItem = new object();
        Assert(combo.SelectedIndex == 1 && changes == 1, "Unknown SelectedItem changed selection or raised an event.");
        combo.SelectedItem = null;
        Assert(combo.SelectedIndex == -1 && combo.SelectedItem == null && changes == 2, "Null SelectedItem did not clear selection once.");
        Assert(combo.DropDownStyle == Forms.ComboBoxStyle.DropDownList, "ToolStripComboBox did not forward DropDownStyle.");
    }

    private static void ToolStripOverflowStateValidates()
    {
        using var item = new Forms.ToolStripProgressBar();
        Assert(item.Overflow == Forms.ToolStripItemOverflow.AsNeeded, "ToolStripItem Overflow did not default to AsNeeded.");
        item.Overflow = Forms.ToolStripItemOverflow.Never;
        item.Overflow = Forms.ToolStripItemOverflow.Never;
        Assert(item.Overflow == Forms.ToolStripItemOverflow.Never, "ToolStripItem Overflow did not retain Never.");
        AssertThrowsInvalidEnum(() => item.Overflow = (Forms.ToolStripItemOverflow)(-1));
        AssertThrowsInvalidEnum(() => item.Overflow = (Forms.ToolStripItemOverflow)3);
        Assert(item.Overflow == Forms.ToolStripItemOverflow.Never, "Invalid Overflow assignment mutated state.");
    }

    private static void ComboBoxItemRangesPreserveDesignerOrder()
    {
        using var combo = new Forms.ToolStripComboBox();
        combo.Items.AddRange(new object[] { "Hexadecimal", "Octal", "Decimal" });
        Assert(combo.Items.Count == 3, "ComboBox item range did not add every designer item.");
        Assert((string)combo.Items[0] == "Hexadecimal", "ComboBox item range changed the first item.");
        Assert((string)combo.Items[2] == "Decimal", "ComboBox item range changed item order.");
    }

    private static void AssertThrowsInvalidEnum(Action action)
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

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class DoubleBufferedProbeControl : Forms.Control
    {
        public bool IsDoubleBuffered
        {
            get => DoubleBuffered;
            set => DoubleBuffered = value;
        }

        public bool HasStyle(Forms.ControlStyles style) => GetStyle(style);
    }

    private sealed class HexEditorInputKeyProbeControl : Forms.Control
    {
        public int CommandCount { get; private set; }

        protected override bool IsInputKey(Forms.Keys keyData)
        {
            return (keyData & Forms.Keys.KeyCode) is Forms.Keys.Left or Forms.Keys.Right or Forms.Keys.Up or Forms.Keys.Down or Forms.Keys.Tab;
        }

        protected override bool ProcessCmdKey(ref Forms.Message msg, Forms.Keys keyData)
        {
            CommandCount++;
            return true;
        }
    }
}
