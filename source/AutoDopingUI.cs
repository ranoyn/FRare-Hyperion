using System;
using System.Linq;
using System.Drawing;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace FreeNormalFinder {
public sealed partial class ToolForm {
    Form autoDopingWindow;Action<BirthLineage> autoDopingRouteSync;
    sealed class AutoDopingChoice {public BirthLineage Record;public string Path;public int Number;public override string ToString(){return String.IsNullOrWhiteSpace(Record.Name)?"名前なし（"+Number+"）":Record.Name;}}
    void AddAutoDopingButton(FlowLayoutPanel row){var button=new Button{Name="routeAutoDoping",Text="ドーピングを自動計算する",AutoSize=true};row.Controls.Add(button);button.Click+=delegate{try{ShowAutoDopingWindow();}catch(Exception error){ShowError(error);}};}
    public void ShowAutoDopingWindow(){
        if(displayedLineageRecord==null)throw new Exception("ルートを表示してから自動計算してください。");
        if(autoDopingWindow!=null&&!autoDopingWindow.IsDisposed){if(autoDopingRouteSync!=null)autoDopingRouteSync(routeSelectedRecord);autoDopingWindow.Show();autoDopingWindow.BringToFront();return;}
        var window=new Form{Name="autoDopingWindow",Text="ドーピングを自動計算する",ClientSize=new Size(760,224),StartPosition=FormStartPosition.Manual,Font=Font,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,ShowInTaskbar=false};
        autoDopingWindow=window;var area=Screen.FromControl(this).WorkingArea;window.Location=new Point(Math.Min(area.Right-window.Width,Math.Max(area.Left,Right-window.Width)),Math.Max(area.Top,Top+90));
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(10),ColumnCount=1,RowCount=4};foreach(int h in new[]{62,62,36,44})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));window.Controls.Add(layout);
        var selectorRow=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};var generations=new ComboBox{Name="autoDopingGeneration",Width=105,DropDownStyle=ComboBoxStyle.DropDownList};var individual=new ComboBox{Name="autoDopingIndividual",Width=590,DropDownWidth=650,DropDownStyle=ComboBoxStyle.DropDownList};ManualField(selectorRow,"世代",generations);ManualField(selectorRow,"個体名",individual);layout.Controls.Add(selectorRow,0,0);
        var targets=new NumericUpDown[5];string[] names={"HP","AP","こうげきりょく","ぼうぎょりょく","すばやさ"};var targetRow=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};for(int i=0;i<5;i++){targets[i]=new NumericUpDown{Name="autoDopingTarget"+i,Width=135,Minimum=0,Maximum=303};ManualField(targetRow,names[i]+"（＋）",targets[i]);}layout.Controls.Add(targetRow,0,1);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};var run=new Button{Name="autoDopingRun",Text="検索実行",AutoSize=true};var apply=new Button{Name="autoDopingApply",Text="反映",AutoSize=true,Enabled=false};actions.Controls.AddRange(new Control[]{run,apply});layout.Controls.Add(actions,0,2);
        var status=new Label{Name="autoDopingStatus",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};layout.Controls.Add(status,0,3);var tooltip=new ToolTip();
        var choices=new List<AutoDopingChoice>();BirthLineage rootSnapshot=null,plannedRecord=null;AutoDopingPlan plan=null;CancellationTokenSource cancellation=null;bool syncing=false,running=false,applying=false,closed=false;int revision=0;
        Action invalidate=delegate{revision++;plan=null;plannedRecord=null;apply.Enabled=false;status.Text="";tooltip.SetToolTip(status,"");if(cancellation!=null)cancellation.Cancel();};
        Action fillIndividuals=delegate{individual.Items.Clear();if(generations.SelectedItem!=null)foreach(var choice in choices.Where(c=>c.Record.Raw[0x82]+1==(int)generations.SelectedItem))individual.Items.Add(choice);};
        Action<BirthLineage> sync=delegate(BirthLineage selected){
            if(closed||syncing)return;syncing=true;try{
                if(!Object.ReferenceEquals(rootSnapshot,displayedLineageRecord)){invalidate();rootSnapshot=displayedLineageRecord;choices.Clear();generations.Items.Clear();individual.Items.Clear();if(rootSnapshot!=null){CollectAutoDopingChoices(rootSnapshot,"",choices,new HashSet<BirthLineage>(),0);foreach(var g in choices.Select(c=>c.Record.Raw[0x82]+1).Distinct().OrderByDescending(g=>g))generations.Items.Add(g);}}
                var choice=choices.FirstOrDefault(c=>Object.ReferenceEquals(c.Record,selected));if(choice==null){if(selected==null){invalidate();individual.SelectedIndex=-1;}run.Enabled=!running&&!applying&&individual.SelectedItem!=null;return;}
                if(!Object.ReferenceEquals(individual.SelectedItem,choice)){invalidate();generations.SelectedItem=choice.Record.Raw[0x82]+1;fillIndividuals();individual.SelectedItem=choice;}
                run.Enabled=!running&&!applying&&individual.SelectedItem!=null;
            }finally{syncing=false;}
        };
        autoDopingRouteSync=sync;
        Action selectDiagram=delegate{if(syncing||closed)return;invalidate();var choice=individual.SelectedItem as AutoDopingChoice;if(choice!=null){syncing=true;try{SelectAutoDopingRoute(choice.Record);}finally{syncing=false;}}run.Enabled=!running&&!applying&&choice!=null;};
        generations.SelectedIndexChanged+=delegate{if(syncing)return;syncing=true;try{fillIndividuals();if(individual.Items.Count>0)individual.SelectedIndex=0;}finally{syncing=false;}selectDiagram();};
        individual.SelectedIndexChanged+=delegate{selectDiagram();};foreach(var input in targets)input.ValueChanged+=delegate{invalidate();};
        sync(routeSelectedRecord??displayedLineageRecord);if(generations.SelectedItem!=null&&(int)generations.SelectedItem==9){int[] initial={51,69,51,36,138};for(int i=0;i<5;i++)targets[i].Value=initial[i];}
        run.Click+=async delegate{
            if(running||applying||individual.SelectedItem==null)return;invalidate();var chosen=(AutoDopingChoice)individual.SelectedItem;int[] goal=targets.Select(t=>(int)t.Value).ToArray();int generation=(int)generations.SelectedItem,version=revision;var cts=new CancellationTokenSource();cancellation=cts;running=true;run.Enabled=false;status.Text="計算中…";
            try{var result=await Task.Run(()=>new AutoDopingPlanner(chosen.Record,generation,goal,cts.Token).Run());if(closed||version!=revision)return;
                if(result.Found){plan=result;plannedRecord=chosen.Record;apply.Enabled=true;status.Text="ルートが見つかりました。反映を押してドーピングを反映してください。";}
                else {status.Text="このルート情報では指定のドーピングは計算できません。";tooltip.SetToolTip(status,result.Message);}
            }catch(OperationCanceledException){if(!closed&&version==revision)status.Text="";}catch(Exception error){if(!closed&&version==revision){status.Text="このルート情報では指定のドーピングは計算できません。";tooltip.SetToolTip(status,error.Message);}}
            finally{if(Object.ReferenceEquals(cancellation,cts))cancellation=null;cts.Dispose();running=false;if(!closed)run.Enabled=!applying&&individual.SelectedItem!=null;}
        };
        apply.Click+=async delegate{
            if(plan==null||plannedRecord==null||running||applying)return;var chosen=(AutoDopingChoice)individual.SelectedItem;if(chosen==null||!Object.ReferenceEquals(chosen.Record,plannedRecord))return;applying=true;apply.Enabled=run.Enabled=false;var selectedPlan=plan;generations.Enabled=individual.Enabled=false;foreach(var input in targets)input.Enabled=false;
            try{await ApplyAutoDopingPlanAsync(plannedRecord,selectedPlan);if(!closed){invalidate();status.Text="ドーピングを反映しました。";}}
            catch(Exception error){if(!closed){status.Text="反映できませんでした。";tooltip.SetToolTip(status,error.Message);ShowError(error);}}
            finally{applying=false;if(!closed){generations.Enabled=individual.Enabled=true;foreach(var input in targets)input.Enabled=true;run.Enabled=!running&&individual.SelectedItem!=null;apply.Enabled=plan!=null;}}
        };
        window.FormClosed+=delegate{closed=true;if(cancellation!=null)cancellation.Cancel();if(Object.ReferenceEquals(autoDopingWindow,window)){autoDopingWindow=null;autoDopingRouteSync=null;}tooltip.Dispose();};window.Show(this);
    }
    void SelectAutoDopingRoute(BirthLineage record){
        string path=RouteRecordPath(displayedLineageRecord,record,0);if(path==null)return;int g=record.Raw[0x82]+1;
        if(g>=1&&g<=9&&!lineageGenerations[g].Checked){lineageGenerations[g].Checked=true;}
        lineageDiagram.SelectRecord(record);lineageDiagram.RevealSelectedRecord();
        Action<TreeNodeCollection> select=null;select=nodes=>{foreach(TreeNode node in nodes){if(Object.ReferenceEquals(node.Tag,record)){lineageTree.SelectedNode=node;node.EnsureVisible();return;}select(node.Nodes);}};select(lineageTree.Nodes);SelectRouteEditRecord(record);
    }
    void CollectAutoDopingChoices(BirthLineage record,string path,List<AutoDopingChoice> list,HashSet<BirthLineage> chain,int depth){
        if(record==null)return;if(record.Raw==null||record.Raw.Length!=252||depth>20||list.Count>=4095||!chain.Add(record))throw new Exception("ルートの祖先情報を確認できません。");
        if(record.Raw[0x82]>=0&&record.Raw[0x82]<9)list.Add(new AutoDopingChoice{Record=record,Path=path,Number=list.Count(c=>c.Record.Raw[0x82]==record.Raw[0x82])+1});CollectAutoDopingChoices(record.ParentA,path+"A",list,chain,depth+1);CollectAutoDopingChoices(record.ParentB,path+"B",list,chain,depth+1);chain.Remove(record);
    }
    BirthLineage CopyAutoDopingTree(BirthLineage record,string path,int depth,BirthLineage planned){
        if(record==null)throw new Exception("対象のルートが変更されました。");if(depth==path.Length)return MergeAutoDopingNotes(record,planned);
        return new BirthLineage{Name=record.Name,Source=record.Source,Kind=record.Kind,Note=record.Note,Raw=record.Raw,DopingMemo=DopingNotes.Copy(record.DopingMemo),ParentA=path[depth]=='A'?CopyAutoDopingTree(record.ParentA,path,depth+1,planned):record.ParentA,ParentB=path[depth]=='B'?CopyAutoDopingTree(record.ParentB,path,depth+1,planned):record.ParentB};
    }
    BirthLineage MergeAutoDopingNotes(BirthLineage original,BirthLineage planned){
        if(original==null&&planned==null)return null;
        if(original==null||planned==null||original.Raw==null||planned.Raw==null||!original.Raw.SequenceEqual(planned.Raw))throw new Exception("対象のルートが変更されました。");
        DopingNotes.Validate(planned.DopingMemo,original.Raw[0x82]+1,Math.Max(original.Raw[0x82],original.Raw[0x83])+1);
        return new BirthLineage{Name=original.Name,Source=original.Source,Kind=original.Kind,Note=original.Note,Raw=original.Raw,DopingMemo=DopingNotes.Copy(planned.DopingMemo),ParentA=MergeAutoDopingNotes(original.ParentA,planned.ParentA),ParentB=MergeAutoDopingNotes(original.ParentB,planned.ParentB)};
    }
    public async Task ApplyAutoDopingPlanAsync(BirthLineage expected,AutoDopingPlan plan){
        CheckIndividualRegistrationReady();var choice=lineageIndividual.SelectedItem as LineageChoice;int index=lineageIndividual.SelectedIndex;object source=lineageSource.SelectedItem;var saved=source as ForecastSnapshot;string path=RouteRecordPath(displayedLineageRecord,expected,0);
        if(plan==null||!plan.Found||plan.PlannedRoot==null||!Object.ReferenceEquals(plan.SourceRoot,expected)||choice==null||path==null||!Object.ReferenceEquals(routeSelectedRecord,expected)||!Object.Equals(loadedRouteSource,source))throw new Exception("個体の選択が変わりました。もう一度検索してください。");
        if(source is string&&!Object.ReferenceEquals(loadedRouteResultSource,currentDisplayedResults))throw new Exception("現在の結果が更新されました。表示元を選び直してください。");
        if(saved!=null&&(saved.BuiltIn||!persistentBirthInputs.Contains(saved)))throw new Exception("同梱データは直接変更できません。個体を保存してから反映してください。");
        if(!DopingNotes.Calculate(plan.PlannedRoot,expected.Raw[0x82]+1).Total.SequenceEqual(plan.Totals))throw new Exception("配分案の照合に失敗しました。");
        var row=choice.Row;BirthParent original=row.Candidate;if(original==null){if(row.Raw==null||row.Raw[0x94]<0)throw new Exception("反映できる個体データがありません。");original=SavedParentsForBirth(new BirthSnapshot{Label="現在表示中の結果",Rows=new[]{row}})[0];}
        string pendingName=routeNameInput.Text;var tree=CopyAutoDopingTree(original.Lineage??Model.CaptureBirthParent(original),path,0,plan.PlannedRoot);
        var edited=new BirthParent{Name=original.Name,Raw=(int[])original.Raw.Clone(),DopingMemo=DopingNotes.Copy(tree.DopingMemo),Source=original.Source,SourceId=original.SourceId,AncestryKnown=original.AncestryKnown,Lineage=tree};model.CheckBirthParent(edited);
        SetSavingBirthInput(true);try{
            if(saved!=null){var parents=await Task.Run(()=>ReplaceSavedRouteIndividual(saved,index,original,edited));saved.Parents=parents;var info=new FileInfo(SavedInputPath(saved.SavedFileName));saved.SavedLength=info.Length;saved.SavedWriteTicks=info.LastWriteTimeUtc.Ticks;}
            row.Candidate=edited;row.Raw=(int[])edited.Raw.Clone();row.Lineage=tree;row.Parent1=edited.Name;
            if(saved==null&&source is string){var rare=currentDisplayedResults as IEnumerable<ForecastRow>;if(rare!=null){var target=rare.ElementAt(index);editedResultParents[target]=edited;}}
            forecastGrid.Refresh();birthResultGrid.Refresh();RefreshLineageIndividuals();if(index>=0&&index<lineageIndividual.Items.Count)lineageIndividual.SelectedIndex=index;SelectAutoDopingRoute(RouteRecordAt(displayedLineageRecord,path));routeNameInput.Text=pendingName;
        }finally{SetSavingBirthInput(false);}
    }
}
}
