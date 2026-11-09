using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Windows.Forms;

public static class ToyFollowTests {
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static int checks;
    static object Get(object value,string name){var type=value as Type??value.GetType();return type.GetField(name,Flags).GetValue(value is Type?null:value);}
    static void Set(object value,string name,object next){value.GetType().GetField(name,Flags).SetValue(value,next);}
    static object Call(object value,string name,params object[] args){var type=value as Type??value.GetType();return type.GetMethod(name,Flags).Invoke(value is Type?null:value,args);}
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    static bool Near(double a,double b){return Math.Abs(a-b)<.000001;}
    [STAThread] public static int Main(string[] args){
        object manager=null;string data=null;
        try{
            var app=Assembly.LoadFrom(Path.GetFullPath(args[0]));
            var motion=app.GetType("DesktopPet.Motion",true);var lifeType=app.GetType("PetCalendarDesktop.PetLife",true);
            var model=Activator.CreateInstance(motion,new object[]{123});Set(model,"X",200d);Set(model,"Y",300d);Set(model,"Size",64d);
            foreach(var item in new[]{new{Point=new Point(200,280),Distance=0d},new{Point=new Point(282,280),Distance=50d},new{Point=new Point(283,280),Distance=51d},new{Point=new Point(262,204),Distance=50d},new{Point=new Point(200,358),Distance=50d}})
                Check(Near((double)Call(lifeType,"ToyDistance",model,item.Point),item.Distance),"Toy distance differs from token-usage-main silhouette bounds.");
            var surface=app.GetType("DesktopPet.Surface",true);
            var floor=Activator.CreateInstance(surface,new object[]{"test-floor",0d,2000d,900d,true});
            ((IList)Get(model,"Surfaces")).Add(floor);((IList)Get(model,"Screens")).Add(new Rectangle(0,0,2000,1000));
            Call(model,"Reset",200d,floor);Set(model,"ToyTargetX",(double?)400d);Call(model,"Step",.1,true);
            double unit=(double)motion.GetProperty("Unit").GetValue(model,null);
            Check(Near((double)Get(model,"X"),200+120*unit*.1),"Toy movement must use the reference 120px/s speed.");
            for(int i=0;i<100;i++)Call(model,"Step",.1,true);
            Check(Near((double)Get(model,"X"),400-10*unit),"Toy following must stop 10px short of the pointer.");
            Set(model,"GoalX",(double?)1000d);Call(model,"StopToy");
            Check(Get(model,"ToyTargetX")==null&&Near((double)Get(model,"Vx"),0),"Release must clear toy movement immediately.");
            Check(Near((double)Get(model,"GoalX"),1000),"Release must preserve unrelated goals.");
            var shelf=Activator.CreateInstance(surface,new object[]{"test-shelf",100d,500d,500d,false});
            ((IList)Get(model,"Surfaces")).Add(shelf);Call(model,"Reset",200d,shelf);Set(model,"ToyTargetX",(double?)900d);Call(model,"Step",1d,true);
            Check((double)Get(model,"X")>200&&(double)Get(model,"X")<500,"Toy must work on a window shelf and stay on it.");
            Call(model,"Grab");Check(Get(model,"ToyTargetX")==null,"Dragging must clear the toy target.");

            data=Path.Combine(Path.GetTempPath(),"pet-toy-follow-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data,"pets.json"),"{\"enabled\":true,\"allowEmpty\":true,\"summaryEnabled\":false,\"chatterEnabled\":false,\"bedEnabled\":false,\"toyEnabled\":false,\"pets\":[{\"species\":\"cat\",\"palette\":\"치즈\",\"name\":\"A\",\"affinity\":0},{\"species\":\"cat\",\"palette\":\"치즈\",\"name\":\"B\",\"affinity\":0},{\"species\":\"cat\",\"palette\":\"치즈\",\"name\":\"C\",\"affinity\":0}]}");
            var stateType=app.GetType("PetCalendarDesktop.CalendarState",true);object state=Activator.CreateInstance(stateType);
            var snapshot=Expression.Lambda(typeof(Func<>).MakeGenericType(stateType),Expression.Constant(state,stateType)).Compile();
            var errors=new List<Exception>();var managerType=app.GetType("PetCalendarDesktop.CalendarPets",true);
            manager=Activator.CreateInstance(managerType,Flags,null,new object[]{data,snapshot,(Action<Exception>)(e=>errors.Add(e)),false},null);
            Call(manager,"Restore");Application.DoEvents();var life=Get(manager,"life");
            ((Timer)Get(life,"timer")).Stop();var loop=app.GetType("DesktopPet.SimulationLoop",true);((Timer)Get(loop,"timer")).Stop();Call(life,"ConfigureToy",true);
            var active=(IList)Get(manager,"active");Check(active.Count==3,"Fixture pets must restore.");
            var hosts=Array.CreateInstance(app.GetType("DesktopPet.PetHost",true),active.Count);
            for(int i=0;i<active.Count;i++){
                hosts.SetValue(active[i],i);var cat=Get(active[i],"Cat");Set(cat,"X",200d+i*1000);Set(cat,"Y",500d);Set(cat,"Size",64d);Set(cat,"Support","test-floor");Set(cat,"Held",false);Set(cat,"Paused",false);Set(cat,"Sleeping",false);Set(cat,"GoalSleep",false);Set(cat,"GoalX",null);Set(active[i],"systemHidden",false);
            }
            var first=active[0];var second=active[1];var third=active[2];var firstCat=Get(first,"Cat");var secondCat=Get(second,"Cat");var thirdCat=Get(third,"Cat");
            var world=Call(app.GetType("DesktopPet.World",true),"Get",false);double dpi=(double)Call(world,"DpiAt",new Point(200,480));
            int firstBefore=(int)Get(first,"Affinity"),secondBefore=(int)Get(second,"Affinity"),thirdBefore=(int)Get(third,"Affinity");
            var now=DateTime.UtcNow;
            Action<Point,bool> update=(point,down)=>Call(life,"UpdateToy",hosts,point,now,down);
            update(new Point(232+(int)Math.Floor(50*dpi)+1,480),false);
            Check(Get(life,"toyPet")==null,"A pet outside the 50px entry radius must not follow.");
            Set(secondCat,"X",330d);Set(thirdCat,"GoalX",(double?)999d);Set(thirdCat,"GoalSleep",true);
            update(new Point(250,480),false);
            Check(Object.ReferenceEquals(Get(life,"toyPet"),first),"Only the nearest pet should be selected.");
            Check(Get(firstCat,"ToyTargetX")!=null&&Get(secondCat,"ToyTargetX")==null&&Get(thirdCat,"ToyTargetX")==null,"Exactly one pet must have a toy movement target.");
            Check((int)Get(first,"Affinity")==firstBefore+2&&(int)Get(second,"Affinity")==secondBefore&&(int)Get(third,"Affinity")==thirdBefore,"Only the playing pet may receive toy affinity.");
            update(new Point(310,480),false);
            Check(Object.ReferenceEquals(Get(life,"toyPet"),first),"Keep the current pet inside the reference release radius to avoid flicker.");
            Set(secondCat,"X",1200d);
            update(new Point(232+(int)Math.Floor(110*dpi)+1,480),false);
            Check(Get(life,"toyPet")==null&&Get(firstCat,"ToyTargetX")==null,"Leaving the horizontal release radius must stop immediately.");
            Check(!(bool)Get(Get(life,"toy"),"shown"),"The toy window must hide on the same update.");
            Check(Near((double)Get(thirdCat,"GoalX"),999)&&(bool)Get(thirdCat,"GoalSleep"),"Other pets' bed goals must remain untouched.");
            update(new Point(200,480),false);update(new Point(200,444-(int)Math.Floor(110*dpi)-1),false);
            Check(Get(life,"toyPet")==null&&!(bool)Get(Get(life,"toy"),"shown"),"Moving vertically away must hide the toy even at the same X.");
            update(new Point(200,480),false);update(new Point(200,480),true);
            Check(Get(life,"toyPet")==null,"Mouse button presses must release toy play.");
            update(new Point(200,480),false);Set(firstCat,"Paused",true);Set(secondCat,"X",250d);update(new Point(250,480),false);
            Check(Object.ReferenceEquals(Get(life,"toyPet"),second)&&Get(firstCat,"ToyTargetX")==null,"An unavailable target must release before choosing another nearby pet.");
            Call(life,"ConfigureToy",false);
            Check(Get(life,"toyPet")==null&&Get(secondCat,"ToyTargetX")==null&&!(bool)Get(Get(life,"toy"),"shown"),"Disabling toys must clear the single target and window immediately.");
            Check(errors.Count==0,"Toy play logged an unexpected error.");
            ((IDisposable)manager).Dispose();manager=null;
            Check((int)loop.GetProperty("FrameListenerCount",Flags).GetValue(null,null)==0,"Stopping pets must detach toy frame tracking.");
            Console.WriteLine("PASS: "+checks+" toy-follow checks: reference geometry, nearest-only selection, 50/110px boundaries, vertical release, no remote rewards, shelf movement, dragging, disable and cleanup.");
            return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{if(manager!=null)((IDisposable)manager).Dispose();if(data!=null&&Directory.Exists(data))Directory.Delete(data,true);}
    }
}
