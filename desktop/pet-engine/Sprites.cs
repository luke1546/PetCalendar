using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;

namespace DesktopPet {
    internal sealed class FrameInfo {
        public double x {get;set;} public double top {get;set;} public double bottom {get;set;} public double clipBottom {get;set;}
        public double? clipX {get;set;} public double? clipWidth {get;set;}
    }
    internal sealed class AtlasInfo {public double cell {get;set;} public FrameInfo[] frames {get;set;}}
    internal sealed class Sprites : IDisposable {
        internal static Func<string,Stream> ResourceOpen=name=>Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        sealed class Source {public Bitmap Atlas;public AtlasInfo Info;public int References;public string Asset;}
        sealed class Shared {public Source Source;public Palette Palette;public Species Species;public AtlasInfo Info;public double Cell;public int References;}
        sealed class CachedFrame {public IntPtr Handle;public LinkedListNode<string> Node;public int Bytes;public string Asset;}
        static readonly Dictionary<string,Source> sources=new Dictionary<string,Source>();
        static readonly Dictionary<string,Shared> shared=new Dictionary<string,Shared>();
        static readonly Dictionary<string,CachedFrame> frames=new Dictionary<string,CachedFrame>();
        static readonly LinkedList<string> recency=new LinkedList<string>();
        public const int FrameLimit=512;
        static int cachedBytes;
        readonly Shared data;
        readonly string key;
        bool disposed;
        public int PaintCount;
        public static int CachedFrames {get {return frames.Count;}}
        public static int CachedBytes {get {return cachedBytes;}}
        public static int SourceCount {get {return sources.Count;}}
        public Sprites() : this(Species.Cat,Palette.All[0]) {}
        public Sprites(Palette palette) : this(Species.Cat,palette) {}
        public Sprites(Species species,Palette palette) {
            string asset=palette.Variant??species.Asset;key=asset+":"+palette.Name;
            if(!shared.TryGetValue(key,out data)) {
                Source source;
                if(!sources.TryGetValue(asset,out source)) {
                    source=new Source {Asset=asset};
                    using(var stream=ResourceOpen(asset+"-atlas.png"))using(var bitmap=new Bitmap(stream))
                        {
                        source.Atlas=new Bitmap(bitmap.Width,bitmap.Height,PixelFormat.Format32bppArgb);
                        using(var g=Graphics.FromImage(source.Atlas)) {g.CompositingMode=CompositingMode.SourceCopy;g.DrawImageUnscaled(bitmap,0,0);}
                    }
                    using(var reader=new StreamReader(ResourceOpen(asset+"-frames.json")))source.Info=new JavaScriptSerializer().Deserialize<AtlasInfo>(reader.ReadToEnd());
                    if(source.Info.frames.Length!=64 || source.Info.cell<=0)throw new InvalidDataException("Invalid sprite metadata: "+asset);
                    sources.Add(asset,source);
                }
                data=new Shared {Source=source,Info=source.Info,Palette=palette,Species=species};
                source.References++;
                data.Cell=source.Info.cell;
                foreach(var f in source.Info.frames) {
                    double center=f.x+source.Info.cell/2;
                    double left=f.clipX??f.x,right=left+(f.clipWidth??source.Info.cell);
                    data.Cell=Math.Max(data.Cell,Math.Max(2*Math.Max(center-left,right-center),(f.bottom-f.top+2)/.875));
                    data.Cell=Math.Max(data.Cell,(f.clipBottom-f.bottom+1)/.125);
                }
                shared.Add(key,data);
            }
            data.References++;
        }
        internal Bitmap Render(int size,int index,int direction) {
            var bitmap=new Bitmap(size,size,PixelFormat.Format32bppPArgb);
            var frame=data.Info.frames[index];float ratio=(float)(size/data.Cell);
            using(var g=Graphics.FromImage(bitmap)) {
                g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;g.CompositingMode=CompositingMode.SourceCopy;
                float offsetY=(float)(frame.bottom-data.Cell*.875);
                double viewX=frame.x-(data.Cell-data.Info.cell)/2;
                float clipX=(float)((frame.clipX??frame.x)-viewX)*ratio;
                g.SetClip(new RectangleF(clipX,(float)(frame.top-offsetY)*ratio,(float)(frame.clipWidth??data.Info.cell)*ratio,(float)(frame.clipBottom-frame.top)*ratio));
                var atlas=data.Source.Atlas;
                g.DrawImage(atlas,new RectangleF((float)-viewX*ratio,-offsetY*ratio,atlas.Width*ratio,atlas.Height*ratio),new RectangleF(0,0,atlas.Width,atlas.Height),GraphicsUnit.Pixel);
            }
            // Tint the small rendered pose, with explicit straight/premultiplied alpha conversion.
            data.Palette.Apply(bitmap,data.Species);
            if(direction<0)bitmap.RotateFlip(RotateFlipType.RotateNoneFlipX);
            return bitmap;
        }
        public void Paint(IntPtr window,Motion cat) {
            int size=(int)Math.Round(cat.Size);string frameKey=key+":"+size+":"+cat.Frame+":"+cat.Direction;
            CachedFrame image;
            if(!frames.TryGetValue(frameKey,out image)) {
                image=new CachedFrame {Bytes=size*size*4,Asset=key};
                using(var bitmap=Render(size,cat.Frame,cat.Direction))image.Handle=bitmap.GetHbitmap(Color.FromArgb(0));
                image.Node=recency.AddLast(frameKey);frames.Add(frameKey,image);cachedBytes+=image.Bytes;
            }
            recency.Remove(image.Node);recency.AddLast(image.Node);
            IntPtr screen=Native.GetDC(IntPtr.Zero),dc=Native.CreateCompatibleDC(screen),old=Native.SelectObject(dc,image.Handle);
            try {
                var pos=new Native.POINT((int)Math.Round(cat.X-size/2.0),(int)Math.Round(cat.Y-size*.875));var dimensions=new Native.SIZE(size,size);var origin=new Native.POINT(0,0);
                var blend=new Native.BLEND {Op=0,Flags=0,Alpha=255,Format=1};
                if(!Native.UpdateLayeredWindow(window,screen,ref pos,ref dimensions,dc,ref origin,0,ref blend,2))throw new System.ComponentModel.Win32Exception();
                PaintCount++;
            }finally {Native.SelectObject(dc,old);Native.DeleteDC(dc);Native.ReleaseDC(IntPtr.Zero,screen);}
            while(frames.Count>FrameLimit || cachedBytes>32*1024*1024)RemoveFrame(recency.First.Value);
        }
        static void RemoveFrame(string id) {
            var entry=frames[id];Native.DeleteObject(entry.Handle);cachedBytes-=entry.Bytes;recency.Remove(entry.Node);frames.Remove(id);
        }
        public void Dispose() {
            if(disposed)return;disposed=true;
            if(--data.References==0) {
                shared.Remove(key);
                if(--data.Source.References==0) {data.Source.Atlas.Dispose();sources.Remove(data.Source.Asset);}
                var dead=new List<string>();foreach(var entry in frames)if(entry.Value.Asset==key)dead.Add(entry.Key);foreach(var id in dead)RemoveFrame(id);
            }
        }
        internal static void DisposeSources() {if(shared.Count!=0)return;foreach(var source in sources.Values)source.Atlas.Dispose();sources.Clear();}
    }
}
