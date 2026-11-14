using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PetCalendarDesktop {
    public sealed class StickyNote {public string id,text="",color="yellow";public int x=-1,y=-1,width=240,height=220;public bool closed;}

    // Sticky notes opened from the widget's memo button. Saved in sticky-notes.json and
    // reopened when the app starts. × only closes a note (its text is kept and it can be
    // reopened from the widget's + menu); the trash button deletes it for good.
    internal sealed class StickyNotes : IDisposable {
        readonly string path;readonly Action<Exception> log;
        readonly List<StickyNote> notes;readonly Dictionary<string,StickyNoteWindow> windows=new Dictionary<string,StickyNoteWindow>();
        readonly Timer saveTimer=new Timer{Interval=500};
        // A line of a note sent to the calendar: a new schedule, or a task (true when it was added).
        internal event Action<string> EventFromLine;internal Func<string,System.Threading.Tasks.Task<bool>> TaskFromLine;
        internal StickyNotes(string dataDir,Action<Exception> error){
            path=Path.Combine(dataDir,"sticky-notes.json");log=error;
            try{notes=File.Exists(path)?(Codec.Read<List<StickyNote>>(File.ReadAllText(path))??new List<StickyNote>()):new List<StickyNote>();}catch(Exception e){log(e);notes=new List<StickyNote>();}
            notes.RemoveAll(n=>n==null||String.IsNullOrEmpty(n.id));
            saveTimer.Tick+=delegate{saveTimer.Stop();Save();};
        }
        internal int Count{get{return notes.Count;}}
        internal void Restore(){foreach(var note in notes.Where(n=>!n.closed).ToArray())Open(note);}
        // Widget +: straight to a new note, or a menu with the closed notes to reopen.
        internal void Request(Rectangle? beside){
            var closed=notes.Where(n=>n.closed).ToList();if(closed.Count==0){Create(beside);return;}
            var menu=new ContextMenuStrip{ShowImageMargin=false,Font=DesktopTheme.UiFont(10+DesktopTheme.TextScale),BackColor=DesktopTheme.Card,ForeColor=DesktopTheme.Ink};
            menu.Items.Add("+ 새 메모",null,delegate{Create(beside);});menu.Items.Add(new ToolStripSeparator());
            foreach(var note in closed.AsEnumerable().Reverse().Take(12)){var n=note;menu.Items.Add(Preview(n.text),null,delegate{Reopen(n);});}
            if(closed.Count>1)menu.Items.Add("닫은 메모 모두 열기",null,delegate{foreach(var n in closed)Reopen(n);});
            menu.Closed+=delegate{menu.BeginInvoke((Action)menu.Dispose);};menu.Show(Cursor.Position);
        }
        static string Preview(string text){string line=(text??"").Replace("\r","").Split('\n').Select(l=>l.Trim()).FirstOrDefault(l=>l.Length>0)??"(빈 메모)";return line.Length>22?line.Substring(0,22)+"…":line;}
        void Reopen(StickyNote note){note.closed=false;Save();StickyNoteWindow open;if(windows.TryGetValue(note.id,out open)){open.Show();open.FocusText();return;}Open(note).FocusText();}
        // A new empty note beside the given window (the widget): left of it if there is room,
        // otherwise right of it; with no window, near the screen centre. Repeated notes cascade.
        internal void Create(Rectangle? beside){
            var area=beside.HasValue?Screen.FromRectangle(beside.Value).WorkingArea:Screen.PrimaryScreen.WorkingArea;
            float s=ScaleAt(Point.Empty);int w=(int)(240*s),h=(int)(220*s),gap=(int)(14*s),step=(int)(26*s)*(windows.Count%6);int x,y;
            if(beside.HasValue){
                var b=beside.Value;x=b.Left-gap-w-step;
                if(x<area.Left)x=b.Right+gap+step;
                if(x+w>area.Right)x=Math.Max(area.Left,b.Left+step);
                y=b.Top+step;
            }else{x=area.Left+(area.Width-w)/2+step;y=area.Top+(area.Height-h)/2+step;}
            var note=new StickyNote{id=Guid.NewGuid().ToString("N"),x=Math.Max(area.Left,Math.Min(area.Right-w,x)),y=Math.Max(area.Top,Math.Min(area.Bottom-h,y)),width=w,height=h};
            notes.Add(note);Save();Open(note).FocusText();
        }
        static float ScaleAt(Point p){try{using(var g=Graphics.FromHwnd(IntPtr.Zero))return g.DpiX/96f;}catch{return 1;}}
        StickyNoteWindow Open(StickyNote note){
            var window=new StickyNoteWindow(note);windows[note.id]=window;
            window.Changed+=()=>{saveTimer.Stop();saveTimer.Start();};
            window.NewRequested+=()=>Create(window.Bounds);
            window.DeleteRequested+=()=>Delete(note);
            window.CloseRequested+=()=>CloseNote(note);
            window.EventRequested+=line=>{if(EventFromLine!=null)EventFromLine(line);};
            window.TaskRequested+=async line=>{if(TaskFromLine==null)return;bool ok=await TaskFromLine(line);if(!window.IsDisposed)window.Flash(ok?"할 일에 넣었어요 ✓":"할 일에 넣지 못했어요");};
            window.Show();return window;
        }
        void Delete(StickyNote note){
            StickyNoteWindow window;
            if(windows.TryGetValue(note.id,out window)){
                if(note.text.Trim().Length>0&&MessageBox.Show(window,"이 메모를 삭제할까요?\n삭제한 메모는 되돌릴 수 없어요.","스티커 메모",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
                windows.Remove(note.id);window.Close();window.Dispose();
            }
            notes.Remove(note);Save();
        }
        // × keeps the note; an empty one is simply dropped.
        void CloseNote(StickyNote note){
            StickyNoteWindow window;if(windows.TryGetValue(note.id,out window)){windows.Remove(note.id);window.Close();window.Dispose();}
            if((note.text??"").Trim().Length==0)notes.Remove(note);else note.closed=true;Save();
        }
        internal void ApplyFont(){foreach(var w in windows.Values)w.ApplyFont();}
        internal void SetVisible(bool visible){foreach(var w in windows.Values){if(visible)w.Show();else w.Hide();}}
        void Save(){try{StateStore.Atomic(path,Codec.Json(notes),null);}catch(Exception e){log(e);}}
        public void Dispose(){saveTimer.Stop();Save();saveTimer.Dispose();foreach(var w in windows.Values.ToArray()){w.Close();w.Dispose();}windows.Clear();}
    }

    internal sealed class StickyNoteWindow : Form {
        internal static readonly string[] Colors={"yellow","pink","green","blue"};
        static Color Fill(string c){return ColorTranslator.FromHtml(c=="pink"?"#ffd9e2":c=="green"?"#dcf2cf":c=="blue"?"#d8ecfb":"#fff2a8");}
        static Color Bar(string c){return ColorTranslator.FromHtml(c=="pink"?"#ffc4d2":c=="green"?"#c7e8b6":c=="blue"?"#c2e0f7":"#ffe680");}
        readonly StickyNote note;readonly TextBox text=new TextBox();readonly TextScroll scroll;readonly ToolTip tips=new ToolTip();
        internal event Action Changed,NewRequested,DeleteRequested,CloseRequested;internal event Action<string> EventRequested,TaskRequested;
        Rectangle addRect,colorRect,deleteRect,closeRect;string hover;bool loaded;
        [DllImport("user32.dll")]static extern bool ReleaseCapture();
        [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
        internal StickyNoteWindow(StickyNote value){
            note=value;Text="펫 캘린더 · 스티커 메모";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;DoubleBuffered=true;
            AutoScaleMode=AutoScaleMode.None;MinimumSize=new Size(150,120);SetStyle(ControlStyles.ResizeRedraw,true);
            text.Multiline=true;text.BorderStyle=BorderStyle.None;text.AcceptsReturn=true;text.AcceptsTab=true;text.WordWrap=true;text.Text=(note.text??"").Replace("\r\n","\n").Replace("\n","\r\n");scroll=new TextScroll(text);scroll.VisibilityChanged+=Layout2;Controls.Add(scroll.Bar);
            text.TextChanged+=delegate{note.text=text.Text;if(loaded&&Changed!=null)Changed();};
            Controls.Add(text);ApplyColor();ApplyFont();
            // Right-click: the usual editing items plus sending the line (or selection) to the calendar.
            text.MouseDown+=delegate(object sender,MouseEventArgs e){if(e.Button!=MouseButtons.Right)return;int at=text.GetCharIndexFromPosition(e.Location);
                // Keep a selection the user made around the click; a whole-text selection (from focusing the note) is not one.
                bool chosen=text.SelectionLength>0&&text.SelectionLength<text.TextLength&&at>=text.SelectionStart&&at<=text.SelectionStart+text.SelectionLength;if(!chosen)text.Select(at,0);};
            var menu=new ContextMenuStrip{ShowImageMargin=false,BackColor=DesktopTheme.Card,ForeColor=DesktopTheme.Ink};
            var toEvent=new ToolStripMenuItem("이 줄로 일정 만들기",null,delegate{var l=CurrentLine();if(l!=null&&EventRequested!=null)EventRequested(l);});
            var toTask=new ToolStripMenuItem("이 줄을 할 일로 보내기",null,delegate{var l=CurrentLine();if(l!=null&&TaskRequested!=null)TaskRequested(l);});
            var cut=new ToolStripMenuItem("잘라내기",null,delegate{text.Cut();}){ShortcutKeyDisplayString="Ctrl+X"};var copy=new ToolStripMenuItem("복사",null,delegate{text.Copy();}){ShortcutKeyDisplayString="Ctrl+C"};
            var paste=new ToolStripMenuItem("붙여넣기",null,delegate{text.Paste();}){ShortcutKeyDisplayString="Ctrl+V"};var all=new ToolStripMenuItem("모두 선택",null,delegate{text.SelectAll();}){ShortcutKeyDisplayString="Ctrl+A"};
            menu.Items.AddRange(new ToolStripItem[]{toEvent,toTask,new ToolStripSeparator(),cut,copy,paste,all});
            menu.Opening+=delegate{menu.Font=DesktopTheme.UiFont(10+DesktopTheme.TextScale);var l=CurrentLine();bool has=l!=null;bool sel=text.SelectionLength>0;
                toEvent.Text=sel?"선택한 글로 일정 만들기":"이 줄로 일정 만들기";toTask.Text=sel?"선택한 글을 할 일로 보내기":"이 줄을 할 일로 보내기";toEvent.Enabled=toTask.Enabled=has;
                cut.Enabled=copy.Enabled=sel;paste.Enabled=Clipboard.ContainsText();};
            text.ContextMenuStrip=menu;
            Bounds=new Rectangle(note.x,note.y,Math.Max(150,note.width),Math.Max(120,note.height));
            loaded=true;
        }
        float Sc{get{return DeviceDpi/96f;}}
        // The selection's first line, or the caret's line, without list markers ("- ", "• ", "1. ", "[ ] ").
        string CurrentLine(){
            string line;
            if(text.SelectionLength>0)line=text.SelectedText.Replace("\r","").Split('\n').Select(x=>x.Trim()).FirstOrDefault(x=>x.Length>0);
            else{int index=text.GetLineFromCharIndex(text.SelectionStart);var lines=text.Lines;line=index>=0&&index<lines.Length?lines[index]:null;}
            if(line==null)return null;
            line=System.Text.RegularExpressions.Regex.Replace(line.Trim(),@"^(?:[-*•·▪◦✓✔☐☑]\s*|\d+[.)]\s*|\[[ xX]?\]\s*)+","").Trim();
            if(line.Length==0)return null;return line.Length>200?line.Substring(0,200):line;
        }
        internal void Flash(string message){var p=PointToClient(Cursor.Position);tips.Show(message,this,Math.Max(0,Math.Min(Width-40,p.X)),Math.Max(BarHeight,p.Y-(int)(28*Sc)),1800);}
        int BarHeight{get{return (int)Math.Round(30*Sc);}}
        internal void FocusText(){Activate();text.Focus();text.SelectionStart=text.TextLength;}
        internal void ApplyFont(){var old=text.Font;text.Font=DesktopTheme.UiFont(11+DesktopTheme.TextScale);if(old!=null&&old!=Control.DefaultFont)old.Dispose();Invalidate();}
        void ApplyColor(){BackColor=Fill(note.color);text.BackColor=BackColor;if(scroll!=null)scroll.Bar.BackColor=BackColor;text.ForeColor=DesktopTheme.Ink;Invalidate();}
        protected override void OnShown(EventArgs e){base.OnShown(e);Layout2();}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);Layout2();Remember();}
        protected override void OnLocationChanged(EventArgs e){base.OnLocationChanged(e);Remember();}
        void Remember(){if(!loaded||WindowState!=FormWindowState.Normal)return;note.x=Left;note.y=Top;note.width=Width;note.height=Height;if(Changed!=null)Changed();}
        void Layout2(){
            float s=Sc;int pad=(int)Math.Round(12*s),bar=BarHeight,b=(int)Math.Round(22*s),gap=(int)Math.Round(4*s);
            closeRect=new Rectangle(Width-pad-b+(int)(4*s),(bar-b)/2,b,b);deleteRect=new Rectangle(closeRect.X-gap-b,closeRect.Y,b,b);colorRect=new Rectangle(deleteRect.X-gap-b,closeRect.Y,b,b);addRect=new Rectangle(colorRect.X-gap-b,closeRect.Y,b,b);
            int sb=scroll.Bar.Visible?(int)Math.Round(12*s):0,top=bar+(int)(6*s),height=Math.Max(1,Height-top-pad);
            text.SetBounds(pad,top,Math.Max(1,Width-pad*2-sb),height);
            scroll.Bar.SetBounds(Width-(int)Math.Round(4*s)-(int)Math.Round(12*s),top,(int)Math.Round(12*s),height);scroll.Sync();
        }
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;float s=Sc;g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var b=new SolidBrush(Bar(note.color)))g.FillRectangle(b,0,0,Width,BarHeight);
            using(var pen=new Pen(Color.FromArgb(60,0,0,0),1))g.DrawRectangle(pen,0,0,Width-1,Height-1);
            Action<Rectangle,string,string> icon=(r,id,label)=>{if(hover==id)using(var b=new SolidBrush(Color.FromArgb(40,0,0,0)))g.FillEllipse(b,r);
                if(id=="delete"){using(var pen=new Pen(Color.FromArgb(160,0,0,0),1.4f*s)){float cx=r.X+r.Width/2f,top=r.Y+7*s;g.DrawLine(pen,cx-5*s,top,cx+5*s,top);g.DrawLine(pen,cx-1.8f*s,top-1.6f*s,cx+1.8f*s,top-1.6f*s);g.DrawRectangle(pen,cx-3.6f*s,top+1.6f*s,7.2f*s,7.6f*s);g.DrawLine(pen,cx-1.2f*s,top+3.6f*s,cx-1.2f*s,top+7.2f*s);g.DrawLine(pen,cx+1.2f*s,top+3.6f*s,cx+1.2f*s,top+7.2f*s);}return;}
                if(id=="color"){var c=new Rectangle(r.X+(int)(5*s),r.Y+(int)(5*s),r.Width-(int)(10*s),r.Height-(int)(10*s));using(var b=new SolidBrush(Fill(Next(note.color))))g.FillEllipse(b,c);using(var pen=new Pen(Color.FromArgb(120,0,0,0),1.2f*s))g.DrawEllipse(pen,c);return;}
                using(var f=new Font("Segoe UI",11f*s,FontStyle.Bold,GraphicsUnit.Pixel))TextRenderer.DrawText(g,label,f,r,Color.FromArgb(160,0,0,0),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);};
            icon(addRect,"add","+");icon(colorRect,"color","");icon(deleteRect,"delete","");icon(closeRect,"close","×");
            // Folded corner.
            int fold=(int)(14*s);using(var b=new SolidBrush(Color.FromArgb(30,0,0,0)))g.FillPolygon(b,new[]{new Point(Width-fold,Height),new Point(Width,Height-fold),new Point(Width,Height)});
        }
        static string Next(string c){int i=Array.IndexOf(Colors,c);return Colors[(i+1+Colors.Length)%Colors.Length];}
        string HitButton(Point p){return addRect.Contains(p)?"add":colorRect.Contains(p)?"color":deleteRect.Contains(p)?"delete":closeRect.Contains(p)?"close":null;}
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);string h=HitButton(e.Location);if(h!=hover){hover=h;Invalidate(new Rectangle(0,0,Width,BarHeight));tips.SetToolTip(this,h=="add"?"새 메모":h=="color"?"색 바꾸기":h=="delete"?"메모 삭제":h=="close"?"닫기 (내용은 남아 있어요. 위젯 +에서 다시 열기)":"");}Cursor=h!=null?Cursors.Hand:Cursors.Default;}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=null;Invalidate(new Rectangle(0,0,Width,BarHeight));}
        protected override void OnMouseDown(MouseEventArgs e){
            base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;string h=HitButton(e.Location);
            if(h=="add"){if(NewRequested!=null)NewRequested();return;}
            if(h=="color"){note.color=Next(note.color);ApplyColor();if(Changed!=null)Changed();return;}
            if(h=="delete"){if(DeleteRequested!=null)DeleteRequested();return;}
            if(h=="close"){if(CloseRequested!=null)CloseRequested();return;}
            if(e.Y<BarHeight){ReleaseCapture();SendMessage(Handle,0x00a1,new IntPtr(2),IntPtr.Zero);}
        }
        protected override void WndProc(ref Message m){
            if(m.Msg==0x0084){
                base.WndProc(ref m);long packed=m.LParam.ToInt64();var p=PointToClient(new Point((short)(packed&0xffff),(short)((packed>>16)&0xffff)));int edge=(int)Math.Max(5,6*Sc);
                bool left=p.X<edge,right=p.X>=Width-edge,top=p.Y<edge,bottom=p.Y>=Height-edge;
                if(left||right||top||bottom){m.Result=new IntPtr(top?(left?13:right?14:12):bottom?(left?16:right?17:15):left?10:11);}
                return;
            }
            base.WndProc(ref m);
        }
        protected override void Dispose(bool disposing){if(disposing)tips.Dispose();base.Dispose(disposing);}
    }
}
