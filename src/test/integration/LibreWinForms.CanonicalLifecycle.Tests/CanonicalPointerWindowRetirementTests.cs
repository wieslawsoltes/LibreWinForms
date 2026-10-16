// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;
using FluentAssertions;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PortablePointerWindowRetirement_ReleasesItsHeldButton(int retirement)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using RecreatingForm owner = new() { ShowIcon = false, ClientSize = new(160, 100) };
        using Panel target = new() { Bounds = new(10, 10, 80, 60) };
        owner.Controls.Add(target);
        owner.Show();
        SendReentrantPointer(platform, owner, target, LibreInputEventKind.PointerDown);
        Control.MouseButtons.Should().Be(MouseButtons.Left);
        target.Capture.Should().BeTrue();
        switch (retirement)
        {
            case 0: owner.Dispose(); break;
            case 1: owner.Close(); break;
            case 2: owner.Hide(); break;
            case 3: owner.RecreatePortableHandle(); break;
        }

        Control.MouseButtons.Should().Be(MouseButtons.None);
        if (!target.IsDisposed) target.Capture.Should().BeFalse();
    }
}
