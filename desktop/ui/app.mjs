import { CATEGORIES, HOTKEY_KEYS, defaultHotkey, validateHotkey, hotkeyLabel, dateKey, addDays, dayNumber, defaultState, validateState, indexDates, occurrences, occursOn, onDate, dday, ddayOccurrence, selectedDday, isEventDone, reminderTime, repeatLabel } from './model.mjs';

const $ = id => document.getElementById(id);
const nativeDesktop = globalThis.petCalendarDesktop === true;
const esc = value => String(value ?? '').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const paths = {
  repeat:'M20 7h-5m5 0V2M4 17h5m-5 0v5M20 7a8 8 0 0 0-14-3M4 17a8 8 0 0 0 14 3',
  calendar:'M8 2v4m8-4v4M3 10h18M5 4h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2Zm3 10h2m4 0h2m-8 4h2',
  check:'m5 12 4 4L19 6M20 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h9',
  flag:'M5 22V3m0 1c5-4 9 4 14 0v11c-5 4-9-4-14 0',
  left:'m14 5-7 7 7 7',right:'m9 5 7 7-7 7',plus:'M12 5v14M5 12h14',close:'m6 6 12 12M18 6 6 18',
  minimize:'M5 12h14',maximize:'M5 5h14v14H5Z',restore:'M8 8h11v11H8ZM5 16V5h11',
  search:'m21 21-5-5M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Z',
  bell:'M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9ZM10 21h4',
  settings:'m9 3 .5-1h5l.5 3 2 1.2 2.8-1 2.5 4.3-2.3 2v2.4l2.3 2-2.5 4.3-2.8-1-2 1.2-.5 3h-5l-.5-3L7 19.2l-2.8 1-2.5-4.3 2.3-2v-2.4l-2.3-2 2.5-4.3 2.8 1L9 5ZM15.5 12a3.5 3.5 0 1 1-7 0 3.5 3.5 0 0 1 7 0Z',
  download:'M12 3v12m-5-5 5 5 5-5M4 15v5a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-5',
  upload:'M12 16V4m-5 5 5-5 5 5M4 15v5a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-5',
  lock:'M6 10V7a6 6 0 0 1 12 0v3M5 10h14v12H5V10Zm7 5v3',
  trash:'M3 6h18M9 6V3h6v3M5 6l1 15h12l1-15M10 10v7m4-7v7'
};
const icon = name => `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="${paths[name] || paths.calendar}"/></svg>`;
document.querySelectorAll('[data-icon]').forEach(el=>{el.innerHTML=icon(el.dataset.icon);});
$('toastRegion').setAttribute('popover','manual');
let state = defaultState(), selected = dateKey(), month = selected.slice(0,7), filter = 'all', search = '', editingId = null;
let saving = false, loaded = false, deadlineTarget = null, editingOccurrence = null, lastCheck = Date.now()-60000, dayAtLastRender = dateKey();
let refreshPending = false, completionStorageReady = false, nativeVisible = true, remoteDirty = false, renderPending = false;
let renderedState, renderedEvents, renderedTasks, renderedSettings, renderedView={}, eventsEpoch=0;
const monthCache=new Map(), formatters=new Map(), pageSizes={search:100,day:80,tasks:100,dday:100};
const limits={...pageSizes};
function moreRows(scope,total) {return total>limits[scope]?`<button class="list-more" data-page="${scope}">더 보기 · ${limits[scope]} / ${total}개</button>`:'';}
function monthIndex() {
  const token=JSON.stringify([eventsEpoch,month,filter,search]);
  if(monthCache.has(token))return monthCache.get(token);
  const {from,to}=rangeForMonth(),index=indexDates(state.events.filter(matches),from,to);
  monthCache.set(token,index);if(monthCache.size>3)monthCache.delete(monthCache.keys().next().value);
  return index;
}
function selectedRows() {return monthIndex().get(selected)||onDate(occurrences(state.events.filter(matches),selected,selected),selected);}


