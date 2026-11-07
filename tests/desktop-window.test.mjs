import test from 'node:test';
import assert from 'node:assert/strict';
import {promises as fs} from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {defaultState} from '../desktop/ui/model.mjs';
import {DesktopDriver,delay,waitFor,root} from './desktop-driver.mjs';
const run=promisify(execFile);
async function frame(window){
 const script=`Add-Type -AssemblyName System.Windows.Forms;Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class MainFrame {
 [StructLayout(LayoutKind.Sequential)]public struct RECT{public int left,top,right,bottom;}
 [StructLayout(LayoutKind.Sequential)]public struct POINT{public int x,y;}
 [DllImport("user32.dll")]public static extern int GetWindowLong(IntPtr h,int n);
 [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")]public static extern bool ClientToScreen(IntPtr h,ref POINT p);
 [DllImport("user32.dll")]public static extern bool IsZoomed(IntPtr h);
 [DllImport("user32.dll")]public static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
 [DllImport("user32.dll")]public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")]public static extern uint GetDpiForWindow(IntPtr h);
}
'@
$h=[IntPtr]${window};$r=New-Object MainFrame+RECT;$p=New-Object MainFrame+POINT;[MainFrame]::GetWindowRect($h,[ref]$r)|Out-Null;[MainFrame]::ClientToScreen($h,[ref]$p)|Out-Null;$a=[Windows.Forms.Screen]::FromHandle($h).WorkingArea;$s=[MainFrame]::GetWindowLong($h,-16);
[pscustomobject]@{caption=($s -band 0xc00000);sizable=(($s -band 0x40000) -ne 0);maximized=[MainFrame]::IsZoomed($h);minimized=[MainFrame]::IsIconic($h);topInset=($p.y-$r.top);topLeft=[MainFrame]::SendMessage($h,132,[IntPtr]::Zero,[IntPtr]((($r.top+1) -shl 16) -bor (($r.left+1) -band 65535))).ToInt64();dpi=[MainFrame]::GetDpiForWindow($h);x=$r.left;y=$r.top;width=($r.right-$r.left);height=($r.bottom-$r.top);area=$a}|ConvertTo-Json -Compress`;
 return JSON.parse((await run('powershell.exe',['-NoProfile','-NonInteractive','-Command',script],{windowsHide:true})).stdout);
}
async function topDoubleClick(window){
 const script=`Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;public static class TopClick {
 [StructLayout(LayoutKind.Sequential)]public struct RECT{public int left,top,right,bottom;}
 [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")]public static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
}
'@
$h=[IntPtr]${window};$r=New-Object TopClick+RECT;[TopClick]::GetWindowRect($h,[ref]$r)|Out-Null;$x=[int](($r.left+$r.right)/2);$p=[IntPtr](($r.top -shl 16) -bor ($x -band 65535));$hit=[TopClick]::SendMessage($h,132,[IntPtr]::Zero,$p);[TopClick]::SendMessage($h,0xa3,$hit,$p)|Out-Null;$hit.ToInt64()`;
 assert.equal(Number((await run('powershell.exe',['-NoProfile','-NonInteractive','-Command',script],{windowsHide:true})).stdout),2,'very top edge is the window caption');
}
test('메인 창: 중복 제목 표시줄 제거·이동 영역·최소화·최대화·닫기 입력 보존',{timeout:45000},async()=>{
 const tmp=await fs.mkdtemp(path.join(os.tmpdir(),'calendar-window-')),data=path.join(tmp,'data');await fs.mkdir(data);const state=defaultState();state.settings.hotkey.enabled=false;await fs.writeFile(path.join(data,'calendar.json'),JSON.stringify(state));const app=new DesktopDriver(data,path.join(tmp,'profile'));
 const click=id=>app.js(`document.getElementById(${JSON.stringify(id)}).click();true`);
 try{
  await app.ready;const info=await app.command('inspect'),before=await frame(info.window);assert.equal(before.caption,0);assert.equal(before.sizable,true);assert.equal(before.topLeft,13,'top left remains resizable');assert.ok(before.topInset<=12*before.dpi/96,'no empty caption above the content');
  assert.equal(await app.js('document.getElementById("windowControls").hidden'),false);assert.equal(await app.js('getComputedStyle(document.querySelector(".masthead")).getPropertyValue("-webkit-app-region")'),'drag');assert.equal(await app.js('getComputedStyle(document.getElementById("windowClose")).getPropertyValue("-webkit-app-region")'),'no-drag');
  await run('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-File',path.join(root,'tests/capture-native-window.ps1'),'-WindowHandle',String(info.window),'-OutputPath',path.join(root,'artifacts/calendar-no-titlebar.png')],{windowsHide:true});
  await click('windowMaximize');await waitFor(()=>app.js('document.getElementById("windowMaximize").title==="이전 크기로"'),'maximized label');const maximized=await frame(info.window);assert.equal(maximized.maximized,true);assert.ok(maximized.y+maximized.height<=maximized.area.Y+maximized.area.Height+10,'keep taskbar available');
  await click('windowMaximize');await waitFor(()=>app.js('document.getElementById("windowMaximize").title==="최대화"'),'restore label');const restored=await frame(info.window);assert.equal(restored.maximized,false);assert.equal(restored.width,before.width);assert.equal(restored.height,before.height);
  const rounded=JSON.parse((await run('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-File',path.join(root,'tests/check-widget-region.ps1'),'-WindowHandle',String(info.window)],{windowsHide:true})).stdout);assert.equal(rounded.kind,3);assert.equal(rounded.cornersExcluded,true);
  assert.equal(await app.js('getComputedStyle(document.querySelector(".app-shell")).getPropertyValue("-webkit-app-region")'),'drag','top padding also drags');
  await topDoubleClick(info.window);await waitFor(()=>app.js('document.getElementById("windowMaximize").title==="이전 크기로"'),'top-edge maximize');
  const square=JSON.parse((await run('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-File',path.join(root,'tests/check-widget-region.ps1'),'-WindowHandle',String(info.window)],{windowsHide:true})).stdout);assert.equal(square.kind,0,'maximized window fills the screen without rounded gaps');
  await topDoubleClick(info.window);await waitFor(()=>app.js('document.getElementById("windowMaximize").title==="최대화"'),'top-edge restore');assert.equal((await frame(info.window)).width,before.width);
  await click('windowMinimize');await delay(100);assert.equal((await frame(info.window)).minimized,true);await app.command('show');
  await app.js('document.getElementById("taskTitle").value="닫아도 보존하는 입력";true');await click('windowClose');await waitFor(async()=>!(await app.command('inspect')).visible,'close hides to tray');await app.command('show');assert.equal(await app.js('document.getElementById("taskTitle").value'),'닫아도 보존하는 입력');
  assert.deepEqual(await app.js('window.qaErrors'),[]);await fs.writeFile(path.join(root,'artifacts/calendar-window-results.json'),JSON.stringify({before,maximized,restored,controls:'passed',draftPreserved:true,topEdgeDoubleClick:true,roundedNormal:true,squareMaximized:true},null,2));
 }finally{await app.close();await delay(700);await fs.rm(tmp,{recursive:true,force:true,maxRetries:5,retryDelay:300});}
});
