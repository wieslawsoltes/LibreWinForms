// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Drawing;
using System.Runtime.ExceptionServices;
using LibreWinForms.Platform;

namespace System.Windows.Forms;

internal interface IPortableRetainedTextLayoutOwner
{
    bool IsRetainedTextStateCurrent(in PortableRetainedTextState state);
    bool IsRetainedTextMutationActive { get; }
}

internal readonly record struct PortableRetainedTextState(
    string Display, Font Font, Rectangle Viewport, TextFormatFlags Flags,
    ILibreTextLayoutService Service, float DpiX, float DpiY, int DeviceDpi, uint SourceVersion)
{
    internal bool Matches(in PortableRetainedTextState other)
        => Display == other.Display && ReferenceEquals(Font, other.Font)
            && Viewport == other.Viewport && Flags == other.Flags
            && ReferenceEquals(Service, other.Service) && DpiX == other.DpiX
            && DpiY == other.DpiY && DeviceDpi == other.DeviceDpi && SourceVersion == other.SourceVersion;
}

/// <summary>
/// Owns a retained display generation and its viewport, independently of an
/// editor's mask, source mutation, word policy or navigation semantics.
/// Provider calls pin that generation until their complete source frame unwinds.
/// </summary>
internal sealed class PortableRetainedTextLayoutOwner(IPortableRetainedTextLayoutOwner source)
{
    private readonly IPortableRetainedTextLayoutOwner _source = source;
    private Generation? _current, _retired;
    private uint _version, _drainVersion;
    private bool _closed, _draining;

    internal PointF Scroll { get; private set; }
    internal bool NeedsCaretVisibility { get; set; } = true;
    internal bool HasLayout => _current is not null;

    internal Use? Acquire(Graphics graphics, in PortableRetainedTextState state)
    {
        if (_closed || !_source.IsRetainedTextStateCurrent(state)) return null;
        if (_current is { } existing && existing.State.Matches(state))
        {
            existing.Uses++;
            return new(this, existing, _version);
        }

        uint version = _version;
        // Allocate the owner before acquiring the provider resource: allocation
        // failure must never leave a returned layout without retirement storage.
        Generation candidate = new(state);
        candidate.Layout = TextRenderer.CreatePortableTextLayout(state.Service,
            graphics, state.Display, state.Font, state.Viewport.Size, state.Flags | TextFormatFlags.NoClipping);

        bool valid;
        try { valid = !_closed && version == _version && _source.IsRetainedTextStateCurrent(state); }
        catch (Exception failure)
        {
            Retire(candidate);
            Drain(failure);
            throw;
        }

        if (!valid)
        {
            Retire(candidate);
            Drain();
            return null;
        }

        Generation? previous = _current;
        _current = candidate;
        uint publishedVersion = ++_version;
        candidate.Uses++;
        NeedsCaretVisibility = true;
        if (previous is not null) Retire(previous);
        Use use = new(this, candidate, publishedVersion);
        try
        {
            Drain();
            if (!use.IsCurrent)
            {
                use.Release();
                return null;
            }

            return use;
        }
        catch (Exception failure)
        {
            use.Release(failure);
            throw;
        }

    }

    internal void Invalidate()
    {
        Generation? previous = _current;
        _current = null;
        _version++;
        NeedsCaretVisibility = true;
        if (previous is not null) Retire(previous);
        Drain();
    }

    internal void Close()
    {
        _closed = true; // Admission closes before any fallible provider cleanup.
        Invalidate();
    }

    internal void DrainRetired(Exception? originalFailure) => Drain(originalFailure);

    internal void RevealCaret(in LibreTextCaret caret, float caretWidth, Size viewport)
    {
        Scroll = new(Math.Clamp(Scroll.X, caret.Position.X + Math.Min(caretWidth, viewport.Width) - viewport.Width,
            caret.Position.X), Math.Clamp(Scroll.Y, caret.Position.Y + Math.Min(caret.Height, viewport.Height) - viewport.Height,
            caret.Position.Y));
        NeedsCaretVisibility = false;
    }

    private void Retire(Generation generation)
    {
        generation.Next = _retired;
        _retired = generation;
    }

    private void Drain(Exception? originalFailure = null)
    {
        if (_draining || _source.IsRetainedTextMutationActive) return;
        _draining = true;
        uint drainVersion = ++_drainVersion;
        Exception? firstFailure = originalFailure;
        try
        {
            while (true)
            {
                Generation? generation = _retired;
                while (generation is not null && (generation.Uses != 0 || generation.LastDrainVersion == drainVersion))
                    generation = generation.Next;
                if (generation is null) break;
                generation.LastDrainVersion = drainVersion;
                {
                    try
                    {
                        generation.Layout.Dispose();
                        // A disposal callback may prepend another generation.
                        // Remove this exact owner, never a new head by position.
                        Generation? predecessor = null;
                        for (Generation? item = _retired; item is not null; item = item.Next)
                        {
                            if (ReferenceEquals(item, generation))
                            {
                                if (predecessor is null) _retired = item.Next;
                                else predecessor.Next = item.Next;
                                break;
                            }

                            predecessor = item;
                        }
                    }
                    catch (Exception failure)
                    {
                        if (firstFailure is null) firstFailure = failure;
                        else PreserveCleanup(firstFailure, failure);
                    }
                }

            }
        }
        finally { _draining = false; }

        if (originalFailure is null && firstFailure is not null)
            ExceptionDispatchInfo.Capture(firstFailure).Throw();
    }

    internal static void PreserveCleanup(Exception failure, Exception cleanup)
    {
        try { failure.Data["PortableRetainedTextCleanup"] = cleanup; }
        catch { } // Diagnostic allocation cannot replace the original failure.
    }

    internal sealed class Generation(PortableRetainedTextState state)
    {
        internal ILibreTextLayout Layout { get; set; } = null!;
        internal PortableRetainedTextState State { get; } = state;
        internal Generation? Next { get; set; }
        internal int Uses { get; set; }
        internal uint LastDrainVersion { get; set; }
    }

    internal struct Use
    {
        private readonly PortableRetainedTextLayoutOwner _owner;
        private readonly Generation _generation;
        private readonly uint _version;
        private bool _released;

        internal Use(PortableRetainedTextLayoutOwner owner, Generation generation, uint version)
        {
            _owner = owner;
            _generation = generation;
            _version = version;
            _released = false;
        }

        internal ILibreTextLayout Layout => _generation.Layout;
        internal bool IsCurrent => !_released && !_owner._closed
            && _version == _owner._version && ReferenceEquals(_owner._current, _generation)
            && _owner._source.IsRetainedTextStateCurrent(_generation.State);

        internal void Release(Exception? originalFailure = null)
        {
            if (_released) return;
            _released = true;
            _generation.Uses--;
            _owner.Drain(originalFailure);
        }
    }
}
#endif
