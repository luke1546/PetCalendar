using System;
using System.Windows.Forms;
namespace DesktopPet {
    internal static class PetMenu {
        internal static string Noun="동물";
        internal static ToolStripMenuItem Add(Action<Species> add) {
            var menu=new ToolStripMenuItem(Noun+" 추가");
            menu.DropDownItems.Add("랜덤 한 마리",null,delegate {add(null);});menu.DropDownItems.Add(new ToolStripSeparator());
            foreach(var species in Species.All) {var choice=species;menu.DropDownItems.Add(choice.Name,null,delegate {add(choice);});}
            return menu;
        }
    }
}
