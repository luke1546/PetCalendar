using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using DesktopPet;

namespace PetCalendarDesktop {
    internal sealed class PetSpeech : IDisposable {
        readonly Func<PetHost[]> pets;
        readonly Func<CalendarState> state;
        readonly Func<DateTime> now;
        readonly Action<Exception> log;
        readonly Timer timer=new Timer{Interval=1000};
        readonly PetChatter chatter=new PetChatter();
        readonly PetSpeechClock clock=new PetSpeechClock();
        readonly Action<Alarm> openAlarm;
        readonly Action<string> acknowledge;
        readonly Action<string,int> snooze;
        readonly Action<string,string> openSchedule;
        List<Alarm> alerts=new List<Alarm>();
        PetBubble bubble;
        PetHost speaker;
        PetSummary summary;
        Alarm current;
        DateTime expires,lastTick,manualUntil;
        bool running,summaryEnabled=true,chatterEnabled=true,manual;
        int minutes=3,speakerIndex,pendingCelebration;
        string celebrationDay;
        readonly HashSet<string> celebrationKeys=new HashSet<string>();
        // Remembers which one-off notices were shown (persisted by the owner).
        internal Func<string,bool> NoticeSeen=key=>false;internal Action<string> MarkNotice=key=>{};
        DateTime noticeCheck;
        internal PetSpeech(Func<PetHost[]> hosts,Func<CalendarState> snapshot,Func<DateTime> utc,Action<Alarm> open,Action<string> ack,Action<string,string> schedule,Action<Exception> error,Action<string,int> snoozeAlarm=null) {
            pets=hosts;state=snapshot;now=utc;openAlarm=open;acknowledge=ack;openSchedule=schedule;log=error;snooze=snoozeAlarm;
            timer.Tick+=delegate{try{Tick();}catch(Exception e){log(e);ClearBubble();}};
        }
        internal void Start(bool enabled,int interval,bool useChatter=true) {running=true;Configure(enabled,interval);ConfigureChatter(useChatter);chatter.Reset();timer.Start();}
        internal void Configure(bool enabled,int interval) {summaryEnabled=enabled;minutes=interval;clock.Reset(now(),minutes);if(!enabled && current==null)ClearBubble();}
        internal void ConfigureChatter(bool enabled){if(chatterEnabled==enabled)return;chatterEnabled=enabled;chatter.Reset();}
        internal void PersonalityChanged(PetHost pet){chatter.Reschedule(pet,now());}
        internal void Resume() {chatter.Reset();clock.Reset(now(),minutes);lastTick=now();Tick();}
        internal void SetAlerts(List<Alarm> values) {
            bool had=alerts.Count>0;
            alerts=values.GroupBy(a=>a.id).Select(g=>g.First()).OrderBy(a=>a.createdAt).ThenBy(a=>a.id,StringComparer.Ordinal).ToList();
            if(current!=null&&!alerts.Any(a=>a.id==current.id)){current=null;ClearBubble();}
            if(had&&alerts.Count==0)clock.Reset(now(),minutes);
            if(running)Tick();
        }
        internal void Celebrate(bool allDone,CalendarState before=null){
            if(!running)return;
            string today=Rules.Key(now().ToLocalTime());
            if(pendingCelebration==0||celebrationDay!=today)celebrationKeys.Clear();
            celebrationDay=today;
            var old=before==null?new HashSet<string>():new HashSet<string>(Rules.Occurrences(before.events,today,today).Where(o=>Rules.Done(o.@event,o.startDate)).Select(o=>o.key));
            foreach(var occurrence in Rules.Occurrences(state().events,today,today).Where(o=>Rules.Done(o.@event,o.startDate)&&!old.Contains(o.key)))celebrationKeys.Add(occurrence.key);
            pendingCelebration=allDone?2:1;Tick();
        }
        void RefreshCelebration(DateTime local){
            if(pendingCelebration==0&&(summary==null||summary.Kind!="celebration"))return;
            string today=Rules.Key(local);var occurrences=Rules.Occurrences(state().events,today,today);
            bool valid=celebrationDay==today&&occurrences.Any(o=>celebrationKeys.Contains(o.key)&&Rules.Done(o.@event,o.startDate));
            if(!valid){pendingCelebration=0;celebrationKeys.Clear();if(summary!=null&&summary.Kind=="celebration")ClearBubble();return;}
            bool allDone=occurrences.All(o=>Rules.Done(o.@event,o.startDate));
            if(pendingCelebration>0)pendingCelebration=allDone?2:1;
            if(summary!=null&&summary.Kind=="celebration"&&speaker!=null)summary.Text=CelebrationText(allDone);
        }
        internal void Preview() {
            if(!running)throw new AppError(409,"데스크톱 펫을 먼저 켜 주세요.");
            if(alerts.Count>0)throw new AppError(409,"미확인 알림을 확인한 뒤 오늘 일정 안내를 들어 주세요.");
            manual=true;manualUntil=now().AddSeconds(20);Tick();
        }
        internal void Preview(PetHost pet) {
            if(!running||pet.IsDisposed||alerts.Count>0)return;
            ClearBubble();speaker=pet;speaker.SpeechHold=true;
            Preview();
        }
        bool Eligible(PetHost p) {return !p.IsDisposed&&p.ReadyForSpeech&&p.Cat.Support!=null&&p.Cat.State!=Pose.Crouch;}
        bool SelectSpeaker() {
            // Once speaking, stay attached through dragging, jumping and temporary hiding.
            // Only a closed pet relinquishes its message to another pet.
            if(speaker!=null&&!speaker.IsDisposed)return speaker.Visible;
            ReleaseSpeaker();
            var candidates=pets().Where(Eligible).ToArray();
            if(candidates.Length==0)return false;
            speaker=candidates[(speakerIndex++&Int32.MaxValue)%candidates.Length];speaker.SpeechHold=true;
            return true;
        }
        void EnsureBubble() {
            if(bubble!=null)return;
            bubble=new PetBubble();
            bubble.DismissRequested+=Dismiss;
            bubble.SnoozeRequested+=minutes=>{if(current!=null&&snooze!=null)snooze(current.id,minutes);};
            bubble.OpenRequested+=delegate {
                if(current!=null){var alarm=current;openAlarm(alarm);}
                else NextSummary();
            };
        }
        internal void PetClicked(PetHost pet){if(pet==speaker&&current==null&&summary!=null)NextSummary();}
        void NextSummary() {
            if(summary==null||current!=null)return;
            var utc=now();summary=PetSchedule.Build(state(),utc.ToLocalTime(),summary.EventId,summary.StartDate);
            expires=utc.AddSeconds(20);lastTick=utc;
            bubble.Present(PetVoice.Summary(summary,speaker.Kind.Id),false,0,summary.EventId!=null);
            if(SelectSpeaker())bubble.Follow(speaker);
        }
        internal void Dismiss() {
            if(current!=null)acknowledge(current.id);
            else ClearBubble();
        }
        internal void Open() {if(bubble!=null)bubble.ActivateOpen();}
        internal void Tick() {
            if(!running)return;
            DateTime utc=now();double delta=lastTick==default(DateTime)?0:Math.Max(0,Math.Min(2,(utc-lastTick).TotalSeconds));lastTick=utc;
            RefreshCelebration(utc.ToLocalTime());
            if(alerts.Count>0) {
                manual=false;
                if(current==null){ClearBubble();current=alerts[0];}
                if(SelectSpeaker()) {
                    EnsureBubble();bubble.Present(PetVoice.Alert(current,speaker.Kind.Id),true,alerts.Count,current.eventId!="");bubble.Follow(speaker);
                }else if(bubble!=null)bubble.Hide();
            }else {
                if(current!=null){current=null;ClearBubble();clock.Reset(utc,minutes);}
                if(pendingCelebration>0&&SelectSpeaker()){
                    bool allDone=pendingCelebration==2;pendingCelebration=0;manual=false;
                    summary=new PetSummary{Kind="celebration",Text=CelebrationText(allDone)};expires=utc.AddSeconds(8);
                    EnsureBubble();
                    if(!speaker.Cat.Paused&&!speaker.Cat.Held&&!speaker.Cat.Sleeping)speaker.Cat.Jump();
                }
                if(summaryEnabled&&summary==null&&!manual&&utc>=noticeCheck){
                    noticeCheck=utc.AddSeconds(20);
                    var notice=PetNotices.Next(state(),utc.ToLocalTime(),NoticeSeen);
                    if(notice!=null&&SelectSpeaker()){MarkNotice(notice.Key);summary=notice;expires=utc.AddSeconds(15);EnsureBubble();bubble.Present(notice.Text,false,0,false);bubble.Follow(speaker);clock.Reset(utc,minutes);}
                }
                bool due=summaryEnabled&&clock.Due(utc,minutes);
                if(manual && utc>manualUntil)manual=false;
                if(manual || (due&&summary==null)) {
                    if(SelectSpeaker()) {
                        summary=PetSchedule.Build(state(),utc.ToLocalTime());manual=false;expires=utc.AddSeconds(20);
                        EnsureBubble();bubble.Present(PetVoice.Summary(summary,speaker.Kind.Id),false,0,summary.EventId!=null);bubble.Follow(speaker);
                    }
                }
                if(summary!=null) {
                    if(!SelectSpeaker()){ClearBubble();}
                    else {
                        if(bubble.Hovered)expires=expires.AddSeconds(delta);
                        if(utc>=expires)ClearBubble();else {bubble.Present(PetVoice.Summary(summary,speaker.Kind.Id),false,0,summary.EventId!=null);bubble.Follow(speaker);}
                    }
                }
            }
            if(chatterEnabled)chatter.Tick(pets(),utc,alerts.Count>0||summary!=null||manual);
            timer.Interval=alerts.Count>0||summary!=null||manual||chatter.ActiveCount>0?100:1000;
        }
        string CelebrationText(bool allDone){string text=PetVoice.Celebration(speaker==null?"":speaker.Kind.Id,allDone);if(allDone){int streak=PetNotices.Streak(state(),Rules.Key(now().ToLocalTime()));if(streak>=2)text+="\n"+streak+"일 연속 모두 해냈어요 🔥";}return text;}
        void ReleaseSpeaker(){if(speaker!=null&&!speaker.IsDisposed)speaker.SpeechHold=false;speaker=null;}
        void ClearBubble(){ReleaseSpeaker();summary=null;if(bubble!=null)bubble.Hide();}
        internal object Inspect() {return new{running,summaryEnabled,chatterEnabled,intervalMinutes=minutes,nextUtc=running?clock.NextUtc.ToString("o"):null,alarmId=current==null?null:current.id,pending=alerts.Count,chatter=chatter.Inspect(),speakerWindow=speaker==null?0:speaker.Handle.ToInt64(),bubble=bubble==null?null:bubble.Inspect()};}
        internal void Capture(string path) {if(bubble==null||!bubble.Visible)throw new InvalidOperationException("말풍선이 표시되지 않았습니다.");using(var image=new System.Drawing.Bitmap(bubble.Width,bubble.Height)){bubble.DrawToBitmap(image,new System.Drawing.Rectangle(0,0,image.Width,image.Height));image.Save(path);}}
        internal void Stop(){chatter.Reset();running=false;timer.Stop();current=null;manual=false;pendingCelebration=0;celebrationKeys.Clear();alerts.Clear();ClearBubble();if(bubble!=null){bubble.Dispose();bubble=null;}lastTick=default(DateTime);}
        public void Dispose(){Stop();timer.Dispose();}
    }
    // Each pet owns its deadline and bubble. One shared tick drives all chatter.
    internal sealed class PetChatter {
        sealed class Entry {internal DateTime Next,Expires;internal int Last=-1;internal PetBubble Bubble;}
        sealed class Greetings {public Greetings(){}internal string Morning,Night,Holiday;}
        readonly System.Runtime.CompilerServices.ConditionalWeakTable<PetHost,Greetings> greetings=new System.Runtime.CompilerServices.ConditionalWeakTable<PetHost,Greetings>();
        readonly Dictionary<PetHost,Entry> entries=new Dictionary<PetHost,Entry>();
        readonly Random random=new Random();
        DateTime observed;
        internal int ActiveCount {get{return entries.Values.Count(e=>e.Bubble!=null);}}
        DateTime Next(PetHost pet,DateTime utc){int[] minimum={30,60,120,180,300},maximum={60,120,180,300,600};int level=Math.Max(1,Math.Min(5,pet.Personality))-1;return utc.AddSeconds(random.Next(minimum[level],maximum[level]+1));}
        internal void Reschedule(PetHost pet,DateTime utc){Entry entry;if(entries.TryGetValue(pet,out entry))entry.Next=Next(pet,utc);}
        static void Close(Entry entry){if(entry.Bubble!=null){entry.Bubble.Dispose();entry.Bubble=null;}}
        internal void Tick(PetHost[] pets,DateTime utc,bool busy) {
            if(observed!=default(DateTime)&&(utc<observed||utc-observed>TimeSpan.FromMinutes(5))){Reset();}
            observed=utc;
            foreach(var pet in entries.Keys.Where(p=>p.IsDisposed||!pets.Contains(p)).ToArray()){Close(entries[pet]);entries.Remove(pet);}
            foreach(var pet in pets) {
                if(pet.IsDisposed)continue;
                Entry entry;if(!entries.TryGetValue(pet,out entry)){entry=new Entry{Next=Next(pet,utc)};entries.Add(pet,entry);}
                if(entry.Bubble!=null) {
                    if(busy||utc>=entry.Expires)Close(entry);
                    else if(pet.Visible)entry.Bubble.Follow(pet);else entry.Bubble.Hide();
                }
                if(utc<entry.Next)continue;
                entry.Next=Next(pet,utc);
                if(busy||!pet.ReadyForSpeech||pet.Cat.Support==null||pet.Cat.State==Pose.Crouch)continue;
                Close(entry);
                int count=PetVoice.ChatterCount(pet.Kind.Id);if(count==0)continue;
                int choice=entry.Last<0?random.Next(count):count==1?0:random.Next(count-1);if(choice>=entry.Last&&entry.Last>=0&&count>1)choice++;
                entry.Last=choice;entry.Expires=utc.AddSeconds(10);
                var bubble=new PetBubble();entry.Bubble=bubble;
                bubble.DismissRequested+=()=>Close(entry);bubble.OpenRequested+=()=>Close(entry);
                var greeting=greetings.GetOrCreateValue(pet);
                var local=utc.ToLocalTime();string day=Rules.Key(local);bool morning=local.Hour>=6&&local.Hour<10,night=local.Hour>=23||local.Hour<5;
                string message=PetVoice.Chatter(pet.Kind.Id,choice);
                string holiday=PetVoice.Holiday(local);
                if(holiday!=null&&greeting.Holiday!=day){greeting.Holiday=day;message=PetVoice.HolidayGreeting(pet.Kind.Id,holiday);}
                else if(morning&&greeting.Morning!=day){greeting.Morning=day;message=PetVoice.TimeGreeting(pet.Kind.Id,true);}
                else if(night&&greeting.Night!=day){greeting.Night=day;message=PetVoice.TimeGreeting(pet.Kind.Id,false);if(!pet.Cat.Paused&&!pet.Cat.Sleeping)pet.Cat.Nap();}
                bubble.Present(message,false,0,false,true);bubble.Follow(pet);
            }
        }
        internal object Inspect(){return entries.Select(e=>new{window=e.Key.Handle.ToInt64(),species=e.Key.Kind.Id,nextUtc=e.Value.Next.ToString("o"),expiresUtc=e.Value.Bubble==null?null:e.Value.Expires.ToString("o"),bubble=e.Value.Bubble==null?null:e.Value.Bubble.Inspect()}).ToArray();}
        internal void Reset(){foreach(var entry in entries.Values)Close(entry);entries.Clear();observed=default(DateTime);}
    }

}
