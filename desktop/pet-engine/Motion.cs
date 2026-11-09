using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace DesktopPet {
    public enum Pose { Idle, Walk, Groom, Sleep, Crouch, Rise, Fall, Land, Held, Pet }
    public sealed class Surface {
        public string Id,BodyId;
        public double AnchorX;
        public double Left, Right, Y;
        public bool Floor;
        public Surface(string id, double left, double right, double y, bool floor) { Id=id;BodyId=id;AnchorX=left; Left=left; Right=right; Y=y; Floor=floor; }
        public bool Supports(double x, double size) { double pad=Floor?0:Math.Min(size*.22,(Right-Left)/3); return x>=Left+pad && x<=Right-pad; }
    }
    public sealed class Motion {
        public readonly Species Profile;
        public double X,Y,Vx,Vy,Size=96,Age,Distance;
        public Pose State=Pose.Idle;
        public int Direction=1;
        public string Support;
        public List<Surface> Surfaces=new List<Surface>();
        public List<Rectangle> Screens=new List<Rectangle>();
        public bool Paused, Sleeping, Held;
        // Optional walking goal on the floor (the pet bed): walk there, then sleep or idle.
        public double? GoalX; public bool GoalSleep;
        // Separate from bed/meeting goals: only the selected toy pet uses this.
        public double? ToyTargetX;
        readonly Random random;
        double actionLeft=2, jumpVx, jumpVy, jumpDelay, cooldown;
        int desiredDirection=1;
        public double Unit { get { return Size/(96*Profile.Scale); } }
        public Motion(int seed) : this(seed,Species.Cat) {}
        public Motion(int seed,Species profile) { Profile=profile;Size=96*profile.Scale;random=new Random(seed); }
        public void SetPose(Pose pose) { if(State!=pose) { State=pose; Age=0; } }
        public void SetWorld(List<Surface> surfaces, List<Rectangle> screens) {
            Surface old=Surfaces.FirstOrDefault(s=>s.Id==Support);
            Surface next=null;
            if(old!=null)next=surfaces.FirstOrDefault(s=>s.BodyId==old.BodyId && s.Supports(X+s.AnchorX-old.AnchorX,Size));
            if(!Held && Support!=null && old!=null && next!=null) {
                X+=next.AnchorX-old.AnchorX;Y=next.Y;Support=next.Id;
            }
            Surfaces=surfaces;Screens=screens;
            if(!Held && Support!=null && next==null) {Support=null;Sleeping=false;SetPose(Pose.Fall);}
        }
        public void Reset(double x, Surface floor) {
            X=Math.Max(floor.Left+Size/2,Math.Min(floor.Right-Size/2,x)); Y=floor.Y;
            Vx=Vy=0; Support=floor.Id; desiredDirection=Direction; Sleeping=false; Held=false; jumpDelay=0; cooldown=1; SetPose(Pose.Idle); actionLeft=2;
        }
        public void Wake() { Sleeping=false; actionLeft=2; if(Support!=null)SetPose(Pose.Pet); }
        public void Nap(){if(!Held&&Support!=null){Vx=Vy=0;SetPose(Pose.Sleep);actionLeft=10;}}
        public void Sleep() { Sleeping=true; if(Support!=null) { Vx=Vy=0; SetPose(Pose.Sleep); } }
        public void Grab() { GoalX=null;ToyTargetX=null; Held=true; Sleeping=false; Support=null; Vx=Vy=0; jumpDelay=0; SetPose(Pose.Held); }
        public void Release(double vx,double vy) { Held=false;if(Math.Abs(vx)>1)Direction=vx<0?-1:1;desiredDirection=Direction; Vx=Clamp(vx,-420*Unit,420*Unit); Vy=Clamp(vy,-480*Unit,480*Unit); SetPose(Pose.Fall); }
        public static double Clamp(double v,double min,double max) { return Math.Max(min,Math.Min(max,v)); }
        public static bool Trajectory(double x,double y,double tx,double ty,double unit,out double vx,out double vy) {
            double rise=Math.Max(0,y-ty)+65*unit; vx=vy=0;
            if(rise>320*unit || Math.Abs(tx-x)>300*unit || ty-y>300*unit)return false;
            double g=1100*unit;
            vy=-Math.Sqrt(2*g*rise);
            double t=(-vy+Math.Sqrt(vy*vy+2*g*(ty-y)))/g;
            vx=(tx-x)/t;
            return !Double.IsNaN(vx) && Math.Abs(vx)<=320*unit;
        }
        public bool TryTrajectory(double tx,double ty,out double vx,out double vy) {
            vx=vy=0;
            if(!Profile.CanJump || Y-ty>Profile.MaxClimb*Unit || Math.Abs(tx-X)>300*Unit || ty-Y>300*Unit)return false;
            double rise=Math.Max(0,Y-ty)+Profile.JumpHeight*.6842105263*Unit;
            double g=1100*Unit;
            vy=-Math.Sqrt(2*g*rise);
            double time=(-vy+Math.Sqrt(vy*vy+2*g*(ty-Y)))/g;
            vx=(tx-X)/time;
            return !Double.IsNaN(vx) && Math.Abs(vx)<=320*Unit;
        }
        public void Jump() {
            if(Held || Support==null || Paused)return;
            Sleeping=false;
            if(!Profile.CanJump) {Wake();return;}
            // Prefer reachable elevated ledges, with a little room for the paws.
            var targets=Surfaces.Where(s=>s.Id!=Support && s.Y<Y-12*Unit && s.Right-s.Left>Size*.65).OrderBy(s=>Math.Abs((s.Left+s.Right)/2-X)).ToList();
            foreach(var target in targets) {
                double tx=Clamp(X+Direction*100*Unit,target.Left+Size*.3,target.Right-Size*.3),vx,vy;
                if(TryTrajectory(tx,target.Y,out vx,out vy) && HasHeadroom(tx,target.Y) && HasArcHeadroom(vx,vy)) { PrepareJump(vx,vy);return; }
            }
            double flatVy=-Math.Sqrt(2*1100*Unit*Profile.JumpHeight*Unit);
            if(HasArcHeadroom(Direction*90*Unit,flatVy))PrepareJump(Direction*90*Unit,flatVy);else Wake();
        }
        bool HasArcHeadroom(double vx,double vy) {
            double apex=-vy/(1100*Unit);
            for(int i=0;i<=12;i++) {
                double t=apex*i/12,x=X+vx*t,y=Y+vy*t+550*Unit*t*t;
                if(!Screens.Any(r=>x>=r.Left+Size*.25 && x<=r.Right-Size*.25 && y-Size*.88>=r.Top))return false;
            }
            return true;
        }
        bool HasHeadroom(double x,double y) { return Screens.Any(r=>x>=r.Left && x<r.Right && y-Size*.9>=r.Top); }
        public bool JumpToward(double x,double y){
            if(Held||Paused||Support==null||jumpDelay>0||!Profile.CanJump)return false;
            double vx,vy;if(!TryTrajectory(x,y,out vx,out vy)||!HasHeadroom(x,y)||!HasArcHeadroom(vx,vy))return false;
            Sleeping=false;PrepareJump(vx,vy);return true;
        }
        public void StopToy(){
            ToyTargetX=null;if(Held||Support==null)return;
            Vx=0;jumpDelay=0;if(!Sleeping){SetPose(Pose.Idle);actionLeft=.8;}
        }
        void FollowToy(double dt,double goal){
            var surface=Surfaces.FirstOrDefault(s=>s.Id==Support);if(surface==null){ToyTargetX=null;return;}
            double dx=goal-X;if(Math.Abs(dx)>10*Unit)Direction=desiredDirection=dx<0?-1:1;
            double pad=surface.Floor?0:Math.Min(Size*.22,(surface.Right-surface.Left)/3);
            double step=Math.Sign(dx)*Math.Min(120*Unit*dt,Math.Max(0,Math.Abs(dx)-10*Unit));
            double next=Clamp(X+step,surface.Left+pad,surface.Right-pad);
            if(!InsideScreen(next,Y))next=X;
            double moved=next-X;X=next;Distance+=Math.Abs(moved);Vx=dt>0?moved/dt:0;
            SetPose(Math.Abs(moved)>0?Pose.Walk:Pose.Idle);actionLeft=.8;
        }
        void PrepareJump(double vx,double vy) { if(Math.Abs(vx)>1)Direction=vx<0?-1:1; desiredDirection=Direction; jumpVx=vx; jumpVy=vy; jumpDelay=.22/Profile.Tempo; Vx=0; SetPose(Pose.Crouch); }
        public void Step(double dt,bool autonomous) {
            if(Paused)return;
            Age+=dt;
            if(Held)return; cooldown=Math.Max(0,cooldown-dt);
            if(Support==null) { Air(dt); return; }
            if(jumpDelay>0) {
                jumpDelay-=dt;
                if(jumpDelay<=0) { Support=null; Vx=jumpVx; Vy=jumpVy; SetPose(Pose.Rise); }
                return;
            }
            if(State==Pose.Land) { Vx*=Math.Exp(-16*dt); if(Age<.26/Profile.Tempo)return; SetPose(Sleeping?Pose.Sleep:Pose.Idle); actionLeft=.8; }
            if(Sleeping) { Vx=0; SetPose(Pose.Sleep);return; }
            if(!autonomous) { Vx=0;return; }
            if(ToyTargetX.HasValue){FollowToy(dt,ToyTargetX.Value);return;}
            if(GoalX.HasValue) {
                var here=Surfaces.FirstOrDefault(s=>s.Id==Support);double goal=GoalX.Value;
                bool reachable=here!=null&&here.Floor&&Surfaces.Any(s=>s.Floor&&goal>=s.Left&&goal<=s.Right&&Math.Abs(s.Y-Y)<2);
                if(!reachable)GoalX=null;
                else if(Math.Abs(goal-X)<=4*Unit) {Vx=0;GoalX=null;if(GoalSleep){Sleep();return;}SetPose(Pose.Idle);actionLeft=1.5;return;}
                else {desiredDirection=goal<X?-1:1;if(State!=Pose.Walk)SetPose(Pose.Walk);actionLeft=1;}
            }
            actionLeft-=dt;
            if(actionLeft<=0) {
                if(State==Pose.Groom || State==Pose.Sleep || State==Pose.Pet) {SetPose(Pose.Idle);actionLeft=.6;}
                else PickAction();
            }
            if(State!=Pose.Walk) { Vx=0;return; }
            // Finish braking before turning so the paws never walk opposite to the body velocity.
            if(desiredDirection!=Direction && Math.Abs(Vx)<2*Unit) {Direction=desiredDirection;Vx=0;}
            double target=desiredDirection!=Direction?0:Direction*Profile.Speed*Unit;
            if(actionLeft<.45)target*=Clamp(actionLeft/.45,0,1);
            Vx+=Clamp(target-Vx,-(Profile.Speed*4)*Unit*dt,(Profile.Speed*4)*Unit*dt);
            double next=X+Vx*dt;
            if(!InsideScreen(next,Y)) { Direction=-Direction; desiredDirection=Direction; Vx=0; actionLeft=.7; SetPose(Pose.Idle);return; }
            Distance+=Math.Abs(next-X); X=next;
            Surface surface=null;
            foreach(var s in Surfaces)if(s.Id==Support && s.Supports(X,Size)) {surface=s;break;}
            if(surface==null) {
                Surface floor=null;
                foreach(var s in Surfaces)if(s.Floor && s.Supports(X,Size) && Math.Abs(s.Y-Y)<2) {floor=s;break;}
                if(floor!=null)Support=floor.Id;
                else { Support=null; SetPose(Pose.Fall); }
            }
        }
        bool InsideScreen(double x,double y) {
            foreach(var r in Screens)if(x>=r.Left+Size*.25 && x<=r.Right-Size*.25 && y>r.Top && y<=r.Bottom+1)return true;
            foreach(var r in Screens) {
                if(x<r.Left || x>=r.Right || y<=r.Top || y>r.Bottom+1)continue;
                foreach(var q in Screens)if(q!=r && (q.Right==r.Left || q.Left==r.Right) && y>q.Top && y<=q.Bottom+1)return true;
            }
            return false;
        }
        void PickAction() {
            double roll=random.NextDouble();
            if(Profile==Species.RedPanda && roll<.15) {SetPose(Pose.Pet);actionLeft=3.6+random.NextDouble()*1.6;return;}
            double walking=Profile==Species.Capybara?.42:Profile==Species.Elephant?.48:.60;
            double jumping=Profile==Species.Squirrel?.22:Profile==Species.Capybara?.06:.10;
            if(roll<walking) {
                desiredDirection=random.NextDouble()<.35?-Direction:Direction;SetPose(Pose.Walk);
                actionLeft=(Profile==Species.Squirrel?1:2)+random.NextDouble()*(Profile==Species.Squirrel?3:5);
            }
            else if(roll<walking+.13) {SetPose(Pose.Groom);actionLeft=2.5+random.NextDouble()*3;}
            else if(roll<walking+.13+jumping && cooldown==0) {Jump();actionLeft=2;cooldown=Profile==Species.Squirrel?2:5;}
            else if(roll<.92) {SetPose(Pose.Sleep);actionLeft=8+random.NextDouble()*(Profile==Species.Capybara?22:12);}
            else {SetPose(Pose.Idle);actionLeft=1.5+random.NextDouble()*3;}
        }
        void Air(double dt) {
            double ox=X,oy=Y,g=1100*Unit;
            Vy+=g*dt;
            double nx=X+Vx*dt,ny=Y+Vy*dt;
            // Block travel through gaps between monitors and the outer desktop edge.
            if(!InsideScreen(nx,Math.Min(Y,Screens.Count==0?Y:Screens.Max(r=>r.Bottom)))) { nx=X;Vx=-Vx*.25;Direction=Vx<0?-1:1;desiredDirection=Direction; }
            Rectangle monitor=Screens.FirstOrDefault(r=>X>=r.Left && X<r.Right && Y>=r.Top && Y<=r.Bottom+Size);
            if(monitor!=Rectangle.Empty && ny<monitor.Top+Size*.88) { ny=monitor.Top+Size*.88; Vy=Math.Max(0,Vy); }
            Surface landed=null;double first=2,landX=nx;
            if(Vy>=0 && ny>oy)foreach(var s in Surfaces) {
                if(s.Y<oy-.5 || s.Y>ny)continue;
                double t=Clamp((s.Y-oy)/(ny-oy),0,1),cx=ox+(nx-ox)*t;
                if(t<first && s.Supports(cx,Size)) { first=t;landed=s;landX=cx; }
            }
            X=nx;Y=ny;
            if(landed!=null) { X=landX;Y=landed.Y;Support=landed.Id;Vx=Vy=0;SetPose(Pose.Land);actionLeft=.8;cooldown=2; }
            else SetPose(Vy<0?Pose.Rise:Pose.Fall);
            // Recovery when a display disappeared or a drag ended below a work area.
            if(Screens.Count>0 && Y>Screens.Max(r=>r.Bottom)+Size*2) {
                var floor=Surfaces.Where(s=>s.Floor).OrderBy(s=>Math.Abs((s.Left+s.Right)/2-X)).FirstOrDefault();
                if(floor!=null)Reset(X,floor);
            }
        }
        public int Frame {
            get {
                switch(State) {
                    case Pose.Walk:return 8+(int)(Distance/(Size*Profile.Stride))%8;
                    case Pose.Groom:return 16+(int)(Age*8*Profile.Tempo)%8;
                    case Pose.Sleep:return 24+(int)(Age*3*Profile.Tempo)%8;
                    case Pose.Crouch:return 32+Math.Min(2,(int)(Age*14*Profile.Tempo));
                    case Pose.Rise:if(!Profile.CanJump)return 40;return Profile.Rising[Math.Min(Profile.Rising.Length-1,(int)(Age*10*Profile.Tempo))];
                    case Pose.Fall:return Profile.Falling[(int)(Age*6*Profile.Tempo)%Profile.Falling.Length];
                    case Pose.Land:return 44+Math.Min(3,(int)(Age*16*Profile.Tempo));
                    case Pose.Held:return 48+(int)(Age*5*Profile.Tempo)%8;
                    case Pose.Pet:return 56+(int)(Age*8*Profile.Tempo)%8;
                    default:return (int)(Age*4*Profile.Tempo)%8;
                }
            }
        }
    }
}
