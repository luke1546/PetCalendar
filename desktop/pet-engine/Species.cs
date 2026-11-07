using System;
using System.Collections.Generic;

namespace DesktopPet {
    public sealed class Species {
        public readonly string Id,Name,Asset;
        public readonly double Scale,Speed,JumpHeight,MaxClimb,Stride,Tempo;
        public readonly bool NeutralCoat;
        public readonly int[] Rising,Falling;
        public bool CanJump {get {return JumpHeight>0;}}
        Species(string id,string name,string asset,double scale,double speed,double jump,double climb,double stride,double tempo,bool neutral) {
            Id=id;Name=name;Asset=asset;Scale=scale;Speed=speed;JumpHeight=jump;MaxClimb=climb;Stride=stride;Tempo=tempo;NeutralCoat=neutral;
            Rising=id=="cat"?new[]{36,37,38}:id=="dog" || id=="alpaca"?new[]{35,36,37}:new[]{35,36,37,38};
            Falling=id=="cat"?new[]{40,41,42}:id=="elephant"?new[]{40,41,42,43}:new[]{40,41};
        }
        public static readonly Species Cat=new Species("cat","고양이","meowser",1,64,95,240,.085,1,false);
        public static readonly Species Dog=new Species("dog","포메라니안","meowser-pomeranian",1,72,100,240,.085,1.1,true);
        public static readonly Species RedPanda=new Species("redPanda","레서판다","meowser-red-panda",1.5,56,80,180,.065,.8,false);
        public static readonly Species Capybara=new Species("capybara","카피바라","meowser-capybara",1.25,40,50,80,.065,.65,false);
        public static readonly Species Squirrel=new Species("squirrel","다람쥐","meowser-squirrel",1,88,125,280,.09,1.25,false);
        public static readonly Species Alpaca=new Species("alpaca","알파카","meowser-alpaca",1.25,60,85,160,.075,.85,true);
        public static readonly Species Elephant=new Species("elephant","코끼리","meowser-elephant",1.5,36,0,0,.055,.6,true);
        public static readonly Species[] All={Cat,Dog,RedPanda,Capybara,Squirrel,Alpaca,Elephant};
        public static Species Find(string id) {foreach(var s in All)if(s.Id==id)return s;return null;}
        public static T[] Shuffle<T>(T[] input,Random random) {
            var copy=(T[])input.Clone();for(int i=copy.Length-1;i>0;i--) {int j=random.Next(i+1);var v=copy[i];copy[i]=copy[j];copy[j]=v;}return copy;
        }
    }
    internal sealed class SpeciesBag {
        readonly Random random=new Random();readonly Queue<Species> queue=new Queue<Species>();
        Species previous;
        public Species Next() {
            if(queue.Count==0) {
                var entries=Species.Shuffle(Species.All,random);
                if(entries[0]==previous) {var first=entries[0];entries[0]=entries[1];entries[1]=first;}
                foreach(var s in entries)queue.Enqueue(s);
            }
            previous=queue.Dequeue();return previous;
        }
    }
}
