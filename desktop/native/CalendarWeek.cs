using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace PetCalendarDesktop {
    // Week view: the grid area shows the selected date's week (Sunday first) as seven
    // columns over a 24-hour scale. All-day and multi-day schedules sit in a strip on top,
    // timed ones are blocks at their hours (side by side when they overlap). Empty time can be
    // clicked or dragged to add a schedule; blocks drag to other days like month chips.
    public sealed partial class CalendarView {
        internal bool weekView;
        readonly ScrollModel weekScroll=new ScrollModel();
        Rectangle weekTime;bool weekScrollPending=true;
        string weekDragKey;int weekDragAnchor,weekDragCurrent;bool weekDragActive;Point weekDragOrigin;int weekPressMinutes;
        Dictionary<string,List<Occurrence>> weekIndexCache;CalendarState weekIndexState;string weekIndexToken;

        internal void SetWeekView(bool value){
            if(weekView==value)return;weekView=value;prefs.Value.weekView=value;prefs.Save();
            weekScrollPending=true;focusId=null;Render();
        }
        internal string WeekStart(){var d=Rules.Date(selected);return Rules.Key(d.AddDays(-(int)d.DayOfWeek));}
        int HourHeight(){return P(m.Short?38:44);}
        int WeekGutter(){return P(52);}
        int WeekTop(){return weekTime.Y+P(7);}
        Rectangle WeekColumn(Rectangle area,int i){int x0=area.X+WeekGutter(),w=area.Width-WeekGutter();int a=x0+w*i/7,b=x0+w*(i+1)/7;return new Rectangle(a,area.Y,b-a,area.Height);}
        static bool Timed(Occurrence o){return o.@event.startTime!=""&&o.startDate==o.endDate;}
        static int Minutes(string hhmm){return int.Parse(hhmm.Substring(0,2),CultureInfo.InvariantCulture)*60+int.Parse(hhmm.Substring(3,2),CultureInfo.InvariantCulture);}
        static string Clock(int minutes){minutes=Math.Max(0,Math.Min(23*60+59,minutes));return (minutes/60).ToString("00")+":"+(minutes%60).ToString("00");}
        internal string WeekLabel(){
            var a=Rules.Date(WeekStart());var b=a.AddDays(6);
            return a.Month==b.Month?a.Month+"월 "+a.Day+"일 – "+b.Day+"일":a.Month+"월 "+a.Day+"일 – "+b.Month+"월 "+b.Day+"일";
        }
        string WeekCount(){
            var index=WeekIndex();int count=index.Values.SelectMany(l=>l.Select(o=>o.@event.id)).Distinct().Count();
            var a=Rules.Date(WeekStart());return a.ToString("M/d",CultureInfo.InvariantCulture)+" – "+a.AddDays(6).ToString("M/d",CultureInfo.InvariantCulture)+" · "+count+"개 일정";
        }
        internal Dictionary<string,List<Occurrence>> WeekIndex(){
            string from=WeekStart(),to=Rules.AddDays(from,6),token=from+"|"+filter+"|"+search;
            if(weekIndexCache!=null&&weekIndexState==state&&weekIndexToken==token)return weekIndexCache;
            var days=new Dictionary<string,List<Occurrence>>();for(int i=0;i<7;i++)days[Rules.AddDays(from,i)]=new List<Occurrence>();
            foreach(var o in Rules.Occurrences(state.events.Where(Matches),from,to,false)){
                var day=Rules.Date(String.CompareOrdinal(o.startDate,from)<0?from:o.startDate);var end=Rules.Date(o.endDate);var last=Rules.Date(to);
                for(;day<=end&&day<=last;day=day.AddDays(1))days[Rules.Key(day)].Add(o);
            }
            foreach(var list in days.Values)list.Sort(CompareDay);
            weekIndexCache=days;weekIndexState=state;weekIndexToken=token;return days;
        }
        // Minutes since midnight at a client point inside the time grid.
        int MinutesAt(Point p){int y=p.Y+pageScroll.Offset+weekScroll.Offset-WeekTop();return Math.Max(0,Math.Min(24*60-1,(int)((long)y*60/Math.Max(1,HourHeight()))));}
        static int Snap(int minutes){return minutes/30*30;}

        // Weekday header row in week view: "일 9", today's number in orange; click selects the day.
        void PaintWeekHeader(Graphics g,Rectangle wr){
            string from=WeekStart(),today=Rules.Key(DateTime.Today);
            string[] ko={"일","월","화","수","목","금","토"};var wf=F(11,700,"일");var nf=F(11,700,null,"mono");
            for(int d=0;d<7;d++){
                var col=WeekColumn(wr,d);string key=Rules.AddDays(from,d);bool isToday=key==today,isSel=key==selected;string id="wday-"+key;
                if(isSel)using(var b=new SolidBrush(Ui.C("#fff1d1")))g.FillRectangle(b,col.X,wr.Y+P(1),col.Width,wr.Height-P(2));
                else if(Hover(id))using(var b=new SolidBrush(Ui.C("#fff8e9")))g.FillRectangle(b,col.X,wr.Y+P(1),col.Width,wr.Height-P(2));
                Color ink=d==0||holidays.ContainsKey(key)?Ui.C("#cf6154"):d==6?Ui.C("#5d8db3"):DesktopTheme.Ink;
                string num=Rules.Date(key).Day.ToString(CultureInfo.InvariantCulture);int ww=Ui.Width(ko[d],wf),nw=Ui.Width(num,nf);
                int x=col.X+P(m.WkX);var numRect=new Rectangle(x+ww+P(6)-P(4),wr.Y+(wr.Height-P(20))/2,nw+P(8),P(20));
                Ui.Text(g,ko[d],wf,new Rectangle(x,wr.Y+P(1),ww+2,wr.Height-P(2)),ink,TextFormatFlags.VerticalCenter);
                if(isToday)Ui.Fill(g,numRect,P(10),DesktopTheme.Orange);
                Ui.Text(g,num,nf,numRect,isToday?Color.White:ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
                string k=key;var hit=AddHit(id,col,()=>JumpTo(k),FormatDay(key));if(hit!=null){hit.DropDate=key;hit.Focusable=false;}
            }
        }
        void PaintWeek(Graphics g){
            var area=gridRect;var index=WeekIndex();string from=WeekStart(),today=Rules.Key(DateTime.Today);
            int chipH=ChipHeight();var chipFont=F(m.ChipFont,600);var timeFont=F(10);int hourH=HourHeight();
            int maxAll=index.Values.Select(l=>l.Count(o=>!Timed(o))).DefaultIfEmpty(0).Max();int slots=Math.Min(maxAll,3);
            int stripH=slots==0?P(4):P(6)+slots*(chipH+P(3));
            var strip=new Rectangle(area.X,area.Y,area.Width,stripH);
            weekTime=new Rectangle(area.X,strip.Bottom+P(1),area.Width,Math.Max(0,area.Bottom-strip.Bottom-P(1)));
            // Column backgrounds and separators.
            using(var b=new SolidBrush(DesktopTheme.Card))g.FillRectangle(b,area);
            for(int d=0;d<7;d++){
                var col=WeekColumn(area,d);string key=Rules.AddDays(from,d);
                Color? bg=dropDate==key?Ui.C("#fff1d1"):key==selected?Ui.C("#fffaf0"):key==today?Ui.C("#fffbf3"):(Color?)null;
                if(bg!=null)using(var b=new SolidBrush(bg.Value))g.FillRectangle(b,col);
                using(var b=new SolidBrush(DesktopTheme.Line))g.FillRectangle(b,col.X,area.Y,P(1),area.Height);
            }
            using(var b=new SolidBrush(Ui.C("#d5c9b1")))g.FillRectangle(b,area.X,strip.Bottom,area.Width,P(1));
            // All-day strip.
            if(slots>0){
                var lf=F(9,700,"종일");Ui.Text(g,"종일",lf,new Rectangle(area.X,strip.Y,WeekGutter()-P(8),Math.Min(strip.Height,chipH+P(6))),Ui.C("#99886c"),TextFormatFlags.Right|TextFormatFlags.VerticalCenter);
                for(int d=0;d<7;d++){
                    var col=WeekColumn(area,d);string key=Rules.AddDays(from,d);var items=index[key].Where(o=>!Timed(o)).ToList();int y=strip.Y+P(3);
                    for(int k=0;k<slots&&k<items.Count;k++){
                        var r=new Rectangle(col.X+P(3),y,col.Width-P(5),chipH);
                        if(k==slots-1&&items.Count>slots){
                            string more="+"+(items.Count-k)+"개";var mf=F(10,600,more);string id="wmore-"+key;
                            Ui.Text(g,more,mf,new Rectangle(r.X+P(4),r.Y,r.Width,r.Height),Hover(id)?Ui.C("#bf4722"):Ui.C("#8c7450"),TextFormatFlags.VerticalCenter);
                            string k2=key;var mh=AddHit(id,r,()=>{JumpTo(k2);OpenDayList(k2);},"이 날짜의 일정 모두 보기");if(mh!=null)mh.DropDate=key;
                        }else PaintChip(g,items[k],key,r,chipFont,timeFont);
                        y+=chipH+P(3);
                    }
                }
            }
            // Time grid.
            weekScroll.Content=24*hourH+P(14);weekScroll.Viewport=weekTime.Height;weekScroll.Bounds=Page(weekTime);
            if(weekScrollPending&&weekTime.Height>0){
                // Start at 8 a.m., or earlier when this week has an earlier schedule.
                int first=index.Values.SelectMany(l=>l.Where(Timed)).Select(o=>Minutes(o.@event.startTime)).DefaultIfEmpty(8*60).Min();
                weekScroll.Offset=Math.Max(0,Math.Min(8*60,first-30)*hourH/60);weekScrollPending=false;
            }
            weekScroll.Clamp();
            Push(-weekScroll.Offset,weekTime);Apply(g);
            int top=WeekTop();var hf=F(9,400,null,"mono");
            using(var line=new Pen(Ui.C("#efe6d4"),s))using(var half=new Pen(Ui.C("#f6f0e3"),s){DashStyle=DashStyle.Dash}){
                for(int h=0;h<=24;h++){
                    int y=top+h*hourH;g.DrawLine(line,area.X+WeekGutter(),y,area.Right,y);
                    if(h<24)g.DrawLine(half,area.X+WeekGutter(),y+hourH/2,area.Right,y+hourH/2);
                    if(h>0&&h<24){string label=h.ToString("00")+":00";int lh=(int)Math.Ceiling(hf.GetHeight());Ui.Text(g,label,hf,new Rectangle(area.X,y-lh/2,WeekGutter()-P(8),lh),Ui.C("#a8987c"),TextFormatFlags.Right|TextFormatFlags.VerticalCenter);}
                }
            }
            var small=F(9);var titleFont=F(10,600);
            for(int d=0;d<7;d++){
                var col=WeekColumn(area,d);string key=Rules.AddDays(from,d);
                // Empty time: select the day, a second click (or a drag) adds a schedule at that time.
                var slot=AddHit("wcol-"+key,new Rectangle(col.X,top,col.Width,24*hourH),null,null,Page(gridRect));
                if(slot!=null){string k=key;slot.Click=()=>WeekSlotClick(k);slot.DropDate=key;slot.Focusable=false;slot.Cursor=Cursors.Default;}
                // Timed blocks, side by side where they overlap.
                var timed=index[key].Where(Timed).OrderBy(o=>Minutes(o.@event.startTime)).ThenBy(o=>o.@event.title,KoreanText).ToList();
                var lanes=new int[timed.Count];var widths=new int[timed.Count];var laneEnds=new List<int>();int clusterStart=0,clusterEnd=-1;
                Func<Occurrence,int> endOf=o=>{int a=Minutes(o.@event.startTime);int b=o.@event.endTime!=""?Minutes(o.@event.endTime):a+60;return Math.Max(b,a+20);};
                for(int i=0;i<timed.Count;i++){
                    int a=Minutes(timed[i].@event.startTime);
                    if(a>=clusterEnd){for(int j=clusterStart;j<i;j++)widths[j]=laneEnds.Count;laneEnds.Clear();clusterStart=i;}
                    int lane=laneEnds.FindIndex(e=>e<=a);if(lane<0){lane=laneEnds.Count;laneEnds.Add(0);}
                    laneEnds[lane]=endOf(timed[i]);lanes[i]=lane;clusterEnd=Math.Max(clusterEnd,laneEnds[lane]);
                }
                for(int j=clusterStart;j<timed.Count;j++)widths[j]=Math.Max(1,laneEnds.Count);
                for(int i=0;i<timed.Count;i++){
                    var o=timed[i];var e=o.@event;int a=Minutes(e.startTime),b=endOf(o);
                    int inner=col.Width-P(5),laneW=inner/widths[i];
                    var r=new Rectangle(col.X+P(3)+lanes[i]*laneW,top+a*hourH/60+P(1),Math.Max(P(8),laneW-P(2)),Math.Max(P(20),(b-a)*hourH/60-P(2)));
                    PaintWeekBlock(g,o,key,r,small,titleFont);
                }
                if(key==today){
                    var now=DateTime.Now;int y=top+(now.Hour*60+now.Minute)*hourH/60;
                    using(var b=new SolidBrush(Ui.C("#e0573a"))){g.FillRectangle(b,col.X,y-s,col.Width,2*s);g.FillEllipse(b,col.X-4*s,y-4*s,8*s,8*s);}
                }
                if(weekDragActive&&weekDragKey==key){
                    int lo=Math.Min(weekDragAnchor,weekDragCurrent),hi=Math.Max(weekDragAnchor,weekDragCurrent)+30;
                    var r=new Rectangle(col.X+P(2),top+lo*hourH/60,col.Width-P(3),(hi-lo)*hourH/60);
                    using(var b=new SolidBrush(Color.FromArgb(70,DesktopTheme.Orange)))g.FillRectangle(b,r);
                    using(var pen=new Pen(DesktopTheme.Orange,1.5f*s))g.DrawRectangle(pen,r.X,r.Y,r.Width-1,r.Height-1);
                    string label=Clock(lo)+" – "+Clock(Math.Min(hi,24*60-1));Ui.Text(g,label,small,new Rectangle(r.X+P(5),r.Y+P(2),r.Width-P(6),(int)Math.Ceiling(small.GetHeight())+P(2)),Ui.C("#8e442c"),TextFormatFlags.EndEllipsis);
                }
            }
            Pop(g);Ui.ScrollBar(g,Shifted(weekScroll),s);
        }
        void PaintWeekBlock(Graphics g,Occurrence o,string key,Rectangle r,Font small,Font titleFont){
            var e=o.@event;bool done=Rules.Done(e,o.startDate);string id="wk-"+key+"-"+o.key;bool hover=Hover(id)||Hover(id+"-check");
            bool dragging=drag!=null&&drag.Active&&drag.EventId==e.id&&drag.Start==o.startDate;
            Color light=Ui.C(CategoryLight(e.category)),bg=done?Ui.C(hover?"#e3eadc":"#edf1e8"):hover?Ui.Mix(light,Color.Black,.05f):light;
            if(dragging)bg=Ui.Fade(bg,DesktopTheme.Card,.45f);
            using(var outer=Ui.Round(r,4*s))using(var b=new SolidBrush(DesktopTheme.Category(e.category)))g.FillPath(b,outer);
            using(var inner=Ui.Corners(new RectangleF(r.X+3*s,r.Y,r.Width-3*s,r.Height),0,4*s,4*s,0))using(var b=new SolidBrush(bg))g.FillPath(b,inner);
            Color ink=done?Ui.C("#7b7060"):DesktopTheme.Ink;string time=e.startTime+(e.endTime!=""?"–"+e.endTime:"");
            int x=r.X+P(7),w=r.Right-P(3)-x;int sh=(int)Math.Ceiling(small.GetHeight()),th=(int)Math.Ceiling(titleFont.GetHeight());
            var tf=done?F(10,600,e.title,null,FontStyle.Strikeout):F(10,600,e.title);
            if(r.Height>=sh+th+P(6)){
                Ui.Text(g,time,small,new Rectangle(x,r.Y+P(3),w,sh),Ui.C("#766951"),TextFormatFlags.EndEllipsis);
                int ty0=r.Y+P(3)+sh;int lines=Math.Max(1,(r.Bottom-P(3)-ty0)/th);
                var wrapped=Ui.Wrap(e.title,tf,w);
                for(int i=0;i<Math.Min(lines,wrapped.Count);i++){bool last=i==lines-1&&wrapped.Count>lines;string text=last?String.Join(" ",wrapped.Skip(i)):wrapped[i];Ui.Text(g,text,tf,new Rectangle(x,ty0+i*th,w,th),ink,last?TextFormatFlags.EndEllipsis:TextFormatFlags.Default);}
            }else Ui.Text(g,e.startTime+" "+e.title,tf,new Rectangle(x,r.Y,w,r.Height),ink,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            string eid=e.id,occ=o.startDate,title=e.title,category=e.category;
            var hit=AddHit(id,r,()=>OpenEvent(eid,occ,false),e.title+" · "+time);
            if(hit!=null){hit.Cursor=Cursors.Hand;hit.DropDate=key;hit.Drag=()=>new DragInfo{EventId=eid,Start=occ,Day=key,Title=title,Category=category};}
            FocusRing(g,id,r,4*s);
        }
        void WeekSlotClick(string key){
            if(selected!=key){JumpTo(key);return;}
            int start=Snap(weekPressMinutes);OpenNewEvent(key,key,Clock(start),Clock(Math.Min(start+60,24*60-1)));
        }
        // Mouse handling for the time grid; the main handlers call these first.
        bool WeekMouseDown(MouseEventArgs e,Hit hit){
            if(!weekView||hit==null||!hit.Id.StartsWith("wcol-",StringComparison.Ordinal))return false;
            weekPressMinutes=MinutesAt(e.Location);
            if(saving||modal)return false;
            weekDragKey=hit.DropDate;weekDragAnchor=weekDragCurrent=Snap(weekPressMinutes);weekDragOrigin=e.Location;weekDragActive=false;Capture=true;return false;
        }
        bool WeekMouseMove(MouseEventArgs e){
            if(weekDragKey==null)return false;
            if(!weekDragActive&&Math.Abs(e.Y-weekDragOrigin.Y)>P(6))weekDragActive=true;
            if(!weekDragActive)return false;
            int now=Snap(MinutesAt(e.Location));if(now!=weekDragCurrent){weekDragCurrent=now;Invalidate(Page(gridRect));}
            Cursor=Cursors.SizeNS;return true;
        }
        bool WeekMouseUp(){
            if(weekDragKey==null)return false;string key=weekDragKey;bool active=weekDragActive;weekDragKey=null;weekDragActive=false;Capture=false;
            if(!active)return false;
            pressId=null;Cursor=Cursors.Default;Invalidate(Page(gridRect));
            int lo=Math.Min(weekDragAnchor,weekDragCurrent),hi=Math.Max(weekDragAnchor,weekDragCurrent)+30;
            OpenNewEvent(key,key,Clock(lo),Clock(Math.Min(hi,24*60-1)));return true;
        }
        // Month / week switch in the toolbar.
        int ViewSwitchWidth(){return P(2)+2*Math.Max(P(36),Ui.Width("주간",F(11,700,"주간"))+P(18));}
        void PaintViewSwitch(Graphics g,Rectangle r){
            Ui.Box(g,r,8*s,Ui.C("#f6f0e3"),Ui.C("#cdbfa5"),s);
            int half=(r.Width-P(2))/2;
            for(int i=0;i<2;i++){
                bool week=i==1,active=weekView==week;string id=week?"view-week":"view-month",label=week?"주간":"월간";
                var b=new Rectangle(r.X+P(1)+i*half,r.Y+P(1),half,r.Height-P(2));
                if(active)Ui.Box(g,new RectangleF(b.X+P(2),b.Y+P(2),b.Width-P(4),b.Height-P(4)),6*s,DesktopTheme.Card,Ui.C("#9d8d73"),s);
                else if(Hover(id))Ui.Fill(g,new RectangleF(b.X+P(2),b.Y+P(2),b.Width-P(4),b.Height-P(4)),6*s,Ui.C("#efe5d1"));
                Ui.Text(g,label,F(11,active?700:400,label),b,active?DesktopTheme.Ink:Ui.C("#7d6f58"),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
                bool v=week;AddHit(id,b,()=>SetWeekView(v),week?"주간 보기 · 시간대별로 한 주 보기":"월간 보기");FocusRing(g,id,b,6*s);
            }
        }
    }
}
