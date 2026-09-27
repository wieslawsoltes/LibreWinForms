// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;
using FluentAssertions;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Theory]
    [InlineData(false, BorderStyle.None, 0)]
    [InlineData(true, BorderStyle.None, 0)]
    [InlineData(false, BorderStyle.FixedSingle, 1)]
    [InlineData(true, BorderStyle.FixedSingle, 1)]
    [InlineData(false, BorderStyle.Fixed3D, 2)]
    [InlineData(true, BorderStyle.Fixed3D, 2)]
    public void PortableListBoxRowsUseActualFontAndSameHitGeometry(bool createHandle, BorderStyle border, int inset)
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using Font font = new(FontFamily.GenericSansSerif, 17);
        using ListBox list = new() { Font = font, BorderStyle = border, IntegralHeight = false };
        list.Size = new Size(150, font.Height * 3 + 2 * inset);
        list.Items.AddRange(["one", "two", "three", "four", "five"]);
        if (createHandle)
            _ = list.Handle;

        list.ItemHeight.Should().Be(font.Height);
        list.GetItemHeight(4).Should().Be(font.Height);
        list.TopIndex = 1;
        Rectangle first = list.GetItemRectangle(1);
        first.Should().Be(new Rectangle(inset, inset, 150 - 2 * inset, font.Height));
        list.IndexFromPoint(first.Left, first.Top).Should().Be(1);
        list.IndexFromPoint(first.Right - 1, first.Bottom - 1).Should().Be(1);
        list.IndexFromPoint(first.Left, first.Bottom).Should().Be(2);
        list.IndexFromPoint(-1, first.Top).Should().Be(ListBox.NoMatches);
        list.IndexFromPoint(first.Right, first.Top).Should().Be(ListBox.NoMatches);
        list.GetItemRectangle(0).Should().Be(new Rectangle(inset, inset - font.Height, 150 - 2 * inset, font.Height));
        list.IsHandleCreated.Should().Be(createHandle);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableListBoxOffscreenRectanglesRemainUnclippedAndDoNotBecomeHits(bool createHandle)
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using ListBox list = new() { BorderStyle = BorderStyle.None, IntegralHeight = false };
        int height = list.Font.Height;
        list.Size = new Size(120, height * 2);
        list.Items.AddRange(["one", "two", "three", "four", "five"]);
        if (createHandle)
            _ = list.Handle;
        list.TopIndex = 1;

        list.GetItemRectangle(0).Should().Be(new Rectangle(0, -height, 120, height));
        list.GetItemRectangle(3).Should().Be(new Rectangle(0, height * 2, 120, height));
        list.GetItemRectangle(4).Should().Be(new Rectangle(0, height * 3, 120, height));
        list.IndexFromPoint(1, -1).Should().Be(ListBox.NoMatches);
        list.IndexFromPoint(1, height * 2).Should().Be(ListBox.NoMatches);

        list.Height += height / 2;
        list.GetItemRectangle(3).Should().Be(new Rectangle(0, height * 2, 120, height));
        list.IndexFromPoint(1, height * 2).Should().Be(3);
        Action negativeIndex = () => list.GetItemRectangle(-1);
        Action pastEnd = () => list.GetItemRectangle(list.Items.Count);
        negativeIndex.Should().Throw<ArgumentOutOfRangeException>();
        pastEnd.Should().Throw<ArgumentOutOfRangeException>();
        list.IsHandleCreated.Should().Be(createHandle);
    }

    [Fact]
    public void PortableListBoxFontMutationChangesBothRowsAndHits()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using ListBox list = new() { BorderStyle = BorderStyle.None, IntegralHeight = false, Size = new Size(160, 150) };
        list.Items.AddRange(["one", "two", "three"]);
        _ = list.Handle;
        using Font font = new(FontFamily.GenericSansSerif, 23);
        list.Font = font;
        list.ItemHeight.Should().Be(font.Height);
        list.GetItemRectangle(1).Y.Should().Be(font.Height);
        list.IndexFromPoint(2, font.Height).Should().Be(1);
    }

    [Theory]
    [InlineData(RightToLeft.No)]
    [InlineData(RightToLeft.Yes)]
    public void PortableListBoxPaintsOnlyIntersectingSourceRowsAndRestoresClip(RightToLeft direction)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using PaintableListBox list = new() { BorderStyle = BorderStyle.None, RightToLeft = direction };
        list.Items.AddRange(["first &🙂", "second אב", "third", "fourth"]);
        list.Size = new Size(140, list.Font.Height * 2);
        list.TopIndex = 1;
        list.SelectedIndex = 1;
        using Bitmap target = new(160, 120);
        using Graphics graphics = Graphics.FromImage(target);
        graphics.SetClip(new Rectangle(3, 1, 130, list.Font.Height));
        RectangleF oldClip = graphics.ClipBounds;
        bool painted = false;
        list.Paint += (_, _) =>
        {
            graphics.ClipBounds.Should().Be(oldClip);
            painted = true;
        };
        platform.TextDrawStrings.Clear();

        list.PaintContent(graphics, list.GetItemRectangle(1));

        platform.TextDrawStrings.Should().Equal("second אב");
        platform.LastTextBounds.Should().Be(list.GetItemRectangle(1));
        platform.LastTextFormat.Should().Be(LibreTextFormat.NoPrefix | LibreTextFormat.NoPadding
            | LibreTextFormat.SingleLine | LibreTextFormat.ExpandTabs
            | (direction == RightToLeft.Yes ? LibreTextFormat.RightToLeft | LibreTextFormat.Right : 0));
        graphics.ClipBounds.Should().Be(oldClip);
        painted.Should().BeTrue();
        list.Items[1].Should().Be("second אב");
    }

    [Fact]
    public void PortableListBoxFormattingMutationDoesNotPaintRetiredRows()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using PaintableListBox list = new() { BorderStyle = BorderStyle.None, FormattingEnabled = true };
        list.Items.AddRange(["one", "two"]);
        list.Format += (_, _) => list.Items.Clear();
        using Bitmap target = new(160, 120);
        using Graphics graphics = Graphics.FromImage(target);
        platform.TextDrawStrings.Clear();
        list.PaintContent(graphics, list.ClientRectangle);
        platform.TextDrawStrings.Should().BeEmpty();
        list.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(LibreKey.Up, 4)]
    [InlineData(LibreKey.Down, 6)]
    [InlineData(LibreKey.Home, 0)]
    [InlineData(LibreKey.End, 11)]
    [InlineData(LibreKey.PageUp, 3)]
    [InlineData(LibreKey.PageDown, 7)]
    public void PortableListBoxTypedKeysUseCanonicalSelectionAndRevealRow(LibreKey key, int expected)
    {
        RunPortableListBox((platform, owner, list) =>
        {
            list.SelectedIndex = 5;
            List<string> notifications = [];
            list.KeyDown += (_, _) => notifications.Add("key");
            list.SelectedIndexChanged += (_, _) => notifications.Add($"selection:{list.SelectedIndex}");
            SendDropdownKey(platform, owner, key);
            list.SelectedIndex.Should().Be(expected);
            list.SelectedItems.Count.Should().Be(1);
            list.SelectedItems[0].Should().Be(list.Items[expected]);
            list.SelectedIndices.Count.Should().Be(1);
            list.SelectedIndices[0].Should().Be(expected);
            list.GetSelected(expected).Should().BeTrue();
            list.GetItemRectangle(expected).IntersectsWith(list.ClientRectangle).Should().BeTrue();
            notifications.Should().Equal("key", $"selection:{expected}");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableListBoxFilteredOrHandledKeyCannotMutateSelection(bool filter)
    {
        RunPortableListBox((platform, owner, list) =>
        {
            list.SelectedIndex = 2;
            int changes = 0;
            int keys = 0;
            list.SelectedIndexChanged += (_, _) => changes++;
            CallbackKeyboardFilter inputFilter = new(message => filter && message.Msg == 0x100);
            list.KeyDown += (_, e) =>
            {
                keys++;
                e.Handled = true;
            };
            Application.AddMessageFilter(inputFilter);
            try
            {
                SendDropdownKey(platform, owner, LibreKey.Down);
            }
            finally
            {
                Application.RemoveMessageFilter(inputFilter);
            }
            list.SelectedIndex.Should().Be(2);
            changes.Should().Be(0);
            keys.Should().Be(filter ? 0 : 1);
        });
    }

    [Theory]
    [InlineData(LibrePointerButton.Primary, 1)]
    [InlineData(LibrePointerButton.Secondary, -1)]
    public void PortableListBoxTypedPointerChangesSelectionBeforeCanonicalMouseEvents(LibrePointerButton button, int expected)
    {
        RunPortableListBox((platform, owner, list) =>
        {
            List<string> notifications = [];
            list.SelectedIndex = -1;
            list.SelectedIndexChanged += (_, _) => notifications.Add("selection");
            list.MouseDown += (_, _) => notifications.Add($"down:{list.SelectedIndex}");
            list.MouseUp += (_, _) => notifications.Add($"up:{list.SelectedIndex}");
            Rectangle row = list.GetItemRectangle(1);
            Point point = owner.PointToClient(list.PointToScreen(new Point(row.X + 2, row.Y + 2)));
            foreach (LibreInputEventKind kind in new[] { LibreInputEventKind.PointerDown, LibreInputEventKind.PointerUp })
                platform.SendControlInput(owner, new LibreInputEvent(kind, 1, LibreInputModifiers.None,
                    LibreKey.Unknown, null, new LibrePoint(point.X, point.Y), default, button));
            list.SelectedIndex.Should().Be(expected);
            notifications.Should().Equal(button == LibrePointerButton.Primary
                ? ["selection", "down:1", "up:1"] : ["down:-1", "up:-1"]);
        });
    }

    [Fact]
    public void PortableListBoxWheelAccumulatesPartialDetentsWithoutChangingSelection()
    {
        RunPortableListBox((platform, owner, list) =>
        {
            list.SelectedIndex = 1;
            Point point = owner.PointToClient(list.PointToScreen(new Point(2, 2)));
            void Wheel(int delta) => platform.SendControlInput(owner, new LibreInputEvent(
                LibreInputEventKind.PointerWheel, 1, LibreInputModifiers.None, LibreKey.Unknown, null,
                new LibrePoint(point.X, point.Y), new LibrePoint(0, delta), LibrePointerButton.None));
            Wheel(-60);
            list.TopIndex.Should().Be(0);
            Wheel(-60);
            list.TopIndex.Should().Be(SystemInformation.MouseWheelScrollLines);
            list.SelectedIndex.Should().Be(1);
            Wheel(120);
            list.TopIndex.Should().Be(0);
        });
    }

    [Fact]
    public void PortableListBoxHandledWheelDoesNotScroll()
    {
        RunPortableListBox((platform, owner, list) =>
        {
            list.MouseWheel += (_, e) => ((HandledMouseEventArgs)e).Handled = true;
            Point point = owner.PointToClient(list.PointToScreen(new Point(2, 2)));
            platform.SendControlInput(owner, new LibreInputEvent(LibreInputEventKind.PointerWheel,
                1, LibreInputModifiers.None, LibreKey.Unknown, null, new LibrePoint(point.X, point.Y),
                new LibrePoint(0, -120), LibrePointerButton.None));
            list.TopIndex.Should().Be(0);
        });
    }

    [Fact]
    public void PortableListBoxSingleSelectionAndRemovalKeepCanonicalCollectionsAndEvents()
    {
        RunPortableListBox((_, _, list) =>
        {
            list.SelectedIndex = 2;
            list.SetSelected(4, true);
            list.SelectedIndices.Count.Should().Be(1);
            list.SelectedIndices[0].Should().Be(4);
            list.GetSelected(2).Should().BeFalse();
            int changes = 0;
            list.SelectedIndexChanged += (_, _) => changes++;
            list.Items.RemoveAt(4);
            list.SelectedIndex.Should().Be(-1);
            list.SelectedItems.Should().BeEmpty();
            changes.Should().Be(1);
            list.TopIndex = int.MaxValue;
            list.TopIndex.Should().Be(list.Items.Count - 3);
            list.TopIndex = int.MinValue;
            list.TopIndex.Should().Be(0);
            list.Items.Clear();
            list.TopIndex.Should().Be(0);
            list.IndexFromPoint(1, 1).Should().Be(ListBox.NoMatches);
        });
    }

    [Fact]
    public void PortableListBoxEmptyNavigationAndHostAcceptCancelKeysDoNotSelectItems()
    {
        RunPortableListBox((platform, owner, list) =>
        {
            list.Items.Clear();
            SendDropdownKey(platform, owner, LibreKey.Down);
            list.SelectedIndex.Should().Be(-1);
            list.Items.AddRange(["one", "two"]);
            list.SelectedIndex = 0;
            List<Keys> keys = [];
            list.PreviewKeyDown += (_, e) => e.IsInputKey = true;
            list.KeyDown += (_, e) => keys.Add(e.KeyCode);
            SendDropdownKey(platform, owner, LibreKey.Enter);
            SendDropdownKey(platform, owner, LibreKey.Escape);
            keys.Should().Equal(Keys.Enter, Keys.Escape);
            list.SelectedIndex.Should().Be(0);
        });
    }

    [Fact]
    public void PortableListBoxSelectionDisposalDoesNotRaiseLateMouseDown()
    {
        RunPortableListBox((platform, owner, list) =>
        {
            int mouseDown = 0;
            list.MouseDown += (_, _) => mouseDown++;
            list.SelectedIndexChanged += (_, _) => list.Dispose();
            Point point = owner.PointToClient(list.PointToScreen(new Point(2, 2)));
            platform.SendControlInput(owner, new LibreInputEvent(LibreInputEventKind.PointerDown,
                1, LibreInputModifiers.None, LibreKey.Unknown, null, new LibrePoint(point.X, point.Y),
                default, LibrePointerButton.Primary));
            list.IsDisposed.Should().BeTrue();
            mouseDown.Should().Be(0);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void PortableListBoxUnimplementedModesFailExplicitly(int mode)
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using PaintableListBox list = new() { BorderStyle = BorderStyle.None };
        switch (mode)
        {
            case 0: list.DrawMode = DrawMode.OwnerDrawFixed; break;
            case 1: list.SelectionMode = SelectionMode.MultiExtended; break;
            case 2: list.MultiColumn = true; break;
            case 3: list.HorizontalScrollbar = true; break;
            case 4: list.UseCustomTabOffsets = true; break;
            case 5: list.ScrollAlwaysVisible = true; break;
        }

        using Bitmap target = new(160, 120);
        using Graphics graphics = Graphics.FromImage(target);
        Action paint = () => list.PaintContent(graphics, list.ClientRectangle);
        Action hit = () => list.IndexFromPoint(1, 1);
        paint.Should().Throw<PlatformNotSupportedException>();
        hit.Should().Throw<PlatformNotSupportedException>();
    }

    private static void RunPortableListBox(Action<HeadlessPlatform, Form, ListBox> action)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using ListBox list = new() { BorderStyle = BorderStyle.None, IntegralHeight = false, Location = new Point(12, 12) };
        list.Size = new Size(140, list.Font.Height * 3);
        for (int index = 0; index < 12; index++)
            list.Items.Add($"row {index}");
        owner.Controls.Add(list);
        owner.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        list.Focus().Should().BeTrue();
        action(platform, owner, list);
    }

    private sealed class PaintableListBox : ListBox
    {
        internal void PaintContent(Graphics graphics, Rectangle clip)
        {
            using PaintEventArgs args = new(graphics, clip);
            OnPaint(args);
        }
    }
}
