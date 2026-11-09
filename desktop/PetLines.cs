using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PetCalendarDesktop {
    // Affinity: points per pet, five levels, and the dialogue for each level.
    // The lines themselves live in PetLinesData.cs (generated from docs/pet-affinity-lines.md).
    internal static partial class PetLines {
        // Points needed for level 1..5.
        internal static readonly int[] Thresholds={0,40,150,400,900};
        static readonly string[] names={"낯가림","친해지는 중","친구","단짝","가족"};
        // What the pets call the user (settings), "집사" by default.
        internal static string UserTitle="집사";
        static readonly Random random=new Random();
        static readonly Dictionary<string,int> lastPick=new Dictionary<string,int>();

        internal static int Level(int points){int level=1;for(int i=0;i<Thresholds.Length;i++)if(points>=Thresholds[i])level=i+1;return level;}
        internal static string LevelName(int level){return names[Math.Max(1,Math.Min(5,level))-1];}
        // Progress inside the current level, 0..100 (100 at the top level).
        internal static int Percent(int points){int level=Level(points);if(level>=5)return 100;int low=Thresholds[level-1],high=Thresholds[level];return (int)Math.Floor((points-low)*100.0/(high-low));}
        internal static string Hearts(int level){return level>=5?" 💖":level==4?" 💚":" 🌟";}
        static int Tier(int level){return level>=5?2:level>=3?1:0;}

        // One line for a situation (morning, done1, doneAll, pet, toy, longtime, diaryEnd, snack),
        // alternating so the same line is not repeated back to back.
        internal static string Say(string species,int level,string situation){
            string[] options;string key=species+"|"+Tier(level)+"|"+situation;
            if(!lines.TryGetValue(key,out options)&&!lines.TryGetValue("cat|"+Tier(level)+"|"+situation,out options))return null;
            int last;lastPick.TryGetValue(key,out last);int pick=options.Length<2?0:random.Next(options.Length);
            if(options.Length>1&&lastPick.ContainsKey(key)&&pick==last)pick=(pick+1)%options.Length;lastPick[key]=pick;
            return Title(options[pick]);
        }
        internal static string LevelUp(string species,int level){string line;return levelUps.TryGetValue(species+"|"+level,out line)?Title(line):null;}
        internal static string Full(string species){string line;return levelUps.TryGetValue(species+"|full",out line)?Title(line):"아까 먹어서 배불러요. 조금 이따가요 💤";}
        // Everyday lines unlocked up to this level (added to the regular chatter).
        internal static string[] Unlocked(string species,int level){
            var result=new List<string>();for(int l=2;l<=level;l++){string[] add;if(unlocks.TryGetValue(species+"|"+l,out add))result.AddRange(add.Select(Title));}return result.ToArray();
        }

        static bool Batchim(string word){if(String.IsNullOrEmpty(word))return false;char last=word[word.Length-1];return last>=0xac00&&last<=0xd7a3&&(last-0xac00)%28!=0;}
        // Fill {호칭} and fix the particle right after it (이/가, 은/는, 이랑/랑, 이다/다, 이야/야).
        internal static string Title(string text){
            if(text==null||text.IndexOf("{호칭}",StringComparison.Ordinal)<0)return text;
            string title=String.IsNullOrWhiteSpace(UserTitle)?"집사":UserTitle.Trim();bool b=Batchim(title);
            return Regex.Replace(text,"\\{호칭\\}(이랑|이야|이|은|다)?",m=>{
                string p=m.Groups[1].Value;
                switch(p){case "이랑":return title+(b?"이랑":"랑");case "이야":return title+(b?"이야":"야");case "이":return title+(b?"이":"가");case "은":return title+(b?"은":"는");case "다":return title+(b?"이다":"다");default:return title;}
            });
        }
    }
}
