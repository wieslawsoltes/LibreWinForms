// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableDragSourceWindowIsTheActualContainingWindowNotItsFocusOwner(bool popup)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using Panel source = new() { Size = new(80, 40) };
        using ContextMenuStrip menu = new() { AutoClose = false };
        if (popup) menu.Items.Add(new ToolStripControlHost(source));
        else owner.Controls.Add(source);
        owner.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        if (popup) menu.Show(owner, Point.Empty);
        Control window = popup ? menu : owner;
        LibreHandle expected = new(window.Handle, LibreHandleKind.Window);
        int calls = 0;
        platform.DragDropHandler = (request, _) =>
        {
            calls++;
            Assert.Equal(new(source.Handle, LibreHandleKind.LogicalControl), request.Source);
            Assert.Equal(expected, request.SourceWindow);
            if (popup) Assert.NotEqual(new(owner.Handle, LibreHandleKind.Window), request.SourceWindow);
            return LibreDragDropEffects.None;
        };

        Assert.Equal(DragDropEffects.None, source.DoDragDrop("source-owned data", DragDropEffects.Copy));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void PortableDragWithoutNativeWindowDoesNotCreateOrInferOne()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Control source = new();
        int calls = 0;
        platform.DragDropHandler = (request, _) =>
        {
            calls++;
            Assert.Equal(default, request.SourceWindow);
            Assert.False(source.IsHandleCreated);
            // The original positional constructor/deconstruction stays intact.
            var (control, data, effects, offset, image) = request;
            Assert.Equal(request.Source, control);
            Assert.Same(request.Data, data);
            Assert.Equal(LibreDragDropEffects.Copy, effects);
            Assert.Equal(default, offset);
            Assert.False(image);
            return LibreDragDropEffects.None;
        };
        Assert.Equal(DragDropEffects.None, source.DoDragDrop("unhosted data", DragDropEffects.Copy));
        Assert.False(source.IsHandleCreated);
        Assert.Equal(1, calls);
    }
}
