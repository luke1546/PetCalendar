# 업무 캘린더 · PetCalendar

일정과 할 일을 정리하고, 데스크톱 펫과 함께하는 Windows 캘린더입니다.

## 화면

![업무 캘린더](docs/screenshots/calendar.png)

| 바탕화면 위젯 | 펫 말풍선 |
| --- | --- |
| ![위젯](docs/screenshots/widget.png) | ![펫 말풍선](docs/screenshots/pet-bubble.png) |

스크린샷에는 설명을 위한 예시 일정만 사용했습니다.

## 주요 기능

- 일정과 할 일, 반복 일정, 완료 표시, 검색 및 D-day
- 바탕화면 위젯과 일정 알림, JSON 백업
- 고양이, 포메라니안, 레서판다, 카피바라, 다람쥐, 알파카, 코끼리 데스크톱 펫
- 펫별 이름과 일상 대화, 오늘 일정 안내 및 알림 말풍선
- 위젯과 펫 표시 여부, 알림 소리와 안내 주기 설정

## 실행 환경

Windows x64, .NET Framework 4.8, Microsoft Edge WebView2 Runtime이 필요합니다. 일정과 설정은 사용자 PC의 `%LOCALAPPDATA%\\PetCalendar`에 저장됩니다.

## 소스에서 빌드

Windows PowerShell에서 다음 명령을 실행합니다.

```powershell
.\\desktop\\build.ps1
```

결과물은 `dist/app/PetCalendar.exe`입니다. 설치 파일 생성에는 NSIS가 필요하며, 설치한 컴파일러 경로를 지정할 수 있습니다.

```powershell
.\\desktop\\package.ps1 -Compiler 'C:\\Program Files (x86)\\NSIS\\makensis.exe'
```

테스트에는 Node.js와 Windows 환경이 필요합니다.

```powershell
node --test tests/desktop-widget.test.mjs
node --test tests/desktop-pet-speech.test.mjs
powershell -ExecutionPolicy Bypass -File tests/pet-schedule.ps1
```

## 구성

- `desktop/` — Windows 앱, 위젯, 펫과 화면 소스
- `desktop/pet-engine/` — 펫 애니메이션 및 동작 엔진
- `desktop/pets.zip` — 앱에 포함되는 동물 이미지와 프레임 정보
- `tests/` — 일정, 위젯, 펫과 설치 동작 검증
- `docs/screenshots/` — 샘플 데이터로 촬영한 화면

동물 에셋은 제작자의 `token-usage-main` 프로젝트에서 가져왔습니다. WebView2 구성 요소의 출처와 라이선스는 `desktop/webview2/`를 참고하세요.
