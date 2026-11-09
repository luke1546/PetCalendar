using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PetCalendarDesktop {
    // Drawing primitives shared by the native calendar window. Sizes are CSS
    // pixels from desktop/ui/styles.css; every text size carries the same
    // "+1pt + text scale" offset the stylesheet applies.
    // Matches the web app's Intl.Collator('ko'): digits/symbols, then Hangul, then Latin.
// .NET ko-KR puts Latin before Hangul, so the script group is compared first.
public sealed class IntlKoreanComparer : IComparer<string> {
    public static readonly IntlKoreanComparer Instance=new IntlKoreanComparer();
    static readonly StringComparer Culture=StringComparer.Create(Rules.Korean,false);
    static int Group(string s){
        if(String.IsNullOrEmpty(s))return -1;
        char c=s[0];
        if((c>=0xAC00&&c<=0xD7AF)||(c>=0x3130&&c<=0x318F))return 1;
        if(c<0x250&&Char.IsLetter(c))return 2;
        return 0;
    }
    public int Compare(string x,string y){
        x=x??"";y=y??"";
        int gx=Group(x),gy=Group(y);
        if(gx!=gy)return gx.CompareTo(gy);
        return Culture.Compare(x,y);
    }
}
// Optional calendar fonts bundled in fonts.zip, loaded on first use. Each is registered twice:
// with GDI (TextRenderer looks fonts up by name) and with GDI+ (Graphics.DrawString needs the family).
public static class AppFonts {
    sealed class Choice {public string Key,Label,Family;public string[] Files;public float Scale=1;public bool HasBold;}
    static readonly Choice[] choices={
        new Choice{Key="default",Label="기본 (맑은 고딕)"},
        new Choice{Key="jua",Label="주아 · 동글동글",Family="Jua",Files=new[]{"Jua-Regular.ttf"}},
        new Choice{Key="childfund",Label="초록우산어린이 · 아이 손글씨",Family="Childfund Hangul Compact Test",Files=new[]{"SDChildfundkorea-Compact.ttf"},Scale=1.12f}};
    public static IEnumerable<KeyValuePair<string,string>> Options{get{return choices.Select(c=>new KeyValuePair<string,string>(c.Key,c.Label));}}
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)]static extern int AddFontResourceEx(string path,uint flags,IntPtr reserved);
    static readonly PrivateFontCollection collection=new PrivateFontCollection();
    static readonly Dictionary<string,FontFamily> loaded=new Dictionary<string,FontFamily>();
    static Choice Find(string key){return choices.FirstOrDefault(c=>c.Key==key);}
    // Fonts are unpacked once to %LOCALAPPDATA%\PetCalendar\fonts and registered from the file.
    // Windows then maps the file instead of each font engine keeping its own private copy
    // (loading from memory cost 14~45MB per font; from a file it is a few MB).
    static string Unpack(System.IO.Compression.ZipArchive zip,string file){
        string dir=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PetCalendar","fonts");
        System.IO.Directory.CreateDirectory(dir);string path=System.IO.Path.Combine(dir,file);var entry=zip.GetEntry(file);
        if(!System.IO.File.Exists(path)||new System.IO.FileInfo(path).Length!=entry.Length){
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            using(var input=entry.Open())using(var output=System.IO.File.Create(temp))input.CopyTo(output);
            try{if(System.IO.File.Exists(path))System.IO.File.Delete(path);System.IO.File.Move(temp,path);}catch{System.IO.File.Delete(temp);if(!System.IO.File.Exists(path))throw;}
        }
        return path;
    }
    // null for the system default (or if the font could not be loaded).
    public static FontFamily Family(string key){
        var c=Find(key);if(c==null||c.Family==null)return null;FontFamily family;
        if(loaded.TryGetValue(key,out family))return family;
        try{
            using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("petcalendar.fonts.zip"))
            using(var zip=new System.IO.Compression.ZipArchive(stream,System.IO.Compression.ZipArchiveMode.Read)){
                foreach(var file in c.Files){var path=Unpack(zip,file);AddFontResourceEx(path,0x10,IntPtr.Zero);collection.AddFontFile(path);} // 0x10 = FR_PRIVATE
            }
            // Family.Name is localized (names can be Korean on Korean Windows), so match the English name.
            family=collection.Families.FirstOrDefault(f=>f.GetName(1033)==c.Family||f.Name==c.Family);
        }catch{family=null;}
        loaded[key]=family;return family;
    }
    public static float Scale(string key){var c=Find(key);return c==null?1:c.Scale;}
    // Fonts without a bold face stay regular rather than being smeared by synthetic bold,
    // except where the weight carries meaning (titles at 700+) on lighter faces.
    public static bool Bold(string key,int weight){var c=Find(key);if(c==null)return weight>=700;if(c.HasBold)return weight>=700;return false;}
}
public static class Ui {
        static readonly Dictionary<string,Color> colors=new Dictionary<string,Color>();
        static readonly Dictionary<string,Font> fonts=new Dictionary<string,Font>();
        static string monoFamily;
        public static Color C(string hex){
            Color c;if(colors.TryGetValue(hex,out c))return c;
            string h=hex.TrimStart('#');
            if(h.Length==8)c=Color.FromArgb(Convert.ToInt32(h.Substring(6,2),16),Convert.ToInt32(h.Substring(0,2),16),Convert.ToInt32(h.Substring(2,2),16),Convert.ToInt32(h.Substring(4,2),16));
            else c=ColorTranslator.FromHtml("#"+h);
            colors[hex]=c;return c;
        }
        public static Color Mix(Color a,Color b,float amount){return Color.FromArgb((int)(a.R+(b.R-a.R)*amount),(int)(a.G+(b.G-a.G)*amount),(int)(a.B+(b.B-a.B)*amount));}
        public static Color Fade(Color c,Color background,float opacity){return Mix(background,c,opacity);}
        public static float S(Control c){return c.DeviceDpi/96f;}
        public static void Prepare(Graphics g){g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.Half;}
        public static string MonoFamily{get{
            if(monoFamily==null){try{using(var installed=new InstalledFontCollection())monoFamily=installed.Families.Any(f=>f.Name=="Cascadia Code")?"Cascadia Code":"Consolas";}catch{monoFamily="Consolas";}}
            return monoFamily;
        }}
        public static bool HasHangul(string text){if(text==null)return false;foreach(char ch in text)if((ch>=0xac00&&ch<=0xd7af)||(ch>=0x1100&&ch<=0x11ff)||(ch>=0x3130&&ch<=0x318f))return true;return false;}
        // Chrome resolves weight 600/800 against Segoe UI's real faces and
        // falls back to Malgun Gothic Bold for Hangul; GDI links the same way.
        public static Font Font(float scale,float css,int weight=400,string family=null,string text=null,FontStyle extra=FontStyle.Regular){
            float px=(css+(1+DesktopTheme.TextScale)*4f/3f)*scale;
            if(family==null&&DesktopTheme.FontKey!="default"){
                var custom=AppFonts.Family(DesktopTheme.FontKey);
                if(custom!=null){
                    px*=AppFonts.Scale(DesktopTheme.FontKey);var cs=(AppFonts.Bold(DesktopTheme.FontKey,weight)?FontStyle.Bold:FontStyle.Regular)|extra;
                    string ck=custom.Name+"|"+(int)cs+"|"+Math.Round(px*4);Font cf;if(fonts.TryGetValue(ck,out cf))return cf;
                    try{cf=new Font(custom,px,cs,GraphicsUnit.Pixel);}catch{cf=new Font(custom,px,FontStyle.Regular,GraphicsUnit.Pixel);}
                    fonts[ck]=cf;return cf;
                }
            }
            string face;FontStyle style=FontStyle.Regular;
            if(family=="mono"||family=="consolas"){face=family=="mono"?MonoFamily:"Consolas";if(weight>=600)style=FontStyle.Bold;}
            else if(weight>=800){if(text!=null&&!HasHangul(text)){face="Segoe UI Black";}else{face="Segoe UI";style=FontStyle.Bold;}}
            else if(weight>=700){face="Segoe UI";style=FontStyle.Bold;}
            else if(weight>=600){if(text!=null&&!HasHangul(text))face="Segoe UI Semibold";else{face="Segoe UI";style=FontStyle.Bold;}}
            else face="Segoe UI";
            style|=extra;
            string key=face+"|"+(int)style+"|"+Math.Round(px*4);
            Font font;if(fonts.TryGetValue(key,out font))return font;
            try{font=new Font(face,px,style,GraphicsUnit.Pixel);}catch{font=new Font("Segoe UI",px,style,GraphicsUnit.Pixel);}
            fonts[key]=font;return font;
        }
        public const TextFormatFlags Flags=TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding|TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.PreserveGraphicsTranslateTransform;
        public static void Text(Graphics g,string text,Font font,Rectangle rect,Color color,TextFormatFlags flags=TextFormatFlags.Default){
            if(String.IsNullOrEmpty(text)||rect.Width<=0||rect.Height<=0)return;
            TextRenderer.DrawText(g,text,font,rect,color,Flags|TextFormatFlags.SingleLine|flags);
        }
        public static void TextAt(Graphics g,string text,Font font,int x,int y,int height,Color color){Text(g,text,font,new Rectangle(x,y,Width(text,font)+4,height),color,TextFormatFlags.VerticalCenter);}
        static readonly Dictionary<string,int> widths=new Dictionary<string,int>();
        public static int Width(string text,Font font){
            if(String.IsNullOrEmpty(text))return 0;
            string key=font.Name+"|"+font.Size+"|"+(int)font.Style+"|"+text;int w;if(text.Length<64&&widths.TryGetValue(key,out w))return w;
            // The overload without a device context ignores NoPadding (~1/3 em too wide),
            // which pushed centred labels left; measure on a DC like DrawText does.
            w=TextRenderer.MeasureText(MeasureGraphics,text,font,new Size(int.MaxValue,int.MaxValue),Flags|TextFormatFlags.SingleLine).Width;
            if(text.Length<64){if(widths.Count>4000)widths.Clear();widths[key]=w;}
            return w;
        }
        static Graphics advanceGraphics,measureGraphics;static readonly Dictionary<string,float> advances=new Dictionary<string,float>();
        static Graphics MeasureGraphics{get{if(measureGraphics==null)measureGraphics=Graphics.FromImage(new Bitmap(1,1));return measureGraphics;}}
        // Typographic advance (no GDI overhang), used for tight layouts such as date/time segments.
        public static float Advance(string text,Font font){
            if(String.IsNullOrEmpty(text))return 0;
            if(advanceGraphics==null){advanceGraphics=Graphics.FromImage(new Bitmap(1,1));}
            string key=font.Name+"|"+font.Size+"|"+(int)font.Style+"|"+text;float w;if(text.Length<80&&advances.TryGetValue(key,out w))return w;
            w=advanceGraphics.MeasureString(text,font,PointF.Empty,StringFormat.GenericTypographic).Width;
            if(text.Length<80){if(advances.Count>6000)advances.Clear();advances[key]=w;}
            return w;
        }
        public static int LineHeight(Font font){return (int)Math.Ceiling(font.GetHeight());}
        public static float Ascent(Font font){var f=font.FontFamily;return font.Size*f.GetCellAscent(font.Style)/f.GetEmHeight(font.Style);}
        // Letter-spacing has no GDI equivalent, so spaced runs are set per glyph.
        public static float SpacedWidth(string text,Font font,float spacing){if(String.IsNullOrEmpty(text))return 0;return Width(text,font)+spacing*(text.Length-1);}
        static float Glyph(char ch,Font font){if(ch==' ')return Width("a a",font)-Width("aa",font);return Width(ch.ToString(),font);}
        public static void Spaced(Graphics g,string text,Font font,float x,int y,int height,Color color,float spacing){
            if(String.IsNullOrEmpty(text))return;
            // Advance by whole-prefix widths: single-glyph GDI measurements overstate advances.
            // The "|" sentinel does not kern (a trailing "a" kerned under T/V/W/Y).
            for(int i=0;i<text.Length;i++){char ch=text[i];if(ch==' ')continue;float left=x+(i==0?0:Width(text.Substring(0,i)+"|",font)-Width("|",font))+spacing*i;Text(g,ch.ToString(),font,new Rectangle((int)Math.Round(left),y,Width(ch.ToString(),font)+6,height),color,TextFormatFlags.VerticalCenter);}
        }
        // Line breaking follows Chrome's defaults for Korean: any Hangul syllable
        // boundary or space may break; long Latin words break anywhere.
        // Layout and paint both wrap the same strings several times per frame; each wrap
        // measures every break candidate, so results are memoised per text/font/width.
        static readonly Dictionary<string,List<string>> wraps=new Dictionary<string,List<string>>();
        public static List<string> Wrap(string text,Font font,int width){
            if(text==null)text="";width=Math.Max(1,width);
            string key=font.Name+"|"+font.Size+"|"+(int)font.Style+"|"+width+"|"+text;List<string> cached;
            if(wraps.TryGetValue(key,out cached))return new List<string>(cached);
            var result=WrapUncached(text,font,width);if(wraps.Count>3000)wraps.Clear();wraps[key]=result;return new List<string>(result);
        }
        static List<string> WrapUncached(string text,Font font,int width){
            var lines=new List<string>();
            foreach(string paragraph in text.Replace("\r","").Split('\n')){
                // A paragraph that fits whole is one line (the unit loop would accept every unit).
                if(paragraph.Length<300){string whole=paragraph.TrimEnd();if(Advance(whole,font)<=width){lines.Add(whole);continue;}}
                var units=new List<string>();var word=new StringBuilder();
                foreach(char ch in paragraph){
                    bool hangul=(ch>=0xac00&&ch<=0xd7af)||(ch>=0x3130&&ch<=0x318f);
                    if(ch==' '){if(word.Length>0){units.Add(word.ToString());word.Clear();}units.Add(" ");}
                    else if(hangul){if(word.Length>0&&!IsOpening(word[word.Length-1])){units.Add(word.ToString());word.Clear();}word.Append(ch);units.Add(word.ToString());word.Clear();}
                    else if(word.Length==0&&units.Count>0&&units[units.Count-1]!=" "&&IsClosing(ch)){units[units.Count-1]+=ch;}
                    else word.Append(ch);
                }
                if(word.Length>0)units.Add(word.ToString());
                var line=new StringBuilder();
                foreach(string unit in units){
                    string candidate=line.ToString()+unit;
                    if(line.Length==0||Advance(candidate.TrimEnd(),font)<=width){line.Append(unit);continue;}
                    if(unit==" "){lines.Add(line.ToString().TrimEnd());line.Clear();continue;}
                    lines.Add(line.ToString().TrimEnd());line.Clear();
                    if(Advance(unit,font)>width){foreach(char ch in unit){if(line.Length>0&&Advance(line.ToString()+ch,font)>width){lines.Add(line.ToString());line.Clear();}line.Append(ch);}}
                    else line.Append(unit);
                }
                lines.Add(line.ToString().TrimEnd());
            }
            return lines;
        }
        static bool IsClosing(char ch){return ".,!?)]}:;·’”。、".IndexOf(ch)>=0;}
        static bool IsOpening(char ch){return "([{‘“".IndexOf(ch)>=0;}
        public static int Paragraph(Graphics g,string text,Font font,int x,int y,int width,float lineHeight,Color color,bool measureOnly=false,TextFormatFlags align=TextFormatFlags.Default){
            var lines=Wrap(text,font,width);int lh=(int)Math.Round(lineHeight);
            if(!measureOnly)for(int i=0;i<lines.Count;i++)Text(g,lines[i],font,new Rectangle(x,y+i*lh,width,lh),color,TextFormatFlags.VerticalCenter|align);
            return lines.Count*lh;
        }
        public static GraphicsPath Round(RectangleF r,float radius){return DesktopTheme.Round(r,radius);}
        public static GraphicsPath Corners(RectangleF r,float tl,float tr,float br,float bl){
            var p=new GraphicsPath();float max=Math.Min(r.Width,r.Height)/2;
            tl=Math.Min(tl,max);tr=Math.Min(tr,max);br=Math.Min(br,max);bl=Math.Min(bl,max);
            if(tl>0)p.AddArc(r.X,r.Y,tl*2,tl*2,180,90);else p.AddLine(r.X,r.Y,r.X,r.Y);
            if(tr>0)p.AddArc(r.Right-tr*2,r.Y,tr*2,tr*2,270,90);else p.AddLine(r.Right,r.Y,r.Right,r.Y);
            if(br>0)p.AddArc(r.Right-br*2,r.Bottom-br*2,br*2,br*2,0,90);else p.AddLine(r.Right,r.Bottom,r.Right,r.Bottom);
            if(bl>0)p.AddArc(r.X,r.Bottom-bl*2,bl*2,bl*2,90,90);else p.AddLine(r.X,r.Bottom,r.X,r.Bottom);
            p.CloseFigure();return p;
        }
        public static void Fill(Graphics g,RectangleF r,float radius,Color color){if(r.Width<=0||r.Height<=0)return;using(var b=new SolidBrush(color)){if(radius<=0)g.FillRectangle(b,r);else using(var p=Round(r,radius))g.FillPath(b,p);}}
        public static void Stroke(Graphics g,RectangleF r,float radius,Color color,float width){if(r.Width<=0||r.Height<=0)return;var inner=r;inner.Inflate(-width/2,-width/2);using(var pen=new Pen(color,width)){if(radius<=0)g.DrawRectangle(pen,inner.X,inner.Y,inner.Width,inner.Height);else using(var p=Round(inner,Math.Max(0.5f,radius-width/2)))g.DrawPath(pen,p);}}
        // Box with border (CSS border-box semantics) and fill.
        public static void Box(Graphics g,RectangleF r,float radius,Color fill,Color border,float borderWidth){Fill(g,r,radius,border);var inner=r;inner.Inflate(-borderWidth,-borderWidth);Fill(g,inner,Math.Max(0,radius-borderWidth),fill);}
        // Card with a thick colored left edge, as in `border-left:4px solid var(--category)`.
        public static void LeftEdgeCard(Graphics g,RectangleF r,float radius,Color fill,Color border,float edge,Color edgeColor,float s){
            using(var outer=Round(r,radius)){
                using(var b=new SolidBrush(border))g.FillPath(b,outer);
                var clip=g.Clip;g.SetClip(outer,CombineMode.Intersect);
                using(var b=new SolidBrush(edgeColor))g.FillRectangle(b,r.X,r.Y,edge,r.Height);
                g.Clip=clip;
            }
            var inner=new RectangleF(r.X+edge,r.Y+s,r.Width-edge-s,r.Height-2*s);
            using(var p=Corners(inner,Math.Max(0,radius-edge),radius-s,radius-s,Math.Max(0,radius-edge)))using(var b=new SolidBrush(fill))g.FillPath(b,p);
        }
        // Inline SVG icons from desktop/ui/app.mjs, drawn from the same paths.
        public static readonly Dictionary<string,string> IconPaths=new Dictionary<string,string>{
            {"repeat","M20 7h-5m5 0V2M4 17h5m-5 0v5M20 7a8 8 0 0 0-14-3M4 17a8 8 0 0 0 14 3"},
            {"calendar","M8 2v4m8-4v4M3 10h18M5 4h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2Zm3 10h2m4 0h2m-8 4h2"},
            {"check","m5 12 4 4L19 6M20 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h9"},
            {"flag","M5 22V3m0 1c5-4 9 4 14 0v11c-5 4-9-4-14 0"},
            {"left","m14 5-7 7 7 7"},{"right","m9 5 7 7-7 7"},{"plus","M12 5v14M5 12h14"},{"close","m6 6 12 12M18 6 6 18"},
            {"minimize","M5 12h14"},{"maximize","M5 5h14v14H5Z"},{"restore","M8 8h11v11H8ZM5 16V5h11"},
            {"search","m21 21-5-5M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Z"},
            {"bell","M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9ZM10 21h4"},
            {"settings","m9 3 .5-1h5l.5 3 2 1.2 2.8-1 2.5 4.3-2.3 2v2.4l2.3 2-2.5 4.3-2.8-1-2 1.2-.5 3h-5l-.5-3L7 19.2l-2.8 1-2.5-4.3 2.3-2v-2.4l-2.3-2 2.5-4.3 2.8 1L9 5ZM15.5 12a3.5 3.5 0 1 1-7 0 3.5 3.5 0 0 1 7 0Z"},
            {"download","M12 3v12m-5-5 5 5 5-5M4 15v5a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-5"},
            {"upload","M12 16V4m-5 5 5-5 5 5M4 15v5a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-5"},
            {"lock","M6 10V7a6 6 0 0 1 12 0v3M5 10h14v12H5V10Zm7 5v3"},
            {"trash","M3 6h18M9 6V3h6v3M5 6l1 15h12l1-15M10 10v7m4-7v7"},
            {"checkmark","m3.7 8 2.8 2.8 5.8-5.6"},
            {"chevron","m6 9 6 6 6-6"},
            {"clock","M12 7v5l3 2M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z"}
        };
        static readonly Dictionary<string,GraphicsPath> parsed=new Dictionary<string,GraphicsPath>();
        public static GraphicsPath PathFor(string data){GraphicsPath p;if(!parsed.TryGetValue(data,out p)){p=SvgPath.Parse(data);parsed[data]=p;}return p;}
        public static void Icon(Graphics g,string name,RectangleF box,Color color,float stroke=1.8f,float viewBox=24){
            string data;if(!IconPaths.TryGetValue(name,out data))data=IconPaths["calendar"];
            StrokePath(g,data,box,color,stroke,viewBox);
        }
        public static void StrokePath(Graphics g,string data,RectangleF box,Color color,float stroke,float viewBox){
            var mode=g.SmoothingMode;g.SmoothingMode=SmoothingMode.AntiAlias;
            float k=box.Width/viewBox;
            using(var path=(GraphicsPath)PathFor(data).Clone())using(var m=new Matrix()){
                m.Translate(box.X,box.Y);m.Scale(k,k);path.Transform(m);
                using(var pen=new Pen(color,Math.Max(0.6f,stroke*k))){pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;g.DrawPath(pen,path);}
            }
            g.SmoothingMode=mode;
        }
        // input[type=checkbox] from the "Soft peach checkboxes" rules.
        public static void CheckBox(Graphics g,RectangleF box,bool on,bool hover,bool enabled,float radius,float s,Color background){
            var mode=g.SmoothingMode;g.SmoothingMode=SmoothingMode.AntiAlias;
            Color fill=C(on?(hover&&enabled?"#ffd6b9":"#ffe1ca"):(hover&&enabled?"#fff0df":"#fffaf1"));
            Color border=C(on?(hover&&enabled?"#d98d64":"#e7a27e"):(hover&&enabled?"#df9871":"#cbb99d"));
            if(!enabled){fill=Fade(fill,background,.5f);border=Fade(border,background,.5f);}
            Box(g,box,radius,fill,border,1.2f*s);
            if(on)StrokePath(g,IconPaths["checkmark"],box,enabled?C("#a95736"):Fade(C("#a95736"),background,.5f),1.9f,16);
            g.SmoothingMode=mode;
        }
        // Brand mark from desktop/ui/icon.svg.
        public static void Brand(Graphics g,RectangleF box,float rotate){
            var state=g.Save();Ui.Prepare(g);
            g.TranslateTransform(box.X+box.Width/2,box.Y+box.Height/2);g.RotateTransform(rotate);float k=box.Width/64f;g.ScaleTransform(k,k);g.TranslateTransform(-32,-32);
            using(var p=Round(new RectangleF(4,8,54,52),12))using(var b=new SolidBrush(C("#26201a")))g.FillPath(b,p);
            using(var p=Round(new RectangleF(2,4,54,52),12)){using(var b=new SolidBrush(C("#ff7447")))g.FillPath(b,p);using(var pen=new Pen(C("#26201a"),3))g.DrawPath(pen,p);}
            using(var pen=new Pen(C("#26201a"),4)){pen.StartCap=pen.EndCap=LineCap.Round;g.DrawLine(pen,3,22,55,22);g.DrawLine(pen,17,2,17,13);g.DrawLine(pen,40,2,40,13);}
            using(var pen=new Pen(C("#fffdf7"),5)){pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;g.DrawLines(pen,new[]{new PointF(17,37),new PointF(25,45),new PointF(40,29)});}
            g.Restore(state);
        }
        // .brand-paw: four toes and a pad, rotated -12deg.
        public static void Paw(Graphics g,RectangleF box){
            var state=g.Save();Ui.Prepare(g);
            g.TranslateTransform(box.X+box.Width/2,box.Y+box.Height/2);g.RotateTransform(-12);float k=box.Width/24f;g.ScaleTransform(k,k);g.TranslateTransform(-12,-12);
            using(var b=new SolidBrush(C("#8b5a3c"))){
                float[,] toes={{5,8,-25},{10,4.5f,-8},{15,4.5f,8},{20,8,25}};
                for(int i=0;i<4;i++){var t=g.Save();g.TranslateTransform(toes[i,0],toes[i,1]);g.RotateTransform(toes[i,2]);g.FillEllipse(b,-2.2f,-3,4.4f,6);g.Restore(t);}
                using(var pad=(GraphicsPath)PathFor("M12.5 10c-3.1 0-3.8 2.3-6 4.2-2.1 1.8-1.5 5.3 1.2 5.8 1.7.3 3-.8 4.8-.8s3.1 1.1 4.8.8c2.7-.5 3.3-4 1.2-5.8-2.2-1.9-2.9-4.2-6-4.2Z").Clone())g.FillPath(b,pad);
            }
            g.Restore(state);
        }
        // Classic overlay scrollbars from the "rounded paper theme" rules.
        public static void ScrollBar(Graphics g,ScrollModel model,float s){
            if(!model.Visible)return;
            var track=model.Track(s);var mode=g.SmoothingMode;g.SmoothingMode=SmoothingMode.AntiAlias;
            var inner=new RectangleF(track.X,track.Y+6*s,track.Width,Math.Max(1,track.Height-12*s));
            Fill(g,inner,track.Width/2,C("#faf1df"));
            var thumb=model.Thumb(s);Fill(g,thumb,thumb.Width/2,C(model.Dragging?"#c18b60":model.Hover?"#d7a275":"#cfb99a"));
            g.SmoothingMode=mode;
        }
    }
    public sealed class ScrollModel {
        public int Offset,Content,Viewport;public Rectangle Bounds;public bool Hover,Dragging;public int DragStart,DragOffset;
        public int Max{get{return Math.Max(0,Content-Viewport);}}
        public bool Visible{get{return Content>Viewport+1;}}
        public void Clamp(){Offset=Math.Max(0,Math.Min(Max,Offset));}
        public Rectangle Track(float s){int w=(int)Math.Round(12*s);return new Rectangle(Bounds.Right-w,Bounds.Y,w,Bounds.Height);}
        public RectangleF Thumb(float s){
            var t=Track(s);float top=t.Y+6*s,height=Math.Max(1,t.Height-12*s);
            float h=Math.Min(height,Math.Max(32*s,height*Viewport/Math.Max(1f,Content)));
            float y=top+(Max==0?0:(height-h)*Offset/Max);
            return new RectangleF(t.X+3*s,y,t.Width-6*s,h);
        }
        public bool Wheel(int delta,float s){if(!Visible)return false;int before=Offset;Offset-=(int)(delta/120f*100*s);Clamp();return before!=Offset;}
        public void DragTo(int y,float s){var t=Track(s);float travel=Math.Max(1,t.Height-12*s-Thumb(s).Height);Offset=DragOffset+(int)((y-DragStart)*Max/travel);Clamp();}
    }
    // Minimal SVG path parser for the icon set: M L H V C S Q A Z (abs/rel).
    public static class SvgPath {
        public static GraphicsPath Parse(string d){
            var path=new GraphicsPath(FillMode.Winding);int i=0;char cmd=' ';
            PointF cur=PointF.Empty,start=PointF.Empty,ctrl=PointF.Empty;char last=' ';
            Func<float> num=()=>{
                while(i<d.Length&&(d[i]==' '||d[i]==','))i++;
                int s=i;if(i<d.Length&&(d[i]=='-'||d[i]=='+'))i++;
                bool dot=false;while(i<d.Length&&(char.IsDigit(d[i])||(d[i]=='.'&&!dot))){if(d[i]=='.')dot=true;i++;}
                if(i<d.Length&&(d[i]=='e'||d[i]=='E')){i++;if(i<d.Length&&(d[i]=='-'||d[i]=='+'))i++;while(i<d.Length&&char.IsDigit(d[i]))i++;}
                return float.Parse(d.Substring(s,i-s),CultureInfo.InvariantCulture);
            };
            Func<float> flag=()=>{while(i<d.Length&&(d[i]==' '||d[i]==','))i++;float v=d[i]=='1'?1:0;i++;return v;};
            Func<bool> more=()=>{while(i<d.Length&&(d[i]==' '||d[i]==','))i++;return i<d.Length&&(char.IsDigit(d[i])||d[i]=='-'||d[i]=='+'||d[i]=='.');};
            while(i<d.Length){
                while(i<d.Length&&(d[i]==' '||d[i]==','))i++;if(i>=d.Length)break;
                if(char.IsLetter(d[i])){cmd=d[i];i++;}
                bool rel=char.IsLower(cmd);char c=char.ToUpperInvariant(cmd);
                if(c=='Z'){path.CloseFigure();cur=start;last='Z';continue;}
                do{
                    float ox=rel?cur.X:0,oy=rel?cur.Y:0;
                    if(c=='M'){var p=new PointF(num()+ox,num()+oy);path.StartFigure();cur=start=p;cmd=rel?'l':'L';c='L';last='M';continue;}
                    if(c=='L'){var p=new PointF(num()+ox,num()+oy);path.AddLine(cur,p);cur=p;}
                    else if(c=='H'){var p=new PointF(num()+ox,cur.Y);path.AddLine(cur,p);cur=p;}
                    else if(c=='V'){var p=new PointF(cur.X,num()+oy);path.AddLine(cur,p);cur=p;}
                    else if(c=='C'){var a=new PointF(num()+ox,num()+oy);var b=new PointF(num()+ox,num()+oy);var p=new PointF(num()+ox,num()+oy);path.AddBezier(cur,a,b,p);ctrl=b;cur=p;}
                    else if(c=='S'){var a=(last=='C'||last=='S')?new PointF(2*cur.X-ctrl.X,2*cur.Y-ctrl.Y):cur;var b=new PointF(num()+ox,num()+oy);var p=new PointF(num()+ox,num()+oy);path.AddBezier(cur,a,b,p);ctrl=b;cur=p;}
                    else if(c=='Q'){var q=new PointF(num()+ox,num()+oy);var p=new PointF(num()+ox,num()+oy);path.AddBezier(cur,new PointF(cur.X+2f/3*(q.X-cur.X),cur.Y+2f/3*(q.Y-cur.Y)),new PointF(p.X+2f/3*(q.X-p.X),p.Y+2f/3*(q.Y-p.Y)),p);cur=p;}
                    else if(c=='A'){float rx=num(),ry=num(),rot=num(),large=flag(),sweep=flag();var p=new PointF(num()+ox,num()+oy);Arc(path,cur,p,rx,ry,rot,large!=0,sweep!=0);cur=p;}
                    else {i++;break;}
                    last=c;
                }while(more());
            }
            return path;
        }
        static void Arc(GraphicsPath path,PointF p0,PointF p1,float rx,float ry,float angle,bool large,bool sweep){
            if(rx==0||ry==0){path.AddLine(p0,p1);return;}
            double phi=angle*Math.PI/180,cos=Math.Cos(phi),sin=Math.Sin(phi);
            double dx=(p0.X-p1.X)/2.0,dy=(p0.Y-p1.Y)/2.0,x1=cos*dx+sin*dy,y1=-sin*dx+cos*dy;
            double rxs=Math.Abs(rx),rys=Math.Abs(ry),lambda=x1*x1/(rxs*rxs)+y1*y1/(rys*rys);
            if(lambda>1){rxs*=Math.Sqrt(lambda);rys*=Math.Sqrt(lambda);}
            double num=rxs*rxs*rys*rys-rxs*rxs*y1*y1-rys*rys*x1*x1,den=rxs*rxs*y1*y1+rys*rys*x1*x1;
            double coef=(large!=sweep?1:-1)*Math.Sqrt(Math.Max(0,num/den));
            double cx1=coef*rxs*y1/rys,cy1=-coef*rys*x1/rxs;
            double cx=cos*cx1-sin*cy1+(p0.X+p1.X)/2.0,cy=sin*cx1+cos*cy1+(p0.Y+p1.Y)/2.0;
            Func<double,double,double,double,double> ang=(ux,uy,vx,vy)=>{double a=Math.Atan2(ux*vy-uy*vx,ux*vx+uy*vy);return a;};
            double t1=ang(1,0,(x1-cx1)/rxs,(y1-cy1)/rys),dt=ang((x1-cx1)/rxs,(y1-cy1)/rys,(-x1-cx1)/rxs,(-y1-cy1)/rys);
            if(!sweep&&dt>0)dt-=2*Math.PI;else if(sweep&&dt<0)dt+=2*Math.PI;
            int segments=(int)Math.Ceiling(Math.Abs(dt)/(Math.PI/2));double step=dt/segments;
            PointF prev=p0;
            for(int k=0;k<segments;k++){
                double a1=t1+k*step,a2=a1+step,alpha=4.0/3*Math.Tan(step/4);
                Func<double,PointF> pt=a=>new PointF((float)(cx+rxs*Math.Cos(a)*cos-rys*Math.Sin(a)*sin),(float)(cy+rxs*Math.Cos(a)*sin+rys*Math.Sin(a)*cos));
                Func<double,PointF> dv=a=>new PointF((float)(-rxs*Math.Sin(a)*cos-rys*Math.Cos(a)*sin),(float)(-rxs*Math.Sin(a)*sin+rys*Math.Cos(a)*cos));
                var e=pt(a2);var d1=dv(a1);var d2=dv(a2);
                path.AddBezier(prev,new PointF(prev.X+(float)alpha*d1.X,prev.Y+(float)alpha*d1.Y),new PointF(e.X-(float)alpha*d2.X,e.Y-(float)alpha*d2.Y),k==segments-1?p1:e);
                prev=k==segments-1?p1:e;
            }
        }
    }
    public enum BtnKind {Normal,Primary,Danger,Small,Icon,SectionIcon,Close,Window,WindowClose,Plain,Round}
    // .button and its variants. The control reserves a few pixels around the
    // CSS box so the primary shadow and hover lift stay inside the window.
    public class Btn : Button {
        public BtnKind Kind;public string IconName;public float IconSize=20,IconStroke=1.8f;public string Kbd;
        public float FontCss=13;public int Weight=700;public float PadX=14,PadY=10,MinH=38,MinW=0,Gap=7;public Color? Ink;public Color? HoverInk;public Color? Surface;
        public bool Active;
        bool hovered,pressed;
        public Btn(BtnKind kind,string text,string icon=null){
            Kind=kind;Text=text??"";IconName=icon;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;UseMnemonic=false;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.SupportsTransparentBackColor,true);SetStyle(ControlStyles.Opaque,false);
            Cursor=Cursors.Hand;BackColor=Color.Transparent;
            if(kind==BtnKind.Small){FontCss=12;PadX=12;PadY=7;MinH=36;}
            if(kind==BtnKind.Icon){PadX=PadY=8;MinH=MinW=36;}
            if(kind==BtnKind.SectionIcon){PadX=PadY=6;MinH=MinW=30;IconSize=15;}
            if(kind==BtnKind.Close){PadX=PadY=6;MinH=MinW=30;IconSize=17;}
            if(kind==BtnKind.Window||kind==BtnKind.WindowClose){IconSize=16;IconStroke=1.7f;}
            if(kind==BtnKind.Plain){PadX=PadY=0;MinH=0;Weight=400;}
        }
        float Sc{get{return DeviceDpi/96f;}}
        int Lead{get{return Kind==BtnKind.Primary?1:0;}}
        int Trail{get{return Kind==BtnKind.Primary?3:1;}}
        public Size CssSize(){
            float s=Sc;var font=CurrentFont();
            if(Kind==BtnKind.Window||Kind==BtnKind.WindowClose)return new Size((int)Math.Round(30*s),(int)Math.Round(32*s));
            float w=PadX*2,content=0;int parts=0;
            if(IconName!=null){content+=IconSize;parts++;}
            if(Text.Length>0){content+=Ui.Width(Text,font);parts++;}
            if(Kbd!=null){content+=KbdSize(s).Width/s;parts++;}
            w+=content+Math.Max(0,parts-1)*Gap;
            // Chrome snaps a 1.5px border to whole device pixels (1px at 100%).
            float border=Kind==BtnKind.Plain||Kind==BtnKind.Close?0:Math.Max(1,(float)Math.Floor(1.5f*s))/s;
            float h=Math.Max(MinH,PadY*2+Math.Max(IconName!=null?IconSize:0,Text.Length>0?(FontCss+(1+DesktopTheme.TextScale)*4f/3f)*1.3f:0)+border*2);
            w=Math.Max(Math.Max(MinW,w+border*2),Kind==BtnKind.Icon||Kind==BtnKind.SectionIcon||Kind==BtnKind.Close?h:0);
            return new Size((int)Math.Ceiling(w*s),(int)Math.Ceiling(h*s));
        }
        public Size Outer(){var c=CssSize();int pad=(int)Math.Ceiling((Lead+Trail)*Sc);return new Size(c.Width+pad,c.Height+pad);}
        public void PlaceAt(int x,int y){var o=Outer();int lead=(int)Math.Ceiling(Lead*Sc);SetBounds(x-lead,y-lead,o.Width,o.Height);}
        public void PlaceAt(int x,int y,int width){var o=Outer();int lead=(int)Math.Ceiling(Lead*Sc),pad=(int)Math.Ceiling((Lead+Trail)*Sc);SetBounds(x-lead,y-lead,width+pad,o.Height);}
        public override Size GetPreferredSize(Size proposed){return Outer();}
        Font CurrentFont(){return Ui.Font(Sc,FontCss,Weight,null,Text);}
        Size KbdSize(float s){var f=Ui.Font(s,10,400,"mono");int w=Math.Max((int)(19*s),Ui.Width(Kbd,f)+(int)(10*s));return new Size(w,(int)Math.Max(19*s,f.GetHeight()+4*s));}
        Color ParentBack{get{
            if(Surface.HasValue)return Surface.Value;
            Control p=Parent;while(p!=null&&p.BackColor.A<255)p=p.Parent;return p==null?DesktopTheme.Card:p.BackColor;
        }}
        protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(ParentBack);}
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;Ui.Prepare(g);float s=Sc;var back=ParentBack;
            float lead=(float)Math.Ceiling(Lead*s),trail=(float)Math.Ceiling(Trail*s);
            var box=new RectangleF(lead,lead,Width-lead-trail,Height-lead-trail);
            bool enabled=Enabled;float alpha=enabled?1:.55f;
            Func<Color,Color> f=c=>enabled?c:Ui.Fade(c,back,alpha);
            Color ink=Ink??DesktopTheme.Ink;
            if(Kind==BtnKind.Primary){
                float lift=hovered&&enabled&&!pressed?-1*s:pressed?2*s:0,shadow=pressed?0:(hovered&&enabled?3*s:2*s);
                var r=box;r.Offset(lift,lift);
                if(shadow>0){var sh=r;sh.Offset(shadow,shadow);Ui.Fill(g,sh,9*s,f(Ui.C("#725849")));}
                Ui.Box(g,r,9*s,f(hovered&&enabled?Ui.C("#ff865f"):DesktopTheme.Orange),f(Ui.C("#725849")),1.5f*s);box=r;
            }else if(Kind==BtnKind.Window||Kind==BtnKind.WindowClose){
                if(pressed||hovered)Ui.Fill(g,box,6*s,Kind==BtnKind.WindowClose&&!pressed?Ui.C("#ffd5ca"):Ui.C(pressed?"#e8d7b7":"#f1e2c8"));
                if(Kind==BtnKind.WindowClose&&hovered)ink=Ui.C("#a44130");
            }else if(Kind==BtnKind.Close){
                Ui.Fill(g,box,box.Height/2,f(Ui.C("#f1e9da")));
            }else if(Kind==BtnKind.Plain){
                if(hovered&&enabled)ink=HoverInk??Ui.C("#bf4722");
            }else{
                if(pressed)box.Offset(s,s);
                Color fill=Surface.HasValue&&Kind!=BtnKind.Normal&&Kind!=BtnKind.Small&&Kind!=BtnKind.Icon?Surface.Value:DesktopTheme.Card,border=Ui.C("#9d8d73");
                if(Kind==BtnKind.SectionIcon){fill=Ui.C("#fff2d6");border=Ui.C("#ceba96");}
                if(Kind==BtnKind.Danger){fill=Ui.C("#fff4ef");border=Ui.C("#bd6656");ink=Ui.C("#b64238");}
                if(hovered&&enabled)fill=Ui.C("#f9ecd6");
                if(Active)fill=Ui.C("#f2ead9");
                Ui.Box(g,box,9*s,f(fill),f(border),1.5f*s);
            }
            ink=f(ink);
            var font=CurrentFont();float content=0;int parts=0;
            if(IconName!=null){content+=IconSize*s;parts++;}
            int textW=Text.Length>0?Ui.Width(Text,font):0;if(textW>0){content+=textW;parts++;}
            Size kbd=Kbd!=null?KbdSize(s):Size.Empty;if(Kbd!=null){content+=kbd.Width;parts++;}
            content+=Math.Max(0,parts-1)*Gap*s;
            float x=Kind==BtnKind.Plain?box.X:box.X+(box.Width-content)/2,cy=box.Y+box.Height/2;
            if(IconName!=null){Ui.Icon(g,IconName,new RectangleF(x,cy-IconSize*s/2,IconSize*s,IconSize*s),ink,IconStroke);x+=IconSize*s+Gap*s;}
            if(textW>0){Ui.Text(g,Text,font,new Rectangle((int)Math.Round(x),(int)box.Y,textW+2,(int)box.Height),ink,TextFormatFlags.VerticalCenter);x+=textW+Gap*s;}
            if(Kbd!=null){var kr=new RectangleF(x,cy-kbd.Height/2f,kbd.Width,kbd.Height);bool primary=Kind==BtnKind.Primary;Ui.Box(g,kr,4*s,Ui.Mix(primary?(hovered?Ui.C("#ff865f"):DesktopTheme.Orange):DesktopTheme.Card,Color.White,0.31f),f(primary?Ui.Mix(DesktopTheme.Orange,Ui.C("#8e442c"),.36f):Ui.C("#d6c9b0")),s);Ui.Text(g,Kbd,Ui.Font(s,10,400,"mono"),Rectangle.Round(kr),f(Ui.C(primary?"#51311f":"#6e604d")),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
            if(Focused&&ShowFocusCues){var ring=box;ring.Inflate(1.5f*s,1.5f*s);if(ring.X<0)ring.Inflate(-2*s,-2*s);using(var pen=new Pen(DesktopTheme.Orange,2*s))using(var p=Ui.Round(ring,10*s))g.DrawPath(pen,p);}
        }
        protected override void OnMouseEnter(EventArgs e){hovered=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hovered=false;pressed=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left)pressed=true;Invalidate();base.OnMouseDown(e);}
        protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
        protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Space)pressed=true;Invalidate();base.OnKeyDown(e);}
        protected override void OnKeyUp(KeyEventArgs e){pressed=false;Invalidate();base.OnKeyUp(e);}
        protected override void OnEnabledChanged(EventArgs e){Cursor=Enabled?Cursors.Hand:Cursors.WaitCursor;Invalidate();base.OnEnabledChanged(e);}
        protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
        protected override void OnLostFocus(EventArgs e){pressed=false;Invalidate();base.OnLostFocus(e);}
    }
    // Text box that paints its placeholder the way Chrome does (#757575),
    // also while focused and for multi-line inputs.
    public class CueTextBox : TextBox {
        string placeholder="";
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
        public string Placeholder{get{return placeholder;}set{placeholder=value??"";Invalidate();}}
        public CueTextBox(){BorderStyle=BorderStyle.None;}
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);try{SendMessage(Handle,0xd3,new IntPtr(3),IntPtr.Zero);}catch{}}
        protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Invalidate();}
        protected override void WndProc(ref Message m){
            base.WndProc(ref m);
            if(m.Msg==0x000f&&TextLength==0&&placeholder.Length>0){
                try{using(var g=Graphics.FromHwnd(Handle)){var flags=TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding|(Multiline?TextFormatFlags.WordBreak:TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter);TextRenderer.DrawText(g,placeholder,Font,new Rectangle(1,Multiline?0:0,ClientSize.Width-1,Multiline?ClientSize.Height:ClientSize.Height),Ui.C("#757575"),BackColor,flags);}}catch{}
            }
        }
    }
    // .field input / textarea: rounded box with an orange focus ring.
    public class InputBox : Control {
        public readonly CueTextBox Box=new CueTextBox();
        public float FontCss=13,PadX=11,PadY=10;public int Weight=400;public bool Multiline;public float MinHeightCss;
        public event EventHandler ValueChanged;
        public InputBox(bool multiline=false,float fontCss=13,int weight=400){
            Multiline=multiline;FontCss=fontCss;Weight=weight;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            Box.Multiline=multiline;Box.BackColor=Ui.C("#fffaf1");Box.ForeColor=DesktopTheme.Ink;if(multiline){Box.ScrollBars=ScrollBars.None;Box.AcceptsReturn=true;Box.WordWrap=true;}
            Box.GotFocus+=delegate{Invalidate();};Box.LostFocus+=delegate{Invalidate();};Box.TextChanged+=delegate{if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);};
            Controls.Add(Box);Cursor=Cursors.IBeam;
        }
        public override string Text{get{return Box.Text;}set{Box.Text=value??"";}}
        public string Placeholder{get{return Box.Placeholder;}set{Box.Placeholder=value;}}
        public int MaxLength{get{return Box.MaxLength;}set{Box.MaxLength=value;}}
        float Sc{get{return DeviceDpi/96f;}}
        public int PreferredHeight(){float s=Sc;var font=Ui.Font(s,FontCss,Weight);int line=(int)Math.Ceiling(font.GetHeight()*1.0f);int h=(int)Math.Round(line+2*PadY*s+2*s);if(Multiline)h=Math.Max(h+line,(int)Math.Round(Math.Max(65,MinHeightCss)*s));return h;}
        protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);float s=Sc;Box.Font=Ui.Font(s,FontCss,Weight);int padX=(int)Math.Round(PadX*s+s),padY=(int)Math.Round(PadY*s+s);
            if(Multiline)Box.SetBounds(padX,padY,Math.Max(1,Width-2*padX),Math.Max(1,Height-2*padY));
            else{int h=Box.PreferredHeight;Box.SetBounds(padX,(Height-h)/2,Math.Max(1,Width-2*padX),h);}
        }
        protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);PerformLayout();}
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;g.Clear(Parent==null?DesktopTheme.Card:Parent.BackColor);Ui.Prepare(g);float s=Sc;
            var r=new RectangleF(0,0,Width-1,Height-1);bool focus=Box.Focused;
            Ui.Box(g,new RectangleF(0,0,Width,Height),7*s,Ui.C("#fffaf1"),focus?DesktopTheme.Orange:Ui.C("#d5c7ad"),focus?2*s:s);
        }
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);Box.Focus();}
        protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Box.Focus();}
        public new void Focus(){Box.Focus();}
    }
    // Popup that hosts a single owner-drawn control below its anchor.
    public sealed class Popup : ToolStripDropDown {
        public Popup(Control content){
            Margin=Padding.Empty;Padding=Padding.Empty;AutoSize=false;DropShadowEnabled=true;BackColor=DesktopTheme.Card;
            var host=new ToolStripControlHost(content){Margin=Padding.Empty,Padding=Padding.Empty,AutoSize=false,Size=content.Size};
            Items.Add(host);Size=content.Size;
            Closed+=delegate{var t=new Timer{Interval=1};t.Tick+=delegate{t.Stop();t.Dispose();Dispose();};t.Start();};
        }
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);}
    }
    // Select styled like `.field select`, with a themed option list.
    public class SelectField : Control {
        public readonly List<KeyValuePair<string,string>> Options=new List<KeyValuePair<string,string>>();
        string value="";bool hovered;public float FontCss=13,PadX=11,PadY=10;
        public event EventHandler ValueChanged;
        public SelectField(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);TabStop=true;Cursor=Cursors.Default;}
        public SelectField Add(string key,string label){Options.Add(new KeyValuePair<string,string>(key,label));if(Options.Count==1)value=key;return this;}
        public string Value{get{return value;}set{if(Options.All(o=>o.Key!=value))return;if(this.value==value)return;this.value=value;Invalidate();}}
        public string Label{get{var o=Options.FirstOrDefault(x=>x.Key==value);return o.Value??"";}}
        void Choose(string next){if(next==value)return;value=next;Invalidate();if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);}
        float Sc{get{return DeviceDpi/96f;}}
        public int PreferredHeight(){float s=Sc;var font=Ui.Font(s,FontCss,400);return (int)Math.Round(font.GetHeight()+2*PadY*s+2*s+2*s);}
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;g.Clear(Parent==null?DesktopTheme.Card:Parent.BackColor);Ui.Prepare(g);float s=Sc;
            float alpha=Enabled?1:.5f;Color back=Parent==null?DesktopTheme.Card:Parent.BackColor;Func<Color,Color> f=c=>Ui.Fade(c,back,alpha);
            bool focus=Focused;Ui.Box(g,new RectangleF(0,0,Width,Height),7*s,f(Ui.C("#fffaf1")),f(focus?DesktopTheme.Orange:Ui.C("#d5c7ad")),focus?2*s:s);
            // `.field select` inherits weight 700 from .field; Chrome adds ~4px inside the padding
            // and draws a heavy chevron about 8px from the right edge.
            var font=Ui.Font(s,FontCss,700,null,Label);int pad=(int)Math.Round((PadX+4)*s+s);
            Ui.Text(g,Label,font,new Rectangle(pad,0,Math.Max(1,Width-pad-(int)(28*s)),Height),f(DesktopTheme.Ink),TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            Ui.Icon(g,"chevron",new RectangleF(Width-20*s,Height/2f-6*s,12*s,12*s),f(DesktopTheme.Ink),4.4f);
        }
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left||!Enabled)return;Focus();Open();}
        protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
        protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
        protected override void OnEnabledChanged(EventArgs e){Invalidate();base.OnEnabledChanged(e);}
        protected override bool IsInputKey(Keys keyData){return keyData==Keys.Up||keyData==Keys.Down||base.IsInputKey(keyData);}
        protected override void OnKeyDown(KeyEventArgs e){
            base.OnKeyDown(e);int index=Options.FindIndex(o=>o.Key==value);
            if(e.KeyCode==Keys.Up&&!e.Alt&&index>0){Choose(Options[index-1].Key);e.Handled=true;}
            else if(e.KeyCode==Keys.Down&&!e.Alt&&index<Options.Count-1){Choose(Options[index+1].Key);e.Handled=true;}
            else if((e.KeyCode==Keys.Down&&e.Alt)||e.KeyCode==Keys.Space||e.KeyCode==Keys.F4||e.KeyCode==Keys.Enter){Open();e.Handled=true;}
        }
        void Open(){
            float s=Sc;var list=new OptionList(this);int row=(int)Math.Round(30*s);list.Size=new Size(Math.Max(Width,(int)(140*s)),Math.Min(row*Options.Count+(int)(8*s),(int)(320*s)));
            var popup=new Popup(list);list.Picked=key=>{popup.Close();Choose(key);Focus();};popup.Show(this,new Point(0,Height+(int)(2*s)));list.Focus();
        }
        sealed class OptionList : Control {
            readonly SelectField owner;int hover=-1,offset;public Action<string> Picked;
            public OptionList(SelectField field){owner=field;DoubleBuffered=true;SetStyle(ControlStyles.Selectable|ControlStyles.ResizeRedraw,true);hover=owner.Options.FindIndex(o=>o.Key==owner.value);}
            float Sc{get{return owner.DeviceDpi/96f;}}
            int Row{get{return (int)Math.Round(30*Sc);}}
            protected override void OnPaint(PaintEventArgs e){
                var g=e.Graphics;float s=Sc;g.Clear(DesktopTheme.Card);int pad=(int)(4*s);
                using(var pen=new Pen(DesktopTheme.Frame,s))g.DrawRectangle(pen,0,0,Width-1,Height-1);
                for(int i=0;i<owner.Options.Count;i++){
                    var r=new Rectangle(pad,pad+i*Row-offset,Width-2*pad,Row);if(r.Bottom<0||r.Top>Height)continue;
                    bool chosen=owner.Options[i].Key==owner.value;
                    if(i==hover)Ui.Fill(g,r,6*s,Ui.C("#f9ecd6"));else if(chosen)Ui.Fill(g,r,6*s,Ui.C("#fff1d1"));
                    Ui.Text(g,owner.Options[i].Value,Ui.Font(s,owner.FontCss,chosen?700:400,null,owner.Options[i].Value),new Rectangle(r.X+(int)(10*s),r.Y,r.Width-(int)(12*s),r.Height),DesktopTheme.Ink,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                }
            }
            int At(Point p){int i=(p.Y+offset-(int)(4*Sc))/Row;return i>=0&&i<owner.Options.Count?i:-1;}
            protected override void OnMouseMove(MouseEventArgs e){int i=At(e.Location);if(i!=hover){hover=i;Invalidate();}}
            protected override void OnMouseUp(MouseEventArgs e){int i=At(e.Location);if(i>=0&&Picked!=null)Picked(owner.Options[i].Key);}
            protected override void OnMouseWheel(MouseEventArgs e){int max=Math.Max(0,owner.Options.Count*Row+(int)(8*Sc)-Height);offset=Math.Max(0,Math.Min(max,offset-e.Delta/120*Row*2));Invalidate();}
            protected override bool IsInputKey(Keys k){return k==Keys.Up||k==Keys.Down||k==Keys.Enter||base.IsInputKey(k);}
            protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Up&&hover>0)hover--;else if(e.KeyCode==Keys.Down&&hover<owner.Options.Count-1)hover++;else if(e.KeyCode==Keys.Enter&&hover>=0&&Picked!=null){Picked(owner.Options[hover].Key);return;}int top=hover*Row;if(top<offset)offset=top;if(top+Row>offset+Height-(int)(8*Sc))offset=top+Row-Height+(int)(8*Sc);Invalidate();}
        }
    }
    // Offscreen 32bpp DIB section with its own memory DC. GDI text drawn through
    // Graphics.FromHdc(Dc) goes straight to the DIB (an image-backed Graphics would make
    // every TextRenderer call copy pixels through a temporary DC), and BitBlt copies it out.
    public sealed class DibSurface : IDisposable {
        [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER{public int biSize,biWidth,biHeight;public short biPlanes,biBitCount;public int biCompression,biSizeImage,biXPelsPerMeter,biYPelsPerMeter,biClrUsed,biClrImportant;}
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc,ref BITMAPINFOHEADER info,int usage,out IntPtr bits,IntPtr section,int offset);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dest,int x,int y,int w,int h,IntPtr src,int sx,int sy,int rop);
        public readonly int Width,Height;public IntPtr Dc{get{return dc;}}
        IntPtr dc,bitmap,previous;
        public DibSurface(int width,int height){
            Width=Math.Max(1,width);Height=Math.Max(1,height);
            var info=new BITMAPINFOHEADER{biSize=Marshal.SizeOf(typeof(BITMAPINFOHEADER)),biWidth=Width,biHeight=-Height,biPlanes=1,biBitCount=32};
            dc=CreateCompatibleDC(IntPtr.Zero);IntPtr bits;bitmap=CreateDIBSection(dc,ref info,0,out bits,IntPtr.Zero,0);
            if(bitmap==IntPtr.Zero){DeleteDC(dc);dc=IntPtr.Zero;throw new OutOfMemoryException("DIB section");}
            previous=SelectObject(dc,bitmap);
        }
        public void BlitTo(Graphics g,Point at){var target=g.GetHdc();try{BitBlt(target,at.X,at.Y,Width,Height,dc,0,0,0x00CC0020);}finally{g.ReleaseHdc(target);}}
        public void Dispose(){if(dc==IntPtr.Zero)return;SelectObject(dc,previous);DeleteObject(bitmap);DeleteDC(dc);dc=IntPtr.Zero;}
    }
}
namespace PetCalendarDesktop {
    // The first dialog of a session paid ~130 ms of JIT compilation on the UI thread.
    // A low-priority background thread compiles the UI types ahead of time instead.
    public static class Warmup {
        public static void Start(){
            var thread=new System.Threading.Thread(()=>{
                System.Threading.Thread.Sleep(2500);
                foreach(var type in typeof(Warmup).Assembly.GetTypes()){
                    if(type.Namespace!="PetCalendarDesktop"||type.ContainsGenericParameters)continue;
                    if(!(typeof(Control).IsAssignableFrom(type)||typeof(FlowItem).IsAssignableFrom(type)||type==typeof(Ui)||type.Name=="Css"||type.Name=="Rules"))continue;
                    foreach(var method in type.GetMethods(System.Reflection.BindingFlags.DeclaredOnly|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic)){
                        if(method.IsAbstract||method.ContainsGenericParameters)continue;
                        try{System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(method.MethodHandle);}catch{}
                    }
                    foreach(var ctor in type.GetConstructors(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic)){try{System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(ctor.MethodHandle);}catch{}}
                }
            }){IsBackground=true,Priority=System.Threading.ThreadPriority.BelowNormal,Name="ui-warmup"};
            thread.Start();
        }
    }
}
