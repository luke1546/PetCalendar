using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PetCalendarDesktop {
    public sealed class Hotkey { public bool enabled=true; public int modifiers=2; public int key=9; }
    public sealed class Settings {
        public bool hideCompleted=true,notifications=false,desktopPopups=true,notificationSound=true;
        public int? defaultReminder=10;
        public Hotkey hotkey=new Hotkey();
    }
    public sealed class CalendarEvent {
        public string id,title,startDate,endDate,startTime,endTime,category,note,repeat,repeatUntil;
        public int? reminder;
        public bool pinned;
        public string pinnedDate="";
        public string[] completedDates=new string[0];
        public string[] excludedDates=new string[0];
    }
    public sealed class CalendarTask { public string id,title,dueDate,createdAt; public bool done; }
    public sealed class CalendarState {
        public int version=1;
        public long revision;
        public string updatedAt;
        public CalendarEvent[] events=new CalendarEvent[0];
        public CalendarTask[] tasks=new CalendarTask[0];
        public Settings settings=new Settings();
    }
    public sealed class Occurrence {
        public CalendarEvent @event;
        public string key,startDate,endDate;
    }
    public sealed class AppError : Exception {
        public readonly int Status;
        public AppError(int status,string message):base(message){Status=status;}
    }
    public static class Codec {
        public static JavaScriptSerializer Serializer(){return new JavaScriptSerializer{MaxJsonLength=6*1024*1024,RecursionLimit=64};}
        public static string Json(object value){return Serializer().Serialize(value);}
        public static T Read<T>(string value){return Serializer().Deserialize<T>(value);}
        public static Dictionary<string,object> Object(string json){return Serializer().DeserializeObject(json) as Dictionary<string,object>;}
    }
    public static class Rules {
        public static readonly CultureInfo Korean=CultureInfo.GetCultureInfo("ko-KR");
        static readonly string[] Categories={"work","meeting","deadline","personal"};
        static readonly string[] Repeats={"none","daily","weekly","monthly"};
        public static DateTime Date(string key){return DateTime.ParseExact(key,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None);}
        public static string Key(DateTime date){return date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);}
        public static bool IsDate(object value){DateTime date;var text=value as string;return text!=null && Regex.IsMatch(text,"^[1-9][0-9]{3}-[0-9]{2}-[0-9]{2}$") && DateTime.TryParseExact(text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date);}
        public static string AddDays(string key,int days){return Key(Date(key).AddDays(days));}
        static void Check(bool ok,string message){if(!ok)throw new AppError(400,message);}
        static bool Has(Dictionary<string,object> o,string key){return o!=null && o.ContainsKey(key);}
        static object Value(Dictionary<string,object> o,string key){return Has(o,key)?o[key]:null;}
        static bool Number(object value){return value is int || value is long || value is double || value is decimal;}
        static bool Integer(object value){if(!Number(value))return false;double n=Convert.ToDouble(value,CultureInfo.InvariantCulture);return !Double.IsNaN(n)&&!Double.IsInfinity(n)&&n==Math.Floor(n);}
        static bool Time(object value){var s=value as string;return s!=null && (s=="" || Regex.IsMatch(s,"^([01][0-9]|2[0-3]):[0-5][0-9]$"));}
        static bool Reminder(object value){return value==null || (Integer(value) && new[]{0d,10d,30d,60d}.Contains(Convert.ToDouble(value)));}
        static void Text(Dictionary<string,object> o,string key,int max,string label,bool required=false){var s=Value(o,key) as string;Check(s!=null&&s.Length<=max&&(!required||s.Trim().Length>0),label+"을(를) 확인해 주세요.");}
        public static void ValidateHotkey(Hotkey key) {
            Check(key!=null&&key.modifiers>=0&&key.modifiers<=7&&ValidKey(key.key),"단축키 형식을 확인해 주세요.");
            Check(!key.enabled||(key.modifiers&3)!=0||(key.key>=112&&key.key<=122)||(key.modifiers==4&&key.key==192),"Ctrl 또는 Alt를 조합하거나 F1~F11, Shift + `를 선택해 주세요.");
            Check(!key.enabled||!(key.modifiers==1&&key.key==9),"Alt + Tab은 Windows 창 전환 키입니다. 다른 조합을 선택해 주세요.");
        }
        static bool ValidKey(int key){return key==9||key==32||key==192||(key>=65&&key<=90)||(key>=48&&key<=57)||(key>=112&&key<=122);}
        public static string HotkeyLabel(Hotkey h){var parts=new List<string>();if((h.modifiers&2)!=0)parts.Add("Ctrl");if((h.modifiers&1)!=0)parts.Add("Alt");if((h.modifiers&4)!=0)parts.Add("Shift");parts.Add(h.key==9?"Tab":h.key==32?"Space":h.key==192?"`":h.key>=112?"F"+(h.key-111):((char)h.key).ToString());return String.Join(" + ",parts);}
        public static CalendarState Validate(string json) {
            Check(Encoding.UTF8.GetByteCount(json)<=5*1024*1024,"파일은 5MB 이하여야 합니다.");
            Dictionary<string,object> raw;
            try{raw=Codec.Object(json.TrimStart('\uFEFF'));}catch{throw new AppError(400,"올바른 JSON 백업 파일이 아닙니다.");}
            Check(raw!=null&&Integer(Value(raw,"version"))&&Convert.ToDouble(raw["version"])==1,"지원하는 펫 캘린더 백업 파일이 아닙니다.");
            var events=Value(raw,"events") as object[];var tasks=Value(raw,"tasks") as object[];
            Check(events!=null&&tasks!=null,"일정 또는 할 일 형식이 올바르지 않습니다.");
            Check(events.Length<=10000&&tasks.Length<=10000,"저장 가능한 항목 수를 초과했습니다.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            Action<Dictionary<string,object>> id=o=>{Text(o,"id",100,"항목 ID",true);Check(ids.Add((string)o["id"]),"중복된 항목 ID가 있습니다.");};
            foreach(var item in events) {
                var e=item as Dictionary<string,object>;
                Check(e!=null&&IsDate(Value(e,"startDate"))&&IsDate(Value(e,"endDate"))&&String.CompareOrdinal((string)e["endDate"],(string)e["startDate"])>=0,"일정의 시작일과 종료일을 확인해 주세요.");
                Check(Time(Value(e,"startTime"))&&Time(Value(e,"endTime"))&&((string)e["endTime"]==""||(string)e["startTime"]!=""),"일정 시간을 확인해 주세요.");
                Check((string)e["endDate"]!=(string)e["startDate"]||(string)e["endTime"]==""||String.CompareOrdinal((string)e["endTime"],(string)e["startTime"])>=0,"종료 시간이 시작 시간보다 빠릅니다.");
                Check(Categories.Contains(Value(e,"category") as string),"일정 분류를 확인해 주세요.");
                Check(Repeats.Contains(Value(e,"repeat") as string)||Regex.IsMatch(Value(e,"repeat") as string??"","^monthly:(?:[1-4]|-1):[0-6]$"),"반복 방식을 확인해 주세요.");
                Check((Value(e,"repeatUntil") as string)==""||(IsDate(Value(e,"repeatUntil"))&&String.CompareOrdinal((string)e["repeatUntil"],(string)e["startDate"])>=0),"반복 종료일을 확인해 주세요.");
                Check(Has(e,"reminder")&&Reminder(e["reminder"]),"알림 시간을 확인해 주세요.");
                Check(Value(e,"pinned") is bool,"D-day 설정을 확인해 주세요.");
                Check(!Has(e,"pinnedDate")||(e["pinnedDate"] as string)==""||IsDate(e["pinnedDate"]),"D-day 날짜를 확인해 주세요.");
                if(Has(e,"completedDates")){var dates=e["completedDates"] as object[];Check(dates!=null&&dates.Length<=10000&&dates.All(IsDate),"일정 완료 기록을 확인해 주세요.");}
                if(Has(e,"excludedDates")){var dates=e["excludedDates"] as object[];Check(dates!=null&&dates.Length<=10000&&dates.All(IsDate),"반복 일정 제외 날짜를 확인해 주세요.");}
                id(e);Text(e,"title",200,"일정 제목",true);Text(e,"note",5000,"메모");
            }
            foreach(var item in tasks){var t=item as Dictionary<string,object>;Check(t!=null&&((Value(t,"dueDate") as string)==""||IsDate(Value(t,"dueDate")))&&Value(t,"done") is bool,"할 일 형식을 확인해 주세요.");id(t);Text(t,"title",200,"할 일 제목",true);Text(t,"createdAt",40,"생성 시각",true);}
            var s=Value(raw,"settings") as Dictionary<string,object>;
            Check(s!=null&&Value(s,"hideCompleted") is bool&&Value(s,"notifications") is bool&&Has(s,"defaultReminder")&&Reminder(s["defaultReminder"]),"설정 형식을 확인해 주세요.");
            foreach(string name in new[]{"desktopPopups","notificationSound"})Check(!Has(s,name)||s[name] is bool,"설정 형식을 확인해 주세요.");
            if(Has(s,"hotkey")){var h=s["hotkey"] as Dictionary<string,object>;Check(h!=null&&Value(h,"enabled") is bool&&Integer(Value(h,"modifiers"))&&Integer(Value(h,"key")),"단축키 형식을 확인해 주세요.");}
            var rawRevision=Value(raw,"revision");raw["revision"]=Integer(rawRevision)&&Convert.ToDouble(rawRevision)>=0&&Convert.ToDouble(rawRevision)<=9007199254740991d?Convert.ToInt64(rawRevision):0;
            raw["updatedAt"]=Value(raw,"updatedAt") as string;
            CalendarState state;try{state=Codec.Read<CalendarState>(Codec.Json(raw));}catch{throw new AppError(400,"설정 형식을 확인해 주세요.");}
            ValidateHotkey(state.settings.hotkey);
            var rev=Value(raw,"revision");state.revision=Integer(rev)&&Convert.ToDouble(rev)>=0&&Convert.ToDouble(rev)<=9007199254740991d?Convert.ToInt64(rev):0;
            state.updatedAt=Value(raw,"updatedAt") as string;
            foreach(var e in state.events){e.title=e.title.Trim();e.pinnedDate=e.pinnedDate??"";e.completedDates=(e.completedDates??new string[0]).Distinct().ToArray();e.excludedDates=(e.excludedDates??new string[0]).Distinct().ToArray();}
            foreach(var t in state.tasks)t.title=t.title.Trim();
            return state;
        }
        public static bool OccursOn(CalendarEvent e,string key){if((e.excludedDates??new string[0]).Contains(key)||String.CompareOrdinal(key,e.startDate)<0||(e.repeat!="none"&&!String.IsNullOrEmpty(e.repeatUntil)&&String.CompareOrdinal(key,e.repeatUntil)>0))return false;if(e.repeat=="none")return key==e.startDate;if(e.repeat=="daily")return true;DateTime d=Date(key);if(e.repeat=="weekly")return (d-Date(e.startDate)).Days%7==0;if(e.repeat.StartsWith("monthly:")){var parts=e.repeat.Split(':');int week=Int32.Parse(parts[1]),weekday=Int32.Parse(parts[2]);return (int)d.DayOfWeek==weekday&&(week==-1?d.Day+7>DateTime.DaysInMonth(d.Year,d.Month):(d.Day+6)/7==week);}return d.Day==Math.Min(Date(e.startDate).Day,DateTime.DaysInMonth(d.Year,d.Month));}
        public static bool Done(CalendarEvent e,string date){return (e.completedDates??new string[0]).Contains(date);}
        public static List<Occurrence> Occurrences(IEnumerable<CalendarEvent> events,string from,string to,bool sorted=true) {
            var list=new List<Occurrence>();DateTime first=Date(from),last=Date(to);
            foreach(var e in events) {
                DateTime original=Date(e.startDate);int span=(Date(e.endDate)-original).Days;
                if(e.repeat=="none"){if(original<=last&&original.AddDays(span)>=first)list.Add(new Occurrence{@event=e,key=e.id+"@"+e.startDate,startDate=e.startDate,endDate=e.endDate});continue;}
                DateTime start=first.Ticks/TimeSpan.TicksPerDay>=span?first.AddDays(-span):DateTime.MinValue;if(start<original)start=original;
                DateTime finish=String.IsNullOrEmpty(e.repeatUntil)?last:Date(e.repeatUntil);if(finish>last)finish=last;
                if(e.repeat=="weekly"){int offset=(start-original).Days%7;if(offset!=0)start=start.AddDays(7-offset);}
                if(e.repeat.StartsWith("monthly")) {
                    var cursor=new DateTime(start.Year,start.Month,1);var parts=e.repeat.Split(':');
                    while(cursor<=finish) {
                        int days=DateTime.DaysInMonth(cursor.Year,cursor.Month),day;
                        if(parts.Length==1)day=Math.Min(original.Day,days);
                        else {int week=Int32.Parse(parts[1]),weekday=Int32.Parse(parts[2]);day=week==-1?days-((int)new DateTime(cursor.Year,cursor.Month,days).DayOfWeek-weekday+7)%7:1+(weekday-(int)cursor.DayOfWeek+7)%7+(week-1)*7;}
                        var date=new DateTime(cursor.Year,cursor.Month,day);
                        if(date>=start&&date<=finish){string key=Key(date);if(!(e.excludedDates??new string[0]).Contains(key))list.Add(new Occurrence{@event=e,key=e.id+"@"+key,startDate=key,endDate=Key(date.AddDays(span))});}
                        if(cursor.Year==9999&&cursor.Month==12)break;cursor=cursor.AddMonths(1);
                    }
                    continue;
                }
                for(DateTime day=start;day<=finish;) {
                    string key=Key(day);
                    if(!(e.excludedDates??new string[0]).Contains(key))list.Add(new Occurrence{@event=e,key=e.id+"@"+key,startDate=key,endDate=Key(day.AddDays(span))});
                    if(day>=DateTime.MaxValue.Date.AddDays(-32))break;
                    day=day.AddDays(e.repeat=="weekly"?7:1);
                }
            }
            if(!sorted)return list;
            return list.OrderBy(o=>o.startDate,StringComparer.Ordinal).ThenBy(o=>o.@event.startTime,StringComparer.Ordinal).ThenBy(o=>o.@event.title,StringComparer.Create(Korean,false)).ToList();
        }
        public static string Dday(string key,string today){int days=(Date(key)-Date(today)).Days;return days==0?"D-day":days>0?"D−"+days:"D+"+(-days);}
        public static Occurrence DdayOccurrence(CalendarEvent e,string today) {
            string start=e.startDate;
            if(e.repeat!="none") {
                if(!String.IsNullOrEmpty(e.pinnedDate)&&OccursOn(e,e.pinnedDate))start=e.pinnedDate;
                else {string from=String.CompareOrdinal(today,e.startDate)>0?today:e.startDate;bool found=false;for(int i=0;i<=62;i++){string key=AddDays(from,i);if(OccursOn(e,key)){start=key;found=true;break;}}if(!found&&!String.IsNullOrEmpty(e.repeatUntil))for(int i=0;i<=62;i++){string key=AddDays(e.repeatUntil,-i);if(OccursOn(e,key)){start=key;break;}}}
            }
            return new Occurrence{@event=e,key=e.id+"@"+start,startDate=start,endDate=AddDays(start,(Date(e.endDate)-Date(e.startDate)).Days)};
        }
        public static long Epoch(DateTime utc){return (utc.ToUniversalTime().Ticks-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).Ticks)/TimeSpan.TicksPerMillisecond;}
        public static long? ReminderAt(Occurrence o){if(!o.@event.reminder.HasValue||Done(o.@event,o.startDate))return null;DateTime local=Date(o.startDate).Add(TimeSpan.Parse(String.IsNullOrEmpty(o.@event.startTime)?"09:00":o.@event.startTime,CultureInfo.InvariantCulture));var zone=TimeZoneInfo.Local;if(zone.IsInvalidTime(local)){var next=local;while(zone.IsInvalidTime(next))next=next.AddMinutes(1);var previous=local;while(zone.IsInvalidTime(previous))previous=previous.AddMinutes(-1);local=local.Add(zone.GetUtcOffset(next)-zone.GetUtcOffset(previous));}TimeSpan offset=zone.IsAmbiguousTime(local)?zone.GetAmbiguousTimeOffsets(local).Max():zone.GetUtcOffset(local);return Epoch(new DateTimeOffset(local,offset).UtcDateTime)-o.@event.reminder.Value*60000L;}
    }
    public sealed class StateStore {
        public readonly string DirectoryPath,FilePath;
        readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);
        CalendarState current;
        public event Action Changed;
        public CalendarState Snapshot {get{return current;}}
        public StateStore(string directory){DirectoryPath=directory;FilePath=Path.Combine(directory,"calendar.json");Directory.CreateDirectory(Path.Combine(directory,"backups"));if(File.Exists(FilePath)){try{current=Rules.Validate(File.ReadAllText(FilePath,Encoding.UTF8));}catch(Exception e){throw new AppError(500,"저장 파일을 읽지 못했습니다. 원본을 보존했습니다.\n"+FilePath+"\n"+e.Message);}}else{current=new CalendarState();Atomic(FilePath,Codec.Json(current),null);}}
        public async Task<CalendarState> Save(string json,Func<Hotkey,Task> apply,bool forceHotkey=false) {
            CalendarState next=await Task.Run(()=>Rules.Validate(json));await gate.WaitAsync();
            Hotkey previous=current.settings.hotkey;bool changed=Codec.Json(next.settings.hotkey)!=Codec.Json(previous)||forceHotkey;
            try {
                if(next.revision!=current.revision)throw new AppError(409,"다른 창에서 일정이 변경되었습니다. 최신 내용을 불러온 뒤 다시 저장해 주세요.");
                if(changed)await apply(next.settings.hotkey);
                next.revision=current.revision+1;next.updatedAt=DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ",CultureInfo.InvariantCulture);
                string backup=Path.Combine(DirectoryPath,"backups","calendar-"+current.revision.ToString("D10")+"-"+Rules.Epoch(DateTime.UtcNow)+".json");
                Exception writeFailure=null;try{await Task.Run(()=>{Atomic(FilePath,Codec.Json(next),backup);});}catch(Exception error){writeFailure=error;}
                if(writeFailure!=null){if(changed)await apply(previous);throw writeFailure;}
                current=next;
                await Task.Run(()=>{try{var files=Directory.GetFiles(Path.Combine(DirectoryPath,"backups"),"calendar-*.json").Where(p=>Regex.IsMatch(Path.GetFileName(p),"^calendar-[0-9]+-[0-9]+\\.json$")).OrderBy(p=>p,StringComparer.Ordinal).ToArray();foreach(string old in files.Take(Math.Max(0,files.Length-30)))File.Delete(old);}catch{}});
                if(Changed!=null)Changed();return next;
            }finally{gate.Release();}
        }
        public async Task Drain(){await gate.WaitAsync();gate.Release();}
        public static void Atomic(string path,string text,string backup){string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{byte[] bytes=new UTF8Encoding(false).GetBytes(text);using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}if(File.Exists(path))File.Replace(temp,path,backup);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
    }
}
