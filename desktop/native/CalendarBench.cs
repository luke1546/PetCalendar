using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PetCalendarDesktop {
    // Diagnostic only (automation "calendar-bench"): times what a user does most,
    // each step including its synchronous repaint, so slow paths show up as numbers.
    public sealed partial class CalendarView {
        internal object Bench(){
            var result=new Dictionary<string,object>();
            Func<int,Action<int>,object> time=(n,step)=>{
                var ms=new List<double>();
                for(int i=0;i<n;i++){var sw=Stopwatch.StartNew();step(i);Update();ms.Add(sw.Elapsed.TotalMilliseconds);Application.DoEvents();}
                ms.Sort();return new{n=n,median=Math.Round(ms[n/2],2),p95=Math.Round(ms[Math.Max(0,(int)(n*.95)-1)],2),max=Math.Round(ms[n-1],2)};
            };
            Relayout();Update();
            // First event dialog of the session (the warm-up should already have run).
            {var cold=Stopwatch.StartNew();OpenEvent(null,selected,false);foreach(var d in DialogForm.Open)d.Update();result["firstOpen"]=Math.Round(cold.Elapsed.TotalMilliseconds,1);foreach(var d in DialogForm.Open.ToArray())d.CloseDialog();Application.DoEvents();}
            result["size"]=Width+"x"+Height;result["fit"]=m.Fit;
            result["fullRepaint"]=time(20,i=>{paintAll=true;Invalidate();});
            PerfProbe.BenchBegin();result["monthMove"]=time(20,i=>MonthMove(i%2==0?1:-1));result["monthSections"]=PerfProbe.BenchEnd();
            var cellHits=hits.Where(h=>h.DropDate!=null).Select(h=>h.Rect).ToList();
            result["hoverCells"]=cellHits.Count;
            if(cellHits.Count>0)result["hoverMove"]=time(40,i=>{var r=cellHits[(i*5)%cellHits.Count];OnMouseMove(new MouseEventArgs(MouseButtons.None,0,r.X+r.Width/2,r.Y+r.Height/2,0));});
            var slow=new List<string>();
            result["selectDay"]=time(20,i=>{if(cellHits.Count>0){var h=hits.FirstOrDefault(x=>x.DropDate!=null&&x.Rect==cellHits[(i*3)%cellHits.Count]);if(h!=null&&h.Click!=null){PerfProbe.BenchBegin();var sw=Stopwatch.StartNew();h.Click();Update();var parts=PerfProbe.BenchEnd();if(sw.ElapsedMilliseconds>30)slow.Add(h.Id+" "+sw.ElapsedMilliseconds+"ms "+string.Join(",",parts.Select(p=>p.Key+"="+p.Value)));}}});
            result["slowSelect"]=slow;
            if(!m.Fit&&pageScroll.Visible)result["pageScroll"]=time(20,i=>{pageScroll.Offset+=i%2==0?P(40):-P(40);pageScroll.Clamp();ScrolledBy(pageScroll);});
            if(dayScroll.Visible)result["dayScroll"]=time(20,i=>{dayScroll.Offset+=i%2==0?P(40):-P(40);dayScroll.Clamp();ScrolledBy(dayScroll);});
            if(taskScroll.Visible)result["taskScroll"]=time(20,i=>{taskScroll.Offset+=i%2==0?P(40):-P(40);taskScroll.Clamp();ScrolledBy(taskScroll);});
            var form=FindForm();var size0=form.Size;
            result["resize"]=time(20,i=>{form.Size=new Size(size0.Width+(i%2==0?P(12):0),size0.Height+(i%2==0?P(8):0));});form.Size=size0;Update();
            string day=selected;
            result["openClose"]=time(8,i=>{OpenEvent(null,day,false);Application.DoEvents();foreach(var d in DialogForm.Open.ToArray())d.CloseDialog();});
            PerfProbe.BenchBegin();
            var openParts=new Dictionary<string,double>();Action<string,double> add=(k,v)=>{double o;openParts.TryGetValue(k,out o);openParts[k]=o+v/8;};
            for(int i=0;i<8;i++){
                var sw=Stopwatch.StartNew();var dialog=new EventDialog(this,null,day,false);add("construct",sw.Elapsed.TotalMilliseconds);
                sw.Restart();dialog.Show(dialog.TitleBox.Box);add("show",sw.Elapsed.TotalMilliseconds);
                sw.Restart();dialog.Update();Update();add("firstPaint",sw.Elapsed.TotalMilliseconds);
                sw.Restart();Application.DoEvents();add("events",sw.Elapsed.TotalMilliseconds);
                sw.Restart();dialog.CloseDialog();Update();add("close",sw.Elapsed.TotalMilliseconds);Application.DoEvents();
            }
            result["openSections"]=PerfProbe.BenchEnd();result["openParts"]=openParts.ToDictionary(p=>p.Key,p=>Math.Round(p.Value,2));
            PerfProbe.BenchBegin();for(int i=0;i<10;i++){form.Size=new Size(size0.Width+(i%2==0?P(12):0),size0.Height);Update();}form.Size=size0;Update();result["resizeSections"]=PerfProbe.BenchEnd();
            // A wheel notch, then another 150 ms later: time(ms):offset(paint ms) per glide frame.
            foreach(var pair in new[]{Tuple.Create("glideTask",taskScroll),Tuple.Create("glideDay",dayScroll)}){
                var sm=pair.Item2;if(!sm.Visible)continue;sm.Offset=0;Invalidate();Update();
                var trace=new List<string>();var clock=Stopwatch.StartNew();
                sm.Wheel(-120,s,()=>{var t=clock.Elapsed.TotalMilliseconds;ScrolledBy(sm);Update();trace.Add(Math.Round(t)+":"+sm.Offset+"("+Math.Round(clock.Elapsed.TotalMilliseconds-t,1)+")");});
                while(clock.ElapsedMilliseconds<150){Application.DoEvents();System.Threading.Thread.Sleep(1);}
                sm.Wheel(-120,s,()=>{var t=clock.Elapsed.TotalMilliseconds;ScrolledBy(sm);Update();trace.Add(Math.Round(t)+":"+sm.Offset+"("+Math.Round(clock.Elapsed.TotalMilliseconds-t,1)+")");});
                while(clock.ElapsedMilliseconds<600){Application.DoEvents();System.Threading.Thread.Sleep(1);}
                result[pair.Item1]=string.Join(" ",trace);
            }
            OnMouseLeave(EventArgs.Empty);
            return result;
        }
    }
}
