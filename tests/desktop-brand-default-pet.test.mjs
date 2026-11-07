import test from 'node:test';
import assert from 'node:assert/strict';
import {promises as fs} from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {defaultState} from '../desktop/ui/model.mjs';
import {DesktopDriver,waitFor} from './desktop-driver.mjs';

test('새 설치 기본 고양이와 기존 사용자 설정 유지', {timeout:60000}, async()=>{
 const temp=await fs.mkdtemp(path.join(os.tmpdir(),'pet-calendar-default-'));let app;
 try {
  const data=path.join(temp,'new'),profile=path.join(temp,'profile');await fs.mkdir(data);
  app=new DesktopDriver(data,profile);await app.ready;
  await waitFor(async()=>(await app.api('GET','/api/pets')).body.count===1,'default cat');
  let pets=(await app.command('pets-inspect')).pets;assert.equal(pets.enabled,true);assert.equal(pets.pets.length,1);assert.equal(pets.pets[0].species,'cat');assert.ok(pets.pets[0].palette);assert.ok(pets.pets[0].personality>=1&&pets.pets[0].personality<=5);
  assert.equal(await app.js("document.querySelector('.brand h1').textContent.trim()"),'펫 캘린더');assert.equal(await app.js("!!document.querySelector('.brand h1 .brand-paw')"),true);
  const selected={palette:pets.pets[0].palette,personality:pets.pets[0].personality};await app.close();
  app=new DesktopDriver(data,profile);await app.ready;pets=(await app.command('pets-inspect')).pets;assert.equal(pets.pets[0].palette,selected.palette);assert.equal(pets.pets[0].personality,selected.personality);
  await app.api('POST','/api/pets',{action:'disable'});await app.close();app=new DesktopDriver(data,profile);await app.ready;assert.equal((await app.api('GET','/api/pets')).body.enabled,false);await app.close();
  const old=path.join(temp,'old'),oldProfile=path.join(temp,'old-profile');await fs.mkdir(old);const oldState=defaultState();oldState.settings.hotkey.enabled=false;await fs.writeFile(path.join(old,'calendar.json'),JSON.stringify(oldState));
  app=new DesktopDriver(old,oldProfile);await app.ready;assert.equal((await app.api('GET','/api/pets')).body.enabled,false);
 }finally{if(app)await app.close();await fs.rm(temp,{recursive:true,force:true,maxRetries:10,retryDelay:500});}
});
