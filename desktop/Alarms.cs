using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PetCalendarDesktop {
    public sealed class Alarm {
        public string id,eventId,startDate,title,body;
        public long createdAt;
    }
    public sealed class AlarmLedger {
        public Dictionary<string,long> sent=new Dictionary<string,long>();
        public List<Alarm> unread=new List<Alarm>();
        public bool legacyImported;
        public List<SnoozedAlarm> snoozed=new List<SnoozedAlarm>();
    }
    public sealed class SnoozedAlarm { public Alarm alarm; public long dueAt; }
    public sealed class AlarmService : IDisposable {
        readonly StateStore store;
        readonly string path;
        readonly object gate=new object();
        readonly System.Threading.Timer timer;
        AlarmLedger ledger;
        long lastCheck=Rules.Epoch(DateTime.UtcNow)-60000;
        bool disposed;
        public event Action<List<Alarm>,bool,bool> Delivered;
        public event Action<string,string> InApp;
        public event Action Resume;
        public event Action<Exception> Error;
        public int SoundBatches {get;private set;}
        public AlarmService(StateStore state) {
            store=state;path=Path.Combine(store.DirectoryPath,"notifications.json");
            ledger=Load();timer=new System.Threading.Timer(_=>Run(DateTime.UtcNow),null,10000,Timeout.Infinite);
            store.Changed+=Changed;
            SystemEvents.PowerModeChanged+=PowerChanged;SystemEvents.TimeChanged+=TimeChanged;
        }
        AlarmLedger Load(){foreach(string file in new[]{path,path+".bak"}){if(!File.Exists(file))continue;try{var value=Codec.Read<AlarmLedger>(File.ReadAllText(file));if(value!=null&&value.sent!=null&&value.unread!=null){if(value.snoozed==null)value.snoozed=new List<SnoozedAlarm>();return value;}}catch{}}return new AlarmLedger();}
        void Save(){StateStore.Atomic(path,Codec.Json(ledger),File.Exists(path)?path+".bak":null);}
        public List<Alarm> Pending(){lock(gate)return ledger.unread.ToList();}
        public Dictionary<string,long> Sent(){lock(gate)return new Dictionary<string,long>(ledger.sent);}
        public void ImportLegacy(string text){lock(gate){if(!ledger.legacyImported){try{var old=Codec.Read<Dictionary<string,long>>(text);if(old!=null)foreach(var entry in old){long current;if(!ledger.sent.TryGetValue(entry.Key,out current)||entry.Value>current)ledger.sent[entry.Key]=entry.Value;}}catch{}ledger.legacyImported=true;Save();}if(!disposed)timer.Change(10,Timeout.Infinite);}}
        public void Restore(){List<Alarm> pending=Pending();if(pending.Count>0&&store.Snapshot.settings.desktopPopups&&Delivered!=null)Delivered(pending,false,false);}
        void Changed(){lock(gate){if(disposed)return;var valid=store.Snapshot.events.ToDictionary(e=>e.id);int removed=ledger.unread.RemoveAll(a=>a.eventId!=""&&(!valid.ContainsKey(a.eventId)||(Rules.Done(valid[a.eventId],a.startDate)||!Rules.OccursOn(valid[a.eventId],a.startDate))));removed+=ledger.snoozed.RemoveAll(s=>s.alarm.eventId!=""&&(!valid.ContainsKey(s.alarm.eventId)||(Rules.Done(valid[s.alarm.eventId],s.alarm.startDate)||!Rules.OccursOn(valid[s.alarm.eventId],s.alarm.startDate))));if(removed>0)Save();timer.Change(10,Timeout.Infinite);}if(Delivered!=null)Delivered(Pending(),false,false);}
        void PowerChanged(object sender,PowerModeChangedEventArgs e){if(e.Mode==PowerModes.Resume){if(Resume!=null)Resume();Run(DateTime.UtcNow);}}
        void TimeChanged(object sender,EventArgs e){if(Resume!=null)Resume();Run(DateTime.UtcNow);}
        public void CheckAt(DateTime utc){Run(utc);}
        void Run(DateTime utc) {
            try {
                var newAlerts=new List<Alarm>();List<Alarm> pending;
                lock(gate) {
                    if(disposed)return;
                    long now=Rules.Epoch(utc),since=Math.Max(lastCheck,now-24*3600000L);if(since>now)since=now-60000;
                    var state=store.Snapshot;DateTime local=utc.ToLocalTime();
                    // We wake at least daily. Only the 24-hour catch-up and next wake
                    // can be relevant; reminders are at most one hour before start.
                    string from=Rules.Key(local.Date.AddDays(-2)),to=Rules.Key(local.Date.AddDays(2));
                    long next=now+24*3600000L;
                    bool changed=false;
                    foreach(var snooze in ledger.snoozed.ToArray()) {
                        if(snooze.dueAt<=now){ledger.snoozed.Remove(snooze);snooze.alarm.createdAt=now;newAlerts.Add(snooze.alarm);if(state.settings.desktopPopups)ledger.unread.Add(snooze.alarm);changed=true;}
                        else if(snooze.dueAt<next)next=snooze.dueAt;
                    }
                    foreach(var o in Rules.Occurrences(state.events.Where(e=>e.reminder.HasValue),from,to)) {
                        var at=Rules.ReminderAt(o);if(!at.HasValue)continue;
                        string id=o.key+"@"+at.Value;
                        if(at.Value>since&&at.Value<=now&&!ledger.sent.ContainsKey(id)) {
                            var alert=new Alarm{id=id,eventId=o.@event.id,startDate=o.startDate,title=o.@event.title,body=Rules.Date(o.startDate).ToString("M월 d일 (ddd)",Rules.Korean)+" "+(o.@event.startTime==""?"오전 9시 · 종일 일정":o.@event.startTime)+(at.Value<now-60000?"\n잠시 자리를 비운 동안 알림 시간이 지났어요.":""),createdAt=now};
                            ledger.sent[id]=now;newAlerts.Add(alert);if(state.settings.desktopPopups)ledger.unread.Add(alert);
                        }
                        if(at.Value>now&&!ledger.sent.ContainsKey(id)&&at.Value<next)next=at.Value;
                    }
                    lastCheck=now;
                    foreach(string id in ledger.sent.Where(p=>p.Value<now-7*86400000L).Select(p=>p.Key).ToArray())ledger.sent.Remove(id);
                    if(changed||newAlerts.Count>0)Save();
                    pending=ledger.unread.ToList();
                    timer.Change((int)Math.Max(20,Math.Min(86400000L,next-now+10)),Timeout.Infinite);
                    if(newAlerts.Count>0&&state.settings.desktopPopups&&state.settings.notificationSound)SoundBatches++;
                }
                foreach(var alarm in newAlerts)if(InApp!=null)InApp(alarm.title,alarm.body);
                if(newAlerts.Count>0&&Delivered!=null)Delivered(pending,true,false);
            }catch(Exception e){if(Error!=null)Error(e);lock(gate){if(!disposed)timer.Change(60000,Timeout.Infinite);}}
        }
        public void Test(){List<Alarm> list;lock(gate){var alarm=new Alarm{id="test-"+Guid.NewGuid().ToString("N"),eventId="",startDate=Rules.Key(DateTime.Now),title="알림이 잘 도착했어요",body="펫 캘린더를 숨겨두어도 이렇게 알려드립니다.",createdAt=Rules.Epoch(DateTime.UtcNow)};ledger.unread.Add(alarm);Save();list=ledger.unread.ToList();if(store.Snapshot.settings.notificationSound)SoundBatches++;}if(Delivered!=null)Delivered(list,true,true);}
        public void Acknowledge(string id){lock(gate){if(id==null)ledger.unread.Clear();else ledger.unread.RemoveAll(a=>a.id==id);Save();}if(Delivered!=null)Delivered(Pending(),false,false);}
        public void Snooze(string id,int minutes){if(minutes!=5&&minutes!=10)return;lock(gate){var alarm=ledger.unread.FirstOrDefault(a=>a.id==id);if(alarm==null)return;ledger.unread.Remove(alarm);ledger.snoozed.RemoveAll(s=>s.alarm.id==id);ledger.snoozed.Add(new SnoozedAlarm{alarm=alarm,dueAt=Rules.Epoch(DateTime.UtcNow.AddMinutes(minutes))});Save();if(!disposed)timer.Change(10,Timeout.Infinite);}if(Delivered!=null)Delivered(Pending(),false,false);}
        public void Dispose(){lock(gate){if(disposed)return;disposed=true;timer.Dispose();store.Changed-=Changed;SystemEvents.PowerModeChanged-=PowerChanged;SystemEvents.TimeChanged-=TimeChanged;}}
    }
    public sealed class AlarmPopup : Form {
        readonly WidgetAgenda list=new WidgetAgenda();
        readonly Label caption=new Label();
        readonly WidgetButton close=new WidgetButton();
        readonly ToolTip tips=new ToolTip();
        List<Alarm> alarms=new List<Alarm>();
        bool layingOut;

        public event Action<Alarm> OpenAlarm;
        public event Action<Alarm,int> SnoozeAlarm;
        public event Action CloseAll;
        public string MonitorName {get;private set;}
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x80;return p;}}
        public AlarmPopup() {
            SuspendLayout();AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;
            BackColor=DesktopTheme.Paper;ForeColor=DesktopTheme.Ink;Font=new Font("Segoe UI",11+DesktopTheme.TextScale);DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);Size=new Size(360,148);
            caption.Name="alarm-title";caption.Font=new Font(Font,FontStyle.Bold);caption.TextAlign=ContentAlignment.MiddleLeft;caption.AutoEllipsis=true;caption.UseMnemonic=false;Controls.Add(caption);
            close.Name="alarm-close";close.Text="×";close.Quiet=true;close.AccessibleName="모든 알림 확인하고 닫기";close.ForeColor=ForeColor;close.Click+=delegate{if(CloseAll!=null)CloseAll();};Controls.Add(close);tips.SetToolTip(close,close.AccessibleName);
            list.Name="alarm-list";list.AccessibleName="미확인 일정 알림";list.BackColor=BackColor;list.Scrollbar.AccessibleName="알림 목록 스크롤";Controls.Add(list);
            ResumeLayout(true);LayoutPopup();
        }
        public void ApplyTextScale(){if(IsDisposed)return;var old=Font;var oldCaption=caption.Font;Font=new Font("Segoe UI",11+DesktopTheme.TextScale);caption.Font=new Font(Font,FontStyle.Bold);oldCaption.Dispose();old.Dispose();LayoutPopup();Invalidate(true);}
        void LayoutPopup(){if(layingOut||list.IsDisposed)return;layingOut=true;try{int item=DesktopTheme.Px(this,86);Height=DesktopTheme.Px(this,62)+Math.Min(4,Math.Max(1,alarms.Count))*item;int pad=DesktopTheme.Px(this,12);caption.SetBounds(DesktopTheme.Px(this,16),DesktopTheme.Px(this,10),Math.Max(1,ClientSize.Width-DesktopTheme.Px(this,62)),DesktopTheme.Px(this,32));close.SetBounds(ClientSize.Width-DesktopTheme.Px(this,42),DesktopTheme.Px(this,10),DesktopTheme.Px(this,30),DesktopTheme.Px(this,30));list.SetBounds(pad,DesktopTheme.Px(this,50),Math.Max(1,ClientSize.Width-2*pad),Height-DesktopTheme.Px(this,62));foreach(Control row in list.Items.Controls){row.Width=Math.Max(1,list.ContentWidth);row.Height=item;}list.Reflow();}finally{layingOut=false;}}
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);DesktopTheme.ShapeWindow(this,14,false);}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);LayoutPopup();DesktopTheme.ShapeWindow(this,14,false);}
        protected override void OnDpiChanged(DpiChangedEventArgs e){base.OnDpiChanged(e);DesktopTheme.ShapeWindow(this,14,false);if(IsHandleCreated&&!IsDisposed)BeginInvoke(new Action(()=>{LayoutPopup();PlaceOnScreen(Screen.FromHandle(Handle));}));}
        void PlaceOnScreen(Screen screen){var area=screen.WorkingArea;MonitorName=screen.DeviceName;int gap=DesktopTheme.Px(this,16);Location=new Point(Math.Max(area.Left,area.Right-Width-gap),Math.Max(area.Top,area.Bottom-Height-gap));}
        public void UpdateAlarms(List<Alarm> values,bool move) {
            alarms=values.OrderByDescending(a=>a.createdAt).ToList();
            list.Items.SuspendLayout();foreach(Control row in list.Items.Controls.Cast<Control>().ToArray()){list.Items.Controls.Remove(row);row.Dispose();}
            foreach(var alarm in alarms){var value=alarm;var row=new AlarmRow(alarm){Name="alarm-"+alarm.id,AccessibleName=alarm.title+" · "+alarm.body,Margin=Padding.Empty};row.Click+=delegate{if(OpenAlarm!=null)OpenAlarm(value);};var menu=new ContextMenuStrip();foreach(int minutes in new[]{5,10}){int delay=minutes;menu.Items.Add(minutes+"분 미루기",null,(s,e)=>{if(SnoozeAlarm!=null)SnoozeAlarm(value,delay);});}row.ContextMenuStrip=menu;row.Disposed+=(s,e)=>menu.Dispose();tips.SetToolTip(row,alarm.title+"\n"+alarm.body+"\n우클릭하여 알림 미루기");list.Items.Controls.Add(row);}list.Items.ResumeLayout(true);
            caption.Text="알림 "+alarms.Count+"개 · 우클릭해 미루기";LayoutPopup();
            if(move||!Visible)PlaceOnScreen(Screen.FromHandle(GetForegroundWindow()));
            if(alarms.Count==0)Hide();else{Show();LayoutPopup();Invalidate();}
        }
        protected override void OnPaintBackground(PaintEventArgs e){DesktopTheme.PaintWindow(this,e.Graphics,14,false);}
        protected override void OnFormClosing(FormClosingEventArgs e){if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;if(CloseAll!=null)CloseAll();}base.OnFormClosing(e);}
        public object InspectLayout(){return new{dpi=DeviceDpi,width=ClientSize.Width,height=ClientSize.Height,caption=caption.Bounds,close=close.Bounds,list=list.Bounds,itemHeight=DesktopTheme.Px(this,86),scrollMaximum=list.Scrollbar.Maximum,scrollbarWindow=list.Scrollbar.Handle.ToInt64()};}
        protected override void Dispose(bool disposing){if(disposing)tips.Dispose();base.Dispose(disposing);}
    }    public sealed class AlarmRow : WidgetButton {
        readonly Alarm alarm;
        public AlarmRow(Alarm value){alarm=value;BackColor=DesktopTheme.Paper;ForeColor=DesktopTheme.Ink;}
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;float s=DeviceDpi/96f;var rect=new RectangleF(2*s,3*s,Math.Max(1,Width-6*s),Math.Max(1,Height-7*s));
            using(var shape=DesktopTheme.Round(rect,10*s)){using(var fill=new SolidBrush(hovered?ColorTranslator.FromHtml("#fff8e9"):DesktopTheme.Card))g.FillPath(fill,shape);using(var border=new Pen(Focused&&ShowFocusCues?DesktopTheme.Orange:DesktopTheme.Line,s))g.DrawPath(border,shape);}
            using(var accent=new Pen(DesktopTheme.Orange,3*s)){accent.StartCap=accent.EndCap=LineCap.Round;g.DrawLine(accent,rect.Left+2*s,rect.Top+10*s,rect.Left+2*s,rect.Bottom-10*s);}
            using(var bold=new Font(Font.FontFamily,11+DesktopTheme.TextScale,FontStyle.Bold))TextRenderer.DrawText(g,alarm.title,bold,Rectangle.Round(new RectangleF(rect.X+12*s,rect.Y+8*s,rect.Width-24*s,24*s)),ForeColor,TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
            using(var small=new Font(Font.FontFamily,10+DesktopTheme.TextScale))TextRenderer.DrawText(g,alarm.body,small,Rectangle.Round(new RectangleF(rect.X+12*s,rect.Y+34*s,rect.Width-24*s,37*s)),DesktopTheme.Muted,TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        }
    }

}
