using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    static void Run(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length!=2 ||
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true" ||
            Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT")!="github-hosted")
            throw new InvalidOperationException("Disposable hosted Windows only; no desktop or input.");
        string input=Path.GetFullPath(args[0]), output=Path.GetFullPath(args[1]);
        string temporary=Path.GetFullPath(Environment.GetEnvironmentVariable("RUNNER_TEMP")!);
        if(!output.StartsWith(temporary+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) ||
            Directory.Exists(output) || File.Exists(output)) throw new InvalidOperationException("New owned output required.");
        if(!typeof(Graphics).Assembly.Location.Contains("Microsoft.WindowsDesktop.App",StringComparison.Ordinal))
            throw new InvalidOperationException("Original Microsoft Drawing required.");
        using JsonDocument manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(input,"inputs.json")));
        JsonElement data=manifest.RootElement;
        if(data.GetProperty("glyphs").GetArrayLength()!=440 || data.GetProperty("fonts").GetArrayLength()!=2 ||
            data.GetProperty("ppem").GetInt32()!=32) throw new InvalidOperationException("Authored input inventory changed.");
        string text=new(Enumerable.Range(0,440).Select(i=>(char)(0xe000+i)).ToArray());
        Directory.CreateDirectory(output);
        Stopwatch clock=Stopwatch.StartNew();
        List<object> fonts=[],frames=[];
        long pngBytes=0;
        foreach(JsonElement specification in data.GetProperty("fonts").EnumerateArray())
        {
            string file=specification.GetProperty("file").GetString()!;
            if(Path.GetFileName(file)!=file) throw new InvalidOperationException("Invalid input name.");
            string path=Path.Combine(input,file), family=specification.GetProperty("family").GetString()!;
            string expected=specification.GetProperty("sha256").GetString()!;
            if(Hash(File.ReadAllBytes(path))!=expected) throw new InvalidOperationException("Authored font hash changed.");
            if(AddFontResourceExW(path,0x10,0)!=1) throw new InvalidOperationException("Private GDI font admission failed.");
            try
            {
                using PrivateFontCollection collection=new();
                collection.AddFontFile(path);
                using FontFamily selected=collection.Families.Single();
                if(selected.Name!=family) throw new InvalidOperationException("Private face substitution.");
                using Font font=new(selected,32,FontStyle.Regular,GraphicsUnit.Pixel);
                fonts.Add(Observe(font,text,expected,specification.GetProperty("gasp").GetInt32()));
                foreach(int hint in new[]{1,2,3}) // SingleBitGridFit, SingleBit, AntiAliasGridFit.
                for(int i=0;i<440;i++)
                {
                    Budget();
                    using Bitmap bitmap=NativeDriver.Draw(family,path,24,hint,text[i].ToString(),
                        [new PointF(8,24)],"OriginalDesignAdvance");
                    if(bitmap.Size!=new Size(64,64)) throw new InvalidOperationException("Unexpected frame.");
                    byte[] bgra=new byte[64*64*4];
                    BitmapData pixels=bitmap.LockBits(new Rectangle(0,0,64,64),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                    try { for(int y=0;y<64;y++) Marshal.Copy(pixels.Scan0+y*pixels.Stride,bgra,y*256,256); }
                    finally { bitmap.UnlockBits(pixels); }
                    string name=$"scan-{frames.Count:D4}.png";
                    bitmap.Save(Path.Combine(output,name),ImageFormat.Png);
                    byte[] png=File.ReadAllBytes(Path.Combine(output,name));pngBytes+=png.Length;
                    frames.Add(new {family,hint,index=i+1,codepoint=(int)text[i],originX=8,originY=24,
                        bgraSha256=Hash(bgra),image=new{name,bytes=png.Length,sha256=Hash(png)}});
                }
            }
            finally { if(!RemoveFontResourceExW(path,0x10,0)) throw new InvalidOperationException("Private GDI font cleanup failed."); }
        }
        Budget();
        if(fonts.Count!=2 || frames.Count!=2640) throw new InvalidOperationException("Incomplete authored inventory.");
        File.WriteAllText(Path.Combine(output,"reference.json"),JsonSerializer.Serialize(new {
            schema="authored-microsoft-scan-reference-v1",qualified=false,inputEvents=0,desktopWindows=0,
            ppem=32,dpi=96,fonts,frames,pngBytes,elapsedSeconds=clock.Elapsed.TotalSeconds,
            inputManifestSha256=Hash(File.ReadAllBytes(Path.Combine(input,"inputs.json"))),
            runtime=Environment.Version.ToString(),os=Environment.OSVersion.VersionString,
            limitation="Authored analytic scan observations; original system-font/application acceptance remains independent."
        },new JsonSerializerOptions{WriteIndented=true}));
        void Budget() { if(clock.Elapsed.TotalSeconds>45 || pngBytes>16*1024*1024) throw new TimeoutException("45-second/16-MiB reference bound exceeded."); }
    }

    static object Observe(Font font,string text,string expected,int gasp)
    {
        nint dc=CreateCompatibleDC(0), selected=0,old=0;
        if(dc==0) throw new InvalidOperationException("Owned DC unavailable.");
        try
        {
            selected=font.ToHfont();old=SelectObject(dc,selected);
            if(selected==0 || old==0 || old==-1) throw new InvalidOperationException("Owned font selection failed.");
            uint size=GetFontData(dc,0,0,null,0);
            if(size==0 || size>1024*1024) throw new InvalidOperationException("Selected font size invalid.");
            byte[] bytes=new byte[checked((int)size)];
            if(GetFontData(dc,0,0,bytes,size)!=size || Hash(bytes)!=expected)
                throw new InvalidOperationException("Selected original font bytes differ from authored input.");
            return new {family=font.Name,gasp,sha256=Hash(bytes),
                outlines=GdiOutlineCapture.Capture(dc,selected,text),
                bitmaps=GdiBitmapCapture.Capture(dc,selected,text)};
        }
        finally
        {
            if(old!=0 && old!=-1) SelectObject(dc,old);
            if(selected!=0) DeleteObject(selected);
            DeleteDC(dc);
        }
    }
    static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] static extern int AddFontResourceExW(string path,uint flags,nint reserved);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool RemoveFontResourceExW(string path,uint flags,nint reserved);
    [DllImport("gdi32.dll",ExactSpelling=true)] static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll",ExactSpelling=true)] static extern nint SelectObject(nint dc,nint value);
    [DllImport("gdi32.dll",ExactSpelling=true)] static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll",ExactSpelling=true)] static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll",ExactSpelling=true)] static extern uint GetFontData(nint dc,uint table,uint offset,[Out] byte[]? bytes,uint count);
}
