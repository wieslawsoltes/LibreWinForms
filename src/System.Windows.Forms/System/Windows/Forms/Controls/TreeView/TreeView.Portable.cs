// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Drawing;

#pragma warning disable RS0016 // Portable overrides implement inherited API without adding new public contracts.

namespace System.Windows.Forms;

public partial class TreeView
{
    private const int PortableBorderInset = 1;
    private const int PortableGlyphWidth = 12;
    private const int PortableImageGap = 3;

    private int _portableVerticalScrollOffset;

    private readonly record struct PortableNodeLayout(
        TreeNode Node,
        Rectangle RowBounds,
        Rectangle GlyphBounds,
        Rectangle ImageBounds,
        Rectangle TextBounds);

    internal Rectangle GetPortableNodeBounds(TreeNode node, bool textOnly)
        => TryGetPortableNodeLayout(node, out PortableNodeLayout layout)
            ? textOnly ? layout.TextBounds : layout.RowBounds
            : Rectangle.Empty;

    internal bool IsPortableNodeVisible(TreeNode node)
        => TryGetPortableNodeLayout(node, out PortableNodeLayout layout)
            && layout.RowBounds.Bottom > 0
            && layout.RowBounds.Top < ClientSize.Height;

    internal void EnsurePortableNodeVisible(TreeNode node)
    {
        if (node.TreeView != this)
        {
            return;
        }

        for (TreeNode? ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            ancestor.Expand();
        }

        if (!TryGetPortableNodeLayout(node, out PortableNodeLayout layout))
        {
            return;
        }

        int viewportTop = PortableBorderInset;
        int viewportBottom = Math.Max(viewportTop, ClientSize.Height - PortableBorderInset);
        if (layout.RowBounds.Top < viewportTop)
        {
            SetPortableVerticalScrollOffset(
                _portableVerticalScrollOffset - (viewportTop - layout.RowBounds.Top));
        }
        else if (layout.RowBounds.Bottom > viewportBottom)
        {
            SetPortableVerticalScrollOffset(
                _portableVerticalScrollOffset + (layout.RowBounds.Bottom - viewportBottom));
        }
    }

    internal bool PortableBeforeExpand(TreeNode node)
    {
        TreeViewCancelEventArgs eventArgs = new(node, false, TreeViewAction.Expand);
        OnBeforeExpand(eventArgs);
        return !eventArgs.Cancel;
    }

    internal void PortableAfterExpand(TreeNode node)
        => OnAfterExpand(new TreeViewEventArgs(node, TreeViewAction.Expand));

    internal bool PortableBeforeCollapse(TreeNode node)
    {
        TreeViewCancelEventArgs eventArgs = new(node, false, TreeViewAction.Collapse);
        OnBeforeCollapse(eventArgs);
        return !eventArgs.Cancel;
    }

    internal void PortableAfterCollapse(TreeNode node)
        => OnAfterCollapse(new TreeViewEventArgs(node, TreeViewAction.Collapse));

    internal void PortableNodeChanged() => Invalidate();

    private TreeNode? GetPortableSelectedNode()
        => _selectedNode?.TreeView == this ? _selectedNode : null;

    private void SetPortableSelectedNode(TreeNode? node, TreeViewAction action)
    {
        if (ReferenceEquals(GetPortableSelectedNode(), node))
        {
            node?.EnsureVisible();
            return;
        }

        if (node is not null && node.TreeView != this)
        {
            // Preserve the canonical deferred-selection contract. TreeNodeCollection realizes
            // this cached node when it is subsequently attached to this TreeView.
            _selectedNode = node;
            return;
        }

        if (node is not null)
        {
            TreeViewCancelEventArgs eventArgs = new(node, false, action);
            OnBeforeSelect(eventArgs);
            if (eventArgs.Cancel)
            {
                return;
            }
        }

        _selectedNode = node;
        node?.EnsureVisible();
        Invalidate();
        if (node is not null)
        {
            OnAfterSelect(new TreeViewEventArgs(node, action));
        }
    }

    private TreeNode? GetPortableTopNode()
    {
        if (_topNode?.TreeView == this)
        {
            return _topNode;
        }

        foreach (PortableNodeLayout layout in EnumeratePortableLayouts())
        {
            if (layout.RowBounds.Bottom > PortableBorderInset)
            {
                return layout.Node;
            }
        }

        return null;
    }

