# PetCalendar 자동 개발 작업 복사본

이 폴더는 OpenClaw 감독관이 Claude Code에 구현을 맡기는 격리 작업 복사본이다.
원본 C:\toyproject\petcalendar (/mnt/c/toyproject/petcalendar) 은 수정하지 않는다.

- 구현 담당은 Claude Code, 분석·독립 검토 담당은 Codex다. 배정된 역할을 지킨다.
- 감독관이 전달한 구체적 작업과 완료 기준을 따른다. 관련 없는 대규모 변경은 하지 않는다.
- 기존 사용자 변경이 포함된 snapshot을 기준으로 작업한다. 원본 작업을 되돌리지 않는다.
- Windows x64 / .NET Framework 4.8 / C# WinForms 앱이다. README와 desktop/build.ps1를 참고한다.
- WSL에서는 Windows PowerShell로 빌드/테스트한다:
  /home/luke1546/.local/bin/petcalendar-check build
  /home/luke1546/.local/bin/petcalendar-check test
- node tests에도 Windows 실행 파일을 쓰는 경우가 있으므로 종합 테스트는 위 test 명령을 사용한다.
- Full 테스트는 실제 마우스를 움직이므로 사용자가 요청한 경우만 실행한다.
- 이 설정 요청은 릴리스, push, 자동 병합을 포함하지 않는다.
- 작업 보고에 원인, 변경 파일, 결과, 실행한 검증과 실패/제한을 포함한다.
- 읽기 전용 또는 연동 검증을 요청받으면 파일을 수정하지 않는다.

Windows PowerShell은 서비스 PATH에 없을 수 있다. 커스텀 명령에는 /mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe 절대 경로를 사용한다.
