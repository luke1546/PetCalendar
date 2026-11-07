import test from 'node:test';
import assert from 'node:assert/strict';
import {promises as fs} from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {defaultState,dateKey,addDays} from '../desktop/ui/model.mjs';
import {DesktopDriver,waitFor,delay,root} from './desktop-driver.mjs';
const run=promisify(execFile);
async function frame(window){const script=`Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class WidgetFrameCheck {
 [StructLayout(LayoutKind.Sequential)] public struct RECT {public int left,top,right,bottom;}
 [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h,int n);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
}
'@
$h=[IntPtr]${window};$r=New-Object WidgetFrameCheck+RECT;[WidgetFrameCheck]::GetWindowRect($h,[ref]$r)|Out-Null;
$tl=(($r.top+1) -shl 16) -bor (($r.left+1) -band 65535);$br=(($r.bottom-2) -shl 16) -bor (($r.right-2) -band 65535);
[pscustomobject]@{caption=([WidgetFrameCheck]::GetWindowLong($h,-16) -band 0xc00000);topLeft=[WidgetFrameCheck]::SendMessage($h,132,[IntPtr]::Zero,[IntPtr]$tl).ToInt64();bottomRight=[WidgetFrameCheck]::SendMessage($h,132,[IntPtr]::Zero,[IntPtr]$br).ToInt64()}|ConvertTo-Json -Compress`;
 const result=await run('powershell.exe',['-NoProfile','-Command',script],{windowsHide:true});return JSON.parse(result.stdout);}
