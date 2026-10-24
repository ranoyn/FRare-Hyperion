using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Globalization;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FreeNormalFinder {
public sealed partial class ToolForm {
    readonly Panel gekiPage=new Panel();readonly Button gekiNav=new Button();
    readonly TextBox gekiStart=new TextBox(),gekiEnd=new TextBox(),gekiName=new TextBox(),gekiInspectSeed=new TextBox();
    readonly ComboBox gekiAntenna=new ComboBox(),gekiHead=new ComboBox(),gekiColor1=new ComboBox(),gekiColor2=new ComboBox(),gekiPattern=new ComboBox(),gekiBody=new ComboBox();
    readonly ComboBox[] gekiFace=new ComboBox[9];readonly CheckBox gekiReverse=new CheckBox();
    readonly Button gekiRun=new Button(),gekiStop=new Button(),gekiReset=new Button(),gekiCsv=new Button(),gekiAll=new Button(),gekiInspect=new Button();
    readonly NumericUpDown gekiLimit=new NumericUpDown();readonly DataGridView gekiResults=new DataGridView(),gekiLineup=new DataGridView();
    readonly Label gekiStatus=new Label(),gekiLineupTitle=new Label();readonly ProgressBar gekiBar=new ProgressBar();
    readonly Panel gekiConditions=new Panel();readonly System.Windows.Forms.Timer gekiTimer=new System.Windows.Forms.Timer();
    GekiEngine gekiEngine;CancellationTokenSource gekiCancel;bool gekiRunning,gekiCloseAfter;uint? gekiShownSeed;
    volatile GekiProgress gekiProgress;List<GekiResultRow> gekiRows=new List<GekiResultRow>();

    public void ShowGeki(){HideManualDopingMemo();gekiPage.Visible=true;identify.Visible=predict.Visible=birthPage.Visible=lineagePage.Visible=false;gekiPage.BringToFront();gekiNav.BackColor=Color.LightBlue;identifyNav.BackColor=predictNav.BackColor=birthNav.BackColor=lineageNav.BackColor=SystemColors.Control;try{EnsureGeki();}catch(Exception e){gekiStatus.Text="ゲキレア用データを読めません："+e.Message;}}
    static uint ParseGekiSeed(string text){uint value;text=(text??"").Trim();if(text.StartsWith("0x",StringComparison.OrdinalIgnoreCase))text=text.Substring(2);if(text.Length!=8||!UInt32.TryParse(text,NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out value))throw new ArgumentException("生成seedは16進数8桁で入力してください（00000000～FFFFFFFF）。");return value;}
    ComboBox ConfigureGekiChoice(ComboBox box,string name,Choice[] choices,int width){box.Name=name;box.Width=width;box.DropDownStyle=ComboBoxStyle.DropDownList;box.DisplayMember="Text";box.ValueMember="Value";box.BindingContext=new BindingContext();box.DataSource=choices;box.SelectedValue=-1;return box;}
    static void GekiField(Control parent,string title,Control control){parent.Controls.Add(LabelFor(title));parent.Controls.Add(control);}
    void BuildGeki(){
        gekiPage.Name="gekiPage";
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=8};foreach(int h in new[]{44,44,40,62,44,24,0,28})layout.RowStyles.Add(h==0?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.Absolute,h));gekiPage.Controls.Add(layout);
        layout.Controls.Add(new Label{Name="gekiIntroduction",Dock=DockStyle.Fill,Text="ゲキレアキャッチ乱数調整用ツールです。未開拓のため使用法が確立されていません。"},0,0);
        var range=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};layout.Controls.Add(range,0,1);
        gekiStart.Name="gekiSeedStart";gekiEnd.Name="gekiSeedEnd";foreach(var box in new[]{gekiStart,gekiEnd,gekiInspectSeed}){box.Width=105;box.MaxLength=10;box.Font=new Font("Consolas",11);}
        gekiStart.Text="00000000";gekiEnd.Text="0000FFFF";GekiField(range,"開始seed",gekiStart);GekiField(range,"～ 終了seed",gekiEnd);
        gekiAll.Name="gekiFullSeedRange";gekiAll.Text="全seed範囲";gekiAll.AutoSize=true;range.Controls.Add(gekiAll);gekiAll.Click+=delegate{gekiStart.Text="00000000";gekiEnd.Text="FFFFFFFF";};
        gekiLimit.Name="gekiResultLimit";gekiLimit.Minimum=10;gekiLimit.Maximum=100000;gekiLimit.Value=10000;gekiLimit.Increment=1000;gekiLimit.Width=85;GekiField(range,"表示上限（件）",gekiLimit);
        gekiConditions.Dock=DockStyle.Fill;layout.Controls.Add(gekiConditions,0,2);var traits=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};gekiConditions.Controls.Add(traits);
        gekiAntenna.Name="gekiAntenna";gekiAntenna.Width=150;gekiAntenna.DropDownStyle=ComboBoxStyle.DropDownList;gekiAntenna.Items.Add("未指定");gekiAntenna.SelectedIndex=0;GekiField(traits,"アンテナ",gekiAntenna);
        GekiField(traits,"頭",ConfigureGekiChoice(gekiHead,"gekiHead",model.Heads(),110));
        GekiField(traits,"色1",ConfigureGekiChoice(gekiColor1,"gekiColor1",model.Colors(),90));GekiField(traits,"色2",ConfigureGekiChoice(gekiColor2,"gekiColor2",model.Colors(),90));
        gekiReverse.Name="gekiReverseColors";gekiReverse.Text="逆順を許可";gekiReverse.Checked=true;gekiReverse.AutoSize=true;gekiReverse.Padding=new Padding(0,5,0,0);traits.Controls.Add(gekiReverse);
        GekiField(traits,"柄",ConfigureGekiChoice(gekiPattern,"gekiPattern",model.Patterns(true),90));GekiField(traits,"体格",ConfigureGekiChoice(gekiBody,"gekiBody",model.Bodies(),88));
        gekiName.Name="gekiName";gekiName.Width=85;gekiName.AutoCompleteMode=AutoCompleteMode.SuggestAppend;gekiName.AutoCompleteSource=AutoCompleteSource.CustomSource;var completion=new AutoCompleteStringCollection();completion.AddRange(model.Data.names);gekiName.AutoCompleteCustomSource=completion;GekiField(traits,"名前",gekiName);foreach(Label label in traits.Controls.OfType<Label>())label.Margin=new Padding(1,3,1,3);
        bool sizingTraits=false;Action sizeTraits=delegate{if(sizingTraits)return;sizingTraits=true;try{int width=traits.Controls.Cast<Control>().Sum(c=>Math.Max(c.Width,c.AutoSize?c.PreferredSize.Width:c.Width)+c.Margin.Horizontal);bool wrap=traits.ClientSize.Width<width;if(traits.GetFlowBreak(gekiPattern)!=wrap)traits.SetFlowBreak(gekiPattern,wrap);float h=wrap?78:40;if(layout.RowStyles[2].Height!=h){layout.RowStyles[2].Height=h;if(layout.IsHandleCreated)layout.BeginInvoke((Action)(()=>{if(!layout.IsDisposed)layout.PerformLayout();}));}}finally{sizingTraits=false;}};traits.SizeChanged+=delegate{sizeTraits();};traits.Layout+=delegate{sizeTraits();};layout.SizeChanged+=delegate{sizeTraits();};sizeTraits();
        var faces=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=9,RowCount=1,Margin=Padding.Empty,Padding=new Padding(0,0,4,0)};faces.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.Controls.Add(faces,0,3);
        int[] order={1,0,2,3,4,5,6,7,8};foreach(int i in order){
            faces.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/9));var group=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,Margin=new Padding(2,0,4,0)};group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));group.RowStyles.Add(new RowStyle(SizeType.Absolute,22));group.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            group.Controls.Add(new Label{Text=Model.FaceNames[i],Dock=DockStyle.Fill,Margin=Padding.Empty,TextAlign=ContentAlignment.MiddleLeft},0,0);
            var box=ConfigureGekiChoice(new ComboBox(),"geki"+Model.FaceKeys[i],model.FaceChoices(i).Select(n=>new Choice{Value=n,Text=n<0?"未指定":n.ToString("D2")}).ToArray(),100);box.Dock=DockStyle.Top;box.Margin=new Padding(0,1,2,4);gekiFace[i]=box;group.Controls.Add(box,0,1);faces.Controls.Add(group,order.ToList().IndexOf(i),0);
        }
        // Contour remains independent: exceptional valid hair/contour pairs must stay searchable.
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};layout.Controls.Add(actions,0,4);
        gekiRun.Name="gekiSearchButton";gekiRun.Text="検索実行";gekiStop.Name="gekiStopButton";gekiStop.Text="中断";gekiStop.Enabled=false;gekiReset.Name="gekiResetButton";gekiReset.Text="リセット";gekiCsv.Name="gekiExportCsv";gekiCsv.Text="結果をCSV保存";gekiCsv.Enabled=false;
        foreach(var button in new[]{gekiRun,gekiStop,gekiReset,gekiCsv}){button.AutoSize=true;actions.Controls.Add(button);}
        gekiInspectSeed.Name="gekiInspectSeed";gekiInspectSeed.Text="00000000";GekiField(actions,"確認seed",gekiInspectSeed);gekiInspect.Name="gekiInspectButton";gekiInspect.Text="10人を表示";gekiInspect.AutoSize=true;actions.Controls.Add(gekiInspect);
        gekiRun.Click+=async delegate{await RunGekiSearch();};gekiStop.Click+=delegate{if(gekiCancel!=null){gekiCancel.Cancel();gekiStop.Enabled=false;gekiStatus.Text="中断処理中…";}};
        gekiReset.Click+=delegate{ResetGekiFilters();};gekiInspect.Click+=delegate{try{ShowGekiLineup(ParseGekiSeed(gekiInspectSeed.Text));}catch(Exception e){ShowError(e);}};
        gekiCsv.Click+=delegate{try{using(var dialog=new SaveFileDialog{Filter="CSV (*.csv)|*.csv",FileName="ゲキレア検索結果.csv"})if(dialog.ShowDialog(this)==DialogResult.OK)ExportGekiCsv(dialog.FileName);}catch(Exception e){ShowError(e);}};
        gekiBar.Name="gekiProgress";gekiBar.Maximum=1000;gekiBar.Dock=DockStyle.Fill;layout.Controls.Add(gekiBar,0,5);
        var split=new SplitContainer{Size=new Size(1000,400),Dock=DockStyle.Fill,Orientation=Orientation.Horizontal,Panel1MinSize=90,Panel2MinSize=90,SplitterDistance=210};layout.Controls.Add(split,0,6);
        ConfigureGekiGrid(gekiResults,"gekiResults");ConfigureGekiGrid(gekiLineup,"gekiLineup");split.Panel1.Controls.Add(gekiResults);split.Panel1.Controls.Add(new Label{Dock=DockStyle.Top,Height=23,Text="検索結果"});split.Panel2.Controls.Add(gekiLineup);gekiLineupTitle.Name="gekiLineupTitle";gekiLineupTitle.Dock=DockStyle.Top;gekiLineupTitle.Height=23;gekiLineupTitle.Text="選択seedの10人";split.Panel2.Controls.Add(gekiLineupTitle);
        gekiResults.SelectionChanged+=delegate{var row=gekiResults.CurrentRow==null?null:gekiResults.CurrentRow.DataBoundItem as GekiResultRow;if(row!=null&&gekiShownSeed!=row.Seed)try{ShowGekiLineup(row.Seed);}catch(Exception e){gekiShownSeed=null;gekiLineup.DataSource=null;gekiLineupTitle.Text=e.Message;}};
        gekiStatus.Name="gekiStatus";gekiStatus.Dock=DockStyle.Fill;gekiStatus.Text="";layout.Controls.Add(gekiStatus,0,7);
        gekiTimer.Interval=250;gekiTimer.Tick+=delegate{UpdateGekiProgress();};
        FormClosing+=delegate(object sender,FormClosingEventArgs e){if(gekiRunning){e.Cancel=true;gekiCloseAfter=true;gekiCancel.Cancel();}};
        FormClosed+=delegate{gekiTimer.Stop();gekiTimer.Dispose();if(gekiCancel!=null)gekiCancel.Dispose();};
    }
    void EnsureGeki(){if(gekiEngine!=null)return;var engine=new GekiEngine(model,Path.Combine(model.DataDirectory,"geki.json"));gekiAntenna.Items.AddRange(engine.Antennas().Cast<object>().ToArray());gekiEngine=engine;}
    void ConfigureGekiGrid(DataGridView grid,string name){
        grid.Name=name;grid.Dock=DockStyle.Fill;grid.ReadOnly=true;grid.AllowUserToAddRows=grid.AllowUserToDeleteRows=false;grid.RowHeadersVisible=false;grid.AutoGenerateColumns=false;grid.MultiSelect=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.BackgroundColor=Color.White;grid.ClipboardCopyMode=DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;grid.Font=new Font("Yu Gothic UI",9);grid.RowTemplate.Height=25;
        string[][] columns={new[]{"SeedHex","生成seed"},new[]{"Slot","生成順"},new[]{"Name","名前"},new[]{"Antenna","アンテナ"},new[]{"Head","頭"},new[]{"Color1","色1"},new[]{"Color2","色2"},new[]{"Pattern","柄"},new[]{"BodyText","体格"},new[]{"Generation","世代"},new[]{"Level","Lv"},new[]{"Hair","髪"},new[]{"FaceShape","輪郭"},new[]{"Brow","眉"},new[]{"Eye","目"},new[]{"Nose","鼻"},new[]{"Mouth","口"},new[]{"Cheek","頬"},new[]{"SkinColor","肌色"},new[]{"HairColor","髪色"}};
        foreach(var c in columns){var column=new DataGridViewTextBoxColumn{Name=c[0],DataPropertyName=c[0],HeaderText=c[1],Width=c[0]=="Antenna"?155:c[0]=="SeedHex"?90:Model.FaceKeys.Contains(c[0])?45:80,SortMode=DataGridViewColumnSortMode.NotSortable};if(Model.FaceKeys.Contains(c[0]))column.DefaultCellStyle.Format="D2";grid.Columns.Add(column);}
        InstallIndividualRegistration(grid);
    }
    GekiQuery ReadGekiQuery(){EnsureGeki();return new GekiQuery{Name=gekiName.Text,Antenna=gekiAntenna.SelectedIndex>0?gekiAntenna.Text:"",Head=Convert.ToInt32(gekiHead.SelectedValue),Color1=Convert.ToInt32(gekiColor1.SelectedValue),Color2=Convert.ToInt32(gekiColor2.SelectedValue),Pattern=Convert.ToInt32(gekiPattern.SelectedValue),Body=Convert.ToInt32(gekiBody.SelectedValue),AllowReverseColors=gekiReverse.Checked,MinGeneration=1,MaxGeneration=gekiEngine.MaxGeneration,Face=gekiFace.Select(c=>Convert.ToInt32(c.SelectedValue)).ToArray()};}
    public void ResetGekiFilters(){if(gekiRunning)return;gekiName.Clear();gekiAntenna.SelectedIndex=0;foreach(var c in new[]{gekiHead,gekiColor1,gekiColor2,gekiPattern,gekiBody}.Concat(gekiFace))c.SelectedValue=-1;gekiReverse.Checked=true;}
    void SetGekiControls(bool enabled){foreach(Control c in new Control[]{gekiStart,gekiEnd,gekiAll,gekiLimit,gekiRun,gekiReset,gekiInspect,gekiInspectSeed,gekiConditions})c.Enabled=enabled;foreach(var c in gekiFace)c.Enabled=enabled;gekiStop.Enabled=!enabled;gekiCsv.Enabled=enabled&&gekiRows.Count>0;}
    void UpdateGekiProgress(){var p=gekiProgress;if(p==null)return;gekiBar.Value=(int)Math.Min(1000,p.Processed*1000/p.Total);if(gekiCancel!=null&&gekiCancel.IsCancellationRequested)return;gekiStatus.Text="検索済み "+p.Processed.ToString("N0")+" / "+p.Total.ToString("N0")+" seed　一致 "+p.Matches.ToString("N0")+"人　判定保留 "+p.Inconclusive.ToString("N0")+" seed";}
    public async Task RunGekiSearch(){
        if(gekiRunning)return;
        try{
            EnsureGeki();uint start=ParseGekiSeed(gekiStart.Text),end=ParseGekiSeed(gekiEnd.Text);if(end<start)throw new ArgumentException("開始seed≦終了seedにしてください。");var query=gekiEngine.ValidateQuery(ReadGekiQuery());int limit=(int)gekiLimit.Value;
            gekiCancel=new CancellationTokenSource();gekiRunning=true;gekiProgress=null;gekiBar.Value=0;SetGekiControls(false);gekiStatus.Text="検索中…";gekiTimer.Start();
            var result=await Task.Run(()=>gekiEngine.Search(query,start,end,limit,gekiCancel.Token,p=>gekiProgress=p));
            if(IsDisposed)return;gekiRows=result.Rows;gekiResults.DataSource=gekiRows;UpdateGekiProgress();if(gekiRows.Count==0){gekiShownSeed=null;gekiLineup.DataSource=null;gekiLineupTitle.Text="一致した個体はありません。「確認seed」で10人を表示できます。";}
            string state=result.Complete?"範囲の検索完了":result.Canceled?"中断":"表示上限で停止";
            gekiStatus.Text=state+"：検索済み "+result.Processed.ToString("N0")+" seed、一致 "+result.Rows.Count.ToString("N0")+"人、判定保留 "+result.Inconclusive.ToString("N0")+" seed"+(result.Complete?"":"　続きの開始seed："+result.NextSeed.ToString("X8"));
        }catch(Exception e){if(!IsDisposed){gekiStatus.Text="検索できません（前の結果を保持）："+e.Message;ShowError(e);}}
        finally{gekiTimer.Stop();gekiRunning=false;if(gekiCancel!=null){gekiCancel.Dispose();gekiCancel=null;}if(!IsDisposed){SetGekiControls(true);if(gekiCloseAfter)Close();}}
    }
    public void ShowGekiLineup(uint seed){EnsureGeki();var people=gekiEngine.Forward(seed);var rows=people.Select(c=>gekiEngine.Describe(seed,c)).ToList();gekiLineup.DataSource=rows;gekiShownSeed=seed;gekiInspectSeed.Text=seed.ToString("X8");gekiLineupTitle.Text="生成seed "+seed.ToString("X8")+" の10人";}
    public void ExportGekiCsv(string path){
        if(gekiRows.Count==0)throw new Exception("保存する検索結果がありません。");
        var columns=gekiResults.Columns.Cast<DataGridViewColumn>().Where(c=>!(c is DataGridViewButtonColumn)).OrderBy(c=>c.DisplayIndex).ToArray();var text=new StringBuilder();text.AppendLine(String.Join(",",columns.Select(c=>GekiCsvCell(c.HeaderText))));
        foreach(var row in gekiRows)text.AppendLine(String.Join(",",columns.Select(c=>{var value=typeof(GekiResultRow).GetProperty(c.DataPropertyName).GetValue(row,null);return GekiCsvCell(value==null?"":Convert.ToString(value,CultureInfo.InvariantCulture));})));
        File.WriteAllText(path,text.ToString(),new UTF8Encoding(true));
    }
    static string GekiCsvCell(string value){if(value.Length>0&&"=+-@".IndexOf(value[0])>=0)value="'"+value;return "\""+value.Replace("\"","\"\"")+"\"";}
}
}
