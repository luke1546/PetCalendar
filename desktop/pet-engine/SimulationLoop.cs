using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DesktopPet {
    internal static class SimulationLoop {
        static readonly List<PetHost> hosts=new List<PetHost>();
        static readonly Timer timer=new Timer {Interval=16};
        static volatile bool suspended;
        internal static event Action FrameAdvanced;
        internal static int FrameListenerCount {get{var listeners=FrameAdvanced;return listeners==null?0:listeners.GetInvocationList().Length;}}
        internal static bool Suspended {get {return suspended;}}
        internal static int Count {get {return hosts.Count;}}
        static SimulationLoop() {timer.Tick+=Tick;}
        internal static void Add(PetHost host) {
            if(hosts.Contains(host))return;
            if(hosts.Count==0) {
                SystemEvents.DisplaySettingsChanged+=DisplayChanged;SystemEvents.PowerModeChanged+=PowerChanged;
                suspended=false;World.Invalidate();timer.Interval=16;timer.Start();
            }
            hosts.Add(host);
        }
        internal static void Wake() {if(hosts.Count>0)timer.Interval=16;}
        internal static void Remove(PetHost host) {
            hosts.Remove(host);
            if(hosts.Count==0) {
                timer.Stop();SystemEvents.DisplaySettingsChanged-=DisplayChanged;SystemEvents.PowerModeChanged-=PowerChanged;World.Clear();
            }
        }
        static void DisplayChanged(object sender,EventArgs args) {World.Invalidate();}
        static void PowerChanged(object sender,PowerModeChangedEventArgs args) {suspended=args.Mode==PowerModes.Suspend;World.Invalidate();}
        static void Tick(object sender,EventArgs args) {
            double now=World.Now;int interval=100;
            // Hosts can remove themselves when their native windows close during callbacks.
            foreach(var host in hosts.ToArray())if(!host.IsDisposed) {host.Pulse(now);interval=Math.Min(interval,host.PreferredInterval);}
            if(FrameAdvanced!=null)FrameAdvanced();
            if(timer.Interval!=interval)timer.Interval=interval;
        }
    }
}
