using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DesktopPet {
    internal sealed class Palette {
        public readonly string Name;
        public readonly Color Fur;
        public readonly string Variant;
        public Palette(string name,Color fur) : this(name,fur,null) {}
        public Palette(string name,Color fur,string variant) {Name=name;Fur=fur;Variant=variant;}
        public static readonly Palette[] All={
            new Palette("치즈",Color.Empty),new Palette("회색",Color.FromArgb(148,155,166)),
            new Palette("검정",Color.FromArgb(70,76,91)),new Palette("하양",Color.FromArgb(239,234,221)),
            new Palette("초코",Color.FromArgb(137,91,72)),new Palette("크림",Color.FromArgb(233,203,149)),
            new Palette("라벤더",Color.FromArgb(181,153,215)),new Palette("하늘",Color.FromArgb(119,179,222)),
            new Palette("민트",Color.FromArgb(125,198,171)),new Palette("분홍",Color.FromArgb(227,156,183))
        };
        public static Palette[] For(Species species) {
            if(species==Species.Cat)return All;
            if(species==Species.Dog)return new[]{new Palette("하양",Color.Empty)};
            if(species==Species.Alpaca)return new[]{new Palette("하양",Color.FromArgb(250,250,246))};
            if(species==Species.Capybara)return new[]{new Palette("갈색",Color.Empty)};
            if(species==Species.Elephant)return new[]{new Palette("기본 회색",Color.Empty),new Palette("밝은 회색",Color.FromArgb(185,185,185))};
            if(species==Species.Squirrel)return new[]{
                new Palette("줄무늬",Color.Empty),
                new Palette("오로라 민트",Color.FromArgb(119,193,172)),new Palette("벚꽃 로즈",Color.FromArgb(218,144,170)),
                new Palette("달빛 오팔",Color.FromArgb(177,192,219)),new Palette("라벤더 드림",Color.FromArgb(180,150,218)),
                new Palette("복숭아 골드",Color.FromArgb(232,176,122)),new Palette("별빛 사파이어",Color.FromArgb(85,128,191)),
                All[1],All[2],All[3]};
            var result=(Palette[])All.Clone();
            result[0]=new Palette("적갈색",Color.Empty);
            return result;
        }
        static byte Channel(double value,int alpha,bool premultiplied) {
            value=Math.Max(0,Math.Min(255,value));return (byte)(premultiplied?value*alpha/255:value);
        }
        public void Apply(Bitmap bitmap) {Apply(bitmap,Species.Cat);}
        public void Apply(Bitmap bitmap,Species species) {
            if(Fur.IsEmpty)return;
            var rect=new Rectangle(0,0,bitmap.Width,bitmap.Height);
            bool premultiplied=bitmap.PixelFormat==PixelFormat.Format32bppPArgb;
            var data=bitmap.LockBits(rect,ImageLockMode.ReadWrite,premultiplied?PixelFormat.Format32bppPArgb:PixelFormat.Format32bppArgb);
            try {
                byte[] bytes=new byte[data.Stride*data.Height];Marshal.Copy(data.Scan0,bytes,0,bytes.Length);
                for(int i=0;i<bytes.Length;i+=4) {
                    int b=bytes[i],g=bytes[i+1],r=bytes[i+2];
                    // Recolor warm saturated fur, preserving cream paws, eyes, outlines and alpha.
                    int alpha=bytes[i+3];if(alpha==0)continue;
                    if(premultiplied && alpha!=255) {r=Math.Min(255,r*255/alpha);g=Math.Min(255,g*255/alpha);b=Math.Min(255,b*255/alpha);}
                    double luminance=r*.30+g*.59+b*.11;
                    if(species==Species.RedPanda) {
                        // The russet face markings and dark brown limbs share the coat's hue.
                        // Keep white facial patches, pink ears and near-neutral eyes intact.
                        double saturation=r==0?0:(r-Math.Min(g,b))/(double)r;
                        bool warm=r>g*1.10 && g>b*1.02 && saturation>(luminance>110?.45:.30);
                        if(!warm || luminance<14)continue;
                        double darkR=12+Fur.R*.22,darkG=12+Fur.G*.22,darkB=12+Fur.B*.22;
                        double targetR,targetG,targetB;
                        if(luminance<65) {
                            double t=Math.Max(0,(luminance-14)/51);
                            targetR=9+(darkR-9)*t;targetG=9+(darkG-9)*t;targetB=9+(darkB-9)*t;
                        }else if(luminance<145) {
                            double t=(luminance-65)/80;
                            targetR=darkR+(Fur.R-darkR)*t;targetG=darkG+(Fur.G-darkG)*t;targetB=darkB+(Fur.B-darkB)*t;
                        }else {
                            double t=Math.Min(.65,(luminance-145)/150);
                            targetR=Fur.R+(255-Fur.R)*t;targetG=Fur.G+(255-Fur.G)*t;targetB=Fur.B+(255-Fur.B)*t;
                        }
                        bytes[i]=Channel(targetB,alpha,premultiplied);bytes[i+1]=Channel(targetG,alpha,premultiplied);bytes[i+2]=Channel(targetR,alpha,premultiplied);
                        continue;
                    }
                    if(species.NeutralCoat) {
                        // Neutral fleece/skin needs a luminance mask, keeping dark facial details and pink ears.
                        if(luminance<65 || (r>g*1.22 && r>b*1.2))continue;
                    }else if(r<90 || r<g*1.17 || g<b*1.2 || r-b<55)continue;
                    double reference=species.NeutralCoat?(species==Species.Elephant?155:225):170;
                    double light=luminance/reference;
                    bytes[i]=Channel(Fur.B*light,alpha,premultiplied);bytes[i+1]=Channel(Fur.G*light,alpha,premultiplied);bytes[i+2]=Channel(Fur.R*light,alpha,premultiplied);
                }
                Marshal.Copy(bytes,0,data.Scan0,bytes.Length);
            }finally {bitmap.UnlockBits(data);}
        }
    }
    internal sealed class PaletteBag {
        readonly Random random=new Random();
        readonly Queue<Palette> bag=new Queue<Palette>();
        readonly Palette[] available;
        Palette previous;
        public PaletteBag() : this(Species.Cat) {}
        public PaletteBag(Species species) {available=Palette.For(species);}
        public Palette Next() {
            if(bag.Count==0) {
                var entries=(Palette[])available.Clone();
                for(int i=entries.Length-1;i>0;i--) {int j=random.Next(i+1);var p=entries[i];entries[i]=entries[j];entries[j]=p;}
                if(entries.Length>1 && previous!=null && entries[0].Name==previous.Name) {var first=entries[0];entries[0]=entries[1];entries[1]=first;}
                foreach(var entry in entries)bag.Enqueue(entry);
            }
            previous=bag.Dequeue();return previous;
        }
    }
}
