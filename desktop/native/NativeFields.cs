using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace PetCalendarDesktop {
    // Segmented editor shared by <input type=date> and <input type=time>.
    // Digits fill the focused segment, arrows step it, and the trailing
    // indicator opens a themed picker, matching Chrome's interaction model.
    public abstract class SegmentField : Control {
        protected int?[] parts;protected int focus=-1;protected string typed="";protected bool hoverIcon;
        public bool Compact;public float FontCss=13,PadX=11;public bool Required;public Color? Surface;
        public event EventHandler ValueChanged;
        protected SegmentField(int count){parts=new int?[count];SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);TabStop=true;Cursor=Cursors.Default;}
        protected float Sc{get{return DeviceDpi/96f;}}
        protected abstract string SegmentText(int index);
        protected abstract string Separator(int index);
        protected abstract int Digits(int index);
        protected abstract int Min(int index);
        protected abstract int Max(int index);
        protected abstract int Today(int index);
        protected abstract string IconName{get;}
        protected abstract void OpenPicker();
        public abstract string Value{get;set;}
        protected void Changed(){Invalidate();if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);}
        // Web date/time inputs inherit the .field font-weight (700) in dialogs; compact task-row fields stay regular.
        protected Font TextFont{get{return Ui.Font(Sc,FontCss,Compact?400:700);}}
        public int PreferredHeight(){float s=Sc;return (int)Math.Round(TextFont.GetHeight()+(Compact?2*s:24*s));}
        public int ContentWidth(){float s=Sc;float w=0;for(int i=0;i<parts.Length;i++)w+=Ui.Width(SegmentText(i),TextFont)+Ui.Width(Separator(i),TextFont);return (int)Math.Ceiling(w)+(int)Math.Round((Compact?22:30)*s);}
        protected List<Rectangle> Segments(){
            var list=new List<Rectangle>();float s=Sc;float x=Compact?0:PadX*s+s;
            for(int i=0;i<parts.Length;i++){float w=Ui.Width(SegmentText(i),TextFont);list.Add(new Rectangle((int)Math.Round(x),0,(int)Math.Ceiling(w),Height));x+=w+Ui.Width(Separator(i),TextFont);}
            return list;
        }
        protected Rectangle IconRect{get{float s=Sc;int size=(int)Math.Round(15*s);var seg=Segments();int x=Compact?seg[seg.Count-1].Right+(int)(9*s):Width-(int)Math.Round(PadX*s+s)-size;return new Rectangle(Math.Min(x,Width-size-1),(Height-size)/2,size,size);}}
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;Color back=Surface??(Parent==null?DesktopTheme.Card:Parent.BackColor);g.Clear(back);Ui.Prepare(g);float s=Sc;
            float alpha=Enabled?1:.5f;Func<Color,Color> f=c=>Ui.Fade(c,back,alpha);
            Color surface=back;
            if(!Compact){bool on=Focused;surface=Ui.C("#fffaf1");Ui.Box(g,new RectangleF(0,0,Width,Height),7*s,f(surface),f(on?DesktopTheme.Orange:Ui.C("#d5c7ad")),on?2*s:s);}
            var segs=Segments();var font=TextFont;Color ink=f(Compact?Ui.C("#8b7959"):DesktopTheme.Ink);
            for(int i=0;i<parts.Length;i++){
                var r=segs[i];if(Focused&&focus==i){var hi=new Rectangle(r.X-1,(Height-(int)font.GetHeight())/2-1,r.Width+2,(int)font.GetHeight()+2);Ui.Fill(g,hi,2*s,Ui.C("#ffdc72"));}
                Ui.Text(g,SegmentText(i),font,new Rectangle(r.X,0,r.Width+2,Height),parts[i].HasValue?ink:f(Compact?Ui.C("#8b7959"):Ui.C("#757575")),TextFormatFlags.VerticalCenter);
                string sep=Separator(i);if(sep.Length>0)Ui.Text(g,sep,font,new Rectangle(r.Right,0,Ui.Width(sep,font)+2,Height),ink,TextFormatFlags.VerticalCenter);
            }
            var icon=IconRect;Ui.Icon(g,IconName,icon,f(hoverIcon?Ui.C("#000000"):ink),2.1f);
        }
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);bool h=IconRect.Contains(e.Location);if(h!=hoverIcon){hoverIcon=h;Invalidate();}Cursor=h?Cursors.Hand:Cursors.Default;}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hoverIcon=false;Invalidate();}
        protected override void OnMouseDown(MouseEventArgs e){
            base.OnMouseDown(e);if(e.Button!=MouseButtons.Left||!Enabled)return;Focus();
            if(IconRect.Contains(e.Location)){OpenPicker();return;}
            var segs=Segments();int hit=segs.FindIndex(r=>e.X>=r.X-4&&e.X<=r.Right+4);focus=hit>=0?hit:(e.X<segs[0].X?0:parts.Length-1);typed="";Invalidate();
        }
        protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);if(focus<0)focus=0;Invalidate();}
        protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);typed="";Invalidate();}
        protected override bool IsInputKey(Keys k){var key=k&Keys.KeyCode;return key==Keys.Left||key==Keys.Right||key==Keys.Up||key==Keys.Down||base.IsInputKey(k);}
        protected override void OnKeyDown(KeyEventArgs e){
            base.OnKeyDown(e);if(focus<0)focus=0;
            if(e.KeyCode==Keys.Left){focus=Math.Max(0,focus-1);typed="";Invalidate();e.Handled=true;}
            else if(e.KeyCode==Keys.Right){focus=Math.Min(parts.Length-1,focus+1);typed="";Invalidate();e.Handled=true;}
            else if(e.KeyCode==Keys.Up||e.KeyCode==Keys.Down){if(e.Alt&&e.KeyCode==Keys.Down){OpenPicker();return;}Step(focus,e.KeyCode==Keys.Up?1:-1);typed="";Changed();e.Handled=true;}
            else if(e.KeyCode==Keys.F4){OpenPicker();e.Handled=true;}
            else if(e.KeyCode==Keys.Back||e.KeyCode==Keys.Delete){parts[focus]=null;typed="";Changed();e.Handled=true;}
            else if(e.KeyCode==Keys.Enter&&false){}
        }
        protected virtual void Step(int index,int delta){int min=Min(index),max=Max(index);parts[index]=parts[index].HasValue?(parts[index].Value-min+delta+(max-min+1))%(max-min+1)+min:Today(index);}
        protected override void OnKeyPress(KeyPressEventArgs e){
            base.OnKeyPress(e);if(focus<0||!char.IsDigit(e.KeyChar))return;e.Handled=true;
            typed+=e.KeyChar;int digits=Digits(focus);int value=int.Parse(typed);
            bool complete=typed.Length>=digits||value*10>Max(focus);
            if(digits==4&&typed.Length<4){parts[focus]=value;Changed();return;}
            parts[focus]=Math.Max(complete?Min(focus):0,Math.Min(Max(focus),value));
            if(complete){if(parts[focus].Value<Min(focus))parts[focus]=Min(focus);typed="";if(focus<parts.Length-1)focus++;}
            Changed();
        }
    }
    public sealed class DateField : SegmentField {
        public DateField():base(3){}
        protected override string IconName{get{return "calendar";}}
        protected override string SegmentText(int i){if(!parts[i].HasValue)return i==0?"년":i==1?"월":"일";return i==0?parts[i].Value.ToString("0000"):parts[i].Value.ToString("00");}
        protected override string Separator(int i){return i<2?"-":"";}
        protected override int Digits(int i){return i==0?4:2;}
        protected override int Min(int i){return i==0?1000:1;}
        protected override int Max(int i){return i==0?9999:i==1?12:31;}
        protected override int Today(int i){var t=DateTime.Today;return i==0?t.Year:i==1?t.Month:t.Day;}
        public override string Value{
            get{if(!parts[0].HasValue||!parts[1].HasValue||!parts[2].HasValue||parts[0]<1000)return "";if(parts[2].Value>DateTime.DaysInMonth(parts[0].Value,parts[1].Value))return "";return parts[0].Value.ToString("0000")+"-"+parts[1].Value.ToString("00")+"-"+parts[2].Value.ToString("00");}
            set{if(Rules.IsDate(value)){var d=Rules.Date(value);parts[0]=d.Year;parts[1]=d.Month;parts[2]=d.Day;}else{parts[0]=parts[1]=parts[2]=null;}typed="";Invalidate();}
        }
        public void SetValue(string value){Value=value;Changed();}
        protected override void OpenPicker(){
            float s=Sc;var picker=new DatePicker(this);picker.Size=new Size((int)Math.Round(250*s),(int)Math.Round(286*s));
            var popup=new Popup(picker);picker.Done=v=>{popup.Close();if(v!=null)SetValue(v);Focus();};popup.Show(this,new Point(Compact?0:0,Height+(int)(2*s)));picker.Focus();
        }
    }
    sealed class DatePicker : Control {
        readonly DateField owner;DateTime month;int hover=-1;string hoverButton;public Action<string> Done;
        public DatePicker(DateField field){owner=field;DoubleBuffered=true;SetStyle(ControlStyles.Selectable|ControlStyles.ResizeRedraw,true);var v=owner.Value;var d=v==""?DateTime.Today:Rules.Date(v);month=new DateTime(d.Year,d.Month,1);}
        float Sc{get{return owner.DeviceDpi/96f;}}
        DateTime First{get{return month.AddDays(-(int)month.DayOfWeek);}}
        Rectangle Cell(int i){float s=Sc;int w=(Width-(int)(16*s))/7,h=(int)(30*s);return new Rectangle((int)(8*s)+i%7*w,(int)(72*s)+i/7*h,w,h);}
        Rectangle Arrow(int dir){float s=Sc;return new Rectangle(Width-(int)((dir<0?64:34)*s),(int)(10*s),(int)(26*s),(int)(26*s));}
        Rectangle Footer(string name){float s=Sc;int y=Height-(int)(36*s);return name=="clear"?new Rectangle((int)(10*s),y,(int)(56*s),(int)(28*s)):new Rectangle(Width-(int)(66*s),y,(int)(56*s),(int)(28*s));}
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;float s=Sc;g.Clear(DesktopTheme.Card);Ui.Prepare(g);
            using(var pen=new Pen(DesktopTheme.Frame,s))g.DrawRectangle(pen,0,0,Width-1,Height-1);
            Ui.Text(g,month.ToString("yyyy년 M월"),Ui.Font(s,13,700),new Rectangle((int)(14*s),(int)(10*s),(int)(150*s),(int)(26*s)),DesktopTheme.Ink,TextFormatFlags.VerticalCenter);
            foreach(int dir in new[]{-1,1}){var r=Arrow(dir);if(hoverButton==(dir<0?"prev":"next"))Ui.Fill(g,r,6*s,Ui.C("#f9ecd6"));Ui.Icon(g,dir<0?"left":"right",new RectangleF(r.X+5*s,r.Y+5*s,16*s,16*s),DesktopTheme.Ink,2f);}
            string[] names={"일","월","화","수","목","금","토"};var small=Ui.Font(s,10,700);
            for(int d=0;d<7;d++){var c=Cell(d);Ui.Text(g,names[d],small,new Rectangle(c.X,(int)(44*s),c.Width,(int)(24*s)),d==0?Ui.C("#cf6154"):d==6?Ui.C("#5d8db3"):DesktopTheme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
            string selected=owner.Value,today=Rules.Key(DateTime.Today);var font=Ui.Font(s,11,400);
            for(int i=0;i<42;i++){
                var day=First.AddDays(i);if(i>=35&&day.Month!=month.Month&&First.AddDays(35).Month!=month.Month)break;
                var c=Cell(i);var r=new RectangleF(c.X+2*s,c.Y+2*s,c.Width-4*s,c.Height-4*s);string key=Rules.Key(day);
                if(key==selected)Ui.Fill(g,r,6*s,DesktopTheme.Orange);else if(i==hover)Ui.Fill(g,r,6*s,Ui.C("#f9ecd6"));
                if(key==today&&key!=selected)Ui.Stroke(g,r,6*s,DesktopTheme.Orange,1.5f*s);
                Color ink=key==selected?Color.White:day.Month!=month.Month?Ui.C("#b7ad9c"):day.DayOfWeek==DayOfWeek.Sunday?Ui.C("#cf6154"):day.DayOfWeek==DayOfWeek.Saturday?Ui.C("#5d8db3"):DesktopTheme.Ink;
                Ui.Text(g,day.Day.ToString(),font,Rectangle.Round(r),ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            }
            foreach(var name in new[]{"clear","today"}){var r=Footer(name);if(hoverButton==name)Ui.Fill(g,r,6*s,Ui.C("#f9ecd6"));Ui.Text(g,name=="clear"?"삭제":"오늘",Ui.Font(s,11,700),r,Ui.C("#b96936"),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
        }
        int At(Point p){for(int i=0;i<42;i++)if(Cell(i).Contains(p)){var day=First.AddDays(i);if(i>=35&&day.Month!=month.Month&&First.AddDays(35).Month!=month.Month)return -1;return i;}return -1;}
        string ButtonAt(Point p){if(Arrow(-1).Contains(p))return "prev";if(Arrow(1).Contains(p))return "next";if(Footer("clear").Contains(p))return "clear";if(Footer("today").Contains(p))return "today";return null;}
        protected override void OnMouseMove(MouseEventArgs e){int i=At(e.Location);string b=ButtonAt(e.Location);if(i!=hover||b!=hoverButton){hover=i;hoverButton=b;Invalidate();}Cursor=i>=0||b!=null?Cursors.Hand:Cursors.Default;}
        protected override void OnMouseUp(MouseEventArgs e){
            string b=ButtonAt(e.Location);
            if(b=="prev"){month=month.AddMonths(-1);Invalidate();return;}if(b=="next"){month=month.AddMonths(1);Invalidate();return;}
            if(b=="clear"){if(Done!=null)Done("");return;}if(b=="today"){if(Done!=null)Done(Rules.Key(DateTime.Today));return;}
            int i=At(e.Location);if(i>=0&&Done!=null)Done(Rules.Key(First.AddDays(i)));
        }
        protected override bool IsInputKey(Keys k){return k==Keys.Left||k==Keys.Right||k==Keys.Up||k==Keys.Down||k==Keys.Enter||base.IsInputKey(k);}
        protected override void OnKeyDown(KeyEventArgs e){
            string current=owner.Value==""?Rules.Key(DateTime.Today):owner.Value;int step=e.KeyCode==Keys.Left?-1:e.KeyCode==Keys.Right?1:e.KeyCode==Keys.Up?-7:e.KeyCode==Keys.Down?7:0;
            if(step!=0){var d=Rules.Date(current).AddDays(step);owner.Value=Rules.Key(d);month=new DateTime(d.Year,d.Month,1);Invalidate();}
            else if(e.KeyCode==Keys.Enter&&Done!=null)Done(current);
        }
    }
    public sealed class TimeField : SegmentField {
        public TimeField():base(3){}
        protected override string IconName{get{return "clock";}}
        protected override string SegmentText(int i){if(!parts[i].HasValue)return "--";return i==0?(parts[0].Value==0?"오전":"오후"):parts[i].Value.ToString("00");}
        protected override string Separator(int i){return i==0?" ":i==1?":":"";}
        protected override int Digits(int i){return i==0?1:2;}
        protected override int Min(int i){return i==1?1:0;}
        protected override int Max(int i){return i==0?1:i==1?12:59;}
        protected override int Today(int i){var t=DateTime.Now;return i==0?(t.Hour<12?0:1):i==1?(t.Hour%12==0?12:t.Hour%12):t.Minute;}
        public override string Value{
            get{if(parts.Any(p=>!p.HasValue))return "";int hour=parts[1].Value%12+(parts[0].Value==1?12:0);return hour.ToString("00")+":"+parts[2].Value.ToString("00");}
            set{TimeSpan t;if(!String.IsNullOrEmpty(value)&&TimeSpan.TryParseExact(value,"hh\\:mm",CultureInfo.InvariantCulture,out t)){parts[0]=t.Hours<12?0:1;parts[1]=t.Hours%12==0?12:t.Hours%12;parts[2]=t.Minutes;}else parts[0]=parts[1]=parts[2]=null;typed="";Invalidate();}
        }
        public void SetValue(string value){Value=value;Changed();}
        protected override void OnKeyPress(KeyPressEventArgs e){
            if(focus==0){char c=char.ToLowerInvariant(e.KeyChar);if(c=='a'||c=='ㅇ'||c=='오'){parts[0]=0;Changed();e.Handled=true;return;}if(c=='p'){parts[0]=1;Changed();e.Handled=true;return;}if(char.IsDigit(e.KeyChar)){focus=1;}}
            base.OnKeyPress(e);
        }
        protected override void OpenPicker(){
            float s=Sc;var picker=new TimePicker(this);picker.Size=new Size((int)Math.Round(196*s),(int)Math.Round(236*s));
            var popup=new Popup(picker);picker.Done=v=>{popup.Close();if(v!=null)SetValue(v);Focus();};popup.Show(this,new Point(0,Height+(int)(2*s)));picker.Focus();
        }
    }
    sealed class TimePicker : Control {
        readonly TimeField owner;int ampm,hour,minute;int[] offsets=new int[3];int hoverCol=-1,hoverRow=-1;public Action<string> Done;
        public TimePicker(TimeField field){owner=field;DoubleBuffered=true;SetStyle(ControlStyles.Selectable|ControlStyles.ResizeRedraw,true);var v=owner.Value;TimeSpan t;if(!TimeSpan.TryParseExact(v,"hh\\:mm",CultureInfo.InvariantCulture,out t))t=new TimeSpan(9,0,0);ampm=t.Hours<12?0:1;hour=t.Hours%12==0?12:t.Hours%12;minute=t.Minutes;}
        float Sc{get{return owner.DeviceDpi/96f;}}
        int Row{get{return (int)Math.Round(28*Sc);}}
        int Count(int col){return col==0?2:col==1?12:60;}
        string Label(int col,int i){return col==0?(i==0?"오전":"오후"):col==1?(i+1).ToString("00"):i.ToString("00");}
        bool Chosen(int col,int i){return col==0?i==ampm:col==1?i+1==hour:i==minute;}
        Rectangle Column(int col){float s=Sc;int w=(Width-(int)(16*s))/3;return new Rectangle((int)(8*s)+col*w,(int)(8*s),w,Height-(int)(52*s));}
        Rectangle Ok{get{float s=Sc;return new Rectangle(Width-(int)(70*s),Height-(int)(38*s),(int)(60*s),(int)(30*s));}}
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);offsets[1]=Math.Max(0,(hour-1)*Row-Row*2);offsets[2]=Math.Max(0,minute*Row-Row*2);}
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;float s=Sc;g.Clear(DesktopTheme.Card);Ui.Prepare(g);
            using(var pen=new Pen(DesktopTheme.Frame,s))g.DrawRectangle(pen,0,0,Width-1,Height-1);var font=Ui.Font(s,12,400);
            for(int col=0;col<3;col++){
                var area=Column(col);var clip=g.Clip;g.SetClip(area);
                for(int i=0;i<Count(col);i++){var r=new Rectangle(area.X+(int)(2*s),area.Y+i*Row-offsets[col],area.Width-(int)(4*s),Row-(int)(2*s));if(r.Bottom<area.Top||r.Top>area.Bottom)continue;
                    if(Chosen(col,i))Ui.Fill(g,r,6*s,Ui.C("#ffe1ca"));else if(col==hoverCol&&i==hoverRow)Ui.Fill(g,r,6*s,Ui.C("#f9ecd6"));
                    Ui.Text(g,Label(col,i),Chosen(col,i)?Ui.Font(s,12,700,null,Label(col,i)):font,r,DesktopTheme.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
                g.Clip=clip;
            }
            var ok=Ok;Ui.Box(g,ok,9*s,DesktopTheme.Orange,Ui.C("#725849"),1.5f*s);Ui.Text(g,"확인",Ui.Font(s,12,700),ok,DesktopTheme.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        }
        void Hit(Point p,out int col,out int row){col=-1;row=-1;for(int c=0;c<3;c++){var a=Column(c);if(a.Contains(p)){int r=(p.Y-a.Y+offsets[c])/Row;if(r>=0&&r<Count(c)){col=c;row=r;}return;}}}
        protected override void OnMouseMove(MouseEventArgs e){int c,r;Hit(e.Location,out c,out r);if(c!=hoverCol||r!=hoverRow){hoverCol=c;hoverRow=r;Invalidate();}Cursor=c>=0||Ok.Contains(e.Location)?Cursors.Hand:Cursors.Default;}
        protected override void OnMouseUp(MouseEventArgs e){
            if(Ok.Contains(e.Location)){Finish();return;}
            int c,r;Hit(e.Location,out c,out r);if(c==0)ampm=r;else if(c==1)hour=r+1;else if(c==2)minute=r;Invalidate();
        }
        void Finish(){int h=hour%12+(ampm==1?12:0);if(Done!=null)Done(h.ToString("00")+":"+minute.ToString("00"));}
        protected override void OnMouseWheel(MouseEventArgs e){int c,r;Hit(PointToClient(Cursor.Position),out c,out r);if(c<0)return;int max=Math.Max(0,Count(c)*Row-Column(c).Height);offsets[c]=Math.Max(0,Math.Min(max,offsets[c]-e.Delta/120*Row*2));Invalidate();}
        protected override bool IsInputKey(Keys k){return k==Keys.Enter||base.IsInputKey(k);}
        protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Enter)Finish();}
    }
    // input[type=number] with Chrome's validity messages.
    public sealed class NumberBox : InputBox {
        public int MinValue=1,MaxValue=1440;
        public NumberBox():base(false){Box.KeyPress+=delegate(object sender,KeyPressEventArgs e){if(!char.IsControl(e.KeyChar)&&!char.IsDigit(e.KeyChar))e.Handled=true;};Box.KeyDown+=delegate(object sender,KeyEventArgs e){int v;if(e.KeyCode!=Keys.Up&&e.KeyCode!=Keys.Down)return;if(!int.TryParse(Box.Text,out v))v=MinValue-(e.KeyCode==Keys.Up?1:0);v=Math.Max(MinValue,Math.Min(MaxValue,v+(e.KeyCode==Keys.Up?1:-1)));Box.Text=v.ToString();Box.SelectionStart=Box.Text.Length;e.Handled=true;};}
        public string Validity(){int v;if(Box.Text.Trim()=="")return "이 입력란을 작성하세요.";if(!int.TryParse(Box.Text,out v))return "숫자를 입력하세요.";if(v<MinValue)return "값은 "+MinValue+" 이상이어야 합니다.";if(v>MaxValue)return "값은 "+MaxValue.ToString("N0",CultureInfo.InvariantCulture)+" 이하여야 합니다.";return null;}
    }
    public static class Validation {
        // Chrome's reportValidity() bubble, anchored below the invalid control.
        public static void Report(Control control,string message){
            var tip=new ToolTip{IsBalloon=false,ToolTipIcon=ToolTipIcon.Warning,ToolTipTitle=""};
            control.Focus();tip.Show(message,control,new Point(0,control.Height+2),2400);
            var timer=new Timer{Interval=2600};timer.Tick+=delegate{timer.Dispose();tip.Dispose();};timer.Start();
        }
    }
}
