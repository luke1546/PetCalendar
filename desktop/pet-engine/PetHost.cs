using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;


namespace DesktopPet {
    // Keeps every pet window above other always-on-top windows, in a fixed stack.
    // Bottom to top: bed, pets, name tags/toy, chatter bubbles, schedule bubbles, alarms.
    // Other topmost windows (widgets, popups) opened later would otherwise cover pets,
    // so the stack is re-asserted every 1.5 s and whenever a bubble appears.
    internal static class PetLayers {
        internal const int Bed=0,Pet=1,Tag=2,Chatter=3,Schedule=4,Alarm=5;
        sealed class Entry{public Form Window;public Func<int> Layer;public long Order;}
        static readonly List<Entry> entries=new List<Entry>();static long next;
        static System.Windows.Forms.Timer timer;
        [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
        [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int w,int h,uint flags);
        internal static void Register(Form window,Func<int> layer){
            entries.Add(new Entry{Window=window,Layer=layer,Order=next++});
            window.Disposed+=delegate{entries.RemoveAll(e=>e.Window==window);};
            if(timer==null){timer=new System.Windows.Forms.Timer{Interval=1500};timer.Tick+=delegate{Raise();};timer.Start();}
        }
        internal static int Count{get{return entries.Count;}}
        internal static void Raise(){
            // HWND_TOPMOST puts a window at the top of the topmost band, so raising bottom-up builds the stack.
            foreach(var e in entries.ToArray().OrderBy(x=>x.Layer()).ThenBy(x=>x.Order)){
                var w=e.Window;if(w.IsDisposed||!w.IsHandleCreated||!IsWindowVisible(w.Handle))continue;
                SetWindowPos(w.Handle,new IntPtr(-1),0,0,0,0,0x0001|0x0002|0x0010|0x0200|0x0400);
            }
        }
    }
    internal sealed class Sample { public Point Point;public double Time; }
    internal sealed class PetHost : Form {
        internal static string SettingsDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DesktopPet");
        internal static string WindowPrefix="DesktopPet",CloseAllLabel="모두 종료";
        internal string PaletteName {get {return paletteName;}}
        internal bool ReadyForSpeech {get {return initialized&&!disposed&&Visible&&!systemHidden&&!dragging&&!Cat.Held&&!menuOpen;}}
        bool speechHold;
        internal bool SpeechHold {get{return speechHold;}set{if(value&&!speechHold&&!Cat.Paused&&Cat.Support!=null&&Cat.State==Pose.Walk){Cat.Vx=0;Cat.SetPose(Pose.Idle);}speechHold=value;}}
        internal readonly Motion Cat;
        internal readonly Species Kind;
        internal readonly Sprites Art;
        internal Action<Species> AddSpeciesRequested;
        internal Action CloseAllRequested;
        internal Action SpeechClicked;
        internal Action SpeechDoubleClicked;
        internal Action RenameRequested;
        internal Action<int> PersonalityRequested;
        // Calendar hooks: affinity points, snack menu item, extra text after the name in the menu,
        // and a suffix (hearts) on the name tag.
        internal int Affinity;internal DateTime FedAtUtc=DateTime.MinValue;
        internal Action SnackRequested;internal Func<string> SnackLabel,IdentityDetail;
        string tagSuffix="";internal string TagSuffix{get{return tagSuffix;}set{tagSuffix=value??"";RefreshTag();}}
        ToolStripMenuItem snackItem;
        internal int Personality=2;
        internal bool SpawnFromCenter;
        internal static readonly string[] PersonalityLabels={"수다쟁이 · 30초~1분","말이 많은 편 · 1~2분","보통 · 2~3분","조용한 편 · 3~5분","과묵함 · 5~10분"};
        string petName="";
        ToolStripMenuItem identity;
        NameTag tag;
        internal string PetName {get{return petName;}set{petName=value??"";UpdateTag();Text=WindowPrefix+" · "+(petName==""?Kind.Name:petName)+" · "+paletteName;if(identity!=null)identity.Text=IdentityText();}}
        string IdentityText(){return petName==""?paletteName+" "+Kind.Name:petName.Replace("&","&&")+" · "+paletteName+" "+Kind.Name;}

