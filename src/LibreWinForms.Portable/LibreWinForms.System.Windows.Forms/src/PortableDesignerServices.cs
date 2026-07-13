using System.ComponentModel;
using System.ComponentModel.Design;

namespace System.ComponentModel.Design
{
    /// <summary>
    /// Provides explicit cleanup for designer-host service aliases owned by a portable design surface.
    /// </summary>
    public interface IPortableDesignSurfaceServiceCleanup
    {
        /// <summary>
        /// Removes designer-host service aliases that outlive design-surface unloading.
        /// </summary>
        void RemoveDesignerHostServices();
    }

    internal sealed class PortableDesignSurfaceServiceCleanup : IPortableDesignSurfaceServiceCleanup
    {
        private PortableDesignerHost? _designerHost;

        internal void Attach(PortableDesignerHost designerHost)
        {
            _designerHost = designerHost;
        }

        internal void Detach(PortableDesignerHost designerHost)
        {
            if (ReferenceEquals(_designerHost, designerHost))
                _designerHost = null;
        }

        public void RemoveDesignerHostServices()
        {
            _designerHost?.RemoveDesignerHostServices();
        }
    }
}

namespace System.Windows.Forms.Design
{
    /// <summary>
    /// Provides the narrow ToolStrip keyboard state and navigation contract needed by portable designer hosts.
    /// </summary>
    public interface IPortableToolStripKeyboardHandlingService
    {
        /// <summary>
        /// Gets whether a ToolStrip template node currently owns keyboard input.
        /// </summary>
        bool TemplateNodeActive { get; }

        /// <summary>
        /// Moves the current ToolStrip designer selection vertically.
        /// </summary>
        /// <param name="down"><see langword="true"/> to move down; <see langword="false"/> to move up.</param>
        void ProcessUpDown(bool down);
    }

    internal sealed class PortableToolStripKeyboardHandlingService : IPortableToolStripKeyboardHandlingService
    {
        private readonly IDesignerHost _designerHost;
        private readonly ISelectionService _selectionService;

        internal PortableToolStripKeyboardHandlingService(
            IDesignerHost designerHost,
            ISelectionService selectionService)
        {
            _designerHost = designerHost;
            _selectionService = selectionService;
        }

        public bool TemplateNodeActive => false;

        public void ProcessUpDown(bool down)
        {
            if (_selectionService.PrimarySelection is not ToolStripItem selectedItem)
                return;

            ToolStrip? owner = FindOwner(selectedItem);
            if (owner is null)
                return;

            int currentIndex = owner.Items.IndexOf(selectedItem);
            int nextIndex = currentIndex + (down ? 1 : -1);
            if ((uint)nextIndex >= (uint)owner.Items.Count)
                return;

            _selectionService.SetSelectedComponents(
                new object[] { owner.Items[nextIndex] },
                SelectionTypes.Replace);
        }

        private ToolStrip? FindOwner(ToolStripItem selectedItem)
        {
            foreach (IComponent? component in _designerHost.Container.Components)
            {
                if (component is ToolStrip toolStrip && toolStrip.Items.Contains(selectedItem))
                    return toolStrip;
            }

            return null;
        }
    }
}
