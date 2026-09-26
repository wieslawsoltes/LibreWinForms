// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class NativeCharacterSequenceTests
{
    [Theory]
    [InlineData(0x61u, "a", LibreInputModifiers.None)]
    [InlineData(0x41u, "A", LibreInputModifiers.Shift)]
    [InlineData(0xe9u, "é", LibreInputModifiers.Alt)]
    [InlineData(0x20acu, "€", LibreInputModifiers.Control | LibreInputModifiers.Alt)]
    [InlineData(0x1f642u, "🙂", LibreInputModifiers.None)]
    public void ActualPlainCallbackPreservesCompleteScalarModifiersAndTimestamp(uint scalar, string text, LibreInputModifiers modifiers)
    {
        NativeCharacterSequence sequence = new();
        Target target = new();
        sequence.Modified(target, scalar, modifiers, 123);
        target.Events.Should().BeEmpty();
        sequence.Plain(target, scalar);
        sequence.Flush();
        target.Events.Should().ContainSingle().Which.Should().Be(new LibreInputEvent(
            LibreInputEventKind.TextInput, 123, modifiers, LibreKey.Unknown, text, default, default, LibrePointerButton.None));
    }

    [Theory]
    [InlineData(LibreInputModifiers.Alt, true)]
    [InlineData(LibreInputModifiers.Alt | LibreInputModifiers.Shift, true)]
    [InlineData(LibreInputModifiers.Control | LibreInputModifiers.Alt, false)]
    [InlineData(LibreInputModifiers.Meta | LibreInputModifiers.Alt, false)]
    [InlineData(LibreInputModifiers.Meta, false)]
    [InlineData(LibreInputModifiers.None, false)]
    public void OnlyUnpairedAltCharactersBecomeSystemText(LibreInputModifiers modifiers, bool system)
    {
        NativeCharacterSequence sequence = new();
        Target target = new();
        sequence.Modified(target, 'f', modifiers, 456);
        sequence.Flush();
        sequence.Flush();
        target.Events.Count.Should().Be(system ? 1 : 0);
        if (system)
            target.Events[0].Should().Be(new LibreInputEvent(LibreInputEventKind.SystemTextInput,
                456, modifiers, LibreKey.Unknown, "f", default, default, LibrePointerButton.None));
    }

    [Fact]
    public void NextCharacterFlushesThePriorOwnerBeforeItsOwnPlainText()
    {
        NativeCharacterSequence sequence = new();
        List<string> order = [];
        Target first = new(input => order.Add("first:" + input.Text));
        Target second = new(input => order.Add("second:" + input.Text));
        sequence.Modified(first, 'f', LibreInputModifiers.Alt, 1);
        sequence.Modified(second, 'x', LibreInputModifiers.None, 2);
        sequence.Plain(second, 'x');
        order.Should().Equal("first:f", "second:x");
        first.Events[0].Kind.Should().Be(LibreInputEventKind.SystemTextInput);
        second.Events[0].Kind.Should().Be(LibreInputEventKind.TextInput);
    }

    [Fact]
    public void ReentrantDeliveryCannotOverwriteTheNewPendingGeneration()
    {
        NativeCharacterSequence sequence = new();
        Target nested = new();
        Target first = new(_ => sequence.Modified(nested, 'n', LibreInputModifiers.Alt, 3));
        Target second = new();
        sequence.Modified(first, 'f', LibreInputModifiers.Alt, 1);
        sequence.Modified(second, 's', LibreInputModifiers.Alt, 2);
        sequence.Flush();
        // The nested unpaired callback is completed at the next native event
        // boundary; it must not overwrite or be overwritten by the ready queue.
        sequence.Flush();
        first.Events.Should().ContainSingle();
        second.Events.Should().ContainSingle();
        nested.Events.Should().ContainSingle().Which.Text.Should().Be("n");
    }

    [Fact]
    public void ReentrantPlainPairCannotConsumeTheOuterPlainCharacter()
    {
        NativeCharacterSequence sequence = new();
        List<string> order = [];
        Target nested = new(input => order.Add("nested:" + input.Text));
        Target first = new(input =>
        {
            order.Add("first:" + input.Text);
            sequence.Modified(nested, 'n', LibreInputModifiers.None, 3);
            sequence.Plain(nested, 'n');
        });
        Target second = new(input => order.Add("second:" + input.Text));
        sequence.Modified(first, 'f', LibreInputModifiers.Alt, 1);
        sequence.Modified(second, 'x', LibreInputModifiers.None, 2);
        order.Should().BeEmpty();
        sequence.Plain(second, 'x');
        sequence.Flush();
        order.Should().Equal("first:f", "second:x", "nested:n");
        second.Events.Should().ContainSingle().Which.Kind.Should().Be(LibreInputEventKind.TextInput);
        nested.Events.Should().ContainSingle().Which.Kind.Should().Be(LibreInputEventKind.TextInput);
    }

    [Fact]
    public void ReentrantRetirementCancelsQueuedCharactersOfOnlyThatOwner()
    {
        NativeCharacterSequence sequence = new();
        Target second = new();
        Target first = new(_ => sequence.Cancel(second));
        sequence.Modified(first, 'f', LibreInputModifiers.Alt, 1);
        sequence.Modified(second, 'x', LibreInputModifiers.None, 2);
        sequence.Plain(second, 'x');
        sequence.Flush();
        first.Events.Should().ContainSingle();
        second.Events.Should().BeEmpty();
    }

    [Fact]
    public void TargetRetirementAndCancellationDoNotAffectAnotherLiveOwner()
    {
        NativeCharacterSequence sequence = new();
        Target first = new();
        Target second = new();
        sequence.Modified(first, 'f', LibreInputModifiers.Alt, 1);
        first.IsAlive = false;
        sequence.Flush();
        first.Events.Should().BeEmpty();
        sequence.Modified(second, 's', LibreInputModifiers.Alt, 2);
        sequence.Cancel(first);
        sequence.Flush();
        second.Events.Should().ContainSingle();
        sequence.Modified(second, 'x', LibreInputModifiers.Alt, 3);
        sequence.Cancel(second);
        sequence.Flush();
        second.Events.Should().ContainSingle();
    }

    [Theory]
    [InlineData(0xd800u)]
    [InlineData(0x110000u)]
    public void InvalidScalarIsRejectedBeforeTheValidPendingEventChanges(uint invalid)
    {
        NativeCharacterSequence sequence = new();
        Target target = new();
        sequence.Modified(target, 'f', LibreInputModifiers.Alt, 1);
        Action invalidInput = () => sequence.Modified(target, invalid, LibreInputModifiers.None, 2);
        invalidInput.Should().Throw<ArgumentOutOfRangeException>();
        sequence.Flush();
        target.Events.Should().ContainSingle().Which.Text.Should().Be("f");
    }

    [Fact]
    public void MismatchedPlainCallbackCannotConsumeAnotherOwnerOrScalar()
    {
        NativeCharacterSequence sequence = new();
        Target owner = new();
        Target other = new();
        sequence.Modified(owner, 'f', LibreInputModifiers.Alt, 1);
        Action wrongOwner = () => sequence.Plain(other, 'f');
        Action wrongScalar = () => sequence.Plain(owner, 'x');
        wrongOwner.Should().Throw<InvalidOperationException>();
        wrongScalar.Should().Throw<InvalidOperationException>();
        sequence.Plain(owner, 'f');
        owner.Events.Should().ContainSingle().Which.Kind.Should().Be(LibreInputEventKind.TextInput);
        other.Events.Should().BeEmpty();
    }

    private sealed class Target(Action<LibreInputEvent>? callback = null) : INativeCharacterTarget
    {
        public bool IsAlive { get; set; } = true;
        internal List<LibreInputEvent> Events { get; } = [];
        public void Input(in LibreInputEvent input)
        {
            Events.Add(input);
            callback?.Invoke(input);
        }
    }
}
