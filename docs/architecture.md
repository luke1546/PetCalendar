# 펫 캘린더 구조와 검증

이 문서는 **지금 빌드되는 코드**만 설명합니다. `desktop/build.ps1`에 적힌 파일이 곧 앱이고, 거기 없는 파일은 앱에 들어가지 않습니다.

## 실행 파일 하나, 창 여러 개

`desktop/build.ps1`이 .NET Framework 4.8 C# 컴파일러(`csc`)로 `PetCalendar.exe` 하나를 만듭니다. WebView2와 `desktop/ui`의 웹 화면은 v3.0에서 걷어냈고, 지금 화면은 모두 WinForms로 직접 그립니다.

| 부분 | 파일 |
|---|---|
| 시작 · 단일 실행 · 설치 보조 명령 | `desktop/Entry.cs`, `desktop/BrandMigration.cs` |
| 데이터 규칙(검증, 반복 계산, D-day, 알림 시각) · 저장 | `desktop/Core.cs` (`Rules`, `StateStore`, `DataRecovery`, `FocusMode`) |
| 저장 파일이 깨졌을 때 백업 고르기 | `desktop/Recovery.cs` |
| 숨은 호스트 창 · 트레이 · 내부 API(`/api/...`) · 자동화 명령 | `desktop/Host.cs` |
| **캘린더 창** | `desktop/native/` — `CalendarView.cs`(배치·그리기·입력), `CalendarLogic.cs`(저장·되돌리기·동작), `CalendarWeek.cs`(주간 보기), `Dialogs.cs`, `Settings.cs`, `IcsImport.cs`(가져오기·내보내기), 공용 위젯 `NativeKit.cs`/`NativeFlow.cs`/`NativeFields.cs` |
| 바탕화면 위젯 · 알림 팝업 · 스티커 메모 | `desktop/Widget.cs`, `desktop/Alarms.cs`, `desktop/StickyNotes.cs` |
| 펫 | `desktop/pet-engine/`(움직임·창 위 걷기·프레임 루프), `desktop/CalendarPets.cs`, `PetSpeech.cs`, `PetBubble.cs`, `PetLife.cs`(침대·장난감·일기), `PetLines*.cs` |

`desktop/ui/model.mjs`는 화면이 아니라 **일정 규칙의 JavaScript 사본**입니다. `tests/desktop-parity.test.mjs`가 C# `Rules`와 같은 결과를 내는지 비교하므로, 반복 규칙을 바꿀 때는 두 곳을 함께 고칩니다.

## 데이터 파일 (`%LOCALAPPDATA%\PetCalendar\data`)

| 파일 | 내용 | 손상되면 |
|---|---|---|
| `calendar.json` | 일정 · 할 일 · 캘린더 설정 | 시작할 때 `backups/`의 자동 백업(저장할 때마다, 최근 30개) 중 하나를 골라 복구 |
| `sticky-notes.json`, `pet-diary.json` | 스티커 메모, 펫 일기 | 원본을 `*.broken-날짜.json`으로 보관하고 빈 상태로 시작, 알림 표시 |
| `pets.json`, `widget.json` | 펫 · 위젯 설정 | — |
| `ui-state.json` | 이 PC만의 화면 상태(주간 보기, 정기 백업 폴더 등) | — |

통합 백업(설정 → 데이터 · 백업)은 위 내용을 한 JSON 파일에 담고, 정기 백업은 같은 파일을 지정한 폴더에 만듭니다.

## 검증

```powershell
.\tools\test.ps1          # 임시 폴더에 빌드한 뒤 전체 검사
.\tools\test.ps1 -Full    # 말풍선 창 검사까지 (실제 마우스를 움직이니 그동안 손대지 마세요)
```

`tools/release.ps1`은 커밋하기 전에 같은 `tools/test.ps1`을 실행하고, 하나라도 실패하면 아무것도 올리지 않습니다. 검사 목록은 `tools/test.ps1` 맨 위에 있고, 거기 없는 테스트 파일은 검사에 포함되지 않습니다.

실제 화면 동작은 `--automation`으로 앱을 띄워 표준 입력으로 명령을 보내 확인합니다. 예: `calendar-state`(선택한 날짜, 열린 창, 화면 좌표), `calendar-bench`(그리기 시간), `focus`, `pets-inspect`, `api`.
