using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PetCalendarDesktop {
    // Native surfaces use the same palette as desktop/ui/styles.css and model.mjs.
    public static class DesktopTheme {
        public static int TextScale;
        public static readonly Color Paper=ColorTranslator.FromHtml("#fff4dd"),Card=ColorTranslator.FromHtml("#fffdf7"),Ink=ColorTranslator.FromHtml("#29241e"),Muted=ColorTranslator.FromHtml("#837866"),Line=ColorTranslator.FromHtml("#e8ddc8"),Orange=ColorTranslator.FromHtml("#ff7146"),Rose=ColorTranslator.FromHtml("#ffc6d4");
        public static int Px(Control control,float value){return (int)Math.Round(value*control.DeviceDpi/96f);}
        public static Color Category(string category){return ColorTranslator.FromHtml(category=="meeting"?"#a5d9f6":category=="deadline"?"#f6acc1":category=="personal"?"#b9dfa7":"#ffb078");}
        public static string CategoryName(string category){return category=="meeting"?"회의":category=="deadline"?"마감":category=="personal"?"개인":"업무";}
        public static GraphicsPath Round(RectangleF r,float radius){float d=Math.Max(1,Math.Min(radius*2,Math.Min(r.Width,r.Height)));var path=new GraphicsPath();path.AddArc(r.X,r.Y,d,d,180,90);path.AddArc(r.Right-d,r.Y,d,d,270,90);path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.X,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;}
        public static readonly Color Frame=ColorTranslator.FromHtml("#c6b79c");
        public static GraphicsPath WindowOutline(Form form,float inset,float radius){return Round(new RectangleF(inset,inset,form.ClientSize.Width-2*inset,form.ClientSize.Height-2*inset),Math.Max(1,radius*form.DeviceDpi/96f-inset));}
        public static void ShapeWindow(Form form,float radius,bool square){
            if(form.ClientSize.Width<=0||form.ClientSize.Height<=0)return;
            var previous=form.Region;
            if(square)form.Region=null;
            else using(var outline=WindowOutline(form,0,radius))form.Region=new Region(outline);
            if(previous!=null)previous.Dispose();
        }
        public static void PaintWindow(Form form,Graphics graphics,float radius,bool square){
            graphics.Clear(Frame);graphics.SmoothingMode=SmoothingMode.AntiAlias;float inset=form.DeviceDpi/96f;
            if(form.ClientSize.Width<=2*inset||form.ClientSize.Height<=2*inset)return;
            using(var paper=new SolidBrush(Paper)){
                if(square)graphics.FillRectangle(paper,inset,inset,form.ClientSize.Width-2*inset,form.ClientSize.Height-2*inset);
                else using(var outline=WindowOutline(form,inset,radius))graphics.FillPath(paper,outline);
            }
        }
    }
    public sealed class WidgetSettings {
        public bool enabled,topmost,locked;
        public bool showDday=true;
        public int x=-1,y=-1,width=380,height=590;
    }
    public sealed class CalendarWidget : Form {
        readonly StateStore store;
        readonly string path;
        readonly Action<string,string> open;
        readonly Action<Exception> log;
        readonly Func<Hotkey,Task> apply;
        readonly CalendarGrid grid=new CalendarGrid();
        readonly Label monthTitle=new Label(),dayTitle=new Label();
        readonly WidgetDday dday=new WidgetDday();
        readonly TableLayoutPanel layout=new TableLayoutPanel();
        readonly WidgetAgenda agenda=new WidgetAgenda();
        readonly ToolStripMenuItem pin=new ToolStripMenuItem(),positionLock=new ToolStripMenuItem(),showDday=new ToolStripMenuItem();
        readonly ContextMenuStrip preferencesMenu=new ContextMenuStrip();
        readonly Timer saveTimer=new Timer(),clock=new Timer();
        readonly ToolTip tips=new ToolTip();
        WidgetSettings settings;
        string selected=Rules.Key(DateTime.Today),month=Rules.Key(DateTime.Today).Substring(0,7),today=Rules.Key(DateTime.Today);
        bool closing,busy,rendering,loaded,layingOut,restoringBounds;
        CalendarState renderedState,rowsState;string renderedDate,renderedMonth,renderedToday,rowsDate;
        Occurrence[] cachedRows;
        public event Action VisibilityPreferenceChanged;
        public bool EnabledPreference {get{return settings.enabled;}}
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
        public CalendarWidget(StateStore state,Func<Hotkey,Task> applyKey,Action<string,string> openEvent,Action<Exception> error) {
            store=state;apply=applyKey;open=openEvent;log=error;path=Path.Combine(store.DirectoryPath,"widget.json");
            try{settings=File.Exists(path)?Codec.Read<WidgetSettings>(File.ReadAllText(path)):new WidgetSettings();}catch(Exception e){log(e);settings=new WidgetSettings();}
            if(settings==null)settings=new WidgetSettings();
            SuspendLayout();AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
            Text="펫 캘린더 · 바탕화면 위젯";ShowInTaskbar=false;FormBorderStyle=FormBorderStyle.None;Padding=new Padding(12);MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.Manual;
            BackColor=DesktopTheme.Paper;ForeColor=DesktopTheme.Ink;Font=new Font("Segoe UI",10+DesktopTheme.TextScale);MinimumSize=new Size(340,480);Size=new Size(380,590);DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);
            layout.Name="widget-layout";layout.Dock=DockStyle.Fill;layout.Margin=Padding.Empty;layout.ColumnCount=1;layout.RowCount=5;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(float height in new float[]{40,196,54,42,0})layout.RowStyles.Add(new RowStyle(height==0?SizeType.Percent:SizeType.Absolute,height==0?100:height));
            var header=Row("header",6);header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(float width in new float[]{34,48,34,32,32})header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,width));
            monthTitle.Name="month-title";monthTitle.Dock=DockStyle.Fill;monthTitle.Margin=Padding.Empty;monthTitle.Font=new Font(Font.FontFamily,15+DesktopTheme.TextScale,FontStyle.Bold);monthTitle.TextAlign=ContentAlignment.MiddleLeft;monthTitle.AutoEllipsis=true;monthTitle.UseMnemonic=false;monthTitle.MouseDown+=DragWidget;header.MouseDown+=DragWidget;
            var prev=Button("‹","prev","이전 달");var now=Button("오늘","today","오늘로 이동");var next=Button("›","next","다음 달");var gear=Button("⚙","settings","위젯 설정");var close=Button("×","close","위젯 닫기");prev.Quiet=now.Quiet=next.Quiet=gear.Quiet=close.Quiet=true;
            header.Controls.Add(monthTitle,0,0);header.Controls.Add(prev,1,0);header.Controls.Add(now,2,0);header.Controls.Add(next,3,0);header.Controls.Add(gear,4,0);header.Controls.Add(close,5,0);
            prev.Click+=delegate{MoveMonth(-1);};next.Click+=delegate{MoveMonth(1);};now.Click+=delegate{selected=Rules.Key(DateTime.Today);month=selected.Substring(0,7);Render();};close.Click+=delegate{HideWidget();};
            pin.Text="항상 위에";pin.Name="topmost";pin.CheckOnClick=true;pin.Checked=settings.topmost;
            positionLock.Text="위치 고정";positionLock.Name="locked";positionLock.CheckOnClick=true;positionLock.Checked=settings.locked;
            preferencesMenu.Name="widget-settings";preferencesMenu.ShowImageMargin=false;preferencesMenu.ShowCheckMargin=true;
            preferencesMenu.BackColor=DesktopTheme.Card;preferencesMenu.ForeColor=DesktopTheme.Ink;
            showDday.Text="D-day 표시";showDday.Name="show-dday";showDday.CheckOnClick=true;showDday.Checked=settings.showDday;
            preferencesMenu.Items.AddRange(new ToolStripItem[]{pin,positionLock,showDday});
            gear.Click+=delegate{if(preferencesMenu.Visible)preferencesMenu.Close();else {Activate();preferencesMenu.Show(gear,new Point(0,gear.Height));}};
            pin.CheckedChanged+=delegate{settings.topmost=pin.Checked;TopMost=settings.topmost;Persist();NotifyPreference();};positionLock.CheckedChanged+=delegate{settings.locked=positionLock.Checked;Persist();NotifyPreference();};TopMost=settings.topmost;
            showDday.CheckedChanged+=delegate{settings.showDday=showDday.Checked;dday.Visible=settings.showDday;FitCalendar();Persist();NotifyPreference();};
            grid.Name="calendar";grid.Dock=DockStyle.Fill;grid.Margin=new Padding(0,4,0,6);grid.SelectDate=SelectDate;grid.AddDate=key=>open(null,key);grid.ForeColor=ForeColor;
            dday.Name="dday";dday.Dock=DockStyle.Fill;dday.Margin=new Padding(0,3,0,3);dday.Click+=delegate{var target=ChosenDday();if(target!=null)open(target.@event.id,target.startDate);else open("@dday",selected);};
            var dayHeader=Row("day-header",2);dayHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));dayHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,86));
            dayTitle.Name="selected-day";dayTitle.Dock=DockStyle.Fill;dayTitle.Margin=Padding.Empty;dayTitle.Font=new Font(Font,FontStyle.Bold);dayTitle.TextAlign=ContentAlignment.MiddleLeft;dayTitle.AutoEllipsis=true;dayTitle.UseMnemonic=false;
            var add=Button("일정","add","선택 날짜에 일정 추가");add.Primary=true;add.Plus=true;dayHeader.Controls.Add(dayTitle,0,0);dayHeader.Controls.Add(add,1,0);add.Click+=delegate{open(null,selected);};
            agenda.Name="agenda";agenda.Dock=DockStyle.Fill;agenda.Margin=Padding.Empty;agenda.Padding=Padding.Empty;agenda.SizeChanged+=delegate{LayoutRows();};
            DoubleClick+=OpenFullCalendar;layout.DoubleClick+=OpenFullCalendar;agenda.DoubleClick+=OpenFullCalendar;
            tips.SetToolTip(agenda,"빈 공간을 더블클릭하면 전체 캘린더가 열립니다.");
            layout.Controls.Add(header,0,0);layout.Controls.Add(grid,0,1);layout.Controls.Add(dday,0,2);layout.Controls.Add(dayHeader,0,3);layout.Controls.Add(agenda,0,4);Controls.Add(layout);
            saveTimer.Interval=400;saveTimer.Tick+=delegate{saveTimer.Stop();Persist();};
            clock.Interval=60000;clock.Tick+=delegate{if(!Visible)return;string nextDay=Rules.Key(DateTime.Today);if(nextDay!=today){if(selected==today){selected=nextDay;month=nextDay.Substring(0,7);}today=nextDay;Render();}};clock.Start();
            LocationChanged+=PositionChanged;SizeChanged+=delegate(object sender,EventArgs e){FitCalendar();PositionChanged(sender,e);};store.Changed+=StateChanged;
            ResumeLayout(true);loaded=true;dday.Visible=settings.showDday;FitCalendar();
        }
        static TableLayoutPanel Row(string name,int columns){var row=new TableLayoutPanel{Name=name,Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=Padding.Empty,ColumnCount=columns,RowCount=1};row.RowStyles.Add(new RowStyle(SizeType.Percent,100));return row;}
        WidgetButton Button(string text,string name,string accessible){var button=new WidgetButton{Text=text,Name=name,AccessibleName=accessible,Dock=DockStyle.Fill,Margin=new Padding(2,3,2,3),FlatStyle=FlatStyle.Flat,BackColor=DesktopTheme.Paper,ForeColor=DesktopTheme.Ink};tips.SetToolTip(button,accessible);return button;}
        void RestoreSavedBounds(){
            restoringBounds=true;try{
                var area=Screen.PrimaryScreen.WorkingArea;var rect=new Rectangle(settings.x,settings.y,Math.Max(MinimumSize.Width,Math.Min(DesktopTheme.Px(this,1000),settings.width)),Math.Max(MinimumSize.Height,Math.Min(DesktopTheme.Px(this,1400),settings.height)));
                var screen=Screen.AllScreens.FirstOrDefault(s=>s.WorkingArea.IntersectsWith(rect));if(screen!=null)area=screen.WorkingArea;
                if(settings.x==-1&&settings.y==-1){rect.Width=DesktopTheme.Px(this,380);rect.Height=DesktopTheme.Px(this,590);rect.X=area.Right-rect.Width-DesktopTheme.Px(this,20);rect.Y=area.Top+DesktopTheme.Px(this,24);}
                rect.Width=Math.Min(rect.Width,area.Width);rect.Height=Math.Min(rect.Height,area.Height);rect.X=Math.Max(area.Left,Math.Min(area.Right-rect.Width,rect.X));rect.Y=Math.Max(area.Top,Math.Min(area.Bottom-rect.Height,rect.Y));Bounds=rect;
            }finally{restoringBounds=false;}FitCalendar();
        }
        protected override void OnLoad(EventArgs e){base.OnLoad(e);RestoreSavedBounds();}
        void UpdateWindowRegion(){DesktopTheme.ShapeWindow(this,14,false);}
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);UpdateWindowRegion();}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);UpdateWindowRegion();}
        protected override void OnDpiChanged(DpiChangedEventArgs e){restoringBounds=true;try{base.OnDpiChanged(e);}finally{restoringBounds=false;}UpdateWindowRegion();if(loaded){FitCalendar();LayoutRows();saveTimer.Stop();saveTimer.Start();}}
        void FitCalendar(){if(!loaded||layingOut)return;layingOut=true;try{layout.RowStyles[2].Height=settings.showDday?DesktopTheme.Px(this,54):0;int available=ClientSize.Height-Padding.Vertical-DesktopTheme.Px(this,40+(settings.showDday?54:0)+42+64);int desired=DesktopTheme.Px(this,26+grid.WeekCount*34);layout.RowStyles[1].Height=Math.Max(DesktopTheme.Px(this,26+grid.WeekCount*28),Math.Min(desired,available));}finally{layingOut=false;}}
        void OpenFullCalendar(object sender,EventArgs e){open("",selected);}
        void DragWidget(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left&&!settings.locked){ReleaseCapture();SendMessage(Handle,0x00a1,new IntPtr(2),IntPtr.Zero);}}
        void PositionChanged(object sender,EventArgs e){if(loaded&&!restoringBounds&&!closing&&WindowState==FormWindowState.Normal){saveTimer.Stop();saveTimer.Start();}}
        void Persist(){if(!loaded||restoringBounds)return;settings.x=Left;settings.y=Top;settings.width=Width;settings.height=Height;try{StateStore.Atomic(path,Codec.Json(settings),File.Exists(path)?path+".bak":null);}catch(Exception e){log(e);}}
        internal WidgetSettings BackupPreferences(){if(loaded&&Visible){settings.x=Left;settings.y=Top;settings.width=Width;settings.height=Height;}return Codec.Read<WidgetSettings>(Codec.Json(settings));}
        internal static WidgetSettings CheckBackup(object value){WidgetSettings s;try{s=Codec.Read<WidgetSettings>(Codec.Json(value));}catch{throw new AppError(400,"위젯 백업 설정을 확인해 주세요.");}if(s==null||s.width<200||s.width>10000||s.height<200||s.height>10000||s.x< -100000||s.x>100000||s.y< -100000||s.y>100000)throw new AppError(400,"위젯 백업 설정을 확인해 주세요.");return s;}
        internal void RestorePreferences(WidgetSettings value){
            saveTimer.Stop();settings=Codec.Read<WidgetSettings>(Codec.Json(value));
            restoringBounds=true;try{Size=new Size(settings.width,settings.height);if(settings.x!=-1&&settings.y!=-1){var target=new Rectangle(settings.x,settings.y,Width,Height);bool visible=false;foreach(var screen in Screen.AllScreens)if(screen.WorkingArea.IntersectsWith(target)){visible=true;break;}if(visible)Location=new Point(settings.x,settings.y);}}finally{restoringBounds=false;}
            pin.Checked=settings.topmost;positionLock.Checked=settings.locked;showDday.Checked=settings.showDday;TopMost=settings.topmost;dday.Visible=settings.showDday;
            if(settings.enabled)ShowWidget();else HideWidget();FitCalendar();Persist();NotifyPreference();
        }
        void StateChanged(){if(IsDisposed)return;try{if(InvokeRequired)BeginInvoke(new Action(()=>{if(Visible)Render();}));else if(Visible)Render();}catch(InvalidOperationException){}}
        public void ShowWidget(){settings.enabled=true;Render();Show();FitCalendar();Persist();NotifyPreference();}
        public void HideWidget(){preferencesMenu.Close();settings.enabled=false;Hide();Persist();NotifyPreference();}
        void NotifyPreference(){if(VisibilityPreferenceChanged!=null)VisibilityPreferenceChanged();}
        public void Restore(){if(settings.enabled)ShowWidget();}
        public void SelectDate(string key){if(!Rules.IsDate(key))throw new AppError(400,"날짜를 확인해 주세요.");selected=key;month=key.Substring(0,7);Render();}
        void MoveMonth(int offset){var d=Rules.Date(month+"-01").AddMonths(offset);month=Rules.Key(d).Substring(0,7);selected=month+"-01";Render();}
        Occurrence ChosenDday(){var e=store.Snapshot.events.FirstOrDefault(x=>x.pinned);return e==null?null:Rules.DdayOccurrence(e,Rules.Key(DateTime.Today));}
        Occurrence[] Rows(){if(rowsState==store.Snapshot&&rowsDate==selected)return cachedRows;rowsState=store.Snapshot;rowsDate=selected;return cachedRows=Rules.Occurrences(store.Snapshot.events,selected,selected,false).OrderBy(o=>Rules.Done(o.@event,o.startDate)).ThenBy(o=>o.@event.startTime=="").ThenBy(o=>o.@event.startTime,StringComparer.Ordinal).ThenBy(o=>o.@event.endTime==""?"99:99":o.@event.endTime,StringComparer.Ordinal).ThenBy(o=>o.@event.title,StringComparer.Create(Rules.Korean,false)).ThenBy(o=>o.startDate,StringComparer.Ordinal).ToArray();}
        public async Task Complete(string key){if(busy)return;var o=Rows().FirstOrDefault(x=>x.key==key);if(o==null)return;busy=true;try{var state=Codec.Read<CalendarState>(Codec.Json(store.Snapshot));var e=state.events.First(x=>x.id==o.@event.id);e.completedDates=Rules.Done(e,o.startDate)?e.completedDates.Where(d=>d!=o.startDate).ToArray():e.completedDates.Concat(new[]{o.startDate}).ToArray();await store.Save(Codec.Json(state),apply);}catch(Exception e){log(e);MessageBox.Show(this,"완료 상태를 저장하지 못했습니다.\n"+e.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Warning);}finally{busy=false;Render();}}
        public void Render(){if(rendering||IsDisposed||renderedState==store.Snapshot&&renderedDate==selected&&renderedMonth==month&&renderedToday==today)return;rendering=true;try{
            monthTitle.Text=Rules.Date(month+"-01").ToString("yyyy년 M월");dayTitle.Text=Rules.Date(selected).ToString("M월 d일 (ddd)",Rules.Korean);
            var target=ChosenDday();dday.SetTarget(target);tips.SetToolTip(dday,target==null?"눌러서 D-day 일정을 선택하세요.":Rules.Dday(target.startDate,today)+" · "+target.@event.title);
            grid.UpdateMonth(month,selected,store.Snapshot.events);FitCalendar();if(renderedDate!=selected)agenda.Scrollbar.Value=0;agenda.Items.SuspendLayout();
            var existing=agenda.Items.Controls.Cast<Control>().OfType<Panel>().Where(c=>c.Name.StartsWith("row-")).ToDictionary(c=>c.Name);
            var keep=new HashSet<Control>();var rows=Rows();int index=0;
            foreach(var o in rows.Take(200)) {
                Panel row;bool done=Rules.Done(o.@event,o.startDate);
                if(!existing.TryGetValue("row-"+o.key,out row)) {
                    row=new Panel{Name="row-"+o.key,Height=DesktopTheme.Px(this,64),Margin=new Padding(0,0,0,DesktopTheme.Px(this,7)),BackColor=DesktopTheme.Paper,Tag=o.key};
                    var check=new ThemeCheckBox{Name="check-"+o.key,AutoSize=false,BackColor=DesktopTheme.Paper};
                    var copy=new WidgetEventButton(o,done){Name=o.key,Tag=o.key};
                    copy.Click+=delegate{var current=copy.Occurrence;open(current.@event.id,current.startDate);};
                    string key=o.key;check.CheckedChanged+=async delegate{if(!rendering)await Complete(key);};
                    copy.Controls.Add(check);row.Controls.Add(copy);agenda.Items.Controls.Add(row);
                }
                var button=(WidgetEventButton)row.Controls[0];var box=(CheckBox)button.Controls[0];
                box.Checked=done;box.AccessibleName=o.@event.title+" 완료";button.Update(o,done);button.AccessibleName=o.@event.title+" 일정 수정";
                tips.SetToolTip(button,(o.@event.startTime==""?"종일":o.@event.startTime+(o.@event.endTime==""?"":" – "+o.@event.endTime))+" · "+o.@event.title);
                keep.Add(row);agenda.Items.Controls.SetChildIndex(row,index++);
            }
            foreach(Control old in agenda.Items.Controls.Cast<Control>().ToArray())if(!keep.Contains(old)){agenda.Items.Controls.Remove(old);old.Dispose();}
            if(rows.Length==0){var empty=new Label{Name="empty-agenda",Text="아직은 비어 있는 하루\n새로운 계획을 하나 적어볼까요?",Height=DesktopTheme.Px(this,82),TextAlign=ContentAlignment.MiddleCenter,ForeColor=DesktopTheme.Muted,UseMnemonic=false,Margin=Padding.Empty};empty.DoubleClick+=OpenFullCalendar;agenda.Items.Controls.Add(empty);}
            if(rows.Length>200){var more=Button("전체 "+rows.Length+"개 일정 보기","more","전체 일정 보기");more.Dock=DockStyle.None;more.Height=DesktopTheme.Px(this,34);more.Click+=delegate{open("",selected);};agenda.Items.Controls.Add(more);}
            agenda.Items.ResumeLayout(true);LayoutRows();renderedState=store.Snapshot;renderedDate=selected;renderedMonth=month;renderedToday=today;
        }finally{rendering=false;}}
        void LayoutRows(){if(layingOut||agenda.IsDisposed)return;layingOut=true;try{int width=Math.Max(1,agenda.ContentWidth-DesktopTheme.Px(this,3));foreach(Control c in agenda.Items.Controls){c.Width=Math.Max(1,width-c.Margin.Horizontal);if(c.Name=="empty-agenda")c.Height=Math.Max(DesktopTheme.Px(this,82),agenda.ClientSize.Height-DesktopTheme.Px(this,12));if(c is Panel&&c.Controls.Count==1&&c.Controls[0] is WidgetEventButton){int check=DesktopTheme.Px(this,24);c.Height=DesktopTheme.Px(this,64);c.Margin=new Padding(0,0,0,DesktopTheme.Px(this,7));c.Controls[0].Bounds=new Rectangle(0,0,c.Width,c.Height);c.Controls[0].Controls[0].Bounds=new Rectangle(DesktopTheme.Px(this,11),DesktopTheme.Px(this,20),check,DesktopTheme.Px(this,24));}}agenda.Reflow();}finally{layingOut=false;}}
        public object Inspect(){return new{settingsMenuVisible=preferencesMenu.Visible,enabled=settings.enabled,visible=Visible,showDday=settings.showDday,topmost=TopMost,locked=settings.locked,x=Left,y=Top,width=Width,height=Height,selected=selected,month=month,rows=Rows().Select(o=>new{id=o.@event.id,key=o.key,title=o.@event.title,done=Rules.Done(o.@event,o.startDate)}).ToArray(),window=Handle.ToInt64()};}
        public object InspectLayout(){var controls=new List<object>();InspectChildren(this,controls);return new{dpi=DeviceDpi,fontSize=Font.SizeInPoints,weeks=grid.WeekCount,calendarWindow=grid.Handle.ToInt64(),agendaWindow=agenda.Handle.ToInt64(),scrollbarWindow=agenda.Scrollbar.Handle.ToInt64(),scrollOffset=agenda.Scrollbar.Value,scrollMaximum=agenda.Scrollbar.Maximum,calendar=grid.InspectCells(),controls=controls.ToArray(),horizontalScroll=false};}
        static void InspectChildren(Control parent,List<object> result){foreach(Control child in parent.Controls.Cast<Control>().Take(parent is FlowLayoutPanel?3:int.MaxValue)){if(!child.Visible)continue;var screen=child.PointToScreen(Point.Empty);result.Add(new{name=child.Name,parent=parent.Name,kind=child is WidgetButton?"button":"control",window=child.Handle.ToInt64(),x=child.Left,y=child.Top,screenX=screen.X,screenY=screen.Y,width=child.Width,height=child.Height,parentWidth=parent.ClientSize.Width,parentHeight=parent.ClientSize.Height,scrolling=parent is FlowLayoutPanel||parent is WidgetAgenda});InspectChildren(child,result);}}
        public void ApplyTextScale(){
            if(IsDisposed)return;
            var oldFont=Font;var oldMonth=monthTitle.Font;var oldDay=dayTitle.Font;
            Font=new Font("Segoe UI",10+DesktopTheme.TextScale);
            monthTitle.Font=new Font(Font.FontFamily,15+DesktopTheme.TextScale,FontStyle.Bold);
            dayTitle.Font=new Font(Font,FontStyle.Bold);
            oldMonth.Dispose();oldDay.Dispose();oldFont.Dispose();renderedState=null;Render();Invalidate(true);
        }
        public void Configure(bool? topmost,bool? locked,bool? ddayVisible=null){if(ddayVisible.HasValue)showDday.Checked=ddayVisible.Value;if(topmost.HasValue)pin.Checked=topmost.Value;if(locked.HasValue)positionLock.Checked=locked.Value;}
        public void ClickButton(string name){var item=preferencesMenu.Items.Find(name,false).FirstOrDefault() as ToolStripMenuItem;if(item!=null){item.PerformClick();preferencesMenu.Close();return;}var button=Controls.Find(name,true).FirstOrDefault() as Button;if(button==null)throw new AppError(404,"위젯 버튼을 찾지 못했습니다.");button.PerformClick();}
        public void ClickCalendar(Point point,bool twice){grid.AutomationClick(point,twice);}
        public void CaptureImage(string file){using(var bmp=new Bitmap(Width,Height)){DrawToBitmap(bmp,new Rectangle(0,0,Width,Height));bmp.Save(file);}}
        public void Stop(){preferencesMenu.Close();Persist();closing=true;Close();}
        protected override void OnFormClosing(FormClosingEventArgs e){if(!closing){e.Cancel=true;HideWidget();}base.OnFormClosing(e);}
        protected override void OnPaintBackground(PaintEventArgs e){DesktopTheme.PaintWindow(this,e.Graphics,14,false);}
        protected override void WndProc(ref Message m){
            if(settings!=null&&settings.locked&&m.Msg==0x0112&&((m.WParam.ToInt64()&0xfff0)==0xf010||(m.WParam.ToInt64()&0xfff0)==0xf000))return;
            if(m.Msg==0x0084){base.WndProc(ref m);if(settings==null||settings.locked){m.Result=new IntPtr(1);return;}long packed=m.LParam.ToInt64();var point=PointToClient(new Point((short)(packed&0xffff),(short)((packed>>16)&0xffff)));int edge=Math.Max(5,DesktopTheme.Px(this,5));bool left=point.X<edge,right=point.X>=ClientSize.Width-edge,top=point.Y<edge,bottom=point.Y>=ClientSize.Height-edge;int hit=top?(left?13:right?14:12):bottom?(left?16:right?17:15):left?10:right?11:1;m.Result=new IntPtr(hit);return;}base.WndProc(ref m);
        }
        protected override void Dispose(bool disposing){if(disposing){store.Changed-=StateChanged;saveTimer.Dispose();clock.Dispose();tips.Dispose();preferencesMenu.Dispose();}base.Dispose(disposing);}
    }
    // The calendar's native agenda uses the same soft track and pill thumb as
    // web surfaces, without adding a WebView or overlaying a system scrollbar.
    public sealed class WidgetAgenda : Panel {
        public readonly FlowLayoutPanel Items=new FlowLayoutPanel();
        public readonly ThemeScrollBar Scrollbar=new ThemeScrollBar();
        bool reflowing;
        int TotalHeight {get{return Items.Controls.Cast<Control>().Sum(c=>c.Height+c.Margin.Vertical);}}
        public int ContentWidth {get{return Math.Max(1,ClientSize.Width-(TotalHeight>ClientSize.Height?DesktopTheme.Px(this,18):0));}}
        public WidgetAgenda(){
            DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw|ControlStyles.StandardClick|ControlStyles.StandardDoubleClick,true);BackColor=DesktopTheme.Paper;
            Items.Name="agenda-items";Items.AutoScroll=false;Items.FlowDirection=FlowDirection.TopDown;Items.WrapContents=false;Items.Margin=Padding.Empty;Items.Padding=Padding.Empty;Items.BackColor=BackColor;
            Scrollbar.Name="agenda-scrollbar";Scrollbar.AccessibleName="일정 목록 스크롤";Scrollbar.ValueChanged+=delegate{Items.Top=-Scrollbar.Value;};
            Items.ControlAdded+=delegate(object sender,ControlEventArgs e){BindWheel(e.Control);};Items.DoubleClick+=delegate{OnDoubleClick(EventArgs.Empty);};Items.MouseWheel+=Wheel;
            Controls.Add(Items);Controls.Add(Scrollbar);Scrollbar.BringToFront();
        }
        void BindWheel(Control control){control.MouseWheel+=Wheel;foreach(Control child in control.Controls)BindWheel(child);control.ControlAdded+=delegate(object sender,ControlEventArgs e){BindWheel(e.Control);};}
        void Wheel(object sender,MouseEventArgs e){if(e is HandledMouseEventArgs&&((HandledMouseEventArgs)e).Handled)return;Scrollbar.ScrollBy(-e.Delta/120*DesktopTheme.Px(this,48));if(e is HandledMouseEventArgs)((HandledMouseEventArgs)e).Handled=true;}
        protected override void OnMouseWheel(MouseEventArgs e){Wheel(this,e);}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);Reflow();}
        public void Reflow(){if(reflowing||Items==null||Scrollbar==null)return;reflowing=true;try{
            int total=TotalHeight;Scrollbar.SetMetrics(total,ClientSize.Height);Scrollbar.Visible=total>ClientSize.Height;
            Scrollbar.Bounds=new Rectangle(Math.Max(0,ClientSize.Width-DesktopTheme.Px(this,12)),0,DesktopTheme.Px(this,12),ClientSize.Height);
            Items.SetBounds(0,-Scrollbar.Value,ContentWidth,Math.Max(1,total));
        }finally{reflowing=false;}}
    }
    public sealed class ThemeScrollBar : Control {
        int value,maximum,viewport=1,total=1,dragY,startValue;
        bool dragging,hovered;
        public event Action ValueChanged;
        public int Maximum {get{return maximum;}}
        public int Value {get{return value;}set{int next=Math.Max(0,Math.Min(maximum,value));if(this.value==next)return;this.value=next;Invalidate();if(ValueChanged!=null)ValueChanged();}}
        public ThemeScrollBar(){DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);TabStop=true;Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.ScrollBar;BackColor=DesktopTheme.Paper;}
        public void SetMetrics(int content,int visible){total=Math.Max(1,content);viewport=Math.Max(1,visible);maximum=Math.Max(0,content-visible);Value=value;Invalidate();}
        public void ScrollBy(int amount){Value+=amount;}
        Rectangle Thumb {get{int pad=DesktopTheme.Px(this,2),track=Math.Max(1,Height-2*pad),height=Math.Min(track,Math.Max(DesktopTheme.Px(this,28),(int)((long)track*viewport/total))),y=pad+(maximum==0?0:(int)((long)(track-height)*value/maximum));return new Rectangle(pad,y,Math.Max(1,Width-2*pad),height);}}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;float scale=DeviceDpi/96f;
            if(Width<4||Height<4)return;using(var shape=DesktopTheme.Round(new RectangleF(scale,scale,Width-2*scale,Height-2*scale),Width/2f))using(var fill=new SolidBrush(ColorTranslator.FromHtml("#faf1df")))g.FillPath(fill,shape);
            using(var shape=DesktopTheme.Round(Thumb,Width/2f))using(var fill=new SolidBrush(ColorTranslator.FromHtml(dragging?"#c18b60":hovered?"#d7a275":"#cfb99a")))g.FillPath(fill,shape);
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,ClientRectangle);
        }
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;Focus();var thumb=Thumb;if(thumb.Contains(e.Location)){dragging=true;dragY=e.Y;startValue=value;Capture=true;}else ScrollBy(e.Y<thumb.Top?-viewport:viewport);Invalidate();}
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging){int travel=Math.Max(1,Height-DesktopTheme.Px(this,4)-Thumb.Height);Value=startValue+(int)((long)(e.Y-dragY)*maximum/travel);}}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);dragging=false;Capture=false;Invalidate();}
        protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture){dragging=false;Invalidate();}}
        protected override void OnMouseEnter(EventArgs e){hovered=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hovered=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseWheel(MouseEventArgs e){ScrollBy(-e.Delta/120*DesktopTheme.Px(this,48));if(e is HandledMouseEventArgs)((HandledMouseEventArgs)e).Handled=true;}
        protected override bool IsInputKey(Keys keys){return keys==Keys.Up||keys==Keys.Down||keys==Keys.PageUp||keys==Keys.PageDown||keys==Keys.Home||keys==Keys.End||base.IsInputKey(keys);}
        protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);int step=DesktopTheme.Px(this,32);switch(e.KeyCode){case Keys.Up:ScrollBy(-step);break;case Keys.Down:ScrollBy(step);break;case Keys.PageUp:ScrollBy(-viewport);break;case Keys.PageDown:ScrollBy(viewport);break;case Keys.Home:Value=0;break;case Keys.End:Value=maximum;break;default:return;}e.Handled=true;e.SuppressKeyPress=true;}
        protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
        protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
    }
    public sealed class ThemeCheckBox : CheckBox {
        bool hovered;
        public ThemeCheckBox(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);SetStyle(ControlStyles.Opaque,false);Cursor=Cursors.Hand;UseMnemonic=false;}
        public override Size GetPreferredSize(Size proposedSize){var text=TextRenderer.MeasureText(Text,Font);return new Size(DesktopTheme.Px(this,24)+text.Width,Math.Max(DesktopTheme.Px(this,24),text.Height+DesktopTheme.Px(this,4)));}
        protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(Parent is WidgetEventButton?((WidgetEventButton)Parent).SurfaceColor:Parent==null?BackColor:Parent.BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;float s=DeviceDpi/96f;var box=new RectangleF(2*s,(Height-16*s)/2,16*s,16*s);
            Color fill=ColorTranslator.FromHtml(Checked?(hovered&&Enabled?"#ffd6b9":"#ffe1ca"):(hovered&&Enabled?"#fff0df":"#fffaf1"));
            Color border=ColorTranslator.FromHtml(Checked?(hovered&&Enabled?"#d98d64":"#e7a27e"):(hovered&&Enabled?"#df9871":"#cbb99d"));
            if(!Enabled){fill=DesktopTheme.Paper;border=DesktopTheme.Line;}
            using(var shape=DesktopTheme.Round(box,5*s)){using(var brush=new SolidBrush(fill))g.FillPath(brush,shape);using(var line=new Pen(border,1.2f*s))g.DrawPath(line,shape);}
            if(Checked||CheckState==CheckState.Indeterminate)using(var pen=new Pen(Enabled?ColorTranslator.FromHtml("#a95736"):DesktopTheme.Muted,1.9f*s)){pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;if(CheckState==CheckState.Indeterminate)g.DrawLine(pen,box.X+4*s,box.Y+8*s,box.X+12*s,box.Y+8*s);else g.DrawLines(pen,new[]{new PointF(box.X+3.7f*s,box.Y+8*s),new PointF(box.X+6.5f*s,box.Y+10.8f*s),new PointF(box.X+12.3f*s,box.Y+5.2f*s)});}
            if(Text.Length>0)TextRenderer.DrawText(g,Text,Font,new Rectangle(DesktopTheme.Px(this,24),0,Math.Max(1,Width-DesktopTheme.Px(this,24)),Height),Enabled?ForeColor:DesktopTheme.Muted,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
            if(Focused&&ShowFocusCues){var focus=box;focus.Inflate(2*s,2*s);using(var shape=DesktopTheme.Round(focus,6*s))using(var pen=new Pen(ColorTranslator.FromHtml("#d98d64"),s)){pen.DashStyle=DashStyle.Dot;g.DrawPath(pen,shape);}}
        }
        protected override void OnMouseEnter(EventArgs e){hovered=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hovered=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnEnabledChanged(EventArgs e){Invalidate();base.OnEnabledChanged(e);}
        protected override void OnCheckedChanged(EventArgs e){Invalidate();base.OnCheckedChanged(e);}
        protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
        protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
    }
    // Retain native focus, keyboard and accessibility behavior for every button.
    public class WidgetButton : Button {
        public bool Primary {get;set;}
        public bool Quiet {get;set;}
        public bool Plus {get;set;}
        protected bool hovered,pressed;
        public WidgetButton(){
            FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            // ButtonBase marks buttons opaque. Our rounded shapes leave the
            // corners unpainted, so every WM_PAINT must clear the background.
            // WM_PRINT/DrawToBitmap hid the stale pixels visible on the desktop.
            SetStyle(ControlStyles.Opaque,false);
            Cursor=Cursors.Hand;UseMnemonic=false;
        }
        protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(Parent==null?BackColor:Parent.BackColor);}
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;float s=DeviceDpi/96f;bool arrow=Text=="‹"||Text=="›",cross=Text=="×";float shadow=Primary&&!pressed?2*s:0;
            var rect=new RectangleF(s,s,Math.Max(1,Width-2*s-shadow-1),Math.Max(1,Height-2*s-shadow-1));if(pressed)rect.Offset(s,s);float radius=arrow||Quiet?rect.Height/2:8*s;var ink=Enabled?ForeColor:DesktopTheme.Muted;
            if(shadow>0){var cast=rect;cast.Offset(shadow,shadow);using(var shape=DesktopTheme.Round(cast,radius))using(var brush=new SolidBrush(DesktopTheme.Ink))g.FillPath(brush,shape);}
            Color fill=Primary?(hovered?ColorTranslator.FromHtml("#ff865f"):DesktopTheme.Orange):(hovered?ColorTranslator.FromHtml("#f9ecd6"):DesktopTheme.Card);if(pressed)fill=ColorTranslator.FromHtml(Primary?"#f46740":"#f1e2c8");
            if(!Quiet||hovered||pressed)using(var shape=DesktopTheme.Round(rect,radius)){using(var brush=new SolidBrush(fill))g.FillPath(brush,shape);if(!Quiet)using(var pen=new Pen(Enabled?DesktopTheme.Ink:DesktopTheme.Line,s))g.DrawPath(pen,shape);}
            float cx=rect.X+rect.Width/2,cy=rect.Y+rect.Height/2;
            using(var pen=new Pen(ink,1.6f*s)){pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
                if(arrow){float dir=Text=="‹"?-1:1;g.DrawLines(pen,new[]{new PointF(cx-dir*2*s,cy-4*s),new PointF(cx+dir*2*s,cy),new PointF(cx-dir*2*s,cy+4*s)});}
                else if(Text=="⚙"){
                    var teeth=new PointF[32];for(int i=0;i<teeth.Length;i++){double angle=i*Math.PI/16;float toothRadius=(i%4<2?8:6.2f)*s;teeth[i]=new PointF(cx+(float)Math.Cos(angle)*toothRadius,cy+(float)Math.Sin(angle)*toothRadius);}g.DrawPolygon(pen,teeth);g.DrawEllipse(pen,cx-2.6f*s,cy-2.6f*s,5.2f*s,5.2f*s);
                }
                else if(cross){g.DrawLine(pen,cx-4*s,cy-4*s,cx+4*s,cy+4*s);g.DrawLine(pen,cx+4*s,cy-4*s,cx-4*s,cy+4*s);}
                else {var text=Rectangle.Round(rect);if(Plus){float px=rect.Left+15*s;g.DrawLine(pen,px-4*s,cy,px+4*s,cy);g.DrawLine(pen,px,cy-4*s,px,cy+4*s);text.X+=DesktopTheme.Px(this,21);text.Width-=DesktopTheme.Px(this,25);}TextRenderer.DrawText(g,Text,Font,text,ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);}
            }
            if(Focused&&ShowFocusCues){var focus=rect;focus.Inflate(-3*s,-3*s);using(var shape=DesktopTheme.Round(focus,Math.Max(2,radius-3*s)))using(var pen=new Pen(ink,s)){pen.DashStyle=DashStyle.Dot;g.DrawPath(pen,shape);}}
        }
        protected override void OnMouseEnter(EventArgs e){hovered=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hovered=false;pressed=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left)pressed=true;Invalidate();base.OnMouseDown(e);}
        protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
        protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Space)pressed=true;Invalidate();base.OnKeyDown(e);}
        protected override void OnKeyUp(KeyEventArgs e){pressed=false;Invalidate();base.OnKeyUp(e);}
        protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
        protected override void OnLostFocus(EventArgs e){pressed=false;Invalidate();base.OnLostFocus(e);}
    }
    public sealed class WidgetEventButton : WidgetButton {
        Occurrence occurrence;bool done;
        public Occurrence Occurrence {get{return occurrence;}}
        public Color SurfaceColor {get{return done?ColorTranslator.FromHtml(hovered?"#e3eadc":"#edf1e8"):(hovered?ColorTranslator.FromHtml("#fff8e9"):DesktopTheme.Card);}}
        public void Update(Occurrence value,bool completed){occurrence=value;done=completed;Invalidate(true);}
        protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);Invalidate(true);}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);Invalidate(true);}
        public WidgetEventButton(Occurrence value,bool completed){occurrence=value;done=completed;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;}
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;float s=DeviceDpi/96f;var rect=new RectangleF(s,s,Math.Max(1,Width-3*s),Math.Max(1,Height-3*s));
            using(var shape=DesktopTheme.Round(rect,8*s)){using(var fill=new SolidBrush(SurfaceColor))g.FillPath(fill,shape);using(var pen=new Pen(Focused&&ShowFocusCues?DesktopTheme.Orange:DesktopTheme.Line,s))g.DrawPath(pen,shape);}
            using(var pen=new Pen(DesktopTheme.Category(occurrence.@event.category),3*s)){pen.StartCap=pen.EndCap=LineCap.Round;g.DrawLine(pen,3*s,11*s,3*s,Height-12*s);}
            string time=occurrence.@event.startTime==""?"종일":occurrence.@event.startTime+(occurrence.@event.endTime==""?"":" – "+occurrence.@event.endTime);
            var flags=TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter;
            int right=DesktopTheme.Px(this,12),left=DesktopTheme.Px(this,40),category=DesktopTheme.Px(this,36);
            using(var small=new Font(Font.FontFamily,9+DesktopTheme.TextScale))TextRenderer.DrawText(g,time,small,new Rectangle(left,DesktopTheme.Px(this,7),Math.Max(1,Width-left-right-category),DesktopTheme.Px(this,18)),DesktopTheme.Muted,flags);
            using(var small=new Font(Font.FontFamily,9+DesktopTheme.TextScale))TextRenderer.DrawText(g,DesktopTheme.CategoryName(occurrence.@event.category),small,new Rectangle(Math.Max(left,Width-right-category),DesktopTheme.Px(this,7),category,DesktopTheme.Px(this,18)),DesktopTheme.Muted,flags|TextFormatFlags.Right);
            using(var title=new Font(Font.FontFamily,10+DesktopTheme.TextScale,FontStyle.Bold|(done?FontStyle.Strikeout:FontStyle.Regular)))TextRenderer.DrawText(g,occurrence.@event.title,title,new Rectangle(left,DesktopTheme.Px(this,29),Math.Max(1,Width-left-right),DesktopTheme.Px(this,24)),done?DesktopTheme.Muted:DesktopTheme.Ink,flags);
        }
    }
    public sealed class WidgetDday : WidgetButton {
        bool chosen;string badge="D-DAY",title="눌러서 중요한 일정 선택";
        public WidgetDday(){Quiet=true;AccessibleName="중요한 일정 D-day";}
        public void SetTarget(Occurrence target){chosen=target!=null;badge=target==null?"D-DAY":Rules.Dday(target.startDate,Rules.Key(DateTime.Today));title=target==null?"눌러서 중요한 일정 선택":target.@event.title;AccessibleName=badge+" · "+title;Cursor=Cursors.Hand;Invalidate();}
        protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;float s=DeviceDpi/96f;var rect=new RectangleF(s,s,Math.Max(1,Width-3*s),Math.Max(1,Height-3*s));using(var shape=DesktopTheme.Round(rect,8*s)){using(var fill=new SolidBrush(ColorTranslator.FromHtml(chosen?(hovered?"#ffe4eb":"#ffedf2"):(hovered?"#f9ecd6":"#fff9ee"))))g.FillPath(fill,shape);using(var pen=new Pen(Focused&&ShowFocusCues?DesktopTheme.Orange:(chosen?ColorTranslator.FromHtml("#e5adbc"):DesktopTheme.Line),s))g.DrawPath(pen,shape);}int badgeWidth;using(var bold=new Font(Font.FontFamily,11+DesktopTheme.TextScale,FontStyle.Bold)){badgeWidth=Math.Min(DesktopTheme.Px(this,112),TextRenderer.MeasureText(badge,bold).Width+DesktopTheme.Px(this,18));TextRenderer.DrawText(g,badge,bold,new Rectangle(DesktopTheme.Px(this,7),0,badgeWidth,Height),ColorTranslator.FromHtml("#9e4f68"),TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis);}TextRenderer.DrawText(g,title,Font,new Rectangle(badgeWidth+DesktopTheme.Px(this,15),0,Math.Max(1,Width-badgeWidth-DesktopTheme.Px(this,27)),Height),DesktopTheme.Ink,TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);}
    }
    public sealed class CalendarGrid : Control {
        CalendarEvent[] cachedEvents;string selected,month,clicked;DateTime first;readonly Dictionary<string,int> counts=new Dictionary<string,int>();
        public int WeekCount {get;private set;}
        public Action<string> SelectDate,AddDate;
        int hovered=-1;
        public CalendarGrid(){DoubleBuffered=true;WeekCount=5;SetStyle(ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);TabStop=true;AccessibleName="위젯 월간 달력";Cursor=Cursors.Hand;}
        public void UpdateMonth(string value,string date,CalendarEvent[] events){if(month==value&&cachedEvents==events){if(selected!=date){selected=date;Invalidate();}return;}cachedEvents=events;month=value;selected=date;var start=Rules.Date(month+"-01");first=start.AddDays(-(int)start.DayOfWeek);WeekCount=((int)start.DayOfWeek+DateTime.DaysInMonth(start.Year,start.Month)+6)/7;counts.Clear();foreach(var o in Rules.Occurrences(events,Rules.Key(first),Rules.Key(first.AddDays(WeekCount*7-1)),false))for(var day=Rules.Date(o.startDate)>first?Rules.Date(o.startDate):first;day<=Rules.Date(o.endDate)&&day<first.AddDays(WeekCount*7);day=day.AddDays(1)){string key=Rules.Key(day);int n;counts.TryGetValue(key,out n);counts[key]=n+1;}Invalidate();}
        int Heading {get{return DesktopTheme.Px(this,24);}}
        Rectangle Cell(int i){int col=i%7,row=i/7;int x=col*Width/7,right=(col+1)*Width/7,y=Heading+row*(Height-Heading)/WeekCount,bottom=Heading+(row+1)*(Height-Heading)/WeekCount;return new Rectangle(x,y,right-x,bottom-y);}
        protected override void OnPaint(PaintEventArgs e){
            base.OnPaint(e);if(month==null||Height<=Heading)return;var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;float s=DeviceDpi/96f;var flags=TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix;
            for(int d=0;d<7;d++){var cell=Cell(d);TextRenderer.DrawText(g,new[]{"일","월","화","수","목","금","토"}[d],Font,new Rectangle(cell.X,0,cell.Width,Heading),d==0?ColorTranslator.FromHtml("#cf6154"):d==6?ColorTranslator.FromHtml("#5d8db3"):DesktopTheme.Muted,flags);}
            using(var pen=new Pen(DesktopTheme.Line,s))g.DrawLine(pen,0,Heading-1,Width,Heading-1);
            for(int i=0;i<WeekCount*7;i++){
                string key=Rules.Key(first.AddDays(i));var cell=Cell(i);var rect=cell;rect.Inflate(-DesktopTheme.Px(this,2),-DesktopTheme.Px(this,2));if(rect.Width<=0||rect.Height<=0)continue;
                bool active=key==selected,isToday=key==Rules.Key(DateTime.Today),outside=!key.StartsWith(month,StringComparison.Ordinal);int count;counts.TryGetValue(key,out count);
                if(active||i==hovered)using(var shape=DesktopTheme.Round(rect,7*s)){using(var fill=new SolidBrush(ColorTranslator.FromHtml(active?"#fff1d1":"#f9f3e7")))g.FillPath(fill,shape);if(active)using(var pen=new Pen(isToday?DesktopTheme.Orange:ColorTranslator.FromHtml("#e6b259"),s))g.DrawPath(pen,shape);}
                int diameter=Math.Min(DesktopTheme.Px(this,23),Math.Max(1,rect.Height-DesktopTheme.Px(this,7)));var number=new Rectangle(rect.X+(rect.Width-diameter)/2,rect.Y,diameter,diameter);
                Color ink=outside?ColorTranslator.FromHtml("#b7ad9c"):i%7==0?ColorTranslator.FromHtml("#cf6154"):i%7==6?ColorTranslator.FromHtml("#5d8db3"):DesktopTheme.Ink;
                if(isToday){using(var brush=new SolidBrush(DesktopTheme.Orange))g.FillEllipse(brush,number);ink=Color.White;}
                TextRenderer.DrawText(g,first.AddDays(i).Day.ToString(),Font,number,ink,flags);
                if(count>0){float dot=3*s;using(var brush=new SolidBrush(outside?DesktopTheme.Line:DesktopTheme.Orange))g.FillEllipse(brush,rect.X+rect.Width/2f-dot/2,rect.Bottom-4*s,dot,dot);}
                if(active&&Focused&&ShowFocusCues){var focus=rect;focus.Inflate(-2,-2);ControlPaint.DrawFocusRectangle(g,focus);}
            }
        }
        int IndexAt(Point point){if(month==null||Height<=Heading||point.Y<Heading||point.X<0||point.X>=Width||point.Y>=Height)return -1;for(int i=0;i<WeekCount*7;i++)if(Cell(i).Contains(point))return i;return -1;}
        public object[] InspectCells(){return Enumerable.Range(0,WeekCount*7).Select(i=>{var c=Cell(i);return (object)new{date=Rules.Key(first.AddDays(i)),x=c.X,y=c.Y,width=c.Width,height=c.Height,hit=IndexAt(new Point(c.X+c.Width/2,c.Y+c.Height/2))==i};}).ToArray();}
        internal void AutomationClick(Point point,bool twice){OnMouseClick(new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0));if(twice)OnMouseDoubleClick(new MouseEventArgs(MouseButtons.Left,2,point.X,point.Y,0));}
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);int next=IndexAt(e.Location);if(next!=hovered){hovered=next;Invalidate();}}
        protected override void OnMouseLeave(EventArgs e){hovered=-1;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);if(e.Button!=MouseButtons.Left)return;Focus();int i=IndexAt(e.Location);clicked=i<0?null:Rules.Key(first.AddDays(i));if(clicked!=null&&SelectDate!=null)SelectDate(clicked);}
        // The first click on an adjacent-month date can redraw the whole grid.
        // Keep that date for the second click instead of hitting the new month.
        protected override void OnMouseDoubleClick(MouseEventArgs e){base.OnMouseDoubleClick(e);if(e.Button!=MouseButtons.Left)return;int i=IndexAt(e.Location);string key=clicked??(i<0?null:Rules.Key(first.AddDays(i)));if(key!=null&&AddDate!=null)AddDate(key);}
        protected override bool IsInputKey(Keys keyData){return keyData==Keys.Left||keyData==Keys.Right||keyData==Keys.Up||keyData==Keys.Down||base.IsInputKey(keyData);}
        protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(selected==null)return;int days=e.KeyCode==Keys.Left?-1:e.KeyCode==Keys.Right?1:e.KeyCode==Keys.Up?-7:e.KeyCode==Keys.Down?7:0;if(days!=0&&SelectDate!=null){SelectDate(Rules.Key(Rules.Date(selected).AddDays(days)));e.Handled=true;}else if(e.KeyCode==Keys.Enter&&AddDate!=null){AddDate(selected);e.Handled=true;}}
    }
}
