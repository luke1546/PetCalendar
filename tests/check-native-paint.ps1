param([Parameter(Mandatory=$true)][string]$ImagePath,[Parameter(Mandatory=$true)][string]$LayoutPath,[long]$WindowHandle=0)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.Encoding]::UTF8
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;using System.Drawing;using System.Runtime.InteropServices;
public static class WidgetFramePixels {
 [DllImport("gdi32.dll")]static extern IntPtr CreateRectRgn(int l,int t,int r,int b);
 [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr r);
 [DllImport("gdi32.dll")]static extern bool PtInRegion(IntPtr r,int x,int y);
 [DllImport("user32.dll")]static extern int GetWindowRgn(IntPtr h,IntPtr r);
 [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr h);
 static void Scan(Bitmap image,IntPtr region,int x,int y,int dx,int dy,int depth,ref int samples,ref int dark,ref int bands){
  while(x>=0&&y>=0&&x<image.Width&&y<image.Height&&!PtInRegion(region,x,y)){x+=dx;y+=dy;}
  int previous=-1;
  for(int i=0;i<depth&&x>=0&&y>=0&&x<image.Width&&y<image.Height;i++,x+=dx,y+=dy){
   if(!PtInRegion(region,x,y))break;var color=image.GetPixel(x,y);samples++;
   if(color.R<196||color.G<181||color.B<154)dark++;
   int brightness=color.R+color.G+color.B;
   if(previous>=0&&brightness<previous-8)bands++;
   previous=brightness;
  }
 }
 public static int[] Check(Bitmap image,IntPtr window){
  int samples=0,dark=0,bands=0,depth=(int)Math.Ceiling(4*GetDpiForWindow(window)/96.0);var region=CreateRectRgn(0,0,0,0);
  try{if(GetWindowRgn(window,region)!=3)throw new Exception("Rounded native region is missing");
   for(int y=0;y<image.Height;y++){Scan(image,region,0,y,1,0,depth,ref samples,ref dark,ref bands);Scan(image,region,image.Width-1,y,-1,0,depth,ref samples,ref dark,ref bands);}
   for(int x=0;x<image.Width;x++){Scan(image,region,x,0,0,1,depth,ref samples,ref dark,ref bands);Scan(image,region,x,image.Height-1,0,-1,depth,ref samples,ref dark,ref bands);}
   return new[]{samples,dark,bands};
  }finally{DeleteObject(region);}
 }
}
'@
$calendarInfo=Get-Content -LiteralPath $LayoutPath -Raw -Encoding UTF8|ConvertFrom-Json
$calendarImage=New-Object Drawing.Bitmap($ImagePath)
$calendarFailures=New-Object 'System.Collections.Generic.List[object]'
$calendarSamples=0
try{
 foreach($calendarButton in $calendarInfo.buttons){
  foreach($calendarCorner in @(@(0,0),@(($calendarButton.width-1),0),@(0,($calendarButton.height-1)),@(($calendarButton.width-1),($calendarButton.height-1)))){
   $calendarX=[int]($calendarButton.x+$calendarCorner[0]);$calendarY=[int]($calendarButton.y+$calendarCorner[1])
   if($calendarX -lt 0 -or $calendarY -lt 0 -or $calendarX -ge $calendarImage.Width -or $calendarY -ge $calendarImage.Height){continue}
   $calendarPixel=$calendarImage.GetPixel($calendarX,$calendarY);$calendarSamples++
   if([Math]::Abs($calendarPixel.R-255) -gt 2 -or [Math]::Abs($calendarPixel.G-244) -gt 2 -or [Math]::Abs($calendarPixel.B-221) -gt 2){
    $calendarFailures.Add([pscustomobject]@{button=$calendarButton.name;x=$calendarX;y=$calendarY;rgb=@($calendarPixel.R,$calendarPixel.G,$calendarPixel.B)})
   }
  }
 }
 # The narrow paper margin inside the window border must also be clean after
 # resize; otherwise the old rounded frame can remain at its previous size.
 $calendarFramePoints=New-Object 'System.Collections.Generic.List[object]'
 for($calendarY=24;$calendarY -lt $calendarImage.Height-24;$calendarY++){$calendarFramePoints.Add(@(3,$calendarY));$calendarFramePoints.Add(@(($calendarImage.Width-4),$calendarY))}
 for($calendarX=24;$calendarX -lt $calendarImage.Width-24;$calendarX++){$calendarFramePoints.Add(@($calendarX,4));$calendarFramePoints.Add(@($calendarX,($calendarImage.Height-4)))}
 foreach($calendarPoint in $calendarFramePoints){$calendarPixel=$calendarImage.GetPixel($calendarPoint[0],$calendarPoint[1]);$calendarSamples++;if([Math]::Abs($calendarPixel.R-255) -gt 2 -or [Math]::Abs($calendarPixel.G-244) -gt 2 -or [Math]::Abs($calendarPixel.B-221) -gt 2){$calendarFailures.Add([pscustomobject]@{button='window-frame';x=$calendarPoint[0];y=$calendarPoint[1];rgb=@($calendarPixel.R,$calendarPixel.G,$calendarPixel.B)})}}
 if($WindowHandle){$calendarFrame=[WidgetFramePixels]::Check($calendarImage,[IntPtr]$WindowHandle);$calendarSamples+=$calendarFrame[0];if($calendarFrame[1] -gt 0 -or $calendarFrame[2] -gt 0){$calendarFailures.Add([pscustomobject]@{button='outer-frame';darkPixels=$calendarFrame[1];extraBands=$calendarFrame[2]})}}
}finally{$calendarImage.Dispose()}
[pscustomobject]@{samples=$calendarSamples;failures=$calendarFailures.ToArray()}|ConvertTo-Json -Depth 5 -Compress
