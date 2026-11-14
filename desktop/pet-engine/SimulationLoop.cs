using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DesktopPet {
    // Drives every pet, bubble and toy. A WinForms timer only fires every ~25-48 ms
    // (system tick granularity), which made walking look choppy, so a background thread
    // with a 1 ms system timer paces frames evenly and asks the UI thread for each one.
    // Moving pets update every refresh; idle/sleeping pets ask for fewer frames.
    internal static class SimulationLoop {
        static readonly List<PetHost> hosts=new List<PetHost>();
        static volatile bool suspended,running;
        static volatile int wantMs=16;
        static int pending;
        static Thread pump;
        static Control marshal;
        internal static event Action FrameAdvanced;
        internal static int FrameListenerCount {get{var listeners=FrameAdvanced;return listeners==null?0:listeners.GetInvocationList().Length;}}
        internal static bool Suspended {get {return suspended;}}
        internal static int Count {get {return hosts.Count;}}
        // Frames actually run; diagnostics compare this over time to get the frame rate.
        internal static long Frames;
        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint period);
        [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint period);
        internal static void Add(PetHost host) {
            if(hosts.Contains(host))return;
            if(hosts.Count==0) {
                SystemEvents.DisplaySettingsChanged+=DisplayChanged;SystemEvents.PowerModeChanged+=PowerChanged;
                suspended=false;World.Invalidate();Start();
            }
            hosts.Add(host);
        }
        internal static void Wake() {if(hosts.Count>0)wantMs=16;}
        internal static void Remove(PetHost host) {
            hosts.Remove(host);
            if(hosts.Count==0) {
                Stop();SystemEvents.DisplaySettingsChanged-=DisplayChanged;SystemEvents.PowerModeChanged-=PowerChanged;World.Clear();
            }
        }
        static void Start() {
            if(running)return;
            if(pump!=null&&pump.IsAlive){running=true;wantMs=16;return;} // old pump not finished yet: keep it
            if(marshal==null){marshal=new Control();marshal.CreateControl();var h=marshal.Handle;}
            running=true;wantMs=16;
            pump=new Thread(Pump){IsBackground=true,Name="pet frames",Priority=ThreadPriority.AboveNormal};pump.Start();
        }
        static void Stop() {running=false;}
        static void Pump() {
            timeBeginPeriod(1);
            try {
                var clock=System.Diagnostics.Stopwatch.StartNew();double next=0;
                while(running) {
                    // Moving pets: 60 fps (16.7 ms). Idle 33 ms, sleeping 100 ms. With a 1 ms system
                    // timer, Sleep lands within ~1 ms, so frames come evenly without busy waiting.
                    int want=wantMs;double gap=want<=16?1000.0/60:want;
                    double now=clock.Elapsed.TotalMilliseconds;
                    if(now<next-.5){Thread.Sleep(Math.Max(1,(int)(next-now)));continue;}
                    next=Math.Max(next+gap,now+gap*.5); // stay on the beat; never queue a burst after a stall
                    if(Interlocked.CompareExchange(ref pending,1,0)!=0)continue; // previous frame still running
                    try{marshal.BeginInvoke((MethodInvoker)Tick);}catch(InvalidOperationException){Interlocked.Exchange(ref pending,0);}
                }
            } finally {timeEndPeriod(1);}
        }
        static void DisplayChanged(object sender,EventArgs args) {World.Invalidate();}
        static void PowerChanged(object sender,PowerModeChangedEventArgs args) {suspended=args.Mode==PowerModes.Suspend;World.Invalidate();}
        static void Tick() {
            try {
                if(!running)return;
                Frames++;
                double now=World.Now;int interval=100;
                // Hosts can remove themselves when their native windows close during callbacks.
                foreach(var host in hosts.ToArray())if(!host.IsDisposed) {host.Pulse(now);interval=Math.Min(interval,host.PreferredInterval);}
                if(FrameAdvanced!=null)FrameAdvanced();
                wantMs=interval;
            } finally {Interlocked.Exchange(ref pending,0);}
        }
    }
}
