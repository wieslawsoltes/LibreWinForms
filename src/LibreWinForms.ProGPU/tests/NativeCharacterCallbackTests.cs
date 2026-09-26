// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using LibreWinForms.Platform;
using Silk.NET.GLFW;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed unsafe class NativeCharacterCallbackTests
{
    [Fact]
    public void NativePairDeliversFullUnicodeAndRetainsTheOriginalPlainSubscriber()
    {
        Callbacks callbacks = new();
        List<uint> previous = [];
        callbacks.Plain = (_, scalar) => previous.Add(scalar);
        NativeCharacterSequence sequence = new();
        Target target = new();
        using GlfwCharacterInput input = new(callbacks, 1, sequence, target, () => 42);
        callbacks.Modified!((WindowHandle*)1, 0x1f642, KeyModifiers.Shift);
        callbacks.Plain!((WindowHandle*)1, 0x1f642);
        sequence.Flush();
        target.Events.Should().ContainSingle().Which.Should().Be(new LibreInputEvent(
            LibreInputEventKind.TextInput, 42, LibreInputModifiers.Shift,
            LibreKey.Unknown, "🙂", default, default, LibrePointerButton.None));
        previous.Should().Equal(0x1f642u);
    }

    [Fact]
    public void ModifierOnlyNativeCallbackIsFlushedAsOneSystemCharacter()
    {
        Callbacks callbacks = new();
        NativeCharacterSequence sequence = new();
        Target target = new();
        using GlfwCharacterInput input = new(callbacks, 1, sequence, target, () => 42);
        callbacks.Modified!((WindowHandle*)1, 'f', KeyModifiers.Alt);
        target.Events.Should().BeEmpty();
        sequence.Flush();
        sequence.Flush();
        target.Events.Should().ContainSingle().Which.Kind.Should().Be(LibreInputEventKind.SystemTextInput);
    }

    [Fact]
    public void ExistingModifiedCallbackIsRestoredAndAdmissionFailsWithoutTouchingPlain()
    {
        Callbacks callbacks = new();
        GlfwCallbacks.CharCallback plain = (_, _) => { };
        GlfwCallbacks.CharModsCallback modified = (_, _, _) => { };
        callbacks.Plain = plain;
        callbacks.Modified = modified;
        Action create = () => new GlfwCharacterInput(callbacks, 1, new(), new Target(), () => 1);
        create.Should().Throw<InvalidOperationException>();
        callbacks.Plain.Should().BeSameAs(plain);
        callbacks.Modified.Should().BeSameAs(modified);
        callbacks.PlainSets.Should().Be(0);
    }

    [Fact]
    public void FailedPlainInstallationRollsBackTheModifiedCallback()
    {
        Callbacks callbacks = new() { FailPlain = true };
        Action create = () => new GlfwCharacterInput(callbacks, 1, new(), new Target(), () => 1);
        create.Should().Throw<InvalidOperationException>().WithMessage("plain setter failed");
        callbacks.Modified.Should().BeNull();
    }

    [Fact]
    public void FailedRollbackDoesNotReplaceTheOriginalInstallationFailure()
    {
        Callbacks callbacks = new() { FailPlain = true, FailModifiedRemoval = true };
        Action create = () => new GlfwCharacterInput(callbacks, 1, new(), new Target(), () => 1);
        InvalidOperationException failure = create.Should().Throw<InvalidOperationException>()
            .WithMessage("plain setter failed").Which;
        failure.Data[nameof(GlfwCharacterInput)].Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("modified removal failed");
        // Even a callback whose native removal failed remains inert.
        callbacks.Modified!((WindowHandle*)1, 'f', KeyModifiers.Alt);
    }

    [Fact]
    public void RetiredCallbacksCannotDeliverOrResurrectPendingInput()
    {
        Callbacks callbacks = new();
        NativeCharacterSequence sequence = new();
        Target target = new();
        GlfwCallbacks.CharCallback previous = (_, _) => { };
        callbacks.Plain = previous;
        GlfwCharacterInput input = new(callbacks, 1, sequence, target, () => 42);
        GlfwCallbacks.CharCallback retiredPlain = callbacks.Plain!;
        GlfwCallbacks.CharModsCallback retiredModified = callbacks.Modified!;
        retiredModified((WindowHandle*)1, 'f', KeyModifiers.Alt);
        input.Dispose();
        input.Dispose();
        retiredModified((WindowHandle*)1, 'x', KeyModifiers.Alt);
        retiredPlain((WindowHandle*)1, 'x');
        sequence.Flush();
        callbacks.Plain.Should().BeSameAs(previous);
        callbacks.Modified.Should().BeNull();
        target.Events.Should().BeEmpty();
    }

    [Fact]
    public void WrongNativeWindowCannotEnterTheCapturedOwnersCharacterSequence()
    {
        Callbacks callbacks = new();
        NativeCharacterSequence sequence = new();
        Target target = new();
        using GlfwCharacterInput input = new(callbacks, 1, sequence, target, () => 42);
        callbacks.Modified!((WindowHandle*)2, 'f', KeyModifiers.Alt);
        callbacks.Plain!((WindowHandle*)2, 'f');
        sequence.Flush();
        target.Events.Should().BeEmpty();
    }

    [Fact]
    public void DisposalPreservesCallbacksInstalledByAnotherOwner()
    {
        Callbacks callbacks = new();
        GlfwCharacterInput input = new(callbacks, 1, new(), new Target(), () => 1);
        GlfwCallbacks.CharCallback replacementPlain = (_, _) => { };
        GlfwCallbacks.CharModsCallback replacementModified = (_, _, _) => { };
        callbacks.Plain = replacementPlain;
        callbacks.Modified = replacementModified;
        input.Dispose();
        callbacks.Plain.Should().BeSameAs(replacementPlain);
        callbacks.Modified.Should().BeSameAs(replacementModified);
    }

    [Fact]
    public void DisposalRetiresBothSlotsEvenWhenPlainDetachmentThrows()
    {
        Callbacks callbacks = new();
        NativeCharacterSequence sequence = new();
        Target target = new();
        GlfwCharacterInput input = new(callbacks, 1, sequence, target, () => 1);
        GlfwCallbacks.CharCallback retired = callbacks.Plain!;
        callbacks.Modified!((WindowHandle*)1, 'f', KeyModifiers.Alt);
        callbacks.FailPlain = true;
        Action dispose = input.Dispose;
        dispose.Should().Throw<InvalidOperationException>().WithMessage("plain setter failed");
        callbacks.Modified.Should().BeNull();
        retired((WindowHandle*)1, 'f');
        sequence.Flush();
        target.Events.Should().BeEmpty();
    }

    private sealed class Target : INativeCharacterTarget
    {
        public bool IsAlive => true;
        internal List<LibreInputEvent> Events { get; } = [];
        public void Input(in LibreInputEvent input) => Events.Add(input);
    }

    private sealed class Callbacks : IGlfwCharacterCallbacks
    {
        internal GlfwCallbacks.CharCallback? Plain { get; set; }
        internal GlfwCallbacks.CharModsCallback? Modified { get; set; }
        internal bool FailPlain { get; set; }
        internal bool FailModifiedRemoval { get; set; }
        internal int PlainSets { get; set; }
        public GlfwCallbacks.CharCallback? SetPlain(GlfwCallbacks.CharCallback? callback)
        {
            PlainSets++;
            if (FailPlain) throw new InvalidOperationException("plain setter failed");
            GlfwCallbacks.CharCallback? previous = Plain;
            Plain = callback;
            return previous;
        }

        public GlfwCallbacks.CharModsCallback? SetModified(GlfwCallbacks.CharModsCallback? callback)
        {
            if (callback is null && FailModifiedRemoval)
                throw new InvalidOperationException("modified removal failed");
            GlfwCallbacks.CharModsCallback? previous = Modified;
            Modified = callback;
            return previous;
        }
    }
}
