using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace PetCalendarDesktop {
    // Diagnostic only: set PETCAL_PERF to a log path to record input → idle latency.
    // Each mouse button / key press is timed until the UI thread next goes idle
    // (all handlers and WM_PAINTs done), with the time spent in named sections.
    public sealed class PerfProbe : IMessageFilter {
        static readonly Stopwatch clock=Stopwatch.StartNew();
        static string path;static long start=-1;static string label;
        static readonly Dictionary<string,double> spent=new Dictionary<string,double>();static readonly Dictionary<string,int> counts=new Dictionary<string,int>();
        static readonly bool Moves=Environment.GetEnvironmentVariable("PETCAL_PERF_MOVES")=="1";
        public static bool On{get{return path!=null;}}
        public static void Install(){
            path=Environment.GetEnvironmentVariable("PETCAL_PERF");if(string.IsNullOrEmpty(path)){path=null;return;}
            Application.AddMessageFilter(new PerfProbe());Application.Idle+=Idle;
        }
        static long lastAt=-1;static string lastKey;
        static void Account(long now){if(lastKey!=null&&start>=0){double ms=(now-lastAt)*1000.0/Stopwatch.Frequency;Add("m:"+lastKey,ms);}lastKey=null;}
        static void Add(string name,double ms){double v;spent.TryGetValue(name,out v);spent[name]=v+ms;int c;counts.TryGetValue(name,out c);counts[name]=c+1;}
        static string Name(int msg){switch(msg){case 0xF:return "paint";case 0x113:return "timer";case 0x200:return "move";case 0x201:return "down";case 0x202:return "up";case 0x100:return "key";case 0x102:return "char";case 0x14:return "erase";default:return msg>=0xC000?"posted":"0x"+msg.ToString("x");}}
        public bool PreFilterMessage(ref Message m){
            long now=clock.ElapsedTicks;Account(now);
            if(m.Msg==0x201||m.Msg==0x202||m.Msg==0x100||m.Msg==0x203||m.Msg==0x20A||(m.Msg==0x200&&Moves)){
                if(start>=0)Flush("(merged)");
                start=clock.ElapsedTicks;var c=Control.FromHandle(m.HWnd);
                label=(m.Msg==0x201?"down":m.Msg==0x202?"up":m.Msg==0x203?"dbl":m.Msg==0x20A?"wheel":m.Msg==0x200?"move":"key"+m.WParam)+" "+(c==null?"?":c.GetType().Name)+" @"+((int)m.LParam&0xffff)+","+(((int)m.LParam>>16)&0xffff);
            }
            if(start>=0){var ctl=Control.FromHandle(m.HWnd);lastKey=Name(m.Msg)+":"+(ctl==null?"?":ctl.GetType().Name);lastAt=clock.ElapsedTicks;}
            return false;
        }
        static void Idle(object sender,EventArgs e){Account(clock.ElapsedTicks);if(start>=0)Flush("");}
        static void Flush(string note){
            double ms=(clock.ElapsedTicks-start)*1000.0/Stopwatch.Frequency;start=-1;
            var sb=new StringBuilder();sb.Append(label).Append(" total=").Append(ms.ToString("0.0")).Append("ms ").Append(note);
            foreach(var pair in spent)sb.Append(" ").Append(pair.Key).Append("=").Append(pair.Value.ToString("0.0")).Append("x").Append(counts[pair.Key]);
            spent.Clear();counts.Clear();
            try{System.IO.File.AppendAllText(path,sb+Environment.NewLine);}catch{}
        }
        // Bench mode: collect named section times without a log file.
        static bool bench;
        public static void BenchBegin(){bench=true;start=0;spent.Clear();counts.Clear();}
        public static Dictionary<string,string> BenchEnd(){bench=false;start=-1;var r=spent.OrderByDescending(p=>p.Value).ToDictionary(p=>p.Key,p=>p.Value.ToString("0.0")+"ms x"+counts[p.Key]);spent.Clear();counts.Clear();return r;}
        public static Span T(string name){return On||bench?new Span(name):null;}
        public sealed class Span : IDisposable {
            readonly string name;readonly long at=clock.ElapsedTicks;
            public Span(string n){name=n;}
            public void Dispose(){if(start<0)return;Add(name,(clock.ElapsedTicks-at)*1000.0/Stopwatch.Frequency);}
        }
    }
}
