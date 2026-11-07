$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$compiler=Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe=Join-Path $env:TEMP 'PetCalendar.PetScheduleTests.exe'
& $compiler /nologo /target:exe /codepage:65001 /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll ('/out:'+$exe) (Join-Path $root 'desktop\Core.cs') (Join-Path $root 'desktop\PetSchedule.cs') (Join-Path $PSScriptRoot 'PetScheduleTests.cs')
if($LASTEXITCODE -ne 0){throw 'Schedule test build failed'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Schedule tests failed'}
