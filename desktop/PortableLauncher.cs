using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("펫 캘린더")]
[assembly: AssemblyProduct("PetCalendar")]
[assembly: AssemblyVersion("3.3.5.0")]
static class PortableLauncher {
    static string Quote(string value){
        var result=new StringBuilder("\"");int slashes=0;
        foreach(char c in value){
            if(c=='\\'){slashes++;continue;}
            if(c=='"'){result.Append('\\',slashes*2+1);result.Append('"');}
            else{result.Append('\\',slashes);result.Append(c);}
            slashes=0;
        }
        result.Append('\\',slashes*2);result.Append('"');return result.ToString();
    }

    [STAThread] static int Main(string[] args){
        try{
            string hash;
            using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("petcalendar.portable.zip"))
            using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").Substring(0,24);
            string baseDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PetCalendar","portable"),target=Path.Combine(baseDir,hash),marker=Path.Combine(target,".complete");
            Directory.CreateDirectory(baseDir);
            using(var mutex=new Mutex(false,"Local\\PetCalendarPortable-"+hash)){
                bool locked=false;
                try{
                    try{locked=mutex.WaitOne(TimeSpan.FromSeconds(60));}catch(AbandonedMutexException){locked=true;}
                    if(!locked)throw new IOException("실행 파일 준비 시간이 초과되었습니다.");
                    if(!File.Exists(marker)||!File.Exists(Path.Combine(target,"PetCalendar.exe"))){
                        if(Directory.Exists(target))Directory.Delete(target,true);
                        Directory.CreateDirectory(target);
                        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("petcalendar.portable.zip"))
                        using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)){
                            string prefix=Path.GetFullPath(target)+Path.DirectorySeparatorChar;
                            foreach(var entry in zip.Entries){
                                string destination=Path.GetFullPath(Path.Combine(target,entry.FullName.Replace('/',Path.DirectorySeparatorChar)));
                                if(!destination.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("실행 파일 구성을 확인할 수 없습니다.");
                                if(entry.FullName.EndsWith("/")){Directory.CreateDirectory(destination);continue;}
                                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                                using(var source=entry.Open())using(var file=File.Create(destination))source.CopyTo(file);
                            }
                        }
                        File.WriteAllText(marker,hash);
                    }
                }finally{if(locked)mutex.ReleaseMutex();}
            }
            var start=new ProcessStartInfo(Path.Combine(target,"PetCalendar.exe")){WorkingDirectory=target,UseShellExecute=false,Arguments=String.Join(" ",Array.ConvertAll(args,Quote))};
            using(var process=Process.Start(start)){
                if(Array.IndexOf(args,"--validate-install")>=0){process.WaitForExit();return process.ExitCode;}
            }
            return 0;
        }catch(Exception error){MessageBox.Show("펫 캘린더를 실행하지 못했습니다.\n"+error.Message,"펫 캘린더",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
    }
}
