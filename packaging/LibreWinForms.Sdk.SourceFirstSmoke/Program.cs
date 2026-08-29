// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design;
using FormsDesign = System.Windows.Forms.Design;
using FormsBehavior = System.Windows.Forms.Design.Behavior;

namespace LibreWinForms.Sdk.SourceFirstSmoke;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        if (!LibrePlatform.IsRegistered)
        {
            throw new InvalidOperationException("The source-first SDK did not register the ProGPU platform backend.");
        }

        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        ApplicationConfiguration.Initialize();
        VerifyHexEditorInputScrollContracts();
        VerifyHexEditorControlContracts();
        VerifyHexEditorMenuContracts();
        VerifyHexEditorToolStripContracts();
        VerifyHexEditorDialogAndConverterContracts();
        VerifyFormsDesignerOptionContracts();
        VerifyFormsDesignerSnapLineContracts();
        VerifyFormsDesignerMenuCommandContracts();

        using Bitmap bitmap = new(64, 64);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (Pen pen = new(Color.Black))
        {
            graphics.DrawCurve(
                pen,
                [new Point(0, 0), new Point(16, 32), new Point(48, 16), new Point(63, 63)]);
        }

        using Form form = new()
        {
            ClientSize = new Size(320, 180),
            Text = "Canonical LibreWinForms SDK smoke"
        };
        form.Controls.Add(new Button { Text = "Source-built", AutoSize = true });

        LibrePlatform.Current.Dispose();
        return 0;
    }

    private static void VerifyHexEditorInputScrollContracts()
    {
        var input = new KeyEventArgs(Keys.Control | Keys.Shift | Keys.Alt | Keys.F);
        if (!input.Control
            || !input.Shift
            || !input.Alt
            || input.KeyValue != (int)Keys.F
            || input.KeyValue is <= 64 or >= 71)
        {
            throw new InvalidOperationException("Canonical KeyEventArgs no longer preserves HexEditor modifier/value checks.");
        }

        // The frozen compatibility vector used 0x5D, but canonical WinForms defines that
        // value as Keys.Apps. Use the upstream unit-test value for an undefined low word.
        var undefined = new KeyEventArgs(Keys.Control | Keys.Shift | Keys.Alt | (Keys)0xFF);
        if (undefined.KeyCode != Keys.None
            || undefined.KeyValue != 0xFF
            || undefined.Modifiers != (Keys.Control | Keys.Shift | Keys.Alt))
        {
            throw new InvalidOperationException("Canonical KeyEventArgs masking no longer matches upstream WinForms.");
        }

        var suppressed = new KeyEventArgs(Keys.A) { SuppressKeyPress = true };
        if (!suppressed.Handled)
        {
            throw new InvalidOperationException("SuppressKeyPress=true did not mark the key event handled.");
        }

        suppressed.SuppressKeyPress = false;
        if (suppressed.Handled)
        {
            throw new InvalidOperationException("SuppressKeyPress=false did not clear the handled state.");
        }

        var vertical = new ScrollEventArgs(
            ScrollEventType.SmallIncrement,
            oldValue: 12,
            newValue: 18,
            ScrollOrientation.VerticalScroll);
        if (vertical.Type != ScrollEventType.SmallIncrement
            || vertical.OldValue != 12
            || vertical.NewValue != 18
            || vertical.ScrollOrientation != ScrollOrientation.VerticalScroll)
        {
            throw new InvalidOperationException("Canonical four-argument ScrollEventArgs changed HexEditor state.");
        }

        var horizontal = new ScrollEventArgs(ScrollEventType.ThumbPosition, oldValue: 4, newValue: 7);
        if (horizontal.OldValue != 4
            || horizontal.NewValue != 7
            || horizontal.ScrollOrientation != ScrollOrientation.HorizontalScroll)
        {
            throw new InvalidOperationException("Canonical ScrollEventArgs no longer defaults to horizontal orientation.");
        }
    }

    private static void VerifyHexEditorControlContracts()
    {
        if (!ReferenceEquals(Cursors.IBeam, Cursors.IBeam)
            || ReferenceEquals(Cursors.IBeam, Cursors.Default)
            || ReferenceEquals(Cursors.IBeam, Cursors.WaitCursor)
            || !ReferenceEquals(Cursors.SizeWE, Cursors.SizeWE)
            || !ReferenceEquals(Cursors.SizeNS, Cursors.SizeNS))
        {
            throw new InvalidOperationException("Canonical stock cursors no longer provide stable typed instances.");
        }

        using var buffered = new DoubleBufferedProbeControl();
        if (buffered.IsDoubleBuffered)
        {
            throw new InvalidOperationException("Canonical controls no longer default DoubleBuffered to false.");
        }

        buffered.IsDoubleBuffered = true;
        if (!buffered.IsDoubleBuffered
            || !buffered.HasStyle(ControlStyles.OptimizedDoubleBuffer)
            || !buffered.HasStyle(ControlStyles.AllPaintingInWmPaint))
        {
            throw new InvalidOperationException("DoubleBuffered=true no longer enables the canonical painting styles.");
        }

        buffered.IsDoubleBuffered = false;
        if (buffered.HasStyle(ControlStyles.OptimizedDoubleBuffer)
            || !buffered.HasStyle(ControlStyles.AllPaintingInWmPaint))
        {
            throw new InvalidOperationException("DoubleBuffered=false changed the canonical asymmetric style semantics.");
        }

        using var input = new HexEditorInputKeyProbeControl();
        var left = new Message { Msg = 0x0100, WParam = (nint)Keys.Left };
        if (input.PreProcessMessage(ref left) || input.CommandCount != 0)
        {
            throw new InvalidOperationException("HexEditor arrow input was consumed as a command.");
        }

        var delete = new Message { Msg = 0x0100, WParam = (nint)Keys.Delete };
        if (!input.PreProcessMessage(ref delete) || input.CommandCount != 1)
        {
            throw new InvalidOperationException("HexEditor Delete input did not reach ProcessCmdKey exactly once.");
        }

        using var userControl = new UserControl();
        if (userControl.BorderStyle != BorderStyle.None)
        {
            throw new InvalidOperationException("UserControl.BorderStyle no longer defaults to None.");
        }

        userControl.BorderStyle = BorderStyle.FixedSingle;
        userControl.BorderStyle = BorderStyle.Fixed3D;
        if (userControl.BorderStyle != BorderStyle.Fixed3D)
        {
            throw new InvalidOperationException("UserControl.BorderStyle did not retain the canonical value.");
        }

        AssertInvalidEnum(() => userControl.BorderStyle = (BorderStyle)(-1));
        AssertInvalidEnum(() => userControl.BorderStyle = (BorderStyle)3);
    }

    private static void VerifyHexEditorMenuContracts()
    {
        using var owner = new Control();
        using var first = new ContextMenuStrip();
        using var replacement = new ContextMenuStrip();
        int changes = 0;
        ContextMenuStrip? observed = null;
        owner.ContextMenuStripChanged += (_, _) =>
        {
            changes++;
            observed = owner.ContextMenuStrip;
        };

        owner.ContextMenuStrip = first;
        owner.ContextMenuStrip = first;
        owner.ContextMenuStrip = replacement;
        first.Dispose();
        if (changes != 2 || !ReferenceEquals(observed, replacement) || !ReferenceEquals(owner.ContextMenuStrip, replacement))
        {
            throw new InvalidOperationException("ContextMenuStrip replacement did not preserve canonical ownership semantics.");
        }

        replacement.Dispose();
        if (changes != 3 || owner.ContextMenuStrip is not null || observed is not null)
        {
            throw new InvalidOperationException("Disposing the current ContextMenuStrip did not clear its owner.");
        }

        using var menu = new DropDownEventProbe();
        int closed = 0;
        ToolStripDropDownCloseReason reason = default;
        menu.Closed += (_, e) =>
        {
            closed++;
            reason = e.CloseReason;
        };

        menu.RaiseClosed(ToolStripDropDownCloseReason.ItemClicked);
        if (closed != 1 || reason != ToolStripDropDownCloseReason.ItemClicked)
        {
            throw new InvalidOperationException("ToolStripDropDown lost its typed close reason.");
        }

        menu.RaiseClosed(ToolStripDropDownCloseReason.CloseCalled);
        if (closed != 2 || reason != ToolStripDropDownCloseReason.CloseCalled)
        {
            throw new InvalidOperationException("ToolStripDropDown did not report the canonical CloseCalled reason.");
        }
    }

    private static void VerifyHexEditorToolStripContracts()
    {
        using var combo = new ToolStripComboBox();
        object hexadecimal = "Hexadecimal";
        object octal = "Octal";
        combo.Items.AddRange([hexadecimal, octal, "Decimal"]);
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        int changes = 0;
        combo.SelectedIndexChanged += (_, _) => changes++;

        combo.SelectedItem = octal;
        combo.SelectedItem = new object();
        if (combo.SelectedIndex != 1 || !ReferenceEquals(combo.SelectedItem, octal) || changes != 1)
        {
            throw new InvalidOperationException("ToolStripComboBox did not preserve canonical selected-item forwarding.");
        }

        combo.SelectedItem = null;
        if (combo.SelectedIndex != -1 || combo.SelectedItem is not null || changes != 2
            || combo.DropDownStyle != ComboBoxStyle.DropDownList
            || combo.Items.Count != 3
            || !Equals(combo.Items[0], hexadecimal)
            || !Equals(combo.Items[2], "Decimal"))
        {
            throw new InvalidOperationException("ToolStripComboBox selection, style, or designer item ordering changed.");
        }

        using var menuItem = new ToolStripMenuItem("File");
        using var child = new ToolStripMenuItem("Open");
        menuItem.DropDownItems.Add(child);
        if (!ReferenceEquals(menuItem.DropDownItems, menuItem.DropDown.Items)
            || menuItem.DropDown.Items.Count != 1
            || !ReferenceEquals(menuItem.DropDown.Items[0], child))
        {
            throw new InvalidOperationException("ToolStripMenuItem exposed divergent drop-down collections.");
        }

        using var button = new ToolStripDropDownButton();
        using var buttonChild = new ToolStripButton();
        button.DropDownItems.Add(buttonChild);
        if (!ReferenceEquals(button.DropDownItems, button.DropDown.Items)
            || button.DropDown.Items.Count != 1
            || !ReferenceEquals(button.DropDown.Items[0], buttonChild))
        {
            throw new InvalidOperationException("ToolStripDropDownButton exposed divergent drop-down collections.");
        }

        using var progress = new ToolStripProgressBar();
        if (progress.Overflow != ToolStripItemOverflow.AsNeeded)
        {
            throw new InvalidOperationException("ToolStripItem.Overflow no longer defaults to AsNeeded.");
        }

        progress.Overflow = ToolStripItemOverflow.Never;
        AssertInvalidEnum(() => progress.Overflow = (ToolStripItemOverflow)(-1));
        AssertInvalidEnum(() => progress.Overflow = (ToolStripItemOverflow)3);
        if (progress.Overflow != ToolStripItemOverflow.Never)
        {
            throw new InvalidOperationException("Invalid ToolStripItem.Overflow input mutated canonical state.");
        }
    }

    private static void VerifyHexEditorDialogAndConverterContracts()
    {
        var converter = new CursorConverter();
        if (!converter.CanConvertFrom(typeof(string))
            || !converter.CanConvertTo(typeof(string))
            || !converter.GetStandardValuesSupported()
            || !ReferenceEquals(converter.ConvertFromInvariantString("IBeam"), Cursors.IBeam)
            || !Equals(converter.ConvertToInvariantString(Cursors.SizeWE), nameof(Cursors.SizeWE)))
        {
            throw new InvalidOperationException("CursorConverter no longer round-trips canonical stock cursors.");
        }

        using var open = new OpenFileDialog
        {
            AddExtension = true,
            CheckFileExists = true,
            DefaultExt = "bin",
            FileName = "sample.bin",
            Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*",
            Multiselect = false,
        };
        using var save = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "bin",
            FileName = "sample.bin",
            Filter = open.Filter,
            OverwritePrompt = true,
        };

        if (!open.CheckFileExists || open.Multiselect || open.DefaultExt != "bin"
            || !save.AddExtension || !save.OverwritePrompt || save.Filter != open.Filter)
        {
            throw new InvalidOperationException("Canonical file-dialog configuration no longer supports the HexEditor contract.");
        }
    }

    private static void AssertInvalidEnum(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidEnumArgumentException)
        {
            return;
        }

        throw new InvalidOperationException("Expected InvalidEnumArgumentException was not thrown.");
    }

    private static void VerifyFormsDesignerOptionContracts()
    {
        var defaults = new FormsDesign.DesignerOptions();
        if (defaults.GridSize != new Size(8, 8)
            || !defaults.ShowGrid
            || !defaults.SnapToGrid
            || defaults.UseSnapLines)
        {
            throw new InvalidOperationException("Canonical DesignerOptions defaults no longer match WinForms.");
        }

        var sharpOptions = new GetterOnlyDesignerOptions(
            new Size(12, 14),
            showGrid: false,
            snapToGrid: false,
            useSnapLines: true);
        if (sharpOptions.GridSize != new Size(12, 14)
            || sharpOptions.ShowGrid
            || sharpOptions.SnapToGrid
            || !sharpOptions.UseSnapLines)
        {
            throw new InvalidOperationException("Getter-only SharpDevelop DesignerOptions overrides lost their values.");
        }

        var service = new FormsDesign.WindowsFormsDesignerOptionService();
        DesignerOptionService.DesignerOptionCollection root = service.Options;
        if (root["DesignerOptions"] is null || root["WindowsFormsDesigner"] is not null)
        {
            throw new InvalidOperationException("Canonical designer option-page identity changed.");
        }

        SetDesignerOption(root, service, nameof(FormsDesign.DesignerOptions.GridSize), new Size(1, 201));
        if (service.CompatibilityOptions.GridSize != new Size(2, 200))
        {
            throw new InvalidOperationException("DesignerOptions did not clamp both grid dimensions to 2..200.");
        }

        SetDesignerOption(root, service, nameof(FormsDesign.DesignerOptions.GridSize), new Size(32, 24));
        SetDesignerOption(root, service, nameof(FormsDesign.DesignerOptions.ShowGrid), false);
        SetDesignerOption(root, service, nameof(FormsDesign.DesignerOptions.SnapToGrid), false);
        SetDesignerOption(root, service, nameof(FormsDesign.DesignerOptions.UseSnapLines), true);
        if (service.CompatibilityOptions.GridSize != new Size(32, 24)
            || service.CompatibilityOptions.ShowGrid
            || service.CompatibilityOptions.SnapToGrid
            || !service.CompatibilityOptions.UseSnapLines)
        {
            throw new InvalidOperationException("SharpDevelop-style option property setting did not reach DesignerOptions.");
        }
    }

    private static void SetDesignerOption(
        DesignerOptionService.DesignerOptionCollection options,
        FormsDesign.WindowsFormsDesignerOptionService service,
        string name,
        object value)
    {
        PropertyDescriptor property = options.Properties.Find(name, ignoreCase: false)
            ?? throw new InvalidOperationException($"Designer option property '{name}' is missing.");
        property.SetValue(service, value);
    }

    private static void VerifyFormsDesignerSnapLineContracts()
    {
        if ((int)FormsBehavior.SnapLineType.Top != 0
            || (int)FormsBehavior.SnapLineType.Bottom != 1
            || (int)FormsBehavior.SnapLineType.Left != 2
            || (int)FormsBehavior.SnapLineType.Right != 3
            || (int)FormsBehavior.SnapLineType.Horizontal != 4
            || (int)FormsBehavior.SnapLineType.Vertical != 5
            || (int)FormsBehavior.SnapLineType.Baseline != 6
            || (int)FormsBehavior.SnapLinePriority.Low != 1
            || (int)FormsBehavior.SnapLinePriority.Medium != 2
            || (int)FormsBehavior.SnapLinePriority.High != 3
            || (int)FormsBehavior.SnapLinePriority.Always != 4)
        {
            throw new InvalidOperationException("Canonical snap-line enum values changed.");
        }

        var baseline = new FormsBehavior.SnapLine(
            FormsBehavior.SnapLineType.Baseline,
            7,
            "Text",
            FormsBehavior.SnapLinePriority.High);
        baseline.AdjustOffset(-3);
        if (!baseline.IsHorizontal
            || baseline.IsVertical
            || baseline.Offset != 4
            || baseline.Filter != "Text"
            || baseline.Priority != FormsBehavior.SnapLinePriority.High
            || baseline.ToString() != "SnapLine: {type = Baseline, offset = 4, priority = High, filter = Text}")
        {
            throw new InvalidOperationException("Canonical SnapLine state, orientation, offset, or formatting changed.");
        }

        var top = new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Top, 0);
        if (!FormsBehavior.SnapLine.ShouldSnap(top, new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Top, 10))
            || FormsBehavior.SnapLine.ShouldSnap(top, new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Left, 0))
            || FormsBehavior.SnapLine.ShouldSnap(top, new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Top, 2, "Text"))
            || !FormsBehavior.SnapLine.ShouldSnap(
                new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Baseline, 0, "Text"),
                new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Baseline, 1, "Text"))
            || FormsBehavior.SnapLine.ShouldSnap(
                new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Baseline, 0, "Text"),
                new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Baseline, 1, "Icon"))
            || !FormsBehavior.SnapLine.ShouldSnap(
                new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Vertical, 0, "Margin.Left"),
                new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Vertical, 1, "Margin.Right"))
            || !FormsBehavior.SnapLine.ShouldSnap(
                new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Vertical, 0, "Padding.Left"),
                new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Vertical, 1, "Margin.Left")))
        {
            throw new InvalidOperationException("Canonical SnapLine.ShouldSnap matching rules changed.");
        }

        using var control = new Control
        {
            Size = new Size(40, 30),
            Margin = new Padding(1, 2, 3, 4),
        };
        using var designer = new FormsDesign.ControlDesigner();
        designer.Initialize(control);
        IList controlLines = designer.SnapLines;
        if (!designer.ParticipatesWithSnapLines
            || controlLines.Count != 8
            || FindSnapLine(controlLines, FormsBehavior.SnapLineType.Top, filter: null).Offset != 0
            || FindSnapLine(controlLines, FormsBehavior.SnapLineType.Bottom, filter: null).Offset != 29
            || FindSnapLine(controlLines, FormsBehavior.SnapLineType.Left, filter: null).Offset != 0
            || FindSnapLine(controlLines, FormsBehavior.SnapLineType.Right, filter: null).Offset != 39
            || FindSnapLine(controlLines, FormsBehavior.SnapLineType.Horizontal, "Margin.Top").Offset != -2
            || FindSnapLine(controlLines, FormsBehavior.SnapLineType.Horizontal, "Margin.Bottom").Offset != 34
            || FindSnapLine(controlLines, FormsBehavior.SnapLineType.Vertical, "Margin.Left").Offset != -1
            || FindSnapLine(controlLines, FormsBehavior.SnapLineType.Vertical, "Margin.Right").Offset != 43
            || controlLines.Cast<FormsBehavior.SnapLine>().Take(4).Any(line => line.Priority != FormsBehavior.SnapLinePriority.Low)
            || controlLines.Cast<FormsBehavior.SnapLine>().Skip(4).Any(line => line.Priority != FormsBehavior.SnapLinePriority.Always))
        {
            throw new InvalidOperationException("ControlDesigner edge or margin snap lines changed.");
        }

        using var parent = new Panel
        {
            Location = new Point(10, 20),
            Size = new Size(100, 80),
            Padding = new Padding(5, 6, 7, 8),
        };
        using var designerParent = new Panel { Size = new Size(200, 160) };
        designerParent.Controls.Add(parent);
        using var parentDesigner = new FormsDesign.ParentControlDesigner();
        parentDesigner.Initialize(parent);
        IList parentLines = parentDesigner.SnapLines;
        if (parentLines.Count != 12
            || FindSnapLine(parentLines, FormsBehavior.SnapLineType.Vertical, "Padding.Left").Offset != 5
            || FindSnapLine(parentLines, FormsBehavior.SnapLineType.Vertical, "Padding.Right").Offset != 93
            || FindSnapLine(parentLines, FormsBehavior.SnapLineType.Horizontal, "Padding.Top").Offset != 6
            || FindSnapLine(parentLines, FormsBehavior.SnapLineType.Horizontal, "Padding.Bottom").Offset != 72)
        {
            string lines = string.Join(
                "; ",
                parentLines.Cast<FormsBehavior.SnapLine>().Select(
                    line => $"{line.SnapLineType}:{line.Filter ?? "<null>"}={line.Offset}/{line.Priority}"));
            throw new InvalidOperationException($"ParentControlDesigner padding snap lines changed: {lines}.");
        }

        var customLine = new FormsBehavior.SnapLine(
            FormsBehavior.SnapLineType.Baseline,
            13,
            "Custom",
            FormsBehavior.SnapLinePriority.Medium);
        using var customControl = new Control();
        using var customDesigner = new CustomSnapLineDesigner(customLine);
        customDesigner.Initialize(customControl);
        if (customDesigner.ParticipatesWithSnapLines
            || customDesigner.SnapLines.Count != 1
            || !ReferenceEquals(customDesigner.SnapLines[0], customLine)
            || !FormsBehavior.SnapLine.ShouldSnap(
                (FormsBehavior.SnapLine)customDesigner.SnapLines[0]!,
                new FormsBehavior.SnapLine(FormsBehavior.SnapLineType.Baseline, 20, "Custom")))
        {
            throw new InvalidOperationException("Public ControlDesigner snap-line overrides changed.");
        }
    }

    private static FormsBehavior.SnapLine FindSnapLine(
        IList lines,
        FormsBehavior.SnapLineType type,
        string? filter)
        => lines.Cast<FormsBehavior.SnapLine>().Single(line => line.SnapLineType == type && line.Filter == filter);

    private static void VerifyFormsDesignerMenuCommandContracts()
    {
        using (var fixture = new VerbFixture())
        {
            var globalOnly = new DesignerVerb("Global only", (_, _) => { });
            var shadowedGlobal = new DesignerVerb("SHARED ACTION", (_, _) => { });
            var localWinner = new DesignerVerb("shared action", (_, _) => { });
            var rootLocal = new DesignerVerb("Root local", (_, _) => { });
            var childLocal = new DesignerVerb("Child local", (_, _) => { });
            var inheritedLocal = new DesignerVerb("Inherited local", (_, _) => { });

            fixture.RootDesigner.Verbs.Add(localWinner);
            fixture.RootDesigner.Verbs.Add(rootLocal);
            fixture.ChildDesigner.Verbs.Add(childLocal);
            fixture.InheritedDesigner.Verbs.Add(inheritedLocal);
            fixture.Commands.AddVerb(globalOnly);
            fixture.Commands.AddVerb(shadowedGlobal);

            fixture.Select(fixture.Root);
            DesignerVerbCollection rootVerbs = fixture.Commands.Verbs;
            if (rootVerbs.Count != 3
                || !rootVerbs.Contains(globalOnly)
                || !rootVerbs.Contains(localWinner)
                || rootVerbs.Contains(shadowedGlobal)
                || !rootVerbs.Contains(rootLocal))
            {
                throw new InvalidOperationException("Root designer verb merging or precedence changed.");
            }

            fixture.Select(fixture.Child);
            DesignerVerbCollection childVerbs = fixture.Commands.Verbs;
            if (childVerbs.Count != 1
                || !childVerbs.Contains(childLocal)
                || childVerbs.Contains(globalOnly))
            {
                throw new InvalidOperationException("Selected child designer verb filtering changed.");
            }

            fixture.Selection.SetSelectedComponents(
                new object[] { fixture.Root, fixture.Child },
                SelectionTypes.Replace);
            if (fixture.Commands.Verbs.Count != 0)
            {
                throw new InvalidOperationException("Designer verbs were exposed for a multi-selection.");
            }

            fixture.Select(fixture.InheritedReadOnly);
            if (fixture.Commands.Verbs.Count != 0)
            {
                throw new InvalidOperationException("Designer verbs were exposed for an inherited read-only component.");
            }
        }

        using (var fixture = new VerbFixture())
        {
            var initial = new DesignerVerb("Initial", (_, _) => { });
            var refreshed = new DesignerVerb("Refreshed", (_, _) => { });
            fixture.ChildDesigner.Verbs.Add(initial);
            fixture.Select(fixture.Child);

            DesignerVerbCollection initialCache = fixture.Commands.Verbs;
            if (!ReferenceEquals(initialCache, fixture.Commands.Verbs))
            {
                throw new InvalidOperationException("Repeated verb reads did not reuse the current selection cache.");
            }

            fixture.ChildDesigner.Verbs.Add(refreshed);
            if (fixture.Commands.Verbs.Count != 1)
            {
                throw new InvalidOperationException("Designer verb cache changed without an invalidation signal.");
            }

            TypeDescriptor.Refresh(typeof(VerbComponent));
            DesignerVerbCollection refreshedCache = fixture.Commands.Verbs;
            if (ReferenceEquals(initialCache, refreshedCache)
                || refreshedCache.Count != 2
                || !refreshedCache.Contains(refreshed))
            {
                throw new InvalidOperationException("TypeDescriptor.Refresh did not rebuild selected designer verbs.");
            }

            fixture.Select(fixture.Root);
            DesignerVerbCollection emptyRootCache = fixture.Commands.Verbs;
            var global = new DesignerVerb("Late global", (_, _) => { });
            fixture.Commands.AddVerb(global);
            DesignerVerbCollection addedGlobalCache = fixture.Commands.Verbs;
            if (!ReferenceEquals(emptyRootCache, addedGlobalCache) || !addedGlobalCache.Contains(global))
            {
                throw new InvalidOperationException("Adding a global verb did not update the root verb cache in place.");
            }

            fixture.Commands.RemoveVerb(global);
            if (!ReferenceEquals(addedGlobalCache, fixture.Commands.Verbs)
                || fixture.Commands.Verbs.Contains(global))
            {
                throw new InvalidOperationException("Removing a global verb did not update the root verb cache in place.");
            }

            fixture.Select(fixture.Child);
            if (ReferenceEquals(refreshedCache, fixture.Commands.Verbs))
            {
                throw new InvalidOperationException("SelectionChanging did not invalidate the designer verb cache.");
            }
        }

        using (var fixture = new VerbFixture())
        {
            int firstInvocations = 0;
            int secondInvocations = 0;
            int exactInvocations = 0;
            int registeredInvocations = 0;
            var first = new DesignerVerb("First virtual", (_, _) => firstInvocations++);
            var exactId = new CommandID(new Guid("30F7C67B-A1D7-4265-B966-AE155096F762"), 73);
            var exact = new DesignerVerb("Exact", (_, _) => exactInvocations++, exactId);
            var second = new DesignerVerb("Second virtual", (_, _) => secondInvocations++);
            var registeredId = new CommandID(new Guid("9A5265D4-BE79-48EC-8B4E-74D130D2AE59"), 91);
            var registered = new MenuCommand((_, _) => registeredInvocations++, registeredId);

            fixture.ChildDesigner.Verbs.Add(first);
            fixture.ChildDesigner.Verbs.Add(exact);
            fixture.ChildDesigner.Verbs.Add(second);
            fixture.Select(fixture.Child);
            fixture.Commands.AddCommand(registered);

            var secondVirtualId = new CommandID(StandardCommands.VerbFirst.Guid, StandardCommands.VerbFirst.ID + 1);
            if (!ReferenceEquals(fixture.Commands.FindCommand(exactId), exact)
                || !ReferenceEquals(fixture.Commands.FindCommand(StandardCommands.VerbFirst), first)
                || !ReferenceEquals(fixture.Commands.FindCommand(secondVirtualId), second)
                || !ReferenceEquals(fixture.Commands.FindCommand(registeredId), registered))
            {
                throw new InvalidOperationException("Exact, virtual, or registered menu-command resolution changed.");
            }

            if (!fixture.Commands.GlobalInvoke(exactId)
                || exactInvocations != 1
                || !fixture.Commands.GlobalInvoke(secondVirtualId)
                || secondInvocations != 1
                || !fixture.Commands.GlobalInvoke(registeredId)
                || registeredInvocations != 1
                || firstInvocations != 0)
            {
                throw new InvalidOperationException("Exact, virtual, or registered menu-command invocation changed.");
            }
        }
    }

    private sealed class DoubleBufferedProbeControl : Control
    {
        public bool IsDoubleBuffered
        {
            get => DoubleBuffered;
            set => DoubleBuffered = value;
        }

        public bool HasStyle(ControlStyles style) => GetStyle(style);
    }

    private sealed class HexEditorInputKeyProbeControl : Control
    {
        public int CommandCount { get; private set; }

        protected override bool IsInputKey(Keys keyData)
            => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Tab;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if ((keyData & Keys.KeyCode) == Keys.Delete)
            {
                CommandCount++;
                return true;
            }

            return false;
        }
    }

    private sealed class DropDownEventProbe : ContextMenuStrip
    {
        public void RaiseClosed(ToolStripDropDownCloseReason reason)
            => OnClosed(new ToolStripDropDownClosedEventArgs(reason));
    }

    private sealed class GetterOnlyDesignerOptions : FormsDesign.DesignerOptions
    {
        private readonly Size _gridSize;
        private readonly bool _showGrid;
        private readonly bool _snapToGrid;
        private readonly bool _useSnapLines;

        public GetterOnlyDesignerOptions(Size gridSize, bool showGrid, bool snapToGrid, bool useSnapLines)
        {
            _gridSize = gridSize;
            _showGrid = showGrid;
            _snapToGrid = snapToGrid;
            _useSnapLines = useSnapLines;
        }

        public override Size GridSize => _gridSize;

        public override bool ShowGrid => _showGrid;

        public override bool SnapToGrid => _snapToGrid;

        public override bool UseSnapLines => _useSnapLines;
    }

    private sealed class CustomSnapLineDesigner : FormsDesign.ControlDesigner
    {
        private readonly IList _snapLines;

        public CustomSnapLineDesigner(FormsBehavior.SnapLine snapLine)
            => _snapLines = new ArrayList { snapLine };

        public override bool ParticipatesWithSnapLines => false;

        public override IList SnapLines => _snapLines;
    }

    private sealed class VerbFixture : IDisposable
    {
        private readonly VerbDesignSurface _surface = new();
        private readonly TypeDescriptionProvider _inheritanceProvider;

        public VerbFixture()
        {
            Host = (IDesignerHost)_surface.GetService(typeof(IDesignerHost))!;
            Selection = (ISelectionService)Host.GetService(typeof(ISelectionService))!;
            Root = (RootVerbControl)Host.CreateComponent(typeof(RootVerbControl), "verbRoot");
            Child = (VerbComponent)Host.CreateComponent(typeof(VerbComponent), "verbChild");
            InheritedReadOnly = (InheritedReadOnlyVerbComponent)Host.CreateComponent(
                typeof(InheritedReadOnlyVerbComponent),
                "inheritedVerbChild");
            _inheritanceProvider = TypeDescriptor.AddAttributes(
                InheritedReadOnly,
                InheritanceAttribute.InheritedReadOnly);
            RootDesigner = (RootVerbDesigner)Host.GetDesigner(Root)!;
            ChildDesigner = (VerbDesigner)Host.GetDesigner(Child)!;
            InheritedDesigner = (VerbDesigner)Host.GetDesigner(InheritedReadOnly)!;
            Commands = new MenuCommandService(Host);
        }

        public IDesignerHost Host { get; }

        public ISelectionService Selection { get; }

        public RootVerbControl Root { get; }

        public VerbComponent Child { get; }

        public InheritedReadOnlyVerbComponent InheritedReadOnly { get; }

        public RootVerbDesigner RootDesigner { get; }

        public VerbDesigner ChildDesigner { get; }

        public VerbDesigner InheritedDesigner { get; }

        public MenuCommandService Commands { get; }

        public void Select(object component)
            => Selection.SetSelectedComponents(new object[] { component }, SelectionTypes.Replace);

        public void Dispose()
        {
            Commands.Dispose();
            TypeDescriptor.RemoveProvider(_inheritanceProvider, InheritedReadOnly);
            _surface.Dispose();
        }
    }

    private sealed class VerbDesignSurface : DesignSurface
    {
        protected override IDesigner? CreateDesigner(IComponent component, bool rootDesigner)
        {
            if (rootDesigner && component is RootVerbControl)
            {
                return new RootVerbDesigner();
            }

            if (component is VerbComponent or InheritedReadOnlyVerbComponent)
            {
                return new VerbDesigner();
            }

            return base.CreateDesigner(component, rootDesigner);
        }
    }

    private sealed class RootVerbControl : Panel;

    private sealed class VerbComponent : Component;

    private sealed class InheritedReadOnlyVerbComponent : Component;

    private sealed class VerbDesigner : ComponentDesigner;

#pragma warning disable CS0618 // IRootDesigner requires the legacy ViewTechnology contract.
    private sealed class RootVerbDesigner : ComponentDesigner, IRootDesigner
    {
        public ViewTechnology[] SupportedTechnologies => [ViewTechnology.Default];

        public object GetView(ViewTechnology technology) => Component;
    }
#pragma warning restore CS0618
}
