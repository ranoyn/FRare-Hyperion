// Private research prototype. Uses only bundled tables and user-entered observations.
using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FreeNormalFinder {
public class Row {public int weight,flags,element,antenna_group;public uint pattern;public int[] name_ids,generation,levels;public uint[] primary,secondary;}
public class Pattern {public uint id;public int weight;}
public class Rarity {public int total;public int[] weights;}
public class Antenna {public int[] thresholds;public uint[] ids;}
public class ColorLabel {public int key;public string label;}
public class Tables {public string schema;public Row[] rows;public Pattern[] patterns;public ulong[] special;public string[] names;public Rarity[] rarity;public int[] color_axes,color_keys,capmax;public int[][] color_tables;public ColorLabel[] color_labels,raw_color_labels;public Dictionary<string,string> abilities;public Antenna[] antenna_rows;}
public class Person {public string Name="",Antenna="";public int Color1=-1,Color2=-1,Head=-1,Pattern=-1,Body=-1,Generation=-1,GenerationLimit=-1,Level=-1;public int[] Face;}
public class Day {public string Date,ColorEncoding;public Person[] People;public bool SinglePerson;public bool AllowReverseColors=true;}
public class State {public const string CurrentEngine="normal-antenna-patterns-v5";public string Engine=CurrentEngine,DataHash,Status="未探索";public Day[] Days;public ulong Next;public List<uint> Keys=new List<uint>();public double Seconds;}
public class Candidate {public string Name,Antenna,BaseAntenna,Payload;public int Color1,Color2,Row,Level,Generation,GenerationLimit,RawPrimary,RawSecondary,Slot,Head,Body,Pattern,AntennaGroup;public int[] Face;}
public class NormalGenerationCaps {public string schema;public int[][] ranges;}
public static class Json {
    public static JavaScriptSerializer Serializer(){return new JavaScriptSerializer{MaxJsonLength=16*1024*1024,RecursionLimit=128};}
    public static T Read<T>(string path){if(new FileInfo(path).Length>16*1024*1024)throw new Exception("ファイルが大きすぎます。");return Serializer().Deserialize<T>(File.ReadAllText(path,Encoding.UTF8));}
    public static void Write(string path,object value){
        string json=Serializer().Serialize(value);
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using(var writer=new StreamWriter(stream,new UTF8Encoding(false)))writer.Write(json);
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
public struct Rng {
    public uint A,B,C,D;
    public void Advance(){unchecked{uint x=(A&0x7fffffff)^B^C;x^=x<<1;uint y=D^(D>>1)^x,m=0U-(y&1),c=x^(y<<10);A=B;B=C^(0x8f7011ee&m);C=c^(0xfc78ff1f&m);D=y;}}
    public uint Next(){Advance();unchecked{uint t=A+(C>>8);return D^t^(0x3793fdff&(0U-(t&1)));}}
    public static Rng Init(uint seed){unchecked{Rng r=new Rng{A=seed,B=0x8f7011ee,C=0xfc78ff1f,D=0x3793fdff};uint previous=seed;for(uint i=1;i<8;i++){uint value=i+0x6c078965*(previous^(previous>>30));switch(i&3){case 0:r.A^=value;previous=r.A;break;case 1:r.B^=value;previous=r.B;break;case 2:r.C^=value;previous=r.C;break;default:r.D^=value;previous=r.D;break;}}if(((r.A&0x7fffffff)|r.B|r.C|r.D)==0){r.A=84;r.B=73;r.C=78;r.D=89;}for(int i=0;i<8;i++)r.Advance();return r;}}
}
public class CompiledDay {public int Number,HeadMask;public Person[] People;public int[] Names;public bool[] Eligible;public bool SinglePerson;public bool AllowReverseColors=true;}
public sealed partial class Model {
    public readonly Tables Data;public readonly string DataHash,DataDirectory;readonly Dictionary<string,int> nameIds;readonly Dictionary<int,string> abilityNames;readonly HashSet<ulong> special;readonly int total,patternTotal;readonly int[] firstRows;readonly string[] baseAntennas;
    public Model(string path){DataDirectory=Path.GetDirectoryName(Path.GetFullPath(path));Data=Json.Read<Tables>(path);using(var sha=SHA256.Create())DataHash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");
        Data.abilities["0"]="アンテナなし";
        if(Data.schema!="free-normal-finder-v1"||Data.rows.Length!=79||Data.raw_color_labels==null||Data.raw_color_labels.Length!=39)throw new Exception("対応しない解析データです。");nameIds=new Dictionary<string,int>(StringComparer.Ordinal);for(int i=0;i<Data.names.Length;i++)nameIds[Data.names[i]]=i;special=new HashSet<ulong>(Data.special);abilityNames=Data.abilities.ToDictionary(p=>Int32.Parse(p.Key,CultureInfo.InvariantCulture),p=>p.Value);
        total=Data.rows.Sum(r=>r.weight);patternTotal=Data.patterns.Sum(r=>r.weight);firstRows=new int[total];int offset=0;for(int i=0;i<Data.rows.Length;i++)for(int k=0;k<Data.rows[i].weight;k++)firstRows[offset++]=i;
        baseAntennas=Data.rows.Select(r=>AntennaFamilyName(r.antenna_group)).ToArray();
        var caps=Json.Read<NormalGenerationCaps>(Path.Combine(DataDirectory,"normal-generation-cap.json"));if(caps.schema!="free-normal-generation-cap-v1"||caps.ranges==null||caps.ranges.Length!=Data.rows.Length||caps.ranges.Any(r=>r==null||r.Length!=2||r[0]<1||r[1]>9||r[0]>r[1]))throw new Exception("通常レアの世代上限データが不正です。");generationCaps=caps.ranges;
        LoadAppearance(Path.GetDirectoryName(path));LoadBirth(Path.GetDirectoryName(path));
    }
    public string[] Antennas(){return OrderAntennaNames(baseAntennas);}
    // RPG3 Editor standard order, followed by its dual-element options. FREE-only families follow.
    static readonly int[] EditorAntennaGroups={0,1,2,4,5,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,23,24,25,26,29,30,31,32,33,34,35,36,37,38,39,40,41,42,44,45,46,47,48,49,50,51,52,58,59,60,66,67,68,74,75,76,82,83,84,90,91,92,98,99,100,102,103,104,106,53,54,55,56,57,62,63,64,65,71,72,73,80,81,89,101};
    public string[] OrderAntennaNames(IEnumerable<string> names){
        var available=names.Distinct().ToArray();var order=EditorAntennaGroups.Concat(Enumerable.Range(0,Data.antenna_rows.Length)).Select(AntennaFamilyName).Distinct();
        return order.Where(available.Contains).Concat(available.Where(n=>!order.Contains(n))).ToArray();
    }
    readonly int[][] generationCaps;
    public string AntennaFamilyName(int group){
        if(group==0)return Data.abilities["0"];
        var row=Data.antenna_rows[group];int id=(int)(row.ids[1]&65535),second=(int)(row.ids[2]&65535);
        // Dual-element branches share their first single-element stage; name the first dual stage.
        if(second>=250&&second<=281)id=second;
        return Data.abilities[id.ToString(CultureInfo.InvariantCulture)];
    }
    public string DisplayAntenna(Candidate person){return person.Antenna==abilityNames[1]?person.BaseAntenna:person.Antenna;}
    public bool AntennaMatches(string selected,string actual,int row){return String.IsNullOrEmpty(selected)||selected==actual||selected==baseAntennas[row];}
    public CompiledDay[] Compile(Day[] days){
        if(days==null||days.Length==0||days.Length>31)throw new Exception("1～31日分の記録を入力してください。");var seen=new HashSet<string>();var output=new List<CompiledDay>();var heads=new HashSet<int>(RareHeads().Select(h=>h.Value));
        foreach(Day day in days){if(day==null||day.ColorEncoding!="color-query-v2")throw new Exception("対応する色の記録形式ではありません。旧記録は入力画面で開いてください。");DateTime date;if(!DateTime.TryParseExact(day.Date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date)||date.Year<2000||date.Year>2099)throw new Exception("日付は2000～2099年で入力してください。");if(!seen.Add(day.Date))throw new Exception("同じ日付が重複しています。");if(day.SinglePerson){output.Add(CompileSingle(day,date));continue;}if(day.People==null||day.People.Length!=10)throw new Exception("各日付に、捕獲・補充前の10人を入力してください。");
            var ids=new int[10];for(int i=0;i<10;i++){var p=day.People[i];if(p==null||String.IsNullOrWhiteSpace(p.Name)||!nameIds.TryGetValue(p.Name.Trim(),out ids[i]))throw new Exception(day.Date+" の "+(i+1)+"人目の名前を確認してください。");p.Name=p.Name.Trim();if(p.Antenna==null)p.Antenna="";if(p.Antenna=="なし")p.Antenna="アンテナなし";if(p.Antenna!=""&&!Data.abilities.Values.Contains(p.Antenna))throw new Exception("アンテナは選択肢から選んでください。");if(!ValidColor(p.Color1)||!ValidColor(p.Color2))throw new Exception("色の指定が不正です。");}
            int headMask=0;for(int i=0;i<10;i++){if(!heads.Contains(day.People[i].Head))throw new Exception("頭は通常レアの候補から選んでください。");if(!IdentificationPatterns().Any(p=>p.Value==day.People[i].Pattern))throw new Exception("柄はマーブル・ダイヤ・虎・レンガ、または未指定を選んでください。");if(day.People[i].Head>=0)headMask|=1<<i;}
            bool[] eligible=new bool[Data.rows.Length];var wanted=new HashSet<int>(ids);for(int i=0;i<Data.rows.Length;i++)eligible[i]=Data.rows[i].name_ids.Any(n=>wanted.Contains(n));
            output.Add(new CompiledDay{Number=(date-new DateTime(1999,1,1)).Days,People=day.People,Names=ids,Eligible=eligible,HeadMask=headMask});
        }return output.OrderBy(d=>d.Number).ToArray();
    }
    bool ValidColor(int key){return key==-1||key==0||(key>=3&&key<=38)||IsFamilyColor(key);}
    CompiledDay CompileSingle(Day day,DateTime date){
        if(day.People==null||day.People.Length!=1||day.People[0]==null)throw new Exception("1人分の情報を入力してください。");
        var p=day.People[0];int name;if(String.IsNullOrWhiteSpace(p.Name)||!nameIds.TryGetValue(p.Name.Trim(),out name))throw new Exception("名前を確認してください。");p.Name=p.Name.Trim();p.Antenna=p.Antenna??"";
        if(p.Antenna!=""&&!Data.abilities.Values.Contains(p.Antenna))throw new Exception("アンテナを確認してください。");
        if(!ValidColor(p.Color1)||!ValidColor(p.Color2)||!RareHeads().Any(c=>c.Value==p.Head)||!Bodies().Any(c=>c.Value==p.Body)||!PatternFilterChoices().Any(c=>c.Value==p.Pattern))throw new Exception("色・頭・体格・柄を確認してください。");
        if(p.Generation!=-1&&(p.Generation<1||p.Generation>MaxGeneration)||p.GenerationLimit!=-1&&(p.GenerationLimit<1||p.GenerationLimit>MaxGenerationLimit)||p.Level!=-1&&p.Level!=10&&p.Level!=16)throw new Exception("世代・レベルを確認してください。");
        if(p.Generation>=0&&p.GenerationLimit>=0&&p.Generation>p.GenerationLimit)throw new Exception("現在世代は成長上限以下にしてください。");
        if(p.Face!=null&&(p.Face.Length!=FaceNames.Length||p.Face.Where((v,i)=>!FaceChoices(i).Contains(v)).Any()))throw new Exception("顔パーツを確認してください。");
        return new CompiledDay{Number=(date-new DateTime(1999,1,1)).Days,People=day.People,Names=new[]{name},SinglePerson=true,AllowReverseColors=day.AllowReverseColors,Eligible=Data.rows.Select(r=>r.name_ids.Contains(name)).ToArray()};
    }
    bool MatchesSingle(uint seed,CompiledDay d){
        Rng rng=Rng.Init(seed);ulong used0=0,used1=0;int sum=total;var p=d.People[0];
        for(int slot=0;slot<10;slot++){
            int row=Select(ref rng,slot,sum,used0,used1);if(row<64)used0|=1UL<<row;else used1|=1UL<<(row-64);sum-=Data.rows[row].weight;
            int a,b,level,head,generation,pattern,cap;ulong payload;string antenna;int name=NextPerson(ref rng,row,out a,out b,out antenna,out level,out payload,out head,out generation,out pattern,out cap);
            if(!d.Eligible[row]||name!=d.Names[0]||!AntennaMatches(p.Antenna,antenna,row)||!ColorPairMatches(p.Color1,p.Color2,a,b,!d.AllowReverseColors)||!PatternMatches(p.Pattern,pattern)||p.Generation>=0&&p.Generation!=generation+1||p.GenerationLimit>=0&&p.GenerationLimit!=cap||p.Level>=0&&p.Level!=level)continue;
            if(p.Head>=0||p.Body>=0||p.Face!=null){var actual=new Candidate();ProjectAppearance(actual,payload,head);if(p.Head>=0&&p.Head!=actual.Head||p.Body>=0&&p.Body!=actual.Body||!FaceMatches(p.Face,actual.Face))continue;}
            return true;
        }return false;
    }
    uint Pattern(uint raw){int rem=(int)(raw%(uint)patternTotal);foreach(var p in Data.patterns){rem-=p.weight;if(rem<0)return p.id;}throw new Exception("柄の候補表が不正です。");}
    int Axis(int index,uint raw){if((raw&0x80000000)!=0)return 0;var row=Data.rarity[index];int rem=(int)(raw%(uint)row.total),sum=0;for(int i=0;i<5;i++){sum=(sum+row.weights[i])&65535;if(rem<sum)return i;}return 5;}
    int RandomColor(ref Rng rng,int axisIndex,int element){int axis=Axis(axisIndex,rng.Next());if(axis>=Data.color_axes.Length)throw new Exception("色の範囲が未対応です。");int row=Data.color_axes[axis]+element;if(row<0||row>=Data.color_tables.Length)row=0;int[] colors=Data.color_tables[row];return colors[(rng.Next()&65535)%(uint)colors.Length];}
    static int Inclusive(ref Rng rng,int a,int b){int lo=Math.Min(a,b),hi=Math.Max(a,b);return lo+(int)(rng.Next()%(uint)(hi-lo+1));}
    public int MaxGeneration {get{return Data.rows.Max(r=>r.generation.Max());}}
    public int MaxGenerationLimit {get{return generationCaps.Max(r=>r.Max());}}
    int NextPerson(ref Rng rng,int index,out int primary,out int secondary,out string ability,out int level,out ulong payload,out int head,out int generation,out int patternCode,out int generationLimit){
        int depth=0;while(true){payload=0;for(int k=0;k<6;k++)payload|=(ulong)(rng.Next()&255)<<(k*8);if(!special.Contains(payload))break;if(++depth>42)throw new Exception("特殊生成が対応範囲を超えました。");}for(int k=0;k<depth*5;k++)rng.Next();
        Row r=Data.rows[index];head=-1;if(((r.flags>>5)&7)!=0){uint raw=rng.Next();if(appearance!=null){int[] choices=appearance.row_heads[index];head=choices[(int)(raw%(uint)choices.Length)];}}uint pattern=(r.flags&1)!=0?Pattern(rng.Next()):r.pattern;
        primary=r.primary.Length!=0?(int)(r.primary[rng.Next()%(uint)r.primary.Length]&255):RandomColor(ref rng,4,r.element);
        if(r.secondary.Length!=0){uint id=r.secondary[rng.Next()%(uint)r.secondary.Length];if(pattern==0x80000000&&id!=0x80000002)id=0x80000000;secondary=(int)(id&255);}else secondary=pattern==0x80000000?0:RandomColor(ref rng,5,r.element);
        patternCode=(int)(pattern&255);if(primary==secondary){secondary=0;patternCode=0;}
        generation=(Inclusive(ref rng,r.generation[0],r.generation[1])-1)&255;var cap=generationCaps[index];generationLimit=Math.Max(generation+1,Inclusive(ref rng,cap[0],cap[1]));level=Math.Min(Math.Min(Inclusive(ref rng,r.levels[0],r.levels[1]),Data.capmax[generation]),999);if(level<=0)throw new Exception("レベル0の経路は未対応です。");
        int current=0;if(r.antenna_group!=0){Antenna ant=Data.antenna_rows[r.antenna_group<Data.antenna_rows.Length?r.antenna_group:0];int stage=0;foreach(int threshold in ant.thresholds){if(level<threshold)break;stage++;}current=stage==0?1:(int)(ant.ids[stage]&65535);}
        if(!abilityNames.TryGetValue(current,out ability))throw new Exception("未対応のアンテナ能力です（ID="+current+"、系統="+r.antenna_group+"、Lv="+level+"）。");
        return r.name_ids[(rng.Next()&65535)%(uint)r.name_ids.Length];
    }
    static bool Used(int row,ulong a,ulong b){return row<64?((a>>row)&1)!=0:((b>>(row-64))&1)!=0;}
    int Select(ref Rng r,int slot,int sum,ulong used0,ulong used1){int rem=(int)(r.Next()%(uint)sum);if(slot==0)return firstRows[rem];for(int i=0;i<Data.rows.Length;i++){if(Used(i,used0,used1))continue;rem-=Data.rows[i].weight;if(rem<0)return i;}throw new Exception("候補行がありません。");}
    public static bool IsFamilyColor(int selection){return selection>=103&&selection<=136&&(selection-100)%3==0;}
    public static bool ColorSelectionMatches(int selection,int actual){
        if(selection==-1)return true;
        if(IsFamilyColor(selection))return actual>=3&&actual<=38&&selection==100+actual-actual%3;
        return selection==actual;
    }
    public static bool ColorPairMatches(int color1,int color2,int actual1,int actual2,bool ordered){
        bool direct=ColorSelectionMatches(color1,actual1)&&ColorSelectionMatches(color2,actual2);
        return direct||(!ordered&&ColorSelectionMatches(color1,actual2)&&ColorSelectionMatches(color2,actual1));
    }
    static bool ColorMatches(Person p,int a,int b){return ColorPairMatches(p.Color1,p.Color2,a,b,false);}
    public bool Matches(uint key,CompiledDay[] days){foreach(var d in days)if(!MatchesDay(unchecked(key+(uint)d.Number),d))return false;return true;}
    bool MatchesDay(uint seed,CompiledDay d){if(d.SinglePerson)return MatchesSingle(seed,d);Rng rng=Rng.Init(seed);ulong used0=0,used1=0,masks0=0,masks1=0;int sum=total,union=0;
        for(int slot=0;slot<10;slot++){int index=Select(ref rng,slot,sum,used0,used1);if(!d.Eligible[index])return false;if(index<64)used0|=1UL<<index;else used1|=1UL<<(index-64);sum-=Data.rows[index].weight;
            int a,b,level,head,generation,pattern,generationLimit;ulong payload;string antenna;int name=NextPerson(ref rng,index,out a,out b,out antenna,out level,out payload,out head,out generation,out pattern,out generationLimit);if(a==b)b=0;
            int mask=0;for(int i=0;i<10;i++)if(d.Names[i]==name&&AntennaMatches(d.People[i].Antenna,antenna,index)&&ColorMatches(d.People[i],a,b)&&(d.People[i].Pattern<0||d.People[i].Pattern==pattern))mask|=1<<i;if(mask==0)return false;
            if((mask&d.HeadMask)!=0){int actualHead=HeadForPayload(payload,head);for(int i=0;i<10;i++)if(d.People[i].Head>=0&&d.People[i].Head!=actualHead)mask&=~(1<<i);if(mask==0)return false;}
            union|=mask;if(PopCount(union)<slot+1)return false;
            if(slot<6)masks0|=(ulong)mask<<(slot*10);else masks1|=(ulong)mask<<((slot-6)*10);
        }int[] owners=Enumerable.Repeat(-1,10).ToArray();for(int i=0;i<10;i++){int seen=0;if(!Assign(i,masks0,masks1,owners,ref seen))return false;}return true;
    }
    static int PopCount(int value){int n=0;while(value!=0){value&=value-1;n++;}return n;}
    static bool Assign(int person,ulong a,ulong b,int[] owners,ref int seen){int mask=(int)((person<6?a>>(person*10):b>>((person-6)*10))&1023);for(int i=0;i<10;i++){int bit=1<<i;if((mask&bit)==0||(seen&bit)!=0)continue;seen|=bit;if(owners[i]<0||Assign(owners[i],a,b,owners,ref seen)){owners[i]=person;return true;}}return false;}
    public Candidate[] Forward(uint seed){return Forward(seed,10);}
    public Candidate[] Forward(uint seed,int maxSlots){
        if(maxSlots<1||maxSlots>Data.rows.Length)throw new Exception("検索枠数は1～79枠で指定してください。");
        var list=new List<Candidate>();Rng rng=Rng.Init(seed);ulong used0=0,used1=0;int sum=total;
        for(int slot=0;slot<maxSlots;slot++){int i=Select(ref rng,slot,sum,used0,used1);if(i<64)used0|=1UL<<i;else used1|=1UL<<(i-64);sum-=Data.rows[i].weight;int a,b,level,head,generation,pattern,generationLimit;ulong payload;string ability;int name=NextPerson(ref rng,i,out a,out b,out ability,out level,out payload,out head,out generation,out pattern,out generationLimit);
            var person=new Candidate{Name=Data.names[name],Antenna=ability,BaseAntenna=baseAntennas[i],Color1=a,Color2=b,Row=i,Level=level,Generation=generation+1,GenerationLimit=generationLimit,RawPrimary=a,RawSecondary=b,Slot=slot+1,Pattern=pattern,AntennaGroup=Data.rows[i].antenna_group};ProjectAppearance(person,payload,head);list.Add(person);
        }return list.ToArray();
    }
}
public sealed class SearchJob {
    public volatile bool Stop;public readonly object Sync=new object();public readonly State State;readonly Model model;readonly CompiledDay[] days;readonly string path;
    public SearchJob(Model model,State state,string path){if(state.Engine!=State.CurrentEngine||state.Next>0x100000000UL)throw new Exception("探索状態の形式が異なります。");this.model=model;State=state;this.path=path;days=model.Compile(state.Days);}
    public void Save(){if(String.IsNullOrEmpty(path))return;lock(Sync)Json.Write(path,State);}
    public void Run(double seconds,int workers){if(Double.IsNaN(seconds)||seconds<=0||(seconds>600&&!Double.IsPositiveInfinity(seconds))||workers<1||workers>4)throw new Exception("探索設定が不正です。");var clock=Stopwatch.StartNew();State.Status="探索中";Save();
        try{while(State.Next<0x100000000UL&&!Stop&&clock.Elapsed.TotalSeconds<seconds){ulong first=State.Next,count=Math.Min(1048576UL,0x100000000UL-first);var hits=new ConcurrentBag<uint>();int chunks=(int)((count+65535)/65536);
                Parallel.For(0,chunks,new ParallelOptions{MaxDegreeOfParallelism=workers},i=>{ulong start=first+(ulong)i*65536,end=Math.Min(first+count,start+65536);for(ulong key=start;key<end;key++)if(model.Matches((uint)key,days))hits.Add((uint)key);});
                lock(Sync){State.Next=first+count;State.Keys.AddRange(hits);State.Keys.Sort();}Save();
            }State.Status=State.Next==0x100000000UL?"全範囲の探索終了":Stop?"中断":"時間上限で中断";
        }catch{State.Status="エラー・最後の完了区間から再開できます";throw;}finally{clock.Stop();State.Seconds+=clock.Elapsed.TotalSeconds;Save();}
    }
    static bool ColorRefines(int oldColor,int newColor){return oldColor==-1||oldColor==newColor||(Model.IsFamilyColor(oldColor)&&newColor>=3&&newColor<=38&&Model.ColorSelectionMatches(oldColor,newColor));}
    public static bool CanRefine(Day[] oldDays,Day[] newDays){if(oldDays==null||newDays==null)return false;foreach(var old in oldDays){Day next=newDays.FirstOrDefault(d=>d.Date==old.Date);if(next==null||next.SinglePerson!=old.SinglePerson||next.ColorEncoding!=old.ColorEncoding||next.People.Length!=old.People.Length)return false;if(old.SinglePerson){if(old.AllowReverseColors!=next.AllowReverseColors)return false;if(Json.Serializer().Serialize(old.People)!=Json.Serializer().Serialize(next.People))return false;continue;}for(int i=0;i<old.People.Length;i++){Person a=old.People[i],b=next.People[i];bool colors=(ColorRefines(a.Color1,b.Color1)&&ColorRefines(a.Color2,b.Color2))||(ColorRefines(a.Color1,b.Color2)&&ColorRefines(a.Color2,b.Color1));if(a.Name!=b.Name||(a.Antenna!=""&&a.Antenna!=b.Antenna)||(a.Head>=0&&a.Head!=b.Head)||(a.Pattern>=0&&a.Pattern!=b.Pattern)||!colors)return false;}}return true;}
}


}
