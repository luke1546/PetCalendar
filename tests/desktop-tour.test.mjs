import test from 'node:test';
import assert from 'node:assert/strict';
import {promises as fs} from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {DesktopDriver,waitFor} from './desktop-driver.mjs';
import {defaultState} from '../desktop/ui/model.mjs';

test('퀵투어에서 고양이 추가 및 꺼진 펫 활성화',{timeout:60000},async()=>{
  const temp=await fs.mkdtemp(path.join(os.tmpdir(),'pet-tour-add-'));
  let app;
  const openPetStep=async()=>{
    await waitFor(()=>app.js('document.getElementById("quickTour").open'),'tour shown');
    await app.js('document.getElementById("tourNext").click();document.getElementById("tourNext").click();true');
    assert.equal(await app.js('document.getElementById("tourAddPet").tagName'),'BUTTON');
  };
  const addCat=async expected=>{
    await app.js('document.getElementById("tourAddPet").click();true');
    await waitFor(async()=>(await app.api('GET','/api/pets')).body.count===expected,'cat added');
    await waitFor(()=>app.js('document.getElementById("tourPetStatus").textContent.includes("데려왔어요")'),'success shown');
    const pets=(await app.command('pets-inspect')).pets;
    assert.equal(pets.enabled,true);
    assert.equal(pets.pets.filter(p=>p.species==='cat').length,expected);
  };
  try{
    const fresh=path.join(temp,'fresh');await fs.mkdir(fresh);
    app=new DesktopDriver(fresh,path.join(temp,'fresh-profile'));await app.ready;
    await waitFor(async()=>(await app.api('GET','/api/pets')).body.count===1,'default cat restored');
    await openPetStep();await addCat(2);
    await app.js('document.getElementById("tourSkip").click();true');
    assert.equal(await app.js('localStorage.getItem("petCalendar.quickTour.v1")'),'done');
    await app.js('document.getElementById("tourHelp").click();true');
    assert.equal(await app.js('document.getElementById("quickTour").open'),true);
    assert.equal(await app.js('document.getElementById("tourProgress").textContent'),'1 / 3');
    assert.equal(await app.js('document.getElementById("tourTitle").textContent'),'일정을 추가해 보세요');
    await app.close();
    const old=path.join(temp,'old');await fs.mkdir(old);await fs.writeFile(path.join(old,'calendar.json'),JSON.stringify(defaultState()));
    await fs.writeFile(path.join(old,'pets.json'),JSON.stringify({enabled:false,allowEmpty:false,pets:[],summaryEnabled:true,chatterEnabled:true,summaryIntervalMinutes:3}));
    app=new DesktopDriver(old,path.join(temp,'old-profile'));await app.ready;
    assert.equal((await app.api('GET','/api/pets')).body.enabled,false);
    await openPetStep();await addCat(1);
    await app.api('POST','/api/pets',{action:'disable'});
    assert.equal((await app.api('GET','/api/pets')).body.enabled,false);
    await addCat(2);
  }finally{if(app)await app.close();await fs.rm(temp,{recursive:true,force:true,maxRetries:10,retryDelay:500});}
});
