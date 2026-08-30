// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Numerics;
using LibreWinForms.Platform;
using ProGPU.Scene;

namespace LibreWinForms.ProGPU;

internal sealed class ProGpuRetainedPaintFrame : ILibreRetainedPaintFrame
{
    private readonly ContainerVisual _root;
    private readonly DrawingVisual _fallbackVisual;
    private readonly DrawingVisual _transientVisual;
    private readonly DrawingVisual _reversibleVisual;
    private readonly Dictionary<LibreHandle, DrawingVisual> _layers;
    private readonly List<DrawingVisual> _orderedLayers = [];
    private readonly HashSet<LibreHandle> _visited = [];
    private bool _completed;

    internal ProGpuRetainedPaintFrame(
        ContainerVisual root,
        DrawingVisual fallbackVisual,
        DrawingVisual transientVisual,
        DrawingVisual reversibleVisual,
        Dictionary<LibreHandle, DrawingVisual> layers,
        LibreRectangle surfaceBounds,
        LibreRectangle dirtyRectangle)
    {
        _root = root;
        _fallbackVisual = fallbackVisual;
        _transientVisual = transientVisual;
        _reversibleVisual = reversibleVisual;
        _layers = layers;
        SurfaceBounds = surfaceBounds;
        DirtyRectangle = dirtyRectangle;

        _fallbackVisual.Context.Clear();
        Graphics = Graphics.FromProGpuDrawingContext(
            _fallbackVisual.Context,
            new RectangleF(0f, 0f, surfaceBounds.Width, surfaceBounds.Height));
    }

    public Graphics Graphics { get; }

    public LibreRectangle SurfaceBounds { get; }

    public LibreRectangle DirtyRectangle { get; }

    public ILibrePaintLayer OpenLayer(
        LibreHandle target,
        LibreRectangle bounds,
        LibreRectangle clipRectangle)
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        if (target.IsNull)
        {
            throw new ArgumentException("A retained paint layer requires a live control handle.", nameof(target));
        }

        if (!_visited.Add(target))
        {
            throw new InvalidOperationException("A control can open only one retained layer per paint frame.");
        }

        bool isNew = !_layers.TryGetValue(target, out DrawingVisual? visual);
        if (isNew)
        {
            visual = new DrawingVisual();
            _layers.Add(target, visual);
        }

        _orderedLayers.Add(visual!);
        visual!.Offset = new Vector2(bounds.X, bounds.Y);
        visual.Size = new Vector2(bounds.Width, bounds.Height);
        visual.ClipBounds = new Rect(
            clipRectangle.X - bounds.X,
            clipRectangle.Y - bounds.Y,
            clipRectangle.Width,
            clipRectangle.Height);

        if (!isNew && !Intersects(bounds, DirtyRectangle))
        {
            return EmptyPaintLayer.Instance;
        }

        visual.Context.Clear();
        Graphics graphics = Graphics.FromProGpuDrawingContext(
            visual.Context,
            new RectangleF(0f, 0f, bounds.Width, bounds.Height));
        graphics.SetClip(new RectangleF(
            clipRectangle.X - bounds.X,
            clipRectangle.Y - bounds.Y,
            clipRectangle.Width,
            clipRectangle.Height));
        return new RecordingPaintLayer(graphics, visual);
    }

    internal void Complete()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        Graphics.Dispose();

        foreach ((LibreHandle target, DrawingVisual visual) in _layers.ToArray())
        {
            if (_visited.Contains(target))
            {
                continue;
            }

            visual.Context.Clear();
            _layers.Remove(target);
        }

        if (!HasExpectedVisualOrder())
        {
            _root.ClearChildren();
            _root.AddChild(_fallbackVisual);
            foreach (DrawingVisual visual in _orderedLayers)
            {
                _root.AddChild(visual);
            }

            _root.AddTopmostChild(_transientVisual);
            _root.AddTopmostChild(_reversibleVisual);
        }
    }

    private bool HasExpectedVisualOrder()
    {
        IReadOnlyList<Visual> children = _root.Children;
        if (children.Count != _orderedLayers.Count + 3
            || !ReferenceEquals(children[0], _fallbackVisual)
            || !ReferenceEquals(children[^2], _transientVisual)
            || !ReferenceEquals(children[^1], _reversibleVisual))
        {
            return false;
        }

        for (int index = 0; index < _orderedLayers.Count; index++)
        {
            if (!ReferenceEquals(children[index + 1], _orderedLayers[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Intersects(LibreRectangle left, LibreRectangle right)
        => left.Width > 0
            && left.Height > 0
            && right.Width > 0
            && right.Height > 0
            && left.X < right.Right
            && right.X < left.Right
            && left.Y < right.Bottom
            && right.Y < left.Bottom;

    private sealed class RecordingPaintLayer(Graphics graphics, DrawingVisual visual) : ILibrePaintLayer
    {
        private Graphics? _graphics = graphics;

        public Graphics? Graphics => _graphics;

        public void Dispose()
        {
            Graphics? recorder = Interlocked.Exchange(ref _graphics, null);
            if (recorder is null)
            {
                return;
            }

            recorder.Dispose();
            visual.Invalidate();
        }
    }

    private sealed class EmptyPaintLayer : ILibrePaintLayer
    {
        internal static EmptyPaintLayer Instance { get; } = new();

        public Graphics? Graphics => null;

        public void Dispose()
        {
        }
    }
}
