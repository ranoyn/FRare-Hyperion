using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;

namespace FreeNormalFinder {
// Deliberately independent of BirthParent/Raw and the birth-input save format.
public sealed class ManualMemoIndividual {
    public string Id,Name;public int Generation;public int? Antenna,Head,Color1,Color2,Pattern,Body;public int? PreviewBodyColor;public string PortraitPng;
    public int?[] Face=new int?[9],Doping=new int?[5];
    public ManualMemoIndividual Copy(){return new ManualMemoIndividual{Id=Id,Name=Name,Generation=Generation,PreviewBodyColor=PreviewBodyColor,PortraitPng=PortraitPng,Antenna=Antenna,Head=Head,Color1=Color1,Color2=Color2,Pattern=Pattern,Body=Body,Face=Face==null?null:(int?[])Face.Clone(),Doping=Doping==null?null:(int?[])Doping.Clone()};}
}
public sealed class ManualMemoDocument {public string Schema="free-manual-doping-memo-v4";public int Version=4;public string Id,Label;public ManualMemoIndividual[] Individuals;public int[] VisibleGenerations;public ManualMemoBranch[] Branches;public string[] Roots;}
public sealed class ManualMemoSavedChoice {public string Id,Label,FileName;public override string ToString(){return Label;}}
public sealed partial class ToolForm {
    readonly Panel manualMemoPage=new Panel();readonly Button manualMemoNav=new Button();readonly ComboBox manualMemoInput=new ComboBox();readonly Button manualMemoDelete=new Button();
    readonly List<ManualMemoIndividual> manualMemoPeople=new List<ManualMemoIndividual>();readonly Dictionary<BirthLineage,ManualMemoIndividual> manualMemoRecords=new Dictionary<BirthLineage,ManualMemoIndividual>();
    readonly Dictionary<BirthLineage,int> manualMemoPluses=new Dictionary<BirthLineage,int>();readonly CheckBox[] manualMemoGenerations=new CheckBox[10];
    LineageDiagram manualMemoDiagram;bool renderingManualMemo,manualMemoDirty,changingManualMemoInput,changingManualMemoGenerations;string manualMemoPath,manualMemoId,manualMemoLabel;long manualMemoLength,manualMemoWriteTicks;
    public Func<string,bool> ConfirmManualMemoDiscard;
    public string ManualMemoStorageDirectory {get{return Path.Combine(Path.GetDirectoryName(savedInputsDirectory),"ドーピングメモ");}}
    public ManualMemoIndividual[] ManualMemoIndividuals {get{return manualMemoPeople.Select(p=>p.Copy()).ToArray();}}
    public BirthLineage[] ManualMemoRoots {get{return manualMemoRecords.Keys.ToArray();}}
    void BuildManualDopingMemo(){
        manualMemoNav.Name="manualDopingMemoModeButton";manualMemoNav.Text="新規ドーピングメモ";manualMemoNav.Size=new Size(190,35);foreach(var button in new[]{identifyNav,predictNav,gekiNav,birthNav})button.Width=140;lineageNav.Width=230;birthNav.Parent.Controls.Add(manualMemoNav);manualMemoPage.Name="manualDopingMemoPage";manualMemoPage.Dock=DockStyle.Fill;manualMemoPage.Visible=false;lineagePage.Parent.Controls.Add(manualMemoPage);manualMemoNav.Click+=delegate{ShowManualDopingMemo();};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};foreach(int h in new[]{38,72,0,110})layout.RowStyles.Add(h==0?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.Absolute,h));manualMemoPage.Controls.Add(layout);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};layout.Controls.Add(actions,0,0);actions.Controls.Add(LabelFor("入力"));manualMemoInput.Name="manualMemoInput";manualMemoInput.Width=300;manualMemoInput.DropDownWidth=420;manualMemoInput.DropDownStyle=ComboBoxStyle.DropDownList;actions.Controls.Add(manualMemoInput);
        var reset=new Button{Name="manualMemoReset",Text="リセット",AutoSize=true};var save=new Button{Name="manualMemoSave",Text="保存",AutoSize=true};var named=new Button{Name="manualMemoSaveAs",Text="名前を付けて保存",AutoSize=true};foreach(var button in new[]{reset,save,named})actions.Controls.Add(button);manualMemoDelete.Name="manualMemoDelete";manualMemoDelete.Text="選択した保存結果を削除";manualMemoDelete.AutoSize=true;manualMemoDelete.Enabled=false;actions.Controls.Add(manualMemoDelete);manualMemoDelete.Click+=delegate{try{DeleteSelectedManualMemo();}catch(Exception error){ShowError(error);}};manualMemoInput.SelectedIndexChanged+=delegate{manualMemoDelete.Enabled=manualMemoInput.SelectedItem!=null;};
        reset.Click+=delegate{ResetManualDopingMemo();};save.Click+=delegate{try{SaveManualDopingMemo();}catch(Exception error){ShowError(error);}};named.Click+=delegate{try{var label=AskSavedName(ManualMemoAutomaticName());if(label!=null)SaveManualDopingMemoAs(label);}catch(Exception error){ShowError(error);}};
        manualMemoInput.SelectedIndexChanged+=delegate{if(changingManualMemoInput)return;var choice=manualMemoInput.SelectedItem as ManualMemoSavedChoice;if(choice==null)return;if(!ConfirmManualDiscard()){SelectManualMemoInput();return;}try{LoadManualDopingMemo(ManualMemoFilePath(choice.FileName));}catch(Exception error){SelectManualMemoInput();ShowError(error);}};
        var tools=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};tools.RowStyles.Add(new RowStyle(SizeType.Absolute,34));tools.RowStyles.Add(new RowStyle(SizeType.Absolute,36));layout.Controls.Add(tools,0,1);var generations=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};generations.Controls.Add(LabelFor("表示世代"));tools.Controls.Add(generations,0,0);
        for(int g=9;g>=1;g--){var box=new CheckBox{Name="manualMemoShowGeneration"+g,Text=g+"世代",Checked=false,AutoSize=true};manualMemoGenerations[g]=box;generations.Controls.Add(box);box.CheckedChanged+=delegate{if(changingManualMemoGenerations)return;manualMemoDirty=true;RenderManualMemo();};}
        manualMemoPortraits=new LineagePortraitRenderer(Path.Combine(model.DataDirectory,"portraits"));manualMemoDiagram=new LineageDiagram(r=>r.Name,DescribeManualMemoRecord,manualMemoPortraits);manualMemoDiagram.PreviewProvider=ManualRecordPreview;manualMemoDiagram.Name="manualMemoDiagram";manualMemoDiagram.IndependentGenerationRows=false;manualMemoDiagram.FilterEmptyGenerations=true;manualMemoDiagram.CanHideEmpty=ManualCanHideRecord;manualMemoDiagram.HasHiddenRight=ManualHasHiddenRight;manualMemoDiagram.RecordActions=true;manualMemoDiagram.EmptyMessage="表示世代を選択してください。";layout.Controls.Add(manualMemoDiagram,0,2);
        var zoomRow=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};tools.Controls.Add(zoomRow,0,1);zoomRow.Controls.Add(LabelFor("倍率"));var minus=new Button{Text="－",Width=34};var plus=new Button{Text="＋",Width=34};var zoom=new NumericUpDown{Name="manualMemoZoom",Minimum=1,Maximum=400,Value=100,DecimalPlaces=1,Width=74};var fit=new Button{Text="全体表示",AutoSize=true};var actual=new Button{Text="100%",AutoSize=true};foreach(var c in new Control[]{minus,plus,zoom,LabelFor("%"),actual,fit})zoomRow.Controls.Add(c);bool syncing=false;zoom.ValueChanged+=delegate{if(!syncing)manualMemoDiagram.SetZoom((double)zoom.Value);};manualMemoDiagram.ZoomChanged+=delegate{syncing=true;try{zoom.Value=(decimal)Math.Max(1,Math.Min(400,manualMemoDiagram.ZoomPercent));}finally{syncing=false;}};minus.Click+=delegate{manualMemoDiagram.SetZoom(manualMemoDiagram.ZoomPercent/1.25);};plus.Click+=delegate{manualMemoDiagram.SetZoom(manualMemoDiagram.ZoomPercent*1.25);};actual.Click+=delegate{manualMemoDiagram.SetZoom(100);};fit.Click+=delegate{manualMemoDiagram.SetZoom(manualMemoDiagram.FitPercent());manualMemoDiagram.FitVerticalSpacing();};
        BuildManualDopingFields(layout);
        manualMemoDiagram.SelectedRecordChanged+=delegate{if(renderingManualMemo)return;ManualMemoBranch branch;if(manualMemoDiagram.SelectedRecord!=null&&manualMemoBranchRecords.TryGetValue(manualMemoDiagram.SelectedRecord,out branch)&&branch.PersonId==null)AddManualIndividualAt(branch);else RefreshManualDopingFields();};
        manualMemoDiagram.RecordActionRequested+=ManualBranchAction;
        RefreshManualMemoInputs();RenderManualMemo();
    }
    bool ConfirmManualDiscard(){return !manualMemoDirty||(ConfirmManualMemoDiscard!=null?ConfirmManualMemoDiscard("保存していないドーピングメモがあります。続けますか？"):MessageBox.Show(this,"保存していないドーピングメモがあります。続けますか？","ドーピングメモ",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes);}
    public void ShowManualDopingMemo(){identify.Visible=predict.Visible=gekiPage.Visible=birthPage.Visible=lineagePage.Visible=false;manualMemoPage.Visible=true;manualMemoPage.BringToFront();manualMemoNav.BackColor=Color.LightBlue;foreach(var b in new[]{identifyNav,predictNav,gekiNav,birthNav,lineageNav})b.BackColor=SystemColors.Control;}
    void HideManualDopingMemo(){manualMemoPage.Visible=false;manualMemoNav.BackColor=SystemColors.Control;}
    // Reset only the working memo. Persisted files are deleted exclusively by
    // DeleteSelectedManualMemo, after its separate confirmation and identity checks.
    public void ResetManualDopingMemo(){
        manualMemoPeople.Clear();
        manualMemoBranches.Clear();
        manualMemoTreeRoots.Clear();
        manualMemoPath=manualMemoId=manualMemoLabel=null;
        manualMemoLength=manualMemoWriteTicks=0;
        changingManualMemoGenerations=true;
        try{for(int g=1;g<=9;g++)manualMemoGenerations[g].Checked=false;}
        finally{changingManualMemoGenerations=false;}
        manualMemoDirty=false;
        SelectManualMemoInput();
        RenderManualMemo();
    }
    static string ManualPersonName(ManualMemoIndividual person){return String.IsNullOrWhiteSpace(person.Name)?person.Generation+"世代":person.Name;}
    static readonly int[] ManualFaceOffsets={0x65,0x66,0x67,0x68,0x69,0x6a,0x6b,0x73,0x74};
    static int[] ManualPortraitRaw(ManualMemoIndividual person){var raw=Enumerable.Repeat(-1,252).ToArray();raw[0x82]=raw[0x83]=person.Generation-1;raw[0x96]=1;raw[0x94]=person.Antenna??-1;raw[0x64]=person.Head??0;raw[0x71]=person.Color1??-1;raw[0x72]=person.Color2??-1;raw[0x70]=person.Pattern??-1;raw[0x6d]=person.Body??-1;for(int i=0;i<9;i++)raw[ManualFaceOffsets[i]]=person.Face[i]??0;return raw;}
    void RenderManualMemo(string selectedId=null){
        if(manualMemoDiagram==null)return;if(selectedId==null){ManualMemoIndividual selected;if(manualMemoDiagram.SelectedRecord!=null&&manualMemoRecords.TryGetValue(manualMemoDiagram.SelectedRecord,out selected))selectedId=selected.Id;}
        renderingManualMemo=true;try{EnsureMemoTournament();manualMemoRecords.Clear();manualMemoPluses.Clear();manualMemoBranchRecords.Clear();var records=manualMemoTreeRoots.Select(id=>MemoBranchRecord(MemoBranch(id))).ToArray();var visible=new bool[10];for(int g=1;g<=9;g++)visible[g]=manualMemoGenerations[g].Checked;manualMemoDiagram.SetForest(records,visible);var target=manualMemoRecords.FirstOrDefault(p=>p.Value.Id==selectedId).Key;if(target!=null)manualMemoDiagram.SelectRecord(target);}finally{renderingManualMemo=false;}RefreshManualDopingFields();
    }
    string DescribeManualMemoRecord(BirthLineage record){ManualMemoIndividual person;if(!manualMemoRecords.TryGetValue(record,out person))return "電波人間を追加";return ManualPersonName(person)+"／"+person.Generation+"世代\r\n"+String.Join("　",Enumerable.Range(0,9).Select(i=>Model.FaceNames[i]+"："+(person.Face[i].HasValue?person.Face[i].Value.ToString("D2"):"未入力")));}
    public ManualMemoIndividual AddEmptyManualIndividual(int generation){if(generation<1||generation>9||!manualMemoGenerations[generation].Checked)throw new Exception("表示世代を選択してください。");EnsureMemoTournament();var branch=manualMemoPluses.Where(p=>p.Value==generation).Select(p=>manualMemoBranchRecords[p.Key]).FirstOrDefault();if(branch==null){branch=NewMemoBranch(generation);var root=branch;while(root.Generation<manualMemoTreeRoots.Select(id=>MemoBranch(id).Generation).DefaultIfEmpty(generation).Max()){var parent=NewMemoBranch(root.Generation+1);parent.ParentA=root.Id;parent.ParentB=NewMemoBranch(root.Generation).Id;root=parent;}manualMemoTreeRoots.Add(root.Id);}return AddManualIndividualAt(branch);}
    ManualMemoIndividual FindManualPerson(string id){var person=manualMemoPeople.FirstOrDefault(p=>p.Id==id);if(person==null)throw new Exception("編集する個体がありません。");return person;}
    void ValidateManualPerson(ManualMemoIndividual person){
        Guid id;if(person==null||!Guid.TryParseExact(person.Id,"N",out id)||person.Generation<1||person.Generation>9||person.Name!=null&&person.Name.Length>20||person.Face==null||person.Face.Length!=9||person.Doping==null||person.Doping.Length!=5)throw new Exception("メモの個体情報が不正です。");
        Func<int?,IEnumerable<Choice>,bool> valid=(value,choices)=>!value.HasValue||choices.Any(c=>c.Value==value.Value&&c.Value>=0);if(!valid(person.Antenna,model.BirthAntennas())||!valid(person.Head,model.Heads())||!valid(person.Color1,model.Colors().Where(c=>c.Value>=3&&c.Value<=38))||!valid(person.Color2,model.Colors().Where(c=>c.Value==0||c.Value>=3&&c.Value<=38))||!valid(person.Pattern,model.Patterns(false))||!valid(person.Body,model.BirthBodies()))throw new Exception("未対応の能力・外見が入力されています。");
        for(int i=0;i<9;i++)if(person.Face[i].HasValue&&(person.Face[i].Value<0||!MemoFaceChoices(i).Any(c=>c.Value==person.Face[i].Value)&&!model.FaceChoices(i).Contains(person.Face[i].Value)))throw new Exception("未対応の顔パーツです。");int cap=DopingNotes.Limit(person.Generation);if(person.Doping.Any(n=>n.HasValue&&(n.Value<0||n.Value>cap))||person.Doping.Sum(n=>n??0)>cap)throw new Exception(person.Generation+"世代の果物は5能力の合計で"+cap+"個までです。");if(person.PreviewBodyColor.HasValue&&(person.PreviewBodyColor<0||person.PreviewBodyColor>32))throw new Exception("体色を確認してください。");ValidateMemoImage(person.PortraitPng);
    }
    public void UpdateManualAbility(string id,int? antenna,int? head,int? color1,int? color2,int? pattern,int? body,string name,int?[] doping){var original=FindManualPerson(id);var draft=original.Copy();draft.Antenna=antenna;draft.Head=head;draft.Color1=color1;draft.Color2=color2;draft.Pattern=pattern;draft.Body=body;draft.Name=String.IsNullOrWhiteSpace(name)?null:name.Trim();draft.Doping=doping==null?new int?[5]:(int?[])doping.Clone();ValidateManualPerson(draft);manualMemoPeople[manualMemoPeople.IndexOf(original)]=draft;manualMemoDirty=true;RenderManualMemo(id);}
    public void UpdateManualFace(string id,int?[] face){var original=FindManualPerson(id);var draft=original.Copy();draft.Face=face==null?new int?[9]:(int?[])face.Clone();ValidateManualPerson(draft);manualMemoPeople[manualMemoPeople.IndexOf(original)]=draft;manualMemoDirty=true;RenderManualMemo(id);}
}
}