    private void SetPortableTopNode(TreeNode? node)
    {
        if (node is null)
        {
            _topNode = null;
            SetPortableVerticalScrollOffset(0);
            return;
        }

        if (node.TreeView != this)
        {
            _topNode = node;
            return;
        }

        for (TreeNode? ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            ancestor.Expand();
        }

        if (TryGetPortableNodeLayout(node, out PortableNodeLayout layout))
        {
            _topNode = node;
            SetPortableVerticalScrollOffset(_portableVerticalScrollOffset + layout.RowBounds.Top - PortableBorderInset);
        }
    }

    private int GetPortableVisibleCount()
        => Math.Max(0, ClientSize.Height - (PortableBorderInset * 2)) / GetPortableRowHeight();

    private TreeViewHitTestInfo PortableHitTest(int x, int y)
    {
        if (x < 0)
        {
            return new TreeViewHitTestInfo(null, TreeViewHitTestLocations.LeftOfClientArea);
        }

        if (x >= ClientSize.Width)
        {
            return new TreeViewHitTestInfo(null, TreeViewHitTestLocations.RightOfClientArea);
        }

        if (y < 0)
        {
            return new TreeViewHitTestInfo(null, TreeViewHitTestLocations.AboveClientArea);
        }

        if (y >= ClientSize.Height)
        {
            return new TreeViewHitTestInfo(null, TreeViewHitTestLocations.BelowClientArea);
        }

        foreach (PortableNodeLayout layout in EnumeratePortableLayouts())
        {
            if (!layout.RowBounds.Contains(x, y))
            {
                continue;
            }

            TreeViewHitTestLocations location = layout.GlyphBounds.Contains(x, y)
                ? TreeViewHitTestLocations.PlusMinus
                : layout.ImageBounds.Contains(x, y)
                    ? TreeViewHitTestLocations.Image
                    : layout.TextBounds.Contains(x, y)
                        ? TreeViewHitTestLocations.Label
                        : x < layout.TextBounds.Left
                            ? TreeViewHitTestLocations.Indent
                            : TreeViewHitTestLocations.RightOfLabel;
            return new TreeViewHitTestInfo(layout.Node, location);
        }

        return new TreeViewHitTestInfo(null, TreeViewHitTestLocations.None);
    }

    private TreeNode? GetPortableNodeAt(int x, int y) => PortableHitTest(x, y).Node;

