param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$calendarRoot=Split-Path $PSScriptRoot -Parent
if(-not $OutputDirectory){$OutputDirectory=Join-Path $calendarRoot 'dist\app'}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
# The native build has no web assets or WebView2; clear leftovers from older builds.
foreach($legacy in @('public','webview2')){$legacyPath=Join-Path $OutputDirectory $legacy;if(Test-Path -LiteralPath $legacyPath){Remove-Item -LiteralPath $legacyPath -Recurse -Force}}
Copy-Item -LiteralPath (Join-Path $calendarRoot 'calendar.ico') -Destination $OutputDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PetCalendar.exe.config') -Destination $OutputDirectory
$calendarCompiler=Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$calendarReferences=@('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.IO.Compression.dll') | ForEach-Object {'/reference:'+$_}
$calendarArgs=@('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001',('/out:'+(Join-Path $OutputDirectory 'PetCalendar.exe')),('/win32icon:'+(Join-Path $calendarRoot 'calendar.ico')))+$calendarReferences+@(
 ('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest')),
 (Join-Path $PSScriptRoot 'BrandMigration.cs'),(Join-Path $PSScriptRoot 'Core.cs'),(Join-Path $PSScriptRoot 'Alarms.cs'),(Join-Path $PSScriptRoot 'Widget.cs'),(Join-Path $PSScriptRoot 'Host.cs'),(Join-Path $PSScriptRoot 'Entry.cs'))
# Native calendar window (replaces desktop/ui + WebView2).
foreach($nativeFile in @('PerfProbe.cs','NativeKit.cs','NativeFields.cs','NativeFlow.cs','CalendarView.cs','CalendarLogic.cs','Dialogs.cs','Settings.cs')){$calendarArgs+=(Join-Path $PSScriptRoot ('native\'+$nativeFile))}
$calendarArgs+=('/resource:'+(Join-Path $PSScriptRoot 'emoji.zip')+',petcalendar.emoji.zip')
$calendarArgs+=('/resource:'+(Join-Path $PSScriptRoot 'fonts.zip')+',petcalendar.fonts.zip')
$calendarArgs+=('/resource:'+(Join-Path $PSScriptRoot 'ui\holidays.json')+',petcalendar.holidays.json')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'EMOJI-LICENSE.md') -Destination $OutputDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FONT-LICENSE.md') -Destination $OutputDirectory
$petEngine=Join-Path $calendarRoot 'desktop\pet-engine'
foreach($petFile in @('Species.cs','Motion.cs','Native.cs','World.cs','SimulationLoop.cs','Palettes.cs','Sprites.cs','PetMenu.cs','PetHost.cs')){$calendarArgs+=(Join-Path $petEngine $petFile)}
$calendarArgs+=(Join-Path $PSScriptRoot 'CalendarPets.cs')
foreach($speechFile in @('PetSchedule.cs','PetVoice.cs','PetBubble.cs','PetSpeech.cs')){$calendarArgs+=(Join-Path $PSScriptRoot $speechFile)}
$petPack=Join-Path $PSScriptRoot 'pets.zip'
if(-not (Test-Path $petPack)){throw 'Run tools/build_animal_pack.py to create the validated animal pack.'}
$calendarArgs+=('/resource:'+$petPack+',petcalendar.pets.zip')
& $calendarCompiler $calendarArgs
if($LASTEXITCODE -ne 0){throw 'Desktop build failed.'}
& $calendarCompiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 ('/out:'+(Join-Path $OutputDirectory 'RemovePetCalendar.exe')) ('/win32icon:'+(Join-Path $calendarRoot 'calendar.ico')) /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'Removal.cs')
if($LASTEXITCODE -ne 0){throw 'Removal helper build failed.'}
$calendarWave=Join-Path $OutputDirectory 'notification.wav'
$calendarStream=[IO.File]::Create($calendarWave)
$calendarWriter=New-Object IO.BinaryWriter($calendarStream)
try {
 $calendarRate=22050;$calendarSamples=[int]($calendarRate*0.28);$calendarLength=$calendarSamples*2
 $calendarWriter.Write([Text.Encoding]::ASCII.GetBytes('RIFF'));$calendarWriter.Write([int](36+$calendarLength));$calendarWriter.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '));$calendarWriter.Write([int]16);$calendarWriter.Write([int16]1);$calendarWriter.Write([int16]1);$calendarWriter.Write([int]$calendarRate);$calendarWriter.Write([int]($calendarRate*2));$calendarWriter.Write([int16]2);$calendarWriter.Write([int16]16);$calendarWriter.Write([Text.Encoding]::ASCII.GetBytes('data'));$calendarWriter.Write([int]$calendarLength)
 for($calendarIndex=0;$calendarIndex -lt $calendarSamples;$calendarIndex++){$calendarTime=$calendarIndex/$calendarRate;$calendarEnvelope=[Math]::Min(1,$calendarTime/0.015)*[Math]::Exp(-12*$calendarTime);$calendarValue=5500*$calendarEnvelope*([Math]::Sin(2*[Math]::PI*880*$calendarTime)+0.35*[Math]::Sin(2*[Math]::PI*1320*$calendarTime));$calendarWriter.Write([int16]$calendarValue)}
}finally{$calendarWriter.Dispose()}
Write-Output (Join-Path $OutputDirectory 'PetCalendar.exe')
