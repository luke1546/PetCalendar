using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PetCalendarDesktop {
    sealed class DragInfo {public string EventId,Start,Day,TaskId,Title,Category;public System.Drawing.Point Origin;public bool Active;}
    // Per-user UI memory that lived in WebView localStorage: quick tour
    // completion and unsaved event-editor drafts.
    public sealed class UiPrefs {
        public sealed class Data {public bool quickTourDone;public Dictionary<string,string> drafts=new Dictionary<string,string>();}
        readonly string path;public Data Value;
        public UiPrefs(string dataDir,bool migrated){
            path=Path.Combine(dataDir,"ui-state.json");
            try{Value=File.Exists(path)?Codec.Read<Data>(File.ReadAllText(path)):null;}catch{Value=null;}
            if(Value==null){Value=new Data();
                // Users upgrading from the WebView build have already seen the tour.
                if(migrated)Value.quickTourDone=true;}
            if(Value.drafts==null)Value.drafts=new Dictionary<string,string>();
        }
        public void Save(){try{StateStore.Atomic(path,Codec.Json(Value),null);}catch{}}
    }
    public sealed partial class CalendarView {
        internal Occurrence deadlineTarget;DragInfo drag;string dropDate;
        CalendarState indexState;string indexToken;Dictionary<string,List<Occurrence>> indexCache;List<CalendarEvent> searchCache;CalendarState searchState;string searchToken;
        string pendingId,pendingKey;bool pending;
        static readonly IntlKoreanComparer KoreanText=IntlKoreanComparer.Instance;
        internal static string CategoryLight(string c){return c=="meeting"?"#eaf6fe":c=="deadline"?"#ffedf2":c=="personal"?"#f0f8e9":"#fff0e3";}
        internal static string FormatDay(string key){return Rules.Date(key).ToString("M월 d일 (ddd)",Rules.Korean);}
        internal static string FormatLong(string key){return Rules.Date(key).ToString("yyyy년 M월 d일 ddd",Rules.Korean);}
        internal static string RepeatLabel(string repeat){
            if(repeat.StartsWith("monthly:")){var p=repeat.Split(':');int week=int.Parse(p[1]),day=int.Parse(p[2]);string w=week==1?"첫째":week==2?"둘째":week==3?"셋째":week==4?"넷째":"마지막";return "매월 "+w+" "+"일월화수목금토"[day]+"요일";}
            return repeat=="daily"?"매일":repeat=="weekly"?"매주":repeat=="monthly"?"매월":"반복 안 함";
        }
        internal KeyValuePair<string,int> RangeForMonth(){var first=Rules.Date(month+"-01");int offset=(int)first.DayOfWeek;int count=DateTime.DaysInMonth(first.Year,first.Month);int size=(offset+count+6)/7*7;return new KeyValuePair<string,int>(Rules.Key(first.AddDays(-offset)),size);}
        bool Matches(CalendarEvent e){return (filter=="all"||e.category==filter)&&(search.Length==0||(e.title+" "+e.note).ToLower(Rules.Korean).Contains(search));}
        internal static int CompareDay(Occurrence a,Occurrence b){
            int c=Rules.Done(a.@event,a.startDate).CompareTo(Rules.Done(b.@event,b.startDate));if(c!=0)return c;
            c=(a.@event.startTime=="").CompareTo(b.@event.startTime=="");if(c!=0)return c;
            c=String.CompareOrdinal(a.@event.startTime,b.@event.startTime);if(c!=0)return Math.Sign(c);
            c=String.CompareOrdinal(a.@event.endTime==""?"99:99":a.@event.endTime,b.@event.endTime==""?"99:99":b.@event.endTime);if(c!=0)return Math.Sign(c);
            c=KoreanText.Compare(a.@event.title,b.@event.title);if(c!=0)return c;return String.CompareOrdinal(a.key,b.key);
        }
        // Expand each recurrence once; each visible date owns its sorted rows.
        internal Dictionary<string,List<Occurrence>> MonthIndex(){
            string token=month+"|"+filter+"|"+search;if(indexCache!=null&&indexState==state&&indexToken==token)return indexCache;
            var range=RangeForMonth();string from=range.Key,to=Rules.AddDays(from,range.Value-1);var days=new Dictionary<string,List<Occurrence>>();
            for(int i=0;i<range.Value;i++)days[Rules.AddDays(from,i)]=new List<Occurrence>();
            foreach(var o in Rules.Occurrences(state.events.Where(Matches),from,to,false)){
                var day=Rules.Date(String.CompareOrdinal(o.startDate,from)<0?from:o.startDate);var end=Rules.Date(o.endDate);var last=Rules.Date(to);
                for(;day<=end&&day<=last;day=day.AddDays(1))days[Rules.Key(day)].Add(o);
            }
            foreach(var list in days.Values)list.Sort(CompareDay);
            indexCache=days;indexState=state;indexToken=token;return days;
        }
        internal List<Occurrence> SelectedRows(){return RowsFor(selected);}
        internal List<Occurrence> RowsFor(string key){List<Occurrence> rows;if(MonthIndex().TryGetValue(key,out rows))return rows;rows=Rules.Occurrences(state.events.Where(Matches),key,key,false).Where(o=>String.CompareOrdinal(o.startDate,key)<=0&&String.CompareOrdinal(o.endDate,key)>=0).ToList();rows.Sort(CompareDay);return rows;}
        int TodayCount(){string today=Rules.Key(DateTime.Today);return Rules.Occurrences(state.events,today,today,false).Count(o=>String.CompareOrdinal(o.startDate,today)<=0&&String.CompareOrdinal(o.endDate,today)>=0);}
        List<CalendarEvent> SearchFound(){if(searchCache!=null&&searchState==state&&searchToken==filter+"|"+search)return searchCache;searchState=state;searchToken=filter+"|"+search;return searchCache=state.events.Where(Matches).OrderBy(e=>e.startDate,StringComparer.Ordinal).ToList();}
        string VisibleCount(){
            var parts=month.Split('-');int year=int.Parse(parts[0]),mon=int.Parse(parts[1]);
            int count=MonthIndex().Where(p=>p.Key.StartsWith(month,StringComparison.Ordinal)).SelectMany(p=>p.Value.Select(o=>o.@event.id)).Distinct().Count();
            return mon.ToString("00")+"월 · "+count+"개 일정"+(year<2020||year>2050?" · 공휴일 자료 없음":year>2027?" · 공휴일 예상":"");
        }
        void Summary(){var e=state.events.FirstOrDefault(x=>x.pinned);deadlineTarget=e==null?null:Rules.DdayOccurrence(e,Rules.Key(DateTime.Today));}
        internal bool IsDdayOccurrence(Occurrence o){return deadlineTarget!=null&&deadlineTarget.@event.id==o.@event.id&&deadlineTarget.startDate==o.startDate;}
        internal void Render(){using(PerfProbe.T("render")){if(!ReferenceEquals(state,stampedState)){stampedState=state;dataStamp++;}Summary();Relayout();Invalidate();foreach(var d in DialogForm.Open.ToArray())d.StateChanged();}}
        internal void JumpTo(string key){selected=key;month=key.Substring(0,7);limits["day"]=pageSizes["day"];dayScroll.Offset=0;Render();}
        void MonthMove(int amount){var next=Rules.Date(month+"-01").AddMonths(amount);month=Rules.Key(next).Substring(0,7);selected=month+"-01";limits["day"]=pageSizes["day"];dayScroll.Offset=0;Render();}
        void CellClick(string key,bool selectOnly){bool same=selected==key;JumpTo(key);if(same&&!selectOnly)OpenEvent(null,selected,false);}
        void ApplySearch(){string value=searchBox.Text.Trim().ToLower(Rules.Korean);if(value==search)return;search=value;limits["search"]=pageSizes["search"];limits["day"]=pageSizes["day"];searchScroll.Offset=0;Render();}
        void FocusTasks(){
            if(!m.Fit){pageScroll.Offset=Math.Max(0,taskPanel.Y+taskPanel.Height/2-Height/2);pageScroll.Clamp();PlaceChildren();}
            taskTitle.Focus();highlight=DateTime.UtcNow;var pulse=new Timer{Interval=33};pulse.Tick+=delegate{var r=Page(taskPanel);r.Inflate(P(6),P(6));Invalidate(r);if((DateTime.UtcNow-highlight).TotalSeconds>2.1){pulse.Stop();pulse.Dispose();Invalidate(r);}};pulse.Start();}
        DateTime highlight;
        // ---------- Saving ----------
        internal static CalendarState Clone(CalendarState value){return Codec.Read<CalendarState>(Codec.Json(value));}
        static string ErrorOf(object body){try{var o=Codec.Object(Codec.Json(body));object e;return o!=null&&o.TryGetValue("error",out e)?e as string:null;}catch{return null;}}
        internal void SetSaveStatus(string text,bool failed){saveStatus=text;saveFailed=failed;Invalidate(Page(footer));}
        void Banner(string message){bannerText=message;bannerVisible=true;Relayout();}
        // Saves are optimistic: the change is shown at once and written in the background.
        // Saves run one after another; each re-applies its change to the latest saved state,
        // so quick successive clicks are never dropped. A failure restores the saved state.
        Task saveChain=Task.FromResult(true);int pendingSaves;bool saveFailedInBatch;
        internal Task<bool> Commit(Action<CalendarState> change,bool applyHotkey=false){return Commit(change,applyHotkey,true);}
        async Task<bool> Commit(Action<CalendarState> change,bool applyHotkey,bool record){
            var before=state;var preview=Clone(state);
            try{change(preview);Rules.Validate(Codec.Json(preview));}catch(Exception error){Toast("내용을 확인해 주세요",error.Message,"error");return false;}
            pendingSaves++;saving=true;state=preview;Render();
            if(pendingSaves==1){saveFailedInBatch=false;foreach(var d in DialogForm.Open.ToArray())d.SavingChanged();}
            var previous=saveChain;var mine=new TaskCompletionSource<bool>();saveChain=mine.Task;
            await previous;
            bool ok=false;
            try{
                var next=Clone(store.Snapshot);change(next);
                var response=await CallApi("PUT","/api/state",Codec.Json(next),applyHotkey);
                if(response.status==409)throw new Exception("다른 창의 변경 내용을 불러왔습니다. 입력 중인 내용을 확인한 뒤 다시 저장해 주세요.");
                if(response.status!=200)throw new Exception(ErrorOf(response.body)??"저장하지 못했습니다.");
                ok=true;
                if(record)Record(before,preview);
            }catch(Exception error){
                saveFailedInBatch=true;Banner(error.Message);SetSaveStatus("저장하지 못함 · 입력 내용을 확인해 주세요",true);Toast("저장을 완료하지 못했어요",error.Message,"error",10000);
            }finally{
                if(--pendingSaves==0){
                    saving=false;var saved=store.Snapshot;
                    // The optimistic view already matches unless something failed.
                    if(saveFailedInBatch){state=saved;Render();}else{state=saved;bannerVisible=false;}
                    if(!saveFailedInBatch){DateTime at;DateTime.TryParse(state.updatedAt,CultureInfo.InvariantCulture,DateTimeStyles.AdjustToUniversal|DateTimeStyles.AssumeUniversal,out at);SetSaveStatus("이 PC에 저장됨 · "+at.ToLocalTime().ToString("HH:mm",CultureInfo.InvariantCulture),false);}
                    foreach(var d in DialogForm.Open.ToArray()){d.SavingChanged();d.StateChanged();}
                }
                mine.SetResult(ok);
            }
            return ok;
        }
        // ---------- Undo / redo ----------
        // Each saved change to events or tasks is remembered as per-item before/after copies.
        // Undo writes the "before" copies back onto the latest state (and redo the "after" ones),
        // so it merges with unrelated edits made since, e.g. from the widget or another window.
        internal sealed class HistoryEntry {public List<ItemChange> Events=new List<ItemChange>(),Tasks=new List<ItemChange>();public string Label="";}
        internal sealed class ItemChange {public string Id,Before,After;public int BeforeIndex,AfterIndex;}
        readonly List<HistoryEntry> undoStack=new List<HistoryEntry>(),redoStack=new List<HistoryEntry>();
        internal HistoryEntry LastRecorded{get{return undoStack.Count>0?undoStack[undoStack.Count-1]:null;}}
        static List<ItemChange> Diff<T>(T[] before,T[] after,Func<T,string> id){
            var result=new List<ItemChange>();var b=new Dictionary<string,int>();var a=new Dictionary<string,int>();
            for(int i=0;i<before.Length;i++)b[id(before[i])]=i;for(int i=0;i<after.Length;i++)a[id(after[i])]=i;
            foreach(var key in b.Keys.Concat(a.Keys.Where(k=>!b.ContainsKey(k)))){
                int bi,ai;bool hb=b.TryGetValue(key,out bi),ha=a.TryGetValue(key,out ai);
                string bj=hb?Codec.Json(before[bi]):null,aj=ha?Codec.Json(after[ai]):null;
                if(bj!=aj)result.Add(new ItemChange{Id=key,Before=bj,After=aj,BeforeIndex=hb?bi:-1,AfterIndex=ha?ai:-1});
            }
            return result;
        }
        void Record(CalendarState before,CalendarState after){
            if(before==null||after==null)return;
            var entry=new HistoryEntry{Events=Diff(before.events,after.events,e=>e.id),Tasks=Diff(before.tasks,after.tasks,t=>t.id)};
            if(entry.Events.Count+entry.Tasks.Count==0)return;
            var first=entry.Events.Concat(entry.Tasks).First();string json=first.After??first.Before;
            try{var o=Codec.Object(json);object title;if(o!=null&&o.TryGetValue("title",out title))entry.Label=title as string??"";}catch{}
            int count=entry.Events.Count+entry.Tasks.Count;if(count>1&&entry.Label.Length>0)entry.Label+=" 외 "+(count-1)+"개";
            undoStack.Add(entry);if(undoStack.Count>100)undoStack.RemoveAt(0);redoStack.Clear();
        }
        static T[] Patch<T>(T[] current,List<ItemChange> changes,bool undo,Func<T,string> id){
            var list=current.ToList();
            foreach(var c in changes){
                string json=undo?c.Before:c.After;int at=undo?c.BeforeIndex:c.AfterIndex;int index=list.FindIndex(x=>id(x)==c.Id);
                if(json==null){if(index>=0)list.RemoveAt(index);continue;}
                var item=Codec.Read<T>(json);if(index>=0)list[index]=item;else list.Insert(Math.Max(0,Math.Min(list.Count,at)),item);
            }
            return list.ToArray();
        }
        static void Apply(CalendarState s,HistoryEntry entry,bool undo){
            s.events=Patch(s.events,entry.Events,undo,e=>e.id);s.tasks=Patch(s.tasks,entry.Tasks,undo,t=>t.id);
            // Only one schedule can be the D-day; keep the restored one.
            var restored=new HashSet<string>(entry.Events.Select(c=>c.Id));
            if(s.events.Count(e=>e.pinned)>1){foreach(var e in s.events)if(e.pinned&&!restored.Contains(e.id)){e.pinned=false;e.pinnedDate="";}
                bool kept=false;foreach(var e in s.events)if(e.pinned){if(kept){e.pinned=false;e.pinnedDate="";}kept=true;}}
        }
        // Returns false when there was nothing to undo or the save failed.
        internal async Task<bool> Undo(HistoryEntry only=null){
            var entry=LastRecorded;if(entry==null||(only!=null&&entry!=only))return false;undoStack.RemoveAt(undoStack.Count-1);
            if(await Commit(s=>Apply(s,entry,true),false,false)){redoStack.Add(entry);return true;}
            undoStack.Add(entry);return false;
        }
        internal async Task<bool> Redo(){
            if(redoStack.Count==0)return false;var entry=redoStack[redoStack.Count-1];redoStack.RemoveAt(redoStack.Count-1);
            if(await Commit(s=>Apply(s,entry,false),false,false)){undoStack.Add(entry);return true;}
            redoStack.Add(entry);return false;
        }
        async void UndoKey(){if(undoStack.Count==0){Toast("되돌릴 작업이 없어요");return;}var entry=LastRecorded;var label=entry.Label;if(await Undo()){Toasts.CloseFor(entry);Toast("실행을 취소했어요",label+"\nCtrl+Y로 다시 실행할 수 있어요.");}}
        async void RedoKey(){if(redoStack.Count==0){Toast("다시 실행할 작업이 없어요");return;}var label=redoStack[redoStack.Count-1].Label;if(await Redo())Toast("다시 실행했어요",label);}
        internal string LastError{get{return bannerText??"";}}
        void Reload(){if(saving)return;state=store.Snapshot;bannerVisible=false;Render();SetSaveStatus("최신 내용을 불러왔어요",false);Toast("최신 내용을 불러왔어요","입력 중인 내용은 확인 후 다시 저장해 주세요.");}
        internal void StoreChanged(){
            if(saving||IsDisposed)return;var next=store.Snapshot;if(next==state)return;bool changed=next.revision!=state.revision;state=next;Render();
            if(changed)SetSaveStatus("다른 창의 변경 내용을 반영했어요",false);
        }
        // ---------- Toasts and prompts ----------
        internal void Toast(string title,string detail="",string kind="info",int duration=6000){Toasts.Show(title,detail,kind,duration);}
        // The undo button rolls back through the shared history while this change is still the
        // latest one (so Ctrl+Z and the button agree); otherwise it falls back to a targeted restore.
        internal void UndoToast(string title,string detail,Func<Task<bool>> restore,string restored="삭제를 되돌렸어요"){
            var entry=LastRecorded;
            var toast=Toasts.Show(title,detail,"info",10000,"되돌리기",async()=>{if(entry!=null&&LastRecorded==entry)return await Undo(entry);return await restore();},restored);
            if(toast!=null)toast.Entry=entry;
        }
        internal Task<bool> Confirm(string title,string text,string label="확인"){
            if(DialogForm.Open.OfType<ConfirmDialog>().Any())return Task.FromResult(false);
            var source=new TaskCompletionSource<bool>();var dialog=new ConfirmDialog(this,title,text,label,source);dialog.Show(dialog.Accept);return source.Task;
        }
        internal Task<string> RepeatScope(string action,string date){var source=new TaskCompletionSource<string>();var dialog=new RepeatScopeDialog(this,FormatDay(date)+" 일정을 "+action+"합니다. 적용할 범위를 선택해 주세요.",source);dialog.Show(dialog.One);return source.Task;}
        // ---------- Schedules ----------
        async void ToggleEvent(string id,string key){
            var e=state.events.FirstOrDefault(x=>x.id==id);if(e==null)return;bool done=!Rules.Done(e,key);
            if(!await Commit(s=>{var item=s.events.FirstOrDefault(x=>x.id==id);if(item==null)return;item.completedDates=item.completedDates.Where(d=>d!=key).Concat(done?new[]{key}:new string[0]).ToArray();}))Render();
        }
        internal void ToggleOccurrence(string id,string key){ToggleEvent(id,key);}
        internal void OpenDayList(string key){if(modal)return;var dialog=new DayListDialog(this,key);dialog.Show(null);}
        internal void OpenEvent(string id,string key,bool duplicate){
            if(saving||modal&&!DialogForm.Open.OfType<EventDialog>().Any()&&false)return;
            if(DialogForm.Open.OfType<EventDialog>().Any()&&!duplicate)return;
            CalendarEvent original=id==null?null:state.events.FirstOrDefault(e=>e.id==id);if(id!=null&&original==null)return;
            foreach(var open in DialogForm.Open.OfType<EventDialog>().ToArray())open.CloseDialog();
            var dialog=new EventDialog(this,original,key,duplicate);dialog.Show(dialog.TitleBox.Box);
        }
        internal async void DeleteSchedule(string id,bool closeEditor,string occurrence,EventDialog editor=null){
            var e=state.events.FirstOrDefault(x=>x.id==id);if(e==null)return;
            if(e.repeat!="none"){
                string scope=await RepeatScope("삭제",occurrence);if(scope==null)return;
                if(scope=="one"){
                    if(await Commit(s=>{var series=s.events.First(x=>x.id==id);series.excludedDates=series.excludedDates.Concat(new[]{occurrence}).Distinct().ToArray();if(series.pinned&&series.pinnedDate==occurrence){series.pinned=false;series.pinnedDate="";}})){
                        if(closeEditor&&editor!=null)editor.Finish();
                        UndoToast("이번 일정을 삭제했어요",e.title,()=>Commit(s=>{var series=s.events.FirstOrDefault(x=>x.id==id);if(series==null)throw new AppError(400,"반복 일정을 찾을 수 없습니다.");series.excludedDates=series.excludedDates.Where(d=>d!=occurrence).ToArray();if(e.pinned&&e.pinnedDate==occurrence&&!s.events.Any(x=>x.pinned)){series.pinned=true;series.pinnedDate=occurrence;}}));
                    }
                    return;
                }
            }
            if(!await Confirm(e.repeat!="none"?"반복 일정을 삭제할까요?":"일정을 삭제할까요?","‘"+e.title+"’"+(e.repeat!="none"?"의 전체 반복 일정이 삭제됩니다.":" 일정을 삭제합니다."),"삭제"))return;
            if(await Commit(s=>{s.events=s.events.Where(x=>x.id!=e.id).ToArray();})){
                if(closeEditor&&editor!=null)editor.Finish();
                var copy=Codec.Read<CalendarEvent>(Codec.Json(e));
                UndoToast("일정을 삭제했어요",e.title,()=>Commit(s=>{if(s.events.Any(x=>x.id==copy.id))throw new AppError(400,"이미 복구된 일정입니다.");var restored=Codec.Read<CalendarEvent>(Codec.Json(copy));if(restored.pinned&&s.events.Any(x=>x.pinned)){restored.pinned=false;restored.pinnedDate="";}s.events=s.events.Concat(new[]{restored}).ToArray();}));
            }
        }
        internal async void ChooseDday(string id,string key,bool toggle){
            if(saving)return;var e=state.events.FirstOrDefault(x=>x.id==id);if(e==null||!Rules.OccursOn(e,key))return;
            bool clear=toggle&&deadlineTarget!=null&&deadlineTarget.@event.id==id&&deadlineTarget.startDate==key;
            if(await Commit(s=>{foreach(var x in s.events){x.pinned=!clear&&x.id==id;x.pinnedDate=x.pinned?key:"";}})){
                foreach(var d in DialogForm.Open.OfType<DdayDialog>().ToArray())d.CloseDialog();
                Toast(clear?"D-day 선택을 해제했어요":"D-day 일정을 선택했어요",e.title);
            }
        }
        internal void OpenDdayDialog(){if(saving)return;limits["dday"]=pageSizes["dday"];var dialog=new DdayDialog(this);dialog.Show(dialog.SearchBox.Box);}
        internal async void ClearDday(DdayDialog dialog){if(await Commit(s=>{foreach(var e in s.events){e.pinned=false;e.pinnedDate="";}}))dialog.CloseDialog();}
        internal void OpenDdayEvent(DdayDialog dialog){
            if(deadlineTarget==null||saving)return;var target=deadlineTarget;dialog.CloseDialog();
            filter="all";search="";searchBox.Text="";JumpTo(target.startDate);OpenEvent(target.@event.id,target.startDate,false);
        }
        async void DropEvent(DragInfo d,string target){
            int delta=(Rules.Date(target)-Rules.Date(d.Day)).Days;if(delta==0)return;
            var original=state.events.FirstOrDefault(e=>e.id==d.EventId);if(original==null)return;
            string scope="all";if(original.repeat!="none"){scope=await RepeatScope("이동",d.Start);if(scope==null)return;}
            var chosen=deadlineTarget;
            bool ok=await Commit(s=>{
                var series=s.events.FirstOrDefault(e=>e.id==d.EventId);if(series==null)throw new AppError(400,"일정을 찾지 못했습니다.");
                if(series.repeat!="none"&&scope=="one"){
                    var moved=Codec.Read<CalendarEvent>(Codec.Json(series));string newStart=Rules.AddDays(d.Start,delta);int duration=(Rules.Date(series.endDate)-Rules.Date(series.startDate)).Days;
                    moved.id=Guid.NewGuid().ToString();moved.startDate=newStart;moved.endDate=Rules.AddDays(newStart,duration);moved.repeat="none";moved.repeatUntil="";moved.excludedDates=new string[0];
                    moved.completedDates=series.completedDates.Contains(d.Start)?new[]{newStart}:new string[0];
                    moved.pinned=series.pinned&&(series.pinnedDate==d.Start||(series.pinnedDate==""&&chosen!=null&&chosen.@event.id==series.id&&chosen.startDate==d.Start));moved.pinnedDate=moved.pinned?newStart:"";
                    series.excludedDates=series.excludedDates.Concat(new[]{d.Start}).Distinct().ToArray();
                    if(moved.pinned){series.pinned=false;series.pinnedDate="";}
                    s.events=s.events.Concat(new[]{moved}).ToArray();
                }else{
                    series.startDate=Rules.AddDays(series.startDate,delta);series.endDate=Rules.AddDays(series.endDate,delta);
                    if(series.repeatUntil!="")series.repeatUntil=Rules.AddDays(series.repeatUntil,delta);
                    if(series.repeat.StartsWith("monthly:")){
                        var date=Rules.Date(Rules.AddDays(d.Start,delta));int day=date.Day,last=DateTime.DaysInMonth(date.Year,date.Month);int oldWeek=int.Parse(series.repeat.Split(':')[1]);
                        int week=oldWeek==-1&&day+7>last?-1:(day+6)/7>4?-1:(day+6)/7;series.repeat="monthly:"+week+":"+(int)date.DayOfWeek;
                    }
                    series.completedDates=series.completedDates.Select(k=>Rules.AddDays(k,delta)).ToArray();series.excludedDates=series.excludedDates.Select(k=>Rules.AddDays(k,delta)).ToArray();
                    if(series.pinnedDate!="")series.pinnedDate=Rules.AddDays(series.pinnedDate,delta);
                    if(series.repeat!="none"){var probe=Codec.Read<CalendarEvent>(Codec.Json(series));probe.excludedDates=new string[0];
                        if(series.completedDates.Concat(series.excludedDates).Concat(series.pinnedDate!=""?new[]{series.pinnedDate}:new string[0]).Any(k=>!Rules.OccursOn(probe,k)))throw new AppError(400,"반복 일정의 기존 기록을 보존하려면 이번 일정만 이동해 주세요.");}
                }
            });
            if(ok){JumpTo(target);UndoToast("일정을 옮겼어요",original.title,()=>Task.FromResult(false),"이동을 되돌렸어요");}
        }
        // A schedule dropped on the task panel becomes a task due on that date. For a repeating
        // schedule the user picks this occurrence only (skipped in the series) or the whole series.
        async void DropToTasks(DragInfo d){
            var original=state.events.FirstOrDefault(e=>e.id==d.EventId);if(original==null)return;
            string scope="all";if(original.repeat!="none"){scope=await RepeatScope("할 일로 이동",d.Start);if(scope==null)return;}
            bool done=Rules.Done(original,d.Start);string due=scope=="one"||original.repeat=="none"?d.Start:original.startDate;
            var task=new CalendarTask{id=scope=="one"?Guid.NewGuid().ToString():original.id,title=original.title,dueDate=due,done=scope=="one"||original.repeat=="none"?done:false,createdAt=DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ",CultureInfo.InvariantCulture)};
            bool ok=await Commit(s=>{
                var series=s.events.FirstOrDefault(e=>e.id==d.EventId);if(series==null)throw new AppError(400,"일정을 찾지 못했습니다.");
                if(scope=="one"){series.excludedDates=series.excludedDates.Concat(new[]{d.Start}).Distinct().ToArray();if(series.pinned&&series.pinnedDate==d.Start){series.pinned=false;series.pinnedDate="";}}
                else s.events=s.events.Where(e=>e.id!=series.id).ToArray();
                if(s.tasks.Any(t=>t.id==task.id))task.id=Guid.NewGuid().ToString();
                s.tasks=s.tasks.Concat(new[]{task}).ToArray();
            });
            if(ok)UndoToast("일정을 할 일로 옮겼어요",original.title,()=>Task.FromResult(false),"일정으로 되돌렸어요");
        }
        // ---------- Tasks ----------
        async void AddTask(){
            string title=taskTitle.Text.Trim();if(title.Length==0){Validation.Report(taskTitle,"이 입력란을 작성하세요.");return;}
            var task=new CalendarTask{id=Guid.NewGuid().ToString(),title=title,dueDate=taskDue.Value,done=false,createdAt=DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ",CultureInfo.InvariantCulture)};
            if(await Commit(s=>{s.tasks=s.tasks.Concat(new[]{task}).ToArray();})){taskTitle.Text="";taskDue.Value="";taskTitle.Focus();}
        }
        async void ToggleTask(string id){var t=state.tasks.FirstOrDefault(x=>x.id==id);if(t==null)return;bool done=!t.done;if(!await Commit(s=>{var item=s.tasks.FirstOrDefault(x=>x.id==id);if(item!=null)item.done=done;}))Invalidate();}
        async void DeleteTask(string id){
            var task=state.tasks.FirstOrDefault(t=>t.id==id);if(task==null)return;
            if(await Confirm("할 일을 삭제할까요?",task.title,"삭제")){var copy=Codec.Read<CalendarTask>(Codec.Json(task));if(await Commit(s=>{s.tasks=s.tasks.Where(t=>t.id!=id).ToArray();}))UndoToast("할 일을 삭제했어요",task.title,()=>Commit(s=>{if(s.tasks.Any(t=>t.id==copy.id))throw new AppError(400,"이미 복구된 할 일입니다.");s.tasks=s.tasks.Concat(new[]{Codec.Read<CalendarTask>(Codec.Json(copy))}).ToArray();}));}
        }
        async void ToggleHideCompleted(){bool value=!state.settings.hideCompleted;limits["tasks"]=pageSizes["tasks"];await Commit(s=>{s.settings.hideCompleted=value;});}
        async void DropTask(string id,string target){
            CalendarTask original=null;CalendarEvent converted=null;
            bool ok=await Commit(s=>{
                var index=Array.FindIndex(s.tasks,t=>t.id==id);if(index<0)throw new AppError(400,"할 일을 찾지 못했습니다.");
                original=Codec.Read<CalendarTask>(Codec.Json(s.tasks[index]));
                converted=new CalendarEvent{id=original.id,title=original.title,startDate=target,endDate=target,startTime="",endTime="",category="work",note="",repeat="none",repeatUntil="",reminder=s.settings.defaultReminder,pinned=false,pinnedDate="",completedDates=original.done?new[]{target}:new string[0],excludedDates=new string[0]};
                s.tasks=s.tasks.Where(t=>t.id!=id).ToArray();s.events=s.events.Concat(new[]{converted}).ToArray();
            });
            if(!ok)return;JumpTo(target);
            string snapshot=Codec.Json(converted);var task=original;
            UndoToast("할 일을 일정으로 옮겼어요",task.title,()=>Commit(s=>{
                var index=Array.FindIndex(s.events,e=>e.id==converted.id);
                if(index<0||Codec.Json(s.events[index])!=snapshot||s.tasks.Any(t=>t.id==task.id))throw new AppError(400,"옮긴 일정이 변경되어 자동으로 되돌릴 수 없습니다.");
                s.events=s.events.Where(e=>e.id!=converted.id).ToArray();s.tasks=s.tasks.Concat(new[]{Codec.Read<CalendarTask>(Codec.Json(task))}).ToArray();
            }),"할 일로 되돌렸어요");
        }
        // ---------- Dialog entry points ----------
        internal void OpenSettings(){OpenSettings(null);}
        internal void OpenSettings(string section){
            var existing=DialogForm.Open.OfType<SettingsDialog>().FirstOrDefault();if(existing!=null){if(section!=null)existing.Reveal(section);return;}
            if(modal)return;var dialog=new SettingsDialog(this,host);dialog.Show(null);if(section!=null)dialog.Reveal(section);
        }
        void ShowHelp(){if(modal)return;new HelpDialog(this).Show(null);}
        internal void ShowQuickTour(){if(modal)return;new QuickTourDialog(this,host).Show(null);}
        internal void FinishTour(){prefs.Value.quickTourDone=true;prefs.Save();}
        public void Ready(){ready=true;if(!prefs.Value.quickTourDone&&!modal)ShowQuickTour();}
        // window.petCalendarOpenEvent: alarms, widget and pets open schedules here.
        public void OpenFromHost(string id,string key){
            if(DialogForm.Open.Count>0){pending=true;pendingId=id;pendingKey=key;Toast("일정을 선택했어요","입력창을 닫으면 해당 일정을 보여드릴게요.");return;}
            if(id=="@dday"){OpenDdayDialog();return;}
            searchTimer.Stop();filter="all";search="";searchBox.Text="";JumpTo(key);
            if(id==null||state.events.Any(e=>e.id==id))OpenEvent(id,key,false);
        }
        void OnDialogsClosed(){if(pending){pending=false;var id=pendingId;var key=pendingKey;BeginInvoke(new Action(()=>OpenFromHost(id,key)));}Focus();}
        public void PetsChanged(){foreach(var d in DialogForm.Open.OfType<SettingsDialog>())d.UpdatePets(null);}
        public void WidgetChanged(){foreach(var d in DialogForm.Open.OfType<SettingsDialog>())d.UpdateWidget(null);}
        public void Reminder(string title,string body){Toast(title,body,"",15000);}
        internal async Task<Dictionary<string,object>> Api(string method,string path,object body=null){
            var response=await CallApi(method,path,body==null?"":Codec.Json(body),false);
            var result=Codec.Object(Codec.Json(response.body));
            if(response.status!=200){object e;throw new Exception(result!=null&&result.TryGetValue("error",out e)?e as string:"요청을 처리하지 못했습니다.");}
            return result;
        }
        internal DesktopHost Host{get{return host;}}
        internal System.Threading.Tasks.Task<ApiResponse> CallApi(string method,string path,string body,bool force){return ApiCall!=null?ApiCall(method,path,body,"1",force):host.Api(method,path,body,"1",force);}
    }
}
