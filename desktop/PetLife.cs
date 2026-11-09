using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DesktopPet;

namespace PetCalendarDesktop {
    public sealed class DiaryEntry {public string date,pet,species,text;}

    // Little bits of pet life beyond the schedule: pets greet each other when they meet,
    // go to sleep on a cushion at night or while the user is away, and keep daily counts
    // (steps, greetings, bed) for the pet diary.
    internal sealed class PetLife : IDisposable {
        sealed class Day {public double StartDistance=-1;public int Meets,Pets,Toys,LevelUp;public bool Bed;public DateTime PetQuietUntil;}
        sealed class Note {public PetHost Pet;public PetBubble Bubble;public DateTime Until;}
        readonly List<Note> notes=new List<Note>();
        // Affinity points for a pet (owner applies and persists them).
        internal Action<PetHost,int> Award;
        // Lets the schedule speech step aside when the user interacts with its speaker.
        internal Action<PetHost> BeforeSpeak;
        internal bool Talking(PetHost pet){return notes.Any(n=>n.Pet==pet&&DateTime.UtcNow<n.Until);}
        sealed class Meeting {public PetHost A,B;public PetBubble BubbleA,BubbleB;public DateTime Start;public int Line;public bool Replied;}
        readonly Func<PetHost[]> pets;readonly Action<Exception> log;
        readonly Timer timer=new Timer{Interval=400};
        readonly Random random=new Random();
        readonly Dictionary<string,DateTime> pairCooldown=new Dictionary<string,DateTime>();
        readonly Dictionary<PetHost,Day> days=new Dictionary<PetHost,Day>();
        readonly HashSet<PetHost> sleepers=new HashSet<PetHost>(),excused=new HashSet<PetHost>();
        Meeting meeting;DateTime nextMeet,nextSteer;string steered;string today;bool bedTime;BedWindow bed;
        internal bool BedEnabled=true,ToyEnabled=true;internal double BedFraction=.82;
        ToyWindow toy;bool toyMode;Point lastCursor;DateTime lastMove;
        internal Action<double> BedMoved;internal Action BedHidden;
        // Test switch: PETCAL_BEDTIME=1 makes it bedtime now.
        static readonly bool ForceBed=Environment.GetEnvironmentVariable("PETCAL_BEDTIME")=="1";
        [StructLayout(LayoutKind.Sequential)] struct LastInput {public uint Size,Time;}
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput info);
        internal PetLife(Func<PetHost[]> hosts,Action<Exception> error){pets=hosts;log=error;timer.Tick+=delegate{try{Tick();}catch(Exception e){log(e);}};}
        internal void Start(){timer.Start();}
        internal void Stop(){timer.Stop();foreach(var n in notes)n.Bubble.Dispose();notes.Clear();EndToy();if(toy!=null){toy.Dispose();toy=null;}EndMeeting();WakeSleepers();RemoveBed();}
        internal void ConfigureToy(bool enabled){ToyEnabled=enabled;if(!enabled)EndToy();}
        internal void ConfigureBed(bool enabled,double fraction){BedEnabled=enabled;BedFraction=Math.Max(.05,Math.Min(.95,fraction));if(!enabled){WakeSleepers();RemoveBed();}}
        public void Dispose(){Stop();timer.Dispose();}

        static double IdleSeconds(){var info=new LastInput{Size=(uint)Marshal.SizeOf(typeof(LastInput))};if(!GetLastInputInfo(ref info))return 0;return unchecked((uint)Environment.TickCount-info.Time)/1000.0;}
        void Tick(){
            var all=pets().Where(p=>!p.IsDisposed).ToArray();
            string key=Rules.Key(DateTime.Now);if(key!=today){today=key;days.Clear();}
            foreach(var pet in all){Day d;if(!days.TryGetValue(pet,out d)){d=new Day();days[pet]=d;}if(d.StartDistance<0)d.StartDistance=pet.Cat.Distance;}
            foreach(var gone in days.Keys.Where(p=>p.IsDisposed||!all.Contains(p)).ToArray())days.Remove(gone);
            BedTick(all);ToyTick(all);if(!toyMode)MeetTick(all);NotesTick();
            timer.Interval=toyMode?40:(notes.Count>0||meeting!=null?100:400);
        }

