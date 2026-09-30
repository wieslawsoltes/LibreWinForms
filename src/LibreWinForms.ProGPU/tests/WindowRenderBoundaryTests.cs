// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;
using ProGPU.Scene;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class WindowRenderBoundaryTests
{
    [Fact]
    public void ActualHostKeepsPaintAndAcquiredTextureScopesInsideItsProductionBoundary()
    {
        // A source wiring guard, not a GPU/native-window fixture. Behavioral
        // cases use the production boundary, queue, dispatcher and paint frame.
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "SourceContracts", "SilkWindowService.cs"));
        string onRender = Section(source, "private void OnRender(", "private void RenderFrame(");
        Assert.Contains("_renderBoundary.Run(_renderFrame)", onRender);
        Assert.Contains("_renderFrame = RenderFrame", source);
        string render = Section(source, "private void RenderFrame(", "private bool OwnsRenderResources(");
        Assert.Contains("using (WgpuContext.PushCurrent(context))", render);
        Assert.Contains("if (_disposed ||", render);
        Assert.Contains("_events.PaintRequested(frame)", render);
        Assert.Contains("frame.Complete()", render);
        Assert.Contains("if (!OwnsRenderResources(context, compositor))", render);
        Assert.Contains("PresentFrame(context, compositor, surfaceBounds)", render);
        Assert.True(render.IndexOf("_events.PaintRequested(frame)", StringComparison.Ordinal)
            < render.IndexOf("frame.Complete()", StringComparison.Ordinal));
        Assert.True(render.LastIndexOf("if (!OwnsRenderResources(context, compositor))", StringComparison.Ordinal)
            < render.IndexOf("PresentFrame(context, compositor, surfaceBounds)", StringComparison.Ordinal));
        Assert.Contains("=> !_disposed && ReferenceEquals(_wgpuContext, context) && ReferenceEquals(_compositor, compositor)", source);
        string presentation = Section(source, "private unsafe void PresentFrame(", "private void OnClosing(");
        Assert.Contains("finally", presentation);
        Assert.Contains("context.Api.TextureViewRelease(targetView)", presentation);
        Assert.Contains("context.Api.TextureRelease(surfaceTexture.Texture)", presentation);
        string retirement = Section(source, "private void ReleaseNativeWindow(", "private void ReleaseRenderingResources(");
        Assert.Contains("RetireNativeWindow(_window, ReleaseRenderingResources,", retirement);
        Assert.Contains("CanReleaseRenderingResources)", retirement);
        Assert.DoesNotContain("_paintRoot.ClearChildren", retirement);
        Assert.DoesNotContain("visual.Context.Clear", retirement);
        Assert.Contains("=> !_renderBoundary.IsActive && !_initializingRenderer", source);
        string cleanup = Section(source, "private void ReleaseRenderingResources(", "private void ReleaseUnpublishedRenderer(");
        Assert.Contains("_compositor?.Dispose()", cleanup);
        Assert.Contains("_wgpuContext?.Dispose()", cleanup);
        Assert.True(cleanup.IndexOf("_compositor?.Dispose()", StringComparison.Ordinal)
            < cleanup.IndexOf("_wgpuContext?.Dispose()", StringComparison.Ordinal));
        string initialization = Section(source, "private void EnsureRenderer(", "private void ApplyInputTransparency(");
        Assert.Contains("_unpublishedContext = context", initialization);
        Assert.Contains("_unpublishedCompositor = compositor", initialization);
        Assert.Contains("ReleaseUnpublishedRenderer()", initialization);
        Assert.Contains("DrainRetiredNativeWindow()", initialization);
    }

    private static string Section(string source, string start, string end)
    {
        int first = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(first >= 0, $"Missing source contract: {start}");
        int last = source.IndexOf(end, first + start.Length, StringComparison.Ordinal);
        Assert.True(last > first, $"Missing source contract: {end}");
        return source[first..last];
    }

    [Fact]
    public void ReentrantPaintCannotConsumeAnotherFrameAndDrainsOnlyOnce()
    {
        int renderCalls = 0, drainCalls = 0;
        WindowRenderBoundary? boundary = null;
        boundary = new(() => { Assert.False(boundary!.IsActive); drainCalls++; });
        boundary.Run(() =>
        {
            Assert.True(boundary.IsActive);
            renderCalls++;
            boundary.Run(() => renderCalls++);
            Assert.True(boundary.IsActive);
            Assert.Equal(0, drainCalls);
        });
        Assert.False(boundary.IsActive);
        Assert.Equal(1, renderCalls);
        Assert.Equal(1, drainCalls);
        boundary.Run(() => renderCalls++);
        Assert.Equal(2, renderCalls);
        Assert.Equal(2, drainCalls);
    }

    [Fact]
    public void RetirementWaitsForAllFrameResourcesBeforeCallingNativeProvider()
    {
        List<string> order = [];
        WindowRenderBoundary? boundary = null;
        NativeWindowRetirementQueue queue = new(_ => { order.Add("native"); return true; });
        boundary = new(queue.Drain);
        boundary.Run(() =>
        {
            try
            {
                queue.Retire(NativeWindowRetirementQueueTests.CreateWindow(),
                    () => order.Add("renderer"), () => !boundary.IsActive);
                queue.Drain();
                Assert.Empty(order);
            }
            finally
            {
                // The real host keeps PushCurrent and acquired texture/view
                // finally blocks inside this production boundary.
                order.Add("view");
                order.Add("texture");
                order.Add("current-context");
            }
        });
        Assert.Equal(new[] { "view", "texture", "current-context", "renderer", "native" }, order);
        Assert.False(queue.HasPending);
    }

    [Fact]
    public void SourcePaintFrameRemainsUsableUntilItsActualCompletion()
    {
        ContainerVisual root = new(), adorners = new();
        DrawingVisual fallback = new(), transient = new(), reversible = new();
        Dictionary<LibreHandle, DrawingVisual> layers = [];
        root.AddChild(fallback);
        root.AddTopmostChild(transient);
        root.AddTopmostChild(adorners);
        root.AddTopmostChild(reversible);
        WindowRenderBoundary? boundary = null;
        bool cleanupCalled = false, frameCompleted = false;
        NativeWindowRetirementQueue queue = new(_ => { Assert.True(frameCompleted); return true; });
        boundary = new(queue.Drain);
        boundary.Run(() =>
        {
            ProGpuRetainedPaintFrame frame = new(root, fallback, transient, adorners, reversible,
                layers, new(0, 0, 100, 100), new(0, 0, 100, 100));
            try
            {
                // An application's source PaintRequested callback closes its
                // owner, pumps nested work, then finishes its original frame.
                queue.Retire(NativeWindowRetirementQueueTests.CreateWindow(), () =>
                {
                    Assert.True(frameCompleted);
                    Assert.Equal(5, root.Children.Count);
                    Assert.Single(layers);
                    cleanupCalled = true;
                    root.ClearChildren();
                    layers.Clear();
                }, () => !boundary.IsActive);
                queue.Drain();
                Assert.False(cleanupCalled);
                using var layer = frame.OpenLayer(new((nint)17, LibreHandleKind.LogicalControl),
                    new(5, 5, 20, 20), new(5, 5, 20, 20));
            }
            finally
            {
                frame.Complete();
                frameCompleted = true;
            }

            Assert.False(cleanupCalled);
        });
        Assert.True(cleanupCalled);
        Assert.Empty(root.Children);
        Assert.Empty(layers);
        Assert.False(queue.HasPending);
    }

    [Fact]
    public void NestedDispatcherPumpCannotReleaseAnActiveSourceFrame()
    {
        int cleanup = 0, native = 0;
        WindowRenderBoundary? boundary = null;
        NativeWindowRetirementQueue queue = new(_ => { native++; return true; });
        using ProGpuDispatcher dispatcher = new(queue);
        boundary = new(dispatcher.DrainNativeWindowRetirements);
        boundary.Run(() =>
        {
            dispatcher.RetireNativeWindow(NativeWindowRetirementQueueTests.CreateWindow(),
                () => cleanup++, () => !boundary.IsActive);
            dispatcher.Post(() => { });
            dispatcher.PumpOnce();
            Assert.Equal(0, cleanup);
            Assert.Equal(0, native);
            Assert.True(queue.HasPending);
        });
        Assert.Equal(1, cleanup);
        Assert.Equal(1, native);
        Assert.False(queue.HasPending);
    }

    [Fact]
    public void PaintFailureStaysPrimaryWhenRetirementFailsAndOwnerCanRetry()
    {
        InvalidOperationException paintFailure = new("paint"), cleanupFailure = new("cleanup");
        bool ready = false;
        int native = 0;
        WindowRenderBoundary? boundary = null;
        NativeWindowRetirementQueue queue = new(_ => { native++; return true; });
        boundary = new(queue.Drain);
        Assert.Same(paintFailure, Assert.Throws<InvalidOperationException>(() => boundary.Run(() =>
        {
            queue.Retire(NativeWindowRetirementQueueTests.CreateWindow(),
                () => { if (!ready) throw cleanupFailure; }, () => !boundary.IsActive);
            throw paintFailure;
        })));
        Assert.False(boundary.IsActive);
        Assert.True(queue.HasPending);
        Assert.Equal(0, native);
        Assert.Empty(paintFailure.Data);
        ready = true;
        queue.Drain();
        Assert.False(queue.HasPending);
        Assert.Equal(1, native);
    }

    [Fact]
    public void SuccessfulFrameReportsRetirementFailureRatherThanClaimingSuccess()
    {
        InvalidOperationException expected = new("retirement");
        WindowRenderBoundary boundary = new(() => throw expected);
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => boundary.Run(() => { })));
        Assert.False(boundary.IsActive);
    }

    [Fact]
    public void AcquiredScopeFailureStillUnwindsBeforeRenderingCleanup()
    {
        bool acquired = false;
        InvalidOperationException expected = new("acquired frame");
        WindowRenderBoundary? boundary = null;
        int retired = 0;
        NativeWindowRetirementQueue queue = new(_ => { Assert.False(acquired); retired++; return true; });
        boundary = new(queue.Drain);
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => boundary.Run(() =>
        {
            acquired = true;
            try
            {
                queue.Retire(NativeWindowRetirementQueueTests.CreateWindow(),
                    () => Assert.False(acquired), () => !boundary.IsActive);
                throw expected;
            }
            finally { acquired = false; }
        })));
        Assert.Equal(1, retired);
        Assert.False(queue.HasPending);
    }

    [Fact]
    public void ActiveFramePreventsDispatcherShutdownWithoutDiscardingItsOwner()
    {
        WindowRenderBoundary? boundary = null;
        NativeWindowRetirementQueue queue = new(_ => true);
        using ProGpuDispatcher dispatcher = new(queue);
        boundary = new(dispatcher.DrainNativeWindowRetirements);
        boundary.Run(() =>
        {
            dispatcher.RetireNativeWindow(NativeWindowRetirementQueueTests.CreateWindow(),
                canReleaseRenderingResources: () => !boundary.IsActive);
            Assert.Throws<InvalidOperationException>(dispatcher.Dispose);
            Assert.True(queue.HasPending);
        });
        Assert.False(queue.HasPending);
        dispatcher.Dispose();
    }

    [Fact]
    public void ForeignThreadCannotEnterTheSourceRenderBoundary()
    {
        int render = 0, drain = 0;
        WindowRenderBoundary boundary = new(() => drain++);
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try { boundary.Run(() => render++); }
            catch (Exception error) { failure = error; }
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(0, render);
        Assert.Equal(0, drain);
        Assert.False(boundary.IsActive);
    }
}
