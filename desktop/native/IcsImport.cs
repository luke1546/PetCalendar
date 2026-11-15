using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PetCalendarDesktop {
    // Reads an iCalendar (.ics) export from Google Calendar, Outlook or Apple Calendar into
    // this app's schedules. Supported: all-day and timed events (UTC, local or TZID times),
    // multi-day spans, DESCRIPTION as the memo, daily/weekly/monthly RRULEs with UNTIL or
    // COUNT, "nth weekday" monthly rules, EXDATE and RDATE (each extra date becomes its own schedule). Rules the app cannot express (every 2
    // weeks, several weekdays, yearly, monthly on the 29th-31st) keep only the first date and
    // are counted as simplified.
    public static class IcsImport {
        public sealed class Result {
            public List<CalendarEvent> Events=new List<CalendarEvent>();
            public int Simplified,Skipped,Cancelled;public List<string> SimplifiedTitles=new List<string>();
            // A VEVENT was read even when none of them became a schedule (all skipped or cancelled).
            public bool FoundVEvent{get{return Events.Count+Skipped+Cancelled>0;}}
        }
        // The notice when nothing new can be imported: says whether the file had no VEVENT at all,
        // or how many were skipped, cancelled or already here.
        internal static void NothingToImport(Result r,int duplicates,out string title,out string detail){
            if(!r.FoundVEvent){title="가져올 일정이 없어요";detail="파일에서 일정(VEVENT)을 찾지 못했어요.";return;}
            var reasons=new List<string>();
            if(duplicates>0)reasons.Add("같은 일정 "+duplicates+"개를 건너뛰었어요.");
            if(r.Skipped>0)reasons.Add("가져오지 못한 일정 "+r.Skipped+"개는 건너뛰었어요.");
            if(r.Cancelled>0)reasons.Add("취소된 일정 "+r.Cancelled+"개는 가져오지 않았어요.");
            title=r.Events.Count==0?"가져올 일정이 없어요":"이미 모두 들어 있어요";detail=String.Join("\n",reasons);
        }
        // Two schedules are the same only when title, start, end, repeat rule and skipped dates all match. The same title and
        // start time with another end or repeat is a separate schedule, so it is imported.
        static string Identity(CalendarEvent e){
            return String.Join("|",new[]{e.title,e.startDate,e.startTime,e.endDate,e.endTime,e.repeat,e.repeatUntil,String.Join(",",(e.excludedDates??new string[0]).OrderBy(x=>x,StringComparer.Ordinal))});
        }
        // The imported schedules that are not already in `existing`; `duplicates` counts the rest. Importing one file twice adds nothing.
        internal static List<CalendarEvent> FreshEvents(IEnumerable<CalendarEvent> existing,IEnumerable<CalendarEvent> imported,out int duplicates){
            var seen=new HashSet<string>(existing.Select(Identity));var fresh=new List<CalendarEvent>();duplicates=0;
            foreach(var e in imported){if(seen.Add(Identity(e)))fresh.Add(e);else duplicates++;}
            return fresh;
        }
        public static Result Parse(string text,string category,int? reminder){
            var result=new Result();var lines=Unfold(text);
            Dictionary<string,List<KeyValuePair<Dictionary<string,string>,string>>> props=null;int depth=0;
            foreach(var line in lines){
                if(line.Equals("BEGIN:VEVENT",StringComparison.OrdinalIgnoreCase)){props=new Dictionary<string,List<KeyValuePair<Dictionary<string,string>,string>>>(StringComparer.OrdinalIgnoreCase);depth=0;continue;}
                if(props==null)continue;
                if(line.StartsWith("BEGIN:",StringComparison.OrdinalIgnoreCase)){depth++;continue;} // VALARM inside the event
                if(line.StartsWith("END:",StringComparison.OrdinalIgnoreCase)){
                    if(depth>0){depth--;continue;}
                    if(line.Equals("END:VEVENT",StringComparison.OrdinalIgnoreCase)){Convert(props,category,reminder,result);props=null;}
                    continue;
                }
                if(depth>0)continue;
                int colon=FindColon(line);if(colon<=0)continue;
                var head=line.Substring(0,colon).Split(';');var name=head[0].Trim();var parameters=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                for(int i=1;i<head.Length;i++){int eq=head[i].IndexOf('=');if(eq>0)parameters[head[i].Substring(0,eq)]=head[i].Substring(eq+1).Trim('"');}
                List<KeyValuePair<Dictionary<string,string>,string>> list;if(!props.TryGetValue(name,out list))props[name]=list=new List<KeyValuePair<Dictionary<string,string>,string>>();
                list.Add(new KeyValuePair<Dictionary<string,string>,string>(parameters,line.Substring(colon+1)));
            }
            return result;
        }
        // A colon inside a quoted parameter (TZID="...:...") is not the value separator.
        static int FindColon(string line){bool quoted=false;for(int i=0;i<line.Length;i++){if(line[i]=='"')quoted=!quoted;else if(line[i]==':'&&!quoted)return i;}return -1;}
        static List<string> Unfold(string text){
            var result=new List<string>();
            foreach(var raw in text.Replace("\r\n","\n").Replace('\r','\n').Split('\n')){
                if(raw.Length>0&&(raw[0]==' '||raw[0]=='\t')&&result.Count>0)result[result.Count-1]+=raw.Substring(1);
                else if(raw.Length>0)result.Add(raw);
            }
            return result;
        }
        static string Unescape(string v){
            var sb=new StringBuilder();
            for(int i=0;i<v.Length;i++){
                if(v[i]=='\\'&&i+1<v.Length){char n=v[++i];sb.Append(n=='n'||n=='N'?'\n':n);}
                else sb.Append(v[i]);
            }
            return sb.ToString();
        }
        static string First(Dictionary<string,List<KeyValuePair<Dictionary<string,string>,string>>> p,string name){List<KeyValuePair<Dictionary<string,string>,string>> l;return p.TryGetValue(name,out l)&&l.Count>0?l[0].Value:null;}
        static Dictionary<string,string> Params(Dictionary<string,List<KeyValuePair<Dictionary<string,string>,string>>> p,string name){List<KeyValuePair<Dictionary<string,string>,string>> l;return p.TryGetValue(name,out l)&&l.Count>0?l[0].Key:new Dictionary<string,string>();}
        // Returns local time, and whether the value was a date without a time.
        static bool ParseWhen(string value,Dictionary<string,string> parameters,out DateTime when,out bool dateOnly){
            when=DateTime.MinValue;dateOnly=false;if(String.IsNullOrEmpty(value))return false;value=value.Trim();
            string type;parameters.TryGetValue("VALUE",out type);
            if(value.Length==8||String.Equals(type,"DATE",StringComparison.OrdinalIgnoreCase)){
                dateOnly=true;return DateTime.TryParseExact(value.Substring(0,Math.Min(8,value.Length)),"yyyyMMdd",CultureInfo.InvariantCulture,DateTimeStyles.None,out when);
            }
            bool utc=value.EndsWith("Z",StringComparison.OrdinalIgnoreCase);string core=utc?value.Substring(0,value.Length-1):value;
            if(!DateTime.TryParseExact(core,new[]{"yyyyMMdd'T'HHmmss","yyyyMMdd'T'HHmm"},CultureInfo.InvariantCulture,DateTimeStyles.None,out when))return false;
            if(utc){when=DateTime.SpecifyKind(when,DateTimeKind.Utc).ToLocalTime();return true;}
            string tzid;
            if(parameters.TryGetValue("TZID",out tzid)){var zone=Zone(tzid);if(zone!=null){try{when=TimeZoneInfo.ConvertTime(when,zone,TimeZoneInfo.Local);}catch(Exception){}}}
            return true;
        }
        // Windows ids work directly; the IANA names Google and Apple use are mapped for common zones.
        static readonly Dictionary<string,string> Iana=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){
            {"Asia/Seoul","Korea Standard Time"},{"Asia/Tokyo","Tokyo Standard Time"},{"Asia/Shanghai","China Standard Time"},{"Asia/Hong_Kong","China Standard Time"},{"Asia/Taipei","Taipei Standard Time"},{"Asia/Singapore","Singapore Standard Time"},
            {"UTC","UTC"},{"Etc/UTC","UTC"},{"Europe/London","GMT Standard Time"},{"Europe/Paris","Romance Standard Time"},{"Europe/Berlin","W. Europe Standard Time"},
            {"America/New_York","Eastern Standard Time"},{"America/Chicago","Central Standard Time"},{"America/Denver","Mountain Standard Time"},{"America/Los_Angeles","Pacific Standard Time"},{"Australia/Sydney","AUS Eastern Standard Time"}};
        static TimeZoneInfo Zone(string id){
            string mapped;if(Iana.TryGetValue(id,out mapped))id=mapped;
            try{return TimeZoneInfo.FindSystemTimeZoneById(id);}catch(Exception){return null;}
        }
        // The end that a DURATION gives after start. A value that is not a duration gives no end; one too large for a date
        // the app can hold returns false, so its VEVENT is skipped instead of failing the whole file.
        static bool EndByDuration(string v,DateTime start,out DateTime? end){
            end=null;if(String.IsNullOrEmpty(v))return true;var m=System.Text.RegularExpressions.Regex.Match(v.Trim(),@"^([+-])?P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$");
            if(!m.Success)return true;
            int[] groups={2,3,4,5,6};long[] unit={7*TimeSpan.TicksPerDay,TimeSpan.TicksPerDay,TimeSpan.TicksPerHour,TimeSpan.TicksPerMinute,TimeSpan.TicksPerSecond};
            decimal ticks=0;
            for(int i=0;i<groups.Length;i++){
                if(!m.Groups[groups[i]].Success)continue;decimal amount;
                if(!decimal.TryParse(m.Groups[groups[i]].Value,NumberStyles.None,CultureInfo.InvariantCulture,out amount))return false;
                if(amount>DateTime.MaxValue.Ticks/unit[i])return false;
                ticks+=amount*unit[i];
            }
            if(start.Ticks+ticks>DateTime.MaxValue.Ticks)return false;
            end=start.AddTicks((long)ticks);return true;
        }
        static void Convert(Dictionary<string,List<KeyValuePair<Dictionary<string,string>,string>>> p,string category,int? reminder,Result result){
            if(First(p,"RECURRENCE-ID")!=null){result.Skipped++;return;} // a changed copy of one repeat; the series itself is imported
            var status=First(p,"STATUS");if(status!=null&&status.Trim().Equals("CANCELLED",StringComparison.OrdinalIgnoreCase)){result.Cancelled++;return;}
            DateTime start,end;bool allDay,endDateOnly;
            if(!ParseWhen(First(p,"DTSTART"),Params(p,"DTSTART"),out start,out allDay)){result.Skipped++;return;}
            bool hasEnd=ParseWhen(First(p,"DTEND"),Params(p,"DTEND"),out end,out endDateOnly);
            if(!hasEnd){DateTime? byDuration;if(!EndByDuration(First(p,"DURATION"),start,out byDuration)){result.Skipped++;return;}if(byDuration!=null){end=byDuration.Value;hasEnd=true;}}
            string title=Unescape(First(p,"SUMMARY")??"").Trim();if(title.Length==0)title="(제목 없음)";if(title.Length>200)title=title.Substring(0,200);
            string note=Unescape(First(p,"DESCRIPTION")??"").Trim();
            string location=Unescape(First(p,"LOCATION")??"").Trim();if(location.Length>0)note=("장소: "+location+(note.Length>0?"\n"+note:""));
            if(note.Length>5000)note=note.Substring(0,5000);
            var e=new CalendarEvent{id=Guid.NewGuid().ToString(),title=title,note=note,category=category,repeat="none",repeatUntil="",reminder=reminder,pinned=false,pinnedDate="",completedDates=new string[0],excludedDates=new string[0]};
            Place(e,start,end,hasEnd,allDay);
            var template=Codec.Read<CalendarEvent>(Codec.Json(e));
            ApplyRule(e,First(p,"RRULE"),start,result);
            if(e.repeat!="none")e.excludedDates=ListDates(p,"EXDATE").Select(d=>Rules.Key(d.Date)).Distinct().ToArray();
            result.Events.Add(e);
            AddRepeatDates(p,e,template,start,end,hasEnd,allDay,result);
        }
        // Sets the date and time fields of a schedule from its start and end; `end` is read only when hasEnd.
        static void Place(CalendarEvent e,DateTime start,DateTime end,bool hasEnd,bool allDay){
            e.startDate=Rules.Key(start.Date);
            if(allDay){
                e.startTime=e.endTime="";
                // An all-day DTEND is the day after the last day.
                var last=hasEnd?end.Date.AddDays(-1):start.Date;if(last<start.Date)last=start.Date;e.endDate=Rules.Key(last);
            }else{
                e.startTime=start.ToString("HH:mm",CultureInfo.InvariantCulture);
                if(!hasEnd||end<=start){e.endDate=e.startDate;e.endTime="";}
                else{
                    // Ending exactly at midnight belongs to the day before.
                    if(end.TimeOfDay==TimeSpan.Zero&&end.Date>start.Date){e.endDate=Rules.Key(end.Date.AddDays(-1));e.endTime=e.endDate==e.startDate?"23:59":"";}
                    else{e.endDate=Rules.Key(end.Date);e.endTime=end.ToString("HH:mm",CultureInfo.InvariantCulture);}
                }
            }
        }
        // RDATE lists extra dates of a series. Each one becomes a one-off schedule with the series' title, memo, time, span and
        // reminder, or with the end of its own PERIOD value. A date the series already has (DTSTART, RRULE) or that EXDATE skips is not added again.
        static void AddRepeatDates(Dictionary<string,List<KeyValuePair<Dictionary<string,string>,string>>> p,CalendarEvent series,CalendarEvent template,DateTime start,DateTime end,bool hasEnd,bool allDay,Result result){
            var taken=new HashSet<string>(ListDates(p,"EXDATE").Select(Slot).Concat(new[]{Slot(start)}));
            foreach(var value in ListValues(p,"RDATE")){
                var when=value.Start;
                if(!taken.Add(Slot(when)))continue;
                // The series is saved to the minute, so seconds in DTSTART or in the RDATE do not make another occurrence.
                if(when.Hour==start.Hour&&when.Minute==start.Minute&&Rules.OccursOn(series,Rules.Key(when.Date)))continue;
                if(value.TooLong){result.Skipped++;continue;}
                var occurrenceEnd=end;var occurrenceHasEnd=hasEnd;
                if(value.End!=null){occurrenceEnd=value.End.Value;occurrenceHasEnd=true;}
                else if(hasEnd){
                    var shift=when-start;long endTicks=end.Ticks+shift.Ticks;
                    if(endTicks<DateTime.MinValue.Ticks||endTicks>DateTime.MaxValue.Ticks){result.Skipped++;continue;}
                    occurrenceEnd=end.Add(shift);
                }
                var extra=Codec.Read<CalendarEvent>(Codec.Json(template));extra.id=Guid.NewGuid().ToString();
                Place(extra,when,occurrenceEnd,occurrenceHasEnd,allDay);
                result.Events.Add(extra);
            }
        }
        // A date and minute, so that the same moment in an RDATE, EXDATE or DTSTART compares equal.
        static string Slot(DateTime d){return Rules.Key(d.Date)+" "+d.ToString("HH:mm",CultureInfo.InvariantCulture);}
        // One value of a DATE, DATE-TIME or PERIOD list: its local start, and the end of a PERIOD when it has one.
        // TooLong is a PERIOD duration that goes past the last date the app can hold.
        sealed class ListValue{public DateTime Start;public DateTime? End;public bool TooLong;}
        static List<DateTime> ListDates(Dictionary<string,List<KeyValuePair<Dictionary<string,string>,string>>> p,string name){
            var dates=new List<DateTime>();foreach(var value in ListValues(p,name))dates.Add(value.Start);
            return dates;
        }
        // The values of an EXDATE or RDATE list. A PERIOD (start/end or start/duration) keeps its end; an EXDATE ignores it.
        static List<ListValue> ListValues(Dictionary<string,List<KeyValuePair<Dictionary<string,string>,string>>> p,string name){
            var values=new List<ListValue>();List<KeyValuePair<Dictionary<string,string>,string>> items;
            if(!p.TryGetValue(name,out items))return values;
            foreach(var item in items)foreach(var v in item.Value.Split(',')){
                var parts=v.Split('/');DateTime d;bool only;
                if(!ParseWhen(parts[0],item.Key,out d,out only))continue;
                var value=new ListValue{Start=d};
                if(parts.Length>1){
                    var tail=parts[1].Trim();DateTime until;
                    if(tail.StartsWith("P",StringComparison.OrdinalIgnoreCase)||tail.StartsWith("+")||tail.StartsWith("-")){DateTime? byDuration;if(EndByDuration(tail,d,out byDuration))value.End=byDuration;else value.TooLong=true;}
                    else if(ParseWhen(tail,item.Key,out until,out only))value.End=until;
                }
                values.Add(value);
            }
            return values;
        }
        // Maps an RRULE onto the app's repeats. Anything the app cannot express exactly (every 3
        // weeks, Mon/Wed/Fri, hourly, months limited by BYMONTH, ...) keeps only the first date and
        // is reported, so a rule is never silently widened (e.g. weekdays turning into every day).
        static readonly HashSet<string> KnownParts=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"FREQ","INTERVAL","UNTIL","COUNT","BYDAY","BYMONTHDAY","BYSETPOS","BYMONTH","WKST"};
        static readonly string WeekdaySet="FR,MO,TH,TU,WE";
        static void ApplyRule(CalendarEvent e,string rule,DateTime start,Result result){
            if(String.IsNullOrEmpty(rule))return;
            var parts=rule.Split(';').Select(x=>x.Split(new[]{'='},2)).Where(x=>x.Length==2).GroupBy(x=>x[0].Trim(),StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First()[1].Trim().ToUpperInvariant(),StringComparer.OrdinalIgnoreCase);
            string freq;parts.TryGetValue("FREQ",out freq);freq=freq??"";
            string interval;parts.TryGetValue("INTERVAL",out interval);int every=1;if(interval!=null&&!int.TryParse(interval,out every))every=0;
            string byDay,byMonthDay,byMonth,bySetPos;parts.TryGetValue("BYDAY",out byDay);parts.TryGetValue("BYMONTHDAY",out byMonthDay);parts.TryGetValue("BYMONTH",out byMonth);parts.TryGetValue("BYSETPOS",out bySetPos);
            string days=byDay==null?null:String.Join(",",byDay.Split(',').Select(x=>x.Trim()).OrderBy(x=>x,StringComparer.Ordinal));
            bool startDay=byDay==null||(!byDay.Contains(",")&&DayIndex(byDay)==(int)start.DayOfWeek);
            string monthNumber=start.Month.ToString(CultureInfo.InvariantCulture),dayNumber=start.Day.ToString(CultureInfo.InvariantCulture);
            string repeat=null;
            if(parts.Keys.Any(k=>!KnownParts.Contains(k))){}
            else if(freq=="DAILY"&&every==1&&byMonthDay==null&&byMonth==null&&bySetPos==null){
                if(byDay==null||days=="FR,MO,SA,SU,TH,TU,WE")repeat="daily";else if(days==WeekdaySet)repeat="weekdays";
            }
            else if(freq=="WEEKLY"&&byMonthDay==null&&byMonth==null&&bySetPos==null){
                if(every==1&&days==WeekdaySet)repeat="weekdays";
                else if(every==1&&days=="FR,MO,SA,SU,TH,TU,WE")repeat="daily";
                else if(startDay&&every==1)repeat="weekly";
                else if(startDay&&every==2)repeat="biweekly";
            }
            else if(freq=="MONTHLY"&&every==1&&byMonth==null){
                // The 29th-31st skip the months that lack them; the app's monthly clamps to the last day instead, so they are not mapped.
                if(byDay==null){if(bySetPos==null&&(byMonthDay==null||byMonthDay==dayNumber)&&start.Day<=28)repeat="monthly";}
                else if(byMonthDay==null){
                    // "2TU" (2nd Tuesday) or BYDAY=TU with BYSETPOS=2; -1 is the last one.
                    var m=System.Text.RegularExpressions.Regex.Match(byDay,@"^([+-]?\d)?([A-Z]{2})$");
                    if(m.Success){int week;string n=m.Groups[1].Success?m.Groups[1].Value:bySetPos;int day=DayIndex(m.Groups[2].Value);
                        if(n!=null&&int.TryParse(n,out week)&&(week==-1||(week>=1&&week<=4))&&day==(int)start.DayOfWeek)repeat="monthly:"+week+":"+day;}
                }
            }
            else if(freq=="YEARLY"&&every==1&&byDay==null&&bySetPos==null&&((byMonth==null||byMonth==monthNumber)&&(byMonthDay==null||byMonthDay==dayNumber)||LeapDayYearly(e,start,byMonth,byMonthDay)))repeat="yearly";
            if(repeat==null){result.Simplified++;result.SimplifiedTitles.Add(e.title);return;}
            e.repeat=repeat;
            string until,count;
            if(parts.TryGetValue("UNTIL",out until)){DateTime u;bool only;if(ParseWhen(until,new Dictionary<string,string>(),out u,out only)){
                // A timed UNTIL is an instant: on its own date the occurrence only counts if it starts at or before that time.
                if(!only&&e.startTime!=""&&u.Date>start.Date&&String.CompareOrdinal(u.ToString("HH:mm",CultureInfo.InvariantCulture),e.startTime)<0)u=u.Date.AddDays(-1);
                e.repeatUntil=Rules.Key(u.Date);}}
            else if(parts.TryGetValue("COUNT",out count)){int c;
                // A COUNT that is not a positive number would leave the series endless: keep only the first date and report it.
                if(!int.TryParse(count,out c)||c<=0){e.repeat="none";e.repeatUntil="";e.excludedDates=new string[0];result.Simplified++;result.SimplifiedTitles.Add(e.title);return;}
                {
                // The last date of the series: walk the app's own rule in ten-year steps until c dates
                // have passed. A count that cannot be reached is not silently cut: only the first date is kept.
                var probe=Codec.Read<CalendarEvent>(Codec.Json(e));probe.repeatUntil="";probe.excludedDates=new string[0];
                var dates=new List<string>();var cursor=start.Date;
                while(dates.Count<c&&c<=100000&&cursor.Year<9000){
                    var to=cursor.AddYears(10);string first=Rules.Key(cursor);
                    dates.AddRange(Rules.Occurrences(new[]{probe},first,Rules.Key(to),false).Select(o=>o.startDate).Where(k=>String.CompareOrdinal(k,first)>=0).OrderBy(k=>k,StringComparer.Ordinal));
                    cursor=to.AddDays(1);
                }
                if(dates.Count>=c)e.repeatUntil=dates[c-1];
                else{e.repeat="none";e.repeatUntil="";e.excludedDates=new string[0];result.Simplified++;result.SimplifiedTitles.Add(e.title);return;}}}
            if(e.repeatUntil!=""&&String.CompareOrdinal(e.repeatUntil,e.startDate)<0)e.repeatUntil=e.startDate;
        }
        // The shifted rules IcsExport writes for a Feb 29 start (see LeapDayRule). Each one is only the app's yearly repeat
        // for the UTC shift of this start, so a rule for another shift (e.g. an all-day start with BYMONTHDAY=-2) is simplified.
        static bool LeapDayYearly(CalendarEvent e,DateTime start,string byMonth,string byMonthDay){
            if(start.Month!=2||start.Day!=29)return false;
            int shift=e.startTime==""?0:(start.ToUniversalTime().Date-start.Date).Days;
            return shift>0?byMonth=="3"&&byMonthDay=="1":byMonth=="2"&&byMonthDay==(shift-1).ToString(CultureInfo.InvariantCulture);
        }
        static int DayIndex(string code){return Array.IndexOf(new[]{"SU","MO","TU","WE","TH","FR","SA"},(code??"").Trim().ToUpperInvariant());}
    }

    // Writes schedules as an iCalendar file that Google Calendar, Outlook and Apple Calendar
    // import. Timed schedules are written in UTC; all-day ones as dates (DTEND is exclusive).
    public static class IcsExport {
        public static string Write(IEnumerable<CalendarEvent> events,Func<string,string> categoryName){
            var sb=new StringBuilder();string stamp=DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'",CultureInfo.InvariantCulture);
            Action<string> line=l=>Fold(sb,l);
            line("BEGIN:VCALENDAR");line("VERSION:2.0");line("PRODID:-//PetCalendar//KO");line("CALSCALE:GREGORIAN");line("X-WR-CALNAME:펫 캘린더");
            foreach(var e in events){
                bool allDay=e.startTime=="";
                line("BEGIN:VEVENT");line("UID:"+e.id+"@petcalendar");line("DTSTAMP:"+stamp);
                if(allDay){line("DTSTART;VALUE=DATE:"+D(e.startDate));line("DTEND;VALUE=DATE:"+D(Rules.AddDays(e.endDate,1)));}
                else{
                    var start=Local(e.startDate,e.startTime);var end=e.endTime!=""?Local(e.endDate,e.endTime):e.endDate!=e.startDate?Local(e.endDate,e.startTime):start.AddHours(1);
                    if(end<=start)end=start.AddHours(1);
                    line("DTSTART:"+Utc(start));line("DTEND:"+Utc(end));
                }
                line("SUMMARY:"+Escape(e.title));
                if(!String.IsNullOrEmpty(e.note))line("DESCRIPTION:"+Escape(e.note));
                string category=categoryName(e.category);if(!String.IsNullOrEmpty(category))line("CATEGORIES:"+Escape(category));
                string rule=Rule(e);if(rule!=null)line("RRULE:"+rule);
                foreach(var x in e.excludedDates??new string[0])line(allDay?"EXDATE;VALUE=DATE:"+D(x):"EXDATE:"+Utc(Local(x,e.startTime)));
                if(e.reminder.HasValue){line("BEGIN:VALARM");line("ACTION:DISPLAY");line("DESCRIPTION:"+Escape(e.title));line("TRIGGER:-PT"+e.reminder.Value+"M");line("END:VALARM");}
                line("END:VEVENT");
            }
            line("END:VCALENDAR");return sb.ToString();
        }
        static string Rule(CalendarEvent e){
            string r;
            switch(e.repeat){
                case "none":return null;
                case "daily":r="FREQ=DAILY";break;
                case "weekdays":r="FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR";break;
                case "weekly":r="FREQ=WEEKLY";break;
                case "biweekly":r="FREQ=WEEKLY;INTERVAL=2";break;
                case "monthly":r="FREQ=MONTHLY;BYMONTHDAY="+Rules.Date(e.startDate).Day;break;
                case "yearly":r="FREQ=YEARLY";if(LeapDay(e.startDate))r+=";"+LeapDayRule(e);break;
                default:
                    if(!e.repeat.StartsWith("monthly:"))return null;
                    var p=e.repeat.Split(':');r="FREQ=MONTHLY;BYDAY="+p[1]+new[]{"SU","MO","TU","WE","TH","FR","SA"}[int.Parse(p[2],CultureInfo.InvariantCulture)];break;
            }
            if(!String.IsNullOrEmpty(e.repeatUntil))r+=";UNTIL="+(e.startTime==""?D(e.repeatUntil):Utc(Local(e.repeatUntil,"23:59")));
            return r;
        }
        // The app's yearly repeat keeps Feb 29 starts on Feb 28 in common years. A plain FREQ=YEARLY skips those
        // years (RFC 5545 ignores dates that do not exist), so the last day of February is named instead.
        static bool LeapDay(string key){var d=Rules.Date(key);return d.Month==2&&d.Day==29;}
        // A timed start is written in UTC, so the rule follows the UTC day: the same day as the local date, the day
        // before (east of UTC, e.g. 00:30 in Korea) or 1 March (west of UTC, e.g. 23:30 in New York).
        static string LeapDayRule(CalendarEvent e){
            int shift=e.startTime==""?0:(Local(e.startDate,e.startTime).ToUniversalTime().Date-Rules.Date(e.startDate)).Days;
            return shift>0?"BYMONTH=3;BYMONTHDAY=1":"BYMONTH=2;BYMONTHDAY="+(shift-1).ToString(CultureInfo.InvariantCulture);
        }
        static DateTime Local(string date,string time){return DateTime.SpecifyKind(Rules.Date(date).Add(TimeSpan.Parse(time,CultureInfo.InvariantCulture)),DateTimeKind.Local);}
        static string Utc(DateTime local){return local.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'",CultureInfo.InvariantCulture);}
        static string D(string key){return key.Replace("-","");}
        static string Escape(string v){return (v??"").Replace("\\","\\\\").Replace(";","\\;").Replace(",","\\,").Replace("\r\n","\n").Replace("\n","\\n");}
        // Lines longer than 75 octets continue on the next line after a space (RFC 5545 3.1).
        static void Fold(StringBuilder sb,string text){
            int octets=0;
            foreach(char ch in text){int size=Encoding.UTF8.GetByteCount(new[]{ch});if(char.IsHighSurrogate(ch))size=4;else if(char.IsLowSurrogate(ch))size=0;
                if(octets+size>75&&size>0){sb.Append("\r\n ");octets=1;}
                sb.Append(ch);octets+=size;}
            sb.Append("\r\n");
        }
    }
}