        // ---------- Toy ----------
        // Near a pet the cursor gets a yarn ball, and pets on the floor chase it.
        // (The real cursor is left alone: changing the system cursor could stick after a crash.)
        void ToyTick(PetHost[] all){
            if(!ToyEnabled||bedTime||all.Length==0){EndToy();return;}
            var c=Cursor.Position;var now=DateTime.UtcNow;
            if(c!=lastCursor){lastCursor=c;lastMove=now;}
            Func<PetHost,bool> free=p=>!p.IsDisposed&&p.Visible&&!p.Cat.Held&&!p.Cat.Paused&&!p.SpeechHold&&p.Cat.Support!=null&&!sleepers.Contains(p);
            if(!toyMode){
                bool near=all.Any(p=>free(p)&&Math.Abs(p.Cat.X-c.X)<p.Cat.Size*1.1&&c.Y>p.Cat.Y-p.Cat.Size*2&&c.Y<p.Cat.Y+p.Cat.Size*.4);
                if(!near||(now-lastMove).TotalSeconds>1)return;
                toyMode=true;EndMeeting();if(toy==null)toy=new ToyWindow();
                var first=all.Where(free).OrderBy(p=>Math.Abs(p.Cat.X-c.X)).FirstOrDefault();
                if(first!=null)Speak(first,PetLines.Say(first.Kind.Id,LevelOf(first),"toy"),4);
                foreach(var p in all.Where(free).Where(p=>Math.Abs(p.Cat.X-c.X)<900)){var d=DayOf(p);if(d.Toys<3){d.Toys++;if(Award!=null)Award(p,2);}}
            }
            if((now-lastMove).TotalSeconds>6||all.All(p=>Math.Abs(p.Cat.X-c.X)>900)){EndToy();return;}
            toy.ShowAt(c,World.Get(false).DpiAt(c));
            int slot=0;
            foreach(var pet in all.OrderBy(p=>Math.Abs(p.Cat.X-c.X))){
                if(!free(pet)||Math.Abs(pet.Cat.X-c.X)>900)continue;
                if(pet.Cat.Sleeping)pet.Cat.Wake();
                double target=c.X+(slot==0?0:((slot%2==1)?-1:1)*pet.Cat.Size*.55*((slot+1)/2));slot++;
                if(!pet.Cat.GoalX.HasValue||Math.Abs(pet.Cat.GoalX.Value-target)>12*pet.Cat.Unit){pet.Cat.GoalX=target;pet.Cat.GoalSleep=false;}
                // Close under a toy held up high: sometimes leap for it.
                double dx=Math.Abs(pet.Cat.X-c.X),above=pet.Cat.Y-pet.Cat.Size*.9-c.Y;
                if(pet.Kind.CanJump&&dx<pet.Cat.Size*.5&&above>10&&above<pet.Cat.Size*1.6&&pet.Cat.State!=Pose.Crouch&&random.NextDouble()<.03){pet.Cat.GoalX=null;pet.Cat.Jump();}
            }
        }
        void EndToy(){
            if(!toyMode)return;toyMode=false;if(toy!=null)toy.HideToy();
            foreach(var pet in pets())if(!pet.IsDisposed&&pet.Cat.GoalX.HasValue&&!pet.Cat.GoalSleep)pet.Cat.GoalX=null;
        }
        // Today's numbers for the diary.
        internal void Stats(PetHost pet,out int steps,out int meets,out bool slept,out int levelUp){
            Day d;steps=0;meets=0;slept=false;levelUp=0;if(pet==null||!days.TryGetValue(pet,out d))return;levelUp=d.LevelUp;
            steps=(int)Math.Max(0,(pet.Cat.Distance-Math.Max(0,d.StartDistance))/Math.Max(1,pet.Cat.Unit*9));meets=d.Meets;slept=d.Bed;
        }

