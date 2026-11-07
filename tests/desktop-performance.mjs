import {promises as fs} from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {spawn} from 'node:child_process';
import assert from 'node:assert/strict';
import {defaultState,dateKey,addDays} from '../desktop/ui/model.mjs';
import {DesktopDriver,waitFor,delay,root} from './desktop-driver.mjs';
const temp=await fs.mkdtemp(path.join(os.tmpdir(),'pet-calendar-performance-')),dataDir=path.join(temp,'data');await fs.mkdir(dataDir);
const initial=defaultState();initial.settings.hotkey.enabled=false;const today=dateKey(),monthStart=today.slice(0,8)+'01';
initial.events=Array.from({length:1000},(_,i)=>({id:'perf-'+i,title:'검증용 일정 '+i,startDate:addDays(monthStart,i%28),endDate:addDays(monthStart,i%28),startTime:'10:00',endTime:'11:00',category:'work',note:'',repeat:i%4===0?'monthly':'none',repeatUntil:'',reminder:null,pinned:false}));
await fs.writeFile(path.join(dataDir,'calendar.json'),JSON.stringify(initial));const app=new DesktopDriver(dataDir,path.join(temp,'profile'));
try{
 await app.ready;
 const durations=[];
 for(let i=0;i<10;i++)durations.push(await app.js('(()=>{const at=performance.now();document.getElementById("nextMonth").click();return performance.now()-at;})()'));
 await app.command('hide');await delay(1500);
 const processIds=(await app.command('inspect')).processIds;
 const script=`$ids=@(${processIds.join(',')});$bytes=0;$cpu=0;foreach($id in $ids){$p=Get-Process -Id $id -ErrorAction SilentlyContinue;if($p){$bytes+=$p.WorkingSet64;$cpu+=$p.TotalProcessorTime.TotalMilliseconds}};Start-Sleep -Seconds 6;$cpu2=0;foreach($id in $ids){$p=Get-Process -Id $id -ErrorAction SilentlyContinue;if($p){$cpu2+=$p.TotalProcessorTime.TotalMilliseconds}};[pscustomobject]@{workingSetMB=[Math]::Round($bytes/1MB,1);idleCpuPercent=[Math]::Round(($cpu2-$cpu)/6000/[Environment]::ProcessorCount*100,3);processCount=$ids.Count}|ConvertTo-Json -Compress`;
 const native=await new Promise((resolve,reject)=>{let out='',error='';const p=spawn('powershell.exe',['-NoProfile','-Command',script],{windowsHide:true,stdio:['ignore','pipe','pipe']});p.stdout.on('data',c=>out+=c);p.stderr.on('data',c=>error+=c);p.once('error',reject);p.once('exit',code=>code===0?resolve(JSON.parse(out)):reject(new Error(error)));});
 const sorted=durations.toSorted((a,b)=>a-b),result={date:new Date().toISOString(),events:1000,startupMs:app.startupMs,monthChangeMs:durations,p95MonthChangeMs:sorted[Math.floor(sorted.length*.95)],...native};
 const widgetAt=Date.now();assert.equal((await app.api('POST','/api/widget',{action:'show'})).status,200);const widget=(await app.command('widget')).widget;const widgetIds=(await app.command('inspect')).processIds;result.widgetOpenMs=Date.now()-widgetAt;result.widgetVisible=widget.visible;result.extraWidgetProcesses=widgetIds.filter(id=>!processIds.includes(id)).length;assert.equal(result.extraWidgetProcesses,0,'widget does not add WebView processes');assert.ok(result.widgetOpenMs<1500,'widget opens within 1.5 seconds with 1000 schedules');
 await fs.writeFile(path.join(root,'artifacts','desktop-performance.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result));
 assert.ok(result.startupMs<3000,'startup < 3 seconds');assert.ok(result.p95MonthChangeMs<100,'month change < 100ms');assert.ok(result.idleCpuPercent<1,'idle CPU < 1%');
}finally{await app.close();await delay(500);await fs.rm(temp,{recursive:true,force:true}).catch(()=>{});}
