using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DesktopPet {
    internal static class Native {
        internal static long ScanCount;
        internal sealed class WindowInfo {public IntPtr Handle;public Rectangle Bounds;}
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X,Y; public POINT(int x,int y){X=x;Y=y;} }
        [StructLayout(LayoutKind.Sequential)] internal struct SIZE { public int X,Y; public SIZE(int x,int y){X=x;Y=y;} }
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left,Top,Right,Bottom; public Rectangle Box { get { return Rectangle.FromLTRB(Left,Top,Right,Bottom); } } }
        [StructLayout(LayoutKind.Sequential,Pack=1)] internal struct BLEND { public byte Op,Flags,Alpha,Format; }
        internal delegate bool EnumProc(IntPtr hwnd,IntPtr param);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback,IntPtr param);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder text,int max);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
        [DllImport("user32.dll",SetLastError=true)] internal static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr hwnd,IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll",SetLastError=true)] internal static extern bool UpdateLayeredWindow(IntPtr hwnd,IntPtr dest,ref POINT pos,ref SIZE size,IntPtr source,ref POINT origin,int color,ref BLEND blend,int flags);
        [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")] static extern int DwmRect(IntPtr hwnd,int attr,out RECT value,int size);
        [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")] static extern int DwmInt(IntPtr hwnd,int attr,out int value,int size);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor,int type,out uint x,out uint y);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT point,uint flags);
        internal static double Dpi(Point point) { try { uint x,y;if(GetDpiForMonitor(MonitorFromPoint(new POINT(point.X,point.Y),2),0,out x,out y)==0)return x/96.0; }catch(EntryPointNotFoundException){} return 1; }
        internal static Rectangle Bounds(IntPtr hwnd) { RECT r; if(DwmRect(hwnd,9,out r,16)!=0)GetWindowRect(hwnd,out r); return r.Box; }
        internal static string Class(IntPtr hwnd) { var s=new StringBuilder(256);GetClassName(hwnd,s,s.Capacity);return s.ToString(); }
        internal static bool Cloaked(IntPtr hwnd) { int v;return DwmInt(hwnd,14,out v,4)==0 && v!=0; }
        internal static bool Shell(IntPtr hwnd) { string c=Class(hwnd);return c=="Progman" || c=="WorkerW" || c=="Shell_TrayWnd" || c=="Shell_SecondaryTrayWnd"; }
        internal static bool Fullscreen(IntPtr own,Rectangle monitor) {
            var h=GetForegroundWindow();
            if(h==IntPtr.Zero || h==own || !IsWindowVisible(h) || IsIconic(h) || Shell(h))return false;
            Rectangle r=Bounds(h);
            return r.Left<=monitor.Left && r.Top<=monitor.Top && r.Right>=monitor.Right && r.Bottom>=monitor.Bottom;
        }
        internal static List<WindowInfo> CaptureWindows() {
            ScanCount++;
            var windows=new List<WindowInfo>();
            EnumWindows(delegate(IntPtr h,IntPtr unused) {
                if(!IsWindowVisible(h) || IsIconic(h))return true;
                // Reject our own tool windows before any DWM calls.
                if((GetWindowLongPtr(h,-20).ToInt64()&0x80)!=0 || Cloaked(h) || Shell(h))return true;
                Rectangle r=Bounds(h);
                if(r.Width>=2 && r.Height>=2)windows.Add(new WindowInfo {Handle=h,Bounds=r});
                return true;
            },IntPtr.Zero);
            return windows;
        }
        internal static List<Surface> Surfaces(IntPtr own,Screen[] screens,double catSize) {
            return BuildSurfaces(CaptureWindows(),screens,catSize);
        }
        internal static List<Surface> BuildSurfaces(List<WindowInfo> windows,Screen[] screens,double catSize) {
            var result=new List<Surface>();
            foreach(var s in screens) {var r=s.WorkingArea;result.Add(new Surface("floor:"+s.DeviceName,r.Left,r.Right,r.Bottom,true));}
            var blockers=new List<Rectangle>();
            foreach(var window in windows) {
                var h=window.Handle;var r=window.Bounds;
                var spans=new List<double[]> {new double[]{r.Left,r.Right}};
                foreach(var b in blockers) {
                    if(b.Top>=r.Top+2 || b.Bottom<=r.Top-catSize*.85)continue;
                    var remaining=new List<double[]>();
                    foreach(var p in spans) {
                        if(b.Right<=p[0] || b.Left>=p[1]) {remaining.Add(p);continue;}
                        if(b.Left>p[0])remaining.Add(new double[]{p[0],b.Left});
                        if(b.Right<p[1])remaining.Add(new double[]{b.Right,p[1]});
                    }
                    spans=remaining;
                }
                int index=0;
                if(r.Width>=catSize*.65 && r.Height>=40)foreach(var p in spans)foreach(var screen in screens) {
                    Rectangle area=screen.WorkingArea;
                    double left=Math.Max(p[0],area.Left),right=Math.Min(p[1],area.Right);
                    if(right-left<catSize*.65 || r.Top-catSize*.85<area.Top || r.Top>area.Bottom)continue;
                    result.Add(new Surface("window:"+h.ToInt64()+":"+index++,left,right,r.Top,false) {BodyId="window:"+h.ToInt64(),AnchorX=r.Left});
                }
                blockers.Add(r);
            }
            return result;
        }
    }
}
