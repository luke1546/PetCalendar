param([string]$OutputFile)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(-not $OutputFile){$OutputFile=Join-Path $root 'dist\PetCalendar-Portable.exe'}
$stage=Join-Path $root 'artifacts\portable-build'
New-Item -ItemType Directory -Force -Path $stage,(Split-Path $OutputFile -Parent) | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory (Join-Path $stage 'app')
$zip=Join-Path $stage 'payload.zip'
if(Test-Path -LiteralPath $zip){Remove-Item -LiteralPath $zip -Force}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $stage 'app'),$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
$compiler=Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 ('/out:'+$OutputFile) ('/win32icon:'+(Join-Path $root 'calendar.ico')) ('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest')) /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll ('/resource:'+$zip+',petcalendar.portable.zip') (Join-Path $PSScriptRoot 'PortableLauncher.cs')
if($LASTEXITCODE -ne 0){throw 'Portable build failed.'}
Write-Output $OutputFile
