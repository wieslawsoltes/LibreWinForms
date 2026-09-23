// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Numerics;
using LibreWinForms.Platform;
using ProGPU.Scene;

namespace LibreWinForms.ProGPU;

internal sealed class ProGpuAdornerStore(ContainerVisual root)
{
    private readonly ContainerVisual _root = root ?? throw new ArgumentNullException(nameof(root));
    private readonly Dictionary<LibreAdornerId, DrawingVisual> _layers = [];

    internal int Count => _layers.Count;

    internal DrawingVisual Commit(
        LibreAdornerId adorner,
        LibreRectangle bounds,
        LibreRectangle clipRectangle,
        DrawingContext recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        if (!_layers.TryGetValue(adorner, out DrawingVisual? visual))
        {
            visual = new DrawingVisual();
            _layers.Add(adorner, visual);
            _root.AddChild(visual);
        }

        visual.Offset = new Vector2(bounds.X, bounds.Y);
        visual.Size = new Vector2(bounds.Width, bounds.Height);
        visual.ClipBounds = new Rect(
            clipRectangle.X - bounds.X,
            clipRectangle.Y - bounds.Y,
            clipRectangle.Width,
            clipRectangle.Height);
        visual.Context.Clear();
        visual.Context.Append(recording);
        visual.Invalidate();
        _root.Invalidate();
        return visual;
    }

    internal bool Remove(LibreAdornerId adorner)
    {
        if (!_layers.Remove(adorner, out DrawingVisual? visual))
        {
            return false;
        }

        visual.Context.Clear();
        _root.RemoveChild(visual);
        _root.Invalidate();
        return true;
    }

    internal void Clear()
    {
        foreach (DrawingVisual visual in _layers.Values)
        {
            visual.Context.Clear();
        }

        _layers.Clear();
        _root.ClearChildren();
        _root.Invalidate();
    }
}
