param(
    [Parameter(Mandatory=$true)][string]$AppDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.Encoding]::UTF8
$AppDirectory=[IO.Path]::GetFullPath($AppDirectory)
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a new output directory to preserve existing evidence.'}
$stage=Join-Path $OutputDirectory 'app'
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item -Path (Join-Path $AppDirectory '*') -Destination $stage -Recurse
$compiler=Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$test=Join-Path $stage 'ReviewUiChecks.exe'
& $compiler /nologo /target:exe /platform:x64 /codepage:65001 ('/out:'+$test) ('/reference:'+(Join-Path $stage 'PetCalendar.exe')) /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'ReviewUiChecks.cs')
if($LASTEXITCODE -ne 0){throw 'Review UI fixture compilation failed.'}
& $test (Join-Path $OutputDirectory 'evidence')
exit $LASTEXITCODE
