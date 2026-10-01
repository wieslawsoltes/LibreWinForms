// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_NATIVE_EDIT_WORD_BOUNDARIES
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using LibreWinForms.Platform;
using LibreWinForms.ProGPU;
using ProGPU.Backend.Native;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    // These cases require the exact qualified native runtime/owned classifier.
    // Device-free transport controls live in ProGpuEditWordBoundaryCaptureTests.
    [Fact]
    public void PortableNativeEditWordBoundary_ActualProviderSnapshotDrivesOriginalSourceDrag()
        => RunNativeWordEditor((platform, owner, editor, probe) =>
        {
            editor.Multiline = false;
            editor.Text = "  alpha  beta  ";
            editor.Record();
            var generation = Assert.IsType<NativeEditWordLayoutProbe>(probe.Layouts.Last());
            LibreEditWordBoundaries first = generation.GetWordBoundaries();
            Assert.Equal(new[] { 0, 2, 9, 15 }, first.Positions.ToArray());
            Assert.Equal(2, first.LeadingContentStart);
            Assert.True(first.Positions.Equals(generation.GetWordBoundaries().Positions));
            using PasswordWordPointer input = new(platform, owner, editor);
            var point = ObservedWordPoint(probe, 3);
            input.Click(point, 1);
            input.Down(point, 2);
            AssertWordRange(editor, 2, 9);
            input.Drag(ObservedWordPoint(probe, 13));
            AssertWordRange(editor, 2, 15);
            input.Drag(point);
            AssertWordRange(editor, 2, 9);
            editor.Record();
            Assert.Same(generation, probe.Layouts.Last());
            editor.Text = "new generation";
            editor.Record();
            Assert.True(generation.Disposed);
            Assert.Throws<ObjectDisposedException>(() => generation.GetWordBoundaries());
            Assert.NotSame(generation, probe.Layouts.Last());
        });

    [Fact]
    public void PortableNativeEditWordBoundary_UnqualifiedSourceRejectsBeforeSelectionOrNotifications()
        => RunNativeWordEditor((platform, owner, editor, probe) =>
        {
            // Actual still-unqualified native property domain, never a forged
            // word inventory or a claim that all 82 unknown observations work.
            editor.Text = "a\u3200b ";
            editor.Record();
            using PasswordWordPointer input = new(platform, owner, editor);
            var point = ObservedWordPoint(probe, 0);
            input.Click(point, 1);
            int start = editor.SelectionStart;
            int length = editor.SelectionLength;
            int notifications = 0;
            editor.MouseDown += (_, _) => notifications++;
            var error = Assert.Throws<ProGpuEditWordBoundaryException>(() => input.Down(point, 2));
            Assert.Equal(NativeRendererStatus.Unsupported, error.Result.Status);
            Assert.Equal(NativeEditWordBoundaryError.UnqualifiedBmpSymbolPolicy, error.Result.ErrorCode);
            Assert.Equal(start, editor.SelectionStart);
            Assert.Equal(length, editor.SelectionLength);
            Assert.Equal(0, notifications);
        });

    [Fact]
    public void PortableNativeEditWordBoundary_PasswordNeverQueriesSourceInventory()
        => RunNativeWordEditor((platform, owner, editor, probe) =>
        {
            editor.PasswordChar = '*';
            editor.Text = "private source\u3200";
            editor.Record();
            Assert.Equal(new string('*', editor.TextLength), probe.LastText);
            using PasswordWordPointer input = new(platform, owner, editor);
            input.Click(new(3, 4), 1);
            input.Down(new(3, 4), 2);
            AssertWordRange(editor, 0, editor.TextLength);
            Assert.Equal(0, probe.Layouts.Cast<NativeEditWordLayoutProbe>().Sum(layout => layout.BoundaryQueries));
        });

    private static void RunNativeWordEditor(Action<HeadlessPlatform, Form, RetainedEditor, NativeEditWordRendererProbe> action,
        [CallerMemberName] string method = "")
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true, method: method)) return;
        const string marker = "LIBREWINFORMS_TEST_RETAINED_TEXT";
        string? previous = Environment.GetEnvironmentVariable(marker);
        Environment.SetEnvironmentVariable(marker, "native-edit-words");
        try
        {
            RunRetainedEditor((platform, owner, editor, probe)
                => action(platform, owner, editor, Assert.IsType<NativeEditWordRendererProbe>(probe)));
        }
        finally { Environment.SetEnvironmentVariable(marker, previous); }
    }

    // Only the explicit fixture declares source admission. The product service
    // remains marker-free until classifier, source geometry and runtime gates.
    private sealed class NativeEditWordRendererProbe : RetainedTextRendererProbe, ILibreEditWordBoundaryService
    {
        protected override RetainedLayoutProbe CreateLayoutProbe(ILibreTextLayout layout, string text)
            => new NativeEditWordLayoutProbe(layout);
    }

    private sealed class NativeEditWordLayoutProbe : RetainedLayoutProbe, ILibreEditWordBoundaryLayout
    {
        private readonly ILibreEditWordBoundaryLayout _words;

        internal NativeEditWordLayoutProbe(ILibreTextLayout layout) : base(layout)
            => _words = Assert.IsAssignableFrom<ILibreEditWordBoundaryLayout>(layout);

        internal int BoundaryQueries { get; private set; }

        public LibreEditWordBoundaries GetWordBoundaries()
        {
            BoundaryQueries++;
            return _words.GetWordBoundaries();
        }
    }
}
#endif
