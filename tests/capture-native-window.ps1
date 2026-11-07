param([long]$WindowHandle=0,[int]$CalendarProcessId=0,[string]$Title='업무 캘린더 · 바탕화면 위젯',[Parameter(Mandatory=$true)][string]$OutputPath,[switch]$NoActivate,[int]$CaptureHeight=0)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.Encoding]::UTF8
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class NativeWindowCapture {
 public delegate bool WindowCallback(IntPtr h,IntPtr p);
 [StructLayout(LayoutKind.Sequential)] public struct RECT {public int left,top,right,bottom;}
 [DllImport("user32.dll")] public static extern bool EnumWindows(WindowCallback callback,IntPtr p);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h,int index);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int hgt,int flags);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("dwmapi.dll")] public static extern int DwmFlush();
 public static IntPtr Find(string title,int pid){IntPtr found=IntPtr.Zero;EnumWindows(delegate(IntPtr h,IntPtr p){uint owner;GetWindowThreadProcessId(h,out owner);var s=new StringBuilder(512);GetWindowText(h,s,s.Capacity);if(IsWindowVisible(h)&&(pid==0||owner==pid)&&s.ToString()==title){found=h;return false;}return true;},IntPtr.Zero);return found;}
}
'@
[NativeWindowCapture]::SetThreadDpiAwarenessContext([IntPtr](-4))|Out-Null
$calendarHandle=if($WindowHandle){[IntPtr]$WindowHandle}else{[NativeWindowCapture]::Find($Title,$CalendarProcessId)}
if($calendarHandle -eq [IntPtr]::Zero -or -not [NativeWindowCapture]::IsWindowVisible($calendarHandle)){throw 'Visible target window not found'}
if(-not $NoActivate){[NativeWindowCapture]::SetWindowPos($calendarHandle,[IntPtr]::Zero,0,0,0,0,3)|Out-Null;[NativeWindowCapture]::SetForegroundWindow($calendarHandle)|Out-Null}
Start-Sleep -Milliseconds 250
[NativeWindowCapture]::DwmFlush()|Out-Null
$calendarRect=New-Object NativeWindowCapture+RECT
if(-not [NativeWindowCapture]::GetWindowRect($calendarHandle,[ref]$calendarRect)){throw 'Cannot locate target window'}
$calendarWidth=$calendarRect.right-$calendarRect.left;$calendarHeight=$calendarRect.bottom-$calendarRect.top
if($CaptureHeight -gt 0){$calendarHeight=[Math]::Min($calendarHeight,$CaptureHeight)}
$calendarWasTopmost=([NativeWindowCapture]::GetWindowLong($calendarHandle,-20) -band 8) -ne 0
if(-not $NoActivate){[NativeWindowCapture]::SetWindowPos($calendarHandle,[IntPtr](-1),0,0,0,0,19)|Out-Null;Start-Sleep -Milliseconds 150;[NativeWindowCapture]::DwmFlush()|Out-Null}
$calendarBitmap=New-Object Drawing.Bitmap($calendarWidth,$calendarHeight)
$calendarGraphics=[Drawing.Graphics]::FromImage($calendarBitmap)
try{$calendarGraphics.CopyFromScreen($calendarRect.left,$calendarRect.top,0,0,$calendarBitmap.Size);$calendarBitmap.Save($OutputPath,[Drawing.Imaging.ImageFormat]::Png)}finally{$calendarGraphics.Dispose();$calendarBitmap.Dispose();if(-not $NoActivate -and -not $calendarWasTopmost){[NativeWindowCapture]::SetWindowPos($calendarHandle,[IntPtr](-2),0,0,0,0,19)|Out-Null}}
[pscustomobject]@{window=$calendarHandle.ToInt64();width=$calendarWidth;height=$calendarHeight;method='CopyFromScreen';caption=([NativeWindowCapture]::GetWindowLong($calendarHandle,-16) -band 0xc00000);file=$OutputPath}|ConvertTo-Json -Compress
