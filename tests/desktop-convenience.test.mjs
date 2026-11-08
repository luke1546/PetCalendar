import test from 'node:test';
import assert from 'node:assert/strict';
import {promises as fs} from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {DesktopDriver,waitFor} from './desktop-driver.mjs';
import {defaultState,dateKey,addDays} from '../desktop/ui/model.mjs';

test('일정 드래그 이동과 글씨 크기 저장',{timeout:60000},async()=>{
  const temp=await fs.mkdtemp(path.join(os.tmpdir(),'pet-convenience-')),data=path.join(temp,'data'),profile=path.join(temp,'profile');await fs.mkdir(data);
  const today=dateKey().slice(0,7)+"-08",tomorrow=addDays(today,1),after=addDays(today,2),initial=defaultState();initial.settings.hotkey.enabled=false;
  const event=(id,repeat='none')=>({id,title:id,startDate:today,endDate:today,startTime:'15:00',endTime:'16:00',category:'work',note:'',repeat,repeatUntil:'',reminder:10,pinned:false,pinnedDate:'',completedDates:[],excludedDates:[]});
  initial.events=[event('single'),event('weekly','weekly')];await fs.writeFile(path.join(data,'calendar.json'),JSON.stringify(initial));
  let app=new DesktopDriver(data,profile);
  const drag=async(id,to)=>{
    const payload=JSON.stringify({id,to});
    await app.js(`(()=>{const {id,to}=${payload},source=document.querySelector('[data-drag-event="'+id+'"]'),target=document.querySelector('[data-date="'+to+'"]');if(!source||!target)throw new Error('Missing drag target');const data=new DataTransfer();source.dispatchEvent(new DragEvent('dragstart',{bubbles:true,cancelable:true,dataTransfer:data}));target.dispatchEvent(new DragEvent('dragover',{bubbles:true,cancelable:true,dataTransfer:data}));target.dispatchEvent(new DragEvent('drop',{bubbles:true,cancelable:true,dataTransfer:data}));return true})()`);
  };
  try{
    await app.ready;await waitFor(()=>app.js('document.getElementById("quickTour").open'),'tour');await app.js('document.getElementById("tourSkip").click();true');
    await drag('single',tomorrow);await waitFor(async()=>(await app.state()).events.find(e=>e.id==='single').startDate===tomorrow,'single moved');
    let moved=(await app.state()).events.find(e=>e.id==='single');assert.equal(moved.endDate,tomorrow);assert.equal(moved.startTime,'15:00');assert.equal(moved.endTime,'16:00');
    await app.js(`document.querySelector('[data-date="${today}"]').click();true`);
    await drag('weekly',after);await waitFor(()=>app.js('document.getElementById("repeatScopeDialog").open'),'repeat scope');
    await app.js('document.getElementById("repeatScopeOne").click();true');
    await waitFor(async()=>(await app.state()).events.length===3,'single occurrence moved');
    const events=(await app.state()).events;assert.ok(events.find(e=>e.id==='weekly').excludedDates.includes(today));moved=events.find(e=>e.id!=='weekly'&&e.id!=='single');assert.equal(moved.repeat,'none');assert.equal(moved.startDate,after);assert.equal(moved.startTime,'15:00');
    const weeklyDay=await app.js(`document.querySelector('[data-drag-event="weekly"]').dataset.dragDay`),weeklyTarget=addDays(weeklyDay,1);
    await drag('weekly',weeklyTarget);await waitFor(()=>app.js('document.getElementById("repeatScopeDialog").open'),'repeat move confirmation');
    await app.js('document.getElementById("repeatScopeCancel").click();true');assert.equal((await app.state()).events.find(e=>e.id==='weekly').startDate,today);
    await drag('weekly',weeklyTarget);await waitFor(()=>app.js('document.getElementById("repeatScopeDialog").open'),'repeat move confirmation');
    await app.js('document.getElementById("repeatScopeAll").click();true');await waitFor(async()=>(await app.state()).events.find(e=>e.id==='weekly').startDate===tomorrow,'series moved');
    const shifted=(await app.state()).events.find(e=>e.id==='weekly');assert.equal(shifted.repeat,'weekly');assert.ok(shifted.excludedDates.includes(tomorrow));
    const now=new Date(),offset=(now.getFullYear()-2026)*12+(now.getMonth()+1-7);
    if(Math.abs(offset)<120){await app.js(`for(let i=0;i<${Math.abs(offset)};i++)document.getElementById("${offset>=0?'prevMonth':'nextMonth'}").click();true`);
      assert.equal(await app.js(`document.querySelector('[data-date="2026-07-17"] .holiday-label')?.textContent`),'제헌절');
      await app.js('document.getElementById("goToday").click();true');}
    const widgetSmall=(await app.command('widget',{command:'layout'})).layout.fontSize;assert.equal(widgetSmall,10);
    const before=await app.js('parseFloat(getComputedStyle(document.getElementById("monthLabel")).fontSize)');
    await app.js('document.getElementById("settingsButton").click();document.getElementById("textScale").value="1";document.getElementById("textScale").dispatchEvent(new Event("change",{bubbles:true}));true');
    await waitFor(async()=>(await app.state()).settings.textScale===1,'font setting saved');
    await waitFor(()=>app.js('parseFloat(getComputedStyle(document.getElementById("monthLabel")).fontSize)>0'),'font applied');
    const normal=await app.js('parseFloat(getComputedStyle(document.getElementById("monthLabel")).fontSize)');assert.ok(normal>before);
    assert.equal((await app.command('widget',{command:'layout'})).layout.fontSize,11);
    await app.js('document.getElementById("textScale").value="2";document.getElementById("textScale").dispatchEvent(new Event("change",{bubbles:true}));true');
    await waitFor(async()=>(await app.state()).settings.textScale===2,'large font saved');
    assert.equal((await app.command('widget',{command:'layout'})).layout.fontSize,12);
    assert.ok((await app.js('parseFloat(getComputedStyle(document.getElementById("monthLabel")).fontSize)'))>normal);
    assert.equal((await app.api('POST','/api/pets',{action:'enable'})).status,200);
    assert.equal((await app.api('POST','/api/pets',{action:'preview'})).status,200);
    await waitFor(async()=>Boolean((await app.command('pets-inspect')).pets.speech.bubble?.visible),'pet speech displayed');
    assert.equal((await app.command('pets-inspect')).pets.speech.bubble.fontSize,13);
    await app.js('document.getElementById("textScale").value="0";document.getElementById("textScale").dispatchEvent(new Event("change",{bubbles:true}));true');
    await waitFor(async()=>(await app.command('pets-inspect')).pets.speech.bubble.fontSize===11,'open bubble font changed');
    await app.js('document.getElementById("textScale").value="2";document.getElementById("textScale").dispatchEvent(new Event("change",{bubbles:true}));true');
    await waitFor(async()=>(await app.state()).settings.textScale===2,'large font restored');
    assert.equal((await app.command('pets-inspect')).pets.speech.bubble.fontSize,13);
    await app.js('document.querySelector("[data-close=settingsDialog]").click();true');
    for(const [width,height] of [[1440,900],[768,1000],[390,844]]){
      await app.command('devtools',{method:'Emulation.setDeviceMetricsOverride',parameters:{width,height,deviceScaleFactor:1,mobile:false}});
      assert.equal(await app.js('document.documentElement.scrollWidth<=innerWidth'),true,`large font horizontal ${width}`);
    }
    await app.command('devtools',{method:'Emulation.clearDeviceMetricsOverride'});
    await app.close();app=new DesktopDriver(data,profile);await app.ready;
    assert.equal((await app.state()).settings.textScale,2);
    assert.equal((await app.command('widget',{command:'layout'})).layout.fontSize,12);
    assert.ok((await app.js('parseFloat(getComputedStyle(document.getElementById("monthLabel")).fontSize)'))>before);
    async function openDate(date){
      await app.js(`window.petCalendarOpenEvent(null,"${date}");true`);
      await waitFor(()=>app.js('document.getElementById("eventDialog").open'),'date opened');
      await app.js('document.querySelector("[data-close=eventDialog]").click();true');
    }
    await openDate('2020-08-17');
    assert.match(await app.js(`document.querySelector('[data-date="2020-08-17"] .holiday-label')?.textContent`),/임시공휴일/);
    await openDate('2050-01-23');
    assert.match(await app.js(`document.querySelector('[data-date="2050-01-23"] .holiday-label')?.textContent`),/설날/);
    assert.match(await app.js(`document.querySelector('[data-date="2050-01-23"]').getAttribute('aria-label')`),/예상/);
    assert.match(await app.js('document.getElementById("visibleCount").textContent'),/공휴일 예상/);
    await openDate('2051-01-01');
    assert.match(await app.js('document.getElementById("visibleCount").textContent'),/공휴일 자료 없음/);

  }finally{await app.close();await fs.rm(temp,{recursive:true,force:true,maxRetries:10,retryDelay:500});}
});

test('공식 공휴일 자료에 겹친 공휴일과 최근 변경사항이 포함된다',async()=>{
  const data=JSON.parse(await fs.readFile(new URL('../desktop/ui/holidays.json',import.meta.url),'utf8'));
  assert.match(data['2025-05-05'],/어린이날/);assert.match(data['2025-05-05'],/부처님오신날/);
  assert.match(data['2026-05-01'],/노동절/);assert.match(data['2026-07-17'],/제헌절/);
  assert.match(data['2027-07-19'],/대체공휴일/);
  assert.match(data['2020-08-17'],/임시공휴일/);assert.match(data['2024-10-01'],/임시공휴일/);
  assert.equal(data['2025-10-10'],undefined);assert.match(data['2028-10-05'],/대체공휴일/);
  assert.match(data['2033-01-31'],/설날/);assert.match(data['2050-01-23'],/설날/);
  assert.equal(data['2028-04-12'],undefined);assert.equal(data['2051-01-01'],undefined);
});
