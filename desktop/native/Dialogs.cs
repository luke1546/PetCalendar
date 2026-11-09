using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PetCalendarDesktop {
    static class Css {
        public static float Px(float css){return css+(1+DesktopTheme.TextScale)*4f/3f;}
        public static TextItem P(string text,float size=12,Color? color=null,float lh=1.8f){var t=new TextItem(text,size,400,color??Ui.C("#8c795a"),lh);float m=Px(size);t.Top=m;t.Bottom=m;return t;}
        public static TextItem Hint(string text){var t=new TextItem(text,11,400,Ui.C("#79694f"),1.7f);float m=Px(11);t.Top=m;t.Bottom=m;return t;}
        public static TextItem H3(string text){var t=new TextItem(text,14,700,DesktopTheme.Ink);t.Bottom=10;return t;}
        public static ThemeCheckBox Check(string text,float size=12,bool on=false){return new ThemeCheckBox{Text=text,Checked=on,Font=Ui.Font(1,size,400,null,text),BackColor=DesktopTheme.Card,ForeColor=DesktopTheme.Ink,AutoSize=false};}
        public static Btn Button(string text,BtnKind kind=BtnKind.Normal,float font=11,string icon=null){var b=new Btn(kind,text,icon){FontCss=font};return b;}
        public static SelectField Select(params string[] pairs){var s=new SelectField();for(int i=0;i+1<pairs.Length;i+=2)s.Add(pairs[i],pairs[i+1]);return s;}
        public static SelectField Reminders(){return Select("null","알림 없음","0","정시","10","10분 전","30","30분 전","60","1시간 전");}
        public static string ReminderKey(int? value){return value.HasValue?value.Value.ToString():"null";}
    }
    // Checkbox whose font follows DPI and the text scale (CSS px, not points).
    public sealed class CssCheck : FlowItem {
        public readonly ThemeCheckBox Box;readonly ControlItem item;readonly float size;
        public CssCheck(ThemeCheckBox box,float css,float top=0,float bottom=0){Box=box;box.BoxCss=15;box.BoxLeft=0;box.TextLeft=20;size=css;item=new ControlItem(box,false);Top=top;Bottom=bottom;}
        public override void Attach(FlowPanel p){item.Attach(p);}
        public override void SetShown(bool shown){Box.Visible=shown;}
        public override int Measure(FlowPanel p,int width){Box.Font=Ui.Font(p.Sc,size,400,null,Box.Text);return Math.Max((int)Math.Round(16*p.Sc),(int)Math.Round(Box.Font.GetHeight()));}
        public override void Arrange(FlowPanel p,Rectangle r){Bounds=new Rectangle(r.X,r.Y,Math.Min(r.Width,(int)Math.Round(Box.TextLeft*p.Sc)+Ui.Width(Box.Text,Box.Font)+(int)(10*p.Sc)),r.Height);item.Bounds=Bounds;}
        public override void Position(FlowPanel p,int scroll){item.Position(p,scroll);}
    }
    // Radio chip used by the category fieldset.
    public sealed class ChoiceChip : Control {
        public string Key;public string Category;public List<ChoiceChip> Group;bool on,hover;public event EventHandler Picked;
        public ChoiceChip(string key,string label,List<ChoiceChip> group){Key=key;Category=key;Text=label;Group=group;group.Add(this);SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Selectable|ControlStyles.ResizeRedraw,true);TabStop=true;Cursor=Cursors.Hand;}
        public bool Checked{get{return on;}set{on=value;Invalidate();}}
        float Sc{get{return DeviceDpi/96f;}}
        public Size Preferred(){float s=Sc;var f=Ui.Font(s,11,700,null,Text);return new Size((int)Math.Ceiling(Ui.Width(Text,f)+14*s+24*s+2*s),(int)Math.Round(f.GetHeight()+16*s+2*s));}
        public void Pick(){foreach(var c in Group)c.Checked=c==this;if(Picked!=null)Picked(this,EventArgs.Empty);}
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;float s=Sc;g.Clear(DesktopTheme.Card);Ui.Prepare(g);var r=new RectangleF(0,0,Width,Height);
            if(on)Ui.Box(g,r,7*s,Ui.C(CalendarView.CategoryLight(Category)),DesktopTheme.Ink,1.5f*s);else Ui.Box(g,r,7*s,hover?Ui.C("#fffaf1"):DesktopTheme.Card,Ui.C("#ddceb4"),s);
            int x=(int)Math.Round(12*s+s);using(var b=new SolidBrush(DesktopTheme.Category(Category)))g.FillEllipse(b,x,(Height-8*s)/2,8*s,8*s);
            Ui.Text(g,Text,Ui.Font(s,11,on?700:400,null,Text),new Rectangle(x+(int)(14*s),0,Width,Height),DesktopTheme.Ink,TextFormatFlags.VerticalCenter);
            if(Focused&&ShowFocusCues){var ring=r;ring.Inflate(-1,-1);using(var pen=new Pen(DesktopTheme.Orange,2*s))using(var p=Ui.Round(ring,7*s))g.DrawPath(pen,p);}
        }
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);Focus();Pick();}
        protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
        protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
        protected override bool IsInputKey(Keys k){return k==Keys.Left||k==Keys.Right||k==Keys.Up||k==Keys.Down||base.IsInputKey(k);}
        protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);int i=Group.IndexOf(this);int n=e.KeyCode==Keys.Left||e.KeyCode==Keys.Up?-1:e.KeyCode==Keys.Right||e.KeyCode==Keys.Down?1:0;if(n!=0){var next=Group[(i+n+Group.Count)%Group.Count];next.Focus();next.Pick();e.Handled=true;}else if(e.KeyCode==Keys.Space){Pick();e.Handled=true;}}
    }
    // .search-box used inside dialogs.
    public sealed class SearchField : Control {
        public readonly CueTextBox Box=new CueTextBox();
        public SearchField(string placeholder){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Box.Placeholder=placeholder;Box.MaxLength=200;Box.BackColor=Ui.C("#f6f0e3");Box.GotFocus+=delegate{Invalidate();};Box.LostFocus+=delegate{Invalidate();};Controls.Add(Box);Cursor=Cursors.IBeam;}
        float Sc{get{return DeviceDpi/96f;}}
        public int PreferredHeight(){return (int)Math.Ceiling(Ui.Font(Sc,11,400).GetHeight()+16*Sc+2*Sc);}
        protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);float s=Sc;Box.Font=Ui.Font(s,11,400);int x=(int)Math.Round(10*s+15*s+6*s);int h=Box.PreferredHeight;Box.SetBounds(x,(Height-h)/2,Math.Max(1,Width-x-(int)(10*s)),h);}
        protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;float s=Sc;g.Clear(DesktopTheme.Card);Ui.Prepare(g);Ui.Box(g,new RectangleF(0,0,Width,Height),7*s,Ui.C("#f6f0e3"),Box.Focused?DesktopTheme.Ink:Ui.C("#dfd4bf"),s);Ui.Icon(g,"search",new RectangleF(10*s,(Height-15*s)/2,15*s,15*s),Ui.C("#978b76"));}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);Box.Focus();}
    }
    public sealed class SearchItem : FlowItem {
        public readonly SearchField Field;readonly ControlItem item;
        public SearchItem(SearchField field){Field=field;item=new ControlItem(field){FixedHeight=0};}
        public override void Attach(FlowPanel p){item.Attach(p);}
        public override int Measure(FlowPanel p,int width){return Field.PreferredHeight();}
        public override void Arrange(FlowPanel p,Rectangle r){Bounds=r;item.Bounds=r;}
        public override void Position(FlowPanel p,int scroll){item.Position(p,scroll);}
    }
    // ---------------- Event editor ----------------
    public sealed class EventDialog : DialogForm {
        readonly CalendarEvent original;readonly string occurrence;readonly bool duplicate;readonly string editingId;
        public readonly InputBox TitleBox=new InputBox(false,16,600){PadX=12,PadY=13,MaxLength=200,Placeholder="예: 주간 회의, 보고서 제출"};
        readonly DateField start=new DateField(),end=new DateField(),until=new DateField();readonly TimeField startTime=new TimeField(),endTime=new TimeField();
        readonly ThemeCheckBox allDay=Css.Check("종일 일정",11,true),pinned=Css.Check("이 일정을 D-day로 표시",12);
        readonly SelectField repeat=Css.Select("none","반복 안 함","daily","매일","weekly","매주","monthly","매월 같은 날짜","monthly-weekday","매월 순번·요일"),reminder=Css.Reminders();
        readonly SelectField week=Css.Select("1","첫째","2","둘째","3","셋째","4","넷째","-1","마지막"),weekday=Css.Select("0","일요일","1","월요일","2","화요일","3","수요일","4","목요일","5","금요일","6","토요일");
        readonly InputBox note=new InputBox(true){MaxLength=5000,Placeholder="장소나 준비할 내용을 적어두세요"};
        readonly List<ChoiceChip> categories=new List<ChoiceChip>();
        readonly Btn delete=new Btn(BtnKind.Danger,"삭제"),copy=new Btn(BtnKind.Normal,"복제"),cancel=new Btn(BtnKind.Normal,"취소"),save=new Btn(BtnKind.Primary,"일정 저장");
        readonly TextItem error=new TextItem("",11,400,Ui.C("#bf4c3b"),1.8f){Top=8,Bottom=8};
        FlowItem notice,timeRow,weekdayRow,untilField;string draftKey,baseline;readonly Timer draftTimer=new Timer{Interval=300};bool loading;
        public EventDialog(CalendarView view,CalendarEvent source,string key,bool copyMode):base(view,copyMode?"일정 복제":source!=null?"일정 수정":"새 일정","나의 새로운 계획"){
            original=source;occurrence=key;duplicate=copyMode;editingId=copyMode?null:(source!=null?source.id:null);WidthCss=550;
            var e=source??new CalendarEvent{title="",startDate=key,endDate=key,startTime="",endTime="",category="work",note="",repeat="none",repeatUntil="",reminder=view.state.settings.defaultReminder,pinned=false};
            var noticeBox=new BoxItem{Fill=Ui.C("#ffedc4"),Radius=7,PadTop=10,PadBottom=10,PadLeft=12,PadRight=12,Top=-8,Bottom=18};noticeBox.Add(new TextItem("반복 일정입니다. 저장하거나 삭제할 때 적용 범위를 선택할 수 있습니다.",11,400,Ui.C("#95702f"),1.8f));notice=Body.Add(noticeBox);
            notice.Visible=source!=null&&source.repeat!="none"&&!copyMode;
            Body.Add(new FieldItem("어떤 일정인가요?",TitleBox));
            var dates=new RowItem(15,new FieldItem("시작일",start),new FieldItem("종료일",end));Body.Add(dates);
            Body.Add(new CssCheck(allDay,11,-2,16));
            timeRow=Body.Add(new RowItem(15,new FieldItem("시작 시간",startTime),new FieldItem("종료 시간",endTime,"선택")));
            var legend=new TextItem("분류",11,700,DesktopTheme.Ink){Bottom=8};var chips=new InlineItem(8){Bottom=18};
            foreach(var c in new[]{"work","meeting","deadline","personal"}){var chip=new ChoiceChip(c,DesktopTheme.CategoryName(c),categories);chip.Picked+=delegate{Draft();};var ci=new ControlItem(chip,false);chips.Parts.Add(ci);}
            Body.Add(legend);Body.Add(chips);
            Body.Add(new RowItem(15,new FieldItem("반복",repeat),new FieldItem("알림",reminder)));
            weekdayRow=Body.Add(new RowItem(15,new FieldItem("순번",week),new FieldItem("요일",weekday)));
            untilField=Body.Add(new FieldItem("반복 종료일",until,"비워두면 계속 반복 · 시작일 이후부터 선택한 규칙 적용"));
            Body.Add(new FieldItem("메모",note,"선택"));
            Body.Add(new CssCheck(pinned,12,0,14));
            var allDayHint=Css.Hint("종일 일정 알림은 오전 9시 기준입니다. 캘린더를 열어둔 동안 알려드려요.");allDayHint.Bottom=0;Body.Add(allDayHint); // <p> bottom margin collapses into the footer's 20px
            Body.Add(error);error.Visible=false;
            Footer(new Control[]{delete,copy},new Control[]{cancel,save});SetFooterVisible(delete,editingId!=null);SetFooterVisible(copy,editingId!=null);
            delete.Click+=delegate{View.DeleteSchedule(editingId,true,occurrence,this);};copy.Click+=delegate{View.OpenEvent(editingId,View.selected,true);};cancel.Click+=delegate{Cancel();};save.Click+=delegate{Submit();};
            // Populate, mirroring openEvent() in app.mjs.
            loading=true;
            TitleBox.Text=e.title+(copyMode?" (복사)":"");
            int shift=source!=null&&!copyMode&&e.repeat!="none"?(Rules.Date(key)-Rules.Date(e.startDate)).Days:0;
            start.Value=Rules.AddDays(e.startDate,shift);end.Value=Rules.AddDays(e.endDate,shift);
            startTime.Value=e.startTime;endTime.Value=e.endTime;allDay.Checked=e.startTime=="";
            repeat.Value=e.repeat.StartsWith("monthly:")?"monthly-weekday":e.repeat;until.Value=e.repeatUntil;
            if(e.repeat.StartsWith("monthly:")){var p=e.repeat.Split(':');week.Value=p[1];weekday.Value=p[2];}
            else{week.Value=Math.Min(4,(Rules.Date(e.startDate).Day+6)/7).ToString();weekday.Value=((int)Rules.Date(e.startDate).DayOfWeek).ToString();}
            reminder.Value=Css.ReminderKey(e.reminder);note.Text=e.note;pinned.Checked=e.pinned&&!copyMode;
            foreach(var c in categories)c.Checked=c.Key==e.category;
            SyncVisibility();
            draftKey=copyMode?"duplicate:"+source.id:source!=null?"edit:"+source.id:"new:"+key;baseline=Codec.Json(Snapshot());
            string saved;if(View.prefs.Value.drafts.TryGetValue(draftKey,out saved)){try{Restore(Codec.Object(saved));SyncVisibility();View.Toast("작성 중이던 내용을 불러왔어요","저장하거나 내용을 수정해 이어서 작성하세요.");}catch{}}
            loading=false;
            allDay.CheckedChanged+=delegate{if(!allDay.Checked&&startTime.Value=="")startTime.Value="09:00";SyncVisibility();Draft();};
            start.ValueChanged+=delegate{if(end.Value==""||String.CompareOrdinal(end.Value,start.Value)<0)if(start.Value!="")end.Value=start.Value;Draft();};
            repeat.ValueChanged+=delegate{SyncVisibility();Draft();};
            foreach(var c in new Control[]{end,until,startTime,endTime,reminder,week,weekday})Hook(c);
            TitleBox.ValueChanged+=delegate{Draft();};note.ValueChanged+=delegate{Draft();};pinned.CheckedChanged+=delegate{Draft();};
            draftTimer.Tick+=delegate{draftTimer.Stop();StoreDraft();};
            foreach(var input in new[]{TitleBox.Box})input.KeyDown+=delegate(object sender,KeyEventArgs k){if(k.KeyCode==Keys.Enter){k.Handled=k.SuppressKeyPress=true;Submit();}};
            foreach(var field in new SegmentField[]{start,end,until,startTime,endTime})field.KeyDown+=delegate(object sender,KeyEventArgs k){if(k.KeyCode==Keys.Enter){k.Handled=true;Submit();}};
        }
        void Hook(Control c){var s=c as SegmentField;if(s!=null)s.ValueChanged+=delegate{Draft();};var sel=c as SelectField;if(sel!=null)sel.ValueChanged+=delegate{Draft();};}
        void SyncVisibility(){timeRow.Visible=!allDay.Checked;untilField.Visible=repeat.Value!="none";weekdayRow.Visible=repeat.Value=="monthly-weekday";if(IsHandleCreated)Relayout();}
        string Category{get{var c=categories.FirstOrDefault(x=>x.Checked);return c==null?"work":c.Key;}}
        Dictionary<string,object> Snapshot(){return new Dictionary<string,object>{{"title",TitleBox.Text},{"startDate",start.Value},{"endDate",end.Value},{"allDay",allDay.Checked},{"startTime",startTime.Value},{"endTime",endTime.Value},{"category",Category},{"repeat",repeat.Value},{"week",week.Value},{"weekday",weekday.Value},{"repeatUntil",until.Value},{"reminder",reminder.Value},{"note",note.Text},{"pinned",pinned.Checked}};}
        void Restore(Dictionary<string,object> d){
            Func<string,string> str=k=>d.ContainsKey(k)?d[k] as string??"":"";Func<string,bool> flag=k=>d.ContainsKey(k)&&d[k] is bool&&(bool)d[k];
            TitleBox.Text=str("title");start.Value=str("startDate");end.Value=str("endDate");allDay.Checked=flag("allDay");startTime.Value=str("startTime");endTime.Value=str("endTime");
            foreach(var c in categories)c.Checked=c.Key==str("category");repeat.Value=str("repeat");week.Value=str("week");weekday.Value=str("weekday");until.Value=str("repeatUntil");reminder.Value=str("reminder");note.Text=str("note");pinned.Checked=flag("pinned");
        }
        void Draft(){if(loading)return;draftTimer.Stop();draftTimer.Start();}
        void StoreDraft(){if(draftKey==null)return;string data=Codec.Json(Snapshot());if(data==baseline)View.prefs.Value.drafts.Remove(draftKey);else View.prefs.Value.drafts[draftKey]=data;View.prefs.Save();}
        void ClearDraft(){draftTimer.Stop();if(draftKey!=null){View.prefs.Value.drafts.Remove(draftKey);View.prefs.Save();}draftKey=null;}
        public override void SavingChanged(){save.Enabled=!View.Saving;}
        public void Finish(){ClearDraft();CloseDialog();}
        public override void Cancel(){if(View.Saving)return;if(draftTimer.Enabled){draftTimer.Stop();StoreDraft();}CloseDialog();}
        void ShowError(string text){error.Set(text);error.Visible=text.Length>0;Relayout();Body.ScrollIntoView(error.Bounds);}
        async void Submit(){
            if(View.Saving)return;
            if(TitleBox.Text.Trim().Length==0){Validation.Report(TitleBox,"이 입력란을 작성하세요.");return;}
            if(start.Value==""){Validation.Report(start,"이 입력란을 작성하세요.");return;}
            if(end.Value==""){Validation.Report(end,"이 입력란을 작성하세요.");return;}
            if(!allDay.Checked&&startTime.Value==""){Validation.Report(startTime,"이 입력란을 작성하세요.");return;}
            var state=View.state;var existing=editingId==null?null:state.events.FirstOrDefault(x=>x.id==editingId);
            var item=new CalendarEvent{id=editingId??Guid.NewGuid().ToString(),title=TitleBox.Text.Trim(),startDate=start.Value,endDate=end.Value,startTime=allDay.Checked?"":startTime.Value,endTime=allDay.Checked?"":endTime.Value,
                category=Category,note=note.Text,repeat=repeat.Value=="monthly-weekday"?"monthly:"+week.Value+":"+weekday.Value:repeat.Value,repeatUntil=repeat.Value=="none"?"":until.Value,
                reminder=reminder.Value=="null"?(int?)null:int.Parse(reminder.Value),pinned=pinned.Checked,
                pinnedDate=existing!=null&&existing.pinnedDate!=""?existing.pinnedDate:(occurrence??start.Value),
                completedDates=existing!=null?existing.completedDates:new string[0],excludedDates=existing!=null?existing.excludedDates:new string[0]};
            if(existing!=null&&existing.repeat!="none"){
                string scope=await View.RepeatScope("수정",occurrence);if(scope==null)return;
                if(scope=="one"){
                    item.id=Guid.NewGuid().ToString();item.repeat="none";item.repeatUntil="";item.excludedDates=new string[0];item.completedDates=existing.completedDates.Contains(occurrence)?new[]{item.startDate}:new string[0];
                    item.pinned=pinned.Checked&&(!existing.pinned||existing.pinnedDate==occurrence);item.pinnedDate=item.pinned?item.startDate:"";
                    try{var probe=CalendarView.Clone(state);probe.events=probe.events.Concat(new[]{item}).ToArray();Rules.Validate(Codec.Json(probe));}catch(Exception x){ShowError(x.Message);return;}
                    string occ=occurrence;if(await View.Commit(s=>{var series=s.events.First(x=>x.id==existing.id);series.excludedDates=series.excludedDates.Concat(new[]{occ}).Distinct().ToArray();if(series.pinned&&series.pinnedDate==occ){series.pinned=false;series.pinnedDate="";}if(item.pinned)foreach(var x in s.events){x.pinned=false;x.pinnedDate="";}s.events=s.events.Concat(new[]{item}).ToArray();})){Finish();View.Toast("이번 일정만 수정했어요",item.title);}
                    return;
                }
                int shift=(Rules.Date(occurrence)-Rules.Date(existing.startDate)).Days;item.startDate=Rules.AddDays(item.startDate,-shift);item.endDate=Rules.AddDays(item.endDate,-shift);
            }
            if(!item.pinned)item.pinnedDate="";else if(!Rules.IsDate(item.pinnedDate)||!Rules.OccursOn(item,item.pinnedDate))item.pinnedDate=item.startDate;
            try{var probe=CalendarView.Clone(state);probe.events=probe.events.Where(x=>x.id!=item.id).Concat(new[]{item}).ToArray();Rules.Validate(Codec.Json(probe));}catch(Exception x){ShowError(x.Message);return;}
            bool editing=editingId!=null;
            bool ok=await View.Commit(s=>{if(item.pinned)foreach(var x in s.events){x.pinned=false;x.pinnedDate="";}int index=Array.FindIndex(s.events,x=>x.id==item.id);if(index<0)s.events=s.events.Concat(new[]{item}).ToArray();else s.events[index]=item;});
            if(ok){Finish();View.Toast(editing?"일정을 수정했어요":"새 일정을 적어두었어요",item.title);}
            else ShowError(View.LastError);
        }
        protected override void Dispose(bool disposing){if(disposing)draftTimer.Dispose();base.Dispose(disposing);}
    }
    // ---------------- D-day picker ----------------
    public sealed class DdayDialog : DialogForm {
        public readonly SearchField SearchBox=new SearchField("중요한 일정 찾기");readonly ChoiceList list;readonly Btn clear=new Btn(BtnKind.Normal,"선택 해제"),open=new Btn(BtnKind.Primary,"선택한 일정 열기");
        public DdayDialog(CalendarView view):base(view,"D-day 일정 선택","중요한 날을 골라주세요"){
            Body.Add(Css.Hint("달력에 등록된 일정 중 하나를 골라주세요. 선택한 일정의 시작일을 기준으로 D-day가 표시됩니다."));
            var si=new SearchItem(SearchBox){Top=16,Bottom=12};Body.Add(si);
            list=new ChoiceList(view);Body.Add(new ListItem(list));
            Footer(new Control[]{clear},new Control[]{open});
            clear.Click+=delegate{View.ClearDday(this);};open.Click+=delegate{View.OpenDdayEvent(this);};
            SearchBox.Box.TextChanged+=delegate{View.limits["dday"]=CalendarView.pageSizes["dday"];list.Offset=0;Refresh2();};
            Refresh2();
        }
        void Refresh2(){list.Query=SearchBox.Box.Text;list.Rebuild();clear.Enabled=open.Enabled=View.deadlineTarget!=null;if(IsHandleCreated)Relayout();}
        public override void StateChanged(){Refresh2();}
        sealed class ListItem : FlowItem {
            readonly ChoiceList list;readonly ControlItem item;public ListItem(ChoiceList l){list=l;item=new ControlItem(l);}
            public override void Attach(FlowPanel p){item.Attach(p);}
            public override int Measure(FlowPanel p,int width){var form=p.FindForm() as DialogForm;int max=(int)Math.Round(Math.Min(380*p.Sc,list.ViewHeight()*0.45f));return Math.Max((int)(60*p.Sc),Math.Min(max,list.ContentHeight(width)));}
            public override void Arrange(FlowPanel p,Rectangle r){Bounds=r;item.Bounds=r;}
            public override void Position(FlowPanel p,int scroll){item.Position(p,scroll);}
        }
    }
    public sealed class ChoiceList : Control {
        readonly CalendarView view;public string Query="";List<Occurrence> rows=new List<Occurrence>();public int Offset;int hover=-1;bool hoverMore;readonly ScrollModel scroll=new ScrollModel();
        public ChoiceList(CalendarView owner){view=owner;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);TabStop=true;}
        float Sc{get{return DeviceDpi/96f;}}
        public int ViewHeight(){return view.ViewportScreen().Height;}
        public void Rebuild(){
            string q=Query.Trim().ToLower(Rules.Korean),today=Rules.Key(DateTime.Today);var target=view.deadlineTarget;
            rows=view.state.events.Where(e=>q.Length==0||(e.title+" "+e.note).ToLower(Rules.Korean).Contains(q)).Select(e=>Rules.DdayOccurrence(e,view.selected)).ToList();
            rows.Sort((a,b)=>{
                int c=(target!=null&&b.@event.id==target.@event.id).CompareTo(target!=null&&a.@event.id==target.@event.id);if(c!=0)return c;
                bool pa=String.CompareOrdinal(a.startDate,today)<0,pb=String.CompareOrdinal(b.startDate,today)<0;c=pa.CompareTo(pb);if(c!=0)return c;
                c=pa?String.CompareOrdinal(b.startDate,a.startDate):String.CompareOrdinal(a.startDate,b.startDate);if(c!=0)return Math.Sign(c);
                return IntlKoreanComparer.Instance.Compare(a.@event.title,b.@event.title);});
            Invalidate();
        }
        int TextWidth(int width){float s=Sc;return width-(int)Math.Round((8+24+20+12+12)*s)-Ui.Width("D−999",Ui.Font(s,13,700))-(int)(12*s);}
        int RowHeight(Occurrence o,int width){float s=Sc;int lines=Ui.Wrap(o.@event.title,Ui.Font(s,13,700,null,o.@event.title),TextWidth(width)).Count;return (int)Math.Ceiling(lines*Css.Px(13)*1.6f*s+4*s+Css.Px(11)*1.7f*s+26*s);}
        public int ContentHeight(int width){float s=Sc;if(rows.Count==0)return (int)Math.Ceiling(Css.Px(11)*1.8f*s+36*s);int h=(int)(8*s);foreach(var o in rows.Take(view.limits["dday"]))h+=RowHeight(o,width)+(int)(7*s);if(rows.Count>view.limits["dday"])h+=(int)Math.Ceiling(Ui.Font(s,12,400).GetHeight()+22*s+12*s);return h;}
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;float s=Sc;g.Clear(DesktopTheme.Card);Ui.Prepare(g);
            int width=Width;int content=ContentHeight(width);scroll.Content=content;scroll.Viewport=Height;scroll.Bounds=ClientRectangle;if(scroll.Visible){width-=(int)(12*s);content=ContentHeight(width);scroll.Content=content;}scroll.Offset=Offset;scroll.Clamp();Offset=scroll.Offset;
            string today=Rules.Key(DateTime.Today);var target=view.deadlineTarget;
            if(rows.Count==0){string text=Query.Trim().Length>0?"검색한 일정이 없어요.":"아직 등록한 일정이 없어요. 달력에 일정을 추가한 뒤 골라주세요.";Ui.Paragraph(g,text,Ui.Font(s,11,400,null,text),(int)(4*s),(int)(18*s)-Offset,width-(int)(8*s),Css.Px(11)*1.8f*s,Ui.C("#a29276"),false,TextFormatFlags.HorizontalCenter);}
            int y=(int)(4*s)-Offset;int i=0;
            foreach(var o in rows.Take(view.limits["dday"])){
                int h=RowHeight(o,width);var r=new RectangleF(4*s,y,width-8*s,h);bool chosen=target!=null&&target.@event.id==o.@event.id&&target.startDate==o.startDate;
                Ui.Box(g,r,8*s,chosen?Ui.C("#ffedf2"):i==hover?Ui.C(CalendarView.CategoryLight(o.@event.category)):Ui.C("#fffaf1"),chosen?Ui.C("#be6d83"):Ui.C("#e3d5bd"),s);
                int x=(int)(r.X+13*s);var flagBox=new RectangleF(x+1*s,r.Y+(h-18*s)/2,18*s,18*s);
                if(chosen)Ui.Text(g,"✓",Ui.Font(s,13,800,"✓"),Rectangle.Round(new RectangleF(x,r.Y,20*s,h)),Ui.C("#b84d6e"),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);else Ui.Icon(g,"flag",flagBox,Ui.C("#af8f70"));
                x+=(int)(32*s);string dday=Rules.Dday(o.startDate,today);var df=Ui.Font(s,13,700,null,dday);int dw=Ui.Width(dday,df);Ui.Text(g,dday,df,new Rectangle((int)(r.Right-13*s-dw),(int)r.Y,dw+2,h),Ui.C("#9e4f68"),TextFormatFlags.VerticalCenter);
                int tw=TextWidth(width);int ty=(int)(r.Y+13*s);ty+=Ui.Paragraph(g,o.@event.title,Ui.Font(s,13,700,null,o.@event.title),x,ty,tw,Css.Px(13)*1.6f*s,DesktopTheme.Ink);
                string small=CalendarView.FormatLong(o.startDate)+" · "+DesktopTheme.CategoryName(o.@event.category)+(o.@event.repeat!="none"?" · 반복":"");Ui.Text(g,small,Ui.Font(s,11,400,null,small),new Rectangle(x,ty+(int)(4*s),tw,(int)Math.Ceiling(Css.Px(11)*1.7f*s)),Ui.C("#79694f"),TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                y+=h+(int)(7*s);i++;
            }
            if(rows.Count>view.limits["dday"]){var mr=new RectangleF(4*s,y-1*s,width-8*s,Ui.Font(s,12,400).GetHeight()+22*s);string text="더 보기 · "+view.limits["dday"]+" / "+rows.Count+"개";Ui.Box(g,mr,9*s,hoverMore?Ui.C("#f9ecd6"):Ui.C("#fff9ee"),DesktopTheme.Line,s);Ui.Text(g,text,Ui.Font(s,12,400,null,text),Rectangle.Round(mr),Ui.C("#79694f"),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
            Ui.ScrollBar(g,scroll,s);
        }
        int At(Point p,out bool more){float s=Sc;more=false;int width=scroll.Visible?Width-(int)(12*s):Width;int y=(int)(4*s)-Offset;int i=0;foreach(var o in rows.Take(view.limits["dday"])){int h=RowHeight(o,width);if(p.Y>=y&&p.Y<y+h&&p.X<width)return i;y+=h+(int)(7*s);i++;}if(rows.Count>view.limits["dday"]&&p.Y>=y&&p.Y<y+Ui.Font(s,12,400).GetHeight()+22*s)more=true;return -1;}
        protected override void OnMouseMove(MouseEventArgs e){bool more;int i=At(e.Location,out more);if(i!=hover||more!=hoverMore){hover=i;hoverMore=more;Invalidate();}Cursor=i>=0||more?Cursors.Hand:Cursors.Default;}
        protected override void OnMouseLeave(EventArgs e){hover=-1;hoverMore=false;Invalidate();}
        protected override void OnMouseUp(MouseEventArgs e){bool more;int i=At(e.Location,out more);if(more){view.limits["dday"]+=CalendarView.pageSizes["dday"];Invalidate();var form=FindForm() as DialogForm;if(form!=null)form.Relayout();return;}if(i>=0){var o=rows[i];view.ChooseDday(o.@event.id,o.startDate,false);}}
        protected override void OnMouseWheel(MouseEventArgs e){Offset-=(int)(e.Delta/120f*100*Sc);Invalidate();var h=e as HandledMouseEventArgs;if(h!=null)h.Handled=true;}
    }
    // ---------------- Day list ("+N개 더 보기") ----------------
    // Every schedule of one date in a popup, like the event editor: tick to complete,
    // click to edit (the editor opens on top), or add a new one for that date.
    public sealed class DayListDialog : DialogForm {
        public readonly string Key;readonly DayList list;readonly Btn add=new Btn(BtnKind.Primary,"일정 추가","plus");
        public DayListDialog(CalendarView view,string key):base(view,CalendarView.FormatDay(key),key==Rules.Key(DateTime.Today)?"오늘의 계획":"선택한 날짜"){
            Key=key;WidthCss=460;
            list=new DayList(view,key);Body.Add(new DayListItem(list){Top=4});
            Footer(new Control[0],new Control[]{add});
            add.Click+=delegate{View.OpenEvent(null,Key,false);};
        }
        public override void StateChanged(){list.Rebuild();if(IsHandleCreated)Relayout();}
        sealed class DayListItem : FlowItem {
            readonly DayList list;readonly ControlItem item;public DayListItem(DayList l){list=l;item=new ControlItem(l);}
            public override void Attach(FlowPanel p){item.Attach(p);}
            public override int Measure(FlowPanel p,int width){int max=(int)Math.Round(list.ViewHeight()*0.6f);return Math.Max((int)(60*p.Sc),Math.Min(max,list.ContentHeight(width)));}
            public override void Arrange(FlowPanel p,Rectangle r){Bounds=r;item.Bounds=r;}
            public override void Position(FlowPanel p,int scroll){item.Position(p,scroll);}
        }
    }
    public sealed class DayList : Control {
        readonly CalendarView view;readonly string key;List<Occurrence> rows=new List<Occurrence>();int Offset;int hover=-1;bool hoverCheck;readonly ScrollModel scroll=new ScrollModel();
        public DayList(CalendarView owner,string date){view=owner;key=date;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);TabStop=true;Rebuild();}
        float Sc{get{return DeviceDpi/96f;}}
        public int ViewHeight(){return view.ViewportScreen().Height;}
        public void Rebuild(){rows=view.RowsFor(key);Invalidate();}
        int Inner(int width){return scroll.Visible?width-(int)(12*Sc):width;}
        int TextWidth(int width){float s=Sc;return width-(int)Math.Round((8+4+12+16+12+14)*s);}
        int RowHeight(Occurrence o,int width){float s=Sc;var e=o.@event;int lines=Ui.Wrap(e.title,Ui.Font(s,13,700,null,e.title),TextWidth(width)).Count;float h=12*s+Css.Px(11)*1.5f*s+4*s+lines*Css.Px(13)*1.55f*s+12*s;if(e.note!="")h+=Math.Min(2,Ui.Wrap(e.note,Ui.Font(s,11,400,null,e.note),TextWidth(width)).Count)*Css.Px(11)*1.6f*s;return (int)Math.Ceiling(Math.Max(h,56*s));}
        public int ContentHeight(int width){float s=Sc;if(rows.Count==0)return (int)Math.Ceiling(Css.Px(11)*1.8f*s+36*s);int h=(int)(4*s);foreach(var o in rows)h+=RowHeight(o,width)+(int)(7*s);return h;}
        RectangleF CheckBox(RectangleF r){float s=Sc;return new RectangleF(r.X+4*s+12*s,r.Y+(r.Height-16*s)/2,16*s,16*s);}
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;float s=Sc;g.Clear(DesktopTheme.Card);Ui.Prepare(g);
            int width=Width;scroll.Content=ContentHeight(width);scroll.Viewport=Height;scroll.Bounds=ClientRectangle;if(scroll.Visible){width=Inner(Width);scroll.Content=ContentHeight(width);}scroll.Offset=Offset;scroll.Clamp();Offset=scroll.Offset;
            if(rows.Count==0){string text="이 날짜에는 일정이 없어요.";Ui.Paragraph(g,text,Ui.Font(s,11,400,null,text),(int)(4*s),(int)(18*s)-Offset,width-(int)(8*s),Css.Px(11)*1.8f*s,Ui.C("#a29276"),false,TextFormatFlags.HorizontalCenter);}
            int y=(int)(4*s)-Offset;int i=0;
            foreach(var o in rows){
                var ev=o.@event;int h=RowHeight(o,width);var r=new RectangleF(2*s,y,width-4*s,h);bool done=Rules.Done(ev,o.startDate);bool hot=i==hover;
                Color light=Ui.C(CalendarView.CategoryLight(ev.category));Color fill=done?Ui.C(hot?"#e3eadc":"#edf1e8"):hot?light:Ui.C("#fffdf8");
                Ui.LeftEdgeCard(g,r,8*s,fill,Ui.C("#e5dac7"),4*s,DesktopTheme.Category(ev.category),s);
                var box=CheckBox(r);Ui.CheckBox(g,box,done,hot&&hoverCheck,true,5*s,s,fill);
                int x=(int)(box.Right+12*s);int tw=TextWidth(width);int ty=(int)(r.Y+12*s);var small=Ui.Font(s,11,400);int lh=(int)Math.Round(Css.Px(11)*1.5f*s);
                string time=ev.startTime!=""?ev.startTime+(ev.endTime!=""?" – "+ev.endTime:""):"종일";if(o.startDate!=o.endDate)time+=" · "+Rules.Date(o.startDate).ToString("M. d.",CultureInfo.InvariantCulture)+" – "+Rules.Date(o.endDate).ToString("M. d.",CultureInfo.InvariantCulture);
                string cat=DesktopTheme.CategoryName(ev.category)+(ev.repeat!="none"?" · 반복":"")+(view.IsDdayOccurrence(o)?" · D-day":"");
                int cw=Ui.Width(cat,Ui.Font(s,11,400,null,cat));Ui.Text(g,time,small,new Rectangle(x,ty,tw-cw-(int)(8*s),lh),Ui.C("#79694f"),TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);Ui.Text(g,cat,Ui.Font(s,11,400,null,cat),new Rectangle(x+tw-cw,ty,cw+2,lh),Ui.C("#79694f"),TextFormatFlags.VerticalCenter);
                ty+=lh+(int)(4*s);var tf=Ui.Font(s,13,700,null,ev.title,done?FontStyle.Strikeout:FontStyle.Regular);ty+=Ui.Paragraph(g,ev.title,tf,x,ty,tw,Css.Px(13)*1.55f*s,done?Ui.C("#7b7060"):DesktopTheme.Ink);
                if(ev.note!=""){var nf=Ui.Font(s,11,400,null,ev.note);var lines=Ui.Wrap(ev.note,nf,tw);string note=String.Join("\n",lines.Take(2))+(lines.Count>2?"…":"");Ui.Paragraph(g,note,nf,x,ty,tw,Css.Px(11)*1.6f*s,Ui.C("#93836a"));}
                y+=h+(int)(7*s);i++;
            }
            Ui.ScrollBar(g,scroll,s);
        }
        int At(Point p,out bool check){float s=Sc;check=false;int width=Inner(Width);int y=(int)(4*s)-Offset;int i=0;foreach(var o in rows){int h=RowHeight(o,width);if(p.Y>=y&&p.Y<y+h&&p.X<width){var box=CheckBox(new RectangleF(2*s,y,width-4*s,h));box.Inflate(5*s,5*s);check=box.Contains(p);return i;}y+=h+(int)(7*s);i++;}return -1;}
        protected override void OnMouseMove(MouseEventArgs e){bool check;int i=At(e.Location,out check);if(i!=hover||check!=hoverCheck){hover=i;hoverCheck=check;Invalidate();}Cursor=i>=0?Cursors.Hand:Cursors.Default;}
        protected override void OnMouseLeave(EventArgs e){hover=-1;hoverCheck=false;Invalidate();}
        protected override void OnMouseUp(MouseEventArgs e){if(e.Button!=MouseButtons.Left)return;bool check;int i=At(e.Location,out check);if(i<0)return;var o=rows[i];if(check)view.ToggleOccurrence(o.@event.id,o.startDate);else view.OpenEvent(o.@event.id,o.startDate,false);}
        protected override void OnMouseWheel(MouseEventArgs e){Offset-=(int)(e.Delta/120f*100*Sc);Invalidate();var h=e as HandledMouseEventArgs;if(h!=null)h.Handled=true;}
    }
    // ---------------- Confirm / repeat scope ----------------
    public sealed class ConfirmDialog : DialogForm {
        public readonly Btn Accept;readonly TaskCompletionSource<bool> result;bool done;
        public ConfirmDialog(CalendarView view,string title,string text,string label,TaskCompletionSource<bool> source):base(view,title,null,false){
            result=source;WidthCss=420;HeaderGap=0;
            var p=new TextItem(text,13,400,Ui.C("#867354"),1.9f){Top=Math.Max(14,Css.Px(13)),Bottom=Css.Px(13)};Body.Add(p);
            var no=new Btn(BtnKind.Normal,"취소");Accept=new Btn(BtnKind.Primary,label);var row=new InlineItem(8,no,Accept){Right=true,Top=22};Body.Add(row);
            no.Click+=delegate{Finish(false);};Accept.Click+=delegate{Finish(true);};
        }
        void Finish(bool value){if(done)return;done=true;CloseDialog();result.TrySetResult(value);}
        public override void Cancel(){Finish(false);}
    }
    public sealed class RepeatScopeDialog : DialogForm {
        public readonly Btn One;readonly TaskCompletionSource<string> result;bool done;
        public RepeatScopeDialog(CalendarView view,string text,TaskCompletionSource<string> source):base(view,"반복 일정 적용 범위",null,false){
            result=source;WidthCss=420;HeaderGap=0;
            Body.Add(new TextItem(text,13,400,Ui.C("#867354"),1.9f){Top=Math.Max(14,Css.Px(13)),Bottom=Css.Px(13)});
            var cancel=new Btn(BtnKind.Normal,"취소");var all=new Btn(BtnKind.Normal,"전체 일정");One=new Btn(BtnKind.Primary,"이번 일정만");Body.Add(new InlineItem(8,cancel,all,One){Right=true,Top=22});
            cancel.Click+=delegate{Finish(null);};all.Click+=delegate{Finish("all");};One.Click+=delegate{Finish("one");};
        }
        void Finish(string value){if(done)return;done=true;CloseDialog();result.TrySetResult(value);}
        public override void Cancel(){Finish(null);}
    }
    // ---------------- Help ----------------
    public sealed class HelpDialog : DialogForm {
        public HelpDialog(CalendarView view):base(view,"가볍게 시작하세요"){
            Color body=Ui.C("#8a7654");Func<TextItem> para=()=>{var t=new TextItem("",12,400,body,2f);t.Top=t.Bottom=Css.Px(12);return t;};
            var p1=para();p1.Runs=new List<Run>{new Run("날짜 숫자를 누르면 오른쪽에 일정이 표시됩니다. 빈 공간을 누르면 해당 날짜가 선택되고, 이미 선택한 날짜의 빈 공간을 다시 누르면 새 일정 입력창이 열립니다. ",-1),new Run("+ 일정 추가",700,DesktopTheme.Ink),new Run("로도 추가할 수 있어요.",-1)};Body.Add(p1);
            Body.Add(para().Set("일정을 누르면 수정·복제·삭제할 수 있습니다. 반복 일정은 이번 회차만 또는 전체를 수정·삭제할 수 있습니다. 상단 ‘중요한 일정 · D-DAY’를 누르면 기존 일정 중 하나를 직접 고를 수 있습니다. 오른쪽 일정의 깃발이나 일정 입력창의 ‘D-day로 표시’로도 선택할 수 있습니다. 반복 일정은 선택한 회차의 시작일을 기준으로 표시합니다. 카드에서 선택을 바꾸거나 해제할 수 있습니다."));
            Body.Add(para().Set("일정은 끌어서 다른 날짜로 옮기거나, 할 일 칸에 놓아 할 일로 바꿀 수 있습니다. 할 일도 달력 날짜로 끌어 일정으로 만들 수 있어요. 일정이 많은 날은 ‘+N개 더 보기’를 눌러 한 번에 확인하세요."));
            Body.Add(para().Set("검색은 모든 기간의 제목과 메모를 찾습니다. 분류 버튼은 한 번 더 누르면 전체로 돌아갑니다."));
            var keys=new[]{new[]{"N","선택한 날짜에 새 일정"},new[]{"/","일정 검색"},new[]{"T","오늘로 이동"},new[]{"←|→","이전 달 · 다음 달"},new[]{"Ctrl|Z","실행 취소 · Ctrl+Y 다시 실행"},new[]{"Esc","입력창 닫기"}};
            var dl=new PaintItem{Top=Css.Px(12),Bottom=Css.Px(12)};
            dl.OnMeasure=(p,w)=>{float s=p.Sc;float row=Math.Max(Css.Px(12)*1.33f,Css.Px(10)+6)*s;return (int)Math.Ceiling(36*s+keys.Length*row+(keys.Length-1)*12*s+2*s);};
            dl.OnPaint=(p,g,r)=>{float s=p.Sc;using(var b=new SolidBrush(DesktopTheme.Line)){g.FillRectangle(b,r.X,r.Y,r.Width,s);g.FillRectangle(b,r.X,r.Bottom-s,r.Width,s);}
                float row=Math.Max(Css.Px(12)*1.33f,Css.Px(10)+6)*s;float y=r.Y+18*s+s;var kf=Ui.Font(s,10,400,"mono");var df=Ui.Font(s,12,400);
                foreach(var k in keys){float x=r.X;foreach(var key in k[0].Split('|')){int kw=Math.Max((int)(19*s),Ui.Width(key,kf)+(int)(10*s));int kh=(int)Math.Max(19*s,kf.GetHeight()+4*s);var kr=new RectangleF(x,y+(row-kh)/2,kw,kh);Ui.Box(g,kr,4*s,Ui.Mix(DesktopTheme.Card,Color.White,.31f),Ui.C("#d6c9b0"),s);Ui.Text(g,key,kf,Rectangle.Round(kr),Ui.C("#6e604d"),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);x+=kw+4*s;}
                    Ui.Text(g,k[1],Ui.Font(s,12,400,null,k[1]),new Rectangle((int)(r.X+87*s),(int)y,(int)(r.Width-87*s),(int)Math.Ceiling(row)),Ui.C("#887354"),TextFormatFlags.VerticalCenter);y+=row+12*s;}};
            Body.Add(dl);
            Body.Add(Css.Hint("창의 X는 트레이로 숨기기입니다. 숨겨두어도 알림은 작동하고, 알림을 누르면 해당 일정이 열립니다. 완전 종료와 재시작은 시계 옆 캘린더 아이콘을 우클릭하세요."));
        }
    }
    // ---------------- Quick tour ----------------
    public sealed class QuickTourDialog : DialogForm {
        readonly DesktopHost host;int step;readonly TextItem status=Css.Hint(""),progress,title,text;readonly PaintItem demo;readonly Btn back=new Btn(BtnKind.Normal,"이전"){MinW=78},next=new Btn(BtnKind.Primary,"다음"){MinW=78},addPet=new Btn(BtnKind.Primary,"한 마리 추가");
        static readonly string[][] Steps={new[]{"일정을 추가해 보세요","날짜를 선택하고 ‘+ 일정 추가’를 눌러 제목과 시간을 입력한 뒤 저장하세요."},new[]{"Ctrl + Tab으로 빠르게","다른 프로그램을 사용 중에도 캘린더를 숨기거나 다시 열 수 있어요.\n단축키는 설정에서 변경할 수 있어요."},new[]{"함께할 펫을 골라보세요","버튼을 누르면 고양이가 추가돼요. 설정 및 백업에서는 다른 펫도 고를 수 있어요."}};
        Bitmap cat;
        public QuickTourDialog(CalendarView view,DesktopHost owner):base(view,null,"QUICK TOUR",false){
            host=owner;WidthCss=460;
            var skip=new Btn(BtnKind.Normal,"건너뛰기"){FontCss=11};skip.Surface=DesktopTheme.Card;skip.Click+=delegate{Finish();};SetHeaderAction(skip);
            demo=new PaintItem{Bottom=22};demo.OnMeasure=(p,w)=>(int)Math.Round(120*p.Sc);demo.OnPaint=PaintDemo;Body.Add(demo);
            addPet.Visible=false;Controls.Remove(addPet);Body.Controls.Add(addPet);addPet.Click+=AddPet;
            Body.Add(status);
            progress=new TextItem("",11,400,DesktopTheme.Muted){Bottom=8};Body.Add(progress);
            title=new TextItem("",23,700,DesktopTheme.Ink){Bottom=12};Body.Add(title);
            text=new TextItem("",14,400,DesktopTheme.Ink,1.9f);Body.Add(text);
            var nav=new InlineItem(14,back,next){SpaceBetween=true,Top=28};Body.Add(nav);Body.Add(new OverlayDots(this,nav));
            back.Click+=delegate{if(step>0){step--;Render();}};next.Click+=delegate{if(step==Steps.Length-1)Finish();else{step++;Render();}};
            Render();
        }
        sealed class OverlayDots : FlowItem {readonly QuickTourDialog d;readonly InlineItem nav;public OverlayDots(QuickTourDialog dialog,InlineItem n){d=dialog;nav=n;}
            public override int Measure(FlowPanel p,int width){return 0;}
            public override void Paint(FlowPanel p,Graphics g){float s=p.Sc;var r=nav.Bounds;float x=r.X+r.Width/2f-(7*3+7*2)*s/2;for(int i=0;i<3;i++){using(var b=new SolidBrush(i==d.step?DesktopTheme.Orange:DesktopTheme.Line))g.FillEllipse(b,x,r.Y+r.Height/2f-3.5f*s,7*s,7*s);x+=14*s;}}}
        void Render(){
            status.Set("");progress.Set((step+1)+" / "+Steps.Length);title.Set(Steps[step][0]);text.Set(Steps[step][1]);
            back.Enabled=step>0;next.Text=step==Steps.Length-1?"시작하기":"다음";addPet.Visible=step==2;if(IsHandleCreated)Relayout();Body.Invalidate();
        }
        void PaintDemo(FlowPanel p,Graphics g,Rectangle r){
            float s=p.Sc;Ui.Box(g,r,14*s,DesktopTheme.Paper,DesktopTheme.Line,s);int cy=r.Y+r.Height/2;
            if(step==0){string t="+ 일정 추가";var f=Ui.Font(s,14,700,null,t);int w=Ui.Width(t,f)+(int)(36*s+2*s),h=(int)Math.Ceiling(f.GetHeight()+24*s+2*s);var b=new RectangleF(r.X+(r.Width-w)/2f,cy-h/2f,w,h);var sh=b;sh.Offset(2*s,2*s);Ui.Fill(g,sh,8*s,DesktopTheme.Ink);Ui.Box(g,b,8*s,DesktopTheme.Orange,DesktopTheme.Ink,s);Ui.Text(g,t,f,Rectangle.Round(b),DesktopTheme.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
            else if(step==1){var kf=Ui.Font(s,24,400,"mono");var plus=Ui.Font(s,16,400);Func<string,SizeF> size=k=>new SizeF(Ui.Width(k,kf)+36*s+2*s,kf.GetHeight()+20*s+2*s);var a=size("Ctrl");var c=size("Tab");float pw=Ui.Width("+",plus);float total=a.Width+c.Width+pw+24*s;float x=r.X+(r.Width-total)/2;
                Action<string,SizeF> key=(k,sz)=>{var kr=new RectangleF(x,cy-sz.Height/2-1.5f*s,sz.Width,sz.Height);var sh=kr;sh.Offset(0,3*s);Ui.Fill(g,sh,8*s,DesktopTheme.Ink);Ui.Box(g,kr,8*s,DesktopTheme.Card,DesktopTheme.Ink,s);Ui.Text(g,k,kf,Rectangle.Round(kr),Ui.C("#6e604d"),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);x+=sz.Width+12*s;};
                key("Ctrl",a);Ui.Text(g,"+",plus,new Rectangle((int)x,r.Y,(int)pw+2,r.Height),DesktopTheme.Ink,TextFormatFlags.VerticalCenter);x+=pw+12*s;key("Tab",c);}
            else{var o=addPet.Outer();float total=100*s+12*s+o.Width;float x=r.X+(r.Width-total)/2;var img=Cat();if(img!=null){var mode=g.InterpolationMode;g.InterpolationMode=InterpolationMode.HighQualityBicubic;int frame=img.Width/8;g.DrawImage(img,new RectangleF(x,cy-50*s,100*s,100*s),new RectangleF(0,0,frame,frame),GraphicsUnit.Pixel);g.InterpolationMode=mode;}
                int bx=(int)(x+112*s),by=cy-o.Height/2-Body.Scroll.Offset;if(addPet.Left!=bx||addPet.Top!=by)BeginInvoke(new Action(()=>addPet.SetBounds(bx,by,o.Width,o.Height)));}
        }
        Bitmap Cat(){
            if(cat!=null)return cat;
            try{using(var pack=Assembly.GetExecutingAssembly().GetManifestResourceStream("petcalendar.pets.zip"))if(pack!=null)using(var zip=new ZipArchive(pack,ZipArchiveMode.Read)){var entry=zip.GetEntry("meowser-atlas.png");if(entry!=null)using(var stream=entry.Open())using(var memory=new MemoryStream()){stream.CopyTo(memory);memory.Position=0;using(var atlas=new Bitmap(memory)){int frame=atlas.Width/8;cat=atlas.Clone(new Rectangle(0,0,frame,frame),atlas.PixelFormat);}}}}catch{}
            if(cat!=null){var full=new Bitmap(cat.Width*8,cat.Height*8);using(var g=Graphics.FromImage(full))g.DrawImage(cat,0,0,cat.Width,cat.Height);cat.Dispose();cat=full;}
            return cat;
        }
        async void AddPet(object sender,EventArgs e){
            addPet.Enabled=false;status.Set("고양이를 데려오는 중이에요…");Relayout();
            try{await View.Api("POST","/api/pets",new Dictionary<string,object>{{"action","tour-spawn"}});status.Set("고양이 한 마리를 데려왔어요!");View.PetsChanged();}
            catch(Exception error){status.Set(String.IsNullOrEmpty(error.Message)?"고양이를 추가하지 못했습니다.":error.Message);}
            finally{if(!IsDisposed){addPet.Enabled=true;Relayout();}}
        }
        void Finish(){View.FinishTour();CloseDialog();}
        public override void Cancel(){Finish();}
        protected override void Dispose(bool disposing){if(disposing&&cat!=null){cat.Dispose();cat=null;}base.Dispose(disposing);}
    }
}
