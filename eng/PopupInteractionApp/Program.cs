// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

namespace PopupInteractionApp;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var startup = PopupInteractionStartup.Parse(args,
#if LIBREWINFORMS_POPUP_APP
            portable: true);
        if (startup.NativeModalSessions &&
            global::LibreWinForms.Platform.LibrePlatform.Current.Windows is not
                global::LibreWinForms.ProGPU.SilkWindowService { EnableNativeModalSessions: true })
            throw new InvalidOperationException("The actual SDK source window service did not retain explicit native modal startup.");
#else
            portable: false);
#endif
        if (!Directory.Exists(startup.EvidenceDirectory) || Directory.EnumerateFileSystemEntries(startup.EvidenceDirectory).Any())
            throw new ArgumentException("Supply a fresh existing evidence directory and a unique run identifier.");
        using System.Threading.Timer watchdog = new(_ => Environment.Exit(124), null, 60_000, Timeout.Infinite);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ApplicationConfiguration.Initialize();
        using InteractionForm form = new(startup.EvidenceDirectory, startup.RunId, startup.ModalDialog);
        Application.Run(form);
    }
}

internal sealed partial class InteractionForm : Form
{
    private readonly string _directory;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly StreamWriter _events;
    private readonly Dictionary<string, int> _counts = new();
    private readonly System.Windows.Forms.Timer _observer = new() { Interval = 100 };
    private readonly ContextMenuStrip _context = new();
    private readonly MenuStrip _menu = new();
    private readonly ToolTip _tip = new() { InitialDelay = 500, ReshowDelay = 500, AutoPopDelay = 5_000 };
    private readonly TextBox _editor = new() { Name = "editor", Location = new(24, 56), Size = new(240, 28) };
    private readonly Button _contextTarget = new() { Name = "context-target", Text = "Right-click for context menu", Location = new(24, 108), Size = new(240, 36) };
    private readonly ComboBox _combo = new() { Name = "combo", Location = new(24, 166), Size = new(240, 28), DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _tipTarget = new() { Name = "tooltip-target", Text = "Hover for tooltip", Location = new(304, 108), Size = new(220, 36) };
    private readonly Dictionary<string, ToolStripItem> _items = new();
    private readonly Dictionary<string, ToolStripDropDown> _popups = new();
    private Button? _modalButton;
    private Button? _ownerInputGuard;
    private InteractionForm? _modalChild;
    private string? _modalEvidence;
    private long _sequence;

    internal InteractionForm(string directory, string run, bool modalAction = false, bool dialog = false)
    {
        _directory = directory;
        _events = new StreamWriter(new FileStream(Path.Combine(directory, "events.jsonl"), FileMode.CreateNew)) { AutoFlush = true };
        // Keep the form and every design-sized child in one canonical autoscale
        // pass, just like a designer-generated InitializeComponent method.
        SuspendLayout();
        Text = $"PopupInteractionApp [{run}]";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new(560, 250);
        if (modalAction && !dialog)
        {
            // Real source layout leaves a guard below the smaller centered
            // dialog. The driver still proves native exposure before input;
            // this intended layout never substitutes for that observation.
            ClientSize = new(840, 580);
            _ownerInputGuard = new Button
            {
                Name = "owner-input-guard", Text = "Owner input guard",
                Location = new(24, 500), Size = new(220, 36)
            };
            _ownerInputGuard.MouseDown += (_, _) => Record("owner-guard-down");
            _ownerInputGuard.MouseUp += (_, _) => Record("owner-guard-up");
            _ownerInputGuard.Click += (_, _) => Record("owner-guard-click");
            Controls.Add(_ownerInputGuard);
        }
        if (dialog) StartPosition = FormStartPosition.CenterParent;

        ToolStripMenuItem contextMore = new("More");
        ToolStripMenuItem contextCommand = new("Context command");
        contextMore.DropDownItems.Add(contextCommand);
        _context.Items.Add("Context first");
        _context.Items.Add(contextMore);
        _contextTarget.ContextMenuStrip = _context;
        RegisterPopup("context", _context);
        RegisterPopup("context-child", contextMore.DropDown);
        _items.Add("context-more", contextMore);
        _items.Add("context-command", contextCommand);
        contextCommand.Click += (_, _) => Record("context-command");

        ToolStripMenuItem file = new("&File");
        ToolStripMenuItem menuMore = new("&More");
        ToolStripMenuItem menuCommand = new("Menu command");
        menuMore.DropDownItems.Add(menuCommand);
        file.DropDownItems.Add("First command");
        file.DropDownItems.Add(menuMore);
        _menu.Items.Add(file);
        MainMenuStrip = _menu;
        RegisterPopup("menu", file.DropDown);
        RegisterPopup("menu-child", menuMore.DropDown);
        _items.Add("menu-file", file);
        _items.Add("menu-more", menuMore);
        _items.Add("menu-command", menuCommand);
        menuCommand.Click += (_, _) => Record("menu-command");
        _menu.MenuActivate += (_, _) => Record("menu-activate");
        _menu.MenuDeactivate += (_, _) => Record("menu-deactivate");

        _combo.Items.AddRange(["Alpha", "Beta", "Gamma"]);
        _combo.SelectedIndex = 0;
        _combo.DropDown += (_, _) => Record("combo-opened");
        _combo.DropDownClosed += (_, _) => Record("combo-closed");
        _combo.SelectionChangeCommitted += (_, _) => Record("combo-committed");
        _tip.SetToolTip(_tipTarget, "Popup interaction tooltip");
        _tip.Popup += (_, _) => Record("tooltip-popup");
        _editor.MouseDown += (_, _) => Record("editor-pointer");
        _editor.TextChanged += (_, _) => Record("editor-text");
        Controls.AddRange([_editor, _contextTarget, _combo, _tipTarget, _menu]);
        if (modalAction || dialog)
        {
            _modalButton = new Button
            {
                Name = dialog ? "close-modal-dialog" : "open-modal-dialog",
                Text = dialog ? "Close modal dialog" : "Open modal dialog",
                Location = new(304, 166), Size = new(220, 36),
                DialogResult = dialog ? DialogResult.OK : DialogResult.None
            };
            _modalButton.MouseDown += (_, _) => Record("modal-button-pointer");
            _modalButton.Click += (_, _) =>
            {
                Record("modal-button-click");
                if (!dialog) OpenModalDialog(run);
            };
            Controls.Add(_modalButton);
            _editor.GotFocus += (_, _) => Record("editor-focus-gained");
            _editor.LostFocus += (_, _) => Record("editor-focus-lost");
            Activated += (_, _) => Record("form-activated");
            Deactivate += (_, _) => Record("form-deactivated");
        }
        Shown += (_, _) => Record("shown");
        Paint += (_, _) => Record("form-paint");
        FormClosed += (_, _) => Record("form-closed");
        _observer.Tick += (_, _) => Snapshot();
        ResumeLayout(false);
        PerformLayout();
        _observer.Start();
    }

    private void OpenModalDialog(string run)
    {
        if (_modalChild is not null)
            throw new InvalidOperationException("The previous modal source generation has not completed.");
        // A new owned destination for every actual user action; never replace
        // the parent's immutable snapshots or reuse a closed dialog's files.
        _modalEvidence = Path.Combine(_directory, "modal-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(_modalEvidence) || File.Exists(_modalEvidence))
            throw new IOException("The modal evidence destination must be new.");
        Directory.CreateDirectory(_modalEvidence);
        using var child = new InteractionForm(_modalEvidence, run + " modal", dialog: true);
        _modalChild = child;
        child.Shown += (_, _) =>
        {
            Record("modal-shown", JsonSerializer.Serialize(new
            {
                evidenceDirectory = _modalEvidence,
                ownerMatches = ReferenceEquals(child.Owner, this),
                ownerHandle = Handle.ToInt64(), dialogHandle = child.Handle.ToInt64(),
                ownerEnabled = Enabled, ownerActive = Form.ActiveForm == this,
                ownerInputEnabled = ObserveModalInputEnabled(),
                dialogActive = Form.ActiveForm == child
            }));
        };
        child.FormClosed += (_, e) => Record("modal-closed", e.CloseReason.ToString());
        Record("modal-open-request", JsonSerializer.Serialize(new
        {
            ownerHandle = Handle.ToInt64(), ownerEnabled = Enabled,
            ownerInputEnabled = ObserveModalInputEnabled(),
            activeControl = ActiveControl?.Name, editorFocused = _editor.Focused
        }));
        try
        {
            DialogResult result = child.ShowDialog(this);
            Record("modal-return", JsonSerializer.Serialize(new
            {
                result = result.ToString(), ownerEnabled = Enabled,
                ownerInputEnabled = ObserveModalInputEnabled(),
                ownerActive = Form.ActiveForm == this, activeControl = ActiveControl?.Name,
                editorFocused = _editor.Focused, dialogVisible = child.Visible
            }));
        }
        finally { _modalChild = null; }
    }

    private void RegisterPopup(string name, ToolStripDropDown popup)
    {
        _popups.Add(name, popup);
        popup.Opened += (_, _) => Record(name + "-opened");
        popup.Closed += (_, e) => Record(name + "-closed", e.CloseReason.ToString());
        popup.Paint += (_, _) => Record(name + "-paint");
    }

    private void Record(string name, string? detail = null)
    {
        _counts.TryGetValue(name, out int count);
        _counts[name] = count + 1;
        _events.WriteLine(JsonSerializer.Serialize(new { name, detail, elapsedMs = _clock.ElapsedMilliseconds }));
    }

    private static object RectangleRecord(Rectangle value)
        => new { x = value.X, y = value.Y, width = value.Width, height = value.Height };

    private bool? ObserveModalInputEnabled()
    {
#if LIBREWINFORMS_POPUP_APP
        // Read the existing typed source window's modal input policy. Public
        // Control.Enabled is a different managed property and may stay true.
        // This is not a query/proof of ordinary Cocoa native input blocking.
        if (!IsHandleCreated || IsDisposed || Disposing || !global::LibreWinForms.Platform.LibrePlatform.IsRegistered)
            return null;
        nint handle = Handle;
        var token = new global::LibreWinForms.Platform.LibreHandle(handle, global::LibreWinForms.Platform.LibreHandleKind.Window);
        var platform = global::LibreWinForms.Platform.LibrePlatform.Current;
        if (!platform.Handles.TryGet(token, out global::LibreWinForms.Platform.ILibreWindow? window) || window.Handle != token)
            return null;
        bool enabled = window.Enabled;
        if (IsDisposed || Disposing || !IsHandleCreated || Handle != handle ||
            !ReferenceEquals(global::LibreWinForms.Platform.LibrePlatform.Current, platform) ||
            !platform.Handles.TryGet(token, out global::LibreWinForms.Platform.ILibreWindow? current) ||
            !ReferenceEquals(current, window) || current.Handle != token)
            return null;
        return enabled;
#else
        // The external Windows driver reads actual IsWindowEnabled for the
        // PID/title-verified original HWND; do not infer it from Control.Enabled.
        return null;
#endif
    }

    private static object? ClientScreen(Control control)
        => control.IsHandleCreated && control.Visible
            ? RectangleRecord(new Rectangle(control.PointToScreen(Point.Empty), control.ClientSize)) : null;

    private void Snapshot()
    {
        // Observe public state only. Do not Focus, Show/Hide, Validate, assign
        // ActiveControl/Text, register DataError, or dispatch managed input.
        var state = new
        {
            schema = 1, pid = Environment.ProcessId, sequence = ++_sequence,
            elapsedMs = _clock.ElapsedMilliseconds, title = Text,
            form = new { client = ClientScreen(this), visible = Visible, active = Form.ActiveForm == this, dpi = DeviceDpi },
            editor = new { client = ClientScreen(_editor), text = _editor.Text, focused = _editor.Focused },
            contextTarget = ClientScreen(_contextTarget), tooltipTarget = ClientScreen(_tipTarget),
            combo = new { client = ClientScreen(_combo), droppedDown = _combo.DroppedDown, selectedIndex = _combo.SelectedIndex },
            modal = _modalButton is null ? null : new
            {
                button = ClientScreen(_modalButton), buttonName = _modalButton.Name,
                guard = _ownerInputGuard is null ? null : new { name = _ownerInputGuard.Name, client = ClientScreen(_ownerInputGuard) },
                evidenceDirectory = _modalEvidence, dialogVisible = _modalChild?.Visible,
                enabled = Enabled, activeControl = ActiveControl?.Name,
                inputEnabled = ObserveModalInputEnabled(),
                ownerHandle = Owner is { IsHandleCreated: true } modalOwner ? modalOwner.Handle.ToInt64() : (long?)null
            },
            counts = _counts,
            popups = _popups.ToDictionary(pair => pair.Key, pair => new { visible = pair.Value.Visible, client = ClientScreen(pair.Value) }),
            items = _items.ToDictionary(pair => pair.Key, pair => new
            {
                selected = pair.Value.Selected, enabled = pair.Value.Enabled,
                client = pair.Value.Owner is { IsHandleCreated: true, Visible: true } owner
                    ? RectangleRecord(new Rectangle(owner.PointToScreen(pair.Value.Bounds.Location), pair.Value.Bounds.Size)) : null
            })
        };
        RecordNativeGeometry(_sequence);
        string pending = Path.Combine(_directory, "snapshot.pending");
        File.WriteAllText(pending, JsonSerializer.Serialize(state));
        // Publish immutable snapshots: replacing a file concurrently open by a
        // Windows reader can fail even though its contents are read-only.
        File.Move(pending, Path.Combine(_directory, $"snapshot-{_sequence:D8}.json"));
    }

    // Optional portable-only observation; the Microsoft build erases this call.
    partial void RecordNativeGeometry(long sequence);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _observer.Dispose();
            _tip.Dispose();
            _context.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
            _events.Dispose();
    }
}
