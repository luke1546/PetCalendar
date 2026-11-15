<#
One-shot release: version bump, tests (in a temp copy, in parallel with the build),
release build, installer, portable exe, checksums, commit, tag, push, GitHub release.

  .\tools\release.ps1 -Version 3.3.5 -Title "quieter toy dialogue"           # full release
  .\tools\release.ps1 -Version 3.3.5 -Title "..." -DryRun                    # everything up to commit, nothing pushed
  .\tools\release.ps1 -Version 3.3.5 -Title "..." -Add desktop\NewFile.cs    # also commit new (untracked) files

Release notes must already exist at docs\releases\v<Version>.md (Korean, user-facing).
Tracked files that changed are committed automatically; new files need -Add.
#>
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory=$true)][string]$Title,
    [string[]]$Add=@(),
    [switch]$DryRun,
    [switch]$SkipTests
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location $root
$started=Get-Date
function Step($text){Write-Host ("[{0,5:N0}s] {1}" -f ((Get-Date)-$started).TotalSeconds,$text) -ForegroundColor Cyan}
$gh=(Get-Command gh -ErrorAction SilentlyContinue).Source
if(-not $gh){$gh=Join-Path $env:ProgramFiles 'GitHub CLI\gh.exe'}
if(-not (Test-Path $gh)){throw 'GitHub CLI (gh) was not found.'}
$tag="v$Version"
$notes=Join-Path $root "docs\releases\$tag.md"
if(-not (Test-Path $notes)){throw "Write the release notes first: docs\releases\$tag.md"}
if(git tag --list $tag){throw "Tag $tag already exists."}
$branch=(git rev-parse --abbrev-ref HEAD).Trim()

Step "Bumping version to $Version"
$utf8=New-Object System.Text.UTF8Encoding($false)
function Bump($path,$pattern,$replacement){
    $full=Join-Path $root $path;$text=[IO.File]::ReadAllText($full)
    if(-not [regex]::IsMatch($text,$pattern)){throw "Version pattern not found in $path"}
    $next=[regex]::Replace($text,$pattern,$replacement)
    $bom=[IO.File]::ReadAllBytes($full);$keepBom=$bom.Length -ge 3 -and $bom[0] -eq 0xEF -and $bom[1] -eq 0xBB -and $bom[2] -eq 0xBF
    [IO.File]::WriteAllText($full,$next,(New-Object System.Text.UTF8Encoding($keepBom)))
}
$v4="$Version.0"
Bump 'desktop\app.manifest' '(<assemblyIdentity version=")[\d.]+(" name="PetCalendar\.Desktop")' "`${1}$v4`${2}"
Bump 'desktop\Entry.cs' '(AssemblyVersion\(")[\d.]+("\))' "`${1}$v4`${2}"
Bump 'desktop\PortableLauncher.cs' '(AssemblyVersion\(")[\d.]+("\))' "`${1}$v4`${2}"
Bump 'desktop\installer.nsi' '(!define VERSION ")[\d.]+(")' "`${1}$Version`${2}"
Bump 'desktop\installer.nsi' '(VIProductVersion ")[\d.]+(")' "`${1}$v4`${2}"
Bump 'desktop\installer.nsi' '("FileVersion" ")[\d.]+(")' "`${1}$v4`${2}"
Bump 'README.md' '(\*\*v)\d+\.\d+\.\d+(\*\*)' "`${1}$Version`${2}"

$testJob=$null
if(-not $SkipTests){
    Step 'Starting tests in a temp copy (runs alongside the build)'
    $copy=Join-Path $env:TEMP 'petcalendar-release-test'
    if(Test-Path $copy){Remove-Item -Recurse -Force $copy}
    New-Item -ItemType Directory $copy | Out-Null
    robocopy $root $copy /E /NFL /NDL /NJH /NJS /NP /XD (Join-Path $root 'dist') (Join-Path $root '.git') (Join-Path $root 'artifacts') (Join-Path $root 'node_modules') | Out-Null
    $testJob=Start-Job -ArgumentList $copy -ScriptBlock {
        param($dir)Set-Location $dir;$log=New-Object System.Collections.Generic.List[string];$failed=$false
        try{& .\desktop\build.ps1 *> $null}catch{$log.Add("BUILD FAILED: $_");return @{failed=$true;log=$log}}
        # The whole suite lives in tools/test.ps1 so a release runs exactly what a developer runs.
        $o=powershell -NoProfile -ExecutionPolicy Bypass -File tools\test.ps1 -AppDirectory dist\app 2>&1|Out-String
        if($LASTEXITCODE -ne 0){$failed=$true}
        foreach($line in $o -split "`r?`n"|?{$_.Trim().Length -gt 0}){$log.Add($line)}
        @{failed=$failed;log=$log}
    }
}

