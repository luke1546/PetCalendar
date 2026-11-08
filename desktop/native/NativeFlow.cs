using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PetCalendarDesktop {
    public struct Run {
        public string Text;public int Weight;public Color? Color;public string Family;
        public Run(string text,int weight=400,Color? color=null,string family=null){Text=text;Weight=weight;Color=color;Family=family;}
    }
    // Block layout used by dialog bodies. Items stack vertically and adjacent
    // vertical margins collapse, as block boxes do in the original stylesheet.
    public abstract class FlowItem {
        public float Top,Bottom;public bool Visible=true;public Rectangle Bounds;public string Name;
        public abstract int Measure(FlowPanel p,int width);
        public virtual void Arrange(FlowPanel p,Rectangle r){Bounds=r;}
        public virtual void Position(FlowPanel p,int scroll){}
        public virtual void Paint(FlowPanel p,Graphics g){}
        public virtual void Attach(FlowPanel p){}
        public virtual void SetShown(bool shown){}
        public virtual bool Click(FlowPanel p,Point pt){return false;}
        public virtual bool Hot(Point pt){return false;}
        public FlowItem Margins(float top,float bottom){Top=top;Bottom=bottom;return this;}
        // CSS margin collapsing: largest positive plus most negative margin.
        static float Collapse(float a,float b){return Math.Max(0,Math.Max(a,b))+Math.Min(0,Math.Min(a,b));}
        public static int Stack(FlowPanel p,IList<FlowItem> items,int x,int y,int width,bool arrange){
            float s=p.Sc;int cursor=y;float pending=0;bool first=true;
            foreach(var item in items){
                if(!item.Visible){item.SetShown(false);continue;}
                item.SetShown(true);
                int gap=first?(int)Math.Round(item.Top*s):(int)Math.Round(Collapse(pending,item.Top)*s);
                int h=item.Measure(p,width);
                // An empty block's own margins collapse through it.
                if(h==0&&!(item is ControlItem)){if(arrange)item.Arrange(p,new Rectangle(x,cursor,width,0));pending=Math.Max(Math.Max(pending,item.Top),item.Bottom);if(first){cursor+=0;}continue;}
                cursor+=gap;if(arrange)item.Arrange(p,new Rectangle(x,cursor,width,h));
                cursor+=h;pending=item.Bottom;first=false;
            }
            return cursor-y+(int)Math.Round(pending*s);
        }
    }
    public sealed class TextItem : FlowItem {
        public List<Run> Runs=new List<Run>();public float Css,LineHeight;public Color Color;public bool Center;public float Spacing;public int Weight;public string Family;
        public TextItem(string text,float css,int weight,Color color,float lineHeight=0,string family=null){Set(text);Css=css;Weight=weight;Color=color;LineHeight=lineHeight;Family=family;}
        public TextItem Set(string text){Runs=new List<Run>{new Run(text??"",-1)};return this;}
        public string Text{get{return String.Concat(Runs.Select(r=>r.Text));}}
        Font FontFor(FlowPanel p,Run r){return Ui.Font(p.Sc,Css,r.Weight<0?Weight:r.Weight,r.Family??Family,r.Text);}
        float Line(FlowPanel p){var f=Ui.Font(p.Sc,Css,Weight,Family);return LineHeight>0?(Css+(1+DesktopTheme.TextScale)*4f/3f)*LineHeight*p.Sc:f.GetHeight();} // Chrome line-height:normal equals the font height
        List<List<KeyValuePair<Run,string>>> Lines(FlowPanel p,int width){
            var result=new List<List<KeyValuePair<Run,string>>>();var line=new List<KeyValuePair<Run,string>>();int used=0;
            foreach(var run in Runs){
                var font=FontFor(p,run);var pieces=run.Text.Split('\n');
                for(int k=0;k<pieces.Length;k++){
                    if(k>0){result.Add(line);line=new List<KeyValuePair<Run,string>>();used=0;}
                    string rest=pieces[k];
                    while(rest.Length>0){
                        var wrapped=Ui.Wrap(rest,font,Math.Max(1,width-used));string first=wrapped[0];
                        if(first.Length==0&&used>0){result.Add(line);line=new List<KeyValuePair<Run,string>>();used=0;rest=rest.TrimStart();continue;}
                        if(first.Length==0)first=rest.Substring(0,1);
                        int consumed=rest.IndexOf(first,StringComparison.Ordinal);if(consumed<0)consumed=0;consumed+=first.Length;
                        line.Add(new KeyValuePair<Run,string>(run,first));used+=(int)Math.Ceiling(Ui.Advance(first,font));rest=rest.Substring(Math.Min(rest.Length,consumed));
                        if(rest.Length>0){result.Add(line);line=new List<KeyValuePair<Run,string>>();used=0;rest=rest.TrimStart(' ');}
                        else if(rest.Length==0&&first.EndsWith(" ")){}
                    }
                }
            }
            result.Add(line);return result;
        }
        public override int Measure(FlowPanel p,int width){if(Text.Length==0)return 0;return (int)Math.Round(Lines(p,width).Count*Line(p));}
        public override void Paint(FlowPanel p,Graphics g){
            var lines=Lines(p,Bounds.Width);float lh=Line(p);
            for(int i=0;i<lines.Count;i++){
                // Lines are wrapped with typographic advances, so they are drawn with the same metric
                // (GDI TextRenderer is wider and would overflow the content edge).
                float total=lines[i].Sum(piece=>Ui.Advance(piece.Value,FontFor(p,piece.Key)));float x=Center?Bounds.X+(Bounds.Width-total)/2f:Bounds.X;int y=(int)Math.Round(Bounds.Y+i*lh);
                foreach(var piece in lines[i]){var font=FontFor(p,piece.Key);float w=Ui.Advance(piece.Value,font);var color=piece.Key.Color??Color;
                    if(Spacing!=0){Ui.Spaced(g,piece.Value,font,(int)Math.Round(x),y,(int)Math.Ceiling(lh),color,Spacing*p.Sc);}
                    else{float ty=y+(lh-font.GetHeight())/2f;using(var brush=new SolidBrush(color))g.DrawString(piece.Value,font,brush,x,ty,StringFormat.GenericTypographic);}
                    x+=w;}
            }
        }
    }
    public sealed class ControlItem : FlowItem {
        public readonly Control Control;public bool Stretch=true;public float FixedWidth,FixedHeight;
        public ControlItem(Control control,bool stretch=true){Control=control;Stretch=stretch;}
        public override void Attach(FlowPanel p){if(Control.Parent!=p)p.Controls.Add(Control);Control.GotFocus+=delegate{p.ScrollIntoView(Bounds);};}
        public override void SetShown(bool shown){Control.Visible=shown;}
        public int PreferredHeight(FlowPanel p){
            if(FixedHeight>0)return (int)Math.Round(FixedHeight*p.Sc);
            var input=Control as InputBox;if(input!=null)return input.PreferredHeight();
            var select=Control as SelectField;if(select!=null)return select.PreferredHeight();
            var seg=Control as SegmentField;if(seg!=null)return seg.PreferredHeight();
            var btn=Control as Btn;if(btn!=null)return btn.Outer().Height;
            var hp=Control as HotkeyPreview;if(hp!=null)return hp.Preferred().Height;
            var chip=Control as ChoiceChip;if(chip!=null)return chip.Preferred().Height;
            var check=Control as ThemeCheckBox;if(check!=null)return Math.Max((int)Math.Round(20*p.Sc),Ui.LineHeight(check.Font)+(int)(2*p.Sc));
            return Control.Height;
        }
        public int PreferredWidth(FlowPanel p,int width){
            if(FixedWidth>0)return (int)Math.Round(FixedWidth*p.Sc);if(Stretch)return width;
            var btn=Control as Btn;if(btn!=null)return btn.Outer().Width;
            var hp=Control as HotkeyPreview;if(hp!=null)return hp.Preferred().Width;
            var chip=Control as ChoiceChip;if(chip!=null)return chip.Preferred().Width;
            var check=Control as ThemeCheckBox;if(check!=null)return (int)Math.Round(24*p.Sc)+Ui.Width(check.Text,check.Font)+(int)(4*p.Sc);
            return Control.Width;
        }
        public override int Measure(FlowPanel p,int width){return PreferredHeight(p);}
        public override void Arrange(FlowPanel p,Rectangle r){Bounds=new Rectangle(r.X,r.Y,Math.Min(r.Width,PreferredWidth(p,r.Width)),r.Height);}
        public override void Position(FlowPanel p,int scroll){var r=Bounds;r.Offset(0,-scroll);if(Control.Bounds!=r)Control.Bounds=r;}
    }
    // `.field`: flex column of label text, optional <small>, and the control.
    public sealed class FieldItem : FlowItem {
        public readonly TextItem Label;public readonly TextItem Small;public readonly ControlItem Input;
        public FieldItem(string label,Control control,string small=null){var input=control as InputBox;if(input!=null&&input.Weight==400)input.Weight=700; // .field input/textarea inherit font-weight:700
            Label=new TextItem(label,11,700,DesktopTheme.Ink);Small=small==null?null:new TextItem(small,11,400,Ui.C("#79694f"),1.7f);Input=new ControlItem(control);Bottom=17;}
        IEnumerable<FlowItem> Parts{get{yield return Label;if(Small!=null)yield return Small;yield return Input;}}
        public override void Attach(FlowPanel p){Input.Attach(p);}
        public override void SetShown(bool shown){Input.SetShown(shown);}
        public override int Measure(FlowPanel p,int width){int h=0;int gap=(int)Math.Round(7*p.Sc);foreach(var part in Parts)h+=part.Measure(p,width)+gap;return h-gap;}
        public override void Arrange(FlowPanel p,Rectangle r){Bounds=r;int y=r.Y,gap=(int)Math.Round(7*p.Sc);foreach(var part in Parts){int h=part.Measure(p,r.Width);part.Arrange(p,new Rectangle(r.X,y,r.Width,h));y+=h+gap;}}
        public override void Position(FlowPanel p,int scroll){Input.Position(p,scroll);}
        public override void Paint(FlowPanel p,Graphics g){Label.Paint(p,g);if(Small!=null)Small.Paint(p,g);}
    }
    // CSS grid row with fractional (or fixed) columns.
    public sealed class RowItem : FlowItem {
        public readonly List<FlowItem> Cells=new List<FlowItem>();public float Gap=15;public float[] Columns;public bool AlignEnd;
        public RowItem(float gap,params FlowItem[] cells){Gap=gap;Cells.AddRange(cells);Bottom=0;}
        int[] Widths(FlowPanel p,int width){
            int n=Cells.Count;int gap=(int)Math.Round(Gap*p.Sc);var cols=Columns??Enumerable.Repeat(-1f,n).ToArray();
            var auto=new int[n];for(int i=0;i<n;i++)if(cols[i]==0){var ci=Cells[i] as ControlItem;auto[i]=ci==null?0:ci.PreferredWidth(p,width);}
            int fixedSum=0;float fr=0;for(int i=0;i<n;i++){var c=cols[i];if(c>0)fixedSum+=(int)Math.Round(c*p.Sc);else if(c==0)fixedSum+=auto[i];else fr+=-c;}
            int free=Math.Max(0,width-gap*(n-1)-fixedSum);var result=new int[n];for(int i=0;i<n;i++)result[i]=cols[i]>0?(int)Math.Round(cols[i]*p.Sc):cols[i]==0?auto[i]:(int)(free*(-cols[i])/Math.Max(0.0001f,fr));return result;
        }
        public override void Attach(FlowPanel p){foreach(var c in Cells)c.Attach(p);}
        public override void SetShown(bool shown){foreach(var c in Cells)c.SetShown(shown&&c.Visible);}
        public override int Measure(FlowPanel p,int width){var w=Widths(p,width);int h=0;for(int i=0;i<Cells.Count;i++)if(Cells[i].Visible)h=Math.Max(h,Cells[i].Measure(p,w[i])+(int)Math.Round((Cells[i].Top+Cells[i].Bottom)*p.Sc));return h;}
        public override void Arrange(FlowPanel p,Rectangle r){Bounds=r;var w=Widths(p,r.Width);int x=r.X,gap=(int)Math.Round(Gap*p.Sc);for(int i=0;i<Cells.Count;i++){int h=Cells[i].Measure(p,w[i]);Cells[i].Arrange(p,new Rectangle(x,AlignEnd?r.Bottom-h:r.Y+(int)Math.Round(Cells[i].Top*p.Sc),w[i],h));x+=w[i]+gap;}}
        public override void Position(FlowPanel p,int scroll){foreach(var c in Cells)c.Position(p,scroll);}
        public override void Paint(FlowPanel p,Graphics g){foreach(var c in Cells)if(c.Visible)c.Paint(p,g);}
        public override bool Click(FlowPanel p,Point pt){foreach(var c in Cells)if(c.Visible&&c.Bounds.Contains(pt)&&c.Click(p,pt))return true;return false;}
        public override bool Hot(Point pt){return Cells.Any(c=>c.Visible&&c.Bounds.Contains(pt)&&c.Hot(pt));}
    }
    // Inline flex row of controls that wraps, like `.settings-actions`.
    public sealed class InlineItem : FlowItem {
        public readonly List<ControlItem> Parts=new List<ControlItem>();public float Gap=8,RowGap=-1;public bool Right,SpaceBetween,AlignEnd,SplitFirst;public int Flex=-1;
        public InlineItem(float gap,params Control[] controls){Gap=gap;foreach(var c in controls)Parts.Add(new ControlItem(c,false));}
        public override void Attach(FlowPanel p){foreach(var c in Parts)c.Attach(p);}
        public override void SetShown(bool shown){foreach(var c in Parts)c.SetShown(shown&&c.Control.Tag as string!="hidden");}
        List<List<int>> Lines(FlowPanel p,int width,out int[] widths){
            int gap=(int)Math.Round(Gap*p.Sc);var shown=Parts.Where(c=>c.Control.Tag as string!="hidden").ToList();widths=new int[Parts.Count];
            for(int i=0;i<Parts.Count;i++)widths[i]=Parts[i].PreferredWidth(p,width);
            if(Flex>=0){int others=0;for(int i=0;i<Parts.Count;i++)if(i!=Flex&&Parts[i].Control.Tag as string!="hidden")others+=widths[i]+gap;widths[Flex]=Math.Max((int)(130*p.Sc),width-others);}
            var lines=new List<List<int>>();var line=new List<int>();int used=0;
            for(int i=0;i<Parts.Count;i++){if(Parts[i].Control.Tag as string=="hidden")continue;int w=widths[i];if(line.Count>0&&used+gap+w>width){lines.Add(line);line=new List<int>();used=0;}used+=(line.Count>0?gap:0)+w;line.Add(i);}
            if(line.Count>0)lines.Add(line);return lines;
        }
        public override int Measure(FlowPanel p,int width){int[] w;var lines=Lines(p,width,out w);int rowGap=(int)Math.Round((RowGap<0?Gap:RowGap)*p.Sc);return lines.Sum(l=>l.Max(i=>Parts[i].PreferredHeight(p)))+Math.Max(0,lines.Count-1)*rowGap;}
        public override void Arrange(FlowPanel p,Rectangle r){
            Bounds=r;int[] w;var lines=Lines(p,r.Width,out w);int gap=(int)Math.Round(Gap*p.Sc),rowGap=(int)Math.Round((RowGap<0?Gap:RowGap)*p.Sc),y=r.Y;
            foreach(var line in lines){
                int h=line.Max(i=>Parts[i].PreferredHeight(p)),total=line.Sum(i=>w[i])+gap*(line.Count-1);int x=Right?r.Right-total:r.X;
                int extra=SpaceBetween&&line.Count>1?(r.Width-total)/(line.Count-1):0;
                foreach(int i in line){if(SplitFirst&&i==line[0]&&line.Count>1){int ph0=Parts[i].PreferredHeight(p);Parts[i].Bounds=new Rectangle(x,y+(h-ph0)/2,w[i],ph0);x=r.Right-(total-w[i]-gap);continue;}int ph=Parts[i].PreferredHeight(p);Parts[i].Bounds=new Rectangle(x,AlignEnd?y+h-ph:y+(h-ph)/2,w[i],ph);x+=w[i]+gap+extra;}
                y+=h+rowGap;
            }
        }
        public override void Position(FlowPanel p,int scroll){foreach(var c in Parts)c.Position(p,scroll);}
    }
    public sealed class PaintItem : FlowItem {
        public Func<FlowPanel,int,int> OnMeasure;public Action<FlowPanel,Graphics,Rectangle> OnPaint;public Func<Point,bool> OnClick;public Func<Point,bool> IsHot;
        public override int Measure(FlowPanel p,int width){return OnMeasure==null?0:OnMeasure(p,width);}
        public override void Paint(FlowPanel p,Graphics g){if(OnPaint!=null)OnPaint(p,g,Bounds);}
        public override bool Click(FlowPanel p,Point pt){return OnClick!=null&&OnClick(pt);}
        public override bool Hot(Point pt){return IsHot!=null?IsHot(pt):OnClick!=null;}
    }
    // Bordered block with padding and its own child flow (sections, groups).
    public class BoxItem : FlowItem {
        public readonly List<FlowItem> Children=new List<FlowItem>();public float PadTop,PadRight,PadBottom,PadLeft,Radius;public Color? Fill;public Color? Border;public bool RuleBottom;public bool RuleTop;
        public override void Attach(FlowPanel p){foreach(var c in Children)c.Attach(p);}
        public override void SetShown(bool shown){foreach(var c in Children)c.SetShown(shown&&c.Visible);}
        public BoxItem Add(params FlowItem[] items){Children.AddRange(items);return this;}
        protected virtual IList<FlowItem> Flow{get{return Children;}}
        public override int Measure(FlowPanel p,int width){float s=p.Sc;int inner=width-(int)Math.Round((PadLeft+PadRight)*s);return (int)Math.Round((PadTop+PadBottom)*s)+FlowItem.Stack(p,Flow,0,0,inner,false)+HeadHeight(p,inner);}
        protected virtual int HeadHeight(FlowPanel p,int width){return 0;}
        public override void Arrange(FlowPanel p,Rectangle r){Bounds=r;float s=p.Sc;int inner=r.Width-(int)Math.Round((PadLeft+PadRight)*s);ArrangeHead(p,r,inner);FlowItem.Stack(p,Flow,r.X+(int)Math.Round(PadLeft*s),r.Y+(int)Math.Round(PadTop*s)+HeadHeight(p,inner),inner,true);}
        protected virtual void ArrangeHead(FlowPanel p,Rectangle r,int inner){}
        public override void Position(FlowPanel p,int scroll){foreach(var c in Flow)if(c.Visible)c.Position(p,scroll);}
        public override void Paint(FlowPanel p,Graphics g){
            float s=p.Sc;var r=new RectangleF(Bounds.X,Bounds.Y,Bounds.Width,Bounds.Height);
            if(Fill.HasValue||Border.HasValue)Ui.Box(g,r,Radius*s,Fill??DesktopTheme.Card,Border??(Fill??DesktopTheme.Card),Border.HasValue?s:0);
            if(RuleBottom)using(var pen=new Pen(DesktopTheme.Line,s))g.DrawLine(pen,r.X,r.Bottom-s/2,r.Right,r.Bottom-s/2);
            if(RuleTop)using(var pen=new Pen(DesktopTheme.Line,s))g.DrawLine(pen,r.X,r.Y+s/2,r.Right,r.Y+s/2);
            PaintHead(p,g);foreach(var c in Flow)if(c.Visible)c.Paint(p,g);
        }
        protected virtual void PaintHead(FlowPanel p,Graphics g){}
        public override bool Click(FlowPanel p,Point pt){foreach(var c in Flow)if(c.Visible&&c.Bounds.Contains(pt)&&c.Click(p,pt))return true;return false;}
        public override bool Hot(Point pt){return Flow.Any(c=>c.Visible&&c.Bounds.Contains(pt)&&c.Hot(pt));}
    }
    // <details class="settings-group"> with its disclosure marker.
    public sealed class GroupItem : BoxItem {
        public string Summary;public bool Open;public Action Toggled;Rectangle head;
        public GroupItem(string summary,bool open){Summary=summary;Open=open;PadLeft=PadRight=14;Radius=12;Fill=Ui.C("#fffaf1");Border=DesktopTheme.Line;Bottom=14;}
        protected override IList<FlowItem> Flow{get{return Open?Children:new List<FlowItem>();}}
        public override void SetShown(bool shown){foreach(var c in Children)c.SetShown(shown&&Open&&c.Visible);}
        Font SummaryFont(FlowPanel p){return Ui.Font(p.Sc,13,700);}
        protected override int HeadHeight(FlowPanel p,int width){return (int)Math.Round(SummaryFont(p).GetHeight()+28*p.Sc);}
        protected override void ArrangeHead(FlowPanel p,Rectangle r,int inner){head=new Rectangle(r.X,r.Y,r.Width,HeadHeight(p,inner));}
        public override int Measure(FlowPanel p,int width){SetShown(Visible);return base.Measure(p,width);}
        protected override void PaintHead(FlowPanel p,Graphics g){
            float s=p.Sc;var font=SummaryFont(p);int x=head.X+(int)Math.Round(14*s);
            var tri=new PointF[3];float cx=x+4*s,cy=head.Y+head.Height/2f;
            if(Open){tri[0]=new PointF(cx-4.5f*s,cy-2.5f*s);tri[1]=new PointF(cx+4.5f*s,cy-2.5f*s);tri[2]=new PointF(cx,cy+3.5f*s);}
            else{tri[0]=new PointF(cx-2.5f*s,cy-4.5f*s);tri[1]=new PointF(cx-2.5f*s,cy+4.5f*s);tri[2]=new PointF(cx+3.5f*s,cy);}
            var mode=g.SmoothingMode;Ui.Prepare(g);using(var b=new SolidBrush(Ui.C("#b18e67")))g.FillPolygon(b,tri);g.SmoothingMode=mode;
            Ui.Text(g,Summary,font,new Rectangle(x+(int)(15*s),head.Y,head.Width,head.Height),Ui.C("#665540"),TextFormatFlags.VerticalCenter);
        }
        public override bool Click(FlowPanel p,Point pt){if(head.Contains(pt)){Open=!Open;if(Toggled!=null)Toggled();p.Reflow();return true;}return base.Click(p,pt);}
        public override bool Hot(Point pt){return head.Contains(pt)||base.Hot(pt);}
    }
    public sealed class RuleItem : FlowItem {
        public override int Measure(FlowPanel p,int width){return Math.Max(1,(int)Math.Round(p.Sc));}
        public override void Paint(FlowPanel p,Graphics g){using(var b=new SolidBrush(DesktopTheme.Line))g.FillRectangle(b,Bounds);}
    }
    public class FlowPanel : Control {
        public readonly List<FlowItem> Items=new List<FlowItem>();public readonly ScrollModel Scroll=new ScrollModel();
        public float PadTop,PadRight=25,PadBottom,PadLeft=25;public int ContentHeight;bool reflowing;
        public float Sc{get{return DeviceDpi/96f;}}
        public FlowPanel(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);BackColor=DesktopTheme.Card;}
        public T Add<T>(T item) where T:FlowItem{Items.Add(item);item.Attach(this);return item;}
        public int MeasureContent(int width){float s=Sc;int inner=width-(int)Math.Round((PadLeft+PadRight)*s);return (int)Math.Round((PadTop+PadBottom)*s)+FlowItem.Stack(this,Items,0,0,inner,false);}
        public void Reflow(){
            if(reflowing||Width<=0)return;reflowing=true;SuspendLayout();
            try{
                float s=Sc;int width=Width;int content=MeasureContent(width);
                if(content>Height+1){width-=(int)Math.Round(12*s);content=MeasureContent(width);}
                int inner=width-(int)Math.Round((PadLeft+PadRight)*s);FlowItem.Stack(this,Items,(int)Math.Round(PadLeft*s),(int)Math.Round(PadTop*s),inner,true);
                ContentHeight=content;Scroll.Content=content;Scroll.Viewport=Height;Scroll.Bounds=ClientRectangle;Scroll.Clamp();
                foreach(var item in Items)if(item.Visible)item.Position(this,Scroll.Offset);
            }finally{ResumeLayout(false);reflowing=false;}
            Invalidate();
        }
        public void ScrollIntoView(Rectangle r){if(r.Top<Scroll.Offset)Scroll.Offset=r.Top-(int)(8*Sc);else if(r.Bottom>Scroll.Offset+Height)Scroll.Offset=r.Bottom-Height+(int)(8*Sc);else return;Scroll.Clamp();Moved();}
        void Moved(){SuspendLayout();foreach(var item in Items)if(item.Visible)item.Position(this,Scroll.Offset);ResumeLayout(false);Invalidate(true);Update();}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);Reflow();}
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;g.Clear(BackColor);Ui.Prepare(g);
            var state=g.Save();g.TranslateTransform(0,-Scroll.Offset);foreach(var item in Items)if(item.Visible)item.Paint(this,g);g.Restore(state);
            Ui.ScrollBar(g,Scroll,Sc);
        }
        Point Content(Point p){return new Point(p.X,p.Y+Scroll.Offset);}
        protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);if(Scroll.Wheel(e.Delta,Sc))Moved();}
        protected override void OnMouseDown(MouseEventArgs e){
            base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;
            if(Scroll.Visible&&Scroll.Track(Sc).Contains(e.Location)){var thumb=Scroll.Thumb(Sc);if(thumb.Contains(e.Location)){Scroll.Dragging=true;Scroll.DragStart=e.Y;Scroll.DragOffset=Scroll.Offset;Capture=true;}else{Scroll.Offset+=e.Y<thumb.Top?-Height:Height;Scroll.Clamp();Moved();}return;}
            var pt=Content(e.Location);foreach(var item in Items)if(item.Visible&&item.Bounds.Contains(pt)&&item.Click(this,pt))return;
        }
        protected override void OnMouseMove(MouseEventArgs e){
            base.OnMouseMove(e);if(Scroll.Dragging){Scroll.DragTo(e.Y,Sc);Moved();return;}
            bool hover=Scroll.Visible&&Scroll.Track(Sc).Contains(e.Location);if(hover!=Scroll.Hover){Scroll.Hover=hover;Invalidate();}
            var pt=Content(e.Location);Cursor=Items.Any(i=>i.Visible&&i.Bounds.Contains(pt)&&i.Hot(pt))?Cursors.Hand:Cursors.Default;
        }
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(Scroll.Dragging){Scroll.Dragging=false;Capture=false;Invalidate();}}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);if(Scroll.Hover){Scroll.Hover=false;Invalidate();}}
        protected override void OnControlAdded(ControlEventArgs e){base.OnControlAdded(e);Hook(e.Control);}
        void Hook(Control c){c.MouseWheel+=delegate(object sender,MouseEventArgs e){var h=e as HandledMouseEventArgs;if(sender is SelectField||(h!=null&&h.Handled))return;if(Scroll.Wheel(e.Delta,Sc))Moved();if(h!=null)h.Handled=true;};foreach(Control child in c.Controls)Hook(child);}
    }
    // ::backdrop over the calendar area while a dialog is open:
    // background:#29241e75; backdrop-filter:blur(3px). With a source view the
    // calendar is snapshotted, blurred and tinted; without one (stacked dialogs)
    // it falls back to a plain translucent tint.
    public sealed class Backdrop : Form {
        readonly Control source;Bitmap image;
        public Backdrop(Control blurSource=null){
            source=blurSource;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;BackColor=Ui.C("#29241e");
            SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint,true);
            if(source==null||!Recapture())Opacity=0.46;
        }
        // Moving the window only moves the snapshot; a new size needs a new one.
        public void Place(Rectangle r){bool resized=Size!=r.Size;Bounds=r;if(resized&&image!=null)Recapture();}
        public bool Recapture(){
            if(source==null||source.IsDisposed||source.Width<=0||source.Height<=0)return false;
            try{
                var shot=new Bitmap(source.Width,source.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                source.DrawToBitmap(shot,new Rectangle(0,0,shot.Width,shot.Height));
                Blur(shot,(int)Math.Round(3*source.DeviceDpi/96f));
                using(var g=Graphics.FromImage(shot))using(var tint=new SolidBrush(Color.FromArgb(0x75,0x29,0x24,0x1e)))g.FillRectangle(tint,0,0,shot.Width,shot.Height);
                if(image!=null)image.Dispose();image=shot;Invalidate();return true;
            }catch{return false;}
        }
        // Three box-blur passes per axis approximate a Gaussian with sigma ~= radius.
        static void Blur(Bitmap bmp,int sigma){
            if(sigma<1)return;int w=bmp.Width,h=bmp.Height;
            var data=bmp.LockBits(new Rectangle(0,0,w,h),System.Drawing.Imaging.ImageLockMode.ReadWrite,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var px=new int[w*h];Marshal.Copy(data.Scan0,px,0,px.Length);
            int r=Math.Max(1,(int)Math.Round(Math.Sqrt(4.0*sigma*sigma+1)/2-0.5));var tmp=new int[px.Length];
            for(int pass=0;pass<3;pass++){BoxPass(px,tmp,w,h,r,true);BoxPass(tmp,px,w,h,r,false);}
            Marshal.Copy(px,0,data.Scan0,px.Length);bmp.UnlockBits(data);
        }
        static void BoxPass(int[] src,int[] dst,int w,int h,int r,bool horizontal){
            int lines=horizontal?h:w,len=horizontal?w:h,step=horizontal?1:w,div=2*r+1;
            for(int l=0;l<lines;l++){
                int start=horizontal?l*w:l;int sr=0,sg=0,sb=0;
                for(int i=-r;i<=r;i++){int c=src[start+Math.Min(len-1,Math.Max(0,i))*step];sr+=(c>>16)&255;sg+=(c>>8)&255;sb+=c&255;}
                for(int i=0;i<len;i++){
                    dst[start+i*step]=unchecked((int)0xff000000)|((sr/div)<<16)|((sg/div)<<8)|(sb/div);
                    int a=src[start+Math.Max(0,i-r)*step],b=src[start+Math.Min(len-1,i+r+1)*step];
                    sr+=((b>>16)&255)-((a>>16)&255);sg+=((b>>8)&255)-((a>>8)&255);sb+=(b&255)-(a&255);
                }
            }
        }
        protected override void OnPaintBackground(PaintEventArgs e){if(image==null)base.OnPaintBackground(e);}
        protected override void OnPaint(PaintEventArgs e){if(image!=null)e.Graphics.DrawImageUnscaled(image,0,0);}
        protected override void Dispose(bool disposing){if(disposing&&image!=null){image.Dispose();image=null;}base.Dispose(disposing);}
        // Form.Show(owner) refuses disabled forms, so disable once shown.
        protected override void OnShown(EventArgs e){base.OnShown(e);Enabled=false;}
        protected override bool ShowWithoutActivation{get{return true;}}
        protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x80;return p;}}
    }
    // <dialog> shell: sticky header, scrolling body, sticky footer.
    public class DialogForm : Form {
        public static readonly List<DialogForm> Open=new List<DialogForm>();
        protected readonly CalendarView View;protected readonly FlowPanel Body=new FlowPanel();
        public string Eyebrow,Heading;public float WidthCss=510,HeaderGap=20;public bool HasFooter;public bool Sticky=true;
        protected readonly List<Control> FooterLeft=new List<Control>(),FooterRight=new List<Control>();readonly HashSet<Control> hiddenFooter=new HashSet<Control>();
        public void SetFooterVisible(Control c,bool visible){if(visible)hiddenFooter.Remove(c);else hiddenFooter.Add(c);c.Visible=visible;if(IsHandleCreated)Relayout();}protected Control HeaderAction;
        protected Btn CloseButton;Backdrop backdrop;bool closing;
        public event Action Closed2;
        public DialogForm(CalendarView view,string heading,string eyebrow=null,bool closeButton=true){
            View=view;Heading=heading;Eyebrow=eyebrow;AutoScaleMode=AutoScaleMode.None;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;KeyPreview=true;
            BackColor=DesktopTheme.Card;Text=heading??eyebrow??"";DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);
            Controls.Add(Body);
            if(closeButton){CloseButton=new Btn(BtnKind.Close,"","close"){Surface=DesktopTheme.Card,AccessibleName="닫기"};CloseButton.Click+=delegate{Cancel();};HeaderAction=CloseButton;Controls.Add(CloseButton);}
        }
        protected float Sc{get{return DeviceDpi/96f;}}
        protected void SetHeaderAction(Control c){if(HeaderAction!=null)Controls.Remove(HeaderAction);HeaderAction=c;Controls.Add(c);}
        protected void Footer(Control[] left,Control[] right){HasFooter=true;FooterLeft.AddRange(left);FooterRight.AddRange(right);foreach(var c in left.Concat(right)){var b=c as Btn;if(b!=null)b.Surface=DesktopTheme.Card;Controls.Add(c);}}
        int HeaderHeight{get{float s=Sc;int text=0;if(Eyebrow!=null)text+=(int)Math.Round(Ui.Font(s,8,700).GetHeight()+5*s);if(Heading!=null)text+=(int)Math.Round(Ui.Font(s,23,700).GetHeight());return Math.Max((int)Math.Round(74*s),text+(int)Math.Round(32*s)+(int)s);}}
        int FooterHeight{get{if(!HasFooter)return 0;float s=Sc;int h=FooterLeft.Concat(FooterRight).Where(c=>!hiddenFooter.Contains(c)).Select(c=>c is Btn?((Btn)c).CssSize().Height:c.Height).DefaultIfEmpty(0).Max();return h+(int)Math.Round(28*s)+(int)s;}}
        public virtual void Relayout(){
            if(View==null||View.IsDisposed)return;float s=Sc;var area=View.ViewportScreen();int shadow=(int)Math.Ceiling(3*s);
            int width=(int)Math.Round(Math.Min(WidthCss*s,area.Width-32*s));
            Body.PadTop=HeaderGap;Body.PadBottom=HasFooter?20:25;
            int header=HeaderHeight,footer=FooterHeight,content=Body.MeasureContent(width);
            int max=(int)Math.Round(area.Height-50*s);int height=Math.Min(max,header+content+footer);
            if(height<header+content+footer){content=Body.MeasureContent(width-(int)Math.Round(12*s));height=Math.Min(max,header+content+footer);}
            Bounds=new Rectangle(area.X+(area.Width-width)/2,area.Y+(area.Height-height)/2,width+shadow,height+shadow);
            int bodyH=Math.Max(1,height-header-footer);Body.SetBounds((int)Math.Round(s),header,width-2*(int)Math.Round(s),HasFooter?bodyH:bodyH-(int)Math.Round(s));Body.PadLeft=Body.PadRight=24;
            // Without a footer the body reaches the rounded bottom edge.
            if(!HasFooter){using(var clip=Ui.Corners(new RectangleF(0,0,Body.Width,Body.Height),0,0,17*s,17*s))Body.Region=new Region(clip);}else Body.Region=null;
            Body.Reflow();
            if(HeaderAction!=null){var b=HeaderAction as Btn;var size=b!=null?b.Outer():HeaderAction.Size;HeaderAction.SetBounds(width-(int)Math.Round(25*s)-size.Width,(header-size.Height)/2,size.Width,size.Height);}
            if(HasFooter){int y=height-footer+(int)Math.Round(14*s)+(int)s,gap=(int)Math.Round(6*s),x=(int)Math.Round(25*s);
                foreach(var c in FooterLeft){if(hiddenFooter.Contains(c))continue;var o=((Btn)c).Outer();c.SetBounds(x,y,o.Width,o.Height);x+=o.Width+gap;}
                x=width-(int)Math.Round(25*s);foreach(var c in FooterRight.AsEnumerable().Reverse()){if(hiddenFooter.Contains(c))continue;var o=((Btn)c).Outer();x-=o.Width;c.SetBounds(x,y,o.Width,o.Height);x-=gap;}}
            using(var shape=Shape())Region=new Region(shape);Invalidate();
        }
        GraphicsPath Shape(){float s=Sc;int shadow=(int)Math.Ceiling(3*s);var p=new GraphicsPath();p.AddPath(Ui.Round(new RectangleF(0,0,Width-shadow,Height-shadow),18*s),false);p.AddPath(Ui.Round(new RectangleF(shadow,shadow,Width-shadow,Height-shadow),18*s),false);p.FillMode=FillMode.Winding;return p;}
        protected override void OnPaintBackground(PaintEventArgs e){
            var g=e.Graphics;float s=Sc;int shadow=(int)Math.Ceiling(3*s);Ui.Prepare(g);g.Clear(Ui.C("#b9a98e"));
            var box=new RectangleF(0,0,Width-shadow,Height-shadow);
            using(var under=new SolidBrush(View==null?DesktopTheme.Paper:Ui.Mix(DesktopTheme.Paper,Ui.C("#29241e"),.46f)))g.FillRectangle(under,0,0,Width,Height);
            Ui.Fill(g,new RectangleF(shadow,shadow,box.Width,box.Height),18*s,Ui.C("#b9a98e"));
            Ui.Box(g,box,18*s,DesktopTheme.Card,DesktopTheme.Frame,s);
            int header=HeaderHeight,x=(int)Math.Round(25*s);
            using(var b=new SolidBrush(DesktopTheme.Line))g.FillRectangle(b,s,header-s,box.Width-2*s,s);
            if(HasFooter){int footer=FooterHeight;using(var b=new SolidBrush(DesktopTheme.Line))g.FillRectangle(b,s,box.Height-footer,box.Width-2*s,s);}
            int textH=Heading==null?0:(int)Math.Round(Ui.Font(s,23,700).GetHeight()),eyebrowH=Eyebrow!=null?(int)Math.Round(Ui.Font(s,8,700).GetHeight()):0,gap=Eyebrow!=null&&Heading!=null?(int)Math.Round(5*s):0;
            int y=(header-(textH+eyebrowH+gap))/2;int titleWidth=(int)box.Width-x*2-(HeaderAction!=null?HeaderAction.Width+(int)(16*s):0);
            if(Eyebrow!=null){Ui.Spaced(g,Eyebrow,Ui.Font(s,8,700,null,Eyebrow),x,y,eyebrowH,Ui.C("#786b59"),1.6f*s);y+=eyebrowH+gap;}
            if(Heading!=null){var titleFont=Ui.Font(s,23,700,null,Heading);Ui.Spaced(g,Heading,titleFont,x,y,textH,DesktopTheme.Ink,-0.7f*s);}
            PaintChrome(g);
        }
        protected virtual void PaintChrome(Graphics g){}
        public virtual void StateChanged(){}
        public virtual void SavingChanged(){}
        public void Show(Control focus){
            Open.Add(this);var owner=View.FindForm();backdrop=new Backdrop(Open.Count==1?View:null);backdrop.Bounds=View.ViewportScreen();backdrop.Show(Open.Count>1?(IWin32Window)Open[Open.Count-2]:owner);
            Relayout();base.Show(backdrop);View.SetModal(true);Activate();if(focus!=null)focus.Focus();else if(CloseButton!=null)Body.Focus();
            Toasts.Raise();
        }
        protected override bool ProcessDialogKey(Keys keyData){if(keyData==Keys.Escape){Cancel();return true;}return base.ProcessDialogKey(keyData);}
        protected override void OnDpiChanged(DpiChangedEventArgs e){base.OnDpiChanged(e);BeginInvoke(new Action(Relayout));}
        public virtual void Cancel(){if(View.Saving)return;CloseDialog();}
        public static void SetAllVisible(bool visible){foreach(var d in Open.ToArray()){if(d.backdrop!=null)d.backdrop.Visible=visible;d.Visible=visible;}if(visible&&Open.Count>0)Open[Open.Count-1].Activate();}
        public static void RelayoutAll(){foreach(var d in Open.ToArray()){if(d.backdrop!=null&&!d.View.IsDisposed)d.backdrop.Place(d.View.ViewportScreen());d.Relayout();}}
        public void CloseDialog(){
            if(closing)return;closing=true;Open.Remove(this);
            // Hand activation back before hiding so Windows does not pick another app.
            if(Open.Count>0)Open[Open.Count-1].Activate();else{var host=View.FindForm();if(host!=null&&host.Visible)host.Activate();}
            Toasts.Raise();
            Hide();if(backdrop!=null){backdrop.Close();backdrop.Dispose();backdrop=null;}
            if(Open.Count==0)View.SetModal(false);
            if(Closed2!=null)Closed2();Close();Dispose();
        }
        protected override void OnFormClosing(FormClosingEventArgs e){if(!closing){e.Cancel=true;BeginInvoke(new Action(Cancel));return;}base.OnFormClosing(e);}
        protected override void OnDeactivate(EventArgs e){base.OnDeactivate(e);}
    }
    // Toast notifications in the bottom-right corner of the calendar area.
    public static class Toasts {
        static readonly List<ToastWindow> active=new List<ToastWindow>();static CalendarView view;
        public static void Attach(CalendarView owner){view=owner;}
        public static void Show(string title,string detail,string kind,int duration,string action=null,Func<Task<bool>> onAction=null,string restored=null){
            if(view==null||view.IsDisposed)return;
            var toast=new ToastWindow(title,detail,kind,action,onAction,restored);active.Add(toast);toast.FormClosed+=delegate{active.Remove(toast);Layout();};
            Layout();if(view.FindForm().Visible)toast.Show(view.FindForm());var timer=new Timer{Interval=duration};toast.Timer=timer;timer.Tick+=delegate{timer.Stop();timer.Dispose();toast.Close();};timer.Start();
        }
        public static void Layout(){
            if(view==null||view.IsDisposed)return;float s=view.DeviceDpi/96f;var area=view.ViewportScreen();int bottom=area.Bottom-(int)Math.Round(22*s),right=area.Right-(int)Math.Round(20*s);
            int width=active.Select(t=>t.PreferredWidth(area.Width-(int)(40*s))).DefaultIfEmpty(0).Max();
            for(int i=active.Count-1;i>=0;i--){var t=active[i];int h=t.PreferredHeight(width);bottom-=h;t.Bounds=new Rectangle(right-width,bottom,width+(int)Math.Ceiling(2*s),h+(int)Math.Ceiling(2*s));t.Invalidate();bottom-=(int)Math.Round(9*s);}
        }
        public static void Raise(){if(view==null)return;var owner=DialogForm.Open.Count>0?(Form)DialogForm.Open[DialogForm.Open.Count-1]:view.FindForm();foreach(var t in active.ToArray()){if(t.IsDisposed)continue;t.Owner=owner;if(t.Visible)t.BringToFront();}}
        public static void SetVisible(bool visible){foreach(var t in active.ToArray()){if(visible){if(!t.Visible)t.Show(view.FindForm());}else t.Hide();}Layout();}
        public static int Count{get{return active.Count;}}
    }
    public sealed class ToastWindow : Form {
        readonly string title,detail,kind,action,restored;readonly Func<Task<bool>> onAction;public Timer Timer;bool hoverAction,busy;
        public ToastWindow(string title,string detail,string kind,string action,Func<Task<bool>> onAction,string restored){
            this.title=title;this.detail=detail??"";this.kind=kind;this.action=action;this.onAction=onAction;this.restored=restored;
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;DoubleBuffered=true;AutoScaleMode=AutoScaleMode.None;
        }
        protected override bool ShowWithoutActivation{get{return true;}}
        protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x80;return p;}}
        float Sc{get{return DeviceDpi/96f;}}
        Font TitleFont{get{return Ui.Font(Sc,12,700,null,title);}}
        Font DetailFont{get{return Ui.Font(Sc,11,400,null,detail);}}
        Font ActionFont{get{return Ui.Font(Sc,17,400,null,action??"×");}}
        int ActionWidth{get{return Ui.Width(action??"×",ActionFont);}}
        public int PreferredWidth(int max){float s=Sc;int text=Math.Max(Ui.Width(title,TitleFont),detail.Split('\n').Select(l=>Ui.Width(l,DetailFont)).DefaultIfEmpty(0).Max());int w=(int)Math.Round(17*2*s+3*s+20*s+12*s*2)+text+ActionWidth;return Math.Min(Math.Min((int)Math.Round(390*s),max),w);}
        int TextWidth(int width){float s=Sc;return width-(int)Math.Round(17*2*s+3*s+20*s+12*s*2)-ActionWidth;}
        public int PreferredHeight(int width){float s=Sc;int h=(int)Math.Round(TitleFont.GetHeight()*1.15f);if(detail.Length>0)h+=(int)Math.Round(4*s)+Ui.Paragraph(null,detail,DetailFont,0,0,TextWidth(width),(11+(1+DesktopTheme.TextScale)*4f/3f)*1.7f*s,Color.Black,true);return Math.Max(h,(int)Math.Round(20*s))+(int)Math.Round(28*s+3*s);}
        Rectangle ActionRect{get{float s=Sc;int shadow=(int)Math.Ceiling(2*s);return new Rectangle(Width-shadow-(int)Math.Round(17*s+1.5f*s)-ActionWidth,(int)Math.Round(12*s),ActionWidth,(int)Math.Round(ActionFont.GetHeight()));}}
        protected override void OnDpiChanged(DpiChangedEventArgs e){base.OnDpiChanged(e);BeginInvoke(new Action(Toasts.Layout));}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);float s=Sc;int shadow=(int)Math.Ceiling(2*s);var p=new GraphicsPath();p.AddPath(Ui.Round(new RectangleF(0,0,Width-shadow,Height-shadow),12*s),false);p.AddPath(Ui.Round(new RectangleF(shadow,shadow,Width-shadow,Height-shadow),12*s),false);p.FillMode=FillMode.Winding;Region=new Region(p);p.Dispose();}
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;float s=Sc;int shadow=(int)Math.Ceiling(2*s);Ui.Prepare(g);g.Clear(DesktopTheme.Paper);
            var box=new RectangleF(0,0,Width-shadow,Height-shadow);Ui.Fill(g,new RectangleF(shadow,shadow,box.Width,box.Height),12*s,DesktopTheme.Frame);
            Color fill=kind=="error"?Ui.C("#ffd5ca"):kind=="info"?DesktopTheme.Card:Ui.C("#ffdc72");Ui.Box(g,box,12*s,fill,DesktopTheme.Frame,1.5f*s);
            int x=(int)Math.Round(17*s+1.5f*s),y=(int)Math.Round(14*s+1.5f*s);
            Ui.Icon(g,kind=="error"?"flag":"bell",new RectangleF(x,y,20*s,20*s),DesktopTheme.Ink);x+=(int)Math.Round(32*s);
            int tw=TextWidth(Width-shadow);int th=(int)Math.Round(TitleFont.GetHeight()*1.15f);Ui.Text(g,title,TitleFont,new Rectangle(x,y,tw,th),DesktopTheme.Ink,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            if(detail.Length>0)Ui.Paragraph(g,detail,DetailFont,x,y+th+(int)Math.Round(4*s),tw,(11+(1+DesktopTheme.TextScale)*4f/3f)*1.7f*s,DesktopTheme.Ink);
            Ui.Text(g,action??"×",ActionFont,ActionRect,busy?DesktopTheme.Muted:hoverAction?Ui.C("#bf4722"):DesktopTheme.Ink,TextFormatFlags.Default);
        }
        protected override void WndProc(ref Message m){
            if(m.Msg==0x0084){long packed=m.LParam.ToInt64();var p=PointToClient(new Point((short)(packed&0xffff),(short)((packed>>16)&0xffff)));var r=ActionRect;r.Inflate((int)(4*Sc),(int)(4*Sc));m.Result=r.Contains(p)?new IntPtr(1):new IntPtr(-1);return;}
            base.WndProc(ref m);
        }
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);bool h=ActionRect.Contains(e.Location);if(h!=hoverAction){hoverAction=h;Cursor=h?Cursors.Hand:Cursors.Default;Invalidate();}}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hoverAction=false;Invalidate();}
        protected override async void OnMouseClick(MouseEventArgs e){
            base.OnMouseClick(e);var r=ActionRect;r.Inflate((int)(4*Sc),(int)(4*Sc));if(!r.Contains(e.Location)||busy)return;
            if(onAction==null){Close();return;}
            busy=true;Invalidate();bool ok=false;try{ok=await onAction();}catch{}
            if(IsDisposed)return;busy=false;Invalidate();
            if(ok){if(Timer!=null)Timer.Dispose();Close();Toasts.Show(restored??"삭제를 되돌렸어요",detail,"info",6000);}
        }
    }
}