        // ---------- Speaking ----------
        Day DayOf(PetHost pet){Day d;if(!days.TryGetValue(pet,out d)){d=new Day();days[pet]=d;}return d;}
        // A short line from one pet (petting, toy, snack, level up). Replaces that pet's previous note.
        internal void Speak(PetHost pet,string text,double seconds=5){
            if(pet==null||pet.IsDisposed||String.IsNullOrEmpty(text))return;
            if(BeforeSpeak!=null)BeforeSpeak(pet);
            foreach(var old in notes.Where(n=>n.Pet==pet).ToArray()){old.Bubble.Dispose();notes.Remove(old);}
            var bubble=new PetBubble();var note=new Note{Pet=pet,Bubble=bubble,Until=DateTime.UtcNow.AddSeconds(seconds)};
            bubble.DismissRequested+=()=>note.Until=DateTime.MinValue;bubble.OpenRequested+=()=>note.Until=DateTime.MinValue;
            bubble.Present(text,false,0,false,true);bubble.Follow(pet);notes.Add(note);timer.Interval=Math.Min(timer.Interval,100);
        }
        void NotesTick(){
            var now=DateTime.UtcNow;
            foreach(var n in notes.ToArray()){if(n.Pet.IsDisposed||now>=n.Until){n.Bubble.Dispose();notes.Remove(n);}else if(n.Pet.Visible)n.Bubble.Follow(n.Pet);}
            if(notes.Count>0)timer.Interval=Math.Min(timer.Interval,100);
        }
        internal static int LevelOf(PetHost pet){return PetLines.Level(pet.Affinity);}
        // Clicked: a petting line now and then (not while the pet is busy talking), +1 up to 5 a day.
        internal void Petted(PetHost pet){
            if(pet==null||pet.IsDisposed||Talking(pet))return;var d=DayOf(pet);var now=DateTime.UtcNow;
            if(now<d.PetQuietUntil)return;d.PetQuietUntil=now.AddSeconds(12);
            Speak(pet,PetLines.Say(pet.Kind.Id,LevelOf(pet),"pet"),4);
            if(d.Pets<5){d.Pets++;if(Award!=null)Award(pet,1);}
        }
        internal void LeveledUp(PetHost pet,int level){
            if(pet==null||pet.IsDisposed)return;DayOf(pet).LevelUp=level;
            Speak(pet,PetLines.LevelUp(pet.Kind.Id,level)+"\n💕 "+PetLines.LevelName(level)+" 사이가 됐어요!",8);
            if(!pet.Cat.Held&&!pet.Cat.Paused){pet.Cat.Sleeping=false;pet.Cat.Jump();}
        }