    private bool ProcessPortableNavigationKey(KeyEventArgs eventArgs)
    {
        TreeNode? selected = GetPortableSelectedNode();
        TreeNode? target = null;
        switch (eventArgs.KeyCode)
        {
            case Keys.Home:
                target = GetPortableFirstVisibleNode();
                break;
            case Keys.End:
                target = GetPortableLastVisibleNode();
                break;
            case Keys.Up:
                target = selected is null ? GetPortableLastVisibleNode() : GetPortableAdjacentNode(selected, -1);
                break;
            case Keys.Down:
                target = selected is null ? GetPortableFirstVisibleNode() : GetPortableAdjacentNode(selected, 1);
                break;
            case Keys.Right:
                if (selected is null)
                {
                    target = GetPortableFirstVisibleNode();
                }
                else if (selected.Nodes.Count > 0 && !selected.IsExpanded)
                {
                    selected.Expand();
                }
                else if (selected.IsExpanded && selected.Nodes.Count > 0)
                {
                    target = selected.Nodes[0];
                }

                break;
            case Keys.Left:
                if (selected?.IsExpanded == true && selected.Nodes.Count > 0)
                {
                    selected.Collapse();
                }
                else
                {
                    target = selected?.Parent;
                }

                break;
            default:
                return false;
        }

        if (target is not null)
        {
            SetPortableSelectedNode(target, TreeViewAction.ByKeyboard);
        }

        eventArgs.Handled = true;
        return true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        TreeViewHitTestInfo hit = PortableHitTest(e.X, e.Y);
        if (hit.Node is null)
        {
            return;
        }

        if ((hit.Location & TreeViewHitTestLocations.PlusMinus) != 0)
        {
            hit.Node.Toggle();
        }
        else
        {
            SetPortableSelectedNode(hit.Node, TreeViewAction.ByMouse);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Delta == 0)
        {
            return;
        }

        int wheelSteps = Math.Max(1, Math.Abs(e.Delta) / 120);
        int scrollLines = Math.Max(1, SystemInformation.MouseWheelScrollLines);
        int direction = e.Delta > 0 ? -1 : 1;
        SetPortableVerticalScrollOffset(
            _portableVerticalScrollOffset + (direction * wheelSteps * scrollLines * GetPortableRowHeight()));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        foreach (PortableNodeLayout layout in EnumeratePortableLayouts())
        {
            if (!layout.RowBounds.IntersectsWith(e.ClipRectangle))
            {
                continue;
            }

            TreeNode node = layout.Node;
            bool selected = ReferenceEquals(GetPortableSelectedNode(), node);
            TreeNodeStates state = selected ? TreeNodeStates.Selected : TreeNodeStates.Default;
            if (DrawMode == TreeViewDrawMode.OwnerDrawAll)
            {
                DrawTreeNodeEventArgs drawAll = new(e.Graphics, node, layout.RowBounds, state);
                OnDrawNode(drawAll);
                if (!drawAll.DrawDefault)
                {
                    continue;
                }
            }

            Color backColor = selected ? SystemColors.Highlight : node.BackColor.IsEmpty ? BackColor : node.BackColor;
            Color foreColor = selected ? SystemColors.HighlightText : node.ForeColor.IsEmpty ? ForeColor : node.ForeColor;
            using (Brush background = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(background, FullRowSelect ? layout.RowBounds : layout.TextBounds);
            }

            if (!layout.GlyphBounds.IsEmpty)
            {
                int centerX = layout.GlyphBounds.Left + (layout.GlyphBounds.Width / 2);
                int centerY = layout.GlyphBounds.Top + (layout.GlyphBounds.Height / 2);
                using Pen glyphPen = new(foreColor);
                e.Graphics.DrawRectangle(glyphPen, centerX - 4, centerY - 4, 8, 8);
                e.Graphics.DrawLine(glyphPen, centerX - 2, centerY, centerX + 2, centerY);
                if (!node.IsExpanded)
                {
                    e.Graphics.DrawLine(glyphPen, centerX, centerY - 2, centerX, centerY + 2);
                }
            }

            Image? image = GetPortableNodeImage(node, selected);
            if (image is not null && !layout.ImageBounds.IsEmpty)
            {
                e.Graphics.DrawImage(image, layout.ImageBounds);
            }

            Font font = node.NodeFont ?? Font;
            bool drawDefaultText = true;
            if (DrawMode == TreeViewDrawMode.OwnerDrawText)
            {
                DrawTreeNodeEventArgs drawText = new(e.Graphics, node, layout.TextBounds, state);
                OnDrawNode(drawText);
                drawDefaultText = drawText.DrawDefault;
            }

            if (drawDefaultText)
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    node.Text,
                    font,
                    layout.TextBounds,
                    foreColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
    }

    private bool TryGetPortableNodeLayout(TreeNode node, out PortableNodeLayout result)
    {
        foreach (PortableNodeLayout layout in EnumeratePortableLayouts())
        {
            if (ReferenceEquals(layout.Node, node))
            {
                result = layout;
                return true;
            }
        }

        result = default;
        return false;
    }

    private IEnumerable<PortableNodeLayout> EnumeratePortableLayouts()
    {
        int visibleIndex = 0;
        foreach (PortableNodeLayout layout in EnumeratePortableLayouts(Nodes, depth: 0, () => visibleIndex++))
        {
            yield return layout;
        }
    }

    private IEnumerable<PortableNodeLayout> EnumeratePortableLayouts(
        TreeNodeCollection nodes,
        int depth,
        Func<int> nextVisibleIndex)
    {
        for (int index = 0; index < nodes.Count; index++)
        {
            TreeNode node = nodes[index];
            yield return CreatePortableNodeLayout(node, depth, nextVisibleIndex());
            if (!node.IsExpanded)
            {
                continue;
            }

            foreach (PortableNodeLayout child in EnumeratePortableLayouts(node.Nodes, depth + 1, nextVisibleIndex))
            {
                yield return child;
            }
        }
    }

    private PortableNodeLayout CreatePortableNodeLayout(TreeNode node, int depth, int visibleIndex)
    {
        int rowHeight = GetPortableRowHeight();
        int rowTop = PortableBorderInset + (visibleIndex * rowHeight) - _portableVerticalScrollOffset;
        int rowWidth = Math.Max(0, ClientSize.Width - (PortableBorderInset * 2));
        Rectangle rowBounds = new(PortableBorderInset, rowTop, rowWidth, rowHeight);
        int glyphLeft = PortableBorderInset + (depth * Indent);
        Rectangle glyphBounds = node.Nodes.Count == 0
            ? Rectangle.Empty
            : new Rectangle(glyphLeft, rowTop, PortableGlyphWidth, rowHeight);
        int contentLeft = glyphLeft + PortableGlyphWidth;
        Image? image = GetPortableNodeImage(node, ReferenceEquals(GetPortableSelectedNode(), node));
        Rectangle imageBounds = Rectangle.Empty;
        if (image is not null && ImageList is not null)
        {
            Size imageSize = ImageList.ImageSize;
            imageBounds = new Rectangle(
                contentLeft,
                rowTop + Math.Max(0, (rowHeight - imageSize.Height) / 2),
                imageSize.Width,
                imageSize.Height);
            contentLeft += imageSize.Width + PortableImageGap;
        }

        Font font = node.NodeFont ?? Font;
        Size measured = TextRenderer.MeasureText(node.Text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        Rectangle textBounds = new(
            contentLeft,
            rowTop,
            Math.Min(Math.Max(0, rowBounds.Right - contentLeft), Math.Max(1, measured.Width)),
            rowHeight);
        return new PortableNodeLayout(node, rowBounds, glyphBounds, imageBounds, textBounds);
    }

    private Image? GetPortableNodeImage(TreeNode node, bool selected)
    {
        if (ImageList is null || ImageList.Images.Count == 0)
        {
            return null;
        }

        string key = selected ? node.SelectedImageKey : node.ImageKey;
        if (!string.IsNullOrEmpty(key) && ImageList.Images.ContainsKey(key))
        {
            return ImageList.Images[key];
        }

        int index = selected ? node.SelectedImageIndex : node.ImageIndex;
        if (index < 0)
        {
            index = selected ? SelectedImageIndex : ImageIndex;
        }

        return index >= 0 && index < ImageList.Images.Count ? ImageList.Images[index] : null;
    }

    private int GetPortableRowHeight()
        => Math.Max(ItemHeight, ImageList?.ImageSize.Height ?? 0);

    private int GetPortableMaximumScrollOffset()
    {
        int count = 0;
        foreach (PortableNodeLayout _ in EnumeratePortableLayouts())
        {
            count++;
        }

        int contentHeight = count * GetPortableRowHeight();
        return Math.Max(0, contentHeight - Math.Max(0, ClientSize.Height - (PortableBorderInset * 2)));
    }

    private void SetPortableVerticalScrollOffset(int value)
    {
        int next = Math.Clamp(value, 0, GetPortableMaximumScrollOffset());
        if (_portableVerticalScrollOffset == next)
        {
            return;
        }

        _portableVerticalScrollOffset = next;
        _topNode = null;
        Invalidate();
    }

    private TreeNode? GetPortableFirstVisibleNode()
    {
        foreach (PortableNodeLayout layout in EnumeratePortableLayouts())
        {
            return layout.Node;
        }

        return null;
    }

    private TreeNode? GetPortableLastVisibleNode()
    {
        TreeNode? result = null;
        foreach (PortableNodeLayout layout in EnumeratePortableLayouts())
        {
            result = layout.Node;
        }

        return result;
    }

    private TreeNode? GetPortableAdjacentNode(TreeNode node, int direction)
    {
        TreeNode? previous = null;
        bool returnNext = false;
        foreach (PortableNodeLayout layout in EnumeratePortableLayouts())
        {
            if (returnNext)
            {
                return layout.Node;
            }

            if (ReferenceEquals(layout.Node, node))
            {
                returnNext = direction > 0;
                if (direction < 0)
                {
                    return previous;
                }
            }

            previous = layout.Node;
        }

        return null;
    }
}
#pragma warning restore RS0016
#endif
