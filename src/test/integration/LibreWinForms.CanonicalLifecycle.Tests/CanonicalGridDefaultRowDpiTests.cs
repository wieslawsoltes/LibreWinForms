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
    public void PortableGridDefaultRowUsesReference192()
    {
        if (RunDpiCaseInNewProcess()) return;
        VerifyGridDefaultRow(2, HighDpiMode.SystemAware, GraphicsUnit.Point, 192);
    }

    [Fact]
    public void PortableGridDefaultRowUsesFractionalReference144()
    {
        if (RunDpiCaseInNewProcess()) return;
        VerifyGridDefaultRow(1.5, HighDpiMode.SystemAware, GraphicsUnit.Point, 144);
    }

    [Fact]
    public void PortableGridDefaultRowLogicalUnitsRemain96()
    {
        if (RunDpiCaseInNewProcess()) return;
        VerifyGridDefaultRow(2, HighDpiMode.DpiUnaware, GraphicsUnit.Point, 96);
    }

    [Fact]
    public void PortableGridDefaultRowPixelFontIsNotMultiplied()
    {
        if (RunDpiCaseInNewProcess()) return;
        VerifyGridDefaultRow(2, HighDpiMode.SystemAware, GraphicsUnit.Pixel, 192);
    }

    [Fact]
    public void PortableGridDefaultRowPreservesExplicitTemplateHeight()
    {
        if (RunDpiCaseInNewProcess()) return;
        PrepareFontAutoScalePlatform(2, HighDpiMode.SystemAware);
        using DataGridView grid = new();
        grid.RowTemplate.Height = 21;
        grid.Columns.Add(new DataGridViewTextBoxColumn());
        int index = grid.Rows.Add("Alice");
        grid.RowTemplate.Height.Should().Be(21);
        grid.Rows[index].Height.Should().Be(21);
        grid.Rows[grid.NewRowIndex].Height.Should().Be(21);
        using DataGridViewRow clone = (DataGridViewRow)grid.Rows[index].Clone();
        clone.Height.Should().Be(21);
    }

    [Fact]
    public void PortableGridDefaultRowKeepsApplicationDefaultFontPolicy()
    {
        if (RunDpiCaseInNewProcess()) return;
        PrepareFontAutoScalePlatform(2, HighDpiMode.SystemAware);
        using Font defaultFont = new(FontFamily.GenericSansSerif, 9.25f);
        Application.SetDefaultFont(defaultFont);
        int expected = (int)Math.Ceiling(Control.DefaultFont.GetHeight(192f)) + 9;
        using Font gridFont = new(FontFamily.GenericSansSerif, 30f);
        using DataGridView grid = new() { Font = gridFont };
        grid.RowTemplate.Height.Should().Be(expected,
            "canonical default rows use the application default font, not a per-grid autosizing policy");
        grid.Columns.Add(new DataGridViewTextBoxColumn());
        grid.Rows[grid.Rows.Add("Alice")].Height.Should().Be(expected);
    }

    private static void VerifyGridDefaultRow(double scale, HighDpiMode mode, GraphicsUnit unit, float dpi)
    {
        PrepareFontAutoScalePlatform(scale, mode);
        using Font font = new(FontFamily.GenericSansSerif, 9.25f, FontStyle.Regular, unit);
        Application.SetDefaultFont(font);
        Font selected = Control.DefaultFont;
        int expected = (int)Math.Ceiling(selected.GetHeight(dpi)) + 9;
        using DataGridViewRow row = new();
        row.Height.Should().Be(expected);
        row.MinimumHeight.Should().Be(3);
        using DataGridViewRow clone = (DataGridViewRow)row.Clone();
        clone.Height.Should().Be(expected);
        using DataGridView grid = new();
        grid.RowTemplate.Height.Should().Be(expected);
        grid.Columns.Add(new DataGridViewTextBoxColumn());
        grid.Rows[grid.Rows.Add("Alice")].Height.Should().Be(expected);
        grid.Rows[grid.NewRowIndex].Height.Should().Be(expected);
        selected.Unit.Should().Be(unit);
    }
}
