// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE

using System.ComponentModel;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

public partial class ToolStripDropDown
{
    private void SetVisibleCorePortable(bool visible)
    {
        if (_state[s_stateInSetVisibleCore] || visible == Visible)
        {
            return;
        }

        _state[s_stateInSetVisibleCore] = true;
        try
        {
            if (visible)
            {
                if (LayoutRequired)
                {
                    LayoutTransaction.DoLayout(this, this, PropertyNames.Visible);
                }

                CancelEventArgs openingEventArgs = new(cancel: DisplayedItems.Count == 0);
                OnOpening(openingEventArgs);
                if (openingEventArgs.Cancel)
                {
                    return;
                }

                try
                {
                    if (OwnerToolStrip is not null)
                    {
                        OwnerToolStrip.ActiveDropDowns.Add(this);
                        OwnerToolStrip.SnapMouseLocation();
                        if (OwnerToolStrip.Capture)
                        {
                            Capture = true;
                        }
                    }

                    base.SetVisibleCore(visible);
                }
                finally
                {
                    OnOpened(EventArgs.Empty);
                }

                return;
            }

            ToolStripDropDownCloseReason reason = _closeReason;
            ResetCloseReason();

            ToolStripDropDownClosingEventArgs closingEventArgs = new(reason)
            {
                Cancel = reason != ToolStripDropDownCloseReason.CloseCalled && !AutoClose,
            };
            OnClosing(closingEventArgs);
            if (closingEventArgs.Cancel)
            {
                return;
            }

            DismissActiveDropDowns();
            CancelAutoExpand();

            try
            {
                base.SetVisibleCore(visible);
            }
            finally
            {
                OwnerToolStrip?.ActiveDropDowns.Remove(this);
                ActiveDropDowns.Clear();
                if (Capture)
                {
                    Capture = false;
                }
            }

            OnClosed(new ToolStripDropDownClosedEventArgs(reason));

            if (!_saveSourceControl)
            {
                SourceControlInternal = null;
            }
        }
        finally
        {
            _state[s_stateInSetVisibleCore] = false;
            _saveSourceControl = false;
        }
    }
}

#endif
