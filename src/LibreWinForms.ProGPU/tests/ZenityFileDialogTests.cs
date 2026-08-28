// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public class ZenityFileDialogTests
{
    [Fact]
    public void Show_UsesTokenizedLinuxDesktopArgumentsAndRepeatsAfterHelp()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        ProGpuDispatcher dispatcher = new();
        RecordingRunner runner = new(
            new LibreDesktopDialogProcessResult(0, "Help\n", string.Empty),
            new LibreDesktopDialogProcessResult(0, "/tmp/first.txt\u001F/tmp/second.txt\n", string.Empty));
        var service = new ZenityLibreFileDialogService(dispatcher, runner);
        int help = 0;
        LibreFileDialogRequest request = CreateRequest(
            LibreFileDialogKind.OpenFile,
            LibreFileDialogOptions.MultiSelect
                | LibreFileDialogOptions.ShowHiddenFiles
                | LibreFileDialogOptions.ShowHelp,
            () => help++);

        LibreFileDialogResult result = service.Show(request);

        result.Accepted.Should().BeTrue();
        result.SelectedPaths.Should().Equal("/tmp/first.txt", "/tmp/second.txt");
        result.FilterIndex.Should().Be(1);
        help.Should().Be(1);
        runner.CallCount.Should().Be(2);
        runner.Executable.Should().Be("zenity");
        runner.Arguments.Should().Contain("--file-selection");
        runner.Arguments.Should().Contain("--modal");
        runner.Arguments.Should().Contain("--multiple");
        runner.Arguments.Should().Contain("--show-hidden");
        runner.Arguments.Should().Contain("--separator=\u001F");
        runner.Arguments.Should().Contain("--extra-button=Help");
        runner.Arguments.Should().Contain("--title=Choose files");
        runner.Arguments.Should().Contain("--filename=/tmp/seed.txt");
        runner.Arguments.Should().Contain("--file-filter=Text files | *.txt *.md");
    }

    [Fact]
    public void Show_SaveAndFolderModesUseNativeChooserFlagsAndCancellationSnapshot()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        ProGpuDispatcher dispatcher = new();
        RecordingRunner saveRunner = new(new LibreDesktopDialogProcessResult(1, string.Empty, string.Empty));
        var saveService = new ZenityLibreFileDialogService(dispatcher, saveRunner, "test-zenity");
        LibreFileDialogRequest saveRequest = CreateRequest(
            LibreFileDialogKind.SaveFile,
            LibreFileDialogOptions.OverwritePrompt,
            null);

        LibreFileDialogResult cancelled = saveService.Show(saveRequest);

        cancelled.Accepted.Should().BeFalse();
        cancelled.SelectedPaths.Should().Equal("seed.txt");
        saveRunner.Executable.Should().Be("test-zenity");
        saveRunner.Arguments.Should().Contain("--save");
        saveRunner.Arguments.Should().Contain("--confirm-overwrite");

        RecordingRunner folderRunner = new(new LibreDesktopDialogProcessResult(0, "/tmp/folder\n", string.Empty));
        var folderService = new ZenityLibreFileDialogService(dispatcher, folderRunner);
        LibreFileDialogRequest folderRequest = CreateRequest(
            LibreFileDialogKind.SelectFolder,
            LibreFileDialogOptions.None,
            null) with
        {
            Title = string.Empty,
            Description = "Choose folder",
            SelectedPaths = [],
        };

        folderService.Show(folderRequest).SelectedPaths.Should().Equal("/tmp/folder");
        folderRunner.Arguments.Should().Contain("--directory");
        folderRunner.Arguments.Should().Contain("--title=Choose folder");
    }

    [Fact]
    public async Task Show_RejectsWrongDispatcherThreadBeforeStartingDesktopProcess()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        ProGpuDispatcher dispatcher = new();
        RecordingRunner runner = new(new LibreDesktopDialogProcessResult(0, "/tmp/file.txt\n", string.Empty));
        var service = new ZenityLibreFileDialogService(dispatcher, runner);
        LibreFileDialogRequest request = CreateRequest(
            LibreFileDialogKind.OpenFile,
            LibreFileDialogOptions.None,
            null);

        Func<Task> show = () => Task.Run(() => service.Show(request));

        await show.Should().ThrowAsync<InvalidOperationException>();
        runner.CallCount.Should().Be(0);
    }

    [Fact]
    public void Show_RejectsDesktopProcessFailureAndInvalidAcceptedSelection()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        ProGpuDispatcher dispatcher = new();
        LibreFileDialogRequest request = CreateRequest(
            LibreFileDialogKind.OpenFile,
            LibreFileDialogOptions.None,
            null);
        var failed = new ZenityLibreFileDialogService(
            dispatcher,
            new RecordingRunner(new LibreDesktopDialogProcessResult(2, string.Empty, "GTK display unavailable")));
        var empty = new ZenityLibreFileDialogService(
            dispatcher,
            new RecordingRunner(new LibreDesktopDialogProcessResult(0, "\n", string.Empty)));

        Action fail = () => failed.Show(request);
        Action acceptEmpty = () => empty.Show(request);

        fail.Should().Throw<InvalidOperationException>().WithMessage("*GTK display unavailable*");
        acceptEmpty.Should().Throw<InvalidOperationException>().WithMessage("*without returning a filesystem path*");
    }

    private static LibreFileDialogRequest CreateRequest(
        LibreFileDialogKind kind,
        LibreFileDialogOptions options,
        Action? helpRequested)
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
            helpRequested,
            default);

    private sealed class RecordingRunner(params LibreDesktopDialogProcessResult[] results)
        : ILibreDesktopDialogProcessRunner
    {
        private readonly Queue<LibreDesktopDialogProcessResult> _results = new(results);

        internal int CallCount { get; private set; }

        internal string Executable { get; private set; } = string.Empty;

        internal IReadOnlyList<string> Arguments { get; private set; } = [];

        public LibreDesktopDialogProcessResult Run(string executable, IReadOnlyList<string> arguments)
        {
            CallCount++;
            Executable = executable;
            Arguments = arguments.ToArray();
            return _results.Dequeue();
        }
    }
}
