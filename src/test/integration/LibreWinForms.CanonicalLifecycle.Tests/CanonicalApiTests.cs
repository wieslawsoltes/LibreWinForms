// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.TestContracts;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Fact]
    public void CanonicalApi_LabelAutoEllipsis()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        CanonicalApiContracts.LabelAutoEllipsis();
    }

    [Fact]
    public void CanonicalApi_ButtonTextImageRelation()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        CanonicalApiContracts.ButtonTextImageRelation();
    }

    [Fact]
    public void CanonicalApi_RowDefaultCellStyle()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        CanonicalApiContracts.RowDefaultCellStyle();
    }

    [Fact]
    public void CanonicalApi_CellEditedFormattedValue()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        CanonicalApiContracts.CellEditedFormattedValue();
    }

    [Fact]
    public void CanonicalApi_ColumnVisibility()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        CanonicalApiContracts.ColumnVisibility();
    }

    [Fact]
    public void CanonicalApi_GridColor()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        CanonicalApiContracts.GridColor();
    }

    [Fact]
    public void CanonicalApi_RowHeaderBorderStyle()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        CanonicalApiContracts.RowHeaderBorderStyle();
    }

    [Fact]
    public void CanonicalApi_ApplicationProductName()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        CanonicalApiContracts.ApplicationProductName();
    }

    [Fact]
    public void CanonicalApi_ApplicationContextLifetime()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        CanonicalApiContracts.ApplicationContextLifetime();
    }

    [Fact]
    public void CanonicalApi_BindableComponentAndUpdateModes()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        CanonicalApiContracts.BindableComponentAndUpdateModes();
    }

    [Fact]
    public void CanonicalApi_BindingAndConversionEventArgs()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        CanonicalApiContracts.BindingAndConversionEventArgs();
    }

    [Fact]
    public void CanonicalApi_NativeAndControlHandleLifetime()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        CanonicalApiContracts.NativeAndControlHandleLifetime();
    }

    [Fact]
    public void CanonicalApi_TableLayoutMixedSizing()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        CanonicalApiContracts.TableLayoutMixedSizing();
    }

    [Fact]
    public void CanonicalApi_TableLayoutSpansAndRtl()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        CanonicalApiContracts.TableLayoutSpansAndRtl();
    }

    [Fact]
    public void CanonicalApi_TableLayoutNestedInvalidation()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        CanonicalApiContracts.TableLayoutNestedInvalidation();
    }

    [Fact]
    public void CanonicalApi_PrintDocumentPreviewAction()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        CanonicalApiContracts.PrintDocumentPreviewAction();
    }

    [Fact]
    public void CanonicalApi_PrintDocumentCancellationOrder()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        CanonicalApiContracts.PrintDocumentCancellationOrder();
    }

    [Fact]
    public void CanonicalApi_PrintDocumentRetainedQuerySettings()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        CanonicalApiContracts.PrintDocumentRetainedQuerySettings();
    }
}
