param([Parameter(Mandatory=$true)][long]$WindowHandle)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.Encoding]::UTF8
Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class WidgetRegionCheck {
 [StructLayout(LayoutKind.Sequential)]public struct RECT {public int left,top,right,bottom;}
 [DllImport("gdi32.dll")]public static extern IntPtr CreateRectRgn(int l,int t,int r,int b);
 [DllImport("gdi32.dll")]public static extern bool DeleteObject(IntPtr region);
 [DllImport("gdi32.dll")]public static extern bool PtInRegion(IntPtr region,int x,int y);
 [DllImport("user32.dll")]public static extern int GetWindowRgn(IntPtr h,IntPtr region);
 [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out RECT rect);
 [DllImport("user32.dll")]public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
'@
[WidgetRegionCheck]::SetThreadDpiAwarenessContext([IntPtr](-4))|Out-Null
$calendarHandle=[IntPtr]$WindowHandle
$calendarRect=New-Object WidgetRegionCheck+RECT
[WidgetRegionCheck]::GetWindowRect($calendarHandle,[ref]$calendarRect)|Out-Null
$calendarWidth=$calendarRect.right-$calendarRect.left;$calendarHeight=$calendarRect.bottom-$calendarRect.top
$calendarRegion=[WidgetRegionCheck]::CreateRectRgn(0,0,0,0)
try{
 $calendarKind=[WidgetRegionCheck]::GetWindowRgn($calendarHandle,$calendarRegion)
 $calendarCorners=@(@(0,0),@(($calendarWidth-1),0),@(0,($calendarHeight-1)),@(($calendarWidth-1),($calendarHeight-1)))
 $calendarSides=@(@([int]($calendarWidth/2),2),@([int]($calendarWidth/2),($calendarHeight-3)),@(2,[int]($calendarHeight/2)),@(($calendarWidth-3),[int]($calendarHeight/2)),@([int]($calendarWidth/2),[int]($calendarHeight/2)))
 $calendarCornersExcluded=$true;$calendarSidesIncluded=$true
 foreach($calendarPoint in $calendarCorners){if([WidgetRegionCheck]::PtInRegion($calendarRegion,$calendarPoint[0],$calendarPoint[1])){$calendarCornersExcluded=$false}}
 foreach($calendarPoint in $calendarSides){if(-not [WidgetRegionCheck]::PtInRegion($calendarRegion,$calendarPoint[0],$calendarPoint[1])){$calendarSidesIncluded=$false}}
 [pscustomobject]@{kind=$calendarKind;width=$calendarWidth;height=$calendarHeight;cornersExcluded=$calendarCornersExcluded;sidesIncluded=$calendarSidesIncluded}|ConvertTo-Json -Compress
}finally{[WidgetRegionCheck]::DeleteObject($calendarRegion)|Out-Null}
