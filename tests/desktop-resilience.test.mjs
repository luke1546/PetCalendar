import test from 'node:test';
import assert from 'node:assert/strict';
import {promises as fs} from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {spawn} from 'node:child_process';
import {once} from 'node:events';
import {defaultState} from '../desktop/ui/model.mjs';
import {DesktopDriver,waitFor,delay,root,executable} from './desktop-driver.mjs';
const quote=s=>"'"+s.replaceAll("'","''")+"'";
function run(exe,args){return new Promise((resolve,reject)=>{let out='',err='';const child=spawn(exe,args,{windowsHide:true,stdio:['ignore','pipe','pipe']});child.stdout.on('data',c=>out+=c);child.stderr.on('data',c=>err+=c);child.once('error',reject);child.once('exit',code=>resolve({code,out,err}));});}
const ps=script=>run('powershell.exe',['-NoProfile','-Command',script]);
test('실제 전역 키·충돌 복구·저장 실패 시 입력 보존·중복 실행·트레이 재시작',{timeout:90000},async()=>{
  const temp=await fs.mkdtemp(path.join(os.tmpdir(),'pet-calendar-resilience-'));
  const dataDir=path.join(temp,'data');await fs.mkdir(dataDir);
  const initial=defaultState();initial.settings.hotkey.enabled=false;
  const file=path.join(dataDir,'calendar.json');await fs.writeFile(file,JSON.stringify(initial));
  const app=new DesktopDriver(dataDir,path.join(temp,'profile'));let other,locker;
  const control=action=>run(executable,['--data-dir',dataDir,'--control',action,'--silent']);
  try{
    await app.ready;
    let state=await app.state();state.settings.hotkey={enabled:true,modifiers:4,key:192};assert.equal((await app.save(state)).status,200);
    const info=await app.command('inspect');
    const hotkey=await run('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-File',path.join(root,'tests/check-window-hotkey.ps1'),'-WindowHandle',String(info.window),'-Modifiers','4','-Key','192']);
    if(hotkey.code!==0&&hotkey.err.includes('Foreground=0'))console.log('LIMITATION physical key input: Windows input desktop is unavailable; registration and conflict recovery tested below');
    else{assert.equal(hotkey.code,0,hotkey.err);assert.equal(JSON.parse(hotkey.out).SameWindowRestored,true);}
    const otherDir=path.join(temp,'other');await fs.mkdir(otherDir);await fs.writeFile(path.join(otherDir,'calendar.json'),JSON.stringify(initial));other=new DesktopDriver(otherDir,path.join(temp,'other-profile'));await other.ready;
    const before=await other.state(),conflict=structuredClone(before);conflict.settings.hotkey={enabled:true,modifiers:4,key:192};assert.equal((await other.save(conflict)).status,422);assert.deepEqual(await other.state(),before);await other.close();
    state=await app.state();state.settings.hotkey.enabled=false;assert.equal((await app.save(state)).status,200);
    const beforeFailure=await app.state();
    locker=spawn('powershell.exe',['-NoProfile','-Command',`$f=[IO.File]::Open(${quote(file)},[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read);[Console]::WriteLine('locked');[Console]::ReadLine()|Out-Null;$f.Dispose()`],{windowsHide:true,stdio:['pipe','pipe','pipe']});
    await once(locker.stdout,'data');
    const failing=structuredClone(beforeFailure);failing.settings.hotkey={enabled:true,modifiers:4,key:192};assert.equal((await app.save(failing)).status,500);assert.deepEqual(await app.state(),beforeFailure);assert.equal((await app.api('GET','/api/hotkey')).body.active,false);
    await app.js('document.getElementById("addEvent").click();document.getElementById("eventTitle").value="저장 실패에도 남는 일정";document.getElementById("saveEvent").click();true');
    await waitFor(()=>app.js('!document.getElementById("saveEvent").disabled'),'failed save finished');
    assert.equal(await app.js('document.getElementById("eventDialog").open'),true);assert.equal(await app.js('document.getElementById("eventTitle").value'),'저장 실패에도 남는 일정');
    const unlocked=once(locker,'exit');locker.stdin.end('\n');await unlocked;locker=null;
    await app.js('document.getElementById("saveEvent").click();true');await waitFor(()=>app.js('!document.getElementById("eventDialog").open'),'retry saved');assert.equal((await app.state()).events[0].title,'저장 실패에도 남는 일정');
    await app.command('hide');const duplicate=await run(executable,['--data-dir',dataDir,'--silent']);assert.equal(duplicate.code,0);assert.equal((await app.command('inspect')).visible,true);assert.equal(app.child.exitCode,null);
    const saved=await fs.readFile(file,'utf8');await app.command('menu',{item:'restart'});await waitFor(()=>app.child.exitCode!==null,'old app exited');
    await waitFor(async()=>(await control('show')).code===0,'restarted pipe ready');await delay(2000);assert.equal(await fs.readFile(file,'utf8'),saved);
    assert.equal((await control('exit')).code,0);await waitFor(async()=>(await control('show')).code===2,'complete exit');
    console.log('PASS Shift + backtick registration, collision rollback, locked-file retry, single instance, native restart/exit');
  }finally{
    if(locker){locker.stdin.end('\n');locker.kill();}if(other)await other.close();await app.close();await control('exit');await delay(500);await fs.rm(temp,{recursive:true,force:true}).catch(()=>{});
  }
});
