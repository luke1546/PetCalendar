using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("펫 캘린더 제거")]
[assembly: AssemblyProduct("PetCalendar")]
[assembly: AssemblyVersion("2.0.0.0")]

// A separate, ordinary .NET executable avoids the self-modifying uninstaller stub.
// Only the current user's registered installation and our explicit files are removed.
static class CalendarRemoval {
    static string Key(bool qa){return @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PetCalendarDesktop"+(qa?"QA":"");}
    static string Normal(string p){return Path.GetFullPath(p).TrimEnd('\\');}
    static bool Same(string a,string b){return String.Equals(Normal(a),Normal(b),StringComparison.OrdinalIgnoreCase);}
    static string Pipe(string data){using(var sha=SHA256.Create()){string value=WindowsIdentity.GetCurrent().User.Value+"|"+Normal(data).ToUpperInvariant();return "PetCalendar-"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").Substring(0,24);}}
    static void Stop(string data) {
        string pipeName=Pipe(data);
        try{using(var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut)){pipe.Connect(500);using(var writer=new StreamWriter(pipe,new UTF8Encoding(false),1024,true)){writer.AutoFlush=true;writer.WriteLine("exit");using(var reader=new StreamReader(pipe,Encoding.UTF8,false,1024,true))reader.ReadLine();}}}catch(TimeoutException){}catch(IOException){}
        for(int i=0;i<150;i++) {
            using(var mutex=new Mutex(false,"Local\\"+pipeName)) {
                bool acquired=false;try{acquired=mutex.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}
                if(acquired){mutex.ReleaseMutex();return;}
            }
            Thread.Sleep(100);
        }
        throw new IOException("캘린더의 저장·종료를 완료하지 못했습니다. 잠시 후 다시 제거해 주세요.");
    }
    static void Delete(string file){if(File.Exists(file))File.Delete(file);}
    static void Empty(string directory){if(Directory.Exists(directory)&&Directory.GetFileSystemEntries(directory).Length==0)Directory.Delete(directory);}
    static void Remove(string root,string data,bool qa) {
        Stop(data);
        string product=Path.Combine(root,"PetCalendar.exe");
        if(File.Exists(product)&&FileVersionInfo.GetVersionInfo(product).ProductName!="PetCalendar")throw new IOException("설치된 캘린더 파일을 확인하지 못했습니다.");
        string link="펫 캘린더"+(qa?" QA":"")+".lnk";
        foreach(var folder in new[]{Environment.SpecialFolder.DesktopDirectory,Environment.SpecialFolder.Startup})Delete(Path.Combine(Environment.GetFolderPath(folder),link));
        string start=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),Path.GetFileNameWithoutExtension(link));
        Delete(Path.Combine(start,link));Empty(start);
        foreach(string file in new[]{"public/index.html","public/app.mjs","public/model.mjs","public/styles.css","public/icon.svg","webview2/Microsoft.Web.WebView2.Core.dll","webview2/Microsoft.Web.WebView2.WinForms.dll","webview2/WebView2Loader.dll","webview2/LICENSE.txt","webview2/source.json","PetCalendar.exe","PetCalendar.exe.config","calendar.ico","notification.wav","RemovePetCalendar.exe"})Delete(Path.Combine(root,file));
        Empty(Path.Combine(root,"public"));Empty(Path.Combine(root,"webview2"));
        Registry.CurrentUser.DeleteSubKeyTree(Key(qa),false);Empty(root);
    }
    [STAThread] static int Main(string[] args) {
        bool silent=Array.IndexOf(args,"/S")>=0,qa=Array.IndexOf(args,"--qa")>=0;
        try {
            using(var registration=Registry.CurrentUser.OpenSubKey(Key(qa))) {
                if(registration==null)return 0;
                string root=(string)registration.GetValue("InstallLocation"),data=(string)registration.GetValue("DataDir");
                if(String.IsNullOrEmpty(root)||String.IsNullOrEmpty(data))throw new IOException("설치 정보를 읽지 못했습니다.");
                root=Normal(root);
                if(!Same(root,AppDomain.CurrentDomain.BaseDirectory))throw new IOException("설치 폴더의 제거 프로그램을 실행해 주세요.");
                if(!silent&&MessageBox.Show("펫 캘린더를 제거할까요?\n일정·할 일·설정과 백업은 보존됩니다.","펫 캘린더 제거",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return 0;
                Stop(data);
                // Windows permits renaming a running image on the same volume.
                // Relocate it first, then finish removal in this process so callers
                // can reliably wait for completion without a detached worker race.
                string temp=Path.Combine(Path.GetDirectoryName(root),".PetCalendarMaintenance");Directory.CreateDirectory(temp);
                File.Move(Application.ExecutablePath,Path.Combine(temp,"Remove-"+Guid.NewGuid().ToString("N")+".exe"));
                Remove(root,data,qa);
                return 0;
            }
        }catch(Exception error){if(!silent)MessageBox.Show(error.Message,"펫 캘린더 제거",MessageBoxButtons.OK,MessageBoxIcon.Error);else Console.Error.WriteLine(error.Message);return 1;}
    }
}
