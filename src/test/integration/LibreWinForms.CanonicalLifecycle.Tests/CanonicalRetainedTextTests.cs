// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Numerics;
using System.Windows.Forms;
using FluentAssertions;
using LibreWinForms.Platform;
using LibreWinForms.ProGPU;
using ProGPU.Scene;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Fact]
    public void PortableRetainedTextHardBreakRowsHaveCaretsButNoDrawableGlyphs()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            editor.Text = "\r\n\r\n";
            editor.Select(0, 0);
            DrawingContext context = editor.Record();
            context.Commands.Should().NotContain(c => c.Type == RenderCommandType.DrawGlyphRun);
            RetainedLayoutProbe layout = probe.Layouts.Last();
            float height = layout.GetCaret(0).Height;
            for (int row = 0; row < 3; row++)
            {
                LibreTextCaret caret = layout.GetCaret(row * 2);
                caret.TextPosition.Should().Be(row * 2);
                caret.Position.Y.Should().Be(row * height);
                caret.Height.Should().Be(height);
            }

            editor.Select(4, 0);
            DrawingContext selected = editor.Record();
            RenderCommand caretInk = selected.Commands.Single(c => c.Type == RenderCommandType.DrawRect);
            caretInk.Rect.Y.Should().Be(2 * height);
            caretInk.Rect.Height.Should().Be(height);
        });
    }

    [Fact]
    public void PortableRetainedTextHardBreakPointerSelectionKeepsSourceIndices()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            editor.Text = "a\r\n\r\nb";
            editor.Select(0, 0);
            editor.Record();
            float height = probe.Layouts.Last().GetCaret(0).Height;
            editor.Press(new Point(100, (int)(height * 1.5f)));
            editor.SelectionStart.Should().Be(3);
            editor.SelectionLength.Should().Be(0);
            editor.Drag(new Point(0, (int)(height * .5f)));
            editor.SelectionStart.Should().Be(0);
            editor.SelectionLength.Should().Be(3);
            editor.Text.Should().Be("a\r\n\r\nb");
            editor.Capture = false;
        });
    }

    [Fact]
    public void PortableRetainedTextHardBreakEnterCreatesOneTrailingRow()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((platform, owner, editor, probe) =>
        {
            editor.AcceptsReturn = true;
            editor.Text = "a";
            editor.Select(1, 0);
            int presses = 0;
            editor.KeyPress += (_, e) => { if (e.KeyChar == '\r') presses++; };
            SendDropdownKey(platform, owner, LibreKey.Enter, release: false);
            editor.Text.Should().Be("a\r\n", "physical Enter must not depend on a host control-character callback");
            SendDropdownText(platform, owner, "\r");
            SendDropdownKeyUp(platform, owner, LibreKey.Enter);
            editor.Text.Should().Be("a\r\n");
            presses.Should().Be(1);
            editor.SelectionStart.Should().Be(3);
            editor.Record();
            RetainedLayoutProbe layout = probe.Layouts.Last();
            LibreTextCaret caret = layout.GetCaret(3);
            caret.TextPosition.Should().Be(3);
            caret.Position.Y.Should().Be(layout.GetCaret(0).Height);
            SendDropdownText(platform, owner, "x");
            editor.Text.Should().Be("a\r\nx");
            editor.ReadOnly = true;
            SendDropdownKey(platform, owner, LibreKey.Enter);
            presses.Should().Be(2);
            editor.Text.Should().Be("a\r\nx");
            editor.ReadOnly = false;
            editor.KeyDown += (_, e) => e.SuppressKeyPress = true;
            SendDropdownKey(platform, owner, LibreKey.Enter);
            presses.Should().Be(2);
            editor.Text.Should().Be("a\r\nx");
        });
    }

    [Fact]
    public void PortableRetainedTextReusesLayoutForSelectionAndPaint()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            editor.Text = "source text";
            editor.Select(1, 3);
            DrawingContext first = editor.Record();
            int count = probe.Layouts.Count;
            RetainedLayoutProbe layout = probe.Layouts.Last();
            editor.Record();
            probe.Layouts.Count.Should().Be(count);
            layout.Colors.Should().Contain(SystemColors.HighlightText);
            layout.GetSelectionRectangles(1, 3).IsEmpty.Should().BeFalse();
            first.Commands.Count(c => c.Type == RenderCommandType.DrawGlyphRun).Should().Be(2);
            probe.LastFontUnit.Should().Be(GraphicsUnit.Pixel);
            editor.Select(2, 0);
            layout.Colors.Clear();
            editor.Record();
            probe.Layouts.Count.Should().Be(count);
            layout.Colors.Should().Equal(editor.ForeColor);
        });
    }

    [Fact]
    public void PortableRetainedTextInvalidatesTextSizeFontAndTargetDpi()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            editor.Text = "first";
            editor.Record();
            RetainedLayoutProbe prior = probe.Layouts.Last();
            editor.Text = "replacement";
            prior.Disposed.Should().BeTrue();
            editor.Record();
            prior = probe.Layouts.Last();
            editor.Width += 20;
            editor.Record();
            prior.Disposed.Should().BeTrue();
            prior = probe.Layouts.Last();
            using Font changed = new(FontFamily.GenericSansSerif, 17, GraphicsUnit.Pixel);
            editor.Font = changed;
            prior.Disposed.Should().BeTrue();
            editor.Record();
            prior = probe.Layouts.Last();
            editor.Record(144);
            prior.Disposed.Should().BeTrue();
            probe.LastFontSize.Should().Be(17, "source pixel fonts are not scaled again by target DPI");
        });
    }

    [Fact]
    public void PortableRetainedTextVisualKeysPreserveSignedSelectionAnchor()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((platform, owner, editor, _) =>
        {
            editor.Text = "abcd";
            editor.Select(2, 0);
            SendRetainedKey(platform, owner, LibreKey.Left, LibreInputModifiers.Shift);
            editor.SelectionStart.Should().Be(1);
            editor.SelectionLength.Should().Be(1);
            SendRetainedKey(platform, owner, LibreKey.Left, LibreInputModifiers.Shift);
            editor.SelectionStart.Should().Be(0);
            editor.SelectionLength.Should().Be(2);
            SendRetainedKey(platform, owner, LibreKey.Right, LibreInputModifiers.Shift);
            editor.SelectionStart.Should().Be(1);
            editor.SelectionLength.Should().Be(1);
            SendRetainedKey(platform, owner, LibreKey.Right, LibreInputModifiers.None);
            editor.SelectionStart.Should().Be(2);
            editor.SelectionLength.Should().Be(0);
            editor.Text.Should().Be("abcd");
            editor.KeyDown += (_, e) => e.Handled = true;
            SendRetainedKey(platform, owner, LibreKey.Right, LibreInputModifiers.None);
            editor.SelectionStart.Should().Be(2, "public handlers retain precedence over default navigation");
        });
    }

    [Fact]
    public void PortableRetainedTextVisualKeysDoNotSplitCombiningClusters()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((platform, owner, editor, _) =>
        {
            editor.Text = "a\u0301b";
            editor.Select(0, 0);
            SendRetainedKey(platform, owner, LibreKey.Right, LibreInputModifiers.None);
            editor.SelectionStart.Should().Be(2);
            SendRetainedKey(platform, owner, LibreKey.Right, LibreInputModifiers.None);
            editor.SelectionStart.Should().Be(3);
            SendRetainedKey(platform, owner, LibreKey.Left, LibreInputModifiers.None);
            editor.SelectionStart.Should().Be(2);
        });
    }

    [Fact]
    public void PortableRetainedTextReadOnlyNavigationDoesNotEdit()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((platform, owner, editor, _) =>
        {
            editor.Text = "read only";
            editor.ReadOnly = true;
            editor.Select(0, 0);
            int edits = 0;
            editor.TextChanged += (_, _) => edits++;
            SendRetainedKey(platform, owner, LibreKey.Right, LibreInputModifiers.Shift);
            editor.SelectionLength.Should().Be(1);
            SendDropdownText(platform, owner, "x");
            editor.Text.Should().Be("read only");
            edits.Should().Be(0);
        });
    }

    [Fact]
    public void PortableRetainedTextPointerUsesSameLayoutAndReleasesCapture()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            editor.Text = "wide text";
            editor.Select(0, 0);
            editor.Record();
            RetainedLayoutProbe layout = probe.Layouts.Last();
            Point start = Point.Round(layout.GetCaret(2).Position);
            Point end = Point.Round(layout.GetCaret(6).Position);
            editor.Press(start);
            editor.SelectionStart.Should().Be(2);
            editor.Capture.Should().BeTrue();
            editor.Drag(end);
            editor.SelectionStart.Should().Be(2);
            editor.SelectionLength.Should().Be(4);
            editor.Capture = false;
            editor.Drag(start);
            editor.SelectionLength.Should().Be(4);
            probe.Layouts.Last().Should().BeSameAs(layout);
        });
    }

    [Fact]
    public void PortableRetainedTextPasswordNeverPassesSourceToLayout()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            editor.PasswordChar = '*';
            editor.Text = "secret";
            editor.Select(1, 2);
            editor.Record();
            probe.LastText.Should().Be("******");
            editor.Text.Should().Be("secret");
            probe.Layouts.Last().GetCaret(6).TextPosition.Should().Be(6);
        });
    }

    [Fact]
    public void PortableRetainedTextScrollKeepsTheSourceViewportFixed()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            editor.WordWrap = false;
            editor.Text = new string('W', 80);
            editor.Select(editor.TextLength, 0);
            DrawingContext context = editor.Record();
            RetainedLayoutProbe layout = probe.Layouts.Last();
            layout.LastOrigin.X.Should().BeLessThan(0);
            float caretX = layout.GetCaret(editor.TextLength).Position.X + layout.LastOrigin.X;
            caretX.Should().BeInRange(0, editor.ClientSize.Width - 1);
            context.Commands.Should().NotContain(c => c.Type == RenderCommandType.PushClip,
                "the scrolling paragraph must not add a translated layout rectangle clip");
            RenderCommand[] clips = context.Commands.Where(c => c.Type == RenderCommandType.PushGeometryClip).ToArray();
            clips.Should().NotBeEmpty();
            foreach (RenderCommand clip in clips)
            {
                clip.Path.Should().NotBeNull();
                clip.Path!.TryGetBounds(out Vector2 min, out Vector2 max).Should().BeTrue();
                min.Should().Be(Vector2.Zero);
                max.Should().Be(new Vector2(editor.ClientSize.Width, editor.ClientSize.Height),
                    "the paragraph offset must not translate or shrink the source viewport");
            }
        });
    }

    [Fact]
    public void PortableRetainedTextDisposalDuringMouseCallbackRetiresLayout()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            editor.Text = "dispose";
            editor.Record();
            RetainedLayoutProbe layout = probe.Layouts.Last();
            int count = probe.Layouts.Count;
            editor.MouseDown += (_, _) => editor.Dispose();
            editor.Press(Point.Empty);
            editor.IsDisposed.Should().BeTrue();
            layout.Disposed.Should().BeTrue();
            probe.Layouts.Count.Should().Be(count);
        });
    }

    private static void SendRetainedKey(HeadlessPlatform platform, Form owner, LibreKey key, LibreInputModifiers modifiers)
    {
        platform.SendControlInput(owner, new LibreInputEvent(LibreInputEventKind.KeyDown,
            1, modifiers, key, null, default, default, LibrePointerButton.None));
        platform.SendControlInput(owner, new LibreInputEvent(LibreInputEventKind.KeyUp,
            1, modifiers, key, null, default, default, LibrePointerButton.None));
    }

    private static void RunRetainedEditor(Action<HeadlessPlatform, Form, RetainedEditor, RetainedTextRendererProbe> action)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        var probe = (RetainedTextRendererProbe)platform.Services.TextRenderer;
        using Form owner = new() { ShowIcon = false, AutoScaleMode = AutoScaleMode.None };
        using RetainedEditor editor = new() { Multiline = true, Bounds = new(8, 8, 240, 80) };
        owner.Controls.Add(editor);
        owner.Shown += (_, _) => platform.Post(() =>
        {
            try
            {
                platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
                editor.Focus().Should().BeTrue();
                action(platform, owner, editor, probe);
            }
            finally { owner.Close(); }
        });
        Application.Run(owner);
        probe.Layouts.Should().OnlyContain(layout => layout.Disposed);
    }

    private sealed class RetainedEditor : TextBox
    {
        internal DrawingContext Record(float dpi = 96)
        {
            DrawingContext context = new();
            using Graphics graphics = Graphics.FromProGpuDrawingContext(context,
                new(0, 0, Width, Height), Matrix4x4.Identity, dpi, dpi);
            OnPaint(new PaintEventArgs(graphics, ClientRectangle));
            return context;
        }

        internal void Press(Point point) => OnMouseDown(new(MouseButtons.Left, 1, point.X, point.Y, 0));
        internal void Drag(Point point) => OnMouseMove(new(MouseButtons.Left, 0, point.X, point.Y, 0));
    }

    private sealed class RetainedTextRendererProbe : ILibreTextRendererService, ILibreTextRowNavigationService
    {
        private readonly ProGpuTextRendererService _renderer = new();
        internal List<RetainedLayoutProbe> Layouts { get; } = [];
        internal string? LastText { get; private set; }
        internal GraphicsUnit LastFontUnit { get; private set; }
        internal float LastFontSize { get; private set; }

        public ILibreTextLayout CreateLayout(Graphics graphics, string text, Font font, Size size, LibreTextFormat format)
        {
            LastText = text;
            LastFontUnit = font.Unit;
            LastFontSize = font.Size;
            var layout = new RetainedLayoutProbe(_renderer.CreateLayout(graphics, text, font, size, format));
            Layouts.Add(layout);
            return layout;
        }

        public void DrawText(Graphics graphics, string text, Font? font, Rectangle bounds, Color foreColor, Color backColor, LibreTextFormat format)
            => _renderer.DrawText(graphics, text, font, bounds, foreColor, backColor, format);
        public Size MeasureText(Graphics? graphics, string text, Font? font, Size size, LibreTextFormat format)
            => _renderer.MeasureText(graphics, text, font, size, format);
    }

    private sealed class RetainedLayoutProbe(ILibreTextLayout layout) : ILibreTextLayout, ILibreTextRowNavigation
    {
        internal bool Disposed { get; private set; }
        internal PointF LastOrigin { get; private set; }
        internal List<Color> Colors { get; } = [];
        public SizeF ContentSize => layout.ContentSize;
        public LibreTextCaret GetCaret(int position, bool trailing = false) => layout.GetCaret(position, trailing);
        public LibreTextCaret MoveCaret(int position, bool trailing, int direction) => layout.MoveCaret(position, trailing, direction);
        public LibreTextCaret GetRowBoundary(int position, bool trailing, bool end)
            => ((ILibreTextRowNavigation)layout).GetRowBoundary(position, trailing, end);
        public LibreTextCaret MoveCaretVertically(int position, bool trailing, int direction, float preferredX)
            => ((ILibreTextRowNavigation)layout).MoveCaretVertically(position, trailing, direction, preferredX);
        public LibreTextHit HitTest(PointF point) => layout.HitTest(point);
        public ReadOnlyMemory<RectangleF> GetSelectionRectangles(int start, int length) => layout.GetSelectionRectangles(start, length);
        public void Draw(Graphics graphics, PointF origin, Color color)
        {
            Disposed.Should().BeFalse();
            Colors.Add(color);
            LastOrigin = origin;
            layout.Draw(graphics, origin, color);
        }

        public void Dispose()
        {
            Disposed.Should().BeFalse("each captured generation is retired exactly once");
            Disposed = true;
            layout.Dispose();
        }
    }
}
