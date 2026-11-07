import test from 'node:test';
import assert from 'node:assert/strict';
import {promises as fs} from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {defaultState,dateKey,addDays,occurrences} from '../desktop/ui/model.mjs';
import {DesktopDriver,waitFor} from './desktop-driver.mjs';
test('반복 회차 삭제 및 복구와 일정 초안', {timeout:60000}, async()=>{
 const temp=await fs.mkdtemp(path.join(os.tmpdir(),'pet-calendar-features-')),data=path.join(temp,'data'),profile=path.join(temp,'profile');await fs.mkdir(data);
 const today=dateKey(),start=addDays(today,-7);const initial=defaultState();initial.settings.hotkey.enabled=false;initial.events=[{id:'series',title:'반복 회의',startDate:start,endDate:start,startTime:'',endTime:'',category:'work',note:'',repeat:'weekly',repeatUntil:'',reminder:null,pinned:false}];await fs.writeFile(path.join(data,'calendar.json'),JSON.stringify(initial));
 const app=new DesktopDriver(data,profile);const click=s=>app.js(`document.querySelector(${JSON.stringify(s)}).click();true`);
 try{await app.ready;await click('[data-delete-event="series"]');await click('#repeatScopeOne');await waitFor(async()=>(await app.state()).events[0].excludedDates.includes(today),'occurrence excluded');assert.equal(occurrences((await app.state()).events,today,today).length,0);await click('.toast .plain-button');await waitFor(async()=>!(await app.state()).events[0].excludedDates.includes(today),'undo');
 await click('[data-event="series"]');await app.js(`document.querySelector('#eventTitle').value='이번 회의';true`);await click('#saveEvent');await click('#repeatScopeOne');await waitFor(async()=>(await app.state()).events.length===2,'occurrence edited');const updated=await app.state();assert.deepEqual(occurrences(updated.events,today,today).map(o=>o.event.title),['이번 회의']);assert.deepEqual(occurrences(updated.events,addDays(today,7),addDays(today,7)).map(o=>o.event.title),['반복 회의']);
 await click('#addEvent');await app.js(`document.querySelector('#eventTitle').value='임시 일정';document.querySelector('#eventTitle').dispatchEvent(new Event('input',{bubbles:true}));true`);await click('[data-close="eventDialog"]');await click('#addEvent');assert.equal(await app.js(`document.querySelector('#eventTitle').value`),'임시 일정');
 }finally{await app.close();await fs.rm(temp,{recursive:true,force:true,maxRetries:10,retryDelay:500});}
});
test('통합 백업은 일정과 펫·위젯 설정을 복원하고 오래된 리비전은 거부', {timeout:60000}, async()=>{
 const temp=await fs.mkdtemp(path.join(os.tmpdir(),'pet-calendar-backup-')),data=path.join(temp,'data'),profile=path.join(temp,'profile');await fs.mkdir(data);const initial=defaultState();initial.settings.hotkey.enabled=false;await fs.writeFile(path.join(data,'calendar.json'),JSON.stringify(initial));
 const app=new DesktopDriver(data,profile);try{await app.ready;await app.api('POST','/api/widget',{action:'configure',topmost:true,locked:true,showDday:false});const backup=(await app.api('GET','/api/backup')).body;assert.equal(backup.widget.locked,true);assert.equal(backup.widget.showDday,false);assert.ok(Array.isArray(backup.pets.pets));await app.api('POST','/api/widget',{action:'configure',topmost:false,locked:false,showDday:true});const result=await app.api('POST','/api/backup',{...backup,expectedRevision:(await app.state()).revision});assert.equal(result.status,200);const restored=(await app.api('GET','/api/backup')).body;assert.equal(restored.widget.locked,true);assert.equal(restored.widget.showDday,false);const stale=await app.api('POST','/api/backup',{...backup,expectedRevision:0});assert.equal(stale.status,409);
 }finally{await app.close();await fs.rm(temp,{recursive:true,force:true,maxRetries:10,retryDelay:500});}
});
test('미룬 알림은 재시작 후에도 예정 시각에 다시 도착', {timeout:60000}, async()=>{
 const temp=await fs.mkdtemp(path.join(os.tmpdir(),'pet-calendar-snooze-')),data=path.join(temp,'data'),profile=path.join(temp,'profile');await fs.mkdir(data);const initial=defaultState();initial.settings.hotkey.enabled=false;await fs.writeFile(path.join(data,'calendar.json'),JSON.stringify(initial));let app=new DesktopDriver(data,profile);
 try{await app.ready;await app.api('POST','/api/desktop/notification-test');const alarm=(await app.command('inspect')).pending[0];await app.command('alarm-snooze',{alarmId:alarm.id,minutes:5});assert.equal((await app.command('inspect')).pending.length,0);await app.close();app=new DesktopDriver(data,profile);await app.ready;assert.equal((await app.command('inspect')).pending.length,0);await app.command('alarm-check',{now:new Date(Date.now()+6*60000).toISOString()});await waitFor(async()=>(await app.command('inspect')).pending.some(a=>a.id===alarm.id),'snooze delivery');
 }finally{await app.close();await fs.rm(temp,{recursive:true,force:true,maxRetries:10,retryDelay:500});}
});
