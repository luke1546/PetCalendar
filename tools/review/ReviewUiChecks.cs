using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PetCalendarDesktop;

// Supplemental checks for the active WinForms UI, independent of the missing historical driver.
// Compile beside the frozen app (see review report); pass a new, isolated output directory.
static class ReviewUiChecks {
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly List<object> checks=new List<object>();
    static string output;
    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")]static extern int GetGuiResources(IntPtr process,int flag);
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Wait(Func<bool> done){var end=DateTime.UtcNow.AddSeconds(12);while(!done()&&DateTime.UtcNow<end){Application.DoEvents();Thread.Sleep(2);}Check(done(),"Timed out");}
    static T Pump<T>(Task<T> task){Wait(()=>task.IsCompleted);return task.GetAwaiter().GetResult();}
    static object Call(object target,string method,params object[] args){return target.GetType().GetMethods(Hidden).Single(m=>m.Name==method&&m.GetParameters().Length==args.Length).Invoke(target,args);}
    static void Run(string name,Action action){try{action();checks.Add(new{name=name,passed=true});}catch(Exception e){checks.Add(new{name=name,passed=false,error=e.ToString()});}finally{foreach(var dialog in DialogForm.Open.ToArray())dialog.CloseDialog();Application.DoEvents();}}
    static CalendarState State(DesktopHost host){return (CalendarState)Pump(host.Api("GET","/api/state","")).body;}
    static void Capture(Form form,string name){using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save(Path.Combine(output,name+".png"));}}
    static void Click(Control control){Check(control.Enabled,"Cannot click a disabled control");typeof(Control).GetMethod("OnClick",Hidden).Invoke(control,new object[]{EventArgs.Empty});}
    static object Resources(int cycle){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();Application.DoEvents();using(var p=Process.GetCurrentProcess()){return new{cycle=cycle,privateMiB=p.PrivateMemorySize64/1048576.0,handles=p.HandleCount,gdi=GetGuiResources(p.Handle,0),user=GetGuiResources(p.Handle,1),forms=Application.OpenForms.Count};}}
    static void Accessible(AccessibleObject item,List<object> rows,List<AccessibleRole> roles,int depth){if(item==null||depth>4)return;rows.Add(new{name=item.Name,role=item.Role.ToString(),children=item.GetChildCount(),depth=depth});roles.Add(item.Role);for(int i=0;i<item.GetChildCount();i++)Accessible(item.GetChild(i),rows,roles,depth+1);}
    [STAThread]static int Main(string[] args){
        output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);var data=Path.Combine(output,"data");Directory.CreateDirectory(data);
        var state=new CalendarState();state.settings.hotkey.enabled=false;state.settings.notificationSound=false;state.settings.desktopPopups=false;
        File.WriteAllText(Path.Combine(data,"calendar.json"),Codec.Json(state));
        File.WriteAllText(Path.Combine(data,"pets.json"),"{\"enabled\":false,\"allowEmpty\":true,\"toyDefaultOff\":true,\"pets\":[]}");
        File.WriteAllText(Path.Combine(data,"ui-state.json"),"{\"quickTourDone\":true,\"drafts\":{}}");
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        using(var host=new DesktopHost(Path.GetDirectoryName(typeof(Entry).Assembly.Location),data,Path.Combine(data,"legacy-profile"),true)){
            host.Show();Application.DoEvents();var view=host.Controls.OfType<CalendarView>().Single();var today=Rules.Key(DateTime.Today);
            Run("editor_validation_draft_unicode_save_undo_redo",()=>{
                var editor=new EventDialog(view,null,today,false);editor.Show(editor.TitleBox.Box);
                Call(editor,"Submit");Application.DoEvents();Check(!editor.IsDisposed&&State(host).events.Length==0,"Empty title was saved or editor closed");
                editor.TitleBox.Text="검토용 한글 🍀 뷁·똠·힣";var note=(InputBox)editor.GetType().GetField("note",Hidden).GetValue(editor);note.Text="첫 줄\r\n둘째 줄";
                editor.Cancel();editor=new EventDialog(view,null,today,false);editor.Show(editor.TitleBox.Box);
                Check(editor.TitleBox.Text=="검토용 한글 🍀 뷁·똠·힣","Unsaved title draft lost");Call(editor,"Submit");Wait(()=>editor.IsDisposed);
                var saved=State(host);Check(saved.events.Length==1&&saved.events[0].title=="검토용 한글 🍀 뷁·똠·힣"&&saved.events[0].note.Contains("둘째 줄"),"Editor save lost Unicode or multiline note");
                Check(Pump((Task<bool>)Call(view,"Undo",new object[]{null}))&&State(host).events.Length==0,"Undo failed");
                Check(Pump((Task<bool>)Call(view,"Redo"))&&State(host).events.Length==1,"Redo failed");
            });
            Run("native_keyboard_n_opens_editor",()=>{
                view.Focus();SendMessage(view.Handle,0x0100,new IntPtr((int)Keys.N),IntPtr.Zero);Application.DoEvents();
                Check(DialogForm.Open.OfType<EventDialog>().Any(),"N key message did not open editor");
            });
            Run("repeat_scope_cancel_single_edit_and_undo",()=>{
                var next=State(host);var series=new CalendarEvent{id="repeat-ui",title="매일 반복 검토",startDate=today,endDate=today,startTime="",endTime="",category="work",note="",repeat="daily",repeatUntil="",reminder=null};
                next.events=next.events.Concat(new[]{series}).ToArray();Check(Pump(host.Api("PUT","/api/state",Codec.Json(next))).status==200,"Fixture save failed");
                var editor=new EventDialog(view,series,today,false);editor.Show(editor.TitleBox.Box);editor.TitleBox.Text="이번 회차만 변경";Call(editor,"Submit");
                Wait(()=>DialogForm.Open.OfType<RepeatScopeDialog>().Any());DialogForm.Open.OfType<RepeatScopeDialog>().Single().Cancel();Application.DoEvents();
                Check(!editor.IsDisposed&&editor.TitleBox.Text=="이번 회차만 변경"&&!State(host).events.Single(e=>e.id==series.id).excludedDates.Any(),"Scope cancellation changed data or lost input");
                Call(editor,"Submit");Wait(()=>DialogForm.Open.OfType<RepeatScopeDialog>().Any());Click(DialogForm.Open.OfType<RepeatScopeDialog>().Single().One);Wait(()=>editor.IsDisposed);
                var saved=State(host);Check(saved.events.Single(e=>e.id==series.id).excludedDates.Contains(today),"Original occurrence was not excluded");
                Check(saved.events.Any(e=>e.id!=series.id&&e.title=="이번 회차만 변경"&&e.repeat=="none"),"Single occurrence edit missing");
                Check(Pump((Task<bool>)Call(view,"Undo",new object[]{null})),"Undo failed");saved=State(host);
                Check(!saved.events.Single(e=>e.id==series.id).excludedDates.Any()&&!saved.events.Any(e=>e.title=="이번 회차만 변경"),"Undo did not restore the series");
            });
            Run("small_window_editor_settings_help_captures",()=>{
                host.Size=new Size(800,600);Application.DoEvents();Capture(host,"main-800x600");
                var editor=new EventDialog(view,null,today,false);editor.Show(editor.TitleBox.Box);Application.DoEvents();Capture(editor,"editor-small-host");editor.CloseDialog();
                Call(view,"OpenSettings");Application.DoEvents();Check(DialogForm.Open.Count==1,"Settings did not open");Capture(DialogForm.Open[0],"settings-small-host");DialogForm.Open[0].CloseDialog();
                Call(view,"ShowHelp");Application.DoEvents();Check(DialogForm.Open.Count==1,"Help did not open");Capture(DialogForm.Open[0],"help-small-host");
            });
            Run("painted_calendar_cells_are_exposed_to_accessibility",()=>{
                var rows=new List<object>();var roles=new List<AccessibleRole>();Accessible(view.AccessibilityObject,rows,roles,0);
                File.WriteAllText(Path.Combine(output,"accessibility-tree.json"),Codec.Json(rows));
                Check(roles.Contains(AccessibleRole.Cell),"CalendarView provider exposes no date cells; external screen-reader traversal still requires manual verification");
            });
            Run("repeated_editor_settings_lifecycle",()=>{
                for(int i=0;i<5;i++){var warm=new EventDialog(view,null,today,false);warm.Show(warm.TitleBox.Box);warm.CloseDialog();}Call(view,"OpenSettings");DialogForm.Open[0].CloseDialog();
                var samples=new List<object>();samples.Add(Resources(0));
                for(int i=1;i<=100;i++){
                    var editor=new EventDialog(view,null,today,false);editor.Show(editor.TitleBox.Box);editor.CloseDialog();
                    if(i%5==0){Call(view,"OpenSettings");DialogForm.Open[0].CloseDialog();}
                    Application.DoEvents();if(i%25==0)samples.Add(Resources(i));
                }
                File.WriteAllText(Path.Combine(output,"dialog-lifecycle.json"),Codec.Json(samples));Check(DialogForm.Open.Count==0,"Dialog registry leaked closed dialogs");
            });
            var shutdown=host.Shutdown(false);Wait(()=>shutdown.IsCompleted);shutdown.GetAwaiter().GetResult();
        }
        File.WriteAllText(Path.Combine(output,"results.json"),Codec.Json(new{checks=checks}));Console.WriteLine(Codec.Json(new{checks=checks}));return checks.Any(c=>!(bool)c.GetType().GetProperty("passed").GetValue(c,null))?1:0;
    }
}