        readonly string paletteName;
        readonly double spawnFraction;
        WorldSnapshot world;
        bool initialized,menuBuilt;
        internal int MoveCount;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu=new ContextMenuStrip();
        readonly List<Sample> samples=new List<Sample>();
        readonly List<ToolStripMenuItem> sizeItems=new List<ToolStripMenuItem>();
        readonly string settings=Path.Combine(SettingsDirectory,"size.txt");
        ToolStripMenuItem pauseItem,sleepItem;
        Screen[] screens;
        double lastTick,accumulator,nextWorld;
        int dip=96;
        bool press,dragging,menuOpen,systemHidden,disposed;
        Point pressAt,lastClickPoint;
        double lastClickAt=Double.NegativeInfinity;
        double grabX,grabY;
        int lastFrame=-1,lastX,lastY,lastDirection,lastSize;
        internal PetHost() : this(Palette.All[0],true,.6) {}
        internal PetHost(Palette palette,bool ownTray,double spawn) : this(Species.Cat,palette,ownTray,spawn) {}
        internal PetHost(Species species,Palette palette,bool ownTray,double spawn) {
            Kind=species;Cat=new Motion(Guid.NewGuid().GetHashCode(),species);
            paletteName=palette.Name;spawnFraction=spawn;Art=new Sprites(species,palette);
            Text=WindowPrefix+" · "+Kind.Name+" · "+paletteName;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;PetLayers.Register(this,()=>PetLayers.Pet);
            StartPosition=FormStartPosition.Manual;Size=new Size(96,96);
            try { int saved;if(int.TryParse(File.ReadAllText(settings),out saved) && new[]{64,96,128}.Contains(saved))dip=saved; }catch(IOException){}catch(UnauthorizedAccessException){}
            if(ownTray)BuildMenu();
            using(var iconFrame=Art.Render(64,0,1)) {
                IntPtr nativeIcon=iconFrame.GetHicon();
                try { using(var original=Icon.FromHandle(nativeIcon))Icon=(Icon)original.Clone(); }
                finally { DestroyIcon(nativeIcon); }
            }
            if(ownTray) {
                tray=new NotifyIcon {Icon=Icon,Text=WindowPrefix+" · "+Kind.Name,ContextMenuStrip=menu,Visible=true};
                tray.DoubleClick+=delegate { BringToFloor(); };
            }

        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        protected override bool ShowWithoutActivation {get {return true;} }
        protected override CreateParams CreateParams {
            get { var p=base.CreateParams;p.ExStyle|=0x80000|0x80|0x08000000;return p; }
        }
        protected override void OnShown(EventArgs e) {
            base.OnShown(e);
            if(initialized)return;initialized=true;RefreshWorld(false);BringToFloor();
            if(SpawnFromCenter){
                var screen=screens.FirstOrDefault(s=>s.Primary)??screens[0];var area=screen.WorkingArea;
                Cat.Grab();Cat.X=area.Left+area.Width/2.0;Cat.Y=area.Top+area.Height/2.0;Cat.Release(0,0);DrawCat(true);
            }
            lastTick=World.Now;SimulationLoop.Add(this);
        }
        void BuildMenu() {
            if(menuBuilt)return;menuBuilt=true;
            identity=new ToolStripMenuItem{Text=IdentityText(),Enabled=false};menu.Items.Add(identity);
            if(RenameRequested!=null)menu.Items.Add("이름 짓기 / 바꾸기",null,delegate{RenameRequested();});
            if(SnackRequested!=null){snackItem=new ToolStripMenuItem("간식 주기",null,delegate{SnackRequested();});menu.Items.Add(snackItem);}
            menu.Items.Add(PetMenu.Add(delegate(Species species) {if(AddSpeciesRequested!=null)AddSpeciesRequested(species);}));
            menu.Items.Add(new ToolStripSeparator());
            var personalities=new List<ToolStripMenuItem>();
            if(PersonalityRequested!=null){var personalityMenu=new ToolStripMenuItem("성격");for(int i=0;i<PersonalityLabels.Length;i++){int choice=i+1;var item=new ToolStripMenuItem(PersonalityLabels[i],null,delegate{PersonalityRequested(choice);});personalityMenu.DropDownItems.Add(item);personalities.Add(item);}menu.Items.Add(personalityMenu);}
            else {
                menu.Items.Add("자유롭게 돌아다니기",null,delegate { Cat.Paused=false;Cat.Sleeping=false;Cat.Wake(); });
                menu.Items.Add(Kind.CanJump?"점프!":"코 인사",null,delegate { Cat.Paused=false;Cat.Jump(); });
                sleepItem=new ToolStripMenuItem("잠자기",null,delegate { Cat.Paused=false;if(Cat.Sleeping)Cat.Wake();else Cat.Sleep(); });menu.Items.Add(sleepItem);
                pauseItem=new ToolStripMenuItem("일시정지",null,delegate { Cat.Paused=!Cat.Paused; });menu.Items.Add(pauseItem);
            }
            var sizes=new ToolStripMenuItem(PetMenu.Noun+" 크기");
            foreach(int value in new[]{64,96,128}) {
                int choice=value;var item=new ToolStripMenuItem(value==64?"작게":value==96?"보통":"크게",null,delegate {
                    dip=choice;ResizeCat();
                    try {Directory.CreateDirectory(Path.GetDirectoryName(settings));File.WriteAllText(settings,dip.ToString());}
                    catch(Exception ex) { if(!(ex is IOException) && !(ex is UnauthorizedAccessException))throw; }
                });item.Tag=value;sizes.DropDownItems.Add(item);sizeItems.Add(item);
            }
            menu.Items.Add(sizes);if(PersonalityRequested==null)menu.Items.Add("바닥으로 데려오기",null,delegate {BringToFloor();});
            menu.Items.Add(new ToolStripSeparator());menu.Items.Add("이 "+PetMenu.Noun+" 보내기",null,delegate {Close();});
            menu.Items.Add(CloseAllLabel,null,delegate {if(CloseAllRequested!=null)CloseAllRequested();else Close();});
            menu.Opening+=delegate { menuOpen=true;if(snackItem!=null&&SnackLabel!=null)snackItem.Text=SnackLabel();if(identity!=null&&IdentityDetail!=null)identity.Text=IdentityText()+IdentityDetail();if(pauseItem!=null)pauseItem.Checked=Cat.Paused;if(sleepItem!=null)sleepItem.Text=Cat.Sleeping?"깨우기":"잠자기";for(int i=0;i<personalities.Count;i++)personalities[i].Checked=Personality==i+1;foreach(var item in sizeItems)item.Checked=(int)item.Tag==dip; };
            menu.Closed+=delegate {menuOpen=false;lastTick=World.Now;accumulator=0;SimulationLoop.Wake();};
        }
        internal int PreferredInterval {
            get {
                if(dragging || Cat.Held)return 16;
                if(systemHidden || Cat.Paused || SimulationLoop.Suspended || menuOpen)return 100;
                if(Cat.Support==null || Cat.State==Pose.Walk || Cat.State==Pose.Crouch || Cat.State==Pose.Land)return 16;
                return Cat.State==Pose.Sleep?100:33;
            }
        }
        internal void RefreshWorld() {RefreshWorld(true);if(initialized)DrawCat(false);}
        void RefreshWorld(bool force) {
            world=World.Get(force);screens=world.Screens;
            ResizeCat();
            if(Cat.SetWorld(world.Surfaces(Cat.Size),world.WorkAreas))World.FastUntil=World.Now+.6;
            Point point=new Point((int)Cat.X,(int)Cat.Y-1);
            if(!Cat.Held && !screens.Any(s=>s.Bounds.Contains(point)))BringToFloor();
            systemHidden=world.Fullscreen(new Point((int)Cat.X,(int)Cat.Y-1));
            if(systemHidden && Visible)Hide();else if(!systemHidden && !Visible)Show();
        }
        void ResizeCat() {
            if(world==null)world=World.Get(false);
            double size=dip*Kind.Scale*world.DpiAt(new Point((int)Cat.X,(int)Cat.Y-1));
            Cat.Size=Math.Max(32,Math.Min(512,size));
        }
        internal void BringToFloor() {
            if(world==null)world=World.Get(false);screens=world.Screens;
            var primary=screens.FirstOrDefault(s=>s.Primary)??screens[0];
            if(Cat.Surfaces.Count==0)Cat.SetWorld(world.Surfaces(Cat.Size),world.WorkAreas);
            var floor=Cat.Surfaces.First(s=>s.Id=="floor:"+primary.DeviceName);
            CancelDrag();Cat.Paused=false;
            Cat.Reset(primary.WorkingArea.Left+primary.WorkingArea.Width*spawnFraction,floor);ResizeCat();
            nextWorld=0;DrawCat(true);SimulationLoop.Wake();
        }
        internal void Pulse(double now) {
            if(disposed || !initialized)return;
            double dt=Math.Min(.25,Math.Max(0,now-lastTick));lastTick=now;
            if(now>=nextWorld) {RefreshWorld(false);nextWorld=now+(now<World.FastUntil?.04:.25);}
            if(systemHidden || SimulationLoop.Suspended || menuOpen) {accumulator=0;return;}
            if(dragging) {
                Point cursor=Cursor.Position;Cat.X=cursor.X+grabX;Cat.Y=cursor.Y+grabY;
                samples.Add(new Sample{Point=cursor,Time=now});samples.RemoveAll(s=>now-s.Time>.12);
                ResizeCat();
            }
            accumulator+=dt;
            // Step by the real frame time (in <=1/120 s slices) so motion is even at 60, 120 or 144 Hz.
            while(accumulator>1e-6) {double step=Math.Min(accumulator,1.0/120);Cat.Step(step,!press&&!speechHold);accumulator-=step;}
            DrawCat(false);
        }
        // Small name floating above a named pet. Speech bubbles sit above it (SpeechTop).
        void UpdateTag(){
            if(petName==""){if(tag!=null){tag.Dispose();tag=null;}return;}
            if(tag==null)tag=new NameTag();tag.SetText(petName+tagSuffix,DpiScale());if(initialized)PlaceTag();
        }
        // Re-render after the font setting changes.
        internal void RefreshTag(){if(tag!=null){tag.SetText(petName+tagSuffix,DpiScale());if(initialized)PlaceTag();}}
        double DpiScale(){if(world==null)world=World.Get(false);return world.DpiAt(new Point((int)Cat.X,(int)Cat.Y-1));}
        int HeadY{get{int size=(int)Math.Round(Cat.Size);return (int)Math.Round(Cat.Y-size*.875)+Art.HeadTop(size);}}
        // Top edge for a speech bubble drawn above this pet.
        internal int SpeechTop{get{int box=(int)Math.Round(Cat.Y-Cat.Size*.875);if(tag==null||!tag.Shown)return box;return Math.Min(box,tag.TagTop-2);}}
        void PlaceTag(){
            if(tag==null)return;bool show=Visible&&!systemHidden;
            if(!show){tag.HideTag();return;}
            if(Math.Abs(tag.Scale-DpiScale())>.01)tag.SetText(petName+tagSuffix,DpiScale());
            tag.MoveTo((int)Math.Round(Cat.X)-tag.Width2/2,HeadY-tag.Height2-(int)Math.Round(2*tag.Scale));
        }
        protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(initialized)PlaceTag();}
        internal void DrawCat(bool force) {
            int x=(int)Math.Round(Cat.X),y=(int)Math.Round(Cat.Y),size=(int)Math.Round(Cat.Size);
            if(tag!=null&&(force||x!=lastX||y!=lastY||size!=lastSize))PlaceTag();
            bool pixelsChanged=force || Cat.Frame!=lastFrame || Cat.Direction!=lastDirection || size!=lastSize;
            if(pixelsChanged)Art.Paint(Handle,Cat);
            else if(x!=lastX || y!=lastY) {
                if(!Native.SetWindowPos(Handle,IntPtr.Zero,(int)Math.Round(Cat.X-size/2.0),(int)Math.Round(Cat.Y-size*.875),0,0,0x215))throw new System.ComponentModel.Win32Exception();
                MoveCount++;
            }
            lastFrame=Cat.Frame;lastX=x;lastY=y;lastDirection=Cat.Direction;lastSize=size;
        }
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            if(e.Button!=MouseButtons.Left)return;
            SimulationLoop.Wake();press=true;pressAt=Cursor.Position;grabX=Cat.X-pressAt.X;grabY=Cat.Y-pressAt.Y;Capture=true;
        }
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);if(!press)return;
            Point p=Cursor.Position;
            if(!dragging && Math.Abs(p.X-pressAt.X)+Math.Abs(p.Y-pressAt.Y)>5) {
                SimulationLoop.Wake();dragging=true;Cat.Grab();samples.Clear();samples.Add(new Sample{Point=pressAt,Time=World.Now-.02});
            }
        }
        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);
            if(e.Button==MouseButtons.Right) {BuildMenu();menu.Show(Cursor.Position);return;}
            if(e.Button!=MouseButtons.Left || !press)return;
            bool doubleClicked=false;
            if(dragging) {
                lastClickAt=Double.NegativeInfinity;
                Point p=Cursor.Position;double now=World.Now;
                Cat.X=p.X+grabX;Cat.Y=p.Y+grabY;
                var recent=samples.Where(s=>now-s.Time<=.1).ToList();double vx=0,vy=0;
                if(recent.Count>=2 && now-recent[0].Time>=.008) {double elapsed=now-recent[0].Time;vx=(p.X-recent[0].Point.X)/elapsed*.65;vy=(p.Y-recent[0].Point.Y)/elapsed*.65;}
                Cat.Release(vx,vy);
            }else {
                Cat.Wake();var point=Cursor.Position;double now=World.Now;
                doubleClicked=(now-lastClickAt)*1000<=SystemInformation.DoubleClickTime && Math.Abs(point.X-lastClickPoint.X)<=SystemInformation.DoubleClickSize.Width/2 && Math.Abs(point.Y-lastClickPoint.Y)<=SystemInformation.DoubleClickSize.Height/2;
                lastClickAt=doubleClicked?Double.NegativeInfinity:now;lastClickPoint=point;
                if(!doubleClicked&&SpeechClicked!=null)SpeechClicked();
            }
            press=dragging=false;Capture=false;samples.Clear();DrawCat(true);
            if(doubleClicked&&SpeechDoubleClicked!=null)SpeechDoubleClicked();
        }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e);if(!Capture && press)CancelDrag(); }
        void CancelDrag() {if(dragging)Cat.Release(0,0);press=dragging=false;Capture=false;samples.Clear();}
        protected override void WndProc(ref Message m) {
            if(m.Msg==0x21) {m.Result=new IntPtr(3);return;} // MA_NOACTIVATE
            if(m.Msg==0x2E0) {World.Invalidate();nextWorld=0;return;}
            base.WndProc(ref m);
        }
        protected override void Dispose(bool disposing) {
            if(disposing && !disposed) {
                disposed=true;SimulationLoop.Remove(this);if(tag!=null){tag.Dispose();tag=null;}
                if(tray!=null) {tray.Visible=false;tray.Dispose();}menu.Dispose();Art.Dispose();if(Icon!=null)Icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
    // Click-through, per-pixel transparent label: a soft pill with the pet's name.
    internal sealed class NameTag : Form {
        // Set by the calendar so name tags use the chosen font; gets the pixel size, returns a new font.
        internal static Func<float,Font> FontFor;
        sealed class Run {internal string Text;internal Image Emoji;internal float Width;}
        static readonly Dictionary<string,Image> emojiImages=new Dictionary<string,Image>();
        static Image EmojiFor(string element){
            string code=Char.ConvertToUtf32(element,0).ToString("x");Image image;
            if(emojiImages.TryGetValue(code,out image))return image;
            using(var stream=typeof(NameTag).Assembly.GetManifestResourceStream("petcalendar.emoji.zip")){
                if(stream!=null)using(var zip=new System.IO.Compression.ZipArchive(stream,System.IO.Compression.ZipArchiveMode.Read)){
                    var entry=zip.GetEntry(code+".png");
                    if(entry!=null)using(var input=entry.Open())using(var original=Image.FromStream(input))image=new Bitmap(original);
                }
            }
            emojiImages[code]=image;return image;
        }
        IntPtr bitmap=IntPtr.Zero;internal int Width2,Height2;internal double Scale=1;internal bool Shown;int x=int.MinValue,y=int.MinValue;
        internal NameTag(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;PetLayers.Register(this,()=>PetLayers.Tag);StartPosition=FormStartPosition.Manual;Size=new Size(1,1);}
        protected override bool ShowWithoutActivation{get{return true;}}
        protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x80000|0x80|0x08000000|0x20;return p;}}
        internal void SetText(string text,double scale){
            Scale=scale;
            using(var image=RenderText(text,scale)){
                IntPtr next=image.GetHbitmap(Color.FromArgb(0));
                if(bitmap!=IntPtr.Zero)Native.DeleteObject(bitmap);bitmap=next;
                Width2=image.Width;Height2=image.Height;x=int.MinValue;
            }
        }
        // GDI+ DrawString does not paint color emoji. Keep the selected font for
        // names and paint bundled emoji artwork with the same measured layout.
        internal static Bitmap RenderText(string text,double scale){
            float s=(float)scale;
            using(var font=FontFor!=null?FontFor(10.5f*s):new Font("Malgun Gothic",10.5f*s,FontStyle.Bold,GraphicsUnit.Pixel))
            using(var format=(StringFormat)StringFormat.GenericTypographic.Clone()){
                format.FormatFlags|=StringFormatFlags.MeasureTrailingSpaces;
                var runs=new List<Run>();var plain=new System.Text.StringBuilder();float contentWidth=0,emojiSize=(float)Math.Ceiling(font.GetHeight());
                using(var probe=new Bitmap(1,1))using(var g=Graphics.FromImage(probe)){
                    Action flush=()=>{if(plain.Length==0)return;string value=plain.ToString();float width=g.MeasureString(value,font,PointF.Empty,format).Width;runs.Add(new Run{Text=value,Width=width});contentWidth+=width;plain.Clear();};
                    var elements=System.Globalization.StringInfo.GetTextElementEnumerator(text??"");
                    while(elements.MoveNext()){
                        string element=elements.GetTextElement();if(element=="\ufe0f")continue;
                        Image emoji=EmojiFor(element);
                        if(emoji==null)plain.Append(element);
                        else{flush();runs.Add(new Run{Emoji=emoji,Width=emojiSize});contentWidth+=emojiSize;}
                    }
                    flush();
                }
                int w=(int)Math.Ceiling(contentWidth+16*s),h=(int)Math.Ceiling(Math.Max(font.GetHeight(),emojiSize)+7*s);w=Math.Max(w,h);
                var image=new Bitmap(w,h,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using(var g=Graphics.FromImage(image)){
                    g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;g.Clear(Color.Transparent);
                    float r=(h-1)/2f;using(var path=new System.Drawing.Drawing2D.GraphicsPath()){path.AddArc(.5f,.5f,r*2,r*2,90,180);path.AddArc(w-1.5f-r*2,.5f,r*2,r*2,270,180);path.CloseFigure();
                        using(var fill=new SolidBrush(Color.FromArgb(215,255,253,247)))g.FillPath(fill,path);using(var pen=new Pen(Color.FromArgb(200,140,116,80),Math.Max(1f,s)))g.DrawPath(pen,path);}
                    float left=(w-contentWidth)/2f;
                    using(var ink=new SolidBrush(Color.FromArgb(255,74,58,38)))foreach(var run in runs){
                        if(run.Emoji!=null)g.DrawImage(run.Emoji,new RectangleF(left,(h-emojiSize)/2f,emojiSize,emojiSize));
                        else g.DrawString(run.Text,font,ink,left,(h-font.GetHeight())/2f,format);
                        left+=run.Width;
                    }
                }
                return image;
            }
        }
        internal void HideTag(){if(Shown&&IsHandleCreated){Native.SetWindowPos(Handle,IntPtr.Zero,0,0,0,0,0x93);}Shown=false;}
        internal void MoveTo(int left,int top){
            if(!IsHandleCreated)CreateHandle();
            if(left!=x||top!=y){
                x=left;y=top;
                IntPtr screen=Native.GetDC(IntPtr.Zero),dc=Native.CreateCompatibleDC(screen),old=Native.SelectObject(dc,bitmap);
                try{var pos=new Native.POINT(x,y);var size=new Native.SIZE(Width2,Height2);var origin=new Native.POINT(0,0);var blend=new Native.BLEND{Op=0,Flags=0,Alpha=255,Format=1};Native.UpdateLayeredWindow(Handle,screen,ref pos,ref size,dc,ref origin,0,ref blend,2);}
                finally{Native.SelectObject(dc,old);Native.DeleteDC(dc);Native.ReleaseDC(IntPtr.Zero,screen);}
            }
            if(!Shown){Native.SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x53);Shown=true;}
        }
        internal int TagTop{get{return y;}}
        protected override void Dispose(bool disposing){if(bitmap!=IntPtr.Zero){Native.DeleteObject(bitmap);bitmap=IntPtr.Zero;}base.Dispose(disposing);}
    }
}
