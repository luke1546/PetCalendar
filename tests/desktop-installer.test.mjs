import test from 'node:test';
import assert from 'node:assert/strict';
import {promises as fs} from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {spawn} from 'node:child_process';
import {defaultState,dateKey} from '../desktop/ui/model.mjs';
import {DesktopDriver,root,waitFor,delay} from './desktop-driver.mjs';
function run(exe,args){const quote=value=>"'"+value.replaceAll("'","''")+"'";const script=`$ErrorActionPreference='Stop';$p=Start-Process -FilePath ${quote(exe)} -ArgumentList @(${args.map(quote).join(',')}) -WindowStyle Hidden -PassThru -Wait;exit $p.ExitCode`;return new Promise((resolve,reject)=>{const child=spawn('powershell.exe',['-NoProfile','-Command',script],{windowsHide:true,stdio:'inherit'});child.once('error',reject);child.once('exit',code=>resolve(code));});}
test('설치 파일: 사용자 설치, 업데이트, 제거 후 데이터 보존, 재설치',{timeout:240000},async()=>{
  const temp=await fs.mkdtemp(path.join(os.tmpdir(),'pet-calendar-install-'));
  const appDir=path.join(root,'artifacts',path.basename(temp),'app'),dataDir=path.join(temp,'data');await fs.mkdir(dataDir);
  const data=defaultState();data.settings.hotkey.enabled=false;data.tasks=[{id:'kept',title:'설치 후에도 보존',dueDate:dateKey(),done:false,createdAt:new Date().toISOString()}];
  const file=path.join(dataDir,'calendar.json'),original=JSON.stringify(data);await fs.writeFile(file,original);
  const setup=path.join(root,'dist/PetCalendar-Setup-QA-Final.exe');
  const startup=path.join(process.env.APPDATA,'Microsoft/Windows/Start Menu/Programs/Startup/펫 캘린더 QA.lnk');let app;
  const oldStartup=path.join(path.dirname(startup),'업무 캘린더 QA.lnk');
  try{
    console.log('CHECK per-user install');
    assert.equal(await run(setup,['/S','/DATA='+dataDir,'/D='+appDir]),0);
    assert.ok((await fs.stat(path.join(appDir,'PetCalendar.exe'))).size>0);
    for(const asset of ['public/tour-cat.png','EMOJI-LICENSE.md']){
      const assetPath=path.join(appDir,asset);assert.ok((await fs.stat(assetPath)).size>0);
      await fs.rename(assetPath,assetPath+'.test-backup');
      try{assert.notEqual(await run(path.join(appDir,'PetCalendar.exe'),['--validate-install','--silent']),0,asset+' is required by install validation');}
      finally{await fs.rename(assetPath+'.test-backup',assetPath);}
    }
    assert.equal(await fs.stat(path.join(appDir,'.install-staging')).then(()=>true,()=>false),false,'staging payload is fully cleaned');
    assert.equal(await fs.readFile(file,'utf8'),original);
    await fs.rename(startup,oldStartup);
    assert.equal(await run(setup,['/S','/DATA='+dataDir,'/D='+appDir]),0,'upgrade recognizes the old startup shortcut');
    assert.ok(await fs.stat(startup),'enabled startup remains enabled under the new name');
    assert.equal(await fs.stat(oldStartup).then(()=>true,()=>false),false,'old owned shortcut is removed');
    app=new DesktopDriver(dataDir,path.join(temp,'profile'),path.join(appDir,'PetCalendar.exe'));await app.ready;
    await fs.unlink(startup);
    console.log('CHECK update with app running');assert.equal(await run(setup,['/S','/DATA='+dataDir,'/D='+appDir]),0);
    await waitFor(()=>app.child.exitCode!==null,'running app stopped for update');
    assert.equal(await fs.stat(startup).then(()=>true,()=>false),false,'disabled startup remains disabled on update');
    assert.equal(await fs.readFile(file,'utf8'),original);
    assert.ok((await fs.readdir(path.join(dataDir,'backups'))).some(n=>n.startsWith('calendar-upgrade-')));
    app=new DesktopDriver(dataDir,path.join(temp,'profile'),path.join(appDir,'PetCalendar.exe'));await app.ready;
    console.log('CHECK uninstall with app running');assert.equal(await run(path.join(appDir,'RemovePetCalendar.exe'),['--qa','/S']),0);
    await waitFor(()=>app.child.exitCode!==null,'running app stopped for uninstall');
    await waitFor(async()=>{try{await fs.stat(path.join(appDir,'PetCalendar.exe'));return false;}catch{return true;}},'uninstalled files',10000);
    await waitFor(async()=>!(await fs.stat(appDir).then(()=>true,()=>false)),'uninstall complete');
    assert.equal(await fs.readFile(file,'utf8'),original);
    console.log('CHECK reinstall');assert.equal(await run(setup,['/S','/DATA='+dataDir,'/D='+appDir]),0);
    assert.equal(await fs.readFile(file,'utf8'),original);
    assert.equal(await run(path.join(appDir,'RemovePetCalendar.exe'),['--qa','/S']),0);
    await waitFor(async()=>!(await fs.stat(path.join(appDir,'RemovePetCalendar.exe')).then(()=>true,()=>false)),'reinstalled app removed');
    console.log(JSON.stringify({installer:'per-user install/update/uninstall/reinstall passed',dataPreserved:true}));
  }finally{
    if(app)await app.close();
    if(await fs.stat(path.join(appDir,'RemovePetCalendar.exe')).then(()=>true,()=>false)){await run(path.join(appDir,'RemovePetCalendar.exe'),['--qa','/S']).catch(()=>{});await delay(2500);}
    const resolved=path.resolve(temp);assert.ok(resolved.startsWith(path.resolve(os.tmpdir())+path.sep));await fs.rm(resolved,{recursive:true,force:true}).catch(()=>{});
    const installRoot=path.dirname(appDir);assert.ok(path.resolve(installRoot).startsWith(path.resolve(root,'artifacts')+path.sep));await fs.rm(installRoot,{recursive:true,force:true}).catch(()=>{});
  }
});
