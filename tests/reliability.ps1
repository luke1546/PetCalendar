<#
Regression checks for the 2026-11-15 review (R1-R5):
 - a C# program built from the app sources: saves, alarm ledger, D-day, .ics COUNT
 - the built app (-AppDirectory) over --automation: a backup restore with a locked
   pet-diary.json must fail as a whole and leave every file as it was (R2)
#>
param([string]$AppDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$engine=Join-Path $root 'desktop\pet-engine'
$compiler=Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe=Join-Path $env:TEMP 'PetCalendar.ReliabilityTests.exe'
$compile=@('/nologo','/target:exe','/platform:x64','/codepage:65001','/main:PetCalendarDesktop.ReliabilityTests',('/out:'+$exe),('/win32manifest:'+(Join-Path $root 'desktop\app.manifest')))
$compile+=@('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.IO.Compression.dll') | ForEach-Object {'/reference:'+$_}
foreach($file in @('BrandMigration.cs','Core.cs','Recovery.cs','Alarms.cs','Widget.cs','StickyNotes.cs','PetSchedule.cs','PetVoice.cs','PetBubble.cs','PetSpeech.cs','PetLife.cs','PetLines.cs','PetLinesData.cs')){$compile+=Join-Path $root ('desktop\'+$file)}
# Widget shares the host API; use the complete host without its normal entry point.
foreach($file in @('PerfProbe.cs','NativeKit.cs','NativeFields.cs','NativeFlow.cs','CalendarView.cs','CalendarLogic.cs','CalendarWeek.cs','IcsImport.cs','CalendarBench.cs','Dialogs.cs','Settings.cs')){$compile+=Join-Path $root ('desktop\native\'+$file)}
$compile+=Join-Path $root 'desktop\Host.cs'
$compile+=Join-Path $root 'desktop\CalendarPets.cs'
foreach($file in @('Species.cs','Motion.cs','Native.cs','World.cs','SimulationLoop.cs','Palettes.cs','Sprites.cs','PetMenu.cs','PetHost.cs')){$compile+=Join-Path $engine $file}
$compile+=Join-Path $PSScriptRoot 'ReliabilityTests.cs'
$compile+=('/resource:'+(Join-Path $root 'desktop\pets.zip')+',petcalendar.pets.zip')
$compile+=('/resource:'+(Join-Path $root 'desktop\emoji.zip')+',petcalendar.emoji.zip')
$compile+=('/resource:'+(Join-Path $root 'desktop\fonts.zip')+',petcalendar.fonts.zip')
$compile+=('/resource:'+(Join-Path $root 'desktop\ui\holidays.json')+',petcalendar.holidays.json')
& $compiler $compile
if($LASTEXITCODE -ne 0){throw 'Reliability test build failed'}
$o=& $exe 2>&1|Out-String;Write-Output $o.Trim()
if($LASTEXITCODE -ne 0){throw 'Reliability checks failed'}

# R2 against the real app
if(-not $AppDirectory){$AppDirectory=Join-Path $root 'distpp'}
$data=Join-Path $env:TEMP ('petcalendar-reliability-'+[Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory $data|Out-Null
$utf8=New-Object Text.UTF8Encoding $false
[IO.File]::WriteAllText((Join-Path $data 'pet-diary.json'),'[{"date":"2026-11-14","pet":"초코","species":"cat","text":"원래 일기"}]',$utf8)
[IO.File]::WriteAllText((Join-Path $data 'sticky-notes.json'),'[{"id":"n1","text":"원래 메모","color":"yellow","x":100,"y":100,"width":240,"height":220,"closed":true}]',$utf8)
$psi=New-Object Diagnostics.ProcessStartInfo (Join-Path $AppDirectory 'PetCalendar.exe'),"--automation --data-dir `"$data`"";$psi.UseShellExecute=$false;$psi.RedirectStandardInput=$true;$psi.RedirectStandardOutput=$true;$psi.StandardOutputEncoding=[Text.Encoding]::UTF8
$p=[Diagnostics.Process]::Start($psi);$script:id=1
function Next{$t=$p.StandardOutput.ReadLineAsync();if(-not $t.Wait(20000)){throw 'app did not answer'};if($t.Result -eq $null){throw 'app exited'};$t.Result}
function Cmd($json){$script:id++;$b=[Text.Encoding]::UTF8.GetBytes('{"id":'+$script:id+','+$json+'}'+"`n");$p.StandardInput.BaseStream.Write($b,0,$b.Length);$p.StandardInput.BaseStream.Flush();while($true){$l=Next;if($l -match ('"id":'+$script:id+',')){return ($l|ConvertFrom-Json)}}}
$checks=0;function Check($ok,$name){if(-not $ok){throw "FAIL $name"};Write-Output "PASS $name";$script:checks++}
try{
  Next|Out-Null
  $backup=(Cmd '"action":"api","method":"GET","path":"/api/backup"').body
  $backup.calendar.tasks=@(@{id='t1';title='복원된 할 일';dueDate='';createdAt='2026-11-15T00:00:00.000Z';done=$false})
  $backup.notes[0].text='백업의 메모';$backup.diary=@(@{date='2026-11-15';pet='초코';species='cat';text='백업의 일기'})
  $backup|Add-Member expectedRevision $backup.calendar.revision -Force
  $diaryPath=Join-Path $data 'pet-diary.json'
  $lock=New-Object IO.FileStream $diaryPath,'Open','Read','Read'
  try{$r=Cmd ('"action":"api","method":"POST","path":"/api/backup","body":'+($backup|ConvertTo-Json -Depth 20 -Compress))}finally{$lock.Dispose()}
  Check ($r.status -ne 200) "restore with a locked diary file is reported as failed ($($r.status))"
  $after=(Cmd '"action":"api","method":"GET","path":"/api/backup"').body
  Check ($after.calendar.tasks.Count -eq 0 -and $after.calendar.revision -eq $backup.calendar.revision) 'the calendar is left as it was'
  Check ($after.notes[0].text -eq '원래 메모') 'notes written before the failure are put back'
  Check (([IO.File]::ReadAllText($diaryPath,[Text.Encoding]::UTF8)) -like '*원래 일기*') 'the diary file is unchanged'
  $r=Cmd ('"action":"api","method":"POST","path":"/api/backup","body":'+($backup|ConvertTo-Json -Depth 20 -Compress))
  $after=(Cmd '"action":"api","method":"GET","path":"/api/backup"').body
  Check ($r.status -eq 200 -and $after.calendar.tasks.Count -eq 1 -and $after.notes[0].text -eq '백업의 메모' -and $after.diary[0].text -eq '백업의 일기') 'the same restore succeeds once the file is free'
}finally{if(-not $p.HasExited){$p.Kill()};Start-Sleep -Milliseconds 300;Remove-Item -Recurse -Force $data -ErrorAction SilentlyContinue}
Write-Output "PASS: $checks restore checks against the app"
