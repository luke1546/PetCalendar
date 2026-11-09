using System;
using System.Linq;

namespace PetCalendarDesktop {
    internal sealed class PetSummary {
        internal string Text,EventId,StartDate,Kind,Title,Time,Key;
        internal int Count;
    }
    internal static class PetSchedule {
        internal static int CompletionReaction(CalendarState before,CalendarState after,DateTime local) {
            string today=Rules.Key(local);
            var previous=Rules.Occurrences(before.events,today,today);
            var current=Rules.Occurrences(after.events,today,today);
            bool completed=current.Any(o=>Rules.Done(o.@event,o.startDate)&&previous.Any(old=>old.@event.id==o.@event.id&&old.startDate==o.startDate&&!Rules.Done(old.@event,old.startDate)));
            return !completed?0:current.All(o=>Rules.Done(o.@event,o.startDate))?2:1;
        }
        static string Title(string value) {return value.Length>90?value.Substring(0,89)+"…":value;}
        static DateTime Start(Occurrence o) {return Rules.Date(o.startDate).Add(TimeSpan.Parse(o.@event.startTime));}
        static DateTime End(Occurrence o) {
            if(!String.IsNullOrEmpty(o.@event.endTime))return Rules.Date(o.endDate).Add(TimeSpan.Parse(o.@event.endTime));
            return o.endDate!=o.startDate?Rules.Date(o.endDate).AddDays(1):Start(o);
        }
        internal static PetSummary Build(CalendarState state,DateTime local,string afterId=null,string afterDate=null) {
            string today=Rules.Key(local);
            var all=Rules.Occurrences(state.events,today,today);
            var remaining=all.Where(o=>!Rules.Done(o.@event,o.startDate)).ToArray();
            if(all.Count==0)return new PetSummary{Kind="empty",Text="오늘은 등록된 일정이 없어요. 나랑 잠깐 쉬어 가요!"};
            if(remaining.Length==0)return new PetSummary{Kind="done",Text="오늘 일정을 모두 마쳤어요. 정말 수고했어요!"};
            var timed=remaining.Where(o=>!String.IsNullOrEmpty(o.@event.startTime)).ToArray();
            var current=timed.Where(o=>Start(o)<=local&&End(o)>local).OrderBy(Start).FirstOrDefault();
            var next=timed.Where(o=>Start(o)>=local).OrderBy(Start).FirstOrDefault();
            var day=remaining.FirstOrDefault(o=>String.IsNullOrEmpty(o.@event.startTime));
            var selected=current??next??day??remaining[0];
            if(afterId!=null) {
                var ordered=remaining.OrderBy(o=>String.IsNullOrEmpty(o.@event.startTime)?1:0).ThenBy(o=>o.startDate,StringComparer.Ordinal).ThenBy(o=>o.@event.startTime,StringComparer.Ordinal).ThenBy(o=>o.@event.id,StringComparer.Ordinal).ToArray();
                int index=Array.FindIndex(ordered,o=>o.@event.id==afterId&&o.startDate==afterDate);
                if(index>=0)selected=ordered[(index+1)%ordered.Length];
            }
            bool isDay=String.IsNullOrEmpty(selected.@event.startTime);
            bool isCurrent=!isDay&&Start(selected)<=local&&End(selected)>local;
            bool isNext=!isDay&&Start(selected)>=local;
            string lead="오늘 할 일이 "+remaining.Length+"개 남았어요!\n";
            string detail=isCurrent?"지금 ‘"+Title(selected.@event.title)+"’ 진행 중이에요. 잘하고 있죠?":isNext?"다음은 "+selected.@event.startTime+" ‘"+Title(selected.@event.title)+"’예요. 잊지 말아요!":isDay?"종일 일정 ‘"+Title(selected.@event.title)+"’도 잊지 말아요!":"시간이 지난 ‘"+Title(selected.@event.title)+"’도 한 번 살펴봐요!";
            return new PetSummary{Kind=isCurrent?"current":isNext?"next":isDay?"day":"past",Title=Title(selected.@event.title),Time=selected.@event.startTime,Count=remaining.Length,Text=lead+detail,EventId=selected.@event.id,StartDate=selected.startDate};
        }
    }
    // One-off pet notices, each shown once (by Key): a morning briefing, a reminder the day
    // before a deadline, and a Sunday evening review of the week with the completion streak.
    internal static class PetNotices {
        static string Short(string value){return value.Length>40?value.Substring(0,39)+"…":value;}
        static System.Collections.Generic.List<Occurrence> On(CalendarState state,string day){return Rules.Occurrences(state.events,day,day).Where(o=>String.CompareOrdinal(o.startDate,day)<=0&&String.CompareOrdinal(o.endDate,day)>=0).ToList();}
        internal static PetSummary Next(CalendarState state,DateTime local,Func<string,bool> seen){
            string today=Rules.Key(local);
            if(local.DayOfWeek==DayOfWeek.Sunday&&local.Hour>=18){string key="week:"+today;if(!seen(key))return new PetSummary{Kind="notice",Key=key,Text=Weekly(state,today)};}
            if(local.Hour>=9){
                string tomorrow=Rules.AddDays(today,1);
                foreach(var o in Rules.Occurrences(state.events,tomorrow,tomorrow).Where(x=>x.startDate==tomorrow&&x.@event.category=="deadline"&&!Rules.Done(x.@event,x.startDate))){
                    string key="eve:"+o.@event.id+":"+o.startDate;if(seen(key))continue;
                    return new PetSummary{Kind="notice",Key=key,Text="내일 마감이에요! ‘"+Short(o.@event.title)+"’"+(o.@event.startTime!=""?" ("+o.@event.startTime+")":"")+" 📌\n오늘 조금 미리 챙겨둘까요?"};
                }
            }
            if(local.Hour>=5&&local.Hour<12){string key="morning:"+today;if(!seen(key))return new PetSummary{Kind="notice",Key=key,Text=Morning(state,local)};}
            return null;
        }
        internal static string Morning(CalendarState state,DateTime local){
            string today=Rules.Key(local);var all=On(state,today);
            if(all.Count==0)return "좋은 아침! 오늘은 일정이 없어요.\n여유롭게 보내요 ☕";
            int deadlines=all.Count(o=>o.@event.category=="deadline");
            var first=all.Where(o=>o.@event.startTime!="").OrderBy(o=>o.@event.startTime,StringComparer.Ordinal).FirstOrDefault();
            string head="좋은 아침! 오늘은 일정 "+all.Count+"개"+(deadlines>0?", 그중 마감 "+deadlines+"개!":"!");
            return head+"\n"+(first!=null?"첫 일정은 "+first.@event.startTime+" ‘"+Short(first.@event.title)+"’예요 ☀️":"하나씩 같이 해봐요 ☀️");
        }
        internal static string Weekly(CalendarState state,string today){
            string from=Rules.AddDays(today,-6);
            var week=Rules.Occurrences(state.events,from,today).Where(o=>String.CompareOrdinal(o.startDate,from)>=0).ToList();
            int done=week.Count(o=>Rules.Done(o.@event,o.startDate));int streak=Streak(state,today);
            if(week.Count==0)return "이번 주는 등록된 일정이 없었어요.\n다음 주도 함께해요! 🌱";
            string text="이번 주 정리! 일정 "+week.Count+"개 중 "+done+"개 완료 "+(done==week.Count?"🎉":"👏");
            if(streak>=2)text+="\n"+streak+"일 연속 모두 해냈어요 🔥";
            else if(done<week.Count)text+="\n다음 주도 차근차근 해봐요!";
            return text;
        }
        // Days in a row (ending today) whose schedules were all completed. Days without
        // schedules are skipped, and an unfinished today does not break the run yet.
        internal static int Streak(CalendarState state,string today){
            int streak=0;string day=today;
            for(int i=0;i<120;i++,day=Rules.AddDays(day,-1)){
                var list=On(state,day);if(list.Count==0)continue;
                if(list.All(o=>Rules.Done(o.@event,o.startDate)))streak++;
                else if(day!=today)break;
            }
            return streak;
        }
    }
    // Wall-clock scheduling is explicit so sleep/resume and interval changes are testable.
    internal sealed class PetSpeechClock {
        internal DateTime NextUtc {get;private set;}
        DateTime observed;
        internal void Reset(DateTime utc,int minutes) {observed=utc;NextUtc=utc.AddMinutes(minutes);}
        internal bool Due(DateTime utc,int minutes) {
            if(utc<observed){Reset(utc,minutes);return false;}
            observed=utc;
            if(utc<NextUtc)return false;
            Reset(utc,minutes);return true;
        }
    }
}
