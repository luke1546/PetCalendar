import test from 'node:test';
import assert from 'node:assert/strict';
import {defaultState,validateState,occurrences,onDate,ddayOccurrence,reminderTime} from '../desktop/ui/model.mjs';
const event=(id,extra={})=>({id,title:id,startDate:'2026-01-01',endDate:'2026-01-01',startTime:'',endTime:'',category:'work',note:'',repeat:'none',repeatUntil:'',reminder:10,pinned:false,...extra});
test('날짜 목록은 미완료→시간→종료시간→종일→완료 순서이며 원본을 변경하지 않는다',()=>{
 const events=[event('all'),event('late',{startTime:'14:00'}),event('done',{startTime:'07:00',completedDates:['2026-01-01']}),event('long',{startTime:'09:00',endTime:'11:00'}),event('short',{startTime:'09:00',endTime:'10:00'}),event('unknown',{startTime:'09:00'})];
 const list=occurrences(events,'2026-01-01','2026-01-01'),before=JSON.stringify(list);
 assert.deepEqual(onDate(list,'2026-01-01').map(o=>o.event.id),['short','long','unknown','late','all','done']);assert.equal(JSON.stringify(list),before);
 events[2].completedDates=[];assert.equal(onDate(list,'2026-01-01')[0].event.id,'done');
});
test('매월 셋째 금요일은 매달 날짜가 바뀌며 종료일·완료·D-day·알림을 지원한다',()=>{
 const e=event('submit',{repeat:'monthly:3:5',repeatUntil:'2026-04-17',startTime:'17:00',pinned:true});
 assert.equal(validateState({...defaultState(),events:[e]}).events[0].repeat,'monthly:3:5');
 const list=occurrences([e],'2026-01-01','2026-06-30');assert.deepEqual(list.map(o=>o.startDate),['2026-01-16','2026-02-20','2026-03-20','2026-04-17']);
 assert.equal(ddayOccurrence(e,'2026-01-17').startDate,'2026-02-20');
 assert.equal(reminderTime(list[1]),new Date('2026-02-20T16:50:00').getTime());e.completedDates=['2026-02-20'];assert.equal(reminderTime(list[1]),null);assert.notEqual(reminderTime(list[2]),null);
 e.pinnedDate='2026-02-20';assert.equal(ddayOccurrence(e,'2026-04-01').startDate,'2026-02-20');
});
test('마지막 요일·윤년·시작일 이후 적용 및 잘못된 규칙 거부',()=>{
 const e=event('last',{startDate:'2024-01-29',endDate:'2024-01-29',repeat:'monthly:-1:5'});
 assert.deepEqual(occurrences([e],'2024-01-01','2024-03-31').map(o=>o.startDate),['2024-02-23','2024-03-29']);
 for(const repeat of ['monthly:0:5','monthly:3:7','monthly:5:5','monthly:3:x'])assert.throws(()=>validateState({...defaultState(),events:[{...e,repeat}]}));
});
