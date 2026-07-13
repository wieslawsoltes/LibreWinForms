using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing;
using System.Drawing.Design;
using System.Linq;
using System.Windows.Forms.Design.Behavior;

namespace System.Windows.Forms.Design
{
    [Flags]
    public enum SelectionRules
    {
        None = 0x00000000,
        Moveable = 0x10000000,
        Visible = 0x40000000,
        Locked = unchecked((int)0x80000000),
        TopSizeable = 0x00000001,
        BottomSizeable = 0x00000002,
        LeftSizeable = 0x00000004,
        RightSizeable = 0x00000008,
        AllSizeable = TopSizeable | BottomSizeable | LeftSizeable | RightSizeable
    }

    public class ControlDesigner : ComponentDesigner
    {
        private bool _dragging;

        protected Point LastPointerScreenPosition { get; private set; }

        public virtual Control Control => (Control)Component;

        public bool AutoResizeHandles { get; set; }

        public virtual bool ParticipatesWithSnapLines => true;

        public virtual IList SnapLines => CreateEdgeAndMarginSnapLines();

        public virtual SelectionRules SelectionRules
        {
            get
            {
                SelectionRules rules = SelectionRules.Visible;
                PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(Component);
                if (properties[nameof(Control.Location)] is { IsReadOnly: false })
                    rules |= SelectionRules.Moveable;
                if (properties[nameof(Control.Size)] is { IsReadOnly: false }
                    && (!Control.AutoSize || !AutoResizeHandles))
                {
                    rules |= SelectionRules.AllSizeable;
                }

                rules = Control.Dock switch
                {
                    DockStyle.Top => rules & ~(SelectionRules.Moveable | SelectionRules.TopSizeable | SelectionRules.LeftSizeable | SelectionRules.RightSizeable),
                    DockStyle.Left => rules & ~(SelectionRules.Moveable | SelectionRules.TopSizeable | SelectionRules.LeftSizeable | SelectionRules.BottomSizeable),
                    DockStyle.Right => rules & ~(SelectionRules.Moveable | SelectionRules.TopSizeable | SelectionRules.BottomSizeable | SelectionRules.RightSizeable),
                    DockStyle.Bottom => rules & ~(SelectionRules.Moveable | SelectionRules.LeftSizeable | SelectionRules.BottomSizeable | SelectionRules.RightSizeable),
                    DockStyle.Fill => rules & ~(SelectionRules.Moveable | SelectionRules.AllSizeable),
                    _ => rules
                };

                if (properties["Locked"]?.GetValue(Component) is bool locked && locked)
                    return SelectionRules.Locked | SelectionRules.Visible;

                return rules;
            }
        }

        public override void Initialize(IComponent component)
        {
            ArgumentNullException.ThrowIfNull(component);
            if (component is not System.Windows.Forms.Control)
                throw new ArgumentException("ControlDesigner requires a Control component.", nameof(component));

            base.Initialize(component);
            Control.AddDesignerMouseHandlers(HandleMouseDown, HandleMouseMove, HandleMouseUp);
            Control.Paint += HandlePaint;
            Control.DragDrop += HandleDragDrop;
        }

        public virtual bool CanBeParentedTo(IDesigner parentDesigner)
        {
            return parentDesigner is ParentControlDesigner parent
                && !Control.Contains(parent.Control);
        }

