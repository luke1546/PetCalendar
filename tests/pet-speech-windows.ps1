$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$engine=Join-Path $root 'desktop\pet-engine'
$compiler=Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe=Join-Path $env:TEMP 'PetCalendar.PetSpeechWindowsTests.exe'
$compile=@('/nologo','/target:exe','/platform:x64','/codepage:65001','/main:PetCalendarDesktop.PetSpeechWindowsTests',('/out:'+$exe),('/win32manifest:'+(Join-Path $root 'desktop\app.manifest')))
$compile+=@('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.IO.Compression.dll') | ForEach-Object {'/reference:'+$_}
foreach($file in @('Core.cs','Alarms.cs','Widget.cs','PetSchedule.cs','PetVoice.cs','PetBubble.cs','PetSpeech.cs')){$compile+=Join-Path $root ('desktop\'+$file)}
# Widget shares the host API; use the complete host without its normal entry point.
$compile+=Join-Path $root 'desktop\Host.cs'
$compile+=Join-Path $root 'desktop\CalendarPets.cs'
foreach($library in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')){$compile+=('/reference:'+(Join-Path $root ('desktop\webview2\'+$library)))}
foreach($file in @('Species.cs','Motion.cs','Native.cs','World.cs','SimulationLoop.cs','Palettes.cs','Sprites.cs','PetMenu.cs','PetHost.cs')){$compile+=Join-Path $engine $file}
$compile+=Join-Path $PSScriptRoot 'PetSpeechWindowsTests.cs'
$compile+=('/resource:'+(Join-Path $root 'desktop\pets.zip')+',petcalendar.pets.zip')
$compile+=('/resource:'+(Join-Path $root 'desktop\emoji.zip')+',petcalendar.emoji.zip')
$compile+=('/resource:'+(Join-Path $root 'desktop\ui\holidays.json')+',petcalendar.holidays.json')
& $compiler $compile
if($LASTEXITCODE -ne 0){throw 'Native speech test build failed'}
& $exe (Join-Path $root 'artifacts')
if($LASTEXITCODE -ne 0){throw 'Native speech checks failed'}
