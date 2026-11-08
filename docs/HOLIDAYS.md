# 공휴일 자료 관리

앱은 오프라인 동작을 위해 `desktop/ui/holidays.json`의 대한민국 공휴일 자료를 사용합니다. 2020~2050년을 지원하며, 범위 밖은 ‘공휴일 자료 없음’으로 표시합니다.

2028~2050년은 2026년 11월 기준 법령에 따라 산출한 **예상 공휴일**입니다. 임시공휴일, 향후 선거일과 법령 변경은 미리 확정할 수 없어 반영하지 않았습니다. 달력의 월별 표시와 해당 날짜의 접근성 설명 및 툴팁에도 예상임을 표시합니다. 공식 자료가 발표되거나 임시공휴일이 지정되면 앱 업데이트로 자료를 갱신해야 합니다.

매년 다음 공식 자료를 확인하여 `tools/holiday_overrides.json`에 확인된 연도의 모든 공휴일을 기록하고, `tools/generate_holidays.py`의 `CONFIRMED` 연도와 `desktop/ui/app.mjs`의 예상 표시 기준을 함께 갱신합니다. 생성 스크립트는 검증된 연도에는 공공 자료를 우선하고, 나머지 연도에는 현행 법령에 따른 추정치를 넣습니다. 연도별 달력자료는 원래 발표 후 임시공휴일이나 법령이 바뀔 수 있으므로 후속 발표도 대조해야 합니다.

- [한국천문연구원 달력자료](https://astro.kasi.re.kr/kor/life/post/calendarData)
- [우주항공청 발표 월력요항](https://astro.kasi.re.kr/life/post/almanac)
- [인사혁신처 공휴일 보도자료](https://www.mpm.go.kr/mpm/comm/newsPress/newsPressRelease/?mode=list&searchCondition=title&searchKeyword=%EA%B3%B5%ED%9C%B4%EC%9D%BC)
- 미래 날짜 산출: MIT 라이선스의 [Python holidays 0.107](https://github.com/vacanza/holidays) 한국 공휴일 규칙. 설날·부처님오신날·추석은 2020~2050년을 Windows 한국 음력과 대조했습니다.

생성 명령(배포 앱에는 Python이나 인터넷 연결이 필요하지 않음):

```sh
python -m pip install holidays==0.107
python tools/generate_holidays.py
```

`holidays.json`은 `YYYY-MM-DD`를 날짜 키로, 표시할 공휴일 이름을 값으로 저장합니다. 같은 날짜에 공휴일이 둘 이상이면 ` · `로 구분합니다. 화면 표시와 펫 인사가 같은 파일을 사용합니다. 갱신 후 공휴일·일정 및 펫 말풍선 검사를 실행하세요.
