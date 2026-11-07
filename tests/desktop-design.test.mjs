import test from 'node:test';
import assert from 'node:assert/strict';
import {promises as fs} from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {defaultState,dateKey} from '../desktop/ui/model.mjs';
import {DesktopDriver,delay,waitFor,root} from './desktop-driver.mjs';
const run=promisify(execFile);
// Exercise WM_DPICHANGED in the isolated test window without changing the user's
// Windows display settings. Real mixed-monitor movement remains a manual check.
async function changeDpi(window,dpi,width=380,height=590){
 const script=`Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class DesignDpi {
 [StructLayout(LayoutKind.Sequential)] public struct RECT {public int left,top,right,bottom;}
 [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,ref RECT r);
}
'@
$r=New-Object DesignDpi+RECT;$r.left=40;$r.top=40;$r.right=40+[int](${width}*${dpi}/96);$r.bottom=40+[int](${height}*${dpi}/96);
[DesignDpi]::SendMessage([IntPtr]${window},0x02e0,[IntPtr](${dpi} -bor (${dpi} -shl 16)),[ref]$r)|Out-Null`;
 await run('powershell.exe',['-NoProfile','-NonInteractive','-Command',script],{windowsHide:true});
}
async function assertRoundedWindow(app){
 const widget=(await app.command('widget')).widget;
 const result=JSON.parse((await run('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-File',path.join(root,'tests/check-widget-region.ps1'),'-WindowHandle',String(widget.window)],{windowsHide:true})).stdout);
 assert.equal(result.kind,3,'actual native window has a rounded region');
 assert.equal(result.cornersExcluded,true,'square corners are outside the window');
 assert.equal(result.sidesIncluded,true,'region follows current size and retains resize edges');
}
function assertLayout(info,label){
 assert.ok(!info.controls.some(c=>c.name==='footer'||c.name==='full'),'large footer removed');
 assert.equal(info.horizontalScroll,false,`${label}: agenda horizontal scroll`);
 for(const c of info.controls){
  assert.ok(c.width>0&&c.height>0,`${label}: ${c.name} has no space`);
  if(!c.scrolling){assert.ok(c.x>=-1&&c.y>=-1,`${label}: ${c.name} starts outside parent`);assert.ok(c.x+c.width<=c.parentWidth+1,`${label}: ${c.name} exceeds parent width`);assert.ok(c.y+c.height<=c.parentHeight+1,`${label}: ${c.name} exceeds parent height`);}
 }
 for(const parent of ['header','day-header','preferences']){
  const children=info.controls.filter(c=>c.parent===parent);
  for(let i=0;i<children.length;i++)for(let j=i+1;j<children.length;j++){
   const a=children[i],b=children[j];assert.ok(a.x+a.width<=b.x||b.x+b.width<=a.x||a.y+a.height<=b.y||b.y+b.height<=a.y,`${label}: ${a.name} overlaps ${b.name}`);
  }
 }
 assert.equal(info.calendar.length,info.weeks*7);
 for(const cell of info.calendar){assert.equal(cell.hit,true,`${label}: incorrect date hit area`);assert.ok(cell.height>=Math.floor(24*info.dpi/96),`${label}: date too short`);}
 const agenda=info.controls.find(c=>c.name==='agenda');assert.ok(agenda.height>=Math.floor(48*info.dpi/96),`${label}: agenda too short`);
}
test('디자인: 위젯 크기·주수·배율 변경·긴 제목과 앱 대화상자 배치',{timeout:120000},async()=>{
 const tmp=await fs.mkdtemp(path.join(os.tmpdir(),'pet-calendar-design-')),data=path.join(tmp,'data');await fs.mkdir(data);
 const state=defaultState(),today=dateKey();state.settings.hotkey.enabled=false;
 state.events=Array.from({length:24},(_,i)=>({id:'design-'+i,title:i===0?'긴 제목 & 분류 아이콘 '.repeat(10):'일정 '+i,startDate:'2026-02-01',endDate:'2026-02-01',startTime:'09:00',endTime:'10:00',category:['work','meeting','deadline','personal'][i%4],note:'긴 메모 '.repeat(20),repeat:'daily',repeatUntil:'',reminder:null,pinned:i===0,pinnedDate:today,completedDates:[]}));
 await fs.writeFile(path.join(data,'calendar.json'),JSON.stringify(state));const app=new DesktopDriver(data,path.join(tmp,'profile'));const results=[];
 const layout=async()=>(await app.command('widget',{command:'layout'})).layout;
 const click=selector=>app.js(`document.querySelector(${JSON.stringify(selector)}).click();true`);
 try{
  await app.ready;await app.api('POST','/api/widget',{action:'show'});
  for(const [width,height] of [[340,480],[380,590],[640,800]]){
   await app.command('widget',{command:'bounds',x:40,y:40,width,height});
   await assertRoundedWindow(app);
   for(const [date,weeks] of [['2026-02-01',4],['2026-11-01',5],['2026-08-01',6]]){
    await app.command('widget',{command:'select',date});await delay(100);const info=await layout();assert.equal(info.weeks,weeks);assertLayout(info,`${width}x${height}/${weeks}weeks`);results.push({width,height,weeks,dpi:info.dpi});if(width===340&&weeks===6)await app.command('widget',{command:'capture',path:path.join(root,'artifacts/design-widget-minimum.png')});
   }
  }
  for(const dpi of [120,144,192,96]){
   const widget=(await app.command('widget')).widget;await changeDpi(widget.window,dpi);await delay(150);const info=await layout();assert.equal(info.dpi,dpi);assertLayout(info,`DPI ${dpi}`);await assertRoundedWindow(app);results.push({dpi,weeks:info.weeks});
   await app.command('widget',{command:'capture',path:path.join(root,`artifacts/design-widget-dpi-${dpi}.png`)});
  }
  await app.command('widget',{command:'select',date:'2026-08-01'});
  const beforeClick=await layout(),cell=beforeClick.calendar[0],x=cell.x+Math.floor(cell.width/2),y=cell.y+Math.floor(cell.height/2);
  await app.command('widget',{command:'calendar-click',x,y,twice:true});
  await waitFor(()=>app.js('document.getElementById("eventDialog").open'),'calendar double click opens event');
  assert.equal(await app.js('document.getElementById("eventStart").value'),cell.date,'adjacent-month double click keeps the clicked date');await click('[data-close=eventDialog]');
  await app.api('POST','/api/desktop/notification-test');
  const popup=(await app.command('inspect')).popupWindow;
  for(const dpi of [120,144,192,96]){
   await changeDpi(popup,dpi,360,148);await delay(100);const info=(await app.command('popup-layout')).layout;
   assert.equal(info.dpi,dpi);assert.ok(info.caption.X+info.caption.Width<=info.close.X,'alarm title overlaps close');
   assert.ok(info.close.X+info.close.Width<=info.width,'alarm close outside window');
   assert.ok(info.list.Y>=info.close.Y+info.close.Height,'alarm list overlaps header');
   assert.ok(info.list.X+info.list.Width<=info.width&&info.list.Y+info.list.Height<=info.height,'alarm list exceeds window');
   assert.equal(info.itemHeight,Math.round(86*dpi/96));assert.ok(Math.abs(info.height-148*dpi/96)<=1,`alarm height remains compact after DPI change: ${JSON.stringify(info)}`);
   await app.command('capture-popup',{path:path.join(root,`artifacts/design-alarm-dpi-${dpi}.png`)});
  }
  await app.command('alarm-ack');
  await app.command('widget',{command:'select',date:today});await app.command('widget',{command:'bounds',x:40,y:40,width:380,height:590});await delay(100);
  await app.command('widget',{command:'capture',path:path.join(root,'artifacts/design-widget-long.png')});
  const crowded=await app.state();crowded.events=Array.from({length:205},(_,i)=>({...crowded.events[0],id:'crowded-'+i,pinned:false,pinnedDate:''}));
  assert.equal((await app.save(crowded)).status,200);await delay(100);assertLayout(await layout(),'205 schedules');
  await app.command('widget',{command:'click',name:'more'});
  assert.equal((await app.save({...await app.state(),events:[]})).status,200);await delay(100);
  assert.equal((await app.command('widget')).widget.rows.length,0);const empty=await layout();const emptyLabel=empty.controls.find(c=>c.name==='empty-agenda');assertLayout(empty,'empty agenda');assert.ok(empty.controls.some(c=>c.name==='empty-agenda'));
  await app.command('widget',{command:'capture',path:path.join(root,'artifacts/design-widget-empty.png')});
  await app.command('hide');
  await run('powershell.exe',['-NoProfile','-Command',`Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;public static class AgendaClick{[DllImport("user32.dll")]public static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);}
'@
[AgendaClick]::SendMessage([IntPtr]${emptyLabel.window},0x201,[IntPtr]1,[IntPtr](80 -bor (30 -shl 16)))|Out-Null;[AgendaClick]::SendMessage([IntPtr]${emptyLabel.window},0x202,[IntPtr]0,[IntPtr](80 -bor (30 -shl 16)))|Out-Null;[AgendaClick]::SendMessage([IntPtr]${emptyLabel.window},0x203,[IntPtr]1,[IntPtr](80 -bor (30 -shl 16)))|Out-Null;[AgendaClick]::SendMessage([IntPtr]${emptyLabel.window},0x202,[IntPtr]0,[IntPtr](80 -bor (30 -shl 16)))|Out-Null;`],{windowsHide:true});
  await waitFor(async()=>(await app.command('inspect')).visible,'empty area double click opens full calendar');
  assert.equal(await app.js('document.getElementById("eventDialog").open'),false,'open calendar without adding an event');

  assert.equal((await app.save({...await app.state(),events:state.events})).status,200);
  const mainWindow=(await app.command('inspect')).window;
  for(const dpi of [120,144,192,96]){
   await changeDpi(mainWindow,dpi,1280,900);await delay(150);
   const region=JSON.parse((await run('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-File',path.join(root,'tests/check-widget-region.ps1'),'-WindowHandle',String(mainWindow)],{windowsHide:true})).stdout);
   assert.equal(region.kind,3,`main window rounded at DPI ${dpi}`);assert.equal(region.cornersExcluded,true);assert.equal(region.sidesIncluded,true);
  }
  for(const [width,height] of [[1920,1000],[1440,900],[1280,620],[1024,768],[800,600],[390,844],[340,600]]){
   await app.command('devtools',{method:'Emulation.setDeviceMetricsOverride',parameters:{width,height,deviceScaleFactor:1,mobile:false}});await delay(100);
   for(const [name,button] of [['main',null],['eventDialog','#addEvent'],['settingsDialog','#settingsButton'],['ddayDialog','#deadlineCard'],['helpDialog','#helpButton']]){
    await app.js("document.querySelectorAll('dialog[open]').forEach(d=>d.close());true");if(button)await click(button);await delay(40);
    const info=await app.js(`(()=>{const root=${name==='main'?"document.querySelector('.app-shell')":`document.getElementById('${name}')`};const groups=[...root.querySelectorAll('.month-controls,.hotkey-modifiers,.category-choices,.dialog-footer>div,.day-top,.event-open')];const collisions=[];for(const group of groups){const children=[...group.children].filter(e=>e.getClientRects().length&&getComputedStyle(e).visibility!=='hidden');for(let i=0;i<children.length;i++)for(let j=i+1;j<children.length;j++){const a=children[i].getBoundingClientRect(),b=children[j].getBoundingClientRect();if(a.width>0&&b.width>0&&a.left<b.right-1&&b.left<a.right-1&&a.top<b.bottom-1&&b.top<a.bottom-1)collisions.push(group.className);}}return {pageOverflow:document.documentElement.scrollWidth>innerWidth,surfaceOverflow:root.scrollWidth>root.clientWidth+2,collisions};})()`);
    assert.equal(info.pageOverflow,false,`${width}/${name}: page overflow`);assert.equal(info.surfaceOverflow,false,`${width}/${name}: surface overflow`);assert.deepEqual(info.collisions,[],`${width}/${name}: controls overlap`);if(width===390)await app.command('capture',{path:path.join(root,`artifacts/design-small-${name}.png`)});
   }
   results.push({width,height,dialogs:'passed'});
  }
  await app.js("document.querySelectorAll('dialog[open]').forEach(d=>d.close());true");await app.command('devtools',{method:'Emulation.clearDeviceMetricsOverride'});await app.command('capture',{path:path.join(root,'artifacts/design-app.png')});
  assert.deepEqual(await app.js('window.qaErrors'),[]);
  await fs.writeFile(path.join(root,'artifacts/design-layout-results.json'),JSON.stringify({results,dpiMethod:'WM_DPICHANGED in isolated windows; no system display settings changed',mixedMonitor:'not available on this single-monitor machine'},null,2));
 }finally{await app.close();await delay(700);await fs.rm(tmp,{recursive:true,force:true,maxRetries:5,retryDelay:300});}
});
