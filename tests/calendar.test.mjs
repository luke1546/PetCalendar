import test from 'node:test';
import assert from 'node:assert/strict';
import { CATEGORIES, defaultState, validateState, validateHotkey, hotkeyLabel, occurrences, onDate, addDays, isDate, dday, ddayOccurrence, selectedDday, isEventDone, reminderTime } from '../desktop/ui/model.mjs';

const event = overrides => ({id:'one',title:'업무 회의',startDate:'2026-01-31',endDate:'2026-01-31',startTime:'10:00',endTime:'11:00',category:'meeting',note:'회의실',repeat:'none',repeatUntil:'',reminder:10,pinned:false,...overrides});
test('실제 달력 날짜, 윤년, 월/연도 경계를 계산한다',()=>{
  assert.equal(isDate('2024-02-29'),true);assert.equal(isDate('2026-02-29'),false);
  assert.equal(isDate('2026-04-31'),false);assert.equal(addDays('2026-12-31',1),'2027-01-01');
  assert.equal(addDays('2024-03-01',-1),'2024-02-29');
});
test('월 반복은 원래 일자를 유지하고 없는 날짜는 말일로 줄인다',()=>{
  const list=occurrences([event({repeat:'monthly'})],'2026-01-01','2026-04-30');
  assert.deepEqual(list.map(o=>o.startDate),['2026-01-31','2026-02-28','2026-03-31','2026-04-30']);
  assert.equal(occurrences([event({startDate:'2024-01-31',endDate:'2024-01-31',repeat:'monthly'})],'2024-02-01','2024-02-29')[0].startDate,'2024-02-29');
});
test('주 반복과 반복 종료일을 적용한다',()=>{
  const list=occurrences([event({repeat:'weekly',repeatUntil:'2026-02-14'})],'2026-01-01','2026-02-28');
  assert.deepEqual(list.map(o=>o.startDate),['2026-01-31','2026-02-07','2026-02-14']);
});
test('매일 반복은 시작일 전에는 나타나지 않는다',()=>{
  const list=occurrences([event({repeat:'daily',repeatUntil:'2026-02-02'})],'2026-01-29','2026-02-05');
  assert.deepEqual(list.map(o=>o.startDate),['2026-01-31','2026-02-01','2026-02-02']);
});
test('여러 날 일정은 월 경계를 넘어 모든 해당 날짜에 나타난다',()=>{
  const e=event({startDate:'2026-01-30',endDate:'2026-02-03'});
  const list=occurrences([e],'2026-02-01','2026-02-28');
  assert.equal(onDate(list,'2026-02-01').length,1);assert.equal(onDate(list,'2026-02-03').length,1);
  assert.equal(onDate(list,'2026-02-04').length,0);
});
test('기간 반복 일정의 마지막 회차는 반복 종료일 이후까지 이어질 수 있다',()=>{
  const list=occurrences([event({startDate:'2026-01-01',endDate:'2026-01-03',repeat:'weekly',repeatUntil:'2026-01-08'})],'2026-01-09','2026-01-10');
  assert.equal(onDate(list,'2026-01-10').length,1);assert.equal(list[0].startDate,'2026-01-08');
});
test('D-day는 시간차가 아닌 날짜 차이로 계산한다',()=>{
  assert.equal(dday('2026-11-05','2026-11-05'),'D-day');
  assert.equal(dday('2026-11-10','2026-11-05'),'D−5');assert.equal(dday('2026-11-01','2026-11-05'),'D+4');
});
test('기존 일정은 미완료로 불러오며 완료 기록을 검증하고 백업에 보존한다',()=>{
  assert.deepEqual(validateState({...defaultState(),events:[event({})]}).events[0].completedDates,[]);
  const state=validateState({...defaultState(),events:[event({completedDates:['2026-01-31','2026-01-31']})]});
  assert.deepEqual(state.events[0].completedDates,['2026-01-31']);
  assert.equal(isEventDone(validateState(JSON.parse(JSON.stringify(state))).events[0]),true);
  for(const completedDates of [null,true,'2026-01-31',['2026-02-30'],[null]]) {
    assert.throws(()=>validateState({...defaultState(),events:[event({completedDates})]}),/완료 기록/);
  }
});

test('D-day는 사용자가 고른 일정만 표시하고 선택하지 않으면 비어 있다',()=>{
  const e=event({id:'chosen',category:'personal',pinned:true});
  assert.equal(selectedDday([event({category:'deadline'}),e],'2026-01-01').event.id,'chosen');
  assert.equal(selectedDday([event({category:'deadline'})],'2026-01-01'),null);
  assert.equal(dday(selectedDday([e],'2026-02-01').startDate,'2026-02-01'),'D+1');
});