        // ---------- Meeting ----------
        static bool Free(PetHost p){return p.ReadyForSpeech&&!p.SpeechHold&&p.Cat.Support!=null&&!p.Cat.Sleeping&&!p.Cat.Paused&&!p.Cat.GoalX.HasValue&&(p.Cat.State==Pose.Idle||p.Cat.State==Pose.Walk||p.Cat.State==Pose.Groom);}
        static string Call(PetHost p){
            string name=String.IsNullOrWhiteSpace(p.PetName)?p.Kind.Name:p.PetName.Trim();char last=name[name.Length-1];
            if(last<0xac00||last>0xd7a3)return name+",";
            return name+((last-0xac00)%28!=0?"아":"야");
        }
        static readonly string[] Greetings={"{0} 안녕! 🐾","{0}, 산책 중이야?","{0}, 같이 놀자!","{0}, 오늘 뭐 했어?"};
        static readonly string[] Replies={"{0} 반가워! 😊","응! 날씨가 좋아서 ☀️","좋아! 잡아봐라~ 🏃","나는 낮잠 잤어 💤"};
        void MeetTick(PetHost[] all){
            var now=DateTime.UtcNow;
            if(meeting!=null){
                double age=(now-meeting.Start).TotalSeconds;
                if(meeting.A.IsDisposed||meeting.B.IsDisposed||meeting.A.Cat.Held||meeting.B.Cat.Held){EndMeeting();return;}
                if(!meeting.Replied&&age>1.4){meeting.Replied=true;if(meeting.BubbleA!=null){meeting.BubbleA.Dispose();meeting.BubbleA=null;}meeting.BubbleB=Say(meeting.B,String.Format(Replies[meeting.Line],Call(meeting.A)));}
                if(meeting.BubbleA!=null)meeting.BubbleA.Follow(meeting.A);if(meeting.BubbleB!=null)meeting.BubbleB.Follow(meeting.B);
                if(age>5.2){var a=meeting.A;EndMeeting();if(a.Kind.CanJump&&random.NextDouble()<.35&&!a.Cat.Held)a.Cat.Jump();}
                return;
            }
            if(now<nextMeet||all.Length<2)return;
            var free=all.Where(Free).ToArray();
            for(int i=0;i<free.Length;i++)for(int j=i+1;j<free.Length;j++){
                var a=free[i];var b=free[j];
                if(a.Cat.Support!=b.Cat.Support&&Math.Abs(a.Cat.Y-b.Cat.Y)>2)continue;
                double dx=Math.Abs(a.Cat.X-b.Cat.X);if(dx<8||dx>(a.Cat.Size+b.Cat.Size)*.55)continue;
                string pair=a.Handle.ToInt64()<b.Handle.ToInt64()?a.Handle+"-"+b.Handle:b.Handle+"-"+a.Handle;DateTime until;
                if(pairCooldown.TryGetValue(pair,out until)&&now<until)continue;
                // Not every brush-past turns into a chat.
                if(pair!=steered&&random.NextDouble()>.6){pairCooldown[pair]=now.AddSeconds(30);continue;}
                steered=null;
                pairCooldown[pair]=now.AddMinutes(4);nextMeet=now.AddSeconds(40);Begin(a,b,now);return;
            }
            // Pets spread over a wide desktop rarely bump into each other, so now and then the
            // two nearest walk over to say hello.
            if(now<nextSteer)return;nextSteer=now.AddSeconds(15);
            if(random.NextDouble()>.5)return;
            PetHost na=null,nb=null;double best=double.MaxValue;
            for(int i=0;i<free.Length;i++)for(int j=i+1;j<free.Length;j++){
                var a=free[i];var b=free[j];if(Math.Abs(a.Cat.Y-b.Cat.Y)>2)continue;
                string pair=a.Handle.ToInt64()<b.Handle.ToInt64()?a.Handle+"-"+b.Handle:b.Handle+"-"+a.Handle;DateTime until;
                if(pairCooldown.TryGetValue(pair,out until)&&now<until)continue;
                double dx=Math.Abs(a.Cat.X-b.Cat.X);if(dx<500&&dx<best){best=dx;na=a;nb=b;}
            }
            if(na==null)return;
            double mid=(na.Cat.X+nb.Cat.X)/2,gap=(na.Cat.Size+nb.Cat.Size)*.2;bool left=na.Cat.X<nb.Cat.X;
            na.Cat.GoalX=mid+(left?-gap:gap);nb.Cat.GoalX=mid+(left?gap:-gap);na.Cat.GoalSleep=nb.Cat.GoalSleep=false;
            steered=na.Handle.ToInt64()<nb.Handle.ToInt64()?na.Handle+"-"+nb.Handle:nb.Handle+"-"+na.Handle;
        }
        void Begin(PetHost a,PetHost b,DateTime now){
            a.SpeechHold=b.SpeechHold=true;
            a.Cat.Direction=b.Cat.X>a.Cat.X?1:-1;b.Cat.Direction=-a.Cat.Direction;a.DrawCat(true);b.DrawCat(true);
            int line=random.Next(Greetings.Length);
            meeting=new Meeting{A=a,B=b,Start=now,Line=line,BubbleA=Say(a,String.Format(Greetings[line],Call(b)))};
            foreach(var p in new[]{a,b}){var d=DayOf(p);d.Meets++;if(d.Meets<=3&&Award!=null)Award(p,1);}
        }
        PetBubble Say(PetHost pet,string text){var bubble=new PetBubble();bubble.DismissRequested+=EndMeeting;bubble.OpenRequested+=EndMeeting;bubble.Present(text,false,0,false,true);bubble.Follow(pet);return bubble;}
        void EndMeeting(){
            var m=meeting;if(m==null)return;meeting=null;
            foreach(var b in new[]{m.BubbleA,m.BubbleB})if(b!=null)b.Dispose();
            foreach(var p in new[]{m.A,m.B})if(!p.IsDisposed)p.SpeechHold=false;
        }

