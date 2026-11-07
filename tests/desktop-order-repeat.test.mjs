import test from 'node:test';
import assert from 'node:assert/strict';
import {promises as fs} from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import {defaultState,dateKey,occurrences,reminderTime} from '../desktop/ui/model.mjs';
import {DesktopDriver,waitFor,delay} from './desktop-driver.mjs';
test('데스크톱 정렬·완료 이동 및 순번 요일 반복 추가·수정·복제·재실행',{timeout:60000},async()=>{
 const dir=await fs.mkdtemp(path.join(os.tmpdir(),'pet-calendar-order-'));const data=path.join(dir,'data');await fs.mkdir(data);const today=dateKey();
 const initial=defaultState();initial.settings.hotkey.enabled=false;
 initial.events=[['all','',''],['late','14:00','15:00'],['long','09:00','11:00'],['short','09:00','10:00']].map(([id,startTime,endTime])=>({id,title:id,startDate:today,endDate:today,startTime,endTime,category:'work',note:'',repeat:'none',repeatUntil:'',reminder:null,pinned:false}));await fs.writeFile(path.join(data,'calendar.json'),JSON.stringify(initial));
 let app=new DesktopDriver(data,path.join(dir,'profile'));
 const click=s=>app.js(`document.querySelector(${JSON.stringify(s)}).click();true`);
 const order=()=>app.js('Array.from(document.querySelectorAll("#dayEvents [data-event]")).map(e=>e.dataset.event)');
 try{
  await app.ready;assert.deepEqual(await order(),['short','long','late','all']);await click('#dayEvents [data-event-check=short]');await waitFor(async()=>(await order()).at(-1)==='short','done last');assert.deepEqual(await order(),['long','late','all','short']);await click('#dayEvents [data-event-check=short]');await waitFor(async()=>(await order())[0]==='short','undo order');
  await click('#addEvent');await app.js('document.getElementById("eventTitle").value="매월 제출";document.getElementById("eventRepeat").value="monthly-weekday";document.getElementById("eventRepeat").dispatchEvent(new Event("change"));document.getElementById("eventRepeatWeek").value="3";document.getElementById("eventRepeatWeekday").value="5";document.getElementById("eventPinned").checked=true;true');assert.equal(await app.js('document.getElementById("monthlyWeekdayFields").hidden'),false);await click('#saveEvent');await waitFor(()=>app.js('!document.getElementById("eventDialog").open'),'saved');
  let state=await app.state(),e=state.events.find(e=>e.title==='매월 제출');assert.equal(e.repeat,'monthly:3:5');const id=e.id;
  await click('#deadlineCard');await click('#openDdayEvent');assert.equal(await app.js('document.getElementById("eventRepeatWeek").value'),'3');assert.equal(await app.js('document.getElementById("eventRepeatWeekday").value'),'5');await click('#duplicateEvent');await click('#saveEvent');await waitFor(()=>app.js('!document.getElementById("eventDialog").open'),'copied');assert.equal((await app.state()).events.find(e=>e.title.includes('복사')).repeat,'monthly:3:5');
  await click(`#dayEvents [data-event="${id}"]`);await app.js('document.getElementById("eventRepeatWeek").value="-1";true');await click('#saveEvent');await waitFor(()=>app.js('!document.getElementById("eventDialog").open'),'edited');assert.equal((await app.state()).events.find(e=>e.id===id).repeat,'monthly:-1:5');
  await app.close();app=new DesktopDriver(data,path.join(dir,'profile'));await app.ready;assert.equal((await app.state()).events.find(e=>e.id===id).repeat,'monthly:-1:5');assert.deepEqual(await app.js('window.qaErrors'),[]);
 }finally{await app.close();await delay(500);await fs.rm(dir,{recursive:true,force:true}).catch(()=>{});}
});
