// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public sealed class TextRendererMarginPolicyTests
{
    [Theory]
    [InlineData(0, false, false, 0, 0)]
    [InlineData(12, false, false, 2, 3)]
    [InlineData(13, false, false, 3, 4)]
    [InlineData(15, false, false, 3, 4)]
    [InlineData(32, false, false, 6, 8)]
    [InlineData(35, false, false, 6, 9)]
    [InlineData(32, false, true, 11, 14)]
    [InlineData(32, true, false, 0, 0)]
    [InlineData(32, true, true, 11, 14)]
    public void OriginalRoundingAndFlagPrecedenceArePreserved(int height, bool none, bool extra, int left, int right)
        => TextRendererMargins.Get(height, none, extra).Should().Be((left, right));
}
