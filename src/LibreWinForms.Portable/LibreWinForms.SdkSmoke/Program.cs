using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.ComponentModel.Design.Serialization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;
using ProGPU.Wpf.Interop;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfWindow = System.Windows.Window;

namespace LibreWinForms.SdkSmoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--run-form", StringComparer.Ordinal))
        {
            return RunMainFormSmoke();
        }

        if (args.Contains("--run-dialog", StringComparer.Ordinal))
        {
            return RunOwnedDialogSmoke();
        }

        if (args.Contains("--run-designer", StringComparer.Ordinal))
        {
            return RunDesignerSmoke();
        }

        if (args.Contains("--run-message-box", StringComparer.Ordinal))
        {
            return RunMessageBoxSmoke();
        }

        if (args.Contains("--run-checkables", StringComparer.Ordinal))
        {
            return RunCheckableControlsSmoke();
        }

        if (args.Contains("--run-listview", StringComparer.Ordinal))
        {
            return RunListViewSmoke();
        }

        if (args.Contains("--run-custom-paint", StringComparer.Ordinal))
        {
            return RunCustomPaintSmoke();
        }

        if (args.Contains("--run-keyboard", StringComparer.Ordinal))
        {
            return RunKeyboardRoutingSmoke();
        }

        Console.WriteLine("LibreWinForms SDK smoke build loaded.");
        return 0;
    }

    private static int RunKeyboardRoutingSmoke()
    {
        bool loaded = false;
        bool filterHandled = false;
        bool processDeleteHandled = false;
        bool processF6Handled = false;
        bool processF12Handled = false;
        bool processInsertHandled = false;
        bool ordinaryKeyReachedKeyDown = false;
        bool timedOut = false;

        var application = new WpfApplication();
        var commandControl = new KeyboardCommandProbeControl
        {
            Size = new System.Drawing.Size(240, 100)
        };
        var host = new DialogInputProbeHost { Child = commandControl };
        var window = new WpfWindow
        {
            Title = "LibreWinForms keyboard routing smoke",
            Width = 320,
            Height = 180,
            Content = host
        };
        var filter = new KeyboardMessageFilter(commandControl);
        commandControl.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Forms.Keys.A)
            {
                ordinaryKeyReachedKeyDown = true;
                eventArgs.Handled = true;
            }
        };

        using var watchdog = new System.Threading.Timer(
            _ => window.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    timedOut = true;
                    window.Close();
                })),
            null,
            TimeSpan.FromSeconds(30),
            Timeout.InfiniteTimeSpan);

        window.Loaded += (_, _) =>
        {
            loaded = true;
            _ = commandControl.Focus();
            window.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    Forms.Application.AddMessageFilter(filter);
                    try
                    {
                        filterHandled = host.RaiseKeyDown(window, System.Windows.Input.Key.F2);
                    }
                    finally
                    {
                        Forms.Application.RemoveMessageFilter(filter);
                    }

                    processDeleteHandled = host.RaiseKeyDown(window, System.Windows.Input.Key.Delete);
                    processF6Handled = host.RaiseKeyDown(window, System.Windows.Input.Key.F6);
                    processF12Handled = host.RaiseKeyDown(window, System.Windows.Input.Key.F12);
                    processInsertHandled = host.RaiseKeyDown(window, System.Windows.Input.Key.Insert);
                    _ = host.RaiseKeyDown(window, System.Windows.Input.Key.A);
                    watchdog.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    window.Close();
                }),
                DispatcherPriority.ApplicationIdle);
        };

        application.Run(window);
        host.Child = null;

        bool filterContract = filter.CallCount == 1
            && filter.LastHWnd == commandControl.Handle
            && filter.LastMessage == 0x0100
            && filter.LastKeyCode == Forms.Keys.F2
            && commandControl.ProcessedKeys.Count == 5
            && commandControl.ProcessedKeys[0] == Forms.Keys.Delete
            && commandControl.ProcessedKeys[1] == Forms.Keys.F6
            && commandControl.ProcessedKeys[2] == Forms.Keys.F12
            && commandControl.ProcessedKeys[3] == Forms.Keys.Insert
            && commandControl.ProcessedKeys[4] == Forms.Keys.A;
        bool success = loaded
            && filterHandled
            && processDeleteHandled
            && processF6Handled
            && processF12Handled
            && processInsertHandled
            && ordinaryKeyReachedKeyDown
            && filterContract
            && !timedOut;
        if (!success)
        {
            Console.Error.WriteLine(
                "LibreWinForms keyboard routing smoke failed"
                + $" loaded={loaded} filterHandled={filterHandled}"
                + $" delete={processDeleteHandled} f6={processF6Handled}"
                + $" f12={processF12Handled} insert={processInsertHandled}"
                + $" ordinaryKey={ordinaryKeyReachedKeyDown} filterContract={filterContract}"
                + $" filterCalls={filter.CallCount} processed={string.Join(',', commandControl.ProcessedKeys)}"
                + $" timedOut={timedOut}");
            return 9;
        }

        Console.WriteLine(
            "LibreWinForms keyboard routing smoke result=Success "
            + "messageFilter=True processCmdKey=True keyDownFallback=True "
            + "handle=True f2=True delete=True f6=True f12=True insert=True");
        return 0;
    }

    private static int RunCustomPaintSmoke()
    {
        var rootControl = new CustomPaintSmokeControl
        {
            Size = new System.Drawing.Size(160, 80)
        };
        var childControl = new CustomPaintSmokeControl
        {
            Bounds = new System.Drawing.Rectangle(20, 18, 70, 32)
        };
        rootControl.Controls.Add(childControl);

        var nativeContext = new ProGPU.Scene.DrawingContext();
        PaintControlForSmoke(rootControl, nativeContext, Matrix4x4.Identity);
        PaintControlForSmoke(childControl, nativeContext, Matrix4x4.Identity);

        bool firstPaint = rootControl.BackgroundPaintCount == 1
            && rootControl.ForegroundPaintCount == 1
            && childControl.BackgroundPaintCount == 1
            && childControl.ForegroundPaintCount == 1;
        int firstCommandCount = nativeContext.Commands.Count;
        bool directCommands = firstCommandCount >= 12;

        var paintSource = (Forms.IPortableWinFormsPaintSource)rootControl;
        long versionBefore = paintSource.PortablePaintVersion;
        rootControl.Invalidate();
        long versionAfter = paintSource.PortablePaintVersion;
        PaintControlForSmoke(rootControl, nativeContext, Matrix4x4.Identity);
        PaintControlForSmoke(childControl, nativeContext, Matrix4x4.Identity);
        bool invalidationRepaints = versionAfter > versionBefore
            && rootControl.BackgroundPaintCount == 2
            && rootControl.ForegroundPaintCount == 2
            && childControl.BackgroundPaintCount == 2
            && childControl.ForegroundPaintCount == 2
            && nativeContext.Commands.Count > firstCommandCount;
        bool typedContract = paintSource.SupportsPortablePainting;
        bool reparentInvalidation = VerifyHostInvalidationSubscriptions();

        var transformedControl = new CustomPaintSmokeControl
        {
            Bounds = new System.Drawing.Rectangle(20, 18, 70, 32),
            ResetTransformDuringPaint = true
        };
        var transformedContext = new ProGPU.Scene.DrawingContext();
        Matrix4x4 outerTransform = Matrix4x4.CreateScale(2f, 3f, 1f)
            * Matrix4x4.CreateTranslation(11f, 13f, 0f);
        PaintControlForSmoke(transformedControl, transformedContext, outerTransform);
        ProGPU.Scene.RenderCommand? transformedBackground = transformedContext.Commands.FirstOrDefault(
            static command => command.Type == ProGPU.Scene.RenderCommandType.DrawRect);
        bool transformedDirectPaint = transformedBackground is { } command
            && command.Rect.X == 51f
            && command.Rect.Y == 67f
            && command.Rect.Width == 140f
            && command.Rect.Height == 96f;
        bool retainedFallbackPaint = VerifyRetainedCustomPaintFallback();
        bool retainedFallbackOwnerDraw = VerifyRetainedOwnerDrawFallback();

        bool success = firstPaint
            && directCommands
            && invalidationRepaints
            && typedContract
            && reparentInvalidation
            && transformedDirectPaint
            && retainedFallbackPaint
            && retainedFallbackOwnerDraw;
        if (!success)
        {
            Console.Error.WriteLine(
                "LibreWinForms custom-paint smoke failed"
                + $" firstPaint={firstPaint} directCommands={directCommands}"
                + $" invalidation={invalidationRepaints} typed={typedContract}"
                + $" reparentInvalidation={reparentInvalidation}"
                + $" transformedDirectPaint={transformedDirectPaint}"
                + $" retainedFallbackPaint={retainedFallbackPaint}"
                + $" retainedFallbackOwnerDraw={retainedFallbackOwnerDraw}"
                + $" rootBackground={rootControl.BackgroundPaintCount}"
                + $" rootForeground={rootControl.ForegroundPaintCount}"
                + $" childBackground={childControl.BackgroundPaintCount}"
                + $" childForeground={childControl.ForegroundPaintCount}"
                + $" commands={nativeContext.Commands.Count}");
            return 8;
        }

        Console.WriteLine(
            "LibreWinForms custom-paint smoke result=Success "
            + "typedDispatch=True background=True foreground=True child=True "
            + "directProGpuCommands=True transformedDirectPaint=True "
            + "resetPreservesHostTransform=True invalidationRepaint=True "
            + "reparentInvalidation=True retainedFallbackPaint=True "
            + "retainedFallbackOwnerDraw=True border3D=True");
        return 0;
    }

    private static bool VerifyRetainedCustomPaintFallback()
    {
        var control = new CustomPaintSmokeControl
        {
            Size = new System.Drawing.Size(96, 48)
        };
        var host = new SmokeWindowsFormsHost { Child = control };
        host.Measure(new System.Windows.Size(96, 48));
        host.Arrange(new System.Windows.Rect(0, 0, 96, 48));

        var visual = new System.Windows.Media.DrawingVisual();
        using (System.Windows.Media.DrawingContext drawingContext = visual.RenderOpen())
        {
            host.RenderForSmoke(drawingContext);
        }

        bool success = host.PortableCustomPaintDispatchCount > 0
            && control.BackgroundPaintCount == host.PortableCustomPaintDispatchCount
            && control.ForegroundPaintCount == host.PortableCustomPaintDispatchCount;
        if (!success)
        {
            Console.Error.WriteLine(
                "LibreWinForms retained custom-paint fallback diagnostic"
                + $" dispatches={host.PortableCustomPaintDispatchCount}"
                + $" background={control.BackgroundPaintCount}"
                + $" foreground={control.ForegroundPaintCount}"
                + $" actualWidth={host.ActualWidth} actualHeight={host.ActualHeight}");
        }
        host.Child = null;
        return success;
    }

    private static bool VerifyRetainedOwnerDrawFallback()
    {
        int drawDispatches = 0;
        var treeView = new Forms.TreeView
        {
            DrawMode = Forms.TreeViewDrawMode.OwnerDrawText,
            Size = new System.Drawing.Size(180, 80)
        };
        treeView.Nodes.Add(new Forms.TreeNode("Owner drawn"));
        treeView.DrawNode += (_, e) =>
        {
            drawDispatches++;
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.DarkSlateBlue);
            e.Graphics.FillRectangle(brush, e.Bounds);
            e.DrawDefault = true;
        };

        var host = new SmokeWindowsFormsHost { Child = treeView };
        host.Measure(new System.Windows.Size(180, 80));
        host.Arrange(new System.Windows.Rect(0, 0, 180, 80));

        var visual = new System.Windows.Media.DrawingVisual();
        using (System.Windows.Media.DrawingContext drawingContext = visual.RenderOpen())
        {
            host.RenderForSmoke(drawingContext);
        }

        bool success = drawDispatches > 0
            && host.PortableOwnerDrawDispatchCount == drawDispatches;
        if (!success)
        {
            Console.Error.WriteLine(
                "LibreWinForms retained owner-draw fallback diagnostic"
                + $" directDispatches={drawDispatches}"
                + $" hostDispatches={host.PortableOwnerDrawDispatchCount}"
                + $" nodes={treeView.Nodes.Count}"
                + $" actualWidth={host.ActualWidth} actualHeight={host.ActualHeight}");
        }
        host.Child = null;
        return success;
    }

    private static bool VerifyHostInvalidationSubscriptions()
    {
        var root = new Forms.Control();
        var left = new Forms.Control();
        var right = new Forms.Control();
        var leaf = new Forms.Control();
        left.Controls.Add(leaf);
        root.Controls.Add(left);
        root.Controls.Add(right);

        var firstHost = new System.Windows.Forms.Integration.WindowsFormsHost { Child = root };
        if (firstHost.PortableInvalidationSubscriptionCount != 4
            || !InvalidationDispatchesExactlyOnce(firstHost, leaf))
        {
            firstHost.Child = null;
            return false;
        }

        right.Controls.Add(leaf);
        if (firstHost.PortableInvalidationSubscriptionCount != 4
            || left.Controls.Contains(leaf)
            || !InvalidationDispatchesExactlyOnce(firstHost, leaf))
        {
            firstHost.Child = null;
            return false;
        }

        var replacement = new Forms.Control();
        right.Controls[0] = replacement;
        long firstBeforeDetachedInvalidation = firstHost.PortableChildInvalidationDispatchCount;
        leaf.Invalidate();
        bool replacementBalanced = firstHost.PortableInvalidationSubscriptionCount == 4
            && firstHost.PortableChildInvalidationDispatchCount == firstBeforeDetachedInvalidation
            && InvalidationDispatchesExactlyOnce(firstHost, replacement);
        if (!replacementBalanced)
        {
            firstHost.Child = null;
            return false;
        }

        right.Controls[0] = leaf;
        var secondRoot = new Forms.Control();
        var secondHost = new System.Windows.Forms.Integration.WindowsFormsHost { Child = secondRoot };
        secondRoot.Controls.Add(leaf);
        long firstBeforeCrossHostInvalidation = firstHost.PortableChildInvalidationDispatchCount;
        long secondBeforeCrossHostInvalidation = secondHost.PortableChildInvalidationDispatchCount;
        leaf.Invalidate();
        bool crossHostBalanced = firstHost.PortableInvalidationSubscriptionCount == 3
            && secondHost.PortableInvalidationSubscriptionCount == 2
            && firstHost.PortableChildInvalidationDispatchCount == firstBeforeCrossHostInvalidation
            && secondHost.PortableChildInvalidationDispatchCount == secondBeforeCrossHostInvalidation + 1;

        firstHost.Child = null;
        secondHost.Child = null;
        return crossHostBalanced
            && firstHost.PortableInvalidationSubscriptionCount == 0
            && secondHost.PortableInvalidationSubscriptionCount == 0;
    }

    private static bool InvalidationDispatchesExactlyOnce(
        System.Windows.Forms.Integration.WindowsFormsHost host,
        Forms.Control control)
    {
        long before = host.PortableChildInvalidationDispatchCount;
        control.Invalidate();
        return host.PortableChildInvalidationDispatchCount == before + 1;
    }

    private static void PaintControlForSmoke(
        CustomPaintSmokeControl control,
        ProGPU.Scene.DrawingContext nativeContext,
        Matrix4x4 outerTransform)
    {
        var paintSource = (Forms.IPortableWinFormsPaintSource)control;
        Matrix4x4 clientTransform = Matrix4x4.CreateTranslation(control.Left, control.Top, 0f)
            * outerTransform;
        using System.Drawing.Graphics graphics = System.Drawing.Graphics.FromProGpuDrawingContext(
            nativeContext,
            clientTransform);
        var paintEventArgs = new Forms.PaintEventArgs(
            graphics,
            new System.Drawing.Rectangle(0, 0, control.Width, control.Height));
        paintSource.PaintPortableBackground(paintEventArgs);
        paintSource.PaintPortable(paintEventArgs);
    }

    private static int RunListViewSmoke()
    {
        using var smallBitmap = new System.Drawing.Bitmap(16, 16);
        using var largeBitmap = new System.Drawing.Bitmap(32, 32);
        using var smallImages = new Forms.ImageList { ImageSize = new System.Drawing.Size(16, 16) };
        using var largeImages = new Forms.ImageList { ImageSize = new System.Drawing.Size(32, 32) };
        smallImages.Images.Add("template", smallBitmap);
        largeImages.Images.Add("template", largeBitmap);

        var listView = new Forms.ListView
        {
            Size = new System.Drawing.Size(218, 96),
            MultiSelect = false,
            SmallImageList = smallImages,
            LargeImageList = largeImages,
            View = Forms.View.LargeIcon
        };
        for (int index = 0; index < 12; index++)
        {
            listView.Items.Add(new Forms.ListViewItem("Template " + index, 0));
        }

        System.Drawing.Rectangle largeFirst = listView.GetItemRect(0);
        System.Drawing.Rectangle largeSecond = listView.GetItemRect(1);
        System.Drawing.Rectangle largeThird = listView.GetItemRect(2);
        bool largeLayout = largeSecond.X > largeFirst.X
            && largeSecond.Y == largeFirst.Y
            && largeThird.X == largeFirst.X
            && largeThird.Y > largeFirst.Y
            && ReferenceEquals(
                listView.GetItemAt(largeSecond.Left + (largeSecond.Width / 2), largeSecond.Top + (largeSecond.Height / 2)),
                listView.Items[1]);

        listView.View = Forms.View.List;
        System.Drawing.Rectangle listFirst = listView.GetItemRect(0);
        System.Drawing.Rectangle listSecond = listView.GetItemRect(1);
        System.Drawing.Rectangle listFifth = listView.GetItemRect(4);
        bool listLayout = listSecond.X == listFirst.X
            && listSecond.Y > listFirst.Y
            && listFifth.X > listFirst.X
            && listFifth.Y == listFirst.Y
            && listFifth.Width < listView.ClientSize.Width
            && listSecond.Height >= smallImages.ImageSize.Height
            && ReferenceEquals(
                listView.GetItemAt(listSecond.Left + 8, listSecond.Top + (listSecond.Height / 2)),
                listView.Items[1]);

        listView.Items[0].Selected = true;
        var down = new Forms.KeyEventArgs(Forms.Keys.Down);
        listView.RaiseKeyDown(down);
        bool keyboardNavigation = down.Handled
            && listView.SelectedItems.Count == 1
            && ReferenceEquals(listView.SelectedItems[0], listView.Items[1]);

        listView.View = Forms.View.LargeIcon;
        var right = new Forms.KeyEventArgs(Forms.Keys.Right);
        listView.RaiseKeyDown(right);
        bool gridNavigation = right.Handled
            && listView.SelectedItems.Count == 1
            && ReferenceEquals(listView.SelectedItems[0], listView.Items[2]);

        listView.View = Forms.View.List;
        int beforeWheelLeft = listView.GetItemRect(0).Left;
        listView.RaiseMouseWheel(new Forms.MouseEventArgs(Forms.MouseButtons.None, 0, 10, 40, -120));
        int afterWheelLeft = listView.GetItemRect(0).Left;
        bool wheelScroll = afterWheelLeft < beforeWheelLeft;

        listView.Items[^1].EnsureVisible();
        System.Drawing.Rectangle lastVisibleBounds = listView.GetItemRect(listView.Items.Count - 1);
        bool ensureVisible = lastVisibleBounds.Left >= 1
            && lastVisibleBounds.Right <= listView.ClientSize.Width - 1
            && lastVisibleBounds.Top >= 1
            && lastVisibleBounds.Bottom <= listView.ClientSize.Height - 1
            && ReferenceEquals(
                listView.GetItemAt(lastVisibleBounds.Left + 8, lastVisibleBounds.Top + (lastVisibleBounds.Height / 2)),
                listView.Items[^1]);

        bool invalidViewRejected = false;
        try
        {
            listView.View = (Forms.View)int.MaxValue;
        }
        catch (InvalidEnumArgumentException)
        {
            invalidViewRejected = true;
        }

        bool invalidItemRejected = false;
        try
        {
            _ = listView.GetItemRect(listView.Items.Count);
        }
        catch (ArgumentOutOfRangeException)
        {
            invalidItemRejected = true;
        }

        int invalidationCount = 0;
        listView.Invalidated += (_, _) => invalidationCount++;
        listView.BeginUpdate();
        listView.View = Forms.View.LargeIcon;
        listView.Items.Add(new Forms.ListViewItem("Deferred template", 0));
        listView.Items[0].ImageIndex = -1;
        smallImages.Images.SetKeyName(0, "renamed-template");
        bool updateSuppressed = invalidationCount == 0;
        listView.EndUpdate();
        bool updateCoalesced = updateSuppressed && invalidationCount == 1;

        bool success = largeLayout
            && listLayout
            && keyboardNavigation
            && gridNavigation
            && wheelScroll
            && ensureVisible
            && invalidViewRejected
            && invalidItemRejected
            && updateCoalesced;
        if (!success)
        {
            Console.Error.WriteLine(
                "LibreWinForms ListView smoke failed"
                + $" largeLayout={largeLayout} listLayout={listLayout}"
                + $" keyboard={keyboardNavigation} gridKeyboard={gridNavigation}"
                + $" wheel={wheelScroll} ensureVisible={ensureVisible}"
                + $" invalidView={invalidViewRejected} invalidItem={invalidItemRejected}"
                + $" updateCoalesced={updateCoalesced} invalidations={invalidationCount}");
            return 7;
        }

        Console.WriteLine(
            "LibreWinForms ListView smoke result=Success "
            + "largeIconLayout=True listLayout=True imageLists=True hitTest=True "
            + "keyboard=True wheel=True ensureVisible=True updateCoalesced=True");
        return 0;
    }

    private static int RunCheckableControlsSmoke()
    {
        var checkEvents = new List<string>();
        var checkBox = new Forms.CheckBox
        {
            ThreeState = true
        };
        checkBox.CheckedChanged += (_, _) => checkEvents.Add("checked:" + checkBox.Checked);
        checkBox.CheckStateChanged += (_, _) => checkEvents.Add("state:" + checkBox.CheckState);
        checkBox.Click += (_, _) => checkEvents.Add("click");
        checkBox.MouseClick += (_, _) => checkEvents.Add("mouse");

        var leftClick = new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, 2, 2, 0);
        var rightClick = new Forms.MouseEventArgs(Forms.MouseButtons.Right, 1, 2, 2, 0);
        checkBox.RaiseMouseClick(leftClick);
        bool checkedTransition = checkBox.CheckState == Forms.CheckState.Checked
            && checkEvents.SequenceEqual(new[] { "checked:True", "state:Checked", "click", "mouse" });

        checkEvents.Clear();
        checkBox.RaiseMouseClick(leftClick);
        bool indeterminateTransition = checkBox.CheckState == Forms.CheckState.Indeterminate
            && checkBox.Checked
            && checkEvents.SequenceEqual(new[] { "state:Indeterminate", "click", "mouse" });

        checkEvents.Clear();
        checkBox.Checked = true;
        bool indeterminateBooleanContract = checkBox.CheckState == Forms.CheckState.Indeterminate
            && checkBox.Checked
            && checkEvents.Count == 0;

        checkBox.RaiseMouseClick(leftClick);
        bool uncheckedTransition = checkBox.CheckState == Forms.CheckState.Unchecked
            && checkEvents.SequenceEqual(new[] { "checked:False", "state:Unchecked", "click", "mouse" });

        checkEvents.Clear();
        checkBox.RaiseMouseClick(rightClick);
        bool rightClickDoesNotToggle = checkBox.CheckState == Forms.CheckState.Unchecked
            && checkEvents.SequenceEqual(new[] { "mouse" });

        checkEvents.Clear();
        checkBox.AutoCheck = false;
        checkBox.RaiseMouseClick(leftClick);
        bool autoCheckFalse = checkBox.CheckState == Forms.CheckState.Unchecked
            && checkEvents.SequenceEqual(new[] { "click", "mouse" });

        checkBox.AutoCheck = true;
        checkEvents.Clear();
        var spaceDown = new Forms.KeyEventArgs(Forms.Keys.Space);
        var spaceUp = new Forms.KeyEventArgs(Forms.Keys.Space);
        checkBox.RaiseKeyDown(spaceDown);
        checkBox.RaiseKeyUp(spaceUp);
        bool spaceActivates = checkBox.CheckState == Forms.CheckState.Checked
            && spaceDown.Handled
            && spaceUp.Handled
            && checkEvents.SequenceEqual(new[] { "checked:True", "state:Checked", "click" });

        bool invalidCheckStateRejected = false;
        try
        {
            checkBox.CheckState = (Forms.CheckState)int.MaxValue;
        }
        catch (InvalidEnumArgumentException)
        {
            invalidCheckStateRejected = true;
        }

        var panel = new Forms.Panel();
        var firstRadio = new Forms.RadioButton { Name = "first" };
        var secondRadio = new Forms.RadioButton { Name = "second" };
        panel.Controls.Add(firstRadio);
        panel.Controls.Add(secondRadio);
        firstRadio.Checked = true;

        var radioEvents = new List<string>();
        firstRadio.CheckedChanged += (_, _) => radioEvents.Add("first:" + firstRadio.Checked);
        secondRadio.CheckedChanged += (_, _) => radioEvents.Add("second:" + secondRadio.Checked);
        secondRadio.Click += (_, _) => radioEvents.Add("click");
        secondRadio.PerformClick();
        bool radioGrouping = !firstRadio.Checked
            && secondRadio.Checked
            && !firstRadio.TabStop
            && secondRadio.TabStop
            && radioEvents.SequenceEqual(new[] { "first:False", "second:True", "click" });

        var independentRadio = new Forms.RadioButton
        {
            AutoCheck = false,
            Checked = true
        };
        panel.Controls.Add(independentRadio);
        independentRadio.PerformClick();
        bool radioAutoCheckFalse = independentRadio.Checked && secondRadio.Checked;

        var nestedPanel = new Forms.Panel();
        var nestedRadio = new Forms.RadioButton { Checked = true };
        nestedPanel.Controls.Add(nestedRadio);
        panel.Controls.Add(nestedPanel);
        bool radioParentBoundary = nestedRadio.Checked && secondRadio.Checked;

        int radioClickCount = 0;
        int radioMouseClickCount = 0;
        secondRadio.Click += (_, _) => radioClickCount++;
        secondRadio.MouseClick += (_, _) => radioMouseClickCount++;
        secondRadio.RaiseMouseClick(rightClick);
        bool radioRightClick = secondRadio.Checked
            && radioClickCount == 0
            && radioMouseClickCount == 1;

        bool metadataContract = string.Equals(
                TypeDescriptor.GetDefaultProperty(checkBox)?.Name,
                nameof(Forms.CheckBox.Checked),
                StringComparison.Ordinal)
            && string.Equals(
                TypeDescriptor.GetDefaultEvent(checkBox)?.Name,
                nameof(Forms.CheckBox.CheckedChanged),
                StringComparison.Ordinal)
            && string.Equals(
                TypeDescriptor.GetDefaultProperty(secondRadio)?.Name,
                nameof(Forms.RadioButton.Checked),
                StringComparison.Ordinal)
            && string.Equals(
                TypeDescriptor.GetDefaultEvent(secondRadio)?.Name,
                nameof(Forms.RadioButton.CheckedChanged),
                StringComparison.Ordinal)
            && checkBox.FlatStyle == Forms.FlatStyle.Standard
            && checkBox.ImageAlign == System.Drawing.ContentAlignment.MiddleCenter
            && checkBox.TextAlign == System.Drawing.ContentAlignment.MiddleLeft
            && secondRadio.TextAlign == System.Drawing.ContentAlignment.MiddleLeft;

        bool success = checkedTransition
            && indeterminateTransition
            && indeterminateBooleanContract
            && uncheckedTransition
            && rightClickDoesNotToggle
            && autoCheckFalse
            && spaceActivates
            && invalidCheckStateRejected
            && radioGrouping
            && radioAutoCheckFalse
            && radioParentBoundary
            && radioRightClick
            && metadataContract;
        if (!success)
        {
            Console.Error.WriteLine(
                "LibreWinForms checkable-controls smoke failed"
                + $" checked={checkedTransition} indeterminate={indeterminateTransition}"
                + $" indeterminateBool={indeterminateBooleanContract} unchecked={uncheckedTransition}"
                + $" rightClick={rightClickDoesNotToggle} autoCheckFalse={autoCheckFalse}"
                + $" space={spaceActivates} invalidState={invalidCheckStateRejected}"
                + $" radioGroup={radioGrouping} radioAutoCheckFalse={radioAutoCheckFalse}"
                + $" radioBoundary={radioParentBoundary} radioRightClick={radioRightClick}"
                + $" metadata={metadataContract}");
            return 6;
        }

        Console.WriteLine(
            "LibreWinForms checkable-controls smoke result=Success "
            + "checkStateMachine=True eventOrder=True leftRightClick=True space=True "
            + "radioGrouping=True parentBoundary=True metadata=True");
        return 0;
    }

    private static int RunMessageBoxSmoke()
    {
        if (!PortableWpfServiceRegistry.TryGetMessageBoxService(
                PortableWpfServiceKey.WinForms,
                out IPortableMessageBoxServiceRegistrar service))
        {
            Console.Error.WriteLine("LibreWinForms message-box smoke failed registrar=False");
            return 5;
        }

        var owner = new MessageBoxSmokeOwner(new IntPtr(0x1234));
        PortableMessageBoxRequest? capturedRequest = null;
        Forms.DialogResult selectedResult;
        using (service.Register(request =>
               {
                   capturedRequest = request;
                   return nameof(Forms.DialogResult.No);
               }))
        {
            selectedResult = Forms.MessageBox.Show(
                owner,
                "Delete selected item?",
                "SharpDevelop",
                Forms.MessageBoxButtons.YesNoCancel,
                Forms.MessageBoxIcon.Warning,
                Forms.MessageBoxDefaultButton.Button2,
                Forms.MessageBoxOptions.RightAlign | Forms.MessageBoxOptions.RtlReading);
        }

        bool requestMapped = selectedResult == Forms.DialogResult.No
            && capturedRequest is not null
            && ReferenceEquals(capturedRequest.Owner, owner)
            && string.Equals(capturedRequest.MessageBoxText, "Delete selected item?", StringComparison.Ordinal)
            && string.Equals(capturedRequest.Caption, "SharpDevelop", StringComparison.Ordinal)
            && string.Equals(capturedRequest.Button, nameof(Forms.MessageBoxButtons.YesNoCancel), StringComparison.Ordinal)
            && string.Equals(capturedRequest.Icon, nameof(Forms.MessageBoxIcon.Warning), StringComparison.Ordinal)
            && string.Equals(capturedRequest.DefaultResult, nameof(Forms.DialogResult.No), StringComparison.Ordinal)
            && capturedRequest.Options.Contains(nameof(Forms.MessageBoxOptions.RightAlign), StringComparison.Ordinal)
            && capturedRequest.Options.Contains(nameof(Forms.MessageBoxOptions.RtlReading), StringComparison.Ordinal)
            && string.Equals(capturedRequest.FallbackResult, nameof(Forms.DialogResult.No), StringComparison.Ordinal);

        bool missingHandlerFallsBack = Forms.MessageBox.Show(
                "Delete item?",
                "SharpDevelop",
                Forms.MessageBoxButtons.YesNo,
                Forms.MessageBoxIcon.Question,
                Forms.MessageBoxDefaultButton.Button2)
            == Forms.DialogResult.No;

        Forms.DialogResult nullHandlerResult;
        using (service.Register(_ => null))
        {
            nullHandlerResult = Forms.MessageBox.Show(
                "Retry operation?",
                "SharpDevelop",
                Forms.MessageBoxButtons.RetryCancel,
                Forms.MessageBoxIcon.Warning,
                Forms.MessageBoxDefaultButton.Button2);
        }
        bool nullHandlerFallsBack = nullHandlerResult == Forms.DialogResult.Cancel;

        Forms.DialogResult continueResult;
        using (service.Register(_ => nameof(Forms.DialogResult.Continue)))
        {
            continueResult = Forms.MessageBox.Show(
                "Continue operation?",
                "SharpDevelop",
                Forms.MessageBoxButtons.CancelTryContinue,
                Forms.MessageBoxIcon.Information,
                Forms.MessageBoxDefaultButton.Button3);
        }
        bool extendedResultsWork = continueResult == Forms.DialogResult.Continue;

        bool invalidHandlerResultRejected = false;
        using (service.Register(_ => "NotADialogResult"))
        {
            try
            {
                _ = Forms.MessageBox.Show("Invalid result");
            }
            catch (InvalidOperationException)
            {
                invalidHandlerResultRejected = true;
            }
        }

        bool invalidButtonsRejected = false;
        try
        {
            _ = Forms.MessageBox.Show(
                "Invalid buttons",
                "SharpDevelop",
                (Forms.MessageBoxButtons)int.MaxValue);
        }
        catch (InvalidEnumArgumentException)
        {
            invalidButtonsRejected = true;
        }

        bool invalidIconRejected = false;
        try
        {
            _ = Forms.MessageBox.Show(
                "Invalid icon",
                "SharpDevelop",
                Forms.MessageBoxButtons.OK,
                (Forms.MessageBoxIcon)int.MaxValue);
        }
        catch (InvalidEnumArgumentException)
        {
            invalidIconRejected = true;
        }

        bool invalidDefaultButtonRejected = false;
        try
        {
            _ = Forms.MessageBox.Show(
                "Invalid default button",
                "SharpDevelop",
                Forms.MessageBoxButtons.OK,
                Forms.MessageBoxIcon.None,
                (Forms.MessageBoxDefaultButton)int.MaxValue);
        }
        catch (InvalidEnumArgumentException)
        {
            invalidDefaultButtonRejected = true;
        }

        bool ownerServiceOptionsRejected = false;
        try
        {
            _ = Forms.MessageBox.Show(
                owner,
                "Invalid owner options",
                "SharpDevelop",
                Forms.MessageBoxButtons.OK,
                Forms.MessageBoxIcon.None,
                Forms.MessageBoxDefaultButton.Button1,
                Forms.MessageBoxOptions.DefaultDesktopOnly);
        }
        catch (ArgumentException)
        {
            ownerServiceOptionsRejected = true;
        }

        bool enumParity = (int)Forms.DialogResult.TryAgain == 10
            && (int)Forms.DialogResult.Continue == 11
            && (int)Forms.MessageBoxButtons.CancelTryContinue == 6
            && Forms.MessageBoxIcon.Stop == Forms.MessageBoxIcon.Hand
            && (int)Forms.MessageBoxDefaultButton.Button4 == 768;

        bool success = requestMapped
            && missingHandlerFallsBack
            && nullHandlerFallsBack
            && extendedResultsWork
            && invalidHandlerResultRejected
            && invalidButtonsRejected
            && invalidIconRejected
            && invalidDefaultButtonRejected
            && ownerServiceOptionsRejected
            && enumParity;
        if (!success)
        {
            Console.Error.WriteLine(
                "LibreWinForms message-box smoke failed"
                + $" requestMapped={requestMapped} missingFallback={missingHandlerFallsBack}"
                + $" nullFallback={nullHandlerFallsBack} extendedResults={extendedResultsWork}"
                + $" invalidHandler={invalidHandlerResultRejected} invalidButtons={invalidButtonsRejected}"
                + $" invalidIcon={invalidIconRejected} invalidDefault={invalidDefaultButtonRejected}"
                + $" ownerOptions={ownerServiceOptionsRejected} enumParity={enumParity}");
            return 5;
        }

        Console.WriteLine(
            "LibreWinForms message-box smoke result=Success typedInterop=True ownerPreserved=True "
            + "defaultResults=True platformFallback=True enumValidation=True extendedResults=True");
        return 0;
    }

    private static int RunMainFormSmoke()
    {
        bool shown = false;
        bool closed = false;
        bool intervalChanged = false;
        bool ticksOnUiThread = true;
        bool stopPreventedFurtherTicks = false;
        bool disposePreventedFurtherTicks = false;
        bool timerExceptionRouted = false;
        bool timedOut = false;
        int tickCount = 0;
        int ticksAtStop = 0;
        int uiThreadId = 0;

        var form = new Forms.Form
        {
            Name = "LibreWinFormsSdkSmoke",
            Text = "LibreWinForms SDK Smoke",
            Width = 320,
            Height = 180,
            StartPosition = Forms.FormStartPosition.CenterScreen
        };

        using var viewModeImage = new System.Drawing.Bitmap(12, 12);
        using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(viewModeImage))
        {
            graphics.Clear(System.Drawing.Color.DodgerBlue);
        }

        var checkedControl = new Forms.CheckBox
        {
            Name = "checkedControl",
            Left = 12,
            Top = 12,
            Width = 130,
            Height = 24,
            Text = "Checked",
            Checked = true
        };
        var indeterminateControl = new Forms.CheckBox
        {
            Name = "indeterminateControl",
            Left = 12,
            Top = 40,
            Width = 130,
            Height = 24,
            Text = "Indeterminate",
            ThreeState = true,
            CheckState = Forms.CheckState.Indeterminate
        };
        var viewModePanel = new Forms.Panel
        {
            Name = "viewModePanel",
            Left = 160,
            Top = 12,
            Width = 70,
            Height = 28
        };
        var largeViewRadio = new Forms.RadioButton
        {
            Name = "largeViewRadio",
            Appearance = Forms.Appearance.Button,
            Left = 0,
            Top = 0,
            Width = 28,
            Height = 24,
            Image = viewModeImage,
            Checked = true
        };
        var smallViewRadio = new Forms.RadioButton
        {
            Name = "smallViewRadio",
            Appearance = Forms.Appearance.Button,
            Left = 32,
            Top = 0,
            Width = 28,
            Height = 24,
            Text = "S"
        };
        viewModePanel.Controls.AddRange(new Forms.Control[] { largeViewRadio, smallViewRadio });
        form.Controls.AddRange(new Forms.Control[] { checkedControl, indeterminateControl, viewModePanel });
        bool checkableRenderTree = checkedControl.Checked
            && indeterminateControl.Checked
            && indeterminateControl.CheckState == Forms.CheckState.Indeterminate
            && largeViewRadio.Checked
            && !smallViewRadio.Checked
            && ReferenceEquals(largeViewRadio.Image, viewModeImage);

        using var formsTimer = new Forms.Timer
        {
            Interval = 30
        };
        using var throwingTimer = new Forms.Timer
        {
            Interval = 10
        };
        using var timerContainer = new Container();
        using var containedTimer = new Forms.Timer(timerContainer)
        {
            Tag = "timer-contract"
        };
        bool invalidIntervalRejected = false;
        try
        {
            containedTimer.Interval = 0;
        }
        catch (ArgumentOutOfRangeException)
        {
            invalidIntervalRejected = true;
        }
        bool timerContract = ReferenceEquals(containedTimer.Site?.Container, timerContainer)
            && string.Equals(containedTimer.Tag as string, "timer-contract", StringComparison.Ordinal)
            && string.Equals(TypeDescriptor.GetDefaultProperty(containedTimer)?.Name, nameof(Forms.Timer.Interval), StringComparison.Ordinal)
            && string.Equals(TypeDescriptor.GetDefaultEvent(containedTimer)?.Name, nameof(Forms.Timer.Tick), StringComparison.Ordinal)
            && containedTimer.ToString().Contains("Interval: 100", StringComparison.Ordinal)
            && invalidIntervalRejected;
        var settleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        int settlePhase = 0;
        using var watchdog = new System.Threading.Timer(
            _ =>
            {
                timedOut = true;
                form.Close();
            },
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        var expectedTimerException = new InvalidOperationException("LibreWinForms timer smoke exception");
        ThreadExceptionEventHandler threadExceptionHandler = (_, eventArgs) =>
        {
            timerExceptionRouted |= ReferenceEquals(eventArgs.Exception, expectedTimerException);
        };
        Forms.Application.ThreadException += threadExceptionHandler;

        throwingTimer.Tick += (_, _) =>
        {
            throwingTimer.Stop();
            throw expectedTimerException;
        };

        formsTimer.Tick += (_, _) =>
        {
            ticksOnUiThread &= Environment.CurrentManagedThreadId == uiThreadId;
            tickCount++;
            if (tickCount == 1)
            {
                formsTimer.Interval = 15;
                intervalChanged = formsTimer.Interval == 15;
            }
            else if (tickCount == 3)
            {
                formsTimer.Stop();
                ticksAtStop = tickCount;
                settlePhase = 1;
                settleTimer.Start();
            }
        };

        settleTimer.Tick += (_, _) =>
        {
            settleTimer.Stop();
            if (settlePhase == 1)
            {
                stopPreventedFurtherTicks = tickCount == ticksAtStop && !formsTimer.Enabled;
                formsTimer.Start();
                formsTimer.Dispose();
                settlePhase = 2;
                settleTimer.Start();
                return;
            }

            disposePreventedFurtherTicks = tickCount == ticksAtStop && !formsTimer.Enabled;
            watchdog.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            form.Close();
        };

        form.Shown += (_, _) =>
        {
            shown = true;
            uiThreadId = Environment.CurrentManagedThreadId;
            watchdog.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
            throwingTimer.Start();
            formsTimer.Start();
        };

        form.FormClosed += (_, _) =>
        {
            closed = true;
            settleTimer.Stop();
            throwingTimer.Stop();
            formsTimer.Stop();
        };

        Forms.Application.Run(form);
        Forms.Application.ThreadException -= threadExceptionHandler;

        if (!shown
            || !closed
            || timedOut
            || tickCount < 3
            || !intervalChanged
            || !ticksOnUiThread
            || !stopPreventedFurtherTicks
            || !disposePreventedFurtherTicks
            || !timerExceptionRouted
            || !timerContract
            || !checkableRenderTree)
        {
            Console.Error.WriteLine(
                $"LibreWinForms SDK smoke failed shown={shown} closed={closed} ticks={tickCount} " +
                $"intervalChanged={intervalChanged} uiThread={ticksOnUiThread} " +
                $"stopPrevented={stopPreventedFurtherTicks} disposePrevented={disposePreventedFurtherTicks} " +
                $"exceptionRouted={timerExceptionRouted} timerContract={timerContract} " +
                $"checkableRenderTree={checkableRenderTree} " +
                $"timedOut={timedOut}");
            return 2;
        }

        Console.WriteLine(
            "LibreWinForms SDK smoke result=Success host=WPF formShown=True formClosed=True " +
            $"timerTicks={tickCount} timerIntervalChanged={intervalChanged} timerUiThread={ticksOnUiThread} " +
            $"timerStopped={stopPreventedFurtherTicks} timerDisposed={disposePreventedFurtherTicks} " +
            $"timerExceptionRouted={timerExceptionRouted} timerContract={timerContract} " +
            $"checkableRenderTree={checkableRenderTree}");
        return 0;
    }

    private static int RunOwnedDialogSmoke()
    {
        bool ownerLoaded = false;
        bool dialogShown = false;
        bool dialogClosed = false;
        bool ownerLinked = false;
        bool staleResultReset = false;
        bool cancelResultDefaulted = false;
        bool validationBlockedFirstAccept = false;
        bool programmaticFocusTracked = false;
        bool programmaticFocusInputRouted = false;
        bool acceptClicked = false;
        bool cancelClicked = false;
        bool enterHandled = false;
        bool escapeHandled = false;
        bool closingCancelVetoed = false;
        bool closingResultVetoed = false;
        bool userCloseObservedCancel = false;
        bool userCloseResultVetoed = false;
        bool timedOut = false;
        int validationAttempts = 0;
        int closingAttempts = 0;
        int userCloseAttempts = 0;
        Forms.DialogResult dialogResult = Forms.DialogResult.None;
        Forms.DialogResult vetoDialogResult = Forms.DialogResult.None;
        Forms.DialogResult cancelDialogResult = Forms.DialogResult.None;
        Forms.DialogResult userCloseDialogResult = Forms.DialogResult.None;
        Forms.Form? activeDialog = null;

        var application = new WpfApplication();
        var ownerWindow = new WpfWindow
        {
            Title = "LibreWinForms SDK Dialog Owner",
            Width = 480,
            Height = 300
        };
        using var watchdog = new System.Threading.Timer(
            _ => ownerWindow.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    timedOut = true;
                    activeDialog?.Close();
                    ownerWindow.Close();
                })),
            null,
            TimeSpan.FromSeconds(60),
            Timeout.InfiniteTimeSpan);

        ownerWindow.Loaded += (_, _) =>
        {
            ownerLoaded = true;
            watchdog.Change(TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);

            var focusProbeForm = new Forms.Form();
            var focusProbeTextBox = new Forms.TextBox();
            focusProbeForm.Controls.Add(focusProbeTextBox);
            var focusProbeHost = new DialogInputProbeHost { Child = focusProbeForm };
            focusProbeTextBox.KeyDown += (_, eventArgs) =>
            {
                programmaticFocusInputRouted = true;
                eventArgs.Handled = true;
            };
            _ = focusProbeTextBox.Focus();
            focusProbeHost.RaiseKeyDown(ownerWindow, System.Windows.Input.Key.A);
            focusProbeHost.Child = null;

            ownerWindow.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    var dialog = new Forms.Form
                    {
                        Name = "LibreWinFormsSdkOwnedDialog",
                        Text = "LibreWinForms SDK Owned Dialog",
                        Width = 340,
                        Height = 200,
                        StartPosition = Forms.FormStartPosition.CenterParent
                    };
                    activeDialog = dialog;

                    var textBox = new Forms.TextBox
                    {
                        Name = "dialogInput",
                        Left = 16,
                        Top = 20,
                        Width = 290,
                        Text = "validated input"
                    };
                    var acceptButton = new Forms.Button
                    {
                        Name = "acceptButton",
                        Left = 150,
                        Top = 120,
                        Text = "OK"
                    };
                    var cancelButton = new Forms.Button
                    {
                        Name = "cancelButton",
                        Left = 230,
                        Top = 120,
                        Text = "Cancel"
                    };
                    dialog.Controls.AddRange(new Forms.Control[] { textBox, acceptButton, cancelButton });
                    dialog.AcceptButton = acceptButton;
                    dialog.CancelButton = cancelButton;
                    dialog.DialogResult = Forms.DialogResult.Cancel;
                    cancelResultDefaulted = cancelButton.DialogResult == Forms.DialogResult.Cancel;

                    textBox.Validating += (_, eventArgs) =>
                    {
                        validationAttempts++;
                        eventArgs.Cancel = validationAttempts == 1;
                    };
                    acceptButton.Click += (_, _) =>
                    {
                        acceptClicked = true;
                        dialog.DialogResult = Forms.DialogResult.OK;
                    };

                    var closeTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(100)
                    };
                    closeTimer.Tick += (_, _) =>
                    {
                        enterHandled |= ((Forms.IWinFormsDialogKeyProcessor)dialog)
                            .TryProcessDialogKey(Forms.Keys.Enter, dialog.ActiveControl);
                        if (validationAttempts == 1)
                        {
                            validationBlockedFirstAccept = !acceptClicked
                                && dialog.DialogResult == Forms.DialogResult.None;
                            return;
                        }

                        closeTimer.Stop();
                    };

                    dialog.Shown += (_, _) =>
                    {
                        dialogShown = true;
                        staleResultReset = dialog.DialogResult == Forms.DialogResult.None;
                        WpfWindow? dialogWindow = WpfApplication.Current.Windows
                            .Cast<WpfWindow>()
                            .FirstOrDefault(window => !ReferenceEquals(window, ownerWindow));
                        ownerLinked = dialogWindow != null
                            && ReferenceEquals(dialogWindow.Owner, ownerWindow);
                        _ = textBox.Focus();
                        programmaticFocusTracked = ReferenceEquals(dialog.ActiveControl, textBox)
                            && textBox.Focused;
                        closeTimer.Start();
                    };
                    dialog.FormClosed += (_, _) => dialogClosed = true;

                    dialogResult = dialog.ShowDialog(new WpfWindowOwner(ownerWindow));
                    activeDialog = null;
                    closeTimer.Stop();

                    var vetoDialog = new Forms.Form
                    {
                        Name = "LibreWinFormsSdkVetoDialog",
                        Text = "LibreWinForms SDK Close Veto Dialog",
                        Width = 300,
                        Height = 160,
                        StartPosition = Forms.FormStartPosition.CenterParent
                    };
                    activeDialog = vetoDialog;
                    var vetoButton = new Forms.Button
                    {
                        Name = "vetoButton",
                        Left = 190,
                        Top = 90,
                        Text = "OK",
                        DialogResult = Forms.DialogResult.OK
                    };
                    vetoDialog.Controls.Add(vetoButton);
                    vetoDialog.AcceptButton = vetoButton;
                    vetoDialog.FormClosing += (_, eventArgs) =>
                    {
                        closingAttempts++;
                        if (closingAttempts == 1)
                        {
                            eventArgs.Cancel = true;
                        }
                        else if (closingAttempts == 2)
                        {
                            vetoDialog.DialogResult = Forms.DialogResult.None;
                        }
                    };

                    var vetoTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(100)
                    };
                    vetoTimer.Tick += (_, _) =>
                    {
                        if (closingAttempts == 0)
                        {
                            vetoButton.PerformClick();
                            return;
                        }

                        if (closingAttempts == 1)
                        {
                            closingCancelVetoed = vetoDialog.DialogResult == Forms.DialogResult.None;
                            vetoButton.PerformClick();
                            return;
                        }

                        closingResultVetoed = closingAttempts == 2
                            && vetoDialog.DialogResult == Forms.DialogResult.None;
                        vetoTimer.Stop();
                        vetoButton.PerformClick();
                    };
                    vetoDialog.Shown += (_, _) => vetoTimer.Start();
                    vetoDialogResult = vetoDialog.ShowDialog(new WpfWindowOwner(ownerWindow));
                    activeDialog = null;
                    vetoTimer.Stop();

                    var cancelDialog = new Forms.Form
                    {
                        Name = "LibreWinFormsSdkCancelDialog",
                        Text = "LibreWinForms SDK Cancel Dialog",
                        Width = 300,
                        Height = 160,
                        StartPosition = Forms.FormStartPosition.CenterParent
                    };
                    activeDialog = cancelDialog;
                    var escapeButton = new Forms.Button
                    {
                        Name = "escapeButton",
                        Left = 190,
                        Top = 90,
                        Text = "Cancel"
                    };
                    cancelDialog.Controls.Add(escapeButton);
                    cancelDialog.CancelButton = escapeButton;
                    escapeButton.Click += (_, _) => cancelClicked = true;

                    var escapeTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(100)
                    };
                    escapeTimer.Tick += (_, _) =>
                    {
                        escapeTimer.Stop();
                        escapeHandled = ((Forms.IWinFormsDialogKeyProcessor)cancelDialog)
                            .TryProcessDialogKey(Forms.Keys.Escape, cancelDialog);
                    };
                    cancelDialog.Shown += (_, _) => escapeTimer.Start();
                    cancelDialogResult = cancelDialog.ShowDialog(new WpfWindowOwner(ownerWindow));
                    activeDialog = null;
                    escapeTimer.Stop();

                    var userCloseDialog = new Forms.Form
                    {
                        Name = "LibreWinFormsSdkUserCloseDialog",
                        Text = "LibreWinForms SDK User Close Dialog",
                        Width = 280,
                        Height = 140,
                        StartPosition = Forms.FormStartPosition.CenterParent
                    };
                    activeDialog = userCloseDialog;
                    userCloseDialog.FormClosing += (_, _) =>
                    {
                        userCloseAttempts++;
                        userCloseObservedCancel |= userCloseDialog.DialogResult == Forms.DialogResult.Cancel;
                        if (userCloseAttempts == 1)
                        {
                            userCloseDialog.DialogResult = Forms.DialogResult.None;
                        }
                    };
                    var userCloseTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(100)
                    };
                    userCloseTimer.Tick += (_, _) =>
                    {
                        if (userCloseAttempts == 0)
                        {
                            userCloseDialog.Close();
                            userCloseResultVetoed = userCloseAttempts == 1
                                && userCloseDialog.Visible
                                && userCloseDialog.DialogResult == Forms.DialogResult.None;
                            return;
                        }

                        userCloseTimer.Stop();
                        userCloseDialog.Close();
                    };
                    userCloseDialog.Shown += (_, _) => userCloseTimer.Start();
                    userCloseDialogResult = userCloseDialog.ShowDialog(new WpfWindowOwner(ownerWindow));
                    activeDialog = null;
                    userCloseTimer.Stop();
                    watchdog.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    ownerWindow.Close();
                }),
                DispatcherPriority.ApplicationIdle);
        };

        application.Run(ownerWindow);

        if (!ownerLoaded
            || !dialogShown
            || !dialogClosed
            || !ownerLinked
            || !staleResultReset
            || !cancelResultDefaulted
            || !validationBlockedFirstAccept
            || !programmaticFocusTracked
            || !programmaticFocusInputRouted
            || validationAttempts < 2
            || !acceptClicked
            || !cancelClicked
            || !enterHandled
            || !escapeHandled
            || !closingCancelVetoed
            || !closingResultVetoed
            || closingAttempts != 3
            || !userCloseObservedCancel
            || !userCloseResultVetoed
            || userCloseAttempts != 2
            || timedOut
            || dialogResult != Forms.DialogResult.OK
            || vetoDialogResult != Forms.DialogResult.OK
            || cancelDialogResult != Forms.DialogResult.Cancel
            || userCloseDialogResult != Forms.DialogResult.Cancel)
        {
            Console.Error.WriteLine(
                $"LibreWinForms SDK owned dialog smoke failed ownerLoaded={ownerLoaded} dialogShown={dialogShown} " +
                $"dialogClosed={dialogClosed} ownerLinked={ownerLinked} staleReset={staleResultReset} " +
                $"cancelDefault={cancelResultDefaulted} validationBlocked={validationBlockedFirstAccept} " +
                $"programmaticFocus={programmaticFocusTracked} focusInput={programmaticFocusInputRouted} " +
                $"validationAttempts={validationAttempts} " +
                $"acceptClicked={acceptClicked} cancelClicked={cancelClicked} " +
                $"enterHandled={enterHandled} escapeHandled={escapeHandled} closeCancelVeto={closingCancelVetoed} " +
                $"closeResultVeto={closingResultVetoed} closingAttempts={closingAttempts} " +
                $"userCloseCancel={userCloseObservedCancel} userCloseResultVeto={userCloseResultVetoed} " +
                $"userCloseAttempts={userCloseAttempts} result={dialogResult} vetoResult={vetoDialogResult} " +
                $"cancelResult={cancelDialogResult} userCloseResult={userCloseDialogResult} timedOut={timedOut}");
            return 3;
        }

        Console.WriteLine(
            "LibreWinForms SDK owned dialog smoke result=Success host=WPF ownerLoaded=True " +
            "dialogShown=True dialogClosed=True ownerLinked=True staleResultReset=True " +
            "validationBlocked=True programmaticFocus=True accept=OK closeVeto=True " +
            "escape=Cancel userCloseResultVeto=True userClose=Cancel");
        return 0;
    }

    private sealed class DialogInputProbeHost : System.Windows.Forms.Integration.WindowsFormsHost
    {
        public bool RaiseKeyDown(System.Windows.Media.Visual inputSource, System.Windows.Input.Key key)
        {
            System.Windows.PresentationSource source = System.Windows.PresentationSource.FromVisual(inputSource)
                ?? throw new InvalidOperationException("The input probe requires a connected presentation source.");
            var keyEventArgs = new System.Windows.Input.KeyEventArgs(
                System.Windows.Input.Keyboard.PrimaryDevice,
                source,
                Environment.TickCount,
                key)
            {
                RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent
            };
            OnKeyDown(keyEventArgs);
            return keyEventArgs.Handled;
        }
    }

    private sealed class KeyboardCommandProbeControl : Forms.Control
    {
        public List<Forms.Keys> ProcessedKeys { get; } = new();

        protected override bool ProcessCmdKey(ref Forms.Message msg, Forms.Keys keyData)
        {
            ProcessedKeys.Add(keyData);
            return keyData is Forms.Keys.Delete or Forms.Keys.F6 or Forms.Keys.F12 or Forms.Keys.Insert;
        }
    }

    private sealed class KeyboardMessageFilter : Forms.IMessageFilter
    {
        private readonly Forms.Control _expectedControl;

        public KeyboardMessageFilter(Forms.Control expectedControl)
        {
            _expectedControl = expectedControl;
        }

        public int CallCount { get; private set; }

        public IntPtr LastHWnd { get; private set; }

        public Forms.Keys LastKeyCode { get; private set; }

        public int LastMessage { get; private set; }

        public bool PreFilterMessage(ref Forms.Message message)
        {
            CallCount++;
            LastHWnd = message.HWnd;
            LastMessage = message.Msg;
            LastKeyCode = (Forms.Keys)message.WParam.ToInt32();
            return ReferenceEquals(Forms.Control.FromChildHandle(message.HWnd), _expectedControl)
                && LastKeyCode == Forms.Keys.F2;
        }
    }

    private static int RunDesignerSmoke()
    {
        const string originalName = "toolStripContainer1";
        const string renamedName = "designerContainer";
        const string updatedText = "LibreWinForms designer smoke";

        using var services = new ServiceContainer();
        var externalMenuCommandService = new MenuCommandService(services);
        services.AddService(typeof(IMenuCommandService), externalMenuCommandService);
        using var surface = new DesignSurface(services);
        var loader = new DesignerSmokeLoader();
        surface.BeginLoad(loader);

        var host = surface.GetService(typeof(IDesignerHost)) as IDesignerHost;
        var component = host?.Container.Components[originalName] as Forms.ToolStripContainer;
        var changeService = component?.Site?.GetService(typeof(IComponentChangeService)) as IComponentChangeService;
        var selectionService = host?.GetService(typeof(ISelectionService)) as ISelectionService;
        var menuCommandService = host?.GetService(typeof(IMenuCommandService)) as IMenuCommandService;
        var serializationManager = host?.GetService(typeof(IDesignerSerializationManager)) as IDesignerSerializationManager;
        PropertyDescriptor? textProperty = component is null ? null : TypeDescriptor.GetProperties(component)[nameof(Forms.Control.Text)];

        bool siteHasChangeService = changeService is not null;
        bool siteHasHost = ReferenceEquals(component?.Site?.GetService(typeof(IDesignerHost)), host);
        bool siteHasContainer = ReferenceEquals(component?.Site?.GetService(typeof(IContainer)), host?.Container);

        var localService = new DesignerSmokeService();
        if (component?.Site is IServiceContainer siteServices)
        {
            siteServices.AddService(typeof(DesignerSmokeService), localService);
        }

        bool siteLocalService = ReferenceEquals(component?.Site?.GetService(typeof(DesignerSmokeService)), localService);

        if (component?.Site?.GetService(typeof(IDictionaryService)) is IDictionaryService dictionary)
        {
            dictionary.SetValue("smoke", updatedText);
        }

        bool siteDictionary = string.Equals(
            (component?.Site?.GetService(typeof(IDictionaryService)) as IDictionaryService)?.GetValue("smoke") as string,
            updatedText,
            StringComparison.Ordinal);

        const string directComponentName = "directComponent1";
        var directComponent = new Component();
        bool directAdding = false;
        bool directAdded = false;
        bool directRemoving = false;
        bool directRemoved = false;
        ComponentEventHandler directAddingHandler = (sender, eventArgs) =>
        {
            directAdding |= ReferenceEquals(sender, host?.Container)
                && ReferenceEquals(eventArgs.Component, directComponent)
                && directComponent.Site is null;
        };
        ComponentEventHandler directAddedHandler = (sender, eventArgs) =>
        {
            directAdded |= ReferenceEquals(sender, host?.Container)
                && ReferenceEquals(eventArgs.Component, directComponent)
                && ReferenceEquals(directComponent.Site?.Container, host?.Container);
        };
        ComponentEventHandler directRemovingHandler = (sender, eventArgs) =>
        {
            directRemoving |= ReferenceEquals(sender, host)
                && ReferenceEquals(eventArgs.Component, directComponent)
                && ReferenceEquals(directComponent.Site?.Container, host?.Container);
        };
        ComponentEventHandler directRemovedHandler = (sender, eventArgs) =>
        {
            directRemoved |= ReferenceEquals(sender, host)
                && ReferenceEquals(eventArgs.Component, directComponent)
                && ReferenceEquals(directComponent.Site?.Container, host?.Container);
        };
        if (changeService is not null)
        {
            changeService.ComponentAdding += directAddingHandler;
            changeService.ComponentAdded += directAddedHandler;
            changeService.ComponentRemoving += directRemovingHandler;
            changeService.ComponentRemoved += directRemovedHandler;
        }

        host?.Container.Add(directComponent, directComponentName);
        bool directRegistered = ReferenceEquals(host?.Container.Components[directComponentName], directComponent)
            && string.Equals(serializationManager?.GetName(directComponent), directComponentName, StringComparison.Ordinal)
            && ReferenceEquals(serializationManager?.GetInstance(directComponentName), directComponent)
            && ReferenceEquals(directComponent.Site?.GetService(typeof(IDesignerHost)), host);
        host?.Container.Remove(directComponent);
        if (changeService is not null)
        {
            changeService.ComponentAdding -= directAddingHandler;
            changeService.ComponentAdded -= directAddedHandler;
            changeService.ComponentRemoving -= directRemovingHandler;
            changeService.ComponentRemoved -= directRemovedHandler;
        }

        bool directLifecycle = directAdding
            && directAdded
            && directRemoving
            && directRemoved
            && directRegistered
            && directComponent.Site is null
            && host?.Container.Components[directComponentName] is null
            && serializationManager?.GetInstance(directComponentName) is null;

        var toolboxItem = new System.Drawing.Design.ToolboxItem(typeof(Forms.Button));
        bool toolboxCreating = false;
        bool toolboxCreated = false;
        toolboxItem.ComponentsCreating += (_, eventArgs) =>
        {
            toolboxCreating = ReferenceEquals(eventArgs.DesignerHost, host);
        };
        toolboxItem.ComponentsCreated += (_, eventArgs) =>
        {
            toolboxCreated = eventArgs.Components is [Forms.Button];
        };

        var toolboxDefaults = new Hashtable
        {
            ["Parent"] = component!,
            [nameof(Forms.Control.Location)] = new System.Drawing.Point(24, 32),
            [nameof(Forms.Control.Size)] = new System.Drawing.Size(120, 28),
            [nameof(Forms.Control.Text)] = "Toolbox button"
        };
        IComponent[] toolboxComponents = host is null
            ? Array.Empty<IComponent>()
            : toolboxItem.CreateComponents(host, toolboxDefaults);
        var toolboxButton = toolboxComponents.Length == 1 ? toolboxComponents[0] as Forms.Button : null;
        IDesigner? toolboxDesigner = toolboxButton is null ? null : host?.GetDesigner(toolboxButton);
        bool toolboxCreation = toolboxCreating
            && toolboxCreated
            && toolboxButton is not null
            && string.Equals(toolboxItem.TypeName, typeof(Forms.Button).FullName, StringComparison.Ordinal)
            && string.Equals(toolboxItem.AssemblyName?.Name, typeof(Forms.Button).Assembly.GetName().Name, StringComparison.Ordinal)
            && ReferenceEquals(toolboxItem.GetType(host), typeof(Forms.Button))
            && ReferenceEquals(toolboxButton.Site?.Container, surface.ComponentContainer)
            && ReferenceEquals(host?.Container.Components[toolboxButton.Site?.Name], toolboxButton)
            && toolboxDesigner is IComponentInitializer
            && ReferenceEquals(toolboxButton.Parent, component)
            && component?.Controls.Contains(toolboxButton) == true
            && toolboxButton.Location == new System.Drawing.Point(24, 32)
            && toolboxButton.Size == new System.Drawing.Size(120, 28)
            && string.Equals(toolboxButton.Text, "Toolbox button", StringComparison.Ordinal)
            && host?.RootComponent is not null
            && host.GetDesigner(host.RootComponent) is IRootDesigner
            && ReferenceEquals(surface.View, host.RootComponent);
        if (toolboxButton is not null)
            host?.DestroyComponent(toolboxButton);
        toolboxCreation &= toolboxButton?.Site is null
            && toolboxButton?.Parent is null
            && (toolboxButton is null || component?.Controls.Contains(toolboxButton) == false)
            && (toolboxButton is null || host?.GetDesigner(toolboxButton) is null);

        DesignerSmokeTrackingDesigner.Reset();
        var attributedComponent = host?.CreateComponent(
            typeof(DesignerSmokeAttributedComponent),
            "attributedComponent1") as DesignerSmokeAttributedComponent;
        bool attributedDesigner = attributedComponent is not null
            && host?.GetDesigner(attributedComponent) is DesignerSmokeTrackingDesigner
            && DesignerSmokeTrackingDesigner.Initialized
            && ReferenceEquals(DesignerSmokeTrackingDesigner.DesignedComponent, attributedComponent);
        if (attributedComponent is not null)
            host?.DestroyComponent(attributedComponent);
        attributedDesigner &= DesignerSmokeTrackingDesigner.Disposed
            && attributedComponent?.Site is null
            && (attributedComponent is null || host?.GetDesigner(attributedComponent) is null);

        var interactionToolboxService = new DesignerSmokeToolboxService();
        (host as IServiceContainer)?.AddService(
            typeof(System.Drawing.Design.IToolboxService),
            interactionToolboxService);
        var interactionTool = new System.Drawing.Design.ToolboxItem(typeof(Forms.Button));
        interactionToolboxService.SetSelectedToolboxItem(interactionTool);
        int interactionComponentCount = host?.Container.Components.Count ?? 0;
        int transactionOpened = 0;
        int transactionClosed = 0;
        int runtimeMouseDown = 0;
        using DesignerSmokeUndoEngine? creationUndoEngine = host is null
            ? null
            : new DesignerSmokeUndoEngine(host);
        EventHandler transactionOpenedHandler = (_, _) => transactionOpened++;
        DesignerTransactionCloseEventHandler transactionClosedHandler = (_, _) => transactionClosed++;
        Forms.MouseEventHandler runtimeMouseDownHandler = (_, _) => runtimeMouseDown++;
        if (host is not null)
        {
            host.TransactionOpened += transactionOpenedHandler;
            host.TransactionClosed += transactionClosedHandler;
        }
        if (component is not null)
            component.MouseDown += runtimeMouseDownHandler;

        component?.RaiseMouseDown(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, 40, 48, 0));
        component?.RaiseMouseMove(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 0, 160, 80, 0));
        component?.RaiseMouseUp(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, 160, 80, 0));

        if (host is not null)
        {
            host.TransactionOpened -= transactionOpenedHandler;
            host.TransactionClosed -= transactionClosedHandler;
        }
        if (component is not null)
            component.MouseDown -= runtimeMouseDownHandler;

        Forms.Button? interactionButton = host?.Container.Components
            .Cast<IComponent>()
            .OfType<Forms.Button>()
            .SingleOrDefault();
        bool interactivePlacement = interactionButton is not null
            && ReferenceEquals(interactionButton.Parent, component)
            && interactionButton.Location == new System.Drawing.Point(40, 48)
            && interactionButton.Size == new System.Drawing.Size(120, 32)
            && ReferenceEquals(interactionButton.Site?.Container, host?.Container)
            && ReferenceEquals(host?.GetDesigner(interactionButton)?.Component, interactionButton)
            && selectionService?.GetComponentSelected(interactionButton) == true
            && interactionToolboxService.SelectedToolboxItemUsedCount == 1
            && interactionToolboxService.GetSelectedToolboxItem() is null
            && transactionOpened == 1
            && transactionClosed == 1
            && runtimeMouseDown == 0
            && component?.Capture == false
            && host?.Container.Components.Count == interactionComponentCount + 1;

        string? interactionButtonName = interactionButton?.Site?.Name;
        bool creationUndoCalled = creationUndoEngine?.UndoOnce() == true;
        bool creationRemovedSite = interactionButton?.Site is null;
        bool creationRemovedParent = interactionButton?.Parent is null;
        bool creationRemovedCount = host?.Container.Components.Count == interactionComponentCount;
        bool creationUndone = creationUndoCalled
            && creationRemovedSite
            && creationRemovedParent
            && creationRemovedCount;
        bool creationRedoCalled = creationUndoEngine?.RedoOnce() == true;
        interactionButton = string.IsNullOrEmpty(interactionButtonName)
            ? null
            : host?.Container.Components[interactionButtonName] as Forms.Button;
        bool creationRestoredComponent = interactionButton is not null;
        bool creationRestoredLocation = interactionButton?.Location == new System.Drawing.Point(40, 48);
        bool creationRestoredSize = interactionButton?.Size == new System.Drawing.Size(120, 32);
        bool creationRestoredParent = ReferenceEquals(interactionButton?.Parent, component);
        bool creationRestoredSite = ReferenceEquals(interactionButton?.Site?.Container, host?.Container);
        bool creationRestoredDesigner = interactionButton is not null
            && ReferenceEquals(host?.GetDesigner(interactionButton)?.Component, interactionButton);
        string creationRestoredParentName = interactionButton?.Parent?.Site?.Name ?? "(null)";
        string creationExpectedParentName = component?.Site?.Name ?? "(null)";
        bool interactiveCreationUndo = creationUndone
            && creationRedoCalled
            && interactionButton is not null
            && creationRestoredLocation
            && creationRestoredSize
            && creationRestoredParent
            && creationRestoredSite
            && creationRestoredDesigner
            && creationUndoEngine?.UndoCount == 1
            && creationUndoEngine.RedoCount == 0;
        creationUndoEngine?.Dispose();

        int manipulationTransactions = 0;
        int manipulationTransactionsClosed = 0;
        int locationChanging = 0;
        int locationChanged = 0;
        int sizeChanging = 0;
        int sizeChanged = 0;
        int manipulationRuntimeMouseDown = 0;
        using DesignerSmokeUndoEngine? manipulationUndoEngine = host is null
            ? null
            : new DesignerSmokeUndoEngine(host);
        int undoing = 0;
        int undone = 0;
        if (manipulationUndoEngine is not null)
        {
            manipulationUndoEngine.Undoing += (_, _) => undoing++;
            manipulationUndoEngine.Undone += (_, _) => undone++;
        }
        EventHandler manipulationTransactionOpenedHandler = (_, _) => manipulationTransactions++;
        DesignerTransactionCloseEventHandler manipulationTransactionClosedHandler = (_, _) => manipulationTransactionsClosed++;
        ComponentChangingEventHandler manipulationChangingHandler = (_, eventArgs) =>
        {
            if (!ReferenceEquals(eventArgs.Component, interactionButton))
                return;
            if (string.Equals(eventArgs.Member?.Name, nameof(Forms.Control.Location), StringComparison.Ordinal))
                locationChanging++;
            if (string.Equals(eventArgs.Member?.Name, nameof(Forms.Control.Size), StringComparison.Ordinal))
                sizeChanging++;
        };
        ComponentChangedEventHandler manipulationChangedHandler = (_, eventArgs) =>
        {
            if (!ReferenceEquals(eventArgs.Component, interactionButton))
                return;
            if (string.Equals(eventArgs.Member?.Name, nameof(Forms.Control.Location), StringComparison.Ordinal))
                locationChanged++;
            if (string.Equals(eventArgs.Member?.Name, nameof(Forms.Control.Size), StringComparison.Ordinal))
                sizeChanged++;
        };
        Forms.MouseEventHandler manipulationRuntimeMouseDownHandler = (_, _) => manipulationRuntimeMouseDown++;
        if (host is not null)
        {
            host.TransactionOpened += manipulationTransactionOpenedHandler;
            host.TransactionClosed += manipulationTransactionClosedHandler;
        }
        if (changeService is not null)
        {
            changeService.ComponentChanging += manipulationChangingHandler;
            changeService.ComponentChanged += manipulationChangedHandler;
        }
        if (interactionButton is not null)
            interactionButton.MouseDown += manipulationRuntimeMouseDownHandler;

        interactionButton?.RaiseMouseDown(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, 60, 16, 0));
        interactionButton?.RaiseMouseMove(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 0, 80, 26, 0));
        interactionButton?.RaiseMouseUp(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, 80, 26, 0));
        interactionButton?.RaiseMouseDown(new Forms.MouseEventArgs(
            Forms.MouseButtons.Left,
            1,
            interactionButton.Width,
            interactionButton.Height,
            0));
        interactionButton?.RaiseMouseMove(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 0, 150, 52, 0));
        interactionButton?.RaiseMouseUp(new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, 150, 52, 0));

        if (host is not null)
        {
            host.TransactionOpened -= manipulationTransactionOpenedHandler;
            host.TransactionClosed -= manipulationTransactionClosedHandler;
        }
        if (changeService is not null)
        {
            changeService.ComponentChanging -= manipulationChangingHandler;
            changeService.ComponentChanged -= manipulationChangedHandler;
        }
        if (interactionButton is not null)
            interactionButton.MouseDown -= manipulationRuntimeMouseDownHandler;

        bool interactiveManipulation = interactionButton is not null
            && interactionButton.Location == new System.Drawing.Point(60, 58)
            && interactionButton.Size == new System.Drawing.Size(150, 52)
            && manipulationTransactions == 2
            && manipulationTransactionsClosed == 2
            && locationChanging == 1
            && locationChanged == 1
            && sizeChanging == 1
            && sizeChanged == 1
            && manipulationRuntimeMouseDown == 0
            && interactionButton.Capture == false
            && selectionService?.GetComponentSelected(interactionButton) == true;

        bool resizeUndone = manipulationUndoEngine?.UndoOnce() == true
            && interactionButton?.Location == new System.Drawing.Point(60, 58)
            && interactionButton.Size == new System.Drawing.Size(120, 32);
        bool moveUndone = manipulationUndoEngine?.UndoOnce() == true
            && interactionButton?.Location == new System.Drawing.Point(40, 48)
            && interactionButton.Size == new System.Drawing.Size(120, 32);
        bool moveRedone = manipulationUndoEngine?.RedoOnce() == true
            && interactionButton?.Location == new System.Drawing.Point(60, 58)
            && interactionButton.Size == new System.Drawing.Size(120, 32);
        bool resizeRedone = manipulationUndoEngine?.RedoOnce() == true
            && interactionButton?.Location == new System.Drawing.Point(60, 58)
            && interactionButton.Size == new System.Drawing.Size(150, 52);
        bool interactiveUndo = resizeUndone
            && moveUndone
            && moveRedone
            && resizeRedone
            && manipulationUndoEngine?.UndoCount == 2
            && manipulationUndoEngine.RedoCount == 0
            && manipulationUndoEngine.UndoInProgress == false
            && undoing == 4
            && undone == 4;
        manipulationUndoEngine?.Dispose();

        MenuCommand? copyCommand = menuCommandService?.FindCommand(StandardCommands.Copy);
        MenuCommand? cutCommand = menuCommandService?.FindCommand(StandardCommands.Cut);
        MenuCommand? deleteCommand = menuCommandService?.FindCommand(StandardCommands.Delete);
        MenuCommand? pasteCommand = menuCommandService?.FindCommand(StandardCommands.Paste);
        MenuCommand? selectAllCommand = menuCommandService?.FindCommand(StandardCommands.SelectAll);
        if (host?.RootComponent is IComponent rootComponent)
            selectionService?.SetSelectedComponents(new object[] { rootComponent }, SelectionTypes.Replace);
        bool rootEditingProtected = copyCommand?.Enabled == false
            && cutCommand?.Enabled == false
            && deleteCommand?.Enabled == false
            && menuCommandService?.GlobalInvoke(StandardCommands.Copy) == false
            && menuCommandService?.GlobalInvoke(StandardCommands.Cut) == false
            && menuCommandService?.GlobalInvoke(StandardCommands.Delete) == false;
        if (host?.RootComponent is IComponent mixedRoot && interactionButton is not null)
        {
            selectionService?.SetSelectedComponents(
                new object[] { mixedRoot, interactionButton },
                SelectionTypes.Replace);
        }
        bool mixedRootEditingProtected = interactionButton is not null
            && cutCommand?.Enabled == false
            && deleteCommand?.Enabled == false
            && menuCommandService?.GlobalInvoke(StandardCommands.Cut) == false
            && menuCommandService?.GlobalInvoke(StandardCommands.Delete) == false
            && ReferenceEquals(interactionButton.Site?.Container, host?.Container);

        var mixedSelectionButton = host?.CreateComponent(
            typeof(Forms.Button),
            "mixedSelectionButton1") as Forms.Button;
        if (mixedSelectionButton is not null && component is not null)
            component.Controls.Add(mixedSelectionButton);
        TypeDescriptionProvider? inheritedProvider = interactionButton is null
            ? null
            : TypeDescriptor.AddAttributes(interactionButton, InheritanceAttribute.Inherited);
        if (interactionButton is not null && mixedSelectionButton is not null)
        {
            selectionService?.SetSelectedComponents(
                new object[] { interactionButton, mixedSelectionButton },
                SelectionTypes.Replace);
        }
        bool mixedInheritedEditingProtected = interactionButton is not null
            && mixedSelectionButton is not null
            && cutCommand?.Enabled == false
            && deleteCommand?.Enabled == false
            && menuCommandService?.GlobalInvoke(StandardCommands.Cut) == false
            && menuCommandService?.GlobalInvoke(StandardCommands.Delete) == false
            && ReferenceEquals(interactionButton.Site?.Container, host?.Container)
            && ReferenceEquals(mixedSelectionButton.Site?.Container, host?.Container);
        if (interactionButton is not null && inheritedProvider is not null)
            TypeDescriptor.RemoveProvider(inheritedProvider, interactionButton);
        if (mixedSelectionButton is not null)
            host?.DestroyComponent(mixedSelectionButton);

        bool selectAllInvoked = menuCommandService?.GlobalInvoke(StandardCommands.SelectAll) == true;
        bool selectAllSelectedButton = interactionButton is not null
            && selectionService?.GetComponentSelected(interactionButton) == true
            && component is not null
            && selectionService.GetComponentSelected(component)
            && host is not null
            && selectionService.SelectionCount == host.Container.Components.Count - 1;
        if (interactionButton is not null)
        {
            selectionService?.SetSelectedComponents(
                new object[] { interactionButton },
                SelectionTypes.Replace);
        }

        const string clipboardButtonText = "Designer clipboard button";
        const string clipboardClickHandler = "designerClipboardButton_Click";
        if (interactionButton is not null)
        {
            interactionButton.Text = clipboardButtonText;
            if (host?.GetService(typeof(IEventBindingService)) is IEventBindingService eventBindingService
                && TypeDescriptor.GetEvents(interactionButton)[nameof(Forms.Control.Click)] is EventDescriptor clickEvent)
            {
                eventBindingService.GetEventProperty(clickEvent).SetValue(interactionButton, clipboardClickHandler);
            }
        }

        bool copyEnabled = copyCommand?.Enabled == true;
        bool copyInvoked = menuCommandService?.GlobalInvoke(StandardCommands.Copy) == true;
        bool pasteEnabled = pasteCommand?.Enabled == true;
        bool pasteInvoked = menuCommandService?.GlobalInvoke(StandardCommands.Paste) == true;
        Forms.Button? copiedButton = selectionService?.PrimarySelection as Forms.Button;
        string? copiedButtonName = copiedButton?.Site?.Name;
        string? copiedClickHandler = null;
        if (copiedButton is not null
            && host?.GetService(typeof(IEventBindingService)) is IEventBindingService copiedEventBindingService
            && TypeDescriptor.GetEvents(copiedButton)[nameof(Forms.Control.Click)] is EventDescriptor copiedClickEvent)
        {
            copiedClickHandler = copiedEventBindingService.GetEventProperty(copiedClickEvent).GetValue(copiedButton) as string;
        }

        bool copyRestoredState = copiedButton is not null
            && !ReferenceEquals(copiedButton, interactionButton)
            && ReferenceEquals(copiedButton.Parent, component)
            && copiedButton.Location == new System.Drawing.Point(70, 68)
            && copiedButton.Size == new System.Drawing.Size(150, 52)
            && string.Equals(copiedButton.Text, clipboardButtonText, StringComparison.Ordinal)
            && copiedClickHandler is null
            && !string.IsNullOrEmpty(copiedButtonName)
            && !string.Equals(copiedButtonName, interactionButton?.Site?.Name, StringComparison.Ordinal)
            && host?.Container.Components.Count == interactionComponentCount + 2;
        bool cutEnabled = cutCommand?.Enabled == true;
        bool cutInvoked = menuCommandService?.GlobalInvoke(StandardCommands.Cut) == true;
        bool cutRemovedCopy = copiedButton?.Site is null
            && copiedButton?.Parent is null
            && host?.Container.Components.Count == interactionComponentCount + 1;
        bool cutPasteEnabled = pasteCommand?.Enabled == true;
        bool cutPasteInvoked = menuCommandService?.GlobalInvoke(StandardCommands.Paste) == true;
        Forms.Button? cutPastedButton = selectionService?.PrimarySelection as Forms.Button;
        bool cutPasteRestored = cutPastedButton is not null
            && !ReferenceEquals(cutPastedButton, interactionButton)
            && ReferenceEquals(cutPastedButton.Parent, component)
            && cutPastedButton.Location == new System.Drawing.Point(80, 78)
            && cutPastedButton.Size == new System.Drawing.Size(150, 52)
            && string.Equals(cutPastedButton.Text, clipboardButtonText, StringComparison.Ordinal)
            && string.Equals(cutPastedButton.Site?.Name, copiedButtonName, StringComparison.Ordinal)
            && host?.Container.Components.Count == interactionComponentCount + 2;
        if (cutPastedButton is not null)
            host?.DestroyComponent(cutPastedButton);
        if (interactionButton is not null)
        {
            selectionService?.SetSelectedComponents(
                new object[] { interactionButton },
                SelectionTypes.Replace);
        }

        bool interactiveClipboardCommands = copyEnabled
            && copyInvoked
            && pasteEnabled
            && pasteInvoked
            && copyRestoredState
            && cutEnabled
            && cutInvoked
            && cutRemovedCopy
            && cutPasteEnabled
            && cutPasteInvoked
            && cutPasteRestored
            && cutPastedButton?.Site is null
            && cutPastedButton?.Parent is null
            && host?.Container.Components.Count == interactionComponentCount + 1;

        const string graphTabName = "graphTabControl1";
        const string graphPageOneName = "graphTabPage1";
        const string graphPageTwoName = "graphTabPage2";
        var graphTabs = host?.CreateComponent(typeof(Forms.TabControl), graphTabName) as Forms.TabControl;
        var graphPageOne = host?.CreateComponent(typeof(Forms.TabPage), graphPageOneName) as Forms.TabPage;
        var graphPageTwo = host?.CreateComponent(typeof(Forms.TabPage), graphPageTwoName) as Forms.TabPage;
        if (graphTabs is not null && graphPageOne is not null && graphPageTwo is not null && component is not null)
        {
            component.Controls.Add(graphTabs);
            graphTabs.Controls.Add(graphPageOne);
            graphTabs.Controls.Add(graphPageTwo);
            graphTabs.Location = new System.Drawing.Point(16, 20);
            graphTabs.Size = new System.Drawing.Size(220, 140);
            graphPageOne.Text = "First page";
            graphPageTwo.Text = "Second page";
            graphTabs.SelectedTab = graphPageTwo;
            selectionService?.SetSelectedComponents(new object[] { graphTabs }, SelectionTypes.Replace);
        }

        bool graphCopied = menuCommandService?.GlobalInvoke(StandardCommands.Copy) == true;
        using DesignerSmokeUndoEngine? graphPasteUndoEngine = host is null
            ? null
            : new DesignerSmokeUndoEngine(host);
        bool graphPasted = menuCommandService?.GlobalInvoke(StandardCommands.Paste) == true;
        var pastedGraphTabs = selectionService?.PrimarySelection as Forms.TabControl;
        Forms.TabPage? pastedGraphPageOne = pastedGraphTabs?.TabPages.Count > 0
            ? pastedGraphTabs.TabPages[0]
            : null;
        Forms.TabPage? pastedGraphPageTwo = pastedGraphTabs?.TabPages.Count > 1
            ? pastedGraphTabs.TabPages[1]
            : null;
        bool graphReferenceRemapped = graphCopied
            && graphPasted
            && pastedGraphTabs is not null
            && !ReferenceEquals(pastedGraphTabs, graphTabs)
            && ReferenceEquals(pastedGraphTabs.Parent, component)
            && pastedGraphTabs.TabPages.Count == 2
            && pastedGraphTabs.SelectedIndex == 1
            && ReferenceEquals(pastedGraphTabs.SelectedTab, pastedGraphPageTwo)
            && !ReferenceEquals(pastedGraphPageOne, graphPageOne)
            && !ReferenceEquals(pastedGraphPageTwo, graphPageTwo)
            && string.Equals(pastedGraphPageOne?.Text, "First page", StringComparison.Ordinal)
            && string.Equals(pastedGraphPageTwo?.Text, "Second page", StringComparison.Ordinal);
        string? pastedGraphTabName = pastedGraphTabs?.Site?.Name;
        string? pastedGraphPageOneName = pastedGraphPageOne?.Site?.Name;
        string? pastedGraphPageTwoName = pastedGraphPageTwo?.Site?.Name;
        bool graphPasteUndoCalled = graphPasteUndoEngine?.UndoOnce() == true;
        bool graphPasteUndone = graphPasteUndoCalled
            && pastedGraphTabs?.Site is null
            && pastedGraphPageOne?.Site is null
            && pastedGraphPageTwo?.Site is null;
        bool graphPasteRedoCalled = graphPasteUndoEngine?.RedoOnce() == true;
        pastedGraphTabs = string.IsNullOrEmpty(pastedGraphTabName)
            ? null
            : host?.Container.Components[pastedGraphTabName] as Forms.TabControl;
        pastedGraphPageOne = string.IsNullOrEmpty(pastedGraphPageOneName)
            ? null
            : host?.Container.Components[pastedGraphPageOneName] as Forms.TabPage;
        pastedGraphPageTwo = string.IsNullOrEmpty(pastedGraphPageTwoName)
            ? null
            : host?.Container.Components[pastedGraphPageTwoName] as Forms.TabPage;
        bool graphPasteRedone = graphPasteRedoCalled
            && pastedGraphTabs is not null
            && pastedGraphPageOne is not null
            && pastedGraphPageTwo is not null
            && pastedGraphTabs.TabPages.Count == 2
            && ReferenceEquals(pastedGraphPageOne.Parent, pastedGraphTabs)
            && ReferenceEquals(pastedGraphPageTwo.Parent, pastedGraphTabs)
            && pastedGraphTabs.SelectedIndex == 1
            && ReferenceEquals(pastedGraphTabs.SelectedTab, pastedGraphPageTwo);
        graphPasteUndoEngine?.Dispose();
        if (pastedGraphTabs is not null)
            selectionService?.SetSelectedComponents(new object[] { pastedGraphTabs }, SelectionTypes.Replace);
        bool pastedGraphDeleted = menuCommandService?.GlobalInvoke(StandardCommands.Delete) == true
            && pastedGraphTabs?.Site is null
            && pastedGraphPageOne?.Site is null
            && pastedGraphPageTwo?.Site is null;

        using DesignerSmokeUndoEngine? graphDeleteUndoEngine = host is null
            ? null
            : new DesignerSmokeUndoEngine(host);
        if (graphTabs is not null)
            selectionService?.SetSelectedComponents(new object[] { graphTabs }, SelectionTypes.Replace);
        bool graphDeleteInvoked = menuCommandService?.GlobalInvoke(StandardCommands.Delete) == true;
        bool graphSubtreeDeleted = graphTabs?.Site is null
            && graphPageOne?.Site is null
            && graphPageTwo?.Site is null;
        bool graphUndoCalled = graphDeleteUndoEngine?.UndoOnce() == true;
        var restoredGraphTabs = host?.Container.Components[graphTabName] as Forms.TabControl;
        var restoredGraphPageOne = host?.Container.Components[graphPageOneName] as Forms.TabPage;
        var restoredGraphPageTwo = host?.Container.Components[graphPageTwoName] as Forms.TabPage;
        bool graphUndoRestored = graphUndoCalled
            && restoredGraphTabs is not null
            && restoredGraphPageOne is not null
            && restoredGraphPageTwo is not null
            && restoredGraphTabs.TabPages.Count == 2
            && ReferenceEquals(restoredGraphPageOne.Parent, restoredGraphTabs)
            && ReferenceEquals(restoredGraphPageTwo.Parent, restoredGraphTabs)
            && restoredGraphTabs.SelectedIndex == 1
            && ReferenceEquals(restoredGraphTabs.SelectedTab, restoredGraphPageTwo)
            && string.Equals(restoredGraphPageOne.Text, "First page", StringComparison.Ordinal)
            && string.Equals(restoredGraphPageTwo.Text, "Second page", StringComparison.Ordinal);
        bool graphRedoCalled = graphDeleteUndoEngine?.RedoOnce() == true;
        bool interactiveComponentGraph = graphReferenceRemapped
            && graphPasteUndone
            && graphPasteRedone
            && pastedGraphDeleted
            && graphDeleteInvoked
            && graphSubtreeDeleted
            && graphUndoRestored
            && graphRedoCalled
            && restoredGraphTabs?.Site is null
            && restoredGraphPageOne?.Site is null
            && restoredGraphPageTwo?.Site is null
            && host?.Container.Components.Count == interactionComponentCount + 1;
        graphDeleteUndoEngine?.Dispose();

        var graphImageList = host?.CreateComponent(
            typeof(Forms.ImageList),
            "graphImageList1") as Forms.ImageList;
        var graphTreeView = host?.CreateComponent(
            typeof(Forms.TreeView),
            "graphTreeView1") as Forms.TreeView;
        if (graphTreeView is not null && graphImageList is not null && component is not null)
        {
            graphTreeView.ImageList = graphImageList;
            component.Controls.Add(graphTreeView);
            selectionService?.SetSelectedComponents(new object[] { graphTreeView }, SelectionTypes.Replace);
        }

        bool referencedGraphCopied = menuCommandService?.GlobalInvoke(StandardCommands.Copy) == true;
        bool referencedGraphPasted = menuCommandService?.GlobalInvoke(StandardCommands.Paste) == true;
        var pastedGraphTreeView = selectionService?.PrimarySelection as Forms.TreeView;
        Forms.ImageList? pastedGraphImageList = pastedGraphTreeView?.ImageList;
        bool referencedGraphRemapped = referencedGraphCopied
            && referencedGraphPasted
            && pastedGraphTreeView is not null
            && pastedGraphImageList is not null
            && !ReferenceEquals(pastedGraphTreeView, graphTreeView)
            && !ReferenceEquals(pastedGraphImageList, graphImageList)
            && ReferenceEquals(pastedGraphTreeView.Parent, component)
            && ReferenceEquals(pastedGraphImageList.Site?.Container, host?.Container)
            && selectionService?.GetComponentSelected(pastedGraphImageList) == true;
        bool pastedReferencedGraphDeleted = menuCommandService?.GlobalInvoke(StandardCommands.Delete) == true
            && pastedGraphTreeView?.Site is null
            && pastedGraphImageList?.Site is null;
        if (graphTreeView is not null && graphImageList is not null)
        {
            selectionService?.SetSelectedComponents(
                new object[] { graphTreeView, graphImageList },
                SelectionTypes.Replace);
        }
        bool originalReferencedGraphDeleted = menuCommandService?.GlobalInvoke(StandardCommands.Delete) == true
            && graphTreeView?.Site is null
            && graphImageList?.Site is null;
        bool interactiveReferencedComponentGraph = referencedGraphRemapped
            && pastedReferencedGraphDeleted
            && originalReferencedGraphDeleted
            && host?.Container.Components.Count == interactionComponentCount + 1;

        if (interactionButton is not null)
            selectionService?.SetSelectedComponents(new object[] { interactionButton }, SelectionTypes.Replace);

        const string deletionPanelName = "deletionPanel1";
        const string deletionChildName = "deletionChild1";
        var deletionPanel = host?.CreateComponent(typeof(Forms.Panel), deletionPanelName) as Forms.Panel;
        var deletionChild = host?.CreateComponent(typeof(Forms.Button), deletionChildName) as Forms.Button;
        if (deletionPanel is not null && deletionChild is not null && component is not null)
        {
            component.Controls.Add(deletionPanel);
            deletionPanel.Controls.Add(deletionChild);
            deletionPanel.Location = new System.Drawing.Point(12, 18);
            deletionPanel.Size = new System.Drawing.Size(180, 90);
            deletionChild.Location = new System.Drawing.Point(8, 10);
            deletionChild.Size = new System.Drawing.Size(90, 24);
            deletionChild.Text = "Nested delete";
        }

        using DesignerSmokeUndoEngine? containerDeleteUndoEngine = host is null
            ? null
            : new DesignerSmokeUndoEngine(host);
        if (deletionPanel is not null)
        {
            selectionService?.SetSelectedComponents(
                new object[] { deletionPanel },
                SelectionTypes.Replace);
        }

        bool containerDeleteEnabled = deleteCommand?.Enabled == true;
        bool containerDeleteInvoked = menuCommandService?.GlobalInvoke(StandardCommands.Delete) == true;
        bool containerSubtreeDeleted = deletionPanel?.Site is null
            && deletionPanel?.Parent is null
            && deletionChild?.Site is null
            && deletionChild?.Parent is null
            && host?.Container.Components.Count == interactionComponentCount + 1;
        bool containerDeleteUndoCalled = containerDeleteUndoEngine?.UndoOnce() == true;
        var restoredDeletionPanel = host?.Container.Components[deletionPanelName] as Forms.Panel;
        var restoredDeletionChild = host?.Container.Components[deletionChildName] as Forms.Button;
        bool containerSubtreeRestored = containerDeleteUndoCalled
            && restoredDeletionPanel is not null
            && restoredDeletionChild is not null
            && ReferenceEquals(restoredDeletionPanel.Parent, component)
            && ReferenceEquals(restoredDeletionChild.Parent, restoredDeletionPanel)
            && restoredDeletionPanel.Location == new System.Drawing.Point(12, 18)
            && restoredDeletionPanel.Size == new System.Drawing.Size(180, 90)
            && restoredDeletionChild.Location == new System.Drawing.Point(8, 10)
            && restoredDeletionChild.Size == new System.Drawing.Size(90, 24)
            && string.Equals(restoredDeletionChild.Text, "Nested delete", StringComparison.Ordinal)
            && selectionService?.GetComponentSelected(restoredDeletionPanel) == true
            && host?.Container.Components.Count == interactionComponentCount + 3;
        bool containerDeleteRedoCalled = containerDeleteUndoEngine?.RedoOnce() == true;
        bool interactiveContainerDelete = containerDeleteEnabled
            && containerDeleteInvoked
            && containerSubtreeDeleted
            && containerSubtreeRestored
            && containerDeleteRedoCalled
            && restoredDeletionPanel?.Site is null
            && restoredDeletionPanel?.Parent is null
            && restoredDeletionChild?.Site is null
            && restoredDeletionChild?.Parent is null
            && host?.Container.Components.Count == interactionComponentCount + 1
            && containerDeleteUndoEngine?.UndoCount == 1
            && containerDeleteUndoEngine.RedoCount == 0;
        containerDeleteUndoEngine?.Dispose();
        if (interactionButton is not null)
        {
            selectionService?.SetSelectedComponents(
                new object[] { interactionButton },
                SelectionTypes.Replace);
        }

        string? removalButtonName = interactionButton?.Site?.Name;
        using DesignerSmokeUndoEngine? removalUndoEngine = host is null
            ? null
            : new DesignerSmokeUndoEngine(host);
        bool deleteEnabled = deleteCommand?.Enabled == true;
        bool deleteInvoked = menuCommandService?.GlobalInvoke(StandardCommands.Delete) == true;
        bool removalCompleted = interactionButton?.Site is null
            && interactionButton?.Parent is null
            && host?.Container.Components.Count == interactionComponentCount
            && ReferenceEquals(selectionService?.PrimarySelection, component);
        bool removalUndoCalled = removalUndoEngine?.UndoOnce() == true;
        interactionButton = string.IsNullOrEmpty(removalButtonName)
            ? null
            : host?.Container.Components[removalButtonName] as Forms.Button;
        bool removalRestored = removalUndoCalled
            && interactionButton is not null
            && interactionButton.Location == new System.Drawing.Point(60, 58)
            && interactionButton.Size == new System.Drawing.Size(150, 52)
            && ReferenceEquals(interactionButton.Parent, component)
            && ReferenceEquals(interactionButton.Site?.Container, host?.Container)
            && ReferenceEquals(host?.GetDesigner(interactionButton)?.Component, interactionButton);
        string? restoredClickHandler = null;
        if (interactionButton is not null
            && host?.GetService(typeof(IEventBindingService)) is IEventBindingService restoredEventBindingService
            && TypeDescriptor.GetEvents(interactionButton)[nameof(Forms.Control.Click)] is EventDescriptor restoredClickEvent)
        {
            restoredClickHandler = restoredEventBindingService.GetEventProperty(restoredClickEvent).GetValue(interactionButton) as string;
        }
        removalRestored &= string.Equals(restoredClickHandler, clipboardClickHandler, StringComparison.Ordinal);
        bool removalRedoCalled = removalUndoEngine?.RedoOnce() == true;
        bool removalRedoCompleted = interactionButton?.Site is null
            && interactionButton?.Parent is null
            && host?.Container.Components.Count == interactionComponentCount;
        bool removalSecondUndoCalled = removalUndoEngine?.UndoOnce() == true;
        interactionButton = string.IsNullOrEmpty(removalButtonName)
            ? null
            : host?.Container.Components[removalButtonName] as Forms.Button;
        string? secondRestoredClickHandler = null;
        if (interactionButton is not null
            && host?.GetService(typeof(IEventBindingService)) is IEventBindingService secondRestoredEventBindingService
            && TypeDescriptor.GetEvents(interactionButton)[nameof(Forms.Control.Click)] is EventDescriptor secondRestoredClickEvent)
        {
            secondRestoredClickHandler = secondRestoredEventBindingService
                .GetEventProperty(secondRestoredClickEvent)
                .GetValue(interactionButton) as string;
        }
        bool interactiveRemovalUndo = removalCompleted
            && removalRestored
            && removalRedoCalled
            && removalRedoCompleted
            && removalSecondUndoCalled
            && interactionButton is not null
            && ReferenceEquals(interactionButton.Parent, component)
            && ReferenceEquals(interactionButton.Site?.Container, host?.Container)
            && string.Equals(secondRestoredClickHandler, clipboardClickHandler, StringComparison.Ordinal)
            && removalUndoEngine?.UndoCount == 0
            && removalUndoEngine.RedoCount == 1;
        bool interactiveStandardCommands = ReferenceEquals(menuCommandService, externalMenuCommandService)
            && copyCommand is not null
            && cutCommand is not null
            && deleteCommand is not null
            && pasteCommand is not null
            && selectAllCommand is not null
            && rootEditingProtected
            && mixedRootEditingProtected
            && mixedInheritedEditingProtected
            && selectAllInvoked
            && selectAllSelectedButton
            && interactiveClipboardCommands
            && interactiveComponentGraph
            && interactiveReferencedComponentGraph
            && interactiveContainerDelete
            && deleteEnabled
            && deleteInvoked;
        removalUndoEngine?.Dispose();
        if (interactionButton is not null)
            host?.DestroyComponent(interactionButton);
        bool crossSurfaceClipboardActivation = RunCrossSurfaceClipboardActivationSmoke();
        interactivePlacement &= interactionButton?.Site is null
            && interactionButton?.Parent is null
            && (interactionButton is null || component?.Controls.Contains(interactionButton) == false)
            && host?.Container.Components.Count == interactionComponentCount;

        var nestedContainer = component?.Site?.GetService(typeof(INestedContainer)) as INestedContainer;
        var nestedComponent = new Component();
        bool nestedAdding = false;
        bool nestedAdded = false;
        ComponentEventHandler nestedAddingHandler = (sender, eventArgs) =>
        {
            nestedAdding |= ReferenceEquals(sender, nestedContainer)
                && ReferenceEquals(eventArgs.Component, nestedComponent);
        };
        ComponentEventHandler nestedAddedHandler = (sender, eventArgs) =>
        {
            nestedAdded |= ReferenceEquals(sender, nestedContainer)
                && ReferenceEquals(eventArgs.Component, nestedComponent);
        };
        if (changeService is not null)
        {
            changeService.ComponentAdding += nestedAddingHandler;
            changeService.ComponentAdded += nestedAddedHandler;
        }

        nestedContainer?.Add(nestedComponent, "nestedComponent1");
        if (changeService is not null)
        {
            changeService.ComponentAdding -= nestedAddingHandler;
            changeService.ComponentAdded -= nestedAddedHandler;
        }

        const string originalNestedName = originalName + ".nestedComponent1";
        bool nestedOwner = ReferenceEquals(nestedContainer?.Owner, component);
        bool nestedSite = ReferenceEquals(nestedComponent.Site?.Container, nestedContainer)
            && nestedComponent.Site?.DesignMode == true
            && string.Equals((nestedComponent.Site as INestedSite)?.FullName, originalNestedName, StringComparison.Ordinal);
        bool nestedHasHost = ReferenceEquals(nestedComponent.Site?.GetService(typeof(IDesignerHost)), host);
        bool nestedHasContainer = ReferenceEquals(
            nestedComponent.Site?.GetService(typeof(IContainer)),
            host?.Container);
        bool nestedHasChangeService = ReferenceEquals(
            nestedComponent.Site?.GetService(typeof(IComponentChangeService)),
            changeService);
        bool nestedHasSiteLocalService = ReferenceEquals(
            nestedComponent.Site?.GetService(typeof(DesignerSmokeService)),
            localService);
        bool nestedSerialization = string.Equals(
                serializationManager?.GetName(nestedComponent),
                originalNestedName,
                StringComparison.Ordinal)
            && ReferenceEquals(serializationManager?.GetInstance(originalNestedName), nestedComponent);

        const string namedNestedName = originalName + ".tools.namedComponent1";
        INestedContainer? namedNestedContainer = component is null
            ? null
            : surface.CreateNestedContainer(component, "tools");
        var namedNestedComponent = new Component();
        namedNestedContainer?.Add(namedNestedComponent, "namedComponent1");
        bool namedNested = ReferenceEquals(namedNestedContainer?.Owner, component)
            && ReferenceEquals(namedNestedComponent.Site?.Container, namedNestedContainer)
            && string.Equals((namedNestedComponent.Site as INestedSite)?.FullName, namedNestedName, StringComparison.Ordinal)
            && ReferenceEquals(namedNestedComponent.Site?.GetService(typeof(IDesignerHost)), host)
            && ReferenceEquals(namedNestedComponent.Site?.GetService(typeof(IComponentChangeService)), changeService)
            && ReferenceEquals(namedNestedComponent.Site?.GetService(typeof(DesignerSmokeService)), localService);
        namedNestedContainer?.Remove(namedNestedComponent);
        namedNestedContainer?.Dispose();
        bool namedNestedRemoved = namedNestedComponent.Site is null
            && serializationManager?.GetInstance(namedNestedName) is null;

        if (component is not null && textProperty is not null)
        {
            changeService?.OnComponentChanging(component, textProperty);
            textProperty.SetValue(component, updatedText);
            changeService?.OnComponentChanged(component, textProperty, string.Empty, updatedText);
            selectionService?.SetSelectedComponents(new object[] { component }, SelectionTypes.Replace);
        }

        bool selected = component is not null && selectionService?.GetComponentSelected(component) == true;
        surface.Flush();
        bool persisted = loader.ContainsPropertyAssignment(originalName, nameof(Forms.Control.Text), updatedText);

        if (component?.Site is not null)
        {
            component.Site.Name = renamedName;
        }

        bool renamed = component is not null
            && ReferenceEquals(host?.Container.Components[renamedName], component)
            && host?.Container.Components[originalName] is null
            && ReferenceEquals(serializationManager?.GetInstance(renamedName), component)
            && serializationManager?.GetInstance(originalName) is null;
        const string renamedNestedName = renamedName + ".nestedComponent1";
        bool nestedRenamed = string.Equals(
                (nestedComponent.Site as INestedSite)?.FullName,
                renamedNestedName,
                StringComparison.Ordinal)
            && string.Equals(serializationManager?.GetName(nestedComponent), renamedNestedName, StringComparison.Ordinal)
            && ReferenceEquals(serializationManager?.GetInstance(renamedNestedName), nestedComponent)
            && serializationManager?.GetInstance(originalNestedName) is null;

        bool success = surface.IsLoaded
            && component is not null
            && siteHasChangeService
            && siteHasHost
            && siteHasContainer
            && siteLocalService
            && siteDictionary
            && directLifecycle
            && toolboxCreation
            && attributedDesigner
            && interactivePlacement
            && interactiveCreationUndo
            && interactiveManipulation
            && interactiveUndo
            && interactiveRemovalUndo
            && interactiveStandardCommands
            && interactiveClipboardCommands
            && interactiveContainerDelete
            && crossSurfaceClipboardActivation
            && nestedContainer is not null
            && nestedOwner
            && nestedSite
            && nestedAdding
            && nestedAdded
            && nestedHasHost
            && nestedHasContainer
            && nestedHasChangeService
            && nestedHasSiteLocalService
            && nestedSerialization
            && namedNested
            && namedNestedRemoved
            && selected
            && persisted
            && renamed
            && nestedRenamed;
        Console.WriteLine(
            "LibreWinForms SDK designer smoke result=" + (success ? "Success" : "Partial")
            + $" loaded={surface.IsLoaded} component={component is not null}"
            + $" siteHasChangeService={siteHasChangeService} siteHasHost={siteHasHost} siteHasContainer={siteHasContainer}"
            + $" siteLocalService={siteLocalService} siteDictionary={siteDictionary} directLifecycle={directLifecycle}"
            + $" toolboxCreation={toolboxCreation} attributedDesigner={attributedDesigner} selected={selected}"
            + $" interactivePlacement={interactivePlacement}"
            + $" interactiveCreationUndo={interactiveCreationUndo}"
            + $" creationUndoCalled={creationUndoCalled} creationRemovedSite={creationRemovedSite}"
            + $" creationRemovedParent={creationRemovedParent} creationRemovedCount={creationRemovedCount}"
            + $" creationRedoCalled={creationRedoCalled} creationRestoredComponent={creationRestoredComponent}"
            + $" creationRestoredLocation={creationRestoredLocation} creationRestoredSize={creationRestoredSize}"
            + $" creationRestoredParent={creationRestoredParent} creationRestoredSite={creationRestoredSite}"
            + $" creationRestoredDesigner={creationRestoredDesigner}"
            + $" creationRestoredParentName={creationRestoredParentName}"
            + $" creationExpectedParentName={creationExpectedParentName}"
            + $" interactiveManipulation={interactiveManipulation}"
            + $" interactiveUndo={interactiveUndo}"
            + $" interactiveRemovalUndo={interactiveRemovalUndo}"
            + $" interactiveStandardCommands={interactiveStandardCommands}"
            + $" interactiveClipboardCommands={interactiveClipboardCommands}"
            + $" interactiveComponentGraph={interactiveComponentGraph}"
            + $" interactiveReferencedComponentGraph={interactiveReferencedComponentGraph}"
            + $" interactiveContainerDelete={interactiveContainerDelete}"
            + $" crossSurfaceClipboardActivation={crossSurfaceClipboardActivation}"
            + $" nestedOwner={nestedOwner} nestedSite={nestedSite} nestedAdding={nestedAdding} nestedAdded={nestedAdded}"
            + $" nestedHasHost={nestedHasHost} nestedHasContainer={nestedHasContainer}"
            + $" nestedHasChangeService={nestedHasChangeService}"
            + $" nestedHasSiteLocalService={nestedHasSiteLocalService} nestedSerialization={nestedSerialization}"
            + $" namedNested={namedNested} namedNestedRemoved={namedNestedRemoved}"
            + $" persisted={persisted} renamed={renamed} nestedRenamed={nestedRenamed}");
        return success ? 0 : 4;
    }

    private static bool RunCrossSurfaceClipboardActivationSmoke()
    {
        Forms.Clipboard.Clear();
        using var servicesA = new ServiceContainer();
        using var servicesB = new ServiceContainer();
        var commandsA = new MenuCommandService(servicesA);
        var commandsB = new MenuCommandService(servicesB);
        servicesA.AddService(typeof(IMenuCommandService), commandsA);
        servicesB.AddService(typeof(IMenuCommandService), commandsB);
        using var manager = new DesignSurfaceManager();
        DesignSurface surfaceA = manager.CreateDesignSurface(servicesA);
        DesignSurface surfaceB = manager.CreateDesignSurface(servicesB);
        surfaceA.BeginLoad(new DesignerSmokeLoader());
        surfaceB.BeginLoad(new DesignerSmokeLoader());

        var hostA = surfaceA.GetService(typeof(IDesignerHost)) as IDesignerHost;
        var hostB = surfaceB.GetService(typeof(IDesignerHost)) as IDesignerHost;
        var selectionA = surfaceA.GetService(typeof(ISelectionService)) as ISelectionService;
        var selectionB = surfaceB.GetService(typeof(ISelectionService)) as ISelectionService;
        var rootA = hostA?.RootComponent as Forms.Control;
        var rootB = hostB?.RootComponent as Forms.Control;
        var copiedButton = hostA?.CreateComponent(
            typeof(Forms.Button),
            "crossSurfaceButton1") as Forms.Button;
        if (copiedButton is not null && rootA is not null)
        {
            copiedButton.Text = "Cross-surface clipboard";
            rootA.Controls.Add(copiedButton);
        }

        selectionB?.SetSelectedComponents(
            rootB is null ? Array.Empty<object>() : new object[] { rootB },
            SelectionTypes.Replace);
        MenuCommand? pasteB = commandsB.FindCommand(StandardCommands.Paste);
        bool initiallyDisabled = pasteB?.Enabled == false;

        selectionA?.SetSelectedComponents(
            copiedButton is null ? Array.Empty<object>() : new object[] { copiedButton },
            SelectionTypes.Replace);
        manager.ActiveDesignSurface = surfaceA;
        bool copied = commandsA.GlobalInvoke(StandardCommands.Copy);
        bool staleBeforeActivation = pasteB?.Enabled == false;

        manager.ActiveDesignSurface = surfaceB;
        bool enabledOnActivation = pasteB?.Enabled == true;
        bool pasted = commandsB.GlobalInvoke(StandardCommands.Paste);
        var pastedButton = selectionB?.PrimarySelection as Forms.Button;
        bool restored = pastedButton is not null
            && !ReferenceEquals(pastedButton, copiedButton)
            && ReferenceEquals(pastedButton.Parent, rootB)
            && string.Equals(pastedButton.Text, "Cross-surface clipboard", StringComparison.Ordinal);

        Forms.Clipboard.Clear();
        return surfaceA.IsLoaded
            && surfaceB.IsLoaded
            && initiallyDisabled
            && copied
            && staleBeforeActivation
            && enabledOnActivation
            && pasted
            && restored;
    }

    private sealed class MessageBoxSmokeOwner : Forms.IWin32Window
    {
        public MessageBoxSmokeOwner(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }
    }

    private sealed class WpfWindowOwner : Forms.IWin32Window
    {
        private readonly WpfWindow _window;

        public WpfWindowOwner(WpfWindow window)
        {
            _window = window;
        }

        public IntPtr Handle => new WindowInteropHelper(_window).Handle;
    }

    private sealed class DesignerSmokeService
    {
    }

    private sealed class DesignerSmokeToolboxService : System.Drawing.Design.IToolboxService
    {
        private System.Drawing.Design.ToolboxItem? _selectedToolboxItem;

        public System.Drawing.Design.CategoryNameCollection CategoryNames { get; } = new(Array.Empty<string>());

        public string? SelectedCategory { get; set; }

        public int SelectedToolboxItemUsedCount { get; private set; }

        public event EventHandler? SelectedCategoryChanged;

        public event EventHandler? SelectedCategoryChanging;

        public void AddCreator(System.Drawing.Design.ToolboxItemCreatorCallback creator, string format)
        {
        }

        public void AddCreator(System.Drawing.Design.ToolboxItemCreatorCallback creator, string format, IDesignerHost host)
        {
        }

        public void AddLinkedToolboxItem(System.Drawing.Design.ToolboxItem toolboxItem, string category, IDesignerHost host)
        {
        }

        public void AddLinkedToolboxItem(System.Drawing.Design.ToolboxItem toolboxItem, IDesignerHost host)
        {
        }

        public void AddToolboxItem(System.Drawing.Design.ToolboxItem toolboxItem)
        {
        }

        public void AddToolboxItem(System.Drawing.Design.ToolboxItem toolboxItem, string category)
        {
        }

        public System.Drawing.Design.ToolboxItem DeserializeToolboxItem(object serializedObject)
        {
            return (System.Drawing.Design.ToolboxItem)serializedObject;
        }

        public System.Drawing.Design.ToolboxItem DeserializeToolboxItem(object serializedObject, IDesignerHost host)
        {
            return DeserializeToolboxItem(serializedObject);
        }

        public System.Drawing.Design.ToolboxItem? GetSelectedToolboxItem()
        {
            return _selectedToolboxItem;
        }

        public System.Drawing.Design.ToolboxItem? GetSelectedToolboxItem(IDesignerHost host)
        {
            return _selectedToolboxItem;
        }

        public System.Drawing.Design.ToolboxItemCollection GetToolboxItems()
        {
            return new System.Drawing.Design.ToolboxItemCollection(Array.Empty<System.Drawing.Design.ToolboxItem>());
        }

        public System.Drawing.Design.ToolboxItemCollection GetToolboxItems(string category)
        {
            return GetToolboxItems();
        }

        public System.Drawing.Design.ToolboxItemCollection GetToolboxItems(string category, IDesignerHost host)
        {
            return GetToolboxItems();
        }

        public System.Drawing.Design.ToolboxItemCollection GetToolboxItems(IDesignerHost host)
        {
            return GetToolboxItems();
        }

        public bool IsSupported(object serializedObject, IDesignerHost host)
        {
            return serializedObject is System.Drawing.Design.ToolboxItem;
        }

        public bool IsToolboxItem(object serializedObject)
        {
            return serializedObject is System.Drawing.Design.ToolboxItem;
        }

        public bool IsToolboxItem(object serializedObject, IDesignerHost host)
        {
            return IsToolboxItem(serializedObject);
        }

        public void Refresh()
        {
        }

        public void RemoveCreator(string format)
        {
        }

        public void RemoveCreator(string format, IDesignerHost host)
        {
        }

        public void RemoveToolboxItem(System.Drawing.Design.ToolboxItem toolboxItem)
        {
        }

        public void RemoveToolboxItem(System.Drawing.Design.ToolboxItem toolboxItem, string category)
        {
        }

        public void SelectedToolboxItemUsed()
        {
            SelectedToolboxItemUsedCount++;
            _selectedToolboxItem = null;
        }

        public object SerializeToolboxItem(System.Drawing.Design.ToolboxItem toolboxItem)
        {
            return toolboxItem;
        }

        public bool SetCursor()
        {
            return _selectedToolboxItem is not null;
        }

        public void SetSelectedToolboxItem(System.Drawing.Design.ToolboxItem toolboxItem)
        {
            _selectedToolboxItem = toolboxItem;
        }
    }

    private sealed class DesignerSmokeUndoEngine : UndoEngine
    {
        private readonly Stack<UndoUnit> _undo = new();
        private readonly Stack<UndoUnit> _redo = new();

        public DesignerSmokeUndoEngine(IServiceProvider provider)
            : base(provider)
        {
        }

        public int UndoCount => _undo.Count;

        public int RedoCount => _redo.Count;

        protected override void AddUndoUnit(UndoUnit unit)
        {
            _undo.Push(unit);
            _redo.Clear();
        }

        public bool UndoOnce()
        {
            if (_undo.Count == 0)
                return false;

            UndoUnit unit = _undo.Pop();
            unit.Undo();
            _redo.Push(unit);
            return true;
        }

        public bool RedoOnce()
        {
            if (_redo.Count == 0)
                return false;

            UndoUnit unit = _redo.Pop();
            unit.Undo();
            _undo.Push(unit);
            return true;
        }
    }

    private sealed class DesignerSmokeLoader : CodeDomDesignerLoader
    {
        private CodeCompileUnit? _writtenUnit;

        protected override CodeDomProvider? CodeDomProvider => null;

        protected override ITypeResolutionService? TypeResolutionService => null;

        protected override CodeCompileUnit Parse()
        {
            var unit = new CodeCompileUnit();
            var codeNamespace = new CodeNamespace("LibreWinForms.SdkSmoke");
            var codeClass = new CodeTypeDeclaration("DesignerSmokeForm");
            codeClass.BaseTypes.Add(typeof(Forms.Form));
            codeClass.Members.Add(new CodeMemberField(typeof(Forms.ToolStripContainer), "toolStripContainer1"));

            var initializeComponent = new CodeMemberMethod
            {
                Name = "InitializeComponent"
            };
            var component = new CodeFieldReferenceExpression(new CodeThisReferenceExpression(), "toolStripContainer1");
            initializeComponent.Statements.Add(new CodeAssignStatement(
                component,
                new CodeObjectCreateExpression(typeof(Forms.ToolStripContainer))));
            initializeComponent.Statements.Add(new CodeAssignStatement(
                new CodePropertyReferenceExpression(component, nameof(Forms.Control.Name)),
                new CodePrimitiveExpression("toolStripContainer1")));
            initializeComponent.Statements.Add(new CodeExpressionStatement(
                new CodeMethodInvokeExpression(
                    new CodePropertyReferenceExpression(new CodeThisReferenceExpression(), nameof(Forms.Control.Controls)),
                    "Add",
                    component)));
            codeClass.Members.Add(initializeComponent);
            codeNamespace.Types.Add(codeClass);
            unit.Namespaces.Add(codeNamespace);
            return unit;
        }

        protected override void Write(CodeCompileUnit unit)
        {
            _writtenUnit = unit;
        }

        public bool ContainsPropertyAssignment(string componentName, string propertyName, object expectedValue)
        {
            if (_writtenUnit is null)
                return false;

            foreach (CodeNamespace codeNamespace in _writtenUnit.Namespaces)
            {
                foreach (CodeTypeDeclaration codeClass in codeNamespace.Types)
                {
                    foreach (CodeMemberMethod method in codeClass.Members.OfType<CodeMemberMethod>())
                    {
                        foreach (CodeAssignStatement assignment in method.Statements.OfType<CodeAssignStatement>())
                        {
                            if (assignment.Left is CodePropertyReferenceExpression property
                                && string.Equals(property.PropertyName, propertyName, StringComparison.Ordinal)
                                && property.TargetObject is CodeFieldReferenceExpression field
                                && field.TargetObject is CodeThisReferenceExpression
                                && string.Equals(field.FieldName, componentName, StringComparison.Ordinal)
                                && assignment.Right is CodePrimitiveExpression value
                                && Equals(value.Value, expectedValue))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }
    }

    private sealed class CustomPaintSmokeControl : Forms.Control
    {
        public CustomPaintSmokeControl()
        {
            SetStyle(
                Forms.ControlStyles.UserPaint
                    | Forms.ControlStyles.AllPaintingInWmPaint
                    | Forms.ControlStyles.OptimizedDoubleBuffer,
                true);
        }

        public int BackgroundPaintCount { get; private set; }

        public int ForegroundPaintCount { get; private set; }

        public bool ResetTransformDuringPaint { get; set; }

        protected override void OnPaintBackground(Forms.PaintEventArgs e)
        {
            BackgroundPaintCount++;
            if (ResetTransformDuringPaint)
            {
                e.Graphics.ResetTransform();
            }

            using var background = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(180, 20, 40, 80));
            e.Graphics.FillRectangle(background, e.ClipRectangle);
            base.OnPaintBackground(e);
        }

        protected override void OnPaint(Forms.PaintEventArgs e)
        {
            ForegroundPaintCount++;
            using var foreground = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(220, 160, 80, 20));
            e.Graphics.FillRectangle(
                foreground,
                new System.Drawing.Rectangle(3, 3, Math.Max(1, e.ClipRectangle.Width - 6), Math.Max(1, e.ClipRectangle.Height - 6)));
            Forms.ControlPaint.DrawBorder3D(
                e.Graphics,
                e.ClipRectangle,
                Forms.Border3DStyle.RaisedInner);
            base.OnPaint(e);
        }
    }

    private sealed class SmokeWindowsFormsHost : System.Windows.Forms.Integration.WindowsFormsHost
    {
        public void RenderForSmoke(System.Windows.Media.DrawingContext drawingContext)
        {
            OnRender(drawingContext);
        }
    }
}

[Designer(typeof(DesignerSmokeTrackingDesigner), typeof(IDesigner))]
public sealed class DesignerSmokeAttributedComponent : Component
{
}

public sealed class DesignerSmokeTrackingDesigner : IDesigner
{
    public static bool Disposed { get; private set; }

    public static IComponent? DesignedComponent { get; private set; }

    public static bool Initialized { get; private set; }

    public IComponent Component { get; private set; } = null!;

    public DesignerVerbCollection Verbs { get; } = new();

    public static void Reset()
    {
        DesignedComponent = null;
        Disposed = false;
        Initialized = false;
    }

    public void Dispose()
    {
        Disposed = true;
        Component = null!;
    }

    public void DoDefaultAction()
    {
    }

    public void Initialize(IComponent component)
    {
        Component = component;
        DesignedComponent = component;
        Initialized = true;
    }
}
