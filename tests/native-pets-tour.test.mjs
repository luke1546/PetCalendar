// Native replacements for the web-driver pet and tour tests.
// They use the native app's --automation protocol (api, pets-inspect, menu) instead of
// DOM access, so they run against the WinForms build. DOM-only steps (settings checkbox,
// tour buttons) are verified by screenshots, not here.
import test from 'node:test';
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createInterface} from 'node:readline';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {defaultState,dateKey} from '../desktop/ui/model.mjs';

const root=fileURLToPath(new URL('../',import.meta.url));
const executable=process.env.PET_CALENDAR_TEST_EXE||path.join(root,'dist/app/PetCalendar.exe');
const delay=ms=>new Promise(r=>setTimeout(r,ms));
async function waitFor(fn,label,timeout=20000){const at=Date.now();let last;while(Date.now()-at<timeout){try{if(await fn())return;}catch(e){last=e;}await delay(150);}throw new Error('Timeout: '+label+(last?' '+last:''));}

class NativeDriver {
  constructor(dataDir,profileDir){
    this.next=0;this.pending=new Map();
    this.child=spawn(executable,['--automation','--data-dir',dataDir,'--profile-dir',profileDir],{cwd:root,windowsHide:false,stdio:['pipe','pipe','pipe']});
    this.exited=new Promise(r=>this.child.on('exit',r));
    this.ready=new Promise((resolve,reject)=>{
      const rl=createInterface({input:this.child.stdout});
      rl.on('line',line=>{
        let m;try{m=JSON.parse(line.replace(/^﻿/,''));}catch{return;}
        if(m.kind==='ready'){resolve(m);return;}
        if(m.kind==='fatal'){reject(new Error(m.error));return;}
        const p=this.pending.get(m.id);if(!p)return;this.pending.delete(m.id);
        m.ok?p.resolve(m):p.reject(new Error(m.error));
      });
      this.child.on('error',reject);
    });
  }
  command(action,extra={}){
    const id=++this.next;
    return new Promise((resolve,reject)=>{
      const timer=setTimeout(()=>{this.pending.delete(id);reject(new Error('Command timeout '+action));},15000);
      this.pending.set(id,{resolve:v=>{clearTimeout(timer);resolve(v);},reject:e=>{clearTimeout(timer);reject(e);}});
      this.child.stdin.write(JSON.stringify({id,action,...extra})+'\n');
    });
  }
  // Api replies carry the HTTP-like status (200/400/403/404/409/422) in `status`.
  api(method,path,body,{marker='1',force=false}={}){return this.command('api',{method,path,body,marker,force});}
  inspect(){return this.command('pets-inspect').then(r=>r.pets);}
  async close(){
    if(this.child.exitCode===null){
      try{await this.command('menu',{item:'exit'});}catch{}
      const done=await Promise.race([this.exited.then(()=>true),delay(5000).then(()=>false)]);
      if(!done)this.child.kill();
    }
    await this.exited;
  }
}

async function freshData(temp,name,calendar={},pets){
  const dir=path.join(temp,name);await fs.mkdir(dir,{recursive:true});
  const state=defaultState();state.settings.hotkey.enabled=false;state.settings.notificationSound=false;
  Object.assign(state,calendar);
  await fs.writeFile(path.join(dir,'calendar.json'),JSON.stringify(state));
  if(pets)await fs.writeFile(path.join(dir,'pets.json'),JSON.stringify(pets));
  return dir;
}

// A truly new install has no calendar.json or pets.json; the app then creates the default cat.
async function emptyData(temp,name){const dir=path.join(temp,name);await fs.mkdir(dir,{recursive:true});return dir;}

async function startApp(dir,temp){
  const app=new NativeDriver(dir,path.join(temp,path.basename(dir)+'-profile'));
  await app.ready;
  return app;
}

