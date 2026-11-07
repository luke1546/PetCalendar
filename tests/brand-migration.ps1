$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$compiler=Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe=Join-Path $env:TEMP 'PetCalendar.BrandMigrationTests.exe'
& $compiler /nologo /target:exe /codepage:65001 /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll ('/out:'+$exe) (Join-Path $root 'desktop\Core.cs') (Join-Path $root 'desktop\BrandMigration.cs') (Join-Path $PSScriptRoot 'BrandMigrationTests.cs')
if($LASTEXITCODE -ne 0){throw 'Migration test compilation failed'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Migration tests failed'}