        protected void DisplayError(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            if (GetService(typeof(IUIService)) is IUIService uiService)
            {
                uiService.ShowError(exception);
                return;
            }

            MessageBox.Show(exception.Message, "Designer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Component is Control control)
            {
                control.RemoveDesignerMouseHandlers(HandleMouseDown, HandleMouseMove, HandleMouseUp);
                control.Paint -= HandlePaint;
                control.DragDrop -= HandleDragDrop;
                control.Capture = false;
            }

            _dragging = false;
            base.Dispose(disposing);
        }

        protected virtual void OnDragDrop(DragEventArgs de)
        {
        }

        protected virtual void OnMouseDragBegin(int x, int y)
        {
            if (GetService(typeof(ISelectionService)) is ISelectionService selectionService)
                selectionService.SetSelectedComponents(new object[] { Component }, SelectionTypes.Primary);

            Control.Capture = true;
        }

        protected virtual void OnMouseDragMove(int x, int y)
        {
        }

        protected virtual void OnMouseDragEnd(bool cancel)
        {
            Control.Capture = false;
        }

        protected virtual void OnPaintAdornments(PaintEventArgs pe)
        {
        }

        protected virtual void OnSetCursor()
        {
            Cursor.Current = Control.Dock == DockStyle.None ? Cursors.SizeAll : Cursors.Default;
        }

        private ArrayList CreateEdgeAndMarginSnapLines()
        {
            int width = Control.Width;
            int height = Control.Height;
            Padding margin = Control.Margin;

            return new ArrayList(8)
            {
                new SnapLine(SnapLineType.Top, 0, SnapLinePriority.Low),
                new SnapLine(SnapLineType.Bottom, height - 1, SnapLinePriority.Low),
                new SnapLine(SnapLineType.Left, 0, SnapLinePriority.Low),
                new SnapLine(SnapLineType.Right, width - 1, SnapLinePriority.Low),
                new SnapLine(SnapLineType.Horizontal, -margin.Top, SnapLine.MarginTop, SnapLinePriority.Always),
                new SnapLine(SnapLineType.Horizontal, margin.Bottom + height, SnapLine.MarginBottom, SnapLinePriority.Always),
                new SnapLine(SnapLineType.Vertical, -margin.Left, SnapLine.MarginLeft, SnapLinePriority.Always),
                new SnapLine(SnapLineType.Vertical, margin.Right + width, SnapLine.MarginRight, SnapLinePriority.Always)
            };
        }

        private void HandleMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            _dragging = true;
            Point screenPoint = Control.PointToScreen(e.Location);
            LastPointerScreenPosition = screenPoint;
            Cursor.Position = screenPoint;
            OnMouseDragBegin(screenPoint.X, screenPoint.Y);
        }

        private void HandleMouseMove(object? sender, MouseEventArgs e)
        {
            Point screenPoint = Control.PointToScreen(e.Location);
            LastPointerScreenPosition = screenPoint;
            Cursor.Position = screenPoint;
            OnSetCursor();
            if (!_dragging)
                return;

            OnMouseDragMove(screenPoint.X, screenPoint.Y);
        }

        private void HandleMouseUp(object? sender, MouseEventArgs e)
        {
            if (!_dragging || e.Button != MouseButtons.Left)
                return;

            LastPointerScreenPosition = Control.PointToScreen(e.Location);
            Cursor.Position = LastPointerScreenPosition;
            _dragging = false;
            OnMouseDragEnd(cancel: false);
        }

        private void HandlePaint(object? sender, PaintEventArgs e) => OnPaintAdornments(e);

        private void HandleDragDrop(object? sender, DragEventArgs e) => OnDragDrop(e);
    }

    public class ParentControlDesigner : ControlDesigner
    {
        private ToolboxItem? _placementTool;
        private Point _placementStart;

        public override IList SnapLines
        {
            get
            {
                IList snapLines = base.SnapLines;
                Rectangle displayRectangle = Control.DisplayRectangle;

                snapLines.Add(new SnapLine(SnapLineType.Vertical, displayRectangle.Left, SnapLine.PaddingLeft, SnapLinePriority.Always));
                snapLines.Add(new SnapLine(SnapLineType.Vertical, displayRectangle.Right, SnapLine.PaddingRight, SnapLinePriority.Always));
                snapLines.Add(new SnapLine(SnapLineType.Horizontal, displayRectangle.Top, SnapLine.PaddingTop, SnapLinePriority.Always));
                snapLines.Add(new SnapLine(SnapLineType.Horizontal, displayRectangle.Bottom, SnapLine.PaddingBottom, SnapLinePriority.Always));
                return snapLines;
            }
        }

        protected override void OnMouseDragBegin(int x, int y)
        {
            if (GetSelectedTool(out ToolboxItem? tool) && tool is not null)
            {
                _placementTool = tool;
                _placementStart = Control.PointToClient(new Point(x, y));
                Control.Capture = true;
                return;
            }

            base.OnMouseDragBegin(x, y);
        }