test('[native] 새 설치 기본 고양이와 기존 사용자 설정 유지',{timeout:90000},async()=>{
  const temp=await fs.mkdtemp(path.join(os.tmpdir(),'pet-native-default-'));let app;
  try{
    const data=await emptyData(temp,'new');
    app=await startApp(data,temp);
    await waitFor(async()=>(await app.api('GET','/api/pets')).body.count===1,'default cat');
    let pets=await app.inspect();
    assert.equal(pets.enabled,true);assert.equal(pets.pets.length,1);assert.equal(pets.pets[0].species,'cat');
    assert.ok(pets.pets[0].palette);assert.ok(pets.pets[0].personality>=1&&pets.pets[0].personality<=5);
    const selected={palette:pets.pets[0].palette,personality:pets.pets[0].personality};
    await app.close();

    app=await startApp(data,temp);pets=await app.inspect();
    assert.equal(pets.pets[0].palette,selected.palette,'palette persists across restart');
    assert.equal(pets.pets[0].personality,selected.personality,'personality persists across restart');
    await app.api('POST','/api/pets',{action:'disable'});
    await app.close();

    app=await startApp(data,temp);
    assert.equal((await app.api('GET','/api/pets')).body.enabled,false,'disabled state persists');
    await app.close();

    const old=await freshData(temp,'old');
    app=await startApp(old,temp);
    assert.equal((await app.api('GET','/api/pets')).body.enabled,false,'existing user with no pet preference stays off');
  }finally{if(app)await app.close();await fs.rm(temp,{recursive:true,force:true,maxRetries:10,retryDelay:500}).catch(()=>{});}
});

test('[native] 동물 사용: 기본 꺼짐, 7종, 이름 규칙, 재시작 보존, 캘린더 파일 불변',{timeout:120000},async()=>{
  const temp=await fs.mkdtemp(path.join(os.tmpdir(),'pet-native-pets-'));let app;
  try{
    const data=await freshData(temp,'data',{tasks:[{id:'pet-test',title:'동물 설정과 독립적인 할 일',dueDate:dateKey(),done:false,createdAt:new Date().toISOString()}]});
    const file=path.join(data,'calendar.json');const original=await fs.readFile(file,'utf8');
    app=await startApp(data,temp);
    let state=await app.inspect();
    assert.equal(state.enabled,false);assert.equal(state.count,0);assert.equal(state.packLoaded,false);assert.equal(state.decodedAtlases,0);assert.equal(state.loopCount,0);
    assert.equal((await app.api('POST','/api/pets',{action:'enable'},{marker:''})).status,403,'change without marker is refused');
    assert.equal((await app.api('POST','/api/pets',{action:'spawn',species:'cat'})).status,409,'spawn while disabled is refused');
    await app.api('POST','/api/pets',{action:'enable'});
    await waitFor(async()=>(await app.inspect()).count===1,'cat enabled');
    state=await app.inspect();assert.equal(state.pets[0].species,'cat');assert.equal(state.packLoaded,true);assert.equal(state.decodedAtlases,1);assert.equal(state.loopCount,1);
    assert.equal((await app.api('POST','/api/pets',{action:'spawn',species:'unknown'})).status,400);
    for(const species of ['dog','redPanda','capybara','squirrel','alpaca','elephant'])
      assert.equal((await app.api('POST','/api/pets',{action:'spawn',species})).status,200);
    state=await app.inspect();
    assert.equal(state.count,7);assert.equal(new Set(state.pets.map(p=>p.species)).size,7);assert.equal(state.decodedAtlases,7);
    assert.equal(state.pets.find(p=>p.species==='dog').palette,'하양');
    assert.equal(state.pets.find(p=>p.species==='alpaca').palette,'하양');
    assert.equal(state.pets.find(p=>p.species==='capybara').palette,'갈색');
    assert.ok(['기본 회색','밝은 회색'].includes(state.pets.find(p=>p.species==='elephant').palette));
    const cat=state.pets.find(p=>p.species==='cat'),dog=state.pets.find(p=>p.species==='dog');
    assert.equal(cat.name,'','older preferences default to unnamed');
    const name='호두 🐾 & <친구>';
    assert.equal((await app.api('POST','/api/pets',{action:'rename',window:cat.window,name})).status,200);
    assert.equal((await app.api('POST','/api/pets',{action:'rename',window:dog.window,name:'구름'})).status,200);
    for(const invalid of ['가'.repeat(25),'줄\n바꿈',null,true])
      assert.equal((await app.api('POST','/api/pets',{action:'rename',window:cat.window,name:invalid})).status,400);
    assert.equal((await app.api('POST','/api/pets',{action:'rename',window:cat.window,name:'변경 금지'},{marker:''})).status,403);
    assert.equal((await app.api('POST','/api/pets',{action:'rename',window:0,name:'없는 펫'})).status,404);
    assert.equal((await app.inspect()).pets.find(p=>p.species==='cat').name,name);
    assert.equal(await fs.readFile(file,'utf8'),original,'calendar bytes are not rewritten by pets');
    await app.close();

    app=await startApp(data,temp);
    state=await app.inspect();
    assert.equal(state.enabled,true);assert.equal(state.count,7,'opted-in pets return on restart');
    assert.equal(state.pets.find(p=>p.species==='cat').name,name,'name persists after restart');
    assert.equal(state.pets.find(p=>p.species==='dog').name,'구름','different pets keep separate names');
    assert.equal((await app.api('POST','/api/pets',{action:'rename',window:state.pets.find(p=>p.species==='cat').window,name:'  '})).status,200);
    assert.equal((await app.inspect()).pets.find(p=>p.species==='cat').name,'','empty name restores default display');
    await app.api('POST','/api/pets',{action:'disable'});
    state=await app.inspect();
    assert.equal(state.count,0);assert.equal(state.packLoaded,false);assert.equal(state.decodedAtlases,0);assert.equal(state.cacheBytes,0);assert.equal(state.loopCount,0);
    await app.close();

    app=await startApp(data,temp);
    state=await app.inspect();
    assert.equal(state.enabled,false);assert.equal(state.packLoaded,false);assert.equal(state.decodedAtlases,0);
    await app.api('POST','/api/pets',{action:'enable'});
    assert.equal((await app.inspect()).count,7,'enable restores chosen companions');
    await app.api('POST','/api/pets',{action:'disable'});
    assert.equal(await fs.readFile(file,'utf8'),original);
    assert.equal(await fs.stat(path.join(data,'pets.json')).then(()=>true,()=>false),true);
    assert.equal(await fs.stat(path.join(data,'meowser-atlas.png')).then(()=>true,()=>false),false,'images are not extracted to disk');
  }finally{if(app)await app.close();await fs.rm(temp,{recursive:true,force:true,maxRetries:10,retryDelay:500}).catch(()=>{});}
});

