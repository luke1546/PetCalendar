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
const psFile=(file,args)=>run('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-File',path.join(root,'tests',file),...args],{windowsHide:true});
async function hover(buttons){
 const moves=buttons.map(b=>`[PaintMouse]::SetCursorPos(${Math.round(b.screenX+b.width/2)},${Math.round(b.screenY+b.height/2)})|Out-Null;Start-Sleep -Milliseconds 50;`).join('\n');
 await run('powershell.exe',['-NoProfile','-NonInteractive','-Command',`Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class PaintMouse {
 [StructLayout(LayoutKind.Sequential)] public struct POINT {public int x,y;}
 [DllImport("user32.dll")]public static extern bool GetCursorPos(out POINT p);
 [DllImport("user32.dll")]public static extern bool SetCursorPos(int x,int y);
}
'@
$p=New-Object PaintMouse+POINT;[PaintMouse]::GetCursorPos([ref]$p)|Out-Null;try{${moves}}finally{[PaintMouse]::SetCursorPos($p.x,$p.y)|Out-Null}`],{windowsHide:true});
}
function buttonBounds(widget,layout){
 const controls=new Map(layout.controls.map(c=>[c.name,c]));
 function position(c){if(c.screenX!==undefined)return {x:c.screenX,y:c.screenY};const p=controls.get(c.parent);const base=p?position(p):{x:widget.x,y:widget.y};return {x:base.x+c.x,y:base.y+c.y};}
 return layout.controls.filter(c=>c.kind==='button'||['prev','today','next','close','dday','add'].includes(c.name)||c.name.startsWith('paint-event@')).map(c=>{const p=position(c);return {...c,screenX:p.x,screenY:p.y,x:p.x-widget.x,y:p.y-widget.y};});
}
test('실제 바탕화면 페인트: 둥근 버튼 모서리에 이전 그림이 남지 않는다',{timeout:45000},async()=>{
 const tmp=await fs.mkdtemp(path.join(os.tmpdir(),'calendar-live-paint-')),data=path.join(tmp,'data');await fs.mkdir(data);
 const state=defaultState(),today=dateKey();state.settings.hotkey.enabled=false;
 state.events=[{id:'paint-event',title:'화면 그리기 확인',startDate:today,endDate:today,startTime:'09:00',endTime:'10:00',category:'meeting',repeat:'none',repeatUntil:'',reminder:null,pinned:true,pinnedDate:today,note:'',completedDates:[]}];
 await fs.writeFile(path.join(data,'calendar.json'),JSON.stringify(state));const app=new DesktopDriver(data,path.join(tmp,'profile'));const results=[];
 async function capture(label){
  const widget=(await app.command('widget')).widget,layout=(await app.command('widget',{command:'layout'})).layout;
  const buttons=buttonBounds(widget,layout);assert.ok(buttons.length>=6,'include toolbar and D-day');
  const imagePath=path.join(root,`artifacts/widget-live-${label}.png`),layoutPath=path.join(tmp,'layout.json');
  await fs.writeFile(layoutPath,JSON.stringify({buttons}));
  await psFile('capture-native-window.ps1',['-WindowHandle',String(widget.window),'-OutputPath',imagePath]);
  const checked=JSON.parse((await psFile('check-native-paint.ps1',['-ImagePath',imagePath,'-LayoutPath',layoutPath,'-WindowHandle',String(widget.window)])).stdout);
  results.push({label,...checked});assert.deepEqual(checked.failures,[],`${label}: stale pixels in live window corners`);return buttons;
 }
 try{
  await app.ready;await app.command('hide');await app.api('POST','/api/widget',{action:'show'});await app.api('POST','/api/widget',{action:'configure',topmost:true});
  await app.command('widget',{command:'bounds',x:80,y:80,width:380,height:590});await delay(150);
  const buttons=await capture('initial');assert.ok(buttons.some(b=>b.name.startsWith('paint-event@')),'include agenda card');await hover(buttons);await capture('after-hover');
  await app.command('widget',{command:'bounds',x:90,y:90,width:440,height:650});await delay(150);await capture('after-resize');
  await app.command('widget',{command:'click',name:'next'});await app.command('widget',{command:'click',name:'prev'});await delay(150);await capture('after-navigation');
  await app.api('POST','/api/widget',{action:'hide'});await app.api('POST','/api/widget',{action:'show'});await delay(150);await capture('after-reopen');
  await app.api('POST','/api/widget',{action:'configure',topmost:false});await capture('without-topmost');
  await fs.writeFile(path.join(root,'artifacts/widget-live-paint-results.json'),JSON.stringify({method:'CopyFromScreen of visible window; not DrawToBitmap',results},null,2));
 }finally{await app.close();await delay(700);await fs.rm(tmp,{recursive:true,force:true,maxRetries:5,retryDelay:300});}
});
