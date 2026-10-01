// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
namespace System.Windows.Forms;

public partial class ToolStrip
{
    internal virtual void InvalidatePortableItemScrollLayout() { }

    // A synchronous source layout borrow, not a callback into an unrelated
    // renderer or a retained per-item wrapper. Ordinary ScrollInternal is intact.
    internal abstract class PortableItemScrollScope
    {
        internal abstract int Count { get; }
        internal abstract bool IsCurrent(int moved, int delta);
        internal abstract ToolStripItem Item(int index);
        internal abstract Point Location(int index, int delta);
        internal abstract void Commit(int delta);
    }

    internal bool ScrollInternal(int delta, PortableItemScrollScope scope)
    {
        if (!scope.IsCurrent(0, delta))
            return false;
        SuspendLayout();
        Exception? failure = null;
        try
        {
            for (int i = 0; i < scope.Count; i++)
            {
                SetItemLocation(scope.Item(i), scope.Location(i, delta));
                // SetBounds is virtual and can close, reparent, relayout or
                // deliver replacement input. Never write the next old item.
                if (!scope.IsCurrent(i + 1, delta))
                    return false;
            }

            scope.Commit(delta);
        }
        catch (Exception error)
        {
            failure = error;
            throw;
        }
        finally
        {
            try { ResumeLayout(false); }
            catch (Exception cleanup) when (failure is not null)
            {
                try { failure.Data["PortableScrollResumeLayout"] = cleanup; }
                catch { /* Diagnostic storage cannot replace the source error. */ }
            }
        }

        if (!scope.IsCurrent(0, 0))
            return false;
        Invalidate();
        return scope.IsCurrent(0, 0);
    }
}
#endif
