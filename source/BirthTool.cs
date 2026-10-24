using System;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Text;
using System.Security.Cryptography;
using System.IO;
using System.Web.Script.Serialization;

namespace FreeNormalFinder {
public sealed class ForecastSnapshot {
    public string Label;public ForecastRow[] Rows;public int Number;public string SavedId,SavedFileName;public long SavedLength,SavedWriteTicks;public bool BuiltIn;
    BirthParent[] parents;readonly object parentLock=new object();
    [ScriptIgnore] public Func<BirthParent[]> ParentsLoader;
    [ScriptIgnore] public bool NeedsLoading {get{return parents==null&&ParentsLoader!=null;}}
    public BirthParent[] Parents {get{lock(parentLock){if(parents==null&&ParentsLoader!=null){var loaded=ParentsLoader();parents=loaded;ParentsLoader=null;}return parents;}}set{lock(parentLock){parents=value;ParentsLoader=null;}}}
    public int Count {get{return Parents!=null?Parents.Length:Rows==null?0:Rows.Length;}}
    public override string ToString(){return Label;}
}
public sealed class BirthSnapshot {public string Label;public BirthResult[] Rows;public ForecastSnapshot Material;public string Mode;public override string ToString(){return Label;}}
public sealed class SavedBirthInput {public int Version=1;public string Id,Label;public BirthParent[] Parents;}
public sealed class LineageChoice {public BirthResult Row;public string Label;public override string ToString(){return Label;}}
public sealed class BirthMaterialChoice {
    public ForecastRow Row;public string Batch;public ForecastSnapshot Snapshot;
    public override string ToString(){return Row==null?"保存した素材から選択":Batch+"／"+Row.Date+"・"+Row.Slot+"／"+Row.Name+"／"+Row.Antenna+"／"+Row.BodyText+"／"+Row.Color1+"・"+Row.Color2+"／"+Row.Pattern+"／"+Row.Head;}
}
// FREE raw trait identifiers; the reference notebooks' New Denpa tables are not used.
public sealed class BirthParentEditor:UserControl {
    readonly Model model;
    readonly TextBox parentName=new TextBox();
    readonly Dictionary<int,ComboBox> fields=new Dictionary<int,ComboBox>();
    readonly GenerationNumber generation=new GenerationNumber(),generationLimit=new GenerationNumber();readonly FlowLayoutPanel generationRange=new FlowLayoutPanel();int level=1;
    readonly CheckBox caught=new CheckBox();
    readonly Panel reverseSpace=new Panel();
    bool updating;string sourceId="",source="手入力";BirthLineage inputLineage;int[] inputRaw;readonly DopingMemoEditor dopingMemo;int acceptedFirst,acceptedLast;
    public event EventHandler Changed;
    static readonly int[] FaceOffsets={0x65,0x66,0x67,0x68,0x69,0x6a,0x6b,0x73,0x74};
    public BirthParentEditor(Model model,int number){
        this.model=model;
        Dock=DockStyle.Fill;Name="birthParent"+number;Margin=Padding.Empty;
        var grid=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty};
        foreach(int h in new[]{58,62,60})grid.RowStyles.Add(new RowStyle(SizeType.Absolute,h));Controls.Add(grid);
        dopingMemo=new DopingMemoEditor(Name+"Doping");grid.Controls.Add(dopingMemo,0,2);dopingMemo.Enabled=false;dopingMemo.Changed+=delegate{if(!updating&&Changed!=null)Changed(this,EventArgs.Empty);};
        var first=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};grid.Controls.Add(first,0,0);
        AddField(first,"アンテナ",Select(0x94,model.BirthAntennas(),true),150);
        AddField(first,"頭",Select(0x64,model.Heads().Where(c=>c.Value>=0&&c.Value<40).ToArray(),true),110);
        AddField(first,"色1",Select(0x71,model.Colors().Where(c=>c.Value>=3&&c.Value<=38).ToArray(),true),90);
        AddField(first,"色2",Select(0x72,model.Colors().Where(c=>c.Value==0||c.Value>=3&&c.Value<=38).ToArray(),true),90);
        reverseSpace.Height=23;first.Controls.Add(reverseSpace);
        AddField(first,"柄",Select(0x70,model.Patterns(false),true),90);
        AddField(first,"体格",Select(0x6d,model.BirthBodies(),true),88);
        parentName.Name=Name+"Name";parentName.MaxLength=20;AddField(first,"名前",parentName,85);ToolForm.StackFieldLabels(first);
        var faces=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=1,ColumnCount=9,Margin=Padding.Empty,Padding=new Padding(0,0,4,0)};faces.RowStyles.Add(new RowStyle(SizeType.Percent,100));grid.Controls.Add(faces,0,1);int[] order={1,0,2,3,4,5,6,7,8};
        for(int pos=0;pos<order.Length;pos++){
            int p=order[pos];faces.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/9));
            var group=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,Margin=new Padding(2,0,4,0),Padding=Padding.Empty};group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));group.RowStyles.Add(new RowStyle(SizeType.Absolute,22));group.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            group.Controls.Add(new Label{Text=Model.FaceNames[p],Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Margin=Padding.Empty},0,0);
            var box=Select(FaceOffsets[p],model.FaceChoices(p).Where(n=>n>=0).Select(n=>new Choice{Value=n,Text=n.ToString("D2")}).ToArray(),true);box.Dock=DockStyle.Top;box.Margin=new Padding(0,1,2,4);group.Controls.Add(box,0,1);faces.Controls.Add(group,pos,0);
        }
        ConfigureNumber(generation,Name+"Generation",9);
        ConfigureNumber(generationLimit,Name+"GenerationLimit",9);generationRange.Name=Name+"GenerationRange";generationRange.Size=new Size(158,26);generationRange.WrapContents=false;generationRange.Margin=new Padding(10,3,3,3);generationRange.Controls.Add(generation);generationRange.Controls.Add(new Label{Text="～",AutoSize=false,Width=20,Height=23});generationRange.Controls.Add(generationLimit);generationRange.Controls.Add(new Label{Text="世代",AutoSize=false,Width=38,Height=23});foreach(Control control in generationRange.Controls)control.Margin=Padding.Empty;
        caught.Name=Name+"Caught";caught.Text="捕獲個体";caught.AutoSize=true;caught.Checked=true;
        var ancestors=new[]{new Choice{Value=0,Text="該当なし"}}.Concat(model.BirthAntennas().Where(c=>c.Value>=11&&c.Value<=21).Select(c=>new Choice{Value=c.Value,Text=model.BirthAntennaFamily(c.Value)})).ToArray();
        Select(0x86,ancestors,false);Select(0x87,ancestors,false);
        fields[0x86].SelectedValue=fields[0x87].SelectedValue=0;
        fields[0x86].Enabled=fields[0x87].Enabled=false;
        parentName.TextChanged+=OnEdited;generation.ValueChanged+=GenerationEdited;
        generationLimit.ValueChanged+=GenerationEdited;
        caught.CheckedChanged+=delegate{fields[0x86].Enabled=fields[0x87].Enabled=!caught.Checked;OnEdited(this,EventArgs.Empty);};
        SyncHair(false);
    }
    static Choice[] Numbers(int count){return Enumerable.Range(0,count).Select(i=>new Choice{Value=i,Text=i.ToString("D2")}).ToArray();}
    static void ConfigureNumber(GenerationNumber box,string name,int max){box.Name=name;box.Width=45;box.Minimum=0;box.Maximum=max;box.SelectedValue=-1;}
    public GenerationNumber GenerationInput {get{return generation;}}
    public GenerationNumber GenerationLimitInput {get{return generationLimit;}}
    public Control GenerationRangeInput {get{return generationRange;}}
    public DopingMemoEditor DopingInput {get{return dopingMemo;}}
    void GenerationEdited(object sender,EventArgs e){if(updating)return;int lower=(int)generation.Value,upper=(int)generationLimit.Value;if(lower>=1&&upper>=lower){if(!dopingMemo.SetRange(lower,upper,true)){updating=true;try{generation.Value=acceptedFirst;generationLimit.Value=acceptedLast;}finally{updating=false;}return;}acceptedFirst=lower;acceptedLast=upper;}else dopingMemo.Enabled=false;if(sender==generation)level=1;OnEdited(sender,e);}
    public ComboBox ParentAntennaInput(int index){return fields[index==0?0x86:0x87];}
    public void AddMetadataFields(FlowLayoutPanel row){row.Controls.Add(generationRange);foreach(int offset in new[]{0x86,0x87}){var input=fields[offset];input.Dock=DockStyle.None;input.Width=150;row.Controls.Add(input);}row.Controls.Add(caught);}
    ComboBox Select(int offset,Choice[] choices,bool empty){
        var c=new ComboBox{Name=Name+"Field"+offset.ToString("X2"),Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,DisplayMember="Text",ValueMember="Value",BindingContext=new BindingContext()};
        c.DataSource=empty?new[]{new Choice{Value=-1,Text="未入力"}}.Concat(choices).ToArray():choices;c.SelectedValue=-1;
        fields.Add(offset,c);c.SelectedIndexChanged+=OnEdited;return c;
    }
    static void AddField(FlowLayoutPanel row,string label,Control input,int width){row.Controls.Add(new Label{Text=label});input.Dock=DockStyle.None;input.Width=width;row.Controls.Add(input);}
    public void SetReverseSpaceWidth(int width){reverseSpace.Width=width;}
    void OnEdited(object sender,EventArgs e){if(updating)return;if(fields.ContainsKey(0x65)&&fields.ContainsKey(0x66)&&Object.ReferenceEquals(sender,fields[0x66]))SyncHair(false);sourceId="";source="手入力";inputLineage=null;if(Changed!=null)Changed(this,EventArgs.Empty);}
    void SyncHair(bool preserve){
        bool previous=updating;updating=true;try{
            int hair=Value(0x66),contour=Value(0x65);fields[0x65].Enabled=hair==0;
            if(hair<0)fields[0x65].SelectedValue=-1;
            else if(hair==0&&contour<0)fields[0x65].SelectedValue=0;
            else if(hair>0&&(!preserve||!model.IsKnownHairContour(hair,contour)))fields[0x65].SelectedValue=Model.ContourForHair(hair);
        }finally{updating=previous;}
    }
    public bool AncestryKnown {get{return caught.Checked||Value(0x86)>=0&&Value(0x87)>=0;}}
    int Value(int offset){return Convert.ToInt32(fields[offset].SelectedValue);}
    public BirthParent Read(){
        if(Convert.ToInt32(generationLimit.SelectedValue)<1||generationLimit.Value<generation.Value)throw new Exception("世代の下限・上限を1～9で設定し、下限以下にならない上限を選択してください。");
        if(String.IsNullOrWhiteSpace(parentName.Text)||Convert.ToInt32(generation.SelectedValue)<1||fields.Any(p=>p.Value.SelectedIndex<0||Value(p.Key)<0&&(!(p.Key==0x86||p.Key==0x87)||!caught.Checked)))throw new Exception("未入力の項目があります。名前・世代・外見・顔パーツをすべて入力してください。出生個体は親のアンテナ1・2も選択してください。");
        int[] f=inputRaw==null?new int[252]:(int[])inputRaw.Clone();foreach(var pair in fields){int value=Value(pair.Key);f[pair.Key]=(pair.Key==0x86||pair.Key==0x87)&&caught.Checked?0:value;}
        f[0x82]=Convert.ToInt32(generation.SelectedValue)-1;f[0x83]=Convert.ToInt32(generationLimit.SelectedValue)-1;f[0x96]=level;
        var memo=dopingMemo.Notes;DopingNotes.Validate(memo,f[0x82]+1,f[0x83]+1);if(String.IsNullOrEmpty(sourceId))sourceId="manual:"+Guid.NewGuid().ToString("N");return new BirthParent{Name=parentName.Text.Trim(),Source=source,SourceId=sourceId,Raw=f,DopingMemo=memo,AncestryKnown=AncestryKnown,Lineage=inputLineage};
    }
    public void Fill(BirthParent parent){
        if(parent==null||parent.Raw==null||parent.Raw.Length!=252)throw new Exception("入力する親の形式が異なります。");
        updating=true;try{
            parentName.Text=parent.Name;generation.SelectedValue=parent.Generation;generationLimit.SelectedValue=parent.GenerationLimit;level=parent.Level;inputRaw=(int[])parent.Raw.Clone();acceptedFirst=parent.Generation;acceptedLast=parent.GenerationLimit;dopingMemo.LoadNotes(parent.DopingMemo,acceptedFirst,acceptedLast);dopingMemo.Enabled=true;
            foreach(var pair in fields){int value=parent.Raw[pair.Key];if((pair.Key==0x86||pair.Key==0x87)&&(value<11||value>21))value=0;pair.Value.SelectedValue=value;if(pair.Value.SelectedIndex<0)throw new Exception("手入力の対応範囲外の値があります。");}
            caught.Checked=parent.Raw[0x86]==0&&parent.Raw[0x87]==0;fields[0x86].Enabled=fields[0x87].Enabled=!caught.Checked;
            sourceId=parent.SourceId;source=parent.Source;inputLineage=parent.Lineage;
            SyncHair(true);
        }finally{updating=false;}if(Changed!=null)Changed(this,EventArgs.Empty);
    }
}


