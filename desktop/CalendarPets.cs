using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Drawing;
using System.Windows.Forms;
using DesktopPet;

namespace PetCalendarDesktop {
    public sealed class PetRecord {public string species="cat",palette="치즈",name="";}
    public sealed class PetPreferences {public bool enabled,allowEmpty,summaryEnabled=true,chatterEnabled=true;public int summaryIntervalMinutes=3;public PetRecord[] pets=new PetRecord[0];}

    // The engine runs on the existing calendar UI thread. No extra process, tray or browser.
    internal sealed class CalendarPets : IDisposable {
        readonly List<PetHost> active=new List<PetHost>();
        readonly Dictionary<string,PaletteBag> colors=new Dictionary<string,PaletteBag>();
        readonly SpeciesBag species=new SpeciesBag();
        readonly Random random=new Random();
        readonly string path;
        readonly Action<Exception> log;
        readonly PetSpeech speech;
        DateTime? testUtc;
        internal event Action<Alarm> OpenAlarm;
        internal event Action<string> Acknowledge;
        internal event Action<string,string> OpenSchedule;
        readonly Func<string,Stream> previousLoader;
        readonly string previousSettings,previousPrefix,previousClose,previousNoun;
        PetPreferences preferences;
        ZipArchive archive;
        Stream archiveStream;
        bool enabled,stopping,disposed;
        public event Action Changed;
        internal bool Enabled {get {return enabled;}}
        internal bool CanNotify {get{return enabled&&active.Count>0;}}
        internal CalendarPets(string dataDir,Func<CalendarState> snapshot,Action<Exception> error) {
            path=Path.Combine(dataDir,"pets.json");log=error;
            try {preferences=File.Exists(path)?Codec.Read<PetPreferences>(File.ReadAllText(path)):new PetPreferences();}
            catch(Exception e) {log(e);preferences=new PetPreferences();}
            if(preferences==null)preferences=new PetPreferences();
            if(preferences.summaryIntervalMinutes<1||preferences.summaryIntervalMinutes>1440)preferences.summaryIntervalMinutes=3;
            speech=new PetSpeech(()=>active.ToArray(),snapshot,()=>testUtc??DateTime.UtcNow,a=>{if(OpenAlarm!=null)OpenAlarm(a);},id=>{if(Acknowledge!=null)Acknowledge(id);},(id,date)=>{if(OpenSchedule!=null)OpenSchedule(id,date);},log);
            preferences.pets=(preferences.pets??new PetRecord[0]).Where(p=>p!=null&&Species.Find(p.species)!=null).Take(256).ToArray();
            previousLoader=Sprites.ResourceOpen;previousSettings=PetHost.SettingsDirectory;previousPrefix=PetHost.WindowPrefix;previousClose=PetHost.CloseAllLabel;previousNoun=PetMenu.Noun;PetMenu.Noun="펫";
            Sprites.ResourceOpen=OpenImage;PetHost.SettingsDirectory=Path.Combine(dataDir,"pets");PetHost.WindowPrefix="업무 캘린더 · 데스크톱 펫";PetHost.CloseAllLabel="펫 모두 보내기";
        }
        Stream OpenImage(string name) {
            if(!enabled)throw new InvalidOperationException("데스크톱 펫이 꺼져 있습니다.");
            if(archive==null) {
                archiveStream=Assembly.GetExecutingAssembly().GetManifestResourceStream("petcalendar.pets.zip");
                if(archiveStream==null)throw new IOException("동물 그림을 찾지 못했습니다.");
                try {archive=new ZipArchive(archiveStream,ZipArchiveMode.Read,true);}
                catch {archiveStream.Dispose();archiveStream=null;throw;}
            }
            var entry=archive.GetEntry(name);
            if(entry==null || entry.Length>12*1024*1024)throw new IOException("동물 그림을 확인하지 못했습니다: "+name);
            var result=new MemoryStream((int)entry.Length);
            try {using(var input=entry.Open())input.CopyTo(result);result.Position=0;return result;}
            catch {result.Dispose();throw;}
        }
        void Save() {StateStore.Atomic(path,Codec.Json(preferences),null);}
        void Notify() {if(Changed!=null)Changed();}
        PetRecord[] Records() {return active.Select(p=>new PetRecord{species=p.Kind.Id,palette=p.PaletteName,name=p.PetName}).ToArray();}
        internal void Restore() {
            if(!preferences.enabled)return;
            try {SetEnabled(true);}
            catch(Exception error) {log(error);enabled=false;speech.Stop();CloseActive();CloseArchive();Notify();}
        }
        internal void SetEnabled(bool value) {
            if(disposed)throw new ObjectDisposedException("동물");
            if(value==enabled)return;
            if(value) {
                var saved=preferences.pets;enabled=true;
                try {
                    if(saved.Length==0&&!preferences.allowEmpty)SpawnInternal(Species.Cat,null);
                    else foreach(var record in saved) {
                        var kind=Species.Find(record.species);
                        var palette=Array.Find(Palette.For(kind),p=>p.Name==record.palette);
                        SpawnInternal(kind,palette??Palette.For(kind)[0],CleanName(record.name));
                    }
                    preferences.enabled=true;preferences.pets=Records();Save();if(active.Count>0)speech.Start(preferences.summaryEnabled,preferences.summaryIntervalMinutes,preferences.chatterEnabled);
                }catch {enabled=false;speech.Stop();CloseActive();CloseArchive();throw;}
            }else {
                preferences.pets=Records();preferences.enabled=false;Save();enabled=false;speech.Stop();CloseActive();CloseArchive();
            }
            Notify();
        }
        internal void Add(string id) {
            if(!enabled)throw new AppError(409,"데스크톱 펫을 먼저 켜 주세요.");
            Species kind=id=="random"?species.Next():Species.Find(id);
            if(kind==null)throw new AppError(400,"동물 종류를 확인해 주세요.");
            bool wasEmpty=active.Count==0;SpawnInternal(kind,null);preferences.pets=Records();Save();if(wasEmpty)speech.Start(preferences.summaryEnabled,preferences.summaryIntervalMinutes,preferences.chatterEnabled);Notify();
        }
        void SpawnInternal(Species kind,Palette selected,string name="") {
            PaletteBag bag;if(!colors.TryGetValue(kind.Id,out bag)){bag=new PaletteBag(kind);colors.Add(kind.Id,bag);}
            double position=.1+random.NextDouble()*.8;
            var pet=new PetHost(kind,selected??bag.Next(),false,position);
            pet.PetName=name;pet.RenameRequested=()=>EditName(pet);
            pet.AddSpeciesRequested=choice=>NativeAction(()=>Add(choice==null?"random":choice.Id));
            pet.CloseAllRequested=()=>NativeAction(ConfirmSendUnnamed);
            pet.FormClosed+=delegate {
                active.Remove(pet);pet.Dispose();
                if(!stopping) {
                    if(active.Count==0){enabled=false;preferences.enabled=false;speech.Stop();CloseArchive();}
                    preferences.pets=Records();try{Save();}catch(Exception error){log(error);}Notify();
                }
            };
            pet.SpeechClicked=()=>speech.PetClicked(pet);
            active.Add(pet);
            try {pet.Show();}
            catch {active.Remove(pet);pet.Dispose();throw;}
        }
        internal void ConfirmSendUnnamed() {
            if(!enabled)return;
            int count=active.Count(p=>String.IsNullOrWhiteSpace(p.PetName));
            if(count==0){MessageBox.Show("이름 없는 펫이 없어 보낼 펫이 없어요.\n이름을 지은 펫은 그대로 남아 있어요.","펫 모두 보내기",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
            using(var dialog=new PetSendDialog(count)){if(dialog.ShowDialog()==DialogResult.OK)SendUnnamed();}
        }
        internal void SendUnnamed() {
            if(!enabled)return;
            var send=active.Where(p=>String.IsNullOrWhiteSpace(p.PetName)).ToArray();
            if(send.Length==0)return;
            var previous=preferences.pets;bool oldEmpty=preferences.allowEmpty;
            preferences.pets=active.Except(send).Select(p=>new PetRecord{species=p.Kind.Id,palette=p.PaletteName,name=p.PetName}).ToArray();preferences.allowEmpty=true;
            try{Save();}catch{preferences.pets=previous;preferences.allowEmpty=oldEmpty;throw;}
            stopping=true;
            try{foreach(var pet in send){pet.Close();pet.Dispose();}}
            finally{stopping=false;}
            if(active.Count==0){speech.Stop();CloseArchive();}else speech.Tick();
            Notify();
        }
        static string CleanName(string value){value=(value??"").Trim();if(value.Length>24){value=value.Substring(0,24);if(Char.IsHighSurrogate(value[value.Length-1]))value=value.Substring(0,value.Length-1);}return new string(value.Where(c=>!Char.IsControl(c)).ToArray());}
        internal void Rename(long window,string value) {
            if(value==null||value.Trim().Length>24||value.Any(Char.IsControl))throw new AppError(400,"이름은 줄바꿈 없이 최대 24자로 입력해 주세요.");
            var pet=active.FirstOrDefault(p=>p.Handle.ToInt64()==window);
            if(pet==null)throw new AppError(404,"이 펫을 찾지 못했습니다.");
            string old=pet.PetName;pet.PetName=value.Trim();
            try{preferences.pets=Records();Save();}catch{pet.PetName=old;preferences.pets=Records();throw;}
            Notify();
        }
        void EditName(PetHost pet) {
            using(var dialog=new PetNameDialog(pet.PetName,pet.Kind.Name)) {
                if(dialog.ShowDialog()==DialogResult.OK) {
                    try{Rename(pet.Handle.ToInt64(),dialog.PetName);}
                    catch(Exception error){log(error);MessageBox.Show(error.Message,"펫 이름",MessageBoxButtons.OK,MessageBoxIcon.Information);}
                }
            }
        }
        void NativeAction(Action action) {try{action();}catch(Exception error){log(error);Notify();}}
        internal void ConfigureSpeech(Dictionary<string,object> request) {
            object enabledValue,intervalValue;
            if(!request.TryGetValue("summaryEnabled",out enabledValue)||!(enabledValue is bool)||!request.TryGetValue("summaryIntervalMinutes",out intervalValue)||!(intervalValue is int||intervalValue is long||intervalValue is double||intervalValue is decimal))throw new AppError(400,"안내 사용 여부와 주기를 확인해 주세요.");
            double interval=Convert.ToDouble(intervalValue);
            if(Double.IsNaN(interval)||Double.IsInfinity(interval)||interval!=Math.Floor(interval)||interval<1||interval>1440)throw new AppError(400,"안내 주기는 1~1,440분 사이의 정수로 입력해 주세요.");
            preferences.summaryEnabled=(bool)enabledValue;preferences.summaryIntervalMinutes=(int)interval;Save();speech.Configure(preferences.summaryEnabled,preferences.summaryIntervalMinutes);Notify();
        }
        internal void ConfigureChatter(Dictionary<string,object> request) {
            object value;if(!request.TryGetValue("chatterEnabled",out value)||!(value is bool))throw new AppError(400,"일상 대화 사용 여부를 확인해 주세요.");
            bool previous=preferences.chatterEnabled;preferences.chatterEnabled=(bool)value;
            try{Save();}catch{preferences.chatterEnabled=previous;throw;}
            speech.ConfigureChatter(preferences.chatterEnabled);Notify();
        }
        internal void Announce(){if(enabled&&active.Count==0)throw new AppError(409,"펫을 한 마리 추가한 뒤 오늘 일정 안내를 들어 주세요.");speech.Preview();}
        internal void Deliver(List<Alarm> values){speech.SetAlerts(enabled?values:new List<Alarm>());}
        internal void ResumeSpeech(){speech.Resume();}
        internal void SpeechCommand(string command,string utc,string image) {
            if(command=="tick"){testUtc=DateTime.Parse(utc,null,System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();speech.Tick();}
            else if(command=="resume")speech.Resume();
            else if(command=="dismiss")speech.Dismiss();
            else if(command=="open")speech.Open();
            else if(command=="capture")speech.Capture(image);
            else throw new AppError(400,"지원하지 않는 말풍선 검사입니다.");
        }
        internal void Home() {foreach(var pet in active)pet.BringToFloor();}
        internal object Inspect() {return new {enabled,count=active.Count,chatterEnabled=preferences.chatterEnabled,summaryEnabled=preferences.summaryEnabled,summaryIntervalMinutes=preferences.summaryIntervalMinutes,species=Species.All.Select(s=>new{id=s.Id,label=s.Name}).ToArray()};}
        internal object Diagnostic() {return new {enabled,count=active.Count,packLoaded=archive!=null,decodedAtlases=Sprites.SourceCount,loopCount=SimulationLoop.Count,cacheBytes=Sprites.CachedBytes,speech=speech.Inspect(),pets=active.Select(p=>new{species=p.Kind.Id,palette=p.PaletteName,name=p.PetName,window=p.Handle.ToInt64(),x=p.Cat.X,y=p.Cat.Y,size=p.Cat.Size,pose=p.Cat.State.ToString()}).ToArray()};}
        void CloseActive() {
            stopping=true;
            try {foreach(var pet in active.ToArray()){pet.Close();pet.Dispose();}active.Clear();}
            finally {stopping=false;}
        }
        void CloseArchive() {if(archive!=null){archive.Dispose();archive=null;}if(archiveStream!=null){archiveStream.Dispose();archiveStream=null;}Sprites.DisposeSources();}
        internal void Stop() {
            if(disposed)return;
            if(enabled){preferences.enabled=true;preferences.pets=Records();try{Save();}catch(Exception error){log(error);}}
            speech.Stop();CloseActive();CloseArchive();enabled=false;
        }
        public void Dispose() {
            if(disposed)return;Stop();speech.Dispose();disposed=true;
            Sprites.ResourceOpen=previousLoader;PetHost.SettingsDirectory=previousSettings;PetHost.WindowPrefix=previousPrefix;PetHost.CloseAllLabel=previousClose;PetMenu.Noun=previousNoun;
        }
    }
    internal sealed class PetSendDialog : Form {
        internal PetSendDialog(int count) {
            Text="펫 모두 보내기";AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
            FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.CenterScreen;
            Font=new Font("Segoe UI",10);BackColor=DesktopTheme.Paper;ForeColor=DesktopTheme.Ink;ClientSize=new Size(390,186);Padding=new Padding(20);
            var text=new Label{Text="이름 없는 펫 "+count+"마리를 정말 보내시겠어요?\n\n이름을 지은 펫은 남아 있어요.\n데스크톱 펫 기능은 켜진 상태로 유지됩니다.",Dock=DockStyle.Fill,UseMnemonic=false};
            var actions=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=36,FlowDirection=FlowDirection.RightToLeft};
            var send=new Button{Text="보내기",DialogResult=DialogResult.OK,AutoSize=true};var cancel=new Button{Text="취소",DialogResult=DialogResult.Cancel,AutoSize=true};
            actions.Controls.Add(send);actions.Controls.Add(cancel);Controls.Add(text);Controls.Add(actions);CancelButton=cancel;AcceptButton=cancel;
            Shown+=delegate{cancel.Focus();};
        }
    }
    internal sealed class PetNameDialog : Form {
        readonly TextBox input=new TextBox{MaxLength=24,Dock=DockStyle.Fill};
        internal string PetName {get{return input.Text.Trim();}}
        internal PetNameDialog(string name,string species) {
            Text="펫 이름 짓기 · "+species;AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
            FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.CenterScreen;
            Font=new Font("Segoe UI",10);BackColor=DesktopTheme.Paper;ForeColor=DesktopTheme.Ink;ClientSize=new Size(370,186);Padding=new Padding(18);
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,28));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
            layout.Controls.Add(new Label{Text=species+"에게 이름을 지어 주세요 (최대 24자)",Dock=DockStyle.Fill,UseMnemonic=false},0,0);
            input.Name="pet-name";input.AccessibleName="펫 이름";input.Text=name;layout.Controls.Add(input,0,1);
            layout.Controls.Add(new Label{Text="비워두면 종류와 색상으로 표시합니다.",Dock=DockStyle.Fill,ForeColor=DesktopTheme.Muted,UseMnemonic=false},0,2);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
            var save=new Button{Text="저장",DialogResult=DialogResult.OK,AutoSize=true};var cancel=new Button{Text="취소",DialogResult=DialogResult.Cancel,AutoSize=true};
            actions.Controls.Add(save);actions.Controls.Add(cancel);layout.Controls.Add(actions,0,3);Controls.Add(layout);AcceptButton=save;CancelButton=cancel;
            Shown+=delegate{input.Focus();input.SelectAll();};
        }
    }

}
