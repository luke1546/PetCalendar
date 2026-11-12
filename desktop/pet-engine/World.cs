using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace DesktopPet {
    // Snapshots are immutable while a simulation step is running. All pets share one capture.
    internal sealed class WorldSnapshot {
        internal readonly Screen[] Screens;
        internal readonly List<Rectangle> WorkAreas=new List<Rectangle>();
        readonly List<Native.WindowInfo> windows;
        readonly Dictionary<int,List<Surface>> surfaces=new Dictionary<int,List<Surface>>();
        readonly double[] dpi;
        readonly Rectangle foreground;
        internal WorldSnapshot() {
            Screens=Screen.AllScreens;dpi=new double[Screens.Length];windows=Native.CaptureWindows();
            for(int i=0;i<Screens.Length;i++) {
                var screen=Screens[i];WorkAreas.Add(screen.WorkingArea);
                dpi[i]=Native.Dpi(new Point(screen.Bounds.Left+screen.Bounds.Width/2,screen.Bounds.Top+screen.Bounds.Height/2));
            }
            var h=Native.GetForegroundWindow();
            if(h!=IntPtr.Zero && Native.IsWindowVisible(h) && !Native.IsIconic(h) && !Native.Shell(h))foreground=Native.Bounds(h);
        }
        internal List<Surface> Surfaces(double size) {
            int key=(int)Math.Ceiling(size);List<Surface> result;
            if(!surfaces.TryGetValue(key,out result)) {result=Native.BuildSurfaces(windows,Screens,key);surfaces.Add(key,result);}
            return result;
        }
        internal int MonitorAt(Point point) {
            for(int i=0;i<Screens.Length;i++)if(Screens[i].Bounds.Contains(point))return i;
            int closest=0;double distance=double.MaxValue;
            for(int i=0;i<Screens.Length;i++) {
                var r=Screens[i].Bounds;double dx=Math.Max(r.Left-point.X,Math.Max(0,point.X-r.Right)),dy=Math.Max(r.Top-point.Y,Math.Max(0,point.Y-r.Bottom));
                double squared=dx*dx+dy*dy;if(squared<distance){closest=i;distance=squared;}
            }
            return closest;
        }
        internal double DpiAt(Point point) {return dpi[MonitorAt(point)];}
        internal bool Fullscreen(Point point) {
            if(foreground.IsEmpty)return false;
            var r=Screens[MonitorAt(point)].Bounds;
            return foreground.Left<=r.Left && foreground.Top<=r.Top && foreground.Right>=r.Right && foreground.Bottom>=r.Bottom;
        }
    }
    internal static class World {
        static WorldSnapshot snapshot;
        static double captured;
        static volatile bool dirty=true;
        internal static double Now {get {return Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;}}
        internal static void Invalidate() {dirty=true;}
        internal static WorldSnapshot Get(bool force) {
            double now=Now;
            if(force || dirty || snapshot==null || now-captured>=(now<FastUntil?.04:.25)) {dirty=false;snapshot=new WorldSnapshot();captured=now;}
            return snapshot;
        }
        internal static void Clear() {snapshot=null;dirty=true;}
        // While a window carrying a pet is moving, windows are re-read every 40 ms instead of 250 ms.
        internal static double FastUntil;
    }
}
