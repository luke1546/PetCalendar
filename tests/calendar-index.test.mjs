import test from 'node:test';
import assert from 'node:assert/strict';
import {occurrences,indexDates,onDate,addDays,occursOn,dayNumber} from '../desktop/ui/model.mjs';
function reference(events,from,to){const list=[];for(const event of events){const span=dayNumber(event.endDate)-dayNumber(event.startDate);for(let key=addDays(from,-span)<event.startDate?event.startDate:addDays(from,-span);key<=to&&(event.repeat!=='none'||key<=event.startDate);key=addDays(key,1))if(occursOn(event,key))list.push({event,key:`${event.id}@${key}`,startDate:key,endDate:addDays(key,span)});}return list.sort((a,b)=>a.startDate.localeCompare(b.startDate)||a.event.startTime.localeCompare(b.event.startTime)||a.event.title.localeCompare(b.event.title,'ko'));}
test('월 이동 계산은 윤년·말일·요일 반복·긴 기간·완료 정렬을 보존한다',()=>{
 const rules=['none','daily','weekly','monthly',...[-1,1,2,3,4].flatMap(w=>Array.from({length:7},(_,d)=>`monthly:${w}:${d}`))];
 const events=Array.from({length:240},(_,i)=>{const start=addDays('2023-01-01',(i*173)%1200);return {id:`i-${i}`,title:['한글','나무','Alpha','9','10'][i%5],startDate:start,endDate:addDays(start,i%4===0?70:i%8),startTime:i%3?'09:00':'',endTime:i%2?'10:00':'',repeat:rules[i%rules.length],repeatUntil:i%3?'':addDays(start,300),completedDates:[addDays(start,7)]};});
 for(const from of ['2023-02-01','2024-02-01','2024-12-15','2025-06-01','2026-11-01']){const to=addDays(from,41),expected=reference(events,from,to);assert.deepEqual(occurrences(events,from,to),expected);const index=indexDates(events,from,to);assert.equal(index.size,42);for(const [key,rows]of index)assert.deepEqual(rows,onDate(expected,key),key);}
});
