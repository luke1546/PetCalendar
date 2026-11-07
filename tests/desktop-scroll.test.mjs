import test from 'node:test';
import assert from 'node:assert/strict';
import {promises as fs} from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {defaultState,dateKey} from '../desktop/ui/model.mjs';
import {DesktopDriver,delay,root} from './desktop-driver.mjs';
const run=promisify(execFile);
async function nativeScroll(window,action,height=100){
 const script=`Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;public static class NativeScroll {
 [DllImport("user32.dll")]public static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
}
'@
$h=[IntPtr]${window};
${action==='wheel'?'[NativeScroll]::SendMessage($h,0x20a,[IntPtr](-120 -shl 16),[IntPtr]::Zero)|Out-Null;':action==='end'?'[NativeScroll]::SendMessage($h,0x100,[IntPtr]35,[IntPtr]::Zero)|Out-Null;':action==='home'?'[NativeScroll]::SendMessage($h,0x100,[IntPtr]36,[IntPtr]::Zero)|Out-Null;':`$start=[IntPtr](6 -bor (10 -shl 16));$end=[IntPtr](6 -bor (${height-8} -shl 16));[NativeScroll]::SendMessage($h,0x201,[IntPtr]1,$start)|Out-Null;[NativeScroll]::SendMessage($h,0x200,[IntPtr]1,$end)|Out-Null;[NativeScroll]::SendMessage($h,0x202,[IntPtr]::Zero,$end)|Out-Null;`}`;
 await run('powershell.exe',['-NoProfile','-NonInteractive','-Command',script],{windowsHide:true});
}
test('테마 스크롤: 메인·목록·입력창·설정·D-day 및 위젯 휠·드래그·키보드',{timeout:60000},async()=>{
 const temp=await fs.mkdtemp(path.join(os.tmpdir(),'calendar-scroll-')),data=path.join(temp,'data');await fs.mkdir(data);const state=defaultState(),today=dateKey();state.settings.hotkey.enabled=false;
 state.events=Array.from({length:80},(_,i)=>({id:'scroll-'+i,title:'테마 스크롤 확인 '+String(i).padStart(2,'0'),startDate:today,endDate:today,startTime:'09:00',endTime:'10:00',category:['work','meeting','deadline','personal'][i%4],note:'준비할 내용입니다.',repeat:'none',repeatUntil:'',reminder:null,pinned:false,completedDates:[]}));
 state.tasks=Array.from({length:80},(_,i)=>({id:'task-'+i,title:'할 일 목록 '+i,done:false,dueDate:today,createdAt:new Date().toISOString()}));
 await fs.writeFile(path.join(data,'calendar.json'),JSON.stringify(state));const app=new DesktopDriver(data,path.join(temp,'profile'));const checks=[];
 const layout=async()=>(await app.command('widget',{command:'layout'})).layout;
 const click=id=>app.js(`document.getElementById(${JSON.stringify(id)}).click();true`);
 async function webScroll(id){
  const info=await app.js(`(()=>{const e=document.getElementById('${id}'),thumb=getComputedStyle(e,'::-webkit-scrollbar-thumb'),track=getComputedStyle(e,'::-webkit-scrollbar');e.scrollTop=0;const r=e.getBoundingClientRect();return {range:e.scrollHeight-e.clientHeight,color:thumb.backgroundColor,radius:thumb.borderTopLeftRadius,width:track.width,x:r.left+r.width/2,y:r.top+Math.min(80,r.height/2)};})()`);
  assert.ok(info.range>0,`${id} scrolls`);assert.equal(info.color,'rgb(207, 185, 154)');assert.equal(info.radius,'999px');assert.equal(info.width,'12px');
  await app.command('devtools',{method:'Input.dispatchMouseEvent',parameters:{type:'mouseWheel',x:info.x,y:info.y,deltaX:0,deltaY:180}});await delay(150);assert.ok(await app.js(`document.getElementById('${id}').scrollTop>0`),`${id} wheel works`);checks.push(id);
 }
 try{
  await app.ready;await app.command('devtools',{method:'Emulation.setDeviceMetricsOverride',parameters:{width:1280,height:620,deviceScaleFactor:1,mobile:false}});await delay(150);
  await webScroll('dayEvents');await webScroll('tasksList');await app.command('capture',{path:path.join(root,'artifacts/theme-calendar-scroll.png')});
  await click('settingsButton');await webScroll('settingsDialog');await app.command('capture',{path:path.join(root,'artifacts/theme-settings-scroll.png')});await app.js('document.getElementById("settingsDialog").close();true');
  await click('deadlineCard');await webScroll('ddayChoices');await app.js('document.getElementById("ddayDialog").close();true');
  await click('addEvent');await webScroll('eventDialog');await app.js('document.getElementById("eventDialog").close();true');
  await app.command('devtools',{method:'Emulation.clearDeviceMetricsOverride'});
  await app.command('hide');await app.api('POST','/api/widget',{action:'show'});await app.command('widget',{command:'bounds',x:80,y:80,width:380,height:590});let info=await layout();assert.ok(info.scrollMaximum>0);assert.ok(info.controls.some(c=>c.name==='agenda-scrollbar'));
  await nativeScroll(info.agendaWindow,'wheel');info=await layout();assert.ok(info.scrollOffset>0,'native wheel');
  await nativeScroll(info.scrollbarWindow,'end');info=await layout();assert.equal(info.scrollOffset,info.scrollMaximum,'End reaches last item');
  await nativeScroll(info.scrollbarWindow,'home');assert.equal((await layout()).scrollOffset,0);
  const bar=info.controls.find(c=>c.name==='agenda-scrollbar');await nativeScroll(info.scrollbarWindow,'drag',bar.height);info=await layout();assert.equal(info.scrollOffset,info.scrollMaximum,'drag thumb reaches bottom');
  await nativeScroll(info.scrollbarWindow,'home');await run('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-File',path.join(root,'tests/capture-native-window.ps1'),'-WindowHandle',String((await app.command('widget')).widget.window),'-OutputPath',path.join(root,'artifacts/theme-widget-scroll.png')],{windowsHide:true});
  const next=await app.state();next.events=[];assert.equal((await app.save(next)).status,200);info=await layout();assert.equal(info.scrollMaximum,0);assert.equal(info.scrollOffset,0);assert.ok(!info.controls.some(c=>c.name==='agenda-scrollbar'),'scrollbar hides for empty agenda');
  assert.deepEqual(await app.js('window.qaErrors'),[]);await fs.writeFile(path.join(root,'artifacts/theme-scroll-results.json'),JSON.stringify({web:checks,native:['wheel','End','Home','thumb drag','empty reset'],passed:true},null,2));
 }finally{await app.close();await delay(700);await fs.rm(temp,{recursive:true,force:true,maxRetries:5,retryDelay:300});}
});
