// Passive original-sample observer: never register validation, focus or input handlers.
#nullable enable
using System;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

internal static class OriginalGridObserver
{
    private static readonly System.Windows.Forms.Timer Samples = new() { Interval = 100 };
    private static Form? _form;
    private static DataGridView? _grid;
    private static int _sequence;

    [ModuleInitializer]
    internal static void Install()
    {
        if (Environment.GetEnvironmentVariable("LIBREWINFORMS_GRID_OBSERVER") != "1") return;
        Application.Idle += FindSample;
        Samples.Tick += (_, _) => Sample();
        Application.ApplicationExit += (_, _) => Samples.Dispose();
    }

    private static void FindSample(object? sender, EventArgs args)
    {
        foreach (Form form in Application.OpenForms)
        {
            if (form is not CSWinFormDataGridView.CustomDataGridViewColumn.MainForm
                || !form.Visible || !form.IsHandleCreated)
                continue;
            var grid = form.Controls.OfType<DataGridView>()
                .SingleOrDefault(control => control.Name == "employeesDataGridView");
            if (grid is null || !grid.IsHandleCreated || grid.ColumnCount != 8 || grid.Rows.Count == 0)
                continue;
            _form = form;
            _grid = grid;
            Application.Idle -= FindSample;
            Samples.Start();
        }
    }

    private static int[]? Units(string? text) => text?.Select(character => (int)character).ToArray();

    private static int[] Rect(Rectangle rect) => new[] { rect.X, rect.Y, rect.Width, rect.Height };

    private static void Sample()
    {
        if (++_sequence > 600 || _form is not { IsDisposed: false, IsHandleCreated: true } form
            || _grid is not { IsDisposed: false, IsHandleCreated: true } grid)
        {
            Samples.Stop();
            return;
        }
        if (grid.VirtualMode) throw new InvalidOperationException("This observer admits the original nonvirtual sample only.");
        var editor = grid.EditingControl as TextBoxBase;
        var address = grid.CurrentCellAddress;
        var state = new OriginalGridState
        {
            Schema = "original-grid-observer-v1",
            ProcessId = Environment.ProcessId, Sequence = _sequence,
            FormTitle = form.Text, DeviceDpi = form.DeviceDpi,
            Client = Rect(form.RectangleToScreen(form.ClientRectangle)),
            GridClient = Rect(grid.RectangleToScreen(grid.ClientRectangle)),
            Cell = Rect(grid.RectangleToScreen(grid.GetCellDisplayRectangle(0, 0, true))),
            FontName = form.Font.Name, FontSize = form.Font.Size, FontHeight = form.Font.Height,
            AutoScaleX = form.CurrentAutoScaleDimensions.Width,
            AutoScaleY = form.CurrentAutoScaleDimensions.Height,
            TemplateHeight = grid.RowTemplate.Height, ActualRowHeight = grid.Rows[0].Height,
            HeaderHeight = grid.ColumnHeadersHeight,
            ColumnWidths = grid.Columns.Cast<DataGridViewColumn>().Select(column => column.Width).ToArray(),
            ColumnHeaders = grid.Columns.Cast<DataGridViewColumn>().Select(column => column.HeaderText).ToArray(),
            FormsAssembly = typeof(Form).Assembly.Location,
            DrawingAssembly = typeof(Font).Assembly.Location,
            FormFocused = form.ContainsFocus,
            InEdit = grid.IsCurrentCellInEditMode,
            CurrentColumn = address.X, CurrentRow = address.Y,
            FirstNameUtf16 = Units(grid.Rows[0].Cells[0].Value as string),
            EditorFocused = editor is { IsDisposed: false, IsHandleCreated: true } && editor.Focused,
            EditorUtf16 = editor is { IsDisposed: false, IsHandleCreated: true } ? Units(editor.Text) : null,
            SelectionStart = editor is { IsDisposed: false, IsHandleCreated: true } ? editor.SelectionStart : -1,
            SelectionLength = editor is { IsDisposed: false, IsHandleCreated: true } ? editor.SelectionLength : -1
        };
        Console.WriteLine("GRID_EDITING " + JsonSerializer.Serialize(state, OriginalGridJson.Default.OriginalGridState));
        Console.Out.Flush();
    }
}

internal sealed class OriginalGridState
{
    public string Schema { get; set; } = "";
    public bool InEdit { get; set; }
    public int CurrentColumn { get; set; }
    public int CurrentRow { get; set; }
    public int[]? FirstNameUtf16 { get; set; }
    public bool EditorFocused { get; set; }
    public int[]? EditorUtf16 { get; set; }
    public int SelectionStart { get; set; }
    public int SelectionLength { get; set; }
    public int ProcessId { get; set; }
    public int Sequence { get; set; }
    public string FormTitle { get; set; } = "";
    public int DeviceDpi { get; set; }
    public int[] Client { get; set; } = Array.Empty<int>();
    public int[] GridClient { get; set; } = Array.Empty<int>();
    public int[] Cell { get; set; } = Array.Empty<int>();
    public string FontName { get; set; } = "";
    public float FontSize { get; set; }
    public int FontHeight { get; set; }
    public float AutoScaleX { get; set; }
    public float AutoScaleY { get; set; }
    public int TemplateHeight { get; set; }
    public int ActualRowHeight { get; set; }
    public int HeaderHeight { get; set; }
    public int[] ColumnWidths { get; set; } = Array.Empty<int>();
    public string[] ColumnHeaders { get; set; } = Array.Empty<string>();
    public string FormsAssembly { get; set; } = "";
    public string DrawingAssembly { get; set; } = "";
    public bool FormFocused { get; set; }
}

[JsonSerializable(typeof(OriginalGridState))]
internal partial class OriginalGridJson : JsonSerializerContext { }
