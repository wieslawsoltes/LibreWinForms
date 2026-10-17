using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using LibreWinForms.Platform;
using LibreWinForms.ProGPU;
using ProGPU.Backend;

internal static class Program
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static StreamWriter? Journal;
    private static int Sequence;
    private static bool StartupPhases;

    [STAThread]
    private static int Main(string[] args)
    {
        if ((args.Length != 1 && (args.Length != 2 || args[1] != "--startup-phases")) || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true"
            || Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted"
            || Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") != "wieslawsoltes/LibreWinForms")
            throw new InvalidOperationException("Disposable hosted Forms runner only.");
        string directory = Path.GetFullPath(args[0]);
        if (Directory.Exists(directory)) throw new IOException("Evidence directory must be new.");
        Directory.CreateDirectory(directory);
        using LibrePlatformServices platform = PortableSetup.Register();
        string forms = typeof(Form).Assembly.Location;
        if (forms.Contains("Microsoft.WindowsDesktop.App", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Source-built portable framework required.");
        if (!Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)) throw new InvalidOperationException("Physical DPI policy failed.");
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var log = new StreamWriter(File.Open(Path.Combine(directory, "events.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        Journal = log;
        StartupPhases = args.Length == 2;
        Phase("framework-initialized");
        using var form = new Form { Text = $"PortableScrollBarRepeat [{Environment.ProcessId}]",
            AutoScaleMode = AutoScaleMode.None, StartPosition = FormStartPosition.Manual,
            Location = new Point(100, 100), ClientSize = new Size(620, 400) };
        Phase("form-constructed");
        var bars = new List<ScrollBar>();
        foreach (string name in new[] { "h-decrement", "h-increment", "h-page", "h-leave", "v-decrement", "v-increment", "v-page" })
        {
            Phase("control-start", name);
            bool vertical = name[0] == 'v';
            ScrollBar bar = vertical ? new VerticalProbe() : new HorizontalProbe();
            int index = bars.Count;
            bar.Name = name;
            bar.Bounds = vertical ? new Rectangle(430 + (index - 4) * 60, 30, 17, 300)
                : new Rectangle(30, 40 + index * 80, 350, 17);
            bar.Minimum = 0; bar.Maximum = 10000; bar.LargeChange = name.EndsWith("page", StringComparison.Ordinal) ? 1000 : 10;
            bar.SmallChange = 1; bar.Value = 5000;
            bar.Scroll += (_, e) => Record(new { kind = "scroll", name, type = e.Type.ToString(), e.OldValue, e.NewValue, value = bar.Value });
            bar.ValueChanged += (_, _) => Record(new { kind = "value", name, value = bar.Value });
            form.Controls.Add(bar); bars.Add(bar);
            Phase("control-added", name);
        }
        bool shown = false;
        form.Shown += (_, _) => { shown = true; Phase("shown"); };
        bool readyPublished = false;
        int samples = 0;
        void Observe()
        {
            if (++samples > 2200) throw new InvalidOperationException("Original observation budget exceeded.");
            if (!shown || !bars.All(b => PaintObservation.Latest.ContainsKey(b))) return;
            object ready = PortableSetup.Observe(form, bars, forms, Stopwatch.Frequency);
            string pending = Path.Combine(directory, "snapshot.tmp");
            File.WriteAllText(pending, JsonSerializer.Serialize(ready));
            File.Move(pending, Path.Combine(directory, $"snapshot-{samples:D6}.json"));
            if (!readyPublished)
            {
                File.WriteAllText(Path.Combine(directory, "ready.tmp"), JsonSerializer.Serialize(ready));
                File.Move(Path.Combine(directory, "ready.tmp"), Path.Combine(directory, "ready.json"));
                Record(new { kind = "ready" });
                readyPublished = true;
            }
        }
        bool completed = false;
        using var timer = new System.Windows.Forms.Timer { Interval = 25 };
        timer.Tick += (_, _) =>
        {
            Observe();
            completed = File.Exists(Path.Combine(directory, "complete"));
            if (completed || Clock.Elapsed.TotalSeconds >= 55) form.Close();
        };
        timer.Start();
        Phase("before-message-loop");
        Application.Run(form);
        Record(new { kind = "closed", completed });
        return completed ? 0 : 1;
    }

    private static object Rect(Rectangle r) => new { x = r.X, y = r.Y, width = r.Width, height = r.Height };
    private interface IOriginalScrollBar { string? SourceClass { get; } }
    private static void Phase(string phase, string? control = null)
    {
        if (StartupPhases) Record(new { kind = "startup-phase", phase, control });
    }
    private static void Record(object value)
    {
        if (++Sequence > 1024) throw new InvalidOperationException("Original event budget exceeded.");
        Journal!.WriteLine(JsonSerializer.Serialize(new { sequence = Sequence, timestamp = Stopwatch.GetTimestamp(), seconds = Clock.Elapsed.TotalSeconds, value }));
    }
    private sealed class HorizontalProbe : HScrollBar, IOriginalScrollBar
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            using var observation = new PaintObservation(this, e.Graphics);
            base.OnPaint(e);
            observation.Commit();
        }
        public string? SourceClass => base.CreateParams.ClassName;
        protected override void WndProc(ref Message m)
        {
            if (m.Msg is 0x113 or 0x201 or 0x202) Record(new { kind = "native", name = Name, message = m.Msg, wparam = (long)m.WParam, value = Value });
            base.WndProc(ref m);
        }
    }
    private sealed class VerticalProbe : VScrollBar, IOriginalScrollBar
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            using var observation = new PaintObservation(this, e.Graphics);
            base.OnPaint(e);
            observation.Commit();
        }
        public string? SourceClass => base.CreateParams.ClassName;
        protected override void WndProc(ref Message m)
        {
            if (m.Msg is 0x113 or 0x201 or 0x202) Record(new { kind = "native", name = Name, message = m.Msg, wparam = (long)m.WParam, value = Value });
            base.WndProc(ref m);
        }
    }
}
