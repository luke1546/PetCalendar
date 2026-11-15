# PetCalendar 자가발전 전용 브랜치

사용자는 이 브랜치에서 사람이 개입하지 않는 지속적 개선을 명시적으로 요청했다.
구현은 Claude Code, 분석과 독립 검토는 Codex, 작업 선택과 최종 판정은 OpenClaw가 담당한다.
외부 실행기가 버전·검증·커밋·태그·산출물·텔레그램 보고를 처리한다.

- 유일한 작업 폴더: /mnt/c/toyproject/petcalendar-autoevolve (Windows C:\toyproject\petcalendar-autoevolve).
- 원본 petcalendar 및 petcalendar-openclaw 폴더를 수정하지 않는다.
- 배정된 작은 개선 한 건만 처리하고 다른 에이전트를 호출하지 않는다.
- 질문이나 승인 대기 대신 주어진 범위에서 스스로 완료한다. 불가능한 것은 정확히 보고한다.
- 분석·검토 요청은 읽기만 한다. 구현 담당만 앱 코드를 수정한다.
- git commit/tag/push/reset/clean/stash/checkout, GitHub 배포, 기존 tools/release.ps1 실행은 외부 실행기만 담당한다.
- AGENTS.md, CLAUDE.md, .autoevolve-owner, .gitignore, .claude/, .github/, .codex/, tools/test.ps1,
  tools/release.ps1, desktop/build.ps1, desktop/package.ps1, desktop/build-portable.ps1을 변경하지 않는다.
- 기존 테스트를 삭제하거나 약화하지 않는다. 실제 회귀를 막는 테스트를 추가한다.
- 버전 숫자와 릴리스 노트는 외부 실행기가 관리하므로 구현 담당이 바꾸지 않는다.
- .env/인증 파일/사용자의 실제 일정 데이터/부모 폴더의 개인 파일에 접근하지 않는다.
- Windows x64 / .NET Framework 4.8 / WinForms 프로젝트다.
- Windows PowerShell: /mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe
- 필요시 빌드·테스트: /home/luke1546/.local/bin/petcalendar-autocheck build 또는 test
- Full 마우스 조작 테스트는 실행하지 않는다.
- 자동화의 성공 여부를 위해 기능을 무력화하거나 검증을 우회하지 않는다.
