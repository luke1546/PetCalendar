﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("펫 캘린더")]
[assembly: AssemblyDescription("일정과 할 일을 정리하는 펫 캘린더")]
[assembly: AssemblyProduct("PetCalendar")]
[assembly: AssemblyVersion("3.5.0.0")]
[assembly: TargetFramework(".NETFramework,Version=v4.8",FrameworkDisplayName=".NET Framework 4.8")]

namespace PetCalendarDesktop {
    public static class Entry {
        static readonly object outputGate=new object();
        public static string DataDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PetCalendar","data");
        static string pipeName;
        static bool restarting;
        static Dictionary<string,string> Options(string[] args){var d=new Dictionary<string,string>();for(int i=0;i<args.Length;i++){if(args[i].StartsWith("--")){string name=args[i].Substring(2);d[name]=i+1<args.Length&&!args[i+1].StartsWith("--")?args[++i]:"1";}}return d;}
        static void Streams(){Console.SetIn(new StreamReader(Console.OpenStandardInput(),new UTF8Encoding(false)));Console.SetOut(new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true});Console.SetError(new StreamWriter(Console.OpenStandardError(),new UTF8Encoding(false)){AutoFlush=true});}
        static void Reply(object value){lock(outputGate)Console.WriteLine(Codec.Json(value));}
        static string PipeFor(string data){using(var sha=SHA256.Create()){string value=WindowsIdentity.GetCurrent().User.Value+"|"+Path.GetFullPath(data).TrimEnd('\\').ToUpperInvariant();return "PetCalendar-"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").Substring(0,24);}}
        public static bool Control(string command,int timeout=1500){try{using(var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut)){pipe.Connect(timeout);using(var writer=new StreamWriter(pipe,new UTF8Encoding(false),1024,true)){writer.AutoFlush=true;writer.WriteLine(command);using(var reader=new StreamReader(pipe,Encoding.UTF8,false,1024,true))return reader.ReadLine()=="ok";}}}catch{return false;}}
        static void Listen(DesktopHost host){var thread=new Thread(delegate(){while(!host.IsDisposed){try{var security=new PipeSecurity();security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));using(var server=new NamedPipeServerStream(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.None,4096,4096,security)){server.WaitForConnection();string action;using(var reader=new StreamReader(server,Encoding.UTF8,false,1024,true))action=reader.ReadLine();using(var writer=new StreamWriter(server,new UTF8Encoding(false),1024,true)){writer.AutoFlush=true;writer.WriteLine("ok");}host.BeginInvoke(new Action(async delegate{if(action=="show")host.ShowCalendar();else if(action=="widget")host.ShowWidget();else if(action=="exit"||action=="restart")await host.Shutdown(action=="restart");}));}}catch{if(host.IsDisposed)return;Thread.Sleep(100);}}});thread.IsBackground=true;thread.Start();}
        static void ModelMode(){Streams();string line;while((line=Console.ReadLine())!=null){try{var r=Codec.Object(line);string action=(string)r["action"];object result=null;if(action=="validate")result=Rules.Validate(Codec.Json(r["state"]));else if(action=="occurrences"){var events=Codec.Read<CalendarEvent[]>(Codec.Json(r["events"]));result=Rules.Occurrences(events,(string)r["from"],(string)r["to"]);}else if(action=="dday")result=Rules.Dday((string)r["date"],(string)r["today"]);else if(action=="selected-dday"){var events=Codec.Read<CalendarEvent[]>(Codec.Json(r["events"]));var e=events.FirstOrDefault(x=>x.pinned);result=e==null?null:Rules.DdayOccurrence(e,(string)r["today"]);}else if(action=="reminder")result=Rules.ReminderAt(Codec.Read<Occurrence>(Codec.Json(r["occurrence"])));else throw new Exception("Unknown model command");Reply(new{ok=true,result=result});}catch(Exception error){Reply(new{ok=false,error=error.Message});}}}
        static bool StopRunning() {
            bool acknowledged=Control("exit",500);
            for(int i=0;i<100;i++) {
                using(var probe=new Mutex(false,"Local\\"+pipeName)) {
                    bool acquired=false;try{acquired=probe.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}
                    if(acquired){probe.ReleaseMutex();return acknowledged;}
                }
                Thread.Sleep(100);
            }
            throw new Exception("실행 중인 캘린더의 저장·종료를 완료하지 못했습니다.");
        }
        static int PrepareInstall() {
            StopRunning();BrandMigration.Prepare(DataDir);
            Directory.CreateDirectory(DataDir);
            bool userMigration=String.Equals(Path.GetFullPath(DataDir).TrimEnd('\\'),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PetCalendar","data"),StringComparison.OrdinalIgnoreCase);
            string original=Path.Combine(DataDir,"calendar.json");
            if(File.Exists(original))Rules.Validate(File.ReadAllText(original));
            var rollback=new Dictionary<string,object>();
            foreach(var folder in userMigration?new[]{Environment.SpecialFolder.DesktopDirectory,Environment.SpecialFolder.Startup}:new Environment.SpecialFolder[0]) {
                string shortcut=Path.Combine(Environment.GetFolderPath(folder),"펫 캘린더.lnk");
                if(File.Exists(shortcut)){string backup=Path.Combine(DataDir,"upgrade-"+folder+".lnk");File.Copy(shortcut,backup,true);rollback[folder.ToString()]=backup;}
            }
            StopRunning();
            Directory.CreateDirectory(Path.Combine(DataDir,"backups"));
            if(File.Exists(original)){Rules.Validate(File.ReadAllText(original));File.Copy(original,Path.Combine(DataDir,"backups","calendar-upgrade-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".json"),false);}
            StateStore.Atomic(Path.Combine(DataDir,"upgrade-rollback.json"),Codec.Json(rollback),null);
            return 0;
        }
        static int RollbackInstall(){string file=Path.Combine(DataDir,"upgrade-rollback.json");if(!File.Exists(file))return 0;var rollback=Codec.Object(File.ReadAllText(file));foreach(var folder in new[]{Environment.SpecialFolder.DesktopDirectory,Environment.SpecialFolder.Startup})if(rollback.ContainsKey(folder.ToString()))File.Copy((string)rollback[folder.ToString()],Path.Combine(Environment.GetFolderPath(folder),"펫 캘린더.lnk"),true);return 0;}
        static void RemovePreviousShortcuts(string name,string root){
            if(name!="업무 캘린더"&&name!="업무 캘린더 QA")return;
            object shell=null;try{
                shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                foreach(var folder in new[]{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.Startup),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),name)}){
                    string link=Path.Combine(folder,name+".lnk");if(!File.Exists(link))continue;object shortcut=null;
                    try{shortcut=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{link});string target=shortcut.GetType().InvokeMember("TargetPath",BindingFlags.GetProperty,null,shortcut,null) as string;if(!String.IsNullOrEmpty(target)&&String.Equals(Path.GetFullPath(target),Path.Combine(root,"PetCalendar.exe"),StringComparison.OrdinalIgnoreCase))File.Delete(link);}
                    finally{if(shortcut!=null&&System.Runtime.InteropServices.Marshal.IsComObject(shortcut))System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);}
                }
            }finally{if(shell!=null&&System.Runtime.InteropServices.Marshal.IsComObject(shell))System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}
        }
        static int ValidateInstall(string root){foreach(string file in new[]{"PetCalendar.exe","calendar.ico","notification.wav","EMOJI-LICENSE.md"})if(!File.Exists(Path.Combine(root,file)))throw new Exception("설치 파일이 없습니다: "+file);using(var pack=Assembly.GetExecutingAssembly().GetManifestResourceStream("petcalendar.pets.zip"))if(pack==null)throw new Exception("동물 리소스가 없습니다.");using(var emoji=Assembly.GetExecutingAssembly().GetManifestResourceStream("petcalendar.emoji.zip"))if(emoji==null)throw new Exception("이모지 리소스가 없습니다.");using(var holidays=Assembly.GetExecutingAssembly().GetManifestResourceStream("petcalendar.holidays.json"))if(holidays==null)throw new Exception("공휴일 리소스가 없습니다.");return 0;}
        [STAThread]
        public static int Main(string[] args) {
            var options=Options(args);bool automation=options.ContainsKey("automation");
            try {
                if(options.ContainsKey("data-dir"))DataDir=Path.GetFullPath(options["data-dir"]);
                pipeName=PipeFor(DataDir);
                string root=AppDomain.CurrentDomain.BaseDirectory;
                if(options.ContainsKey("model-test")){ModelMode();return 0;}
                if(options.ContainsKey("prepare-install"))return PrepareInstall();
                if(options.ContainsKey("finish-brand-upgrade")){BrandMigration.Finish(root,DataDir);return 0;}
                if(options.ContainsKey("finish-display-rename")){RemovePreviousShortcuts(options["finish-display-rename"],root);return 0;}
                if(options.ContainsKey("stop-for-uninstall")){StopRunning();return 0;}
                if(options.ContainsKey("rollback-install"))return RollbackInstall();
                if(options.ContainsKey("validate-install"))return ValidateInstall(root);
                if(options.ContainsKey("control"))return Control(options["control"])?0:2;
                if(!automation&&!options.ContainsKey("data-dir"))BrandMigration.Prepare(DataDir);
                if(automation)Streams();
                bool created;using(var mutex=new Mutex(true,"Local\\"+pipeName,out created)) {
                    if(!created){for(int i=0;i<40;i++){if(Control("show",300))return 0;Thread.Sleep(100);}throw new Exception("캘린더의 응답을 기다리고 있습니다. 잠시 후 다시 실행해 주세요.");}
                    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                    string profile=options.ContainsKey("profile-dir")?Path.GetFullPath(options["profile-dir"]):Path.Combine(Path.GetDirectoryName(DataDir),"WebView2");
                    using(var host=new DesktopHost(root,DataDir,profile,automation)) {
                        host.ExitRequested+=restart=>{restarting=restart;};
                        IntPtr handle=host.Handle;
                        Listen(host);
                        if(automation){host.AutomationReply+=Reply;var reader=new Thread(delegate(){string line;try{while((line=Console.ReadLine())!=null){var request=Codec.Object(line);host.BeginInvoke(new Action(()=>host.Command(request)));}}catch{}finally{try{host.BeginInvoke(new Action(async delegate{await host.Shutdown(false);}));}catch{}}});reader.IsBackground=true;reader.Start();}
                        PerfProbe.Install();Warmup.Start();Application.Run(host);
                    }
                    mutex.ReleaseMutex();
                }
                if(restarting){string command="--data-dir \""+DataDir+"\"";if(options.ContainsKey("profile-dir"))command+=" --profile-dir \""+options["profile-dir"]+"\"";Process.Start(new ProcessStartInfo(Application.ExecutablePath,command){UseShellExecute=false,WorkingDirectory=root});}
                return 0;
            }catch(Exception error){if(automation){Reply(new{kind="fatal",error=error.ToString()});}else if(options.ContainsKey("silent")){try{Directory.CreateDirectory(DataDir);File.AppendAllText(Path.Combine(DataDir,"installer-errors.log"),error.ToString()+Environment.NewLine);}catch{}}else if(options.ContainsKey("model-test"))Console.Error.WriteLine(error);else MessageBox.Show("펫 캘린더를 시작하지 못했습니다. 기존 기록은 그대로 보존했습니다.\n\n"+error.Message,"펫 캘린더",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
        }
    }
}
