param([string]$AppDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(-not $AppDirectory){$AppDirectory=Join-Path $root 'dist\app'}
$stage=Join-Path $root 'artifacts\name-tag-emoji'
New-Item -ItemType Directory -Force -Path $stage | Out-Null
$compiler=Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$test=Join-Path $stage 'NameTagEmojiTests.exe'
& $compiler /nologo /target:exe /platform:x64 /codepage:65001 ('/out:'+$test) /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'NameTagEmojiTests.cs')
if($LASTEXITCODE -ne 0){throw 'Name tag emoji test build failed.'}
& $test (Join-Path $AppDirectory 'PetCalendar.exe') $stage
if($LASTEXITCODE -ne 0){throw 'Name tag emoji checks failed.'}