test('[native] 퀵투어 고양이 추가 및 꺼진 펫 활성화 (투어 추가 API)',{timeout:120000},async()=>{
  const temp=await fs.mkdtemp(path.join(os.tmpdir(),'pet-native-tour-'));let app;
  try{
    const fresh=await emptyData(temp,'fresh');
    app=await startApp(fresh,temp);
    await waitFor(async()=>(await app.api('GET','/api/pets')).body.count===1,'default cat restored');
    const addCat=async expected=>{
      const reply=await app.api('POST','/api/pets',{action:'tour-spawn'});
      assert.equal(reply.status,200,'tour add succeeds');
      await waitFor(async()=>(await app.api('GET','/api/pets')).body.count===expected,'cat added');
      const pets=await app.inspect();
      assert.equal(pets.enabled,true);
      assert.equal(pets.pets.filter(p=>p.species==='cat').length,expected);
    };
    await addCat(2);
    await app.close();

    const old=await freshData(temp,'old',{},{enabled:false,allowEmpty:false,pets:[],summaryEnabled:true,chatterEnabled:true,summaryIntervalMinutes:3});
    app=await startApp(old,temp);
    assert.equal((await app.api('GET','/api/pets')).body.enabled,false);
    await addCat(1);
    await app.api('POST','/api/pets',{action:'disable'});
    assert.equal((await app.api('GET','/api/pets')).body.enabled,false);
    await addCat(2);
  }finally{if(app)await app.close();await fs.rm(temp,{recursive:true,force:true,maxRetries:10,retryDelay:500}).catch(()=>{});}
});