test('위젯: 설정·동기화·정렬·편집·완료·위치복원·닫기·실행 격리',{timeout:90000},async()=>{
 const dir=await fs.mkdtemp(path.join(os.tmpdir(),'pet-calendar-widget-'));const data=path.join(dir,'data'),profile=path.join(dir,'profile');await fs.mkdir(data);
 const initial=defaultState(),today=dateKey();initial.settings.hotkey.enabled=false;
 initial.events=[['all','',''],['long','09:00','11:00'],['short','09:00','10:00']].map(([id,startTime,endTime])=>({id,title:id==='short'?'오전 제출 · 매월 셋째 금요일':id,startDate:today,endDate:today,startTime,endTime,category:'work',note:'',repeat:'none',repeatUntil:'',reminder:null,pinned:id==='short'}));
 await fs.writeFile(path.join(data,'calendar.json'),JSON.stringify(initial));let app=new DesktopDriver(data,profile);
 const inspect=async()=>(await app.command('widget')).widget;
 const click=s=>app.js(`document.querySelector(${JSON.stringify(s)}).click();true`);
 try{
  await app.ready;assert.equal((await inspect()).enabled,false);await click('#settingsButton');await waitFor(()=>app.js('document.getElementById("toggleWidget").textContent==="위젯 열기"'),'settings');await click('#toggleWidget');await waitFor(async()=>(await inspect()).visible,'shown');
  const mainLayout=(await app.command('widget',{command:'layout'})).layout;
  assert.ok(mainLayout.controls.some(c=>c.name==='settings'),'widget has its own settings gear');
  assert.equal(mainLayout.controls.some(c=>['topmost','locked','preferences'].includes(c.name)),false,'preference checkboxes are absent from main widget');
  await app.command('widget',{command:'click',name:'settings'});assert.equal((await inspect()).settingsMenuVisible,true);
  await app.command('widget',{command:'click',name:'topmost'});assert.equal((await inspect()).topmost,true);assert.equal((await inspect()).settingsMenuVisible,false);
  await app.command('widget',{command:'click',name:'settings'});await app.command('widget',{command:'click',name:'locked'});assert.equal((await inspect()).locked,true);
  await app.command('widget',{command:'click',name:'settings'});await app.command('widget',{command:'click',name:'topmost'});assert.equal((await inspect()).topmost,false);
  await app.command('widget',{command:'click',name:'settings'});await app.command('widget',{command:'click',name:'locked'});assert.equal((await inspect()).locked,false);
  assert.equal((await inspect()).showDday,true,'old widget settings default to showing D-day');
  const chosenBefore=(await app.state()).events.find(e=>e.pinned).id;
  await app.command('widget',{command:'click',name:'settings'});await app.command('widget',{command:'click',name:'show-dday'});
  assert.equal((await inspect()).showDday,false);
  assert.equal((await app.command('widget',{command:'layout'})).layout.controls.some(c=>c.name==='dday'),false,'hidden D-day card is absent from layout');
  assert.equal((await app.state()).events.find(e=>e.pinned).id,chosenBefore,'hiding widget D-day keeps selected event');
  await app.command('widget',{command:'click',name:'settings'});await app.command('widget',{command:'click',name:'show-dday'});
  assert.equal((await inspect()).showDday,true);
  assert.equal((await app.command('widget',{command:'layout'})).layout.controls.some(c=>c.name==='dday'),true,'D-day card can be shown again');
  assert.equal((await app.api('POST','/api/widget',{action:'configure',showDday:'bad'})).status,400);
  assert.deepEqual((await inspect()).rows.map(e=>e.id),['short','long','all']);await click('#widgetTopmost');await click('#widgetLocked');await waitFor(async()=>(await inspect()).topmost,'topmost');
  // Change independently after the prior request has finished.
  await waitFor(()=>app.js('!document.getElementById("toggleWidget").disabled'),'widget settled');
  await app.api('POST','/api/widget',{action:'configure',locked:true});assert.equal((await inspect()).locked,true);
  let chrome=await frame((await inspect()).window);assert.equal(chrome.caption,0,'no Windows gray caption');assert.equal(chrome.topLeft,1);assert.equal(chrome.bottomRight,1);
  await app.api('POST','/api/widget',{action:'configure',locked:false});chrome=await frame((await inspect()).window);assert.equal(chrome.topLeft,13,'resize top left');assert.equal(chrome.bottomRight,17,'resize bottom right');await app.api('POST','/api/widget',{action:'configure',locked:true});
  await app.command('hide');assert.equal((await inspect()).visible,true);const checkLayout=(await app.command('widget',{command:'layout'})).layout;
  const check=checkLayout.controls.find(c=>c.name==='check-short@'+today);assert.equal(check.parent,'short@'+today,'widget checkbox is a child of its schedule card');
  await run('powershell.exe',['-NoProfile','-Command',`Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public static class CardCheckClick{[DllImport("user32.dll")]public static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);}';[CardCheckClick]::SendMessage([IntPtr]${check.window},245,[IntPtr]::Zero,[IntPtr]::Zero)|Out-Null`],{windowsHide:true});
  assert.equal(await app.js('document.getElementById("eventDialog").open'),false,'checkbox click does not open schedule editor');await waitFor(async()=>(await app.state()).events.find(e=>e.id==='short').completedDates.includes(today),'widget completion saved');assert.equal((await inspect()).rows.at(-1).id,'short');
  await click('[data-close=settingsDialog]');await app.command('widget',{command:'click',name:'long@'+today});await waitFor(()=>app.js('document.getElementById("eventDialog").open'),'widget event edit');assert.equal(await app.js('document.getElementById("eventTitle").value'),'long');
  // A widget action must preserve the open draft and queue the new event.
  await app.js('document.getElementById("eventTitle").value="保持 draft";true');await app.command('widget',{command:'select',date:addDays(today,1)});await app.command('widget',{command:'click',name:'add'});assert.equal(await app.js('document.getElementById("eventTitle").value'),'保持 draft');await click('[data-close=eventDialog]');await waitFor(()=>app.js('document.getElementById("eventDialog").open&&document.getElementById("eventDialogTitle").textContent==="새 일정"'),'queued add');assert.equal(await app.js('document.getElementById("eventStart").value'),addDays(today,1));await click('[data-close=eventDialog]');
  let state=await app.state();state.events[1].title='전체 창에서 바꾼 일정';assert.equal((await app.save(state)).status,200);await app.command('widget',{command:'select',date:today});assert.equal((await inspect()).rows.find(e=>e.id==='long').title,'전체 창에서 바꾼 일정');
  assert.equal((await app.api('POST','/api/widget',{action:'configure',locked:'bad'})).status,400);assert.equal((await app.api('POST','/api/widget',{action:'show'},{marker:''})).status,403);
  await app.api('POST','/api/widget',{action:'configure',showDday:false});
  await app.command('widget',{command:'bounds',x:60,y:70,width:400,height:640});await delay(500);await app.command('widget',{command:'capture',path:path.join(root,'artifacts','desktop-widget.png')});await app.close();app=new DesktopDriver(data,profile);await app.ready;
  let info=await inspect();assert.equal(info.showDday,false,'hidden D-day preference is restored');assert.equal(info.visible,true);assert.equal(info.topmost,true);assert.equal(info.locked,true);assert.equal(info.width,400);assert.equal(info.height,640);assert.equal(info.x,60);assert.equal(info.y,70);
  await app.command('widget',{command:'click',name:'close'});assert.equal((await inspect()).enabled,false);assert.equal(app.child.exitCode,null);await app.close();app=new DesktopDriver(data,profile);await app.ready;assert.equal((await inspect()).visible,false);assert.deepEqual(await app.js('window.qaErrors'),[]);
  console.log('PASS widget visibility/preferences, same data, completion order, draft-safe edit/add, position restore, restart and close');
 }finally{await app.close();await delay(500);await fs.rm(dir,{recursive:true,force:true}).catch(()=>{});}
});
