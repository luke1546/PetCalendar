"""Generate Korean public holidays; review official overrides before every release.

Requires: python -m pip install holidays==0.107
Future dates are estimates based on rules known at generation time.
"""
import json
from pathlib import Path
import holidays

ROOT=Path(__file__).resolve().parent.parent
START,END,CONFIRMED=2020,2050,2027
OVERRIDES=ROOT/'tools'/'holiday_overrides.json'
OUTPUT=ROOT/'desktop'/'ui'/'holidays.json'


def normalize(name):
    if name=='신정연휴':return '1월 1일'
    if name in ('설날 전날','설날 다음날'):return '설날'
    if name in ('추석 전날','추석 다음날'):return '추석'
    if name.endswith(' 대체 휴일'):return '대체공휴일('+name.removesuffix(' 대체 휴일')+')'
    if name=='국군의 날':return '임시공휴일(국군의 날)'
    return name


def main():
    calendar=holidays.country_holidays('KR',years=range(START,END+1),language='ko')
    result={}
    for day,description in sorted(calendar.items()):
        if not START<=day.year<=END:continue
        parts=description.split('; ')
        # Election schedules and ad hoc holidays can change: only include verified past dates.
        if day.year>CONFIRMED:parts=[name for name in parts if '선거' not in name and '임시공휴일' not in name]
        names=list(dict.fromkeys(map(normalize,parts)))
        if names:result[day.isoformat()]=' · '.join(names)
    # Officially reviewed years replace generated records, including emergency elections.
    overrides=json.loads(OVERRIDES.read_text(encoding='utf-8'))
    for year in {key[:4] for key in overrides}:
        result={key:value for key,value in result.items() if not key.startswith(year+'-')}
    result.update(overrides)
    OUTPUT.write_text(json.dumps(dict(sorted(result.items())),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f'{len(result)} holiday dates written to {OUTPUT}')

if __name__=='__main__':main()
