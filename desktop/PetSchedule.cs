using System;
using System.Linq;

namespace PetCalendarDesktop {
    internal sealed class PetSummary {
        internal string Text,EventId,StartDate,Kind,Title,Time;
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
