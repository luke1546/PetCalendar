using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using PetCalendarDesktop;

// Runs against the freshly built assembly. Every fixture uses the supplied review directory.
static class ReviewChecks {
    static readonly List<object> results = new List<object>();
    static string root;
    [DllImport("user32.dll")]static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
    [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr window,int id);
    static CalendarEvent Event(string repeat = "none") {
        return new CalendarEvent { id="review", title="검토 일정", startDate="2026-01-01", endDate="2026-01-01",
            startTime="09:00", endTime="10:00", category="work", note="", repeat=repeat, repeatUntil="", reminder=10 };
    }
    static CalendarState Initial() { var s=new CalendarState(); s.settings.hotkey.enabled=false; return s; }
    static StateStore Store(string name, CalendarState state) {
        var dir=Path.Combine(root,name); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir,"calendar.json"),Codec.Json(state)); return new StateStore(dir);
    }
    static void Check(bool condition, string message) { if(!condition)throw new Exception(message); }
    static void Run(string name, Action action) {
        try { action(); results.Add(new { name=name, passed=true }); }
        catch(Exception e) { results.Add(new { name=name, passed=false, error=e.ToString() }); }
    }
    static CalendarState Copy(CalendarState s) { return Codec.Read<CalendarState>(Codec.Json(s)); }
    static Task Apply(Hotkey key) { return Task.FromResult(0); }
    static T Pump<T>(Task<T> task) {
        var until=DateTime.UtcNow.AddSeconds(15);
        while(!task.IsCompleted && DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(1);}
        Check(task.IsCompleted,"UI operation timed out");return task.GetAwaiter().GetResult();
    }
    [STAThread] static int Main(string[] args) {
        root=Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        Run("stale_revision_preserves_current_data",()=>{
            var store=Store("conflict",Initial()); var stale=Copy(store.Snapshot);
            var next=Copy(store.Snapshot); next.events=new[]{Event()}; store.Save(Codec.Json(next),Apply).GetAwaiter().GetResult();
            try { store.Save(Codec.Json(stale),Apply).GetAwaiter().GetResult(); throw new Exception("Stale save accepted"); }
            catch(AppError e) { Check(e.Status==409,"Expected revision conflict"); }
            Check(store.Snapshot.events.Length==1 && new StateStore(store.DirectoryPath).Snapshot.events.Length==1,"Saved data lost");
        });
        Run("locked_calendar_rolls_back_hotkey_and_preserves_bytes",()=>{
            var store=Store("locked",Initial()); var before=File.ReadAllBytes(store.FilePath); var next=Copy(store.Snapshot);
            next.settings.hotkey.key=75; var calls=new List<int>(); bool failed=false;
            using(var locked=new FileStream(store.FilePath,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                try { store.Save(Codec.Json(next),key=>{calls.Add(key.key);return Task.FromResult(0);}).GetAwaiter().GetResult(); }
                catch(IOException) { failed=true; }
            }
            Check(failed,"Expected locked-file failure"); Check(calls.SequenceEqual(new[]{75,192}),"Hotkey rollback missing");
            Check(before.SequenceEqual(File.ReadAllBytes(store.FilePath)) && store.Snapshot.revision==0,"Failed save changed data");
            Check(Directory.GetFiles(store.DirectoryPath,"*.tmp").Length==0,"Temporary file leaked");
            store.Save(Codec.Json(next),Apply).GetAwaiter().GetResult(); Check(store.Snapshot.revision==1,"Retry failed");
        });
        Run("corrupted_calendar_is_preserved",()=>{
            var dir=Path.Combine(root,"corrupt");Directory.CreateDirectory(dir);var file=Path.Combine(dir,"calendar.json");
            File.WriteAllText(file,"{invalid");bool failed=false;try{new StateStore(dir);}catch(AppError){failed=true;}
            Check(failed && File.ReadAllText(file)=="{invalid","Corrupted original was overwritten");
        });
        Run("backup_retention_keeps_30_and_protects_named_upgrade_backups",()=>{
            var store=Store("retention",Initial());var protectedFile=Path.Combine(store.DirectoryPath,"backups","calendar-upgrade-review.json");
            File.WriteAllText(protectedFile,"keep");
            for(int i=0;i<35;i++)store.Save(Codec.Json(Copy(store.Snapshot)),Apply).GetAwaiter().GetResult();
            Check(Directory.GetFiles(Path.Combine(store.DirectoryPath,"backups"),"*.json").Length==31 && File.Exists(protectedFile),"Backup retention incorrect");
        });
        Run("notification_write_failure_must_not_report_committed_calendar_as_failed",()=>{
            var initial=Initial();initial.events=new[]{Event()};var store=Store("notification-failure",initial);
            var ledger=new AlarmLedger();ledger.unread.Add(new Alarm{id="review-alarm",eventId="review",startDate="2026-01-01",title="review",body="",createdAt=0});
            var file=Path.Combine(store.DirectoryPath,"notifications.json");File.WriteAllText(file,Codec.Json(ledger));
            using(var alarms=new AlarmService(store))using(var locked=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                var next=Copy(store.Snapshot);next.events=new CalendarEvent[0];Exception error=null;
                try{store.Save(Codec.Json(next),Apply).GetAwaiter().GetResult();}catch(Exception e){error=e;}
                var persisted=new StateStore(store.DirectoryPath).Snapshot;
                Check(error==null || persisted.revision==0,"Save threw "+(error==null?"no error":error.GetType().Name)+" after calendar.json committed revision "+persisted.revision+" and deleted the event");
            }
        });
        Run("alarm_delivery_recovers_after_temporary_ledger_write_failure",()=>{
            var now=DateTime.Now;var initial=Initial();initial.settings.desktopPopups=true;
            var e=Event();e.startDate=e.endDate=Rules.Key(now);e.startTime=now.ToString("HH:mm");e.endTime="";e.reminder=0;initial.events=new[]{e};
            var store=Store("alarm-retry",initial);var file=Path.Combine(store.DirectoryPath,"notifications.json");File.WriteAllText(file,Codec.Json(new AlarmLedger()));
            using(var alarms=new AlarmService(store)){
                int failures=0,deliveries=0;alarms.Error+=error=>{failures++;};alarms.Delivered+=(pending,fresh,force)=>{if(fresh)deliveries++;};
                var due=Rules.Date(e.startDate).Add(TimeSpan.Parse(e.startTime)).ToUniversalTime();
                using(var locked=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))alarms.CheckAt(due);
                Check(failures==1,"Expected one ledger write failure");alarms.CheckAt(due.AddMinutes(1));
                Check(deliveries==1,"Ledger became writable, but delivery count is "+deliveries+"; pending="+alarms.Pending().Count+", sent="+alarms.Sent().Count);
            }
        });
        Run("dday_finds_next_nonexcluded_yearly_occurrence",()=>{
            var e=Event("yearly");e.startDate=e.endDate="2024-02-29";e.pinned=true;e.excludedDates=new[]{"2027-02-28"};
            var next=Rules.DdayOccurrence(e,"2026-03-01");
            Check(next.startDate=="2028-02-29","Expected 2028-02-29; actual "+next.startDate);
        });
        Run("ics_weekly_count_4000_is_preserved",()=>{
            var text="BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:Count review\r\nDTSTART;VALUE=DATE:20260101\r\nRRULE:FREQ=WEEKLY;COUNT=4000\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
            var imported=IcsImport.Parse(text,"work",null);Check(imported.Events.Count==1,"Missing imported event");
            var count=Rules.Occurrences(imported.Events,"2026-01-01","2105-01-01").Count;
            Check(count==4000,"COUNT=4000 imported as "+count+" occurrences; simplified warnings="+imported.Simplified);
        });
        Run("ics_all_day_exclusive_end_exdate_and_unsupported_rule_warning",()=>{
            var text="BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:Span\r\nDTSTART;VALUE=DATE:20260101\r\nDTEND;VALUE=DATE:20260104\r\nRRULE:FREQ=DAILY;COUNT=3\r\nEXDATE;VALUE=DATE:20260102\r\nEND:VEVENT\r\nBEGIN:VEVENT\r\nSUMMARY:Unsupported\r\nDTSTART;VALUE=DATE:20260101\r\nRRULE:FREQ=WEEKLY;INTERVAL=3\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
            var imported=IcsImport.Parse(text,"work",10);var e=imported.Events[0];
            Check(e.endDate=="2026-01-03" && e.excludedDates.SequenceEqual(new[]{"2026-01-02"}),"All-day span or EXDATE lost");
            Check(imported.Simplified==1 && imported.Events[1].repeat=="none","Unsupported rule silently expanded");
        });
        Run("combined_backup_must_not_report_success_when_diary_cannot_be_written",()=>{
            var store=Store("partial-backup",Initial());var dir=store.DirectoryPath;
            File.WriteAllText(Path.Combine(dir,"pets.json"),"{\"enabled\":false,\"allowEmpty\":true,\"toyDefaultOff\":true,\"pets\":[]}");
            File.WriteAllText(Path.Combine(dir,"ui-state.json"),"{\"quickTourDone\":true,\"drafts\":{}}");
            var diaryFile=Path.Combine(dir,"pet-diary.json");File.WriteAllText(diaryFile,"[]");
            int status;string persisted;
            using(var host=new DesktopHost(Path.GetDirectoryName(typeof(Entry).Assembly.Location),dir,Path.Combine(dir,"profile"),true)) {
                var backup=Codec.Object(Codec.Json(Pump(host.Api("GET","/api/backup","" )).body));
                backup["expectedRevision"]=0;
                backup["diary"]=new[]{new DiaryEntry{date="2026-11-15",pet="review",species="cat",text="must persist"}};
                using(var locked=new FileStream(diaryFile,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                    var restored=Pump(host.Api("POST","/api/backup",Codec.Json(backup)));
                    status=restored.status;persisted=File.ReadAllText(diaryFile);
                    Check(status!=200 || persisted.Contains("must persist"),"Restore returned status 200; pet-diary.json is still "+persisted+" and calendar revision is "+new StateStore(dir).Snapshot.revision);
                }
            }
        });
        Run("real_hotkey_collision_rejects_save_without_changing_state",()=>{
            var store=Store("hotkey-collision",Initial());var dir=store.DirectoryPath;
            File.WriteAllText(Path.Combine(dir,"pets.json"),"{\"enabled\":false,\"allowEmpty\":true,\"toyDefaultOff\":true,\"pets\":[]}");
            uint selected=0;const int id=0x4c99;
            foreach(uint key in new uint[]{74,75,76,112,113,114})if(RegisterHotKey(IntPtr.Zero,id,3,key)){selected=key;break;}
            Check(selected!=0,"No free key available for isolated collision fixture");
            try{using(var host=new DesktopHost(Path.GetDirectoryName(typeof(Entry).Assembly.Location),dir,Path.Combine(dir,"profile"),true)){
                var before=File.ReadAllText(store.FilePath);var next=Initial();next.settings.hotkey=new Hotkey{enabled=true,modifiers=3,key=(int)selected};
                var response=Pump(host.Api("PUT","/api/state",Codec.Json(next)));
                Check(response.status==422,"Expected real OS collision response 422; got "+response.status);
                Check(File.ReadAllText(store.FilePath)==before,"Hotkey failure changed persisted state");
                var current=(CalendarState)Pump(host.Api("GET","/api/state","")).body;
                Check(current.revision==0&&!current.settings.hotkey.enabled,"Hotkey failure changed current state");
            }}finally{UnregisterHotKey(IntPtr.Zero,id);}
        });
        Console.WriteLine(Codec.Json(new { checks=results })); return 0;
    }
}