Step 'Building release app'
$app=Join-Path $root 'dist\release-app'
if(Test-Path $app){Remove-Item -Recurse -Force $app}
& .\desktop\build.ps1 -OutputDirectory $app *> (Join-Path $root 'dist\build-log.txt')
Step 'Building installer'
& .\desktop\package.ps1 -AppDirectory $app -OutputFile (Join-Path $root 'dist\PetCalendar-Setup.exe') | Out-Null
Step 'Building single-file portable exe'
& .\desktop\build-portable.ps1 -OutputFile (Join-Path $root 'dist\PetCalendar-Portable.exe') | Out-Null
# Release assets carry the version in their names (PetCalendar-Setup-3.4.6.exe), so downloads
# stay distinguishable; the build outputs above keep their plain names for local scripts.
Get-ChildItem dist -Filter 'PetCalendar-*-*.*.*.exe' -ErrorAction SilentlyContinue|Remove-Item -Force
$assets=@("dist\PetCalendar-Setup-$Version.exe","dist\PetCalendar-Portable-$Version.exe")
Copy-Item 'dist\PetCalendar-Setup.exe' $assets[0] -Force;Copy-Item 'dist\PetCalendar-Portable.exe' $assets[1] -Force
foreach($a in @('dist\release-app\PetCalendar.exe')+$assets){$fv=(Get-Item $a).VersionInfo.FileVersion;if($fv -ne $v4){throw "$a has version $fv, expected $v4"}}
($assets|%{"{0}  {1}" -f (Get-FileHash $_ -Algorithm SHA256).Hash.ToLower(),(Split-Path $_ -Leaf)})|Out-File 'dist\SHA256SUMS.txt' -Encoding ascii

if($testJob){
    Step 'Waiting for tests'
    $result=Receive-Job $testJob -Wait -AutoRemoveJob
    $result.log|%{Write-Host "  $_"}
    if($result.failed){throw 'Tests failed. Nothing was committed or published.'}
}

$sizes=($assets|%{"{0} {1:N1}MB" -f (Split-Path $_ -Leaf),((Get-Item $_).Length/1MB)}) -join ', '
if($DryRun){Step "Dry run done ($sizes). Version files are bumped; nothing committed or pushed.";return}

Step "Committing and tagging $tag"
# Windows PowerShell turns git's stderr warnings (e.g. CRLF notices) into errors; judge by exit code only.
function RunGit{$saved=$ErrorActionPreference;$ErrorActionPreference='Continue';try{& git.exe @args 2>&1|Out-Null;if($LASTEXITCODE -ne 0){throw "git $($args -join ' ') failed."}}finally{$ErrorActionPreference=$saved}}
RunGit add -u
RunGit add -- $notes
foreach($f in $Add){RunGit add -- $f}
$message="Release ${tag}: $Title`n`nCo-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
RunGit commit -q -m $message
RunGit tag $tag
Step "Pushing $branch, main and $tag"
RunGit push -q origin $branch
if($branch -ne 'main'){RunGit push -q origin "${branch}:main"}
RunGit push -q origin $tag

Step 'Publishing GitHub release'
$ErrorActionPreference='Continue'
$url=& $gh release create $tag @assets 'dist\SHA256SUMS.txt' --title $tag --notes-file $notes --latest 2>&1|Select-Object -Last 1
if($LASTEXITCODE -ne 0){throw "gh release create failed (commit and tag are already pushed): $url"}
Step "Released $tag ($sizes)"
Write-Output $url