test('반복 일정에서 고른 회차는 날짜가 지나거나 완료해도 선택을 유지한다',()=>{
  const e=event({repeat:'monthly',pinned:true,pinnedDate:'2026-02-28',completedDates:['2026-02-28']});
  assert.equal(selectedDday([e],'2026-01-31').startDate,'2026-02-28');
  assert.equal(selectedDday([e],'2026-03-10').startDate,'2026-02-28');
  assert.equal(ddayOccurrence({...e,pinnedDate:''},'2026-02-01').startDate,'2026-02-28');
  assert.equal(ddayOccurrence({...e,pinnedDate:'2026-02-27'},'2026-02-01').startDate,'2026-02-28');
});

test('D-day 선택 날짜는 백업에 보존하고 오래된 파일에는 기본값을 적용한다',()=>{
  assert.equal(validateState({...defaultState(),events:[event({})]}).events[0].pinnedDate,'');
  const state=validateState({...defaultState(),events:[event({pinned:true,pinnedDate:'2026-01-31'})]});
  assert.equal(validateState(JSON.parse(JSON.stringify(state))).events[0].pinnedDate,'2026-01-31');
  assert.throws(()=>validateState({...defaultState(),events:[event({pinnedDate:'2026-02-30'})]}),/D-day 날짜/);
});

test('반복 일정은 완료한 회차의 알림만 제외하고 D-day 선택은 유지한다',()=>{
  const e=event({repeat:'weekly',pinned:true,pinnedDate:'2026-01-31',completedDates:['2026-01-31']});
  const list=occurrences([e],'2026-01-31','2026-02-07');
  assert.equal(isEventDone(e,'2026-01-31'),true);assert.equal(isEventDone(e,'2026-02-07'),false);
  assert.equal(reminderTime(list[0]),null);assert.notEqual(reminderTime(list[1]),null);
  assert.equal(selectedDday([e],'2026-02-01').startDate,'2026-01-31');
});
test('종일 알림은 현지 오전 9시, 알림 없음은 null이다',()=>{
  const list=occurrences([event({startTime:'',endTime:'',reminder:30})],'2026-01-31','2026-01-31');
  assert.equal(reminderTime(list[0]),new Date('2026-01-31T08:30:00').getTime());
  list[0].event.reminder=null;assert.equal(reminderTime(list[0]),null);
});
test('잘못된 백업, 중복 ID, 역전된 일시를 거부한다',()=>{
  assert.throws(()=>validateState({version:2}));
  assert.throws(()=>validateState({...defaultState(),events:[event({}),event({})]}),/중복/);
  assert.throws(()=>validateState({...defaultState(),events:[event({endDate:'2026-01-01'})]}),/종료일/);
  assert.throws(()=>validateState({...defaultState(),events:[event({endTime:'08:00'})]}),/종료 시간/);
  assert.throws(()=>validateState({...defaultState(),events:[event({title:'  '})]}),/제목/);
  assert.throws(()=>validateState({...defaultState(),events:[event({category:'__proto__'})]}),/분류/);
});
test('검증된 백업은 지정된 필드만 보존한다',()=>{
  const valid=validateState({...defaultState(),events:[event({extra:'ignored'})],extra:'ignored'});
  assert.equal(valid.events[0].extra,undefined);assert.equal(valid.extra,undefined);
  assert.equal(Object.keys(CATEGORIES).length,4);
});
test('기존 데이터의 단축키 기본값과 조합을 검증한다',()=>{
  const state=defaultState();delete state.settings.hotkey;
  assert.deepEqual(validateState(state).settings.hotkey,{enabled:true,modifiers:1,key:192});
  assert.equal(hotkeyLabel(validateHotkey()),'Alt + `');
  assert.equal(hotkeyLabel(validateHotkey({enabled:true,modifiers:7,key:75})),'Ctrl + Alt + Shift + K');
  assert.throws(()=>validateHotkey({enabled:true,modifiers:0,key:65}),/Ctrl/);
  assert.throws(()=>validateHotkey({enabled:true,modifiers:1,key:9}),/Windows/);
  assert.throws(()=>validateHotkey({enabled:true,modifiers:2,key:123}),/형식/);
  assert.equal(validateHotkey({enabled:true,modifiers:0,key:112}).key,112);
  assert.equal(hotkeyLabel(validateHotkey({enabled:true,modifiers:4,key:192})),'Shift + `');
  assert.throws(()=>validateHotkey({enabled:true,modifiers:0,key:192}),/Ctrl/);
  assert.throws(()=>validateHotkey({enabled:true,modifiers:4,key:65}),/Ctrl/);
});
