using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PetCalendarDesktop {
    public sealed class ApiResponse { public int status; public object body; public ApiResponse(int s,object b){status=s;body=b;} }
    public sealed class DesktopHost : Form {
        public const string ApplicationId="PetCalendar.Desktop";
        readonly string root,profile;
        readonly bool automation;
        readonly StateStore store;
        readonly CalendarView view;
        readonly UiPrefs prefs;
        readonly NotifyIcon tray=new NotifyIcon();
        readonly ContextMenuStrip menu=new ContextMenuStrip();
        readonly AlarmService alarms;
        readonly AlarmPopup popup=new AlarmPopup();
        readonly SoundPlayer sound;
        readonly CalendarWidget widget;
        readonly StickyNotes notes;
        readonly ToolStripMenuItem widgetMenu,petToggle,petAdd;
        readonly CalendarPets pets;
        Hotkey registered=new Hotkey{enabled=false};
        readonly Queue<KeyValuePair<string,string>> deferredReminders=new Queue<KeyValuePair<string,string>>();
        string hotkeyError="";
        bool initialized,exiting,ready;
        Rectangle windowRestoreBounds;
        DateTime started=DateTime.UtcNow;
        public event Action<object> AutomationReply;
        public event Action<bool> ExitRequested;
        public bool Ready {get{return ready;}}
        [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern int SetCurrentProcessExplicitAppUserModelID(string id);
        [DllImport("shell32.dll")] static extern int GetCurrentProcessExplicitAppUserModelID(out IntPtr id);
        [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window,int id);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from,uint to,bool attach);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
        const int HotkeyId=0x4CA1;
        // Keep the native sizing frame, system menu, taskbar identity and snap
        // behavior, while removing the duplicate Windows caption above the UI.
        protected override CreateParams CreateParams {get{var p=base.CreateParams;p.Style=(p.Style&~0x00c00000)|0x000f0000;return p;}}
        public DesktopHost(string appRoot,string dataDir,string profileDir,bool test) {
            root=appRoot;profile=profileDir;automation=test;
            Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(ApplicationId));
            bool freshProfile=!File.Exists(Path.Combine(dataDir,"calendar.json"))&&!File.Exists(Path.Combine(dataDir,"pets.json"));
            store=new StateStore(dataDir);DesktopTheme.TextScale=store.Snapshot.settings.textScale;DesktopTheme.FontKey=store.Snapshot.settings.fontFamily??"default";DesktopTheme.BoldText=store.Snapshot.settings.boldText;DesktopTheme.SetCategories(store.Snapshot.settings.categories,store.Snapshot.settings.hiddenCategories);
            pets=new CalendarPets(dataDir,()=>store.Snapshot,Log,freshProfile);
            // The WebView build kept the tour flag in its browser profile; an
            // existing profile means this user has already been through it.
            prefs=new UiPrefs(dataDir,!freshProfile&&Directory.Exists(profileDir));
            AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
            Text="펫 캘린더";FormBorderStyle=FormBorderStyle.None;Font=new Font("Segoe UI",10);BackColor=DesktopTheme.Paper;Padding=new Padding(6);DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);
            var area=Screen.PrimaryScreen.WorkingArea;Size=new Size(Math.Min(1440,area.Width-40),Math.Min(1000,area.Height-40));MinimumSize=new Size(800,600);StartPosition=FormStartPosition.CenterScreen;
            Icon=new Icon(Path.Combine(root,"calendar.ico"));
            view=new CalendarView(this,store,prefs){Dock=DockStyle.Fill};view.WindowAction+=WindowAction;Controls.Add(view);
            widget=new CalendarWidget(store,ApplyHotkey,(id,date)=>{ShowCalendar();view.OpenFromHost(id,date);},Log);widget.Icon=Icon;
            notes=new StickyNotes(dataDir,Log);notes.EventFromLine+=line=>{ShowCalendar();BeginInvoke(new Action(()=>view.NewEventFromText(line)));};notes.TaskFromLine=line=>view.AddTaskText(line);widget.NoteRequested+=()=>notes.Request(widget.Visible?widget.Bounds:(Rectangle?)null);
            menu.Items.Add("캘린더 열기",null,delegate{ShowCalendar();});menu.Items.Add(new ToolStripSeparator());
            widgetMenu=new ToolStripMenuItem("바탕화면 위젯",null,delegate{if(widget.EnabledPreference)widget.HideWidget();else widget.ShowWidget();});widgetMenu.Checked=widget.EnabledPreference;menu.Items.Add(widgetMenu);
            widget.VisibilityPreferenceChanged+=delegate{widgetMenu.Checked=widget.EnabledPreference;view.WidgetChanged();};
            var petMenu=new ToolStripMenuItem("데스크톱 펫");
            petToggle=new ToolStripMenuItem("데스크톱 펫 사용",null,delegate{try{pets.SetEnabled(!pets.Enabled);}catch(Exception error){Log(error);MessageBox.Show(this,error.Message,"데스크톱 펫");}});
            petAdd=DesktopPet.PetMenu.Add(kind=>{try{pets.Add(kind==null?"random":kind.Id);}catch(Exception error){Log(error);}});petAdd.Enabled=false;
            petMenu.DropDownItems.Add(petToggle);petMenu.DropDownItems.Add(petAdd);petMenu.DropDownItems.Add("펫 바닥으로 데려오기",null,delegate{pets.Home();});menu.Items.Add(petMenu);
            pets.Changed+=delegate{petToggle.Checked=pets.Enabled;petAdd.Enabled=pets.Enabled;view.PetsChanged();if(ready)RouteAlarms(alarms.Pending(),false,false);};
            menu.Items.Add("재시작",null,async delegate{await Shutdown(true);});menu.Items.Add("완전 종료",null,async delegate{await Shutdown(false);});
            tray.Icon=Icon;tray.Text="펫 캘린더";tray.ContextMenuStrip=menu;tray.DoubleClick+=delegate{ShowCalendar();};tray.Visible=true;
            sound=new SoundPlayer(Path.Combine(root,"notification.wav"));
            alarms=new AlarmService(store);
            alarms.Delivered+=(list,fresh,force)=>Ui(()=>RouteAlarms(list,fresh,force));
            alarms.InApp+=(title,body)=>Ui(()=>{if(pets.CanNotify&&store.Snapshot.settings.desktopPopups)return;var message=new KeyValuePair<string,string>(title,body);if(Visible&&WindowState!=FormWindowState.Minimized)view.Reminder(title,body);else{deferredReminders.Enqueue(message);while(deferredReminders.Count>20)deferredReminders.Dequeue();}});
            alarms.Resume+=()=>Ui(()=>{pets.ResumeSpeech();view.Invalidate();});
            alarms.Error+=Log;
            popup.OpenAlarm+=alarm=>{alarms.Acknowledge(alarm.id);ShowCalendar();if(alarm.eventId!="")view.OpenFromHost(alarm.eventId,alarm.startDate);};
            popup.CloseAll+=()=>alarms.Acknowledge(null);
            popup.SnoozeAlarm+=(alarm,minutes)=>alarms.Snooze(alarm.id,minutes);
            pets.Acknowledge+=id=>alarms.Acknowledge(id);
            pets.Snooze+=(id,minutes)=>alarms.Snooze(id,minutes);
            pets.OpenAlarm+=alarm=>{alarms.Acknowledge(alarm.id);ShowCalendar();if(alarm.eventId!="")view.OpenFromHost(alarm.eventId,alarm.startDate);};
            pets.OpenSchedule+=(id,date)=>{ShowCalendar();if(id!=null)view.OpenFromHost(id,date);};
            var previousCalendar=store.Snapshot;
            store.Changed+=()=>{
                var next=store.Snapshot;var before=previousCalendar;previousCalendar=next;DesktopTheme.SetCategories(next.settings.categories,next.settings.hiddenCategories);
                string nextFont=next.settings.fontFamily??"default";
                if(DesktopTheme.TextScale!=next.settings.textScale||DesktopTheme.FontKey!=nextFont||DesktopTheme.BoldText!=next.settings.boldText){
                    DesktopTheme.TextScale=next.settings.textScale;DesktopTheme.FontKey=nextFont;DesktopTheme.BoldText=next.settings.boldText;widget.ApplyTextScale();popup.ApplyTextScale();notes.ApplyFont();
                    foreach(var bubble in Application.OpenForms.OfType<PetBubble>().ToArray())bubble.ApplyTextScale();
                    view.TextScaleChanged();pets.FontChanged();
                }
                try{pets.CalendarChanged(before,next);}catch(Exception error){Log(error);}
                view.StoreChanged();
            };
        }
        void RouteAlarms(List<Alarm> list,bool fresh,bool force) {
            bool show=(store.Snapshot.settings.desktopPopups||force)&&list.Count>0;
            if(pets.CanNotify){popup.Hide();pets.Deliver(show?list:new List<Alarm>());}
            else {pets.Deliver(new List<Alarm>());if(show)popup.UpdateAlarms(list,fresh);else popup.Hide();}
            if(show&&fresh&&store.Snapshot.settings.notificationSound)try{sound.Play();}catch{}
        }
        void Ui(Action action){if(IsDisposed||exiting)return;try{if(InvokeRequired)BeginInvoke(action);else action();}catch(InvalidOperationException){}}
        void Log(Exception e){try{File.AppendAllText(Path.Combine(store.DirectoryPath,"desktop-errors.log"),DateTime.UtcNow.ToString("o")+" "+e+Environment.NewLine,Encoding.UTF8);}catch{}}
        void FitHostToScreen(){
            if(WindowState!=FormWindowState.Normal)return;
            var area=Screen.FromControl(this).WorkingArea;
            MinimumSize=new Size(Math.Min(area.Width,DesktopTheme.Px(this,800)),Math.Min(area.Height,DesktopTheme.Px(this,600)));
            int width=Math.Min(Width,area.Width),height=Math.Min(Height,area.Height);
            Bounds=new Rectangle(Math.Max(area.Left,Math.Min(Left,area.Right-width)),Math.Max(area.Top,Math.Min(Top,area.Bottom-height)),width,height);
        }
        void ShapeHost(){DesktopTheme.ShapeWindow(this,18,WindowState!=FormWindowState.Normal);}
        void DisableNativeFrame(){int disabled=1;DwmSetWindowAttribute(Handle,2,ref disabled,sizeof(int));}
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);DisableNativeFrame();ShapeHost();}
        protected override void OnDpiChanged(DpiChangedEventArgs e){base.OnDpiChanged(e);ShapeHost();if(IsHandleCreated&&!IsDisposed)BeginInvoke(new Action(FitHostToScreen));}
        protected override void OnShown(EventArgs e) {
            base.OnShown(e);FitHostToScreen();if(initialized)return;initialized=true;
            try {
                try{ApplyHotkey(store.Snapshot.settings.hotkey).Wait();}catch(Exception error){var inner=error is AggregateException&&error.InnerException!=null?error.InnerException:error;hotkeyError=inner.Message;Log(inner);}
                ready=true;widget.Restore();notes.Restore();pets.Restore();alarms.Restore();PublishWindowState();
                Reply(new{kind="ready",pid=Process.GetCurrentProcess().Id,elapsedMs=(DateTime.UtcNow-started).TotalMilliseconds});
                view.Focus();BeginInvoke(new Action(view.Ready));
            }catch(Exception error){Log(error);Reply(new{kind="fatal",error=error.ToString()});MessageBox.Show(this,"펫 캘린더를 열지 못했습니다.\n"+error.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Error);}
        }
        public object HotkeyStatus(){return new{available=true,active=registered.enabled,error=hotkeyError,label=Rules.HotkeyLabel(store.Snapshot.settings.hotkey),pid=Process.GetCurrentProcess().Id,tray=tray.Visible,windowReady=ready};}
        // Whether a key combination can be registered right now (nothing else holds it).
        internal bool HotkeyFree(Hotkey h){if(!RegisterHotKey(Handle,HotkeyId+1,(uint)h.modifiers|0x4000,(uint)h.key))return false;UnregisterHotKey(Handle,HotkeyId+1);return true;}
        Task ApplyHotkey(Hotkey next) {
            Rules.ValidateHotkey(next);
            if(Codec.Json(next)==Codec.Json(registered)&&hotkeyError=="")return Task.FromResult(0);
            Hotkey previous=registered;
            if(previous.enabled)UnregisterHotKey(Handle,HotkeyId);
            if(next.enabled&&!RegisterHotKey(Handle,HotkeyId,(uint)next.modifiers|0x4000,(uint)next.key)) {
                if(previous.enabled)RegisterHotKey(Handle,HotkeyId,(uint)previous.modifiers|0x4000,(uint)previous.key);
                hotkeyError="이 단축키는 다른 프로그램에서 사용 중이거나 Windows에서 허용하지 않습니다. 다른 키를 선택해 주세요.";
                throw new AppError(422,hotkeyError);
            }
            registered=new Hotkey{enabled=next.enabled,modifiers=next.modifiers,key=next.key};hotkeyError="";return Task.FromResult(0);
        }
        public async Task<ApiResponse> Api(string method,string path,string body,string marker="1",bool force=false) {
            try {
                if(exiting)throw new AppError(503,"캘린더를 종료하고 있습니다.");
                if(method!="GET"&&marker!="1")throw new AppError(403,"올바르지 않은 저장 요청입니다.");
                if(method=="GET"&&path=="/api/state")return new ApiResponse(200,store.Snapshot);
                if(method=="GET"&&path=="/api/backup")return new ApiResponse(200,new{version=1,calendar=store.Snapshot,pets=pets.BackupPreferences(),widget=widget.BackupPreferences()});
                if(method=="POST"&&path=="/api/backup"){
                    var request=Codec.Object(body);if(request==null||!request.ContainsKey("calendar")||!request.ContainsKey("pets")||!request.ContainsKey("widget"))throw new AppError(400,"통합 백업 파일을 확인해 주세요.");
                    var calendar=Rules.Validate(Codec.Json(request["calendar"]));var petBackup=CalendarPets.CheckBackup(request["pets"]);var widgetBackup=CalendarWidget.CheckBackup(request["widget"]);
                    var previousPets=pets.BackupPreferences();var previousWidget=widget.BackupPreferences();
                    if(!request.ContainsKey("expectedRevision")||Convert.ToInt64(request["expectedRevision"])!=store.Snapshot.revision)throw new AppError(409,"다른 창에서 일정이 변경되었습니다. 최신 내용을 불러온 뒤 다시 복원해 주세요.");
                    try{pets.RestorePreferences(petBackup);widget.RestorePreferences(widgetBackup);calendar.revision=Convert.ToInt64(request["expectedRevision"]);return new ApiResponse(200,await store.Save(Codec.Json(calendar),ApplyHotkey));}
                    catch{try{pets.RestorePreferences(previousPets);widget.RestorePreferences(previousWidget);}catch(Exception rollback){Log(rollback);}throw;}
                }
                if(method=="PUT"&&path=="/api/state")return new ApiResponse(200,await store.Save(body,ApplyHotkey,force));
                if(method=="GET"&&path=="/api/hotkey")return new ApiResponse(200,HotkeyStatus());
                if(method=="GET"&&path=="/api/pets")return new ApiResponse(200,pets.Inspect());
                if(method=="GET"&&path=="/api/pets/diary")return new ApiResponse(200,new{entries=pets.DiaryEntries()});
                if(method=="POST"&&path=="/api/pets") {
                    var request=Codec.Object(body);string action=request.ContainsKey("action")?request["action"] as string:"";
                    if(action=="enable")pets.SetEnabled(true);
                    else if(action=="disable")pets.SetEnabled(false);
                    else if(action=="spawn")pets.Add(request.ContainsKey("species")?request["species"] as string:"cat");
                    else if(action=="tour-spawn")pets.AddFromTour();
                    else if(action=="home")pets.Home();
                    else if(action=="rename") {
                        object window,name;if(!request.TryGetValue("window",out window)||!(window is int||window is long)||!request.TryGetValue("name",out name)||!(name is string))throw new AppError(400,"펫과 이름을 확인해 주세요.");
                        pets.Rename(Convert.ToInt64(window),(string)name);
                    }
                    else if(action=="configureSpeech")pets.ConfigureSpeech(request);
                    else if(action=="configureChatter")pets.ConfigureChatter(request);
                    else if(action=="configureBed")pets.ConfigureBed(request);
                    else if(action=="feed"){object window;if(!request.TryGetValue("window",out window)||!(window is int||window is long))throw new AppError(400,"펫을 확인해 주세요.");pets.FeedWindow(Convert.ToInt64(window));}
                    else if(action=="preview")pets.Announce();
                    else throw new AppError(400,"데스크톱 펫 동작을 확인해 주세요.");
                    return new ApiResponse(200,pets.Inspect());
                }
                if(method=="GET"&&path=="/api/widget")return new ApiResponse(200,widget.Inspect());
                if(method=="POST"&&path=="/api/widget"){
                    var request=Codec.Object(body);string action=request.ContainsKey("action")?request["action"] as string:"";
                    if(action=="show")widget.ShowWidget();else if(action=="hide")widget.HideWidget();else if(action=="configure"){
                        if((request.ContainsKey("topmost")&&!(request["topmost"] is bool))||(request.ContainsKey("locked")&&!(request["locked"] is bool))||(request.ContainsKey("showDday")&&!(request["showDday"] is bool)))throw new AppError(400,"위젯 설정을 확인해 주세요.");
                        widget.Configure(request.ContainsKey("topmost")?(bool?)request["topmost"]:null,request.ContainsKey("locked")?(bool?)request["locked"]:null,request.ContainsKey("showDday")?(bool?)request["showDday"]:null);
                    }else throw new AppError(400,"위젯 동작을 확인해 주세요.");return new ApiResponse(200,widget.Inspect());
                }
                if(method=="GET"&&path=="/api/health")return new ApiResponse(200,new{app="pet-calendar-desktop-v2",pid=Process.GetCurrentProcess().Id});
                if(method=="POST"&&path=="/api/desktop/notification-test"){alarms.Test();return new ApiResponse(200,new{ok=true});}
                if(method=="POST"&&path=="/api/window/show"){ShowCalendar();return new ApiResponse(200,new{ok=true});}
                if(method=="POST"&&(path=="/api/lifecycle?action=exit"||path=="/api/lifecycle?action=restart")){BeginInvoke(new Action(async delegate{await Shutdown(path.EndsWith("restart"));}));return new ApiResponse(200,new{ok=true});}
                throw new AppError(404,"페이지를 찾을 수 없습니다.");
            }catch(AppError error){return new ApiResponse(error.Status,new{error=error.Message});}
            catch(Exception error){Log(error);return new ApiResponse(500,new{error="저장하지 못했습니다. 입력 내용을 유지했습니다. "+error.Message});}
        }
        // Dialogs and toasts are separate windows; keep them with the calendar.
        void SyncSurfaces(){bool visible=Visible&&WindowState!=FormWindowState.Minimized;DialogForm.SetAllVisible(visible);Toasts.SetVisible(visible);if(visible)DialogForm.RelayoutAll();}
        protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);SyncSurfaces();}
        protected override void OnMove(EventArgs e){base.OnMove(e);if(view!=null){Toasts.Layout();DialogForm.RelayoutAll();}}
        public void ShowCalendar(){Show();if(WindowState==FormWindowState.Minimized)SetWindowMode(FormWindowState.Normal);Activate();if(!SetForegroundWindow(Handle)){uint process;uint foreground=GetWindowThreadProcessId(GetForegroundWindow(),out process),own=GetCurrentThreadId();bool attached=foreground!=0&&foreground!=own&&AttachThreadInput(own,foreground,true);try{SetForegroundWindow(Handle);}finally{if(attached)AttachThreadInput(own,foreground,false);}}if(DialogForm.Open.Count>0)DialogForm.Open[DialogForm.Open.Count-1].Activate();else view.Focus();view.Invalidate();while(deferredReminders.Count>0){var reminder=deferredReminders.Dequeue();if(!pets.CanNotify||!store.Snapshot.settings.desktopPopups)view.Reminder(reminder.Key,reminder.Value);}}
        public void Toggle(){IntPtr front=GetForegroundWindow();bool ours=front==Handle||DialogForm.Open.Any(d=>d.IsHandleCreated&&d.Handle==front);if(Visible&&ours&&WindowState!=FormWindowState.Minimized)Hide();else ShowCalendar();}
        public void ShowWidget(){widget.ShowWidget();}
        void PublishWindowState(){view.SetMaximized(WindowState==FormWindowState.Maximized);}
        void SetWindowMode(FormWindowState mode){
            if(WindowState==FormWindowState.Normal)windowRestoreBounds=Bounds;
            WindowState=mode;
            // WinForms adds the native sizing frame to restored client bounds;
            // our frame is inside the client, so retain the original outer size.
            if(mode==FormWindowState.Normal&&!windowRestoreBounds.IsEmpty){Bounds=windowRestoreBounds;FitHostToScreen();}
            PublishWindowState();
        }
        public void WindowAction(string action){
            if(action=="minimize")SetWindowMode(FormWindowState.Minimized);
            else if(action=="maximize"){MaximizedBounds=Screen.FromControl(this).WorkingArea;SetWindowMode(WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized);}
            else if(action=="close")Close();
            PublishWindowState();
        }
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);ShapeHost();if(view!=null){PublishWindowState();SyncSurfaces();Toasts.Layout();}}
        public async Task Shutdown(bool restart){if(exiting)return;exiting=true;menu.Enabled=false;await store.Drain();widget.Stop();pets.Stop();alarms.Dispose();popup.Dispose();tray.Visible=false;if(registered.enabled)UnregisterHotKey(Handle,HotkeyId);if(ExitRequested!=null)ExitRequested(restart);Close();}
        protected override void OnPaintBackground(PaintEventArgs e){DesktopTheme.PaintWindow(this,e.Graphics,18,WindowState!=FormWindowState.Normal);}
        protected override void WndProc(ref Message message){
            if(message.Msg==0x0312&&message.WParam.ToInt32()==HotkeyId){Toggle();return;}
            if(message.Msg==0x031e){base.WndProc(ref message);DisableNativeFrame();return;}
            if(message.Msg==0x0085){message.Result=IntPtr.Zero;return;}
            if(message.Msg==0x0086){message.Result=new IntPtr(1);return;}
            if(message.Msg==0x00a3&&message.WParam.ToInt64()==2){WindowAction("maximize");return;}
            if(message.Msg==0x0112){
                long command=message.WParam.ToInt64()&0xfff0;
                if(command==0xf030){MaximizedBounds=Screen.FromControl(this).WorkingArea;SetWindowMode(FormWindowState.Maximized);return;}
                if(command==0xf020){SetWindowMode(FormWindowState.Minimized);return;}
                if(command==0xf120&&WindowState!=FormWindowState.Normal){SetWindowMode(FormWindowState.Normal);return;}
            }
            // Keep WS_THICKFRAME for Windows sizing/snap without drawing its
            // empty non-client frame. The small paper margin supplies resize hits.
            if(message.Msg==0x0083){message.Result=IntPtr.Zero;return;}
            if(message.Msg==0x0084){
                base.WndProc(ref message);
                long packed=message.LParam.ToInt64();var p=PointToClient(new Point((short)(packed&0xffff),(short)((packed>>16)&0xffff)));
                int code=ResizeHit(p);if(code!=0){message.Result=new IntPtr(code);return;}
                int corner=DesktopTheme.Px(this,22);
                if(p.Y>=0&&p.Y<Padding.Top&&p.X>=corner&&p.X<ClientSize.Width-corner)message.Result=new IntPtr(2);
                return;
            }
            base.WndProc(ref message);
        }
        // Sizing band along the window edge. It reaches a little past the paper margin into the
        // calendar surface (which passes these points through), so edges are easy to grab.
        // Corners get a longer grab zone along each side.
        internal int ResizeHit(Point p){
            if(WindowState!=FormWindowState.Normal)return 0;int edge=DesktopTheme.Px(this,10),corner=DesktopTheme.Px(this,22);
            bool left=p.X<edge,right=p.X>=ClientSize.Width-edge,top=p.Y<edge,bottom=p.Y>=ClientSize.Height-edge;
            if(!(left||right||top||bottom))return 0;
            bool nearLeft=p.X<corner,nearRight=p.X>=ClientSize.Width-corner,nearTop=p.Y<corner,nearBottom=p.Y>=ClientSize.Height-corner;
            if((top&&nearLeft)||(left&&nearTop))return 13;if((top&&nearRight)||(right&&nearTop))return 14;
            if((bottom&&nearLeft)||(left&&nearBottom))return 16;if((bottom&&nearRight)||(right&&nearBottom))return 17;
            return top?12:bottom?15:left?10:11;
        }
        protected override void OnFormClosing(FormClosingEventArgs e){if(!exiting&&e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();return;}base.OnFormClosing(e);}
        protected override void Dispose(bool disposing){if(disposing){pets.Dispose();notes.Dispose();widget.Dispose();alarms.Dispose();popup.Dispose();tray.Visible=false;tray.Dispose();menu.Dispose();sound.Dispose();}base.Dispose(disposing);}
        public void Reply(object value){if(AutomationReply!=null)AutomationReply(value);}
        public async void Command(Dictionary<string,object> request) {
            object id=request.ContainsKey("id")?request["id"]:null;
            try {
                string action=(string)request["action"];
                if(action=="api"){var result=await Api((string)request["method"],(string)request["path"],request.ContainsKey("body")?Codec.Json(request["body"]):"",request.ContainsKey("marker")?(string)request["marker"]:"1",request.ContainsKey("force")&&(bool)request["force"]);Reply(new{id=id,ok=true,status=result.status,body=result.body});return;}
                if(action=="widget"){
                    string command=request.ContainsKey("command")?(string)request["command"]:"inspect";
                    if(command=="select")widget.SelectDate((string)request["date"]);
                    else if(command=="complete")await widget.Complete((string)request["key"]);
                    else if(command=="bounds")widget.Bounds=new Rectangle(Convert.ToInt32(request["x"]),Convert.ToInt32(request["y"]),Convert.ToInt32(request["width"]),Convert.ToInt32(request["height"]));
                    else if(command=="close")widget.Close();
                    else if(command=="click")widget.ClickButton((string)request["name"]);
                    else if(command=="calendar-click")widget.ClickCalendar(new Point(Convert.ToInt32(request["x"]),Convert.ToInt32(request["y"])),request.ContainsKey("twice")&&(bool)request["twice"]);
                    else if(command=="capture")widget.CaptureImage((string)request["path"]);
                    else if(command=="layout"){Reply(new{id=id,ok=true,layout=widget.InspectLayout()});return;}
                    Reply(new{id=id,ok=true,widget=widget.Inspect()});return;
                }
                if(action=="pets-speech"){
                    pets.SpeechCommand(request.ContainsKey("command")?request["command"] as string:"",request.ContainsKey("utc")?request["utc"] as string:null,request.ContainsKey("path")?request["path"] as string:null);
                    Reply(new{id=id,ok=true,pets=pets.Diagnostic()});return;
                }
                if(action=="pets-send"){pets.SendUnnamed();Reply(new{id=id,ok=true,pets=pets.Diagnostic()});return;}
                if(action=="calendar-import-ics"){view.ImportIcs((string)request["path"]);Reply(new{id=id,ok=true});return;}
                if(action=="calendar-state"){Reply(new{id=id,ok=true,calendar=view.Describe()});return;}
                if(action=="calendar-bench"){if(request.ContainsKey("width"))Size=new Size(Convert.ToInt32(request["width"]),Convert.ToInt32(request["height"]));Reply(new{id=id,ok=true,bench=view.Bench()});return;}
                if(action=="pets-inspect"){Reply(new{id=id,ok=true,pets=pets.Diagnostic()});return;}
                if(action=="inspect"){IntPtr appId;GetCurrentProcessExplicitAppUserModelID(out appId);string identity=Marshal.PtrToStringUni(appId);Marshal.FreeCoTaskMem(appId);var processes=new[]{Process.GetCurrentProcess().Id};Reply(new{id=id,ok=true,visible=IsWindowVisible(Handle),window=Handle.ToInt64(),tray=tray.Visible,ready=ready,applicationId=identity,executable=Application.ExecutablePath,processIds=processes,pending=alarms.Pending(),popupVisible=popup.Visible,popupWindow=popup.Handle.ToInt64(),popupMonitor=popup.MonitorName,soundBatches=alarms.SoundBatches});return;}
                if(action=="show")ShowCalendar();else if(action=="hide")Hide();else if(action=="close-window")Close();
                else if(action=="menu"){string item=(string)request["item"];if(item=="show")ShowCalendar();else{Reply(new{id=id,ok=true});await Shutdown(item=="restart");return;}}
                else if(action=="alarm-check")alarms.CheckAt(DateTime.Parse((string)request["now"],null,DateTimeStyles.RoundtripKind).ToUniversalTime());
                else if(action=="alarm-ack")alarms.Acknowledge(request.ContainsKey("alarmId")?request["alarmId"] as string:null);
                else if(action=="alarm-snooze")alarms.Snooze((string)request["alarmId"],Convert.ToInt32(request["minutes"]));
                else if(action=="capture"){using(var image=new Bitmap(view.Width,view.Height)){view.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save((string)request["path"]);}}
                else if(action=="popup-layout"){Reply(new{id=id,ok=true,layout=popup.InspectLayout()});return;}
                else if(action=="capture-popup"){using(var image=new Bitmap(popup.Width,popup.Height)){popup.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save((string)request["path"]);}}
                else if(action=="exit"){Reply(new{id=id,ok=true});await Shutdown(false);return;}
                else throw new AppError(400,"지원하지 않는 명령입니다.");
                Reply(new{id=id,ok=true});
            }catch(Exception error){Reply(new{id=id,ok=false,error=error.Message});}
        }
    }
}
