using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Security.Cryptography;
using System.Text;

namespace FreeNormalFinder {
public sealed class AutoDopingPlanRow {public string Path,Name;public int Generation;public int[] Added,Counts;}
public sealed class AutoDopingPlan {public bool Found,UnknownHistory,Limited;public string Message;public int[] Totals;public AutoDopingPlanRow[] Rows;public long Steps;public BirthLineage PlannedRoot,SourceRoot;}
// Works on a detached plan only. A failed bounded search is never a proof of
// impossibility, and no genetic data or saved record is changed.
public sealed class AutoDopingPlanner {
    sealed class Slot {public string Key,Path;public int Generation;public BirthLineage Record;public Slot A,B,Previous;public int[] Base=new int[5],Upper=new int[5];}
    sealed class Need {public Slot Node;public int[] Prior;}
    readonly Dictionary<string,int[]> initial=new Dictionary<string,int[]>();
    readonly Dictionary<string,Slot> slots=new Dictionary<string,Slot>();
    readonly List<Slot> all=new List<Slot>();readonly CancellationToken cancel;readonly Stopwatch clock=Stopwatch.StartNew();
    readonly int[] target;readonly BirthLineage root;readonly int generation;Slot top;long steps;bool unknown,limited;Dictionary<string,int[]> answer;
    const long StepLimit=250000;const int Seconds=30;
    public AutoDopingPlanner(BirthLineage root,int generation,int[] target,CancellationToken cancel){this.root=root;this.generation=generation;this.target=(int[])target.Clone();this.cancel=cancel;}
    public AutoDopingPlan Run(){
        if(target.Length!=5||target.Any(n=>n<0||n>303))throw new Exception("目標値を確認してください。");
        top=Build(root,"R",generation,new HashSet<BirthLineage>(),0);var state=Copy(initial);try{if(!TryFiveStatConstruction(Copy(state)))ChooseRoot(0,state);}catch(SearchBoundException){limited=true;}
        if(answer==null)return new AutoDopingPlan{Found=false,Limited=limited,UnknownHistory=unknown,Steps=steps,Message=limited?"探索上限までに配分案が見つかりませんでした。実現不可能と確定したわけではありません。":"この条件で配分案が見つかりませんでした。記録済みの使用数・目標値・祖先情報を確認してください。実現不可能と確定したわけではありません。"};
        var clone=Clone(root,"R",new HashSet<BirthLineage>());var actual=DopingNotes.Calculate(clone,generation);if(!actual.Total.SequenceEqual(target))throw new Exception("計算結果の照合に失敗しました。保存データは変更していません。");
        var rows=all.GroupBy(s=>s.Key).Where(g=>g.First().Generation<=generation).Select(g=>{var s=g.First();int[] count=Get(answer,s);return new AutoDopingPlanRow{Path=String.Join(" / ",g.Select(x=>x.Path).Distinct()),Name=s.Record.Name??"",Generation=s.Generation,Counts=(int[])count.Clone(),Added=count.Zip(initial[s.Key],(a,b)=>a-b).ToArray()};}).Where(r=>r.Added.Any(v=>v>0)).OrderByDescending(r=>r.Generation).ThenBy(r=>r.Path).ToArray();
        return new AutoDopingPlan{Found=true,UnknownHistory=unknown||!actual.Complete,Totals=actual.Total,Rows=rows,Steps=steps,PlannedRoot=clone,SourceRoot=root,Message="目標値と一致する配分案が見つかりました。最少使用数を保証するものではありません。"};
    }
    void Tick(){cancel.ThrowIfCancellationRequested();steps++;if(steps>StepLimit||clock.Elapsed.TotalSeconds>Seconds){limited=true;throw new SearchBoundException();}}
    sealed class SearchBoundException:Exception {}
    static Dictionary<string,int[]> Copy(Dictionary<string,int[]> state){return new Dictionary<string,int[]>(state);}
    static int First(BirthLineage n){return n.Raw[0x82]+1;}
    static int Last(BirthLineage n){return Math.Max(First(n),n.Raw[0x83]+1);}
    static string CaptureKey(BirthLineage n,int g){using(var sha=SHA256.Create()){string value=(n.Source??"")+"\n"+(n.Name??"")+"\n"+String.Join(",",n.Raw)+"\n"+g;return "capture:"+Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));}}
    Slot Build(BirthLineage record,string path,int g,HashSet<BirthLineage> chain,int depth){
        if(record==null||record.Raw==null||record.Raw.Length!=252||depth>30||all.Count>=4095||g<First(record)||g>Last(record)||g>9||!chain.Add(record))throw new Exception("計算に必要な祖先・世代情報が不足しているか、循環・探索対象数の上限があります。");
        if(Enumerable.Range(0,5).Any(i=>record.Raw[0xA0+i]!=0))throw new Exception("旧形式の現世代ドーピングが残っています。フルーツの使用数を確認してから計算してください。");
        DopingNotes.Validate(record.DopingMemo,First(record),Last(record));
        bool leaf=record.ParentA==null&&record.ParentB==null;
        var s=new Slot{Record=record,Path=path,Generation=g,Key=leaf?CaptureKey(record,g):path+":"+g};slots[path+":"+g]=s;all.Add(s);
        var minimum=DopingNotes.Counts(record.DopingMemo,g);int[] previous;if(initial.TryGetValue(s.Key,out previous))minimum=minimum.Zip(previous,Math.Max).ToArray();if(minimum.Sum()>DopingNotes.Limit(g))throw new Exception("同じ捕獲個体の使用記録に矛盾があります。");initial[s.Key]=minimum;
        if(g>First(record)){chain.Remove(record);s.Previous=Build(record,path,g-1,chain,depth+1);for(int i=0;i<5;i++)s.Upper[i]=Math.Min(255,s.Previous.Upper[i]+DopingNotes.Limit(g-1));}
        else if(!leaf){s.A=Build(record.ParentA,path+"A",g-1,chain,depth+1);s.B=Build(record.ParentB,path+"B",g-1,chain,depth+1);for(int i=0;i<5;i++)s.Upper[i]=Math.Min(255,Math.Max(s.A.Upper[i],s.B.Upper[i])+DopingNotes.Limit(g-1));}
        else {for(int i=0;i<5;i++)s.Base[i]=s.Upper[i]=record.Raw[0xA6+i];if(g>1)unknown=true;}
        chain.Remove(record);return s;
    }
    int[] Get(Dictionary<string,int[]> state,Slot s){int[] value;return state.TryGetValue(s.Key,out value)?value:initial[s.Key];}
    bool Set(Dictionary<string,int[]> state,Slot s,int stat,int value){var next=(int[])Get(state,s).Clone();next[stat]=Math.Max(next[stat],value);if(next.Sum()>DopingNotes.Limit(s.Generation))return false;state[s.Key]=next;return true;}
    static IEnumerable<int> Order(int low,int high){if(low>high)yield break;yield return low;if(high!=low)yield return high;for(int n=low+1;n<high;n++)yield return n;}
    // Fast constructive seed for the article's five-stat 9-generation plan.
    // Other target vectors use the inverse search below, not this template.
    bool TryFiveStatConstruction(Dictionary<string,int[]> state){
        if(generation!=9||!target.OrderBy(v=>v).SequenceEqual(new[]{36,51,51,69,138}))return false;
        int speed=Array.IndexOf(target,138),ap=Array.IndexOf(target,69),guard=Array.IndexOf(target,36);var pair=Enumerable.Range(0,5).Where(i=>target[i]==51).ToArray();int hp=pair[0],power=pair[1];
        string[] paths={"R","RA","RB","RAA","RAB","RBA","RBB","RBBA","RBBB"};int[] types={speed,speed,speed,speed,speed,ap,ap,power,power};
        for(int i=0;i<paths.Length;i++){int g=10-paths[i].Length;Slot s;if(!slots.TryGetValue(paths[i]+":"+g,out s)||s.Previous!=null)return false;if(!Set(state,s,types[i],DopingNotes.Limit(g)))return false;}
        var taskPaths=new[]{"RBBB","RBBA","RAA","RAB","RBA"};var stats=new[]{guard,power,speed,hp,ap};
        return TemplateTasks(0,taskPaths,stats,state);
    }
    List<List<Tuple<Slot,int[]>>> FullPriorChoices(Slot n,int stat){
        Tick();if(n.A==null)return new List<List<Tuple<Slot,int[]>>>{new List<Tuple<Slot,int[]>>()};
        var result=new List<List<Tuple<Slot,int[]>>>();int best=Math.Max(n.A.Upper[stat],n.B.Upper[stat]);
        foreach(var selected in new[]{n.A,n.B})if(selected.Upper[stat]==best)foreach(var more in FullPriorChoices(selected,stat)){
            var row=new List<Tuple<Slot,int[]>>();foreach(var child in new[]{n.A,n.B}){var value=new int[5];value[stat]=DopingNotes.Limit(child.Generation);row.Add(Tuple.Create(child,value));}row.AddRange(more);result.Add(row);
        }return result;
    }
    bool TemplateTasks(int index,string[] paths,int[] stats,Dictionary<string,int[]> state){
        Tick();if(index==paths.Length){answer=state;if(DopingNotes.Calculate(Clone(root,"R",new HashSet<BirthLineage>()),generation).Total.SequenceEqual(target))return true;answer=null;return false;}
        Slot n=slots[paths[index]+":"+(10-paths[index].Length)];
        foreach(var choice in FullPriorChoices(n,stats[index])){var next=Copy(state);bool valid=true;foreach(var entry in choice)for(int stat=0;stat<5;stat++)if(!Set(next,entry.Item1,stat,entry.Item2[stat])){valid=false;break;}if(valid&&TemplateTasks(index+1,paths,stats,next))return true;}return false;
    }
    bool ChooseRoot(int stat,Dictionary<string,int[]> state){
        try {Tick();if(stat==5){var prior=target.Zip(Get(state,top),(t,c)=>t-2*c).ToArray();if(prior.Any(v=>v<0))return false;return Solve(new List<Need>{new Need{Node=top,Prior=prior}},state);}
            int min=Math.Max(Get(state,top)[stat],Math.Max(0,(target[stat]-top.Upper[stat]+1)/2));int max=Math.Min(DopingNotes.Limit(generation),target[stat]/2);
            foreach(int value in Order(min,max)){var next=Copy(state);if(Set(next,top,stat,value)&&ChooseRoot(stat+1,next))return true;}return false;
        }catch(SearchBoundException){limited=true;return false;}
    }
    bool Solve(List<Need> pending,Dictionary<string,int[]> state){
        Tick();if(pending.Count==0){answer=state;var value=DopingNotes.Calculate(Clone(root,"R",new HashSet<BirthLineage>()),generation);if(value.Total.SequenceEqual(target))return true;answer=null;return false;}
        var need=pending[0];var rest=pending.Skip(1).ToList();var n=need.Node;if(need.Prior.Where((v,i)=>v>n.Upper[i]).Any())return false;
        if(n.A==null&&n.Previous==null)return need.Prior.Where((v,i)=>v>n.Base[i]).Any()?false:Solve(rest,state);
        return Allocate(need,rest,0,new int[5],new int[5],state);
    }
    bool Allocate(Need need,List<Need> rest,int stat,int[] da,int[] db,Dictionary<string,int[]> state){
        Tick();var n=need.Node;var a=n.Previous??n.A;var b=n.Previous==null?n.B:null;
        if(stat==5){var work=new List<Need>();if(da.Any(v=>v>0))work.Add(new Need{Node=a,Prior=da});if(b!=null&&db.Any(v=>v>0))work.Add(new Need{Node=b,Prior=db});work.AddRange(rest);return Solve(work,state);}
        int demand=need.Prior[stat],upper=b==null?a.Upper[stat]:Math.Max(a.Upper[stat],b.Upper[stat]);int max=Math.Min(demand,DopingNotes.Limit(a.Generation));int min=Math.Max(0,demand-upper);
        foreach(int q in Order(min,max)){
            int inherited=demand-q;var choices=b==null?new[]{0}:inherited==0?new[]{0}:new[]{0,1};
            foreach(int side in choices){if((side==0?a.Upper[stat]:b.Upper[stat])<inherited)continue;var va=(int[])da.Clone();var vb=(int[])db.Clone();if(side==0)va[stat]=inherited;else vb[stat]=inherited;
                if(b==null){var next=Copy(state);if(Set(next,a,stat,q)&&Allocate(need,rest,stat+1,va,vb,next))return true;continue;}
                int oldA=Get(state,a)[stat],oldB=Get(state,b)[stat];int required=Math.Max(0,2*q-oldA-oldB);int roomA=DopingNotes.Limit(a.Generation)-Get(state,a).Sum(),roomB=DopingNotes.Limit(b.Generation)-Get(state,b).Sum();
                int lo=Math.Max(0,required-roomB),hi=Math.Min(required,roomA);if(lo>hi)continue;
                var splits=new[]{Math.Max(lo,Math.Min(hi,required/2)),lo,hi}.Concat(Enumerable.Range(lo,hi-lo+1)).Distinct();
                foreach(int addA in splits){var next=Copy(state);if(Set(next,a,stat,oldA+addA)&&Set(next,b,stat,oldB+required-addA)&&Allocate(need,rest,stat+1,va,vb,next))return true;}
            }
        }return false;
    }
    BirthLineage Clone(BirthLineage record,string path,HashSet<BirthLineage> chain){
        if(record==null)return null;if(!chain.Add(record))throw new Exception("祖先情報が循環しています。");var notes=new List<DopingEntry>();for(int g=First(record);g<=Last(record);g++){Slot s;if(slots.TryGetValue(path+":"+g,out s)){var value=Get(answer,s);if(value.Any(v=>v!=0))notes.Add(new DopingEntry{Generation=g,Counts=(int[])value.Clone()});}else {var value=DopingNotes.Counts(record.DopingMemo,g);if(value.Any(v=>v!=0))notes.Add(new DopingEntry{Generation=g,Counts=value});}}
        var result=new BirthLineage{Name=record.Name,Source=record.Source,Kind=record.Kind,Note=record.Note,Raw=record.Raw,DopingMemo=notes.ToArray(),ParentA=Clone(record.ParentA,path+"A",chain),ParentB=Clone(record.ParentB,path+"B",chain)};chain.Remove(record);return result;
    }
}
}
