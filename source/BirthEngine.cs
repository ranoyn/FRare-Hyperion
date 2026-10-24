using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace FreeNormalFinder {
public class BirthWeight {public int color_index,weight_candidate;}
public class BirthGenetic {public int[] parent_keys;public BirthWeight[][] candidate_modes;}
public class BirthMerge {public int partner,candidate;}
public class BirthAntenna {public int ancestry_candidate,ancestry_group,merge_weight,fallback_weight;public BirthMerge[] merges;}
public class BirthTables {
    public string schema;public int[] feature_offsets,masks,axis0,axis1,head_weights,color_weights,mapped_keys,patterns,capmax,capmin,recombination;
    public int[][] heads,color_keys,personality;public BirthGenetic[] genetics;public BirthAntenna[] antennas;
}
public sealed class BirthLineage {
    public string Name,Source,Kind,Note;public int[] Raw;public BirthLineage ParentA,ParentB;public DopingEntry[] DopingMemo;
}
public class BirthParent {
    public string Name="",Source="",SourceId="";public int[] Raw;public bool AncestryKnown=true;public BirthLineage Lineage;public DopingEntry[] DopingMemo;
    public int Generation {get{return Raw[0x82]+1;}}
    public int GenerationLimit {get{return Math.Max(Generation,Raw[0x83]+1);}}
    public int Level {get{return Raw[0x96];}}
    public override string ToString(){return Name+"（"+Generation+"代・Lv"+Level+"／"+Source+"）";}
}
public sealed class EventBirthCatalog {public string schema,Label;public BirthParent[] Parents;}
public class BirthRequest {public BirthParent ParentA,ParentB;public bool AssumeTrained;public string Tag;}
public class BirthResult {
    public string Name {get{return Candidate!=null?Candidate.Name??"":Lineage!=null?Lineage.Name??"":"";}}
    public string GenerationText {get{return Raw==null?"":Raw[0x83]<Raw[0x82]?Generation.ToString():Generation+"～"+Math.Max(Generation,Raw[0x83]+1);}}
    public string Parent1 {get;set;}public string Parent2 {get;set;}public string Parent1Source {get;set;}public string Parent2Source {get;set;}public string ChildSourceId;public BirthParent Candidate;public BirthLineage Lineage;
    public string BodyText {get{return Model.BodyText(Body);}}
    public string Antenna {get;set;}public int Body {get;set;}public string Color1 {get;set;}public string Color2 {get;set;}
    public string Head {get;set;}public string Pattern {get;set;}public int Generation {get;set;}
    public int Hair {get;set;}public int FaceShape {get;set;}public int Brow {get;set;}public int Eye {get;set;}
    public int Nose {get;set;}public int Mouth {get;set;}public int Cheek {get;set;}public int SkinColor {get;set;}public int HairColor {get;set;}
    public string Condition {get;set;}public int[] Raw;public uint Seed;public int DrawCount;public uint[] VisualState;
}
public sealed partial class Model {
    public static BirthLineage CaptureBirthParent(BirthParent p){return new BirthLineage{Name=p.Name,Source=p.Source,Kind=p.Lineage!=null?p.Lineage.Kind:(p.SourceId??"").StartsWith("rare:")?"レアキャ":"個体登録",Note=p.Lineage!=null?p.Lineage.Note:(!p.AncestryKnown?"祖先アンテナ値は不明です。出生元の個体は記録されていません。":"捕獲・登録個体。出生元の個体は記録されていません。"),Raw=p.Raw==null?null:(int[])p.Raw.Clone(),DopingMemo=DopingNotes.Copy(p.DopingMemo),ParentA=p.Lineage==null?null:p.Lineage.ParentA,ParentB=p.Lineage==null?null:p.Lineage.ParentB};}
    public static BirthLineage CreateBirthLineage(int[] raw,BirthParent a,BirthParent b,string note){return new BirthLineage{Name="",Source="出生予測",Kind=raw==null?"未予測":"出生",Note=note,Raw=raw==null?null:(int[])raw.Clone(),ParentA=CaptureBirthParent(a),ParentB=CaptureBirthParent(b)};}
    BirthTables birthTables;
    static readonly string[] BirthPatterns={"なし","ボーダー","ストライプ","ツートン","水玉","アニマル","マーブル","虎","レンガ","ダイヤ"};
    public Choice[] Patterns(bool unspecified){
        var ids=new[]{0,3,1,2,4,5,6,9,7,8};
        var choices=ids.Select(i=>new Choice{Value=i,Text=PatternText(i)});
        if(unspecified)choices=new[]{new Choice{Value=-1,Text="未指定"}}.Concat(choices);
        return choices.ToArray();
    }
    public Choice[] PatternFilterChoices(){
        return Patterns(true).Concat(new[]{new Choice{Value=100,Text="雑柄"},new Choice{Value=101,Text="レア柄1"},new Choice{Value=102,Text="レア柄2"},new Choice{Value=103,Text="レア柄3"}}).ToArray();
    }
    public static bool PatternMatches(int selected,int actual){
        switch(selected){case -1:return true;case 100:return actual==3||actual==1||actual==2;case 101:return actual==4||actual==5;case 102:return actual==6;case 103:return actual==9||actual==7||actual==8;default:return selected>=0&&selected<=9&&selected==actual;}
    }
    void LoadBirth(string directory){string p=Path.Combine(directory,"birth.json");if(!File.Exists(p))return;birthTables=Json.Read<BirthTables>(p);if(birthTables.schema!="free-birth-visual-v1"||birthTables.feature_offsets.Length!=12||birthTables.heads.Length!=70||birthTables.mapped_keys.Length!=39||birthTables.capmax.Length!=10)throw new Exception("出生データの形式が異なります。");}
    public bool BirthAvailable {get{return birthTables!=null;}}
    public Choice[] BirthAntennas(){
        if(birthTables==null)throw new InvalidOperationException("出生データがありません。");
        var order=BirthAntennaFamilies();return Enumerable.Range(0,birthTables.antennas.Length).OrderBy(i=>Array.IndexOf(order,AntennaFamilyName(i))).ThenBy(i=>i).Select(i=>{
            return new Choice{Value=i,Text=AntennaFamilyName(i)+"［系統"+i.ToString("D2")+"］"};
        }).ToArray();
    }
    public string PatternText(int n){return n>=0&&n<BirthPatterns.Length?BirthPatterns[n]:"未対応";}
    public string BirthAntennaFamily(int group){
        if(birthTables==null||group<0||group>=birthTables.antennas.Length)return null;
        return AntennaFamilyName(group);
    }
    public string[] BirthAntennaFamilies(){return OrderAntennaNames(Enumerable.Range(0,birthTables.antennas.Length).Select(BirthAntennaFamily));}
    public string BirthAntennaText(int group,int level){
        if(group<0||group>=Data.antenna_rows.Length)throw new Exception("未対応のアンテナ系統です。");
        int id=0;if(group!=0){var ant=Data.antenna_rows[group];int stage=0;foreach(int t in ant.thresholds){if(level<t)break;stage++;}if(stage==0)return AntennaFamilyName(group);id=(int)(ant.ids[stage]&65535);}
        return Data.abilities.ContainsKey(id.ToString())?Data.abilities[id.ToString()]:"ID"+id;
    }
    public void CheckBirthParent(BirthParent p){ValidateBirthParent(p);}
    public EventBirthCatalog LoadEventBirthCatalog(){
        var catalog=Json.Read<EventBirthCatalog>(Path.Combine(DataDirectory,"event.json"));
        if(catalog==null||catalog.schema!="free-event-birth-input-v1"||catalog.Label!="event"||catalog.Parents==null||(catalog.Parents.Length!=72&&catalog.Parents.Length!=84))throw new Exception("eventの個体データの形式が異なります。");
        var ids=new HashSet<string>();foreach(var p in catalog.Parents){ValidateBirthParent(p);bool monthly=(p.SourceId??"").StartsWith("event:monthly:"),whf=(p.SourceId??"").StartsWith("event:whf:");if(String.IsNullOrWhiteSpace(p.Name)||(!monthly&&!whf)||!ids.Add(p.SourceId)||p.Generation!=3||p.Level!=(monthly?48:28))throw new Exception("eventの個体情報が不正です。");}
        if(catalog.Parents.Count(p=>p.SourceId.StartsWith("event:monthly:"))!=72||catalog.Parents.Count(p=>p.SourceId.StartsWith("event:whf:"))!=catalog.Parents.Length-72)throw new Exception("eventの月別・WHF個体数が不正です。");
        return catalog;
    }
    void ValidateBirthParent(BirthParent p){
        if(birthTables==null)throw new InvalidOperationException("data/birth.jsonがありません。dataフォルダも更新してください。");
        if(p==null||p.Raw==null||p.Raw.Length!=252||p.Name!=null&&p.Name.Length>20)throw new ArgumentException("親のデータが不正です。");
        int[] f=p.Raw;if(f.Where((v,i)=>i!=0x96).Any(v=>v<0||v>255))throw new ArgumentException("親の内部値が不正です。");DopingNotes.Validate(p.DopingMemo,p.Generation,p.GenerationLimit);
        if(f[0x64]<0||f[0x64]>=40||f[0x6d]<0||f[0x6d]>=30||f[0x82]<0||f[0x82]>=9||p.Level<1||p.Level>999)throw new ArgumentException("対応範囲外の頭・体格・世代・レベルです。");
        int[] at={0x65,0x66,0x67,0x68,0x69,0x6a,0x6b,0x73,0x74},limits={16,51,32,64,64,64,56,6,12};
        for(int i=0;i<at.Length;i++)if(f[at[i]]<0||f[at[i]]>=limits[i])throw new ArgumentException("対応範囲外の顔パーツです。");
        if(f[0x71]<3||f[0x71]>38||f[0x70]<0||f[0x70]>=birthTables.patterns.Length||f[0x72]!=0&&(f[0x72]<3||f[0x72]>38))throw new ArgumentException("対応範囲外の色・柄です。");
        if((f[0x70]==0)!=(f[0x72]==0))throw new ArgumentException("柄と副色の組み合わせは未対応です。");
        foreach(int o in new[]{0x86,0x87,0x94})if(f[o]<0||f[o]>=birthTables.antennas.Length)throw new ArgumentException("未対応の祖先アンテナ値です。");
    }
    public BirthParent ParentFromCandidate(Candidate c,string source,string id){
        if(c==null||c.Face==null||c.Face.Length!=9)throw new Exception("親候補を選択してください。");
        int[] f=new int[252];int[] at={0x65,0x66,0x67,0x68,0x69,0x6a,0x6b,0x73,0x74};for(int i=0;i<at.Length;i++)f[at[i]]=c.Face[i];
        f[0x64]=c.Head;f[0x6d]=c.Body;f[0x70]=c.Pattern;f[0x71]=c.RawPrimary;f[0x72]=c.RawSecondary;f[0x82]=c.Generation-1;f[0x83]=Math.Max(c.Generation,c.GenerationLimit)-1;f[0x94]=c.AntennaGroup;f[0x96]=c.Level;
        var p=new BirthParent{Name=c.Name,Source=source,SourceId=id,Raw=f};ValidateBirthParent(p);return p;
    }
    public List<BirthParent> LoadBirthParents(string path){
        if(new FileInfo(path).Length!=1906187)throw new Exception("この試作は1,906,187バイトの解析済みFREE BIN形式に対応します。");
        byte[] bytes=File.ReadAllBytes(path);string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");
        var list=new List<BirthParent>();var encoding=new UnicodeEncoding(false,false,true);
        for(int i=0;i<64;i++){int start=0x2588+252*i;try{
            int end=0;while(end<40&&BitConverter.ToUInt16(bytes,start+0x14+end)!=0)end+=2;if(end==0||end==40)continue;
            string name=encoding.GetString(bytes,start+0x14,end);if(name.Any(Char.IsControl)||String.IsNullOrWhiteSpace(name))continue;
            int[] raw=new int[252];for(int j=0;j<252;j++)raw[j]=bytes[start+j];raw[0x96]=BitConverter.ToUInt16(bytes,start+0x96);
            var p=new BirthParent{Name=name,Source="BIN・枠"+(i+1),SourceId="bin:"+hash+":"+start,Raw=raw};ValidateBirthParent(p);list.Add(p);
        }catch(ArgumentException){}}
        if(list.Count==0)throw new Exception("対応する個体を見つけられませんでした。BINは変更していません。");return list;
    }
    sealed class BirthRandom {
        public Rng State;public int Count;
        public BirthRandom(uint seed){State=Rng.Init(seed);}
        public int Draw(int n){if(n<=0)throw new Exception("出生抽選表の重みが不正です。");Count++;return (int)(State.Next()%(uint)n);}
    }
    public static bool SameBirthInputIndividual(BirthParent first,BirthParent second){
        // Separate births can share a predicted appearance and origin ID.
        if((first.SourceId??"").StartsWith("birth:",StringComparison.Ordinal)&&(second.SourceId??"").StartsWith("birth:",StringComparison.Ordinal))return false;
        return Object.ReferenceEquals(first,second)||!String.IsNullOrEmpty(first.SourceId)&&first.SourceId==second.SourceId;
    }
    public BirthResult PredictBirth(BirthParent first,BirthParent second,bool assumeTrained){
        ValidateBirthParent(first);ValidateBirthParent(second);
        if(SameBirthInputIndividual(first,second))throw new Exception("同じ個体を両方の親にはできません。");
        if(first.Generation!=second.Generation)throw new Exception("親は同じ世代を選んでください。");
        var t=birthTables;int g=first.Raw[0x82],level=t.capmax[g];if(level<t.capmin[g]||level<1)throw new Exception("この世代は現在の解析表では通常出生の条件を満たせません。");
        if(!assumeTrained&&(first.Level!=level||second.Level!=level))throw new Exception("両親ともLv"+level+"が必要です。育成後を調べる場合は「育成した仮定」を選んでください。");
        int[] a=(int[])first.Raw.Clone(),b=(int[])second.Raw.Clone();if(assumeTrained)a[0x96]=b[0x96]=level;
        int cmp=0;foreach(int o in t.feature_offsets){cmp=a[o].CompareTo(b[o]);if(cmp!=0)break;}
        bool nameOrderUnknown=cmp==0&&(String.IsNullOrWhiteSpace(first.Name)||String.IsNullOrWhiteSpace(second.Name));if(cmp==0)cmp=String.CompareOrdinal(first.Name,second.Name);if(cmp>0){var x=a;a=b;b=x;}
        byte[] payload=new byte[12];for(int i=0;i<12;i++)payload[i]=(byte)((a[t.feature_offsets[i]]+b[t.feature_offsets[i]])&255);
        byte[] digest;using(var sha=SHA256.Create())digest=sha.ComputeHash(payload);uint seed=unchecked(BitConverter.ToUInt32(digest,8)+BitConverter.ToUInt32(digest,12));
        var rng=new BirthRandom(seed);int[] f=new int[252];int[][] groups={new[]{0x65,0x66},new[]{0x67},new[]{0x68},new[]{0x69},new[]{0x6a},new[]{0x6b},new[]{0x73},new[]{0x74}};
        int[] choices=new int[8];bool[] diff=new bool[8];int sum=0,count=0,last=0;
        for(int i=0;i<8;i++){choices[i]=rng.Draw(2);sum+=choices[i];int o=groups[i].Last();diff[i]=a[o]!=b[o];if(diff[i]){count++;last=i;}}
        if(count>0&&!(sum==8&&count<=1)&&rng.Draw(count)==0)choices[last]=sum==0?1:0;
        for(int i=0;i<8;i++)foreach(int o in groups[i])f[o]=choices[i]==0?a[o]:b[o];
        int[] offsets={0x68,0x6a,0x69,0x67,0x6b,0x66,0x65,0x64},starts={0,0x40,0x80,0xc0,0xe0,0x118,0x14b,0x15b},bits={0,1,2,6,7,8,3,4,5},scores=new int[9];
        for(int i=0;i<offsets.Length;i++){int mask=t.masks[starts[i]+f[offsets[i]]];for(int j=0;j<9;j++)scores[j]+=(mask>>bits[j])&1;}
        int max=scores.Max(),index=Array.IndexOf(scores,max);if(scores.Count(s=>s==max)>1){int[] weights=Enumerable.Range(0,9).Select(i=>scores[i]==max?t.axis0[i/3]+t.axis1[i%3]:0).ToArray();int total=weights.Sum(),rem=total>0?rng.Draw(total):0;index=5;for(int i=0;i<9;i++){rem-=weights[i];if(rem<0){index=i;break;}}}
        f[0x76]=index/3|index%3<<4;
        int candidate=t.heads[a[0x64]][b[0x64]];bool accept=candidate!=70&&rng.Draw(100)<t.head_weights[candidate];f[0x64]=accept?candidate:(rng.Draw(2)==0?a[0x64]:b[0x64]);
        int q0=a[0x6d]/5,q1=b[0x6d]/5,r0=a[0x6d]%5,r1=b[0x6d]%5;
        f[0x6d]=(5*(Math.Min(q0,q1)+rng.Draw(Math.Abs(q0-q1)+1))+Math.Min(r0,r1)+rng.Draw(Math.Abs(r0-r1)+1))&255;
        int[] colors;
        if(a[0x70]==0&&b[0x70]==0){
            int ka=t.mapped_keys[a[0x71]],kb=t.mapped_keys[b[0x71]],key=ka==kb&&a[0x71]!=b[0x71]?ka|0x2000:ka|kb;
            var record=t.genetics.FirstOrDefault(r=>(r.parent_keys[0]|r.parent_keys[1])==key||(r.parent_keys[0]==r.parent_keys[1]&&(r.parent_keys[0]|0x2000)==key));
            if(record==null)throw new Exception("色の組み合わせ表がありません。");int rem=rng.Draw(100),selected=0;
            foreach(var c in record.candidate_modes[1]){rem-=c.weight_candidate;if(rem<0){selected=c.color_index;break;}}
            int x=t.color_keys[selected][0],y=t.color_keys[selected][1];
            if(y!=0){colors=ka==kb?new[]{a[0x71],b[0x71]}:new[]{x==ka?a[0x71]:x==kb?b[0x71]:ResolveBirthColor(x,rng),y==ka?a[0x71]:y==kb?b[0x71]:ResolveBirthColor(y,rng)};if(rng.Draw(2)!=0)Array.Reverse(colors);}
            else {int c=ka==kb?(x==ka?(rng.Draw(2)==0?a[0x71]:b[0x71]):ResolveBirthColor(x,rng)):x==ka?a[0x71]:x==kb?b[0x71]:ResolveBirthColor(x,rng);colors=new[]{c,0};}
        }else {
            bool mix=rng.Draw(100)<t.recombination[g+1];int[][] pairs={new[]{a[0x71],a[0x72]},new[]{b[0x71],b[0x72]}};
            if(mix){int[] pool=pairs.SelectMany(p=>p).ToArray(),weights=pool.Select(c=>c==0?0:BirthColorWeight(t.mapped_keys[c])).ToArray();colors=new int[2];
                for(int j=0;j<2;j++){int total=weights.Sum(),rem=total>0?rng.Draw(total):0;for(int i=0;i<4;i++){rem-=weights[i];if(rem<0){colors[j]=pool[i];weights[i]=0;break;}}}if(rng.Draw(2)!=0)Array.Reverse(colors);
            }else {int wa=BirthColorWeight(BirthColorKey(pairs[0])),wb=BirthColorWeight(BirthColorKey(pairs[1])),total=wa+wb,rem=total>0?rng.Draw(total):0;colors=(int[])pairs[rem<wa?0:1].Clone();}
        }
        if(colors[0]==colors[1]){colors[1]=0;f[0x70]=0;}
        else if(colors[1]==0||colors[1]==2)f[0x70]=0;
        else if(a[0x70]==b[0x70]){f[0x70]=a[0x70];if(f[0x70]==0){int total=t.patterns.Sum(),rem=total>0?rng.Draw(total):0;f[0x70]=1;for(int i=0;i<t.patterns.Length;i++){rem-=t.patterns[i];if(rem<0){f[0x70]=i;break;}}}}
        else if(a[0x70]==0)f[0x70]=b[0x70];else if(b[0x70]==0)f[0x70]=a[0x70];
        else {int total=(t.patterns[a[0x70]]+t.patterns[b[0x70]])&255;int rem=total>0?rng.Draw(total):0;f[0x70]=rem<t.patterns[b[0x70]]?b[0x70]:a[0x70];}
        f[0x71]=colors[0];f[0x72]=colors[1];uint[] visualState={rng.State.A,rng.State.B,rng.State.C,rng.State.D};int visualDraws=rng.Count;
        int[] previousScores=new int[7];int chosen=0,axis=f[0x76]&15;
        for(int n=0;n<6;n++){int[] row=t.personality[starts[n]+f[offsets[n]]],updated=new int[7];int maximum=-100000;chosen=0;
            for(int j=0;j<7;j++){if(j==0&&axis==0||(j==2||j==3)&&axis==2)continue;int add=row[2+j];if(add>=128)add-=256;updated[j]=previousScores[j]+add;if(updated[j]>maximum||updated[j]==maximum&&previousScores[chosen]<previousScores[j]){maximum=updated[j];chosen=j;}}
            if(updated.All(v=>v==previousScores[0]))chosen=axis==0?1:0;previousScores=updated;
        }f[0x77]=chosen;
        f[0x94]=BirthAntenna(a,b,rng);f[0x86]=a[0x94];f[0x87]=b[0x94];f[0x82]=g+1;f[0x96]=1;f[0x84]=1;
        return new BirthResult{Raw=f,Seed=seed,DrawCount=visualDraws,VisualState=visualState,Antenna=BirthAntennaText(f[0x94],1),Body=f[0x6d],Color1=ColorText(f[0x71]),Color2=ColorText(f[0x72]),Head=HeadText(f[0x64]),Pattern=PatternText(f[0x70]),Generation=g+2,FaceShape=f[0x65],Hair=f[0x66],Brow=f[0x67],Eye=f[0x68],Nose=f[0x69],Mouth=f[0x6a],Cheek=f[0x6b],SkinColor=f[0x73],HairColor=f[0x74],Condition=(nameOrderUnknown?"未確定：名前未入力のため同値時の親順序を確定できません。 ":"")+(assumeTrained?"育成仮定：両親Lv"+level:"現在Lv"+level)+"／子Lv1。名前・能力値・出生可否の全条件は予測対象外。"};
    }
    int ResolveBirthColor(int key,BirthRandom rng){var choices=birthTables.mapped_keys.Select((k,i)=>new{k,i}).Where(x=>x.k==key).Take(10).Select(x=>x.i).ToArray();return choices.Length==0?3:choices[rng.Draw(choices.Length)];}
    int BirthColorKey(int[] pair){int a=birthTables.mapped_keys[pair[0]],b=birthTables.mapped_keys[pair[1]];return a==b&&pair[0]!=pair[1]?a|0x2000:a|b;}
    int BirthColorWeight(int key){int index=Array.FindIndex(birthTables.color_keys,k=>(k[0]|k[1])==key);return birthTables.color_weights[index<0?0:index];}
    int BirthAntenna(int[] a,int[] b,BirthRandom rng){
        var rows=birthTables.antennas;int group=0;var seen=new HashSet<int>();bool special=true;
        foreach(int id in new[]{a[0x86],a[0x87],b[0x86],b[0x87]}){var r=rows[id];if(id==0||group!=0&&group!=r.ancestry_group||r.ancestry_candidate<1||r.ancestry_candidate>=20||!seen.Add(r.ancestry_candidate)){special=false;break;}if(group==0)group=r.ancestry_group;}
        if(special&&group!=0)return group;var x=rows[a[0x94]];var y=rows[b[0x94]];var ma=x.merges.FirstOrDefault(v=>v.partner==b[0x94]);var mb=y.merges.FirstOrDefault(v=>v.partner==a[0x94]);
        if(ma!=null&&mb!=null&&ma.candidate!=0&&mb.candidate!=0&&rng.Draw(100)<y.merge_weight){int id=rng.Draw(2)==0?ma.candidate:mb.candidate;if(id!=0)return id;}
        int total=x.fallback_weight+y.fallback_weight,rem=total>0?rng.Draw(total):0;return rem<y.fallback_weight?b[0x94]:a[0x94];
    }
}
}
