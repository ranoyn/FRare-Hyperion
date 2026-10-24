using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FreeNormalFinder {
// Optional, separate metadata. Never write fruit notes into genetic/raw fields.
public sealed class DopingEntry {public int Generation;public int[] Counts;}
public sealed class DopingCalculation {
    public int[] Current=new int[5],Inherited=new int[5];public bool Complete=true;public bool Legacy;
    public int[] Total {get{return Current.Zip(Inherited,(a,b)=>a+b).ToArray();}}
}
public static class DopingNotes {
    public static int BirthInheritance(DopingCalculation a,DopingCalculation b,int stat){return Math.Min(255,(a.Current[stat]+b.Current[stat])/4+Math.Max(a.Inherited[stat],b.Inherited[stat]));}
    public static int ReincarnationInheritance(DopingCalculation previous,int stat){return Math.Min(255,previous.Inherited[stat]+previous.Current[stat]/2);}
    static readonly int[] Limits={3,5,7,9,12,15,18,21,24};
    public static int Limit(int generation){if(generation<1||generation>9)throw new ArgumentException("ドーピング世代は1～9で指定してください。");return Limits[generation-1];}
    public static DopingEntry[] Copy(DopingEntry[] notes){return notes==null?null:notes.Select(e=>new DopingEntry{Generation=e.Generation,Counts=(int[])e.Counts.Clone()}).ToArray();}
    public static int[] Counts(DopingEntry[] notes,int generation){var row=notes==null?null:notes.FirstOrDefault(e=>e.Generation==generation);return row==null?new int[5]:(int[])row.Counts.Clone();}
    public static void Validate(DopingEntry[] notes,int first,int last){
        if(notes==null)return;var seen=new HashSet<int>();foreach(var e in notes){if(e==null||e.Generation<first||e.Generation>last||!seen.Add(e.Generation)||e.Counts==null||e.Counts.Length!=5||e.Counts.Any(n=>n<0||n>24)||e.Counts.Sum()>Limit(e.Generation))throw new ArgumentException("世代別のドーピングメモが不正です。果物の個数と世代範囲を確認してください。");}
    }
    public static int[] RemovedGenerations(DopingEntry[] notes,int first,int last){return (notes??new DopingEntry[0]).Where(e=>(e.Generation<first||e.Generation>last)&&e.Counts.Any(n=>n!=0)).Select(e=>e.Generation).Distinct().OrderBy(g=>g).ToArray();}
    public static string RemovalMessage(IEnumerable<int> generations){return String.Join("・",generations.Distinct().OrderBy(g=>g).Select(g=>g.ToString()))+"世代のドーピング入力は消えます。\r\n世代範囲を変更しますか？";}
    public static DopingCalculation Calculate(BirthLineage root,int generation){return Calculate(root,generation,new Dictionary<BirthLineage,Dictionary<int,DopingCalculation>>(),new HashSet<BirthLineage>(),0);}
    static DopingCalculation Calculate(BirthLineage node,int generation,Dictionary<BirthLineage,Dictionary<int,DopingCalculation>> cache,HashSet<BirthLineage> path,int depth){
        var result=new DopingCalculation();if(node==null||node.Raw==null||node.Raw.Length!=252||depth>30||path.Contains(node)){result.Complete=false;return result;}
        Dictionary<int,DopingCalculation> values;if(cache.TryGetValue(node,out values)&&values.ContainsKey(generation))return values[generation];
        int first=node.Raw[0x82]+1,last=Math.Max(first,node.Raw[0x83]+1);if(generation<first||generation>last||generation>9){result.Complete=false;return result;}Validate(node.DopingMemo,first,last);path.Add(node);
        if(node.ParentA!=null||node.ParentB!=null){var a=Calculate(node.ParentA,first-1,cache,path,depth+1);var b=Calculate(node.ParentB,first-1,cache,path,depth+1);result.Complete=a.Complete&&b.Complete;result.Legacy=a.Legacy||b.Legacy;for(int i=0;i<5;i++)result.Inherited[i]=BirthInheritance(a,b,i);}
        else {for(int i=0;i<5;i++)result.Inherited[i]=node.Raw[0xA6+i];if(first>1&&!result.Inherited.Any(n=>n!=0)&&!(node.Source??"").Contains("BIN"))result.Complete=false;}
        // Legacy boost bytes stay untouched; they are not silently reinterpreted as fruit counts.
        if(Enumerable.Range(0,5).Any(i=>node.Raw[0xA0+i]!=0)){result.Legacy=true;result.Complete=false;}
        for(int g=first;g<=generation;g++){int[] counts=Counts(node.DopingMemo,g);for(int i=0;i<5;i++){result.Current[i]=counts[i]*2;if(g<generation)result.Inherited[i]=ReincarnationInheritance(result,i);}}
        path.Remove(node);if(!cache.TryGetValue(node,out values)){values=new Dictionary<int,DopingCalculation>();cache[node]=values;}values[generation]=result;return result;
    }
}
public sealed class DopingMemoEditor:UserControl {
    public readonly ComboBox GenerationPicker=new ComboBox();public readonly NumericUpDown[] Inputs=Enumerable.Range(0,5).Select(i=>new NumericUpDown()).ToArray();
    public Func<string,bool> ConfirmDiscard;public event EventHandler Changed;
    readonly Label allowance=new Label();readonly FlowLayoutPanel row=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};readonly Dictionary<int,int[]> data=new Dictionary<int,int[]>(),dirty=new Dictionary<int,int[]>();readonly HashSet<int> discarded=new HashSet<int>();bool filling;int first=1,last=1,shown;
    public DopingMemoEditor(string prefix):this(prefix,true){}
    public DopingMemoEditor(string prefix,bool showGeneration){
        Name=prefix+"Memo";Height=60;Dock=DockStyle.Fill;Margin=Padding.Empty;Controls.Add(row);
        GenerationPicker.Name=prefix+"Generation";GenerationPicker.Width=115;GenerationPicker.DropDownStyle=ComboBoxStyle.DropDownList;GenerationPicker.Visible=showGeneration;if(showGeneration){row.Controls.Add(new Label{Text="ドーピング世代"});row.Controls.Add(GenerationPicker);}
        string[] names={"HP","AP","こうげきりょく","ぼうぎょりょく","すばやさ"};for(int i=0;i<5;i++){row.Controls.Add(new Label{Text=names[i]});var box=Inputs[i];box.Name=prefix+i;box.Width=95;box.Maximum=24;row.Controls.Add(box);int stat=i;box.ValueChanged+=delegate{if(filling||shown==0)return;data[shown][stat]=(int)box.Value;if(!dirty.ContainsKey(shown))dirty[shown]=new int[5];dirty[shown][stat]=1;UpdateLimits();if(Changed!=null)Changed(this,EventArgs.Empty);};}
        for(int i=0;i<5;i++){int stat=i;Inputs[i].KeyDown+=delegate(object sender,KeyEventArgs e){if(filling||shown==0)return;if(e.KeyCode==Keys.Delete||e.KeyCode==Keys.Back||e.KeyCode>=Keys.D0&&e.KeyCode<=Keys.D9||e.KeyCode>=Keys.NumPad0&&e.KeyCode<=Keys.NumPad9){if(!dirty.ContainsKey(shown))dirty[shown]=new int[5];dirty[shown][stat]=1;}};}
        ToolForm.StackFieldLabels(row);allowance.Name=prefix+"Allowance";allowance.AutoSize=true;allowance.Margin=new Padding(6,26,0,0);row.Controls.Add(allowance);
        GenerationPicker.SelectedIndexChanged+=delegate{if(filling)return;shown=GenerationPicker.SelectedItem==null?0:(int)GenerationPicker.SelectedItem;FillCounts();if(Changed!=null)Changed(this,EventArgs.Empty);};LoadNotes(null,1,1);
    }
    public bool HasEdits {get{return dirty.Count>0||discarded.Count>0;}}
    public bool IsStatEdited(int stat){int[] edits;return dirty.TryGetValue(shown,out edits)&&edits[stat]!=0;}
    public void AddAction(Button action){action.Margin=new Padding(3,24,3,0);row.Controls.Add(action);row.Controls.SetChildIndex(action,row.Controls.IndexOf(allowance));}
    public void RestoreDraft(DopingEntry[] notes,int lower,int upper){LoadNotes(notes,lower,upper);foreach(int g in data.Keys)dirty[g]=new[]{1,1,1,1,1};if(Changed!=null)Changed(this,EventArgs.Empty);}
    protected override void Dispose(bool disposing){if(disposing&&GenerationPicker.Parent==null)GenerationPicker.Dispose();base.Dispose(disposing);}
    public int SelectedGeneration {get{return shown;}}
    public void SetUnavailable(){filling=true;try{data.Clear();dirty.Clear();discarded.Clear();shown=0;GenerationPicker.Items.Clear();foreach(var input in Inputs){input.Maximum=24;input.Value=0;}allowance.Text="";Enabled=false;}finally{filling=false;}}
    public DopingEntry[] Notes {get{return data.Where(p=>p.Value.Any(n=>n!=0)).OrderBy(p=>p.Key).Select(p=>new DopingEntry{Generation=p.Key,Counts=(int[])p.Value.Clone()}).ToArray();}}
    public void LoadNotes(DopingEntry[] notes,int lower,int upper){
        DopingNotes.Validate(notes,lower,upper);data.Clear();dirty.Clear();discarded.Clear();foreach(var entry in notes??new DopingEntry[0])data[entry.Generation]=(int[])entry.Counts.Clone();first=lower;last=upper;shown=lower;FillGenerations();
    }
    public bool SetRange(int lower,int upper,bool confirm){
        if(lower<1||upper<lower||upper>9){Enabled=false;return true;}
        int[] removed=DopingNotes.RemovedGenerations(Notes,lower,upper);if(confirm&&removed.Length>0&&!Confirm(DopingNotes.RemovalMessage(removed)))return false;
        foreach(int g in removed)discarded.Add(g);foreach(int g in data.Keys.Where(g=>g<lower||g>upper).ToArray()){data.Remove(g);dirty.Remove(g);}first=lower;last=upper;shown=Math.Max(first,Math.Min(last,shown));Enabled=true;FillGenerations();return true;
    }
    bool Confirm(string message){return ConfirmDiscard!=null?ConfirmDiscard(message):MessageBox.Show(FindForm(),message,"ドーピングメモ",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes;}
    void FillGenerations(){filling=true;try{GenerationPicker.Items.Clear();for(int g=first;g<=last;g++)GenerationPicker.Items.Add(g);GenerationPicker.SelectedItem=shown;}finally{filling=false;}FillCounts();}
    void FillCounts(){filling=true;try{if(shown>0&&!data.ContainsKey(shown))data[shown]=new int[5];for(int i=0;i<5;i++){Inputs[i].Maximum=24;Inputs[i].Value=shown==0?0:data[shown][i];}}finally{filling=false;}UpdateLimits();}
    void UpdateLimits(){if(shown==0)return;int sum=data[shown].Sum(),limit=DopingNotes.Limit(shown);filling=true;try{for(int i=0;i<5;i++)Inputs[i].Maximum=limit-sum+data[shown][i];allowance.Text=sum+" / "+limit+"個";}finally{filling=false;}}
    public DopingEntry[] ApplyTo(DopingEntry[] original,int lower,int upper,bool patch){
        if(original==null&&!HasEdits)return null;
        var result=(original??new DopingEntry[0]).Where(e=>e.Generation>=lower&&e.Generation<=upper&&!discarded.Contains(e.Generation)).ToDictionary(e=>e.Generation,e=>(int[])e.Counts.Clone());
        if(!patch)return Notes.Where(e=>e.Generation>=lower&&e.Generation<=upper).ToArray();
        foreach(var item in dirty){if(item.Key<lower||item.Key>upper)throw new Exception(item.Key+"世代は対象個体の世代範囲外です。");int[] counts;if(!result.TryGetValue(item.Key,out counts)){counts=new int[5];result[item.Key]=counts;}for(int i=0;i<5;i++)if(item.Value[i]!=0)counts[i]=data[item.Key][i];}
        var notes=result.Where(e=>e.Value.Any(n=>n!=0)).OrderBy(e=>e.Key).Select(e=>new DopingEntry{Generation=e.Key,Counts=e.Value}).ToArray();DopingNotes.Validate(notes,lower,upper);return notes;
    }
}
}
