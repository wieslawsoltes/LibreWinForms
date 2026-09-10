// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using FluentAssertions;
using Xunit;

namespace LibreWinForms.Platform.Tests;

public class ManagedLibreFontDialogServiceTests
{
    [Fact]
    public void Show_SelectsFamilyStyleSizeEffectsAndColorAndRaisesCallbacks()
    {
        using var host = new ManagedLibreColorDialogServiceTests.ColorDialogHost();
        ManagedLibreFontDialogService service = CreateService(host);
        LibreFontDialogSelection applied = default;
        int applyRequests = 0;
        int helpRequests = 0;
        host.EnqueueKey(LibreKey.Down);
        host.EnqueueKey(LibreKey.Tab);
        host.EnqueueKey(LibreKey.Down);
        host.EnqueueKey(LibreKey.Tab);
        host.EnqueueKey(LibreKey.Down);
        host.EnqueueKey(LibreKey.Tab);
        host.EnqueueKey(LibreKey.Space);
        host.EnqueueKey(LibreKey.Tab);
        host.EnqueueKey(LibreKey.Space);
        host.EnqueueKey(LibreKey.Tab);
        host.EnqueueKey(LibreKey.Down);
        host.EnqueueKey(LibreKey.Tab);
        host.EnqueueKey(LibreKey.Enter);
        host.EnqueueKey(LibreKey.Tab);
        host.EnqueueKey(LibreKey.Enter);
        host.EnqueueKey(LibreKey.Tab);
        host.EnqueueKey(LibreKey.Enter);

        LibreFontDialogResult result = service.Show(new LibreFontDialogRequest(
            new("Alpha", 10, FontStyle.Regular, 1, false, Color.Black),
            MinimumSize: 9,
            MaximumSize: 14,
            LibreFontDialogOptions.AllowVectorFonts
                | LibreFontDialogOptions.ShowEffects
                | LibreFontDialogOptions.ShowColor
                | LibreFontDialogOptions.ShowApply
                | LibreFontDialogOptions.ShowHelp,
            selection =>
            {
                applyRequests++;
                applied = selection;
            },
            () => helpRequests++,
            Owner: default));

        result.Accepted.Should().BeTrue();
        result.Selection.FamilyName.Should().Be("Bravo Mono");
        result.Selection.SizeInPoints.Should().Be(11);
        result.Selection.Style.Should().Be(FontStyle.Bold | FontStyle.Underline | FontStyle.Strikeout);
        result.Selection.Color.ToArgb().Should().Be(Color.DimGray.ToArgb());
        applied.Should().Be(result.Selection);
        applyRequests.Should().Be(1);
        helpRequests.Should().Be(1);
        host.LastCreateOptions.Title.Should().Be("Font");
        host.WindowShown.Should().BeTrue();
        host.WindowActivated.Should().BeTrue();
        host.PaintCount.Should().BeGreaterThan(0);
        host.TextDrawCount.Should().BeGreaterThan(0);
        host.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void Show_AppliesTypedFixedPitchFilterAndEscapeCancels()
    {
        using var host = new ManagedLibreColorDialogServiceTests.ColorDialogHost();
        ManagedLibreFontDialogService service = CreateService(host);
        host.EnqueueKey(LibreKey.Escape);

        LibreFontDialogResult result = service.Show(new LibreFontDialogRequest(
            new("Bravo Mono", 12, FontStyle.Regular, 1, false, Color.Black),
            MinimumSize: 0,
            MaximumSize: 0,
            LibreFontDialogOptions.AllowVectorFonts | LibreFontDialogOptions.FixedPitchOnly,
            ApplyRequested: null,
            HelpRequested: null,
            Owner: default));

        result.Accepted.Should().BeFalse();
        result.Selection.FamilyName.Should().Be("Bravo Mono");
        host.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void Show_PreservesHiddenEffectsAndExactNonPaletteColor()
    {
        using var host = new ManagedLibreColorDialogServiceTests.ColorDialogHost();
        ManagedLibreFontDialogService service = CreateService(host);
        Color custom = Color.FromArgb(17, 93, 201);
        host.EnqueueKey(LibreKey.Enter);

        LibreFontDialogResult result = service.Show(new LibreFontDialogRequest(
            new("Alpha", 10, FontStyle.Italic | FontStyle.Underline | FontStyle.Strikeout, 1, false, custom),
            MinimumSize: 0,
            MaximumSize: 0,
            LibreFontDialogOptions.AllowVectorFonts | LibreFontDialogOptions.ShowColor,
            ApplyRequested: null,
            HelpRequested: null,
            Owner: default));

        result.Accepted.Should().BeTrue();
        result.Selection.Style.Should().Be(FontStyle.Italic | FontStyle.Underline | FontStyle.Strikeout);
        result.Selection.Color.ToArgb().Should().Be(custom.ToArgb());
    }

    [Fact]
    public void Show_RejectsMissingRequiredFontAndWrongThreadBeforeCreatingWindow()
    {
        using var host = new ManagedLibreColorDialogServiceTests.ColorDialogHost();
        ManagedLibreFontDialogService service = CreateService(host);
        LibreFontDialogRequest missing = new(
            new("Missing", 12, FontStyle.Regular, 1, false, Color.Black),
            0,
            0,
            LibreFontDialogOptions.AllowVectorFonts | LibreFontDialogOptions.FontMustExist,
            null,
            null,
            default);

        Action showMissing = () => service.Show(missing);
        showMissing.Should().Throw<ArgumentException>().WithParameterName("familyName");

        host.HasDispatcherAccess = false;
        Action wrongThread = () => service.Show(missing with
        {
            Options = LibreFontDialogOptions.AllowVectorFonts,
        });
        wrongThread.Should().Throw<InvalidOperationException>().WithMessage("*owning dispatcher thread*");
        host.WindowCreateCount.Should().Be(0);
    }

    private static ManagedLibreFontDialogService CreateService(
        ManagedLibreColorDialogServiceTests.ColorDialogHost host)
        => new(host, host.Handles, host, host, host, host, new TestFontCatalog());

    private sealed class TestFontCatalog : ILibreFontCatalog
    {
        public IReadOnlyList<LibreFontFamilyInfo> GetFamilies() =>
        [
            new("Alpha", true, false, true, false, false, true, false, false),
            new("Bravo Mono", true, true, false, false, true, true, false, false),
        ];
    }
}
