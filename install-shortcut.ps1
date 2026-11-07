$ErrorActionPreference = 'Stop'
$calendarInstalled = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PetCalendarDesktop' -ErrorAction SilentlyContinue
if (-not $calendarInstalled) { throw 'Install the PetCalendar desktop app first.' }
$calendarExecutable = Join-Path $calendarInstalled.InstallLocation 'PetCalendar.exe'
if (-not (Test-Path -LiteralPath $calendarExecutable -PathType Leaf)) { throw 'Installed desktop executable was not found.' }
$calendarShortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) '펫 캘린더.lnk'
$calendarShell = New-Object -ComObject WScript.Shell
try {
    $calendarShortcut = $calendarShell.CreateShortcut($calendarShortcutPath)
    $calendarShortcut.TargetPath = $calendarExecutable
    $calendarShortcut.Arguments = '--data-dir "' + $calendarInstalled.DataDir + '"'
    $calendarShortcut.WorkingDirectory = $calendarInstalled.InstallLocation
    $calendarShortcut.IconLocation = $calendarExecutable + ',0'
    $calendarShortcut.Save()
    Write-Output $calendarShortcutPath
} finally {
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($calendarShell)
}
