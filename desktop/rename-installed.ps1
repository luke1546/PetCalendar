$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root=Split-Path $PSScriptRoot -Parent
$stage=Join-Path $root 'artifacts\rename-build'
$setup=Join-Path $root 'dist\PetCalendar-Setup.exe'
$legacyName='WorkCalendar'
$legacyKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\'+$legacyName+'Desktop'
$newKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PetCalendarDesktop'
$legacy=Get-ItemProperty $legacyKey
$oldInstall=$legacy.InstallLocation
$oldExe=Join-Path $oldInstall ($legacyName+'.exe')
$oldUser=Join-Path $env:LOCALAPPDATA $legacyName
$newUser=Join-Path $env:LOCALAPPDATA 'PetCalendar'
$newData=Join-Path $newUser 'data'
$backup=Join-Path $root ('artifacts\rename-rollback-'+(Get-Date -Format 'yyyyMMddHHmmss'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$startupLink=Join-Path ([Environment]::GetFolderPath('Startup')) '업무 캘린더.lnk'
$startupBefore=Test-Path $startupLink
$desktopLink=Join-Path ([Environment]::GetFolderPath('Desktop')) '업무 캘린더.lnk'
foreach($pair in @(@($desktopLink,'desktop.lnk'),@($startupLink,'startup.lnk'))){if(Test-Path $pair[0]){Copy-Item $pair[0] (Join-Path $backup $pair[1])}}
& reg.exe export ('HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\'+$legacyName+'Desktop') (Join-Path $backup 'registration.reg') /y | Out-Null
if($LASTEXITCODE -ne 0){throw 'Could not back up the previous registration'}
if((Get-Item (Join-Path $stage 'PetCalendar.exe')).VersionInfo.FileVersion -ne '2.6.0.0'){throw 'Unexpected staged version'}
$control=Start-Process $oldExe -ArgumentList '--control','exit','--silent','--data-dir',('"'+$legacy.DataDir+'"') -PassThru
if(-not $control.WaitForExit(15000) -or $control.ExitCode -ne 0){throw 'Previous calendar did not acknowledge normal shutdown'}
foreach($owner in @(Get-CimInstance Win32_Process | Where-Object {$_.ExecutablePath -eq $oldExe})){$p=Get-Process -Id $owner.ProcessId -ErrorAction SilentlyContinue;if($p -and -not $p.WaitForExit(15000)){throw 'Previous calendar is still saving'}}
$calendar=Join-Path $legacy.DataDir 'calendar.json'
$hash=(Get-FileHash $calendar).Hash
$pets=Get-Content -Raw -Encoding UTF8 (Join-Path $legacy.DataDir 'pets.json') | ConvertFrom-Json
$widget=if(Test-Path (Join-Path $legacy.DataDir 'widget.json')){Get-Content -Raw -Encoding UTF8 (Join-Path $legacy.DataDir 'widget.json')|ConvertFrom-Json}else{$null}
Copy-Item -LiteralPath $legacy.DataDir -Destination (Join-Path $backup 'data') -Recurse
[IO.Compression.ZipFile]::CreateFromDirectory($oldInstall,(Join-Path $backup 'previous-app.zip'))
try {
 Copy-Item -Path (Join-Path $stage '*') -Destination (Join-Path $root 'dist\app') -Force -Recurse
 Copy-Item $setup (Join-Path (Split-Path $root -Parent) 'PetCalendar-Setup.exe') -Force
 $install=Start-Process $setup -ArgumentList '/S' -PassThru
 if(-not $install.WaitForExit(60000) -or $install.ExitCode -ne 0){throw 'Renamed installer failed'}
 $registered=Get-ItemProperty $newKey
 if($registered.DisplayVersion -ne '2.6.0'){throw 'Registered version mismatch'}
 $newExe=Join-Path $registered.InstallLocation 'PetCalendar.exe'
 if((Get-FileHash $newExe).Hash -ne (Get-FileHash (Join-Path $stage 'PetCalendar.exe')).Hash){throw 'Installed executable mismatch'}
 if((Get-Item $newExe).VersionInfo.ProductName -ne 'PetCalendar'){throw 'Product metadata mismatch'}
 if((Get-FileHash (Join-Path $registered.DataDir 'calendar.json')).Hash -ne $hash){throw 'Calendar bytes changed during rename'}
 if(Test-Path $legacyKey){throw 'Previous product registration was not cleaned'}
 if(Test-Path $oldExe){throw 'Previous installed executable remains'}
 $afterPets=Get-Content -Raw -Encoding UTF8 (Join-Path $registered.DataDir 'pets.json')|ConvertFrom-Json
 foreach($field in @('enabled','summaryEnabled','summaryIntervalMinutes','chatterEnabled','allowEmpty')){if([string]$pets.$field -ne [string]$afterPets.$field){throw ('Pet option changed: '+$field)}}
 if((ConvertTo-Json -Compress -InputObject @($pets.pets)) -ne (ConvertTo-Json -Compress -InputObject @($afterPets.pets))){throw 'Pet names, species or colors changed'}
 if($widget){$afterWidget=Get-Content -Raw -Encoding UTF8 (Join-Path $registered.DataDir 'widget.json')|ConvertFrom-Json;foreach($field in @('enabled','topmost','locked','showDday','x','y','width','height')){if([string]$widget.$field -ne [string]$afterWidget.$field){throw ('Widget option changed: '+$field)}}}
 if((Test-Path $startupLink) -ne $startupBefore){throw 'Startup preference changed'}
 $shell=New-Object -ComObject WScript.Shell
 try{foreach($link in @($desktopLink,$startupLink)){if(Test-Path $link){if($shell.CreateShortcut($link).TargetPath -ne $newExe){throw 'Shortcut still targets the previous executable'}}}}finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)}
 # Retain old uninstaller maintenance artifacts under the new folder label.
 $oldMaintenance=Join-Path (Split-Path $oldInstall -Parent) ('.'+$legacyName+'Maintenance')
 $newMaintenance=Join-Path (Split-Path $registered.InstallLocation -Parent) '.PetCalendarMaintenance'
 if(Test-Path $oldMaintenance){New-Item -ItemType Directory $newMaintenance -Force|Out-Null;foreach($file in Get-ChildItem $oldMaintenance -File){if((Get-Item $file.FullName).VersionInfo.ProductName -eq $legacyName){Move-Item $file.FullName (Join-Path $newMaintenance $file.Name)}};if(@(Get-ChildItem $oldMaintenance -Force).Count -eq 0){Remove-Item $oldMaintenance}}
 $started=Start-Process $newExe -PassThru
 Start-Sleep -Milliseconds 2500
 if($started.HasExited){throw 'Renamed application exited unexpectedly'}
 if((Get-FileHash (Join-Path $registered.DataDir 'calendar.json')).Hash -ne $hash){throw 'Calendar bytes changed after startup'}
 $result=[pscustomobject]@{Version='2.6.0';EnglishName='PetCalendar';DisplayName='업무 캘린더';Project=$root;Executable=$newExe;Data=$registered.DataDir;CalendarPreserved=$true;PetPreferencesPreserved=$true;WidgetPreferencesPreserved=$true;StartupPreserved=$true;PreviousRegistrationRemoved=$true;PID=$started.Id;Backup=$backup;InstallerMiB=[Math]::Round((Get-Item $setup).Length/1MB,3)}
 $result|ConvertTo-Json|Set-Content -Encoding UTF8 (Join-Path $root 'artifacts\rename-deployment.json')
 $result|ConvertTo-Json
}catch {
 $failure=$_
 $stop=Start-Process (Join-Path $stage 'PetCalendar.exe') -ArgumentList '--control','exit','--silent','--data-dir',('"'+$newData+'"') -PassThru
 $null=$stop.WaitForExit(15000)
 if((Test-Path $newUser) -and -not (Test-Path $oldUser)){Move-Item $newUser $oldUser}
 New-Item -ItemType Directory -Path $oldInstall -Force|Out-Null
 $zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $backup 'previous-app.zip'))
 try{foreach($entry in $zip.Entries){$destination=Join-Path $oldInstall $entry.FullName;if($entry.Name -eq ''){New-Item -ItemType Directory -Path $destination -Force|Out-Null;continue};New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force|Out-Null;[IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$destination,$true)}}finally{$zip.Dispose()}
 & reg.exe import (Join-Path $backup 'registration.reg') | Out-Null
 if(Test-Path $newKey){Remove-Item $newKey -Recurse}
 foreach($pair in @(@($desktopLink,'desktop.lnk'),@($startupLink,'startup.lnk'))){if(Test-Path (Join-Path $backup $pair[1])){Copy-Item (Join-Path $backup $pair[1]) $pair[0] -Force}}
 Start-Process $oldExe | Out-Null
 throw $failure
}