function styles(category) { const c=CATEGORIES[category]; return `--category:${c.color};--event-light:${c.light}`; }
function eventCheckbox(o) {
  return `<input type="checkbox" class="event-check" data-event-check="${esc(o.event.id)}" data-occurrence="${o.startDate}" ${isEventDone(o.event,o.startDate)?'checked':''} ${completionStorageReady?'':'disabled'} aria-label="${esc(o.event.title)} · ${formatDay(o.startDate)} 완료" title="${o.event.repeat==='none'?'일정 완료':'이 회차만 완료'}">`;
}
function readState(input) {
  const valid=validateState(input);
  completionStorageReady=input.events.every(e=>Object.hasOwn(e,'completedDates'));
  $('completionNotice').hidden=completionStorageReady;
  return valid;
}
function formatDay(key, options = {month:'long',day:'numeric',weekday:'short'}) {
  const token=JSON.stringify(options);if(!formatters.has(token))formatters.set(token,new Intl.DateTimeFormat('ko-KR',options));
  return formatters.get(token).format(new Date(`${key}T12:00:00`));
}
function showDialog(id) { if (!$(id).open) $(id).showModal(); }
function closeDialog(id) { if (!saving) $(id).close(); }
function toast(title, detail='',kind='info',duration=6000) {
  const box=document.createElement('div'); box.className=`toast ${kind}`;
  box.innerHTML=`<div>${icon(kind==='error'?'flag':'bell')}</div><div style="flex:1"><strong>${esc(title)}</strong>${detail?`<p>${esc(detail)}</p>`:''}</div><button class="plain-button" aria-label="알림 닫기">×</button>`;
  const region=$('toastRegion'); region.append(box);
  try { if (!region.matches(':popover-open')) region.showPopover(); } catch { /* Older browser: fixed toast fallback. */ }
  const remove=()=>{box.remove(); if(!region.children.length) {try{region.hidePopover();}catch{}}};
  box.querySelector('button').onclick=remove;
  setTimeout(remove,duration);
}
function undoToast(title,detail,restore,restoredMessage='삭제를 되돌렸어요') {
  const box=document.createElement('div');box.className='toast info';
  box.innerHTML=`<div>${icon('bell')}</div><div style="flex:1"><strong>${esc(title)}</strong><p>${esc(detail)}</p></div><button class="plain-button">되돌리기</button>`;
  const region=$('toastRegion');region.append(box);
  try{if(!region.matches(':popover-open'))region.showPopover();}catch{}
  const remove=()=>{box.remove();if(!region.children.length)try{region.hidePopover();}catch{}};
  const timer=setTimeout(remove,10000);
  box.querySelector('button').onclick=async()=>{const button=box.querySelector('button');button.disabled=true;if(await restore()){clearTimeout(timer);remove();toast(restoredMessage,detail);}else button.disabled=false;};
}
const draftPrefix='petCalendar.eventDraft.';
let currentDraftKey=null,draftBaseline='';
function formDraft() {
  return [...$('eventForm').elements].filter(e=>(e.name||e.id)&&['INPUT','TEXTAREA','SELECT'].includes(e.tagName)).map(e=>({name:e.name,id:e.id,value:e.value,checked:e.checked,type:e.type}));
}
function storeDraft() {
  if(!currentDraftKey)return;
  try{const data=JSON.stringify(formDraft());if(data===draftBaseline)localStorage.removeItem(currentDraftKey);else localStorage.setItem(currentDraftKey,data);}catch{}
}
function clearDraft(){if(currentDraftKey)try{localStorage.removeItem(currentDraftKey);}catch{}currentDraftKey=null;}
function banner(message) { $('errorText').textContent=message; $('errorBanner').hidden=false; }
function saveStatus(text,failed=false) {
  $('saveStatus').innerHTML=`<i class="status-dot"${failed?' style="background:#c05b46"':''}></i> ${esc(text)}`;
  $('saveStatus').classList.toggle('save-failed',failed);
}
async function fetchState() {
  const response=await fetch('/api/state',{cache:'no-store',signal:AbortSignal.timeout(8000)});
  if(!response.ok) throw new Error('저장된 기록을 불러오지 못했습니다. 바탕화면 바로가기로 다시 열어주세요.');
  return readState(await response.json());
}
async function commit(change,{applyHotkey=false}={}) {
  if(saving || !loaded) return false;
  const next=structuredClone(state);
  try {change(next);validateState(next);} catch(error){toast('내용을 확인해 주세요',error.message,'error');return false;}
  saving=true; saveStatus('저장 중…');
  document.querySelectorAll('button[type=submit]').forEach(b=>b.disabled=true);
  try {
    const response=await fetch('/api/state',{method:'PUT',headers:{'Content-Type':'application/json','X-Calendar-Request':'1',...(applyHotkey?{'X-Calendar-Hotkey-Apply':'1'}:{})},body:JSON.stringify(next),signal:AbortSignal.timeout(20000)});
    const result=await response.json();
    if(response.status===409) {
      state=await fetchState();render();
      throw new Error('다른 창의 변경 내용을 불러왔습니다. 입력 중인 내용을 확인한 뒤 다시 저장해 주세요.');
    }
    if(!response.ok) throw new Error(result.error || '저장하지 못했습니다.');
    state=readState(result); render(); $('errorBanner').hidden=true;
    saveStatus(`이 PC에 저장됨 · ${new Date(state.updatedAt).toLocaleTimeString('ko-KR',{hour:'2-digit',minute:'2-digit',hour12:false})}`);
    return true;
  } catch(error) {
    const message=error.name==='TimeoutError'?'저장 응답이 늦어지고 있습니다. 최신 내용을 불러와 저장 여부를 확인해 주세요.':error.message;
    banner(message);saveStatus('저장하지 못함 · 입력 내용을 확인해 주세요',true);toast('저장을 완료하지 못했어요',message,'error',10000);return false;
  } finally {saving=false;document.querySelectorAll('button[type=submit]').forEach(b=>b.disabled=false);if(remoteDirty&&nativeVisible)refresh(true);}
}
function matches(event) {return (filter==='all'||event.category===filter) && (!search||`${event.title} ${event.note}`.toLocaleLowerCase().includes(search));}
function rangeForMonth() {
  const first=`${month}-01`, offset=new Date(`${first}T12:00:00`).getDay();
  const [year,m]=month.split('-').map(Number), count=new Date(year,m,0).getDate();
  const size=Math.ceil((offset+count)/7)*7;
  return {from:addDays(first,-offset),to:addDays(first,-offset+size-1),size};
}
function jumpTo(key) {selected=key;month=key.slice(0,7);render();}
function monthMove(amount) {
  const [year,m]=month.split('-').map(Number), next=new Date(year,m-1+amount,1,12);
  month=dateKey(next).slice(0,7);selected=`${month}-01`;render();
}
function render(force=false) {
  if(!loaded)return;
  if(nativeDesktop&&!nativeVisible){renderPending=true;return;}
  renderPending=false;
  let eventsChanged=force,tasksChanged=force,settingsChanged=force;
  if(state!==renderedState) {
    const events=JSON.stringify(state.events),tasks=JSON.stringify(state.tasks),settings=JSON.stringify(state.settings);
    eventsChanged ||= events!==renderedEvents;tasksChanged ||= tasks!==renderedTasks;settingsChanged ||= settings!==renderedSettings;
    renderedState=state;renderedEvents=events;renderedTasks=tasks;renderedSettings=settings;
  }
  if(eventsChanged){eventsEpoch++;monthCache.clear();}
  const today=dateKey(),newDay=today!==renderedView.today,queryChanged=filter!==renderedView.filter||search!==renderedView.search,scaleChanged=(state.settings.textScale??0)!==renderedView.textScale;
  if(scaleChanged)document.documentElement.style.setProperty('--text-extra',`${state.settings.textScale??0}pt`);
  const calendarChanged=eventsChanged||queryChanged||month!==renderedView.month||newDay||scaleChanged;
  const dayChanged=calendarChanged||selected!==renderedView.selected;
  if(queryChanged){limits.search=pageSizes.search;limits.day=pageSizes.day;}
  if(selected!==renderedView.selected)limits.day=pageSizes.day;
  if(eventsChanged||tasksChanged||newDay)renderSummary();
  if(force||filter!==renderedView.filter)renderFilters();
  if(calendarChanged)renderCalendar();
  else if(dayChanged)for(const cell of $('calendarGrid').querySelectorAll('[data-date]')) {
    const active=cell.dataset.date===selected;cell.classList.toggle('selected',active);cell.setAttribute('aria-selected',String(active));
  }
  if(dayChanged)renderDay();
  if(tasksChanged||settingsChanged||newDay){if(tasksChanged)limits.tasks=pageSizes.tasks;renderTasks();}
  if(settingsChanged){$('textScale').value=String(state.settings.textScale??0);renderNotifications();$('hideCompleted').checked=state.settings.hideCompleted;$('defaultReminder').value=String(state.settings.defaultReminder);}
  if($('ddayDialog').open&&(eventsChanged||dayChanged))renderDdayChoices();
  renderedView={today,filter,search,month,selected,textScale:state.settings.textScale??0};
}
function renderSummary() {
  const today=dateKey();
  $('todayCount').textContent=onDate(occurrences(state.events,today,today),today).length;
  $('taskCount').textContent=state.tasks.filter(t=>!t.done).length;
  const target=deadlineTarget=selectedDday(state.events,today);
  $('deadlineCard').classList.toggle('unselected',!target);
  $('deadlineTitle').textContent=target?target.event.title:'일정을 선택하세요';
  $('deadlineValue').textContent=target?dday(target.startDate,today):'선택';
  const description=target?`${target.event.title} · ${formatDay(target.startDate)} · 클릭하여 D-day 일정 변경`:'클릭하여 달력에서 중요한 일정 선택';
  $('deadlineCard').title=description;
  $('deadlineCard').setAttribute('aria-label',`중요한 일정 D-day · ${description}`);
}
function isDdayOccurrence(o) {return deadlineTarget?.event.id===o.event.id && deadlineTarget.startDate===o.startDate;}
function renderDdayChoices() {
  const query=$('ddaySearch').value.trim().toLocaleLowerCase(),today=dateKey();
  const choices=state.events.filter(e=>!query||`${e.title} ${e.note}`.toLocaleLowerCase().includes(query))
    .map(e=>ddayOccurrence(e,selected)).sort((a,b)=>Number(b.event.id===deadlineTarget?.event.id)-Number(a.event.id===deadlineTarget?.event.id)
      ||Number(a.startDate<today)-Number(b.startDate<today)||(a.startDate<today?b.startDate.localeCompare(a.startDate):a.startDate.localeCompare(b.startDate))||a.event.title.localeCompare(b.event.title,'ko'));
  $('ddayChoices').innerHTML=choices.length?choices.slice(0,limits.dday).map(o=>`<button class="dday-choice ${isDdayOccurrence(o)?'chosen':''}" data-dday="${esc(o.event.id)}" data-dday-date="${o.startDate}" aria-pressed="${isDdayOccurrence(o)}" style="${styles(o.event.category)}"><span class="dday-choice-flag">${isDdayOccurrence(o)?'✓':icon('flag')}</span><span class="dday-choice-copy"><strong>${esc(o.event.title)}</strong><small>${formatDay(o.startDate,{year:'numeric',month:'long',day:'numeric',weekday:'short'})} · ${CATEGORIES[o.event.category].label}${o.event.repeat!=='none'?' · 반복':''}</small></span><b>${dday(o.startDate,today)}</b></button>`).join(''):
    `<p class="empty-tasks">${query?'검색한 일정이 없어요.':'아직 등록한 일정이 없어요. 달력에 일정을 추가한 뒤 골라주세요.'}</p>`;
  $('ddayChoices').insertAdjacentHTML('beforeend',moreRows('dday',choices.length));
  $('clearDday').disabled=!deadlineTarget;$('openDdayEvent').disabled=!deadlineTarget;
}
async function chooseDday(id,key,toggle=false) {
  if(saving||!loaded)return;
  const event=state.events.find(e=>e.id===id);if(!event||!occursOn(event,key))return;
  const clear=toggle&&deadlineTarget?.event.id===id&&deadlineTarget.startDate===key;
  if(await commit(s=>{for(const e of s.events){e.pinned=!clear&&e.id===id;e.pinnedDate=e.pinned?key:'';}})) {
    closeDialog('ddayDialog');toast(clear?'D-day 선택을 해제했어요':'D-day 일정을 선택했어요',event.title);
  }
}
function renderFilters() {
  $('categoryFilters').innerHTML=`<button class="filter-chip ${filter==='all'?'active':''}" data-filter="all" aria-pressed="${filter==='all'}">전체</button>`+
    Object.entries(CATEGORIES).map(([key,c])=>`<button class="filter-chip ${filter===key?'active':''}" data-filter="${key}" aria-pressed="${filter===key}" style="--category:${c.color}"><i class="category-dot"></i>${c.label}</button>`).join('');
}
let holidayDates={};
let lastDensity;
function calendarDensity() {
  const {size}=rangeForMonth(),height=$('calendarGrid').getBoundingClientRect().height/(size/7);
  return matchMedia('(min-width:1001px) and (min-height:620px)').matches?(height<76?0:height<100?1:height<125?2:3):3;
}
function renderCalendar() {
  const [year,m]=month.split('-').map(Number), today=dateKey();
  $('monthLabel').textContent=`${year}년 ${m}월`;
  $('monthEnglish').textContent=new Date(year,m-1,1).toLocaleDateString('en-US',{month:'long'}).toUpperCase();
  const {from,to,size}=rangeForMonth();
  const grid=$('calendarGrid');
  grid.style.setProperty('--calendar-rows',size/7);
  const perDay=lastDensity=calendarDensity(),index=monthIndex();
  const html=[];
  for(let i=0;i<size;i++) {
    const key=addDays(from,i), day=new Date(`${key}T12:00:00`).getDay(), items=index.get(key);
    const sameMonth=key.startsWith(month), isToday=key===today, isSelected=key===selected, holiday=holidayDates[key], holidayEstimated=Number(key.slice(0,4))>2027;
    const fontSpace=(state.settings.textScale??0)>0&&matchMedia('(min-width:1001px) and (min-height:620px)').matches?1:0;
    const slots=Math.max(0,perDay-(holiday?1:0)-fontSpace);
    html.push(`<div class="day-cell ${sameMonth?'':'outside'} ${isToday?'today':''} ${isSelected?'selected':''}" data-date="${key}" tabindex="0" role="gridcell" aria-selected="${isSelected}" aria-label="${formatDay(key,{year:'numeric',month:'long',day:'numeric',weekday:'long'})}${holiday?', '+esc(holiday)+(holidayEstimated?' (예상)':''):''}, 일정 ${items.length}건"><div class="day-top"><div class="day-heading"><span class="day-number ${day===0||holiday?'sunday':day===6?'saturday':''}">${Number(key.slice(8))}</span>${isToday?'<span class="today-word">TODAY</span>':''}</div><button class="day-add" data-new="${key}" aria-label="${formatDay(key)}에 일정 추가" title="일정 추가">${icon('plus')}</button></div>${holiday?`<span class="holiday-label" title="${esc(holiday)}${holidayEstimated?' (예상 공휴일)':''}">${esc(holiday)}</span>`:''}`+
      items.slice(0,slots).map(o=>`<div class="event-chip ${isEventDone(o.event,o.startDate)?'done':''}" draggable="true" data-drag-event="${esc(o.event.id)}" data-drag-start="${o.startDate}" data-drag-day="${key}" style="${styles(o.event.category)}">${eventCheckbox(o)}<button class="event-open" data-event="${esc(o.event.id)}" data-event-date="${o.startDate}" title="${esc(o.event.title)}${o.event.startTime?' · '+o.event.startTime:''}">${o.event.startTime&&o.startDate===key?`<span class="event-time">${o.event.startTime}</span>`:''}<span class="event-title">${o.startDate<key?'↳ ':''}${esc(o.event.title)}</span>${isDdayOccurrence(o)?`<span class="dday-marker" title="선택한 D-day 일정">${icon('flag')}</span>`:''}${o.event.repeat!=='none'?`<span class="repeat-symbol">${icon('repeat')}</span>`:''}</button></div>`).join('')+
      (items.length>slots?`<button class="more-button" data-more="${key}">${slots?`+${items.length-slots}개 더 보기`:`일정 ${items.length}개`}</button>`:'')+'</div>');
  }
  $('calendarGrid').innerHTML=html.join('');
  const monthEvents=new Set([...index].filter(([key])=>key.startsWith(month)).flatMap(([,rows])=>rows.map(o=>o.event.id)));
  $('visibleCount').textContent=`${String(m).padStart(2,'0')}월 · ${monthEvents.size}개 일정${year<2020||year>2050?' · 공휴일 자료 없음':year>2027?' · 공휴일 예상':''}`;
  $('searchResults').hidden=!search;
  if(search) {
    const found=state.events.filter(matches).sort((a,b)=>a.startDate.localeCompare(b.startDate));
    $('searchResults').innerHTML=`<h3>전체 기간 검색 결과 · ${found.length}건</h3>`+
      (found.length?found.slice(0,limits.search).map(e=>`<button class="search-result" data-event="${esc(e.id)}" style="${styles(e.category)}"><i class="category-dot"></i><strong>${esc(e.title)}</strong><span>${esc(e.startDate)}${e.repeat!=='none'?' · 반복':''}</span></button>`).join(''):'<p class="empty-tasks">제목이나 메모에 맞는 일정이 없어요.</p>');
    $('searchResults').insertAdjacentHTML('beforeend',moreRows('search',found.length));
  }
}
function renderDay() {
  $('selectedLabel').textContent=formatDay(selected);
  $('selectedRelative').textContent=selected===dateKey()?'오늘의 계획':'선택한 날짜';
  const items=selectedRows();
  $('dayEvents').innerHTML=items.length?items.slice(0,limits.day).map(o=>{
    const e=o.event, isMulti=o.startDate!==o.endDate;
    return `<div class="day-event-row ${isEventDone(e,o.startDate)?'done':''}"><div class="day-event-card" style="${styles(e.category)}">${eventCheckbox(o)}<button class="day-event" data-event="${esc(e.id)}" data-event-date="${o.startDate}" style="${styles(e.category)}"><div class="day-event-top"><span>${e.startTime?esc(e.startTime)+(e.endTime?' – '+esc(e.endTime):''):'종일'}</span><span>${CATEGORIES[e.category].label}${isDdayOccurrence(o)?' · D-day':''}</span></div><h3>${esc(e.title)}</h3>${e.note?`<p>${esc(e.note)}</p>`:''}${isMulti||e.repeat!=='none'?`<div class="event-meta">${isMulti?`${formatDay(o.startDate,{month:'numeric',day:'numeric'})} – ${formatDay(o.endDate,{month:'numeric',day:'numeric'})}`:''}${e.repeat!=='none'?` ${icon('repeat')} ${repeatLabel(e.repeat)}`:''}</div>`:''}</button><div class="day-event-actions"><button class="dday-pick ${isDdayOccurrence(o)?'chosen':''}" data-dday="${esc(e.id)}" data-dday-date="${o.startDate}" aria-pressed="${isDdayOccurrence(o)}" aria-label="${esc(e.title)} · ${formatDay(o.startDate)} ${isDdayOccurrence(o)?'D-day 선택 해제':'D-day로 선택'}" title="${isDdayOccurrence(o)?'다시 누르면 D-day 선택 해제':'이 일정을 D-day로 선택'}">${icon('flag')}</button><button class="event-delete" data-delete-event="${esc(e.id)}" data-occurrence="${o.startDate}" aria-label="${esc(e.title)} 일정 삭제" title="일정 삭제">${icon('trash')}</button></div></div></div>`;
  }).join(''):`<div class="empty-day"><div class="empty-drawing">${icon('calendar')}</div><strong>${filter!=='all'||search?'조건에 맞는 일정이 없어요':'아직은 비어 있는 하루'}</strong><p>${filter!=='all'||search?'다른 분류나 검색어를 확인해 보세요.':'새로운 계획을 하나 적어볼까요?'}</p><button data-new="${selected}">+ 일정 추가하기</button></div>`;
  $('dayEvents').insertAdjacentHTML('beforeend',moreRows('day',items.length));
}
function renderTasks() {
  const today=dateKey(), completed=state.tasks.filter(t=>t.done).length;
  $('taskProgress').textContent=`${completed}/${state.tasks.length}`;
  $('taskProgressBar').style.width=state.tasks.length?`${completed/state.tasks.length*100}%`:'0%';
  const list=state.tasks.filter(t=>!state.settings.hideCompleted||!t.done).sort((a,b)=>Number(a.done)-Number(b.done)||(a.dueDate||'9999').localeCompare(b.dueDate||'9999')||a.createdAt.localeCompare(b.createdAt));
  $('tasksList').innerHTML=list.length?list.slice(0,limits.tasks).map(t=>`<div class="task-item ${t.done?'done':''}" draggable="true" data-drag-task="${esc(t.id)}" title="달력으로 끌어 일정을 만들 수 있어요"><input type="checkbox" ${t.done?'checked':''} data-task-check="${esc(t.id)}" aria-label="${esc(t.title)} 완료"><div class="task-copy"><span class="task-name">${esc(t.title)}</span>${t.dueDate?`<span class="task-date ${!t.done&&t.dueDate<today?'overdue':''}">${formatDay(t.dueDate,{month:'numeric',day:'numeric'})} ${!t.done?`· ${t.dueDate<today?'기한 지남':dday(t.dueDate,today)}`:''}</span>`:''}</div><button class="delete-task" data-task-delete="${esc(t.id)}" aria-label="${esc(t.title)} 삭제">${icon('trash')}</button></div>`).join(''):
    `<div class="empty-tasks">${state.tasks.length&&completed===state.tasks.length?'오늘도 하나씩, 모두 해냈어요. ✓':'머릿속 할 일을 여기에 내려놓으세요.'}</div>`;
  $('tasksList').insertAdjacentHTML('beforeend',moreRows('tasks',list.length));
}
function renderNotifications() {
  if(nativeDesktop){
    const enabled=state.settings.desktopPopups;
    $('notificationButton').closest('.notification-card').classList.toggle('enabled',enabled);
    $('notificationDescription').textContent='창을 숨겨도 알려드립니다. 데스크톱 펫을 켜면 펫의 말풍선으로, 끄면 알림창으로 표시합니다. 알림은 확인할 때까지 유지됩니다. PC 절전 중에는 지연될 수 있으며, 종일 일정은 오전 9시 기준입니다.';
    document.querySelector('#helpDialog .field-hint').textContent='창의 X는 트레이로 숨기기입니다. 숨겨두어도 알림은 작동하고, 알림을 누르면 해당 일정이 열립니다. 완전 종료와 재시작은 시계 옆 캘린더 아이콘을 우클릭하세요.';
    $('notificationTitle').textContent=enabled?'데스크톱 알림이 켜져 있어요':'일정 알림을 켜둘까요?';
    $('notificationSubtitle').textContent=enabled?'숨겨두어도, 확인할 때까지 알려드려요.':'실행 중인 동안, 놓치지 않도록.';
    $('enableNotifications').textContent=enabled?'데스크톱 알림 끄기':'데스크톱 알림 켜기';
    $('permissionStatus').textContent=enabled?'확인할 때까지 알림 유지 · 알림을 누르면 해당 일정을 엽니다.':'화면 안 알림만 사용합니다. 데스크톱 알림은 언제든 켤 수 있어요.';
    $('desktopSoundRow').hidden=false;$('notificationSound').checked=state.settings.notificationSound;
    return;
  }
  const permission='Notification' in window?Notification.permission:'unsupported';
  const enabled=state.settings.notifications&&permission==='granted';
  $('notificationTitle').textContent=enabled?'일정 알림이 켜져 있어요':permission==='denied'?'화면에서 일정을 알려드려요':'일정 알림을 켜둘까요?';
  $('notificationSubtitle').textContent=enabled?'캘린더를 열어두면 알려드릴게요.':permission==='denied'?'윈도우 알림은 브라우저에서 차단됨':'열어둔 동안, 놓치지 않도록.';
  $('enableNotifications').textContent=enabled?'윈도우 알림 끄기':'윈도우 알림 켜기';
  $('permissionStatus').textContent=enabled?'윈도우 알림 사용 중 · 화면 알림도 함께 표시됩니다.':permission==='denied'?'알림이 차단되어 있습니다. 주소창의 사이트 설정에서 알림을 허용해 주세요.':'현재는 화면 알림을 사용합니다. 윈도우 알림은 허용 후 사용할 수 있습니다.';
}
function hotkeyDraft() {return {enabled:$('hotkeyEnabled').checked,modifiers:($('hotkeyCtrl').checked?2:0)|($('hotkeyAlt').checked?1:0)|($('hotkeyShift').checked?4:0),key:Number($('hotkeyKey').value)};}
function previewHotkey() {
  const value=hotkeyDraft();$('hotkeyPreview').textContent=value.enabled?hotkeyLabel(value):'사용 안 함';
  for(const id of ['hotkeyCtrl','hotkeyAlt','hotkeyShift','hotkeyKey'])$(id).disabled=!value.enabled;
}
function renderHotkey(value=state.settings.hotkey) {
  $('hotkeyEnabled').checked=value.enabled;$('hotkeyCtrl').checked=!!(value.modifiers&2);$('hotkeyAlt').checked=!!(value.modifiers&1);$('hotkeyShift').checked=!!(value.modifiers&4);
  $('hotkeyKey').innerHTML=HOTKEY_KEYS.map(([key,label])=>`<option value="${key}">${key===192?'` (백틱 · ~)':label}</option>`).join('');$('hotkeyKey').value=String(value.key);previewHotkey();
}
async function refreshHotkeyStatus() {
  try {
    const response=await fetch('/api/hotkey',{signal:AbortSignal.timeout(5000)});if(!response.ok)throw new Error('단축키 상태를 불러오지 못했습니다. 바탕화면 바로가기로 다시 열어 주세요.');
    const status=await response.json();
    $('hotkeyStatus').textContent=status.error || (status.active?`사용 중 · ${status.label}`:'창 단축키가 꺼져 있습니다.');
    $('hotkeyStatus').classList.toggle('is-error',!!status.error);
  }catch(error){$('hotkeyStatus').textContent=error.message;$('hotkeyStatus').classList.add('is-error');}
}
function openEvent(id=null,key=selected,duplicate=false) {
  if(!loaded||saving)return;
  const original=id?state.events.find(e=>e.id===id):null;
  editingOccurrence=key;
  if(id&&!original)return;
  editingId=duplicate?null:id;
  const e=original||{title:'',startDate:key,endDate:key,startTime:'',endTime:'',category:'work',note:'',repeat:'none',repeatUntil:'',reminder:state.settings.defaultReminder,pinned:false};
  $('eventForm').reset();$('eventError').textContent='';
  $('eventDialogTitle').textContent=duplicate?'일정 복제':original?'일정 수정':'새 일정';
  $('eventTitle').value=e.title+(duplicate?' (복사)':'');
  const displayShift=original&&!duplicate&&e.repeat!=='none'?dayNumber(key)-dayNumber(e.startDate):0;
  $('eventStart').value=addDays(e.startDate,displayShift);$('eventEnd').value=addDays(e.endDate,displayShift);
  $('eventStartTime').value=e.startTime;$('eventEndTime').value=e.endTime;
  $('eventAllDay').checked=!e.startTime;$('timeFields').hidden=!e.startTime;
  $('eventStartTime').required=!!e.startTime;
  $('eventRepeat').value=e.repeat.startsWith('monthly:')?'monthly-weekday':e.repeat;$('eventRepeatUntil').value=e.repeatUntil;
  const rule=e.repeat.startsWith('monthly:')?e.repeat.split(':').slice(1):[Math.min(4,Math.ceil(Number(e.startDate.slice(8))/7)),new Date(`${e.startDate}T12:00:00`).getDay()];
  $('eventRepeatWeek').value=String(rule[0]);$('eventRepeatWeekday').value=String(rule[1]);
  $('monthlyWeekdayFields').hidden=$('eventRepeat').value!=='monthly-weekday';
  $('repeatUntilField').hidden=e.repeat==='none';
  $('eventReminder').value=String(e.reminder);$('eventNote').value=e.note;
  $('eventPinned').checked=!!e.pinned&&!duplicate;
  $('eventCategories').innerHTML=Object.entries(CATEGORIES).map(([value,c])=>`<label class="category-choice" style="${styles(value)}"><input type="radio" name="category" value="${value}" ${value===e.category?'checked':''} required><i class="category-dot"></i>${c.label}</label>`).join('');
  $('repeatNotice').hidden=!original||original.repeat==='none'||duplicate;
  $('deleteEvent').hidden=!editingId;$('duplicateEvent').hidden=!editingId;
  currentDraftKey=draftPrefix+(duplicate?'duplicate:'+id:id?'edit:'+id:'new:'+key);
  draftBaseline=JSON.stringify(formDraft());
  try {
    const draft=JSON.parse(localStorage.getItem(currentDraftKey));
    if(Array.isArray(draft)){
      for(const saved of draft){const control=[...$('eventForm').elements].find(e=>(saved.id?e.id===saved.id:e.name===saved.name)&&(e.type!=='radio'||e.value===saved.value));if(control){if(control.type==='checkbox'||control.type==='radio')control.checked=!!saved.checked;else control.value=saved.value;}}
      $('eventAllDay').dispatchEvent(new Event('change'));$('eventRepeat').dispatchEvent(new Event('change'));
      toast('작성 중이던 내용을 불러왔어요','저장하거나 내용을 수정해 이어서 작성하세요.');
    }
  }catch{}
  showDialog('eventDialog');setTimeout(()=>$('eventTitle').focus(),50);
}
$('eventForm').addEventListener('input',storeDraft);
$('eventForm').addEventListener('change',storeDraft);
async function confirmAction(title,text,label='확인') {
  if($('confirmDialog').open)return false;
  $('confirmTitle').textContent=title;$('confirmText').textContent=text;$('confirmAccept').textContent=label;
  showDialog('confirmDialog');
  return new Promise(resolve=>{
    let done=false;
    const finish=value=>{if(done)return;done=true;$('confirmDialog').close();$('confirmAccept').removeEventListener('click',yes);$('confirmCancel').removeEventListener('click',no);$('confirmDialog').removeEventListener('cancel',cancel);resolve(value);};
    const yes=()=>finish(true),no=()=>finish(false),cancel=event=>{event.preventDefault();finish(false);};
    $('confirmAccept').addEventListener('click',yes);$('confirmCancel').addEventListener('click',no);$('confirmDialog').addEventListener('cancel',cancel);
  });
}
function repeatScope(action,date){
  $('repeatScopeText').textContent=`${formatDay(date)} 일정을 ${action}합니다. 적용할 범위를 선택해 주세요.`;showDialog('repeatScopeDialog');
  return new Promise(resolve=>{const dialog=$('repeatScopeDialog');const finish=value=>{dialog.close();for(const [element,handler] of handlers)element.removeEventListener('click',handler);dialog.removeEventListener('cancel',cancel);resolve(value);};const handlers=[[$('repeatScopeOne'),()=>finish('one')],[$('repeatScopeAll'),()=>finish('all')],[$('repeatScopeCancel'),()=>finish(null)]];const cancel=e=>{e.preventDefault();finish(null);};for(const [element,handler] of handlers)element.addEventListener('click',handler);dialog.addEventListener('cancel',cancel);});
}
async function enableNotifications() {
  if(nativeDesktop){await commit(s=>{s.settings.desktopPopups=!s.settings.desktopPopups;});return;}
  if(state.settings.notifications&&'Notification' in window&&Notification.permission==='granted') {
    await commit(s=>{s.settings.notifications=false;});return;
  }
  if(!('Notification' in window)){toast('이 브라우저는 윈도우 알림을 지원하지 않아요','화면 내 알림은 계속 작동합니다.');return;}
  let permission;
  try{permission=await Notification.requestPermission();}catch{permission='denied';}
  if(permission==='granted') {
    if(await commit(s=>{s.settings.notifications=true;})) {notify('일정 알림을 켰어요','캘린더를 열어둔 동안 일정을 알려드립니다.',true);}
  }else{toast('화면 내 알림을 사용합니다','윈도우 알림을 원하면 주소창의 사이트 설정에서 알림을 허용해 주세요.');}
  renderNotifications();
}
function notify(title,body,force=false,tag='') {
  toast(title,body,'',15000);
  if(nativeDesktop){if(force)fetch('/api/desktop/notification-test',{method:'POST',headers:{'X-Calendar-Request':'1'}}).catch(error=>toast('데스크톱 알림을 표시하지 못했어요',error.message,'error'));return;}
  if((force||state.settings.notifications)&&'Notification' in window&&Notification.permission==='granted') {
    try{const n=new Notification(title,{body,icon:'/icon.svg',tag:tag||'pet-calendar-test'});n.onclick=()=>{window.focus();n.close();};}catch{toast('윈도우 알림을 표시하지 못했어요','브라우저와 Windows의 알림 설정을 확인해 주세요.');}
  }
}
async function checkReminders() {
  if(nativeDesktop)return;
  if(!loaded)return;
  const now=Date.now(),since=Math.max(lastCheck,now-24*3600000);lastCheck=now;
  const run=async()=>{
    let sent;
    try{sent=JSON.parse(localStorage.getItem('petCalendar.reminders')||localStorage.getItem('workCalendar.reminders')||'{}');if(!sent||Array.isArray(sent)||typeof sent!=='object')sent={};}catch{sent={};}
    const list=occurrences(state.events,addDays(dateKey(new Date(since)),-1),addDays(dateKey(new Date(now)),1));
    const due=[];
    for(const o of list){const at=reminderTime(o),id=`${o.key}@${at}`;if(at!==null&&at>since&&at<=now&&!sent[id]){due.push(o);sent[id]=now;}}
    for(const [key,value] of Object.entries(sent))if(value<now-7*86400000)delete sent[key];
    try{localStorage.setItem('petCalendar.reminders',JSON.stringify(sent));}catch{ /* Reminders still work for the current session. */ }
    if(due.length>3)notify(`확인할 일정이 ${due.length}개 있어요`,due.slice(0,3).map(o=>o.event.title).join('\n')+'\n캘린더에서 일정을 확인해 주세요.',false,`summary-${now}`);
    else for(const o of due)notify(o.event.title,`${formatDay(o.startDate)} ${o.event.startTime||'오전 9시 · 종일 일정'}${reminderTime(o)<now-60000?'\n잠시 자리를 비운 동안 알림 시간이 지났어요.':''}`,false,o.key);
  };
  if(navigator.locks){await navigator.locks.request('petCalendar.reminders',run);}else await run();
}
async function refresh(force=false) {
  if(nativeDesktop&&!force)return;
  if(nativeDesktop&&!nativeVisible){remoteDirty=true;return;}
  if(!loaded||saving||refreshPending||document.querySelector('dialog[open]')){remoteDirty=true;return;}
  remoteDirty=false;
  refreshPending=true;
  try{const remote=await fetchState();if(remote.revision!==state.revision){state=remote;render();saveStatus('다른 창의 변경 내용을 반영했어요');}}
  catch{saveStatus('연결 확인 필요 · 바로가기로 다시 열어주세요',true);}
  finally{refreshPending=false;if(remoteDirty&&!document.querySelector('dialog[open]')&&nativeVisible&&!saving)queueMicrotask(()=>refresh(true));}
}
function tick() {
  const now=new Date();$('todayLabel').textContent=now.toLocaleDateString('ko-KR',{year:'numeric',month:'long',day:'numeric',weekday:'long'});
  $('clockLabel').textContent=now.toLocaleTimeString('ko-KR',{hour:'2-digit',minute:'2-digit',hour12:false});
  if(dayAtLastRender!==dateKey()){dayAtLastRender=dateKey();render();}
}

document.addEventListener('click',event=>{
  const page=event.target.closest('[data-page]');if(page){const scope=page.dataset.page,container=page.parentElement,offset=container.scrollTop;limits[scope]+=pageSizes[scope];({search:renderCalendar,day:renderDay,tasks:renderTasks,dday:renderDdayChoices})[scope]();container.scrollTop=offset;container.querySelector('[data-page]')?.focus({preventScroll:true});return;}
  const target=event.target.closest('[data-close]');if(target)closeDialog(target.dataset.close);
  const remove=event.target.closest('[data-delete-event]');if(remove){deleteSchedule(remove.dataset.deleteEvent,false,remove.dataset.occurrence);return;}
  const pin=event.target.closest('[data-dday]');if(pin){chooseDday(pin.dataset.dday,pin.dataset.ddayDate,pin.classList.contains('dday-pick'));return;}
  const item=event.target.closest('[data-event]');if(item){openEvent(item.dataset.event,item.dataset.eventDate||selected);return;}
  const add=event.target.closest('[data-new]');if(add){selected=add.dataset.new;month=selected.slice(0,7);render();openEvent();return;}
  const more=event.target.closest('[data-more]');if(more){jumpTo(more.dataset.more);$('dayEvents').scrollIntoView({behavior:'smooth',block:'nearest'});return;}
});
let draggedSchedule=null,draggedTask=null;
function clearCalendarDrag(){draggedSchedule=null;draggedTask=null;for(const el of document.querySelectorAll('.dragging,.drag-over'))el.classList.remove('dragging','drag-over');}
$('tasksList').addEventListener('dragstart',event=>{
  const item=event.target.closest('[data-drag-task]');
  if(!item||event.target.closest('input,button')||saving){event.preventDefault();return;}
  draggedTask={id:item.dataset.dragTask};draggedSchedule=null;event.dataTransfer.effectAllowed='move';event.dataTransfer.setData('text/plain',draggedTask.id);item.classList.add('dragging');
});
$('tasksList').addEventListener('dragend',clearCalendarDrag);
$('calendarGrid').addEventListener('dragstart',event=>{
  const chip=event.target.closest('[data-drag-event]');
  if(!chip||event.target.closest('input')||saving){event.preventDefault();return;}
  draggedTask=null;draggedSchedule={id:chip.dataset.dragEvent,start:chip.dataset.dragStart,day:chip.dataset.dragDay};
  event.dataTransfer.effectAllowed='move';event.dataTransfer.setData('text/plain',draggedSchedule.id);chip.classList.add('dragging');
});
$('calendarGrid').addEventListener('dragover',event=>{
  if(!draggedSchedule&&!draggedTask)return;const cell=event.target.closest('.day-cell');if(!cell)return;
  event.preventDefault();event.dataTransfer.dropEffect='move';
  for(const item of $('calendarGrid').querySelectorAll('.drag-over'))if(item!==cell)item.classList.remove('drag-over');
  cell.classList.add('drag-over');
});
$('calendarGrid').addEventListener('dragleave',event=>{
  const cell=event.target.closest('.day-cell');if(cell&&!cell.contains(event.relatedTarget))cell.classList.remove('drag-over');
});
$('calendarGrid').addEventListener('dragend',clearCalendarDrag);
$('calendarGrid').addEventListener('drop',async event=>{
  const cell=event.target.closest('.day-cell'),drag=draggedSchedule,taskDrag=draggedTask;
  if(!cell||(!drag&&!taskDrag))return;event.preventDefault();clearCalendarDrag();
  const target=cell.dataset.date;
  if(taskDrag){
    let original,converted;
    const success=await commit(s=>{
      const index=s.tasks.findIndex(t=>t.id===taskDrag.id);if(index<0)throw new Error('할 일을 찾지 못했습니다.');
      original=structuredClone(s.tasks[index]);
      converted={id:original.id,title:original.title,startDate:target,endDate:target,startTime:'',endTime:'',category:'work',note:'',repeat:'none',repeatUntil:'',reminder:s.settings.defaultReminder,pinned:false,pinnedDate:'',completedDates:original.done?[target]:[],excludedDates:[]};
      s.tasks.splice(index,1);s.events.push(converted);
    });
    if(success){jumpTo(target);undoToast('할 일을 일정으로 옮겼어요',original.title,()=>commit(s=>{
      const index=s.events.findIndex(e=>e.id===converted.id);
      if(index<0||JSON.stringify(s.events[index])!==JSON.stringify(converted)||s.tasks.some(t=>t.id===original.id))throw new Error('옮긴 일정이 변경되어 자동으로 되돌릴 수 없습니다.');
      s.events.splice(index,1);s.tasks.push(original);
    }),'할 일로 되돌렸어요');}
    return;
  }
  const delta=dayNumber(target)-dayNumber(drag.day);
  if(!delta)return;
  const original=state.events.find(e=>e.id===drag.id);if(!original)return;
  let scope='all';if(original.repeat!=='none'){scope=await repeatScope('이동',drag.start);if(!scope)return;}
  const success=await commit(s=>{
    const series=s.events.find(e=>e.id===drag.id);if(!series)throw new Error('일정을 찾지 못했습니다.');
    if(series.repeat!=='none'&&scope==='one'){
      const moved=structuredClone(series),newStart=addDays(drag.start,delta),duration=dayNumber(series.endDate)-dayNumber(series.startDate);
      moved.id=crypto.randomUUID();moved.startDate=newStart;moved.endDate=addDays(newStart,duration);moved.repeat='none';moved.repeatUntil='';moved.excludedDates=[];
      moved.completedDates=series.completedDates.includes(drag.start)?[newStart]:[];
      moved.pinned=series.pinned&&(series.pinnedDate===drag.start||(!series.pinnedDate&&deadlineTarget?.event.id===series.id&&deadlineTarget.startDate===drag.start));moved.pinnedDate=moved.pinned?newStart:'';
      series.excludedDates=[...new Set([...series.excludedDates,drag.start])];
      if(moved.pinned){series.pinned=false;series.pinnedDate='';}
      s.events.push(moved);
    }else{
      series.startDate=addDays(series.startDate,delta);series.endDate=addDays(series.endDate,delta);
      if(series.repeatUntil)series.repeatUntil=addDays(series.repeatUntil,delta);
      if(series.repeat.startsWith('monthly:')){
        const movedStart=addDays(drag.start,delta),date=new Date(`${movedStart}T12:00:00Z`),day=date.getUTCDate(),last=new Date(Date.UTC(date.getUTCFullYear(),date.getUTCMonth()+1,0)).getUTCDate();
        const oldWeek=Number(series.repeat.split(':')[1]),week=oldWeek===-1&&day+7>last?-1:Math.ceil(day/7)>4?-1:Math.ceil(day/7);
        series.repeat=`monthly:${week}:${date.getUTCDay()}`;
      }
      series.completedDates=series.completedDates.map(key=>addDays(key,delta));series.excludedDates=series.excludedDates.map(key=>addDays(key,delta));
      if(series.pinnedDate)series.pinnedDate=addDays(series.pinnedDate,delta);
      if(series.repeat!=='none'&&[...series.completedDates,...series.excludedDates,...(series.pinnedDate?[series.pinnedDate]:[])].some(key=>!occursOn({...series,excludedDates:[]},key)))throw new Error('반복 일정의 기존 기록을 보존하려면 이번 일정만 이동해 주세요.');
    }
  });
  if(success){jumpTo(target);toast('일정을 옮겼어요',original.title);}
});
$('calendarGrid').addEventListener('click',event=>{
  if(event.target.closest('button,input,.event-chip'))return;
  const cell=event.target.closest('[data-date]');if(!cell)return;
  const key=cell.dataset.date,same=selected===key,selectOnly=!!event.target.closest('.day-top');
  jumpTo(key);
  if(same&&!selectOnly)openEvent();
});
document.addEventListener('change',async event=>{
  const checkbox=event.target.closest('[data-event-check]');if(!checkbox)return;
  const id=checkbox.dataset.eventCheck,key=checkbox.dataset.occurrence,done=checkbox.checked;
  const scope=checkbox.closest('#calendarGrid')?'calendarGrid':'dayEvents';
  const hadFocus=document.activeElement===checkbox;
  const success=await commit(s=>{
    const e=s.events.find(item=>item.id===id);if(!e)return;
    e.completedDates=(e.completedDates||[]).filter(date=>date!==key);
    if(done)e.completedDates.push(key);
  });
  if(!success)render(true);
  if(hadFocus){
    const replacement=[...$(scope).querySelectorAll('[data-event-check]')].find(el=>el.dataset.eventCheck===id && el.dataset.occurrence===key);
    replacement?.focus({preventScroll:true});
  }
});
$('calendarGrid').addEventListener('keydown',event=>{
  if(event.target!==event.target.closest('[data-date]'))return;
  if(event.key==='Enter'||event.key===' '){event.preventDefault();jumpTo(event.target.dataset.date);openEvent();}
});
$('categoryFilters').addEventListener('click',event=>{
  const b=event.target.closest('[data-filter]');if(!b)return;filter=filter===b.dataset.filter?'all':b.dataset.filter;render();
});
let searchTimer;
$('searchInput').addEventListener('input',event=>{
  clearTimeout(searchTimer);const value=event.target.value.trim().toLocaleLowerCase();
  const apply=()=>{search=value;render();$('searchResults').removeAttribute('aria-busy');};
  if(state.events.length>=1000){$('searchResults').setAttribute('aria-busy','true');searchTimer=setTimeout(apply,120);}else apply();
});
$('prevMonth').onclick=()=>monthMove(-1);$('nextMonth').onclick=()=>monthMove(1);
$('goToday').onclick=$('todayCard').onclick=()=>jumpTo(dateKey());
$('addEvent').onclick=$('addSelectedEvent').onclick=()=>openEvent();
$('taskCard').onclick=()=>{$('taskPanel').scrollIntoView({behavior:'smooth',block:'center'});$('taskPanel').classList.remove('highlight');void $('taskPanel').offsetWidth;$('taskPanel').classList.add('highlight');$('taskTitle').focus({preventScroll:true});};
$('deadlineCard').onclick=()=>{if(!loaded||saving)return;$('ddaySearch').value='';limits.dday=pageSizes.dday;renderDdayChoices();showDialog('ddayDialog');};
$('ddaySearch').oninput=()=>{limits.dday=pageSizes.dday;renderDdayChoices();};
$('clearDday').onclick=async()=>{if(await commit(s=>{for(const e of s.events){e.pinned=false;e.pinnedDate='';}}))closeDialog('ddayDialog');};
$('openDdayEvent').onclick=()=>{clearTimeout(searchTimer);if(!deadlineTarget||saving)return;const target=deadlineTarget;closeDialog('ddayDialog');filter='all';search='';$('searchInput').value='';jumpTo(target.startDate);openEvent(target.event.id,target.startDate);};
$('eventAllDay').onchange=()=>{$('timeFields').hidden=$('eventAllDay').checked;$('eventStartTime').required=!$('eventAllDay').checked;if(!$('eventAllDay').checked&&!$('eventStartTime').value)$('eventStartTime').value='09:00';};
$('eventStart').onchange=()=>{if($('eventEnd').value<$('eventStart').value)$('eventEnd').value=$('eventStart').value;};
$('eventRepeat').onchange=()=>{$('repeatUntilField').hidden=$('eventRepeat').value==='none';$('monthlyWeekdayFields').hidden=$('eventRepeat').value!=='monthly-weekday';};
$('eventForm').onsubmit=async event=>{
  event.preventDefault();if(saving)return;
  const f=new FormData($('eventForm'));
  const item={id:editingId||crypto.randomUUID(),title:f.get('title').trim(),startDate:f.get('startDate'),endDate:f.get('endDate'),
    startTime:$('eventAllDay').checked?'':f.get('startTime'),endTime:$('eventAllDay').checked?'':f.get('endTime'),
    category:f.get('category'),note:f.get('note'),repeat:f.get('repeat')==='monthly-weekday'?`monthly:${$('eventRepeatWeek').value}:${$('eventRepeatWeekday').value}`:f.get('repeat'),repeatUntil:f.get('repeat')==='none'?'':f.get('repeatUntil'),
    reminder:f.get('reminder')==='null'?null:Number(f.get('reminder')),pinned:$('eventPinned').checked,
    pinnedDate:state.events.find(e=>e.id===editingId)?.pinnedDate||editingOccurrence||f.get('startDate'),
    completedDates:state.events.find(e=>e.id===editingId)?.completedDates||[],excludedDates:state.events.find(e=>e.id===editingId)?.excludedDates||[]};
  const original=state.events.find(e=>e.id===editingId);
  if(original&&original.repeat!=='none'){
    const scope=await repeatScope('수정',editingOccurrence);if(!scope)return;
    if(scope==='one'){
      item.id=crypto.randomUUID();item.repeat='none';item.repeatUntil='';item.excludedDates=[];item.completedDates=original.completedDates.includes(editingOccurrence)?[item.startDate]:[];item.pinned=$('eventPinned').checked&&(!original.pinned||original.pinnedDate===editingOccurrence);item.pinnedDate=item.pinned?item.startDate:'';
      try{validateState({...state,events:[...state.events,item]});}catch(error){$('eventError').textContent=error.message;return;}
      if(await commit(s=>{const series=s.events.find(e=>e.id===original.id);series.excludedDates=[...new Set([...series.excludedDates,editingOccurrence])];if(series.pinned&&series.pinnedDate===editingOccurrence){series.pinned=false;series.pinnedDate='';}if(item.pinned)for(const e of s.events){e.pinned=false;e.pinnedDate='';}s.events.push(item);})){clearDraft();$('eventDialog').close();toast('이번 일정만 수정했어요',item.title);}return;
    }
    const shift=dayNumber(editingOccurrence)-dayNumber(original.startDate);item.startDate=addDays(item.startDate,-shift);item.endDate=addDays(item.endDate,-shift);
  }
  if(!item.pinned)item.pinnedDate='';else if(!occursOn(item,item.pinnedDate))item.pinnedDate=item.startDate;
  try{validateState({...state,events:[...state.events.filter(e=>e.id!==item.id),item]});}catch(error){$('eventError').textContent=error.message;return;}
  const success=await commit(s=>{if(item.pinned)for(const e of s.events){e.pinned=false;e.pinnedDate='';}const index=s.events.findIndex(e=>e.id===item.id);if(index<0)s.events.push(item);else s.events[index]=item;});
  if(success){clearDraft();$('eventDialog').close();toast(editingId?'일정을 수정했어요':'새 일정을 적어두었어요',item.title);}
  else $('eventError').textContent=$('errorText').textContent;
};
async function deleteSchedule(id,closeEditor=false,occurrence=editingOccurrence||selected){
  const e=state.events.find(item=>item.id===id);if(!e)return;
  if(e.repeat!=='none'){
    const scope=await repeatScope('삭제',occurrence);if(!scope)return;
    if(scope==='one'){
      if(await commit(s=>{const series=s.events.find(item=>item.id===id);series.excludedDates=[...new Set([...series.excludedDates,occurrence])];if(series.pinned&&series.pinnedDate===occurrence){series.pinned=false;series.pinnedDate='';}})){if(closeEditor){clearDraft();$('eventDialog').close();}undoToast('이번 일정을 삭제했어요',e.title,()=>commit(s=>{const series=s.events.find(item=>item.id===id);if(!series)throw new Error('반복 일정을 찾을 수 없습니다.');series.excludedDates=series.excludedDates.filter(date=>date!==occurrence);if(e.pinned&&e.pinnedDate===occurrence&&!s.events.some(item=>item.pinned)){series.pinned=true;series.pinnedDate=occurrence;}}));}return;
    }
  }
  if(!await confirmAction(e.repeat!=='none'?'반복 일정을 삭제할까요?':'일정을 삭제할까요?',`‘${e.title}’${e.repeat!=='none'?'의 전체 반복 일정이 삭제됩니다.':' 일정을 삭제합니다.'}`,'삭제'))return;
  if(await commit(s=>{s.events=s.events.filter(item=>item.id!==e.id);})){if(closeEditor){clearDraft();$('eventDialog').close();}undoToast('일정을 삭제했어요',e.title,()=>commit(s=>{if(s.events.some(item=>item.id===e.id))throw new Error('이미 복구된 일정입니다.');const restored=structuredClone(e);if(restored.pinned&&s.events.some(item=>item.pinned)){restored.pinned=false;restored.pinnedDate='';}s.events.push(restored);}));}
}
$('deleteEvent').onclick=()=>deleteSchedule(editingId,true);
$('duplicateEvent').onclick=()=>openEvent(editingId,selected,true);
$('taskForm').onsubmit=async event=>{
  event.preventDefault();const title=$('taskTitle').value.trim();if(!title)return;
  const task={id:crypto.randomUUID(),title,dueDate:$('taskDue').value,done:false,createdAt:new Date().toISOString()};
  if(await commit(s=>s.tasks.push(task))){$('taskTitle').value='';$('taskDue').value='';$('taskTitle').focus();}
};
$('tasksList').addEventListener('change',async event=>{
  const id=event.target.dataset.taskCheck;if(!id)return;const done=event.target.checked;
  if(!await commit(s=>{const t=s.tasks.find(t=>t.id===id);if(t)t.done=done;}))renderTasks();
});
$('tasksList').addEventListener('click',async event=>{
  const b=event.target.closest('[data-task-delete]');if(!b)return;const task=state.tasks.find(t=>t.id===b.dataset.taskDelete);if(!task)return;
  if(await confirmAction('할 일을 삭제할까요?',task.title,'삭제'))if(await commit(s=>{s.tasks=s.tasks.filter(t=>t.id!==task.id);}))undoToast('할 일을 삭제했어요',task.title,()=>commit(s=>{if(s.tasks.some(t=>t.id===task.id))throw new Error('이미 복구된 할 일입니다.');s.tasks.push(structuredClone(task));}));
});
$('textScale').onchange=async()=>{const value=Number($('textScale').value);if(!await commit(s=>{s.settings.textScale=value;}))$('textScale').value=String(state.settings.textScale??0);};
$('hideCompleted').onchange=async()=>{const value=$('hideCompleted').checked;if(!await commit(s=>{s.settings.hideCompleted=value;}))$('hideCompleted').checked=state.settings.hideCompleted;};
$('settingsButton').onclick=()=>{renderNotifications();renderHotkey();refreshHotkeyStatus();if(nativeDesktop){updateWidget();updatePets();}document.querySelectorAll('#settingsDialog .settings-group').forEach(group=>{group.open=group.id==='petSettings';});showDialog('settingsDialog');$('settingsDialog').scrollTop=0;};
$('helpButton').onclick=()=>showDialog('helpDialog');
$('notificationButton').onclick=()=>{if(nativeDesktop){$('settingsButton').click();$('notificationSettings').open=true;$('notificationSettings').scrollIntoView({block:'start'});return;}if(state.settings.notifications&&'Notification' in window&&Notification.permission==='granted'){$('settingsButton').click();}else enableNotifications();};
$('notificationSound').onchange=async()=>{const value=$('notificationSound').checked;if(!await commit(s=>{s.settings.notificationSound=value;}))$('notificationSound').checked=state.settings.notificationSound;};
$('enableNotifications').onclick=enableNotifications;
$('testNotification').onclick=()=>notify('알림이 잘 도착했어요','펫 캘린더가 다가오는 일정을 알려드립니다.',true);
$('defaultReminder').onchange=async()=>{const value=$('defaultReminder').value;await commit(s=>{s.settings.defaultReminder=value==='null'?null:Number(value);});};
for(const id of ['hotkeyEnabled','hotkeyCtrl','hotkeyAlt','hotkeyShift','hotkeyKey'])$(id).onchange=previewHotkey;
$('resetHotkey').onclick=()=>{renderHotkey(defaultHotkey());$('hotkeyStatus').textContent='기본값을 선택했습니다. ‘단축키 적용’을 누르면 저장됩니다.';$('hotkeyStatus').classList.remove('is-error');};
$('saveHotkey').onclick=async()=>{
  if(saving)return;
  let value;try{value=validateHotkey(hotkeyDraft());}catch(error){$('hotkeyStatus').textContent=error.message;$('hotkeyStatus').classList.add('is-error');return;}
  $('saveHotkey').disabled=true;$('hotkeyStatus').textContent='단축키 적용 중…';
  try {
    if(await commit(s=>{s.settings.hotkey=value;},{applyHotkey:true})){renderHotkey();await refreshHotkeyStatus();toast(value.enabled?'창 단축키를 설정했어요':'창 단축키를 껐어요',value.enabled?hotkeyLabel(value):'다른 프로그램에서 이 키를 다시 사용할 수 있습니다.');}
    else{$('hotkeyStatus').textContent=$('errorText').textContent;$('hotkeyStatus').classList.add('is-error');}
  }finally{$('saveHotkey').disabled=false;}
};
$('exportBackup').onclick=async()=>{
  let backup=state;
  try{if(nativeDesktop){const response=await fetch('/api/backup',{cache:'no-store'});if(!response.ok)throw new Error('펫과 위젯 설정을 불러오지 못했습니다.');backup=await response.json();}}catch(error){toast('백업하지 못했어요',error.message,'error');return;}
  const blob=new Blob([JSON.stringify(backup,null,2)],{type:'application/json'}),url=URL.createObjectURL(blob),a=document.createElement('a');
  a.href=url;a.download=`펫캘린더-백업-${dateKey()}.json`;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);toast('백업 파일을 내보냈어요',nativeDesktop?'저장한 위치에서 백업 파일을 확인해 주세요.':'브라우저의 다운로드 폴더에서 확인해 주세요.');
};
$('importBackup').onclick=()=>$('backupFile').click();
$('backupFile').onchange=async()=>{
  const file=$('backupFile').files[0];if(!file)return;
  try{
    if(file.size>5*1024*1024)throw new Error('백업 파일은 5MB 이하여야 합니다.');
    const backup=JSON.parse((await file.text()).replace(/^\uFEFF/,''));const combined=!!backup.calendar;
    if(combined&&!nativeDesktop)throw new Error('펫과 위젯이 포함된 백업은 데스크톱 앱에서 복원해 주세요.');
    const imported=validateState(combined?backup.calendar:backup);
    if(!await confirmAction('백업을 복원할까요?',`일정 ${imported.events.length}개, 할 일 ${imported.tasks.length}개로 현재 기록을 교체합니다.\n교체 전 기록은 이 PC의 자동 백업에 보관합니다.`,'복원'))return;
    if(combined){if(saving)return;saving=true;try{const response=await fetch('/api/backup',{method:'POST',headers:{'Content-Type':'application/json','X-Calendar-Request':'1'},body:JSON.stringify({...backup,expectedRevision:state.revision}),signal:AbortSignal.timeout(20000)});const result=await response.json();if(!response.ok)throw new Error(result.error||'백업을 복원하지 못했습니다.');state=readState(result);render();await updateWidget();await updatePets();toast('백업을 복원했어요','일정, 할 일, 펫과 위젯 설정을 불러왔습니다.');}finally{saving=false;}}
    else if(await commit(s=>{s.events=imported.events;s.tasks=imported.tasks;s.settings=imported.settings;}))toast('백업을 복원했어요','일정과 할 일, 설정을 불러왔습니다.');
  }catch(error){toast('백업 파일을 확인해 주세요',error.message,'error',10000);}
  finally{$('backupFile').value='';}
};
$('dismissError').onclick=()=>{$('errorBanner').hidden=true;};
$('reloadButton').onclick=async()=>{
  if(saving)return;
  try{state=await fetchState();$('errorBanner').hidden=true;render();saveStatus('최신 내용을 불러왔어요');toast('최신 내용을 불러왔어요','입력 중인 내용은 확인 후 다시 저장해 주세요.');}catch(error){banner(error.message);}
};
document.querySelectorAll('dialog').forEach(dialog=>dialog.addEventListener('cancel',event=>{if(saving)event.preventDefault();}));
document.addEventListener('keydown',event=>{
  if(event.ctrlKey||event.metaKey||event.altKey||event.isComposing||document.querySelector('dialog[open]')||event.target.closest('input,textarea,select,[contenteditable]'))return;
  if(event.key.toLowerCase()==='n'){event.preventDefault();openEvent();}
  if(event.key==='/'){event.preventDefault();$('searchInput').focus();}
  if(event.key.toLowerCase()==='t'){event.preventDefault();jumpTo(dateKey());}
  if(event.target.closest('[data-date]')||event.target.closest('button'))return;
  if(event.key==='ArrowLeft'){event.preventDefault();monthMove(-1);}
  if(event.key==='ArrowRight'){event.preventDefault();monthMove(1);}
});
window.addEventListener('focus',()=>{refresh(nativeDesktop);tick();checkReminders();});
document.addEventListener('visibilitychange',()=>{if(document.visibilityState==='visible'){refresh();checkReminders();}});
window.addEventListener('beforeunload',event=>{if(saving){event.preventDefault();event.returnValue='';}});
let resizeFrame;
new ResizeObserver(()=>{
  cancelAnimationFrame(resizeFrame);
  resizeFrame=requestAnimationFrame(()=>{if(loaded&&nativeVisible&&calendarDensity()!==lastDensity)renderCalendar();});
}).observe($('calendarGrid'));
const tourSteps=[
  {title:'일정을 추가해 보세요',text:'날짜를 선택하고 ‘+ 일정 추가’를 눌러 제목과 시간을 입력한 뒤 저장하세요.',demo:'<span class="tour-demo-button">+ 일정 추가</span>'},
  {title:'Ctrl + Tab으로 빠르게',text:'다른 프로그램을 사용 중에도 캘린더를 숨기거나 다시 열 수 있어요.\n단축키는 설정에서 변경할 수 있어요.',demo:'<kbd>Ctrl</kbd><span>+</span><kbd>Tab</kbd>'},
  {title:'함께할 펫을 골라보세요',text:'버튼을 누르면 고양이가 추가돼요. 설정 및 백업에서는 다른 펫도 고를 수 있어요.',demo:'<span class="tour-demo-pet" aria-hidden="true"></span><button id="tourAddPet" type="button" class="button primary">한 마리 추가</button>'}
];
let tourStep=0;
function renderTour(){
  const step=tourSteps[tourStep];
  $('tourPetStatus').textContent='';
  $('tourTitle').textContent=step.title;$('tourText').textContent=step.text;$('tourDemo').innerHTML=step.demo;
  $('tourProgress').textContent=`${tourStep+1} / ${tourSteps.length}`;
  $('tourBack').disabled=tourStep===0;$('tourNext').textContent=tourStep===tourSteps.length-1?'시작하기':'다음';
  [...$('tourDots').children].forEach((dot,index)=>dot.classList.toggle('active',index===tourStep));
}
function finishTour(){
  try{localStorage.setItem('petCalendar.quickTour.v1','done');}catch{}
  $('quickTour').close();
}
$('tourDemo').addEventListener('click',async event=>{
  const button=event.target.closest('#tourAddPet');if(!button||button.disabled)return;
  button.disabled=true;$('tourPetStatus').textContent='고양이를 데려오는 중이에요…';
  try{
    const response=await fetch('/api/pets',{method:'POST',headers:{'Content-Type':'application/json','X-Calendar-Request':'1'},body:JSON.stringify({action:'tour-spawn'}),signal:AbortSignal.timeout(15000)});
    const result=await response.json();if(!response.ok)throw new Error(result.error||'고양이를 추가하지 못했습니다.');
    $('tourPetStatus').textContent='고양이 한 마리를 데려왔어요!';
    updatePets();
  }catch(error){$('tourPetStatus').textContent=error.message||'고양이를 추가하지 못했습니다.';}
  finally{button.disabled=false;}
});
$('tourSkip').onclick=finishTour;
$('tourBack').onclick=()=>{if(tourStep>0){tourStep--;renderTour();}};
$('tourNext').onclick=()=>{if(tourStep===tourSteps.length-1)finishTour();else{tourStep++;renderTour();}};
$('quickTour').addEventListener('cancel',event=>{event.preventDefault();finishTour();});
function showQuickTour(){tourStep=0;renderTour();showDialog('quickTour');}
$('tourHelp').onclick=showQuickTour;
function startQuickTour(){
  if(!nativeDesktop)return;
  try{if(localStorage.getItem('petCalendar.quickTour.v1')==='done')return;}catch{}
  if(document.querySelector('dialog[open]'))return;
  showQuickTour();
}
async function init() {
  try{
    state=await fetchState();try{const response=await fetch('/holidays.json');if(response.ok)holidayDates=await response.json();}catch{}loaded=true;render();tick();$('loadingScreen').hidden=true;
    if(state.updatedAt)saveStatus(`이 PC에 저장됨 · ${new Date(state.updatedAt).toLocaleDateString('ko-KR')}`);
    await checkReminders();
    if(nativeDesktop)window.chrome.webview.postMessage({type:'desktop-ready',legacyReminders:localStorage.getItem('petCalendar.reminders')||localStorage.getItem('workCalendar.reminders')||'{}'});
    startQuickTour();
  }catch(error){$('loadingScreen').innerHTML=`${icon('flag')}<p>${esc(error.message)}</p><button id="retryLoad" class="button primary">다시 시도</button>`;$('retryLoad').onclick=init;}
}
if(nativeDesktop){
  document.documentElement.classList.add('native-desktop');
  $('windowControls').hidden=false;$('tourHelp').hidden=false;
  for(const [id,action] of [['windowMinimize','minimize'],['windowMaximize','maximize'],['windowClose','close']])$(id).onclick=()=>window.chrome.webview.postMessage({type:'window-action',action});
  $('widgetSettings').hidden=false;$('petSettings').hidden=false;
  let pendingAlarm=null;
  window.petCalendarVisibility=visible=>{nativeVisible=visible;if(!visible)remoteDirty=true;if(visible){if(petsPreference)updatePets();if(renderPending)render(true);refresh(true);tick();}};
  window.petCalendarOpenEvent=async(id,key)=>{
    if(document.querySelector('dialog[open]')){pendingAlarm={id,key};toast('일정을 선택했어요','입력창을 닫으면 해당 일정을 보여드릴게요.');return;}
    while(refreshPending)await new Promise(resolve=>setTimeout(resolve,10));
    if(remoteDirty)await refresh(true);
    if(document.querySelector('dialog[open]')){pendingAlarm={id,key};return;}
    if(id==='@dday'){$('deadlineCard').click();return;}
    clearTimeout(searchTimer);filter='all';search='';$('searchInput').value='';jumpTo(key);
    if(id===null||state.events.some(e=>e.id===id))openEvent(id,key);
  };
  document.querySelectorAll('dialog').forEach(d=>d.addEventListener('close',()=>{if(pendingAlarm&&!document.querySelector('dialog[open]')){const alarm=pendingAlarm;pendingAlarm=null;window.petCalendarOpenEvent(alarm.id,alarm.key);}if(remoteDirty&&nativeVisible&&!document.querySelector('dialog[open]'))refresh(true);}));
  window.chrome.webview.addEventListener('message',event=>{
    const message=event.data;
    if(message.type==='window-state'){
      const button=$('windowMaximize'),label=message.maximized?'이전 크기로':'최대화';
      button.setAttribute('aria-label',label);button.title=label;button.innerHTML=icon(message.maximized?'restore':'maximize');
    }
    if(message.type==='state-changed')refresh(true);
    if(message.type==='reminder')toast(message.title,message.body,'',15000);
    if(message.type==='open-event')window.petCalendarOpenEvent(message.eventId,message.startDate);
    if(message.type==='new-event')window.petCalendarOpenEvent(null,message.startDate);
    if(message.type==='widget-changed')updateWidget();
    if(message.type==='pets-changed')updatePets();
    if(message.type==='resume')window.petCalendarVisibility(true);
  });
}
let petsPreference=null,petsSaving=false,petsRefreshPending=false,petSpeechDirty=false;
async function updatePets(action,species,values={}) {
  if(!nativeDesktop)return;
  if(petsSaving){if(!action)petsRefreshPending=true;return;}
  petsSaving=true;
  const controls=[$('petsEnabled'),$('petSpecies'),$('addPet'),$('petSummaryEnabled'),$('petSummaryMinutes'),$('savePetSpeech'),$('previewPetSpeech'),$('petChatterEnabled')];controls.forEach(control=>control.disabled=true);
  try {
    const response=await fetch('/api/pets',action?{method:'POST',headers:{'Content-Type':'application/json','X-Calendar-Request':'1'},body:JSON.stringify({action,...(species?{species}:{}),...values}),signal:AbortSignal.timeout(15000)}:{signal:AbortSignal.timeout(8000)});
    const result=await response.json();if(!response.ok)throw new Error(result.error||'데스크톱 펫을 변경하지 못했습니다.');
    petsPreference=result;$('petsEnabled').checked=result.enabled;$('petChatterEnabled').checked=result.chatterEnabled;if(action==='configureSpeech')petSpeechDirty=false;if(!petSpeechDirty){$('petSummaryEnabled').checked=result.summaryEnabled;$('petSummaryMinutes').value=String(result.summaryIntervalMinutes);}
    $('petsStatus').textContent=result.enabled?(result.count?`펫 ${result.count}마리와 함께하고 있어요.`:"데스크톱 펫이 켜져 있어요. 원하는 펫을 추가해 보세요."):'데스크톱 펫이 꺼져 있습니다.';
  }catch(error){$('petsEnabled').checked=Boolean(petsPreference?.enabled);toast('데스크톱 펫을 확인해 주세요',error.message,'error');}
  finally {petsSaving=false;$('petChatterEnabled').checked=petsPreference?.chatterEnabled??true;$('petChatterEnabled').disabled=false;$('petsEnabled').disabled=false;for(const control of controls.slice(1,3))control.disabled=!petsPreference?.enabled;$('petSummaryEnabled').disabled=false;$('petSummaryMinutes').disabled=!$('petSummaryEnabled').checked;$('savePetSpeech').disabled=false;$('previewPetSpeech').disabled=!petsPreference?.enabled||!petsPreference?.count;if(petsRefreshPending){petsRefreshPending=false;queueMicrotask(()=>updatePets());}}
}
$('petChatterEnabled').onchange=()=>updatePets('configureChatter',null,{chatterEnabled:$('petChatterEnabled').checked});
$('petsEnabled').onchange=()=>updatePets($('petsEnabled').checked?'enable':'disable');
$('addPet').onclick=()=>updatePets('spawn',$('petSpecies').value);
$('petSummaryEnabled').onchange=()=>{petSpeechDirty=true;$('petSummaryMinutes').disabled=!$('petSummaryEnabled').checked;};
$('petSummaryMinutes').oninput=()=>{petSpeechDirty=true;};
$('savePetSpeech').onclick=()=>{const input=$('petSummaryMinutes');if(!input.checkValidity()){input.reportValidity();return;}updatePets('configureSpeech',null,{summaryEnabled:$('petSummaryEnabled').checked,summaryIntervalMinutes:Number(input.value)});};
$('previewPetSpeech').onclick=()=>updatePets('preview');
let widgetPreference=null,widgetSaving=false;
async function updateWidget(action,values={}) {
  if(!nativeDesktop||widgetSaving)return;
  widgetSaving=true;
  const controls=[$('toggleWidget'),$('widgetTopmost'),$('widgetLocked')];controls.forEach(c=>c.disabled=true);
  try{
    const response=await fetch('/api/widget',action?{method:'POST',headers:{'Content-Type':'application/json','X-Calendar-Request':'1'},body:JSON.stringify({action,...values}),signal:AbortSignal.timeout(8000)}:{signal:AbortSignal.timeout(8000)});
    const result=await response.json();if(!response.ok)throw new Error(result.error||'위젯 설정을 변경하지 못했습니다.');widgetPreference=result;
    $('toggleWidget').textContent=result.enabled?'위젯 닫기':'위젯 열기';$('widgetTopmost').checked=result.topmost;$('widgetLocked').checked=result.locked;
  }catch(error){if(widgetPreference){$('widgetTopmost').checked=widgetPreference.topmost;$('widgetLocked').checked=widgetPreference.locked;}toast('위젯 설정을 확인해 주세요',error.message,'error');}finally{widgetSaving=false;controls.forEach(c=>c.disabled=false);}
}
$('toggleWidget').onclick=()=>updateWidget(widgetPreference?.enabled?'hide':'show');
$('widgetTopmost').onchange=()=>updateWidget('configure',{topmost:$('widgetTopmost').checked});
$('widgetLocked').onchange=()=>updateWidget('configure',{locked:$('widgetLocked').checked});
setInterval(()=>{if(!nativeDesktop||nativeVisible&&document.visibilityState==='visible')tick();},nativeDesktop?60000:1000);
if(!nativeDesktop){setInterval(checkReminders,15000);setInterval(refresh,15000);}init();
