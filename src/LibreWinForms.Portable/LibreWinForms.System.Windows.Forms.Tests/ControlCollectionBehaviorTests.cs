using System;
using Forms = System.Windows.Forms;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class ControlCollectionBehaviorTests
{
    public static void Run()
    {
        AddedRemovedReplacementAndClearPublishTypedEvents();
        ReparentingMovesTheControlAndPublishesBalancedEvents();
        ReplacementDetachesFromThePreviousParent();
        ReaddingAndReorderingDoNotDuplicateChildren();
        NonGenericListAddsUseTheAuthoritativeControlCollectionPath();
        TabPageReparentingKeepsBothCollectionsSynchronized();
        ParentingCyclesFailClosed();
        ControlSizesClampNegativeDimensions();
        SplitContainerSupportsDesignerInitialization();
        Console.WriteLine("LibreWinForms control collection event tests passed.");
    }

    private static void AddedRemovedReplacementAndClearPublishTypedEvents()
    {
        var owner = new Forms.Control();
        var first = new Forms.Control { Name = "first" };
        var second = new Forms.Control { Name = "second" };
        var replacement = new Forms.Control { Name = "replacement" };
        var events = new System.Collections.Generic.List<string>();
        owner.ControlAdded += (_, e) => events.Add("add:" + e.Control.Name);
        owner.ControlRemoved += (_, e) => events.Add("remove:" + e.Control.Name);

        owner.Controls.Add(first);
        owner.Controls.Add(second);
        owner.Controls[0] = replacement;
        owner.Controls.Remove(second);
        owner.Controls.Clear();

        string sequence = string.Join(",", events);
        Assert(
            sequence == "add:first,add:second,remove:first,add:replacement,remove:second,remove:replacement",
            "Unexpected ControlAdded/ControlRemoved sequence: " + sequence);
        Assert(first.Parent is null && second.Parent is null && replacement.Parent is null,
            "Removed controls retained their parent.");
    }

    private static void ReparentingMovesTheControlAndPublishesBalancedEvents()
    {
        var oldParent = new Forms.Control { Name = "old" };
        var newParent = new Forms.Control { Name = "new" };
        var child = new Forms.Control { Name = "child" };
        var events = new System.Collections.Generic.List<string>();
        oldParent.ControlRemoved += (_, e) =>
        {
            events.Add("remove:" + e.Control.Name);
            Assert(e.Control.Parent is null, "ControlRemoved observed the stale old parent.");
            Assert(!oldParent.Controls.Contains(e.Control), "ControlRemoved observed a stale old-parent collection entry.");
        };
        newParent.ControlAdded += (_, e) =>
        {
            events.Add("add:" + e.Control.Name);
            Assert(ReferenceEquals(e.Control.Parent, newParent), "ControlAdded did not observe the new parent.");
            Assert(newParent.Controls.Contains(e.Control), "ControlAdded did not observe the new collection entry.");
        };

        oldParent.Controls.Add(child);
        newParent.Controls.Add(child);

        Assert(!oldParent.Controls.Contains(child), "Reparenting left the control in the old collection.");
        Assert(newParent.Controls.Count == 1 && ReferenceEquals(newParent.Controls[0], child),
            "Reparenting did not create one authoritative new-parent entry.");
        Assert(ReferenceEquals(child.Parent, newParent), "Reparenting left an incorrect Parent value.");
        Assert(string.Join(",", events) == "remove:child,add:child",
            "Reparenting events were not balanced and ordered.");
    }

    private static void ReplacementDetachesFromThePreviousParent()
    {
        var oldParent = new Forms.Control();
        var newParent = new Forms.Control();
        var oldChild = new Forms.Control { Name = "oldChild" };
        var replacement = new Forms.Control { Name = "replacement" };
        int oldParentRemoved = 0;
        int newParentRemoved = 0;
        int newParentAdded = 0;
        oldParent.ControlRemoved += (_, e) =>
        {
            if (ReferenceEquals(e.Control, replacement))
            {
                oldParentRemoved++;
            }
        };
        newParent.ControlRemoved += (_, e) =>
        {
            if (ReferenceEquals(e.Control, oldChild))
            {
                newParentRemoved++;
            }
        };
        newParent.ControlAdded += (_, e) =>
        {
            if (ReferenceEquals(e.Control, replacement))
            {
                newParentAdded++;
            }
        };

        oldParent.Controls.Add(replacement);
        newParent.Controls.Add(oldChild);
        newParent.Controls[0] = replacement;

        Assert(oldParentRemoved == 1 && newParentRemoved == 1 && newParentAdded == 1,
            "Replacement did not publish exactly one removal/addition per changed parent.");
        Assert(oldParent.Controls.Count == 0 && newParent.Controls.Count == 1,
            "Replacement left stale or duplicate collection entries.");
        Assert(oldChild.Parent is null && ReferenceEquals(replacement.Parent, newParent),
            "Replacement produced incorrect Parent values.");
    }

    private static void ReaddingAndReorderingDoNotDuplicateChildren()
    {
        var parent = new Forms.Control();
        var first = new Forms.Control { Name = "first" };
        var second = new Forms.Control { Name = "second" };
        int added = 0;
        int removed = 0;
        parent.ControlAdded += (_, _) => added++;
        parent.ControlRemoved += (_, _) => removed++;
        parent.Controls.Add(first);
        parent.Controls.Add(second);

        parent.Controls.Add(first);
        Assert(parent.Controls.Count == 2 && ReferenceEquals(parent.Controls[1], first),
            "Readding an existing child did not move it to the requested position.");
        Assert(added == 2 && removed == 0, "Pure reordering published add/remove events.");

        parent.Controls.SetChildIndex(first, 0);
        Assert(ReferenceEquals(parent.Controls[0], first) && parent.Controls.Count == 2,
            "SetChildIndex duplicated or lost a child.");
        Assert(added == 2 && removed == 0, "SetChildIndex published add/remove events.");
    }

    private static void NonGenericListAddsUseTheAuthoritativeControlCollectionPath()
    {
        var parent = new Forms.Control();
        var child = new Forms.Control();
        Assert(parent.Controls is System.Collections.IList,
            "ControlCollection does not expose the non-generic IList contract required by XML forms.");

        ((System.Collections.IList)parent.Controls).Add(child);
        Assert(parent.Controls.Count == 1
            && ReferenceEquals(parent.Controls[0], child)
            && ReferenceEquals(child.Parent, parent),
            "The non-generic IList path bypassed authoritative control parenting.");
    }

    private static void TabPageReparentingKeepsBothCollectionsSynchronized()
    {
        var oldTabs = new Forms.TabControl();
        var newTabs = new Forms.TabControl();
        var page = new Forms.TabPage { Name = "page" };
        oldTabs.TabPages.Add(page);

        newTabs.Controls.Add(page);

        Assert(oldTabs.Controls.Count == 0 && oldTabs.TabPages.Count == 0,
            "TabPage reparenting left stale entries in the old TabControl.");
        Assert(newTabs.Controls.Count == 1 && newTabs.TabPages.Count == 1,
            "TabPage reparenting did not synchronize the new TabControl collections.");
        Assert(ReferenceEquals(newTabs.Controls[0], page)
            && ReferenceEquals(newTabs.TabPages[0], page)
            && ReferenceEquals(page.Parent, newTabs),
            "TabPage reparenting produced mismatched collection/Parent state.");
    }

    private static void ParentingCyclesFailClosed()
    {
        var root = new Forms.Control();
        var child = new Forms.Control();
        root.Controls.Add(child);

        bool threw = false;
        try
        {
            child.Controls.Add(root);
        }
        catch (ArgumentException)
        {
            threw = true;
        }

        Assert(threw, "Parenting an ancestor below its descendant did not fail closed.");
        Assert(root.Parent is null && ReferenceEquals(child.Parent, root),
            "Rejected cyclic parenting mutated the tree.");
        Assert(root.Controls.Count == 1 && child.Controls.Count == 0,
            "Rejected cyclic parenting mutated a child collection.");
    }

    private static void ControlSizesClampNegativeDimensions()
    {
        var control = new Forms.Control
        {
            Size = new System.Drawing.Size(-20, -30)
        };
        Assert(control.Width == 0 && control.Height == 0,
            "Control.Size retained negative dimensions that cannot be represented by a WPF Rect.");

        control.SetBounds(4, 5, -6, 7);
        Assert(control.Left == 4 && control.Top == 5 && control.Width == 0 && control.Height == 7,
            "Control.SetBounds did not preserve location while clamping negative dimensions.");
    }

    private static void SplitContainerSupportsDesignerInitialization()
    {
        var splitContainer = new Forms.SplitContainer();
        Assert(splitContainer is System.ComponentModel.ISupportInitialize,
            "SplitContainer does not publish the generated-designer initialization contract.");
        Assert(splitContainer.Orientation == Forms.Orientation.Vertical,
            "SplitContainer does not use the WinForms vertical default orientation.");
        Assert(splitContainer.SplitterDistance == 50
            && splitContainer.SplitterWidth == 4
            && splitContainer.Panel1MinSize == 25
            && splitContainer.Panel2MinSize == 25,
            "SplitContainer does not use the WinForms default splitter geometry.");
        Assert(splitContainer.Controls.Count == 2
            && ReferenceEquals(splitContainer.Controls[0], splitContainer.Panel1)
            && ReferenceEquals(splitContainer.Controls[1], splitContainer.Panel2),
            "SplitContainer panels are not authoritative child controls.");

        ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
        splitContainer.Orientation = Forms.Orientation.Horizontal;
        splitContainer.SplitterDistance = 42;
        splitContainer.Panel1MinSize = 30;
        splitContainer.Panel2MinSize = 35;
        splitContainer.SplitterWidth = 6;
        Assert(splitContainer.Orientation == Forms.Orientation.Horizontal
            && splitContainer.SplitterDistance == 42,
            "SplitContainer did not keep live orientation and distance updates during initialization.");
        Assert(splitContainer.SplitterWidth == 4,
            "SplitContainer applied its deferred splitter width before EndInit.");
        Assert(splitContainer.Panel1MinSize == 25 && splitContainer.Panel2MinSize == 25,
            "SplitContainer applied deferred panel minimum sizes before EndInit.");

        ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
        Assert(splitContainer.SplitterWidth == 6
            && splitContainer.Panel1MinSize == 30
            && splitContainer.Panel2MinSize == 35,
            "SplitContainer did not validate and apply deferred layout values at EndInit.");

        ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
        splitContainer.SplitterWidth = 0;
        Assert(splitContainer.SplitterWidth == 6,
            "SplitContainer exposed an invalid pending splitter width.");
        bool invalidWidthRejected = false;
        try
        {
            ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
        }
        catch (ArgumentOutOfRangeException)
        {
            invalidWidthRejected = true;
        }

        Assert(invalidWidthRejected && splitContainer.SplitterWidth == 6,
            "SplitContainer did not reject an invalid deferred splitter width at EndInit.");

        bool immediateInvalidWidthRejected = false;
        try
        {
            splitContainer.SplitterWidth = 0;
        }
        catch (ArgumentOutOfRangeException)
        {
            immediateInvalidWidthRejected = true;
        }

        Assert(immediateInvalidWidthRejected && splitContainer.SplitterWidth == 6,
            "SplitContainer did not reject an invalid live splitter width immediately.");
        Assert(new Forms.NumericUpDown() is System.ComponentModel.ISupportInitialize,
            "NumericUpDown does not publish its generated-designer initialization contract.");
        Assert(new Forms.TrackBar() is System.ComponentModel.ISupportInitialize,
            "TrackBar does not publish its generated-designer initialization contract.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
