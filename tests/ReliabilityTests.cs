using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PetCalendarDesktop {
    // Regression checks for the 2026-11-15 review (docs/review-2026-11-15.md):
    // R1 a committed save is reported as saved, R3 alarms survive a locked ledger,
    // R4 D-day finds the real next date, R5 .ics COUNT is kept exactly,
    // and a month typed one digit at a time ("0" then "1") never throws or stores a partial date.
    // R6 the month title, toolbar buttons and bottom-bar texts never overlap in small windows or with large text.
    internal static class ReliabilityTests {
        static int count;
        static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);count++;}
        static string Temp(){var dir=Path.Combine(Path.GetTempPath(),"petcalendar-reliability-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);return dir;}
        static CalendarEvent Event(string id,string date,string repeat="none",string until="",string[] excluded=null,int? reminder=null,string time=""){
            return new CalendarEvent{id=id,title="일정 "+id,startDate=date,endDate=date,startTime=time,endTime="",category="work",note="",repeat=repeat,repeatUntil=until,reminder=reminder,pinned=false,pinnedDate="",completedDates=new string[0],excludedDates=excluded??new string[0]};
        }
        // Holds a file open for reading only, so StateStore.Atomic cannot replace it.
        static FileStream Lock(string path){return new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);}

        [STAThread] static int Main(string[] args){
            try{
                Dday();Ics();DateTyping();ToolbarLayout();Task.Run(()=>Saves()).Wait();
                Console.WriteLine(count+" reliability checks passed");return 0;
            }catch(Exception error){Console.WriteLine("FAIL "+(error is AggregateException?error.InnerException.Message:error.Message));return 1;}
        }

        static void Dday(){
            // R4: 2024-02-29 yearly, 2027-02-28 excluded, seen from 2026-03-01: next is 2028-02-29.
            var leap=Event("leap","2024-02-29","yearly",excluded:new[]{"2027-02-28"});
            Check(Rules.DdayOccurrence(leap,"2026-03-01").startDate=="2028-02-29","yearly D-day skips an excluded year and lands on the next real date");
            var weekly=Event("weekly","2026-01-05","weekly",excluded:Enumerable.Range(0,20).Select(i=>Rules.AddDays("2026-03-02",7*i)).ToArray());
            Check(Rules.DdayOccurrence(weekly,"2026-03-01").startDate==Rules.AddDays("2026-03-02",140),"weekly D-day looks past twenty excluded weeks");
            var ended=Event("ended","2026-01-01","monthly","2026-06-30");
            Check(Rules.DdayOccurrence(ended,"2026-11-15").startDate=="2026-06-01","an ended series counts down to its last date");
            var allGone=Event("gone","2026-01-01","yearly","2027-12-31",new[]{"2026-01-01","2027-01-01"});
            Check(Rules.DdayOccurrence(allGone,"2026-11-15").startDate=="2026-01-01","a series with no dates left keeps its start date");
        }

        // Typing into an existing date's month goes through the real key handlers, as a keyboard would.
        // R6: at 800×600 and with large text, the month title shares no pixels with the toolbar buttons or the bottom
        // count, the bottom hint is dropped before it can reach the count, and the buttons still work after the layout changes.
        static void ToolbarLayout(){
            string dir=Temp();var store=new StateStore(dir);var scale=DesktopTheme.TextScale;var bold=DesktopTheme.BoldText;
            try{
                // 800×600 is the host's MinimumSize (Host.cs), so it is the narrowest window the layout has to support.
                foreach(var size in new[]{new Size(800,600),new Size(1280,800)})foreach(var large in new[]{false,true})foreach(var week in new[]{false,true}){
                    DesktopTheme.TextScale=large?2:0;DesktopTheme.BoldText=large;
                    string where=size.Width+"x"+size.Height+(large?" large text":"")+(week?" week view":" month view");
                    using(var form=new Form{ClientSize=size}){
                        var view=new CalendarView(null,store,new UiPrefs(dir,true));view.Dock=DockStyle.Fill;form.Controls.Add(view);form.Show();
                        view.SetWeekView(week);view.Update();Application.DoEvents();view.Update();
                        var bounds=new Rectangle(0,0,view.Width,view.Height);var toolbar=view.ToolbarBox;var title=view.ToolbarTitleBox;var buttons=view.ToolbarHits();
                        Check(buttons.Count==6,"all six toolbar buttons are laid out ("+where+")");
                        // Full is the unclipped rect, so a button pushed past the toolbar is caught instead of being cut down to fit.
                        var clashing=buttons.Where(b=>title.IntersectsWith(b.Full)||!toolbar.Contains(b.Full)).Select(b=>b.Id).ToList();
                        Check(clashing.Count==0&&bounds.Contains(title),"the month title and toolbar buttons do not overlap or leave the toolbar ("+where+", two rows: "+view.ToolbarTwoRows+(clashing.Count>0?"; "+string.Join(",",clashing):"")+")");
                        Check(buttons.All(b=>b.Focusable&&b.Click!=null),"toolbar buttons stay in the keyboard tab order ("+where+")");
                        var hint=view.BottomHintBox;var count=view.BottomCountBox;
                        Check(bounds.Contains(count)&&(hint.IsEmpty||!hint.IntersectsWith(count)),"the bottom hint never overlaps the schedule count ("+where+")");
                        var before=view.selected;buttons.Single(b=>b.Id=="next-month").Click();
                        Check(view.selected!=before,"the next button still moves the calendar ("+where+")");
                        buttons.Single(b=>b.Id=="view-week").Click();
                        Check(view.weekView,"the week button still switches the view ("+where+")");
                    }
                }
            }finally{DesktopTheme.TextScale=scale;DesktopTheme.BoldText=bold;Directory.Delete(dir,true);}
            Console.WriteLine("PASS R6 toolbar layout checks in both view modes, normal and large text");
        }

        static void DateTyping(){
            var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            var keyDown=typeof(DateField).GetMethod("OnKeyDown",flags);var keyPress=typeof(DateField).GetMethod("OnKeyPress",flags);
            var field=new DateField();field.Value="2026-11-15";
            var seen=new List<string>();field.ValueChanged+=(s,e)=>seen.Add(field.Value);
            Action<KeyPressEventArgs> type=k=>keyPress.Invoke(field,new object[]{k});
            keyDown.Invoke(field,new object[]{new KeyEventArgs(Keys.Right)});
            type(new KeyPressEventArgs('0'));
            Check(seen.Count>0&&seen.Last()=="","a month typed as a lone 0 reads as no date and does not throw");
            type(new KeyPressEventArgs('1'));
            Check(field.Value=="2026-01-15","typing 01 into the month completes to 2026-01-15");
            type(new KeyPressEventArgs('0'));
            Check(field.Value=="","a day typed as a lone 0 is not stored as 2026-01-00");
            type(new KeyPressEventArgs('5'));
            Check(field.Value=="2026-01-05","typing 05 into the day completes to 2026-01-05");
        }

        static void Ics(){
            // R5: weekly COUNT=4000 needs 76 years; the last date is exact, not cut at a horizon.
            var r=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART;VALUE=DATE:20260101\r\nRRULE:FREQ=WEEKLY;COUNT=4000\r\nSUMMARY:긴 반복\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
            var e=r.Events.Single();
            Check(e.repeat=="weekly"&&e.repeatUntil==Rules.AddDays("2026-01-01",7*3999)&&r.Simplified==0,"weekly COUNT=4000 keeps all 4000 dates (until "+e.repeatUntil+")");
            Check(Rules.Occurrences(new[]{e},e.startDate,e.repeatUntil,false).Count==4000,"the imported series has exactly 4000 dates");
            r=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART;VALUE=DATE:20260101\r\nRRULE:FREQ=DAILY;COUNT=500000\r\nSUMMARY:너무 긴 반복\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
            Check(r.Events.Single().repeat=="none"&&r.Simplified==1,"a COUNT the app cannot hold is reported, not silently cut");
        }

        static async Task Saves(){
            string dir=Temp();var store=new StateStore(dir);var failures=new List<Exception>();store.ListenerFailed+=failures.Add;
            string tomorrow=Rules.Key(DateTime.Today.AddDays(1));
            var state=CalendarView.Clone(store.Snapshot);state.events=new[]{Event("a",tomorrow,reminder:10,time:"10:00"),Event("b",tomorrow,reminder:10,time:"11:00")};
            await store.Save(Codec.Json(state),h=>Task.FromResult(0));
            using(var alarms=new AlarmService(store)){
                var delivered=new List<string>();alarms.Delivered+=(list,fresh,silent)=>{if(fresh)delivered.AddRange(list.Select(a=>a.eventId));};
                var errors=new List<Exception>();alarms.Error+=errors.Add;
                string ledger=Path.Combine(dir,"notifications.json");
                // First alarm, ledger writable.
                alarms.CheckAt(DateTime.Today.AddDays(1).AddHours(9).AddMinutes(51).ToUniversalTime());
                Check(delivered.Count(id=>id=="a")==1&&File.Exists(ledger),"first reminder delivered and recorded");
                // R3: the next alarm comes while the ledger is locked: still delivered, written once unlocked.
                using(Lock(ledger)){
                    alarms.CheckAt(DateTime.Today.AddDays(1).AddHours(10).AddMinutes(51).ToUniversalTime());
                    Check(delivered.Count(id=>id=="b")==1,"reminder is delivered while its ledger file is locked");
                    Check(errors.Count>0,"the failed ledger write is reported to the log");
                }
                alarms.CheckAt(DateTime.Today.AddDays(1).AddHours(10).AddMinutes(53).ToUniversalTime());
                Check(delivered.Count(id=>id=="b")==1,"a locked-then-freed ledger does not deliver the reminder twice");
                Check(File.ReadAllText(ledger).Contains(tomorrow)&&Codec.Read<AlarmLedger>(File.ReadAllText(ledger)).sent.Keys.Count(k=>k.StartsWith("b@"))==1,"the ledger is written once the file is free again");
                // R1: deleting a schedule with an unread alert while the ledger is locked is still a successful save.
                var next=CalendarView.Clone(store.Snapshot);next.events=next.events.Where(x=>x.id!="a").ToArray();
                CalendarState saved=null;Exception thrown=null;
                using(Lock(ledger)){try{saved=await store.Save(Codec.Json(next),h=>Task.FromResult(0));}catch(Exception error){thrown=error;}}
                Check(thrown==null&&saved!=null,"a committed save is reported as saved even when the alarm ledger is locked"+(thrown!=null?" ("+thrown.Message+")":""));
                var onDisk=Rules.Validate(File.ReadAllText(Path.Combine(dir,"calendar.json")));
                Check(onDisk.revision==saved.revision&&onDisk.events.All(x=>x.id!="a")&&store.Snapshot.revision==saved.revision,"response, memory and file agree after the save");
            }
            // A listener that throws is logged through ListenerFailed, not turned into a failed save.
            Action boom=()=>{throw new IOException("listener failed");};store.Changed+=boom;
            var again=CalendarView.Clone(store.Snapshot);again.tasks=new[]{new CalendarTask{id="t",title="할 일",dueDate="",createdAt="2026-11-15T00:00:00.000Z",done=false}};
            var result=await store.Save(Codec.Json(again),h=>Task.FromResult(0));store.Changed-=boom;
            Check(result.tasks.Length==1&&failures.Any(f=>f.Message=="listener failed"),"a throwing Changed listener is logged and the save still succeeds");
            try{Directory.Delete(dir,true);}catch(Exception){}
        }
    }
}
