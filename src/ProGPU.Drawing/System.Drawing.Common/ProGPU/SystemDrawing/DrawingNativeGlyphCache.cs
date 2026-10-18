using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ProGPU.Backend.Native;
using ProGPU.Scene.Native;
using ProGPU.Text;

namespace ProGPU.SystemDrawing;

// The cache owns CPU font generations only. DrawingContext acquires the
// independent recorded lease before the temporary caller use is released.
internal static class DrawingNativeGlyphCache
{
    private static readonly ConditionalWeakTable<TtfFont, FontCache> Fonts = new();

    internal static Use Acquire(TtfFont font, ushort[] glyphs, Vector2[] positions,
        uint pixelsPerEm26_6, float rasterScale, NativeFontOutlinePolicy policy)
        => Fonts.GetValue(font, static source => new(source)).Acquire(
            glyphs, positions, pixelsPerEm26_6, rasterScale, policy);

    internal sealed class Use : IDisposable
    {
        private Entry? _entry;
        internal Use(Entry entry) => _entry = entry;
        internal HintedGlyphGeometry Geometry =>
            _entry?.Geometry ?? throw new ObjectDisposedException(nameof(Use));
        public void Dispose() => Interlocked.Exchange(ref _entry, null)?.Release();
    }

    internal sealed class Entry
    {
        private readonly object _gate = new();
        private int _uses;
        private bool _retired;
        internal readonly ushort[] Glyphs;
        internal readonly Vector2[] Positions;
        internal readonly uint PixelsPerEm26_6;
        internal readonly float RasterScale;
        internal readonly NativeFontOutlinePolicy Policy;
        internal readonly HintedGlyphGeometry Geometry;
        internal readonly long Cost;
        internal bool IsRetired => Volatile.Read(ref _retired);

        internal Entry(ushort[] glyphs, Vector2[] positions, uint em, float scale,
            NativeFontOutlinePolicy policy, HintedGlyphGeometry geometry, int fontBytes)
        {
            Glyphs = glyphs;
            Positions = positions;
            PixelsPerEm26_6 = em;
            RasterScale = scale;
            Policy = policy;
            Geometry = geometry;
            // Charge complete source bytes conservatively even if the native
            // producer shares their owner across captures.
            Cost = fontBytes + (long)glyphs.Length * (sizeof(ushort) + Unsafe.SizeOf<Vector2>() +
                Unsafe.SizeOf<FontGlyphPlacement>()) +
                (long)geometry.OutlineCount * Unsafe.SizeOf<GpuGlyphRecord>() +
                (long)geometry.SegmentCount * Unsafe.SizeOf<GpuSegment>();
        }

        internal bool Matches(ushort[] glyphs, Vector2[] positions, uint em,
            float scale, NativeFontOutlinePolicy policy)
            => PixelsPerEm26_6 == em && Policy == policy &&
                BitConverter.SingleToInt32Bits(RasterScale) == BitConverter.SingleToInt32Bits(scale) &&
                Glyphs.AsSpan().SequenceEqual(glyphs) &&
                MemoryMarshal.AsBytes(Positions.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(positions.AsSpan()));

        internal Use Acquire()
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_retired, this);
                var use = new Use(this);
                _uses = checked(_uses + 1);
                return use;
            }
        }

        internal void Retire()
        {
            lock (_gate)
            {
                _retired = true;
                if (_uses == 0) Geometry.Dispose();
            }
        }

        internal void Release()
        {
            lock (_gate)
            {
                _uses--;
                if (_retired && _uses == 0) Geometry.Dispose();
            }
        }
    }

    private sealed class FontCache(TtfFont font)
    {
        private const int MaximumEntries = 16;
        private const long MaximumCost = 8 * 1024 * 1024;
        private readonly object _gate = new();
        private readonly List<Entry> _entries = [];
        private NativeTextShapingContext? _context;
        private long _cost;

        internal Use Acquire(ushort[] glyphs, Vector2[] positions, uint em,
            float scale, NativeFontOutlinePolicy policy)
        {
            lock (_gate)
            {
                for (int index = 0; index < _entries.Count; index++)
                {
                    Entry entry = _entries[index];
                    if (entry.IsRetired)
                    {
                        // A failed retirement retains its owner for retry; it
                        // must never be offered as a fresh recording candidate.
                        entry.Retire();
                        _entries.RemoveAt(index--);
                        _cost -= entry.Cost;
                        continue;
                    }
                    if (!entry.Matches(glyphs, positions, em, scale, policy)) continue;
                    Use found = entry.Acquire();
                    _entries.RemoveAt(index);
                    _entries.Add(entry);
                    return found;
                }

                ushort[] originalGlyphs = (ushort[])glyphs.Clone();
                Vector2[] originalPositions = (Vector2[])positions.Clone();
                uint[] nativeGlyphs = Array.ConvertAll(originalGlyphs, static glyph => (uint)glyph);
                _context ??= new(font.FontData.Span, checked((uint)font.FaceIndex));
                using NativeHintedFontBatch batch = _context.CaptureOutlineBatch(0, nativeGlyphs,
                    em, em, NativeFontHintInterpreter.TrueType35, policy);
                HintedGlyphGeometry geometry = NativeHintedGlyphGeometryFactory.CreateFontBatch(batch,
                    originalPositions, scale, coverage: NativeHintedCoverage.AntialiasedVector);
                Entry candidate;
                try { candidate = new(originalGlyphs, originalPositions, em, scale, policy, geometry, font.FontData.Length); }
                catch (Exception failure) { CleanupAfterFailure(geometry.Dispose, failure); throw; }
                Use use;
                try { use = candidate.Acquire(); }
                catch (Exception failure) { CleanupAfterFailure(candidate.Retire, failure); throw; }
                try
                {
                    if (candidate.Cost <= MaximumCost)
                    {
                        // Retire before publishing the candidate. An active
                        // caller protects its wrapper until it records a lease.
                        while (_entries.Count >= MaximumEntries || _cost + candidate.Cost > MaximumCost)
                        {
                            Entry oldest = _entries[0];
                            oldest.Retire();
                            _entries.RemoveAt(0);
                            _cost -= oldest.Cost;
                        }
                        _entries.Add(candidate);
                        _cost += candidate.Cost;
                    }
                    else candidate.Retire();
                    return use;
                }
                catch (Exception failure)
                {
                    CleanupAfterFailure(candidate.Retire, failure);
                    CleanupAfterFailure(use.Dispose, failure);
                    throw;
                }
            }
        }
    }

    private static void CleanupAfterFailure(Action cleanup, Exception failure)
    {
        try { cleanup(); }
        catch (Exception retirementFailure)
        {
            try { failure.Data["NativeGlyphCacheCleanupFailure"] = retirementFailure; }
            catch { /* Cleanup diagnostics must not replace the original failure. */ }
        }
    }
}
