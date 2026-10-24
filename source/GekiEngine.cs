using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;

namespace FreeNormalFinder {
public class GekiTables {public string schema;public Row[] rows;public int[][] row_heads;public Pattern[] patterns;}
public class GekiQuery {
    public string Name="",Antenna="";
    public int Head=-1,Color1=-1,Color2=-1,Body=-1,Pattern=-1,MinGeneration=1,MaxGeneration=3;
    public bool AllowReverseColors=true;
    public int[] Face;
}
public sealed class GekiResultRow {
    public uint Seed {get;set;}public Candidate Person {get;set;}
    public string SeedHex {get{return Seed.ToString("X8");}}
    public int Slot {get{return Person.Slot;}}public string Name {get{return Person.Name;}}
    public string Antenna {get;set;}public string Head {get;set;}public string BodyText {get{return Model.BodyText(Person.Body);}}
    public string Color1 {get;set;}public string Color2 {get;set;}public string Pattern {get;set;}
    public int Generation {get{return Person.Generation;}}public int Level {get{return Person.Level;}}
    public int Hair {get{return Person.Face[1];}}public int FaceShape {get{return Person.Face[0];}}
    public int Brow {get{return Person.Face[2];}}public int Eye {get{return Person.Face[3];}}
    public int Nose {get{return Person.Face[4];}}public int Mouth {get{return Person.Face[5];}}
    public int Cheek {get{return Person.Face[6];}}public int SkinColor {get{return Person.Face[7];}}
    public int HairColor {get{return Person.Face[8];}}
}
public sealed class GekiProgress {
    public ulong Processed,Total,NextSeed;public int Matches;public ulong Inconclusive;
}
public sealed class GekiScanResult {
    public List<GekiResultRow> Rows=new List<GekiResultRow>();
    public List<uint> InconclusiveExamples=new List<uint>();
    public ulong Start,End,NextSeed,Processed,Inconclusive;public bool Canceled,LimitReached;
    public bool Complete {get{return NextSeed>End;}}
}
public sealed class GekiUnsupportedException:NotSupportedException {
    public GekiUnsupportedException():base("特殊個体の生成経路は未対応のため、このseedは判定保留です。"){}
}
public sealed partial class Model {
    internal void ProjectGekiAppearance(Candidate c,ulong payload,int head){ProjectAppearance(c,payload,head);}
}
// Ordinary scalar entry generation only. No RAM, clock, console key or next-entry timing.
public sealed class GekiEngine {
    readonly Model model;readonly Tables shared;readonly GekiTables data;
    readonly HashSet<ulong> specials;readonly int total,patternTotal;readonly int[] firstRows;
    readonly string[] baseAntennas;
    public int MaxGeneration {get{return data.rows.Max(r=>r.generation.Max());}}
    public GekiEngine(Model model,string path){
        this.model=model;shared=model.Data;data=Json.Read<GekiTables>(path);
        if(data==null||data.schema!="free-geki-scalar-probe-v1"||data.rows==null||data.rows.Length!=55||data.row_heads==null||data.row_heads.Length!=55||data.patterns==null||data.patterns.Length==0)throw new Exception("ゲキレア用データの形式が異なります。");
        for(int i=0;i<55;i++){
            var r=data.rows[i];
            if(r==null||r.weight<=0||r.name_ids==null||r.name_ids.Length==0||r.name_ids.Any(n=>n<0||n>=shared.names.Length)||r.primary==null||r.secondary==null||r.generation==null||r.generation.Length!=2||r.generation.Any(g=>g<1||g>shared.capmax.Length)||r.levels==null||r.levels.Length!=2||r.levels.Any(l=>l<1||l>999)||r.antenna_group<0||r.antenna_group>=shared.antenna_rows.Length||data.row_heads[i]==null||(((r.flags>>5)&7)!=0&&data.row_heads[i].Length==0))throw new Exception("ゲキレア候補表に不正な値があります。");
        }
        if(data.patterns.Any(p=>p==null||p.weight<=0))throw new Exception("ゲキレアの柄の重みが不正です。");
        total=data.rows.Sum(r=>r.weight);patternTotal=data.patterns.Sum(p=>p.weight);
        if(total>1000000)throw new Exception("ゲキレア候補表が大きすぎます。");
        firstRows=new int[total];int pos=0;for(int i=0;i<55;i++)for(int j=0;j<data.rows[i].weight;j++)firstRows[pos++]=i;
        specials=new HashSet<ulong>(shared.special);
        baseAntennas=data.rows.Select(r=>Ability(r.antenna_group==0?0:(int)(shared.antenna_rows[r.antenna_group].ids[1]&65535))).ToArray();
    }
    string Ability(int id){string s;if(!shared.abilities.TryGetValue(id.ToString(),out s))throw new Exception("未対応のアンテナIDです。");return s;}
    int Axis(int n,uint raw){if((raw&0x80000000)!=0)return 0;var r=shared.rarity[n];int rem=(int)(raw%(uint)r.total),sum=0;for(int i=0;i<5;i++){sum=(sum+r.weights[i])&65535;if(rem<sum)return i;}return 5;}
    int Color(ref Rng rng,int axis,int element){int n=shared.color_axes[Axis(axis,rng.Next())]+element;if(n<0||n>=shared.color_tables.Length)n=0;int[] row=shared.color_tables[n];return row[(rng.Next()&65535)%(uint)row.Length];}
    static int Inclusive(ref Rng rng,int a,int b){return Math.Min(a,b)+(int)(rng.Next()%(uint)(Math.Abs(a-b)+1));}
    int CurrentAbility(Row row,int level){if(row.antenna_group==0)return 0;var a=shared.antenna_rows[row.antenna_group];int stage=0;foreach(int threshold in a.thresholds){if(level<threshold)break;stage++;}return stage==0?1:(int)(a.ids[stage]&65535);}
    public string[] Antennas(){
        var values=new HashSet<string>(baseAntennas);
        foreach(var row in data.rows)for(int g=row.generation.Min();g<=row.generation.Max();g++)for(int l=row.levels.Min();l<=row.levels.Max();l++)values.Add(Ability(CurrentAbility(row,Math.Min(l,Math.Min(shared.capmax[g-1],999)))));
        return values.OrderBy(s=>s).ToArray();
    }
    IEnumerable<Candidate> Generate(uint seed){
        var rng=Rng.Init(seed);ulong used=0;int sum=total;
        for(int slot=0;slot<10;slot++){
            int rem=(int)(rng.Next()%(uint)sum),index;
            if(slot==0)index=firstRows[rem];else{for(index=0;index<55;index++){if((used&(1UL<<index))!=0)continue;rem-=data.rows[index].weight;if(rem<0)break;}}
            if(index>=55)throw new Exception("ゲキレアの抽選重みが不正です。");
            var row=data.rows[index];used|=1UL<<index;sum-=row.weight;
            ulong payload=0;for(int i=0;i<6;i++)payload|=(ulong)(rng.Next()&255)<<(8*i);
            if(specials.Contains(payload))throw new GekiUnsupportedException();
            int head=-1;if(((row.flags>>5)&7)!=0){var h=data.row_heads[index];head=h[rng.Next()%(uint)h.Length];}
            uint pattern=row.pattern;if((row.flags&1)!=0){rem=(int)(rng.Next()%(uint)patternTotal);foreach(var p in data.patterns){rem-=p.weight;if(rem<0){pattern=p.id;break;}}}
            int primary=row.primary.Length>0?(int)(row.primary[rng.Next()%(uint)row.primary.Length]&255):Color(ref rng,4,row.element);
            int secondary=0;if(row.secondary.Length>0){uint value=row.secondary[rng.Next()%(uint)row.secondary.Length];if(pattern==0x80000000&&value!=0x80000002)value=0x80000000;secondary=(int)(value&255);}else if(pattern!=0x80000000)secondary=Color(ref rng,5,row.element);
            int patternCode=(int)(pattern&255);if(primary==secondary){secondary=0;patternCode=0;}
            int generation=Inclusive(ref rng,row.generation[0],row.generation[1])-1;rng.Next();
            int level=Math.Min(Inclusive(ref rng,row.levels[0],row.levels[1]),Math.Min(shared.capmax[generation],999));
            int name=row.name_ids[(rng.Next()&65535)%(uint)row.name_ids.Length];
            yield return new Candidate{Slot=slot+1,Name=shared.names[name],Antenna=Ability(CurrentAbility(row,level)),BaseAntenna=baseAntennas[index],RawPrimary=primary,RawSecondary=secondary,Color1=primary,Color2=secondary,Head=head,Body=-1,Level=level,Generation=generation+1,Row=index,Pattern=patternCode,AntennaGroup=row.antenna_group,Payload=payload.ToString("X12")};
        }
    }
    void Project(Candidate c){ulong payload=UInt64.Parse(c.Payload,System.Globalization.NumberStyles.AllowHexSpecifier);model.ProjectGekiAppearance(c,payload,c.Head);}
    public Candidate[] Forward(uint seed){var people=Generate(seed).ToArray();foreach(var c in people)Project(c);return people;}
    public GekiResultRow Describe(uint seed,Candidate c){return new GekiResultRow{Seed=seed,Person=c,Antenna=model.DisplayAntenna(c),Head=model.HeadText(c.Head),Color1=model.ColorText(c.RawPrimary),Color2=model.ColorText(c.RawSecondary),Pattern=model.PatternText(c.Pattern)};}
    public GekiQuery ValidateQuery(GekiQuery source){
        if(source==null)throw new ArgumentException("検索条件がありません。");
        var q=new GekiQuery{Name=(source.Name??"").Trim(),Antenna=source.Antenna??"",Head=source.Head,Color1=source.Color1,Color2=source.Color2,Body=source.Body,Pattern=source.Pattern,MinGeneration=source.MinGeneration,MaxGeneration=source.MaxGeneration,AllowReverseColors=source.AllowReverseColors,Face=source.Face==null?null:(int[])source.Face.Clone()};
        if(q.Antenna.Length>0&&!Antennas().Contains(q.Antenna)||!model.Heads().Any(c=>c.Value==q.Head)||!model.Colors().Any(c=>c.Value==q.Color1)||!model.Colors().Any(c=>c.Value==q.Color2)||!model.Bodies().Any(c=>c.Value==q.Body)||!model.Patterns(true).Any(c=>c.Value==q.Pattern)||q.MinGeneration<1||q.MaxGeneration>MaxGeneration||q.MaxGeneration<q.MinGeneration||q.Face!=null&&(q.Face.Length!=9||q.Face.Where((n,i)=>!model.FaceChoices(i).Contains(n)).Any()))throw new ArgumentException("検索条件の指定が不正です。");
        return q;
    }
    bool BasicMatch(Candidate c,GekiQuery q){return (q.Name.Length==0||c.Name.Contains(q.Name))&&(q.Antenna.Length==0||q.Antenna==c.Antenna||q.Antenna==c.BaseAntenna)&&Model.ColorPairMatches(q.Color1,q.Color2,c.RawPrimary,c.RawSecondary,!q.AllowReverseColors)&&(q.Pattern<0||q.Pattern==c.Pattern)&&c.Generation>=q.MinGeneration&&c.Generation<=q.MaxGeneration;}
    public GekiScanResult Search(GekiQuery query,ulong start,ulong end,int maxRows,CancellationToken cancel,Action<GekiProgress> progress){
        if(start>0xffffffffUL||end>0xffffffffUL||end<start||maxRows<1||maxRows>100000)throw new ArgumentException("seed範囲・表示上限を確認してください。");
        var q=ValidateQuery(query);var result=new GekiScanResult{Start=start,End=end,NextSeed=start};ulong totalSeeds=end-start+1;
        for(ulong seed=start;seed<=end;seed++){
            if(cancel.IsCancellationRequested){result.Canceled=true;break;}
            var matches=new List<GekiResultRow>();
            try{
                foreach(var c in Generate((uint)seed)){
                    if(!BasicMatch(c,q))continue;
                    Project(c);
                    if(q.Head>=0&&c.Head!=q.Head||q.Body>=0&&c.Body!=q.Body||!Model.FaceMatches(q.Face,c.Face))continue;
                    matches.Add(Describe((uint)seed,c));
                }
            }catch(GekiUnsupportedException){matches.Clear();result.Inconclusive++;if(result.InconclusiveExamples.Count<1000)result.InconclusiveExamples.Add((uint)seed);}
            // Commit whole seeds so cancellation/limit never reports a partially scanned seed.
            if(result.Rows.Count+matches.Count>maxRows){result.LimitReached=true;break;}
            result.Rows.AddRange(matches);result.Processed++;result.NextSeed=seed+1;
            if(progress!=null&&((result.Processed&255)==0||seed==end))progress(new GekiProgress{Processed=result.Processed,Total=totalSeeds,NextSeed=result.NextSeed,Matches=result.Rows.Count,Inconclusive=result.Inconclusive});
        }
        if(progress!=null)progress(new GekiProgress{Processed=result.Processed,Total=totalSeeds,NextSeed=result.NextSeed,Matches=result.Rows.Count,Inconclusive=result.Inconclusive});
        return result;
    }
}
}
