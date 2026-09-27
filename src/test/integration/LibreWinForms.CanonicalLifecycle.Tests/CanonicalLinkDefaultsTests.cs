// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;
using FluentAssertions;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Fact]
    public void PortableLinkDefaultsUseCanonicalSettingsColors()
    {
        if (RunDpiCaseInNewProcess()) return;
        UseHeadlessPlatform(autoCloseWindows: false);
        Application.IsDarkModeEnabled.Should().BeFalse();
        SystemInformation.HighContrast.Should().BeFalse();
        using LinkLabel label = new();
        label.LinkColor.Should().Be(ExpectedLinkColor("Anchor Color", Color.Blue));
        label.ActiveLinkColor.Should().Be(ExpectedLinkColor("Anchor Color Hover", Color.Red));
        label.VisitedLinkColor.Should().Be(ExpectedLinkColor("Anchor Color Visited", Color.Purple));
        label.LinkBehavior.Should().Be(LinkBehavior.SystemDefault);
    }

    [Fact]
    public void PortableLinkDefaultsPaintEnabledSystemBehavior()
    {
        if (RunDpiCaseInNewProcess()) return;
        VerifyPortableLinkPainting(LinkBehavior.SystemDefault, visited: false);
    }

    [Fact]
    public void PortableLinkDefaultsPaintVisitedSystemBehavior()
    {
        if (RunDpiCaseInNewProcess()) return;
        VerifyPortableLinkPainting(LinkBehavior.SystemDefault, visited: true);
    }

    [Fact]
    public void PortableLinkDefaultsPreserveExplicitColorsAndBehavior()
    {
        if (RunDpiCaseInNewProcess()) return;
        VerifyPortableLinkPainting(LinkBehavior.NeverUnderline, visited: false, explicitColors: true);
    }

    private static void VerifyPortableLinkPainting(LinkBehavior behavior, bool visited, bool explicitColors = false)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Bitmap target = new(180, 60);
        using Graphics graphics = Graphics.FromImage(target);
        using PaintingLinkLabel label = new()
        {
            Text = "link", Size = new Size(160, 30), UseCompatibleTextRendering = false,
            LinkBehavior = behavior, LinkVisited = visited,
        };
        if (explicitColors)
        {
            label.LinkColor = Color.Green;
            label.ActiveLinkColor = Color.Orange;
            label.VisitedLinkColor = Color.Brown;
        }

        label.PaintTo(graphics);
        platform.TextDrawStrings.Should().Contain("link");
        Color expected = explicitColors ? Color.Green : visited
            ? ExpectedLinkColor("Anchor Color Visited", Color.Purple)
            : ExpectedLinkColor("Anchor Color", Color.Blue);
        platform.LastDrawnTextColor.Should().Be(expected);
        platform.LastDrawnTextFont.Should().NotBeNull();
        platform.LastDrawnTextFont!.Value.Style.HasFlag(FontStyle.Underline)
            .Should().Be(behavior == LinkBehavior.SystemDefault && ExpectedSystemUnderline());
        label.LinkBehavior.Should().Be(behavior);
        label.IsHandleCreated.Should().BeFalse();
        if (explicitColors)
        {
            label.ActiveLinkColor.Should().Be(Color.Orange);
            label.VisitedLinkColor.Should().Be(Color.Brown);
        }
    }

    private static Color ExpectedLinkColor(string name, Color missing)
    {
        if (!OperatingSystem.IsWindows()) return missing;
        using var settings = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Internet Explorer\Settings");
        string? value = (string?)settings?.GetValue(name);
        if (value is null) return missing;
        string[] components = value.Split(',');
        int[] rgb = new int[3];
        for (int i = 0; i < Math.Min(3, components.Length); i++) int.TryParse(components[i], out rgb[i]);
        return Color.FromArgb(rgb[0], rgb[1], rgb[2]);
    }

    private static bool ExpectedSystemUnderline()
    {
        if (!OperatingSystem.IsWindows()) return true;
        using var settings = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Internet Explorer\Main");
        string? value = (string?)settings?.GetValue("Anchor Underline");
        return !string.Equals(value, "no", StringComparison.InvariantCultureIgnoreCase)
            && !string.Equals(value, "hover", StringComparison.InvariantCultureIgnoreCase);
    }
}
