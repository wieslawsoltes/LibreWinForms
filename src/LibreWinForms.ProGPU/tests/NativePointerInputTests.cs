// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;
using ProGPU.Backend;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class NativePointerInputTests
{
    [Theory]
    [InlineData(NativePointerEventKind.Move, LibreInputEventKind.PointerMove)]
    [InlineData(NativePointerEventKind.Drag, LibreInputEventKind.PointerMove)]
    [InlineData(NativePointerEventKind.Enter, LibreInputEventKind.PointerMove)]
    [InlineData(NativePointerEventKind.Down, LibreInputEventKind.PointerDown)]
    [InlineData(NativePointerEventKind.Up, LibreInputEventKind.PointerUp)]
    [InlineData(NativePointerEventKind.Leave, LibreInputEventKind.PointerLeave)]
    [InlineData(NativePointerEventKind.Cancel, LibreInputEventKind.PointerCancel)]
    public void NativeMetadataSurvivesSingleCanonicalProjection(NativePointerEventKind kind, LibreInputEventKind expected)
    {
        using Fixture f = new();
        bool button = kind is NativePointerEventKind.Down or NativePointerEventKind.Up or NativePointerEventKind.Drag;
        NativePointerEvent packet = new(kind, 16777217.25, -3.75, 1.12500001,
            button ? 1 : -1, button ? 2 : 0, (NativePointerModifiers)255);
        f.Provider.Emit(packet);
        LibreInputEvent actual = Assert.Single(f.Target.Inputs);
        Assert.Equal(expected, actual.Kind);
        Assert.Equal(new LibrePoint(33554435, -8), actual.Position);
        Assert.Equal(11250000, actual.Timestamp);
        Assert.Equal((LibreInputModifiers)15, actual.Modifiers);
        Assert.Equal(expected is LibreInputEventKind.PointerDown or LibreInputEventKind.PointerUp
            ? LibrePointerButton.Secondary : LibrePointerButton.None, actual.Button);
        LibreNativePointerMetadata raw = Assert.IsType<LibreNativePointerMetadata>(actual.NativePointer);
        Assert.Equal(packet.X, raw.X); Assert.Equal(packet.Y, raw.Y);
        Assert.Equal(packet.Timestamp, raw.Timestamp); Assert.Equal(packet.Button, raw.Button);
        Assert.Equal(packet.ClickCount, raw.ClickCount); Assert.Equal((LibreNativePointerModifiers)255, raw.Modifiers);
        Assert.Equal(kind.ToString(), raw.Kind.ToString());
        Assert.Equal(1, f.Target.Flushes);
    }

    [Theory]
    [InlineData(0, LibrePointerButton.Primary)]
    [InlineData(1, LibrePointerButton.Secondary)]
    [InlineData(2, LibrePointerButton.Middle)]
    [InlineData(3, LibrePointerButton.XButton1)]
    [InlineData(4, LibrePointerButton.XButton2)]
    public void NativeButtonsUseCanonicalIdentities(int button, LibrePointerButton expected)
    {
        using Fixture f = new();
        f.Provider.Emit(Packet(NativePointerEventKind.Down) with { Button = button });
        Assert.Equal(expected, Assert.Single(f.Target.Inputs).Button);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void InvalidPacketIsRejectedBeforeCharactersOrSource(int invalid)
    {
        using Fixture f = new();
        NativePointerEvent packet = invalid switch
        {
            0 => Packet() with { X = double.NaN },
            1 => Packet() with { Y = double.PositiveInfinity },
            2 => Packet() with { Timestamp = double.NaN },
            3 => Packet() with { Timestamp = -1 },
            4 => Packet() with { Modifiers = (NativePointerModifiers)256 },
            5 => Packet() with { ClickCount = 1 },
            6 => Packet() with { Button = 0 },
            7 => Packet(NativePointerEventKind.Down) with { Button = -1 },
            8 => Packet(NativePointerEventKind.Drag) with { Button = 64 },
            9 => Packet(NativePointerEventKind.Up) with { ClickCount = -1 },
            10 => Packet() with { ScrollX = double.NaN },
            11 => Packet() with { ScrollPhase = 1 },
            12 => Packet() with { ScrollProtocol = NativePointerScrollProtocol.AppKit },
            _ => Packet() with { Kind = (NativePointerEventKind)99 },
        };
        Assert.Throws<ArgumentException>(() => f.Provider.Emit(packet));
        Assert.Empty(f.Target.Inputs); Assert.Equal(0, f.Target.Flushes);
        Assert.Equal(0, f.Target.Mappings);
    }

    [Theory]
    [InlineData(NativePointerScrollUnit.Lines)]
    [InlineData(NativePointerScrollUnit.Points)]
    public void NativeScrollWithoutPhaseProtocolRemainsExplicitlyUnadmitted(NativePointerScrollUnit unit)
    {
        using Fixture f = new();
        Assert.Throws<PlatformNotSupportedException>(() => f.Provider.Emit(Packet(NativePointerEventKind.Scroll)
            with { ScrollY = .25, ScrollUnit = unit, ScrollPhase = 1 }));
        Assert.Empty(f.Target.Inputs); Assert.Equal(0, f.Target.Flushes);
        Assert.Equal(0, f.Target.Mappings);
    }

    [Theory]
    [InlineData(NativePointerScrollUnit.Lines, 1)]
    [InlineData(NativePointerScrollUnit.Points, 2)]
    public void NativeScrollRetainsBothAxesAndSubscriptionGeneration(NativePointerScrollUnit unit, double scale)
    {
        using Fixture f = new();
        NativePointerEvent packet = Packet(NativePointerEventKind.Scroll) with
        {
            ScrollX = -.125, ScrollY = 16777217.25, ScrollUnit = unit,
            ScrollProtocol = NativePointerScrollProtocol.AppKit
        };
        f.Provider.Emit(packet);
        LibreInputEvent input = Assert.Single(f.Target.Inputs);
        Assert.Equal(LibreInputEventKind.PointerScroll, input.Kind);
        Assert.Equal(default, input.Delta);
        Assert.Equal(LibreNativePointerKind.Scroll, input.NativePointer!.Value.Kind);
        LibreNativeScrollMetadata scroll = input.NativeScroll!.Value;
        Assert.Equal(packet.ScrollX, scroll.X); Assert.Equal(packet.ScrollY, scroll.Y);
        Assert.Equal(unit.ToString(), scroll.Unit.ToString());
        Assert.Equal(LibreNativeScrollProtocol.AppKit, scroll.Protocol);
        Assert.Equal(0U, scroll.Phase); Assert.Equal(0U, scroll.MomentumPhase);
        Assert.Equal(scale, scroll.PointScale); Assert.Equal(f.Provider.InputGeneration, scroll.Generation);
        f.Provider.InputGeneration++;
        f.Provider.Emit(packet);
        Assert.Same(scroll.Stream, f.Target.Inputs[1].NativeScroll!.Value.Stream);
        Assert.NotEqual(scroll.Generation, f.Target.Inputs[1].NativeScroll!.Value.Generation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void InvalidNativeScrollDoesNotFlushOrMap(int invalid)
    {
        using Fixture f = new();
        NativePointerEvent packet = Packet(NativePointerEventKind.Scroll);
        packet = invalid switch
        {
            0 => packet with { ScrollX = double.NaN },
            1 => packet with { ScrollY = double.PositiveInfinity },
            2 => packet with { ScrollUnit = (NativePointerScrollUnit)99 },
            _ => packet with { ScrollProtocol = (NativePointerScrollProtocol)99 }
        };
        Assert.Throws<ArgumentException>(() => f.Provider.Emit(packet));
        Assert.Empty(f.Target.Inputs); Assert.Equal(0, f.Target.Flushes); Assert.Equal(0, f.Target.Mappings);
    }

    [Theory]
    [InlineData(1U, 0U)]
    [InlineData(2U, 0U)]
    [InlineData(4U, 0U)]
    [InlineData(8U, 0U)]
    [InlineData(16U, 0U)]
    [InlineData(32U, 0U)]
    [InlineData(0U, 1U)]
    [InlineData(0U, 2U)]
    [InlineData(0U, 4U)]
    [InlineData(0U, 8U)]
    [InlineData(0U, 16U)]
    public void NativeAppKitPhasesRetainTheirExactIdentity(uint phase, uint momentum)
    {
        using Fixture f = new();
        f.Provider.Emit(Packet(NativePointerEventKind.Scroll) with
        {
            ScrollProtocol = NativePointerScrollProtocol.AppKit,
            ScrollPhase = phase, MomentumPhase = momentum, ScrollY = -.25
        });
        LibreNativeScrollMetadata raw = Assert.Single(f.Target.Inputs).NativeScroll!.Value;
        Assert.Equal(phase, raw.Phase); Assert.Equal(momentum, raw.MomentumPhase); Assert.Equal(-.25, raw.Y);
    }

    [Theory]
    [InlineData(3U, 0U)]
    [InlineData(64U, 0U)]
    [InlineData(0U, 3U)]
    [InlineData(0U, 32U)]
    [InlineData(0U, 64U)]
    [InlineData(1U, 1U)]
    public void InvalidNativeAppKitPhasesCannotFlushOrMap(uint phase, uint momentum)
    {
        using Fixture f = new();
        Assert.Throws<ArgumentException>(() => f.Provider.Emit(Packet(NativePointerEventKind.Scroll) with
        {
            ScrollProtocol = NativePointerScrollProtocol.AppKit,
            ScrollPhase = phase, MomentumPhase = momentum, ScrollY = -8, ScrollUnit = NativePointerScrollUnit.Points
        }));
        Assert.Empty(f.Target.Inputs); Assert.Equal(0, f.Target.Flushes); Assert.Equal(0, f.Target.Mappings);
        Assert.Equal(0, f.Target.PointScaleReads);
    }

    [Fact]
    public void UnsupportedExtraButtonDoesNotBecomeAButtonlessClick()
    {
        using Fixture f = new();
        Assert.Throws<PlatformNotSupportedException>(() => f.Provider.Emit(Packet(NativePointerEventKind.Down) with { Button = 5 }));
        Assert.Empty(f.Target.Inputs); Assert.Equal(0, f.Target.Flushes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CharacterFlushCannotForwardAnObsoleteProviderTail(int transition)
    {
        using Fixture f = new();
        f.Target.Flush = () =>
        {
            f.Target.Flush = null;
            if (transition == 0) f.Provider.InputGeneration++;
            if (transition == 1) f.Subscription.Dispose();
            if (transition == 2) f.Target.Current = false;
            if (transition == 3) f.Provider.Emit(Packet() with { X = 99 });
        };
        f.Provider.Emit(Packet());
        if (transition == 3) Assert.Equal(198, Assert.Single(f.Target.Inputs).Position.X);
        else Assert.Empty(f.Target.Inputs);
        if (transition == 0)
        {
            f.Provider.Emit(Packet() with { X = 77 });
            Assert.Equal(154, Assert.Single(f.Target.Inputs).Position.X);
        }
    }

    [Fact]
    public void MappingCallbackCannotPublishOldGeneration()
    {
        using Fixture f = new();
        f.Target.Mapping = () => f.Provider.InputGeneration++;
        f.Provider.Emit(Packet());
        Assert.Empty(f.Target.Inputs); Assert.Equal(0, f.Target.Flushes);
    }

    [Fact]
    public void NewPolicyGenerationAdmitsCancellationAndSubsequentReopenedInput()
    {
        using Fixture f = new();
        f.Provider.Emit(Packet(NativePointerEventKind.Down));
        f.Provider.InputGeneration++;
        f.Provider.Emit(Packet(NativePointerEventKind.Cancel));
        f.Provider.InputGeneration++;
        f.Provider.Emit(Packet(NativePointerEventKind.Enter));
        Assert.Equal(new[] { LibreInputEventKind.PointerDown, LibreInputEventKind.PointerCancel, LibreInputEventKind.PointerMove },
            f.Target.Inputs.Select(input => input.Kind));
    }

    [Fact]
    public void CharacterFailureIsNotSwallowed()
    {
        using Fixture f = new();
        InvalidOperationException expected = new("character callback");
        f.Target.Flush = () => throw expected;
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => f.Provider.Emit(Packet())));
        Assert.Empty(f.Target.Inputs);
    }

    [Fact]
    public void RetiredCallbackCannotEnterReplacementSubscription()
    {
        using Fixture f = new();
        Action<NativePointerEvent> old = f.Provider.Callbacks!;
        f.Subscription.Dispose();
        using NativePointerInput replacement = new(f.Provider, f.Target);
        f.Target.Subscription = replacement;
        old(Packet());
        Assert.Empty(f.Target.Inputs);
        f.Provider.Emit(Packet());
        Assert.Single(f.Target.Inputs);
    }

    [Fact]
    public void UnsubscribeFailureStillRetiresQueuedCallbacks()
    {
        Fixture f = new();
        Action<NativePointerEvent> old = f.Provider.Callbacks!;
        InvalidOperationException expected = new("remove accessor");
        f.Provider.UnsubscribeFailure = expected;
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(f.Subscription.Dispose));
        old(Packet()); f.Provider.Emit(Packet());
        Assert.Empty(f.Target.Inputs);
        f.Subscription.Dispose(); // Already retired; do not enter accessor twice.
        f.Provider.UnsubscribeFailure = null;
        f.Provider.PointerEvent -= old;
        f.Provider.Dispose();
    }

    [Fact]
    public void FailedSubscriptionPreservesOriginalAndRetiresCallback()
    {
        NativePointerTestContext provider = new()
        {
            SubscribeFailure = new InvalidOperationException("add"),
            UnsubscribeFailure = new InvalidOperationException("remove"),
        };
        Target target = new();
        Exception actual = Assert.Throws<InvalidOperationException>(() => new NativePointerInput(provider, target));
        Assert.Same(provider.SubscribeFailure, actual);
        Assert.Same(provider.UnsubscribeFailure, actual.Data["NativePointerUnsubscribe"]);
        provider.Emit(Packet());
        Assert.Empty(target.Inputs);
    }

    [Fact]
    public void OnlyActualKeyboardlessNativePopupMayOmitGlfwCharacters()
    {
        NativePointerTestContext provider = new();
        Assert.False(NativePointerInput.RequiresGlfwCharacters(provider, true, 0));
        Assert.True(NativePointerInput.RequiresGlfwCharacters(provider, false, 123));
        Assert.Throws<PlatformNotSupportedException>(() => NativePointerInput.RequiresGlfwCharacters(provider, false, 0));
        provider.Keyboards = [null!]; // Count only; never a fabricated keyboard used for input.
        Assert.Throws<PlatformNotSupportedException>(() => NativePointerInput.RequiresGlfwCharacters(provider, true, 0));
        Assert.Throws<PlatformNotSupportedException>(() => NativePointerInput.RequiresGlfwCharacters(new PlainInputContext(), true, 0));
    }

    [Theory]
    [InlineData(LibreWindowCoordinateMode.Logical, 2, 2, 1, -1)]
    [InlineData(LibreWindowCoordinateMode.Logical, 2, 1, 0, 0)]
    [InlineData(LibreWindowCoordinateMode.DevicePixels, 2, 2, 2, -2)]
    public void NativePointRoundsOnlyAfterActualCoordinateMapping(LibreWindowCoordinateMode mode,
        double dpi, double framebuffer, int x, int y)
        => Assert.Equal(new LibrePoint(x, y), LibreWindowCoordinates.ToManagedPoint(.75, -.75, mode, dpi, framebuffer));

    [Fact]
    public void OriginalConstructorAndEightValueDeconstructionRemainAvailable()
    {
        LibreInputEvent input = new(LibreInputEventKind.PointerDown, 123, LibreInputModifiers.Alt,
            LibreKey.Unknown, null, new(4, 5), new(6, 7), LibrePointerButton.Primary);
        var (kind, time, modifiers, key, text, point, delta, button) = input;
        Assert.Equal(LibreInputEventKind.PointerDown, kind); Assert.Equal(123, time);
        Assert.Equal(LibreInputModifiers.Alt, modifiers); Assert.Equal(LibreKey.Unknown, key);
        Assert.Null(text); Assert.Equal(new(4, 5), point); Assert.Equal(new(6, 7), delta);
        Assert.Equal(LibrePointerButton.Primary, button); Assert.Null(input.NativePointer);
    }

    internal static NativePointerEvent Packet(NativePointerEventKind kind = NativePointerEventKind.Move)
        => new(kind, 1.25, 2.75, 3.125, kind is NativePointerEventKind.Down or NativePointerEventKind.Up or NativePointerEventKind.Drag ? 0 : -1,
            0, NativePointerModifiers.Shift);

    private sealed class Fixture : IDisposable
    {
        internal NativePointerTestContext Provider { get; } = new();
        internal Target Target { get; } = new();
        internal NativePointerInput Subscription { get; }
        internal Fixture() { Subscription = new(Provider, Target); Target.Subscription = Subscription; }
        public void Dispose() { Subscription.Dispose(); Provider.Dispose(); }
    }

    private sealed class Target : INativePointerTarget
    {
        internal NativePointerInput? Subscription { get; set; }
        internal bool Current { get; set; } = true;
        internal Action? Mapping { get; set; }
        internal Action? Flush { get; set; }
        internal List<LibreInputEvent> Inputs { get; } = [];
        internal int Flushes { get; private set; }
        internal int Mappings { get; private set; }
        internal int PointScaleReads { get; private set; }
        public bool IsCurrent(NativePointerInput subscription) => Current && ReferenceEquals(subscription, Subscription);
        public double NativePointScale { get { PointScaleReads++; return 2; } }
        public LibrePoint MapPoint(double x, double y)
        {
            Mappings++;
            Mapping?.Invoke();
            return LibreWindowCoordinates.ToManagedPoint(x, y, LibreWindowCoordinateMode.DevicePixels, 2, 2);
        }

        public void FlushCharacters() { Flushes++; Flush?.Invoke(); }
        public ProGpuDragCancellation? PrepareCancellation() => null;
        public void Input(in LibreInputEvent input) => Inputs.Add(input);
    }

    private sealed class PlainInputContext : Silk.NET.Input.IInputContext
    {
        public nint Handle => 321;
        public IReadOnlyList<Silk.NET.Input.IKeyboard> Keyboards => [];
        public IReadOnlyList<Silk.NET.Input.IMouse> Mice => [];
        public IReadOnlyList<Silk.NET.Input.IGamepad> Gamepads => [];
        public IReadOnlyList<Silk.NET.Input.IJoystick> Joysticks => [];
        public IReadOnlyList<Silk.NET.Input.IInputDevice> OtherDevices => [];
        public event Action<Silk.NET.Input.IInputDevice, bool>? ConnectionChanged { add { } remove { } }
        public void Dispose() { }
    }
}
