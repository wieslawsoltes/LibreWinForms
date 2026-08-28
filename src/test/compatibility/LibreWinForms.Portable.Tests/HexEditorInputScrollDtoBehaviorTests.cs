using System;
using Forms = System.Windows.Forms;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class HexEditorInputScrollDtoBehaviorTests
{
    public static void Run()
    {
        KeyEventArgumentsExposeHexEditorModifierAndValueChecks();
        KeyEventArgumentsMatchUpstreamMaskingAndSuppressionSemantics();
        FourArgumentScrollEventPreservesHexEditorVerticalScrollState();
        ExistingScrollConstructorKeepsHorizontalDefault();
        Console.WriteLine(
            "LibreWinForms HexEditor DTO tests passed: modifiers=3 keyValue=1 verticalScroll=1 existingCtor=1.");
    }

    private static void KeyEventArgumentsMatchUpstreamMaskingAndSuppressionSemantics()
    {
        var undefined = new Forms.KeyEventArgs(
            Forms.Keys.Control | Forms.Keys.Shift | Forms.Keys.Alt | (Forms.Keys)0x5D);

        Assert(undefined.KeyCode == Forms.Keys.None, "Undefined key codes did not normalize to Keys.None.");
        Assert(undefined.KeyValue == 0x5D, "KeyValue did not preserve an undefined low-word key value.");
        Assert(
            undefined.Modifiers == (Forms.Keys.Control | Forms.Keys.Shift | Forms.Keys.Alt),
            "KeyEventArgs.Modifiers did not use the Keys.Modifiers mask.");

        var suppressed = new Forms.KeyEventArgs(Forms.Keys.A);
        suppressed.SuppressKeyPress = true;
        Assert(suppressed.Handled, "SuppressKeyPress=true did not mark the event handled.");
        suppressed.SuppressKeyPress = false;
        Assert(!suppressed.Handled, "SuppressKeyPress=false did not clear the handled state.");
    }

    private static void KeyEventArgumentsExposeHexEditorModifierAndValueChecks()
    {
        var input = new Forms.KeyEventArgs(
            Forms.Keys.Control | Forms.Keys.Shift | Forms.Keys.Alt | Forms.Keys.F);

        Assert(input.Control, "HexEditor Control modifier check was false.");
        Assert(input.Shift, "HexEditor Shift modifier check was false.");
        Assert(input.Alt, "HexEditor Alt modifier check was false.");
        Assert(input.KeyValue == (int)Forms.Keys.F, "HexEditor KeyValue did not remove modifiers.");
        Assert(
            input.KeyValue > 64 && input.KeyValue < 71,
            "HexEditor hexadecimal A-F input range no longer accepts F.");
    }

    private static void FourArgumentScrollEventPreservesHexEditorVerticalScrollState()
    {
        const int oldValue = 12;
        const int value = 18;
        var scroll = new Forms.ScrollEventArgs(
            Forms.ScrollEventType.SmallIncrement,
            oldValue,
            value,
            Forms.ScrollOrientation.VerticalScroll);

        Assert(scroll.Type == Forms.ScrollEventType.SmallIncrement, "HexEditor scroll type changed.");
        Assert(scroll.OldValue == oldValue, "HexEditor old scroll value changed.");
        Assert(scroll.NewValue == value, "HexEditor new scroll value changed.");
        Assert(
            scroll.ScrollOrientation == Forms.ScrollOrientation.VerticalScroll,
            "HexEditor vertical scroll orientation changed.");
    }

    private static void ExistingScrollConstructorKeepsHorizontalDefault()
    {
        var scroll = new Forms.ScrollEventArgs(Forms.ScrollEventType.ThumbPosition, 4, 7);

        Assert(scroll.OldValue == 4 && scroll.NewValue == 7, "Existing ScrollEventArgs values changed.");
        Assert(
            scroll.ScrollOrientation == Forms.ScrollOrientation.HorizontalScroll,
            "Existing ScrollEventArgs constructor no longer defaults to horizontal orientation.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
