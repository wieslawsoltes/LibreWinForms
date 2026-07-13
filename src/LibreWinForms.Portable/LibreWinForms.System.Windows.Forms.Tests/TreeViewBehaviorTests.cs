using System;
using System.Diagnostics;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class TreeViewBehaviorTests
{
    private static int Main()
    {
        try
        {
            LayoutHitTestingExpansionAndScrollingStayInSync();
            ImageGeometryUsesImageListMetrics();
            KeyboardNavigationUsesVisibleTreeOrder();
            BeginUpdateCoalescesTreeInvalidation();
            WideTreeTraversalUsesLinearCollectionAccess();
            MutableNodeStateInvalidatesAndRaisesAfterCheckOnce();
            SelectionGeometryUsesLabelBoundsUnlessFullRowSelect();
            ListViewListModeUsesColumnarGeometry();
            ControlCollectionBehaviorTests.Run();
            DragDropHostBehaviorTests.Run();
            DispatcherInvocationBehaviorTests.Run();
            KeyboardRoutingBehaviorTests.Run();
            HexEditorInputScrollDtoBehaviorTests.Run();
            HexEditorContractBehaviorTests.Run();
            CreateGraphicsHostBehaviorTests.Run();
            TextRendererBehaviorTests.Run();
            ScrollableControlBehaviorTests.Run();
            Console.WriteLine("LibreWinForms TreeView behavior tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void ImageGeometryUsesImageListMetrics()
    {
        using var bitmap = new Bitmap(20, 20);
        using var imageList = new Forms.ImageList { ImageSize = new Size(20, 20) };
        imageList.Images.Add(bitmap);
        var treeView = new Forms.TreeView
        {
            Size = new Size(160, 52),
            ImageIndex = 0,
            ImageList = imageList
        };
        Forms.TreeNode root = treeView.Nodes.Add("Root");

        Assert(treeView.TryGetNodeLayout(root, out Forms.TreeNodeLayout layout), "Image-backed root layout is missing.");
        Assert(layout.RowBounds.Height == 22, "Tree row height does not accommodate ImageList.ImageSize.");
        Assert(layout.ImageBounds == new Rectangle(16, 4, 20, 20), "Image bounds do not use ImageList metrics.");
        Assert(layout.TextBounds.Left == 39, "Text bounds do not follow the typed image bounds.");

        int invalidated = 0;
        treeView.Invalidated += (_, _) => invalidated++;
        imageList.ImageSize = new Size(24, 18);
        Assert(invalidated == 1, "ImageList metric changes did not invalidate TreeView.");
        Assert(treeView.TryGetNodeLayout(root, out layout), "Resized image-backed root layout is missing.");
        Assert(layout.ImageBounds.Width == 24 && layout.TextBounds.Left == 43, "Updated ImageList metrics did not reach layout.");
    }

    private static void LayoutHitTestingExpansionAndScrollingStayInSync()
    {
        var treeView = new Forms.TreeView { Size = new Size(180, 58) };
        var root = new Forms.TreeNode("Root");
        var children = new Forms.TreeNode[7];
        for (int index = 0; index < children.Length; index++)
        {
            children[index] = root.Nodes.Add("Child " + index);
        }

        Forms.TreeNode branch = children[5];
        Forms.TreeNode deepNode = branch.Nodes.Add("Deep node");
        Forms.TreeNode secondRoot = treeView.Nodes.Add("Second root");
        treeView.Nodes.Insert(0, root);

        Assert(treeView.TryGetNodeLayout(root, out Forms.TreeNodeLayout rootLayout), "Collapsed root layout is missing.");
        Assert(rootLayout.VisibleIndex == 0 && rootLayout.Depth == 0, "Root visible index/depth is incorrect.");
        Assert(rootLayout.RowBounds.Top == 3 && rootLayout.RowBounds.Height == 18, "Root row geometry is incorrect.");
        Assert(!rootLayout.GlyphBounds.IsEmpty, "Expandable root has no glyph hit bounds.");
        Assert(rootLayout.TextBounds.Left == 16, "Root text does not reserve the glyph slot.");
        Assert(root.Bounds == rootLayout.TextBounds, "TreeNode.Bounds does not match the shared text geometry.");
        Assert(
            ReferenceEquals(treeView.GetNodeAt(rootLayout.RowBounds.Right - 1, CenterY(rootLayout.RowBounds)), root),
            "Row hit testing does not use the shared row bounds.");

        bool toggled = treeView.TryToggleExpansionAt(
            CenterX(rootLayout.GlyphBounds),
            CenterY(rootLayout.GlyphBounds));
        Assert(toggled && root.IsExpanded, "Glyph hit did not expand the root node.");
        Assert(treeView.TryGetNodeLayout(children[0], out Forms.TreeNodeLayout firstChildLayout), "Expanded child layout is missing.");
        Assert(firstChildLayout.VisibleIndex == 1 && firstChildLayout.Depth == 1, "Expanded child order/depth is incorrect.");
        Assert(firstChildLayout.GlyphBounds.IsEmpty, "Leaf node unexpectedly has glyph hit bounds.");
        Assert(firstChildLayout.TextBounds.Left == 30, "Child indentation does not match the renderer contract.");

        treeView.SelectedNode = secondRoot;
        Assert(treeView.TryGetNodeLayout(secondRoot, out Forms.TreeNodeLayout selectedLayout), "Selected root layout is missing.");
        Assert(
            selectedLayout.RowBounds.Top >= 3 && selectedLayout.RowBounds.Bottom <= treeView.ClientSize.Height - 1,
            "Selecting a visible node did not scroll it into the viewport.");
        Assert(
            ReferenceEquals(treeView.GetNodeAt(CenterX(selectedLayout.RowBounds), CenterY(selectedLayout.RowBounds)), secondRoot),
            "Scrolled hit testing does not agree with selected-node layout.");

        root.Collapse();
        branch.Collapse();
        treeView.SelectedNode = deepNode;
        Assert(root.IsExpanded && branch.IsExpanded, "Selecting a descendant did not expand all ancestors.");
        Assert(treeView.TryGetNodeLayout(deepNode, out Forms.TreeNodeLayout deepLayout), "Selected descendant layout is missing.");
        Assert(
            deepLayout.RowBounds.Top >= 3 && deepLayout.RowBounds.Bottom <= treeView.ClientSize.Height - 1,
            "Selected descendant was not scrolled into the viewport.");
        Assert(
            ReferenceEquals(treeView.GetNodeAt(CenterX(deepLayout.RowBounds), CenterY(deepLayout.RowBounds)), deepNode),
            "Descendant hit testing does not follow the scrolled layout.");

        Assert(treeView.TryGetNodeLayout(root, out Forms.TreeNodeLayout beforeWheel), "Scrolled root layout is missing.");
        treeView.RaiseMouseWheel(new Forms.MouseEventArgs(Forms.MouseButtons.None, 0, 0, 0, 120));
        Assert(treeView.TryGetNodeLayout(root, out Forms.TreeNodeLayout afterWheel), "Wheel-up root layout is missing.");
        Assert(afterWheel.RowBounds.Top > beforeWheel.RowBounds.Top, "Mouse wheel did not move the shared row geometry.");

        root.Collapse();
        branch.Collapse();
        deepNode.EnsureVisible();
        Assert(root.IsExpanded && branch.IsExpanded, "TreeNode.EnsureVisible did not expand all ancestors.");
        Assert(treeView.TryGetNodeLayout(deepNode, out Forms.TreeNodeLayout ensuredLayout), "Ensured descendant layout is missing.");
        Assert(
            ensuredLayout.RowBounds.Top >= 3 && ensuredLayout.RowBounds.Bottom <= treeView.ClientSize.Height - 1,
            "TreeNode.EnsureVisible did not bring the row into the viewport.");

        treeView.SelectedNode = root;
        Assert(treeView.TryGetNodeLayout(root, out rootLayout), "Selected root layout is missing before glyph collapse.");
        Assert(
            treeView.TryToggleExpansionAt(CenterX(rootLayout.GlyphBounds), CenterY(rootLayout.GlyphBounds)),
            "Expanded root glyph was not hit-testable.");
        Assert(!root.IsExpanded && ReferenceEquals(treeView.SelectedNode, root), "Glyph collapse changed selection or failed to collapse.");
    }

    private static void KeyboardNavigationUsesVisibleTreeOrder()
    {
        var treeView = new Forms.TreeView { Size = new Size(160, 76) };
        Forms.TreeNode root = treeView.Nodes.Add("Root");
        Forms.TreeNode branch = root.Nodes.Add("Branch");
        Forms.TreeNode leaf = branch.Nodes.Add("Leaf");
        Forms.TreeNode sibling = root.Nodes.Add("Sibling");
        Forms.TreeNode secondRoot = treeView.Nodes.Add("Second root");
        Forms.TreeViewAction lastAction = Forms.TreeViewAction.Unknown;
        treeView.AfterSelect += (_, e) => lastAction = e.Action;
        treeView.SelectedNode = root;

        RaiseKey(treeView, Forms.Keys.Right);
        Assert(root.IsExpanded && ReferenceEquals(treeView.SelectedNode, root), "Right did not expand the selected node.");
        RaiseKey(treeView, Forms.Keys.Right);
        Assert(ReferenceEquals(treeView.SelectedNode, branch), "Right did not move to the first visible child.");
        RaiseKey(treeView, Forms.Keys.Right);
        Assert(branch.IsExpanded && ReferenceEquals(treeView.SelectedNode, branch), "Right did not expand the selected branch.");
        RaiseKey(treeView, Forms.Keys.Right);
        Assert(ReferenceEquals(treeView.SelectedNode, leaf), "Right did not enter the expanded branch.");
        Assert(lastAction == Forms.TreeViewAction.ByKeyboard, "Keyboard selection did not publish ByKeyboard action.");

        RaiseKey(treeView, Forms.Keys.Left);
        Assert(ReferenceEquals(treeView.SelectedNode, branch), "Left did not move from a leaf to its parent.");
        RaiseKey(treeView, Forms.Keys.Left);
        Assert(!branch.IsExpanded && ReferenceEquals(treeView.SelectedNode, branch), "Left did not collapse the selected branch.");
        RaiseKey(treeView, Forms.Keys.Left);
        Assert(ReferenceEquals(treeView.SelectedNode, root), "Left did not move from a collapsed branch to its parent.");
        RaiseKey(treeView, Forms.Keys.Down);
        Assert(ReferenceEquals(treeView.SelectedNode, branch), "Down did not follow visible tree order.");
        RaiseKey(treeView, Forms.Keys.Down);
        Assert(ReferenceEquals(treeView.SelectedNode, sibling), "Down did not skip collapsed descendants.");
        RaiseKey(treeView, Forms.Keys.End);
        Assert(ReferenceEquals(treeView.SelectedNode, secondRoot), "End did not select the last visible node.");
        RaiseKey(treeView, Forms.Keys.Home);
        Assert(ReferenceEquals(treeView.SelectedNode, root), "Home did not select the first visible node.");
        RaiseKey(treeView, Forms.Keys.Up);
        Assert(ReferenceEquals(treeView.SelectedNode, root), "Up moved before the first visible node.");

        treeView.ExpandAll();
        Assert(root.IsExpanded && branch.IsExpanded, "ExpandAll did not expand descendants.");
        treeView.CollapseAll();
        Assert(!root.IsExpanded && !branch.IsExpanded, "CollapseAll did not collapse descendants.");
    }

    private static void BeginUpdateCoalescesTreeInvalidation()
    {
        var treeView = new Forms.TreeView();
        int invalidated = 0;
        treeView.Invalidated += (_, _) => invalidated++;
        treeView.BeginUpdate();
        treeView.Nodes.Add("One");
        treeView.Nodes.Add("Two");
        Assert(invalidated == 0, "BeginUpdate did not suppress intermediate invalidations.");
        treeView.EndUpdate();
        Assert(invalidated == 1, "EndUpdate did not publish one coalesced invalidation.");
    }

    private static void WideTreeTraversalUsesLinearCollectionAccess()
    {
        const int nodeCount = 12_000;
        var treeView = new Forms.TreeView { Size = new Size(320, 180) };
        treeView.BeginUpdate();
        try
        {
            for (int index = 0; index < nodeCount; index++)
            {
                treeView.Nodes.Add("Node " + index);
            }
        }
        finally
        {
            treeView.EndUpdate();
        }

        Forms.TreeNodeLayoutEnumerator warmup = treeView.GetVisibleNodeLayouts().GetEnumerator();
        Assert(warmup.MoveNext(), "Wide-tree traversal warmup did not produce a node.");

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        Forms.TreeNodeLayoutEnumerator layouts = treeView.GetVisibleNodeLayouts().GetEnumerator();
        int visited = 0;
        while (layouts.MoveNext())
        {
            visited++;
        }

        stopwatch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert(visited == nodeCount, "Wide-tree traversal skipped visible root nodes.");
        Assert(
            layouts.CollectionAccessCount == nodeCount,
            "Wide-tree traversal did not perform one indexed collection access per visible root node.");
        Assert(allocated < 1024, "Wide-tree traversal allocated on the ordinary-depth hot path: " + allocated + " bytes.");
        Assert(stopwatch.Elapsed < TimeSpan.FromSeconds(2), "Wide-tree traversal exceeded the linear-time regression budget.");

        var deepTree = new Forms.TreeView { Size = new Size(200, 200) };
        Forms.TreeNode cursor = deepTree.Nodes.Add("Depth 0");
        for (int depth = 1; depth < 14; depth++)
        {
            cursor.Expand();
            cursor = cursor.Nodes.Add("Depth " + depth);
        }

        Forms.TreeNodeLayoutEnumerator deepLayouts = deepTree.GetVisibleNodeLayouts().GetEnumerator();
        int deepCount = 0;
        while (deepLayouts.MoveNext())
        {
            Assert(deepLayouts.Current.Depth == deepCount, "Overflow traversal stack reported the wrong depth.");
            deepCount++;
        }

        Assert(deepCount == 14, "Overflow traversal stack lost deep expanded nodes.");
    }

    private static void MutableNodeStateInvalidatesAndRaisesAfterCheckOnce()
    {
        var treeView = new Forms.TreeView { Size = new Size(220, 80) };
        Forms.TreeNode node = treeView.Nodes.Add("Original");
        int invalidated = 0;
        treeView.Invalidated += (_, _) => invalidated++;

        node.Text = "Renamed";
        Assert(invalidated == 1, "Renaming an attached TreeNode did not invalidate its TreeView.");
        node.Text = "Renamed";
        Assert(invalidated == 1, "Assigning identical TreeNode.Text invalidated needlessly.");

        node.ImageIndex = 2;
        node.SelectedImageIndex = 3;
        node.ImageKey = "normal";
        node.SelectedImageKey = "selected";
        Assert(invalidated == 5, "Mutable TreeNode image properties did not invalidate exactly once each.");
        Assert(node.ImageIndex == -1 && node.SelectedImageIndex == -1, "Image keys did not replace index selection.");

        treeView.ImageIndex = 4;
        treeView.SelectedImageIndex = 5;
        Assert(invalidated == 7, "TreeView default image index changes did not invalidate retained rendering.");

        node.IsVisible = false;
        Assert(!treeView.TryGetNodeLayout(node, out _), "Hidden TreeNode remained in visible layout traversal.");
        node.IsVisible = true;
        Assert(treeView.TryGetNodeLayout(node, out _), "Restored TreeNode did not return to visible layout traversal.");

        int afterCheckCount = 0;
        bool handlerObservedCheckedState = false;
        bool nestedNodeEventObserved = false;
        Forms.TreeViewAction action = Forms.TreeViewAction.ByMouse;
        Forms.TreeNode secondNode = treeView.Nodes.Add("Second");
        treeView.AfterCheck += (_, e) =>
        {
            afterCheckCount++;
            action = e.Action;
            if (ReferenceEquals(e.Node, node))
            {
                handlerObservedCheckedState = node.Checked;
                secondNode.Checked = true;
                node.Checked = false;
            }
            else if (ReferenceEquals(e.Node, secondNode))
            {
                nestedNodeEventObserved = true;
            }
        };

        node.Checked = true;
        Assert(afterCheckCount == 2, "AfterCheck suppressed the normal nested notification for a different TreeNode.");
        Assert(handlerObservedCheckedState, "AfterCheck ran before TreeNode.Checked was updated.");
        Assert(nestedNodeEventObserved && secondNode.Checked, "Nested TreeNode.Checked state/event was not preserved.");
        Assert(!node.Checked, "A non-recursive AfterCheck update did not preserve the handler's final checked state.");
        Assert(action == Forms.TreeViewAction.Unknown, "Programmatic TreeNode.Checked reported the wrong action.");
    }

    private static void SelectionGeometryUsesLabelBoundsUnlessFullRowSelect()
    {
        var treeView = new Forms.TreeView { Size = new Size(260, 80) };
        Forms.TreeNode node = treeView.Nodes.Add("Short label");
        treeView.SelectedNode = node;

        Assert(treeView.TryGetNodeLayout(node, out Forms.TreeNodeLayout layout), "Selected node layout is missing.");
        Assert(layout.SelectionBounds == layout.TextBounds, "Default TreeView selection is not constrained to label bounds.");
        Assert(layout.TextBounds.Width < layout.RowBounds.Width, "TreeNode label bounds still span the complete row.");
        Assert(layout.OwnerDrawBounds.Width > layout.TextBounds.Width, "OwnerDrawAll bounds were accidentally narrowed to the text label.");

        int invalidated = 0;
        treeView.Invalidated += (_, _) => invalidated++;
        treeView.FullRowSelect = true;
        Assert(invalidated == 1, "TreeView.FullRowSelect did not invalidate retained rendering.");
        Assert(treeView.TryGetNodeLayout(node, out layout), "Full-row selected node layout is missing.");
        Assert(layout.SelectionBounds == layout.RowBounds, "FullRowSelect did not expand selection to the shared row bounds.");
        Assert(layout.TextBounds.Width < layout.SelectionBounds.Width, "FullRowSelect unexpectedly widened owner-draw text bounds.");
    }

    private static void ListViewListModeUsesColumnarGeometry()
    {
        var listView = new Forms.ListView
        {
            Size = new Size(300, 70),
            MultiSelect = false,
            View = Forms.View.List
        };
        for (int index = 0; index < 7; index++)
        {
            listView.Items.Add("Item " + index);
        }

        Rectangle first = listView.GetItemRect(0);
        Rectangle second = listView.GetItemRect(1);
        Rectangle fourth = listView.GetItemRect(3);
        Assert(second.Left == first.Left && second.Top > first.Top, "List mode does not fill the first column top-to-bottom.");
        Assert(fourth.Left > first.Left && fourth.Top == first.Top, "List mode did not start a second column.");
        Assert(fourth.Width < listView.ClientSize.Width, "List mode still uses SmallIcon full-width rows.");
        Assert(
            ReferenceEquals(listView.GetItemAt(CenterX(fourth), CenterY(fourth)), listView.Items[3]),
            "List mode hit testing disagrees with columnar item geometry.");

        listView.Items[0].Selected = true;
        RaiseListKey(listView, Forms.Keys.Down);
        Assert(listView.Items[1].Selected, "List mode Down did not move within its column.");
        RaiseListKey(listView, Forms.Keys.Right);
        Assert(listView.Items[4].Selected, "List mode Right did not preserve the row in the next column.");
        RaiseListKey(listView, Forms.Keys.Left);
        Assert(listView.Items[1].Selected, "List mode Left did not return to the previous column.");
        listView.CheckBoxes = true;
        RaiseListKey(listView, Forms.Keys.Space);
        Assert(listView.Items[1].Checked, "List mode Space did not preserve ListView checkbox keyboard behavior.");

        listView.EnsureVisible(6);
        Rectangle ensured = listView.GetItemRect(6);
        Assert(ensured.Left >= 1 && ensured.Right <= listView.ClientSize.Width - 1, "List mode EnsureVisible did not scroll horizontally.");
        Assert(
            ReferenceEquals(listView.GetItemAt(CenterX(ensured), CenterY(ensured)), listView.Items[6]),
            "List mode hit testing did not follow horizontal scrolling.");

        Rectangle beforeWheel = listView.GetItemRect(0);
        listView.RaiseMouseWheel(new Forms.MouseEventArgs(Forms.MouseButtons.None, 0, 0, 0, 120));
        Rectangle afterWheel = listView.GetItemRect(0);
        Assert(afterWheel.Left > beforeWheel.Left, "List mode wheel-up did not move the horizontal item geometry.");

        listView.View = Forms.View.SmallIcon;
        Rectangle smallIconFirst = listView.GetItemRect(0);
        Rectangle smallIconSecond = listView.GetItemRect(1);
        Assert(smallIconSecond.Top > smallIconFirst.Top, "SmallIcon rows no longer advance vertically.");
        Assert(smallIconFirst.Width == listView.ClientSize.Width - 2, "SmallIcon mode no longer uses full-width rows.");
    }

    private static void RaiseKey(Forms.TreeView treeView, Forms.Keys key)
    {
        var eventArgs = new Forms.KeyEventArgs(key);
        treeView.RaiseKeyDown(eventArgs);
        Assert(eventArgs.Handled, key + " was not handled by TreeView navigation.");
    }

    private static void RaiseListKey(Forms.ListView listView, Forms.Keys key)
    {
        var eventArgs = new Forms.KeyEventArgs(key);
        listView.RaiseKeyDown(eventArgs);
        Assert(eventArgs.Handled, key + " was not handled by ListView navigation.");
    }

    private static int CenterX(Rectangle bounds) => bounds.Left + Math.Max(0, bounds.Width / 2);

    private static int CenterY(Rectangle bounds) => bounds.Top + Math.Max(0, bounds.Height / 2);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
