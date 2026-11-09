using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

// Test the bundled font through the same loader and GDI path used by the app.
// This catches fonts whose cmap lists a syllable but whose glyph is empty.
public static class FontCoverageTests {
    [StructLayout(LayoutKind.Sequential)] struct Mat2 {public int a,b,c,d;}
    [StructLayout(LayoutKind.Sequential)] struct Metrics {public uint width,height;public int x,y;public short cellX,cellY;}
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)] static extern uint GetGlyphOutlineW(IntPtr dc,uint ch,uint format,out Metrics metrics,uint bytes,IntPtr buffer,ref Mat2 matrix);
    const string FamilyName="Childfund Hangul Compact Test";
    const string FontHash="d49cc1b4c3c4a695841e726c340c9dbf542185077041c682b4440c16071c8675";
    static void Check(bool success,string message){if(!success)throw new Exception(message);}
    [STAThread] public static int Main(string[] args){
        try {
            Check(args.Length==1,"Pass the built PetCalendar.exe path.");
            var app=Assembly.LoadFrom(Path.GetFullPath(args[0]));
            using(var stream=app.GetManifestResourceStream("petcalendar.fonts.zip")){
                Check(stream!=null,"Missing embedded font archive.");
                using(var archive=new ZipArchive(stream,ZipArchiveMode.Read)){
                    Check(archive.GetEntry("Jua-Regular.ttf")!=null,"Jua must remain available.");
                    Check(archive.GetEntry("SDChildfundkorea.ttf")==null,"Old incomplete font must not be bundled.");
                    var entry=archive.GetEntry("SDChildfundkorea-Compact.ttf");
                    Check(entry!=null&&entry.Length==2295844,"Missing compact Hangul font.");
                    using(var input=entry.Open())using(var sha=SHA256.Create())
                        Check(BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant()==FontHash,"Bundled font differs from validated compact font.");
                }
            }
            Type fonts=app.GetType("PetCalendarDesktop.AppFonts",true);
            var family=(FontFamily)fonts.GetMethod("Family").Invoke(null,new object[]{"childfund"});
            Check(family!=null&&family.GetName(1033)==FamilyName,"Existing childfund choice must load the compact font.");
            var jua=(FontFamily)fonts.GetMethod("Family").Invoke(null,new object[]{"jua"});
            Check(jua!=null&&jua.GetName(1033)=="Jua","Jua must still load.");
            app.GetType("PetCalendarDesktop.DesktopTheme",true).GetField("FontKey").SetValue(null,"childfund");
            var uiFont=(Font)app.GetType("PetCalendarDesktop.Ui",true).GetMethod("Font").Invoke(null,new object[]{1f,12f,400,null,"뷁 똠 힣",FontStyle.Regular});
            Check(uiFont.FontFamily.GetName(1033)==FamilyName,"Calendar text must use the selected compact font.");
            int checkedCount=0;
            using(var font=new Font(family,100,FontStyle.Regular,GraphicsUnit.Pixel)){
                IntPtr dc=CreateCompatibleDC(IntPtr.Zero),handle=font.ToHfont(),old=SelectObject(dc,handle);
                var matrix=new Mat2{a=65536,d=65536};
                try {
                    for(uint code=0xAC00;code<=0xD7A3;code++){
                        Metrics metrics;uint result=GetGlyphOutlineW(dc,code,0,out metrics,0,IntPtr.Zero,ref matrix);
                        Check(result!=UInt32.MaxValue&&metrics.width>0&&metrics.height>0,"Empty Hangul glyph U+"+code.ToString("X4"));
                        checkedCount++;
                    }
                }finally{SelectObject(dc,old);DeleteObject(handle);DeleteDC(dc);}
            }
            Console.WriteLine("PASS: embedded compact font hash, existing childfund selection, Jua, calendar text family, and "+checkedCount+" nonempty Hangul glyphs.");
            return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
