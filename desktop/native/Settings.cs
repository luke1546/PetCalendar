using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PetCalendarDesktop {
    // <output class="hotkey-preview">
    public sealed class HotkeyPreview : Control {
        public HotkeyPreview(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
        float Sc{get{return DeviceDpi/96f;}}
        Font TextFont{get{return Ui.Font(Sc,13,700,"consolas");}}
        public Size Preferred(){float s=Sc;return new Size((int)Math.Ceiling(Ui.Width(Text,TextFont)+26*s+2*s),(int)Math.Ceiling(Math.Max(36*s,TextFont.GetHeight()+16*s+2*s)));}
        protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Invalidate();var p=Parent as FlowPanel;if(p!=null)p.Reflow();}
        protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;float s=Sc;g.Clear(Ui.C("#fffaf1"));Ui.Prepare(g);Ui.Box(g,new RectangleF(0,0,Width,Height),7*s,Ui.C("#fff0cf"),Ui.C("#ceba96"),s);Ui.Text(g,Text,TextFont,ClientRectangle,DesktopTheme.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
    }
    public sealed class SettingsDialog : DialogForm {
        readonly DesktopHost host;readonly GroupItem petGroup,keyGroup,notifyGroup,dataGroup,screenGroup;
        readonly ThemeCheckBox petsEnabled=Css.Check("데스크톱 펫 사용"),summaryEnabled=Css.Check("오늘 일정 정기 안내",12,true),chatterEnabled=Css.Check("일상 대화 사용",12,true);
        readonly SelectField species=Css.Select("cat","고양이","dog","포메라니안","redPanda","레서판다","capybara","카피바라","squirrel","다람쥐","alpaca","알파카","elephant","코끼리","random","랜덤");
        readonly Btn addPet=Css.Button("한 마리 추가",BtnKind.Primary),savePetSpeech=Css.Button("주기 적용"),previewPetSpeech=Css.Button("지금 안내 듣기");
        readonly ThemeCheckBox bedEnabled=Css.Check("펫 침대 놓기",12,true),toyEnabled=Css.Check("마우스 장난감 놀이",12,true);readonly Btn openDiary=Css.Button("펫 일기 보기",BtnKind.Normal,11);readonly TimeField diaryTime=new TimeField();
        readonly TextItem affinityList=new TextItem("",12,400,Ui.C("#6e5d45"),1.9f){Top=10,Bottom=4};readonly TextItem snackStatus=Css.Hint("");
        readonly InputBox userTitle=new InputBox(false){MaxLength=8,Placeholder="예: 집사, 언니, 주인님"};readonly Btn saveTitle=Css.Button("호칭 적용");
        readonly NumberBox minutes=new NumberBox{MinValue=1,MaxValue=1440};readonly TextItem petsStatus=Css.Hint("데스크톱 펫이 꺼져 있습니다.");
        readonly ThemeCheckBox hotkeyEnabled=Css.Check("단축키 사용",12,true),ctrl=Css.Check("Ctrl",12,true),alt=Css.Check("Alt"),shift=Css.Check("Shift");
        readonly SelectField hotkeyKey=new SelectField();readonly HotkeyPreview preview=new HotkeyPreview{Text="Alt + `"};readonly Btn resetHotkey=Css.Button("기본값"),saveHotkey=Css.Button("단축키 적용",BtnKind.Primary);readonly TextItem hotkeyStatus=new TextItem("",11,400,Ui.C("#79694f"),1.8f);
        readonly Btn toggleWidget=Css.Button("위젯 열기");readonly ThemeCheckBox widgetTopmost=Css.Check("항상 위에 표시"),widgetLocked=Css.Check("위젯 위치·크기 고정");
        readonly TextItem notificationDescription=Css.P(""),permissionStatus=Css.Hint("");readonly Btn enableNotifications=Css.Button("데스크톱 알림 켜기"),testNotification=Css.Button("알림 테스트");
        readonly ThemeCheckBox notificationSound=Css.Check("알림 소리");readonly SelectField defaultReminder=Css.Reminders();
        readonly Btn exportBackup=Css.Button("백업 내보내기",BtnKind.Normal,11,"download"),importBackup=Css.Button("백업 복원",BtnKind.Normal,11,"upload");
        readonly SelectField textScale=Css.Select("0","작게","1","보통","2","크게");readonly SelectField fontFamily=FontSelect();
        readonly InputBox categoryName=new InputBox(false){MaxLength=12,Placeholder="예: 운동, 공부"};readonly Btn addCategory=Css.Button("분류 추가",BtnKind.Primary);
        readonly SelectField categoryColor=Css.Select("#c9a7f0","보라","#ffd86b","노랑","#8fd8c8","민트","#9fb4ff","파랑","#ff9fb2","장미","#a8c66c","올리브","#d4b896","모래","#bdbdbd","회색");
        readonly List<KeyValuePair<Rectangle,string>> categoryDeletes=new List<KeyValuePair<Rectangle,string>>();GroupItem categoryGroup;
        static SelectField FontSelect(){var f=new SelectField();foreach(var o in AppFonts.Options)f.Add(o.Key,o.Value);return f;}
        Dictionary<string,object> petsPreference,widgetPreference;bool petsSaving,petsRefreshPending,petSpeechDirty,widgetSaving,syncing;
        static readonly int[] Keys={9,32,192,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,81,82,83,84,85,86,87,88,89,90,48,49,50,51,52,53,54,55,56,57,112,113,114,115,116,117,118,119,120,121,122};
        public SettingsDialog(CalendarView view,DesktopHost owner):base(view,"설정 및 백업"){
            host=owner;
            foreach(var b in new[]{addPet,savePetSpeech,previewPetSpeech}){b.MinH=40;}
            foreach(int k in Keys)hotkeyKey.Add(k.ToString(),k==9?"Tab":k==32?"Space":k==192?"` (백틱 · ~)":k>=112?"F"+(k-111):((char)k).ToString());
            // 데스크톱 펫
            petGroup=new GroupItem("데스크톱 펫",true);var pets=Section();
            pets.Add(Css.H3("데스크톱 펫"),Css.P("작은 친구가 바탕화면과 창 위를 산책해요."),new CssCheck(petsEnabled,12));
            var addRow=new RowItem(8,new FieldItem("추가할 펫",species){Top=17,Bottom=0},new ControlItem(addPet,false)){Columns=new[]{-1f,0f},AlignEnd=true,Top=14};pets.Add(addRow);
            var speech=Sub();speech.Add(Css.H3("오늘 일정 말풍선"),new CssCheck(summaryEnabled,12),Css.Hint("미완료 일정 수와 진행 중이거나 다음에 시작할 일정을 알려드려요."));
            speech.Add(new RowItem(8,new FieldItem("안내 주기 (분)",minutes){Top=17,Bottom=0},new ControlItem(savePetSpeech,false),new ControlItem(previewPetSpeech,false)){Columns=new[]{-1f,0f,0f},AlignEnd=true,Top=14});
            speech.Add(Css.Hint("1~1,440분 사이로 설정하세요. 시간 알림은 확인할 때까지 말풍선에 남고, 펫을 끄면 알림창으로 알려드립니다."));pets.Add(speech);
            var chatter=Sub();chatter.Add(Css.H3("일상 대화"),new CssCheck(chatterEnabled,12),Css.Hint("펫이 가끔 말을 걸어요."));pets.Add(chatter);
            var life=Sub();life.Add(Css.H3("침대 · 장난감 · 일기"),new CssCheck(toyEnabled,12),Css.Hint("펫 근처로 마우스를 가져가면 털실 공이 달리고, 펫들이 쫓아와요."),new CssCheck(bedEnabled,12,10),Css.Hint("밤(23시~6시)이나 10분 넘게 자리를 비우면 펫들이 침대로 가서 자요. 침대는 끌어서 옮기고, 우클릭으로 치울 수 있어요."),new RowItem(8,new FieldItem("일기 쓰는 시간",diaryTime){Top=0,Bottom=0},new ControlItem(openDiary,false)){Columns=new[]{-1f,0f},AlignEnd=true,Top=14},Css.Hint("이 시간이 지나면 펫이 그날 하루를 일기로 남겨요. 퇴근 전에 맞춰 두면 좋아요."));pets.Add(life);
            var bond=Sub();bond.Add(Css.H3("호감도 · 간식"),Css.P("할 일·일정을 끝내고, 쓰다듬고, 함께 놀수록 펫과 가까워져요. 단계가 오르면 말투와 대사가 바뀌어요."),affinityList,snackStatus,new RowItem(8,new FieldItem("나를 부르는 호칭",userTitle){Top=0,Bottom=0},new ControlItem(saveTitle,false)){Columns=new[]{-1f,0f},AlignEnd=true,Top=14},Css.Hint("‘가족’ 단계가 되면 펫이 이 호칭으로 불러요."));pets.Add(bond);
            pets.Add(petsStatus);petGroup.Add(pets);Body.Add(petGroup);
            // 단축키 · 바탕화면 위젯
            keyGroup=new GroupItem("단축키 · 바탕화면 위젯",false);var keys=Section();
            keys.Add(Css.H3("창 보이기 · 숨기기 단축키"),Css.P("어디서든 단축키로 창을 숨기거나 다시 열 수 있어요. 숨겨도 위젯과 알림은 계속 작동합니다."),new CssCheck(hotkeyEnabled,12));
            var mods=new BoxItem();mods.Add(new TextItem("조합키",11,700,DesktopTheme.Ink){Bottom=11});var modRow=new InlineItem(12,ctrl,alt,shift){RowGap=10,Bottom=10};mods.Add(modRow);
            var keyField=new FieldItem("기본키",hotkeyKey){Bottom=0};var grid=new RowItem(15,mods,keyField){Columns=new[]{-1f,110f},AlignEnd=true,Top=18,Bottom=14};keys.Add(grid);
            var actions=new InlineItem(7,preview,resetHotkey,saveHotkey){SplitFirst=true};keys.Add(actions);
            hotkeyStatus.Top=hotkeyStatus.Bottom=Css.Px(11);keys.Add(hotkeyStatus);
            var widget=Section();widget.RuleTop=true;widget.PadTop=18;widget.Add(Css.H3("바탕화면 위젯"),Css.P("작은 달력과 일정을 바탕화면에 띄워두세요."),new InlineItem(8,toggleWidget),new CssCheck(widgetTopmost,12,12),new CssCheck(widgetLocked,12,12));
            keyGroup.Add(keys,widget);Body.Add(keyGroup);
            // 일정 알림
            notifyGroup=new GroupItem("일정 알림",false);var notify=Section();
            notify.Add(Css.H3("일정 알림"),notificationDescription,new InlineItem(8,enableNotifications,testNotification),permissionStatus,new CssCheck(notificationSound,12),new FieldItem("새 일정의 기본 알림",defaultReminder){Top=17,Bottom=0});
            notifyGroup.Add(notify);Body.Add(notifyGroup);
            // 데이터 · 백업
            dataGroup=new GroupItem("데이터 · 백업",false);var data=Section();
            data.Add(Css.H3("소중한 기록, 안전하게"),Css.P("일정, 할 일, 캘린더 설정과 펫·위젯 설정을 함께 백업합니다."),new InlineItem(8,exportBackup,importBackup),Css.Hint("복원하면 현재 기록을 교체합니다. 교체 전 일정과 할 일은 자동 백업에 남습니다."));
            dataGroup.Add(data);Body.Add(dataGroup);
            // 화면
            screenGroup=new GroupItem("화면",false);var screen=Section();screen.Add(Css.H3("글씨"),new FieldItem("글씨 크기",textScale){Top=17,Bottom=0},new FieldItem("글꼴",fontFamily,"캘린더 창과 입력창에 쓰는 글꼴이에요. 위젯과 펫 말풍선은 기본 글꼴을 써요."){Top=17,Bottom=0});screenGroup.Add(screen);Body.Add(screenGroup);
            // 일정 분류
            categoryGroup=new GroupItem("일정 분류",false);var cats=Section();
            var list=new PaintItem{Top=12,Bottom=4};
            list.OnMeasure=(p,w)=>{float s=p.Sc;int n=DesktopTheme.CustomCategories.Length;return (int)Math.Ceiling(n==0?Css.Px(11)*1.8f*s:n*36*s);};
            list.OnPaint=(p,g,r)=>{float s=p.Sc;categoryDeletes.Clear();var all=DesktopTheme.CustomCategories;
                if(all.Length==0){Ui.Text(g,"아직 만든 분류가 없어요.",Ui.Font(s,11),new Rectangle(r.X,r.Y,r.Width,r.Height),Ui.C("#a29276"),TextFormatFlags.VerticalCenter);return;}
                int y=r.Y;foreach(var c in all){var row=new Rectangle(r.X,y,r.Width,(int)(32*s));Ui.Box(g,row,8*s,Ui.C("#fffaf1"),DesktopTheme.Line,s);
                    using(var b=new SolidBrush(DesktopTheme.Category(c.id)))g.FillEllipse(b,row.X+12*s,row.Y+(row.Height-10*s)/2,10*s,10*s);
                    Ui.Text(g,c.name,Ui.Font(s,12,700,null,c.name),new Rectangle(row.X+(int)(30*s),row.Y,row.Width-(int)(100*s),row.Height),DesktopTheme.Ink,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                    var del=new Rectangle(row.Right-(int)(56*s),row.Y,(int)(48*s),row.Height);Ui.Text(g,"삭제",Ui.Font(s,11,400,null,"삭제"),del,Ui.C("#b34d35"),TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter);
                    categoryDeletes.Add(new KeyValuePair<Rectangle,string>(del,c.id));y+=(int)(36*s);}};
            list.OnClick=pt=>{foreach(var d in categoryDeletes)if(d.Key.Contains(pt)){DeleteCategory(d.Value);return true;}return false;};
            list.IsHot=pt=>categoryDeletes.Any(d=>d.Key.Contains(pt));
            var addRowC=new RowItem(8,new FieldItem("새 분류 이름",categoryName){Top=0,Bottom=0},new FieldItem("색",categoryColor){Top=0,Bottom=0},new ControlItem(addCategory,false)){Columns=new[]{-1f,96f,0f},AlignEnd=true,Top=14};
            cats.Add(Css.H3("나만의 분류"),Css.P("업무 · 회의 · 마감 · 개인 말고도 ‘운동’, ‘공부’처럼 원하는 분류를 색과 함께 만들 수 있어요."),list,addRowC,Css.Hint("분류를 지우면 그 분류의 일정은 ‘업무’로 바뀌어요. 최대 20개까지 만들 수 있어요."));
            categoryGroup.Add(cats);Body.Add(categoryGroup);
            addCategory.Click+=delegate{AddCategory();};categoryName.Box.KeyDown+=delegate(object sender,KeyEventArgs k){if(k.KeyCode==System.Windows.Forms.Keys.Enter){k.Handled=k.SuppressKeyPress=true;AddCategory();}};
            FixedHeightCss=760;
            // Behaviour
            petsEnabled.CheckedChanged+=delegate{if(!syncing)UpdatePets(petsEnabled.Checked?"enable":"disable");};
            chatterEnabled.CheckedChanged+=delegate{if(!syncing)UpdatePets("configureChatter",null,new Dictionary<string,object>{{"chatterEnabled",chatterEnabled.Checked}});};
            addPet.Click+=delegate{UpdatePets("spawn",species.Value);};
            bedEnabled.CheckedChanged+=delegate{if(!syncing)UpdatePets("configureBed",null,new Dictionary<string,object>{{"bedEnabled",bedEnabled.Checked}});};
            openDiary.Click+=delegate{View.OpenDiary();};
            saveTitle.Click+=delegate{SaveTitle();};userTitle.Box.KeyDown+=delegate(object sender,KeyEventArgs k){if(k.KeyCode==System.Windows.Forms.Keys.Enter){k.Handled=k.SuppressKeyPress=true;SaveTitle();}};
            diaryTime.ValueChanged+=delegate{if(!syncing&&diaryTime.Value!="")UpdatePets("configureBed",null,new Dictionary<string,object>{{"diaryTime",diaryTime.Value}});};
            toyEnabled.CheckedChanged+=delegate{if(!syncing)UpdatePets("configureBed",null,new Dictionary<string,object>{{"toyEnabled",toyEnabled.Checked}});};
            summaryEnabled.CheckedChanged+=delegate{if(syncing)return;petSpeechDirty=true;minutes.Enabled=summaryEnabled.Checked;};
            minutes.ValueChanged+=delegate{if(!syncing)petSpeechDirty=true;};
            savePetSpeech.Click+=delegate{string invalid=minutes.Validity();if(invalid!=null){Validation.Report(minutes,invalid);return;}UpdatePets("configureSpeech",null,new Dictionary<string,object>{{"summaryEnabled",summaryEnabled.Checked},{"summaryIntervalMinutes",int.Parse(minutes.Text)}});};
            previewPetSpeech.Click+=delegate{UpdatePets("preview");};
            foreach(var c in new[]{hotkeyEnabled,ctrl,alt,shift})c.CheckedChanged+=delegate{if(!syncing)PreviewHotkey();};hotkeyKey.ValueChanged+=delegate{PreviewHotkey();};
            resetHotkey.Click+=delegate{RenderHotkey(new Hotkey());SetHotkeyStatus("기본값을 선택했습니다. ‘단축키 적용’을 누르면 저장됩니다.",false);};
            saveHotkey.Click+=SaveHotkey;
            toggleWidget.Click+=delegate{UpdateWidget(widgetPreference!=null&&(bool)widgetPreference["enabled"]?"hide":"show");};
            widgetTopmost.CheckedChanged+=delegate{if(!syncing)UpdateWidget("configure",new Dictionary<string,object>{{"topmost",widgetTopmost.Checked}});};
            widgetLocked.CheckedChanged+=delegate{if(!syncing)UpdateWidget("configure",new Dictionary<string,object>{{"locked",widgetLocked.Checked}});};
            enableNotifications.Click+=async delegate{await View.Commit(s=>{s.settings.desktopPopups=!s.settings.desktopPopups;});};
            testNotification.Click+=async delegate{View.Toast("알림이 잘 도착했어요","펫 캘린더가 다가오는 일정을 알려드립니다.","",15000);try{await View.Api("POST","/api/desktop/notification-test",new Dictionary<string,object>());}catch(Exception error){View.Toast("데스크톱 알림을 표시하지 못했어요",error.Message,"error");}};
            notificationSound.CheckedChanged+=async delegate{if(syncing)return;bool value=notificationSound.Checked;if(!await View.Commit(s=>{s.settings.notificationSound=value;}))Sync();};
            defaultReminder.ValueChanged+=async delegate{if(syncing)return;string value=defaultReminder.Value;await View.Commit(s=>{s.settings.defaultReminder=value=="null"?(int?)null:int.Parse(value);});};
            fontFamily.ValueChanged+=async delegate{if(syncing)return;string value=fontFamily.Value;if(!await View.Commit(s=>{s.settings.fontFamily=value;}))Sync();};
            textScale.ValueChanged+=async delegate{if(syncing)return;int value=int.Parse(textScale.Value);if(!await View.Commit(s=>{s.settings.textScale=value;}))Sync();};
            exportBackup.Click+=ExportBackup;importBackup.Click+=ImportBackup;
            Sync();RenderHotkey(View.state.settings.hotkey);RefreshHotkeyStatus();UpdateWidget(null);UpdatePets(null);
        }
        async void AddCategory(){
            string name=categoryName.Text.Trim();if(name.Length==0){Validation.Report(categoryName,"분류 이름을 적어 주세요.");return;}
            var existing=View.state.settings.categories??new CategoryDef[0];
            if(existing.Length>=20){View.Toast("분류는 20개까지 만들 수 있어요","쓰지 않는 분류를 지운 뒤 다시 추가해 주세요.","error");return;}
            if(existing.Any(c=>c.name==name)||CalendarView.AllCategories().Take(4).Any(k=>DesktopTheme.CategoryName(k)==name)){Validation.Report(categoryName,"이미 있는 분류 이름이에요.");return;}
            var def=new CategoryDef{id="c-"+Guid.NewGuid().ToString("N").Substring(0,8),name=name,color=categoryColor.Value};
            if(await View.Commit(s=>{s.settings.categories=(s.settings.categories??new CategoryDef[0]).Concat(new[]{def}).ToArray();})){categoryName.Text="";View.Toast("분류를 만들었어요",name);}
        }
        async void DeleteCategory(string id){
            var def=(View.state.settings.categories??new CategoryDef[0]).FirstOrDefault(c=>c.id==id);if(def==null)return;
            int used=View.state.events.Count(e=>e.category==id);
            if(used>0&&!await View.Confirm("분류를 삭제할까요?","‘"+def.name+"’ 일정 "+used+"개는 ‘업무’로 바뀝니다.","삭제"))return;
            if(await View.Commit(s=>{s.settings.categories=(s.settings.categories??new CategoryDef[0]).Where(c=>c.id!=id).ToArray();foreach(var e in s.events)if(e.category==id)e.category="work";}))View.CategoryRemoved(id);
        }
        void SaveTitle(){string value=userTitle.Text.Trim();if(value.Length==0){Validation.Report(userTitle,"호칭을 적어 주세요.");return;}UpdatePets("configureBed",null,new Dictionary<string,object>{{"title",value}});View.Toast("호칭을 바꿨어요","‘가족’이 된 펫이 이제 ‘"+value+"’(이)라고 불러요.");}
        // "초코 · 친구 ♥ 72%" per pet, from the pets status.
        void RenderAffinity(Dictionary<string,object> result){
            object raw;var lines=new List<string>();
            if(result!=null&&result.TryGetValue("affinity",out raw)){var items=raw as object[]??(raw is System.Collections.ArrayList?((System.Collections.ArrayList)raw).ToArray():new object[0]);
                foreach(var o in items.OfType<Dictionary<string,object>>()){object n,l,pc,h,f;o.TryGetValue("name",out n);o.TryGetValue("label",out l);o.TryGetValue("percent",out pc);o.TryGetValue("hearts",out h);o.TryGetValue("fed",out f);int lv=Number(o,"level");
                    lines.Add("• "+n+" · "+l+(h as string!=""?" "+h:"")+(lv<5?"  ("+pc+"%)":"")+(f is bool&&(bool)f?"  · 배불러요":""));}}
            affinityList.Set(lines.Count==0?"함께하는 펫이 없어요.":String.Join("\n",lines));
            snackStatus.Set("간식 "+Number(result,"snacks")+"개 ·할 일이나 일정을 끝내면 하나씩 생겨요. 펫을 우클릭해서 줄 수 있고, 한 번 먹으면 10분 뒤에 또 먹어요.");
            object t;if(result!=null&&result.TryGetValue("title",out t)&&t is string&&!userTitle.Focused)userTitle.Text=(string)t;
        }
        static BoxItem Section(){return new BoxItem{PadTop=6,PadBottom=16};}
        static BoxItem Sub(){return new BoxItem{RuleTop=true,Top=20,PadTop=18};}
        public void Reveal(string section){if(section=="notifications"){notifyGroup.Open=true;Relayout();Body.Scroll.Offset=notifyGroup.Bounds.Y;Body.Scroll.Clamp();Body.Reflow();}}
        public override void StateChanged(){Sync();Relayout();}
        // renderNotifications() and the settings echoes from render().
        void Sync(){
            syncing=true;try{
                var settings=View.state.settings;bool enabled=settings.desktopPopups;
                notificationDescription.Set("창을 숨겨도 알려드립니다. 데스크톱 펫을 켜면 펫의 말풍선으로, 끄면 알림창으로 표시합니다. 알림은 확인할 때까지 유지됩니다. PC 절전 중에는 지연될 수 있으며, 종일 일정은 오전 9시 기준입니다.");
                enableNotifications.Text=enabled?"데스크톱 알림 끄기":"데스크톱 알림 켜기";
                permissionStatus.Set(enabled?"확인할 때까지 알림 유지 · 알림을 누르면 해당 일정을 엽니다.":"화면 안 알림만 사용합니다. 데스크톱 알림은 언제든 켤 수 있어요.");
                notificationSound.Checked=settings.notificationSound;defaultReminder.Value=Css.ReminderKey(settings.defaultReminder);textScale.Value=settings.textScale.ToString();fontFamily.Value=settings.fontFamily??"default";
            }finally{syncing=false;}
            if(IsHandleCreated)Body.Reflow();
        }
        Hotkey Draft(){return new Hotkey{enabled=hotkeyEnabled.Checked,modifiers=(ctrl.Checked?2:0)|(alt.Checked?1:0)|(shift.Checked?4:0),key=int.Parse(hotkeyKey.Value)};}
        void PreviewHotkey(){var value=Draft();preview.Text=value.enabled?Rules.HotkeyLabel(value):"사용 안 함";foreach(Control c in new Control[]{ctrl,alt,shift,hotkeyKey})c.Enabled=value.enabled;}
        void RenderHotkey(Hotkey value){syncing=true;try{hotkeyEnabled.Checked=value.enabled;ctrl.Checked=(value.modifiers&2)!=0;alt.Checked=(value.modifiers&1)!=0;shift.Checked=(value.modifiers&4)!=0;hotkeyKey.Value=value.key.ToString();}finally{syncing=false;}PreviewHotkey();}
        void SetHotkeyStatus(string text,bool error){hotkeyStatus.Set(text);hotkeyStatus.Color=error?Ui.C("#b64238"):Ui.C("#79694f");if(IsHandleCreated)Body.Reflow();}
        async void RefreshHotkeyStatus(){
            try{var status=await View.Api("GET","/api/hotkey");string error=status["error"] as string;SetHotkeyStatus(!String.IsNullOrEmpty(error)?error:(bool)status["active"]?"사용 중 · "+status["label"]:"창 단축키가 꺼져 있습니다.",!String.IsNullOrEmpty(error));}
            catch(Exception error){SetHotkeyStatus(error.Message,true);}
        }
        async void SaveHotkey(object sender,EventArgs e){
            if(View.Saving)return;Hotkey value=Draft();
            try{Rules.ValidateHotkey(value);}catch(Exception error){SetHotkeyStatus(error.Message,true);return;}
            saveHotkey.Enabled=false;SetHotkeyStatus("단축키 적용 중…",false);
            try{
                if(await View.Commit(s=>{s.settings.hotkey=value;},true)){RenderHotkey(View.state.settings.hotkey);RefreshHotkeyStatus();View.Toast(value.enabled?"창 단축키를 설정했어요":"창 단축키를 껐어요",value.enabled?Rules.HotkeyLabel(value):"다른 프로그램에서 이 키를 다시 사용할 수 있습니다.");}
                else SetHotkeyStatus(View.LastError,true);
            }finally{if(!IsDisposed)saveHotkey.Enabled=true;}
        }
        static bool Flag(Dictionary<string,object> d,string key){object v;return d!=null&&d.TryGetValue(key,out v)&&v is bool&&(bool)v;}
        static int Number(Dictionary<string,object> d,string key){object v;return d!=null&&d.TryGetValue(key,out v)&&v!=null?Convert.ToInt32(v):0;}
        public async void UpdatePets(string action,string speciesId=null,Dictionary<string,object> values=null){
            if(petsSaving){if(action==null)petsRefreshPending=true;return;}
            petsSaving=true;var controls=new Control[]{petsEnabled,species,addPet,summaryEnabled,minutes,savePetSpeech,previewPetSpeech,chatterEnabled};foreach(var c in controls)c.Enabled=false;
            try{
                Dictionary<string,object> result;
                if(action==null)result=await View.Api("GET","/api/pets");
                else{var body=new Dictionary<string,object>{{"action",action}};if(speciesId!=null)body["species"]=speciesId;if(values!=null)foreach(var v in values)body[v.Key]=v.Value;result=await View.Api("POST","/api/pets",body);}
                if(IsDisposed)return;petsPreference=result;syncing=true;
                try{petsEnabled.Checked=Flag(result,"enabled");chatterEnabled.Checked=Flag(result,"chatterEnabled");bedEnabled.Checked=Flag(result,"bedEnabled");RenderAffinity(result);toyEnabled.Checked=Flag(result,"toyEnabled");object dt;if(result.TryGetValue("diaryTime",out dt)&&dt is string&&diaryTime.Value!=(string)dt)diaryTime.Value=(string)dt;if(action=="configureSpeech")petSpeechDirty=false;if(!petSpeechDirty){summaryEnabled.Checked=Flag(result,"summaryEnabled");minutes.Text=Number(result,"summaryIntervalMinutes").ToString();}}finally{syncing=false;}
                int count=Number(result,"count");petsStatus.Set(Flag(result,"enabled")?(count>0?"펫 "+count+"마리와 함께하고 있어요.":"데스크톱 펫이 켜져 있어요. 원하는 펫을 추가해 보세요."):"데스크톱 펫이 꺼져 있습니다.");
            }catch(Exception error){if(IsDisposed)return;syncing=true;petsEnabled.Checked=Flag(petsPreference,"enabled");syncing=false;View.Toast("데스크톱 펫을 확인해 주세요",error.Message,"error");}
            finally{
                petsSaving=false;
                if(!IsDisposed){syncing=true;chatterEnabled.Checked=petsPreference==null||Flag(petsPreference,"chatterEnabled");syncing=false;chatterEnabled.Enabled=true;petsEnabled.Enabled=true;
                    species.Enabled=addPet.Enabled=Flag(petsPreference,"enabled");summaryEnabled.Enabled=true;minutes.Enabled=summaryEnabled.Checked;savePetSpeech.Enabled=true;previewPetSpeech.Enabled=Flag(petsPreference,"enabled")&&Number(petsPreference,"count")>0;Body.Reflow();
                    if(petsRefreshPending){petsRefreshPending=false;BeginInvoke(new Action(()=>UpdatePets(null)));}}
            }
        }
        public async void UpdateWidget(string action,Dictionary<string,object> values=null){
            if(widgetSaving)return;widgetSaving=true;var controls=new Control[]{toggleWidget,widgetTopmost,widgetLocked};foreach(var c in controls)c.Enabled=false;
            try{
                Dictionary<string,object> result;
                if(action==null)result=await View.Api("GET","/api/widget");
                else{var body=new Dictionary<string,object>{{"action",action}};if(values!=null)foreach(var v in values)body[v.Key]=v.Value;result=await View.Api("POST","/api/widget",body);}
                if(IsDisposed)return;widgetPreference=result;syncing=true;try{toggleWidget.Text=Flag(result,"enabled")?"위젯 닫기":"위젯 열기";widgetTopmost.Checked=Flag(result,"topmost");widgetLocked.Checked=Flag(result,"locked");}finally{syncing=false;}
            }catch(Exception error){if(IsDisposed)return;if(widgetPreference!=null){syncing=true;widgetTopmost.Checked=Flag(widgetPreference,"topmost");widgetLocked.Checked=Flag(widgetPreference,"locked");syncing=false;}View.Toast("위젯 설정을 확인해 주세요",error.Message,"error");}
            finally{widgetSaving=false;if(!IsDisposed){foreach(var c in controls)c.Enabled=true;Body.Reflow();}}
        }
        async void ExportBackup(object sender,EventArgs e){
            Dictionary<string,object> backup;
            try{backup=await View.Api("GET","/api/backup");}catch(Exception error){View.Toast("백업하지 못했어요",error.Message,"error");return;}
            using(var dialog=new SaveFileDialog{FileName="펫캘린더-백업-"+Rules.Key(DateTime.Today)+".json",Filter="JSON 파일 (*.json)|*.json",DefaultExt="json",AddExtension=true,OverwritePrompt=true}){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                try{File.WriteAllText(dialog.FileName,JsonPretty.Write(backup),new UTF8Encoding(false));View.Toast("백업 파일을 내보냈어요","저장한 위치에서 백업 파일을 확인해 주세요.");}
                catch(Exception error){View.Toast("백업하지 못했어요",error.Message,"error");}
            }
        }
        async void ImportBackup(object sender,EventArgs e){
            string file;using(var dialog=new OpenFileDialog{Filter="JSON 파일 (*.json)|*.json",CheckFileExists=true}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;file=dialog.FileName;}
            try{
                if(new FileInfo(file).Length>5*1024*1024)throw new AppError(400,"백업 파일은 5MB 이하여야 합니다.");
                string text=File.ReadAllText(file,Encoding.UTF8).TrimStart('﻿');Dictionary<string,object> backup;
                try{backup=Codec.Object(text);}catch{throw new AppError(400,"올바른 JSON 백업 파일이 아닙니다.");}
                if(backup==null)throw new AppError(400,"지원하는 펫 캘린더 백업 파일이 아닙니다.");
                bool combined=backup.ContainsKey("calendar")&&backup["calendar"]!=null;
                var imported=Rules.Validate(Codec.Json(combined?backup["calendar"]:backup));
                if(!await View.Confirm("백업을 복원할까요?","일정 "+imported.events.Length+"개, 할 일 "+imported.tasks.Length+"개로 현재 기록을 교체합니다.\n교체 전 기록은 이 PC의 자동 백업에 보관합니다.","복원"))return;
                if(combined){
                    if(View.Saving)return;backup["expectedRevision"]=View.state.revision;
                    var response=await View.CallApi("POST","/api/backup",Codec.Json(backup),false);
                    if(response.status!=200){var o=Codec.Object(Codec.Json(response.body));throw new Exception(o!=null&&o.ContainsKey("error")?o["error"] as string:"백업을 복원하지 못했습니다.");}
                    View.state=(CalendarState)response.body;View.Render();UpdateWidget(null);UpdatePets(null);View.Toast("백업을 복원했어요","일정, 할 일, 펫과 위젯 설정을 불러왔습니다.");
                }else if(await View.Commit(s=>{s.events=imported.events;s.tasks=imported.tasks;s.settings=imported.settings;}))View.Toast("백업을 복원했어요","일정과 할 일, 설정을 불러왔습니다.");
            }catch(Exception error){View.Toast("백업 파일을 확인해 주세요",error.Message,"error",10000);}
        }
    }
    // JSON.stringify(value,null,2) for the backup file.
    public static class JsonPretty {
        public static string Write(object value){var b=new StringBuilder();Emit(Codec.Serializer().DeserializeObject(Codec.Json(value)),b,0);return b.ToString();}
        static void Emit(object v,StringBuilder b,int depth){
            var dict=v as Dictionary<string,object>;var list=v as object[];string pad=new string(' ',depth*2+2),close=new string(' ',depth*2);
            if(dict!=null){if(dict.Count==0){b.Append("{}");return;}b.Append("{\n");int i=0;foreach(var p in dict){b.Append(pad).Append(Codec.Json(p.Key)).Append(": ");Emit(p.Value,b,depth+1);if(++i<dict.Count)b.Append(',');b.Append('\n');}b.Append(close).Append('}');return;}
            if(list!=null){if(list.Length==0){b.Append("[]");return;}b.Append("[\n");for(int i=0;i<list.Length;i++){b.Append(pad);Emit(list[i],b,depth+1);if(i<list.Length-1)b.Append(',');b.Append('\n');}b.Append(close).Append(']');return;}
            b.Append(v==null?"null":Codec.Json(v));
        }
    }
}
