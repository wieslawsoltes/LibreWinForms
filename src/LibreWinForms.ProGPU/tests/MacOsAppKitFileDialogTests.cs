// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class MacOsAppKitFileDialogTests
{
    [Fact]
    public void Show_MapsOpenPanelStateAndReturnsOwnedSelection()
    {
        ProGpuDispatcher dispatcher = new();
        RecordingNative native = new(new(true, ["/tmp/one.png", "/tmp/two.jpeg"]));
        RecordingOwnerResolver owners = new((nint)0x1234);
        var service = new MacOsAppKitFileDialogService(dispatcher, native, owners);
        LibreHandle owner = new((nint)42, LibreHandleKind.Window);
        LibreFileDialogRequest request = CreateRequest(
            LibreFileDialogKind.OpenFile,
            LibreFileDialogOptions.MultiSelect
                | LibreFileDialogOptions.ShowHiddenFiles
                | LibreFileDialogOptions.DereferenceLinks
                | LibreFileDialogOptions.ReadOnlyChecked,
            owner) with
        {
            Description = "Select source images",
            Filters =
            [
                new LibreFileDialogFilter("Text", ["*.txt"]),
                new LibreFileDialogFilter("Images", ["*.png", "jpeg", "*.PNG"]),
            ],
            FilterIndex = 2,
        };

        LibreFileDialogResult result = service.Show(request);

        result.Accepted.Should().BeTrue();
        result.SelectedPaths.Should().Equal("/tmp/one.png", "/tmp/two.jpeg");
        result.FilterIndex.Should().Be(2);
        result.ReadOnlyChecked.Should().BeTrue();
        owners.Owner.Should().Be(owner);
        native.CallCount.Should().Be(1);
        native.Request.Kind.Should().Be(LibreFileDialogKind.OpenFile);
        native.Request.Title.Should().Be("Choose files");
        native.Request.Message.Should().Be("Select source images");
        native.Request.InitialDirectory.Should().Be("/tmp");
        native.Request.InitialName.Should().Be("seed.txt");
        native.Request.AllowedExtensions.Should().Equal("png", "jpeg");
        native.Request.AllowsMultipleSelection.Should().BeTrue();
        native.Request.ShowsHiddenFiles.Should().BeTrue();
        native.Request.ResolvesAliases.Should().BeTrue();
        native.Request.OwnerWindow.Should().Be((nint)0x1234);
    }

    [Fact]
    public void Show_CancellationPreservesCanonicalSnapshot()
    {
        ProGpuDispatcher dispatcher = new();
        RecordingNative native = new(new(false, ["/tmp/ignored.txt"]));
        var service = new MacOsAppKitFileDialogService(
            dispatcher,
            native,
            new RecordingOwnerResolver(0));
        LibreFileDialogRequest request = CreateRequest(
            LibreFileDialogKind.SaveFile,
            LibreFileDialogOptions.ReadOnlyChecked,
            default);

        LibreFileDialogResult result = service.Show(request);

        result.Accepted.Should().BeFalse();
        result.SelectedPaths.Should().Equal("seed.txt");
        result.FilterIndex.Should().Be(1);
        result.ReadOnlyChecked.Should().BeTrue();
    }

    [Fact]
    public void CreateNativeRequest_MapsSaveAndFolderLocationsAndCapabilities()
    {
        LibreFileDialogRequest save = CreateRequest(
            LibreFileDialogKind.SaveFile,
            LibreFileDialogOptions.ShowHiddenFiles | LibreFileDialogOptions.ShowNewFolderButton,
            default) with
        {
            SelectedPaths = ["/var/tmp/report.md"],
        };
        LibreFileDialogRequest folder = CreateRequest(
            LibreFileDialogKind.SelectFolder,
            LibreFileDialogOptions.ShowNewFolderButton | LibreFileDialogOptions.MultiSelect,
            default) with
        {
            Title = string.Empty,
            Description = "Choose destination",
            InitialDirectory = "/Users/example/Documents",
            SelectedPaths = [],
        };

        MacOsFileDialogNativeRequest saveNative =
            MacOsAppKitFileDialogService.CreateNativeRequest(save, 0);
        MacOsFileDialogNativeRequest folderNative =
            MacOsAppKitFileDialogService.CreateNativeRequest(folder, 0);

        saveNative.InitialDirectory.Should().Be("/var/tmp");
        saveNative.InitialName.Should().Be("report.md");
        saveNative.AllowedExtensions.Should().Equal("txt", "md");
        saveNative.CanCreateDirectories.Should().BeTrue();
        folderNative.Title.Should().Be("Choose destination");
        folderNative.Message.Should().BeEmpty();
        folderNative.InitialDirectory.Should().Be("/Users/example/Documents");
        folderNative.InitialName.Should().BeEmpty();
        folderNative.AllowedExtensions.Should().BeEmpty();
        folderNative.AllowsMultipleSelection.Should().BeFalse();
        folderNative.CanCreateDirectories.Should().BeTrue();
    }

    [Fact]
    public void Show_RejectsUnavailableHelpAndUnrepresentableWildcardBeforeNativePanel()
    {
        ProGpuDispatcher dispatcher = new();
        RecordingOwnerResolver owners = new(0);
        RecordingNative unavailable = new(new(false, []), isAvailable: false);
        var unavailableService = new MacOsAppKitFileDialogService(dispatcher, unavailable, owners);
        Action noPlatform = () => unavailableService.Show(
            CreateRequest(LibreFileDialogKind.OpenFile, LibreFileDialogOptions.None, default));

        RecordingNative native = new(new(false, []));
        var service = new MacOsAppKitFileDialogService(dispatcher, native, owners);
        LibreFileDialogRequest help = CreateRequest(
            LibreFileDialogKind.OpenFile,
            LibreFileDialogOptions.ShowHelp,
            default) with
        {
            HelpRequested = static () => { },
        };
        LibreFileDialogRequest wildcard = CreateRequest(
            LibreFileDialogKind.OpenFile,
            LibreFileDialogOptions.None,
            default) with
        {
            Filters = [new LibreFileDialogFilter("Prefix", ["report-??.txt"])],
        };

        noPlatform.Should().Throw<PlatformNotSupportedException>().WithMessage("*requires macOS*");
        ((Action)(() => service.Show(help))).Should()
            .Throw<PlatformNotSupportedException>()
            .WithMessage("*Help callback*");
        ((Action)(() => service.Show(wildcard))).Should()
            .Throw<PlatformNotSupportedException>()
            .WithMessage("*cannot represent*report-??.txt*");
        native.CallCount.Should().Be(0);
    }

    [Fact]
    public void Show_EnforcesDispatcherAndAcceptedSelectionShape()
    {
        ProGpuDispatcher dispatcher = new();
        RecordingNative native = new(new(true, ["/tmp/one.txt", "/tmp/two.txt"]));
        var service = new MacOsAppKitFileDialogService(
            dispatcher,
            native,
            new RecordingOwnerResolver(0));
        LibreFileDialogRequest request = CreateRequest(
            LibreFileDialogKind.OpenFile,
            LibreFileDialogOptions.None,
            default);

        Exception? wrongThread = null;
        Thread thread = new(() =>
        {
            try
            {
                service.Show(request);
            }
            catch (Exception exception)
            {
                wrongThread = exception;
            }
        });
        thread.Start();
        thread.Join();

        wrongThread.Should().BeOfType<InvalidOperationException>();
        Action multiple = () => service.Show(request);
        multiple.Should().Throw<InvalidOperationException>().WithMessage("*multiple paths*");
    }

    private static LibreFileDialogRequest CreateRequest(
        LibreFileDialogKind kind,
        LibreFileDialogOptions options,
        LibreHandle owner)
        => new(
            kind,
            "Choose files",
            string.Empty,
            "/tmp",
            ["seed.txt"],
            "txt",
            [new LibreFileDialogFilter("Text files", ["*.txt", "*.md"])],
            1,
            options,
            null,
            [],
            null,
            owner);

    private sealed class RecordingNative(
        MacOsFileDialogNativeResult result,
        bool isAvailable = true) : IMacOsFileDialogNative
    {
        public bool IsAvailable { get; } = isAvailable;

        internal int CallCount { get; private set; }

        internal MacOsFileDialogNativeRequest Request { get; private set; }

        public MacOsFileDialogNativeResult Show(in MacOsFileDialogNativeRequest request)
        {
            CallCount++;
            Request = request;
            return result;
        }
    }

    private sealed class RecordingOwnerResolver(nint result) : IMacOsFileDialogOwnerResolver
    {
        internal LibreHandle Owner { get; private set; }

        public nint Resolve(LibreHandle owner)
        {
            Owner = owner;
            return result;
        }
    }
}
