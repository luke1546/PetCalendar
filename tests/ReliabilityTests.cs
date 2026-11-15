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
                Dday();Ics();IcsDuplicates();IcsRepeatDates();LeapDayIcs();DateTyping();TaskShortcuts();ToolbarLayout();WeekYear();Backups();WheelSteps();PopupWheels();Task.Run(()=>AlarmCatchUp()).Wait();Task.Run(()=>Saves()).Wait();
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
            // A pinned occurrence that has passed hands D-day to the next real date; one still ahead stays pinned.
            var passed=Event("passed","2026-11-01","daily");passed.pinnedDate="2026-11-15";
            Check(Rules.DdayOccurrence(passed,"2026-11-16").startDate=="2026-11-16","a pinned occurrence that has passed moves D-day to the next date");
            var ahead=Event("ahead","2026-11-01","daily");ahead.pinnedDate="2026-11-20";
            Check(Rules.DdayOccurrence(ahead,"2026-11-16").startDate=="2026-11-20","a pinned occurrence still ahead keeps D-day");
            var skipped=Event("skipped","2026-11-02","weekly",excluded:new[]{"2026-11-16"});skipped.pinnedDate="2026-11-09";
            Check(Rules.DdayOccurrence(skipped,"2026-11-16").startDate=="2026-11-23","a passed pinned occurrence moves past an excluded date to the next one");
            var endedPinned=Event("endedPinned","2026-11-01","daily","2026-11-14");endedPinned.pinnedDate="2026-11-10";
            Check(Rules.DdayOccurrence(endedPinned,"2026-11-16").startDate=="2026-11-14","a passed pinned occurrence of an ended series counts down to its last date");
            DdayChoiceFollowsToday();
        }

        // The D-day picker judges pinned dates by the real today, not by the date selected in the calendar:
        // a pinned occurrence still ahead of today stays the row even when the calendar is set far past it.
        static void DdayChoiceFollowsToday(){
            string dir=Temp();var store=new StateStore(dir);
            try{
                using(var form=new Form{ClientSize=new Size(1280,800)}){
                    var view=new CalendarView(null,store,new UiPrefs(dir,true));view.Dock=DockStyle.Fill;form.Controls.Add(view);form.Show();
                    string today=Rules.Key(DateTime.Today);
                    var daily=Event("picker","2000-01-01","daily");daily.pinned=true;daily.pinnedDate=Rules.AddDays(today,3);
                    view.state.events=new[]{daily};
                    view.selected=Rules.AddDays(today,30);
                    var list=new ChoiceList(view);list.Rebuild();
                    var rows=(List<Occurrence>)typeof(ChoiceList).GetField("rows",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(list);
                    Check(rows.Count==1&&rows[0].startDate==Rules.AddDays(today,3),"the D-day picker keeps a pinned occurrence still ahead of today when the calendar is set later");
                }
            }finally{Directory.Delete(dir,true);}
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

        // R7: the week view names the year in its title, its short title and its bottom count, and a week across a
        // year end shows both years, so a December/January week cannot be mistaken for the other year's week.
        static void WeekYear(){
            string dir=Temp();var store=new StateStore(dir);
            try{
                using(var form=new Form{ClientSize=new Size(1280,800)}){
                    var view=new CalendarView(null,store,new UiPrefs(dir,true));view.Dock=DockStyle.Fill;form.Controls.Add(view);form.Show();
                    view.SetWeekView(true);
                    view.selected="2026-12-30";
                    Check(view.WeekLabel()=="2026년 12월 27일 – 2027년 1월 2일","the week title names both years when the week crosses a year end");
                    Check(view.WeekLabelShort()=="2026 12/27–2027 1/2","the short week title keeps both years when the week crosses a year end");
                    Check(view.WeekCount().StartsWith("2026 12/27 – 2027 1/2 · ",StringComparison.Ordinal),"the bottom count names both years when the week crosses a year end");
                    view.selected="2026-11-16";
                    Check(view.WeekLabel()=="2026년 11월 15일 – 21일","the week title names the year inside one month");
                    Check(view.WeekLabelShort()=="2026 11/15–21","the short week title names the year inside one month");
                    Check(view.WeekCount().StartsWith("2026 11/15 – 11/21 · ",StringComparison.Ordinal),"the bottom count names the year inside one month");
                }
            }finally{Directory.Delete(dir,true);}
            Console.WriteLine("PASS R7 week view titles and counts show the year");
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

        // R8: while the task due-date field has focus, Ctrl+C/V go to that field (not to schedule copy/paste),
        // Ctrl+Z/Y still reach undo and redo, the title box keeps all four, and the calendar keeps its shortcuts.
        static void TaskShortcuts(){
            string dir=Temp();var store=new StateStore(dir);var flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
            try{
                using(var form=new Form{ClientSize=new Size(1280,800)}){
                    var view=new CalendarView(null,store,new UiPrefs(dir,true));view.Dock=DockStyle.Fill;form.Controls.Add(view);form.Show();form.Activate();
                    var processKey=typeof(CalendarView).GetMethod("ProcessCmdKey",flags);
                    Func<Keys,bool> shortcut=k=>{var msg=Message.Create(view.Handle,0x0100,(IntPtr)(int)(k&Keys.KeyCode),IntPtr.Zero);return (bool)processKey.Invoke(view,new object[]{msg,k});};
                    var due=(Control)typeof(CalendarView).GetField("taskDue",flags).GetValue(view);
                    var title=(Control)typeof(CalendarView).GetField("taskTitle",flags).GetValue(view);
                    var keys=new[]{Keys.Control|Keys.C,Keys.Control|Keys.V,Keys.Control|Keys.Z,Keys.Control|Keys.Y};
                    var undoKeys=new[]{Keys.Control|Keys.Z,Keys.Control|Keys.Y,Keys.Control|Keys.Shift|Keys.Z};
                    due.Focus();Check(due.ContainsFocus,"the task due-date field can take focus in the test window");
                    Check(!shortcut(Keys.Control|Keys.C)&&!shortcut(Keys.Control|Keys.V),"Ctrl+C/V in the task due-date field stay with the field instead of running schedule copy and paste");
                    Check(undoKeys.All(shortcut),"Ctrl+Z/Y/Shift+Z in the task due-date field still run undo and redo as they do elsewhere");
                    title.Focus();Check(title.ContainsFocus&&keys.All(k=>!shortcut(k)),"Ctrl+C/V/Z/Y in the task title box stay with the box");
                    view.Focus();Check(!due.ContainsFocus&&!title.ContainsFocus&&shortcut(Keys.Control|Keys.C)&&shortcut(Keys.Control|Keys.V),"Ctrl+C and Ctrl+V still run schedule copy and paste outside the task text fields");
                }
            }finally{Directory.Delete(dir,true);}
            Console.WriteLine("PASS R8 task due-date field keeps Ctrl+C/V while focused, Ctrl+Z/Y still undo and redo, and schedule shortcuts work elsewhere");
        }

        // Two "지금 백업" runs in the same minute keep both files, and the newest-10 rule counts the files that exist.
        static void Backups(){
            string dir=Temp();string folder=Path.Combine(dir,"backups");var store=new StateStore(dir);
            try{
                var view=new CalendarView(null,store,new UiPrefs(dir,true));view.prefs.Value.backupFolder=folder;
                int calls=0;view.ApiCall=(m,p,b,v,f)=>Task.FromResult(new ApiResponse(200,new Dictionary<string,object>{{"version",1},{"run",++calls}}));
                string prefix=CalendarView.AutoBackupPrefix;Func<string,int,bool> holdsRun=(path,run)=>System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(path),"\"run\"\\s*:\\s*"+run+"\\b");
                string first=view.RunAutoBackup(false).GetAwaiter().GetResult();
                string second=view.RunAutoBackup(false).GetAwaiter().GetResult();
                var made=Directory.GetFiles(folder,prefix+"*.json");
                Check(first!=null&&second!=null&&first!=second&&made.Length==2,"two backups in the same minute are two files");
                Check(holdsRun(first,1)&&holdsRun(second,2),"each file keeps the snapshot taken when it was made");
                // Ten older backups already present: the next run keeps the newest ten and drops the oldest.
                for(int i=0;i<10;i++)File.WriteAllText(Path.Combine(folder,prefix+"2026-10-"+(10+i).ToString("D2")+"-0900.json"),"{}");
                string third=view.RunAutoBackup(false).GetAwaiter().GetResult();
                var kept=Directory.GetFiles(folder,prefix+"*.json");
                Check(kept.Length==10&&kept.Contains(first)&&kept.Contains(second)&&kept.Contains(third)&&!kept.Contains(Path.Combine(folder,prefix+"2026-10-10-0900.json")),"the newest ten unique backups are kept and the oldest is dropped");
            }finally{try{Directory.Delete(dir,true);}catch(Exception){}}
            Console.WriteLine("PASS same-minute backups keep every file and the newest-10 rule holds");
        }

        // Touchpads send deltas under one notch: small turns add up like one full notch, and turning back cancels them.
        static void WheelSteps(){
            var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            var agendaWheel=typeof(WidgetAgenda).GetMethod("Wheel",flags);
            Func<int,WidgetAgenda> agenda=delta=>{var a=new WidgetAgenda();a.Scrollbar.SetMetrics(1000,200);agendaWheel.Invoke(a,new object[]{a,new MouseEventArgs(MouseButtons.None,0,0,0,delta)});return a;};
            var one=agenda(-120);var halves=agenda(-60);agendaWheel.Invoke(halves,new object[]{halves,new MouseEventArgs(MouseButtons.None,0,0,0,-60)});
            Check(one.Scrollbar.GlideTarget>0&&halves.Scrollbar.GlideTarget==one.Scrollbar.GlideTarget,"two -60 wheel turns in the agenda scroll as far as one -120 turn");
            var back=agenda(-120);double down=back.Scrollbar.GlideTarget;
            agendaWheel.Invoke(back,new object[]{back,new MouseEventArgs(MouseButtons.None,0,0,0,60)});
            Check(back.Scrollbar.GlideTarget==down,"one +60 turn does not yet undo a -120 turn");
            agendaWheel.Invoke(back,new object[]{back,new MouseEventArgs(MouseButtons.None,0,0,0,60)});
            Check(back.Scrollbar.GlideTarget==0,"two +60 turns cancel the earlier -120 turn in the agenda");
            var bar=new ThemeScrollBar();bar.SetMetrics(1000,200);
            var barWheel=typeof(ThemeScrollBar).GetMethod("OnMouseWheel",flags);
            barWheel.Invoke(bar,new object[]{new MouseEventArgs(MouseButtons.None,0,0,0,-60)});barWheel.Invoke(bar,new object[]{new MouseEventArgs(MouseButtons.None,0,0,0,-60)});
            var barOne=new ThemeScrollBar();barOne.SetMetrics(1000,200);barWheel.Invoke(barOne,new object[]{new MouseEventArgs(MouseButtons.None,0,0,0,-120)});
            Check(bar.GlideTarget>0&&bar.GlideTarget==barOne.GlideTarget,"two -60 wheel turns on the scrollbar scroll as far as one -120 turn");
            Console.WriteLine("PASS small wheel turns add up in the agenda and the scrollbar");
        }

        static void PopupWheels(){
            // Time and select popups: 120-unit turns and small touchpad deltas scroll the list, never the chosen value.
            var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            Func<TimePicker> time=()=>new TimePicker(new TimeField()){Size=new Size(196,236)};
            Func<TimePicker,int[]> offsets=p=>(int[])typeof(TimePicker).GetField("offsets",flags).GetValue(p);
            Func<TimePicker,int,Point> hourAt=(p,row)=>{float s=p.DeviceDpi/96f;return new Point(p.Width/2,(int)(8*s)+(int)Math.Round(28*s)*row+1);};
            var timeWheel=typeof(TimePicker).GetMethod("WheelAt",flags);
            var hourHalves=time();Action<TimePicker,int> wheel=(p,delta)=>timeWheel.Invoke(p,new object[]{hourAt(p,3),delta});
            wheel(hourHalves,-60);
            Check(offsets(hourHalves)[1]==0,"one -60 turn in the time picker waits for a full notch");
            wheel(hourHalves,-60);
            var hourOne=time();wheel(hourOne,-120);
            Check(offsets(hourOne)[1]>0&&offsets(hourHalves)[1]==offsets(hourOne)[1],"two -60 turns in the time picker scroll as far as one -120 turn");
            string picked=null;var done=time();done.Done=v=>picked=v;
            wheel(done,-60);wheel(done,-60);
            Check(offsets(done)[1]>0,"two -60 wheel turns scroll the hour column of the time picker");
            var keyDown=typeof(TimePicker).GetMethod("OnKeyDown",flags);
            keyDown.Invoke(done,new object[]{new KeyEventArgs(Keys.Enter)});
            Check(picked=="09:00","small wheel turns in the time picker leave the chosen time unchanged");
            var top=time();wheel(top,120);
            Check(offsets(top)[1]==0,"wheel turns above the first hour stay at the top of the time picker");
            var bottomFar=time();var bottomNear=time();
            for(int i=0;i<100;i++)wheel(bottomFar,-120);for(int i=0;i<40;i++)wheel(bottomNear,-120);
            Check(offsets(bottomFar)[1]>0&&offsets(bottomFar)[1]==offsets(bottomNear)[1],"wheel turns past the last hour stop at the end of the time picker");

            var select=new SelectField();for(int i=0;i<12;i++)select.Add("k"+i,"항목 "+i);
            var listType=typeof(SelectField).GetNestedType("OptionList",BindingFlags.NonPublic);
            string pickedOption=null;
            Func<string> keyOf=()=>select.Value;
            Func<object> list=()=>{var o=Activator.CreateInstance(listType,flags,null,new object[]{select},null);((Control)o).Size=new Size(200,120);listType.GetField("Picked",flags).SetValue(o,(Action<string>)(k=>pickedOption=k));return o;};
            var listWheel=listType.GetMethod("OnMouseWheel",flags);
            Func<object,int> listOffset=o=>(int)listType.GetField("offset",flags).GetValue(o);
            var halves=list();listWheel.Invoke(halves,new object[]{new MouseEventArgs(MouseButtons.None,0,0,0,-60)});
            Check(listOffset(halves)==0,"one -60 turn in the select popup waits for a full notch");
            listWheel.Invoke(halves,new object[]{new MouseEventArgs(MouseButtons.None,0,0,0,-60)});
            var whole=list();listWheel.Invoke(whole,new object[]{new MouseEventArgs(MouseButtons.None,0,0,0,-120)});
            Check(listOffset(whole)>0&&listOffset(halves)==listOffset(whole),"two -60 turns in the select popup scroll as far as one -120 turn");
            var up=list();listWheel.Invoke(up,new object[]{new MouseEventArgs(MouseButtons.None,0,0,0,120)});
            Check(listOffset(up)==0,"wheel turns above the first option stay at the top of the select popup");
            var end=list();for(int i=0;i<100;i++)listWheel.Invoke(end,new object[]{new MouseEventArgs(MouseButtons.None,0,0,0,-60)});
            var endFew=list();for(int i=0;i<50;i++)listWheel.Invoke(endFew,new object[]{new MouseEventArgs(MouseButtons.None,0,0,0,-60)});
            Check(listOffset(end)==listOffset(endFew)&&listOffset(end)>0,"wheel turns past the last option stop at the end of the select popup");
            Check(keyOf()=="k0"&&pickedOption==null,"wheel turns in the select popup neither change nor pick the selected option");
            Console.WriteLine("PASS small wheel turns scroll the time and select popups without changing the selection");
        }

        static void Ics(){
            // R5: weekly COUNT=4000 needs 76 years; the last date is exact, not cut at a horizon.
            var r=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART;VALUE=DATE:20260101\r\nRRULE:FREQ=WEEKLY;COUNT=4000\r\nSUMMARY:긴 반복\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
            var e=r.Events.Single();
            Check(e.repeat=="weekly"&&e.repeatUntil==Rules.AddDays("2026-01-01",7*3999)&&r.Simplified==0,"weekly COUNT=4000 keeps all 4000 dates (until "+e.repeatUntil+")");
            Check(Rules.Occurrences(new[]{e},e.startDate,e.repeatUntil,false).Count==4000,"the imported series has exactly 4000 dates");
            r=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART;VALUE=DATE:20260101\r\nRRULE:FREQ=DAILY;COUNT=500000\r\nSUMMARY:너무 긴 반복\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
            Check(r.Events.Single().repeat=="none"&&r.Simplified==1,"a COUNT the app cannot hold is reported, not silently cut");
            // A COUNT that is zero, negative or not a number must not become an endless weekly series.
            foreach(var bad in new[]{"0","-3","abc",""}){
                r=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART;VALUE=DATE:20260101\r\nRRULE:FREQ=WEEKLY;COUNT="+bad+"\r\nSUMMARY:잘못된 COUNT\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
                e=r.Events.Single();
                Check(e.repeat=="none"&&e.repeatUntil==""&&r.Simplified==1,"COUNT='"+bad+"' is reported as simplified, not an endless repeat");
            }
            // BYMONTHDAY=31 from Jan 31 skips February and April; the app's monthly would clamp to Feb 28, so the rule is kept as its first date.
            r=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART;VALUE=DATE:20260131\r\nRRULE:FREQ=MONTHLY;BYMONTHDAY=31;COUNT=3\r\nSUMMARY:말일 반복\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
            e=r.Events.Single();
            Check(e.repeat=="none"&&e.startDate=="2026-01-31"&&r.Simplified==1,"BYMONTHDAY=31 is not widened into a clamped monthly series");
            var first=Rules.Occurrences(new[]{e},"2026-01-01","2026-12-31",false).Select(o=>o.startDate).ToList();
            Check(first.Count==1&&first[0]=="2026-01-31","BYMONTHDAY=31 keeps only the first date, not Feb 28 or Mar 31");
            r=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART;VALUE=DATE:20260131\r\nRRULE:FREQ=MONTHLY;COUNT=3\r\nSUMMARY:말일 반복2\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
            Check(r.Events.Single().repeat=="none"&&r.Simplified==1,"a 31st monthly rule without BYMONTHDAY is not clamped either");
            // Days 1-28 are unchanged: COUNT=3 from Jan 15 ends on Mar 15.
            r=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART;VALUE=DATE:20260115\r\nRRULE:FREQ=MONTHLY;BYMONTHDAY=15;COUNT=3\r\nSUMMARY:15일 반복\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
            e=r.Events.Single();
            Check(e.repeat=="monthly"&&e.repeatUntil=="2026-03-15"&&r.Simplified==0,"monthly BYMONTHDAY=15 COUNT=3 still ends on 2026-03-15 (until "+e.repeatUntil+")");
            // A timed UNTIL is an instant: 18:00 daily ending at 17:00 on Jan 5 must not keep a Jan 5 occurrence.
            Func<string,string> untilOf=rule=>{
                r=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART:20260101T180000\r\nRRULE:FREQ=DAILY;"+rule+"\r\nSUMMARY:시각 UNTIL\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
                return r.Events.Single().repeatUntil;};
            Check(untilOf("UNTIL=20260105T170000")=="2026-01-04","UNTIL 17:00 on Jan 5 excludes the 18:00 occurrence on Jan 5 (until "+untilOf("UNTIL=20260105T170000")+")");
            Check(untilOf("UNTIL=20260105T180000")=="2026-01-05","UNTIL equal to the start time keeps the Jan 5 occurrence");
            Check(untilOf("UNTIL=20260105T190000")=="2026-01-05","UNTIL after the start time keeps the Jan 5 occurrence");
            Check(untilOf("UNTIL=20260105")=="2026-01-05","a date-only UNTIL on a timed series keeps its date");
            Check(untilOf("UNTIL=20260101T170000")=="2026-01-01","UNTIL on the start date is unchanged by the time rule");
            var untilUtc=new DateTime(2026,1,5,17,0,0).ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'",System.Globalization.CultureInfo.InvariantCulture);
            Check(untilOf("UNTIL="+untilUtc)=="2026-01-04","a UTC UNTIL is compared in local time (UNTIL="+untilUtc+")");
            // An all-day series keeps its date-only UNTIL behaviour even when UNTIL carries a time.
            r=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART;VALUE=DATE:20260101\r\nRRULE:FREQ=DAILY;UNTIL=20260105T170000\r\nSUMMARY:종일 UNTIL\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
            Check(r.Events.Single().repeatUntil=="2026-01-05","an all-day series keeps the UNTIL date");
            // A DURATION too large for the app's dates skips only its own VEVENT; the normal event in the same file is still imported and counted.
            foreach(var bad in new[]{"P2147483648D","P99999999999999999999999999D","PT2562047788H","P1000000000W"}){
                r=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART:20260101T100000\r\nDURATION:"+bad+"\r\nSUMMARY:범위 밖\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
                Check(r.Events.Count==0&&r.Skipped==1,"DURATION="+bad+" skips its VEVENT and counts it as skipped, without an exception");
            }
            r=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART:20260101T100000\r\nDURATION:P2147483648D\r\nSUMMARY:범위 밖\r\nEND:VEVENT\r\nBEGIN:VEVENT\r\nDTSTART:20260102T100000\r\nDURATION:PT1H\r\nSUMMARY:정상\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
            Check(r.Events.Count==1&&r.Events[0].title=="정상"&&r.Skipped==1,"a bad DURATION does not stop the normal VEVENT after it from being imported");
            // A normal DURATION still gives the end: PT1H from 10:00 ends at 11:00, and P1W on an all-day start covers 7 days.
            r=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART:20260101T100000\r\nDURATION:PT1H\r\nSUMMARY:한 시간\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
            e=r.Events.Single();
            Check(e.endDate=="2026-01-01"&&e.endTime=="11:00"&&r.Skipped==0,"DURATION PT1H from 10:00 ends at 11:00 (end "+e.endDate+" "+e.endTime+")");
            r=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART;VALUE=DATE:20260101\r\nDURATION:P1W\r\nSUMMARY:일주일\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
            e=r.Events.Single();
            Check(e.startDate=="2026-01-01"&&e.endDate=="2026-01-07"&&r.Skipped==0,"DURATION P1W on an all-day start ends on 2026-01-07 (end "+e.endDate+")");
            // A file whose only VEVENT is skipped imports nothing, but the notice says a VEVENT was skipped, not that none was found.
            r=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART:20260101T100000\r\nDURATION:P2147483648D\r\nSUMMARY:범위 밖\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
            Check(r.Events.Count==0&&r.Skipped==1&&r.FoundVEvent,"a file with only a skipped VEVENT still reports that a VEVENT was found");
            string noticeTitle,noticeDetail;
            IcsImport.NothingToImport(r,0,out noticeTitle,out noticeDetail);
            Check(noticeTitle=="가져올 일정이 없어요"&&noticeDetail.Contains("가져오지 못한 일정 1개는 건너뛰었어요")&&!noticeDetail.Contains("찾지 못했어요"),"a skipped-only file notice counts the skipped VEVENT (detail: "+noticeDetail+")");
            r=IcsImport.Parse("BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n","work",null);
            IcsImport.NothingToImport(r,0,out noticeTitle,out noticeDetail);
            Check(!r.FoundVEvent&&noticeDetail=="파일에서 일정(VEVENT)을 찾지 못했어요.","a file without any VEVENT still says none was found");
            r=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART:20260101T100000\r\nSTATUS:CANCELLED\r\nSUMMARY:취소\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
            IcsImport.NothingToImport(r,0,out noticeTitle,out noticeDetail);
            Check(r.Cancelled==1&&noticeDetail.Contains("취소된 일정 1개")&&!noticeDetail.Contains("찾지 못했어요"),"a cancelled-only file notice counts the cancelled VEVENT");
            IcsImport.NothingToImport(r,2,out noticeTitle,out noticeDetail);
            Check(noticeDetail.Contains("같은 일정 2개를 건너뛰었어요."),"duplicates are still counted in the notice");
        }

        static void IcsDuplicates(){
            // Same title and start time, but another end, or a repeat: three different schedules, all imported.
            const string text="BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART:20260301T100000\r\nDTEND:20260301T110000\r\nSUMMARY:병원\r\nEND:VEVENT\r\nBEGIN:VEVENT\r\nDTSTART:20260301T100000\r\nDTEND:20260301T120000\r\nSUMMARY:병원\r\nEND:VEVENT\r\nBEGIN:VEVENT\r\nDTSTART:20260301T100000\r\nDTEND:20260301T110000\r\nRRULE:FREQ=WEEKLY\r\nSUMMARY:병원\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
            var parsed=IcsImport.Parse(text,"work",null);
            int duplicates;var fresh=IcsImport.FreshEvents(new CalendarEvent[0],parsed.Events,out duplicates);
            Check(parsed.Events.Count==3&&fresh.Count==3&&duplicates==0,"same title and start with another end or repeat are all imported");
            // The stored copies go through the same JSON as a save; importing the same text again then adds nothing.
            var stored=fresh.Select(x=>Codec.Read<CalendarEvent>(Codec.Json(x))).ToArray();
            var again=IcsImport.Parse(text,"work",null);
            var none=IcsImport.FreshEvents(stored,again.Events,out duplicates);
            Check(none.Count==0&&duplicates==3,"importing the same iCalendar file again adds nothing");
            // A changed skipped date makes a different series, so it is imported next to the stored one.
            var changed=IcsImport.Parse(text.Replace("RRULE:FREQ=WEEKLY","RRULE:FREQ=WEEKLY\r\nEXDATE:20260308T100000"),"work",null);
            var added=IcsImport.FreshEvents(stored,changed.Events,out duplicates);
            Check(added.Count==1&&duplicates==2&&added[0].repeat=="weekly","a repeat with other skipped dates is imported, the rest are still recognized");
        }

        static void IcsRepeatDates(){
            // RDATE adds dates to a repeat: each extra date is a schedule with the series' title, memo, time, span and reminder.
            const string weekly="BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART:20260301T100000\r\nDTEND:20260301T110000\r\nRRULE:FREQ=WEEKLY;COUNT=3\r\nRDATE:20260322T100000\r\nSUMMARY:치과\r\nDESCRIPTION:세척 예약\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
            var r=IcsImport.Parse(weekly,"work",10);
            var series=r.Events.Where(x=>x.repeat=="weekly").ToList();var extras=r.Events.Where(x=>x.repeat=="none").ToList();
            Check(series.Count==1&&series[0].repeatUntil==Rules.AddDays("2026-03-01",14)&&extras.Count==1,"an RRULE with an RDATE keeps the repeat and adds the extra date as its own schedule");
            var x0=extras[0];
            Check(x0.startDate=="2026-03-22"&&x0.endDate=="2026-03-22"&&x0.startTime=="10:00"&&x0.endTime=="11:00","the RDATE occurrence keeps the series' time (start "+x0.startDate+" "+x0.startTime+")");
            Check(x0.title=="치과"&&x0.note=="세척 예약"&&x0.reminder==10&&x0.id!=series[0].id&&x0.excludedDates.Length==0,"the RDATE occurrence keeps the title, memo and reminder under a new id");
            // A multi-day all-day series keeps its span on an RDATE date.
            r=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART;VALUE=DATE:20260301\r\nDTEND;VALUE=DATE:20260303\r\nRDATE;VALUE=DATE:20260320\r\nSUMMARY:출장\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
            Check(r.Events.Count==2&&r.Events[1].startDate=="2026-03-20"&&r.Events[1].endDate=="2026-03-21"&&r.Events[1].startTime==""&&r.Events[1].repeat=="none","an all-day RDATE without RRULE keeps the two-day span and adds the date");
            // A date-time PERIOD counts by its start; a date-time RDATE on another minute is another occurrence.
            r=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART:20260301T100000\r\nDTEND:20260301T110000\r\nRRULE:FREQ=WEEKLY;COUNT=3\r\nRDATE;VALUE=PERIOD:20260322T140000/PT2H\r\nRDATE:20260308T150000\r\nSUMMARY:병원\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
            var starts=r.Events.Where(x=>x.repeat=="none").Select(x=>x.startDate+" "+x.startTime).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            Check(starts.SequenceEqual(new[]{"2026-03-08 15:00","2026-03-22 14:00"}),"a PERIOD RDATE counts by its start and a same-day RDATE at another time is another occurrence ("+string.Join(", ",starts)+")");
            // A PERIOD keeps its own end, from a duration or an explicit end time, and does not take the series' 10:00-11:00.
            r=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART:20260301T100000\r\nDTEND:20260301T110000\r\nRRULE:FREQ=WEEKLY;COUNT=3\r\nRDATE;VALUE=PERIOD:20260322T140000/PT2H\r\nSUMMARY:병원\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
            var byDuration=r.Events.Where(x=>x.repeat=="none").ToList();
            Check(byDuration.Count==1&&byDuration[0].startDate=="2026-03-22"&&byDuration[0].startTime=="14:00"&&byDuration[0].endDate=="2026-03-22"&&byDuration[0].endTime=="16:00","a PERIOD RDATE with a duration keeps its own end (14:00-16:00)");
            Check(r.Events[0].startTime=="10:00"&&r.Events[0].endTime=="11:00","the series keeps its own 10:00-11:00 when an RDATE has a PERIOD");
            r=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART:20260301T100000\r\nDTEND:20260301T110000\r\nRRULE:FREQ=WEEKLY;COUNT=3\r\nRDATE;VALUE=PERIOD:20260322T140000/20260322T153000,20260315T220000/PT3H\r\nSUMMARY:병원\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
            var explicitEnd=r.Events.Where(x=>x.repeat=="none").OrderBy(x=>x.startDate,StringComparer.Ordinal).ToList();
            Check(explicitEnd.Count==2&&explicitEnd[1].endDate=="2026-03-22"&&explicitEnd[1].endTime=="15:30","a PERIOD RDATE with an explicit end time keeps that end (14:00-15:30)");
            Check(explicitEnd[0].startTime=="22:00"&&explicitEnd[0].endDate=="2026-03-16"&&explicitEnd[0].endTime=="01:00","a PERIOD RDATE running past midnight ends on the next day (22:00-01:00)");
            // The series' own date (DTSTART), an RRULE date, a repeated RDATE and an EXDATE-skipped RDATE are not added twice or at all.
            r=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART:20260301T100000\r\nDTEND:20260301T110000\r\nRRULE:FREQ=WEEKLY;COUNT=3\r\nEXDATE:20260315T100000\r\nRDATE:20260301T100000,20260308T100000,20260315T100000,20260322T100000,20260322T100000\r\nSUMMARY:병원\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
            var extra=r.Events.Where(x=>x.repeat=="none").ToList();
            Check(extra.Count==1&&extra[0].startDate=="2026-03-22","an RDATE that repeats the start, an RRULE date or an EXDATE is not added again (extras: "+extra.Count+")");
            Check(r.Events.Count==2&&r.Events[0].excludedDates.SequenceEqual(new[]{"2026-03-15"}),"the series keeps its EXDATE and the RDATE extra is the only added occurrence");
            // DTSTART with seconds is saved to the minute, so an RDATE on an RRULE date at the same minute is not a second occurrence.
            r=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART:20260301T100030\r\nDTEND:20260301T110000\r\nRRULE:FREQ=WEEKLY;COUNT=3\r\nRDATE:20260308T100000,20260322T100045\r\nSUMMARY:병원\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
            var secondsExtra=r.Events.Where(x=>x.repeat=="none").Select(x=>x.startDate+" "+x.startTime).ToArray();
            Check(secondsExtra.SequenceEqual(new[]{"2026-03-22 10:00"}),"an RDATE on an RRULE date at the same minute as a DTSTART with seconds is not added twice (extras: "+string.Join(", ",secondsExtra)+")");
            // Without RRULE, DTSTART plus RDATE is one schedule per date, and importing the file again adds nothing.
            const string plain="BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART:20260301T100000\r\nDTEND:20260301T110000\r\nRDATE:20260305T100000,20260310T100000\r\nSUMMARY:회의\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
            r=IcsImport.Parse(plain,"work",null);
            Check(r.Events.Count==3&&r.Events.All(x=>x.repeat=="none"),"DTSTART plus two RDATE dates imports three one-off schedules");
            var stored=r.Events.Select(x=>Codec.Read<CalendarEvent>(Codec.Json(x))).ToArray();
            int duplicates;var fresh=IcsImport.FreshEvents(stored,IcsImport.Parse(plain,"work",null).Events,out duplicates);
            Check(fresh.Count==0&&duplicates==3,"importing a file with RDATE dates again adds nothing");
        }

        static void LeapDayIcs(){
            // A yearly series from Feb 29 exports so that common years keep Feb 28 and leap years keep Feb 29, once each,
            // and it comes back as a yearly series with the same dates. Timed starts are checked too: 00:30 falls on another UTC day in some zones.
            foreach(var e in new[]{Event("leap-all","2024-02-29","yearly"),Event("leap-timed","2024-02-29","yearly",time:"00:30")}){
                var text=IcsExport.Write(new[]{e},c=>"");
                Check(text.Contains("RRULE:FREQ=YEARLY;BYMONTH=")&&!text.Contains("RRULE:FREQ=YEARLY\r\n"),"a Feb 29 yearly series exports a rule that keeps common years, not a plain FREQ=YEARLY ("+e.id+")");
                if(e.startTime=="")Check(text.Contains("RRULE:FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=-1\r\n"),"an all-day Feb 29 yearly series exports to the last day of February");
                var back=IcsImport.Parse(text,"work",null);
                var imported=back.Events.Single();
                var expected=Rules.Occurrences(new[]{e},"2024-01-01","2030-12-31",false).Select(o=>o.startDate).ToList();
                var actual=Rules.Occurrences(new[]{imported},"2024-01-01","2030-12-31",false).Select(o=>o.startDate).ToList();
                Check(expected.Contains("2025-02-28")&&expected.Contains("2028-02-29")&&expected.Count==7,"the app's Feb 29 yearly series has Feb 28 in common years and Feb 29 in leap years");
                Check(imported.repeat=="yearly"&&back.Simplified==0,"an exported Feb 29 yearly series is imported as a repeat, not simplified ("+e.id+")");
                Check(actual.SequenceEqual(expected),"the exported Feb 29 yearly series keeps the app's dates after re-import ("+e.id+")");
                Check(imported.startDate==e.startDate&&imported.startTime==e.startTime,"the exported Feb 29 yearly series keeps its start date and time ("+e.id+")");
            }
            // A shifted leap-day rule only matches the start whose UTC shift it was written for: other rules are simplified, not widened.
            foreach(var rule in new[]{"FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=-2","FREQ=YEARLY;BYMONTH=3;BYMONTHDAY=1"}){
                var r=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART;VALUE=DATE:20240229\r\nRRULE:"+rule+"\r\nSUMMARY:외부 윤년 규칙\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
                Check(r.Events.Single().repeat=="none"&&r.Simplified==1,"an all-day Feb 29 start with "+rule+" is simplified, not a yearly repeat");
            }
            var local=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART:20240229T120000\r\nRRULE:FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=-2\r\nSUMMARY:외부 윤년 시각 규칙\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
            Check(local.Events.Single().repeat=="none"&&local.Simplified==1,"a noon Feb 29 start with BYMONTHDAY=-2 (another UTC shift) is simplified");
            local=IcsImport.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART:20240229T120000\r\nRRULE:FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=-1\r\nSUMMARY:외부 윤년 시각 규칙2\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n","work",null);
            Check(local.Events.Single().repeat=="yearly"&&local.Simplified==0,"a noon Feb 29 start with BYMONTHDAY=-1 keeps its yearly repeat");
        }

        // A reminder that came due while the app was closed is shown once after a restart; one older than 24 hours is not.
        static void AlarmCatchUp(){
            string dir=Temp();var store=new StateStore(dir);var now=DateTime.Now;
            var state=CalendarView.Clone(store.Snapshot);
            state.events=new[]{Event("recent",Rules.Key(now.AddMinutes(-10)),reminder:0,time:now.AddMinutes(-10).ToString("HH:mm")),Event("stale",Rules.Key(now.AddHours(-25)),reminder:0,time:now.AddHours(-25).ToString("HH:mm"))};
            store.Save(Codec.Json(state),h=>Task.FromResult(0)).GetAwaiter().GetResult();
            var delivered=new List<string>();Action<List<Alarm>,bool,bool> record=(list,fresh,silent)=>{if(fresh)delivered.AddRange(list.Select(a=>a.eventId));};
            using(var alarms=new AlarmService(store)){alarms.Delivered+=record;alarms.CheckAt(DateTime.UtcNow);}
            Check(delivered.SequenceEqual(new[]{"recent"}),"a reminder missed while the app was closed is shown once after restart, and one older than 24 hours is not ("+string.Join(",",delivered)+")");
            using(var alarms=new AlarmService(store)){alarms.Delivered+=record;alarms.CheckAt(DateTime.UtcNow);}
            Check(delivered.Count==1,"a reminder already recorded in the ledger is not shown again after another restart");
            try{Directory.Delete(dir,true);}catch(Exception){}
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