        protected override void OnMouseDragMove(int x, int y)
        {
            if (_placementTool is not null)
            {
                Control.Invalidate();
                return;
            }

            base.OnMouseDragMove(x, y);
        }

        protected override void OnMouseDragEnd(bool cancel)
        {
            ToolboxItem? tool = _placementTool;
            _placementTool = null;
            if (tool is not null)
            {
                try
                {
                    if (!cancel)
                        CreateTool(tool, _placementStart, Control.PointToClient(LastPointerScreenPosition));
                }
                finally
                {
                    Control.Capture = false;
                    Control.Invalidate();
                }

                return;
            }

            base.OnMouseDragEnd(cancel);
        }

        protected virtual IComponent[] CreateTool(ToolboxItem tool, Point start, Point end)
        {
            ArgumentNullException.ThrowIfNull(tool);
            if (GetService(typeof(IDesignerHost)) is not IDesignerHost host)
                return Array.Empty<IComponent>();

            Rectangle bounds = Rectangle.FromLTRB(
                Math.Min(start.X, end.X),
                Math.Min(start.Y, end.Y),
                Math.Max(start.X, end.X),
                Math.Max(start.Y, end.Y));
            bool hasSize = bounds.Width >= SystemInformation.DragSize.Width
                || bounds.Height >= SystemInformation.DragSize.Height;
            var defaultValues = new Hashtable
            {
                ["Parent"] = Control,
                [nameof(Control.Location)] = hasSize ? bounds.Location : start
            };
            if (hasSize)
                defaultValues[nameof(Control.Size)] = bounds.Size;

            using DesignerTransaction transaction = host.CreateTransaction("Create " + tool.DisplayName);
            IComponent[] components = tool.CreateComponents(host, defaultValues);
            foreach (Control child in components.OfType<Control>())
            {
                if (!ReferenceEquals(child.Parent, Control))
                    Control.Controls.Add(child);
            }

            if (components.Length > 0
                && GetService(typeof(ISelectionService)) is ISelectionService selectionService)
            {
                selectionService.SetSelectedComponents(components, SelectionTypes.Replace);
            }

            transaction.Commit();
            if (GetService(typeof(IToolboxService)) is IToolboxService toolboxService)
                toolboxService.SelectedToolboxItemUsed();
            return components;
        }

        protected IComponent[] CreateToolCentered(ToolboxItem tool)
        {
            Point center = new(Control.ClientRectangle.Width / 2, Control.ClientRectangle.Height / 2);
            return CreateTool(tool, center, center);
        }

        private bool GetSelectedTool(out ToolboxItem? tool)
        {
            IDesignerHost? host = GetService(typeof(IDesignerHost)) as IDesignerHost;
            IToolboxService? toolbox = GetService(typeof(IToolboxService)) as IToolboxService;
            tool = host is null
                ? toolbox?.GetSelectedToolboxItem()
                : toolbox?.GetSelectedToolboxItem(host) ?? toolbox?.GetSelectedToolboxItem();
            return tool?.GetType(host) is Type toolType
                && typeof(IComponent).IsAssignableFrom(toolType);
        }
    }

    public class DocumentDesigner : ParentControlDesigner, IRootDesigner, IToolboxUser
    {
        private static readonly ViewTechnology[] s_supportedTechnologies = { ViewTechnology.Default };

        public ViewTechnology[] SupportedTechnologies => (ViewTechnology[])s_supportedTechnologies.Clone();

        public object GetView(ViewTechnology technology)
        {
            if (technology != ViewTechnology.Default)
                throw new ArgumentException("Unsupported designer view technology.", nameof(technology));

            return Control;
        }

        public bool GetToolSupported(ToolboxItem tool)
        {
            ArgumentNullException.ThrowIfNull(tool);
            IDesignerHost? host = GetService(typeof(IDesignerHost)) as IDesignerHost;
            return tool.GetType(host) is Type toolType
                && typeof(IComponent).IsAssignableFrom(toolType);
        }

        public void ToolPicked(ToolboxItem tool)
        {
            if (GetToolSupported(tool))
                CreateToolCentered(tool);
        }
    }

    public class ComponentTray : Component
    {
        public bool ShowLargeIcons { get; set; }
    }
}
