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
        readonly Action<string,string> openSchedule;
        List<Alarm> alerts=new List<Alarm>();
        PetBubble bubble;
        PetHost speaker;
        PetSummary summary;
        Alarm current;
        DateTime expires,lastTick,manualUntil;
        bool running,summaryEnabled=true,chatterEnabled=true,manual;
        int minutes=3,speakerIndex;
        internal PetSpeech(Func<PetHost[]> hosts,Func<CalendarState> snapshot,Func<DateTime> utc,Action<Alarm> open,Action<string> ack,Action<string,string> schedule,Action<Exception> error) {
            pets=hosts;state=snapshot;now=utc;openAlarm=open;acknowledge=ack;openSchedule=schedule;log=error;
            timer.Tick+=delegate{try{Tick();}catch(Exception e){log(e);ClearBubble();}};
        }
        internal void Start(bool enabled,int interval,bool useChatter=true) {running=true;Configure(enabled,interval);ConfigureChatter(useChatter);chatter.Reset();timer.Start();}
        internal void Configure(bool enabled,int interval) {summaryEnabled=enabled;minutes=interval;clock.Reset(now(),minutes);if(!enabled && current==null)ClearBubble();}
        internal void ConfigureChatter(bool enabled){if(chatterEnabled==enabled)return;chatterEnabled=enabled;chatter.Reset();}
        internal void Resume() {chatter.Reset();clock.Reset(now(),minutes);lastTick=now();Tick();}
        internal void SetAlerts(List<Alarm> values) {
            bool had=alerts.Count>0;
            alerts=values.GroupBy(a=>a.id).Select(g=>g.First()).OrderBy(a=>a.createdAt).ThenBy(a=>a.id,StringComparer.Ordinal).ToList();
            if(current!=null&&!alerts.Any(a=>a.id==current.id)){current=null;ClearBubble();}
            if(had&&alerts.Count==0)clock.Reset(now(),minutes);
            if(running)Tick();
        }
        internal void Preview() {
            if(!running)throw new AppError(409,"데스크톱 펫을 먼저 켜 주세요.");
            if(alerts.Count>0)throw new AppError(409,"미확인 알림을 확인한 뒤 오늘 일정 안내를 들어 주세요.");
            manual=true;manualUntil=now().AddSeconds(20);Tick();
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
            if(alerts.Count>0) {
                manual=false;
                if(current==null){ClearBubble();current=alerts[0];}
                if(SelectSpeaker()) {
                    EnsureBubble();bubble.Present(PetVoice.Alert(current,speaker.Kind.Id),true,alerts.Count,current.eventId!="");bubble.Follow(speaker);
                }else if(bubble!=null)bubble.Hide();
            }else {
                if(current!=null){current=null;ClearBubble();clock.Reset(utc,minutes);}
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
        void ReleaseSpeaker(){if(speaker!=null&&!speaker.IsDisposed)speaker.SpeechHold=false;speaker=null;}
        void ClearBubble(){ReleaseSpeaker();summary=null;if(bubble!=null)bubble.Hide();}
        internal object Inspect() {return new{running,summaryEnabled,chatterEnabled,intervalMinutes=minutes,nextUtc=running?clock.NextUtc.ToString("o"):null,alarmId=current==null?null:current.id,pending=alerts.Count,chatter=chatter.Inspect(),speakerWindow=speaker==null?0:speaker.Handle.ToInt64(),bubble=bubble==null?null:bubble.Inspect()};}
        internal void Capture(string path) {if(bubble==null||!bubble.Visible)throw new InvalidOperationException("말풍선이 표시되지 않았습니다.");using(var image=new System.Drawing.Bitmap(bubble.Width,bubble.Height)){bubble.DrawToBitmap(image,new System.Drawing.Rectangle(0,0,image.Width,image.Height));image.Save(path);}}
        internal void Stop(){chatter.Reset();running=false;timer.Stop();current=null;manual=false;alerts.Clear();ClearBubble();if(bubble!=null){bubble.Dispose();bubble=null;}lastTick=default(DateTime);}
        public void Dispose(){Stop();timer.Dispose();}
    }
    // Each pet owns its deadline and bubble. One shared tick drives all chatter.
    internal sealed class PetChatter {
        sealed class Entry {internal DateTime Next,Expires;internal int Last=-1;internal PetBubble Bubble;}
        readonly Dictionary<PetHost,Entry> entries=new Dictionary<PetHost,Entry>();
        readonly Random random=new Random();
        DateTime observed;
        internal int ActiveCount {get{return entries.Values.Count(e=>e.Bubble!=null);}}
        DateTime Next(DateTime utc){return utc.AddSeconds(random.Next(60,121));}
        static void Close(Entry entry){if(entry.Bubble!=null){entry.Bubble.Dispose();entry.Bubble=null;}}
        internal void Tick(PetHost[] pets,DateTime utc,bool busy) {
            if(observed!=default(DateTime)&&(utc<observed||utc-observed>TimeSpan.FromMinutes(5))){Reset();}
            observed=utc;
            foreach(var pet in entries.Keys.Where(p=>p.IsDisposed||!pets.Contains(p)).ToArray()){Close(entries[pet]);entries.Remove(pet);}
            foreach(var pet in pets) {
                if(pet.IsDisposed)continue;
                Entry entry;if(!entries.TryGetValue(pet,out entry)){entry=new Entry{Next=Next(utc)};entries.Add(pet,entry);}
                if(entry.Bubble!=null) {
                    if(busy||utc>=entry.Expires)Close(entry);
                    else if(pet.Visible)entry.Bubble.Follow(pet);else entry.Bubble.Hide();
                }
                if(utc<entry.Next)continue;
                entry.Next=Next(utc);
                if(busy||!pet.ReadyForSpeech||pet.Cat.Support==null||pet.Cat.State==Pose.Crouch)continue;
                Close(entry);
                int count=PetVoice.ChatterCount(pet.Kind.Id);if(count==0)continue;
                int choice=entry.Last<0?random.Next(count):count==1?0:random.Next(count-1);if(choice>=entry.Last&&entry.Last>=0&&count>1)choice++;
                entry.Last=choice;entry.Expires=utc.AddSeconds(10);
                var bubble=new PetBubble();entry.Bubble=bubble;
                bubble.DismissRequested+=()=>Close(entry);bubble.OpenRequested+=()=>Close(entry);
                bubble.Present(PetVoice.Chatter(pet.Kind.Id,choice),false,0,false,true);bubble.Follow(pet);
            }
        }
        internal object Inspect(){return entries.Select(e=>new{window=e.Key.Handle.ToInt64(),species=e.Key.Kind.Id,nextUtc=e.Value.Next.ToString("o"),expiresUtc=e.Value.Bubble==null?null:e.Value.Expires.ToString("o"),bubble=e.Value.Bubble==null?null:e.Value.Bubble.Inspect()}).ToArray();}
        internal void Reset(){foreach(var entry in entries.Values)Close(entry);entries.Clear();observed=default(DateTime);}
    }

}
