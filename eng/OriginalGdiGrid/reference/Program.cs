using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Forms;

internal static class Program
{
    internal const string Text = "Name Age 123 Hello Windows Forms";
    internal const int Width = 384, Height = 64;
    private static readonly Stopwatch Clock = new();
    private static readonly List<object> Captures = [];
    private static long EvidenceBytes, PngBytes;
    private static string Output = "";
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static void Budget()
    {
        if (Clock.Elapsed.TotalSeconds >= 45 || Captures.Count > 206 || PngBytes > 16 * 1024 * 1024 || EvidenceBytes > 48 * 1024 * 1024)
            throw new TimeoutException("Original 45-second process / fixed 206-capture evidence bound exceeded.");
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try { Execute(args); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Execute(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length != 1 || Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted" || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
            throw new InvalidOperationException("Disposable original Windows reference only.");
        Output = Path.GetFullPath(args[0]);
        string temporary = Path.GetFullPath(Environment.GetEnvironmentVariable("RUNNER_TEMP")!);
        if (Output != args[0] || !Output.StartsWith(temporary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Directory.Exists(Output) || File.Exists(Output))
            throw new InvalidOperationException("New task-owned RUNNER_TEMP output required.");
        foreach (Type type in new[] { typeof(Form), typeof(Graphics) })
            if (!type.Assembly.Location.Contains("Microsoft.WindowsDesktop.App", StringComparison.Ordinal)) throw new InvalidOperationException("Original Microsoft framework required.");
        Directory.CreateDirectory(Output); Clock.Start();
        var before = Settings(); List<object> fonts = [];
        File.WriteAllText(Path.Combine(Output,"system-before.json"),JsonSerializer.Serialize(before));
        foreach (var spec in new[] {
            (Name:"Microsoft Sans Serif", Points:9.75f, Sha:"89b42a12ea0379133fb2f4a1d1bd53058fb61e2343c1d509452d5761acc85b7a"),
            (Name:"Segoe UI", Points:9f, Sha:"8134dbcd09e7b123c9a7f229d49cffbcb01352cc72ea5e1076b65d0dca9f73cd") })
        {
            Budget(); using Font font = new(spec.Name, spec.Points, FontStyle.Regular, GraphicsUnit.Point, 0);
            if (font.Name != spec.Name) throw new InvalidOperationException("Original font substituted.");
            using var native = new GdiMemoryFont(font, spec.Sha);
            fonts.Add(new { spec.Name, spec.Points, sourceSha256=spec.Sha, native.Metadata });
            File.WriteAllText(Path.Combine(Output,"fonts.json"),JsonSerializer.Serialize(fonts));
            using (Bitmap original = Canonical(font, Color.White, opaque:true)) Save(original, spec.Name, "canonical-original-opaque", -1, Color.White);
            foreach (Color background in new[] { Color.White, Color.Black, Color.FromArgb(255, 53, 117, 181) })
            {
                Budget();
                using (Bitmap canonical = Canonical(font, background, opaque:false)) Save(canonical, spec.Name, "canonical-transparent", -1, background);
                Save(native.Draw(background, sourceIndex:null), spec.Name, "gdi-realized-text", -1, background);
                for (int slot=0; slot<Text.Length; slot++)
                {
                    Budget(); Save(native.Draw(background, slot), spec.Name, "gdi-glyph-index", slot, background);
                }
            }
        }
        var after = Settings(); Budget();
        if (Captures.Count != 206 || JsonSerializer.Serialize(before) != JsonSerializer.Serialize(after)) throw new InvalidOperationException("Incomplete inventory or changed system text state.");
        File.WriteAllText(Path.Combine(Output,"reference.json"),JsonSerializer.Serialize(new { schema="original-gdi-grid-reference-v1", qualified=false, inputEvents=0, desktopWindows=0,
            text=Text, width=Width, height=Height, dpi=96, before, after, fonts, captures=Captures, elapsedSeconds=Clock.Elapsed.TotalSeconds,
            evidenceBytes=EvidenceBytes, runtime=Environment.Version.ToString(), os=Environment.OSVersion.VersionString,
            formsAssembly=typeof(Form).Assembly.Location, drawingAssembly=typeof(Graphics).Assembly.Location },new JsonSerializerOptions{WriteIndented=true}));
        Budget();
    }
    private static Bitmap Canonical(Font font, Color background, bool opaque)
    {
        Bitmap bitmap = new(Width,Height,PixelFormat.Format32bppArgb); bitmap.SetResolution(96,96);
        try {
            using Graphics graphics=Graphics.FromImage(bitmap); graphics.Clear(background); graphics.TextRenderingHint=TextRenderingHint.SystemDefault; graphics.TextContrast=4;
            var flags=TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine;
            if(opaque) TextRenderer.DrawText(graphics,Text,font,new Rectangle(8,8,Width-16,Height-16),Color.Black,Color.White,flags);
            else TextRenderer.DrawText(graphics,Text,font,new Rectangle(8,8,Width-16,Height-16),Color.Black,flags);
            return bitmap;
        } catch { bitmap.Dispose(); throw; }
    }
    private static object[] Settings() => new uint[]{0x004a,0x200a,0x200c,0x2012}.Select(action=>{
        bool success=SystemParametersInfoW(action,0,out uint value,0);return (object)new {action,success,value,error=success?0:Marshal.GetLastWin32Error()}; }).ToArray();
    private static void Save(Bitmap bitmap,string font,string route,int slot,Color background)
    {
        byte[] rgba=new byte[Width*Height*4];
        for(int y=0;y<Height;y++) for(int x=0;x<Width;x++) { Color c=bitmap.GetPixel(x,y);int i=(y*Width+x)*4;rgba[i]=c.R;rgba[i+1]=c.G;rgba[i+2]=c.B;rgba[i+3]=c.A; }
        Save(rgba,font,route,slot,background);
    }
    private static void Save(byte[] rgba,string font,string route,int slot,Color background)
    {
        Budget(); string stem=$"{Captures.Count:D3}-{route}-{slot}"; string raw=stem+".rgba",png=stem+".png";
        File.WriteAllBytes(Path.Combine(Output,raw),rgba);
        using Bitmap bitmap=new(Width,Height,PixelFormat.Format32bppArgb);
        for(int y=0;y<Height;y++) for(int x=0;x<Width;x++) { int i=(y*Width+x)*4;bitmap.SetPixel(x,y,Color.FromArgb(rgba[i+3],rgba[i],rgba[i+1],rgba[i+2])); }
        bitmap.Save(Path.Combine(Output,png),ImageFormat.Png);byte[] image=File.ReadAllBytes(Path.Combine(Output,png));EvidenceBytes+=rgba.Length+image.Length;PngBytes+=image.Length;
        Captures.Add(new {font,route,slot,character=slot<0?(int?)null:Text[slot],background=background.ToArgb(),foreground=Color.Black.ToArgb(),raw,rgbaSha256=Hash(rgba),png,pngSha256=Hash(image),alphaPolicy="Unmodified observed alpha bytes; BI_RGB GDI alpha is not a coverage claim."});
        File.WriteAllText(Path.Combine(Output,"partial-captures.json"),JsonSerializer.Serialize(Captures));
        Budget();
    }
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint action,uint parameter,out uint value,uint flags);
}
