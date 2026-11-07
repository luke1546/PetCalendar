param([string]$Compiler,[switch]$Qa,[string]$AppDirectory)
$ErrorActionPreference='Stop'
$calendarRoot=Split-Path $PSScriptRoot -Parent
if(-not $Compiler){$Compiler=Join-Path $calendarRoot 'artifacts\tools\nsis-3.11\makensis.exe'}
if(-not (Test-Path -LiteralPath $Compiler)){throw 'NSIS 3.11 compiler is required.'}
$calendarOptions=@('/V2')
if($Qa){$calendarOptions+='/DQA_BUILD'}
if($AppDirectory){$calendarOptions+=('/DAPP_DIRECTORY='+[IO.Path]::GetFullPath($AppDirectory))}
$calendarOptions+=Join-Path $PSScriptRoot 'installer.nsi'
& $Compiler $calendarOptions
if($LASTEXITCODE -ne 0){throw 'Installer build failed.'}
