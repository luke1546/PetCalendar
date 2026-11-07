export const CATEGORIES = {
  work: { label: '업무', color: '#ffb078', light: '#fff0e3' },
  meeting: { label: '회의', color: '#a5d9f6', light: '#eaf6fe' },
  deadline: { label: '마감', color: '#f6acc1', light: '#ffedf2' },
  personal: { label: '개인', color: '#b9dfa7', light: '#f0f8e9' }
};
export const DAY = 86400000;
export const HOTKEY_KEYS = [[9,'Tab'],[32,'Space'],[192,'`'],...Array.from({length:26},(_,i)=>[65+i,String.fromCharCode(65+i)]),...Array.from({length:10},(_,i)=>[48+i,String(i)]),...Array.from({length:11},(_,i)=>[112+i,`F${i+1}`])];
export function defaultHotkey() {return {enabled:true,modifiers:2,key:9};}
export function validateHotkey(value = defaultHotkey()) {
  check(value && typeof value.enabled==='boolean' && Number.isInteger(value.modifiers) && value.modifiers>=0 && value.modifiers<=7
    && HOTKEY_KEYS.some(([key])=>key===value.key),'단축키 형식을 확인해 주세요.');
  check(!value.enabled || (value.modifiers&3)!==0 || (value.key>=112 && value.key<=122) || (value.modifiers===4 && value.key===192),'Ctrl 또는 Alt를 조합하거나 F1~F11, Shift + `를 선택해 주세요.');
  check(!value.enabled || !(value.modifiers===1 && value.key===9),'Alt + Tab은 Windows 창 전환 키입니다. 다른 조합을 선택해 주세요.');
  return {enabled:value.enabled,modifiers:value.modifiers,key:value.key};
}
export function hotkeyLabel(value) {
  return [...(value.modifiers&2?['Ctrl']:[]),...(value.modifiers&1?['Alt']:[]),...(value.modifiers&4?['Shift']:[]),HOTKEY_KEYS.find(([key])=>key===value.key)?.[1]||'?'].join(' + ');
}
export function dateKey(date = new Date()) {
  return `${date.getFullYear()}-${String(date.getMonth()+1).padStart(2,'0')}-${String(date.getDate()).padStart(2,'0')}`;
}
export function dayNumber(key) { return Date.parse(`${key}T00:00:00Z`) / DAY; }
export function addDays(key, days) { return new Date((dayNumber(key)+days)*DAY).toISOString().slice(0,10); }
export function isDate(value) {
  return typeof value === 'string' && /^[1-9]\d{3}-\d{2}-\d{2}$/.test(value)
    && Number.isFinite(dayNumber(value)) && new Date(dayNumber(value)*DAY).toISOString().slice(0,10) === value;
}
export function defaultState() {
  return { version: 1, revision: 0, updatedAt: null, events: [], tasks: [],
    settings: { hideCompleted: true, notifications: false, defaultReminder: 10, hotkey:defaultHotkey(), desktopPopups:true, notificationSound:true } };
}
function check(condition, message) { if (!condition) throw new Error(message); }
function str(value, max, label, required = false) {
  check(typeof value === 'string' && value.length <= max && (!required || value.trim().length > 0), `${label}을(를) 확인해 주세요.`);
  return value;
}
function time(value) { return typeof value === 'string' && (value === '' || /^([01]\d|2[0-3]):[0-5]\d$/.test(value)); }
export function validateState(input) {
  check(input && input.version === 1, '지원하는 펫 캘린더 백업 파일이 아닙니다.');
  check(Array.isArray(input.events) && Array.isArray(input.tasks), '일정 또는 할 일 형식이 올바르지 않습니다.');
  check(input.events.length <= 10000 && input.tasks.length <= 10000, '저장 가능한 항목 수를 초과했습니다.');
  const ids = new Set();
  function id(value) {
    str(value, 100, '항목 ID', true); check(!ids.has(value), '중복된 항목 ID가 있습니다.'); ids.add(value); return value;
  }
  const events = input.events.map(e => {
    check(e && isDate(e.startDate) && isDate(e.endDate) && e.endDate >= e.startDate, '일정의 시작일과 종료일을 확인해 주세요.');
    check(time(e.startTime) && time(e.endTime) && (!e.endTime || e.startTime), '일정 시간을 확인해 주세요.');
    check(e.endDate !== e.startDate || !e.endTime || e.endTime >= e.startTime, '종료 시간이 시작 시간보다 빠릅니다.');
    check(Object.hasOwn(CATEGORIES, e.category), '일정 분류를 확인해 주세요.');
    check(['none','daily','weekly','monthly'].includes(e.repeat) || /^monthly:(?:[1-4]|-1):[0-6]$/.test(e.repeat), '반복 방식을 확인해 주세요.');
    check(e.repeatUntil === '' || (isDate(e.repeatUntil) && e.repeatUntil >= e.startDate), '반복 종료일을 확인해 주세요.');
    check([null,0,10,30,60].includes(e.reminder), '알림 시간을 확인해 주세요.');
    check(typeof e.pinned === 'boolean', 'D-day 설정을 확인해 주세요.');
    check(e.pinnedDate===undefined || e.pinnedDate==='' || isDate(e.pinnedDate),'D-day 날짜를 확인해 주세요.');
    const completedDates=e.completedDates===undefined?[]:e.completedDates;
    check(Array.isArray(completedDates) && completedDates.length<=10000 && completedDates.every(isDate), '일정 완료 기록을 확인해 주세요.');
    const excludedDates=e.excludedDates===undefined?[]:e.excludedDates;
    check(Array.isArray(excludedDates) && excludedDates.length<=10000 && excludedDates.every(isDate), '반복 일정 제외 날짜를 확인해 주세요.');
    return { id: id(e.id), title: str(e.title,200,'일정 제목',true).trim(), startDate:e.startDate, endDate:e.endDate,
      startTime:e.startTime, endTime:e.endTime, category:e.category, note:str(e.note,5000,'메모'),
      repeat:e.repeat, repeatUntil:e.repeatUntil, reminder:e.reminder, pinned:e.pinned, pinnedDate:e.pinnedDate||'', completedDates:[...new Set(completedDates)], excludedDates:[...new Set(excludedDates)] };
  });
  const tasks = input.tasks.map(t => {
    check(t && (t.dueDate === '' || isDate(t.dueDate)) && typeof t.done === 'boolean', '할 일 형식을 확인해 주세요.');
    return { id:id(t.id), title:str(t.title,200,'할 일 제목',true).trim(), dueDate:t.dueDate, done:t.done, createdAt:str(t.createdAt,40,'생성 시각',true) };
  });
  const s = input.settings;
  check(s && typeof s.hideCompleted === 'boolean' && typeof s.notifications === 'boolean'
    && [null,0,10,30,60].includes(s.defaultReminder), '설정 형식을 확인해 주세요.');
  check((s.desktopPopups===undefined||typeof s.desktopPopups==='boolean')&&(s.notificationSound===undefined||typeof s.notificationSound==='boolean'),'설정 형식을 확인해 주세요.');
  return { version:1, revision:Number.isSafeInteger(input.revision) && input.revision >= 0 ? input.revision : 0,
    updatedAt:typeof input.updatedAt === 'string' ? input.updatedAt : null, events,tasks,
    settings:{ hideCompleted:s.hideCompleted, notifications:s.notifications, defaultReminder:s.defaultReminder, hotkey:validateHotkey(s.hotkey),desktopPopups:s.desktopPopups??true,notificationSound:s.notificationSound??true } };
}
export function occursOn(event, key) {
  if(event.excludedDates?.includes(key))return false;
  if (key < event.startDate || (event.repeat !== 'none' && event.repeatUntil && key > event.repeatUntil)) return false;
  if (event.repeat === 'none') return key === event.startDate;
  if (event.repeat === 'daily') return true;
  if (event.repeat === 'weekly') return (dayNumber(key)-dayNumber(event.startDate)) % 7 === 0;
  const [year,month,day] = key.split('-').map(Number);
  const last = new Date(Date.UTC(year,month,0)).getUTCDate();
  if(event.repeat.startsWith('monthly:')) {
    const [,week,weekday]=event.repeat.split(':').map(Number);
    return new Date(`${key}T12:00:00Z`).getUTCDay()===weekday && (week===-1?day+7>last:Math.ceil(day/7)===week);
  }
  return day === Math.min(Number(event.startDate.slice(8)),last);
}
const koreanCompare = new Intl.Collator('ko').compare;
const keyCompare = new Intl.Collator().compare;
const compareText = (a,b) => a < b ? -1 : a > b ? 1 : 0;
export function compareDayOccurrences(a,b) {
  return Number(isEventDone(a.event,a.startDate))-Number(isEventDone(b.event,b.startDate))
    || Number(!a.event.startTime)-Number(!b.event.startTime)
    || compareText(a.event.startTime,b.event.startTime)
    || compareText(a.event.endTime||'99:99',b.event.endTime||'99:99')
    || koreanCompare(a.event.title,b.event.title) || keyCompare(a.key,b.key);
}
function expand(events, from, to, visit) {
  const first=dayNumber(from),last=dayNumber(to);
  for(const event of events) {
    const origin=dayNumber(event.startDate),span=dayNumber(event.endDate)-origin;
    const begin=Math.max(origin,first-span),end=Math.min(last,event.repeat==='none'?origin:event.repeatUntil?dayNumber(event.repeatUntil):last);
    if(begin>end)continue;
    const excluded=new Set(event.excludedDates||[]);
    const emit=number=>{const key=new Date(number*DAY).toISOString().slice(0,10);if(!excluded.has(key))visit({event,key:`${event.id}@${key}`,startDate:key,endDate:span?new Date((number+span)*DAY).toISOString().slice(0,10):key});};
    if(event.repeat==='none'){emit(origin);continue;}
    if(event.repeat==='daily'||event.repeat==='weekly') {
      const step=event.repeat==='weekly'?7:1;
      for(let day=origin+Math.ceil((begin-origin)/step)*step;day<=end;day+=step)emit(day);
      continue;
    }
    const date=new Date(begin*DAY),until=new Date(end*DAY);
    const rule=event.repeat.split(':').map(Number),originalDay=Number(event.startDate.slice(8));
    for(let year=date.getUTCFullYear(),month=date.getUTCMonth();year<until.getUTCFullYear()||year===until.getUTCFullYear()&&month<=until.getUTCMonth();month===11?(year++,month=0):month++) {
      const days=new Date(Date.UTC(year,month+1,0)).getUTCDate();
      const day=event.repeat==='monthly'?Math.min(originalDay,days):rule[1]===-1?days-(new Date(Date.UTC(year,month,days)).getUTCDay()-rule[2]+7)%7:1+(rule[2]-new Date(Date.UTC(year,month,1)).getUTCDay()+7)%7+(rule[1]-1)*7;
      const number=Date.UTC(year,month,day)/DAY;
      if(number>=begin&&number<=end)emit(number);
    }
  }
}
export function occurrences(events, from, to) {
  const result=[];
  expand(events,from,to,o=>result.push(o));
  return result.sort((a,b)=>compareText(a.startDate,b.startDate)||compareText(a.event.startTime,b.event.startTime)||koreanCompare(a.event.title,b.event.title));
}
// Expand each recurrence once; each visible date owns its already sorted rows.
export function indexDates(events, from, to) {
  const days=new Map();
  for(let key=from;key<=to;key=addDays(key,1))days.set(key,[]);
  expand(events,from,to,o=>{
    for(let key=o.startDate<from?from:o.startDate;key<=to&&key<=o.endDate;key=addDays(key,1))days.get(key).push(o);
  });
  for(const rows of days.values())rows.sort(compareDayOccurrences);
  return days;
}
export function onDate(list, key) {
  return list.filter(o=>o.startDate<=key&&o.endDate>=key).sort(compareDayOccurrences);
}
export function repeatLabel(repeat) {
  if(repeat.startsWith('monthly:')){const [,week,day]=repeat.split(':').map(Number);return `매월 ${{1:'첫째',2:'둘째',3:'셋째',4:'넷째',[-1]:'마지막'}[week]} ${['일','월','화','수','목','금','토'][day]}요일`;}
  return {none:'반복 안 함',daily:'매일',weekly:'매주',monthly:'매월'}[repeat];
}
export function isEventDone(event, startDate = event.startDate) {
  return (event.completedDates||[]).includes(startDate);
}
export function dday(key, today = dateKey()) {
  const diff = dayNumber(key)-dayNumber(today);
  return diff === 0 ? 'D-day' : diff > 0 ? `D−${diff}` : `D+${-diff}`;
}
export function ddayOccurrence(event, today = dateKey()) {
  let startDate=event.startDate;
  if(event.repeat!=='none') {
    if(event.pinnedDate && occursOn(event,event.pinnedDate))startDate=event.pinnedDate;
    else {
      const from=today>event.startDate?today:event.startDate;
      let found=false;
      for(let i=0;i<=62;i++) {
        const key=addDays(from,i);
        if(occursOn(event,key)){startDate=key;found=true;break;}
      }
      if(!found && event.repeatUntil) {
        for(let i=0;i<=62;i++) {
          const key=addDays(event.repeatUntil,-i);
          if(occursOn(event,key)){startDate=key;break;}
        }
      }
    }
  }
  return {event,startDate,endDate:addDays(startDate,dayNumber(event.endDate)-dayNumber(event.startDate))};
}
export function selectedDday(events, today = dateKey()) {
  const selected=events.find(event=>event.pinned);
  return selected?ddayOccurrence(selected,today):null;
}
export function reminderTime(occurrence) {
  if (occurrence.event.reminder === null || isEventDone(occurrence.event,occurrence.startDate)) return null;
  return new Date(`${occurrence.startDate}T${occurrence.event.startTime || '09:00'}:00`).getTime()-occurrence.event.reminder*60000;
}
