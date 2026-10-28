using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

// Original Windows diagnostic only. No product renderer, window, input or font installation.
internal sealed class GdiMemoryFont : IDisposable
{
    private nint _dc, _font, _bitmap, _oldFont, _oldBitmap, _bits;
    private readonly ushort[] _glyphs = new ushort[Program.Text.Length];
    private TEXTMETRIC _metrics;
    internal object Metadata { get; private set; } = null!;
    internal GdiMemoryFont(Font font, string expectedSha)
    {
        try
        {
            _dc=CreateCompatibleDC(0); Check(_dc!=0);
            _font=font.ToHfont(); Check(_font!=0); _oldFont=SelectObject(_dc,_font); Check(Valid(_oldFont));
            byte[] logfont=new byte[92]; Check(GetObjectW(_font,logfont.Length,logfont)==92);
            if(logfont[26]!=0) throw new InvalidOperationException("Original DEFAULT_QUALITY selection required.");
            StringBuilder face=new(128); Check(GetTextFaceW(_dc,face.Capacity,face)>0);
            if(face.ToString()!=font.Name) throw new InvalidOperationException("Selected native face differs.");
            uint length=GetFontData(_dc,0,0,null,0); if(length==0 || length==uint.MaxValue || length>32*1024*1024) throw new InvalidOperationException("Original font extent invalid.");
            byte[] bytes=new byte[length]; Check(GetFontData(_dc,0,0,bytes,length)==length);
            string actualSha=Program.Hash(bytes);
            if(actualSha!=expectedSha) throw new InvalidOperationException($"Original selected font hash changed: {face}, bytes={length}, SHA256={actualSha}; expected={expectedSha}. Do not substitute a baseline.");
            Check(GetTextMetricsW(_dc,out _metrics));
            if(GetDeviceCaps(_dc,88)!=96 || GetDeviceCaps(_dc,90)!=96) throw new InvalidOperationException("Original 96-DPI reference required.");
            Check(GetGlyphIndicesW(_dc,Program.Text,Program.Text.Length,_glyphs,1)==(uint)_glyphs.Length);
            if(_glyphs.Any(g=>g==ushort.MaxValue)) throw new InvalidOperationException("Original glyph missing.");
            int[] cumulative=new int[_glyphs.Length]; Check(GetTextExtentExPointI(_dc,_glyphs,_glyphs.Length,int.MaxValue,out int fit,cumulative,out SIZE extent));
            if(fit!=_glyphs.Length) throw new InvalidOperationException("Incomplete native extent observation.");
            ABC[] widths=new ABC[_glyphs.Length]; Check(GetCharABCWidthsI(_dc,0,(uint)_glyphs.Length,_glyphs,widths));
            Metadata=new {fontSha256=expectedSha,selectedLogFontBase64=Convert.ToBase64String(logfont),requestedQuality=0,nativeFace=face.ToString(),
                glyphIndices=_glyphs,metrics=new {_metrics.Height,_metrics.Ascent,_metrics.Descent,_metrics.InternalLeading,_metrics.ExternalLeading,_metrics.AveCharWidth,_metrics.MaxCharWidth},
                cumulativeAdvances=cumulative,extent=new {extent.X,extent.Y},abc=widths.Select(w=>new {w.A,w.B,w.C}).ToArray(),
                fullTextOrigin=new {x=8,y=8+_metrics.Ascent},glyphOrigin=new{x=16,y=32},placement="Original realized GDI advances; isolated glyph origin explicit. No supplied lpDx."};
            BITMAPINFOHEADER header=new(){Size=40,Width=Program.Width,Height=-Program.Height,Planes=1,BitCount=32,SizeImage=Program.Width*Program.Height*4};
            _bitmap=CreateDIBSection(_dc,ref header,0,out _bits,0,0); Check(_bitmap!=0 && _bits!=0); _oldBitmap=SelectObject(_dc,_bitmap); Check(Valid(_oldBitmap));
            Check(SetBkMode(_dc,1)!=0); Check(SetTextAlign(_dc,24)!=uint.MaxValue); Check(SetTextColor(_dc,0)!=uint.MaxValue);
        }
        catch(Exception primary) { try { Dispose(); } catch(Exception cleanup) {primary.Data["NativeCleanup"]=cleanup.ToString();} throw; }
    }
    internal byte[] Draw(Color background, int? sourceIndex)
    {
        if(_dc==0) throw new ObjectDisposedException(nameof(GdiMemoryFont)); Program.Budget();
        byte[] bgra=new byte[Program.Width*Program.Height*4];
        for(int i=0;i<bgra.Length;i+=4){bgra[i]=background.B;bgra[i+1]=background.G;bgra[i+2]=background.R;bgra[i+3]=background.A;}
        Marshal.Copy(bgra,0,_bits,bgra.Length);
        if(sourceIndex is int slot)
        {
            if((uint)slot >= (uint)_glyphs.Length) throw new ArgumentOutOfRangeException(nameof(sourceIndex));
            Check(ExtTextOutGlyphs(_dc,16,32,0x10,0,new[]{_glyphs[slot]},1,0));
        }
        else Check(ExtTextOutText(_dc,8,8+_metrics.Ascent,0,0,Program.Text,(uint)Program.Text.Length,0));
        Check(GdiFlush()); Marshal.Copy(_bits,bgra,0,bgra.Length);
        for(int i=0;i<bgra.Length;i+=4) (bgra[i],bgra[i+2])=(bgra[i+2],bgra[i]);
        return bgra;
    }
    public void Dispose()
    {
        List<string> errors=[];
        if(_dc!=0 && Valid(_oldBitmap) && !Valid(SelectObject(_dc,_oldBitmap))) errors.Add("restore bitmap");
        if(_dc!=0 && Valid(_oldFont) && !Valid(SelectObject(_dc,_oldFont))) errors.Add("restore font");
        if(_dc!=0 && !DeleteDC(_dc)) errors.Add("delete DC"); _dc=0;
        if(_bitmap!=0 && !DeleteObject(_bitmap)) errors.Add("delete bitmap"); _bitmap=0;
        if(_font!=0 && !DeleteObject(_font)) errors.Add("delete font"); _font=0;
        if(errors.Count!=0) throw new InvalidOperationException("Owned native cleanup failed: "+string.Join(",",errors));
    }
    private static bool Valid(nint value)=>value!=0 && value!=-1;
    private static void Check(bool value){if(!value)throw new InvalidOperationException("Original GDI operation failed (last error "+Marshal.GetLastWin32Error()+").");}
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFOHEADER {internal uint Size;internal int Width,Height;internal ushort Planes,BitCount;internal uint Compression,SizeImage;internal int XPelsPerMeter,YPelsPerMeter;internal uint ColorsUsed,ColorsImportant;}
    [StructLayout(LayoutKind.Sequential)] private struct TEXTMETRIC {internal int Height,Ascent,Descent,InternalLeading,ExternalLeading,AveCharWidth,MaxCharWidth,Weight,Overhang,DigitizedAspectX,DigitizedAspectY;internal ushort FirstChar,LastChar,DefaultChar,BreakChar;internal byte Italic,Underlined,StruckOut,PitchAndFamily,CharSet;}
    [StructLayout(LayoutKind.Sequential)] private struct SIZE {internal int X,Y;}
    [StructLayout(LayoutKind.Sequential)] private struct ABC {internal int A;internal uint B;internal int C;}
    [DllImport("gdi32.dll",SetLastError=true)] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll",SetLastError=true)] private static extern nint SelectObject(nint dc,nint obj);
    [DllImport("gdi32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll",SetLastError=true)] private static extern int GetObjectW(nint obj,int size,[Out]byte[] bytes);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern int GetTextFaceW(nint dc,int size,StringBuilder name);
    [DllImport("gdi32.dll",SetLastError=true)] private static extern uint GetFontData(nint dc,uint table,uint offset,[Out]byte[]? bytes,uint length);
    [DllImport("gdi32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetTextMetricsW(nint dc,out TEXTMETRIC metrics);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(nint dc,int index);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern uint GetGlyphIndicesW(nint dc,string text,int count,[Out]ushort[] glyphs,uint flags);
    [DllImport("gdi32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetTextExtentExPointI(nint dc,ushort[] glyphs,int count,int maximum,out int fit,[Out]int[] advances,out SIZE size);
    [DllImport("gdi32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetCharABCWidthsI(nint dc,uint first,uint count,ushort[] glyphs,[Out]ABC[] widths);
    [DllImport("gdi32.dll",SetLastError=true)] private static extern nint CreateDIBSection(nint dc,ref BITMAPINFOHEADER header,uint usage,out nint bits,nint section,uint offset);
    [DllImport("gdi32.dll",SetLastError=true)] private static extern int SetBkMode(nint dc,int mode);
    [DllImport("gdi32.dll",SetLastError=true)] private static extern uint SetTextAlign(nint dc,uint flags);
    [DllImport("gdi32.dll",SetLastError=true)] private static extern uint SetTextColor(nint dc,uint color);
    [DllImport("gdi32.dll",EntryPoint="ExtTextOutW",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool ExtTextOutGlyphs(nint dc,int x,int y,uint options,nint rectangle,ushort[] glyphs,uint count,nint advances);
    [DllImport("gdi32.dll",EntryPoint="ExtTextOutW",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool ExtTextOutText(nint dc,int x,int y,uint options,nint rectangle,string text,uint count,nint advances);
    [DllImport("gdi32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GdiFlush();
}
