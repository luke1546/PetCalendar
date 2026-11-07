import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {spawn} from 'node:child_process';
import {DesktopDriver,root,waitFor,delay} from './desktop-driver.mjs';
import {defaultState,dateKey} from '../desktop/ui/model.mjs';
const quote=s=>"'"+s.replaceAll("'","''")+"'";
function run(exe,args){return new Promise((resolve,reject)=>{const p=spawn('powershell.exe',['-NoProfile','-Command',`$p=Start-Process ${quote(exe)} -ArgumentList @(${args.map(quote).join(',')}) -PassThru -Wait;exit $p.ExitCode`],{windowsHide:true,stdio:'inherit'});p.on('error',reject);p.on('exit',resolve);});}
test('이름 변경 업데이트: 이전 실행본 정상 종료·기존 데이터·펫 이름·새 앱 ID 유지',{timeout:120000},async()=>{
 const temp=await fs.mkdtemp(path.join(os.tmpdir(),'pet-calendar-brand-')),data=path.join(temp,'data'),profile=path.join(temp,'profile');await fs.mkdir(data);
 const appDir=path.join(root,'artifacts',path.basename(temp),'app'),setup=path.join(root,'dist/PetCalendar-Setup-QA-Final.exe');
 const state=defaultState();state.settings.hotkey.enabled=false;state.settings.notificationSound=false;state.tasks=[{id:'kept',title:'이름을 바꿔도 유지할 기록',dueDate:dateKey(),done:false,createdAt:new Date().toISOString()}];
 const file=path.join(data,'calendar.json'),bytes=JSON.stringify(state);await fs.writeFile(file,bytes);
 await fs.writeFile(path.join(data,'pets.json'),JSON.stringify({enabled:true,summaryEnabled:false,chatterEnabled:false,pets:[{species:'cat',palette:'라벤더',name:'호두'},{species:'dog',palette:'하양',name:'구름'}]}));
 let legacy,app;
 try{
  legacy=new DesktopDriver(data,profile,path.join(root,'artifacts/legacy-brand-fixture/PetCalendar.exe'));await legacy.ready;
  assert.equal((await legacy.command('pets-inspect')).pets.count,2);
  assert.equal(await run(setup,['/S','/DATA='+data,'/D='+appDir]),0);
  await waitFor(()=>legacy.child.exitCode!==null,'legacy process exits gracefully');assert.equal(await fs.readFile(file,'utf8'),bytes);
  app=new DesktopDriver(data,profile,path.join(appDir,'PetCalendar.exe'));await app.ready;
  assert.equal((await app.command('inspect')).applicationId,'PetCalendar.Desktop');
  const pets=(await app.command('pets-inspect')).pets;assert.equal(pets.count,2);assert.deepEqual(pets.pets.map(p=>[p.species,p.palette,p.name]),[['cat','라벤더','호두'],['dog','하양','구름']]);
  assert.equal((await app.state()).tasks[0].title,state.tasks[0].title);assert.equal(await app.js('window.petCalendarDesktop'),true);assert.deepEqual(await app.js('window.qaErrors'),[]);
  assert.equal(await run(path.join(appDir,'RemovePetCalendar.exe'),['--qa','/S']),0);await waitFor(()=>app.child.exitCode!==null,'new app stops for uninstall');
  assert.equal(await fs.readFile(file,'utf8'),bytes);assert.ok((await fs.readdir(path.join(data,'backups'))).some(n=>n.startsWith('calendar-upgrade-')));
 }finally{
  if(legacy)await legacy.close();if(app)await app.close();
  if(await fs.stat(path.join(appDir,'RemovePetCalendar.exe')).then(()=>true,()=>false))await run(path.join(appDir,'RemovePetCalendar.exe'),['--qa','/S']);
  await delay(700);await fs.rm(temp,{recursive:true,force:true}).catch(()=>{});await fs.rm(path.dirname(appDir),{recursive:true,force:true}).catch(()=>{});
 }
});
