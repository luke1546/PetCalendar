import test from 'node:test';
import assert from 'node:assert/strict';
import {promises as fs} from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {DesktopDriver,waitFor} from './desktop-driver.mjs';
import {defaultState,dateKey,addDays} from '../desktop/ui/model.mjs';

test('할 일을 달력으로 이동하고 되돌리기',{timeout:60000},async()=>{
  const temp=await fs.mkdtemp(path.join(os.tmpdir(),'pet-task-drag-')),data=path.join(temp,'data');await fs.mkdir(data);
  const today=dateKey().slice(0,7)+'-08',target=addDays(today,2),initial=defaultState();
  initial.settings.hotkey.enabled=false;initial.settings.hideCompleted=false;initial.settings.defaultReminder=30;
  initial.tasks=[{id:'todo',title:'보고서 준비',dueDate:today,done:false,createdAt:'2026-01-01T00:00:00Z'},
    {id:'done',title:'완료한 할 일',dueDate:'',done:true,createdAt:'2026-01-02T00:00:00Z'},
    {id:'fail',title:'보존할 할 일',dueDate:'',done:false,createdAt:'2026-01-03T00:00:00Z'}];
  await fs.writeFile(path.join(data,'calendar.json'),JSON.stringify(initial));const app=new DesktopDriver(data,path.join(temp,'profile'));
  const drag=async id=>app.js(`(()=>{const source=document.querySelector('[data-drag-task="${id}"]'),target=document.querySelector('[data-date="${target}"]');if(!source||!target)throw new Error('Missing task or date');const transfer=new DataTransfer();source.dispatchEvent(new DragEvent('dragstart',{bubbles:true,cancelable:true,dataTransfer:transfer}));target.dispatchEvent(new DragEvent('dragover',{bubbles:true,cancelable:true,dataTransfer:transfer}));target.dispatchEvent(new DragEvent('drop',{bubbles:true,cancelable:true,dataTransfer:transfer}));return true})()`);
  try{
    await app.ready;await waitFor(()=>app.js('document.getElementById("quickTour").open'),'tour');await app.js('document.getElementById("tourSkip").click();true');
    await drag('todo');await waitFor(async()=>(await app.state()).events.some(e=>e.id==='todo'),'task converted');
    let state=await app.state();assert.equal(state.tasks.some(t=>t.id==='todo'),false);
    let event=state.events.find(e=>e.id==='todo');assert.equal(event.title,'보고서 준비');assert.equal(event.startDate,target);assert.equal(event.endDate,target);assert.equal(event.startTime,'');assert.equal(event.reminder,30);assert.deepEqual(event.completedDates,[]);
    await app.js('[...document.querySelectorAll(".toast button")].find(b=>b.textContent==="되돌리기").click();true');
    await waitFor(async()=>(await app.state()).tasks.some(t=>t.id==='todo'),'conversion undone');state=await app.state();assert.equal(state.events.some(e=>e.id==='todo'),false);assert.equal(state.tasks.find(t=>t.id==='todo').dueDate,today);
    await drag('done');await waitFor(async()=>(await app.state()).events.some(e=>e.id==='done'),'completed task converted');
    state=await app.state();event=state.events.find(e=>e.id==='done');assert.deepEqual(event.completedDates,[target]);assert.equal(state.tasks.some(t=>t.id==='done'),false);
    await app.js('window.taskTestFetch=window.fetch;window.fetch=(url,options)=>options?.method==="PUT"?Promise.resolve(new Response(JSON.stringify({error:"test failure"}),{status:500,headers:{"Content-Type":"application/json"}})):window.taskTestFetch(url,options);true');
    await drag('fail');await waitFor(()=>app.js('!document.getElementById("errorBanner").hidden'),'failed save shown');
    await app.js('window.fetch=window.taskTestFetch;true');state=await app.state();assert.ok(state.tasks.some(t=>t.id==='fail'));assert.equal(state.events.some(e=>e.id==='fail'),false);
  }finally{await app.close();await fs.rm(temp,{recursive:true,force:true,maxRetries:10,retryDelay:500});}
});
