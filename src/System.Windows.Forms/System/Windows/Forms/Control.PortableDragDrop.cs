// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Drawing;
using LibreWinForms.Platform;

namespace System.Windows.Forms;

public unsafe partial class Control
{
    private const DragDropEffects PortableValidDragDropEffects =
        DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link | DragDropEffects.Scroll;

    private DragDropEffects DoPortableDragDrop(
        DataObject dataObject,
        DragDropEffects allowedEffects,
        Bitmap? dragImage,
        Point cursorOffset,
        bool useDefaultDragImage)
    {
        PortableDragDropSession session = new(
            this,
            dataObject,
            allowedEffects,
            dragImage,
            cursorOffset,
            useDefaultDragImage);
        LibreDragDropRequest request = new(
            _window.PortableHandle,
            new PortableDataTransfer(dataObject),
            ToLibreDragDropEffects(allowedEffects & PortableValidDragDropEffects),
            new LibrePoint(cursorOffset.X, cursorOffset.Y),
            useDefaultDragImage);
        LibreDragDropEffects result = LibrePlatform.Current.DragDrop.DoDragDrop(request, session);
        return ToDragDropEffects(result) & allowedEffects & PortableValidDragDropEffects;
    }

    private static LibreDragDropEffects ToLibreDragDropEffects(DragDropEffects effects)
        => (LibreDragDropEffects)(int)effects;

    private static DragDropEffects ToDragDropEffects(LibreDragDropEffects effects)
        => (DragDropEffects)(int)effects;

    private sealed class PortableDataTransfer(DataObject dataObject) : ILibreDataTransfer
    {
        public IReadOnlyList<string> Formats => dataObject.GetFormats(autoConvert: false);

        public bool Contains(string format, bool autoConvert)
            => dataObject.GetDataPresent(format, autoConvert);

#pragma warning disable WFDEV005 // The platform data-transfer boundary preserves arbitrary IDataObject payloads.
        public object? GetData(string format, bool autoConvert)
            => dataObject.GetData(format, autoConvert);
#pragma warning restore WFDEV005
    }

    private sealed class PortableDragDropSession(
        Control source,
        DataObject dataObject,
        DragDropEffects allowedEffects,
        Bitmap? dragImage,
        Point cursorOffset,
        bool useDefaultDragImage) : ILibreDragDropSession
    {
        public LibreDragTransition Enter(
            LibreHandle hitTarget,
            int keyState,
            LibrePoint screenPosition,
            LibreDragDropEffects effect)
        {
            Control? target = ResolveAllowedTarget(hitTarget);
            if (target is null)
            {
                return default;
            }

            DragEventArgs args = CreateArgs(keyState, screenPosition, effect);
            target.OnDragEnter(args);
            return new LibreDragTransition(
                target._window.PortableHandle,
                ToLibreDragDropEffects(Normalize(args.Effect)));
        }

        public LibreDragDropEffects Over(
            LibreHandle targetHandle,
            int keyState,
            LibrePoint screenPosition,
            LibreDragDropEffects effect)
        {
            Control? target = ResolveActiveTarget(targetHandle);
            if (target is null)
            {
                return LibreDragDropEffects.None;
            }

            DragEventArgs args = CreateArgs(keyState, screenPosition, effect);
            target.OnDragOver(args);
            return ToLibreDragDropEffects(Normalize(args.Effect));
        }

        public void Leave(LibreHandle target)
            => ResolveTarget(target)?.OnDragLeave(EventArgs.Empty);

        public LibreDragDropEffects Drop(
            LibreHandle targetHandle,
            int keyState,
            LibrePoint screenPosition,
            LibreDragDropEffects effect)
        {
            Control? target = ResolveActiveTarget(targetHandle);
            if (target is null)
            {
                return LibreDragDropEffects.None;
            }

            DragEventArgs args = CreateArgs(keyState, screenPosition, effect);
            target.OnDragDrop(args);
            return ToLibreDragDropEffects(Normalize(args.Effect));
        }

        public LibreDragAction QueryContinue(int keyState, bool escapePressed)
        {
            QueryContinueDragEventArgs args = new(keyState, escapePressed, DragAction.Continue);
            source.OnQueryContinueDrag(args);
            return (LibreDragAction)(int)args.Action;
        }

        public bool GiveFeedback(LibreDragDropEffects effect)
        {
            GiveFeedbackEventArgs args = new(
                ToDragDropEffects(effect),
                useDefaultCursors: true,
                dragImage,
                cursorOffset,
                useDefaultDragImage);
            source.OnGiveFeedback(args);
            return args.UseDefaultCursors;
        }

        private DragEventArgs CreateArgs(
            int keyState,
            LibrePoint screenPosition,
            LibreDragDropEffects effect)
            => new(
                dataObject,
                keyState,
                screenPosition.X,
                screenPosition.Y,
                allowedEffects,
                Normalize(ToDragDropEffects(effect)));

        private DragDropEffects Normalize(DragDropEffects effect)
            => effect & allowedEffects & PortableValidDragDropEffects;

        private static Control? ResolveAllowedTarget(LibreHandle handle)
        {
            Control? target = ResolveTarget(handle);
            while (target is not null && !target.AllowDrop)
            {
                target = target.ParentInternal;
            }

            return target;
        }

        private static Control? ResolveActiveTarget(LibreHandle handle)
        {
            Control? target = ResolveTarget(handle);
            return target?.AllowDrop == true ? target : null;
        }

        private static Control? ResolveTarget(LibreHandle handle)
            => handle.Kind == LibreHandleKind.LogicalControl ? FromHandle(handle.Value) : null;
    }
}
#endif
