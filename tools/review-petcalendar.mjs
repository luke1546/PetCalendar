// Run with Windows Node.js. Writes only to the review output and dedicated test artifacts.
// node tools/review-petcalendar.mjs --out artifacts/review-YYYY-MM-DD
// Add --resources for 30 starts and 3 x 60-second samples per pet/visibility condition.
import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {spawn} from 'node:child_process';
import {createInterface} from 'node:readline';
import {fileURLToPath} from 'node:url';
import {performance} from 'node:perf_hooks';
import {defaultState,dateKey,ddayOccurrence} from '../desktop/ui/model.mjs';

if(process.platform!=='win32')throw new Error('Use Windows node.exe so .NET/WinForms and Windows paths are exercised.');
const root=fileURLToPath(new URL('../',import.meta.url));
const argv=process.argv.slice(2),value=(key,fallback)=>{const at=argv.indexOf(key);return at<0?fallback:argv[at+1];};
const out=path.resolve(root,value('--out',`artifacts/review-${new Date().toISOString().replaceAll(':','-')}`));
const relative=path.relative(path.join(root,'artifacts'),out);
if(!relative || relative.startsWith('..') || path.isAbsolute(relative))throw new Error('--out must be a new directory below artifacts/');
await fs.mkdir(out,{recursive:true});await fs.mkdir(path.join(out,'logs'),{recursive:true});
if(await fs.stat(path.join(out,'results.json')).then(()=>true,()=>false))throw new Error('Existing results would be overwritten; choose a new --out.');
const executable=path.join(out,'app','PetCalendar.exe');
const result={status:'running',startedAt:new Date().toISOString(),environment:{platform:process.platform,node:process.version,timezone:Intl.DateTimeFormat().resolvedOptions().timeZone},steps:[],checks:[],limitations:[]};
const hash=bytes=>createHash('sha256').update(bytes).digest('hex');
const save=()=>fs.writeFile(path.join(out,'results.json'),JSON.stringify(result,null,2));
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
const psQuote=s=>"'"+s.replaceAll("'","''")+"'";
async function run(name,command,args,{timeout=120000,env=process.env}={}) {
  const start=performance.now();console.log(`START ${name}`);
  const child=spawn(command,args,{cwd:root,env,windowsHide:true,stdio:['ignore','pipe','pipe']});
  let stdout='',stderr='',spawnError,timedOut=false;
  child.stdout.on('data',c=>stdout+=c);child.stderr.on('data',c=>stderr+=c);
  child.on('error',error=>{spawnError=error.message;});
  const timer=setTimeout(()=>{timedOut=true;spawn('taskkill.exe',['/PID',String(child.pid),'/T','/F'],{windowsHide:true,stdio:'ignore'});},timeout);
  const code=await new Promise(resolve=>{child.once('close',resolve);});clearTimeout(timer);
  const log=`${command} ${args.join(' ')}\n\n${stdout}\n${stderr}`;await fs.writeFile(path.join(out,'logs',`${name}.log`),log);
  const step={name,code,passed:code===0&&!timedOut&&!spawnError,timedOut,spawnError,durationMs:Math.round(performance.now()-start),log:`logs/${name}.log`};
  result.steps.push(step);await save();console.log(`DONE ${name}: ${step.passed?'PASS':'FAIL'}`);return {...step,stdout,stderr};
}
async function sources() {
  const found={};async function walk(dir){for(const entry of await fs.readdir(path.join(root,dir),{withFileTypes:true})){const rel=path.join(dir,entry.name);if(entry.isDirectory()){if(!['webview2','pets-assets','screenshots'].includes(entry.name))await walk(rel);}else if(/\.(cs|mjs|ps1|md|nsi|manifest|config)$/.test(entry.name))found[rel.replaceAll('\\','/')]=hash(await fs.readFile(path.join(root,rel)));}}
  for(const dir of ['desktop','tests','tools','docs'])await walk(dir);found['README.md']=hash(await fs.readFile(path.join(root,'README.md')));return found;
}
class App {
  constructor(data) {
    this.pending=new Map();this.next=0;this.stderr='';
    this.child=spawn(executable,['--automation','--data-dir',data,'--profile-dir',path.join(data,'legacy-profile')],{cwd:path.dirname(executable),windowsHide:false,stdio:['pipe','pipe','pipe']});
    this.exited=new Promise(resolve=>this.child.once('exit',(code,signal)=>resolve({code,signal})));
    this.ready=new Promise((resolve,reject)=>{
      const timer=setTimeout(()=>reject(new Error('Startup timed out: '+this.stderr)),20000);this.startupTimer=timer;
      this.reader=createInterface({input:this.child.stdout});
      this.reader.on('line',line=>{let m;try{m=JSON.parse(line.replace(/^\uFEFF/,''));}catch{return;}
        if(m.kind==='ready'){clearTimeout(timer);this.pid=m.pid;resolve(m);return;}
        if(m.kind==='fatal'){clearTimeout(timer);reject(new Error(m.error));return;}
        const p=this.pending.get(m.id);if(p){clearTimeout(p.timer);this.pending.delete(m.id);m.ok?p.resolve(m):p.reject(new Error(m.error));}
      });
      this.child.once('error',error=>{clearTimeout(timer);reject(error);});
      this.child.once('exit',(code,signal)=>{clearTimeout(timer);reject(new Error(`Process exited ${code}/${signal}: ${this.stderr}`));for(const p of this.pending.values()){clearTimeout(p.timer);p.reject(new Error('App exited'));}this.pending.clear();});
    });
    this.child.stderr.on('data',c=>this.stderr+=c);this.child.stdin.on('error',()=>{});
  }
  call(action,extra={}) {const id=++this.next;return new Promise((resolve,reject)=>{const timer=setTimeout(()=>{this.pending.delete(id);reject(new Error('Command timed out: '+action));},30000);this.pending.set(id,{resolve,reject,timer});this.child.stdin.write(JSON.stringify({id,action,...extra})+'\n');});}
  api(method,url,body,extra={}){return this.call('api',{method,path:url,...(body===undefined?{}:{body}),...extra});}
  async close(){clearTimeout(this.startupTimer);if(this.child.exitCode===null&&!this.child.killed){try{await this.call('exit');}catch{}const done=await Promise.race([this.exited.then(()=>true),delay(5000).then(()=>false)]);if(!done)this.child.kill();}this.reader?.close();}
}
async function fixture(name,{pets=0,events=0,tasks=0}={}) {
  const data=path.join(out,'data',name);await fs.mkdir(data,{recursive:true});const state=defaultState(),today=dateKey();
  state.settings.hotkey.enabled=false;state.settings.notificationSound=false;state.settings.desktopPopups=false;
  state.events=Array.from({length:events},(_,i)=>({id:`review-e-${i}`,title:`검토 일정 ${i}`,startDate:today,endDate:today,startTime:'09:00',endTime:'10:00',category:'work',note:'한글·🍀 데이터',repeat:'none',repeatUntil:'',reminder:null,pinned:false,pinnedDate:'',completedDates:[],excludedDates:[]}));
  state.tasks=Array.from({length:tasks},(_,i)=>({id:`review-t-${i}`,title:`검토 할 일 ${i}`,dueDate:today,done:false,createdAt:new Date().toISOString()}));
  await fs.writeFile(path.join(data,'calendar.json'),JSON.stringify(state));
  await fs.writeFile(path.join(data,'ui-state.json'),JSON.stringify({quickTourDone:true,hotkeyMoved:true,drafts:{}}));
  await fs.writeFile(path.join(data,'pets.json'),JSON.stringify({enabled:pets>0,allowEmpty:true,chatterEnabled:false,summaryEnabled:false,bedEnabled:false,toyEnabled:false,toyDefaultOff:true,pets:Array.from({length:pets},(_,i)=>({species:['cat','dog','redPanda','capybara','squirrel','alpaca','elephant'][i],name:`검토${i}`,personality:0}))}));
  return data;
}
async function check(name,fn){try{const detail=await fn();result.checks.push({name,passed:true,...(detail===undefined?{}:{detail})});console.log(`CHECK ${name}: PASS`);}catch(error){result.checks.push({name,passed:false,error:error.stack});console.log(`CHECK ${name}: FAIL ${error.message}`);}await save();}
async function inventory(){
  result.sourceStart=await sources();
  result.sourceSnapshot='source';
  for(const file of Object.keys(result.sourceStart)){
    const target=path.join(out,'source',file);await fs.mkdir(path.dirname(target),{recursive:true});await fs.copyFile(path.join(root,file),target);
  }
  result.assets={};
  for(const file of ['calendar.ico','desktop/pets.zip','desktop/emoji.zip','desktop/fonts.zip','desktop/ui/holidays.json']){
    const bytes=await fs.readFile(path.join(root,file));result.assets[file]=hash(bytes);const target=path.join(out,'source',file);await fs.mkdir(path.dirname(target),{recursive:true});await fs.writeFile(target,bytes);
  }
  await run('git-status','git',['status','--short']);await run('git-effective-diff','git',['diff','--ignore-space-at-eol','--stat']);
  const files=(await fs.readdir(path.join(root,'tests'))).filter(f=>f.endsWith('.test.mjs')).sort();result.testInventory=[];
  for(const file of files){const source=await fs.readFile(path.join(root,'tests',file),'utf8'),missing=[];
    for(const match of source.matchAll(/(?:from\s*|import\s*)['"](\.[^'"]+)['"]/g)){if(!await fs.stat(path.resolve(root,'tests',match[1])).then(()=>true,()=>false))missing.push(match[1]);}
    result.testInventory.push({file,missingImports:missing,classification:missing.length?'blocked-import':'runnable'});
  }
  const previous=path.join(root,'artifacts/native-migration/soak-final-results.json');
  const old=await fs.readFile(previous,'utf8').then(JSON.parse,()=>null);if(old)result.historicalSoak={status:old.status,startedAt:old.startedAt,sha256:old.sha256,samples:old.samples?.length,error:old.error};
}
async function tests(){
  const env={...process.env,PET_CALENDAR_TEST_EXE:executable};
  for(const t of result.testInventory.filter(t=>t.classification==='runnable'))await run(t.file.replace('.test.mjs',''),process.execPath,['--test',`tests/${t.file}`],{env,timeout:150000});
  const blocked=result.testInventory.find(t=>t.classification==='blocked-import');if(blocked)await run('missing-driver-reproduction',process.execPath,['--test',`tests/${blocked.file}`],{env});
  const suites=['brand-migration','pet-schedule','font-coverage','name-tag-emoji','toy-follow','pet-speech-windows','legacy-storage','native-text-renderer'];
  for(const suite of suites){const args=['-NoProfile','-ExecutionPolicy','Bypass','-File',`tests/${suite}.ps1`];if(['font-coverage','name-tag-emoji','toy-follow'].includes(suite))args.push('-AppDirectory',path.dirname(executable));await run(suite,'powershell.exe',args,{timeout:150000});}
  const fixtureDir=path.join(out,'core-fixture');await fs.cp(path.dirname(executable),fixtureDir,{recursive:true});
  const testExe=path.join(fixtureDir,'ReviewChecks.exe');
  const compiled=await run('review-core-compile',path.join(process.env.SystemRoot,'Microsoft.NET/Framework64/v4.0.30319/csc.exe'),['/nologo','/target:exe','/platform:x64','/codepage:65001',`/out:${testExe}`,`/reference:${executable}`,'/reference:System.Core.dll','/reference:System.Web.Extensions.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll',path.join(root,'tools','review','ReviewChecks.cs')]);
  if(compiled.passed){const ran=await run('review-core',testExe,[path.join(out,'core-data')]);try{result.coreChecks=JSON.parse(ran.stdout.trim()).checks;}catch{result.limitations.push('Core fixture returned no parseable JSON.');}await save();}
  const uiOutput=path.join(out,'ui');
  await run('review-ui','powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-File',path.join(root,'tools','review','review-ui.ps1'),'-AppDirectory',path.dirname(executable),'-OutputDirectory',uiOutput],{timeout:150000});
  try{result.uiChecks=JSON.parse((await fs.readFile(path.join(uiOutput,'evidence','results.json'),'utf8')).replace(/^\uFEFF/,'')).checks;}catch{result.limitations.push('UI fixture returned no parseable results.');}await save();
}
async function smoke(){
  const data=await fixture('smoke',{events:3,tasks:3});let app=new App(data);
  try{
    await app.ready;
    await check('unicode_save_revision_and_conflict',async()=>{
      const original=(await app.api('GET','/api/state')).body;const next=structuredClone(original);next.tasks[0].title='검토 저장 🍀 뷁·똠·힣';
      const saved=await app.api('PUT','/api/state',next);assert.equal(saved.status,200);assert.equal(saved.body.revision,original.revision+1);
      assert.equal((await app.api('PUT','/api/state',original)).status,409);assert.equal((await app.api('GET','/api/state')).body.tasks[0].title,next.tasks[0].title);
    });
    await check('invalid_save_and_marker_preserve_state',async()=>{const original=(await app.api('GET','/api/state')).body;const bad=structuredClone(original);bad.tasks[0].title='';assert.equal((await app.api('PUT','/api/state',bad)).status,400);assert.equal((await app.api('PUT','/api/state',original,{marker:'invalid'})).status,403);assert.deepEqual((await app.api('GET','/api/state')).body,original);});
    await check('combined_backup_restore_and_stale_revision',async()=>{
      const backup=(await app.api('GET','/api/backup')).body;const changed=(await app.api('GET','/api/state')).body;changed.tasks=[];assert.equal((await app.api('PUT','/api/state',changed)).status,200);
      const current=(await app.api('GET','/api/state')).body;assert.equal((await app.api('POST','/api/backup',{...backup,expectedRevision:current.revision-1})).status,409);
      const restored=await app.api('POST','/api/backup',{...backup,expectedRevision:current.revision});assert.equal(restored.status,200);assert.equal(restored.body.tasks.length,3);
      const invalid=structuredClone(backup);invalid.calendar.events[0].startDate='bad';assert.equal((await app.api('POST','/api/backup',{...invalid,expectedRevision:restored.body.revision})).status,400);assert.deepEqual((await app.api('GET','/api/state')).body,restored.body);
    });
    await check('widget_completion_synchronizes_calendar',async()=>{const state=(await app.api('GET','/api/state')).body;await app.api('POST','/api/widget',{action:'show'});await app.call('widget',{command:'select',date:dateKey()});await app.call('widget',{command:'complete',key:`${state.events[0].id}@${dateKey()}`});assert.ok((await app.api('GET','/api/state')).body.events[0].completedDates.includes(dateKey()));await app.api('POST','/api/widget',{action:'hide'});});
    await check('current_ui_capture_and_interaction_bench',async()=>{await app.call('capture',{path:path.join(out,'main.png')});const response=await app.call('calendar-bench',{width:1100,height:700});await fs.writeFile(path.join(out,'ui-bench.json'),JSON.stringify(response.bench,null,2));return {file:'main.png',bench:'ui-bench.json'};});
    if(result.testInventory.some(t=>t.missingImports.includes('./desktop-driver.mjs')))
      await check('legacy_native_ui_protocol_compatibility',async()=>{await app.call('native-ui',{command:'layout'});});
    await check('independent_browserless_process_modules_and_payload',async()=>{
      const command=`$ErrorActionPreference='Stop';$a=[Reflection.Assembly]::ReflectionOnlyLoadFrom(${psQuote(executable)});$p=Get-Process -Id ${app.pid};$q=New-Object 'Collections.Generic.Queue[int]';$q.Enqueue(${app.pid});$children=@();while($q.Count){$parent=$q.Dequeue();foreach($c in @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$parent")){$children+=$c.Name;$q.Enqueue([int]$c.ProcessId)}};[pscustomobject]@{references=@($a.GetReferencedAssemblies()|ForEach-Object {$_.Name});modules=@($p.Modules|ForEach-Object {$_.ModuleName});children=$children}|ConvertTo-Json -Compress`;
      const probe=await run('browserless-os-probe','powershell.exe',['-NoProfile','-Command',command]);assert.ok(probe.passed);const info=JSON.parse(probe.stdout);assert.equal([...info.references,...info.modules,...info.children].some(n=>/webview|msedge|libcef|chrome_elf/i.test(n)),false);assert.deepEqual(info.children,[]);assert.equal(await fs.stat(path.join(data,'legacy-profile')).then(()=>true,()=>false),false);return info;
    });
    const before=(await app.api('GET','/api/state')).body;await app.close();app=new App(data);await app.ready;
    await check('restart_preserves_saved_state',async()=>assert.deepEqual((await app.api('GET','/api/state')).body,before));
  }finally{await app.close();}
}
async function resources(){
  result.resources={scope:'Current build only; 1000 events / 220 tasks; hotkeys/sound/chatter/bed/toy disabled. No claim of improvement over an unverified historical build.',starts:[],samples:[]};
  const startData=await fixture('startup',{events:1000,tasks:220});
  for(let i=0;i<30;i++){const begin=performance.now();const app=new App(startData);try{const ready=await app.ready;result.resources.starts.push({index:i,wallMs:performance.now()-begin,appMs:ready.elapsedMs});}finally{await app.close();}await save();if(i%5===4)console.log(`STARTUP ${i+1}/30`);}
  for(let round=0;round<3;round++)for(const pets of [0,1,7])for(const visible of [true,false]){
    const data=await fixture(`resource-${round}-${pets}-${visible}`,{pets,events:1000,tasks:220}),app=new App(data);
    try{await app.ready;assert.equal((await app.api('GET','/api/pets')).body.count,pets,'Resource fixture must restore the requested pet count');if(!visible)await app.call('hide');await delay(3000);
      const command=`$ErrorActionPreference='Stop';$p=Get-Process -Id ${app.pid};$before=$p.TotalProcessorTime.TotalMilliseconds;$clock=[Diagnostics.Stopwatch]::StartNew();Start-Sleep -Seconds 60;$p.Refresh();[pscustomobject]@{pid=$p.Id;privateMiB=$p.PrivateMemorySize64/1MB;workingSetMiB=$p.WorkingSet64/1MB;handles=$p.HandleCount;threads=$p.Threads.Count;cpuMs=$p.TotalProcessorTime.TotalMilliseconds-$before;elapsedMs=$clock.Elapsed.TotalMilliseconds;logicalProcessors=[Environment]::ProcessorCount}|ConvertTo-Json -Compress`;
      const sample=await run(`resource-${round}-${pets}-${visible}`,'powershell.exe',['-NoProfile','-Command',command],{timeout:90000});
      if(sample.passed){const info=JSON.parse(sample.stdout);info.cpuPercentOneCore=100*info.cpuMs/info.elapsedMs;info.cpuPercentMachine=info.cpuPercentOneCore/info.logicalProcessors;const actual=(await app.api('GET','/api/pets')).body;result.resources.samples.push({round,pets,visible,actualPets:actual.count,...info});}
    }finally{await app.close();}await save();
  }
}
try{
  await inventory();
  const build=await run('build','powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-File',path.join(out,'source','desktop','build.ps1'),'-OutputDirectory',path.dirname(executable)]);
  if(!build.passed)throw new Error('Build failed; runtime review stopped.');
  result.executable={path:executable,sha256:hash(await fs.readFile(executable)),files:await fs.readdir(path.dirname(executable))};await save();
  await tests();await smoke();
  await check('javascript_dday_exclusion_independent_check',async()=>{const event={id:'review',startDate:'2024-02-29',endDate:'2024-02-29',repeat:'yearly',repeatUntil:'',pinned:true,pinnedDate:'',excludedDates:['2027-02-28']};assert.equal(ddayOccurrence(event,'2026-03-01').startDate,'2028-02-29');});
  if(argv.includes('--resources'))await resources();else result.limitations.push('30-start and 60-second resource matrix not requested in this invocation.');
  result.limitations.push('8-hour soak not started: baseline regression gate has blocked/failed checks. Historical soak ended after process disappearance and does not establish its cause.');
  result.limitations.push('Real IME, mixed-monitor DPI, screen reader and installer lifecycle require separately recorded interactive verification. API/DrawToBitmap checks are not equivalent.');
}catch(error){result.fatal=error.stack;process.exitCode=1;}
finally{
  const end=await sources();result.changedDuringReview=Object.keys({...result.sourceStart,...end}).filter(file=>result.sourceStart?.[file]!==end[file]);
  result.finishedAt=new Date().toISOString();result.summary={stepsPassed:result.steps.filter(s=>s.passed).length,stepsFailed:result.steps.filter(s=>!s.passed).length,checksPassed:result.checks.filter(s=>s.passed).length,checksFailed:result.checks.filter(s=>!s.passed).length,corePassed:result.coreChecks?.filter(s=>s.passed).length,coreFailed:result.coreChecks?.filter(s=>!s.passed).length,uiPassed:result.uiChecks?.filter(s=>s.passed).length,uiFailed:result.uiChecks?.filter(s=>!s.passed).length,blockedTestFiles:result.testInventory?.filter(s=>s.classification==='blocked-import').length};
  result.status=result.fatal?'interrupted':result.summary.stepsFailed||result.summary.checksFailed||result.summary.coreFailed||result.summary.uiFailed||result.summary.blockedTestFiles?'completed-with-findings':'completed';
  await save();console.log(JSON.stringify({out,summary:result.summary,fatal:result.fatal}));
  if(result.summary.stepsFailed||result.summary.checksFailed||result.summary.coreFailed||result.summary.uiFailed||result.summary.blockedTestFiles)process.exitCode=1;
}
