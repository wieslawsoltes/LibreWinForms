// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Xml.Linq;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

[Collection(ProGpuDesktopCaptureCollection.Name)]
public class ProGpuStartupOptionsTests
{
    private const string NativeModalArgument = "--libre-native-modal-sessions";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OrdinaryArgumentsPreserveDefault(bool isMacOS)
    {
        Assert.False(ProGpuStartupOptions.ParseArguments(["--application-option", "value"], isMacOS).EnableNativeModalSessions);
        Assert.False(default(ProGpuStartupOptions).EnableNativeModalSessions);
    }

    [Theory]
    [InlineData("--libre-native-modal-sessions=true")]
    [InlineData("--libre-native-modal-sessions=false")]
    [InlineData("--libre-native-modal-sessions=automatic")]
    [InlineData("--libre-native-modal-session")]
    [InlineData("--libre-native-modal-other")]
    public void UnknownNativeModalArgumentsReject(string argument)
        => Assert.Throws<ArgumentException>(() => ProGpuStartupOptions.ParseArguments([argument], isMacOS: true));

    [Fact]
    public void DuplicateSelectionAndUnsupportedPlatformReject()
    {
        Assert.Throws<ArgumentException>(() => ProGpuStartupOptions.ParseArguments(
            [NativeModalArgument, "other", NativeModalArgument], true));
        Assert.Throws<PlatformNotSupportedException>(() => ProGpuStartupOptions.ParseArguments([NativeModalArgument], false));
    }

    [Fact]
    public void StartupOwnsSnapshotWithoutConsumingArgumentsOrLateApplicationData()
    {
        string[] arguments = ["application-data", NativeModalArgument, "--", NativeModalArgument];
        string[] original = (string[])arguments.Clone();
        var startup = ProGpuStartupOptions.ParseArguments(arguments, true);
        Assert.True(startup.EnableNativeModalSessions);
        Assert.Equal(original, arguments);
        arguments[1] = "changed-after-startup";
        Assert.True(startup.EnableNativeModalSessions);
        Assert.False(ProGpuStartupOptions.ParseArguments(["--", NativeModalArgument], false).EnableNativeModalSessions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ActualServiceConstructionRetainsSelectedSourcePolicy(bool enabled)
    {
        var startup = ProGpuStartupOptions.ParseArguments(enabled ? [NativeModalArgument] : [], true);
        using LibrePlatformServices services = ProGpuPlatform.CreateServices(startup.EnableNativeModalSessions);
        Assert.Equal(enabled, Assert.IsType<SilkWindowService>(services.Windows).EnableNativeModalSessions);
        Assert.IsType<ProGpuPopupSurfaceService>(services.Popups);
    }

    [Fact]
    public void ActualSdkGeneratedInitializerSelectsBeforeRegisteringAndRetainsDefaultBranch()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "SourceContracts", "LibreWinForms.Sdk.targets"));
        string[] lines = document.Descendants("_LibreWinFormsApplicationBootstrapLine")
            .Select(element => (string)element.Attribute("Include")!)
            .ToArray();
        int parse = Array.FindIndex(lines, line => line.Contains("ProGpuStartupOptions.ParseArguments", StringComparison.Ordinal));
        int selected = Array.FindIndex(lines, line => line.Contains("ProGpuPlatform.Register(enableNativeModalSessions: true)", StringComparison.Ordinal));
        int ordinary = Array.FindIndex(lines, line => line.Contains("ProGpuPlatform.Register()", StringComparison.Ordinal));
        Assert.True(parse >= 0 && selected > parse && ordinary > selected);
        Assert.Contains("Environment.GetCommandLineArgs(), 1)", lines[parse]);
        Assert.Equal("if (startup.EnableNativeModalSessions)", lines[selected - 1].Trim());
        Assert.Equal("else", lines[ordinary - 1].Trim());
        Assert.Single(lines.Where(line => line.Contains("ProGpuStartupOptions.ParseArguments", StringComparison.Ordinal)));
        Assert.Contains(lines, line => line.Contains("ModuleInitializer", StringComparison.Ordinal));
    }
}
