using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace PetCalendarDesktop {
    // Runs before the calendar starts. When calendar.json cannot be read, the app used to stop
    // with an error; now the user picks one of the automatic backups (newest first, with its
    // time and how many schedules and tasks it holds) or starts empty. Either way the unreadable
    // file is first copied aside, so nothing is lost.
    internal static class CalendarRecovery {
        internal sealed class Candidate {public string Path;public DateTime When;public int Events,Tasks;}
        // Returns false when the user closed the window instead of choosing.
        internal static bool Run(string dataDir,Action<string> announce){
            string file=Path.Combine(dataDir,"calendar.json");if(!File.Exists(file))return true;
            string error;
            try{Rules.Validate(File.ReadAllText(file,Encoding.UTF8));return true;}catch(Exception e){error=e.Message;}
            var candidates=Candidates(dataDir);
            if(announce!=null)announce(Codec.Json(new{kind="recovery",error,backups=candidates.Select(c=>new{file=System.IO.Path.GetFileName(c.Path),events=c.Events,tasks=c.Tasks}).ToArray()}));
            using(var dialog=new RecoveryDialog(error,candidates)){
                if(dialog.ShowDialog()!=DialogResult.OK)return false;
                DataRecovery.Preserve(file,"일정");
                string text=dialog.Choice==null?Codec.Json(new CalendarState()):File.ReadAllText(dialog.Choice.Path,Encoding.UTF8);
                StateStore.Atomic(file,text,null);
                lock(DataRecovery.Notices)DataRecovery.Notices.Add(dialog.Choice==null?"빈 달력으로 새로 시작했어요.":"자동 백업("+dialog.Choice.When.ToString("M월 d일 HH:mm",CultureInfo.GetCultureInfo("ko-KR"))+")으로 일정 "+dialog.Choice.Events+"개, 할 일 "+dialog.Choice.Tasks+"개를 복구했어요.");
                return true;
            }
        }
        static List<Candidate> Candidates(string dataDir){
            var list=new List<Candidate>();string folder=Path.Combine(dataDir,"backups");if(!Directory.Exists(folder))return list;
            foreach(var path in Directory.GetFiles(folder,"calendar-*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(60)){
                try{var state=Rules.Validate(File.ReadAllText(path,Encoding.UTF8));list.Add(new Candidate{Path=path,When=File.GetLastWriteTime(path),Events=state.events.Length,Tasks=state.tasks.Length});}
                catch(Exception){} // an unreadable backup is not offered
                if(list.Count>=30)break;
            }
            return list;
        }
    }

    internal sealed class RecoveryDialog : Form {
        readonly ListView list=new ListView{View=View.Details,FullRowSelect=true,MultiSelect=false,HideSelection=false,HeaderStyle=ColumnHeaderStyle.Nonclickable};
        readonly Button restore=new Button{Text="이 백업으로 복구",AutoSize=true},empty=new Button{Text="빈 달력으로 시작",AutoSize=true},quit=new Button{Text="닫기",AutoSize=true,DialogResult=DialogResult.Cancel};
        readonly List<CalendarRecovery.Candidate> candidates;
        internal CalendarRecovery.Candidate Choice;
        internal RecoveryDialog(string error,List<CalendarRecovery.Candidate> backups){
            candidates=backups;Text="펫 캘린더 · 일정 복구";StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;
            AutoScaleMode=AutoScaleMode.Dpi;Font=DesktopTheme.UiFont(10);BackColor=DesktopTheme.Card;ForeColor=DesktopTheme.Ink;ClientSize=new Size(560,460);ShowInTaskbar=true;TopMost=true;
            var title=new Label{Text="일정 파일을 읽지 못했어요",Font=DesktopTheme.UiFont(14,FontStyle.Bold),AutoSize=true,Location=new Point(20,18)};
            var body=new Label{Text=(backups.Count>0?"자동으로 만들어 둔 백업 중 하나로 복구할 수 있어요. 읽지 못한 파일은 지우지 않고 따로 보관해요.":"복구할 자동 백업이 없어요. 읽지 못한 파일은 따로 보관하고 빈 달력으로 시작할 수 있어요.")+"\n\n원인: "+Short(error),
                AutoSize=false,Location=new Point(20,52),Size=new Size(520,96),ForeColor=Color.FromArgb(0x79,0x69,0x4f)};
            list.Location=new Point(20,156);list.Size=new Size(520,236);
            list.Columns.Add("저장 시각",250);list.Columns.Add("일정",120,HorizontalAlignment.Right);list.Columns.Add("할 일",120,HorizontalAlignment.Right);
            foreach(var c in backups){var item=new ListViewItem(c.When.ToString("yyyy. M. d. (ddd) HH:mm:ss",CultureInfo.GetCultureInfo("ko-KR")));item.SubItems.Add(c.Events+"개");item.SubItems.Add(c.Tasks+"개");item.Tag=c;list.Items.Add(item);}
            if(list.Items.Count>0)list.Items[0].Selected=true;
            list.Visible=backups.Count>0;list.ItemActivate+=delegate{Accept();}; // double-click or Enter on a row
            var buttons=new FlowLayoutPanel{FlowDirection=FlowDirection.RightToLeft,Location=new Point(20,404),Size=new Size(520,40),WrapContents=false};
            buttons.Controls.Add(quit);if(backups.Count>0)buttons.Controls.Add(restore);buttons.Controls.Add(empty);
            restore.Click+=delegate{Accept();};
            empty.Click+=delegate{if(MessageBox.Show(this,"빈 달력으로 시작할까요?\n읽지 못한 파일은 data 폴더에 따로 보관돼요.","일정 복구",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;Choice=null;DialogResult=DialogResult.OK;};
            list.SelectedIndexChanged+=delegate{restore.Enabled=list.SelectedItems.Count>0;};
            Controls.AddRange(new Control[]{title,body,list,buttons});AcceptButton=backups.Count>0?restore:empty;CancelButton=quit;ActiveControl=backups.Count>0?(Control)list:empty;
        }
        void Accept(){if(list.SelectedItems.Count==0)return;Choice=(CalendarRecovery.Candidate)list.SelectedItems[0].Tag;DialogResult=DialogResult.OK;}
        static string Short(string text){text=(text??"").Replace("\r","").Replace("\n"," ");return text.Length>160?text.Substring(0,160)+"…":text;}
    }
}
