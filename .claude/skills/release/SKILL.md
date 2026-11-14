---
name: release
description: Release PetCalendar to GitHub in one pass (version bump, tests, installer, single-file portable exe, checksums, commit, tag, push, GitHub release). Use when the user asks to release, deploy or 배포/릴리즈 a version (e.g. "3.3.5 릴리즈", "릴리즈해줘").
---

# PetCalendar release

Everything mechanical is done by `tools/release.ps1`. Your job is only: pick the version, write the notes, run the script, report the link.

## 1. Version
- Use the version the user names. Otherwise take the latest tag (`git tag --sort=-creatordate | head -1`) and bump the patch number (3.3.4 → 3.3.5); a batch of new features bumps the minor (3.3.x → 3.4.0).
- Make sure the working branch is current: `git fetch -q --tags origin` and, if `origin/main` is ahead, `git merge --ff-only origin/main` first (releases also happen from other sessions).

## 2. Notes: `docs/releases/v<version>.md`
Korean, for users, short. Look at `git log --oneline <last tag>..HEAD` and `git diff --stat <last tag>` to see what changed, then follow this shape:

```markdown
펫 캘린더 v3.3.5입니다. <한 줄 요약>

## <변경 묶음 제목>

- <사용자가 체감하는 변화, 무엇이 어떻게 달라졌는지>

## 다운로드

- **`PetCalendar-Setup-3.3.5.exe`**: 설치판입니다. 기존 설치 위에 덮어 설치하면 일정과 설정이 그대로 유지됩니다.
- **`PetCalendar-Portable-3.3.5.exe`**: 설치 없이 이 파일 하나로 실행됩니다.

Windows x64, .NET Framework 4.8이 필요합니다. 체크섬은 `SHA256SUMS.txt`에서 확인할 수 있습니다.
```

## 3. Run (one command, about 2–3 minutes)
```powershell
.\tools\release.ps1 -Version 3.3.5 -Title "<short English commit summary>"
```
- Tracked files that changed are committed automatically. New files from this change need `-Add path1,path2` (check `git status --short`; never add unrelated untracked files).
- `-DryRun` stops before commit/push (builds and tests only). `-SkipTests` only if the user asks for speed and tests already passed this session.
- Run it in the background (it takes a few minutes) and wait for the result; do not poll.
- Release assets are uploaded as `PetCalendar-Setup-<version>.exe` and `PetCalendar-Portable-<version>.exe`; use those names in the notes' 다운로드 section.
- The script bumps every version spot (app.manifest, Entry.cs, PortableLauncher.cs, installer.nsi, README), runs the tests in a temp copy in parallel with the release build, refuses to commit if any test fails, and checks that all three exes report the new version.

## 4. Report
Reply in Korean with the release URL the script prints, the asset sizes, and what was in it. If the script stopped, say at which step and that nothing was published unless it got past "Publishing GitHub release".