public sealed partial class ToolForm {
    readonly Panel birthPage=new Panel();readonly Button birthNav=new Button();
    readonly Panel lineagePage=new Panel();readonly Button lineageNav=new Button();
    readonly ComboBox lineageSource=new ComboBox(),lineageIndividual=new ComboBox();
    readonly TreeView lineageTree=new TreeView();readonly TextBox lineageDetails=new TextBox();readonly Label lineageStatus=new Label();bool updatingLineage;
    LineageDiagram lineageDiagram;BirthLineage displayedLineageRecord;readonly CheckBox[] lineageGenerations=new CheckBox[10];readonly NumericUpDown lineageZoom=new NumericUpDown();bool changingLineageGenerations,changingLineageZoom;
    readonly Label birthStatus=new Label();
    readonly DataGridView birthResultGrid=new DataGridView();
    readonly ComboBox savedForecasts=new ComboBox();
    readonly ComboBox[] birthMaterials={new ComboBox(),new ComboBox()};
    readonly ComboBox[] birthBatches={new ComboBox(),new ComboBox()};
    readonly Button[] deleteBirthInputs={new Button(),new Button()};
    readonly TextBox registrationSaveName=new TextBox();
    readonly List<ForecastSnapshot> persistentBirthInputs=new List<ForecastSnapshot>();
    public Func<string,string> RequestSaveName;
    public Func<string,bool> ConfirmSavedInputDeletion;
    readonly RadioButton birthModeBirth=new RadioButton(),birthModeFilter=new RadioButton(),birthModeRegister=new RadioButton(),birthModeEdit=new RadioButton();
    readonly Panel birthConditionHost=new Panel();
    readonly TextBox birthNameFilter=new TextBox();readonly Button birthResetButton=new Button();
    public string BirthMode {get{return birthModeEdit.Checked?"名前/ドーピング/世代変更":birthModeRegister.Checked?"個体登録":birthModeFilter.Checked?"絞り込み":"出生";}}
    BirthResult[] birthSearchResults=new BirthResult[0];bool savingBirthInput;
    readonly Dictionary<int,ComboBox> birthFilters=new Dictionary<int,ComboBox>();
    readonly ComboBox birthAntennaFilter=new ComboBox(),birthSavedResults=new ComboBox();
    readonly CheckBox birthReverseColors=new CheckBox();
    readonly List<BirthSnapshot> birthSnapshots=new List<BirthSnapshot>();int nextBirthSnapshotNumber;
    bool updatingBirthFilters;
    int birthFilterHeight=120;
    readonly List<ForecastSnapshot> forecastSnapshots=new List<ForecastSnapshot>();int nextForecastHistoryNumber,nextBirthHistoryNumber;
    int nextForecastSnapshotNumber;
    BirthParentEditor parentEditorA,parentEditorB;
    public void ShowBirth(){HideManualDopingMemo();birthPage.Visible=true;identify.Visible=predict.Visible=gekiPage.Visible=lineagePage.Visible=false;birthPage.BringToFront();birthNav.BackColor=Color.LightBlue;identifyNav.BackColor=predictNav.BackColor=gekiNav.BackColor=lineageNav.BackColor=SystemColors.Control;}
    void BuildBirth(){
        birthPage.Name="birthPage";
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6};
        foreach(int n in new[]{32,42,120,44,0,0})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,n));layout.RowStyles[4].SizeType=SizeType.Percent;layout.RowStyles[4].Height=100;birthPage.Controls.Add(layout);birthStatus.Visible=false;
        var modesHost=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};var modes=new FlowLayoutPanel{Dock=DockStyle.Left,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false};modes.Controls.Add(LabelFor("モード"));int modeIndex=0;foreach(var mode in new[]{birthModeBirth,birthModeFilter,birthModeRegister,birthModeEdit}){mode.Name=new[]{"birthModeBirth","birthModeFilter","birthModeRegister","birthModeEdit"}[modeIndex];mode.Text=new[]{"出生","絞り込み","個体登録","名前/ドーピング/世代変更"}[modeIndex++];mode.AutoSize=true;modes.Controls.Add(mode);}birthModeBirth.Checked=true;modesHost.Controls.Add(modes);layout.Controls.Add(modesHost,0,0);
        var tools=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};
        var inputLabels=new[]{LabelFor("親1"),LabelFor("親2")};
        for(int i=0;i<2;i++){
            tools.Controls.Add(inputLabels[i]);
            var batch=birthBatches[i];batch.Name="birthBatch"+(i+1);batch.Width=220;batch.DropDownWidth=420;batch.DropDownStyle=ComboBoxStyle.DropDownList;tools.Controls.Add(batch);batch.SelectedIndexChanged+=delegate{ClearBirthResults();UpdateDeleteBirthInputs();};
            int target=i;var deleteInput=deleteBirthInputs[i];deleteInput.Name="birthDeleteInput"+(i+1);deleteInput.Text="選択した保存結果を削除";deleteInput.AutoSize=true;deleteInput.Enabled=false;tools.Controls.Add(deleteInput);deleteInput.Click+=delegate{try{var selected=SelectedBirthBatch(target);bool confirmed=ConfirmSavedInputDeletion!=null?ConfirmSavedInputDeletion(selected.Label):MessageBox.Show(this,"「"+selected.Label+"」を完全に削除しますか？この操作は元に戻せません。", "保存結果の削除",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes;if(confirmed)DeleteSavedBirthInput(target);}catch(Exception error){ShowError(error);}};
            birthMaterials[i].Items.Add(new BirthMaterialChoice());birthMaterials[i].SelectedIndex=0;
        }
        var purgeDeleted=new Button{Name="birthPurgeDeletedInputs",Text="以前の削除済みを完全削除",AutoSize=true,Visible=false};tools.Controls.Add(purgeDeleted);purgeDeleted.Click+=delegate{try{if(MessageBox.Show(this,"以前「削除済み」に退避した保存ファイルを完全削除します。元に戻せません。よろしいですか？","完全削除",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes){int count=PurgeLegacyDeletedBirthInputs();purgeDeleted.Visible=Directory.Exists(Path.Combine(savedInputsDirectory,"削除済み"));birthStatus.Text=count+"件の以前の削除済みファイルを完全削除しました。";}}catch(Exception error){ShowError(error);}};
        var inputHost=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};inputHost.Controls.Add(tools);layout.Controls.Add(inputHost,0,1);AlignInlineRow(tools);
        BuildBirthEditInputs(tools);TrackCurrentResultRows();
        parentEditorA=new BirthParentEditor(model,1);parentEditorB=new BirthParentEditor(model,2);parentEditorB.Visible=false;var manualPanel=new Panel{Dock=DockStyle.Fill,Visible=false,AutoScroll=true,Margin=Padding.Empty};manualPanel.Controls.Add(parentEditorA);manualPanel.Controls.Add(parentEditorB);birthConditionHost.Dock=DockStyle.Fill;birthConditionHost.Controls.Add(manualPanel);layout.Controls.Add(birthConditionHost,0,2);
        var registrationInputs=new FlowLayoutPanel{Name="birthRegistrationInputs",Dock=DockStyle.Fill,Visible=false,WrapContents=false};registrationInputs.Controls.Add(LabelFor("保存名"));registrationSaveName.Name="birthRegistrationSaveName";registrationSaveName.AutoSize=false;registrationSaveName.Size=birthBatches[0].Size;registrationSaveName.MaxLength=80;registrationInputs.Controls.Add(registrationSaveName);parentEditorA.AddMetadataFields(registrationInputs);AlignInlineRow(registrationInputs);inputHost.Controls.Add(registrationInputs);
        var metadataInputs=new Control[]{parentEditorA.GenerationRangeInput,parentEditorA.ParentAntennaInput(0),parentEditorA.ParentAntennaInput(1)};var metadataCaptions=new Label[3];for(int i=0;i<3;i++){var caption=new Label{Name=new[]{"birthRegistrationGenerationCaption","birthRegistrationParentAntenna1Caption","birthRegistrationParentAntenna2Caption"}[i],Text=new[]{"世代","親のアンテナ1","親のアンテナ2"}[i],Width=metadataInputs[i].Width,Height=23,Visible=false,TextAlign=ContentAlignment.MiddleLeft};metadataCaptions[i]=caption;modesHost.Controls.Add(caption);caption.BringToFront();}bool placingMetadata=false;Action placeGenerationCaption=delegate{if(placingMetadata)return;placingMetadata=true;try{registrationSaveName.Size=birthBatches[0].Size;registrationInputs.Padding=new Padding(0,Math.Max(0,birthBatches[0].Top-registrationSaveName.Margin.Top),0,0);int wanted=Math.Max(10,modes.Right+12-registrationInputs.Left-registrationSaveName.Right-3);if(metadataInputs[0].Margin.Left!=wanted)metadataInputs[0].Margin=new Padding(wanted,metadataInputs[0].Margin.Top,3,3);for(int i=0;i<3;i++){var caption=metadataCaptions[i];caption.Visible=birthModeRegister.Checked;if(caption.Visible){caption.Left=modesHost.PointToClient(metadataInputs[i].Parent.PointToScreen(metadataInputs[i].Location)).X;caption.Top=Math.Max(0,modesHost.Height-caption.Height);}}}finally{placingMetadata=false;}};modesHost.Layout+=delegate{placeGenerationCaption();};registrationInputs.Layout+=delegate{placeGenerationCaption();};
        birthConditionHost.Margin=Padding.Empty;
        Action sizeManualPanel=delegate{layout.RowStyles[2].Height=birthModeRegister.Checked?Math.Max(180,birthFilterHeight):birthFilterHeight;};layout.SizeChanged+=delegate{sizeManualPanel();};
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};
        var run=new Button{Name="birthPredictButton",Text="検索実行",AutoSize=true};birthRunButton=run;actions.Controls.Add(run);actions.Controls.Add(birthPauseResume);birthPauseResume.Click+=async delegate{await ToggleBirthSearchAsync();};birthResetButton.Name="birthFilterReset";birthResetButton.Text="リセット";birthResetButton.AutoSize=true;actions.Controls.Add(birthResetButton);layout.Controls.Add(actions,0,3);
        ConfigureBirthGrid(birthResultGrid,"birthResult");
        InstallEmptyResultMessage(birthResultGrid);InstallEmptyResultMessage(forecastGrid);
        AddBirthColumn(birthResultGrid,"Name","名前",75);
        foreach(var c in new[]{new[]{"Antenna","アンテナ"},new[]{"Body","体格"},new[]{"Head","頭"},new[]{"Color1","色1"},new[]{"Color2","色2"},new[]{"Pattern","柄"},new[]{"Generation","世代"},new[]{"Hair","髪"},new[]{"FaceShape","輪郭"},new[]{"Brow","眉"},new[]{"Eye","目"},new[]{"Nose","鼻"},new[]{"Mouth","口"},new[]{"Cheek","頬"},new[]{"SkinColor","肌色"},new[]{"HairColor","髪色"}}){AddBirthColumn(birthResultGrid,c[0],c[1],c[0]=="Antenna"?105:c[0]=="Head"||c[0]=="Pattern"?70:c[0].StartsWith("Color")?78:40);if(c[0]=="Body"||Array.IndexOf(Model.FaceKeys,c[0])>=0)birthResultGrid.Columns[c[0]].DefaultCellStyle.Format="D2";}
        birthResultGrid.Columns["Body"].DataPropertyName="BodyText";birthResultGrid.Columns["Body"].DefaultCellStyle.Format="";birthResultGrid.Columns["Body"].MinimumWidth=62;birthResultGrid.Columns["Body"].FillWeight=72;
        birthResultGrid.Columns["Generation"].DataPropertyName="GenerationText";
        var progress=new ProgressBar{Name="birthProgress",Dock=DockStyle.Fill,Maximum=1000};
        var progressText=new Label{Name="birthProgressText",Dock=DockStyle.Fill,Text="0%",AutoEllipsis=true,TextAlign=ContentAlignment.MiddleRight};
        var progressRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,420));progressRow.Controls.Add(progress,0,0);progressRow.Controls.Add(progressText,1,0);
        var resultHost=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};resultHost.RowStyles.Add(new RowStyle(SizeType.Absolute,24));resultHost.RowStyles.Add(new RowStyle(SizeType.Percent,100));resultHost.Controls.Add(progressRow,0,0);resultHost.Controls.Add(birthResultGrid,0,1);BuildBirthResultTools(actions);birthSearchProgress=progress;birthSearchProgressText=progressText;birthSearchInputs=new Control[]{modesHost,inputHost,birthConditionHost,birthResultGrid}.Concat(actions.Controls.Cast<Control>().Where(c=>c!=birthPauseResume)).ToArray();InstallSearchSessionLifetime();layout.Controls.Add(resultHost,0,4);birthStatus.Name="birthStatus";birthStatus.Dock=DockStyle.Fill;birthStatus.Text="同世代の親2人を入力してください。単色は色2・柄を「なし」にします。\r\n出生個体で祖先アンテナ値が不明でも外見は予測できます（アンテナのみ未予測）。名前・能力値・出生可否の全条件は対象外です。";layout.Controls.Add(birthStatus,0,5);
        birthResultGrid.RowTemplate.Height=26;birthResultGrid.DefaultCellStyle.WrapMode=DataGridViewTriState.False;
        InstallIndividualRegistration(birthResultGrid);InstallIndividualRegistration(forecastGrid);
        SetFixedResultColumns(birthResultGrid);SetFixedResultColumns(forecastGrid);birthResultGrid.FontChanged+=delegate{SetFixedResultColumns(birthResultGrid);};
        birthResultGrid.CellToolTipTextNeeded+=delegate(object sender,DataGridViewCellToolTipTextNeededEventArgs e){if(e.RowIndex<0)return;var result=birthResultGrid.Rows[e.RowIndex].DataBoundItem as BirthResult;if(result!=null)e.ToolTipText=BirthResultOrigin(result,e.RowIndex);};
        birthResultGrid.CellFormatting+=delegate(object sender,DataGridViewCellFormattingEventArgs e){if(e.RowIndex<0)return;var result=birthResultGrid.Rows[e.RowIndex].DataBoundItem as BirthResult;string column=birthResultGrid.Columns[e.ColumnIndex].Name;if(result!=null&&result.Raw==null&&column!="RegisterIndividual"&&column!="Parent1"&&column!="Parent2"&&column!="Parent1Source"&&column!="Parent2Source"&&column!="Condition"){e.Value="";e.FormattingApplied=true;}};
        birthStatus.Text="モード・入力・条件を選んで「検索実行」で結果を表示し、「保存」または「名前を付けて保存」で入力候補に残せます。\r\n保存した個体は次回起動時も残ります。出力履歴は今回の起動中のみ保持します。";
        run.Click+=async delegate{try{if(birthModeEdit.Checked)ApplyBirthIndividualEdits();else if(birthModeRegister.Checked)RegisterBirthIndividual();else await RunBirthSearchAsync();}catch(Exception error){ShowError(error);}};
        EventHandler invalidate=delegate{ClearBirthResults();birthStatus.Text="個体の入力が変わりました。「検索実行」で入力内容を結果欄に表示してください。";};parentEditorA.Changed+=invalidate;parentEditorB.Changed+=invalidate;
        var saveResults=new Button{Name="forecastSaveResults",Text="保存",AutoSize=true};forecastButton.Parent.Controls.Add(saveResults);var saveNamed=new Button{Name="forecastSaveNamedResults",Text="名前を付けて保存",AutoSize=true};forecastButton.Parent.Controls.Add(saveNamed);
        forecastButton.Parent.Controls.Add(LabelFor("出力履歴"));savedForecasts.Name="forecastSavedResults";savedForecasts.Width=175;savedForecasts.DropDownWidth=420;savedForecasts.DropDownStyle=ComboBoxStyle.DropDownList;forecastButton.Parent.Controls.Add(savedForecasts);
        var restore=new Button{Name="forecastRestoreResults",Text="出力履歴を表示",AutoSize=true};forecastButton.Parent.Controls.Add(restore);
        var deleteResults=new Button{Name="forecastDeleteResults",Text="選択した履歴を削除",AutoSize=true};forecastButton.Parent.Controls.Add(deleteResults);
        saveResults.Click+=async delegate{try{await SaveForecastResultsAsync();}catch(Exception e){ShowError(e);}};
        saveNamed.Click+=async delegate{try{var name=AskSavedName("レアキャッチ");if(name!=null)await SaveForecastResultsAsync(name);}catch(Exception e){ShowError(e);}};
        restore.Click+=delegate{try{RestoreForecastResults(savedForecasts.SelectedIndex);}catch(Exception e){ShowError(e);}};
        deleteResults.Click+=delegate{try{DeleteForecastResults(savedForecasts.SelectedIndex);}catch(Exception e){ShowError(e);}};
        EventHandler modeChanged=async delegate(object sender,EventArgs e){if(!((RadioButton)sender).Checked)return;bool registration=birthModeRegister.Checked,editing=birthModeEdit.Checked;purgeDeleted.Visible=birthModeFilter.Checked&&Directory.Exists(Path.Combine(savedInputsDirectory,"削除済み"));manualPanel.Visible=registration;tools.Visible=!registration;registrationInputs.Visible=registration;foreach(Control c in birthConditionHost.Controls)if(c.Name=="birthResultFilters")c.Visible=!registration&&!editing;birthEditPanel.Visible=editing;birthEditSource.Visible=editing;birthEditIndividual.Visible=birthEditIndividualCaption.Visible=editing;inputLabels[0].Text=birthModeFilter.Checked||editing?"入力":"親1";inputLabels[0].Visible=!registration;birthBatches[0].Visible=!registration&&!editing;deleteBirthInputs[0].Visible=!registration&&!editing;inputLabels[1].Visible=birthBatches[1].Visible=deleteBirthInputs[1].Visible=birthModeBirth.Checked;sizeManualPanel();placeGenerationCaption();ResetBirthResultFilters();ClearBirthResults();birthPauseResume.Enabled=!registration&&!editing&&birthSession!=null&&!birthSession.Complete;run.Text=editing?"変更を反映":"検索実行";birthStatus.Text=editing?"個体名が未選択なら全員の編集した項目を変更します。元の保存は変更しません。":BirthMode+"：条件を指定し「検索実行」で下に結果を表示します。「保存」で次回起動後も使えます。";if(editing)try{RefreshBirthEditSources();await LoadBirthEditIndividualsAsync();}catch(Exception error){ShowError(error);}};foreach(var mode in new[]{birthModeBirth,birthModeFilter,birthModeRegister,birthModeEdit})mode.CheckedChanged+=modeChanged;
        LoadBuiltInEventBirthInputs();LoadPersistentBirthInputs();
    }
    void LoadBuiltInEventBirthInputs(){
        try{var catalog=model.LoadEventBirthCatalog();AddBirthBatchChoice(new ForecastSnapshot{Label=catalog.Label,Parents=catalog.Parents,BuiltIn=true});}
        catch(Exception error){birthStatus.Text="eventの初期データを読み込めません："+error.GetBaseException().Message+"\r\ndata/event.jsonを含むフォルダ全体を展開してください。ほかの入力は使用できます。";}
    }
    readonly Panel birthEditPanel=new Panel();readonly ComboBox birthEditIndividual=new ComboBox(),birthEditSource=new ComboBox();readonly Label birthEditIndividualCaption=new Label();
    readonly TextBox birthEditName=new TextBox();readonly GenerationNumber birthEditGeneration=new GenerationNumber(),birthEditLimit=new GenerationNumber();
    readonly DopingMemoEditor birthEditMemo=new DopingMemoEditor("birthEditDoping");
    BirthParent[] birthEditParents=new BirthParent[0];int birthEditLoadVersion,editAcceptedFirst,editAcceptedLast;bool fillingBirthEdit,editNameChanged,editGenerationChanged;
    public Func<string,bool> ConfirmDopingDiscard;
    readonly ForecastSnapshot currentEditChoice=new ForecastSnapshot{Label="現在表示中の結果",BuiltIn=true};
    void BuildBirthEditInputs(FlowLayoutPanel inputRow){
        birthEditSource.Name="birthEditSource";birthEditSource.Width=220;birthEditSource.DropDownWidth=420;birthEditSource.DropDownStyle=ComboBoxStyle.DropDownList;birthEditSource.Visible=false;inputRow.Controls.Add(birthEditSource);inputRow.Controls.SetChildIndex(birthEditSource,2);
        birthEditIndividualCaption.Text="個体名";birthEditIndividualCaption.Visible=false;inputRow.Controls.Add(birthEditIndividualCaption);birthEditIndividual.Name="birthEditIndividual";birthEditIndividual.Width=300;birthEditIndividual.DropDownWidth=650;birthEditIndividual.DropDownStyle=ComboBoxStyle.DropDownList;birthEditIndividual.Visible=false;inputRow.Controls.Add(birthEditIndividual);
        birthEditPanel.Name="birthEditPanel";birthEditPanel.Dock=DockStyle.Fill;birthEditPanel.Visible=false;birthConditionHost.Controls.Add(birthEditPanel);
        var rows=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};rows.RowStyles.Add(new RowStyle(SizeType.Absolute,58));rows.RowStyles.Add(new RowStyle(SizeType.Absolute,62));birthEditPanel.Controls.Add(rows);
        var identity=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};rows.Controls.Add(identity,0,0);identity.Controls.Add(LabelFor("名前"));birthEditName.Name="birthEditName";birthEditName.Width=220;birthEditName.MaxLength=20;identity.Controls.Add(birthEditName);
        var range=new FlowLayoutPanel{Size=new Size(158,26),WrapContents=false,Margin=Padding.Empty};foreach(var box in new[]{birthEditGeneration,birthEditLimit}){box.Minimum=0;box.Maximum=9;box.Value=0;box.Width=45;}birthEditGeneration.Name="birthEditGeneration";birthEditLimit.Name="birthEditGenerationLimit";range.Controls.Add(birthEditGeneration);range.Controls.Add(new Label{Text="～",Width=20,Height=23});range.Controls.Add(birthEditLimit);range.Controls.Add(new Label{Text="世代",Width=38,Height=23});foreach(Control c in range.Controls)c.Margin=Padding.Empty;identity.Controls.Add(LabelFor("世代"));identity.Controls.Add(range);StackFieldLabels(identity);
        rows.Controls.Add(birthEditMemo,0,1);birthEditMemo.ConfirmDiscard=ConfirmDopingRemoval;
        EventHandler edited=delegate(object sender,EventArgs e){if(!fillingBirthEdit&&birthModeEdit.Checked){if(sender==birthEditName)editNameChanged=true;if(sender==birthEditGeneration||sender==birthEditLimit){int lower=(int)birthEditGeneration.Value,upper=(int)birthEditLimit.Value;int memoLower=lower==0&&upper==0?birthEditParents.Select(p=>p.Generation).DefaultIfEmpty(1).Max():lower,memoUpper=lower==0&&upper==0?birthEditParents.Select(p=>p.GenerationLimit).DefaultIfEmpty(1).Min():upper;if(!birthEditMemo.SetRange(memoLower,memoUpper,true)){fillingBirthEdit=true;try{birthEditGeneration.Value=editAcceptedFirst;birthEditLimit.Value=editAcceptedLast;}finally{fillingBirthEdit=false;}return;}if(lower>=1&&upper>=lower||lower==0&&upper==0){editAcceptedFirst=lower;editAcceptedLast=upper;}editGenerationChanged=lower!=0||upper!=0;}ClearBirthResults();birthStatus.Text="「変更を反映」で変更後の個体を表示します。";}};birthEditName.TextChanged+=edited;birthEditGeneration.ValueChanged+=edited;birthEditLimit.ValueChanged+=edited;birthEditMemo.Changed+=edited;
        birthEditIndividual.SelectedIndexChanged+=delegate{if(fillingBirthEdit)return;FillBirthEditIndividual();ClearBirthResults();};birthEditSource.SelectedIndexChanged+=async delegate{if(!birthModeEdit.Checked||fillingBirthEdit)return;try{await LoadBirthEditIndividualsAsync();}catch(Exception error){ShowError(error);}};
        birthEditName.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Delete||e.KeyCode==Keys.Back)edited(sender,e);};
    }
    void RefreshBirthEditSources(){object selected=birthEditSource.SelectedItem;fillingBirthEdit=true;try{birthEditSource.Items.Clear();birthEditSource.Items.Add(currentEditChoice);foreach(var item in birthBatches[0].Items)birthEditSource.Items.Add(item);birthEditSource.SelectedItem=selected;}finally{fillingBirthEdit=false;}}
    public async Task LoadBirthEditIndividualsAsync(){
        int version=++birthEditLoadVersion;birthEditParents=new BirthParent[0];birthEditIndividual.Items.Clear();FillBirthEditIndividual();var batch=birthEditSource.SelectedItem as ForecastSnapshot;if(batch==null)return;if(!Object.ReferenceEquals(batch,currentEditChoice))await LoadBirthInputsAsync(batch);if(version!=birthEditLoadVersion||!birthModeEdit.Checked||!Object.ReferenceEquals(batch,birthEditSource.SelectedItem))return;
        if(Object.ReferenceEquals(batch,currentEditChoice)){var rows=CurrentResultRows().Where(r=>r.Raw!=null&&r.Raw[0x94]>=0).ToArray();birthEditParents=rows.Length==0?new BirthParent[0]:SavedParentsForBirth(new BirthSnapshot{Label=batch.Label,Rows=rows});}else birthEditParents=BirthParentsForBatch(batch);birthEditIndividual.BeginUpdate();try{for(int i=0;i<birthEditParents.Length;i++){var p=birthEditParents[i];birthEditIndividual.Items.Add(String.IsNullOrWhiteSpace(p.Name)?"結果"+(i+1)+"／"+BirthParentTraits(p.Raw):p.Name);}birthEditIndividual.SelectedIndex=-1;}finally{birthEditIndividual.EndUpdate();}FillBirthEditIndividual();
    }
    void FillBirthEditIndividual(){
        fillingBirthEdit=true;try{int index=birthEditIndividual.SelectedIndex;bool selected=index>=0&&index<birthEditParents.Length;birthEditPanel.Enabled=birthEditParents.Length>0;birthEditName.Text=selected?birthEditParents[index].Name??"":"";birthEditGeneration.Value=selected?birthEditParents[index].Generation:0;birthEditLimit.Value=selected?Math.Min(9,birthEditParents[index].GenerationLimit):0;editAcceptedFirst=(int)birthEditGeneration.Value;editAcceptedLast=(int)birthEditLimit.Value;int lower=selected?editAcceptedFirst:birthEditParents.Select(p=>p.Generation).DefaultIfEmpty(1).Max(),upper=selected?editAcceptedLast:birthEditParents.Select(p=>p.GenerationLimit).DefaultIfEmpty(1).Min();if(lower<=upper){birthEditMemo.LoadNotes(selected?birthEditParents[index].DopingMemo:null,lower,upper);birthEditMemo.Enabled=true;}else birthEditMemo.SetUnavailable();editNameChanged=editGenerationChanged=false;}finally{fillingBirthEdit=false;}
    }
    public BirthResult ApplyBirthIndividualEdits(){
        int index=birthEditIndividual.SelectedIndex;bool all=index<0;if(birthEditParents.Length==0)throw new Exception("入力を選択してください。");if(all&&!editNameChanged&&!editGenerationChanged&&!birthEditMemo.HasEdits)throw new Exception("変更する項目を入力してください。");if((!all||editGenerationChanged)&&(birthEditGeneration.Value<1||birthEditLimit.Value<birthEditGeneration.Value))throw new Exception("世代の下限・上限を1～9で設定してください。");
        if(all&&editGenerationChanged){var removed=birthEditParents.SelectMany(p=>DopingNotes.RemovedGenerations(p.DopingMemo,(int)birthEditGeneration.Value,(int)birthEditLimit.Value)).Distinct().ToArray();if(removed.Length>0&&!ConfirmDopingRemoval(DopingNotes.RemovalMessage(removed)))return null;}
        var rows=new List<BirthResult>();foreach(var original in all?birthEditParents:new[]{birthEditParents[index]}){var raw=(int[])original.Raw.Clone();if(editGenerationChanged){raw[0x82]=(int)birthEditGeneration.Value-1;raw[0x83]=(int)birthEditLimit.Value-1;}var memo=birthEditMemo.ApplyTo(original.DopingMemo,raw[0x82]+1,Math.Max(raw[0x82],raw[0x83])+1,true);
        var parent=new BirthParent{Name=!all||editNameChanged?birthEditName.Text.Trim():original.Name,Raw=raw,DopingMemo=memo,Source=original.Source,SourceId=original.SourceId,AncestryKnown=original.AncestryKnown};if(original.Lineage!=null){var tree=original.Lineage;parent.Lineage=new BirthLineage{Name=parent.Name,Raw=(int[])raw.Clone(),DopingMemo=DopingNotes.Copy(memo),Source=tree.Source,Kind=tree.Kind,Note="",ParentA=tree.ParentA,ParentB=tree.ParentB};}model.CheckBirthParent(parent);rows.Add(BirthRowForParent(parent));}SetBirthResults(rows.ToArray());RecordBirthHistory();birthStatus.Text=rows.Count+"人の変更後の個体を表示しました。「保存」で別の保存として残せます。";return rows[0];
    }
    bool ConfirmDopingRemoval(string message){return ConfirmDopingDiscard!=null?ConfirmDopingDiscard(message):MessageBox.Show(this,message,"ドーピングメモ",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes;}
    string BirthParentTraits(int[] raw){if(raw==null)return "";return (raw[0x94]<0?"":model.BirthAntennaText(raw[0x94],raw[0x96]))+"/"+model.HeadText(raw[0x64])+"/"+model.ColorText(raw[0x71])+"/"+model.ColorText(raw[0x72])+"/"+model.PatternText(raw[0x70]);}
    string BirthParentOrigin(BirthParent parent,string savedLabel){return "（"+(String.IsNullOrWhiteSpace(parent.Name)?savedLabel+"/"+BirthParentTraits(parent.Raw):parent.Name)+"）";}
    string LineageParentOrigin(BirthLineage record){if(record==null)return "（）";string label=(record.Source??"").Split('／')[0];return "（"+(String.IsNullOrWhiteSpace(record.Name)?label+"/"+BirthParentTraits(record.Raw):record.Name)+"）";}
    public string BirthResultOrigin(BirthResult result,int index){
        if(result.Candidate==null)return "結果"+(index+1)+"（"+result.Parent1Source+"×"+result.Parent2Source+"）";
        var p=result.Candidate;string source=p.Source??"";int origin=source.IndexOf("／結果",StringComparison.Ordinal);if(origin>=0&&!source.Contains("上流に未確定"))return source.Substring(origin+1);var tree=p.Lineage;if(tree!=null&&(tree.ParentA!=null||tree.ParentB!=null))return "結果"+(index+1)+"（"+LineageParentOrigin(tree.ParentA)+"×"+LineageParentOrigin(tree.ParentB)+"）";return BirthParentOrigin(p,source.Split('／')[0]);
    }
    void AddBirthBatchChoice(ForecastSnapshot batch){foreach(var input in birthBatches){object selected=input.SelectedItem;input.Items.Add(batch);input.SelectedItem=selected;}if(birthEditSource.Items.Count>0)birthEditSource.Items.Add(batch);if(lineageSource.Items.Count>0)lineageSource.Items.Add(batch);}
    static void InstallEmptyResultMessage(DataGridView grid){
        var message=new Label{Name=grid.Name+"EmptyMessage",Text="虚無が見つかりました",BackColor=Color.White,TextAlign=ContentAlignment.MiddleCenter,Visible=false};grid.Controls.Add(message);
        Action update=delegate{if(grid.IsDisposed)return;message.SetBounds(1,grid.ColumnHeadersHeight+8,Math.Max(0,grid.ClientSize.Width-2),40);message.Visible=grid.DataSource!=null&&grid.Rows.Count==0;message.BringToFront();};grid.DataBindingComplete+=delegate{update();};grid.DataSourceChanged+=delegate{update();};grid.SizeChanged+=delegate{update();};
    }
    public void RecordForecastHistory(IEnumerable<ForecastRow> results){
        var rows=results.ToArray();if(rows.Length==0)return;int number=++nextForecastHistoryNumber;var history=new ForecastSnapshot{Rows=rows,Number=number,Label="レアキャッチ履歴"+number+"・"+rows.Length+"人（"+rows[0].Date+"～"+rows[rows.Length-1].Date+"）"};forecastSnapshots.Add(history);savedForecasts.Items.Add(history);savedForecasts.SelectedIndex=savedForecasts.Items.Count-1;
    }
    void RecordBirthHistory(){
        var results=birthResultGrid.DataSource as IEnumerable<BirthResult>;if(results==null)return;var rows=results.ToArray();if(rows.Length==0)return;int number=++nextBirthHistoryNumber;var history=new BirthSnapshot{Rows=rows,Label=BirthMode+"履歴"+number+"・"+rows.Length+"件",Mode=BirthMode};birthSnapshots.Add(history);birthSavedResults.Items.Add(history);birthSavedResults.SelectedIndex=birthSavedResults.Items.Count-1;
    }
    static JavaScriptSerializer SavedInputSerializer(){return new JavaScriptSerializer{MaxJsonLength=256*1024*1024,RecursionLimit=128};}
    void ValidateSavedInput(SavedBirthInput data){
        Guid id;if(data==null||data.Version!=1||!Guid.TryParseExact(data.Id,"N",out id)||String.IsNullOrWhiteSpace(data.Label)||data.Label.Length>80||data.Parents==null||data.Parents.Length==0||data.Parents.Length>100000)throw new Exception("保存個体の形式が異なります。");
        foreach(var parent in data.Parents){if(parent==null||parent.Raw==null||parent.Raw.Length!=252||String.IsNullOrEmpty(parent.SourceId))throw new Exception("保存個体のデータが不完全です。");model.CheckBirthParent(parent);}
    }
    void CommitSavedBirthInput(ForecastSnapshot batch){persistentBirthInputs.Add(batch);AddBirthBatchChoice(batch);UpdateDeleteBirthInputs();}
    ForecastSnapshot PersistBirthInput(string label,BirthParent[] parents){var batch=WriteSavedBirthInput(label,parents);CommitSavedBirthInput(batch);return batch;}
    void SetSavingBirthInput(bool value){savingBirthInput=value;birthPage.Enabled=predict.Enabled=gekiPage.Enabled=lineagePage.Enabled=!value;UseWaitCursor=value;}
    void InstallIndividualRegistration(DataGridView grid){
        grid.Columns.Add(new DataGridViewButtonColumn{Name="RegisterIndividual",HeaderText="登録",Text="登録",UseColumnTextForButtonValue=true,Width=60,MinimumWidth=60,AutoSizeMode=DataGridViewAutoSizeColumnMode.None,SortMode=DataGridViewColumnSortMode.NotSortable});
        var entries=grid==birthResultGrid||grid==forecastGrid?new[]{new[]{"FacePreview","顔プレビュー"},new[]{"RouteIndividual","ルート確認"}}:new[]{new[]{"FacePreview","顔プレビュー"}};foreach(var entry in entries)grid.Columns.Add(new DataGridViewButtonColumn{Name=entry[0],HeaderText=entry[1],Text=entry[1],UseColumnTextForButtonValue=true,Width=90,MinimumWidth=70,AutoSizeMode=DataGridViewAutoSizeColumnMode.None,SortMode=DataGridViewColumnSortMode.NotSortable});grid.Columns["RegisterIndividual"].DisplayIndex=0;grid.CellContentClick+=async delegate(object sender,DataGridViewCellEventArgs e){if(e.RowIndex<0||e.ColumnIndex<0)return;try{if(grid.Columns[e.ColumnIndex].Name=="FacePreview")ShowFacePreview(grid.Name,e.RowIndex);else if(grid.Columns[e.ColumnIndex].Name=="RouteIndividual")await ShowResultRouteAsync(grid.Name,e.RowIndex);}catch(Exception error){ShowError(error);}};
        grid.CellContentClick+=async delegate(object sender,DataGridViewCellEventArgs e){
            if(e.RowIndex<0||e.ColumnIndex<0||grid.Columns[e.ColumnIndex].Name!="RegisterIndividual")return;
            try{CheckIndividualRegistrationReady();var row=grid.Rows[e.RowIndex].DataBoundItem;string initial=IndividualRegistrationName(row);var label=AskSavedName(initial);if(label!=null)await RegisterResultIndividualAsync(grid.Name,e.RowIndex,label);}catch(Exception error){ShowError(error);}
        };
    }
    object currentDisplayedResults;readonly Dictionary<object,BirthParent> editedResultParents=new Dictionary<object,BirthParent>();LineagePortraitRenderer facePreviewRenderer;public int FacePreviewCacheCount {get{return facePreviewRenderer==null?0:facePreviewRenderer.CacheCount;}}
    void TrackCurrentResultRows(){foreach(var grid in new[]{birthResultGrid,forecastGrid}){var observed=grid;observed.DataSourceChanged+=delegate{if(observed.DataSource!=null)currentDisplayedResults=observed.DataSource;};}FormClosed+=delegate{if(facePreviewRenderer!=null)facePreviewRenderer.Dispose();};}
    BirthParent ParentForForecastRow(ForecastRow row,string source,string id){BirthParent edited;if(editedResultParents.TryGetValue(row,out edited))return edited;return model.ParentFromCandidate(row.ParentCandidate,source,id);}
    BirthResult RowForResultObject(object row){var birth=row as BirthResult;if(birth!=null)return birth;var rare=row as ForecastRow;if(rare!=null)return BirthRowForParent(ParentForForecastRow(rare,rare.Date+"／"+rare.Slot,"rare:"+rare.Date+":"+rare.ParentCandidate.Slot+":"+rare.ParentCandidate.Payload));var geki=row as GekiResultRow;if(geki!=null)return BirthRowForParent(model.ParentFromCandidate(geki.Person,"ゲキレア／"+geki.SeedHex,"geki:"+geki.SeedHex+":"+geki.Slot));throw new Exception("個体の結果を選択してください。");}
    BirthResult[] CurrentResultRows(){var birth=currentDisplayedResults as IEnumerable<BirthResult>;if(birth!=null)return birth.ToArray();var rare=currentDisplayedResults as IEnumerable<ForecastRow>;return rare==null?new BirthResult[0]:rare.Select(row=>RowForResultObject(row)).ToArray();}
    DataGridView ResultGrid(string name){if(name==birthResultGrid.Name)return birthResultGrid;if(name==forecastGrid.Name)return forecastGrid;if(name==gekiResults.Name)return gekiResults;if(name==gekiLineup.Name)return gekiLineup;throw new Exception("対象の結果一覧がありません。");}
    BirthResult ResultAt(string name,int index){var grid=ResultGrid(name);if(index<0||index>=grid.Rows.Count)throw new Exception("個体を選択してください。");return RowForResultObject(grid.Rows[index].DataBoundItem);}
    public Bitmap CreateFacePreviewImage(string gridName,int rowIndex){var row=ResultAt(gridName,rowIndex);if(row.Raw==null)throw new Exception("顔データがありません。");if(facePreviewRenderer==null)facePreviewRenderer=new LineagePortraitRenderer(Path.Combine(model.DataDirectory,"portraits"));var face=facePreviewRenderer.Get(row.Raw);if(face==null)throw new Exception("顔データがありません。");return (Bitmap)face.Image.Clone();}
    public void ShowFacePreview(string gridName,int rowIndex){var row=ResultAt(gridName,rowIndex);var image=CreateFacePreviewImage(gridName,rowIndex);var window=new Form{Name="facePreviewWindow",Text="顔プレビュー"+(String.IsNullOrWhiteSpace(row.Name)?"":" — "+row.Name),ClientSize=new Size(340,384),MinimumSize=new Size(220,260),StartPosition=FormStartPosition.CenterParent,BackColor=Color.White};var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));layout.Controls.Add(new PictureBox{Dock=DockStyle.Fill,Image=image,SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.White},0,0);layout.Controls.Add(new Label{Name="facePreviewNotice",Text="色1のみ反映、柄は反映しません。",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter},0,1);window.Controls.Add(layout);window.FormClosed+=delegate{image.Dispose();};window.Show(this);}
    public async Task ShowResultRouteAsync(string gridName,int rowIndex){var grid=ResultGrid(gridName);ResultAt(gridName,rowIndex);currentDisplayedResults=grid.DataSource;ShowLineage();lineageSource.SelectedIndex=0;await LoadLineageSourceAsync();if(rowIndex>=0&&rowIndex<lineageIndividual.Items.Count)lineageIndividual.SelectedIndex=rowIndex;}
    void CheckIndividualRegistrationReady(){if(savingBirthInput||forecastRunning||birthRunning||gekiRunning)throw new Exception("検索・保存が終わってから操作してください。");}
    string IndividualRegistrationName(object row){
        var rare=row as ForecastRow;if(rare!=null)return rare.Name;
        var geki=row as GekiResultRow;if(geki!=null)return geki.Name;
        var birth=row as BirthResult;if(birth==null)throw new Exception("登録する個体を選択してください。");
        if(birth.Raw==null||birth.Raw[0x94]<0)throw new Exception("計算不可・アンテナ未予測の個体は登録できません。");
        return birth.Candidate==null?"出生個体":birth.Candidate.Name;
    }
    public async Task<int> RegisterResultIndividualAsync(string gridName,int rowIndex,string saveName){
        CheckIndividualRegistrationReady();
        DataGridView grid=gridName==forecastGrid.Name?forecastGrid:gridName==birthResultGrid.Name?birthResultGrid:gridName==gekiResults.Name?gekiResults:gridName==gekiLineup.Name?gekiLineup:null;
        if(grid==null||rowIndex<0||rowIndex>=grid.Rows.Count)throw new Exception("登録する個体を選択してください。");
        object row=grid.Rows[rowIndex].DataBoundItem;IndividualRegistrationName(row);
        if(String.IsNullOrWhiteSpace(saveName)||saveName.Trim().Length>80)throw new Exception("保存名は1～80文字で入力してください。");
        string label=saveName.Trim();SetSavingBirthInput(true);
        try{
            var batch=await Task.Run(()=>{
                BirthParent[] parents;var rare=row as ForecastRow;var geki=row as GekiResultRow;
                if(rare!=null)parents=BirthParentsForBatch(new ForecastSnapshot{Rows=new[]{rare},Label=label});
                else if(geki!=null)parents=new[]{model.ParentFromCandidate(geki.Person,label+"／ゲキレア・seed "+geki.SeedHex+"・"+geki.Slot,"geki:"+geki.SeedHex+":"+geki.Slot+":"+geki.Person.Payload)};
                else parents=SavedParentsForBirth(new BirthSnapshot{Rows=new[]{(BirthResult)row},Label=label});
                if(parents.Length!=1)throw new Exception("1人だけの登録に失敗しました。");
                return WriteSavedBirthInput(label,parents);
            });
            CommitSavedBirthInput(batch);string message="「"+batch.Label+"」として1人を登録しました。次回起動後も保存一覧から選択できます。";
            if(grid==forecastGrid)forecastStatus.Text=message;else if(grid==birthResultGrid)birthStatus.Text=message;else gekiStatus.Text=message;
            return 1;
        }finally{SetSavingBirthInput(false);}
    }
    public async Task<int> SaveForecastResultsAsync(string saveName=null){
        if(savingBirthInput||forecastRunning)throw new Exception("検索・保存が終わってから操作してください。");var current=forecastGrid.DataSource as IEnumerable<ForecastRow>;if(current==null)throw new Exception("先に検索を実行してください。");var rows=current.ToArray();if(rows.Length==0)throw new Exception("検索結果が0人のため保存できません。");int number=nextForecastSnapshotNumber+1;string label=saveName??("レアキャッチ"+number+"・"+rows.Length+"人（"+rows[0].Date+"～"+rows[rows.Length-1].Date+"）");
        SetSavingBirthInput(true);forecastStatus.Text="条件に一致した"+rows.Length+"人を保存中…";try{var batch=await Task.Run(()=>WriteSavedBirthInput(label,BirthParentsForBatch(new ForecastSnapshot{Rows=rows,Number=number,Label=label})));CommitSavedBirthInput(batch);nextForecastSnapshotNumber=number;forecastStatus.Text=rows.Length+"人を「"+batch.Label+"」として保存しました。次回起動後も残ります。";return rows.Length;}finally{SetSavingBirthInput(false);}
    }
    void UpdateDeleteBirthInputs(){for(int i=0;i<2;i++){var batch=birthBatches[i].SelectedItem as ForecastSnapshot;deleteBirthInputs[i].Enabled=batch!=null&&!batch.BuiltIn&&!String.IsNullOrEmpty(batch.SavedId);}}
    string AskSavedName(string initial){
        if(RequestSaveName!=null)return RequestSaveName(initial);
        using(var dialog=new Form{Text="名前を付けて保存",ClientSize=new Size(400,120),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,Font=Font}){
            var title=new Label{Text="保存名",Location=new Point(12,10),AutoSize=true};var input=new TextBox{Text=initial??"",MaxLength=80,Location=new Point(12,34),Width=376};var ok=new Button{Text="保存",Location=new Point(208,78),Width=85};var cancel=new Button{Text="キャンセル",Location=new Point(303,78),Width=85,DialogResult=DialogResult.Cancel};ok.Click+=delegate{if(String.IsNullOrWhiteSpace(input.Text)){MessageBox.Show(dialog,"保存名を入力してください。","未入力",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}dialog.DialogResult=DialogResult.OK;};dialog.Controls.AddRange(new Control[]{title,input,ok,cancel});dialog.AcceptButton=ok;dialog.CancelButton=cancel;dialog.Shown+=delegate{input.SelectAll();input.Focus();};return dialog.ShowDialog(this)==DialogResult.OK?input.Text.Trim():null;
        }
    }
    public void BeginBirthMaterialSearch(int target){
        if(target<0||target>1)throw new ArgumentOutOfRangeException("target");ShowPredictor();
        forecastStatus.Text="素材検索：予測キーと検索範囲・特徴を指定して「検索実行」→「保存」。出生画面の入力欄で親"+(target+1)+"を選択できます。親の入力は保持しています。";
        if(String.IsNullOrWhiteSpace(forecastKey.Text))forecastKey.Focus();
    }
    static void ConfigureBirthGrid(DataGridView g,string name){g.Name=name;g.Dock=DockStyle.Fill;g.ReadOnly=true;g.AllowUserToAddRows=g.AllowUserToDeleteRows=false;g.RowHeadersVisible=false;g.AutoGenerateColumns=false;g.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;g.BackgroundColor=Color.White;g.Font=new Font("Yu Gothic UI",9);g.ColumnHeadersHeight=30;g.RowTemplate.Height=38;g.DefaultCellStyle.WrapMode=DataGridViewTriState.True;g.ClipboardCopyMode=DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;}
    static void AddBirthColumn(DataGridView g,string property,string text,int weight){g.Columns.Add(new DataGridViewTextBoxColumn{Name=property,DataPropertyName=property,HeaderText=text,FillWeight=weight,MinimumWidth=property=="Antenna"?90:property=="Body"?35:30,SortMode=DataGridViewColumnSortMode.NotSortable});}
    public void SetBirthParent(int target,BirthParent parent){if(target<0||target>1)throw new ArgumentOutOfRangeException("target");(target==0?parentEditorA:parentEditorB).Fill(parent);}
    public BirthParent GetBirthParent(int target){if(target<0||target>1)throw new ArgumentOutOfRangeException("target");return (target==0?parentEditorA:parentEditorB).Read();}
    public int SaveForecastResults(){return SaveForecastResults(null);}
    public int SaveForecastResults(string saveName){
        if(forecastRunning||savingBirthInput)throw new Exception("検索・保存が終わってから操作してください。");
        var rows=forecastGrid.DataSource as IEnumerable<ForecastRow>;if(rows==null)throw new Exception("先に検索を実行してください。");
        var snapshot=rows.ToArray();if(snapshot.Length==0)throw new Exception("検索結果が0人のため保存できません。");
        int number=nextForecastSnapshotNumber+1;string label=saveName??("レアキャッチ"+number+"・"+snapshot.Length+"人（"+snapshot[0].Date+"～"+snapshot[snapshot.Length-1].Date+"）");var parents=BirthParentsForBatch(new ForecastSnapshot{Rows=snapshot,Number=number,Label=label});var batch=PersistBirthInput(label,parents);batch.Rows=snapshot;batch.Number=number;nextForecastSnapshotNumber=number;
        foreach(var pick in birthMaterials){pick.BeginUpdate();try{pick.Items.AddRange(snapshot.Select(row=>(object)new BirthMaterialChoice{Row=row,Batch=batch.Label,Snapshot=batch}).ToArray());}finally{pick.EndUpdate();}}
        forecastStatus.Text=snapshot.Length+"人を「"+batch.Label+"」として保存しました。出生・絞り込みの入力から選択でき、次回起動後も残ります。";
        return snapshot.Length;
    }
    public void RestoreForecastResults(int index){
        if(forecastRunning)throw new Exception("検索が終わってから保存結果を表示してください。");
        if(index<0||index>=forecastSnapshots.Count)throw new Exception("出力履歴を選んでください。");
        forecastGrid.DataSource=forecastSnapshots[index].Rows.ToList();ShowPredictor();
        forecastStatus.Text=forecastSnapshots[index].Label+"の出力履歴を表示しています。検索条件欄と保存個体は変更していません。";
    }
    public void DeleteForecastResults(int index){
        if(forecastRunning)throw new Exception("検索が終わってから保存結果を削除してください。");
        if(index<0||index>=forecastSnapshots.Count)throw new Exception("削除する出力履歴を選んでください。");
        var batch=forecastSnapshots[index];forecastSnapshots.RemoveAt(index);savedForecasts.Items.RemoveAt(index);
        savedForecasts.SelectedIndex=forecastSnapshots.Count==0?-1:Math.Min(index,forecastSnapshots.Count-1);
        forecastStatus.Text=batch.Label+"の出力履歴を削除しました。保存個体と現在の結果は保持しています。";
    }
    public void ApplyBirthMaterial(int target){
        if(target<0||target>1)throw new ArgumentOutOfRangeException("target");
        var item=birthMaterials[target].SelectedItem as BirthMaterialChoice;
        if(item==null||item.Row==null||item.Row.ParentCandidate==null)throw new Exception("保存した検索結果から素材を選択してください。");
        var row=item.Row;string id="rare:"+row.Date+":"+row.ParentCandidate.Slot+":"+row.ParentCandidate.Payload;
        SetBirthParent(target,model.ParentFromCandidate(row.ParentCandidate,item.Batch+"／"+row.Date+"・"+row.Slot,id));
        birthStatus.Text=row.Name+"を親"+(target+1)+"へ入力しました。保存した全素材はそのまま残ります。未捕獲の素材は予測どおり捕獲できた場合の入力です。";
    }
    public BirthResult PredictBirthNow(){
        var result=model.PredictBirth(parentEditorA.Read(),parentEditorB.Read(),true);
        result.Parent1=parentEditorA.Read().Name;result.Parent2=parentEditorB.Read().Name;
        if(!parentEditorA.AncestryKnown||!parentEditorB.AncestryKnown){result.Antenna="未予測";result.Condition+=" 祖先アンテナ値が不明なためアンテナは対象外（外見は予測済み）。";result.Raw[0x94]=-1;}
        result.Lineage=Model.CreateBirthLineage(result.Raw,parentEditorA.Read(),parentEditorB.Read(),result.Condition);SetBirthResults(new[]{result});RecordBirthHistory();return result;
    }
    void BuildBirthResultTools(FlowLayoutPanel toolbar){
        var filters=new TableLayoutPanel{Name="birthResultFilters",Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};foreach(int h in new[]{58,62})filters.RowStyles.Add(new RowStyle(SizeType.Absolute,h));birthConditionHost.Controls.Add(filters);
        var first=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};filters.Controls.Add(first,0,0);
        first.Controls.Add(LabelFor("アンテナ"));birthAntennaFilter.Name="birthFilterAntenna";birthAntennaFilter.Width=150;birthAntennaFilter.DropDownWidth=250;birthAntennaFilter.DropDownStyle=ComboBoxStyle.DropDownList;birthAntennaFilter.Items.Add("未指定");birthAntennaFilter.Items.AddRange(model.BirthAntennaFamilies().Cast<object>().ToArray());birthAntennaFilter.SelectedIndex=0;first.Controls.Add(birthAntennaFilter);
        AddBirthFilter(first,0x64,"頭",model.Heads(),110);
        AddBirthFilter(first,0x71,"色1",model.Colors(),90);AddBirthFilter(first,0x72,"色2",model.Colors(),90);
        birthReverseColors.Name="birthFilterReverseColors";birthReverseColors.Text="逆順を許可";birthReverseColors.Checked=true;birthReverseColors.AutoSize=true;birthReverseColors.Padding=new Padding(0,5,0,0);first.Controls.Add(birthReverseColors);
        AddBirthFilter(first,0x70,"柄",model.PatternFilterChoices(),90);
        AddBirthFilter(first,0x6d,"体格",model.BodyFilterChoices(false),88);
        first.Controls.Add(LabelFor("名前"));birthNameFilter.Name="birthFilterName";birthNameFilter.Width=85;first.Controls.Add(birthNameFilter);
        StackFieldLabels(first);birthFilterHeight=120;parentEditorA.SetReverseSpaceWidth(birthReverseColors.PreferredSize.Width);parentEditorB.SetReverseSpaceWidth(birthReverseColors.PreferredSize.Width);
        var faces=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=1,ColumnCount=9,Margin=Padding.Empty,Padding=new Padding(0,0,4,0)};faces.RowStyles.Add(new RowStyle(SizeType.Percent,100));filters.Controls.Add(faces,0,1);int[] order={1,0,2,3,4,5,6,7,8},offsets={0x65,0x66,0x67,0x68,0x69,0x6a,0x6b,0x73,0x74};
        for(int pos=0;pos<9;pos++){
            int i=order[pos];faces.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/9));
            var group=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,Margin=new Padding(2,0,4,0),Padding=Padding.Empty};group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));group.RowStyles.Add(new RowStyle(SizeType.Absolute,22));group.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            group.Controls.Add(new Label{Text=Model.FaceNames[i],Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Margin=Padding.Empty},0,0);
            var box=new ComboBox{Name="birthFilter"+offsets[i].ToString("X2"),Dock=DockStyle.Top,Margin=new Padding(0,1,2,4),DropDownStyle=ComboBoxStyle.DropDownList,DisplayMember="Text",ValueMember="Value",BindingContext=new BindingContext()};box.DataSource=model.FaceChoices(i).Select(n=>new Choice{Value=n,Text=n<0?"未指定":n.ToString("D2")}).ToArray();box.SelectedValue=-1;birthFilters.Add(offsets[i],box);group.Controls.Add(box,0,1);faces.Controls.Add(group,pos,0);
        }
        birthFilters[0x66].SelectedIndexChanged+=delegate{SyncBirthFilterHair();};SyncBirthFilterHair();
        EventHandler filterChanged=delegate{if(!updatingBirthFilters&&birthResultGrid.DataSource!=null&&!birthModeRegister.Checked)birthStatus.Text="条件を変更しました。「検索実行」で反映します。表示・履歴・保存の対象は前回検索で一致した結果のままです。";};foreach(var box in birthFilters.Values)box.SelectedIndexChanged+=filterChanged;birthAntennaFilter.SelectedIndexChanged+=filterChanged;birthReverseColors.CheckedChanged+=filterChanged;birthNameFilter.TextChanged+=filterChanged;
        birthResetButton.Click+=delegate{if(birthModeEdit.Checked){FillBirthEditIndividual();ClearBirthResults();return;}ResetBirthResultFilters();if(birthResultGrid.DataSource!=null&&!birthModeRegister.Checked)birthStatus.Text="条件をリセットしました。「検索実行」で反映します。前回の結果は保持しています。";};
        var save=new Button{Name="birthSaveResults",Text="保存",AutoSize=true};toolbar.Controls.Add(save);var saveNamed=new Button{Name="birthSaveNamedResults",Text="名前を付けて保存",AutoSize=true};toolbar.Controls.Add(saveNamed);toolbar.Controls.Add(LabelFor("出力履歴"));birthSavedResults.Name="birthSavedResults";birthSavedResults.Width=175;birthSavedResults.DropDownWidth=420;birthSavedResults.DropDownStyle=ComboBoxStyle.DropDownList;toolbar.Controls.Add(birthSavedResults);var restore=new Button{Name="birthRestoreResults",Text="出力履歴を表示",AutoSize=true};var delete=new Button{Name="birthDeleteResults",Text="選択した履歴を削除",AutoSize=true};toolbar.Controls.Add(restore);toolbar.Controls.Add(delete);
        save.Click+=async delegate{try{await SaveBirthResultsAsync();}catch(Exception e){ShowError(e);}};restore.Click+=delegate{try{RestoreBirthResults(birthSavedResults.SelectedIndex);}catch(Exception e){ShowError(e);}};delete.Click+=delegate{try{DeleteBirthResults(birthSavedResults.SelectedIndex);}catch(Exception e){ShowError(e);}};
        saveNamed.Click+=async delegate{try{var initial=birthModeRegister.Checked&&!String.IsNullOrWhiteSpace(registrationSaveName.Text)?registrationSaveName.Text:birthModeRegister.Checked?parentEditorA.Read().Name:BirthMode;var name=AskSavedName(initial);if(name!=null)await SaveBirthResultsAsync(name);}catch(Exception e){ShowError(e);}};
    }
    ComboBox AddBirthFilter(Control panel,int offset,string label,Choice[] choices,int width){var title=LabelFor(label);panel.Controls.Add(title);var box=new ComboBox{Name="birthFilter"+offset.ToString("X2"),Width=width,DropDownStyle=ComboBoxStyle.DropDownList,DisplayMember="Text",ValueMember="Value",BindingContext=new BindingContext()};box.DataSource=choices;box.SelectedValue=-1;panel.Controls.Add(box);birthFilters.Add(offset,box);return box;}
    void SyncBirthFilterHair(){bool previous=updatingBirthFilters;updatingBirthFilters=true;try{int hair=Convert.ToInt32(birthFilters[0x66].SelectedValue);var contour=birthFilters[0x65];contour.Enabled=hair==0;if(hair<0)contour.SelectedValue=-1;else if(hair>0)contour.SelectedValue=Model.ContourForHair(hair);}finally{updatingBirthFilters=previous;}}
    void ResetBirthResultFilters(){updatingBirthFilters=true;try{birthNameFilter.Clear();birthAntennaFilter.SelectedIndex=0;birthReverseColors.Checked=true;foreach(var box in birthFilters.Values)box.SelectedValue=-1;SyncBirthFilterHair();}finally{updatingBirthFilters=false;}}
    void ClearBirthResults(){birthSearchResults=new BirthResult[0];birthResultGrid.DataSource=null;}
    public void SetBirthResults(BirthResult[] results){var matches=CaptureBirthResultFilter();birthSearchResults=results.Where(matches).ToArray();birthResultGrid.DataSource=birthSearchResults;birthStatus.Text=BirthMode+"結果：条件に一致した"+birthSearchResults.Length+"件のみ保持しています。条件の変更は「検索実行」で反映します。";}
    Func<BirthResult,bool> CaptureBirthResultFilter(){
        var values=birthFilters.ToDictionary(p=>p.Key,p=>Convert.ToInt32(p.Value.SelectedValue));string antenna=birthAntennaFilter.SelectedIndex>0?birthAntennaFilter.Text:null;string name=birthNameFilter.Text.Trim();bool active=name.Length>0||antenna!=null||values.Any(p=>p.Value>=0);
        bool registration=birthModeRegister.Checked,reverse=birthReverseColors.Checked;
        return r=>registration||!active||r.Raw!=null&&(name.Length==0||r.Candidate!=null&&(r.Candidate.Name??"").Contains(name))&&(antenna==null||model.BirthAntennaFamily(r.Raw[0x94])==antenna)&&Model.ColorPairMatches(values[0x71],values[0x72],r.Raw[0x71],r.Raw[0x72],!reverse)&&values.All(p=>p.Value<0||p.Key==0x71||p.Key==0x72||(p.Key==0x70?Model.PatternMatches(p.Value,r.Raw[p.Key]):p.Key==0x6d?Model.BodyMatches(p.Value,r.Raw[p.Key]):r.Raw[p.Key]==p.Value));
    }
    public int SaveBirthResults(){return SaveBirthResults(null);}
    BirthSnapshot CaptureBirthSave(string saveName,int number){
        var rows=birthResultGrid.DataSource as IEnumerable<BirthResult>;if(rows==null||!rows.Any())throw new Exception("保存する出生結果がありません。");
        if(birthModeRegister.Checked)parentEditorA.Read();string label=saveName??(birthModeRegister.Checked?(String.IsNullOrWhiteSpace(registrationSaveName.Text)?rows.First().Candidate.Name:registrationSaveName.Text.Trim()):BirthMode+"保存"+number+"・"+rows.Count()+"件");return new BirthSnapshot{Rows=rows.ToArray(),Label=label,Mode=BirthMode};
    }
    BirthParent[] SavedParentsForBirth(BirthSnapshot snapshot){var parents=new List<BirthParent>();
        for(int i=0;i<snapshot.Rows.Length;i++){var r=snapshot.Rows[i];if(r.Raw==null||r.Raw[0x94]<0)continue;if(r.Candidate!=null){var p=r.Candidate;parents.Add(new BirthParent{Name=p.Name,Raw=(int[])p.Raw.Clone(),DopingMemo=DopingNotes.Copy(p.DopingMemo),Source=p.Source,SourceId=p.SourceId,AncestryKnown=p.AncestryKnown,Lineage=p.Lineage});continue;}if(String.IsNullOrEmpty(r.ChildSourceId))r.ChildSourceId="birth:"+Guid.NewGuid().ToString("N");parents.Add(new BirthParent{Name=r.Name,Raw=(int[])r.Raw.Clone(),DopingMemo=r.Lineage==null?null:DopingNotes.Copy(r.Lineage.DopingMemo),Source=snapshot.Label+"／"+BirthResultOrigin(r,i),SourceId=r.ChildSourceId,Lineage=r.Lineage});}
        if(parents.Count==0)throw new Exception("出生の入力に使える個体がありません。計算不可・アンテナ未予測の結果は保存できません。");return parents.ToArray();
    }
    public int SaveBirthResults(string saveName){
        if(savingBirthInput)throw new Exception("保存が終わってから操作してください。");int number=nextBirthSnapshotNumber+1;var snapshot=CaptureBirthSave(saveName,number);var batch=PersistBirthInput(snapshot.Label,SavedParentsForBirth(snapshot));nextBirthSnapshotNumber=number;birthStatus.Text=batch.Label+"を保存し、入力候補"+batch.Count+"人を追加しました。保存個体は次回起動後も残ります。";return snapshot.Rows.Length;
    }
    public async Task<int> SaveBirthResultsAsync(string saveName=null){
        if(savingBirthInput)throw new Exception("保存が終わってから操作してください。");int number=nextBirthSnapshotNumber+1;var snapshot=CaptureBirthSave(saveName,number);SetSavingBirthInput(true);birthStatus.Text="条件に一致した"+snapshot.Rows.Length+"件を保存中…";
        try{var batch=await Task.Run(()=>WriteSavedBirthInput(snapshot.Label,SavedParentsForBirth(snapshot)));CommitSavedBirthInput(batch);nextBirthSnapshotNumber=number;birthStatus.Text=batch.Label+"を保存し、入力候補"+batch.Count+"人を追加しました。次回起動後も残ります。";return snapshot.Rows.Length;}finally{SetSavingBirthInput(false);}
    }
    public void RestoreBirthResults(int index){if(index<0||index>=birthSnapshots.Count)throw new Exception("出力履歴を選んでください。");var snapshot=birthSnapshots[index];SetBirthMode(snapshot.Mode);ResetBirthResultFilters();SetBirthResults(snapshot.Rows);birthStatus.Text=snapshot.Label+"を表示しました（"+birthResultGrid.Rows.Count+"件）。絞り込み条件を未指定へ戻しました。親の選択・入力は保持しています。";}
    public void DeleteBirthResults(int index){if(index<0||index>=birthSnapshots.Count)throw new Exception("削除する出力履歴を選んでください。");var snapshot=birthSnapshots[index];birthSnapshots.RemoveAt(index);birthSavedResults.Items.RemoveAt(index);birthSavedResults.SelectedIndex=birthSnapshots.Count==0?-1:Math.Min(index,birthSnapshots.Count-1);birthStatus.Text=snapshot.Label+"の出力履歴を削除しました。保存個体・現在の表示・手入力は保持しています。";}
    ForecastSnapshot SelectedBirthBatch(int target){var batch=birthBatches[target].SelectedItem as ForecastSnapshot;if(batch==null)throw new Exception("親"+(target+1)+"の保存結果を選んでください。");return batch;}
    public void SetBirthMode(string mode){if(mode=="出生")birthModeBirth.Checked=true;else if(mode=="絞り込み")birthModeFilter.Checked=true;else if(mode=="個体登録")birthModeRegister.Checked=true;else if(mode=="名前/ドーピング/世代変更")birthModeEdit.Checked=true;else throw new ArgumentException("未対応のモードです。");}
    IEnumerable<BirthParent> EnumerateBirthParents(ForecastSnapshot batch){return batch.Parents??batch.Rows.Select(row=>ParentForForecastRow(row,(batch.Label??"レアキャッチ"+batch.Number)+"／"+row.Date+"・"+row.Slot,"rare:"+row.Date+":"+row.ParentCandidate.Slot+":"+row.ParentCandidate.Payload));}
    BirthParent[] BirthParentsForBatch(ForecastSnapshot batch){return EnumerateBirthParents(batch).ToArray();}
    BirthResult BirthRowForParent(BirthParent p,bool lineage=true){int[] f=(int[])p.Raw.Clone();return new BirthResult{Candidate=p,Lineage=lineage?Model.CaptureBirthParent(p):null,Raw=f,Parent1=p.Name,Parent1Source=p.Source,Condition="登録・保存個体／"+p.Source,Antenna=model.BirthAntennaText(f[0x94],p.Level),Body=f[0x6d],Color1=model.ColorText(f[0x71]),Color2=model.ColorText(f[0x72]),Head=model.HeadText(f[0x64]),Pattern=model.PatternText(f[0x70]),Generation=p.Generation,FaceShape=f[0x65],Hair=f[0x66],Brow=f[0x67],Eye=f[0x68],Nose=f[0x69],Mouth=f[0x6a],Cheek=f[0x6b],SkinColor=f[0x73],HairColor=f[0x74]};}
    public BirthResult RegisterBirthIndividual(){var parent=parentEditorA.Read();model.CheckBirthParent(parent);var row=BirthRowForParent(parent);SetBirthResults(new[]{row});RecordBirthHistory();return row;}
    public BirthResult[] FilterSavedBirthIndividuals(){var batch=SelectedBirthBatch(0);var matches=CaptureBirthResultFilter();var rows=new List<BirthResult>();foreach(var parent in EnumerateBirthParents(batch)){var row=BirthRowForParent(parent,false);if(!matches(row))continue;row.Lineage=Model.CaptureBirthParent(parent);rows.Add(row);}SetBirthResults(rows.ToArray());RecordBirthHistory();return birthSearchResults;}
    public BirthResult[] PredictBirthBatches(ForecastSnapshot first,ForecastSnapshot second,bool trained){
        return PredictBirthBatches(first,second,trained,CaptureBirthResultFilter());
    }
    BirthResult[] PredictBirthBatches(ForecastSnapshot first,ForecastSnapshot second,bool trained,Func<BirthResult,bool> matches,Action<long,long> progress=null){
        long completed=0,total=(long)first.Count*second.Count;var rows=new List<BirthResult>();foreach(var row in BirthSearchSteps(first,second,trained,matches)){completed++;if(progress!=null)progress(completed,total);if(row==null)continue;if(rows.Count>=100000)throw new Exception("検索条件に一致する出生結果が10万件を超えます。");rows.Add(row);}return rows.ToArray();
    }
    IEnumerable<BirthResult> BirthSearchSteps(ForecastSnapshot first,ForecastSnapshot second,bool trained,Func<BirthResult,bool> matches){
        if(first==null||second==null||first.Parents==null&&first.Rows==null||second.Parents==null&&second.Rows==null)throw new Exception("両親の保存結果を選んでください。");
        Func<ForecastRow,BirthParent> convert=row=>model.ParentFromCandidate(row.ParentCandidate,row.Date+"・"+row.Slot,"rare:"+row.Date+":"+row.ParentCandidate.Slot+":"+row.ParentCandidate.Payload);
        var aa=first.Parents??first.Rows.Select(convert).ToArray();var bb=second.Parents??second.Rows.Select(convert).ToArray();
        var ancestry=new Dictionary<BirthParent,BirthLineage>();
        foreach(var a in aa)foreach(var b in bb){
            if(Model.SameBirthInputIndividual(a,b)){yield return null;continue;}
            BirthResult result;try{result=model.PredictBirth(a,b,trained);}catch(Exception e){result=new BirthResult{Condition="未予測："+e.Message};}
            result.Parent1=a.Name??"";result.Parent2=b.Name??"";
            result.Parent1Source=BirthParentOrigin(a,first.Label??"保存"+first.Number);result.Parent2Source=BirthParentOrigin(b,second.Label??"保存"+second.Number);
            if(result.Raw!=null){
                if(!a.AncestryKnown||!b.AncestryKnown){result.Antenna="未予測";result.Raw[0x94]=-1;result.Condition+=" 祖先アンテナ不明のためアンテナは未予測。";}
            }
            if(!matches(result)){yield return null;continue;}
            if(result.Raw!=null){
                using(var sha=SHA256.Create()){string[] ids={a.SourceId,b.SourceId};Array.Sort(ids,StringComparer.Ordinal);result.ChildSourceId="birth:"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(String.Join("|",ids)+"|"+String.Join(",",result.Raw)))).Replace("-","");}
            }if(!ancestry.ContainsKey(a))ancestry[a]=Model.CaptureBirthParent(a);if(!ancestry.ContainsKey(b))ancestry[b]=Model.CaptureBirthParent(b);result.Lineage=new BirthLineage{Name="",Source="出生予測",Kind=result.Raw==null?"未予測":"出生",Note="",Raw=result.Raw==null?null:(int[])result.Raw.Clone(),ParentA=ancestry[a],ParentB=ancestry[b]};yield return result;
        }
    }
    readonly TextBox routeNameInput=new TextBox(),routeDopingSummary=new TextBox();readonly DopingMemoEditor routeDopingMemo=new DopingMemoEditor("routeIndividualDoping",false);readonly Button routeApplyChanges=new Button(),routeApplyNameChanges=new Button();BirthLineage routeSelectedRecord;bool fillingRouteMemo;
    string routeEditPath;object loadedRouteSource,loadedRouteResultSource;
    void BuildRouteEditFields(Control host){
        var editor=new TableLayoutPanel{Name="routeIndividualEditor",Dock=DockStyle.Top,Height=172,ColumnCount=1,RowCount=4,Margin=Padding.Empty};foreach(int height in new[]{30,44,52,46})editor.RowStyles.Add(new RowStyle(SizeType.Absolute,height));host.Controls.Add(editor);
        var nameRow=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};nameRow.Controls.Add(LabelFor("名前"));routeNameInput.Name="routeIndividualName";routeNameInput.Width=220;routeNameInput.MaxLength=20;nameRow.Controls.Add(routeNameInput);routeApplyNameChanges.Name="routeApplyNameChanges";routeApplyNameChanges.Text="変更";routeApplyNameChanges.AutoSize=true;routeApplyNameChanges.Enabled=false;nameRow.Controls.Add(routeApplyNameChanges);AlignInlineRow(nameRow);editor.Controls.Add(nameRow,0,0);
        lineageDetails.Name="lineageDetails";lineageDetails.Dock=DockStyle.Fill;lineageDetails.ReadOnly=true;lineageDetails.Multiline=true;lineageDetails.ScrollBars=ScrollBars.Vertical;lineageDetails.Margin=Padding.Empty;editor.Controls.Add(lineageDetails,0,1);
        editor.Controls.Add(routeDopingMemo,0,2);routeDopingMemo.Changed+=delegate{if(!fillingRouteMemo)UpdateRouteDopingCalculation();};routeDopingSummary.Name="routeDopingCalculation";routeDopingSummary.Dock=DockStyle.Fill;routeDopingSummary.Multiline=true;routeDopingSummary.ReadOnly=true;routeDopingSummary.ScrollBars=ScrollBars.Vertical;editor.Controls.Add(routeDopingSummary,0,3);
        routeApplyChanges.Name="routeApplyChanges";routeApplyChanges.Text="変更";routeApplyChanges.AutoSize=true;routeApplyChanges.Enabled=false;routeDopingMemo.AddAction(routeApplyChanges);routeApplyChanges.Click+=async delegate{try{await ApplyRouteDopingChangesAsync();}catch(Exception error){ShowError(error);}};routeApplyNameChanges.Click+=async delegate{try{await ApplyRouteNameChangesAsync();}catch(Exception error){ShowError(error);}};
    }
    string RouteRecordPath(BirthLineage root,BirthLineage selected,int depth){if(root==null||selected==null||depth>20)return null;if(Object.ReferenceEquals(root,selected))return "";string path=RouteRecordPath(root.ParentA,selected,depth+1);if(path!=null)return "A"+path;path=RouteRecordPath(root.ParentB,selected,depth+1);return path==null?null:"B"+path;}
    BirthLineage RouteRecordAt(BirthLineage root,string path){foreach(char branch in path){if(root==null||branch!='A'&&branch!='B')throw new Exception("選択個体を確認できません。");root=branch=='A'?root.ParentA:root.ParentB;}return root;}
    void SelectRouteEditRecord(BirthLineage record){
        routeEditPath=RouteRecordPath(displayedLineageRecord,record,0);var saved=lineageSource.SelectedItem as ForecastSnapshot;bool valid=record!=null&&record.Raw!=null&&record.Raw[0x94]>=0&&routeEditPath!=null;bool writable=valid&&(saved==null||!saved.BuiltIn)&&Object.Equals(loadedRouteSource,lineageSource.SelectedItem);routeSelectedRecord=valid?record:null;routeNameInput.Text=record==null?"":record.Name??"";routeNameInput.Enabled=writable;fillingRouteMemo=true;try{int first=valid?record.Raw[0x82]+1:1,last=valid?Math.Max(first,record.Raw[0x83]+1):1;routeDopingMemo.LoadNotes(valid?record.DopingMemo:null,first,last);routeDopingMemo.Enabled=valid;foreach(var box in routeDopingMemo.Inputs)box.Enabled=writable;}finally{fillingRouteMemo=false;}routeApplyChanges.Enabled=routeApplyNameChanges.Enabled=writable;lineageDetails.Text=record==null?"":String.Join("\r\n",DescribeLineage(record).Split(new[]{"\r\n"},StringSplitOptions.RemoveEmptyEntries).Skip(1));UpdateRouteDopingCalculation();if(valid&&lineageDiagram!=null&&!Object.ReferenceEquals(lineageDiagram.SelectedRecord,record))lineageDiagram.SelectRecord(record);if(autoDopingRouteSync!=null)autoDopingRouteSync(record);
    }
    void UpdateRouteDopingCalculation(){
        if(routeSelectedRecord==null){routeDopingSummary.Clear();return;}try{var record=routeSelectedRecord;var draft=new BirthLineage{Name=record.Name,Source=record.Source,Kind=record.Kind,Raw=record.Raw,DopingMemo=routeDopingMemo.HasEdits?routeDopingMemo.Notes:record.DopingMemo,ParentA=record.ParentA,ParentB=record.ParentB};var value=DopingNotes.Calculate(draft,record.Raw[0x82]+1);string[] names={"HP","AP","こうげきりょく","ぼうぎょりょく","すばやさ"};routeDopingSummary.Text="ドーピング合計値："+String.Join("　",Enumerable.Range(0,5).Select(i=>names[i]+" +"+value.Total[i]))+"\r\n"+"現世代："+String.Join(" / ",value.Current)+"　継承："+String.Join(" / ",value.Inherited)+(value.Legacy?"　旧形式の現世代加算は果物メモに未換算です。":"");}catch(Exception error){routeDopingSummary.Text="計算できません："+error.Message;}
    }
    public void SelectRouteRecordPath(string path){if(path==null||path.Length>20)throw new Exception("選択個体を確認できません。");var record=RouteRecordAt(displayedLineageRecord,path);if(record==null)throw new Exception("選択個体がありません。");lineageDiagram.SelectRecord(record);SelectRouteEditRecord(record);}
    BirthLineage CopyEditedRoutePath(BirthLineage root,string path,int depth,string name,DopingEntry[] doping){
        if(root==null)throw new Exception("選択個体の系統情報がありません。");bool target=depth==path.Length;int[] raw=root.Raw;if(target){if(raw==null)throw new Exception("選択個体のデータがありません。");raw=(int[])raw.Clone();model.CheckBirthParent(new BirthParent{Name=name,Raw=raw,DopingMemo=doping});}
        return new BirthLineage{Name=target?name:root.Name,Source=root.Source,Kind=root.Kind,Note=root.Note,Raw=raw,DopingMemo=DopingNotes.Copy(target?doping:root.DopingMemo),ParentA=!target&&path[depth]=='A'?CopyEditedRoutePath(root.ParentA,path,depth+1,name,doping):root.ParentA,ParentB=!target&&path[depth]=='B'?CopyEditedRoutePath(root.ParentB,path,depth+1,name,doping):root.ParentB};
    }
    public Task ApplyRouteIndividualChangesAsync(){return ApplyRouteFieldsAsync(true,true);}
    public Task ApplyRouteNameChangesAsync(){return ApplyRouteFieldsAsync(true,false);}
    public Task ApplyRouteDopingChangesAsync(){return ApplyRouteFieldsAsync(false,true);}
    async Task ApplyRouteFieldsAsync(bool applyName,bool applyDoping){
        CheckIndividualRegistrationReady();var choice=lineageIndividual.SelectedItem as LineageChoice;int index=lineageIndividual.SelectedIndex;object source=lineageSource.SelectedItem;var saved=source as ForecastSnapshot;string path=routeEditPath;if(choice==null||path==null||routeSelectedRecord==null||!Object.Equals(loadedRouteSource,source))throw new Exception("変更する個体を選択してください。");if(source is string&&!Object.ReferenceEquals(loadedRouteResultSource,currentDisplayedResults))throw new Exception("現在の結果が更新されました。表示元を選び直してください。");if(saved!=null&&(saved.BuiltIn||!persistentBirthInputs.Contains(saved)))throw new Exception("同梱データは直接変更できません。個体を保存してから変更してください。");string pendingName=routeNameInput.Text,name=applyName?pendingName.Trim():routeSelectedRecord.Name??"";if(name.Length>20)throw new Exception("名前は20文字以内で入力してください。");bool keepDopingDraft=!applyDoping&&routeDopingMemo.HasEdits;var pendingDoping=keepDopingDraft?routeDopingMemo.Notes:null;var doping=applyDoping&&routeDopingMemo.HasEdits?routeDopingMemo.Notes:DopingNotes.Copy(routeSelectedRecord.DopingMemo);var row=choice.Row;BirthParent original=row.Candidate;if(original==null){if(row.Raw==null||row.Raw[0x94]<0)throw new Exception("変更できる個体データがありません。");original=SavedParentsForBirth(new BirthSnapshot{Label="現在表示中の結果",Rows=new[]{row}})[0];}
        var tree=CopyEditedRoutePath(original.Lineage??Model.CaptureBirthParent(original),path,0,name,doping);var edited=new BirthParent{Name=path.Length==0?name:original.Name,Raw=path.Length==0?(int[])tree.Raw.Clone():(int[])original.Raw.Clone(),DopingMemo=DopingNotes.Copy(path.Length==0?tree.DopingMemo:original.DopingMemo),Source=original.Source,SourceId=original.SourceId,AncestryKnown=original.AncestryKnown,Lineage=tree};model.CheckBirthParent(edited);
        SetSavingBirthInput(true);try{if(saved!=null){var parents=await Task.Run(()=>ReplaceSavedRouteIndividual(saved,index,original,edited));saved.Parents=parents;var info=new FileInfo(SavedInputPath(saved.SavedFileName));saved.SavedLength=info.Length;saved.SavedWriteTicks=info.LastWriteTimeUtc.Ticks;}
            row.Candidate=edited;row.Raw=(int[])edited.Raw.Clone();row.Lineage=edited.Lineage;row.Parent1=edited.Name;
            if(saved==null&&source is string){var rare=currentDisplayedResults as IEnumerable<ForecastRow>;if(rare!=null){var target=rare.ElementAt(index);editedResultParents[target]=edited;target.Name=edited.Name;}}
            forecastGrid.Refresh();birthResultGrid.Refresh();RefreshLineageIndividuals();if(index>=0&&index<lineageIndividual.Items.Count)lineageIndividual.SelectedIndex=index;SelectRouteRecordPath(path);if(!applyName)routeNameInput.Text=pendingName;if(keepDopingDraft)routeDopingMemo.RestoreDraft(pendingDoping,routeSelectedRecord.Raw[0x82]+1,Math.Max(routeSelectedRecord.Raw[0x82],routeSelectedRecord.Raw[0x83])+1);
        }finally{SetSavingBirthInput(false);}
    }
    void BuildLineage(){
        lineagePage.Name="lineagePage";var layout=new TableLayoutPanel{Name="lineageLayout",Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));lineagePage.Controls.Add(layout);
        var sourceRow=new FlowLayoutPanel{Name="lineageSourceRow",Dock=DockStyle.Fill,WrapContents=false};sourceRow.Controls.Add(LabelFor("入力"));lineageSource.Name="lineageSource";lineageSource.Width=300;lineageSource.DropDownWidth=620;lineageSource.DropDownStyle=ComboBoxStyle.DropDownList;sourceRow.Controls.Add(lineageSource);
        sourceRow.Controls.Add(LabelFor("個体名"));lineageIndividual.Name="lineageIndividual";lineageIndividual.Width=210;lineageIndividual.DropDownWidth=620;lineageIndividual.DropDownStyle=ComboBoxStyle.DropDownList;sourceRow.Controls.Add(lineageIndividual);AddAutoDopingButton(sourceRow);
        var expand=new Button{Name="lineageExpandAll",Text="すべて展開",AutoSize=true};var collapse=new Button{Name="lineageCollapseAll",Text="折りたたむ",AutoSize=true};sourceRow.Controls.Add(expand);sourceRow.Controls.Add(collapse);expand.Click+=delegate{lineageTree.ExpandAll();};collapse.Click+=delegate{lineageTree.CollapseAll();};layout.Controls.Add(sourceRow,0,0);
        var split=new SplitContainer{Name="routeDiagramSplit",Size=new Size(1000,650),Dock=DockStyle.Fill,Orientation=Orientation.Horizontal,FixedPanel=FixedPanel.Panel2,SplitterWidth=6,Panel1MinSize=170,Panel2MinSize=64,SplitterDistance=472};split.Panel2.AutoScroll=true;split.Panel2.AutoScrollMinSize=new Size(0,172);layout.Controls.Add(split,0,1);
        var views=new TabControl{Name="lineageViews",Dock=DockStyle.Fill};TabPage diagramPage=new TabPage("図"),listPage=new TabPage("一覧");views.TabPages.Add(diagramPage);views.TabPages.Add(listPage);split.Panel1.Controls.Add(views);
        var diagramLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};diagramLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,72));diagramLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));diagramPage.Controls.Add(diagramLayout);
        var toolRows=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};toolRows.RowStyles.Add(new RowStyle(SizeType.Absolute,34));toolRows.RowStyles.Add(new RowStyle(SizeType.Absolute,36));diagramLayout.Controls.Add(toolRows,0,0);
        var generationRow=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};generationRow.Controls.Add(LabelFor("表示世代"));toolRows.Controls.Add(generationRow,0,0);
        lineageDiagram=new LineageDiagram(DiagramCaption,DescribeLineage,new LineagePortraitRenderer(Path.Combine(model.DataDirectory,"portraits")));diagramLayout.Controls.Add(lineageDiagram,0,1);lineageDiagram.SelectedRecordChanged+=delegate{SelectRouteEditRecord(lineageDiagram.SelectedRecord);};
        for(int generation=9;generation>=1;generation--){var check=new CheckBox{Name="lineageShowGeneration"+generation,Text=generation+"世代",AutoSize=true,Checked=true,Margin=new Padding(2,6,2,2)};lineageGenerations[generation]=check;generationRow.Controls.Add(check);check.CheckedChanged+=delegate{if(!changingLineageGenerations)RenderLineageDiagram();};}
        var showAll=new Button{Name="lineageShowAllGenerations",Text="全世代",AutoSize=true};generationRow.Controls.Add(showAll);showAll.Click+=delegate{changingLineageGenerations=true;try{for(int g=1;g<=9;g++)lineageGenerations[g].Checked=true;}finally{changingLineageGenerations=false;}RenderLineageDiagram();};
        var recent=new Button{Name="lineageRecentGenerations",Text="直近3世代",AutoSize=true};generationRow.Controls.Add(recent);recent.Click+=delegate{int top=displayedLineageRecord!=null&&displayedLineageRecord.Raw!=null?displayedLineageRecord.Raw[0x82]+1:9;changingLineageGenerations=true;try{for(int g=1;g<=9;g++)lineageGenerations[g].Checked=g<=top&&g>=top-2;}finally{changingLineageGenerations=false;}RenderLineageDiagram();FitLineageDiagram();};
        var zoomRow=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};toolRows.Controls.Add(zoomRow,0,1);zoomRow.Controls.Add(LabelFor("倍率"));
        var smaller=new Button{Name="lineageZoomOut",Text="－",Width=34};var larger=new Button{Name="lineageZoomIn",Text="＋",Width=34};zoomRow.Controls.Add(smaller);zoomRow.Controls.Add(larger);
        lineageZoom.Name="lineageZoom";lineageZoom.Width=74;lineageZoom.Minimum=1;lineageZoom.Maximum=400;lineageZoom.DecimalPlaces=1;lineageZoom.Increment=10;lineageZoom.Value=100;zoomRow.Controls.Add(lineageZoom);zoomRow.Controls.Add(LabelFor("%"));lineageZoom.ValueChanged+=delegate{if(!changingLineageZoom)lineageDiagram.SetZoom((double)lineageZoom.Value);};
        lineageDiagram.ZoomChanged+=delegate{decimal value=(decimal)Math.Round(lineageDiagram.ZoomPercent,1);value=Math.Max(lineageZoom.Minimum,Math.Min(lineageZoom.Maximum,value));changingLineageZoom=true;try{if(lineageZoom.Value!=value)lineageZoom.Value=value;}finally{changingLineageZoom=false;}};smaller.Click+=delegate{lineageDiagram.SetZoom(lineageDiagram.ZoomPercent/1.25);};larger.Click+=delegate{lineageDiagram.SetZoom(lineageDiagram.ZoomPercent*1.25);};
        var actual=new Button{Name="lineageActualSize",Text="100%",AutoSize=true};zoomRow.Controls.Add(actual);actual.Click+=delegate{lineageDiagram.SetZoom(100);};var fit=new Button{Name="lineageFitDiagram",Text="全体表示",AutoSize=true};zoomRow.Controls.Add(fit);fit.Click+=delegate{FitLineageDiagram();};zoomRow.Controls.Add(LabelFor("プレビューは色1のみ反映、柄は反映しません。"));
        lineageTree.Name="lineageTree";lineageTree.Dock=DockStyle.Fill;lineageTree.HideSelection=false;lineageTree.FullRowSelect=true;lineageTree.ShowNodeToolTips=true;lineageTree.ItemHeight=29;listPage.Controls.Add(lineageTree);views.SelectedIndexChanged+=delegate{expand.Visible=collapse.Visible=views.SelectedTab==listPage;};expand.Visible=collapse.Visible=false;
        BuildRouteEditFields(split.Panel2);lineageStatus.Name="lineageStatus";lineageStatus.Visible=false;
        lineageSource.SelectedIndexChanged+=async delegate{if(!updatingLineage)try{await LoadLineageSourceAsync();}catch(Exception error){ShowError(error);}};lineageIndividual.SelectedIndexChanged+=delegate{if(!updatingLineage)RenderLineage();};lineageTree.AfterSelect+=delegate(object sender,TreeViewEventArgs e){SelectRouteEditRecord(e.Node.Tag as BirthLineage);};
    }
    public void ShowLineage(){HideManualDopingMemo();lineagePage.Visible=true;identify.Visible=predict.Visible=gekiPage.Visible=birthPage.Visible=false;lineagePage.BringToFront();lineageNav.BackColor=Color.LightBlue;identifyNav.BackColor=predictNav.BackColor=gekiNav.BackColor=birthNav.BackColor=SystemColors.Control;RefreshLineageSources();}
    public void ShowCurrentBirthLineage(){int index=birthResultGrid.CurrentRow==null?0:birthResultGrid.CurrentRow.Index;ShowLineage();lineageSource.SelectedIndex=0;RefreshLineageIndividuals();if(index>=0&&index<lineageIndividual.Items.Count)lineageIndividual.SelectedIndex=index;}
    BirthResult[] loadedRouteRows=new BirthResult[0];int routeLoadVersion;
    async void RefreshLineageSources(){var previous=lineageSource.SelectedItem;updatingLineage=true;try{lineageSource.Items.Clear();lineageSource.Items.Add("現在表示中の結果");foreach(var snapshot in birthSnapshots)lineageSource.Items.Add(snapshot);foreach(var saved in birthBatches[0].Items)lineageSource.Items.Add(saved);lineageSource.SelectedIndex=previous!=null&&lineageSource.Items.Contains(previous)?lineageSource.Items.IndexOf(previous):0;}finally{updatingLineage=false;}try{await LoadLineageSourceAsync();}catch(Exception error){ShowError(error);}}
    public async Task LoadLineageSourceAsync(){int version=++routeLoadVersion;object selected=lineageSource.SelectedItem;SelectRouteEditRecord(null);BirthResult[] rows;var saved=selected as ForecastSnapshot;var history=selected as BirthSnapshot;if(saved!=null){await LoadBirthInputsAsync(saved);if(version!=routeLoadVersion||!Object.ReferenceEquals(lineageSource.SelectedItem,selected))return;rows=BirthParentsForBatch(saved).Select(p=>BirthRowForParent(p,false)).ToArray();}else rows=history!=null?history.Rows:CurrentResultRows();if(version!=routeLoadVersion)return;loadedRouteSource=selected;loadedRouteResultSource=currentDisplayedResults;loadedRouteRows=rows;RefreshLineageIndividuals();}
    void RefreshLineageIndividuals(){
        var previous=lineageIndividual.SelectedItem as LineageChoice;var rows=loadedRouteRows;
        updatingLineage=true;lineageIndividual.BeginUpdate();try{lineageIndividual.Items.Clear();int index=0;foreach(var row in rows){lineageIndividual.Items.Add(new LineageChoice{Row=row,Label="結果"+(++index)+"："+row.Name+"／"+(row.Raw==null?"":"第"+row.Generation+"世代／"+row.Antenna+"／"+row.Color1+"・"+row.Color2)});}int selected=previous==null?-1:lineageIndividual.Items.Cast<LineageChoice>().ToList().FindIndex(c=>Object.ReferenceEquals(c.Row,previous.Row));lineageIndividual.SelectedIndex=selected>=0?selected:lineageIndividual.Items.Count>0?0:-1;}finally{lineageIndividual.EndUpdate();updatingLineage=false;}RenderLineage();
    }
    void RenderLineage(){
        lineageTree.BeginUpdate();try{lineageTree.Nodes.Clear();lineageDetails.Clear();displayedLineageRecord=null;SelectRouteEditRecord(null);var choice=lineageIndividual.SelectedItem as LineageChoice;if(choice==null){RenderLineageDiagram();return;}
            var row=choice.Row;var record=row.Candidate!=null?Model.CaptureBirthParent(row.Candidate):row.Lineage;var snapshot=lineageSource.SelectedItem as BirthSnapshot;
            if(record==null)record=new BirthLineage{Name="",Source="来歴未記録",Kind="未記録",Note="この結果の作成元は記録されていません。",Raw=row.Raw==null?null:(int[])row.Raw.Clone()};
            var saved=lineageSource.SelectedItem as ForecastSnapshot;var rootRecord=new BirthLineage{Name=record.Name,Source=(snapshot!=null?snapshot.Label:saved!=null?saved.Label:"現在の結果")+"／"+record.Source,Kind=record.Kind,Note=record.Note,Raw=record.Raw,DopingMemo=DopingNotes.Copy(record.DopingMemo),ParentA=record.ParentA,ParentB=record.ParentB};
            displayedLineageRecord=rootRecord;var root=BuildLineageNode(rootRecord,"対象",new HashSet<BirthLineage>(),0);lineageTree.Nodes.Add(root);root.Expand();foreach(TreeNode parent in root.Nodes)parent.Expand();lineageTree.SelectedNode=root;lineageTree.TopNode=root;RenderLineageDiagram();FitLineageDiagram();SelectRouteEditRecord(rootRecord);
        }finally{lineageTree.EndUpdate();}
    }
    string DiagramCaption(BirthLineage record){string name=record.Name??"";var raw=record.Raw;if(raw==null)return "世代不明\n"+name+"\n"+(record.Kind??"未記録");return "第"+(raw[0x82]+1)+"世代　"+name+"\n"+(raw[0x94]<0?"アンテナ未予測":model.BirthAntennaText(raw[0x94],raw[0x96]))+"\n"+Model.BodyText(raw[0x6d])+"／"+model.ColorText(raw[0x71])+"・"+model.ColorText(raw[0x72]);}
    void FitLineageDiagram(){lineageZoom.Value=(decimal)Math.Round(lineageDiagram.FitPercent(),1);lineageDiagram.SetZoom((double)lineageZoom.Value);lineageDiagram.FitVerticalSpacing();}
    void RenderLineageDiagram(){if(lineageDiagram==null)return;bool fit=lineageDiagram.IsOverview;var visible=new bool[10];for(int g=1;g<=9;g++)visible[g]=lineageGenerations[g]!=null&&lineageGenerations[g].Checked;lineageDiagram.SetLineage(displayedLineageRecord,visible);if(fit)FitLineageDiagram();lineageStatus.Text="ドラッグ：縦横移動 ／ 二本指・Shift＋ホイール：横移動 ／ Ctrl＋ホイール・ピンチ：拡大縮小。顔は体色1・濃淡なし・柄なし。破線は世代省略。"+(lineageDiagram.Limited?" 表示上限・循環を検出したため一部を省略しています。":"");}
    TreeNode BuildLineageNode(BirthLineage record,string relation,HashSet<BirthLineage> path,int depth){
        string name=record.Name??"";string source=record.Source??"";string shortSource=source.Length>90?source.Substring(0,90)+"…":source;
        var node=new TreeNode(relation+"："+name+" ［"+record.Kind+"］"+(record.Raw==null?"":"／第"+(record.Raw[0x82]+1)+"世代／"+Model.BodyText(record.Raw[0x6d])+"／"+model.ColorText(record.Raw[0x71])+"・"+model.ColorText(record.Raw[0x72]))) {Tag=record,ToolTipText=LineageParentOrigin(record)};
        node.Text+="／"+shortSource;if(depth>=20||!path.Add(record)){node.Nodes.Add("これ以上は展開できません。");return node;}if(record.ParentA!=null)node.Nodes.Add(BuildLineageNode(record.ParentA,"親1",path,depth+1));if(record.ParentB!=null)node.Nodes.Add(BuildLineageNode(record.ParentB,"親2",path,depth+1));path.Remove(record);return node;
    }
    string DescribeLineage(BirthLineage record){
        var lines=new List<string>{"名前："+(record.Name??"")+"　区分："+record.Kind};int[] f=record.Raw;
        if(f!=null){lines.Add("第"+(f[0x82]+1)+"世代／アンテナ："+(f[0x94]<0?"未予測":model.BirthAntennaText(f[0x94],f[0x96]))+"／体格："+Model.BodyText(f[0x6d])+"／色1："+model.ColorText(f[0x71])+"／色2："+model.ColorText(f[0x72])+"／柄："+model.PatternText(f[0x70])+"／頭："+model.HeadText(f[0x64]));int[] offsets={0x65,0x66,0x67,0x68,0x69,0x6a,0x6b,0x73,0x74};lines.Add(String.Join("　",new[]{1,0,2,3,4,5,6,7,8}.Select(i=>Model.FaceNames[i]+"："+f[offsets[i]].ToString("D2"))));}
        return String.Join("\r\n",lines);
    }
}
}