        // ---------- Bed ----------
        Screen BedScreen{get{return Screen.PrimaryScreen;}}
        double BedCenter{get{var area=BedScreen.WorkingArea;return area.Left+area.Width*BedFraction;}}
        void RemoveBed(){if(bed!=null){bed.Dispose();bed=null;}}
        void PlaceBed(PetHost[] all){
            if(!BedEnabled||all.Length==0){RemoveBed();return;}
            double scale=World.Get(false).DpiAt(new Point(BedScreen.WorkingArea.Left+1,BedScreen.WorkingArea.Bottom-1));
            bool created=bed==null;
            if(created){bed=new BedWindow();bed.Moved+=x=>{var area=BedScreen.WorkingArea;BedFraction=Math.Max(.05,Math.Min(.95,(x-area.Left)/(double)area.Width));if(BedMoved!=null)BedMoved(BedFraction);};bed.HideRequested+=()=>{if(BedHidden!=null)BedHidden();};}
            bed.Place(BedScreen.WorkingArea,BedCenter,scale);
            // Pets (and their name tags) stay in front of the cushion.
            if(created)foreach(var p in all)Native.SetWindowPos(p.Handle,new IntPtr(-1),0,0,0,0,0x13);
        }
        void WakeSleepers(){foreach(var p in sleepers)if(!p.IsDisposed){p.Cat.GoalX=null;if(p.Cat.Sleeping)p.Cat.Wake();}sleepers.Clear();excused.Clear();bedTime=false;}
        void BedTick(PetHost[] all){
            PlaceBed(all);
            var local=DateTime.Now;bool night=local.Hour>=23||local.Hour<6;
            bool now=bed!=null&&!bed.Dragging&&(night||ForceBed||IdleSeconds()>=600);
            if(!now){if(bedTime)WakeSleepers();return;}
            bedTime=true;double center=BedCenter;int slot=0;
            foreach(var pet in all){
                if(excused.Contains(pet))continue;
                if(sleepers.Contains(pet)){
                    // Woken by a click or carried away: leave that pet be until bedtime ends.
                    if(!pet.Cat.Sleeping&&!pet.Cat.GoalX.HasValue)excused.Add(pet);else slot++;
                    continue;
                }
                var here=pet.Cat.Surfaces.FirstOrDefault(s=>s.Id==pet.Cat.Support);
                if(pet.Cat.Held||pet.SpeechHold||pet.Cat.Paused||!pet.Visible||here==null||!here.Floor)continue;
                if(!pet.Cat.Surfaces.Any(s=>s.Floor&&center>=s.Left&&center<=s.Right&&Math.Abs(s.Y-pet.Cat.Y)<2))continue;
                double spread=bed.Width*.4;double offset=slot==0?0:((slot%2==1)?-1:1)*spread*((slot+1)/2);slot++;
                pet.Cat.Sleeping=false;pet.Cat.GoalX=center+offset;pet.Cat.GoalSleep=true;sleepers.Add(pet);
                Day d;if(days.TryGetValue(pet,out d))d.Bed=true;
            }
        }
    }

    // Yarn ball hanging off the cursor tip; click-through so it never gets in the way.
    internal sealed class ToyWindow : Form {
        IntPtr bitmap=IntPtr.Zero;int size;double scale;bool shown;int x=int.MinValue,y=int.MinValue;
        internal ToyWindow(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;Size=new Size(1,1);Text="펫 캘린더 · 장난감";}
        protected override bool ShowWithoutActivation{get{return true;}}
        protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x80000|0x80|0x08000000|0x20;return p;}}
        void Render(){
            size=(int)Math.Round(36*scale);float s=(float)scale;
            using(var image=new Bitmap(size,size,System.Drawing.Imaging.PixelFormat.Format32bppPArgb))using(var g=Graphics.FromImage(image)){
                g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.Transparent);
                using(var thread=new Pen(Color.FromArgb(220,214,92,120),1.6f*s))g.DrawBezier(thread,1*s,1*s,10*s,4*s,6*s,14*s,15*s,17*s);
                var ball=new RectangleF(12*s,12*s,20*s,20*s);
                using(var fill=new SolidBrush(Color.FromArgb(255,255,158,178)))g.FillEllipse(fill,ball);
                using(var line=new Pen(Color.FromArgb(230,214,92,120),1.4f*s)){g.DrawArc(line,ball.X+3*s,ball.Y+2*s,14*s,16*s,200,140);g.DrawArc(line,ball.X+2*s,ball.Y+5*s,16*s,12*s,20,140);g.DrawLine(line,ball.X+5*s,ball.Y+3*s,ball.X+15*s,ball.Y+17*s);g.DrawEllipse(line,ball);}
                if(bitmap!=IntPtr.Zero)Native.DeleteObject(bitmap);bitmap=image.GetHbitmap(Color.FromArgb(0));
            }
            x=int.MinValue;
        }
        internal void ShowAt(Point cursor,double dpi){
            if(bitmap==IntPtr.Zero||Math.Abs(dpi-scale)>.01){scale=dpi;Render();}
            if(!IsHandleCreated)CreateHandle();
            int left=cursor.X+(int)(4*scale),top=cursor.Y+(int)(10*scale);
            if(left!=x||top!=y){
                x=left;y=top;
                IntPtr screen=Native.GetDC(IntPtr.Zero),dc=Native.CreateCompatibleDC(screen),old=Native.SelectObject(dc,bitmap);
                try{var pos=new Native.POINT(x,y);var sz=new Native.SIZE(size,size);var origin=new Native.POINT(0,0);var blend=new Native.BLEND{Op=0,Flags=0,Alpha=255,Format=1};Native.UpdateLayeredWindow(Handle,screen,ref pos,ref sz,dc,ref origin,0,ref blend,2);}
                finally{Native.SelectObject(dc,old);Native.DeleteDC(dc);Native.ReleaseDC(IntPtr.Zero,screen);}
            }
            if(!shown){Native.SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x53);shown=true;}
        }
        internal void HideToy(){if(shown&&IsHandleCreated)Native.SetWindowPos(Handle,IntPtr.Zero,0,0,0,0,0x93);shown=false;}
        protected override void Dispose(bool disposing){if(bitmap!=IntPtr.Zero){Native.DeleteObject(bitmap);bitmap=IntPtr.Zero;}base.Dispose(disposing);}
    }

    // The cushion: a click-and-drag, per-pixel transparent window sitting on the taskbar edge.
    internal sealed class BedWindow : Form {
        IntPtr bitmap=IntPtr.Zero;int w,h,x=int.MinValue,y=int.MinValue;double scale;Rectangle area;
        bool press;int grab;internal bool Dragging;
        internal event Action<int> Moved;internal event Action HideRequested;
        readonly ContextMenuStrip menu=new ContextMenuStrip();
        internal BedWindow(){
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;Size=new Size(1,1);Text="펫 캘린더 · 펫 침대";
            menu.Items.Add("침대 치우기",null,delegate{if(HideRequested!=null)HideRequested();});
        }
        protected override bool ShowWithoutActivation{get{return true;}}
        protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x80000|0x80|0x08000000;return p;}}
        void Render(){
            w=(int)Math.Round(150*scale);h=(int)Math.Round(42*scale);float s=(float)scale;
            using(var image=new Bitmap(w,h,System.Drawing.Imaging.PixelFormat.Format32bppPArgb))using(var g=Graphics.FromImage(image)){
                g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.Transparent);
                var body=new RectangleF(2*s,6*s,w-4*s,h-8*s);
                using(var shadow=new SolidBrush(Color.FromArgb(70,60,40,20)))g.FillEllipse(shadow,body.X+2*s,body.Y+4*s,body.Width-4*s,body.Height-2*s);
                using(var rim=new SolidBrush(Color.FromArgb(255,214,150,112)))g.FillEllipse(rim,body);
                var top=new RectangleF(body.X+8*s,body.Y+3*s,body.Width-16*s,body.Height*.62f);
                using(var cushion=new LinearGradientBrush(top,Color.FromArgb(255,255,226,196),Color.FromArgb(255,245,196,160),LinearGradientMode.Vertical))g.FillEllipse(cushion,top);
                using(var pen=new Pen(Color.FromArgb(160,190,120,86),1.2f*s){DashStyle=DashStyle.Dot})g.DrawEllipse(pen,top.X+4*s,top.Y+3*s,top.Width-8*s,top.Height-6*s);
                using(var edge=new Pen(Color.FromArgb(200,150,96,66),1.2f*s))g.DrawEllipse(edge,body);
                if(bitmap!=IntPtr.Zero)Native.DeleteObject(bitmap);bitmap=image.GetHbitmap(Color.FromArgb(0));
            }
            x=int.MinValue;
        }
        internal void Place(Rectangle workArea,double center,double dpi){
            if(Math.Abs(dpi-scale)>.01||bitmap==IntPtr.Zero){scale=dpi;Render();}
            area=workArea;if(Dragging)return;
            MoveTo((int)Math.Round(center)-w/2);
        }
        bool shown;
        void MoveTo(int left){
            left=Math.Max(area.Left,Math.Min(area.Right-w,left));int top=area.Bottom-(int)Math.Round(h*.62);
            if(!IsHandleCreated)CreateHandle();
            if(left!=x||top!=y){
                x=left;y=top;
                IntPtr screen=Native.GetDC(IntPtr.Zero),dc=Native.CreateCompatibleDC(screen),old=Native.SelectObject(dc,bitmap);
                try{var pos=new Native.POINT(x,y);var size=new Native.SIZE(w,h);var origin=new Native.POINT(0,0);var blend=new Native.BLEND{Op=0,Flags=0,Alpha=255,Format=1};Native.UpdateLayeredWindow(Handle,screen,ref pos,ref size,dc,ref origin,0,ref blend,2);}
                finally{Native.SelectObject(dc,old);Native.DeleteDC(dc);Native.ReleaseDC(IntPtr.Zero,screen);}
            }
            if(!shown){Native.SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x53);shown=true;}
        }
        public new int Width{get{return w;}}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;press=true;grab=Cursor.Position.X-x;Capture=true;}
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);Cursor=Cursors.SizeWE;if(!press)return;Dragging=true;MoveTo(Cursor.Position.X-grab);}
        protected override void OnMouseUp(MouseEventArgs e){
            base.OnMouseUp(e);
            if(e.Button==MouseButtons.Right){menu.Show(Cursor.Position);return;}
            if(!press)return;press=false;Capture=false;
            if(Dragging){Dragging=false;if(Moved!=null)Moved(x+w/2);}
        }
        protected override void WndProc(ref Message m){if(m.Msg==0x21){m.Result=new IntPtr(3);return;}base.WndProc(ref m);}
        protected override void Dispose(bool disposing){if(bitmap!=IntPtr.Zero){Native.DeleteObject(bitmap);bitmap=IntPtr.Zero;}if(disposing)menu.Dispose();base.Dispose(disposing);}
    }

    // The pet's daily diary, written in the evening and kept in pet-diary.json (latest 400 days).
    internal sealed class PetDiary {
        readonly string path;List<DiaryEntry> entries;
        internal PetDiary(string dataDir){
            path=Path.Combine(dataDir,"pet-diary.json");
            try{entries=File.Exists(path)?(Codec.Read<List<DiaryEntry>>(File.ReadAllText(path))??new List<DiaryEntry>()):new List<DiaryEntry>();}catch{entries=new List<DiaryEntry>();}
        }
        internal DiaryEntry[] Entries{get{return entries.OrderByDescending(e=>e.date,StringComparer.Ordinal).ToArray();}}
        static string Tail(string species){return species=="cat"?"냥":species=="dog"?"멍":"";}
        static string Short(string t){t=(t??"").Trim();return t.Length>18?t.Substring(0,17)+"…":t;}
        internal string Write(CalendarState state,string date,PetHost pet,int steps,int meets,bool bed,int levelUp){
            string s=Tail(pet.Kind.Id);
            var day=Rules.Occurrences(state.events,date,date).Where(o=>String.CompareOrdinal(o.startDate,date)<=0&&String.CompareOrdinal(o.endDate,date)>=0).ToList();
            var done=day.Where(o=>Rules.Done(o.@event,o.startDate)).ToList();
            var lines=new List<string>();
            if(day.Count==0)lines.Add("오늘 집사는 일정 없이 느긋한 하루를 보냈다"+s+".");
            else if(done.Count==0)lines.Add("오늘 일정이 "+day.Count+"개 있었는데 아직 못 끝냈다"+s+". 내일은 같이 해보자"+s+"!");
            else{
                string names=String.Join(", ",done.Take(2).Select(o=>"‘"+Short(o.@event.title)+"’"));
                lines.Add("집사가 "+names+(done.Count>2?" 등 ":" ")+"일정 "+done.Count+"개를 끝냈다"+s+"!"+(done.Count==day.Count?" 전부 해냈다"+s+" 🎉":""));
            }
            string me="나는 "+(steps>0?steps.ToString("N0")+"걸음 산책하고":"오늘 거의 안 움직이고");
            me+=meets>0?" 친구랑 "+meets+"번 인사했다"+s+".":" 낮잠을 잤다"+s+".";
            lines.Add(me);
            if(bed)lines.Add("침대에서 푹 잤다"+s+" 💤");
            if(levelUp>0)lines.Add("오늘부터 우리는 ‘"+PetLines.LevelName(levelUp)+"’ 사이다"+s+"! 💕");
            lines.Add(PetLines.Say(pet.Kind.Id,PetLines.Level(pet.Affinity),"diaryEnd"));
            string text=String.Join("\n",lines);
            string name=String.IsNullOrWhiteSpace(pet.PetName)?pet.PaletteName+" "+pet.Kind.Name:pet.PetName;
            entries.RemoveAll(e=>e.date==date&&e.pet==name);
            entries.Add(new DiaryEntry{date=date,pet=name,species=pet.Kind.Id,text=text});
            if(entries.Count>400)entries=entries.OrderByDescending(e=>e.date,StringComparer.Ordinal).Take(400).ToList();
            StateStore.Atomic(path,Codec.Json(entries),null);
            return text;
        }
    }
}
