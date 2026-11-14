using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PetCalendarDesktop {
    // Reads an iCalendar (.ics) export from Google Calendar, Outlook or Apple Calendar into
    // this app's schedules. Supported: all-day and timed events (UTC, local or TZID times),
    // multi-day spans, DESCRIPTION as the memo, daily/weekly/monthly RRULEs with UNTIL or
    // COUNT, "nth weekday" monthly rules and EXDATE. Rules the app cannot express (every 2
    // weeks, several weekdays, yearly) keep only the first date and are counted as simplified.
    public static class IcsImport {
        public sealed class Result {
            public List<CalendarEvent> Events=new List<CalendarEvent>();
            public int Simplified,Skipped,Cancelled;
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
        static TimeSpan? Duration(string v){
            if(String.IsNullOrEmpty(v))return null;var m=System.Text.RegularExpressions.Regex.Match(v.Trim(),@"^([+-])?P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$");
            if(!m.Success)return null;Func<int,int> n=i=>m.Groups[i].Success?int.Parse(m.Groups[i].Value,CultureInfo.InvariantCulture):0;
            return new TimeSpan(n(2)*7+n(3),n(4),n(5),n(6));
        }
        static void Convert(Dictionary<string,List<KeyValuePair<Dictionary<string,string>,string>>> p,string category,int? reminder,Result result){
            if(First(p,"RECURRENCE-ID")!=null){result.Skipped++;return;} // a changed copy of one repeat; the series itself is imported
            var status=First(p,"STATUS");if(status!=null&&status.Trim().Equals("CANCELLED",StringComparison.OrdinalIgnoreCase)){result.Cancelled++;return;}
            DateTime start,end;bool allDay,endDateOnly;
            if(!ParseWhen(First(p,"DTSTART"),Params(p,"DTSTART"),out start,out allDay)){result.Skipped++;return;}
            bool hasEnd=ParseWhen(First(p,"DTEND"),Params(p,"DTEND"),out end,out endDateOnly);
            if(!hasEnd){var d=Duration(First(p,"DURATION"));if(d!=null){end=start+d.Value;hasEnd=true;}}
            string title=Unescape(First(p,"SUMMARY")??"").Trim();if(title.Length==0)title="(제목 없음)";if(title.Length>200)title=title.Substring(0,200);
            string note=Unescape(First(p,"DESCRIPTION")??"").Trim();
            string location=Unescape(First(p,"LOCATION")??"").Trim();if(location.Length>0)note=("장소: "+location+(note.Length>0?"\n"+note:""));
            if(note.Length>5000)note=note.Substring(0,5000);
            var e=new CalendarEvent{id=Guid.NewGuid().ToString(),title=title,note=note,category=category,repeat="none",repeatUntil="",reminder=reminder,pinned=false,pinnedDate="",completedDates=new string[0],excludedDates=new string[0]};
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
            ApplyRule(e,First(p,"RRULE"),start,result);
            List<KeyValuePair<Dictionary<string,string>,string>> ex;
            if(e.repeat!="none"&&p.TryGetValue("EXDATE",out ex)){
                var dates=new List<string>();
                foreach(var item in ex)foreach(var v in item.Value.Split(',')){DateTime d;bool only;if(ParseWhen(v,item.Key,out d,out only))dates.Add(Rules.Key(d.Date));}
                e.excludedDates=dates.Distinct().ToArray();
            }
            result.Events.Add(e);
        }
        static void ApplyRule(CalendarEvent e,string rule,DateTime start,Result result){
            if(String.IsNullOrEmpty(rule))return;
            var parts=rule.Split(';').Select(x=>x.Split(new[]{'='},2)).Where(x=>x.Length==2).ToDictionary(x=>x[0].Trim().ToUpperInvariant(),x=>x[1].Trim(),StringComparer.OrdinalIgnoreCase);
            string freq;parts.TryGetValue("FREQ",out freq);freq=(freq??"").ToUpperInvariant();
            string interval;bool every=!parts.TryGetValue("INTERVAL",out interval)||interval=="1";
            string byDay;parts.TryGetValue("BYDAY",out byDay);
            string repeat=null;
            if(every&&freq=="DAILY")repeat="daily";
            else if(every&&freq=="WEEKLY"&&(byDay==null||(!byDay.Contains(",")&&DayIndex(byDay)==(int)start.DayOfWeek)))repeat="weekly";
            else if(every&&freq=="MONTHLY"){
                if(byDay==null){string byMonthDay;if(!parts.TryGetValue("BYMONTHDAY",out byMonthDay)||byMonthDay==start.Day.ToString(CultureInfo.InvariantCulture))repeat="monthly";}
                else{
                    // "2TU" (2nd Tuesday) or BYDAY=TU with BYSETPOS=2; -1 is the last one.
                    var m=System.Text.RegularExpressions.Regex.Match(byDay,@"^([+-]?\d)?([A-Z]{2})$");string pos;parts.TryGetValue("BYSETPOS",out pos);
                    if(m.Success){int week;string n=m.Groups[1].Success?m.Groups[1].Value:pos;int day=DayIndex(m.Groups[2].Value);
                        if(n!=null&&int.TryParse(n,out week)&&(week==-1||(week>=1&&week<=4))&&day>=0)repeat="monthly:"+week+":"+day;}
                }
            }
            if(repeat==null){result.Simplified++;return;}
            e.repeat=repeat;
            string until,count;
            if(parts.TryGetValue("UNTIL",out until)){DateTime u;bool only;if(ParseWhen(until,new Dictionary<string,string>(),out u,out only))e.repeatUntil=Rules.Key(u.Date);}
            else if(parts.TryGetValue("COUNT",out count)){int c;if(int.TryParse(count,out c)&&c>0){
                var last=repeat=="daily"?start.AddDays(c-1):repeat=="weekly"?start.AddDays(7*(c-1)):start.AddMonths(c-1);
                if(repeat.StartsWith("monthly:"))last=last.AddDays(7); // nth-weekday dates drift within the month
                e.repeatUntil=Rules.Key(last.Date);}}
            if(e.repeatUntil!=""&&String.CompareOrdinal(e.repeatUntil,e.startDate)<0)e.repeatUntil=e.startDate;
        }
        static int DayIndex(string code){return Array.IndexOf(new[]{"SU","MO","TU","WE","TH","FR","SA"},(code??"").Trim().ToUpperInvariant());}
    }
}
