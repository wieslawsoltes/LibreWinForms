using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class DragDropHostBehaviorTests
{
    public static void Run()
    {
        FailClosedAndUnhostedCoordinatesUseTheFullAncestorChain();
        TypedHostRoutesDataEffectsEventsAndCoordinates();
        Console.WriteLine(
            "LibreWinForms drag/drop host tests passed: failClosed=1 formats=1 enterOverDrop=1 transitionLeave=1 allowDropParent=1 effectMask=1 cancel=1 coordinates=1.");
    }

    private static void FailClosedAndUnhostedCoordinatesUseTheFullAncestorChain()
    {
        var root = new Forms.Control { Location = new Point(10, 20) };
        var parent = new Forms.Control { Location = new Point(30, 40) };
        var child = new Forms.Control { Location = new Point(5, 6) };
        root.Controls.Add(parent);
        parent.Controls.Add(child);

        Point screen = child.PointToScreen(new Point(2, 3));
        Assert(screen == new Point(47, 69), "Unhosted PointToScreen did not include every ancestor offset.");
        Assert(child.PointToClient(screen) == new Point(2, 3), "Unhosted point conversion did not round-trip.");
        Assert(
            child.DoDragDrop("unhosted", Forms.DragDropEffects.Copy) == Forms.DragDropEffects.None,
            "DoDragDrop reported false success without a capable host.");

        bool invalidEffectRejected = false;
        try
        {
            _ = child.DoDragDrop("invalid", (Forms.DragDropEffects)0x100);
        }
        catch (InvalidEnumArgumentException)
        {
            invalidEffectRejected = true;
        }

        Assert(invalidEffectRejected, "DoDragDrop accepted an unknown effect bit.");
    }

    private static void TypedHostRoutesDataEffectsEventsAndCoordinates()
    {
        var source = new Forms.Control { Name = "source", Location = new Point(7, 9) };
        var targetParent = new Forms.Control
        {
            Name = "parent",
            Location = new Point(20, 30),
            AllowDrop = true
        };
        var targetChild = new Forms.Control
        {
            Name = "child",
            Location = new Point(4, 5),
            AllowDrop = false
        };
        var secondTarget = new Forms.Control
        {
            Name = "second",
            Location = new Point(70, 80),
            AllowDrop = true
        };
        targetParent.Controls.Add(targetChild);

        var sequence = new List<string>();
        targetParent.DragEnter += (_, e) =>
        {
            sequence.Add("parent.enter");
            Assert(e.KeyState == 8, "DragEnter did not preserve the Control modifier key state.");
            Assert(e.X == 640 && e.Y == 480, "DragEnter did not publish screen coordinates.");
            e.Effect = Forms.DragDropEffects.Move;
        };
        targetParent.DragOver += (_, e) =>
        {
            sequence.Add("parent.over");
            Assert(e.Effect == Forms.DragDropEffects.Move, "DragOver did not receive the accepted enter effect.");
            e.Effect = Forms.DragDropEffects.Copy;
        };
        targetParent.DragLeave += (_, _) => sequence.Add("parent.leave");
        secondTarget.DragEnter += (_, e) =>
        {
            sequence.Add("second.enter");
            e.Effect = Forms.DragDropEffects.Copy;
        };
        secondTarget.DragOver += (_, e) =>
        {
            sequence.Add("second.over");
            e.Effect = Forms.DragDropEffects.Link;
        };
        secondTarget.DragDrop += (_, e) =>
        {
            sequence.Add("second.drop");
            Assert(e.Effect == Forms.DragDropEffects.None, "Disallowed DragOver effect was not masked before Drop.");
            e.Effect = Forms.DragDropEffects.Copy;
        };

        int disallowedEnterCalls = 0;
        targetChild.DragEnter += (_, _) => disallowedEnterCalls++;
        var rejectedArgs = new Forms.DragEventArgs(
            new Forms.DataObject("rejected"),
            0,
            0,
            0,
            Forms.DragDropEffects.Copy,
            Forms.DragDropEffects.Copy);
        targetChild.RaiseDragEnter(rejectedArgs);
        Assert(disallowedEnterCalls == 0, "A control with AllowDrop=false received DragEnter.");
        Assert(rejectedArgs.Effect == Forms.DragDropEffects.None, "A disallowed target retained an accepted effect.");

        var host = new FakeDragDropApplicationHost(targetChild, targetParent, secondTarget);
        Forms.Application.RegisterPortableApplicationHost(host);

        var data = new Forms.DataObject();
        data.SetData(Forms.DataFormats.FileDrop, new[] { "/tmp/Project.csproj", "/tmp/Readme.txt" });
        data.SetData(Forms.DataFormats.UnicodeText, "portable drag text");
        Forms.DragDropEffects result = source.DoDragDrop(
            data,
            Forms.DragDropEffects.Copy | Forms.DragDropEffects.Move);

        Assert(result == Forms.DragDropEffects.Copy, "The typed drag host did not return the final accepted effect.");
        Assert(ReferenceEquals(host.LastSource, source), "The drag source did not reach the typed host.");
        Assert(ReferenceEquals(host.LastData, data), "The IDataObject was replaced before reaching the typed host.");
        Assert(
            data.GetFormats(autoConvert: false).Length == 2
            && data.GetDataPresent(Forms.DataFormats.FileDrop, autoConvert: false)
            && data.GetDataPresent(Forms.DataFormats.UnicodeText, autoConvert: false),
            "FileDrop/text formats were not retained by the portable DataObject.");
        Assert(host.ParentFallbackObserved, "AllowDrop parent fallback was not used for a disallowed child.");
        Assert(host.DisallowedEffectWasMasked, "An effect outside AllowedEffect was not masked.");
        Assert(
            string.Join(",", sequence) ==
            "parent.enter,parent.over,parent.leave,second.enter,second.over,second.drop",
            "Unexpected drag event sequence: " + string.Join(",", sequence));
        Assert(!host.SessionActive, "The completed fake drag session was not cleaned up.");

        Point hostedScreen = targetChild.PointToScreen(new Point(3, 4));
        Assert(hostedScreen == new Point(327, 439), "Typed hosted PointToScreen used the wrong ancestor/surface offset.");
        Assert(targetChild.PointToClient(hostedScreen) == new Point(3, 4), "Typed hosted coordinates did not round-trip.");

        sequence.Clear();
        host.CancelNext = true;
        result = source.DoDragDrop(data, Forms.DragDropEffects.Copy);
        Assert(result == Forms.DragDropEffects.None, "A canceled drag returned a successful effect.");
        Assert(string.Join(",", sequence) == "parent.enter,parent.leave", "Cancel did not publish Enter then Leave.");
        Assert(!host.SessionActive, "The canceled fake drag session was not cleaned up.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FakeDragDropApplicationHost :
        Forms.IWinFormsApplicationHost,
        Forms.IWinFormsDragDropHost,
        Forms.IWinFormsCoordinateHost
    {
        private readonly Forms.Control _initialHit;
        private readonly Forms.Control _parentTarget;
        private readonly Forms.Control _secondTarget;

        public FakeDragDropApplicationHost(
            Forms.Control initialHit,
            Forms.Control parentTarget,
            Forms.Control secondTarget)
        {
            _initialHit = initialHit;
            _parentTarget = parentTarget;
            _secondTarget = secondTarget;
        }

        public bool CancelNext { get; set; }

        public bool DisallowedEffectWasMasked { get; private set; }

        public Forms.IDataObject? LastData { get; private set; }

        public Forms.Control? LastSource { get; private set; }

        public bool ParentFallbackObserved { get; private set; }

        public bool SessionActive { get; private set; }

        public Forms.DragDropEffects DoDragDrop(
            Forms.Control source,
            Forms.IDataObject data,
            Forms.DragDropEffects allowedEffects)
        {
            LastSource = source;
            LastData = data;
            SessionActive = true;
            try
            {
                Forms.Control? firstTarget = ResolveAllowedTarget(_initialHit);
                ParentFallbackObserved = ReferenceEquals(firstTarget, _parentTarget);
                if (firstTarget == null)
                {
                    return Forms.DragDropEffects.None;
                }

                var enter = NewArgs(data, allowedEffects, Forms.DragDropEffects.Copy);
                firstTarget.RaiseDragEnter(enter);
                Forms.DragDropEffects effect = Normalize(enter.Effect, allowedEffects);
                if (CancelNext)
                {
                    CancelNext = false;
                    firstTarget.RaiseDragLeave(EventArgs.Empty);
                    return Forms.DragDropEffects.None;
                }

                var over = NewArgs(data, allowedEffects, effect);
                firstTarget.RaiseDragOver(over);
                effect = Normalize(over.Effect, allowedEffects);

                firstTarget.RaiseDragLeave(EventArgs.Empty);
                var secondEnter = NewArgs(data, allowedEffects, effect);
                _secondTarget.RaiseDragEnter(secondEnter);
                effect = Normalize(secondEnter.Effect, allowedEffects);

                var secondOver = NewArgs(data, allowedEffects, effect);
                _secondTarget.RaiseDragOver(secondOver);
                effect = Normalize(secondOver.Effect, allowedEffects);
                DisallowedEffectWasMasked = effect == Forms.DragDropEffects.None;

                var drop = NewArgs(data, allowedEffects, effect);
                _secondTarget.RaiseDragDrop(drop);
                return Normalize(drop.Effect, allowedEffects);
            }
            finally
            {
                SessionActive = false;
            }
        }

        public bool TryPointToScreen(
            Forms.Control control,
            Point point,
            out Point screenPoint)
        {
            int x = point.X + 300;
            int y = point.Y + 400;
            for (Forms.Control? current = control; current != null; current = current.Parent)
            {
                x += current.Left;
                y += current.Top;
            }

            screenPoint = new Point(x, y);
            return true;
        }

        public bool TryPointToClient(
            Forms.Control control,
            Point point,
            out Point clientPoint)
        {
            int x = point.X - 300;
            int y = point.Y - 400;
            for (Forms.Control? current = control; current != null; current = current.Parent)
            {
                x -= current.Left;
                y -= current.Top;
            }

            clientPoint = new Point(x, y);
            return true;
        }

        public void Run(Forms.Form mainForm) => throw new NotSupportedException();

        public Forms.DialogResult ShowDialog(Forms.Form form, Forms.IWin32Window? owner) =>
            throw new NotSupportedException();

        public void ExitThread()
        {
        }

        private static Forms.Control? ResolveAllowedTarget(Forms.Control? control)
        {
            while (control != null && !control.AllowDrop)
            {
                control = control.Parent;
            }

            return control;
        }

        private static Forms.DragEventArgs NewArgs(
            Forms.IDataObject data,
            Forms.DragDropEffects allowedEffects,
            Forms.DragDropEffects effect)
        {
            return new Forms.DragEventArgs(data, keyState: 8, x: 640, y: 480, allowedEffects, effect);
        }

        private static Forms.DragDropEffects Normalize(
            Forms.DragDropEffects effect,
            Forms.DragDropEffects allowedEffects)
        {
            return effect & allowedEffects &
                (Forms.DragDropEffects.Copy |
                 Forms.DragDropEffects.Move |
                 Forms.DragDropEffects.Link |
                 Forms.DragDropEffects.Scroll);
        }
    }
}
