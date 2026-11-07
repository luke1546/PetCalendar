param([int]$Modifiers=2,[int]$Key=9,[long]$WindowHandle=0)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false)
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class CalendarHotkeyCheck {
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
 [StructLayout(LayoutKind.Sequential)] public struct RECT {public int Left;public int Top;public int Right;public int Bottom;}
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string className,string title);
 [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hwnd,int command);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
}
'@
function Send-CalendarKey {
    $calendarModifierKeys=@()
    if($Modifiers -band 2){$calendarModifierKeys+=17}
    if($Modifiers -band 1){$calendarModifierKeys+=18}
    if($Modifiers -band 4){$calendarModifierKeys+=16}
    try {
        foreach($calendarModifier in $calendarModifierKeys){[CalendarHotkeyCheck]::keybd_event([byte]$calendarModifier,0,0,[UIntPtr]::Zero)}
        [CalendarHotkeyCheck]::keybd_event([byte]$Key,0,0,[UIntPtr]::Zero)
        Start-Sleep -Milliseconds 35
    } finally {
        [CalendarHotkeyCheck]::keybd_event([byte]$Key,0,2,[UIntPtr]::Zero)
        [array]::Reverse($calendarModifierKeys)
        foreach($calendarModifier in $calendarModifierKeys){[CalendarHotkeyCheck]::keybd_event([byte]$calendarModifier,0,2,[UIntPtr]::Zero)}
    }
}
$calendarHandle=if($WindowHandle){[IntPtr]$WindowHandle}else{[CalendarHotkeyCheck]::FindWindow('Chrome_WidgetWin_1','업무 캘린더 · 나만의 업무 공간')}
if($calendarHandle -eq [IntPtr]::Zero){throw '테스트할 캘린더 창이 없습니다.'}
$calendarWasMaximized=[CalendarHotkeyCheck]::IsZoomed($calendarHandle)
try {
    if([CalendarHotkeyCheck]::GetForegroundWindow() -ne $calendarHandle){
        Send-CalendarKey
        for($calendarTry=0;$calendarTry -lt 20;$calendarTry++){if([CalendarHotkeyCheck]::GetForegroundWindow() -eq $calendarHandle){break};Start-Sleep -Milliseconds 100}
        if([CalendarHotkeyCheck]::GetForegroundWindow() -ne $calendarHandle){throw ('단축키로 캘린더를 활성화하지 못했습니다. Target='+$calendarHandle.ToInt64()+' Foreground='+[CalendarHotkeyCheck]::GetForegroundWindow().ToInt64())}
    }
    $calendarBefore=New-Object CalendarHotkeyCheck+RECT
    [CalendarHotkeyCheck]::GetWindowRect($calendarHandle,[ref]$calendarBefore) | Out-Null
    $calendarBeforeZoomed=[CalendarHotkeyCheck]::IsZoomed($calendarHandle)
    Send-CalendarKey
    for($calendarTry=0;$calendarTry -lt 20;$calendarTry++){if(-not [CalendarHotkeyCheck]::IsWindowVisible($calendarHandle)){break};Start-Sleep -Milliseconds 100}
    if([CalendarHotkeyCheck]::IsWindowVisible($calendarHandle)){throw '단축키로 창을 숨기지 못했습니다.'}
    if([CalendarHotkeyCheck]::IsIconic($calendarHandle)){throw '숨기기 대신 최소화되었습니다.'}
    if(-not [CalendarHotkeyCheck]::IsWindow($calendarHandle)){throw '원래 창이 사라졌습니다.'}
    Send-CalendarKey
    for($calendarTry=0;$calendarTry -lt 20;$calendarTry++){if([CalendarHotkeyCheck]::IsWindowVisible($calendarHandle) -and [CalendarHotkeyCheck]::GetForegroundWindow() -eq $calendarHandle){break};Start-Sleep -Milliseconds 100}
    if(-not [CalendarHotkeyCheck]::IsWindowVisible($calendarHandle)){throw '다시 누른 단축키로 복원하지 못했습니다.'}
    if([CalendarHotkeyCheck]::GetForegroundWindow() -ne $calendarHandle){throw '복원 후 캘린더를 활성화하지 못했습니다.'}
    $calendarAfter=New-Object CalendarHotkeyCheck+RECT
    [CalendarHotkeyCheck]::GetWindowRect($calendarHandle,[ref]$calendarAfter) | Out-Null
    if($calendarBefore.Left -ne $calendarAfter.Left -or $calendarBefore.Top -ne $calendarAfter.Top -or $calendarBefore.Right -ne $calendarAfter.Right -or $calendarBefore.Bottom -ne $calendarAfter.Bottom -or $calendarBeforeZoomed -ne [CalendarHotkeyCheck]::IsZoomed($calendarHandle)){throw '숨기기 전 창 위치 또는 크기가 바뀌었습니다.'}
    [pscustomobject]@{Hidden=$true;NotMinimized=$true;SameWindowRestored=$true;PlacementPreserved=$true;Foreground=$true;Modifiers=$Modifiers;Key=$Key} | ConvertTo-Json -Compress
}finally{if(-not [CalendarHotkeyCheck]::IsWindowVisible($calendarHandle)){[CalendarHotkeyCheck]::ShowWindowAsync($calendarHandle,5) | Out-Null};if($calendarWasMaximized){[CalendarHotkeyCheck]::ShowWindowAsync($calendarHandle,3) | Out-Null}elseif([CalendarHotkeyCheck]::IsIconic($calendarHandle)){[CalendarHotkeyCheck]::ShowWindowAsync($calendarHandle,9) | Out-Null}}
