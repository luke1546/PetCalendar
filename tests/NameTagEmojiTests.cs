using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

public static class NameTagEmojiTests {
    const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static|BindingFlags.Instance;
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static int ColorPixels(Bitmap image,string emoji){
        int count=0;
        for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++){
            Color c=image.GetPixel(x,y);if(c.A<100)continue;
            bool match=emoji=="🌟"?c.R>180&&c.G>100&&c.B<140&&c.R>c.G+15:
                emoji=="💚"?c.G>100&&c.G>c.R+35&&c.G>c.B+20:
                c.R>180&&c.R>c.G+60&&c.B>c.G+10;
            if(match)count++;
        }
        return count;
    }
    [STAThread] public static int Main(string[] args){
        try{
            Check(args.Length==2,"Pass built app path and output directory.");
            var app=Assembly.LoadFrom(Path.GetFullPath(args[0]));Directory.CreateDirectory(args[1]);
            var lines=app.GetType("PetCalendarDesktop.PetLines",true);
            var hearts=lines.GetMethod("Hearts",Hidden);var level=lines.GetMethod("Level",Hidden);
            string[] expected={"🌟","🌟","🌟","💚","💖"};int[] points={0,40,150,400,900};
            for(int i=0;i<5;i++){
                Check((int)level.Invoke(null,new object[]{points[i]})==i+1,"Affinity threshold changed.");
                Check(((string)hearts.Invoke(null,new object[]{i+1})).Trim()==expected[i],"Incorrect badge for level "+(i+1));
            }
            var tagType=app.GetType("DesktopPet.NameTag",true);var set=tagType.GetMethod("SetText",Hidden);
            var render=tagType.GetMethod("RenderText",Hidden);var fontFactory=tagType.GetField("FontFor",Hidden);
            var appFonts=app.GetType("PetCalendarDesktop.AppFonts",true);
            string[] keys={"default","jua","childfund"};double[] scales={1,1.5,2};int checkedCount=0;
            using(var sheet=new Bitmap(1400,970))using(var g=Graphics.FromImage(sheet))using(var label=new Font("Malgun Gothic",11)){
                g.Clear(Color.FromArgb(255,248,234));g.InterpolationMode=InterpolationMode.NearestNeighbor;
                g.DrawString("Affinity name tags: levels 1 / 2 / 3 / 4 / 5 (2x preview)",label,Brushes.SaddleBrown,15,15);
                int row=0;
                foreach(string key in keys){
                    var family=key=="default"?null:(FontFamily)appFonts.GetMethod("Family").Invoke(null,new object[]{key});
                    Check(key=="default"||family!=null,"Could not load font "+key);
                    float factor=(float)appFonts.GetMethod("Scale").Invoke(null,new object[]{key});
                    fontFactory.SetValue(null,(Func<float,Font>)(px=>family==null?new Font("Malgun Gothic",px,FontStyle.Bold,GraphicsUnit.Pixel):new Font(family,px*factor,FontStyle.Regular,GraphicsUnit.Pixel)));
                    foreach(double scale in scales){
                        int top=65+row++*100;g.DrawString(key+" / "+(scale*100)+"%",label,Brushes.SaddleBrown,15,top+10);
                        using(var tag=(Form)Activator.CreateInstance(tagType,true))for(int i=0;i<5;i++){
                            string text="호두 "+expected[i];
                            using(var image=(Bitmap)render.Invoke(null,new object[]{text,scale})){
                                Check(ColorPixels(image,expected[i])>3,"Missing color emoji: "+key+" / "+scale+" / "+expected[i]);
                                set.Invoke(tag,new object[]{text,scale});
                                Check((int)tagType.GetField("Width2",Hidden).GetValue(tag)==image.Width,"Name tag width differs from measured image.");
                                Check((int)tagType.GetField("Height2",Hidden).GetValue(tag)==image.Height,"Name tag height differs from measured image.");
                                IntPtr handle=(IntPtr)tagType.GetField("bitmap",Hidden).GetValue(tag);
                                using(var native=Image.FromHbitmap(handle))Check(ColorPixels(native,expected[i])>3,"Color lost in layered-window bitmap.");
                                g.DrawImage(image,new Rectangle(190+i*235,top,image.Width*2,image.Height*2));checkedCount++;
                            }
                        }
                    }
                }
                sheet.Save(Path.Combine(args[1],"name-tags.png"));
            }
            Console.WriteLine("PASS: 5 affinity levels and "+checkedCount+" color name tags across 3 fonts and 100/150/200% DPI, including actual layered-window bitmaps.");
            return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
