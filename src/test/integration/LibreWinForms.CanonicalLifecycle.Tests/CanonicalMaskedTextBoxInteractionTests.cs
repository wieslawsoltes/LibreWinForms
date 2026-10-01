// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Numerics;
using System.Windows.Forms;
using LibreWinForms.Platform;
using LibreWinForms.ProGPU;
using ProGPU.Scene;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Fact]
    public void PortableMaskedInteractionSharesActualDisplayGeneration()
    {
        if (RunDpiCaseInNewProcess()) return;
        RunMaskedEditor((_, _, editor, probe) =>
        {
            foreach (MaskFormat format in Enum.GetValues<MaskFormat>())
            {
                editor.TextMaskFormat = format;
                editor.Select(3, 2);
                DrawingContext frame = editor.Record();
                MaskedLayoutProbe layout = probe.Layouts.Last();
                Assert.Equal("12-__", probe.LastText);
                Assert.Equal(3, editor.SelectionStart);
                Assert.Equal(2, editor.SelectionLength);
                Assert.False(layout.GetSelectionRectangles(3, 2).IsEmpty);
                Assert.Contains(SystemColors.HighlightText, layout.Colors);
                Assert.Equal(2, frame.Commands.Count(c => c.Type == RenderCommandType.DrawGlyphRun));
                int count = probe.Layouts.Count;
                editor.Record();
                Assert.Equal(count, probe.Layouts.Count);
                Assert.Equal(GraphicsUnit.Pixel, probe.LastFontUnit);
            }
        });
    }

    [Fact]
    public void PortableMaskedInteractionPointerDefaultsPrecedeNotifications()
    {
        if (RunDpiCaseInNewProcess()) return;
        RunMaskedEditor((_, _, editor, probe) =>
        {
            editor.Record();
            MaskedLayoutProbe layout = probe.Layouts.Last();
            editor.BeforeDown = _ => Assert.Equal(1, editor.SelectionStart);
            editor.MouseDown += (_, _) => { editor.Select(2, 1); editor.Capture = false; };
            editor.Press(MaskedCaretPoint(layout, 1));
            Assert.Equal(2, editor.SelectionStart);
            Assert.Equal(1, editor.SelectionLength);
            editor.Drag(MaskedCaretPoint(layout, 5));
            Assert.Equal(2, editor.SelectionStart);
            Assert.Equal(1, editor.SelectionLength);
            Assert.False(editor.Capture);
        });
    }

    [Fact]
    public void PortableMaskedInteractionDragPreservesSignedAnchorAndMaskEditing()
    {
        if (RunDpiCaseInNewProcess()) return;
        RunMaskedEditor((_, _, editor, probe) =>
        {
            editor.Record();
            MaskedLayoutProbe layout = probe.Layouts.Last();
            editor.Press(MaskedCaretPoint(layout, 4));
            editor.Drag(MaskedCaretPoint(layout, 1));
            Assert.Equal(1, editor.SelectionStart);
            Assert.Equal(3, editor.SelectionLength);
            editor.Cancel();
            editor.Select(4, -3);
            Assert.True(editor.Command(Keys.Shift | Keys.Right));
            Assert.Equal(2, editor.SelectionStart);
            Assert.Equal(2, editor.SelectionLength);
            editor.Select(3, 0);
            editor.Type('3');
            Assert.Equal("12-3_", editor.MaskedTextProvider!.ToDisplayString());
            Assert.Equal(4, editor.SelectionStart);
        });
    }

    [Fact]
    public void PortableMaskedInteractionProtectedNotificationsAndUserMouseDoNotSelect()
    {
        if (RunDpiCaseInNewProcess()) return;
        RunMaskedEditor((_, _, editor, probe) =>
        {
            editor.Record();
            Point point = MaskedCaretPoint(probe.Layouts.Last(), 3);
            editor.Select(0, 0);
            editor.NotifyDown(point);
            Assert.Equal(0, editor.SelectionStart);
            editor.SetUserMouse(true);
            editor.Press(point);
            Assert.Equal(0, editor.SelectionStart);
            editor.Cancel();
        });
    }

    [Fact]
    public void PortableMaskedInteractionAutomaticallyScrollsWholeFrameButRefusesPublicScroll()
    {
        if (RunDpiCaseInNewProcess()) return;
        RunMaskedEditor((_, _, editor, probe) =>
        {
            editor.Mask = new string('&', 80);
            editor.Text = new string('W', 80);
            editor.Select(80, 0);
            DrawingContext frame = editor.Record();
            MaskedLayoutProbe layout = probe.Layouts.Last();
            Assert.True(layout.LastOrigin.X < 0);
            LibreTextCaret caret = layout.GetCaret(80);
            Assert.InRange(caret.Position.X + layout.LastOrigin.X, 1, editor.ClientSize.Width - 2);
            Assert.DoesNotContain(frame.Commands, c => c.Type == RenderCommandType.PushClip);
            PointF origin = layout.LastOrigin;
            editor.ScrollToCaret();
            ((TextBoxBase)editor).ScrollToCaret();
            editor.Record();
            Assert.Equal(origin, layout.LastOrigin);
            int target = editor.GetCharIndexFromPosition(new((int)(caret.Position.X + origin.X), 4));
            Assert.InRange(target, 78, 79); // Public last-character policy excludes the terminal caret.
        });
    }

    [Fact]
    public void PortableMaskedInteractionPositionQueriesUseDisplayIndicesAndOriginalRowFrame()
    {
        if (RunDpiCaseInNewProcess()) return;
        RunMaskedEditor((_, _, editor, probe) =>
        {
            editor.TextMaskFormat = MaskFormat.ExcludePromptAndLiterals;
            editor.Record();
            MaskedLayoutProbe layout = probe.Layouts.Last();
            ILibreTextSourceGeometry geometry = layout;
            for (int i = 0; i < 5; i++)
            {
                Point expected = Point.Truncate(new(geometry.GetSourcePositionPoint(i).X + layout.LastOrigin.X, 0));
                Assert.Equal(expected, editor.GetPositionFromCharIndex(i));
            }

            Assert.Equal(Point.Empty, editor.GetPositionFromCharIndex(5));
            Assert.Equal("12", editor.Text);
            Assert.Equal("12-__", probe.LastText);
        });
    }

    [Fact]
    public void PortableMaskedInteractionFocusPromptAndReadonlyDisplayKeepSourceSelection()
    {
        if (RunDpiCaseInNewProcess()) return;
        RunMaskedEditor((_, owner, editor, probe) =>
        {
            editor.HidePromptOnLeave = true;
            editor.Select(5, -2);
            editor.Record();
            Assert.Equal("12-__", probe.LastText);
            using Button other = new();
            owner.Controls.Add(other);
            Assert.True(other.Focus());
            editor.Record();
            Assert.Equal("12-", probe.LastText);
            Assert.True(editor.Focus());
            editor.Record();
            Assert.Equal("12-__", probe.LastText);
            Assert.Equal(3, editor.SelectionStart);
            Assert.Equal(2, editor.SelectionLength);
            editor.ReadOnly = true;
            editor.Record();
            Assert.Equal("12-", probe.LastText);
            editor.ReadOnly = false;
            editor.Record();
            Assert.Equal("12-__", probe.LastText);
            Assert.Equal(3, editor.SelectionStart);
            Assert.Equal(2, editor.SelectionLength);
            editor.Select(5, 0);
            editor.ReadOnly = true;
            int caretQueries = probe.Layouts.Sum(layout => layout.CaretQueries);
            editor.Record();
            Assert.Equal(caretQueries, probe.Layouts.Sum(layout => layout.CaretQueries));
            editor.ReadOnly = false;
            editor.Record();
            Assert.Equal(5, editor.SelectionStart);
            Assert.Equal(0, editor.SelectionLength);
        });
    }

    [Fact]
    public void PortableMaskedInteractionPasswordProjectionRetainsUtf16Length()
    {
        if (RunDpiCaseInNewProcess()) return;
        RunMaskedEditor((_, _, editor, probe) =>
        {
            foreach (bool nullMask in new[] { false, true })
            {
                editor.Mask = nullMask ? "" : "00-00";
                editor.Text = nullMask ? "A🙂B" : "12";
                editor.PasswordChar = '*';
                editor.Record();
                Assert.Equal(nullMask ? "****" : "**-__", probe.LastText);
                int length = probe.LastText!.Length;
                editor.Select(length, 0);
                editor.Record();
                Assert.Equal(length, probe.Layouts.Last().GetCaret(length).TextPosition);
                editor.UseSystemPasswordChar = true;
                editor.Record();
                Assert.Equal(nullMask ? "●●●●" : "●●-__", probe.LastText);
                editor.UseSystemPasswordChar = false;
            }
        });
    }

    [Fact]
    public void PortableMaskedInteractionInvalidatesFontSizeDpiAndHandleGenerations()
    {
        if (RunDpiCaseInNewProcess()) return;
        RunMaskedEditor((_, _, editor, probe) =>
        {
            editor.Record();
            MaskedLayoutProbe prior = probe.Layouts.Last();
            editor.Width += 20;
            editor.Record();
            Assert.True(prior.Disposed);
            prior = probe.Layouts.Last();
            using Font font = new(FontFamily.GenericSansSerif, 17, GraphicsUnit.Pixel);
            editor.Font = font;
            Assert.True(prior.Disposed);
            editor.Record();
            prior = probe.Layouts.Last();
            editor.Record(144);
            Assert.True(prior.Disposed);
            Assert.Equal(17, probe.LastFontSize);
            prior = probe.Layouts.Last();
            editor.RecreateSourceHandle();
            Assert.True(prior.Disposed);
        });
    }

    [Fact]
    public void PortableMaskedInteractionRetainsQueryFrameThroughReentrantDisposal()
    {
        if (RunDpiCaseInNewProcess()) return;
        RunMaskedEditor((_, _, editor, probe) =>
        {
            editor.Record();
            MaskedLayoutProbe layout = probe.Layouts.Last();
            Point point = MaskedCaretPoint(layout, 3);
            int notifications = 0;
            editor.MouseDown += (_, _) => notifications++;
            layout.AfterHit = () =>
            {
                editor.Dispose();
                Assert.False(layout.Disposed, "the provider callback still owns its exact retained query frame");
            };
            editor.Press(point);
            Assert.True(layout.Disposed);
            Assert.Equal(0, notifications);
        });
    }

    [Fact]
    public void PortableMaskedInteractionRejectsReplacedCandidateBeforePointerPublication()
    {
        if (RunDpiCaseInNewProcess()) return;
        RunMaskedEditor((_, owner, editor, probe) =>
        {
            editor.Record();
            Point point = MaskedCaretPoint(probe.Layouts.Last(), 3);
            editor.Width += 1;
            probe.AfterCreate = () => { probe.AfterCreate = null; editor.Mask = "000-000"; editor.Select(1, 0); };
            int notifications = 0;
            editor.MouseDown += (_, _) => notifications++;
            editor.Press(point);
            Assert.True(probe.Layouts.Last().Disposed);
            Assert.Equal(1, editor.SelectionStart);
            Assert.Equal(0, notifications);
            editor.Cancel();

            // Focus loss with unchanged display still retires the exact source
            // frame, independently of display equality and provider callbacks.
            using Button other = new();
            owner.Controls.Add(other);
            Assert.True(editor.Focus());
            editor.Record();
            point = MaskedCaretPoint(probe.Layouts.Last(), 2);
            editor.Width += 1;
            probe.AfterCreate = () => { probe.AfterCreate = null; Assert.True(other.Focus()); };
            editor.Press(point);
            Assert.True(probe.Layouts.Last().Disposed);
            Assert.Equal(0, notifications);
            editor.Cancel();

            Assert.True(editor.Focus());
            editor.Record();
            MaskedLayoutProbe frame = probe.Layouts.Last();
            frame.AfterHit = () => { frame.AfterHit = null; Assert.True(other.Focus()); Assert.False(frame.Disposed); };
            editor.Press(MaskedCaretPoint(frame, 2));
            Assert.True(frame.Disposed);
            Assert.Equal(0, notifications);
            editor.Cancel();

            Assert.True(editor.Focus());
            editor.Record();
            frame = probe.Layouts.Last();
            editor.Select(1, 2);
            frame.Colors.Clear();
            frame.AfterDraw = () => { frame.AfterDraw = null; Assert.True(other.Focus()); Assert.False(frame.Disposed); };
            editor.Record();
            Assert.Single(frame.Colors); // No obsolete selected-foreground draw tail.
            Assert.True(frame.Disposed);

            Assert.True(editor.Focus());
            editor.Record();
            frame = probe.Layouts.Last();
            frame.BeforeDispose = () =>
            {
                frame.BeforeDispose = null;
                // Cleanup runs after the complete original mask mutation. It
                // may publish another source owner, which the old setter cannot
                // overwrite when its retirement callback returns.
                Assert.Equal("00-00", editor.Mask);
                editor.Mask = "&&&&";
                editor.Text = "done";
                editor.Select(3, 0);
            };
            editor.Mask = "00-00";
            Assert.Equal("&&&&", editor.Mask);
            Assert.Equal("done", editor.Text);
            Assert.Equal(3, editor.SelectionStart);
            Assert.True(frame.Disposed);
            editor.Record();
            Assert.Equal("done", probe.LastText);
        });
    }

    [Fact]
    public void PortableMaskedInteractionRetainsDrawFrameAndPreservesPrimaryFailure()
    {
        if (RunDpiCaseInNewProcess()) return;
        RunMaskedEditor((_, _, editor, probe) =>
        {
            editor.Record();
            MaskedLayoutProbe layout = probe.Layouts.Last();
            InvalidOperationException primary = new("original draw failure");
            InvalidOperationException cleanup = new("original cleanup failure");
            layout.BeforeDispose = () => { layout.BeforeDispose = null; throw cleanup; };
            layout.AfterDraw = () =>
            {
                layout.AfterDraw = null;
                editor.Dispose();
                Assert.False(layout.Disposed);
                throw primary;
            };
            Assert.Same(primary, Assert.Throws<InvalidOperationException>(() => editor.Record()));
            Assert.Same(cleanup, primary.Data["PortableRetainedTextCleanup"]);
            Assert.False(layout.Disposed);
            editor.Dispose();
            Assert.True(layout.Disposed);
            Assert.Equal(2, layout.DisposalAttempts);
        });
    }

    private static void RunMaskedEditor(Action<HeadlessPlatform, Form, RetainedMaskedEditor, MaskedRendererProbe> action)
    {
        MaskedRendererProbe probe = new();
        HeadlessPlatform platform = new(autoCloseWindows: false, textRenderer: probe);
        LibrePlatform.Register(platform.Services);
        platform.BorderSizeValue = new(1, 1);
        using Form owner = new() { ShowIcon = false, AutoScaleMode = AutoScaleMode.None };
        using RetainedMaskedEditor editor = new(platform, owner) { Bounds = new(8, 8, 240, 30), Mask = "00-00", Text = "12" };
        owner.Controls.Add(editor);
        owner.Shown += (_, _) => platform.Post(() =>
        {
            try
            {
                platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
                Assert.True(editor.Focus());
                action(platform, owner, editor, probe);
            }
            finally { owner.Close(); }
        });
        Application.Run(owner);
        Assert.All(probe.Layouts, layout => Assert.True(layout.Disposed));
    }

    private static Point MaskedCaretPoint(MaskedLayoutProbe layout, int position)
    {
        LibreTextCaret caret = layout.GetCaret(position);
        return Point.Round(new(caret.Position.X + layout.LastOrigin.X,
            caret.Position.Y + layout.LastOrigin.Y + caret.Height / 2));
    }

    private sealed class RetainedMaskedEditor(HeadlessPlatform platform, Form owner) : MaskedTextBox
    {
        internal Action<MouseEventArgs>? BeforeDown { get; set; }
        internal DrawingContext Record(float dpi = 96)
        {
            DrawingContext frame = new();
            using Graphics graphics = Graphics.FromProGpuDrawingContext(frame,
                new(0, 0, Width, Height), Matrix4x4.Identity, dpi, dpi);
            OnPaint(new(graphics, ClientRectangle));
            return frame;
        }

        internal void Press(Point point) => Send(LibreInputEventKind.PointerDown, point);
        internal void Drag(Point point) => Send(LibreInputEventKind.PointerMove, point);
        internal void Cancel() => Send(LibreInputEventKind.PointerCancel, Point.Empty);
        internal void SetUserMouse(bool enabled) => SetStyle(ControlStyles.UserMouse, enabled);
        internal void NotifyDown(Point point) => OnMouseDown(new(MouseButtons.Left, 1, point.X, point.Y, 0));
        internal void RecreateSourceHandle() => RecreateHandle();
        internal bool Command(Keys key)
        {
            Message message = Message.Create(Handle, 0x100, (nint)key, 0);
            return ProcessCmdKey(ref message, key);
        }

        internal void Type(char character) => platform.SendControlInput(owner,
            new(LibreInputEventKind.TextInput, 1, LibreInputModifiers.None, LibreKey.Unknown,
                character.ToString(), default, default, LibrePointerButton.None));
        protected override void OnMouseDown(MouseEventArgs e) { BeforeDown?.Invoke(e); base.OnMouseDown(e); }
        private void Send(LibreInputEventKind kind, Point point)
        {
            Point position = owner.PointToClient(PointToScreen(point));
            platform.SendControlInput(owner, new(kind, 1, LibreInputModifiers.None, LibreKey.Unknown, null,
                new(position.X, position.Y), default, LibrePointerButton.Primary));
        }
    }

    private sealed class MaskedRendererProbe : ILibreTextRendererService, ILibreTextSourceGeometryService
    {
        private readonly ProGpuTextRendererService _renderer = new();
        internal List<MaskedLayoutProbe> Layouts { get; } = [];
        internal Action? AfterCreate { get; set; }
        internal string? LastText { get; private set; }
        internal GraphicsUnit LastFontUnit { get; private set; }
        internal float LastFontSize { get; private set; }
        public ILibreTextLayout CreateLayout(Graphics graphics, string text, Font font, Size size, LibreTextFormat format)
        {
            LastText = text;
            LastFontUnit = font.Unit;
            LastFontSize = font.Size;
            MaskedLayoutProbe layout = new(_renderer.CreateLayout(graphics, text, font, size, format));
            Layouts.Add(layout);
            AfterCreate?.Invoke();
            return layout;
        }

        public void DrawText(Graphics graphics, string text, Font? font, Rectangle bounds, Color foreground, Color background, LibreTextFormat format)
            => _renderer.DrawText(graphics, text, font, bounds, foreground, background, format);
        public Size MeasureText(Graphics? graphics, string text, Font? font, Size size, LibreTextFormat format)
            => _renderer.MeasureText(graphics, text, font, size, format);
    }

    private sealed class MaskedLayoutProbe(ILibreTextLayout layout) : ILibreTextLayout, ILibreTextSourceGeometry, ILibreTextRowNavigation
    {
        internal List<Color> Colors { get; } = [];
        internal bool Disposed { get; private set; }
        internal int DisposalAttempts { get; private set; }
        internal int CaretQueries { get; private set; }
        internal PointF LastOrigin { get; private set; }
        internal Action? AfterHit { get; set; }
        internal Action? AfterDraw { get; set; }
        internal Action? BeforeDispose { get; set; }
        public SizeF ContentSize => layout.ContentSize;
        public int RowCount => ((ILibreTextSourceGeometry)layout).RowCount;
        public int GetRowSourceStart(int index) => ((ILibreTextSourceGeometry)layout).GetRowSourceStart(index);
        public int GetRowIndexFromTextPosition(int index) => ((ILibreTextSourceGeometry)layout).GetRowIndexFromTextPosition(index);
        public int GetCaretRowIndex(int position, bool trailing) => ((ILibreTextSourceGeometry)layout).GetCaretRowIndex(position, trailing);
        public PointF GetSourcePositionPoint(int position) => ((ILibreTextSourceGeometry)layout).GetSourcePositionPoint(position);
        public LibreTextCaret GetCaret(int position, bool trailing = false)
        {
            CaretQueries++;
            return layout.GetCaret(position, trailing);
        }

        public LibreTextCaret MoveCaret(int position, bool trailing, int direction) => layout.MoveCaret(position, trailing, direction);
        public LibreTextCaret GetRowBoundary(int position, bool trailing, bool end)
            => ((ILibreTextRowNavigation)layout).GetRowBoundary(position, trailing, end);
        public LibreTextCaret MoveCaretVertically(int position, bool trailing, int direction, float preferredX)
            => ((ILibreTextRowNavigation)layout).MoveCaretVertically(position, trailing, direction, preferredX);
        public LibreTextHit HitTest(PointF point)
        {
            LibreTextHit hit = layout.HitTest(point);
            AfterHit?.Invoke();
            return hit;
        }

        public ReadOnlyMemory<RectangleF> GetSelectionRectangles(int start, int length) => layout.GetSelectionRectangles(start, length);
        public void Draw(Graphics graphics, PointF origin, Color color)
        {
            Assert.False(Disposed);
            Colors.Add(color);
            LastOrigin = origin;
            layout.Draw(graphics, origin, color);
            AfterDraw?.Invoke();
        }

        public void Dispose()
        {
            DisposalAttempts++;
            BeforeDispose?.Invoke();
            Assert.False(Disposed);
            layout.Dispose();
            Disposed = true;
        }
    }
}
