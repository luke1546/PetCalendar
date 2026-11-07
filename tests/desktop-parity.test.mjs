import test from 'node:test';
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createInterface} from 'node:readline';
import {executable} from './desktop-driver.mjs';
import {defaultState,validateState,occurrences,dday,selectedDday,reminderTime,addDays} from '../desktop/ui/model.mjs';
const event=(id,extra={})=>({id,title:'업무 일정 '+id,startDate:'2026-01-31',endDate:'2026-01-31',startTime:'10:00',endTime:'11:00',category:'meeting',note:'회의실',repeat:'none',repeatUntil:'',reminder:10,pinned:false,pinnedDate:'',completedDates:[],excludedDates:[],...extra});
test('기존 JavaScript와 C# 저장·반복·D-day·알림 계산 결과가 같다',async()=>{
  const child=spawn(executable,['--model-test'],{windowsHide:true,stdio:['pipe','pipe','pipe']});
  const queue=[];const reader=createInterface({input:child.stdout});let diagnostic='';child.stderr.on('data',c=>diagnostic+=c);
  reader.on('line',line=>{const waiting=queue.shift();if(waiting){clearTimeout(waiting.timer);waiting.resolve(JSON.parse(line));}});
  const call=data=>new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(new Error('Parity timeout '+diagnostic)),5000);queue.push({resolve,reject,timer});child.stdin.write(JSON.stringify(data)+'\n');});
  let checked=0;
  try{
    for(const repeat of ['none','daily','weekly','monthly','monthly:3:5','monthly:1:0','monthly:4:6','monthly:-1:5'])for(const day of ['2024-01-31','2024-02-29','2026-01-31','2026-12-31'])for(const span of [0,2,35]){
      const e=event('repeat',{startDate:day,endDate:addDays(day,span),repeat,repeatUntil:repeat==='none'?'':addDays(day,100)});
      const from=addDays(day,15),to=addDays(day,120);
      assert.deepEqual((await call({action:'occurrences',events:[e],from,to})).result,occurrences([e],from,to));checked++;
    }
    for(const repeat of ['none','weekly','monthly','monthly:3:5','monthly:-1:5'])for(const pinnedDate of ['', '2026-02-28']){
      const state={...defaultState(),events:[event('one',{repeat,pinned:true,pinnedDate,completedDates:['2026-01-31']})]};
      assert.deepEqual((await call({action:'validate',state})).result,validateState(state));checked++;
      for(const today of ['2025-12-01','2026-02-01','2026-04-30']){
        const expected=selectedDday(state.events,today),actual=(await call({action:'selected-dday',events:state.events,today})).result;
        assert.equal(actual.startDate,expected.startDate);assert.equal(actual.endDate,expected.endDate);checked++;
        assert.equal((await call({action:'dday',date:actual.startDate,today})).result,dday(expected.startDate,today));checked++;
      }
    }
    for(const startTime of ['', '00:15','09:00','23:59'])for(const reminder of [null,0,10,30,60]){
      const e=event('alarm',{startTime,endTime:'',reminder});const o=occurrences([e],e.startDate,e.startDate)[0];
      assert.equal((await call({action:'reminder',occurrence:o})).result,reminderTime(o));checked++;
    }
    const invalid=[{version:2},{...defaultState(),events:[event('same'),event('same')]},{...defaultState(),events:[event('bad',{endDate:'2025-01-01'})]},{...defaultState(),events:[event('bad',{startTime:'24:00'})]},{...defaultState(),events:[event('bad',{completedDates:['2026-02-30']})]}];
    for(const state of invalid){assert.throws(()=>validateState(state));assert.equal((await call({action:'validate',state})).ok,false);checked++;}
    for(const hotkey of [{enabled:true,modifiers:4,key:192},{enabled:true,modifiers:7,key:75},{enabled:false,modifiers:0,key:9}]){
      const state=defaultState();state.settings.hotkey=hotkey;assert.deepEqual((await call({action:'validate',state})).result,validateState(state));checked++;
    }
    console.log(JSON.stringify({parityCases:checked}));
  }finally{child.stdin.end();reader.close();}
});
