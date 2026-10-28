using System.Buffers.Binary;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

internal static class Program
{
    private static readonly Stopwatch Clock=new();
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Budget(){if(Clock.Elapsed.TotalSeconds>=15)throw new TimeoutException("15-second metadata observation limit.");}
    [STAThread] private static int Main(string[] args)
    {
        string? output=null;var result=new Dictionary<string,object?>{["qualified"]=false,["completed"]=false,["inputEvents"]=0,["desktopWindows"]=0,["imageCaptures"]=0};
        try
        {
            if(!OperatingSystem.IsWindows() || args.Length!=1 || Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT")!="github-hosted")throw new InvalidOperationException("Hosted Windows metadata reference only.");
            output=Path.GetFullPath(args[0]);string temporary=Path.GetFullPath(Environment.GetEnvironmentVariable("RUNNER_TEMP")!);
            if(output!=args[0] || !output.StartsWith(temporary+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || File.Exists(output) || Directory.Exists(output))throw new InvalidOperationException("Fresh owned output required.");
            Directory.CreateDirectory(output);Clock.Start();
            using Stream input=Assembly.GetExecutingAssembly().GetManifestResourceStream("original.json")!;using JsonDocument original=JsonDocument.Parse(input);
            result["original"]=original.RootElement.Clone();byte[] expectedLogFont=Convert.FromBase64String(original.RootElement.GetProperty("selectedLogFontBase64").GetString()!);
            if(expectedLogFont.Length!=92)throw new InvalidOperationException("Original LOGFONT size changed.");
            using Font font=new("Segoe UI",9f,FontStyle.Regular,GraphicsUnit.Point,0);
            result["managedFont"]=new {font.Name,font.OriginalFontName,font.SizeInPoints,font.GdiCharSet,font.Style};
            List<object> selections=[];result["selections"]=selections;
            selections.Add(Observe("original-Font.ToHfont",font.ToHfont(),expectedLogFont,original.RootElement.GetProperty("fontSha256").GetString()!));
            selections.Add(Observe("CreateFontIndirectW-exact-original-LOGFONT",CreateFontIndirectW(expectedLogFont),expectedLogFont,original.RootElement.GetProperty("fontSha256").GetString()!));
            result["runtime"]=Environment.Version.ToString();result["os"]=Environment.OSVersion.VersionString;result["osDescription"]=RuntimeInformation.OSDescription;
            result["frameworks"]=new[]{typeof(Form).Assembly,typeof(Graphics).Assembly}.Select(assembly=>{
                string path=assembly.Location;if(!path.Contains("Microsoft.WindowsDesktop.App",StringComparison.Ordinal))throw new InvalidOperationException("Original Microsoft framework required.");
                return new {path,sha256=Hash(File.ReadAllBytes(path)),version=FileVersionInfo.GetVersionInfo(path).FileVersion};}).ToArray();
            using Process process=Process.GetCurrentProcess();List<object> modules=[];
            foreach(ProcessModule module in process.Modules)if(module.ModuleName.Equals("gdi32.dll",StringComparison.OrdinalIgnoreCase)||module.ModuleName.Equals("gdiplus.dll",StringComparison.OrdinalIgnoreCase))modules.Add(new {path=module.FileName,version=module.FileVersionInfo.FileVersion,sha256=Hash(File.ReadAllBytes(module.FileName))});
            result["loadedOriginalModules"]=modules;Budget();result["elapsedSeconds"]=Clock.Elapsed.TotalSeconds;result["completed"]=true;
            File.WriteAllText(Path.Combine(output,"metadata.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Budget();return 0;
        }
        catch(Exception error){result["error"]=error.ToString();result["completed"]=false;Console.Error.WriteLine(error);if(output!=null&&Directory.Exists(output))File.WriteAllText(Path.Combine(output,"metadata.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));return 1;}
    }
    private static object Observe(string recipe,nint font,byte[] expectedLogFont,string expectedSha)
    {
        nint dc=0,old=0;Exception? primary=null;
        try
        {
            Budget();Check(font!=0);dc=CreateCompatibleDC(0);Check(dc!=0);old=SelectObject(dc,font);Check(old!=0&&old!=-1);
            byte[] actualLogFont=new byte[92];Check(GetObjectW(font,92,actualLogFont)==92);StringBuilder face=new(128);Check(GetTextFaceW(dc,128,face)>0);Check(GetTextMetricsW(dc,out TEXTMETRIC metrics));
            uint length=GetFontData(dc,0,0,null,0);Check(length>0&&length<=32*1024*1024);byte[] bytes=new byte[length];Check(GetFontData(dc,0,0,bytes,length)==length);string hash=Hash(bytes);
            var names=Names(bytes);string candidate=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Fonts","segoeui.ttf");
            object file;
            if(File.Exists(candidate)){long size=new FileInfo(candidate).Length;Check(size>0&&size<=32*1024*1024);string fileHash=Hash(File.ReadAllBytes(candidate));file=new {candidatePath=candidate,bytes=size,sha256=fileHash,exactSelectedBytes=fileHash==hash};}
            else file=new {candidatePath=candidate,available=false};
            Budget();return new {recipe,nativeFace=face.ToString(),selectedLogFontBase64=Convert.ToBase64String(actualLogFont),exactOriginalLogFont=actualLogFont.AsSpan().SequenceEqual(expectedLogFont),
                height=BinaryPrimitives.ReadInt32LittleEndian(actualLogFont),width=BinaryPrimitives.ReadInt32LittleEndian(actualLogFont.AsSpan(4)),weight=BinaryPrimitives.ReadInt32LittleEndian(actualLogFont.AsSpan(16)),charset=actualLogFont[23],quality=actualLogFont[26],
                fontBytes=length,fontSha256=hash,exactHistoricalFont=hash==expectedSha,names,matchingWindowsFontsFile=file,
                dpiX=GetDeviceCaps(dc,88),dpiY=GetDeviceCaps(dc,90),metrics=new {metrics.Height,metrics.Ascent,metrics.Descent,metrics.InternalLeading,metrics.ExternalLeading,metrics.AveCharWidth,metrics.MaxCharWidth,metrics.CharSet}};
        }
        catch(Exception error){primary=error;throw;}
        finally
        {
            List<string> errors=[];if(dc!=0&&old!=0&&old!=-1){nint restored=SelectObject(dc,old);if(restored==0||restored==-1)errors.Add("font restore");}if(dc!=0&&!DeleteDC(dc))errors.Add("DC delete");if(font!=0&&!DeleteObject(font))errors.Add("font delete");
            if(errors.Count>0){string message=string.Join(",",errors);if(primary!=null)primary.Data["NativeCleanup"]=message;else throw new InvalidOperationException(message);}
        }
    }
    private static object[] Names(byte[] bytes)
    {
        static ushort U16(ReadOnlySpan<byte> s,int o)=>BinaryPrimitives.ReadUInt16BigEndian(s.Slice(o,2));
        static uint U32(ReadOnlySpan<byte> s,int o)=>BinaryPrimitives.ReadUInt32BigEndian(s.Slice(o,4));
        if(bytes.Length<12||U32(bytes,0)!=0x00010000)throw new InvalidOperationException("Expected selected static TrueType sfnt.");
        int count=U16(bytes,4);if(count>256||12+count*16>bytes.Length)throw new InvalidOperationException("Font directory bound.");
        var entries=new List<(uint Offset,uint Length)>();
        for(int i=0;i<count;i++){int p=12+i*16;if(U32(bytes,p)==0x6e616d65)entries.Add((U32(bytes,p+8),U32(bytes,p+12)));}
        if(entries.Count!=1)throw new InvalidOperationException("One original name table required.");var entry=entries[0];if(entry.Length<6||entry.Length>128*1024||(ulong)entry.Offset+entry.Length>(ulong)bytes.Length)throw new InvalidOperationException("Name table extent.");
        ReadOnlySpan<byte> table=bytes.AsSpan((int)entry.Offset,(int)entry.Length);int records=U16(table,2),storage=U16(table,4);if(records>2048||6+records*12>table.Length||storage<6+records*12)throw new InvalidOperationException("Name inventory bound.");
        List<object> names=[];
        for(int i=0;i<records;i++){int p=6+i*12;ushort platform=U16(table,p),encoding=U16(table,p+2),language=U16(table,p+4),id=U16(table,p+6),length=U16(table,p+8),offset=U16(table,p+10);int start=storage+offset;if(start+length>table.Length)throw new InvalidOperationException("Name string extent.");
            if(platform==3 && (encoding==1||encoding==10) && language==0x409 && id>=1 && id<=6){if(length>1024 || (length&1)!=0 || names.Count>=32)throw new InvalidOperationException("Name metadata bound.");names.Add(new{platform,encoding,language,id,text=Encoding.BigEndianUnicode.GetString(table.Slice(start,length))});}}
        return names.ToArray();
    }
    private static void Check(bool ok){if(!ok)throw new InvalidOperationException("Original GDI metadata call failed: "+Marshal.GetLastWin32Error());}
    [StructLayout(LayoutKind.Sequential)] private struct TEXTMETRIC {internal int Height,Ascent,Descent,InternalLeading,ExternalLeading,AveCharWidth,MaxCharWidth,Weight,Overhang,DigitizedAspectX,DigitizedAspectY;internal ushort FirstChar,LastChar,DefaultChar,BreakChar;internal byte Italic,Underlined,StruckOut,PitchAndFamily,CharSet;}
    [DllImport("gdi32.dll",SetLastError=true)]private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")]private static extern int GetDeviceCaps(nint dc,int index);
    [DllImport("gdi32.dll",SetLastError=true)]private static extern nint CreateFontIndirectW(byte[] logfont);
    [DllImport("gdi32.dll",SetLastError=true)]private static extern nint SelectObject(nint dc,nint value);
    [DllImport("gdi32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll",SetLastError=true)]private static extern int GetObjectW(nint obj,int length,[Out]byte[] data);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern int GetTextFaceW(nint dc,int length,StringBuilder name);
    [DllImport("gdi32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetTextMetricsW(nint dc,out TEXTMETRIC metrics);
    [DllImport("gdi32.dll",SetLastError=true)]private static extern uint GetFontData(nint dc,uint table,uint offset,[Out]byte[]? bytes,uint length);
}
