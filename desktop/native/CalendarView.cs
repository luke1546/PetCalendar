using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PetCalendarDesktop {
    // Responsive values from desktop/ui/styles.css. "Fit" is the
    // (min-width:1001px) and (min-height:620px) layout that keeps the whole
    // calendar in view; outside it the page scrolls like the web version.
    sealed class Metrics {
        public bool Fit,Short,Narrow,Wide,Single,Tight;
        public float ShellT,ShellX,ShellB,MastPadB,MastMarB,Brand,H1;
        public float CardGap,CardMarB,CardMinH,CardPadY,CardPadX,Stat,Deadline,H3,CardInner,StatIconGap;public bool CardIcons,DeadlineIcon;
        public float WsGap,SideW;
        public float TbT,TbX,TbB,MonthH2;public bool English;public float MonthGap,AddMargin,AddPadX;public bool AddKbd;
        public float FtX,FtB,ChipPadX,SearchW;
        public float WkY,WkX;
        public float CellT,CellB,CellX,DayTopH,DayTopMB,DayNum,DayNumCss,ChipFont,ChipLH,MorePadT,GridMinRow;
        public float BottomY,BottomX;
        public float SideGap,SecT,SecX,SecB;public bool Kicker;
        public float EmptyT,EmptyB,EmptyDraw,EmptyIcon,EmptyDrawMB;
        public float NotifY,NotifX,FootT,FootMinH,BannerMB,DayMax,TaskMax;
        public Metrics(float w,float h){
            // The window always fits on one screen (no page scrolling); narrower windows
            // slim the sidebar and gutters instead of stacking panels.
            Fit=true;Tight=w<1000;Short=h<=760;Narrow=w<=1250;Wide=w>=1600;Single=false;
            if(Fit){ShellT=Short?12:18;ShellX=Tight?16:28;ShellB=Short?8:10;MastPadB=Short?8:13;MastMarB=Short?11:16;Brand=Short?34:40;H1=Short?22:24;
                CardGap=Tight?12:17;CardMarB=Short?12:18;CardMinH=Short?60:78;CardPadY=Short?10:14;CardPadX=Short?16:20;Stat=Short?28:34;}
            else{ShellT=Narrow?24:(Wide?35:28);ShellX=Narrow?24:38;ShellB=Narrow?24:18;MastPadB=22;MastMarB=22;Brand=46;H1=26;
                CardGap=Single?15:19;CardMarB=27;CardMinH=104;CardPadY=Single?18:(Narrow?19:21);CardPadX=Single?16:(Narrow?19:24);Stat=Single?34:(Narrow?36:40);}
            Deadline=Narrow?22:25;H3=Single?12:(Narrow?13:14);CardInner=Narrow?8:16;StatIconGap=Narrow?5:14;CardIcons=!Tight;DeadlineIcon=!Narrow;
            WsGap=Fit?(Tight?14:20):(Narrow?18:22);SideW=Wide?330:(Tight?250:(Narrow?280:310));
            if(Fit){TbT=Short?10:15;TbX=Short?14:18;TbB=Short?8:12;FtX=Short?14:18;FtB=Short?8:12;WkY=Short?7:9;WkX=12;}
            else{TbT=Narrow?19:22;TbX=Narrow?15:22;TbB=Narrow?15:17;FtX=Narrow?15:22;FtB=Narrow?15:18;WkY=Narrow?11:12;WkX=Narrow?10:13;}
            MonthH2=Narrow?23:26;English=!Narrow;MonthGap=Narrow?8:12;AddMargin=Narrow?4:9;AddPadX=Narrow?10:14;AddKbd=!Narrow;ChipPadX=Narrow?6:8;SearchW=Tight?128:(Narrow?158:195);
            if(Fit){CellT=Short?3:6;CellB=Short?3:6;CellX=Short?4:5;DayTopH=Short?20:23;DayTopMB=Short?2:3;DayNum=Short?20:23;DayNumCss=Short?11:12;ChipFont=10;ChipLH=1.2f;MorePadT=3;}
            else{CellT=Narrow?7:9;CellB=Narrow?7:8;CellX=Narrow?4:6;DayTopH=24;DayTopMB=6;DayNum=23;DayNumCss=12;ChipFont=Narrow?9:10;ChipLH=1.25f;MorePadT=4;}
            GridMinRow=Wide?112:(Narrow&&!Single?100:105);
            BottomY=Fit?9:12;BottomX=Fit?14:17;
            SideGap=Fit?(Short?10:14):(Wide?22:19);
            if(Fit){SecT=Short?10:14;SecX=Short?14:16;SecB=Short?8:11;}else{SecT=Narrow?18:20;SecX=Narrow?16:19;SecB=Narrow?14:15;}
            Kicker=!Short;
            EmptyT=Fit?(Short?7:14):22;EmptyB=Fit?(Short?9:16):25;EmptyDraw=Short?33:49;EmptyIcon=Short?20:25;EmptyDrawMB=Short?8:12;
            NotifY=Fit?(Short?9:11):14;NotifX=Fit?(Short?12:14):15;FootT=Fit?(Short?8:13):21;FootMinH=Fit?(Short?20:25):0;BannerMB=Fit?12:20;
            DayMax=Single?350:335;TaskMax=Single?350:270;
        }
    }
    sealed class Hit {
        public string Id;public Rectangle Rect;public Action Click,DoubleClick;public string Tip;public bool Focusable=true;public Rectangle Repaint;
        public Cursor Cursor;public Func<object> Drag;public string DropDate;public ScrollModel Scroll;
    }
    public sealed partial class CalendarView : Control {
        readonly DesktopHost host;readonly StateStore store;
        internal CalendarState state;internal string selected,month,filter="all",search="";
        internal bool saving;public bool Saving{get{return saving;}}
        bool modal,maximized,ready;
        internal readonly UiPrefs prefs;
        readonly CueTextBox searchBox=new CueTextBox(),taskTitle=new CueTextBox();
        readonly DateField taskDue=new DateField{Compact=true,FontCss=9,TabStop=true,Surface=DesktopTheme.Card};
        readonly ToolTip tips=new ToolTip{InitialDelay=600,ReshowDelay=200,AutoPopDelay=8000};
        readonly Timer clock=new Timer{Interval=1000},searchTimer=new Timer{Interval=120};
        string saveStatus="이 PC에 자동 저장";bool saveFailed;string bannerText;bool bannerVisible;
        readonly ScrollModel pageScroll=new ScrollModel(),dayScroll=new ScrollModel(),taskScroll=new ScrollModel(),searchScroll=new ScrollModel();
        internal readonly Dictionary<string,int> limits=new Dictionary<string,int>{{"search",100},{"day",80},{"tasks",100},{"dday",100}};
        internal static readonly Dictionary<string,int> pageSizes=new Dictionary<string,int>{{"search",100},{"day",80},{"tasks",100},{"dday",100}};
        List<Hit> hits=new List<Hit>(),building=new List<Hit>();string hoverId,pressId,focusId,tipShown;
        Metrics m;string clockText="",todayText="",dayAtRender;
        internal Dictionary<string,string> holidays=new Dictionary<string,string>();
        float s=1;int ty;readonly Stack<KeyValuePair<int,Rectangle>> frames=new Stack<KeyValuePair<int,Rectangle>>();Rectangle clip;
        // Layout rectangles in page coordinates.
        Rectangle shell,masthead,summary,banner,workspace,calendarPanel,sidebar,dayPanel,taskPanel,notifCard,footer,gridRect,searchRect,bottomRect,weekdaysRect,toolbarRect,filterRect,searchBoxRect,dayList,taskList,taskEntry,taskBottom,progressRect;
        int rows,pageHeight;
        public event Action<string> WindowAction;
        internal Func<string,string,string,string,bool,System.Threading.Tasks.Task<ApiResponse>> ApiCall;
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
        public CalendarView(DesktopHost owner,StateStore calendar,UiPrefs ui){
            host=owner;store=calendar;prefs=ui;state=store.Snapshot;selected=Rules.Key(DateTime.Today);month=selected.Substring(0,7);dayAtRender=selected;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
            TabStop=true;BackColor=DesktopTheme.Paper;
            try{using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("petcalendar.holidays.json"))if(stream!=null)using(var reader=new System.IO.StreamReader(stream,System.Text.Encoding.UTF8)){var parsed=Codec.Object(reader.ReadToEnd());foreach(var pair in parsed)holidays[pair.Key]=pair.Value as string;}}catch{}
            searchBox.Placeholder="일정 검색";searchBox.MaxLength=200;searchBox.BackColor=Ui.C("#f6f0e3");searchBox.AccessibleName="일정 제목과 메모 검색";
            searchBox.TextChanged+=delegate{searchTimer.Stop();if(state.events.Length>=1000)searchTimer.Start();else ApplySearch();};
            searchBox.GotFocus+=delegate{Invalidate(Page(searchBoxRect));};searchBox.LostFocus+=delegate{Invalidate(Page(searchBoxRect));};
            searchBox.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Escape&&searchBox.Text.Length>0){searchBox.Text="";e.Handled=e.SuppressKeyPress=true;}};
            searchTimer.Tick+=delegate{searchTimer.Stop();ApplySearch();};
            taskTitle.Placeholder="작은 일부터 적어보세요";taskTitle.MaxLength=200;taskTitle.BackColor=Ui.C("#fffaf0");taskTitle.AccessibleName="새 할 일";
            taskTitle.GotFocus+=delegate{Invalidate(Page(taskEntry));};taskTitle.LostFocus+=delegate{Invalidate(Page(taskEntry));};
            taskTitle.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Enter){e.Handled=e.SuppressKeyPress=true;AddTask();}};
            taskDue.AccessibleName="할 일 마감일";
            Controls.Add(searchBox);Controls.Add(taskTitle);Controls.Add(taskDue);
            foreach(Control c in Controls)c.MouseWheel+=ChildWheel;
            clock.Tick+=delegate{Tick();};clock.Start();
            Toasts.Attach(this);
            Tick();
            if(state.updatedAt!=null){DateTime at;if(DateTime.TryParse(state.updatedAt,CultureInfo.InvariantCulture,DateTimeStyles.AdjustToUniversal|DateTimeStyles.AssumeUniversal,out at))saveStatus="이 PC에 저장됨 · "+at.ToLocalTime().ToString("yyyy. M. d.",CultureInfo.InvariantCulture);}
        }
        int P(float css){return (int)Math.Round(css*s);}
        Font F(float css,int weight=400,string text=null,string family=null,FontStyle extra=FontStyle.Regular){return Ui.Font(s,css,weight,family,text,extra);}
        float LH(float css,int weight=400){return F(css,weight).GetHeight();}
        float Css(float px){return px+(1+DesktopTheme.TextScale)*4f/3f;}
        internal Rectangle ViewportScreen(){return RectangleToScreen(ClientRectangle);}
        Rectangle Page(Rectangle r){r.Offset(0,-pageScroll.Offset);return r;}
        public void SetMaximized(bool value){maximized=value;Invalidate();}
        internal void SetModal(bool value){modal=value;if(!value)OnDialogsClosed();Invalidate();}
        void ChildWheel(object sender,MouseEventArgs e){var h=e as HandledMouseEventArgs;if(h!=null&&h.Handled)return;OnMouseWheel(new MouseEventArgs(e.Button,e.Clicks,PointToClient(Cursor.Position).X,PointToClient(Cursor.Position).Y,e.Delta));if(h!=null)h.Handled=true;}
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Relayout();}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);Relayout();}
        protected override void OnDpiChangedAfterParent(EventArgs e){base.OnDpiChangedAfterParent(e);Relayout();}
        internal void TextScaleChanged(){Relayout();Toasts.Layout();}
        void Tick(){
            var now=DateTime.Now;string date=now.ToString("yyyy년 M월 d일 dddd",Rules.Korean),time=now.ToString("HH:mm",CultureInfo.InvariantCulture);
            if(date!=todayText||time!=clockText){todayText=date;clockText=time;Invalidate(Page(masthead));}
            string day=Rules.Key(DateTime.Today);if(day!=dayAtRender){dayAtRender=day;Render();}
        }
        // ---------- Layout ----------
        bool paintAll=true;int paintedOffset=-1;List<KeyValuePair<string,List<Hit>>> sectionHits=new List<KeyValuePair<string,List<Hit>>>();
        void Relayout(){using(PerfProbe.T("layout"))RelayoutCore();}
        void RelayoutCore(){
            if(!IsHandleCreated||Width<=0||Height<=0)return;paintAll=true;
            s=DeviceDpi/96f;m=new Metrics(Width/s,Height/s);
            int x0=0,width=Width;if(!m.Fit){pageScroll.Bounds=ClientRectangle;}
            int shellW=Math.Min(width,P(1540));int sx=(width-shellW)/2;
            shell=new Rectangle(sx+P(m.ShellX),P(m.ShellT),shellW-2*P(m.ShellX),0);
            int y=shell.Y;
            // Masthead: brand block vs. header actions, centered, then a 2px rule.
            float brandText=LH(11,700)+4*s+Css(m.H1)*1.3f*s;float stamp=LH(12)+4*s+F(20,400,null,"mono").GetHeight();
            int mastContent=(int)Math.Ceiling(Math.Max(Math.Max(m.Brand*s,brandText),Math.Max(stamp,36*s)));
            masthead=new Rectangle(shell.X,y,shell.Width,mastContent+P(m.MastPadB)+P(2));y=masthead.Bottom+P(m.MastMarB);
            int cardH=(int)Math.Max(P(m.CardMinH),SummaryContent()+2*P(m.CardPadY)+2*s);
            summary=new Rectangle(shell.X,y,shell.Width,cardH);y=summary.Bottom+P(m.CardMarB);
            if(bannerVisible){int bh=(int)Math.Ceiling(Math.Max(LH(12),36*s)+24*s+3*s);banner=new Rectangle(shell.X,y,shell.Width,bh);y=banner.Bottom+P(m.BannerMB);}else banner=Rectangle.Empty;
            int footH=(int)Math.Ceiling(Math.Max(P(m.FootMinH)-P(m.FootT),LH(10)))+P(m.FootT);
            var range=RangeForMonth();rows=range.Value/7;
            int sideW=m.Single?shell.Width:P(m.SideW),gap=P(m.WsGap);
            int calW=m.Single?shell.Width:shell.Width-sideW-gap;
            int toolbarH=P(m.TbT)+(int)Math.Ceiling(Math.Max(38*s,Css(m.MonthH2)*1.33f*s))+P(m.TbB);
            int filterH=(int)Math.Ceiling(Math.Max(30*s,LH(11)+16*s))+P(m.FtB);
            int weekH=(int)Math.Ceiling(LH(11,700))+2*P(m.WkY)+P(2);
            int bottomH=(int)Math.Ceiling(LH(11))+2*P(m.BottomY)+P(1);
            int chrome=P(2)+toolbarH+filterH+weekH+bottomH;
            int wsH;
            if(m.Fit){
                int total=Height-P(m.ShellB);wsH=Math.Max(P(200),total-footH-y);
                int gridH=wsH-chrome;int searchH=0;
                if(search.Length>0){searchH=Math.Min(SearchContentHeight(calW),(int)(wsH*0.28f));gridH-=searchH;}
                calendarPanel=new Rectangle(shell.X,y,calW,wsH);
                LayoutCalendarPanel(toolbarH,filterH,weekH,gridH,searchH,bottomH);
                sidebar=new Rectangle(calendarPanel.Right+gap,y,sideW,wsH);
                int notifH=NotificationHeight(sideW);int sg=P(m.SideGap);int avail=wsH-notifH-2*sg;
                int dayH=Math.Max(P(110),(int)(avail/2.1f)),taskH=Math.Max(P(155),avail-dayH);
                dayPanel=new Rectangle(sidebar.X,y,sideW,dayH);taskPanel=new Rectangle(sidebar.X,dayPanel.Bottom+sg,sideW,taskH);notifCard=new Rectangle(sidebar.X,taskPanel.Bottom+sg,sideW,notifH);
                footer=new Rectangle(shell.X,y+wsH,shell.Width,footH);pageHeight=Height;
            }else{
                int rowH=GridRowHeights(calW).Sum();int searchH=search.Length>0?Math.Min(SearchContentHeight(calW),P(330)):0;
                calendarPanel=new Rectangle(shell.X,y,calW,chrome+rowH+searchH);
                LayoutCalendarPanel(toolbarH,filterH,weekH,rowH,searchH,bottomH);
                int sg=P(m.SideGap);
                if(m.Single){
                    int colW=(shell.Width-sg)/2,top=calendarPanel.Bottom+gap;int dayH=DayPanelNatural(colW),taskH=TaskPanelNatural(colW);
                    dayPanel=new Rectangle(shell.X,top,colW,dayH);taskPanel=new Rectangle(shell.X+colW+sg,top,shell.Width-colW-sg,taskH);
                    int notifH=NotificationHeight(shell.Width);notifCard=new Rectangle(shell.X,Math.Max(dayPanel.Bottom,taskPanel.Bottom)+sg,shell.Width,notifH);
                    sidebar=Rectangle.Union(dayPanel,notifCard);wsH=notifCard.Bottom-y;
                }else{
                    sidebar=new Rectangle(calendarPanel.Right+gap,y,sideW,0);int dayH=DayPanelNatural(sideW),taskH=TaskPanelNatural(sideW),notifH=NotificationHeight(sideW);
                    dayPanel=new Rectangle(sidebar.X,y,sideW,dayH);taskPanel=new Rectangle(sidebar.X,dayPanel.Bottom+sg,sideW,taskH);notifCard=new Rectangle(sidebar.X,taskPanel.Bottom+sg,sideW,notifH);
                    wsH=Math.Max(calendarPanel.Height,notifCard.Bottom-y);
                }
                footer=new Rectangle(shell.X,y+wsH,shell.Width,footH);pageHeight=footer.Bottom+P(m.ShellB);
            }
            workspace=new Rectangle(shell.X,y,shell.Width,wsH);
            pageScroll.Content=m.Fit?Height:pageHeight;pageScroll.Viewport=Height;pageScroll.Bounds=ClientRectangle;pageScroll.Clamp();
            LayoutSidebar();
            PlaceChildren();Invalidate();
        }
        void LayoutCalendarPanel(int toolbarH,int filterH,int weekH,int gridH,int searchH,int bottomH){
            var p=calendarPanel;int y=p.Y+P(1);
            toolbarRect=new Rectangle(p.X+P(1),y,p.Width-P(2),toolbarH);y+=toolbarH;
            filterRect=new Rectangle(p.X+P(1),y,p.Width-P(2),filterH);y+=filterH;
            int searchH0=(int)Math.Ceiling(LH(11)+14*s+2*s);int sw=P(m.SearchW);
            searchBoxRect=new Rectangle(filterRect.Right-P(m.FtX)-sw,filterRect.Y+(filterH-P(m.FtB)-searchH0)/2,sw,searchH0);
            weekdaysRect=new Rectangle(p.X+P(1),y,p.Width-P(2),weekH);y+=weekH;
            gridRect=new Rectangle(p.X+P(1),y,p.Width-P(2),gridH);y+=gridH;
            searchRect=searchH>0?new Rectangle(p.X+P(1),y,p.Width-P(2),searchH):Rectangle.Empty;y+=searchH;
            bottomRect=new Rectangle(p.X+P(1),y,p.Width-P(2),bottomH);
            searchScroll.Bounds=searchRect;searchScroll.Viewport=searchRect.Height;
        }
        int SummaryContent(){float left=LH(9,700)+6*s+LH(m.H3,700);float right=Css(m.Stat)*s;return (int)Math.Ceiling(Math.Max(left,right));}
        int NotificationHeight(int width){
            bool enabled=state.settings.desktopPopups;float py=enabled?10:m.NotifY;
            float text=Css(11)*1.33f*s+(enabled?0:4*s+LH(11));return (int)Math.Ceiling(Math.Max(text,20*s)+2*py*s+2*s);
        }
        int SectionHeaderHeight(){float k=m.Kicker?LH(8,700)+6*s:0;float h2=LH(17,700);return (int)Math.Ceiling(Math.Max(k+h2,30*s)+(m.SecT+m.SecB)*s);}
        int TaskFormHeight(){float entry=(float)Math.Ceiling(Math.Max(24*s,LH(11))+14*s+2*s);float bottom=Math.Max(LH(9),20*s)+18*s;return (int)Math.Ceiling(entry+bottom+6*s);}
        int DayPanelNatural(int width){int content=DayContentHeight(width-P(32));return P(2)+SectionHeaderHeight()+Math.Min(P(m.DayMax),content+P(m.Fit?12:17));}
        int TaskPanelNatural(int width){int content=TasksContentHeight(width-P(32));return P(2)+SectionHeaderHeight()+TaskFormHeight()+Math.Min(P(m.TaskMax),content+P(18));}
        void LayoutSidebar(){
            int head=SectionHeaderHeight();
            dayList=new Rectangle(dayPanel.X+P(1),dayPanel.Y+P(1)+head,dayPanel.Width-P(2),Math.Max(0,dayPanel.Bottom-P(1)-(dayPanel.Y+P(1)+head)));
            dayScroll.Bounds=dayList;
            int y=taskPanel.Y+P(1)+head;int entryH=(int)Math.Ceiling(Math.Max(24*s,LH(11))+14*s+2*s);
            taskEntry=new Rectangle(taskPanel.X+P(17),y,taskPanel.Width-P(34),entryH);y+=entryH;
            int bottomH=(int)Math.Ceiling(Math.Max(LH(9),20*s)+18*s);taskBottom=new Rectangle(taskEntry.X,y,taskEntry.Width,bottomH);y+=bottomH;
            progressRect=new Rectangle(taskPanel.X+P(17),y+P(2),taskPanel.Width-P(34),P(4));y+=P(6);
            taskList=new Rectangle(taskPanel.X+P(1),y,taskPanel.Width-P(2),Math.Max(0,taskPanel.Bottom-P(1)-y));taskScroll.Bounds=taskList;
        }
        void PlaceChildren(){
            float sc=s;int off=pageScroll.Offset;
            searchBox.Font=F(11);var sb=searchBoxRect;int ih=searchBox.PreferredHeight;int left=sb.X+P(10)+P(15)+P(6),right=sb.Right-P(10)-KbdWidth("/",9)-P(6);
            searchBox.SetBounds(left,sb.Y+(sb.Height-ih)/2-off,Math.Max(1,right-left),ih);
            taskTitle.Font=F(11);ih=taskTitle.PreferredHeight;var te=taskEntry;taskTitle.SetBounds(te.X+P(9),te.Y+(te.Height-ih)/2-off,Math.Max(1,te.Width-P(9)-P(8)-P(24)-P(5)),ih);
            taskDue.FontCss=9;int labelW=Ui.Width("마감일",F(9))+P(3);int dueH=(int)Math.Ceiling(LH(9)+4*s);
            taskDue.SetBounds(taskBottom.X+labelW,taskBottom.Y+(taskBottom.Height-dueH)/2-off,Math.Min(P(105),taskDue.ContentWidth()),dueH);
            bool fitVisible=true;searchBox.Visible=taskTitle.Visible=taskDue.Visible=fitVisible;
        }
        int[] GridRowHeights(int width){
            var heights=new int[rows];var range=RangeForMonth();var index=MonthIndex();int cellW=width/7;
            for(int r=0;r<rows;r++){int h=P(m.GridMinRow);for(int c=0;c<7;c++){string key=Rules.AddDays(range.Key,r*7+c);h=Math.Max(h,CellNatural(key,index));}heights[r]=h;}
            return heights;
        }
        int ChipHeight(){return (int)Math.Ceiling(Math.Max(Css(m.ChipFont)*m.ChipLH*s,12*s)+6*s);}
        int CellNatural(string key,Dictionary<string,List<Occurrence>> index){
            List<Occurrence> items;index.TryGetValue(key,out items);int count=items==null?0:items.Count;bool holiday=holidays.ContainsKey(key);
            int fontSpace=0;int slots=Math.Max(0,3-(holiday?1:0)-fontSpace);
            int h=P(m.CellT)+P(m.DayTopH)+P(m.DayTopMB)+P(m.CellB);if(holiday)h+=(int)Math.Ceiling(Css(10)*1.3f*s+2*s);
            h+=Math.Min(count,slots)*(ChipHeight()+P(3));if(count>slots)h+=(int)Math.Ceiling(Css(11)*m.ChipLH*s+m.MorePadT*s);return h;
        }
        // ---------- Painting ----------
        void Push(int dy,Rectangle area){frames.Push(new KeyValuePair<int,Rectangle>(ty,clip));var a=area;a.Offset(0,ty);clip=Rectangle.Intersect(clip,a);ty+=dy;}
        void Pop(Graphics g){var f=frames.Pop();ty=f.Key;clip=f.Value;g.ResetTransform();g.TranslateTransform(-bx,-by);g.TranslateTransform(0,ty);g.SetClip(clip);}
        void Apply(Graphics g){g.ResetTransform();g.TranslateTransform(-bx,-by);g.SetClip(clip);g.TranslateTransform(0,ty);}
        Hit AddHit(string id,Rectangle r,Action click,string tip=null,Rectangle? repaint=null){
            var c=r;c.Offset(0,ty);c.Intersect(clip);if(c.Width<=0||c.Height<=0)return null;
            var rp=repaint??r;rp.Offset(0,ty);
            var h=new Hit{Id=id,Rect=c,Click=click,Tip=tip,Repaint=rp};building.Add(h);return h;
        }
        bool Hover(string id){return hoverId==id&&!modal;}
        bool Pressed(string id){return pressId==id&&hoverId==id;}
        void FocusRing(Graphics g,string id,RectangleF r,float radius){if(focusId!=id||!Focused||!ShowFocusCues)return;var ring=r;ring.Inflate(4.5f*s,4.5f*s);using(var pen=new Pen(DesktopTheme.Orange,3*s))using(var p=Ui.Round(ring,radius+4*s))g.DrawPath(pen,p);}
        protected override void OnPaint(PaintEventArgs e){
            if(m==null)Relayout();if(m==null)return;using(PerfProbe.T("calpaint"))PaintAll(e.Graphics,e.ClipRectangle);}
        // ---------- Section cache ----------
        // Text is the expensive part of a paint (GDI DrawText, ~0.1 ms per Korean run), so
        // each section is drawn once into an offscreen DIB and blitted while its inputs are
        // unchanged: selecting a date redraws the grid and day panel, not the masthead,
        // summary cards and task list. A signature lists every input a section reads.
        sealed class SectionCache{public string Sig;public DibSurface Dib;public Rectangle Area;public List<Hit> Hits;}
        readonly Dictionary<string,SectionCache> caches=new Dictionary<string,SectionCache>();
        int bx,by,dataStamp;CalendarState stampedState;
        string Interactive(SectionCache c){
            if(c==null||c.Hits==null)return "new";string h="",p="",f="";
            if(!modal)foreach(var hit in c.Hits){if(hit.Id==hoverId)h=hoverId;if(hit.Id==pressId)p=pressId;if(hit.Id==focusId)f=focusId;}
            return h+"|"+p+"|"+(f!=""&&Focused&&ShowFocusCues?f:"");
        }
        string SectionSig(string name,SectionCache c){
            if(drag!=null)return null;
            string today=Rules.Key(DateTime.Today);
            string common=s+"|"+DesktopTheme.TextScale+DesktopTheme.FontKey+"|"+Width+"x"+Height+"|"+pageScroll.Offset+"|"+Interactive(c)+"|";
            switch(name){
                case "masthead":return common+todayText+"|"+clockText+"|"+maximized;
                case "summary":return common+dataStamp+"|"+today;
                case "banner":return common+bannerText;
                case "calendar":return common+dataStamp+"|"+today+"|"+month+"|"+filter+"|"+search+"|"+limits["search"]+"|"+searchBox.Focused+"|"+searchScroll.Offset+"|"+searchScroll.Hover+"|"+searchScroll.Dragging+"|"+dropDate;
                case "day":return common+dataStamp+"|"+today+"|"+selected+"|"+filter+"|"+search+"|"+limits["day"]+"|"+dayScroll.Offset+"|"+dayScroll.Hover+"|"+dayScroll.Dragging;
                case "task":if((DateTime.UtcNow-highlight).TotalSeconds<2.2)return null;return common+dataStamp+"|"+today+"|"+limits["tasks"]+"|"+taskScroll.Offset+"|"+taskScroll.Hover+"|"+taskScroll.Dragging+"|"+taskTitle.Focused+"|"+saving;
                case "notif":return common+dataStamp;
                // The footer touches the panels above it (and is cheap), so it is drawn directly.
            }
            return null;
        }
        void DropCaches(){foreach(var c in caches.Values)if(c.Dib!=null)c.Dib.Dispose();caches.Clear();foreach(var c in cells.Values)if(c.Dib!=null)c.Dib.Dispose();cells.Clear();}
        // Only sections touching the dirty region are drawn (the OS clips the rest); the
        // others keep the hit targets from their last paint. Layout/scroll changes repaint all.
        void PaintAll(Graphics g,Rectangle dirty){
            g.Clear(DesktopTheme.Paper);Ui.Prepare(g);g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Summary();
            bool full=paintAll||drag!=null||pageScroll.Offset!=paintedOffset||dirty.Contains(ClientRectangle);
            paintAll=false;paintedOffset=pageScroll.Offset;var previous=sectionHits;sectionHits=new List<KeyValuePair<string,List<Hit>>>();
            Action<string,Rectangle,Action<Graphics>> section=(name,area,paint)=>{
                var r=Page(area);r.Inflate(P(12),P(12));
                if(!full&&!r.IntersectsWith(dirty)){foreach(var pair in previous)if(pair.Key==name){sectionHits.Add(pair);return;}}
                SectionCache c;caches.TryGetValue(name,out c);
                var cap=Page(area);cap.Inflate(P(5),P(5));cap.Intersect(ClientRectangle);
                string sig=cap.Width>0&&cap.Height>0?SectionSig(name,c):null;
                if(sig==null){clip=ClientRectangle;ty=-pageScroll.Offset;frames.Clear();Apply(g);building=new List<Hit>();using(PerfProbe.T("sec:"+name))paint(g);sectionHits.Add(new KeyValuePair<string,List<Hit>>(name,building));return;}
                if(c==null||c.Sig!=sig||c.Area!=cap){
                    if(c==null){c=new SectionCache();caches[name]=c;}
                    if(c.Dib==null||c.Dib.Width!=cap.Width||c.Dib.Height!=cap.Height){if(c.Dib!=null)c.Dib.Dispose();c.Dib=new DibSurface(cap.Width,cap.Height);}
                    using(var dg=Graphics.FromHdc(c.Dib.Dc)){
                        dg.Clear(DesktopTheme.Paper);Ui.Prepare(dg);dg.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                        bx=cap.X;by=cap.Y;clip=ClientRectangle;ty=-pageScroll.Offset;frames.Clear();Apply(dg);building=new List<Hit>();
                        try{using(PerfProbe.T("sec:"+name))paint(dg);}finally{bx=by=0;}
                    }
                    c.Hits=building;c.Area=cap;c.Sig=SectionSig(name,c);
                }
                c.Dib.BlitTo(g,cap.Location);
                sectionHits.Add(new KeyValuePair<string,List<Hit>>(name,c.Hits));
            };
            section("masthead",masthead,PaintMasthead);section("summary",summary,PaintSummary);
            if(bannerVisible)section("banner",banner,PaintBanner);
            section("calendar",calendarPanel,PaintCalendarPanel);section("grid",gridRect,PaintGrid);section("day",dayPanel,PaintDayPanel);section("task",taskPanel,PaintTaskPanel);
            section("notif",notifCard,PaintNotification);section("footer",footer,PaintFooter);
            clip=ClientRectangle;ty=-pageScroll.Offset;frames.Clear();Apply(g);building=new List<Hit>();if(drag!=null)PaintDragGhost(g);
            g.ResetTransform();g.ResetClip();if(!m.Fit)Ui.ScrollBar(g,pageScroll,s);
            var all=new List<Hit>();foreach(var pair in sectionHits)all.AddRange(pair.Value);all.AddRange(building);hits=all;
        }
        void PaintMasthead(Graphics g){
            var r=masthead;int contentH=r.Height-P(m.MastPadB)-P(2);int cy=r.Y+contentH/2;
            // Brand
            float img=m.Brand*s;Ui.Brand(g,new RectangleF(r.X,cy-img/2,img,img),-5);
            int tx=r.X+(int)Math.Ceiling(img)+P(13);var eyebrow=F(11,700,"일정과 함께하는 작은 친구");int eh=(int)Math.Ceiling(eyebrow.GetHeight());float h1px=Css(m.H1)*s;int h1h=(int)Math.Ceiling(h1px*1.3f);
            int blockH=eh+P(4)+h1h;int by=cy-blockH/2;
            Ui.Spaced(g,"일정과 함께하는 작은 친구",eyebrow,tx,by,eh,Ui.C("#786b59"),0.3f*s);
            var h1=F(m.H1,800,"펫 캘린더");Ui.Spaced(g,"펫 캘린더",h1,tx,by+eh+P(4),h1h,DesktopTheme.Ink,-1.2f*s);
            float titleW=Ui.SpacedWidth("펫 캘린더",h1,-1.2f*s);float paw=h1px*0.78f;Ui.Paw(g,new RectangleF(tx+titleW-1.2f*s+h1px*0.08f+Ui.SpacedWidth(" ",h1,0),by+eh+P(4)+(h1h-paw)/2f-h1px*0.02f,paw,paw));
            var drag=new Rectangle(r.X,r.Y,r.Width,contentH);
            // Header actions, right to left.
            int x=r.Right;
            var win=new[]{"close","maximize","minimize"};int bw=P(30),bh=P(32);
            for(int i=0;i<3;i++){x-=bw;var b=new Rectangle(x,cy-bh/2,bw,bh);string id="win-"+win[i];bool hover=Hover(id),press=Pressed(id);
                if(hover||press)Ui.Fill(g,b,6*s,i==0&&!press?Ui.C("#ffd5ca"):Ui.C(press?"#e8d7b7":"#f1e2c8"));
                string icon=win[i]=="maximize"&&maximized?"restore":win[i];Ui.Icon(g,icon,new RectangleF(b.X+7*s,b.Y+8*s,16*s,16*s),i==0&&hover?Ui.C("#a44130"):DesktopTheme.Ink,1.7f);
                string action=win[i];AddHit(id,b,()=>{if(WindowAction!=null)WindowAction(action);},win[i]=="close"?"트레이로 숨기기":win[i]=="maximize"?(maximized?"이전 크기로":"최대화"):"최소화");
                x-=i<2?P(2):0;}
            x-=P(8);using(var b=new SolidBrush(DesktopTheme.Line))g.FillRectangle(b,x,cy-bh/2,P(1),bh);x-=P(16);
            int ib=P(36);x-=ib;var settings=new Rectangle(x,cy-ib/2,ib,ib);ButtonBox(g,"settings",settings,false);Ui.Icon(g,"settings",new RectangleF(settings.X+8*s,settings.Y+8*s,20*s,20*s),DesktopTheme.Ink);
            AddHit("settings",settings,OpenSettings,"설정 및 백업");FocusRing(g,"settings",settings,9*s);
            x-=P(16)+ib;var help=new Rectangle(x,cy-ib/2,ib,ib);ButtonBox(g,"tour-help",help,false);Ui.Text(g,"?",F(16,800,"?"),help,DesktopTheme.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            AddHit("tour-help",help,ShowQuickTour,"퀵투어 다시 보기");FocusRing(g,"tour-help",help,9*s);
            x-=P(16);var dateFont=F(12,400,todayText);var clockFont=F(20,400,null,"mono");int dh=(int)Math.Ceiling(dateFont.GetHeight()),ch=(int)Math.Ceiling(clockFont.GetHeight());int sy=cy-(dh+P(4)+ch)/2;
            int dw=Ui.Width(todayText,dateFont);Ui.Text(g,todayText,dateFont,new Rectangle(x-dw-2,sy,dw+2,dh),Ui.C("#71644f"),TextFormatFlags.Right|TextFormatFlags.VerticalCenter);
            float cw=Ui.SpacedWidth(clockText,clockFont,1.5f*s)+1.5f*s;Ui.Spaced(g,clockText,clockFont,x-cw,sy+dh+P(4),ch,DesktopTheme.Ink,1.5f*s);
            using(var b=new SolidBrush(DesktopTheme.Ink))g.FillRectangle(b,r.X,r.Bottom-P(2),r.Width,P(2));
        }
        void ButtonBox(Graphics g,string id,Rectangle r,bool primary,Color? fill=null,Color? border=null,float radius=9){
            bool hover=Hover(id),press=Pressed(id);var box=new RectangleF(r.X,r.Y,r.Width,r.Height);
            if(primary){float lift=press?2*s:hover?-1*s:0,shadow=press?0:hover?3*s:2*s;box.Offset(lift,lift);if(shadow>0){var sh=box;sh.Offset(shadow,shadow);Ui.Fill(g,sh,radius*s,Ui.C("#725849"));}Ui.Box(g,box,radius*s,hover?Ui.C("#ff865f"):DesktopTheme.Orange,Ui.C("#725849"),1.5f*s);return;}
            if(press)box.Offset(s,s);Ui.Box(g,box,radius*s,hover?Ui.C("#f9ecd6"):(fill??DesktopTheme.Card),border??Ui.C("#9d8d73"),1.5f*s);
        }
        int KbdWidth(string text,float css){return Math.Max(P(css<10?15:19),Ui.Width(text,F(css,400,null,"mono"))+P(10));}
        void Kbd(Graphics g,string text,Rectangle r,float css,bool primary=false){Ui.Box(g,r,4*s,primary?Ui.Mix(DesktopTheme.Orange,Color.White,.31f):Ui.Mix(Ui.C("#f6f0e3"),Color.White,.31f),primary?Ui.Mix(DesktopTheme.Orange,Ui.C("#8e442c"),.36f):Ui.C("#d6c9b0"),s);Ui.Text(g,text,F(css,400,null,"mono"),r,Ui.C(primary?"#51311f":"#6e604d"),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
        void PaintSummary(Graphics g){
            int gap=P(m.CardGap);float total=summary.Width-2*gap;int w1=(int)(total/3.12f),w3=(int)total-2*w1;
            var cards=new[]{new Rectangle(summary.X,summary.Y,w1,summary.Height),new Rectangle(summary.X+w1+gap,summary.Y,w1,summary.Height),new Rectangle(summary.X+2*w1+2*gap,summary.Y,w3,summary.Height)};
            string today=Rules.Key(DateTime.Today);var target=deadlineTarget;
            string[] ids={"today-card","task-card","deadline-card"};
            for(int i=0;i<3;i++){
                var r=cards[i];bool hover=Hover(ids[i]);var box=new RectangleF(r.X,r.Y,r.Width,r.Height);if(hover)box.Offset(-1*s,-2*s);
                Color fill=i==0?Ui.C("#ffdc72"):i==1?Ui.C("#c5e5b6"):target==null?Ui.C("#fff9ee"):Ui.C("#ffc6d4");
                var shadow=box;float so=hover?3*s:2*s;shadow.Offset(so,so);Ui.Fill(g,shadow,15*s,DesktopTheme.Frame);Ui.Box(g,box,15*s,fill,DesktopTheme.Frame,s);
                int px=P(m.CardPadX)+P(1),inner=(int)box.Width-2*px;int x=(int)box.X+px;int cy=(int)(box.Y+box.Height/2);
                // Right: stat value.
                string value=i==0?TodayCount().ToString():i==1?state.tasks.Count(t=>!t.done).ToString():(target==null?"선택":Rules.Dday(target.startDate,today));
                string unit=i==0?"건":i==1?"개":null;bool showIcon=m.CardIcons&&(i<2||m.DeadlineIcon);
                var valueFont=i==2?F(m.Deadline,800,value):F(m.Stat,800,value);float spacing=i==2?-0.7f*s:-2f*s;var unitFont=F(12);
                float vw=Ui.SpacedWidth(value,valueFont,spacing)+spacing,uw=unit==null?0:Ui.Width(unit,unitFont),iw=showIcon?29*s:0;float statGap=i==2?4*s:6*s;
                float statW=vw+(unit!=null?statGap+uw:0)+(showIcon?m.StatIconGap*s+statGap+iw:0);
                float sx=box.Right-px-statW;int vh=(int)Math.Ceiling(valueFont.GetHeight());float baseline=cy+Ui.Ascent(valueFont)-vh/2f;
                Color valueInk=i==2&&target==null?Ui.C("#837866"):DesktopTheme.Ink;
                Ui.Spaced(g,value,valueFont,sx,cy-vh/2,vh,valueInk,spacing);float cx=sx+vw;
                if(unit!=null){cx+=statGap;int uh=(int)Math.Ceiling(unitFont.GetHeight());Ui.Text(g,unit,unitFont,new Rectangle((int)cx,(int)(baseline-Ui.Ascent(unitFont)),(int)uw+3,uh),DesktopTheme.Ink);cx+=uw;}
                if(showIcon){cx+=statGap+m.StatIconGap*s;Ui.Icon(g,i==0?"calendar":i==1?"check":"flag",new RectangleF(cx,cy-iw/2,iw,iw),Ui.Fade(DesktopTheme.Ink,fill,.65f),1.6f);}
                // Left: kicker + h3.
                int leftW=(int)(sx-x-m.CardInner*s);string kicker=i==0?"TODAY":i==2?"중요한 일정 · D-DAY":null;string title=i==0?"오늘의 일정":i==1?"남은 할 일":target==null?"일정을 선택하세요":target.@event.title;
                var kf=F(9,700,kicker);var tf=F(m.H3,700,title);int kh=kicker==null?0:(int)Math.Ceiling(kf.GetHeight()),th=(int)Math.Ceiling(tf.GetHeight());int blockH=kh+(kicker==null?0:P(6))+th;int ly=cy-blockH/2;
                if(kicker!=null){Ui.Spaced(g,kicker,kf,x,ly,kh,Ui.C("#665b4c"),1.4f*s);ly+=kh+P(6);}
                Ui.Text(g,title,tf,new Rectangle(x,ly,Math.Max(1,leftW),th),DesktopTheme.Ink,TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter);
                Action click=i==0?(Action)(()=>{JumpTo(Rules.Key(DateTime.Today));OpenUpcoming();}):i==1?(Action)FocusTasks:(Action)OpenDdayDialog;
                string tip=i==2?(target!=null?target.@event.title+" · "+FormatDay(target.startDate)+" · 클릭하여 D-day 일정 변경":"클릭하여 달력에서 중요한 일정 선택"):null;
                AddHit(ids[i],r,click,tip,Rectangle.Inflate(r,P(4),P(4)));FocusRing(g,ids[i],box,15*s);
            }
        }
        void PaintBanner(Graphics g){
            var r=banner;Ui.Box(g,r,9*s,Ui.C("#fff0e9"),Ui.C("#c05b46"),1.5f*s);var font=F(12,400,bannerText);
            int x=r.X+P(15.5f),right=r.Right-P(15.5f);var close=F(20);int cw=Ui.Width("×",close);var closeRect=new Rectangle(right-cw,r.Y,cw,r.Height);
            Ui.Text(g,"×",close,closeRect,Hover("banner-close")?Ui.C("#bf4722"):Ui.C("#a44130"),TextFormatFlags.VerticalCenter);AddHit("banner-close",closeRect,()=>{bannerVisible=false;Relayout();});
            var bf=F(12,700,"최신 내용 불러오기");int bw=Ui.Width("최신 내용 불러오기",bf)+P(27);var btn=new Rectangle(closeRect.X-P(14)-bw,r.Y+(r.Height-P(36))/2,bw,P(36));ButtonBox(g,"banner-reload",btn,false);
            Ui.Text(g,"최신 내용 불러오기",bf,btn,DesktopTheme.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);AddHit("banner-reload",btn,Reload);
            Ui.Text(g,bannerText,font,new Rectangle(x,r.Y,btn.X-P(14)-x,r.Height),Ui.C("#a44130"),TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        }
        void Panel(Graphics g,Rectangle r){var box=new RectangleF(r.X,r.Y,r.Width,r.Height);var sh=box;sh.Offset(2*s,2*s);Ui.Fill(g,sh,16*s,Ui.C("#d8c9ae"));Ui.Box(g,box,16*s,DesktopTheme.Card,DesktopTheme.Frame,s);}
        void PaintCalendarPanel(Graphics g){
            var p=calendarPanel;Panel(g,p);
            // Toolbar
            var tb=toolbarRect;int x=tb.X+P(m.TbX);int cy=tb.Y+P(m.TbT)+(tb.Height-P(m.TbT)-P(m.TbB))/2;
            var parts=month.Split('-');int year=int.Parse(parts[0]),mon=int.Parse(parts[1]);string label=year+"년 "+mon+"월";
            var h2=F(m.MonthH2,700,label);int h2h=(int)Math.Ceiling(h2.GetHeight());Ui.Spaced(g,label,h2,x,cy-h2h/2,h2h,DesktopTheme.Ink,-1f*s);
            if(m.English){float hw=Ui.SpacedWidth(label,h2,-s);string eng=new DateTime(year,mon,1).ToString("MMMM",CultureInfo.GetCultureInfo("en-US")).ToUpperInvariant();var ef=F(10,400,null,"mono");float baseline=cy-h2h/2f+Ui.Ascent(h2);int eh=(int)Math.Ceiling(ef.GetHeight());Ui.Spaced(g,eng,ef,x+hw+P(m.MonthGap),(int)(baseline-Ui.Ascent(ef)),eh,DesktopTheme.Muted,1f*s);}
            int right=tb.Right-P(m.TbX);
            // Add event (primary)
            var addFont=F(13,700,"일정 추가");int addTw=(int)Math.Ceiling(Ui.Advance("일정 추가",addFont));int addKbdW=m.AddKbd?KbdWidth("N",10):0;
            // The label is centred in the button; the plus icon and the N badge sit on either side of it, with equal side padding.
            int addIconSide=P(3)+P(20)+P(7);int addBadgeSide=m.AddKbd?P(7)+addKbdW:0;int addSide=P(m.AddPadX)+Math.Max(addIconSide,addBadgeSide);
            int addW=addSide*2+addTw;var add=new Rectangle(right-addW-P(2),cy-P(19),addW,P(38));
            ButtonBox(g,"add-event",add,true);var addBox=add;if(Hover("add-event")&&!Pressed("add-event"))addBox.Offset(-P(1),-P(1));if(Pressed("add-event"))addBox.Offset(P(2),P(2));
            int addTextX=addBox.X+addSide;Ui.Icon(g,"plus",new RectangleF(addBox.X+P(m.AddPadX)-P(5),addBox.Y+(addBox.Height-20*s)/2,20*s,20*s),DesktopTheme.Ink);
            // Typographic drawing so the glyphs fill exactly the advance width that the layout centres.
using(var labelBrush=new SolidBrush(DesktopTheme.Ink))g.DrawString("일정 추가",addFont,labelBrush,addTextX,addBox.Y+(addBox.Height-addFont.GetHeight())/2f,StringFormat.GenericTypographic);
            if(m.AddKbd){int kw=addKbdW,kh=Math.Max(P(19),(int)Math.Ceiling(F(10,400,null,"mono").GetHeight()+4*s));Kbd(g,"N",new Rectangle(addBox.X+addW-P(m.AddPadX)-kw,addBox.Y+(addBox.Height-kh)/2,kw,kh),10,true);}
            AddHit("add-event",add,()=>OpenEvent(null,selected,false));FocusRing(g,"add-event",add,9*s);
            int bx=add.X-P(m.AddMargin)-P(6)-P(36);var next=new Rectangle(bx,cy-P(18),P(36),P(36));ButtonBox(g,"next-month",next,false);Ui.Icon(g,"right",new RectangleF(next.X+8*s,next.Y+8*s,20*s,20*s),DesktopTheme.Ink);AddHit("next-month",next,()=>MonthMove(1),"다음 달");FocusRing(g,"next-month",next,9*s);
            var tf=F(12,700,"오늘");int todayW=Ui.Width("오늘",tf)+P(27);bx-=P(6)+todayW;var today=new Rectangle(bx,cy-P(18),todayW,P(36));ButtonBox(g,"go-today",today,false);var tbx=today;if(Pressed("go-today"))tbx.Offset(P(1),P(1));Ui.Text(g,"오늘",tf,tbx,DesktopTheme.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);AddHit("go-today",today,()=>JumpTo(Rules.Key(DateTime.Today)));FocusRing(g,"go-today",today,9*s);
            bx-=P(6)+P(36);var prev=new Rectangle(bx,cy-P(18),P(36),P(36));ButtonBox(g,"prev-month",prev,false);Ui.Icon(g,"left",new RectangleF(prev.X+8*s,prev.Y+8*s,20*s,20*s),DesktopTheme.Ink);AddHit("prev-month",prev,()=>MonthMove(-1),"이전 달");FocusRing(g,"prev-month",prev,9*s);
            // Filters
            var fr=filterRect;int fy=fr.Y,fh=fr.Height-P(m.FtB);int chipH=Math.Max(P(30),(int)Math.Ceiling(LH(11)+12*s+2*s));int fx=fr.X+P(m.FtX);
            var chips=new List<KeyValuePair<string,string>>{new KeyValuePair<string,string>("all","전체")};foreach(var c in AllCategories())chips.Add(new KeyValuePair<string,string>(c,DesktopTheme.CategoryName(c)));
            foreach(var chip in chips){
                bool active=filter==chip.Key;var cf=F(11,active?700:400,chip.Value);int cw=Ui.Width(chip.Value,cf)+P(m.ChipPadX)*2+P(2)+(chip.Key=="all"?0:P(14));
                var cr=new Rectangle(fx,fy+(fh-chipH)/2,cw,chipH);string id="filter-"+chip.Key;if(cr.Right>searchBoxRect.X-P(8))break; // custom categories past the search box are left out
                if(active)Ui.Box(g,cr,6*s,Ui.C("#f2ead9"),DesktopTheme.Ink,s);else if(Hover(id))Ui.Fill(g,cr,6*s,Ui.C("#f4eddc"));
                int cx=cr.X+P(m.ChipPadX)+P(1);if(chip.Key!="all"){using(var b=new SolidBrush(DesktopTheme.Category(chip.Key)))g.FillEllipse(b,cx,cr.Y+(cr.Height-P(8))/2f,8*s,8*s);cx+=P(14);}
                Ui.Text(g,chip.Value,cf,new Rectangle(cx,cr.Y,cr.Right-cx,cr.Height),active?DesktopTheme.Ink:Ui.C("#716653"),TextFormatFlags.VerticalCenter);
                string key=chip.Key;AddHit(id,cr,()=>{filter=filter==key?"all":key;Render();});FocusRing(g,id,cr,6*s);fx=cr.Right+P(5);
            }
            var sb=searchBoxRect;bool focus=searchBox.Focused;Ui.Box(g,sb,7*s,Ui.C("#f6f0e3"),focus?DesktopTheme.Ink:Ui.C("#dfd4bf"),s);
            Ui.Icon(g,"search",new RectangleF(sb.X+P(10),sb.Y+(sb.Height-15*s)/2,15*s,15*s),Ui.C("#978b76"));
            int skw=KbdWidth("/",9),skh=Math.Max(P(16),(int)Math.Ceiling(F(9,400,null,"mono").GetHeight()+2*s));Kbd(g,"/",new Rectangle(sb.Right-P(10)-skw,sb.Y+(sb.Height-skh)/2,skw,skh),9);
            // Weekdays
            var wr=weekdaysRect;using(var b=new SolidBrush(Ui.C("#f9f3e7")))g.FillRectangle(b,wr);
            using(var b=new SolidBrush(DesktopTheme.Ink))g.FillRectangle(b,wr.X,wr.Y,wr.Width,P(1));using(var b=new SolidBrush(Ui.C("#d5c9b1")))g.FillRectangle(b,wr.X,wr.Bottom-P(1),wr.Width,P(1));
            string[] ko={"일","월","화","수","목","금","토"},en={"SUN","MON","TUE","WED","THU","FRI","SAT"};var wf=F(11,700,"일");var sf=F(8,400,null,"consolas");
            for(int d=0;d<7;d++){int cx0=wr.X+wr.Width*d/7+P(m.WkX);Color ink=d==0?Ui.C("#cf6154"):d==6?Ui.C("#5d8db3"):DesktopTheme.Ink;int ww=Ui.Width(ko[d],wf);
                Ui.Text(g,ko[d],wf,new Rectangle(cx0,wr.Y+P(1),ww+2,wr.Height-P(2)),ink,TextFormatFlags.VerticalCenter);Ui.Text(g,en[d],sf,new Rectangle(cx0+ww+P(5),wr.Y+P(1),P(40),wr.Height-P(2)),Ui.Fade(ink,Ui.C("#f9f3e7"),.55f),TextFormatFlags.VerticalCenter);}
            if(!searchRect.IsEmpty)PaintSearchResults(g);
            // Bottom bar
            var br=bottomRect;using(var b=new SolidBrush(Ui.C("#ded2bb")))g.FillRectangle(b,br.X,br.Y,br.Width,P(1));
            var bf=F(11);int bx0=br.X+P(m.BottomX);using(var b=new SolidBrush(DesktopTheme.Orange))g.FillEllipse(b,bx0,br.Y+P(1)+(br.Height-P(1)-5*s)/2,5*s,5*s);
            Ui.Text(g,"날짜 선택 · 선택한 날짜의 빈 공간을 누르면 일정 추가",bf,new Rectangle(bx0+P(10),br.Y+P(1),br.Width,br.Height-P(1)),Ui.C("#99886c"),TextFormatFlags.VerticalCenter);
            string count=VisibleCount();var mf=F(9,400,null,"mono");int mw=Ui.Width(count,mf);Ui.Text(g,count,mf,new Rectangle(br.Right-P(m.BottomX)-mw-2,br.Y+P(1),mw+2,br.Height-P(1)),Ui.C("#99886c"),TextFormatFlags.VerticalCenter|TextFormatFlags.Right);
        }
        int[] RowHeights(){if(!m.Fit)return GridRowHeights(gridRect.Width);var h=new int[rows];for(int r=0;r<rows;r++)h[r]=gridRect.Height*(r+1)/rows-gridRect.Height*r/rows;return h;}
        int Density(int rowHeight){float h=rowHeight/s;return m.Fit?(h<76?0:h<100?1:h<125?2:3):3;}
        void PaintGrid(Graphics target){
            var range=RangeForMonth();var index=MonthIndex();string today=Rules.Key(DateTime.Today);var heights=RowHeights();int y=gridRect.Y;
            var numberFontCss=m.DayNumCss;var chipFont=F(m.ChipFont,600);var timeFont=F(10);int chipH=ChipHeight();
            for(int r=0;r<rows;r++){
                int rowH=heights[r];int perDay=Density(rowH);
                for(int c=0;c<7;c++){
                    int x0=gridRect.X+gridRect.Width*c/7,x1=gridRect.X+gridRect.Width*(c+1)/7;var cell=new Rectangle(x0,y,x1-x0,rowH);
                    string key=Rules.AddDays(range.Key,r*7+c);List<Occurrence> items;if(!index.TryGetValue(key,out items))items=new List<Occurrence>();
                    CachedCell(target,r*7+c,cell,key,items,today,perDay,g=>{
                    bool same=key.StartsWith(month,StringComparison.Ordinal),isToday=key==today,isSel=key==selected;string holiday;holidays.TryGetValue(key,out holiday);
                    string id="cell-"+key;bool hover=Hover(id)||HoverIn(id);bool over=dropDate==key;
                    Color bg=over||isSel?Ui.C("#fff1d1"):hover?Ui.C("#fff8e9"):!same?Ui.C("#f8f4eb"):DesktopTheme.Card;
                    using(var b=new SolidBrush(bg))g.FillRectangle(b,cell);
                    using(var b=new SolidBrush(DesktopTheme.Line)){if(c<6)g.FillRectangle(b,cell.Right-P(1),cell.Y,P(1),cell.Height);if(r<rows-1)g.FillRectangle(b,cell.X,cell.Bottom-P(1),cell.Width,P(1));}
                    if(isSel){var inset=new RectangleF(cell.X+s,cell.Y+s,cell.Width-2*s,cell.Height-2*s);using(var pen=new Pen(isToday?DesktopTheme.Orange:Ui.C("#e6b259"),2*s))g.DrawRectangle(pen,inset.X,inset.Y,inset.Width,inset.Height);}
                    if(over){var o=new RectangleF(cell.X+4*s,cell.Y+4*s,cell.Width-8*s,cell.Height-8*s);using(var pen=new Pen(DesktopTheme.Orange,2*s){DashStyle=DashStyle.Dash})g.DrawRectangle(pen,o.X,o.Y,o.Width,o.Height);}
                    var cellHit=AddHit(id,cell,()=>CellClick(key,false),null);if(cellHit!=null){cellHit.DropDate=key;cellHit.Focusable=true;}
                    Push(0,cell);Apply(g);
                    int cx=cell.X+P(m.CellX),cw=cell.Width-2*P(m.CellX),cyy=cell.Y+P(m.CellT);
                    // day-top
                    var top=new Rectangle(cx+P(4),cyy,cw-P(8),P(m.DayTopH));
                    var topHit=AddHit(id+"-top",top,()=>CellClick(key,true));if(topHit!=null){topHit.DropDate=key;topHit.Focusable=false;topHit.Repaint=Page(cell);}
                    int dn=P(m.DayNum);var numRect=new Rectangle(top.X,top.Y+(top.Height-dn)/2,dn,dn);string num=int.Parse(key.Substring(8)).ToString();
                    int dow=(int)Rules.Date(key).DayOfWeek;Color numInk=isToday?Color.White:(dow==0||holiday!=null)?Ui.C("#cf6154"):dow==6?Ui.C("#5d8db3"):DesktopTheme.Ink;
                    if(!same&&!isToday)numInk=Ui.Fade(numInk,bg,.36f);
                    var nf=F(numberFontCss,isToday?800:600,num);int nw=Ui.Width(num,nf);if(nw+P(4)>dn)numRect.Width=nw+P(4);
                    if(isToday)using(var b=new SolidBrush(DesktopTheme.Orange))g.FillEllipse(b,numRect);
                    Ui.Text(g,num,nf,numRect,numInk,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
                    if(isToday){var tw=F(8,700,"TODAY");Ui.Text(g,"TODAY",tw,new Rectangle(numRect.Right+P(4),top.Y,P(60),top.Height),Ui.C("#c85029"),TextFormatFlags.VerticalCenter);}
                    var addR=new Rectangle(top.Right-P(21),top.Y+(top.Height-P(22))/2,P(21),P(22));
                    if(hover||isSel||focusId==id)Ui.Icon(g,"plus",new RectangleF(addR.X+4*s,addR.Y+4.5f*s,13*s,13*s),Ui.C("#b8a88b"));
                    string newKey=key;var addHit=AddHit(id+"-add",addR,()=>{selected=newKey;month=newKey.Substring(0,7);Render();OpenEvent(null,newKey,false);},"일정 추가",Page(cell));if(addHit!=null)addHit.Focusable=false;
                    int iy=top.Bottom+P(m.DayTopMB);
                    int fontSpace=DesktopTheme.TextScale>0&&m.Fit?1:0;int slots=Math.Max(0,perDay-(holiday!=null?1:0)-fontSpace);
                    if(holiday!=null){bool est=int.Parse(key.Substring(0,4))>2027;var hf=F(10,400,holiday);int hh=(int)Math.Ceiling(Css(10)*1.3f*s);Ui.Text(g,holiday,hf,new Rectangle(cx+P(3),iy+P(1),cw-P(6),hh),Ui.C("#c35149"),TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);var hhit=AddHit(id+"-holiday",new Rectangle(cx,iy,cw,hh+P(2)),()=>CellClick(key,false),holiday+(est?" (예상 공휴일)":""),Page(cell));if(hhit!=null){hhit.Focusable=false;hhit.DropDate=key;}iy+=hh+P(2);}
                    foreach(var o in items.Take(slots)){iy+=P(3);PaintChip(g,o,key,new Rectangle(cx,iy,cw,chipH),chipFont,timeFont);iy+=chipH;}
                    if(items.Count>slots){string more=slots>0?"+"+(items.Count-slots)+"개 더 보기":"일정 "+items.Count+"개";var mf=F(11,400,more);int mh=(int)Math.Ceiling(Css(11)*m.ChipLH*s);var mr=new Rectangle(cx,iy,Math.Min(cw,Ui.Width(more,mf)+P(10)),mh+P(m.MorePadT));
                        Ui.Text(g,more,mf,new Rectangle(mr.X+P(5),mr.Y+P(m.MorePadT),mr.Width,mh),Hover(id+"-more")?Ui.C("#bf4722"):Ui.C("#8c7450"),TextFormatFlags.VerticalCenter);
                        var mhit=AddHit(id+"-more",mr,()=>{JumpTo(newKey);OpenDayList(newKey);},"이 날짜의 일정 모두 보기");if(mhit!=null)mhit.DropDate=key;}
                    Pop(g);
                    FocusRing(g,id,new RectangleF(cell.X+3*s,cell.Y+3*s,cell.Width-6*s,cell.Height-6*s),0);
                    });
                }
                y+=rowH;
            }
        }
        // Per-cell cache inside the grid: hovering or pressing a date, or ticking one event,
        // redraws only the cells involved instead of all 35-42.
        sealed class CellCache{public string Sig;public DibSurface Dib;public List<Hit> Hits;}
        readonly Dictionary<int,CellCache> cells=new Dictionary<int,CellCache>();
        string Mine(string a,string b){return a!=null&&!modal&&a.StartsWith(b,StringComparison.Ordinal)?a:"";}
        void CachedCell(Graphics g,int slot,Rectangle cell,string key,List<Occurrence> items,string today,int perDay,Action<Graphics> paint){
            if(drag!=null||frames.Count>0||ty!=-pageScroll.Offset){paint(g);return;}
            var sb=new System.Text.StringBuilder();
            sb.Append(key).Append('|').Append(cell).Append('|').Append(key.StartsWith(month,StringComparison.Ordinal)).Append('|').Append(key==selected).Append('|').Append(key==today).Append('|').Append(perDay).Append('|').Append(dropDate==key).Append('|').Append(s).Append('|').Append(DesktopTheme.TextScale).Append(DesktopTheme.FontKey).Append('|');
            string cellId="cell-"+key,chipId="chip-"+key+"-";
            sb.Append(Mine(hoverId,cellId)).Append(Mine(hoverId,chipId)).Append('|').Append(Mine(pressId,cellId)).Append(Mine(pressId,chipId)).Append('|');
            if(Focused&&ShowFocusCues)sb.Append(Mine(focusId,cellId)).Append(Mine(focusId,chipId));sb.Append('|');
            foreach(var o in items){var e=o.@event;sb.Append(o.key).Append('/').Append(o.startDate).Append('/').Append(e.title).Append('/').Append(e.startTime).Append('/').Append(e.category).Append('/').Append(e.repeat).Append('/').Append(Rules.Done(e,o.startDate)).Append('/').Append(IsDdayOccurrence(o)).Append(';');}
            string sig=sb.ToString();CellCache cc;cells.TryGetValue(slot,out cc);
            if(cc==null||cc.Sig!=sig){
                if(cc==null){cc=new CellCache();cells[slot]=cc;}
                if(cc.Dib==null||cc.Dib.Width!=cell.Width||cc.Dib.Height!=cell.Height){if(cc.Dib!=null)cc.Dib.Dispose();cc.Dib=new DibSurface(cell.Width,cell.Height);}
                int sx=bx,sy=by;var outerClip=clip;var outer=building;building=new List<Hit>();
                using(var dg=Graphics.FromHdc(cc.Dib.Dc)){
                    dg.Clear(DesktopTheme.Card);Ui.Prepare(dg);dg.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    bx=cell.X;by=cell.Y+ty;Apply(dg);
                    try{paint(dg);}finally{bx=sx;by=sy;clip=outerClip;}
                }
                cc.Hits=building;cc.Sig=sig;building=outer;Apply(g);
            }
            cc.Dib.BlitTo(g,new Point(cell.X-bx,cell.Y+ty-by));building.AddRange(cc.Hits);
        }
        bool HoverIn(string id){return hoverId!=null&&hoverId.StartsWith(id+"-",StringComparison.Ordinal)&&!modal;}
        void PaintChip(Graphics g,Occurrence o,string key,Rectangle r,Font font,Font timeFont){
            var e=o.@event;bool done=Rules.Done(e,o.startDate);string id="chip-"+key+"-"+o.key;bool hover=Hover(id)||Hover(id+"-check");bool dragging=drag!=null&&drag.EventId==e.id&&drag.Start==o.startDate;
            Color light=Ui.C(CategoryLight(e.category)),bg=done?Ui.C(hover?"#e3eadc":"#edf1e8"):hover?Ui.Mix(light,Color.Black,.04f):light;
            if(dragging)bg=Ui.Fade(bg,DesktopTheme.Card,.45f);
            using(var outer=Ui.Round(r,3*s)){using(var b=new SolidBrush(dragging?Ui.Fade(DesktopTheme.Category(e.category),DesktopTheme.Card,.45f):DesktopTheme.Category(e.category)))g.FillPath(b,outer);}
            using(var inner=Ui.Corners(new RectangleF(r.X+3*s,r.Y,r.Width-3*s,r.Height),0,3*s,3*s,0))using(var b=new SolidBrush(bg))g.FillPath(b,inner);
            int x=r.X+P(3)+P(4);var box=new RectangleF(x,r.Y+(r.Height-12*s)/2,12*s,12*s);
            Ui.CheckBox(g,box,done,Hover(id+"-check"),true,4*s,s,bg);
            string occurrence=o.startDate,eventId=e.id;
            var chk=AddHit(id+"-check",Rectangle.Round(new RectangleF(box.X-2*s,box.Y-2*s,box.Width+4*s,box.Height+4*s)),()=>ToggleEvent(eventId,occurrence),e.repeat=="none"?"일정 완료":"이 회차만 완료",Page(r));
            x+=P(16);Color ink=done?Ui.C("#7b7060"):DesktopTheme.Ink;int right=r.Right-P(4);
            bool repeat=e.repeat!="none",pinned=IsDdayOccurrence(o);
            if(repeat){right-=P(12);Ui.Icon(g,"repeat",new RectangleF(right,r.Y+(r.Height-12*s)/2,12*s,12*s),Ui.Fade(ink,bg,.6f));right-=P(4);}
            if(pinned){right-=P(11);Ui.Icon(g,"flag",new RectangleF(right,r.Y+(r.Height-11*s)/2,11*s,11*s),Ui.C("#bd5169"));right-=P(4);}
            if(e.startTime!=""&&o.startDate==key){var tf=done?F(10,400,null,null,FontStyle.Strikeout):timeFont;int tw=Ui.Width(e.startTime,tf);Ui.Text(g,e.startTime,tf,new Rectangle(x,r.Y,tw+2,r.Height),done?Ui.C("#7b7060"):Ui.C("#766951"),TextFormatFlags.VerticalCenter);x+=tw+P(4);}
            string title=(String.CompareOrdinal(o.startDate,key)<0?"↳ ":"")+e.title;var titleFont=done?F(m.ChipFont,600,title,null,FontStyle.Strikeout):F(m.ChipFont,600,title);
            Ui.Text(g,title,titleFont,new Rectangle(x,r.Y,Math.Max(1,right-x),r.Height),ink,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            var hit=AddHit(id,r,()=>OpenEvent(eventId,occurrence,false),e.title+(e.startTime!=""?" · "+e.startTime:""));
            if(hit!=null){hit.Cursor=Cursors.Hand;hit.DropDate=key;string dragKey=key;hit.Drag=()=>new DragInfo{EventId=eventId,Start=occurrence,Day=dragKey,Title=e.title,Category=e.category};}
            if(chk!=null){chk.DropDate=key;}
            FocusRing(g,id,r,3*s);
        }
        void PaintSearchResults(Graphics g){
            var r=searchRect;using(var b=new SolidBrush(DesktopTheme.Line))g.FillRectangle(b,r.X,r.Y,r.Width,P(1));
            var found=SearchFound();int content=SearchContentHeight(calendarPanel.Width);searchScroll.Content=content;searchScroll.Viewport=r.Height;searchScroll.Bounds=Page(r);searchScroll.Clamp();
            Push(-searchScroll.Offset,new Rectangle(r.X,r.Y+P(1),r.Width,r.Height-P(1)));Apply(g);
            int width=r.Width-P(40)-(searchScroll.Visible?P(12):0);int x=r.X+P(20),y=r.Y+P(1)+P(8)+P(8);
            string header="전체 기간 검색 결과 · "+found.Count+"건";var hf=F(12,700,header);int hh=(int)Math.Ceiling(hf.GetHeight());Ui.Text(g,header,hf,new Rectangle(x,y,width,hh),Ui.C("#897451"),TextFormatFlags.VerticalCenter);y+=hh+P(12);
            if(found.Count==0){var ef=F(11);int eh=(int)Math.Ceiling(Css(11)*1.8f*s);Ui.Text(g,"제목이나 메모에 맞는 일정이 없어요.",ef,new Rectangle(x,y+P(14),width,eh),Ui.C("#a29276"),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
            var rf=F(12,700);var df=F(10);int rowH=RowSearchHeight();
            foreach(var e in found.Take(limits["search"])){
                var row=new Rectangle(x,y,width,rowH);string id="search-"+e.id;if(Hover(id))using(var b=new SolidBrush(Ui.C("#fff3dc")))g.FillRectangle(b,row);
                using(var pen=new Pen(DesktopTheme.Line,s){DashStyle=DashStyle.Dash})g.DrawLine(pen,row.X,row.Bottom-s/2,row.Right,row.Bottom-s/2);
                int rx=row.X+P(4);using(var b=new SolidBrush(DesktopTheme.Category(e.category)))g.FillEllipse(b,rx,row.Y+(row.Height-P(8))/2f,8*s,8*s);rx+=P(18);
                string date=e.startDate+(e.repeat!="none"?" · 반복":"");int dw=Ui.Width(date,df);Ui.Text(g,date,df,new Rectangle(row.Right-P(4)-dw,row.Y,dw+2,row.Height),Ui.C("#8c7a5e"),TextFormatFlags.VerticalCenter);
                Ui.Text(g,e.title,F(12,700,e.title),new Rectangle(rx,row.Y,Math.Max(1,row.Right-P(14)-dw-rx),row.Height),DesktopTheme.Ink,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                string eid=e.id;AddHit(id,row,()=>OpenEvent(eid,selected,false));y+=rowH;
            }
            if(found.Count>limits["search"])PaintMore(g,"search",found.Count,new Rectangle(x,y+P(6),width,MoreHeight()));
            Pop(g);Ui.ScrollBar(g,Shifted(searchScroll),s);
        }
        ScrollModel Shifted(ScrollModel model){var c=new ScrollModel{Offset=model.Offset,Content=model.Content,Viewport=model.Viewport,Bounds=model.Bounds,Hover=model.Hover,Dragging=model.Dragging};var b=c.Bounds;b.Offset(0,ty+pageScroll.Offset);return c;}
        int RowSearchHeight(){return (int)Math.Ceiling(LH(12,700)+22*s+s);}
        int MoreHeight(){return (int)Math.Ceiling(LH(12)+20*s+2*s);}
        int SearchContentHeight(int width){if(search.Length==0)return 0;var n=Math.Min(SearchFound().Count,limits["search"]);int h=P(1)+P(16)+(int)Math.Ceiling(LH(12,700))+P(12)+n*RowSearchHeight()+P(8);if(n==0)h+=(int)Math.Ceiling(Css(11)*1.8f*s)+P(28);if(SearchFound().Count>limits["search"])h+=MoreHeight()+P(12);return h;}
        void PaintMore(Graphics g,string scope,int total,Rectangle r){string text="더 보기 · "+limits[scope]+" / "+total+"개";string id="more-"+scope;Ui.Box(g,r,9*s,Hover(id)?Ui.C("#f9ecd6"):Ui.C("#fff9ee"),DesktopTheme.Line,s);Ui.Text(g,text,F(12,400,text),r,Ui.C("#79694f"),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);AddHit(id,r,()=>{limits[scope]+=pageSizes[scope];Relayout();});}
        void SectionHeader(Graphics g,Rectangle panel,string kicker,string title,string chip,out Rectangle head){
            head=new Rectangle(panel.X+P(1),panel.Y+P(1),panel.Width-P(2),SectionHeaderHeight());int x=head.X+P(m.SecX);int cy=head.Y+P(m.SecT)+(head.Height-P(m.SecT)-P(m.SecB))/2;
            var kf=F(8,700,kicker);var tf=F(17,700,title);int kh=kicker!=null&&m.Kicker?(int)Math.Ceiling(kf.GetHeight()):0;int th=(int)Math.Ceiling(tf.GetHeight());int block=kh+(kh>0?P(6):0)+th;int y=cy-block/2;
            if(kh>0){Ui.Spaced(g,kicker,kf,x,y,kh,Ui.C("#9c8c72"),1.1f*s);y+=kh+P(6);}
            Ui.Spaced(g,title,tf,x,y,th,DesktopTheme.Ink,-0.6f*s);
            if(chip!=null){float tw=Ui.SpacedWidth(title,tf,-0.6f*s);var cf=F(10,400,null,"consolas");int cw=Ui.Width(chip,cf)+P(12),ch=(int)Math.Ceiling(cf.GetHeight()+6*s);var cr=new Rectangle(x+(int)tw+P(6),y+(th-ch)/2,cw,ch);Ui.Box(g,cr,5*s,Ui.C("#f1eadb"),Ui.C("#dfd4bd"),s);Ui.Text(g,chip,cf,cr,Ui.C("#8e7b5b"),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
        }
        void PaintDayPanel(Graphics g){
            Panel(g,dayPanel);Rectangle head;string rel=selected==Rules.Key(DateTime.Today)?"오늘의 계획":"선택한 날짜";
            SectionHeader(g,dayPanel,rel,FormatDay(selected),null,out head);
            int cy=head.Y+P(m.SecT)+(head.Height-P(m.SecT)-P(m.SecB))/2;var add=new Rectangle(head.Right-P(m.SecX)-P(30),cy-P(15),P(30),P(30));
            ButtonBox(g,"day-add",add,false,Ui.C("#fff2d6"),Ui.C("#ceba96"));Ui.Icon(g,"plus",new RectangleF(add.X+7.5f*s,add.Y+7.5f*s,15*s,15*s),DesktopTheme.Ink);AddHit("day-add",add,()=>OpenEvent(null,selected,false),"선택 날짜에 일정 추가");FocusRing(g,"day-add",add,9*s);
            var list=dayList;int width=list.Width-P(32);int content=DayContentHeight(width);
            dayScroll.Content=content+P(m.Fit?12:17);dayScroll.Viewport=list.Height;if(dayScroll.Visible){width-=P(12);content=DayContentHeight(width);dayScroll.Content=content+P(m.Fit?12:17);}
            dayScroll.Bounds=Page(list);dayScroll.Clamp();
            Push(-dayScroll.Offset,list);Apply(g);PaintDayEvents(g,new Rectangle(list.X+P(16),list.Y,width,content));Pop(g);
            Ui.ScrollBar(g,Shifted(dayScroll),s);
        }
        int DayContentHeight(int width){
            var items=SelectedRows();if(items.Count==0)return EmptyDayHeight();
            int h=0;foreach(var o in items.Take(limits["day"]))h+=DayEventHeight(o,width)+P(8);h-=P(8);if(items.Count>limits["day"])h+=MoreHeight()+P(12);return h;
        }
        int EmptyDayHeight(){return (int)Math.Ceiling(m.EmptyT*s+m.EmptyDraw*s+m.EmptyDrawMB*s+LH(12,600)+6*s+Css(10)*1.8f*s*2+LH(11,700)+10*s+m.EmptyB*s);}
        int DayEventHeight(Occurrence o,int width){
            var e=o.@event;int textW=width-P(4)-P(1)-P(36)-P(12)-P(34)-P(1);
            float h=12*s+Css(11)*1.5f*s+5*s+Ui.Wrap(e.title,F(12,700,e.title),textW).Count*Css(12)*1.6f*s;
            if(e.note!="")h+=5*s+Ui.Wrap(e.note,F(10,400,e.note),textW).Count*Css(10)*1.7f*s;
            if(o.startDate!=o.endDate||e.repeat!="none")h+=6*s+Css(11)*1.7f*s;
            h+=12*s+2*s;return (int)Math.Ceiling(Math.Max(h,62*s));
        }
        void PaintDayEvents(Graphics g,Rectangle area){
            var items=SelectedRows();int y=area.Y;
            if(items.Count==0){PaintEmptyDay(g,area);return;}
            foreach(var o in items.Take(limits["day"])){
                var e=o.@event;int h=DayEventHeight(o,area.Width);var card=new Rectangle(area.X,y,area.Width,h);bool done=Rules.Done(e,o.startDate);string id="day-"+o.key;
                bool hover=Hover(id)||HoverIn(id);Color light=Ui.C(CategoryLight(e.category));Color fill=done?Ui.C(hover?"#e3eadc":"#edf1e8"):hover?light:Ui.C("#fffdf8");
                if(drag!=null&&drag.Active&&drag.EventId==e.id&&drag.Start==o.startDate)fill=Ui.Fade(fill,DesktopTheme.Card,.45f);
                Ui.LeftEdgeCard(g,card,8*s,fill,Ui.C("#e5dac7"),4*s,DesktopTheme.Category(e.category),s);
                int x=card.X+P(4)+P(10);var box=new RectangleF(x,card.Y+(card.Height-16*s)/2,16*s,16*s);Ui.CheckBox(g,box,done,Hover(id+"-check"),true,5*s,s,fill);
                string eid=e.id,occ=o.startDate;AddHit(id+"-check",Rectangle.Round(new RectangleF(box.X-3*s,box.Y-3*s,22*s,22*s)),()=>ToggleEvent(eid,occ),e.repeat=="none"?"일정 완료":"이 회차만 완료",Page(card));
                x+=P(26);int actionsW=P(34);int textW=card.Right-actionsW-P(12)-x;int ty0=card.Y+P(12);
                var small=F(11);string time=e.startTime!=""?e.startTime+(e.endTime!=""?" – "+e.endTime:""):"종일";string cat=DesktopTheme.CategoryName(e.category)+(IsDdayOccurrence(o)?" · D-day":"");
                int lh=(int)Math.Round(Css(11)*1.5f*s);Ui.Text(g,time,small,new Rectangle(x,ty0,textW,lh),Ui.C("#79694f"),TextFormatFlags.VerticalCenter);int cw=Ui.Width(cat,F(11,400,cat));Ui.Text(g,cat,F(11,400,cat),new Rectangle(x+textW-cw,ty0,cw+2,lh),Ui.C("#79694f"),TextFormatFlags.VerticalCenter);
                ty0+=lh+P(5);var tf=F(12,700,e.title,null,done?FontStyle.Strikeout:FontStyle.Regular);ty0+=Ui.Paragraph(g,e.title,tf,x,ty0,textW,Css(12)*1.6f*s,done?Ui.C("#7b7060"):DesktopTheme.Ink);
                if(e.note!=""){ty0+=P(5);ty0+=Ui.Paragraph(g,e.note,F(10,400,e.note),x,ty0,textW,Css(10)*1.7f*s,Ui.C("#93836a"));}
                if(o.startDate!=o.endDate||e.repeat!="none"){ty0+=P(6);int mx=x;var mf=F(11);int mh=(int)Math.Round(Css(11)*1.7f*s);
                    if(o.startDate!=o.endDate){string span=Rules.Date(o.startDate).ToString("M. d.",CultureInfo.InvariantCulture)+" – "+Rules.Date(o.endDate).ToString("M. d.",CultureInfo.InvariantCulture);Ui.Text(g,span,mf,new Rectangle(mx,ty0,textW,mh),Ui.C("#79694f"),TextFormatFlags.VerticalCenter);mx+=Ui.Width(span,mf);}
                    if(e.repeat!="none"){mx+=Ui.Width(" ",mf);Ui.Icon(g,"repeat",new RectangleF(mx,ty0+(mh-12*s)/2,12*s,12*s),Ui.C("#79694f"));mx+=P(12)+Ui.Width(" ",mf);string rl=RepeatLabel(e.repeat);Ui.Text(g,rl,F(11,400,rl),new Rectangle(mx,ty0,textW,mh),Ui.C("#79694f"),TextFormatFlags.VerticalCenter);}}
                // actions
                int axc=card.Right-P(6)-P(26);int ay=card.Y+(card.Height-P(54))/2;bool chosen=IsDdayOccurrence(o);
                var flag=new Rectangle(axc,ay,P(26),P(26));var trash=new Rectangle(axc,ay+P(28),P(26),P(26));
                if(Hover(id+"-flag")||chosen)Ui.Box(g,flag,6*s,DesktopTheme.Rose,Ui.C("#e5adbc"),s);Ui.Icon(g,"flag",new RectangleF(flag.X+6*s,flag.Y+6*s,14*s,14*s),Hover(id+"-flag")||chosen?Ui.C("#a13e58"):Ui.C("#ae9d7f"));
                if(Hover(id+"-trash"))Ui.Box(g,trash,6*s,Ui.C("#ffe8dd"),Ui.C("#edc3ae"),s);Ui.Icon(g,"trash",new RectangleF(trash.X+6*s,trash.Y+6*s,14*s,14*s),Hover(id+"-trash")?Ui.C("#b34d35"):Ui.C("#ae9d7f"));
                AddHit(id+"-flag",flag,()=>ChooseDday(eid,occ,true),chosen?"다시 누르면 D-day 선택 해제":"이 일정을 D-day로 선택",Page(card));
                AddHit(id+"-trash",trash,()=>DeleteSchedule(eid,false,occ),"일정 삭제",Page(card));
                var bodyHit=AddHit(id,new Rectangle(box.Right>0?(int)box.Right+P(6):card.X,card.Y,card.Right-actionsW-((int)box.Right+P(6)),card.Height),()=>OpenEvent(eid,occ,false),"끌어서 다른 날짜나 할 일로 옮길 수 있어요",Page(card));
                if(bodyHit!=null){string dayKey=selected,title=e.title,category=e.category;bodyHit.Drag=()=>new DragInfo{EventId=eid,Start=occ,Day=dayKey,Title=title,Category=category};}
                FocusRing(g,id,card,8*s);y+=h+P(8);
            }
            if(items.Count>limits["day"])PaintMore(g,"day",items.Count,new Rectangle(area.X,y-P(8)+P(6),area.Width,MoreHeight()));
        }
        void PaintEmptyDay(Graphics g,Rectangle area){
            bool filtered=filter!="all"||search.Length>0;int y=area.Y+P(m.EmptyT);int cx=area.X+area.Width/2;float d=m.EmptyDraw*s;
            var state0=g.Save();g.TranslateTransform(cx,y+d/2);g.RotateTransform(-5);var dr=new RectangleF(-d/2,-d/2,d,d);Ui.Fill(g,dr,13*s,Ui.C("#fbf3e2"));using(var pen=new Pen(Ui.C("#c8b793"),1.5f*s){DashStyle=DashStyle.Dash})using(var p=Ui.Round(new RectangleF(dr.X+.75f*s,dr.Y+.75f*s,d-1.5f*s,d-1.5f*s),12*s))g.DrawPath(pen,p);
            Ui.Icon(g,"calendar",new RectangleF(-m.EmptyIcon*s/2,-m.EmptyIcon*s/2,m.EmptyIcon*s,m.EmptyIcon*s),Ui.C("#b79960"));g.Restore(state0);
            y+=(int)d+P(m.EmptyDrawMB);string strong=filtered?"조건에 맞는 일정이 없어요":"아직은 비어 있는 하루";var sf=F(12,600,strong);int sh=(int)Math.Ceiling(sf.GetHeight());Ui.Text(g,strong,sf,new Rectangle(area.X,y,area.Width,sh),Ui.C("#89795d"),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);y+=sh+P(6);
            string p1=filtered?"다른 분류나 검색어를 확인해 보세요.":"새로운 계획을 하나 적어볼까요?";y+=Ui.Paragraph(g,p1,F(10,400,p1),area.X,y,area.Width,Css(10)*1.8f*s,Ui.C("#ad9e85"),false,TextFormatFlags.HorizontalCenter);
            string b="+ 일정 추가하기";var bf=F(11,700,b);int bw=Ui.Width(b,bf)+P(10),bh=(int)Math.Ceiling(bf.GetHeight())+P(10);var br=new Rectangle(cx-bw/2,y,bw,bh);
            Ui.Text(g,b,bf,new Rectangle(br.X,br.Y+P(10),br.Width,bh-P(10)),Ui.C("#b96936"),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);AddHit("empty-add",br,()=>OpenEvent(null,selected,false));
        }
        void PaintTaskPanel(Graphics g){
            double pulseAt=(DateTime.UtcNow-highlight).TotalSeconds;
            if(pulseAt<2){float a=(float)Math.Sin(Math.PI*(pulseAt%1));var ring=new RectangleF(taskPanel.X-4*s*a,taskPanel.Y-4*s*a,taskPanel.Width+8*s*a,taskPanel.Height+8*s*a);Ui.Fill(g,ring,(16+4*a)*s,Ui.C("#c5e5b6"));}
            Panel(g,taskPanel);int done=state.tasks.Count(t=>t.done);Rectangle head;
            if(dropDate==TaskDrop){var o=new RectangleF(taskPanel.X+4*s,taskPanel.Y+4*s,taskPanel.Width-8*s,taskPanel.Height-8*s);using(var b=new SolidBrush(Ui.C("#f3f8ee")))using(var p=Ui.Round(o,13*s))g.FillPath(b,p);using(var pen=new Pen(Ui.C("#92b576"),2*s){DashStyle=DashStyle.Dash})using(var p=Ui.Round(o,13*s))g.DrawPath(pen,p);}
            SectionHeader(g,taskPanel,null,"할 일",done+"/"+state.tasks.Length,out head);
            var ff=F(31,400,"✿");int fh=(int)Math.Ceiling(ff.GetHeight());int fw=Ui.Width("✿",ff);int cy=head.Y+P(m.SecT)+(head.Height-P(m.SecT)-P(m.SecB))/2;Ui.Text(g,"✿",ff,new Rectangle(head.Right-P(m.SecX)-fw-2,cy-fh/2,fw+4,fh),Ui.C("#b3c995"),TextFormatFlags.VerticalCenter);
            var te=taskEntry;bool focus=taskTitle.Focused;Ui.Box(g,te,7*s,Ui.C("#fffaf0"),focus?DesktopTheme.Orange:Ui.C("#d9ccb3"),s);
            var addR=new Rectangle(te.Right-P(9)-P(24),te.Y+(te.Height-P(24))/2,P(24),P(24));Ui.Box(g,addR,6*s,Hover("task-add")?Ui.Mix(Ui.C("#c5e5b6"),Color.Black,.04f):Ui.C("#c5e5b6"),DesktopTheme.Ink,s);
            {float pc=addR.X+addR.Width/2f,pm=addR.Y+addR.Height/2f,arm=5.5f*s;using(var pen=new Pen(DesktopTheme.Ink,1.6f*s){StartCap=LineCap.Round,EndCap=LineCap.Round}){var mode=g.SmoothingMode;g.SmoothingMode=SmoothingMode.AntiAlias;g.DrawLine(pen,pc-arm,pm,pc+arm,pm);g.DrawLine(pen,pc,pm-arm,pc,pm+arm);g.SmoothingMode=mode;}}AddHit("task-add",addR,AddTask,"할 일 추가");
            var tb=taskBottom;var lf=F(9);Ui.Text(g,"마감일",lf,new Rectangle(tb.X,tb.Y,P(60),tb.Height),Ui.C("#9d8a6a"),TextFormatFlags.VerticalCenter);
            string hide="완료 숨기기";int hw=Ui.Width(hide,lf);var hideRect=new Rectangle(tb.Right-hw-P(14),tb.Y,hw+P(14),tb.Height);
            var hb=new RectangleF(hideRect.X,tb.Y+(tb.Height-11*s)/2,11*s,11*s);Ui.CheckBox(g,hb,state.settings.hideCompleted,Hover("hide-completed"),!saving,5*s,s,DesktopTheme.Card);
            Ui.Text(g,hide,lf,new Rectangle(hideRect.X+P(14),tb.Y,hw+2,tb.Height),Ui.C("#9d8a6a"),TextFormatFlags.VerticalCenter);AddHit("hide-completed",hideRect,ToggleHideCompleted);FocusRing(g,"hide-completed",hb,5*s);
            var pr=progressRect;Ui.Fill(g,pr,3*s,Ui.C("#f0e9d9"));if(state.tasks.Length>0&&done>0){var bar=new RectangleF(pr.X,pr.Y,pr.Width*done/(float)state.tasks.Length,pr.Height);var c=g.Clip;using(var p=Ui.Round(pr,3*s))g.SetClip(p,CombineMode.Intersect);using(var b=new SolidBrush(Ui.C("#92b576")))g.FillRectangle(b,bar);g.Clip=c;}
            var list=taskList;int width=list.Width-P(32);int content=TasksContentHeight(width);taskScroll.Content=content+P(18);taskScroll.Viewport=list.Height;if(taskScroll.Visible){width-=P(12);content=TasksContentHeight(width);taskScroll.Content=content+P(18);}
            taskScroll.Bounds=Page(list);taskScroll.Clamp();
            Push(-taskScroll.Offset,list);Apply(g);PaintTasks(g,new Rectangle(list.X+P(16),list.Y+P(5),width,content));Pop(g);Ui.ScrollBar(g,Shifted(taskScroll),s);
        }
        List<CalendarTask> TaskRows(){return state.tasks.Where(t=>!state.settings.hideCompleted||!t.done).OrderBy(t=>t.done).ThenBy(t=>t.dueDate==""?"9999":t.dueDate,StringComparer.Ordinal).ThenBy(t=>t.createdAt,StringComparer.Ordinal).ToList();}
        int TaskItemHeight(CalendarTask t,int width){int textW=width-P(16)-P(8)-P(8)-P(16);float h=24*s+Ui.Wrap(t.title,F(11,400,t.title),textW).Count*Css(11)*1.7f*s;if(t.dueDate!="")h+=3*s+Css(11)*1.7f*s;return (int)Math.Ceiling(h+s);}
        int TasksContentHeight(int width){var list=TaskRows();if(list.Count==0)return (int)Math.Ceiling(28*s+Css(11)*1.8f*s);int h=list.Take(limits["tasks"]).Sum(t=>TaskItemHeight(t,width));if(list.Count>limits["tasks"])h+=MoreHeight()+P(12);return h;}
        void PaintTasks(Graphics g,Rectangle area){
            var list=TaskRows();string today=Rules.Key(DateTime.Today);int y=area.Y;
            if(list.Count==0){bool all=state.tasks.Length>0&&state.tasks.All(t=>t.done);string text=all?"오늘도 하나씩, 모두 해냈어요. ✓":"머릿속 할 일을 여기에 내려놓으세요.";Ui.Paragraph(g,text,F(11,400,text),area.X,y+P(14),area.Width,Css(11)*1.8f*s,Ui.C("#a29276"),false,TextFormatFlags.HorizontalCenter);return;}
            int shown=Math.Min(list.Count,limits["tasks"]);
            for(int i=0;i<shown;i++){
                var t=list[i];int h=TaskItemHeight(t,area.Width);var row=new Rectangle(area.X,y,area.Width,h);string id="task-"+t.id;bool dragging=drag!=null&&drag.TaskId==t.id;
                if(t.done)using(var b=new SolidBrush(Ui.C("#edf1e8")))g.FillRectangle(b,row);
                if(i<shown-1||list.Count>shown)using(var pen=new Pen(Ui.C("#e1d6c1"),s){DashStyle=DashStyle.Dash})g.DrawLine(pen,row.X,row.Bottom-s/2,row.Right,row.Bottom-s/2);
                var box=new RectangleF(row.X,row.Y+12*s+(Css(11)*1.7f*s-16*s)/2,16*s,16*s);Ui.CheckBox(g,box,t.done,Hover(id+"-check"),true,5*s,s,t.done?Ui.C("#edf1e8"):DesktopTheme.Card);
                string tid=t.id;AddHit(id+"-check",Rectangle.Round(new RectangleF(box.X-2*s,box.Y-2*s,20*s,20*s)),()=>ToggleTask(tid),null,Page(row));
                int x=row.X+P(24);int textW=row.Width-P(24)-P(24);int yy=row.Y+P(12);var nf=F(11,400,t.title,null,t.done?FontStyle.Strikeout:FontStyle.Regular);Color ink=dragging?Ui.Fade(DesktopTheme.Ink,DesktopTheme.Card,.45f):t.done?Ui.C("#7b7060"):DesktopTheme.Ink;
                yy+=Ui.Paragraph(g,t.title,nf,x,yy,textW,Css(11)*1.7f*s,ink);
                if(t.dueDate!=""){bool overdue=!t.done&&String.CompareOrdinal(t.dueDate,today)<0;string date=Rules.Date(t.dueDate).ToString("M. d.",CultureInfo.InvariantCulture)+" "+(!t.done?"· "+(overdue?"기한 지남":Rules.Dday(t.dueDate,today)):"");Ui.Text(g,date.TrimEnd(),F(11,400,date),new Rectangle(x,yy+P(3),textW,(int)Math.Round(Css(11)*1.7f*s)),overdue?Ui.C("#c05b46"):Ui.C("#79694f"),TextFormatFlags.VerticalCenter);}
                var del=new Rectangle(row.Right-P(16),row.Y+P(12)+P(2),P(16),P(16));Ui.Icon(g,"trash",new RectangleF(del.X+3*s,del.Y+1.5f*s,13*s,13*s),Hover(id+"-delete")?Ui.C("#b34736"):Ui.C("#c6bba6"));
                AddHit(id+"-delete",del,()=>DeleteTask(tid),null,Page(row));
                var body=AddHit(id,new Rectangle(x,row.Y,del.X-x,row.Height),null,"달력으로 끌어 일정을 만들 수 있어요",Page(row));
                if(body!=null){body.Cursor=Cursors.SizeAll;body.Focusable=false;body.Drag=()=>new DragInfo{TaskId=tid,Title=t.title,Category="work"};}
                y+=h;
            }
            if(list.Count>limits["tasks"])PaintMore(g,"tasks",list.Count,new Rectangle(area.X,y+P(6),area.Width,MoreHeight()));
        }
        void PaintNotification(Graphics g){
            var r=notifCard;bool enabled=state.settings.desktopPopups;bool hover=Hover("notification");
            Ui.Box(g,r,13*s,hover?Ui.C("#ffe7b0"):Ui.C("#ffedc5"),DesktopTheme.Line,s);float px=enabled?12:m.NotifX;int x=r.X+P(px)+P(1);int cy=r.Y+r.Height/2;
            Ui.Icon(g,"bell",new RectangleF(x,cy-10*s,20*s,20*s),Ui.C("#b88c42"));x+=P(31);
            string title=enabled?"데스크톱 알림이 켜져 있어요":"일정 알림을 켜둘까요?",sub=enabled?"숨겨두어도, 확인할 때까지 알려드려요.":"실행 중인 동안, 놓치지 않도록.";
            var tf=F(11,700,title);int th=(int)Math.Ceiling(Css(11)*1.33f*s);int textW=r.Right-P(px)-P(25)-x;
            if(enabled)Ui.Text(g,title,tf,new Rectangle(x,cy-th/2,textW,th),DesktopTheme.Ink,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            else{var sf=F(11,400,sub);int sh=(int)Math.Ceiling(sf.GetHeight());int top=cy-(th+P(4)+sh)/2;Ui.Text(g,title,tf,new Rectangle(x,top,textW,th),DesktopTheme.Ink,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);Ui.Text(g,sub,sf,new Rectangle(x,top+th+P(4),textW,sh),Ui.C("#79694f"),TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);}
            Ui.Icon(g,"right",new RectangleF(r.Right-P(px)-P(15),cy-7*s,14*s,14*s),DesktopTheme.Ink);
            AddHit("notification",r,()=>OpenSettings("notifications"));FocusRing(g,"notification",r,13*s);
        }
        void PaintFooter(Graphics g){
            var r=footer;int y=r.Y+P(m.FootT);int h=r.Bottom-y;var f=F(9,400,saveStatus);int x=r.X+P(1);
            using(var b=new SolidBrush(saveFailed?Ui.C("#c05b46"):Ui.C("#72a464")))g.FillEllipse(b,x,y+(h-5*s)/2,5*s,5*s);
            Ui.Text(g,saveStatus,f,new Rectangle(x+P(11),y,r.Width/2,h),saveFailed?Ui.C("#c05b46"):Ui.C("#a18d6c"),TextFormatFlags.VerticalCenter);
            var hf=F(10);string help="사용법 · 단축키 ";int hw=Ui.Width(help,hf),aw=Ui.Width("↗",hf);var hr=new Rectangle(r.Right-P(1)-hw-aw,y,hw+aw,h);bool hover=Hover("help");
            Ui.Text(g,help,hf,new Rectangle(hr.X,y,hw+2,h),hover?Ui.C("#bf4722"):Ui.C("#7e6c4d"),TextFormatFlags.VerticalCenter);Ui.Text(g,"↗",hf,new Rectangle(hr.X+hw,y,aw+2,h),hover?Ui.C("#bf4722"):Ui.C("#c48140"),TextFormatFlags.VerticalCenter);
            AddHit("help",hr,ShowHelp);
        }
        void PaintDragGhost(Graphics g){
            if(drag==null||!drag.Active)return;g.ResetTransform();g.ResetClip();var p=PointToClient(Cursor.Position);var font=F(10,600,drag.Title);int w=Math.Min(P(200),Ui.Width(drag.Title,font)+P(24));var r=new Rectangle(p.X+P(10),p.Y+P(8),w,P(22));
            Ui.Fill(g,new RectangleF(r.X,r.Y,r.Width,r.Height),3*s,Ui.Fade(DesktopTheme.Category(drag.Category),DesktopTheme.Card,.7f));Ui.Fill(g,new RectangleF(r.X+3*s,r.Y,r.Width-3*s,r.Height),3*s,Ui.Fade(Ui.C(CategoryLight(drag.Category)),DesktopTheme.Card,.85f));
            Ui.Text(g,drag.Title,font,new Rectangle(r.X+P(10),r.Y,r.Width-P(14),r.Height),Ui.Fade(DesktopTheme.Ink,DesktopTheme.Card,.8f),TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        }
        // ---------- Input ----------
        Hit HitAt(Point p){for(int i=hits.Count-1;i>=0;i--)if(hits[i].Rect.Contains(p))return hits[i];return null;}
        ScrollModel ScrollAt(Point p){foreach(var sm in new[]{dayScroll,taskScroll,searchScroll})if(sm.Visible&&sm.Bounds.Contains(p))return sm;if(!m.Fit&&pageScroll.Visible)return pageScroll;return null;}
        void InvalidateHit(string id){if(id==null)return;foreach(var h in hits)if(h.Id==id){var r=h.Repaint;r.Inflate(P(6),P(6));Invalidate(r);}}
        protected override void OnMouseMove(MouseEventArgs e){
            base.OnMouseMove(e);if(m==null)return;
            foreach(var sm in new[]{dayScroll,taskScroll,searchScroll,pageScroll})if(sm.Dragging){sm.DragTo(e.Y,s);ScrolledBy(sm);return;}
            if(drag!=null){if(!drag.Active&&(Math.Abs(e.X-drag.Origin.X)>P(4)||Math.Abs(e.Y-drag.Origin.Y)>P(4))){drag.Active=true;Cursor=Cursors.SizeAll;Invalidate();}
                if(drag.Active){var over=HitAt(e.Location);string date=over==null?null:over.DropDate;if(drag.EventId!=null&&Page(taskPanel).Contains(e.Location))date=TaskDrop;if(date!=dropDate){dropDate=date;}Invalidate();return;}}
            bool barHover=false;foreach(var sm in new[]{dayScroll,taskScroll,searchScroll,pageScroll}){bool hv=sm.Visible&&sm.Track(s).Contains(e.Location)&&(sm!=pageScroll||!m.Fit);if(hv!=sm.Hover){sm.Hover=hv;Invalidate(sm.Track(s));}barHover|=hv;}
            var hit=barHover?null:HitAt(e.Location);string id=hit==null?null:hit.Id;
            if(id!=hoverId){InvalidateHit(hoverId);hoverId=id;InvalidateHit(hoverId);
                string tip=hit==null?null:hit.Tip;if(tip!=tipShown){tipShown=tip;tips.SetToolTip(this,tip??"");}}
            Cursor=hit!=null?(hit.Cursor??Cursors.Hand):Cursors.Default;
        }
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);if(drag!=null&&drag.Active)return;InvalidateHit(hoverId);hoverId=null;foreach(var sm in new[]{dayScroll,taskScroll,searchScroll,pageScroll})if(sm.Hover){sm.Hover=false;Invalidate();}}
        protected override void OnMouseDown(MouseEventArgs e){
            base.OnMouseDown(e);if(e.Button!=MouseButtons.Left||m==null)return;
            foreach(var sm in new[]{dayScroll,taskScroll,searchScroll,pageScroll}){if(!sm.Visible||(sm==pageScroll&&m.Fit))continue;if(sm.Track(s).Contains(e.Location)){var thumb=sm.Thumb(s);if(thumb.Contains(e.Location)){sm.Dragging=true;sm.DragStart=e.Y;sm.DragOffset=sm.Offset;Capture=true;}else{sm.Offset+=e.Y<thumb.Top?-sm.Viewport:sm.Viewport;sm.Clamp();ScrolledBy(sm);}return;}}
            var hit=HitAt(e.Location);
            if(hit==null){var page=new Point(e.X,e.Y+pageScroll.Offset);bool inBlock=new[]{summary,banner,calendarPanel,dayPanel,taskPanel,notifCard}.Any(r=>r.Contains(page))||(footer.Contains(page)&&false);
                if(!modal&&(masthead.Contains(page)||!inBlock)){if(e.Clicks>=2){if(WindowAction!=null)WindowAction("maximize");return;}ReleaseCapture();SendMessage(FindForm().Handle,0x00a1,new IntPtr(2),IntPtr.Zero);return;}
                Focus();return;}
            if(!(hit.Id=="settings"||hit.Id.StartsWith("win-")))Focus();
            pressId=hit.Id;focusId=null;InvalidateHit(pressId);
            if(hit.Drag!=null&&!saving){drag=(DragInfo)hit.Drag();drag.Origin=e.Location;Capture=true;}
        }
        protected override void OnMouseUp(MouseEventArgs e){
            base.OnMouseUp(e);
            foreach(var sm in new[]{dayScroll,taskScroll,searchScroll,pageScroll})if(sm.Dragging){sm.Dragging=false;Capture=false;Invalidate();return;}
            if(drag!=null){var d=drag;drag=null;string target=dropDate;dropDate=null;Capture=false;Cursor=Cursors.Default;
                if(d.Active){Invalidate();if(target!=null)Drop(d,target);pressId=null;return;}}
            string pressed=pressId;pressId=null;InvalidateHit(pressed);
            if(e.Button!=MouseButtons.Left)return;var hit=HitAt(e.Location);if(hit!=null&&hit.Id==pressed&&hit.Click!=null&&!modal)hit.Click();
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e){base.OnMouseDoubleClick(e);}
        protected override void OnMouseWheel(MouseEventArgs e){
            base.OnMouseWheel(e);if(m==null||modal)return;var p=PointToClient(Cursor.Position);var sm=ScrollAt(p);
            if(sm!=null&&sm!=pageScroll){int before=sm.Offset;sm.Wheel(e.Delta,s);if(sm.Offset!=before){ScrolledBy(sm);return;}}
            if(!m.Fit&&pageScroll.Wheel(e.Delta,s))ScrolledBy(pageScroll);
        }
        void ScrolledBy(ScrollModel sm){if(sm==pageScroll)PlaceChildren();Invalidate();}
        // Pseudo drop date for the task panel: schedules dropped there become tasks.
        const string TaskDrop="@tasks";
        void Drop(DragInfo d,string target){if(target==TaskDrop){if(d.EventId!=null)DropToTasks(d);return;}if(d.TaskId!=null)DropTask(d.TaskId,target);else DropEvent(d,target);}
        protected override bool IsInputKey(Keys k){var key=k&Keys.KeyCode;return key==Keys.Left||key==Keys.Right||key==Keys.Up||key==Keys.Down||key==Keys.Enter||key==Keys.Space||base.IsInputKey(k);}
        protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
        protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
        protected override bool ProcessCmdKey(ref Message msg,Keys keyData){
            if(modal)return base.ProcessCmdKey(ref msg,keyData);
            // Text boxes keep their own Ctrl+Z for typing.
            bool typing=searchBox.ContainsFocus||taskTitle.ContainsFocus;
            if(!typing&&keyData==(Keys.Control|Keys.Z)){UndoKey();return true;}
            if(!typing&&(keyData==(Keys.Control|Keys.Y)||keyData==(Keys.Control|Keys.Shift|Keys.Z))){RedoKey();return true;}
            if(keyData==Keys.Tab||keyData==(Keys.Tab|Keys.Shift)){if(Focused){MoveFocus(keyData==Keys.Tab?1:-1);return true;}}
            return base.ProcessCmdKey(ref msg,keyData);
        }
        void MoveFocus(int dir){var list=hits.Where(h=>h.Focusable&&h.Click!=null).Select(h=>h.Id).Distinct().ToList();if(list.Count==0)return;int i=focusId==null?(dir>0?-1:list.Count):list.IndexOf(focusId);i+=dir;if(i<0||i>=list.Count){focusId=null;Invalidate();if(dir>0)searchBox.Focus();else SelectNextControl(this,false,true,true,true);return;}focusId=list[i];Invalidate();}
        protected override void OnKeyDown(KeyEventArgs e){
            base.OnKeyDown(e);if(modal||e.Control||e.Alt)return;
            if((e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space)&&focusId!=null){var h=hits.FirstOrDefault(x=>x.Id==focusId);if(h!=null&&h.Click!=null){if(focusId.StartsWith("cell-")&&!focusId.Contains("-check")){JumpTo(focusId.Substring(5));OpenEvent(null,selected,false);}else h.Click();}e.Handled=true;return;}
            HandleShortcut(e);
        }
        internal bool HandleShortcut(KeyEventArgs e){
            if(modal||e.Control||e.Alt)return false;
            if(e.KeyCode==Keys.N){OpenEvent(null,selected,false);e.Handled=e.SuppressKeyPress=true;return true;}
            if(e.KeyCode==Keys.OemQuestion||e.KeyCode==Keys.Divide){searchBox.Focus();e.Handled=e.SuppressKeyPress=true;return true;}
            if(e.KeyCode==Keys.T){JumpTo(Rules.Key(DateTime.Today));e.Handled=e.SuppressKeyPress=true;return true;}
            if(e.KeyCode==Keys.W){OpenUpcoming();e.Handled=e.SuppressKeyPress=true;return true;}
            if(e.KeyCode==Keys.D){OpenDiary();e.Handled=e.SuppressKeyPress=true;return true;}
            if(focusId!=null)return false;
            if(e.KeyCode==Keys.Left){MonthMove(-1);e.Handled=true;return true;}
            if(e.KeyCode==Keys.Right){MonthMove(1);e.Handled=true;return true;}
            return false;
        }
        // Points in the window's sizing band fall through to the host window, which resizes.
        protected override void WndProc(ref Message msg){
            if(msg.Msg==0x0084&&host!=null&&!host.IsDisposed){long packed=msg.LParam.ToInt64();var screen=new Point((short)(packed&0xffff),(short)((packed>>16)&0xffff));if(host.ResizeHit(host.PointToClient(screen))!=0){msg.Result=new IntPtr(-1);return;}}
            base.WndProc(ref msg);
        }
        protected override void Dispose(bool disposing){if(disposing){DropCaches();clock.Dispose();searchTimer.Dispose();tips.Dispose();}base.Dispose(disposing);}
    }
}
