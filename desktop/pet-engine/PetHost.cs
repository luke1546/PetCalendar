using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;


namespace DesktopPet {
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
        internal Action RenameRequested;
        internal Action<int> PersonalityRequested;
        internal int Personality=2;
        internal static readonly string[] PersonalityLabels={"수다쟁이 · 30초~1분","말이 많은 편 · 1~2분","보통 · 2~3분","조용한 편 · 3~5분","과묵함 · 5~10분"};
        string petName="";
        ToolStripMenuItem identity;
        internal string PetName {get{return petName;}set{petName=value??"";Text=WindowPrefix+" · "+(petName==""?Kind.Name:petName)+" · "+paletteName;if(identity!=null)identity.Text=petName==""?paletteName+" "+Kind.Name:petName.Replace("&","&&")+" · "+paletteName+" "+Kind.Name;}}

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
        Point pressAt;
        double grabX,grabY;
        int lastFrame=-1,lastX,lastY,lastDirection,lastSize;
        internal PetHost() : this(Palette.All[0],true,.6) {}
        internal PetHost(Palette palette,bool ownTray,double spawn) : this(Species.Cat,palette,ownTray,spawn) {}
        internal PetHost(Species species,Palette palette,bool ownTray,double spawn) {
            Kind=species;Cat=new Motion(Guid.NewGuid().GetHashCode(),species);
            paletteName=palette.Name;spawnFraction=spawn;Art=new Sprites(species,palette);
            Text=WindowPrefix+" · "+Kind.Name+" · "+paletteName;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;
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
            if(initialized)return;initialized=true;RefreshWorld(false);BringToFloor();lastTick=World.Now;SimulationLoop.Add(this);
        }
        void BuildMenu() {
            if(menuBuilt)return;menuBuilt=true;
            identity=new ToolStripMenuItem{Text=petName==""?paletteName+" "+Kind.Name:petName.Replace("&","&&")+" · "+paletteName+" "+Kind.Name,Enabled=false};menu.Items.Add(identity);
            if(RenameRequested!=null)menu.Items.Add("이름 짓기 / 바꾸기",null,delegate{RenameRequested();});
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
            menu.Opening+=delegate { menuOpen=true;if(pauseItem!=null)pauseItem.Checked=Cat.Paused;if(sleepItem!=null)sleepItem.Text=Cat.Sleeping?"깨우기":"잠자기";for(int i=0;i<personalities.Count;i++)personalities[i].Checked=Personality==i+1;foreach(var item in sizeItems)item.Checked=(int)item.Tag==dip; };
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
            Cat.SetWorld(world.Surfaces(Cat.Size),world.WorkAreas);
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
            if(now>=nextWorld) {RefreshWorld(false);nextWorld=now+.25;}
            if(systemHidden || SimulationLoop.Suspended || menuOpen) {accumulator=0;return;}
            if(dragging) {
                Point cursor=Cursor.Position;Cat.X=cursor.X+grabX;Cat.Y=cursor.Y+grabY;
                samples.Add(new Sample{Point=cursor,Time=now});samples.RemoveAll(s=>now-s.Time>.12);
                ResizeCat();
            }
            accumulator+=dt;
            while(accumulator>=1.0/120) {Cat.Step(1.0/120,!press&&!speechHold);accumulator-=1.0/120;}
            DrawCat(false);
        }
        internal void DrawCat(bool force) {
            int x=(int)Math.Round(Cat.X),y=(int)Math.Round(Cat.Y),size=(int)Math.Round(Cat.Size);
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
            if(dragging) {
                Point p=Cursor.Position;double now=World.Now;
                Cat.X=p.X+grabX;Cat.Y=p.Y+grabY;
                var recent=samples.Where(s=>now-s.Time<=.1).ToList();double vx=0,vy=0;
                if(recent.Count>=2 && now-recent[0].Time>=.008) {double elapsed=now-recent[0].Time;vx=(p.X-recent[0].Point.X)/elapsed*.65;vy=(p.Y-recent[0].Point.Y)/elapsed*.65;}
                Cat.Release(vx,vy);
            }else {Cat.Wake();if(SpeechClicked!=null)SpeechClicked();}
            press=dragging=false;Capture=false;samples.Clear();DrawCat(true);
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
                disposed=true;SimulationLoop.Remove(this);
                if(tray!=null) {tray.Visible=false;tray.Dispose();}menu.Dispose();Art.Dispose();if(Icon!=null)Icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
