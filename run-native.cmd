@echo off
cd /d "%~dp0"
if not exist dist\test-data mkdir dist\test-data
if exist "%LOCALAPPDATA%\PetCalendar\data\calendar.json" if not exist dist\test-data\calendar.json copy "%LOCALAPPDATA%\PetCalendar\data\calendar.json" dist\test-data\ >nul
if exist dist\app\PetCalendar.exe dist\app\PetCalendar.exe --data-dir "%~dp0dist\test-data" --control exit
timeout /t 2 /nobreak >nul
powershell -NoProfile -ExecutionPolicy Bypass -File desktop\build.ps1 > dist\build-log.txt 2>&1
if errorlevel 1 (echo BUILD FAILED>>dist\build-log.txt & type dist\build-log.txt & pause & exit /b 1)
echo BUILD OK>>dist\build-log.txt
start "" dist\app\PetCalendar.exe --data-dir "%~dp0dist\test-data"
