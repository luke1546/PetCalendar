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
    public sealed class PetRecord {public string species="cat",palette="치즈",name="",fedAt="";public int personality,affinity;}
    public sealed class PetPreferences {public bool enabled,allowEmpty,summaryEnabled=true,chatterEnabled=true;public int summaryIntervalMinutes=3;public PetRecord[] pets=new PetRecord[0];public string[] notices=new string[0];public bool bedEnabled=true,toyEnabled,toyDefaultOff;public double bedFraction=.82;public string diaryTime="17:50";
        // Affinity: snacks earned by finishing schedules and tasks, what pets call the user,
        // the last day the pets were out, and completions already rewarded (latest 500).
        public int snacks;public string title="집사",lastSeen="";public string[] credited=new string[0];}

    // The engine runs on the existing calendar UI thread. No extra process, tray or browser.
    internal sealed class CalendarPets : IDisposable {
        readonly List<PetHost> active=new List<PetHost>();
        readonly Dictionary<string,PaletteBag> colors=new Dictionary<string,PaletteBag>();
        readonly SpeciesBag species=new SpeciesBag();
        readonly Random random=new Random();
        readonly string path;
        readonly Action<Exception> log;
        readonly PetSpeech speech;
        readonly PetLife life;readonly PetDiary diary;
        DateTime? testUtc;
        internal event Action<Alarm> OpenAlarm;
        internal event Action<string> Acknowledge;
        internal event Action<string,int> Snooze;
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
        internal CalendarPets(string dataDir,Func<CalendarState> snapshot,Action<Exception> error,bool fresh=false) {
            path=Path.Combine(dataDir,"pets.json");log=error;
            try {preferences=File.Exists(path)?Codec.Read<PetPreferences>(File.ReadAllText(path)):new PetPreferences{enabled=fresh};}
            catch(Exception e) {log(e);preferences=new PetPreferences();}
            if(preferences==null)preferences=new PetPreferences();
            if(preferences.summaryIntervalMinutes<1||preferences.summaryIntervalMinutes>1440)preferences.summaryIntervalMinutes=3;
            // v3.4: the yarn-ball toy is off by default; existing installs are switched off once.
            if(!preferences.toyDefaultOff){preferences.toyEnabled=false;preferences.toyDefaultOff=true;if(File.Exists(path))try{Save();}catch(Exception e){log(e);}}
            if(fresh&&!File.Exists(path))Save();
            speech=new PetSpeech(()=>active.ToArray(),snapshot,()=>testUtc??DateTime.UtcNow,a=>{if(OpenAlarm!=null)OpenAlarm(a);},id=>{if(Acknowledge!=null)Acknowledge(id);},(id,date)=>{if(OpenSchedule!=null)OpenSchedule(id,date);},log,(id,minutes)=>{if(Snooze!=null)Snooze(id,minutes);});
            // One-off notices (morning briefing, deadline eve, weekly review) are remembered here so
            // a restart does not repeat them. Only the latest 60 keys are kept.
            NameTag.FontFor=px=>{string key=DesktopTheme.FontKey;var family=key!="default"?AppFonts.Family(key):null;if(family==null)return new Font("Malgun Gothic",px,FontStyle.Bold,GraphicsUnit.Pixel);return new Font(family,px*AppFonts.Scale(key),DesktopTheme.BoldText||AppFonts.Bold(key,700)?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);};
            diary=new PetDiary(dataDir);
            life=new PetLife(()=>active.ToArray(),log);life.ConfigureBed(preferences.bedEnabled,preferences.bedFraction);life.ConfigureToy(preferences.toyEnabled);
            life.BedMoved=fraction=>{preferences.bedFraction=fraction;try{Save();}catch(Exception e){log(e);}};
            life.BedHidden=()=>NativeAction(()=>ConfigureBed(new Dictionary<string,object>{{"bedEnabled",false}}));
            speech.DiaryTime=()=>{TimeSpan t;return TimeSpan.TryParseExact(preferences.diaryTime??"",@"hh\:mm",null,out t)?t:new TimeSpan(17,50,0);};
            speech.WriteDiary=pet=>{int steps,meets,levelUp;bool bed;life.Stats(pet,out steps,out meets,out bed,out levelUp);return diary.Write(snapshot(),Rules.Key(DateTime.Now),pet,steps,meets,bed,levelUp);};
            life.Award=(pet,points)=>Award(pet,points);life.BeforeSpeak=pet=>speech.Yield(pet);speech.Busy=pet=>life.Talking(pet);
            PetLines.UserTitle=String.IsNullOrWhiteSpace(preferences.title)?"집사":preferences.title;
            speech.NoticeSeen=key=>preferences.notices!=null&&preferences.notices.Contains(key);
            speech.MarkNotice=key=>{var list=(preferences.notices??new string[0]).Concat(new[]{key}).ToList();if(list.Count>60)list=list.Skip(list.Count-60).ToList();preferences.notices=list.ToArray();try{Save();}catch(Exception e){log(e);}};
            preferences.pets=(preferences.pets??new PetRecord[0]).Where(p=>p!=null&&Species.Find(p.species)!=null).Take(256).ToArray();
            previousLoader=Sprites.ResourceOpen;previousSettings=PetHost.SettingsDirectory;previousPrefix=PetHost.WindowPrefix;previousClose=PetHost.CloseAllLabel;previousNoun=PetMenu.Noun;PetMenu.Noun="펫";
            Sprites.ResourceOpen=OpenImage;PetHost.SettingsDirectory=Path.Combine(dataDir,"pets");PetHost.WindowPrefix="펫 캘린더 · 데스크톱 펫";PetHost.CloseAllLabel="펫 모두 보내기";
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
        internal PetPreferences BackupPreferences(){return Codec.Read<PetPreferences>(Codec.Json(preferences));}
        internal static PetPreferences CheckBackup(object value){
            PetPreferences p;try{p=Codec.Read<PetPreferences>(Codec.Json(value));}catch{throw new AppError(400,"펫 백업 설정을 확인해 주세요.");}
            if(p==null||p.pets==null||p.pets.Length>256||p.summaryIntervalMinutes<1||p.summaryIntervalMinutes>1440)throw new AppError(400,"펫 백업 설정을 확인해 주세요.");
            foreach(var record in p.pets){var kind=record==null?null:Species.Find(record.species);if(kind==null||record.name==null||record.name!=CleanName(record.name)||record.personality<0||record.personality>5||!Array.Exists(Palette.For(kind),x=>x.Name==record.palette))throw new AppError(400,"펫 종류, 색상, 성격 또는 이름을 확인해 주세요.");}
            return p;
        }
        internal void RestorePreferences(PetPreferences value){
            if(enabled)SetEnabled(false);
            preferences=Codec.Read<PetPreferences>(Codec.Json(value));bool shouldEnable=preferences.enabled;preferences.enabled=false;
            if(shouldEnable)SetEnabled(true);else{Save();Notify();}
        }
        void Notify() {if(Changed!=null)Changed();}
        PetRecord Record(PetHost pet){return new PetRecord{species=pet.Kind.Id,palette=pet.PaletteName,name=pet.PetName,personality=pet.Personality,affinity=pet.Affinity,fedAt=pet.FedAtUtc==DateTime.MinValue?"":pet.FedAtUtc.ToString("o")};}
        PetRecord[] Records() {return active.Select(Record).ToArray();}
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
                    if(saved.Length==0&&!preferences.allowEmpty)SpawnInternal(Species.Cat,null,dropFromCenter:true);
                    else foreach(var record in saved) {
                        var kind=Species.Find(record.species);
                        var palette=Array.Find(Palette.For(kind),p=>p.Name==record.palette);
                        SpawnInternal(kind,palette??Palette.For(kind)[0],CleanName(record.name),record.personality,false,record);
                    }
                    preferences.enabled=true;preferences.pets=Records();Save();life.Start();Greet();if(active.Count>0)speech.Start(preferences.summaryEnabled,preferences.summaryIntervalMinutes,preferences.chatterEnabled);
                }catch {enabled=false;speech.Stop();CloseActive();CloseArchive();throw;}
            }else {
                preferences.pets=Records();preferences.enabled=false;Save();enabled=false;life.Stop();speech.Stop();CloseActive();CloseArchive();
            }
            Notify();
        }
        internal void AddFromTour() {
            if(!enabled) {
                bool createsDefault=preferences.pets.Length==0&&!preferences.allowEmpty;
                SetEnabled(true);
                if(createsDefault)return;
            }
            Add("cat");
        }
        internal void Add(string id) {
            if(!enabled)throw new AppError(409,"데스크톱 펫을 먼저 켜 주세요.");
            Species kind=id=="random"?species.Next():Species.Find(id);
            if(kind==null)throw new AppError(400,"동물 종류를 확인해 주세요.");
            bool wasEmpty=active.Count==0;SpawnInternal(kind,null,dropFromCenter:true);preferences.pets=Records();Save();if(wasEmpty)speech.Start(preferences.summaryEnabled,preferences.summaryIntervalMinutes,preferences.chatterEnabled);Notify();
        }
        void SpawnInternal(Species kind,Palette selected,string name="",int personality=0,bool dropFromCenter=false,PetRecord saved=null) {
            PaletteBag bag;if(!colors.TryGetValue(kind.Id,out bag)){bag=new PaletteBag(kind);colors.Add(kind.Id,bag);}
            double position=.1+random.NextDouble()*.8;
            var pet=new PetHost(kind,selected??bag.Next(),false,position){SpawnFromCenter=dropFromCenter};
            if(saved!=null){pet.Affinity=Math.Max(0,saved.affinity);DateTime fed;if(DateTime.TryParse(saved.fedAt,null,System.Globalization.DateTimeStyles.RoundtripKind,out fed))pet.FedAtUtc=fed.ToUniversalTime();}
            pet.SnackRequested=()=>NativeAction(()=>Feed(pet));
            pet.SnackLabel=()=>"간식 주기 ("+preferences.snacks+"개)";
            pet.IdentityDetail=()=>{int level=PetLines.Level(pet.Affinity);return " · "+PetLines.LevelName(level)+(level<5?" "+PetLines.Percent(pet.Affinity)+"%":"")+PetLines.Hearts(level);};
            pet.TagSuffix=PetLines.Hearts(PetLines.Level(pet.Affinity));
            pet.PetName=name;pet.Personality=personality>=1&&personality<=5?personality:random.Next(1,6);pet.RenameRequested=()=>EditName(pet);
            pet.PersonalityRequested=value=>NativeAction(()=>SetPersonality(pet.Handle.ToInt64(),value));
            pet.AddSpeciesRequested=choice=>NativeAction(()=>Add(choice==null?"random":choice.Id));
            pet.CloseAllRequested=()=>NativeAction(ConfirmSendUnnamed);
            pet.FormClosed+=delegate {
                active.Remove(pet);pet.Dispose();
                if(!stopping) {
                    if(active.Count==0){enabled=false;preferences.enabled=false;speech.Stop();CloseArchive();}
                    preferences.pets=Records();try{Save();}catch(Exception error){log(error);}Notify();
                }
            };
            pet.SpeechClicked=()=>{speech.PetClicked(pet);life.Petted(pet);};
            pet.SpeechDoubleClicked=()=>NativeAction(()=>speech.Preview(pet));
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
            preferences.pets=active.Except(send).Select(Record).ToArray();preferences.allowEmpty=true;
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
        internal void SetPersonality(long window,int value) {
            if(value<1||value>5)throw new AppError(400,"펫 성격을 확인해 주세요.");
            var pet=active.FirstOrDefault(p=>p.Handle.ToInt64()==window);if(pet==null)throw new AppError(404,"이 펫을 찾지 못했습니다.");
            int previous=pet.Personality;pet.Personality=value;
            try{preferences.pets=Records();Save();}catch{pet.Personality=previous;preferences.pets=Records();throw;}
            speech.PersonalityChanged(pet);Notify();
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
        // Bed and toy switches (either key may be sent).
        internal void ConfigureBed(Dictionary<string,object> request){
            object bed,toy,time,title;bool hasBed=request.TryGetValue("bedEnabled",out bed),hasToy=request.TryGetValue("toyEnabled",out toy),hasTime=request.TryGetValue("diaryTime",out time);
            if(request.TryGetValue("title",out title)){var text=(title as string??"").Trim();if(text.Length<1||text.Length>8||text.Any(Char.IsControl)||text.IndexOfAny(new[]{'{','}'})>=0)throw new AppError(400,"호칭은 1~8자로 적어 주세요.");preferences.title=text;PetLines.UserTitle=text;Save();if(!hasBed&&!hasToy&&!hasTime){Notify();return;}}
            if(hasTime){var text=time as string;TimeSpan parsed;if(text==null||!System.Text.RegularExpressions.Regex.IsMatch(text,"^([01][0-9]|2[0-3]):[0-5][0-9]$")||!TimeSpan.TryParse(text,out parsed))throw new AppError(400,"일기 쓰는 시간을 확인해 주세요.");string oldTime=preferences.diaryTime;preferences.diaryTime=text;try{Save();}catch{preferences.diaryTime=oldTime;throw;}if(!hasBed&&!hasToy){Notify();return;}}
            if((!hasBed&&!hasToy)||(hasBed&&!(bed is bool))||(hasToy&&!(toy is bool)))throw new AppError(400,"펫 침대·장난감 사용 여부를 확인해 주세요.");
            bool oldBed=preferences.bedEnabled,oldToy=preferences.toyEnabled;if(hasBed)preferences.bedEnabled=(bool)bed;if(hasToy)preferences.toyEnabled=(bool)toy;
            try{Save();}catch{preferences.bedEnabled=oldBed;preferences.toyEnabled=oldToy;throw;}
            life.ConfigureBed(preferences.bedEnabled,preferences.bedFraction);life.ConfigureToy(preferences.toyEnabled);Notify();
        }
        internal DiaryEntry[] DiaryEntries(){return diary.Entries;}
        internal void FontChanged(){foreach(var pet in active.ToArray())if(!pet.IsDisposed)pet.RefreshTag();}
        // ---------- Affinity and snacks ----------
        internal void Award(PetHost pet,int points){
            if(pet==null||pet.IsDisposed||points<=0)return;
            int before=PetLines.Level(pet.Affinity);pet.Affinity=Math.Min(100000,pet.Affinity+points);int after=PetLines.Level(pet.Affinity);
            preferences.pets=Records();try{Save();}catch(Exception e){log(e);}
            if(after>before){pet.TagSuffix=PetLines.Hearts(after);life.LeveledUp(pet,after);}
        }
        // Each finished schedule (per date) or task earns one snack and +4 for every pet, once only.
        int Credit(CalendarState before,CalendarState after){
            var keys=new List<string>();
            foreach(var e in after.events){var old=before.events.FirstOrDefault(x=>x.id==e.id);foreach(var d in e.completedDates)if(old==null||!old.completedDates.Contains(d))keys.Add("e:"+e.id+":"+d);}
            foreach(var t in after.tasks)if(t.done){var old=before.tasks.FirstOrDefault(x=>x.id==t.id);if(old==null||!old.done)keys.Add("t:"+t.id);}
            var credited=new HashSet<string>(preferences.credited??new string[0]);var fresh=keys.Where(k=>credited.Add(k)).ToList();
            if(fresh.Count==0)return 0;
            var list=(preferences.credited??new string[0]).Concat(fresh).ToList();if(list.Count>500)list=list.Skip(list.Count-500).ToList();preferences.credited=list.ToArray();
            preferences.snacks=Math.Min(999,preferences.snacks+fresh.Count);
            foreach(var pet in active.ToArray())Award(pet,4*fresh.Count);
            try{Save();}catch(Exception e){log(e);}
            return fresh.Count;
        }
        static readonly TimeSpan SnackCooldown=TimeSpan.FromMinutes(10);
        internal void FeedWindow(long window){var pet=active.FirstOrDefault(p=>p.Handle.ToInt64()==window);if(pet==null)throw new AppError(404,"이 펫을 찾지 못했습니다.");Feed(pet);}
        internal void Feed(PetHost pet){
            if(pet==null||pet.IsDisposed)return;
            if(preferences.snacks<=0){life.Speak(pet,"간식이 없어요 🍪\n할 일이나 일정을 끝내면 하나씩 생겨요!",5);return;}
            var wait=pet.FedAtUtc+SnackCooldown-DateTime.UtcNow;
            if(wait>TimeSpan.Zero){life.Speak(pet,PetLines.Full(pet.Kind.Id)+"\n("+Math.Max(1,(int)Math.Ceiling(wait.TotalMinutes))+"분 뒤에 먹을 수 있어요)",5);return;}
            preferences.snacks--;pet.FedAtUtc=DateTime.UtcNow;
            life.Speak(pet,PetLines.Say(pet.Kind.Id,PetLines.Level(pet.Affinity),"snack")+"\n🍪 남은 간식 "+preferences.snacks+"개",6);
            if(!pet.Cat.Held&&pet.Cat.Support!=null){pet.Cat.Sleeping=false;pet.Cat.GoalX=null;pet.Cat.SetPose(Pose.Groom);}
            Award(pet,3);Notify();
        }
        // First outing of the day: +2 for everyone, and a "missed you" line after two days away.
        void Greet(){
            string today=Rules.Key(DateTime.Now);if(preferences.lastSeen==today||active.Count==0)return;
            DateTime last;bool away=DateTime.TryParse(preferences.lastSeen,out last)&&(DateTime.Today-last.Date).TotalDays>=2;
            preferences.lastSeen=today;foreach(var pet in active.ToArray())Award(pet,2);try{Save();}catch(Exception e){log(e);}
            if(away){var t=new Timer{Interval=6000};t.Tick+=delegate{t.Stop();t.Dispose();var pet=active.FirstOrDefault(p=>!p.IsDisposed&&p.Visible);if(pet!=null)life.Speak(pet,PetLines.Say(pet.Kind.Id,PetLines.Level(pet.Affinity),"longtime"),7);};t.Start();}
        }
        internal void CalendarChanged(CalendarState before,CalendarState after){
            if(!enabled)return;
            int snacks=Credit(before,after);speech.PendingSnacks+=snacks;
            int reaction=PetSchedule.CompletionReaction(before,after,DateTime.Now);
            if(reaction>0)speech.Celebrate(reaction==2,before);
            else if(snacks>0){speech.PendingSnacks=0;var pet=active.FirstOrDefault(p=>!p.IsDisposed&&p.Visible&&!p.SpeechHold&&!life.Talking(p));if(pet!=null)life.Speak(pet,PetLines.Say(pet.Kind.Id,PetLines.Level(pet.Affinity),"done1")+"\n🍪 간식 +"+snacks+" (펫 우클릭 → 간식 주기)",6);}
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
        internal object Inspect() {return new {enabled,count=active.Count,chatterEnabled=preferences.chatterEnabled,bedEnabled=preferences.bedEnabled,toyEnabled=preferences.toyEnabled,snacks=preferences.snacks,title=String.IsNullOrWhiteSpace(preferences.title)?"집사":preferences.title,affinity=active.Select(p=>{int level=PetLines.Level(p.Affinity);return new{name=String.IsNullOrWhiteSpace(p.PetName)?p.PaletteName+" "+p.Kind.Name:p.PetName,level,label=PetLines.LevelName(level),percent=PetLines.Percent(p.Affinity),hearts=PetLines.Hearts(level).Trim(),fed=p.FedAtUtc+TimeSpan.FromMinutes(10)>DateTime.UtcNow};}).ToArray(),diaryTime=preferences.diaryTime??"17:50",summaryEnabled=preferences.summaryEnabled,summaryIntervalMinutes=preferences.summaryIntervalMinutes,species=Species.All.Select(s=>new{id=s.Id,label=s.Name}).ToArray()};}
        internal object Diagnostic() {return new {enabled,count=active.Count,packLoaded=archive!=null,decodedAtlases=Sprites.SourceCount,loopCount=SimulationLoop.Count,cacheBytes=Sprites.CachedBytes,speech=speech.Inspect(),pets=active.Select(p=>new{species=p.Kind.Id,palette=p.PaletteName,name=p.PetName,personality=p.Personality,window=p.Handle.ToInt64(),x=p.Cat.X,y=p.Cat.Y,size=p.Cat.Size,pose=p.Cat.State.ToString()}).ToArray()};}
        void CloseActive() {
            stopping=true;
            try {foreach(var pet in active.ToArray()){pet.Close();pet.Dispose();}active.Clear();}
            finally {stopping=false;}
        }
        void CloseArchive() {if(archive!=null){archive.Dispose();archive=null;}if(archiveStream!=null){archiveStream.Dispose();archiveStream=null;}Sprites.DisposeSources();}
        internal void Stop() {
            if(disposed)return;
            if(enabled){preferences.enabled=true;preferences.pets=Records();try{Save();}catch(Exception error){log(error);}}
            life.Stop();speech.Stop();CloseActive();CloseArchive();enabled=false;
        }
        public void Dispose() {
            if(disposed)return;Stop();speech.Dispose();life.Dispose();disposed=true;
            Sprites.ResourceOpen=previousLoader;PetHost.SettingsDirectory=previousSettings;PetHost.WindowPrefix=previousPrefix;PetHost.CloseAllLabel=previousClose;PetMenu.Noun=previousNoun;
        }
    }
    internal sealed class PetSendDialog : Form {
        internal PetSendDialog(int count) {
            Text="펫 모두 보내기";AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.CenterScreen;
            Font=new Font("Segoe UI",11+DesktopTheme.TextScale);BackColor=DesktopTheme.Paper;ForeColor=DesktopTheme.Ink;ClientSize=new Size(390,186);Padding=new Padding(20);DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);
            var text=new Label{Text="이름 없는 펫 "+count+"마리를 정말 보내시겠어요?\n\n이름을 지은 펫은 남아 있어요.\n데스크톱 펫 기능은 켜진 상태로 유지됩니다.",Dock=DockStyle.Fill,UseMnemonic=false};
            var actions=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=36,FlowDirection=FlowDirection.RightToLeft};
            var send=new WidgetButton{Text="보내기",DialogResult=DialogResult.OK,Primary=true,Size=new Size(88,30)};var cancel=new WidgetButton{Text="취소",DialogResult=DialogResult.Cancel,Size=new Size(88,30)};
            actions.Controls.Add(cancel);actions.Controls.Add(send);Controls.Add(text);Controls.Add(actions);CancelButton=cancel;AcceptButton=cancel;
            Shown+=delegate{cancel.Focus();};
        }
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);DesktopTheme.ShapeWindow(this,14,false);}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);DesktopTheme.ShapeWindow(this,14,false);}
        protected override void OnDpiChanged(DpiChangedEventArgs e){base.OnDpiChanged(e);DesktopTheme.ShapeWindow(this,14,false);}
        protected override void OnPaintBackground(PaintEventArgs e){DesktopTheme.PaintWindow(this,e.Graphics,14,false);}
    }
    // Borderless, painted like the other pet dialogs (no gray system title bar).
    internal sealed class PetNameDialog : Form {
        readonly TextBox input=new TextBox{MaxLength=24,BorderStyle=BorderStyle.None};
        readonly Panel field=new Panel{Height=36,Dock=DockStyle.Top};
        internal string PetName {get{return input.Text.Trim();}}
        [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
        internal PetNameDialog(string name,string species) {
            Text="펫 이름 짓기 · "+species;AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.CenterScreen;
            Font=DesktopTheme.UiFont(11+DesktopTheme.TextScale);BackColor=DesktopTheme.Paper;ForeColor=DesktopTheme.Ink;ClientSize=new Size(380,222);Padding=new Padding(22,18,22,18);DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);
            var title=new Label{Text="펫 이름 짓기",Dock=DockStyle.Top,Height=32,Font=DesktopTheme.UiFont(13+DesktopTheme.TextScale,FontStyle.Bold),UseMnemonic=false};
            var prompt=new Label{Text=species+"에게 이름을 지어 주세요 (최대 24자)",Dock=DockStyle.Top,Height=28,UseMnemonic=false};
            input.Name="pet-name";input.AccessibleName="펫 이름";input.Text=name;input.BackColor=ColorTranslator.FromHtml("#fffaf1");input.ForeColor=DesktopTheme.Ink;
            field.BackColor=DesktopTheme.Paper;field.Controls.Add(input);
            field.Resize+=delegate{int h=input.PreferredHeight;input.SetBounds(12,(field.Height-h)/2,Math.Max(1,field.Width-24),h);};
            field.Paint+=(sender,e)=>{e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;bool focus=input.Focused;var r=new RectangleF(1,1,field.Width-3,field.Height-3);
                using(var shape=DesktopTheme.Round(r,8))using(var fill=new SolidBrush(input.BackColor))using(var pen=new Pen(focus?DesktopTheme.Orange:ColorTranslator.FromHtml("#d5c7ad"),focus?2:1)){e.Graphics.FillPath(fill,shape);e.Graphics.DrawPath(pen,shape);}};
            input.GotFocus+=delegate{field.Invalidate();};input.LostFocus+=delegate{field.Invalidate();};field.Click+=delegate{input.Focus();};
            var hint=new Label{Text="비워두면 종류와 색상으로 표시합니다.",Dock=DockStyle.Top,Height=30,ForeColor=DesktopTheme.Muted,TextAlign=ContentAlignment.MiddleLeft,UseMnemonic=false};
            var actions=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=36,FlowDirection=FlowDirection.RightToLeft};
            var save=new WidgetButton{Text="저장",DialogResult=DialogResult.OK,Primary=true,Size=new Size(88,30)};var cancel=new WidgetButton{Text="취소",DialogResult=DialogResult.Cancel,Size=new Size(88,30)};
            actions.Controls.Add(save);actions.Controls.Add(cancel);
            Controls.Add(hint);Controls.Add(field);Controls.Add(prompt);Controls.Add(title);Controls.Add(actions);AcceptButton=save;CancelButton=cancel;
            // Drag the dialog by any empty spot.
            MouseEventHandler drag=(sender,e)=>{if(e.Button!=MouseButtons.Left)return;ReleaseCapture();SendMessage(Handle,0x00a1,new IntPtr(2),IntPtr.Zero);};
            MouseDown+=drag;title.MouseDown+=drag;prompt.MouseDown+=drag;hint.MouseDown+=drag;
            Shown+=delegate{input.Focus();input.SelectAll();};
        }
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);DesktopTheme.ShapeWindow(this,14,false);}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);DesktopTheme.ShapeWindow(this,14,false);}
        protected override void OnDpiChanged(DpiChangedEventArgs e){base.OnDpiChanged(e);DesktopTheme.ShapeWindow(this,14,false);}
        protected override void OnPaintBackground(PaintEventArgs e){DesktopTheme.PaintWindow(this,e.Graphics,14,false);}
    }

}
