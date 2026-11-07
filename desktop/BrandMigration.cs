using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace PetCalendarDesktop {
    // Old identifiers are retained only to safely upgrade an existing installation.
    internal static class BrandMigration {
        internal const string LegacyName="WorkCalendar";
        internal const string LegacyKey=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\WorkCalendarDesktop";
        static string UserRoot(string name){return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),name);}
        static bool Same(string a,string b){return String.Equals(Path.GetFullPath(a).TrimEnd('\\'),Path.GetFullPath(b).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase);}
        static string LegacyPipe(string data){using(var sha=SHA256.Create()){string value=WindowsIdentity.GetCurrent().User.Value+"|"+Path.GetFullPath(data).TrimEnd('\\').ToUpperInvariant();return LegacyName+"-"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").Substring(0,24);}}
        internal static void StopLegacy(string data) {
            string name=LegacyPipe(data);
            try{using(var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut)){pipe.Connect(500);using(var writer=new StreamWriter(pipe,new UTF8Encoding(false),1024,true)){writer.AutoFlush=true;writer.WriteLine("exit");using(var reader=new StreamReader(pipe,Encoding.UTF8,false,1024,true))reader.ReadLine();}}}catch(TimeoutException){}catch(IOException){}
            for(int i=0;i<150;i++){using(var mutex=new Mutex(false,"Local\\"+name)){bool acquired=false;try{acquired=mutex.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}if(acquired){mutex.ReleaseMutex();return;}}Thread.Sleep(100);}
            throw new IOException("기존 캘린더의 저장과 종료를 완료하지 못했습니다.");
        }
        internal static void MoveUserRoot(string previous,string current) {
            if(!Directory.Exists(previous))return;
            if(Directory.Exists(current)){if(Directory.GetFileSystemEntries(current).Length>0)throw new IOException("새 캘린더 저장 폴더에 기록이 있어 기존 기록을 덮어쓰지 않았습니다.");Directory.Delete(current);}
            for(int i=0;;i++){try{Directory.Move(previous,current);return;}catch(IOException){if(i>=29)throw;Thread.Sleep(100);}}
        }
        internal static void Prepare(string data) {
            string oldData=Path.Combine(UserRoot(LegacyName),"data"),newData=Path.Combine(UserRoot("PetCalendar"),"data");
            if(Same(data,newData)) {
                if(!Directory.Exists(UserRoot(LegacyName)))return;
                StopLegacy(oldData);
                if(File.Exists(Path.Combine(newData,"calendar.json")))return;
                MoveUserRoot(UserRoot(LegacyName),UserRoot("PetCalendar"));
            }else {
                // Explicit/custom data directories stay where the user placed them.
                StopLegacy(data);
            }
        }
        static readonly string[] Payload={"WorkCalendar.exe","WorkCalendar.exe.config","RemoveWorkCalendar.exe","calendar.ico","notification.wav","public/index.html","public/app.mjs","public/model.mjs","public/styles.css","public/icon.svg","webview2/Microsoft.Web.WebView2.Core.dll","webview2/Microsoft.Web.WebView2.WinForms.dll","webview2/WebView2Loader.dll","webview2/LICENSE.txt","webview2/source.json"};
        internal static void Finish(string root,string data) {
            using(var current=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\PetCalendarDesktop")) {
                if(current==null||!Same((string)current.GetValue("InstallLocation"),root))throw new IOException("새 캘린더의 설치 정보를 확인하지 못했습니다.");
            }
            string previous;
            using(var old=Registry.CurrentUser.OpenSubKey(LegacyKey)){if(old==null)return;previous=old.GetValue("InstallLocation") as string;}
            if(String.IsNullOrEmpty(previous)||Same(previous,root))throw new IOException("이전 설치 폴더를 확인하지 못했습니다.");
            string exe=Path.Combine(previous,LegacyName+".exe");
            if(File.Exists(exe)&&FileVersionInfo.GetVersionInfo(exe).ProductName!=LegacyName)throw new IOException("이전 캘린더 파일이 일치하지 않아 정리를 중단했습니다.");
            string backup=Path.Combine(Path.GetDirectoryName(data),"brand-upgrade-backup");Directory.CreateDirectory(backup);
            string archive=Path.Combine(backup,"previous-app.zip");
            if(!File.Exists(archive))using(var stream=File.Create(archive))using(var zip=new ZipArchive(stream,ZipArchiveMode.Create))foreach(string name in Payload){string file=Path.Combine(previous,name);if(!File.Exists(file))continue;var entry=zip.CreateEntry(name,CompressionLevel.Optimal);using(var input=File.OpenRead(file))using(var output=entry.Open())input.CopyTo(output);}
            StateStore.Atomic(Path.Combine(backup,"migration.json"),Codec.Json(new{previousInstall=previous,currentInstall=root,currentData=data,migratedUtc=DateTime.UtcNow.ToString("o")}),null);
            foreach(string name in Payload){string file=Path.Combine(previous,name);for(int i=0;File.Exists(file);i++){try{File.Delete(file);}catch(IOException){if(i>=29)throw;Thread.Sleep(100);}}}
            foreach(string folder in new[]{Path.Combine(previous,"public"),Path.Combine(previous,"webview2"),previous})if(Directory.Exists(folder)&&Directory.GetFileSystemEntries(folder).Length==0)Directory.Delete(folder);
            Registry.CurrentUser.DeleteSubKeyTree(LegacyKey,false);
        }
    }
}
