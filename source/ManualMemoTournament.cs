using System;
using System.Linq;
using System.Collections.Generic;
namespace FreeNormalFinder {
public sealed class ManualMemoBranch {public string Id,PersonId,ParentA,ParentB;public int Generation;public bool Hidden;}
public sealed partial class ToolForm {
    readonly List<ManualMemoBranch> manualMemoBranches=new List<ManualMemoBranch>();
    readonly List<string> manualMemoTreeRoots=new List<string>();
    readonly Dictionary<BirthLineage,ManualMemoBranch> manualMemoBranchRecords=new Dictionary<BirthLineage,ManualMemoBranch>();
    ManualMemoBranch MemoBranch(string id){return manualMemoBranches.FirstOrDefault(b=>b.Id==id);}
    ManualMemoBranch NewMemoBranch(int generation){if(manualMemoBranches.Count>=4095)throw new Exception("メモの枠は4095個までです。");var b=new ManualMemoBranch{Id=Guid.NewGuid().ToString("N"),Generation=generation};manualMemoBranches.Add(b);return b;}
    void EnsureMemoParents(ManualMemoBranch b){if(b.Generation<=1||b.ParentA!=null)return;b.ParentA=NewMemoBranch(b.Generation-1).Id;b.ParentB=NewMemoBranch(b.Generation-1).Id;}
    void EnsureMemoTournament(){
        int highest=Enumerable.Range(1,9).Where(g=>manualMemoGenerations[g].Checked).DefaultIfEmpty(0).Max();if(highest==0)return;
        if(manualMemoTreeRoots.Count==0)manualMemoTreeRoots.Add(NewMemoBranch(highest).Id);
        int existing=manualMemoTreeRoots.Select(id=>MemoBranch(id).Generation).Max();
        int futureCount=manualMemoBranches.Count,rootCount=manualMemoTreeRoots.Count;for(int g=existing;g<highest;g++){futureCount+=(rootCount+1)/2+rootCount%2;rootCount=(rootCount+1)/2;}if(futureCount>4095)throw new Exception("メモの枠は4095個までです。");
        // Only add new higher-generation links; changing visibility never
        // deletes or rewires an existing relationship.
        while(existing<highest){
            var next=new List<string>();var old=manualMemoTreeRoots.Select(MemoBranch).ToList();
            for(int i=0;i<old.Count;i+=2){var a=old[i];var b=i+1<old.Count?old[i+1]:NewMemoBranch(existing);
                if(a.Generation!=existing||b.Generation!=existing)throw new Exception("メモの世代接続を確認してください。");
                var parent=NewMemoBranch(existing+1);parent.ParentA=a.Id;parent.ParentB=b.Id;a.Hidden=b.Hidden=false;next.Add(parent.Id);
            }manualMemoTreeRoots.Clear();manualMemoTreeRoots.AddRange(next);existing++;
        }
    }
    void ImportLegacyMemoGraph(){
        manualMemoBranches.Clear();manualMemoTreeRoots.Clear();
        // Old flat saves have no parent information. Keep every person as an
        // independent tree rather than inventing their parentage.
        int highest=manualMemoPeople.Select(p=>p.Generation).Concat(Enumerable.Range(1,9).Where(g=>manualMemoGenerations[g].Checked)).DefaultIfEmpty(0).Max();
        foreach(var p in manualMemoPeople){var node=NewMemoBranch(p.Generation);node.PersonId=p.Id;EnsureMemoParents(node);while(node.Generation<highest){var upper=NewMemoBranch(node.Generation+1);upper.ParentA=node.Id;upper.ParentB=NewMemoBranch(node.Generation).Id;node=upper;}manualMemoTreeRoots.Add(node.Id);}
        if(manualMemoPeople.Count>0)manualMemoTreeRoots.Add(NewMemoBranch(highest).Id);
    }
    bool MemoBranchHasPerson(ManualMemoBranch b){return b!=null&&(b.PersonId!=null||b.ParentA!=null&&(MemoBranchHasPerson(MemoBranch(b.ParentA))||MemoBranchHasPerson(MemoBranch(b.ParentB))));}
    bool CanHideMemoBranch(ManualMemoBranch b){return b!=null&&!b.Hidden&&b.PersonId==null&&!MemoBranchHasPerson(b)&&manualMemoBranches.Any(parent=>parent.ParentB==b.Id);}
    public void SetManualMemoBranchHidden(string id,bool hidden){
        var branch=MemoBranch(id);if(branch==null)throw new Exception("枠がありません。");if(hidden&&!CanHideMemoBranch(branch))throw new Exception("右側の空の＋枠だけ非表示にできます。");
        branch.Hidden=hidden;manualMemoDirty=true;RenderManualMemo();
    }
    bool ManualCanHideRecord(BirthLineage record){ManualMemoBranch branch;return manualMemoBranchRecords.TryGetValue(record,out branch)&&CanHideMemoBranch(branch);}
    bool ManualHasHiddenRight(BirthLineage record){ManualMemoBranch branch;return manualMemoBranchRecords.TryGetValue(record,out branch)&&branch.ParentB!=null&&MemoBranch(branch.ParentB).Hidden;}
    void ManualBranchAction(BirthLineage record,string action){
        ManualMemoBranch branch;if(!manualMemoBranchRecords.TryGetValue(record,out branch))return;
        if(action=="枠を非表示"){SetManualMemoBranchHidden(branch.Id,true);return;}
        if(action=="枠を戻す"){if(branch.ParentB!=null)SetManualMemoBranchHidden(branch.ParentB,false);return;}
        ManualMemoIndividual person;if(!manualMemoRecords.TryGetValue(record,out person))return;if(action=="ドーピング")EditManualMemoAbility(person.Id);else if(action=="顔パーツ")EditManualMemoFace(person.Id);
    }
    BirthLineage MemoBranchRecord(ManualMemoBranch branch){
        if(branch==null||branch.Hidden)return null;ManualMemoIndividual person=branch.PersonId==null?null:FindManualPerson(branch.PersonId);int[] raw;
        if(person!=null)raw=ManualPortraitRaw(person);else{raw=Enumerable.Repeat(-1,252).ToArray();raw[0x82]=raw[0x83]=branch.Generation-1;}
        var record=new BirthLineage{Name=person==null?"":ManualPersonName(person),Raw=raw,Kind=person==null?"追加":"メモ",Source=person==null?branch.Id:person.Id};
        manualMemoBranchRecords[record]=branch;if(person==null)manualMemoPluses[record]=branch.Generation;else manualMemoRecords[record]=person;
        record.ParentA=MemoBranchRecord(MemoBranch(branch.ParentA));record.ParentB=MemoBranchRecord(MemoBranch(branch.ParentB));return record;
    }
    ManualMemoIndividual AddManualIndividualAt(ManualMemoBranch branch){
        if(branch==null||branch.Hidden||branch.PersonId!=null)throw new Exception("空の＋枠を選んでください。");if(manualMemoPeople.Count>=256)throw new Exception("この試作では256人まで登録できます。");
        int needed=(branch.Generation>1&&branch.ParentA==null?2:0)+(manualMemoTreeRoots.Contains(branch.Id)&&!manualMemoTreeRoots.Select(MemoBranch).Any(b=>b.Id!=branch.Id&&b.PersonId==null)?1:0);if(manualMemoBranches.Count+needed>4095)throw new Exception("メモの枠は4095個までです。");
        var person=new ManualMemoIndividual{Id=Guid.NewGuid().ToString("N"),Generation=branch.Generation};manualMemoPeople.Add(person);branch.PersonId=person.Id;EnsureMemoParents(branch);
        if(manualMemoTreeRoots.Contains(branch.Id)&&!manualMemoTreeRoots.Select(MemoBranch).Any(b=>b.PersonId==null))manualMemoTreeRoots.Add(NewMemoBranch(branch.Generation).Id);
        if(branch.Generation>1){changingManualMemoGenerations=true;try{manualMemoGenerations[branch.Generation-1].Checked=true;}finally{changingManualMemoGenerations=false;}}
        manualMemoDirty=true;RenderManualMemo(person.Id);return person.Copy();
    }
    void ValidateMemoGraph(ManualMemoDocument doc){
        if(doc.Branches==null||doc.Roots==null||doc.Branches.Length>4095||doc.Roots.Distinct().Count()!=doc.Roots.Length)throw new Exception("メモの接続情報が不正です。");
        var map=new Dictionary<string,ManualMemoBranch>();var people=doc.Individuals.ToDictionary(p=>p.Id);var used=new HashSet<string>();Guid guid;
        foreach(var b in doc.Branches){if(b==null||!Guid.TryParseExact(b.Id,"N",out guid)||map.ContainsKey(b.Id)||b.Generation<1||b.Generation>9||b.PersonId!=null&&(!people.ContainsKey(b.PersonId)||people[b.PersonId].Generation!=b.Generation||!used.Add(b.PersonId))||(b.ParentA==null)!=(b.ParentB==null))throw new Exception("メモの枠情報が不正です。");map.Add(b.Id,b);}
        var seen=new HashSet<string>();Action<string,int,bool> visit=null;visit=(id,g,isRight)=>{ManualMemoBranch b;if(!map.TryGetValue(id,out b)||!seen.Add(id)||b.Generation!=g||b.Hidden&&(!isRight||b.PersonId!=null))throw new Exception("メモの世代接続が不正です。");if(b.ParentA!=null){if(g<=1)throw new Exception("1世代より下に枠は作れません。");visit(b.ParentA,g-1,false);visit(b.ParentB,g-1,true);}};
        foreach(var id in doc.Roots){if(!map.ContainsKey(id))throw new Exception("メモの先頭枠がありません。");visit(id,map[id].Generation,false);}
        if(seen.Count!=map.Count||used.Count!=people.Count)throw new Exception("メモに接続されていない個体・枠があります。");
        Func<string,bool> hasPerson=null;hasPerson=id=>map[id].PersonId!=null||map[id].ParentA!=null&&(hasPerson(map[id].ParentA)||hasPerson(map[id].ParentB));if(map.Values.Any(b=>b.Hidden&&hasPerson(b.Id)))throw new Exception("入力済みの個体を非表示にはできません。");
    }
}
}
