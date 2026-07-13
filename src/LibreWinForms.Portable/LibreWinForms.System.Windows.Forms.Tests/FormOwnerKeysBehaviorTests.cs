using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using Forms = System.Windows.Forms;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class FormOwnerKeysBehaviorTests
{
    public static void Run()
    {
        ModelessOwnerAndTopMostUseTypedHostState();
        KeysConverterMatchesCommonWinFormsShortcutStrings();
        Console.WriteLine(
            "LibreWinForms form owner/keys tests passed: typedOwner=1 ownerClose=1 topMost=3 shortcuts=10 invalid=2.");
    }

    private static void ModelessOwnerAndTopMostUseTypedHostState()
    {
        var host = new FakeWindowApplicationHost();
        Forms.Application.RegisterPortableApplicationHost(host);

        using var owner = new Forms.Form { Name = "owner" };
        using var child = new Forms.Form { Name = "child" };
        int shownCount = 0;
        int closedCount = 0;
        Forms.CloseReason closedReason = Forms.CloseReason.None;
        child.Shown += (_, _) => shownCount++;
        child.FormClosed += (_, e) =>
        {
            closedCount++;
            closedReason = e.CloseReason;
        };

        child.TopMost = true;
        Assert(child.TopMost, "Pre-show TopMost state was not retained by Form.");
        child.Show(owner);

        Assert(ReferenceEquals(child.Owner, owner), "Form.Show(owner) did not retain the typed Form owner.");
        Assert(ReferenceEquals(host.ShownForm, child), "The typed modeless host received the wrong form.");
        Assert(ReferenceEquals(host.ShownOwner, owner), "The typed modeless host received the wrong owner.");
        Assert(host.TopMostAtShow, "The typed host did not observe pre-show TopMost state.");
        Assert(child.Visible && shownCount == 1, "Form.Show(owner) did not publish one modeless shown lifecycle.");

        child.TopMost = false;
        child.TopMost = true;
        Assert(
            host.TopMostUpdates.Count == 3
                && host.TopMostUpdates[0]
                && !host.TopMostUpdates[1]
                && host.TopMostUpdates[2],
            "Form.TopMost did not synchronize false/true changes through the typed host.");

        owner.Close();
        Assert(!child.Visible, "Closing the owner did not close its modeless child form.");
        Assert(closedCount == 1, "Owner close published an incorrect child FormClosed count.");
        Assert(closedReason == Forms.CloseReason.FormOwnerClosing, "Owner close used the wrong child close reason.");

        AssertThrows<ArgumentNullException>(() => child.Show(owner: null!), "Form.Show accepted a null owner.");
        AssertThrows<InvalidOperationException>(() => child.Show(child), "Form.Show accepted the form itself as owner.");
    }

    private static void KeysConverterMatchesCommonWinFormsShortcutStrings()
    {
        var converter = new Forms.KeysConverter();
        TypeConverter registeredConverter = TypeDescriptor.GetConverter(typeof(Forms.Keys));
        Assert(registeredConverter is Forms.KeysConverter, "Keys does not advertise KeysConverter through TypeDescriptor.");

        AssertKeys(converter, "Control+Shift+F", Forms.Keys.Control | Forms.Keys.Shift | Forms.Keys.F);
        AssertKeys(converter, "Ctrl+Alt+H", Forms.Keys.Control | Forms.Keys.Alt | Forms.Keys.H);
        AssertKeys(converter, "Control + H", Forms.Keys.Control | Forms.Keys.H);
        AssertKeys(converter, "F3", Forms.Keys.F3);
        AssertKeys(converter, "0", Forms.Keys.D0);
        AssertKeys(converter, "None", Forms.Keys.None);
        AssertKeys(converter, "(none)", Forms.Keys.None);

        Assert(
            converter.ConvertToInvariantString(Forms.Keys.Control | Forms.Keys.Alt | Forms.Keys.Shift | Forms.Keys.F1)
                == "Ctrl+Alt+Shift+F1",
            "KeysConverter did not format modifiers in native WinForms order.");
        Assert(
            converter.ConvertToInvariantString(Forms.Keys.Control | Forms.Keys.H) == "Ctrl+H",
            "KeysConverter did not format a common Control shortcut.");
        Assert(
            converter.ConvertToInvariantString(Forms.Keys.None) == "(none)",
            "KeysConverter did not format the native no-key display string.");

        object? enumValue = converter.ConvertFrom(
            context: null,
            CultureInfo.InvariantCulture,
            new Enum[] { Forms.Keys.Control, Forms.Keys.Shift, Forms.Keys.F });
        Assert(
            enumValue is Forms.Keys keys && keys == (Forms.Keys.Control | Forms.Keys.Shift | Forms.Keys.F),
            "KeysConverter did not combine an Enum[] shortcut value.");
        Assert(converter.ConvertFromInvariantString("   ") is null, "KeysConverter did not preserve empty-string semantics.");

        AssertThrows<ArgumentException>(
            () => converter.ConvertFromInvariantString("Control+DefinitelyNotAKey"),
            "KeysConverter accepted an unknown key token.");
        AssertThrows<FormatException>(
            () => converter.ConvertFromInvariantString("A+B"),
            "KeysConverter accepted two non-modifier keys.");
    }

    private static void AssertKeys(Forms.KeysConverter converter, string text, Forms.Keys expected)
    {
        object? converted = converter.ConvertFromInvariantString(text);
        Assert(converted is Forms.Keys keys && keys == expected, $"KeysConverter parsed '{text}' incorrectly.");
    }

    private static void AssertThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class FakeWindowApplicationHost :
        Forms.IWinFormsApplicationHost,
        Forms.IWinFormsWindowHost
    {
        public Forms.Form? ShownForm { get; private set; }

        public Forms.IWin32Window? ShownOwner { get; private set; }

        public bool TopMostAtShow { get; private set; }

        public List<bool> TopMostUpdates { get; } = new();

        public bool TryShow(Forms.Form form, Forms.IWin32Window owner)
        {
            ShownForm = form;
            ShownOwner = owner;
            TopMostAtShow = form.TopMost;
            return true;
        }

        public bool TrySetTopMost(Forms.Form form, bool topMost)
        {
            TopMostUpdates.Add(topMost);
            return ReferenceEquals(form, ShownForm);
        }

        public void Run(Forms.Form mainForm) => throw new NotSupportedException();

        public Forms.DialogResult ShowDialog(Forms.Form form, Forms.IWin32Window? owner) =>
            throw new NotSupportedException();

        public void ExitThread()
        {
        }
    }
}
