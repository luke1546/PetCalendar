using System;
using System.IO;
using PetCalendarDesktop;
static class BrandMigrationTests {
    static int count;
    static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);count++;}
    static void Main(){
        string root=Path.Combine(Path.GetTempPath(),"pet-calendar-migration-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            string old=Path.Combine(root,"previous"),current=Path.Combine(root,"current");Directory.CreateDirectory(Path.Combine(old,"data"));Directory.CreateDirectory(Path.Combine(old,"WebView2"));
            File.WriteAllText(Path.Combine(old,"data","calendar.json"),"일정 원본");File.WriteAllText(Path.Combine(old,"data","pets.json"),"펫 이름과 색상");File.WriteAllText(Path.Combine(old,"data","widget.json"),"위젯 위치");File.WriteAllText(Path.Combine(old,"data","notifications.json"),"미확인 알림");File.WriteAllText(Path.Combine(old,"WebView2","profile"),"웹 프로필");
            BrandMigration.MoveUserRoot(old,current);
            Check(!Directory.Exists(old)&&Directory.Exists(current),"user root moves to the renamed folder");
            foreach(var name in new[]{"calendar.json","pets.json","widget.json","notifications.json"})Check(File.Exists(Path.Combine(current,"data",name)),name+" remains present");
            Check(File.ReadAllText(Path.Combine(current,"data","calendar.json"))=="일정 원본"&&File.ReadAllText(Path.Combine(current,"WebView2","profile"))=="웹 프로필","calendar bytes and web profile remain unchanged");
            BrandMigration.MoveUserRoot(old,current);Check(File.Exists(Path.Combine(current,"data","calendar.json")),"completed migration is harmless when repeated");
            Directory.CreateDirectory(old);File.WriteAllText(Path.Combine(old,"old"),"old");
            bool rejected=false;try{BrandMigration.MoveUserRoot(old,current);}catch(IOException){rejected=true;}
            Check(rejected&&File.ReadAllText(Path.Combine(old,"old"))=="old"&&File.ReadAllText(Path.Combine(current,"data","calendar.json"))=="일정 원본","existing destination is never overwritten");
            string empty=Path.Combine(root,"empty");Directory.CreateDirectory(empty);BrandMigration.MoveUserRoot(old,empty);Check(File.ReadAllText(Path.Combine(empty,"old"))=="old","empty destination supports migration");
            Console.WriteLine(count+" brand migration checks passed");
        }finally{Directory.Delete(root,true);}
    }
}
