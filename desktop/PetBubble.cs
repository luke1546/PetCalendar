using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using DesktopPet;

namespace PetCalendarDesktop {
    internal sealed class PetBubble : Form {
        readonly Label body=new Label();
        readonly Timer typing=new Timer{Interval=30};
        int revealed;
        readonly Button open=new Button(),dismiss=new Button(),close=new Button();
        readonly Font textFont=new Font("Segoe UI",10);
        bool alarm,tailBelow=true;
        int tailX,tailSize=10;
        string value="";
        string lookSpecies="cat",lookPalette;
        Color furColor=Color.FromArgb(226,159,92),fillColor=DesktopTheme.Card,edgeColor=DesktopTheme.Line,accentColor=DesktopTheme.Ink;
        PetHost following;
        string layoutValue;
        bool layoutAlarm;
        int layoutDpi,layoutScreenWidth,layoutScreenHeight,bubbleWidth,bubbleHeight,bodyHeight,pad,closeSpace;
        float layoutFontSize;
        internal int FollowMoves,LayoutPasses;
        internal event Action OpenRequested,DismissRequested;
        internal bool Hovered {get {return Visible&&Bounds.Contains(Cursor.Position);}}
        internal string MessageText {get {return value;}}
        internal PetBubble() {
            AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
            FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;ShowInTaskbar=false;Font=textFont;
            SetStyle(ControlStyles.Selectable,false);
            Text="데스크톱 펫 · 말풍선";BackColor=DesktopTheme.Card;ForeColor=DesktopTheme.Ink;DoubleBuffered=true;
            typing.Tick+=delegate {revealed=Math.Min(value.Length,revealed+2);if(revealed<value.Length&&Char.IsHighSurrogate(value[revealed-1]))revealed++;body.Text=value.Substring(0,revealed);if(revealed>=value.Length)typing.Stop();};
            body.UseMnemonic=false;body.AutoEllipsis=true;
            foreach(var button in new[]{open,dismiss,close}) {button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderSize=0;button.BackColor=DesktopTheme.Card;button.ForeColor=DesktopTheme.Ink;button.TabStop=false;}
            open.Text="일정 열기";dismiss.Text="확인";close.Text="×";
            open.Click+=delegate{if(OpenRequested!=null)OpenRequested();};
            dismiss.Click+=delegate{if(DismissRequested!=null)DismissRequested();};
            close.Click+=delegate{if(DismissRequested!=null)DismissRequested();};
            Controls.AddRange(new Control[]{body,open,dismiss,close});
            body.Cursor=Cursors.Hand;body.Click+=delegate{if(OpenRequested!=null)OpenRequested();};
            close.AccessibleName="말풍선 닫기";body.AccessibleName="데스크톱 펫 일정 안내";
        }
        protected override bool ShowWithoutActivation {get{return true;}}
        // Form.TopMost activates the window during CreateHandle on .NET Framework.
        // Set the native style instead so the first announcement also preserves focus.
        protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x80|0x8;return p;}}
        protected override void WndProc(ref Message m){if(m.Msg==0x21){m.Result=new IntPtr(3);return;}base.WndProc(ref m);}
        internal void Present(string text,bool isAlarm,int count,bool hasEvent,bool chatter=false) {
            if(value!=text||!Visible) {value=text;revealed=0;body.Text="";if(Visible)typing.Start();}
            Text=chatter?"데스크톱 펫 · 잡담":"데스크톱 펫 · 말풍선";
            body.AccessibleName=chatter?"데스크톱 펫 잡담":"데스크톱 펫 일정 안내";
            alarm=isAlarm;
            open.Visible=dismiss.Visible=isAlarm;
            dismiss.Text=count>1?"확인 · 다음 "+(count-1)+"개":"확인했어!";
            open.Text=hasEvent?"일정 보기":"캘린더 보기";
            close.AccessibleName=isAlarm?"현재 알림 확인하고 닫기":"일정 안내 닫기";
        }
        internal void Follow(PetHost pet) {
            ApplyLook(pet);
            if(following!=pet){Unfollow();following=pet;pet.FormClosed+=FollowedPetClosed;SimulationLoop.FrameAdvanced+=FollowFrame;SimulationLoop.Wake();}
            FollowCore(pet);
        }
        static Color Mix(Color a,Color b,double amount){return Color.FromArgb((int)Math.Round(a.R+(b.R-a.R)*amount),(int)Math.Round(a.G+(b.G-a.G)*amount),(int)Math.Round(a.B+(b.B-a.B)*amount));}
        void ApplyLook(PetHost pet){
            if(lookSpecies==pet.Kind.Id&&lookPalette==pet.PaletteName)return;
            lookSpecies=pet.Kind.Id;lookPalette=pet.PaletteName;
            var palette=Array.Find(Palette.For(pet.Kind),p=>p.Name==pet.PaletteName);
            furColor=palette==null?Color.Empty:palette.Fur;
            if(furColor.IsEmpty){switch(lookSpecies){
                case "cat":furColor=Color.FromArgb(226,159,92);break;
                case "dog":furColor=Color.FromArgb(239,234,221);break;
                case "redPanda":furColor=Color.FromArgb(185,91,62);break;
                case "capybara":furColor=Color.FromArgb(151,110,78);break;
                case "squirrel":furColor=Color.FromArgb(172,126,78);break;
                case "alpaca":furColor=Color.FromArgb(250,250,246);break;
                default:furColor=Color.FromArgb(145,151,156);break;
            }}
            fillColor=Mix(Color.FromArgb(255,253,248),furColor,.25);
            edgeColor=Mix(Color.FromArgb(178,157,133),furColor,.30);
            accentColor=Mix(Color.FromArgb(105,76,59),furColor,.35);
            BackColor=fillColor;body.ForeColor=Color.FromArgb(67,51,42);
            foreach(var button in new[]{open,dismiss,close}){button.BackColor=fillColor;button.ForeColor=accentColor;button.FlatAppearance.MouseOverBackColor=Mix(fillColor,furColor,.12);button.FlatAppearance.MouseDownBackColor=Mix(fillColor,furColor,.22);}
            layoutValue=null;Invalidate();
        }
        void FollowedPetClosed(object sender,FormClosedEventArgs args){Hide();Unfollow();}
        void Unfollow(){if(following==null)return;following.FormClosed-=FollowedPetClosed;following=null;SimulationLoop.FrameAdvanced-=FollowFrame;}
        void FollowFrame(){
            var pet=following;if(pet==null)return;
            if(pet.IsDisposed||!pet.Visible){Hide();Unfollow();return;}
            try{FollowCore(pet);}catch(Exception error){System.Diagnostics.Trace.TraceError(error.ToString());Hide();Unfollow();}
        }
        bool MeasureLayout(Size area) {
            if(layoutValue==value&&layoutAlarm==alarm&&layoutDpi==DeviceDpi&&layoutScreenWidth==area.Width&&layoutScreenHeight==area.Height&&layoutFontSize==body.Font.SizeInPoints)return false;
            LayoutPasses++;layoutValue=value;layoutAlarm=alarm;layoutDpi=DeviceDpi;layoutScreenWidth=area.Width;layoutScreenHeight=area.Height;layoutFontSize=body.Font.SizeInPoints;
            pad=DesktopTheme.Px(this,alarm?16:12);closeSpace=DesktopTheme.Px(this,24);tailSize=DesktopTheme.Px(this,16);
            int maxWidth=Math.Max(1,Math.Min(DesktopTheme.Px(this,290),area.Width-12));
            var flags=TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix|TextFormatFlags.TextBoxControl;
            var natural=TextRenderer.MeasureText(value,body.Font,new Size(Math.Max(1,maxWidth-pad*2-closeSpace),Int32.MaxValue),flags);
            bubbleWidth=alarm?maxWidth:Math.Min(maxWidth,Math.Max(DesktopTheme.Px(this,80),natural.Width+pad*2+closeSpace));
            int measured=TextRenderer.MeasureText(value,body.Font,new Size(Math.Max(1,bubbleWidth-pad*2-closeSpace),Int32.MaxValue),flags).Height;
            bodyHeight=Math.Max(body.Font.Height,Math.Min(measured+DesktopTheme.Px(this,2),Math.Min(DesktopTheme.Px(this,230),Math.Max(body.Font.Height,area.Height-130))));
            bubbleHeight=(alarm?DesktopTheme.Px(this,62):pad*2)+bodyHeight+tailSize;
            return true;
        }
        void FollowCore(PetHost pet) {
            int centerX=(int)Math.Round(pet.Cat.X);
            var anchor=new Rectangle((int)Math.Round(pet.Cat.X-pet.Cat.Size/2),(int)Math.Round(pet.Cat.Y-pet.Cat.Size*.875),(int)Math.Round(pet.Cat.Size),(int)Math.Round(pet.Cat.Size));
            var area=Screen.FromPoint(new Point(centerX,(int)pet.Cat.Y-1)).WorkingArea;
            bool layout=MeasureLayout(area.Size);
            int width=bubbleWidth,height=bubbleHeight;
            bool below=anchor.Top-height>=area.Top;
            int x=Math.Max(area.Left+6,Math.Min(centerX-width/2,area.Right-width-6));
            int y=below?anchor.Top-height:anchor.Bottom;
            y=Math.Max(area.Top+6,Math.Min(y,area.Bottom-height-6));
            int newTailX=Math.Max(DesktopTheme.Px(this,24),Math.Min(centerX-x,width-DesktopTheme.Px(this,24)));
            bool resize=Size!=new Size(width,height),flip=tailBelow!=below;
            bool shape=layout||resize||flip||tailX!=newTailX;
            tailBelow=below;tailX=newTailX;
            if(!IsHandleCreated||resize)Bounds=new Rectangle(x,y,width,height);
            else if(Left!=x||Top!=y){if(!Native.SetWindowPos(Handle,IntPtr.Zero,x,y,0,0,0x215))throw new System.ComponentModel.Win32Exception();FollowMoves++;}
            if(layout||resize||flip){
                int top=below?0:tailSize;
                close.SetBounds(width-DesktopTheme.Px(this,28),top+DesktopTheme.Px(this,6),DesktopTheme.Px(this,22),DesktopTheme.Px(this,22));
                body.SetBounds(pad,top+pad,Math.Max(1,width-pad*2-closeSpace),bodyHeight);
                int buttonY=top+pad+bodyHeight+DesktopTheme.Px(this,6);
                int buttonWidth=DesktopTheme.Px(this,112),gap=DesktopTheme.Px(this,8);
                open.SetBounds(width-pad-buttonWidth*2-gap,buttonY,buttonWidth,DesktopTheme.Px(this,30));
                dismiss.SetBounds(width-pad-buttonWidth,buttonY,buttonWidth,DesktopTheme.Px(this,30));
            }
            if(shape)Shape();
            if(!Visible){Show();if(revealed<value.Length)typing.Start();}
        }
        RectangleF Card {get{return new RectangleF(1,tailBelow?1:tailSize,Math.Max(1,Width-2),Math.Max(1,Height-tailSize-2));}}
        PointF[] Tail {get {float y=tailBelow?Height-tailSize-2:Card.Top;return new[]{new PointF(tailX-DesktopTheme.Px(this,9),y),new PointF(tailX,tailBelow?Height-1:1),new PointF(tailX+DesktopTheme.Px(this,9),y)};}}
        void Shape(){using(var path=DesktopTheme.Round(Card,DesktopTheme.Px(this,18))) {var next=new Region(path);using(var triangle=new GraphicsPath()){triangle.AddPolygon(Tail);next.Union(triangle);}var old=Region;Region=next;if(old!=null)old.Dispose();}Invalidate();}
        protected override void OnDpiChanged(DpiChangedEventArgs e){base.OnDpiChanged(e);layoutValue=null;Shape();}
        void Paw(Graphics g,float x,float y){float s=DeviceDpi/96f;using(var ink=new SolidBrush(Color.FromArgb(95,accentColor))){g.FillEllipse(ink,x-2.6f*s,y-1.8f*s,5.2f*s,3.8f*s);for(int i=0;i<3;i++)g.FillEllipse(ink,x+(i-1)*3*s-1*s,y-5*s+(i==1?-1*s:0),2.2f*s,2.7f*s);}}
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var fill=new SolidBrush(fillColor))using(var edge=new Pen(edgeColor,DesktopTheme.Px(this,1)))using(var card=DesktopTheme.Round(Card,DesktopTheme.Px(this,18))) {
                g.FillPath(fill,card);g.DrawPath(edge,card);
                g.FillPolygon(fill,Tail);using(var seam=new Pen(fillColor,DesktopTheme.Px(this,3)))g.DrawLine(seam,Tail[0],Tail[2]);g.DrawLines(edge,Tail);
                Paw(g,DesktopTheme.Px(this,15),Card.Bottom-DesktopTheme.Px(this,6));
                if(alarm)using(var alert=new SolidBrush(accentColor))g.FillEllipse(alert,Width-DesktopTheme.Px(this,10),Card.Bottom-DesktopTheme.Px(this,9),DesktopTheme.Px(this,3),DesktopTheme.Px(this,3));
            }
        }
        protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(!Visible){typing.Stop();Unfollow();}}
        protected override void Dispose(bool disposing){if(disposing){Unfollow();typing.Dispose();}base.Dispose(disposing);if(disposing)textFont.Dispose();}
        internal object Inspect(){return new{visible=Visible,text=value,alarm,window=IsHandleCreated?Handle.ToInt64():0,dpi=DeviceDpi,species=lookSpecies,fill=ColorTranslator.ToHtml(fillColor),edge=ColorTranslator.ToHtml(edgeColor),fur=ColorTranslator.ToHtml(furColor),followMoves=FollowMoves,layoutPasses=LayoutPasses,x=Left,y=Top,width=Width,height=Height};}
        internal void ActivateOpen(){open.PerformClick();}
        internal void ActivateDismiss(){dismiss.PerformClick();}
    }
}
