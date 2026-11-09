import test from 'node:test';
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createInterface} from 'node:readline';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {defaultState} from '../desktop/ui/model.mjs';

const root=fileURLToPath(new URL('../',import.meta.url));
const executable=process.env.PET_CALENDAR_TEST_EXE||path.join(root,'dist/app/PetCalendar.exe');
class App {
  constructor(data){
    this.pending=new Map();this.id=0;
    this.child=spawn(executable,['--automation','--data-dir',data,'--profile-dir',path.join(data,'profile')],{cwd:root,stdio:['pipe','pipe','pipe']});
    this.exited=new Promise(resolve=>this.child.once('exit',resolve));
    this.ready=new Promise((resolve,reject)=>{
      this.reader=createInterface({input:this.child.stdout});
      this.reader.on('line',line=>{
        let m;try{m=JSON.parse(line.replace(/^\uFEFF/,''));}catch{return;}
        if(m.kind==='ready'){resolve(m);return;}if(m.kind==='fatal'){reject(new Error(m.error));return;}
        const p=this.pending.get(m.id);if(!p)return;clearTimeout(p.timer);this.pending.delete(m.id);m.ok?p.resolve(m):p.reject(new Error(m.error));
      });
      this.child.once('error',reject);
    });
    this.child.stdin.on('error',()=>{});
  }
  call(action,extra={}){const id=++this.id;return new Promise((resolve,reject)=>{const timer=setTimeout(()=>{this.pending.delete(id);reject(new Error('Command timed out: '+action));},15000);this.pending.set(id,{resolve,reject,timer});this.child.stdin.write(JSON.stringify({id,action,...extra})+'\n');});}
  api(method,path,body){return this.call('api',{method,path,...(body===undefined?{}:{body})});}
  async close(){if(this.child.exitCode===null)await this.call('exit');await this.exited;this.reader.close();}
}

test('affinity badges upgrade from star to green and sparkling hearts and survive restart',{timeout:60000},async()=>{
  const data=await fs.mkdtemp(path.join(os.tmpdir(),'pet-affinity-badges-'));let app;
  const state=defaultState();state.settings.hotkey.enabled=false;state.settings.fontFamily='childfund';state.settings.notificationSound=false;
  state.tasks=[{id:'done',title:'이름표 갱신 검증',dueDate:'',done:false,createdAt:new Date().toISOString()}];
  await fs.writeFile(path.join(data,'calendar.json'),JSON.stringify(state));
  await fs.writeFile(path.join(data,'ui-state.json'),JSON.stringify({quickTourDone:true,hotkeyMoved:true,drafts:{}}));
  await fs.writeFile(path.join(data,'pets.json'),JSON.stringify({enabled:true,allowEmpty:true,summaryEnabled:false,chatterEnabled:false,bedEnabled:false,toyEnabled:false,snacks:2,pets:[146,396,896].map((affinity,i)=>({species:'cat',palette:'치즈',name:'이름표'+(i+1),personality:3,affinity,fedAt:''}))}));
  try{
    app=new App(data);await app.ready;
    let pets=(await app.api('GET','/api/pets')).body;
    assert.deepEqual(pets.affinity.map(p=>p.level),[2,3,4]);assert.deepEqual(pets.affinity.map(p=>p.hearts),['🌟','🌟','💚']);assert.equal(pets.snacks,2);
    const current=(await app.api('GET','/api/state')).body;current.tasks[0].done=true;assert.equal((await app.api('PUT','/api/state',current)).status,200);
    pets=(await app.api('GET','/api/pets')).body;
    assert.deepEqual(pets.affinity.map(p=>p.level),[3,4,5]);assert.deepEqual(pets.affinity.map(p=>p.hearts),['🌟','💚','💖']);assert.equal(pets.snacks,3);
    assert.deepEqual(pets.affinity.map(p=>p.name),['이름표1','이름표2','이름표3']);
    await app.close();app=new App(data);await app.ready;pets=(await app.api('GET','/api/pets')).body;
    assert.deepEqual(pets.affinity.map(p=>p.level),[3,4,5]);assert.deepEqual(pets.affinity.map(p=>p.hearts),['🌟','💚','💖']);assert.equal(pets.snacks,3);
    assert.equal((await app.api('GET','/api/state')).body.settings.fontFamily,'childfund');
  }finally{if(app)await app.close();await fs.rm(data,{recursive:true,force:true,maxRetries:10,retryDelay:200});}
});
