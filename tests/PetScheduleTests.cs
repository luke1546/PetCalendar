using System;
using PetCalendarDesktop;
static class PetScheduleTests {
    static int checks;
    static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);checks++;}
    static CalendarEvent Event(string id,string date,string time,string end="",string repeat="none"){return new CalendarEvent{id=id,title=id,startDate=date,endDate=date,startTime=time,endTime=end,repeat=repeat,repeatUntil="",category="work",note=""};}
    static void Main(){
        var now=new DateTime(2026,11,7,10,0,0);string today=Rules.Key(now);
        var state=new CalendarState();Check(PetSchedule.Build(state,now).Text.Contains("등록된 일정이 없어요"),"empty day");
        CalendarEvent past=Event("past",today,"09:00"),next=Event("next",today,"11:00"),day=Event("day",today,"");state.events=new[]{past,next,day};
        var summary=PetSchedule.Build(state,now);Check(summary.Text.Contains("3개")&&summary.EventId=="next"&&summary.Text.Contains("11:00"),"unfinished count and next timed event");
        var second=PetSchedule.Build(state,now,summary.EventId,summary.StartDate);Check(second.EventId=="day","click advances to next unfinished event");
        var third=PetSchedule.Build(state,now,second.EventId,second.StartDate);Check(third.EventId=="past","last event wraps to first");
        day.completedDates=new[]{today};Check(PetSchedule.Build(state,now,summary.EventId,summary.StartDate).EventId=="past","cycle excludes completed events");day.completedDates=new string[0];
        var current=Event("current",today,"09:30","10:30");state.events=new[]{past,next,current};summary=PetSchedule.Build(state,now);Check(summary.EventId=="current"&&summary.Text.Contains("진행 중"),"ongoing event has priority");
        current.completedDates=new[]{today};Check(PetSchedule.Build(state,now).EventId=="next","completed occurrence excluded");
        state.events=new[]{past,day};Check(PetSchedule.Build(state,now).EventId=="day","all-day event when no future timed event");
        state.events=new[]{past};Check(PetSchedule.Build(state,now).Text.Contains("시간이 지난"),"past unfinished event is not called next");
        past.completedDates=new[]{today};Check(PetSchedule.Build(state,now).Text.Contains("모두 마쳤어요"),"all completed");
        var daily=Event("daily",Rules.AddDays(today,-1),"11:00",repeat:"daily");state.events=new[]{daily};summary=PetSchedule.Build(state,now);Check(summary.StartDate==today&&summary.EventId=="daily","recurring occurrence uses today start date");
        daily.completedDates=new[]{today};Check(PetSchedule.Build(state,now).Text.Contains("모두 마쳤어요"),"recurring completion uses occurrence date");
        var span=Event("span",Rules.AddDays(today,-1),"09:00");span.endDate=Rules.AddDays(today,1);state.events=new[]{span};summary=PetSchedule.Build(state,now);Check(summary.Text.Contains("진행 중")&&summary.StartDate==span.startDate,"multi-day event remains ongoing");
        span.completedDates=new[]{span.startDate};Check(PetSchedule.Build(state,now).Text.Contains("모두 마쳤어요"),"multi-day completion uses original occurrence start");
        state.events=new[]{next};next.title=new string('가',200);Check(PetSchedule.Build(state,now).Text.Length<160,"long titles are bounded");
        next.title="회의 <태그> & 확인";Check(PetSchedule.Build(state,now).Text.Contains(next.title),"native speech keeps literal title characters");
        Check(PetSchedule.Build(state,now.AddDays(1)).Text.Contains("등록된 일정이 없어요"),"next day does not reuse old daily summary");
        var utc=new DateTime(2026,11,7,1,0,0,DateTimeKind.Utc);var clock=new PetSpeechClock();clock.Reset(utc,30);
        Check(clock.NextUtc==utc.AddMinutes(30),"default due time");Check(!clock.Due(utc.AddMinutes(29),30),"no early announcement");Check(clock.Due(utc.AddMinutes(30),30),"due announcement");Check(!clock.Due(utc.AddMinutes(30),30),"same tick does not duplicate");
        Check(clock.Due(utc.AddHours(3),30)&&clock.NextUtc==utc.AddHours(3).AddMinutes(30),"missed periods coalesce into one due check");
        clock.Reset(utc.AddHours(10),30);Check(!clock.Due(utc.AddHours(10),30),"resume reset has no catch-up burst");
        Check(!clock.Due(utc,30)&&clock.NextUtc==utc.AddMinutes(30),"backward clock change rearms schedule");
        clock.Reset(utc,1);Check(clock.Due(utc.AddMinutes(1),1),"custom one minute interval");
        clock.Reset(utc,1440);Check(clock.NextUtc==utc.AddDays(1),"maximum day-long interval");
        Console.WriteLine(checks+" schedule and clock checks passed");
    }
}
