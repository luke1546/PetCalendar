using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using DesktopPet;

namespace PetCalendarDesktop {
    internal static class PetSpeechWindowsTests {
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        static int count;
        static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);count++;}
        static void Pump(int ms){var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<ms){Application.DoEvents();Thread.Sleep(5);}}
        static object Field(object item,string name){return item.GetType().GetProperty(name).GetValue(item,null);}
        static object ChatterEntry(PetChatter chatter,PetHost pet){return ((Array)chatter.Inspect()).Cast<object>().Single(e=>(long)Field(e,"window")==pet.Handle.ToInt64());}
        static DateTime ChatterDue(PetChatter chatter,PetHost pet){return DateTime.Parse((string)Field(ChatterEntry(chatter,pet),"nextUtc"),null,System.Globalization.DateTimeStyles.RoundtripKind);}
        static void SendPets(CalendarPets manager,DialogResult answer) {
            using(var timer=new System.Windows.Forms.Timer{Interval=50}){
                timer.Tick+=delegate{var dialog=Application.OpenForms.OfType<PetSendDialog>().Single();Check(dialog.AcceptButton==dialog.CancelButton,"bulk-send confirmation defaults to cancel");timer.Stop();dialog.DialogResult=answer;};
                timer.Start();manager.ConfirmSendUnnamed();
            }
        }
        static void CheckBulkSend() {
            string data=Path.Combine(Path.GetTempPath(),"calendar-send-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
            var state=new CalendarState();var errors=new List<Exception>();string path=Path.Combine(data,"pets.json");
            try {
                using(var manager=new CalendarPets(data,()=>state,errors.Add)) {
                    manager.SetEnabled(true);manager.Add("dog");
                    var named=Application.OpenForms.OfType<PetHost>().Single(p=>p.Kind==Species.Cat);
                    manager.Rename(named.Handle.ToInt64(),"호두");
                    Check(Application.OpenForms.OfType<PetHost>().All(p=>p.Personality>=1&&p.Personality<=5),"summoned pets receive a valid random personality");
                    typeof(PetHost).GetMethod("BuildMenu",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(named,null);
                    var menu=(ContextMenuStrip)typeof(PetHost).GetField("menu",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(named);
                    var options=menu.Items.OfType<ToolStripMenuItem>().Single(i=>i.Text=="성격");Check(options.DropDownItems.Count==5,"pet menu offers five personalities");
                    Check(!menu.Items.OfType<ToolStripMenuItem>().Any(i=>new[]{"자유롭게 돌아다니기","점프!","코 인사","잠자기","일시정지","바닥으로 데려오기"}.Contains(i.Text)),"calendar pet menu hides behavior commands");
                    ((ToolStripMenuItem)options.DropDownItems[4]).PerformClick();Check(named.Personality==5&&Codec.Read<PetPreferences>(File.ReadAllText(path)).pets.Single(p=>p.name=="호두").personality==5,"menu selection saves this pet's personality");
                    manager.SetEnabled(false);manager.SetEnabled(true);named=Application.OpenForms.OfType<PetHost>().Single(p=>p.PetName=="호두");Check(named.Personality==5&&manager.BackupPreferences().pets.Single(p=>p.name=="호두").personality==5,"personality survives reactivation and is included in backups");
                    string before=File.ReadAllText(path);
                    SendPets(manager,DialogResult.Cancel);Check(Application.OpenForms.OfType<PetHost>().Count()==2&&File.ReadAllText(path)==before,"cancelling bulk-send keeps every pet and preferences unchanged");
                    SendPets(manager,DialogResult.OK);Check(manager.Enabled&&manager.CanNotify&&Application.OpenForms.OfType<PetHost>().Single()==named&&named.PetName=="호두","bulk-send removes only unnamed pets and keeps the feature enabled");
                    Check(Codec.Read<PetPreferences>(File.ReadAllText(path)).pets.Single().name=="호두"&&Codec.Read<PetPreferences>(File.ReadAllText(path)).pets.Single().personality==5,"named pet and personality are preserved in saved preferences");
                    manager.Rename(named.Handle.ToInt64(),"");SendPets(manager,DialogResult.OK);
                    Check(manager.Enabled&&!manager.CanNotify&&!Application.OpenForms.OfType<PetHost>().Any(),"sending the last unnamed pet keeps feature on with zero pets");
                    Check(SimulationLoop.Count==0&&Sprites.CachedBytes==0,"empty enabled feature releases image and animation resources");
                    var prefs=Codec.Read<PetPreferences>(File.ReadAllText(path));Check(prefs.enabled&&prefs.allowEmpty&&prefs.pets.Length==0,"empty enabled state is saved explicitly");
                    manager.SetEnabled(false);manager.SetEnabled(true);Check(!Application.OpenForms.OfType<PetHost>().Any(),"turning the feature off and on does not recreate sent pets");
                }
                using(var manager=new CalendarPets(data,()=>state,errors.Add)) {
                    manager.Restore();Check(manager.Enabled&&!Application.OpenForms.OfType<PetHost>().Any(),"restarting restores zero pets without creating a default cat");
                    manager.Add("squirrel");Check(manager.CanNotify&&Application.OpenForms.OfType<PetHost>().Single().Kind==Species.Squirrel,"pets can be added again while feature remains enabled");
                }
                using(var manager=new CalendarPets(data,()=>state,errors.Add)) {
                    manager.Restore();Check(manager.Enabled&&Application.OpenForms.OfType<PetHost>().Single().Kind==Species.Squirrel,"newly added pet persists across restart");
                }
                Check(errors.Count==0,"bulk-send lifecycle has no errors");
            }finally{Directory.Delete(data,true);}
        }
        static int ReactionChecks(){
            using(var scaling=new PetBubble()){
                Check(scaling.Font.SizeInPoints==11,"speech uses baseline font");
                DesktopTheme.TextScale=2;scaling.ApplyTextScale();
                Check(scaling.Font.SizeInPoints==13,"speech font updates with larger setting");
                DesktopTheme.TextScale=0;scaling.ApplyTextScale();
            }

            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("petcalendar.pets.zip"))
            using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)){
                Sprites.ResourceOpen=name=>{var output=new MemoryStream();using(var input=zip.GetEntry(name).Open())input.CopyTo(output);output.Position=0;return output;};
                foreach(var kind in Species.All)using(var falling=new PetHost(kind,Palette.For(kind)[0],false,.4){SpawnFromCenter=true}){
                    falling.Show();Pump(50);var area=Screen.PrimaryScreen.WorkingArea;
                    Check(falling.Cat.State==Pose.Fall&&Math.Abs(falling.Cat.X-(area.Left+area.Width/2.0))<1&&falling.Cat.Y>=area.Top+area.Height/2.0&&falling.Cat.Y<area.Top+area.Height/2.0+40,kind.Id+" starts falling from center of screen");
                    double startY=falling.Cat.Y;Pump(250);Check(falling.Cat.Y>startY,kind.Id+" moves downward after spawning");
                }
                using(var pet=new PetHost(Species.Cat,Palette.For(Species.Cat)[0],false,.4)){
                    pet.Show();Pump(80);pet.BringToFloor();pet.Cat.Paused=true;
                    var chatter=new PetChatter();
                    try{
                        Check(PetVoice.Holiday(new DateTime(2026,7,17))=="제헌절","holiday resource identifies Constitution Day");
                        Check(PetVoice.Holiday(new DateTime(2030,1,1))=="1월 1일","future holiday resource is available");
                        Check(PetVoice.Holiday(new DateTime(2051,1,1))==null,"years outside supported range are not guessed");
                        var holidayUtc=new DateTime(2026,7,17,12,0,0,DateTimeKind.Local).ToUniversalTime();
                        chatter.Tick(new[]{pet},holidayUtc,false);chatter.Tick(new[]{pet},ChatterDue(chatter,pet),false);
                        Check(((string)Field(Field(ChatterEntry(chatter,pet),"bubble"),"text")).Contains("제헌절"),"pets greet on holidays");chatter.Reset();
                        var morning=new DateTime(2026,11,9,7,0,0,DateTimeKind.Local).ToUniversalTime();
                        chatter.Tick(new[]{pet},morning,false);chatter.Tick(new[]{pet},ChatterDue(chatter,pet),false);
                        Check(((string)Field(Field(ChatterEntry(chatter,pet),"bubble"),"text")).Contains("아침"),"morning greeting");
                        chatter.Tick(new[]{pet},ChatterDue(chatter,pet),false);
                        Check(!((string)Field(Field(ChatterEntry(chatter,pet),"bubble"),"text")).Contains("좋은 아침"),"morning greeting is not repeated");
                        chatter.Reset();chatter.Tick(new[]{pet},morning.AddMinutes(5),false);chatter.Tick(new[]{pet},ChatterDue(chatter,pet),false);
                        Check(!((string)Field(Field(ChatterEntry(chatter,pet),"bubble"),"text")).Contains("좋은 아침"),"morning history survives reset");
                        chatter.Reset();pet.Cat.Paused=false;
                        var night=new DateTime(2026,11,9,23,0,0,DateTimeKind.Local).ToUniversalTime();
                        chatter.Tick(new[]{pet},night,false);chatter.Tick(new[]{pet},ChatterDue(chatter,pet),false);
                        Check(((string)Field(Field(ChatterEntry(chatter,pet),"bubble"),"text")).Contains("아직 안 자냥"),"night greeting");
                        Check(pet.Cat.State==Pose.Sleep&&!pet.Cat.Sleeping,"temporary night nap");
                    }finally{chatter.Reset();}
                    pet.BringToFloor();var utc=DateTime.UtcNow;string today=Rules.Key(utc.ToLocalTime());
                    var state=new CalendarState{events=new[]{
                        new CalendarEvent{id="one",title="one",startDate=today,endDate=today,repeat="none",completedDates=new[]{today}},
                        new CalendarEvent{id="two",title="two",startDate=today,endDate=today,repeat="none"}
                    }};
                    using(var speech=new PetSpeech(()=>new[]{pet},()=>state,()=>utc,a=>{},id=>{},(id,date)=>{},e=>{throw e;})){
                        speech.Start(false,3,false);speech.Celebrate(false);
                        var bubble=Application.OpenForms.OfType<PetBubble>().Single();
                        Check(bubble.MessageText.Contains("하나 끝냈다냥")&&pet.Cat.State==Pose.Crouch,"completion celebrates with a jump");
                        state.events[1].completedDates=new[]{today};speech.Celebrate(true);Check(bubble.MessageText.Contains("전부"),"all done has a special message");
                        state.events[1].completedDates=new string[0];speech.Tick();Check(!bubble.MessageText.Contains("전부"),"active celebration updates after undo");
                        pet.BringToFloor();pet.Cat.Paused=true;
                        speech.SetAlerts(new List<Alarm>{new Alarm{id="alarm",title="test",body="test",eventId=""}});
                        var before=Codec.Read<CalendarState>(Codec.Json(state));state.events[1].completedDates=new[]{today};
                        speech.Celebrate(true,before);Check((string)Field(speech.Inspect(),"alarmId")=="alarm","celebration preserves unread alarm");
                        state.events[1].completedDates=new string[0];speech.SetAlerts(new List<Alarm>());
                        Check(!bubble.Visible,"undo cancels queued celebration even when another event was already complete");
                        speech.SetAlerts(new List<Alarm>{new Alarm{id="alarm2",title="test",body="test",eventId=""}});
                        state.events[1].completedDates=new[]{today};speech.Celebrate(true,before);speech.SetAlerts(new List<Alarm>());
                        Check(bubble.Visible&&bubble.MessageText.Contains("전부"),"valid queued celebration follows alarm");
                        speech.SetAlerts(new List<Alarm>{new Alarm{id="alarm3",title="test",body="test",eventId=""}});
                        speech.Celebrate(true,before);state.events=state.events.Take(1).ToArray();speech.SetAlerts(new List<Alarm>());
                        Check(!bubble.Visible,"deleting the completed event cancels its celebration");
                        speech.SetAlerts(new List<Alarm>{new Alarm{id="alarm4",title="test",body="test",eventId=""}});
                        speech.Celebrate(true);utc=utc.AddDays(1);speech.SetAlerts(new List<Alarm>());
                        Check(!bubble.Visible,"queued celebrations expire when the day changes");
                    }
                }
            }
            Console.WriteLine(count+" reaction checks passed");return 0;
        }
        [STAThread] static int Main(string[] args) {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            if(Environment.GetEnvironmentVariable("PET_REACTIONS_ONLY")=="1")return ReactionChecks();
            Point cursor=Cursor.Position;
            try {
                using(var label=new EmojiLabel{Text="💛",BackColor=Color.White,ForeColor=Color.Black,Size=new Size(60,40)})
                using(var bitmap=new Bitmap(60,40)) {
                    label.DrawToBitmap(bitmap,new Rectangle(0,0,60,40));bool yellow=false;
                    for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++){var color=bitmap.GetPixel(x,y);if(color.R>160&&color.G>100&&color.B<100)yellow=true;}
                    Check(yellow,"speech emoji renders in color rather than monochrome");
                    const string sentence="오늘 일정이 없다냥";
                    int sentenceWidth=TextRenderer.MeasureText(sentence,label.Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine).Width;
                    Check(label.MeasureMessage(sentence,1000).Width==sentenceWidth,"Korean sentences retain normal spacing as a complete text run");
                    Check(label.MeasureMessage("💛\n💛",60).Height>label.MeasureMessage("💛",60).Height,"emoji measurement respects multiline layout");
                }
                foreach(var species in Species.All) {
                    Check(PetVoice.ChatterCount(species.Id)==20&&Enumerable.Range(0,20).Select(i=>PetVoice.Chatter(species.Id,i)).Distinct().Count()==20,species.Id+" has twenty distinct casual lines");
                    var sample=new PetSummary{Kind="next",Title="회의 다냥 <태그> & 멍!",Time="11:00",Count=3};
                    string spoken=PetVoice.Summary(sample,species.Id);
                    Check(spoken.Contains(sample.Title)&&spoken.Contains("11:00")&&spoken.Contains("3개"),species.Id+" voice preserves title, time and count");
                    Check(PetVoice.Summary(new PetSummary{Kind="done"},species.Id).Length>0&&PetVoice.Summary(new PetSummary{Kind="empty"},species.Id).Length>0,species.Id+" has empty and completed day dialogue");
                    foreach(string kind in new[]{"current","day","past"}){sample.Kind=kind;Check(PetVoice.Summary(sample,species.Id).Contains(sample.Title),species.Id+" "+kind+" dialogue preserves title");}
                    var alert=new Alarm{eventId="event",title=sample.Title,body="11월 7일 11:00\n10분 전"};
                    Check(PetVoice.Alert(alert,species.Id).Contains(alert.title)&&PetVoice.Alert(alert,species.Id).EndsWith(alert.body),species.Id+" reminder keeps exact alarm details");
                }
                using(var nameDialog=new PetNameDialog("기존 이름","고양이"))using(var nameTimer=new System.Windows.Forms.Timer{Interval=50}) {
                    nameTimer.Tick+=delegate{var input=nameDialog.Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<TextBox>().Single();Check(input.Text=="기존 이름","name dialog preloads existing name");input.Text="호두 🐾";nameDialog.DialogResult=DialogResult.OK;nameTimer.Stop();};
                    nameTimer.Start();Check(nameDialog.ShowDialog()==DialogResult.OK&&nameDialog.PetName=="호두 🐾","name dialog saves Korean and emoji names");
                }
                using(var nameDialog=new PetNameDialog("기존 이름","고양이"))using(var nameTimer=new System.Windows.Forms.Timer{Interval=50}) {
                    nameTimer.Tick+=delegate{nameDialog.DialogResult=DialogResult.Cancel;nameTimer.Stop();};nameTimer.Start();Check(nameDialog.ShowDialog()==DialogResult.Cancel,"name dialog supports cancel");
                }
                Check(Species.All.Select(kind=>PetVoice.Summary(new PetSummary{Kind="done"},kind.Id)).Distinct().Count()==7,"all seven species have distinct dialogue");
                using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("petcalendar.pets.zip"))
                using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)) {
                    Sprites.ResourceOpen=name=>{var output=new MemoryStream();using(var input=zip.GetEntry(name).Open())input.CopyTo(output);output.Position=0;return output;};
                    using(var fixture=new Form{Text="Pet speech checks",StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(Screen.PrimaryScreen.WorkingArea.Left+30,Screen.PrimaryScreen.WorkingArea.Top+40,400,300)})
                    using(var first=new PetHost(Species.Cat,Palette.For(Species.Cat)[0],false,.4))
                    using(var second=new PetHost(Species.Cat,Palette.For(Species.Cat)[0],false,.6)) {
                        first.RenameRequested=()=>{first.PetName="새 이름";};
                        fixture.Show();SetForegroundWindow(fixture.Handle);first.Show();second.Show();Pump(80);
                        typeof(PetHost).GetMethod("BuildMenu",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(first,null);
                        var petMenu=(ContextMenuStrip)typeof(PetHost).GetField("menu",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(first);
                        var rename=petMenu.Items.OfType<ToolStripMenuItem>().Single(item=>item.Text=="이름 짓기 / 바꾸기");rename.PerformClick();
                        Check(first.PetName=="새 이름"&&first.Text.Contains("새 이름")&&petMenu.Items[0].Text.Contains("새 이름"),"pet menu rename updates only that pet and its title");
                        Check(second.PetName=="","renaming one pet leaves the other unchanged");
                        first.Cat.Paused=second.Cat.Paused=true;
                        using(var compact=new PetBubble()) {
                            compact.Present("멍! 같이 있어서 좋아요 🦴",false,0,false,true);compact.Follow(first);var shortSize=compact.Size;Pump(800);
                            Check(compact.Size==shortSize,"typing does not resize a short bubble");
                            Check(shortSize.Height<DesktopTheme.Px(compact,85)&&shortSize.Width<DesktopTheme.Px(compact,290),"short chatter uses a compact bubble in both dimensions");
                            compact.Present("멍!",false,0,false,true);compact.Follow(first);Check(compact.Width<shortSize.Width,"very short speech shrinks further");
                            var text=compact.Controls.OfType<Label>().Single();var close=compact.Controls.OfType<Button>().Single(b=>b.Text=="×");Check(text.Right<close.Left,"compact speech text leaves room for its close button");
                            compact.Present("오늘 할 일이 3개 남았어요!\n다음은 11:00 회의예요. 자료와 준비물을 챙기고 여유롭게 준비해요 🐾",false,0,true);compact.Follow(first);
                            Check(compact.Height>shortSize.Height,"longer speech wraps into a taller bubble");
                            compact.Present("회의가 시작돼요!",true,2,true);compact.Follow(first);
                            Check(compact.Controls.OfType<Button>().All(b=>b.Left>=0&&b.Right<=compact.Width&&b.Bottom<=compact.Height),"alarm action buttons fit inside the bubble");
                        }

                        using(var preview=new Bitmap(960,450)) {
                            var kinds=new[]{Species.Cat,Species.Cat,Species.Cat,Species.Dog,Species.RedPanda,Species.Capybara,Species.Squirrel,Species.Alpaca,Species.Elephant};
                            var coats=new[]{Palette.All[0],Palette.All[6],Palette.All[8],Palette.For(Species.Dog)[0],Palette.For(Species.RedPanda)[0],Palette.For(Species.Capybara)[0],Palette.For(Species.Squirrel)[0],Palette.For(Species.Alpaca)[0],Palette.For(Species.Elephant)[0]};
                            var fills=new List<string>();
                            using(var g=Graphics.FromImage(preview)){
                                g.Clear(Color.FromArgb(245,239,226));
                                for(int i=0;i<kinds.Length;i++)using(var pet=new PetHost(kinds[i],coats[i],false,.5))using(var animalBubble=new PetBubble()) {
                                    pet.Show();Pump(40);pet.BringToFloor();pet.Cat.Paused=true;var message=PetVoice.Chatter(kinds[i].Id,0);animalBubble.Present(message,false,0,false,true);animalBubble.Follow(pet);
                                    animalBubble.Controls.OfType<Label>().Single().Text=message;
                                    fills.Add((string)Field(animalBubble.Inspect(),"fill"));
                                    Check((string)Field(animalBubble.Inspect(),"species")==kinds[i].Id,kinds[i].Id+" bubble selects the speaker's animal style");
                                    using(var shot=new Bitmap(animalBubble.Width,animalBubble.Height)){animalBubble.DrawToBitmap(shot,new Rectangle(0,0,shot.Width,shot.Height));var saved=g.Save();g.TranslateTransform(i%3*320+10,i/3*150+30);g.SetClip(animalBubble.Region,System.Drawing.Drawing2D.CombineMode.Replace);g.DrawImageUnscaled(shot,0,0);g.Restore(saved);}
                                    g.DrawString(kinds[i].Name+" · "+coats[i].Name,SystemFonts.DefaultFont,Brushes.SaddleBrown,i%3*320+14,i/3*150+8);
                                }
                            }
                            Check(fills.Take(3).Distinct().Count()==3,"different cat coat colors produce different bubble tints");
                            Check(fills.Distinct().Count()>=7,"animal and coat colors produce distinct readable pastel backgrounds");
                            if(args.Length>0)preview.Save(Path.Combine(args[0],"pet-bubble-looks.png"));
                        }
                        int listeners=SimulationLoop.FrameListenerCount;
                        using(var smooth=new PetBubble()) {
                            first.BringToFloor();first.Cat.SetPose(Pose.Walk);SimulationLoop.Wake();
                            smooth.Present("멍! 같이 있어서 좋아요 🦴",false,0,false,true);smooth.Follow(first);Pump(40);
                            int moves=smooth.FollowMoves,layouts=smooth.LayoutPasses;var foreground=Native.GetForegroundWindow();
                            Pump(400);
                            Check(smooth.FollowMoves-moves>=4,"walking bubble updates on animation frames rather than a hundred millisecond clock");
                            Check(smooth.LayoutPasses==layouts,"moving bubble reuses measured text and layout");
                            Check(smooth.Bottom==(int)Math.Round(first.Cat.Y-first.Cat.Size*.875),"bubble remains attached to the pet's head on each frame");
                            Check(Native.GetForegroundWindow()==foreground,"smooth following never steals focus");
                            first.Cat.Grab();moves=smooth.FollowMoves;
                            for(int i=0;i<8;i++){first.Cat.X+=4;first.Cat.Y-=3;Pump(35);}
                            Check(smooth.FollowMoves-moves>=7,"held pet's bubble follows each drag movement without retargeting");
                            Check(SimulationLoop.FrameListenerCount==listeners+1,"one visible bubble owns one frame listener");
                            smooth.Hide();Check(SimulationLoop.FrameListenerCount==listeners,"hiding bubble detaches frame listener");
                            smooth.Follow(first);Check(SimulationLoop.FrameListenerCount==listeners+1,"showing bubble reattaches exactly one frame listener");
                            smooth.Follow(first);Check(SimulationLoop.FrameListenerCount==listeners+1,"repeated speech ticks do not duplicate frame listeners");
                            first.Cat.Release(0,0);first.BringToFloor();first.Cat.Paused=true;
                        }
                        Check(SimulationLoop.FrameListenerCount==listeners,"disposing bubble releases its animation subscription");
                        DateTime utc=DateTime.UtcNow;var state=new CalendarState();
                        var errors=new List<Exception>();var alerts=new List<Alarm>();PetSpeech speech=null;
                        using(speech=new PetSpeech(()=>new[]{first,second},()=>state,()=>utc,a=>{},id=>{alerts.RemoveAll(a=>a.id==id);speech.SetAlerts(alerts);},(id,date)=>{},errors.Add)) {
                            Cursor.Position=fixture.Location;
                            var foreground=Native.GetForegroundWindow();speech.Start(true,1);speech.Preview();Pump(30);
                            var bubble=Application.OpenForms.OfType<PetBubble>().Single();
                            Check(bubble.Visible,"manual summary is visible");
                            var dialogue=bubble.Controls.OfType<Label>().Single();
                            Check(dialogue.Text.Length<bubble.MessageText.Length,"dialogue starts with progressive text");
                            Pump(1000);Check(dialogue.Text==bubble.MessageText,"progressive dialogue reveals the complete message");
                            Check(bubble.Controls.OfType<Button>().Count(b=>b.Visible)==1,"regular speech shows only its small close control");
                            Check((Native.GetWindowLongPtr(bubble.Handle,-20).ToInt64()&0x8)!=0,"speech window remains topmost without activating");
                            Check(Native.GetForegroundWindow()==foreground,"speech does not steal foreground focus");
                            Check(new[]{first,second}.Count(p=>p.SpeechHold)==1,"only one pet speaks at a time");
                            var previousSpeaker=first.SpeechHold?first:second;
                            Check(first.Cat.Paused&&second.Cat.Paused,"speech preserves user pause choices");
                            Check(Screen.FromControl(bubble).WorkingArea.Contains(bubble.Bounds),"bubble stays inside the monitor working area");
                            Cursor.Position=new Point(bubble.Left+20,bubble.Top+45);
                            for(int i=0;i<20;i++){Cursor.Position=new Point(bubble.Left+20,bubble.Top+45);utc=utc.AddSeconds(1);speech.Tick();}
                            Check(bubble.Visible,"hover keeps regular summary visible beyond twenty seconds");
                            Cursor.Position=fixture.Location;utc=utc.AddSeconds(10);speech.Tick();Check(bubble.Visible,"summary retains its remaining reading time after hover");
                            utc=utc.AddSeconds(11);speech.Tick();if(bubble.Visible)Console.WriteLine("Expiry diagnostic "+Codec.Json(speech.Inspect())+" cursor="+Cursor.Position+" expires="+typeof(PetSpeech).GetField("expires",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(speech));Check(!bubble.Visible,"regular summary closes after twenty unhovered seconds");
                            Check(!first.SpeechHold&&!second.SpeechHold,"closing summary releases both pets");
                            var local=utc.ToLocalTime();var date=Rules.Key(local);
                            state.events=new[]{new CalendarEvent{id="one",title="첫 번째 일정",startDate=date,endDate=date,startTime="",endTime="",repeat="none",repeatUntil="",category="work",note=""},new CalendarEvent{id="two",title="두 번째 일정",startDate=date,endDate=date,startTime="",endTime="",repeat="none",repeatUntil="",category="work",note=""}};
                            speech.Preview();Check(!previousSpeaker.SpeechHold,"regular guides rotate speakers");
                            Check(Native.GetForegroundWindow()==foreground,"reopening speech preserves foreground focus");
                            var cycleSpeaker=first.SpeechHold?first:second;
                            speech.Open();Check(bubble.MessageText.Contains("두 번째 일정")&&cycleSpeaker.SpeechHold,"clicking summary advances on the same pet");
                            speech.PetClicked(cycleSpeaker);Check(bubble.MessageText.Contains("첫 번째 일정"),"clicking the speaking pet cycles back to first event");
                            Cursor.Position=fixture.Location;utc=utc.AddSeconds(16);speech.Tick();Check(bubble.Visible,"summary remains visible after sixteen seconds");
                            utc=utc.AddSeconds(5);speech.Tick();Check(!bubble.Visible,"clicked summary expires after twenty seconds");
                            speech.Dismiss();
                            Check(!first.SpeechHold&&!second.SpeechHold,"manual close releases movement hold");
                            state.events[0].completedDates=new[]{date};speech.Celebrate(false);Check(bubble.Visible&&bubble.MessageText.Contains("🎉"),"completion shows a celebration bubble");
                            state.events[1].completedDates=new[]{date};speech.Celebrate(true);Check(bubble.MessageText.Contains("전부"),"all done shows special celebration");
                            Check(first.Cat.Paused&&second.Cat.Paused,"celebration respects paused pets");speech.Dismiss();state.events[0].completedDates=state.events[1].completedDates=new string[0];
                            Check(PetVoice.TimeGreeting("cat",true).Contains("아침")&&PetVoice.TimeGreeting("cat",false).Contains("아직 안 자냥"),"cat has distinct morning and late night dialogue");
                            speech.Configure(true,2);utc=utc.AddSeconds(60);speech.Tick();Check(!bubble.Visible,"changed interval resets next summary");
                            speech.Resume();utc=utc.AddSeconds(119);speech.Tick();Check(!bubble.Visible,"resume resets summary deadline");
                            utc=utc.AddSeconds(1);speech.Tick();Check(bubble.Visible,"summary appears at resumed deadline");speech.Dismiss();
                            first.Cat.Grab();second.Cat.Grab();
                            alerts.Add(new Alarm{id="first",eventId="",title="알림",body=new string('가',200),createdAt=1});speech.SetAlerts(alerts);
                            Check(!bubble.Visible&&alerts.Count==1,"held pets keep unread alarm queued");
                            first.Cat.Release(0,0);first.BringToFloor();first.Cat.Paused=true;speech.Tick();
                            Check(bubble.Visible,"queued alarm appears when a pet becomes available");
                            first.Cat.Grab();first.Cat.X+=60;first.Cat.Y-=100;first.DrawCat(true);speech.Tick();
                            Check(bubble.Visible&&first.SpeechHold&&!second.SpeechHold,"lifting the speaker keeps its alarm attached to that pet");
                            second.Cat.Release(0,0);second.BringToFloor();second.Cat.Paused=true;speech.Tick();
                            Check(first.SpeechHold&&!second.SpeechHold,"another available pet does not take over a held speaker's alarm");
                            first.Cat.Release(0,0);first.BringToFloor();first.Cat.Paused=true;speech.Tick();
                            Check(Screen.FromControl(bubble).WorkingArea.Contains(bubble.Bounds),"long alarm stays on screen");
                            using(var full=new Form{FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,Bounds=Screen.PrimaryScreen.Bounds}) {
                                full.Show();SetForegroundWindow(full.Handle);Pump(80);first.RefreshWorld();second.RefreshWorld();speech.Tick();
                                Check(!first.Visible&&!second.Visible&&!bubble.Visible,"fullscreen hides pets and speech together");
                                Check(alerts.Count==1,"fullscreen does not acknowledge unread alarm");
                                full.Close();fixture.Activate();Pump(40);first.RefreshWorld();second.RefreshWorld();speech.Tick();
                                Check(bubble.Visible,"unread alarm returns after fullscreen ends");
                            }
                            utc=utc.AddHours(3);speech.Tick();Check(bubble.Visible,"alarm never expires automatically");
                            speech.Dismiss();Check(alerts.Count==0&&!bubble.Visible,"confirm acknowledges the current alarm");
                            first.Cat.Grab();second.Cat.Grab();utc=utc.AddMinutes(2);speech.Tick();
                            first.Cat.Release(0,0);first.BringToFloor();first.Cat.Paused=true;speech.Tick();
                            Check(!bubble.Visible,"unavailable regular summaries are skipped without a catch-up burst");
                            speech.Configure(false,10);speech.ConfigureChatter(true);speech.Resume();
                            var chatterItems=(Array)Field(speech.Inspect(),"chatter");
                            var firstItem=chatterItems.Cast<object>().Single(e=>(long)Field(e,"window")==first.Handle.ToInt64());
                            utc=DateTime.Parse((string)Field(firstItem,"nextUtc"),null,System.Globalization.DateTimeStyles.RoundtripKind);speech.Tick();
                            Check(Application.OpenForms.OfType<PetBubble>().Any(b=>b.Text=="데스크톱 펫 · 잡담"),"enabled chatter is displayed by speech controller");
                            speech.ConfigureChatter(false);Check(!Application.OpenForms.OfType<PetBubble>().Any(b=>b.Text=="데스크톱 펫 · 잡담"),"turning off chatter immediately disposes its active bubbles");
                            utc=utc.AddMinutes(2);speech.Tick();Check(((Array)Field(speech.Inspect(),"chatter")).Length==0,"disabled chatter does not schedule messages");
                            speech.Preview();Check(bubble.Visible,"manual schedule summary still works with chatter disabled");speech.Dismiss();
                            speech.ConfigureChatter(true);speech.Tick();Check(((Array)Field(speech.Inspect(),"chatter")).Length==2,"reenabling chatter creates independent fresh cooldowns");
                            speech.Stop();Check(bubble.IsDisposed&&!first.SpeechHold&&!second.SpeechHold,"stop disposes speech UI and movement holds");
                            Check(errors.Count==0,"native speech loop has no errors");
                            first.BringToFloor();second.BringToFloor();first.Cat.Paused=second.Cat.Paused=true;
                            var chatter=new PetChatter();var baseUtc=utc;
                            try {
                                int[] mins={30,60,120,180,300},maxs={60,120,180,300,600};
                                for(int level=1;level<=5;level++){first.Personality=level;chatter.Reset();chatter.Tick(new[]{first},baseUtc,false);var due=ChatterDue(chatter,first);Check(due>=baseUtc.AddSeconds(mins[level-1])&&due<=baseUtc.AddSeconds(maxs[level-1]),"personality "+level+" uses its configured chatter range");}
                                first.Personality=second.Personality=2;chatter.Reset();
                                chatter.Tick(new[]{first,second},baseUtc,false);
                                var firstDue=ChatterDue(chatter,first);var secondDue=ChatterDue(chatter,second);
                                Check(firstDue>=baseUtc.AddSeconds(60)&&firstDue<=baseUtc.AddSeconds(120)&&secondDue>=baseUtc.AddSeconds(60)&&secondDue<=baseUtc.AddSeconds(120),"each pet starts its own sixty to one hundred twenty second cooldown");
                                first.Personality=1;chatter.Reschedule(first,baseUtc);Check(ChatterDue(chatter,first)>=baseUtc.AddSeconds(30)&&ChatterDue(chatter,first)<=baseUtc.AddSeconds(60)&&ChatterDue(chatter,second)==secondDue,"changing one personality only reschedules that pet");first.Personality=2;chatter.Reschedule(first,baseUtc);firstDue=ChatterDue(chatter,first);
                                chatter.Tick(new[]{first,second},baseUtc.AddSeconds(59),false);Check(chatter.ActiveCount==0,"chatter does not appear before cooldown");
                                var earlier=firstDue<=secondDue?first:second;var later=earlier==first?second:first;var earlyDue=ChatterDue(chatter,earlier);var laterDue=ChatterDue(chatter,later);
                                chatter.Tick(new[]{first,second},earlyDue,false);Check(chatter.ActiveCount>0,"pet speaks when its own cooldown expires");
                                if(laterDue>earlyDue)Check(ChatterDue(chatter,later)==laterDue,"one pet speaking does not reset another pet's cooldown");
                                var earlyEntry=ChatterEntry(chatter,earlier);string message=(string)Field(Field(earlyEntry,"bubble"),"text");
                                var next=ChatterDue(chatter,earlier);Check(next>=earlyDue.AddSeconds(60)&&next<=earlyDue.AddSeconds(120),"speaker receives an independent randomized next cooldown");
                                chatter.Tick(new[]{first,second},earlyDue.AddSeconds(9),false);Check(Field(ChatterEntry(chatter,earlier),"bubble")!=null,"chatter remains for nine seconds");
                                chatter.Tick(new[]{first,second},earlyDue.AddSeconds(10),false);Check(Field(ChatterEntry(chatter,earlier),"bubble")==null,"chatter closes at ten seconds");
                                chatter.Tick(new[]{first,second},next,false);Check((string)Field(Field(ChatterEntry(chatter,earlier),"bubble"),"text")!=message,"a pet avoids consecutive duplicate chatter");
                                chatter.Tick(new[]{first,second},next,true);Check(chatter.ActiveCount==0,"calendar speech preempts every chatter bubble");
                                chatter.Tick(new[]{first,second},next.AddHours(3),false);Check(chatter.ActiveCount==0,"resume does not cause a chatter burst");
                                chatter.Tick(new[]{first},next.AddHours(3),false);Check(((Array)chatter.Inspect()).Length==1,"removed pets release their chatter state");
                                DateTime together=utc.AddHours(4);bool closeDeadlines=false;
                                for(int attempt=0;attempt<100&&!closeDeadlines;attempt++){
                                    chatter.Reset();chatter.Tick(new[]{first,second},together,false);
                                    firstDue=ChatterDue(chatter,first);secondDue=ChatterDue(chatter,second);
                                    closeDeadlines=Math.Abs((firstDue-secondDue).TotalSeconds)<10;
                                }
                                Check(closeDeadlines,"independent cooldown fixture has overlapping speaking times");
                                var early=firstDue<secondDue?firstDue:secondDue;var late=firstDue>secondDue?firstDue:secondDue;
                                chatter.Tick(new[]{first,second},early,false);chatter.Tick(new[]{first,second},late,false);
                                Check(chatter.ActiveCount==2,"two pets can show separate chatter bubbles at the same time");
                                Check((long)Field(Field(ChatterEntry(chatter,first),"bubble"),"window")!=(long)Field(Field(ChatterEntry(chatter,second),"bubble"),"window"),"pets do not share or replace a chatter window");
                                chatter.Reset();var morningUtc=new DateTime(2026,11,9,7,0,0,DateTimeKind.Local).ToUniversalTime();
                                chatter.Tick(new[]{first},morningUtc,false);var morningDue=ChatterDue(chatter,first);chatter.Tick(new[]{first},morningDue,false);
                                Check(((string)Field(Field(ChatterEntry(chatter,first),"bubble"),"text")).Contains("아침"),"morning cooldown produces a greeting");
                                chatter.Tick(new[]{first},ChatterDue(chatter,first),false);
                                Check(!((string)Field(Field(ChatterEntry(chatter,first),"bubble"),"text")).Contains("좋은 아침"),"morning greeting does not repeat every chatter interval");
                                chatter.Reset();first.BringToFloor();first.Cat.Paused=false;
                                var nightUtc=new DateTime(2026,11,9,23,0,0,DateTimeKind.Local).ToUniversalTime();
                                chatter.Tick(new[]{first},nightUtc,false);chatter.Tick(new[]{first},ChatterDue(chatter,first),false);
                                Check(((string)Field(Field(ChatterEntry(chatter,first),"bubble"),"text")).Contains("아직 안 자냥"),"late night cooldown produces sleepy dialogue");
                                Check(first.Cat.State==Pose.Sleep&&!first.Cat.Sleeping,"night nap is temporary rather than persistent sleep");first.Cat.Paused=true;

                            }finally{chatter.Reset();}
                            Check(chatter.ActiveCount==0&&((Array)chatter.Inspect()).Length==0,"stopping chatter releases all bubbles and cooldowns");
                        }
                    }
                    Sprites.DisposeSources();Sprites.ResourceOpen=null;
                }
                CheckBulkSend();
                Console.WriteLine(count+" native speech checks passed");return 0;
            }catch(Exception error){Console.Error.WriteLine(error);return 1;}
            finally{Cursor.Position=cursor;}
        }
    }
}
