<#
One command for every automated check of the app in this repository:

  .\tools\test.ps1                         # build to a temp folder, then run everything
  .\tools\test.ps1 -AppDirectory dist\app  # test an existing build
  .\tools\test.ps1 -Full                   # also the speech-window test (moves the real mouse; don't touch it)

It runs the node tests (JS model, C#/JS parity, native app over --automation) and the
PowerShell test programs against the same PetCalendar.exe, prints one line per suite and
exits 1 if anything failed. tools/release.ps1 runs exactly this before committing.

The list below is the whole suite. A test file that is not listed here is not part of it.
#>
param([string]$AppDirectory,[switch]$Full)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent;Set-Location $root
$nodeTests=@('tests/calendar.test.mjs','tests/calendar-index.test.mjs','tests/calendar-order-repeat.test.mjs','tests/desktop-parity.test.mjs','tests/native-pets-tour.test.mjs','tests/native-affinity-badges.test.mjs')
$psTests=@('tests/toy-follow.ps1','tests/name-tag-emoji.ps1','tests/font-coverage.ps1','tests/brand-migration.ps1','tests/pet-schedule.ps1')
# pet-speech-windows drives the real mouse pointer over bubbles, so it is opt-in (and kept out of releases).
if($Full){$psTests+='tests/pet-speech-windows.ps1'}

if(-not $AppDirectory){
    $AppDirectory=Join-Path $env:TEMP 'petcalendar-test-build'
    Write-Host "Building to $AppDirectory"
    & .\desktop\build.ps1 -OutputDirectory $AppDirectory *> (Join-Path $env:TEMP 'petcalendar-test-build.log')
    if(-not (Test-Path (Join-Path $AppDirectory 'PetCalendar.exe'))){Write-Output 'BUILD FAILED (see %TEMP%\petcalendar-test-build.log)';exit 1}
}
$AppDirectory=(Resolve-Path $AppDirectory).Path
$env:PET_CALENDAR_TEST_EXE=Join-Path $AppDirectory 'PetCalendar.exe'
$failed=$false

$out=node --test @nodeTests 2>&1|Out-String;$m=[regex]::Match($out,'# pass (\d+)[\s\S]*?# fail (\d+)')
if($LASTEXITCODE -ne 0 -or -not $m.Success -or $m.Groups[2].Value -ne '0'){$failed=$true;Write-Output ("node tests FAILED`n"+$out.Substring([Math]::Max(0,$out.Length-3000)))}
else{Write-Output "node tests: $($m.Groups[1].Value) passed"}

foreach($ps in $psTests){
    $o=powershell -NoProfile -ExecutionPolicy Bypass -File $ps -AppDirectory $AppDirectory 2>&1|Out-String
    $line=($o -split "`n"|Where-Object{$_ -match 'PASS|passed'}|Select-Object -Last 1)
    if($LASTEXITCODE -ne 0 -or -not $line){$failed=$true;Write-Output ("$ps FAILED`n"+$o.Substring([Math]::Max(0,$o.Length-2000)))}
    else{Write-Output "$ps $($line.Trim())"}
}
if($failed){exit 1}
