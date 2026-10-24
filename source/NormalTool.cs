using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FreeNormalFinder {
public class ForecastRow {
    public Candidate ParentCandidate {get;set;}
    public string Date {get;set;}
    public string Slot {get;set;}
    public string Name {get;set;}
    public string Antenna {get;set;}
    public int Body {get;set;}
    public string BodyText {get{return Model.BodyText(Body);}}
    public string Head {get;set;}
    public string Color1 {get;set;}
    public string Color2 {get;set;}
    public string Pattern {get;set;}
    public int Hair {get;set;}
    public int FaceShape {get;set;}
    public int Brow {get;set;}
    public int Eye {get;set;}
    public int Nose {get;set;}
    public int Mouth {get;set;}
    public int Cheek {get;set;}
    public int SkinColor {get;set;}
    public int HairColor {get;set;}
    public int Generation {get;set;}
    public string GenerationText {get{return Generation+"～"+(ParentCandidate==null?Generation:Math.Max(Generation,ParentCandidate.GenerationLimit));}}
    public int Level {get;set;}
}
public class Preferences {public string Key="",Start="",End="";public int DateRangeVersion,GenerationFilterVersion;public bool AllowReverseColors=true,UseGenerationFilter;public int Slots=10,Head=-1,Body=-1,Pattern=-1,MinGeneration=1,MaxGeneration=1,IncludedGeneration=-1,Level=-1;public int[] Face;}
public class Choice {public int Value {get;set;} public string Text {get;set;} public override string ToString(){return Text;}}
public sealed class GenerationNumber:NumericUpDown {
    public object SelectedValue {get{return Value==0?-1:(int)Value;}set{Value=Math.Max(Minimum,Math.Min(Maximum,Convert.ToInt32(value)<0?0:Convert.ToInt32(value)));}}
    protected override void UpdateEditText(){Text=Value==0?"―":Value.ToString("0");}
}
public sealed partial class Model {
    public static bool GenerationRangeContains(int current,int limit,int requestedMin,int requestedMax){return current<=requestedMin&&requestedMax<=limit;}
    public static uint ParseKey(string text){
        text=(text??"").Trim();if(text.StartsWith("0x",StringComparison.OrdinalIgnoreCase))text=text.Substring(2);
        uint result;if(text.Length!=8||!UInt32.TryParse(text,NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out result))
            throw new Exception("予測キーは16進数8桁で入力してください（例：00000000）。");
        return result;
    }
    static readonly int[] ColorBases={3,6,9,12,15,18,21,24,27,36,33,30};
    static readonly string[] ColorNames={"黒","赤","水色","緑","オレンジ","黄","青","白","紫","銀","金","桃"};
    public string ColorText(int raw){
        if(raw==-1)return "未指定";
        if(raw==0)return "(なし)";
        bool family=IsFamilyColor(raw);int value=family?raw-100:raw;
        if(value>=3&&value<=38){string name=ColorNames[Array.IndexOf(ColorBases,value-value%3)];return family?name:new[]{"通常","濃","薄"}[value%3]+name;}
        return "未対応色（"+raw.ToString("X2")+"）";
    }
    public Choice[] Colors(){
        var values=new List<int>{-1,0};values.AddRange(ColorBases.Select(c=>100+c));
        for(int tone=0;tone<3;tone++)values.AddRange(ColorBases.Select(c=>c+tone));
        return values.Select(c=>new Choice{Value=c,Text=ColorText(c)}).ToArray();
    }
    public Choice[] IdentificationColors(){return Colors().Where(c=>c.Value==-1||c.Value==0||IsFamilyColor(c.Value)).ToArray();}
    public Choice[] SingleIdentificationColors(){return Colors().Where(c=>c.Value<3||c.Value>5).ToArray();}
    public Choice[] IdentificationPatterns(){return new[]{-1,6,9,7,8}.Select(p=>new Choice{Value=p,Text=p<0?"未指定":PatternText(p)}).ToArray();}
    public static int IdentificationColor(int value){return value>=3&&value<=38?100+value-value%3:value;}
    public static Day[] ImportDays(Day[] days,out bool migrated){
        if(days==null||days.Length==0||days.Length>31)throw new Exception("記録は1～31日分にしてください。");
        migrated=false;
        foreach(var day in days){
            if(day==null||day.People==null||day.People.Length!=(day.SinglePerson?1:10))throw new Exception("各日付の人数が不正です。");
            foreach(var person in day.People){if(person==null)throw new Exception("人物の記録が不正です。");if(person.Antenna=="アンテナのねっこ"){person.Antenna="";migrated=true;}}
            if(day.SinglePerson)foreach(var person in day.People){if(person.Color1>=3&&person.Color1<=5){person.Color1=103;migrated=true;}if(person.Color2>=3&&person.Color2<=5){person.Color2=103;migrated=true;}}
            if(day.ColorEncoding=="color-query-v2")continue;
            if(day.ColorEncoding=="raw-v1"){
                foreach(var person in day.People){if(person==null)throw new Exception("人物の記録が不正です。");if(person.Color1==1||person.Color1==2){person.Color1=-1;migrated=true;}if(person.Color2==1||person.Color2==2){person.Color2=-1;migrated=true;}}
                day.ColorEncoding="color-query-v2";continue;
            }
            if(!String.IsNullOrEmpty(day.ColorEncoding))throw new Exception("対応しない色の記録形式です。");
            int[] masks={1,2,4,8,16,32,64,128,256,512,1024,2048};int[] bases={3,6,9,12,15,18,21,24,27,30,33,36};
            foreach(var person in day.People){
                if(person==null)throw new Exception("人物の記録が不正です。");
                int a=Array.IndexOf(masks,person.Color1),b=Array.IndexOf(masks,person.Color2);
                person.Color1=a>=0?100+bases[a]:person.Color1==0?0:-1;person.Color2=b>=0?100+bases[b]:person.Color2==0?0:-1;
            }
            day.ColorEncoding="color-query-v2";migrated=true;
        }
        return days;
    }
    public List<ForecastRow> Forecast(uint key,DateTime first,DateTime last,string name,string antenna,int color1,int color2){
        return Forecast(key,first,last,name,antenna,color1,color2,10,-1,null,1,1,-1,-1,false,-1,false);
    }
    public List<ForecastRow> Forecast(uint key,DateTime first,DateTime last,string name,string antenna,int color1,int color2,int maxSlots,int head,int[] face,int minGeneration,int maxGeneration,int body=-1,int pattern=-1,bool allowReverseColors=false,int level=-1,bool useGenerationFilter=true,int includedGeneration=-1){
        var rows=new List<ForecastRow>();foreach(var row in ForecastSteps(key,first,last,name,antenna,color1,color2,maxSlots,head,face,minGeneration,maxGeneration,body,pattern,allowReverseColors,level,useGenerationFilter,includedGeneration)){if(row==null)continue;if(rows.Count>=100000)throw new Exception("検索結果が10万人を超えます。条件を絞ってください。");rows.Add(row);}return rows;
    }
    public IEnumerable<ForecastRow> ForecastSteps(uint key,DateTime first,DateTime last,string name,string antenna,int color1,int color2,int maxSlots,int head,int[] face,int minGeneration,int maxGeneration,int body=-1,int pattern=-1,bool allowReverseColors=false,int level=-1,bool useGenerationFilter=true,int includedGeneration=-1){
        first=first.Date;last=last.Date;
        if(first<new DateTime(2001,1,1)||last>new DateTime(2050,12,31)||last<first)throw new Exception("期間は2001/01/01～2050/12/31の範囲で、開始日≦終了日にしてください。");
        if(!ValidColor(color1)||!ValidColor(color2))throw new Exception("体色の指定が不正です。");
        if(!String.IsNullOrEmpty(antenna)&&!Data.abilities.Values.Contains(antenna))throw new Exception("アンテナは選択肢から選んでください。");
        if(maxSlots<1||maxSlots>79||!RareHeads().Any(h=>h.Value==head))throw new Exception("検索枠数・頭の条件を確認してください。");
        if(!BodyFilterChoices(true).Any(b=>b.Value==body))throw new Exception("体格の条件を確認してください。");
        if(!PatternFilterChoices().Any(p=>p.Value==pattern))throw new Exception("柄の条件を確認してください。");
        if(useGenerationFilter&&(minGeneration<1||minGeneration>MaxGeneration||maxGeneration>MaxGenerationLimit||minGeneration>maxGeneration))throw new Exception("世代数の範囲を確認してください。");
        if(includedGeneration!=-1&&(includedGeneration<1||includedGeneration>MaxGenerationLimit))throw new Exception("含む世代を確認してください。");
        if(useGenerationFilter&&includedGeneration>=0&&(includedGeneration<minGeneration||includedGeneration>maxGeneration))throw new Exception("「世代範囲」と「含む世代」を揃えてください。含む世代が世代範囲の外になっています。");
        if(level!=-1&&level!=10&&level!=16)throw new Exception("レベルは未指定・Lv10・Lv16から選んでください。");
        if(face!=null&&(face.Length!=FaceNames.Length||face.Where((value,i)=>!FaceChoices(i).Contains(value)).Any()))throw new Exception("顔パーツの指定が不正です。");

        if(!String.IsNullOrEmpty(name)&&!Data.names.Any(n=>n.IndexOf(name,StringComparison.Ordinal)>=0))yield break;
        for(DateTime date=first;date<=last;date=date.AddDays(1)){
            int number=(date-new DateTime(1999,1,1)).Days;
            foreach(var person in Forward(unchecked(key+(uint)number),maxSlots)){
                if(!String.IsNullOrEmpty(name)&&person.Name.IndexOf(name,StringComparison.Ordinal)<0){yield return null;continue;}
                if(!AntennaMatches(antenna,person.Antenna,person.Row)){yield return null;continue;}
                if(!ColorPairMatches(color1,color2,person.RawPrimary,person.RawSecondary,!allowReverseColors)){yield return null;continue;}
                if(!BodyMatches(body,person.Body)){yield return null;continue;}
                if(!PatternMatches(pattern,person.Pattern)){yield return null;continue;}
                if(head>=0&&person.Head!=head||!FaceMatches(face,person.Face)){yield return null;continue;}
                if(useGenerationFilter&&(person.Generation<minGeneration||person.GenerationLimit>maxGeneration)){yield return null;continue;}
                if(includedGeneration>=0&&!GenerationRangeContains(person.Generation,person.GenerationLimit,includedGeneration,includedGeneration)){yield return null;continue;}
                if(level>=0&&person.Level!=level){yield return null;continue;}
                yield return new ForecastRow{ParentCandidate=person,Date=date.ToString("yyyy/MM/dd"),Slot=person.Slot+"枠・"+(person.Slot<=10?"最初の10人":"補充"+(person.Slot-10)+"枠"),Name=person.Name,Antenna=DisplayAntenna(person),Body=person.Body,Head=HeadText(person.Head),Color1=ColorText(person.RawPrimary),Color2=ColorText(person.RawSecondary),Pattern=PatternText(person.Pattern),Hair=person.Face[1],FaceShape=person.Face[0],Brow=person.Face[2],Eye=person.Face[3],Nose=person.Face[4],Mouth=person.Face[5],Cheek=person.Face[6],SkinColor=person.Face[7],HairColor=person.Face[8],Generation=person.Generation,Level=person.Level};
            }
        }
        yield break;
    }
}

public sealed partial class ToolForm:Form {
    readonly Model model;readonly string preferencesPath,rememberedKeysPath,savedInputsDirectory;
    readonly ComboBox rememberedKeys=new ComboBox();readonly Button rememberKey=new Button(),deleteKey=new Button();bool rememberedKeysLoadFailed;
    readonly Panel identify=new Panel(),predict=new Panel();
    readonly Button identifyNav=new Button(),predictNav=new Button(),add=new Button(),remove=new Button(),start=new Button(),stop=new Button();
    readonly Button copy=new Button(),use=new Button(),forecastButton=new Button();
    readonly TabControl tabs=new TabControl();readonly Dictionary<TabPage,DateTimePicker> dates=new Dictionary<TabPage,DateTimePicker>();
    readonly NumericUpDown minutes=new NumericUpDown(),workers=new NumericUpDown();readonly ProgressBar progress=new ProgressBar();
    readonly CheckBox useTimeLimit=new CheckBox();
    readonly RadioButton identifyTen=new RadioButton(),identifySingle=new RadioButton();
    readonly Panel singleInput=new Panel();readonly Label identifyDescription=new Label();
    readonly DateTimePicker singleDate=new DateTimePicker();readonly TextBox singleName=new TextBox();
    readonly Dictionary<string,ComboBox> singleChoices=new Dictionary<string,ComboBox>();
    readonly Button deleteIdentificationHistory=new Button();
    readonly Label status=new Label(),keyNote=new Label(),forecastStatus=new Label();readonly ListBox results=new ListBox();
    readonly TextBox resultKey=new TextBox(),forecastKey=new TextBox(),nameFilter=new TextBox();
    readonly DateTimePicker dateStart=new DateTimePicker(),dateEnd=new DateTimePicker();
    readonly Button fullRangeButton=new Button(),forecastResetButton=new Button();readonly CheckBox allowReverseColors=new CheckBox();
    readonly ComboBox abilityFilter=new ComboBox(),colorFilter1=new ComboBox(),colorFilter2=new ComboBox();
    readonly ComboBox headFilter=new ComboBox(),bodyFilter=new ComboBox(),patternFilter=new ComboBox();readonly NumericUpDown slotLimit=new NumericUpDown();
    readonly ComboBox[] faceFilters=new ComboBox[Model.FaceNames.Length];readonly GenerationNumber minGenerationFilter=new GenerationNumber(),maxGenerationFilter=new GenerationNumber();bool faceSyncUpdating;
    readonly CheckBox generationFilterEnabled=new CheckBox();
    readonly GenerationNumber includedGenerationFilter=new GenerationNumber();readonly CheckBox singleReverseColors=new CheckBox();
    readonly ComboBox levelFilter=new ComboBox();
    readonly DataGridView forecastGrid=new DataGridView();readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    readonly List<uint> resultValues=new List<uint>();
    SearchJob job;State previous;Task task;bool running,closeAfter,forecastRunning;
    public Action<string> CopyToClipboard=Clipboard.SetText;
    public ToolForm(Model model,string storageRoot){
        this.model=model;Directory.CreateDirectory(storageRoot);preferencesPath=Path.Combine(storageRoot,"予測キー設定.json");rememberedKeysPath=Path.Combine(storageRoot,"予測キー記憶.json");savedInputsDirectory=Path.Combine(storageRoot,"保存個体");
        Text=AppInfo.DisplayName;Name="freeNormalTool";ClientSize=new Size(1450,900);MinimumSize=new Size(1120,800);
        Font=new Font("Yu Gothic UI",10);BackColor=Color.FromArgb(237,247,253);StartPosition=FormStartPosition.CenterScreen;
        var shell=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,Padding=new Padding(10)};
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute,44));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(shell);
        var navigation=new FlowLayoutPanel{Dock=DockStyle.Fill};identifyNav.Text="特定ツール";predictNav.Text="レアキャ検索";
        identifyNav.Name="identifyModeButton";predictNav.Name="predictModeButton";
        birthNav.Text="出生解析";birthNav.Name="birthModeButton";lineageNav.Text="ドーピングメモ・ルート確認";lineageNav.Name="lineageModeButton";
        gekiNav.Text="ゲキレア検索";gekiNav.Name="gekiModeButton";
        foreach(var button in new[]{identifyNav,predictNav,gekiNav,birthNav,lineageNav}){button.Size=new Size(button==lineageNav?250:160,35);navigation.Controls.Add(button);}shell.Controls.Add(navigation,0,0);
        var pages=new Panel{Dock=DockStyle.Fill};identify.Dock=predict.Dock=gekiPage.Dock=birthPage.Dock=lineagePage.Dock=DockStyle.Fill;pages.Controls.Add(lineagePage);pages.Controls.Add(birthPage);pages.Controls.Add(gekiPage);pages.Controls.Add(predict);pages.Controls.Add(identify);shell.Controls.Add(pages,0,1);
        BuildIdentify();BuildPredict();BuildBirth();BuildLineage();BuildGeki();BuildManualDopingMemo();identifyNav.Click+=delegate{ShowIdentify();};predictNav.Click+=delegate{ShowPredictor();};gekiNav.Click+=delegate{ShowGeki();};birthNav.Click+=delegate{ShowBirth();};lineageNav.Click+=delegate{ShowLineage();};
        timer.Interval=500;timer.Tick+=delegate{RefreshStatus();};timer.Start();
        FormClosing+=delegate(object sender,FormClosingEventArgs e){if(savingBirthInput){e.Cancel=true;birthStatus.Text=forecastStatus.Text="保存中です。完了してから閉じてください。";return;}if(running){closeAfter=true;job.Stop=true;e.Cancel=true;status.Text="現在の小区間を完了して閉じます。探索の途中位置は破棄します。";}};
        FormClosed+=delegate{timer.Stop();timer.Dispose();};
        LoadPreferences();
        LoadRememberedKeys();
        AddDay(null);
        while(tabs.TabCount<2)AddDay(null);tabs.SelectedIndex=0;UpdateDayButtons();
        ShowIdentify();
    }
    static Label LabelFor(string text){return new Label{Text=text,AutoSize=true,Padding=new Padding(4,7,0,0)};}
    static void AlignInlineRow(FlowLayoutPanel row){
        bool updating=false;
        Action align=delegate{
            if(updating||row.IsDisposed||row.Controls.Count==0)return;updating=true;row.SuspendLayout();
            try{
                int height=row.Controls.Cast<Control>().Where(c=>!(c is Label)).Select(c=>c.Height).DefaultIfEmpty(row.Font.Height+8).Max();
                foreach(Control control in row.Controls){
                    var label=control as Label;
                    if(label!=null){bool first=row.Controls.IndexOf(label)==0;label.Padding=new Padding(first?1:label.Padding.Left,0,label.Padding.Right,0);int width=first?TextRenderer.MeasureText("検索範囲",row.Font).Width+8:label.GetPreferredSize(Size.Empty).Width;label.AutoSize=false;label.TextAlign=ContentAlignment.MiddleLeft;label.Size=new Size(width,height);if(first)label.Margin=new Padding(1,3,1,3);}
                    if(row.Controls.IndexOf(control)==1)control.Margin=new Padding(1,control.Margin.Top,control.Margin.Right,control.Margin.Bottom);
                    var check=control as CheckBox;if(check!=null)check.Padding=Padding.Empty;
                    int space=Math.Max(0,height-control.Height);control.Margin=new Padding(control.Margin.Left,3+space/2,control.Margin.Right,3+space-space/2);
                }
            }finally{row.ResumeLayout(false);updating=false;}
        };
        row.Layout+=delegate{align();};row.FontChanged+=delegate{align();};align();
    }
    static void WrapInlineFilters(FlowLayoutPanel row,Control breakAfter,Action<bool> resize){
        bool updating=false;row.WrapContents=true;
        Action fit=delegate{if(updating||row.IsDisposed||row.ClientSize.Width<=0)return;updating=true;
            try{int width=row.Controls.Cast<Control>().Sum(c=>(c.AutoSize?Math.Max(c.Width,c.PreferredSize.Width):c.Width)+c.Margin.Horizontal);bool wrap=width>row.ClientSize.Width;row.SetFlowBreak(breakAfter,wrap);resize(wrap);}
            finally{updating=false;}
        };
        row.SizeChanged+=delegate{fit();};row.Layout+=delegate{fit();};fit();
    }
    void BuildIdentify(){
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6};
        foreach(int height in new[]{28,40,0,25,46,162})layout.RowStyles.Add(height==0?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.Absolute,height));identify.Controls.Add(layout);
        identifyDescription.Dock=DockStyle.Fill;identifyDescription.Text="レアキャッチに出現した10人の情報を、確認できる範囲で入力してください。";layout.Controls.Add(identifyDescription,0,0);
        var tools=new FlowLayoutPanel{Dock=DockStyle.Fill};
        add.Text="日付を追加";remove.Text="この日を削除";
        deleteIdentificationHistory.Name="identifyDeleteHistory";deleteIdentificationHistory.Text="リセット";
        identifyTen.Name="identifyTenPeople";identifyTen.Text="10人から特定する";identifyTen.AutoSize=true;identifyTen.Checked=true;identifySingle.Name="identifySinglePerson";identifySingle.Text="1人から特定する";identifySingle.AutoSize=true;tools.Controls.Add(identifyTen);tools.Controls.Add(identifySingle);
        foreach(var b in new[]{add,remove,deleteIdentificationHistory}){b.AutoSize=true;tools.Controls.Add(b);}layout.Controls.Add(tools,0,1);tabs.Dock=DockStyle.Fill;
        var inputs=new Panel{Dock=DockStyle.Fill};inputs.Controls.Add(tabs);singleInput.Dock=DockStyle.Fill;inputs.Controls.Add(singleInput);BuildSingleIdentification();layout.Controls.Add(inputs,0,2);
        identifySingle.CheckedChanged+=delegate{UpdateIdentificationMode();};UpdateIdentificationMode();
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill};useTimeLimit.Name="identifyUseTimeLimit";useTimeLimit.Text="計算上限時間を設定する";useTimeLimit.AutoSize=true;useTimeLimit.Padding=new Padding(0,5,0,0);useTimeLimit.Checked=false;actions.Controls.Add(useTimeLimit);minutes.Name="identifyTimeLimitMinutes";minutes.Minimum=1;minutes.Maximum=10;minutes.Value=10;minutes.Width=55;minutes.Enabled=false;actions.Controls.Add(minutes);actions.Controls.Add(LabelFor("分"));useTimeLimit.CheckedChanged+=delegate{minutes.Enabled=!running&&useTimeLimit.Checked;};
        actions.Controls.Add(LabelFor("使用スレッド（並列計算数）"));workers.Minimum=1;workers.Maximum=4;workers.Value=2;workers.Width=50;actions.Controls.Add(workers);
        start.Name="identifySearch";stop.Name="identifyStop";start.Text="検索実行/再開";stop.Text="中断";start.AutoSize=stop.AutoSize=true;stop.Enabled=false;actions.Controls.Add(start);actions.Controls.Add(stop);layout.Controls.Add(actions,0,4);
        var bottom=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,57));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,43));
        status.Dock=DockStyle.Fill;status.Text="未探索。10人の情報を入力して検索してください。\r\n"+IdentificationGuidance;bottom.Controls.Add(status,0,0);
        var keyPanel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};keyPanel.RowStyles.Add(new RowStyle(SizeType.Percent,100));keyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute,31));keyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute,36));keyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute,25));
        results.Name="candidateKeys";results.Dock=DockStyle.Fill;keyPanel.Controls.Add(results,0,0);
        resultKey.Name="identifiedKey";resultKey.ReadOnly=true;resultKey.Font=new Font("Consolas",13);resultKey.Dock=DockStyle.Fill;keyPanel.Controls.Add(resultKey,0,1);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill};copy.Name="keyCopyButton";use.Name="useKeyButton";copy.Text="キーをコピー";use.Text="このキーでレアキャ検索";copy.AutoSize=use.AutoSize=true;copy.Enabled=use.Enabled=false;buttons.Controls.Add(copy);buttons.Controls.Add(use);keyPanel.Controls.Add(buttons,0,2);keyNote.Dock=DockStyle.Fill;keyPanel.Controls.Add(keyNote,0,3);bottom.Controls.Add(keyPanel,1,0);layout.Controls.Add(bottom,0,5);
        progress.Name="identifyProgress";progress.Maximum=1000;progress.Dock=DockStyle.Fill;layout.Controls.Add(progress,0,3);
        tabs.SelectedIndexChanged+=delegate{UpdateDayButtons();};
        add.Click+=delegate{if(tabs.TabCount<31)AddDay(null);UpdateDayButtons();};remove.Click+=delegate{if(!running&&tabs.SelectedIndex>=2){var page=tabs.SelectedTab;dates.Remove(page);tabs.TabPages.Remove(page);page.Dispose();UpdateDayButtons();}};
        start.Click+=delegate{BeginSearch();};stop.Click+=delegate{if(job!=null)job.Stop=true;stop.Enabled=false;status.Text="現在の小区間を完了して中断します。開いている間は途中から再開できます。";};
        deleteIdentificationHistory.Click+=delegate{try{ClearIdentificationHistory();}catch(Exception error){ShowError(error);}};
        results.SelectedIndexChanged+=delegate{UpdateSelectedKey();};copy.Click+=delegate{try{CopySelectedKey();}catch(Exception error){ShowError(error);}};use.Click+=delegate{UseSelectedKey();};
    }
    void BuildPredict(){
        levelFilter.BindingContext=new BindingContext();
        patternFilter.BindingContext=new BindingContext();
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=7};int[] rowHeights={0,64,64,62,62,44,100};for(int i=0;i<rowHeights.Length;i++)layout.RowStyles.Add(new RowStyle(i==6?SizeType.Percent:SizeType.Absolute,rowHeights[i]));predict.Controls.Add(layout);
        var keys=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};keys.Controls.Add(LabelFor("予測キー"));forecastKey.Name="predictionKey";forecastKey.Font=new Font("Consolas",13);forecastKey.Width=155;forecastKey.MaxLength=10;keys.Controls.Add(forecastKey);
        rememberKey.Name="rememberPredictionKey";rememberKey.Text="予測キーを記憶";rememberKey.AutoSize=true;keys.Controls.Add(rememberKey);keys.Controls.Add(LabelFor("記憶したキー"));rememberedKeys.Name="rememberedPredictionKeys";rememberedKeys.Width=145;rememberedKeys.DropDownStyle=ComboBoxStyle.DropDownList;keys.Controls.Add(rememberedKeys);deleteKey.Name="deleteRememberedKey";deleteKey.Text="選択キーを削除";deleteKey.AutoSize=true;keys.Controls.Add(deleteKey);layout.Controls.Add(keys,0,1);
        rememberedKeys.SelectedIndexChanged+=delegate{if(rememberedKeys.SelectedIndex>=0&&!forecastRunning)forecastKey.Text=(string)rememberedKeys.SelectedItem;};
        rememberKey.Click+=delegate{try{RememberPredictionKey();forecastStatus.Text="予測キーを記憶しました。次回起動後も一覧から選べます。";}catch(Exception e){ShowError(e);}};
        deleteKey.Click+=delegate{try{DeleteRememberedPredictionKey(rememberedKeys.SelectedIndex);forecastStatus.Text="選択したキーを記憶一覧から削除しました。入力中のキーは保持しています。";}catch(Exception e){ShowError(e);}};
        var period=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};period.Controls.Add(LabelFor("検索範囲"));ConfigureDate(dateStart);ConfigureDate(dateEnd);foreach(var picker in new[]{dateStart,dateEnd}){picker.MinDate=new DateTime(2001,1,1);picker.MaxDate=new DateTime(2050,12,31);}SetFullSearchRange();dateStart.Name="predictionDateStart";dateEnd.Name="predictionDateEnd";period.Controls.Add(dateStart);period.Controls.Add(LabelFor("～"));period.Controls.Add(dateEnd);fullRangeButton.Name="predictionFullDateRange";fullRangeButton.Text="全期間に設定";fullRangeButton.AutoSize=true;fullRangeButton.Click+=delegate{SetFullSearchRange();};period.Controls.Add(fullRangeButton);period.Controls.Add(LabelFor("補充"));slotLimit.Name="predictionSlotLimit";slotLimit.Minimum=0;slotLimit.Maximum=69;slotLimit.Value=0;slotLimit.Width=65;period.Controls.Add(slotLimit);period.Controls.Add(LabelFor("枠まで"));layout.Controls.Add(period,0,2);
        var filters=new FlowLayoutPanel{Dock=DockStyle.Fill};
        filters.Controls.Add(LabelFor("アンテナ"));abilityFilter.Name="predictionAntennaFilter";abilityFilter.DropDownStyle=ComboBoxStyle.DropDownList;abilityFilter.Width=190;abilityFilter.Items.Add("指定なし");abilityFilter.Items.AddRange(model.Antennas().Cast<object>().ToArray());abilityFilter.SelectedIndex=0;filters.Controls.Add(abilityFilter);
        filters.Controls.Add(LabelFor("頭"));headFilter.Name="predictionHeadFilter";headFilter.Width=110;headFilter.DropDownStyle=ComboBoxStyle.DropDownList;headFilter.DisplayMember="Text";headFilter.ValueMember="Value";headFilter.DataSource=model.RareHeads();headFilter.SelectedValue=-1;filters.Controls.Add(headFilter);
        filters.Controls.Add(LabelFor("色1"));ConfigureColor(colorFilter1);colorFilter1.Name="predictionColor1";filters.Controls.Add(colorFilter1);filters.Controls.Add(LabelFor("色2"));ConfigureColor(colorFilter2);colorFilter2.Name="predictionColor2";filters.Controls.Add(colorFilter2);
        allowReverseColors.Name="predictionAllowReverseColors";allowReverseColors.Text="逆順を許可";allowReverseColors.Checked=true;allowReverseColors.AutoSize=true;allowReverseColors.Padding=new Padding(0,5,0,0);filters.Controls.Add(allowReverseColors);
        filters.Controls.Add(LabelFor("柄"));patternFilter.Name="predictionPatternFilter";patternFilter.Width=90;patternFilter.DropDownStyle=ComboBoxStyle.DropDownList;patternFilter.DisplayMember="Text";patternFilter.ValueMember="Value";patternFilter.DataSource=model.PatternFilterChoices();patternFilter.SelectedValue=-1;filters.Controls.Add(patternFilter);
        filters.Controls.Add(LabelFor("体格"));bodyFilter.Name="predictionBodyFilter";bodyFilter.Width=75;bodyFilter.DropDownStyle=ComboBoxStyle.DropDownList;bodyFilter.DisplayMember="Text";bodyFilter.ValueMember="Value";bodyFilter.DataSource=model.BodyFilterChoices(true);bodyFilter.SelectedValue=-1;filters.Controls.Add(bodyFilter);
        filters.Controls.Add(LabelFor("名前"));nameFilter.Name="predictionNameFilter";nameFilter.Width=105;nameFilter.AutoCompleteMode=AutoCompleteMode.SuggestAppend;nameFilter.AutoCompleteSource=AutoCompleteSource.CustomSource;var completion=new AutoCompleteStringCollection();completion.AddRange(model.Data.names);nameFilter.AutoCompleteCustomSource=completion;filters.Controls.Add(nameFilter);
        nameFilter.Width=85;abilityFilter.Width=150;bodyFilter.Width=88;colorFilter1.Width=colorFilter2.Width=90;filters.WrapContents=true;layout.Controls.Add(filters,0,3);
        foreach(Label label in filters.Controls.OfType<Label>())label.Margin=new Padding(1,3,1,3);
        var appearanceFilters=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};
        period.WrapContents=true;period.SetFlowBreak(period.Controls[period.Controls.Count-1],true);includedGenerationFilter.Name="predictionIncludedGeneration";includedGenerationFilter.Minimum=0;includedGenerationFilter.Maximum=model.MaxGenerationLimit;includedGenerationFilter.Value=0;includedGenerationFilter.Width=45;period.Controls.Add(includedGenerationFilter);period.Controls.Add(LabelFor("世代を含む"));
        generationFilterEnabled.Name="predictionUseGenerationFilter";generationFilterEnabled.Text="世代範囲";generationFilterEnabled.AutoSize=true;generationFilterEnabled.Checked=false;period.Controls.Add(generationFilterEnabled);foreach(var generation in new[]{minGenerationFilter,maxGenerationFilter}){generation.Width=45;generation.Minimum=1;generation.Maximum=generation==minGenerationFilter?model.MaxGeneration:model.MaxGenerationLimit;generation.Value=1;generation.Enabled=false;}minGenerationFilter.Name="predictionMinGeneration";maxGenerationFilter.Name="predictionMaxGeneration";period.Controls.Add(minGenerationFilter);period.Controls.Add(LabelFor("～"));period.Controls.Add(maxGenerationFilter);period.Controls.Add(LabelFor("世代"));generationFilterEnabled.CheckedChanged+=delegate{minGenerationFilter.Enabled=maxGenerationFilter.Enabled=!forecastRunning&&generationFilterEnabled.Checked;};
        period.Controls.Add(LabelFor("レベル"));levelFilter.Name="predictionLevel";levelFilter.Width=85;levelFilter.DropDownStyle=ComboBoxStyle.DropDownList;levelFilter.DisplayMember="Text";levelFilter.ValueMember="Value";levelFilter.DataSource=new[]{new Choice{Value=-1,Text="未指定"},new Choice{Value=10,Text="Lv10"},new Choice{Value=16,Text="Lv16"}};levelFilter.SelectedValue=-1;period.Controls.Add(levelFilter);
        dateStart.Width=dateEnd.Width=120;slotLimit.Width=45;foreach(Control control in period.Controls)control.Margin=new Padding(1,3,1,3);foreach(Label label in period.Controls.OfType<Label>())label.Padding=new Padding(1,7,0,0);
        BuildPredictionTop(layout,keys,period);StackFieldLabels(filters);
        forecastButton.Name="forecastSearchButton";forecastButton.Text="検索実行";forecastButton.AutoSize=true;appearanceFilters.Controls.Add(forecastButton);appearanceFilters.Controls.Add(forecastPauseResume);forecastPauseResume.Click+=async delegate{await ToggleForecastSearchAsync();};forecastResetButton.Name="forecastFilterReset";forecastResetButton.Text="リセット";forecastResetButton.AutoSize=true;appearanceFilters.Controls.Add(forecastResetButton);forecastResetButton.Click+=delegate{ResetForecastFilters();};layout.Controls.Add(appearanceFilters,0,5);
        var individualFilters=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=9,RowCount=1,Margin=Padding.Empty,Padding=new Padding(0,0,4,0)};
        individualFilters.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        int[] faceOrder={1,0,2,3,4,5,6,7,8};
        for(int position=0;position<faceOrder.Length;position++){
            int i=faceOrder[position];individualFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/9));
            var group=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,Margin=new Padding(2,0,4,0),Padding=Padding.Empty};
            group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            group.RowStyles.Add(new RowStyle(SizeType.Absolute,22));group.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            group.Controls.Add(new Label{Text=Model.FaceNames[i],Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Margin=Padding.Empty},0,0);
            var box=new ComboBox{Name="prediction"+Model.FaceKeys[i]+"Filter",Dock=DockStyle.Top,Margin=new Padding(0,1,2,4),DropDownStyle=ComboBoxStyle.DropDownList,DisplayMember="Text",ValueMember="Value",BindingContext=new BindingContext()};
            box.DataSource=model.FaceChoices(i).Select(n=>new Choice{Value=n,Text=n<0?"未指定":n.ToString("D2")}).ToArray();box.SelectedValue=-1;faceFilters[i]=box;group.Controls.Add(box,0,1);individualFilters.Controls.Add(group,position,0);
        }
        layout.Controls.Add(individualFilters,0,4);
        individualFilters.SizeChanged+=delegate{layout.RowStyles[4].Height=Math.Max(62,faceFilters.Max(c=>c.PreferredHeight)+30);};
        faceFilters[1].SelectedIndexChanged+=delegate{SyncForecastHair(false);};SyncForecastHair(false);
        forecastGrid.Name="forecastResults";forecastGrid.Dock=DockStyle.Fill;forecastGrid.ReadOnly=true;forecastGrid.AllowUserToAddRows=forecastGrid.AllowUserToDeleteRows=false;forecastGrid.AllowUserToResizeColumns=false;forecastGrid.RowHeadersVisible=false;forecastGrid.AutoGenerateColumns=false;forecastGrid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None;forecastGrid.BackgroundColor=Color.White;forecastGrid.ClipboardCopyMode=DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;forecastGrid.Font=new Font("Yu Gothic UI",9);forecastGrid.DefaultCellStyle.Padding=new Padding(1,0,1,0);forecastGrid.ColumnHeadersDefaultCellStyle.Padding=new Padding(0);forecastGrid.ColumnHeadersDefaultCellStyle.WrapMode=DataGridViewTriState.False;forecastGrid.ColumnHeadersHeight=26;
        var columns=new List<string[]>{new[]{"Date","日付"},new[]{"Slot","枠"},new[]{"Name","名前"},new[]{"Generation","世代数"},new[]{"Antenna","アンテナ"},new[]{"Body","体格"},new[]{"Head","頭"},new[]{"Color1","色1"},new[]{"Color2","色2"},new[]{"Pattern","柄"}};
        int[] faceColumnOrder={1,0,2,3,4,5,6,7,8};foreach(int i in faceColumnOrder)columns.Add(new[]{Model.FaceKeys[i],Model.FaceNames[i]});
        columns.Add(new[]{"Level","レベル"});
        foreach(var item in columns){var column=new DataGridViewTextBoxColumn{DataPropertyName=item[0],Name=item[0],HeaderText=item[1],MinimumWidth=16,SortMode=DataGridViewColumnSortMode.NotSortable};if(Model.FaceKeys.Contains(item[0]))column.DefaultCellStyle.Format="D2";forecastGrid.Columns.Add(column);}forecastGrid.RowTemplate.Height=34;
        layout.Controls.Add(forecastGrid,0,6);forecastStatus.Visible=false;
        forecastGrid.Columns["Generation"].DataPropertyName="GenerationText";
        forecastGrid.Columns["Body"].DataPropertyName="BodyText";forecastGrid.Columns["Body"].DefaultCellStyle.Format="";forecastGrid.Columns["Body"].MinimumWidth=58;forecastGrid.Columns["Body"].FillWeight=66;
        foreach(DataGridViewColumn column in forecastGrid.Columns){
            bool numeric=column.Name=="Generation"||column.Name=="Level"||Model.FaceKeys.Contains(column.Name);
            column.MinimumWidth=16;
            column.DefaultCellStyle.Alignment=numeric?DataGridViewContentAlignment.MiddleCenter:DataGridViewContentAlignment.MiddleLeft;
            column.HeaderCell.Style.Alignment=numeric?DataGridViewContentAlignment.MiddleCenter:DataGridViewContentAlignment.MiddleLeft;
            column.DefaultCellStyle.WrapMode=DataGridViewTriState.False;
        }
        forecastGrid.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleLeft;
        forecastGrid.DefaultCellStyle.WrapMode=DataGridViewTriState.False;
        forecastGrid.RowTemplate.Height=Math.Max(24,forecastGrid.Font.Height+6);
        forecastGrid.FontChanged+=delegate{FitForecastColumns();};FitForecastColumns();
        forecastButton.Click+=async delegate{await RunForecast();};
    }
    void BuildPredictionTop(TableLayoutPanel layout,Control oldKeys,Control oldPeriod){
        var top=new FlowLayoutPanel{Name="predictionTopFields",Dock=DockStyle.Fill,WrapContents=true,Margin=Padding.Empty,Padding=Padding.Empty};
        var generations=new FlowLayoutPanel{Name="predictionGenerationFields",Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty,Padding=Padding.Empty};
        layout.Controls.Remove(oldKeys);layout.Controls.Remove(oldPeriod);layout.RowStyles[1].Height=64;layout.RowStyles[2].Height=64;layout.Controls.Add(top,0,1);layout.Controls.Add(generations,0,2);
        forecastKey.Width=105;rememberedKeys.Width=100;rememberKey.AutoSize=deleteKey.AutoSize=false;rememberKey.Width=110;deleteKey.Width=105;rememberKey.Height=deleteKey.Height=29;
        PredictionTopField(top,"予測キー",forecastKey.Width+rememberKey.Width+4,forecastKey,rememberKey);PredictionTopField(top,"記憶したキー",rememberedKeys.Width+deleteKey.Width+4,rememberedKeys,deleteKey);
        dateStart.Width=dateEnd.Width=112;fullRangeButton.AutoSize=false;fullRangeButton.Width=100;fullRangeButton.Height=29;
        PredictionTopField(top,"検索範囲",dateStart.Width+dateEnd.Width+fullRangeButton.Width+38,dateStart,TopSuffix("～",20),dateEnd,fullRangeButton);
        slotLimit.Width=includedGenerationFilter.Width=minGenerationFilter.Width=maxGenerationFilter.Width=45;
        PredictionTopField(generations,"補充",108,slotLimit,TopSuffix("枠まで",55));
        PredictionTopField(generations,"世代数",142,includedGenerationFilter,TopSuffix("世代を含む",90));
        var range=PredictionTopField(generations,"",164,minGenerationFilter,TopSuffix("～",20),maxGenerationFilter,TopSuffix("世代",38));foreach(var label in range.Controls.OfType<Label>().ToArray())label.Dispose();generationFilterEnabled.AutoSize=false;generationFilterEnabled.Size=new Size(150,23);range.Controls.Add(generationFilterEnabled);generationFilterEnabled.Location=Point.Empty;generationFilterEnabled.Size=new Size(150,23);generationFilterEnabled.BringToFront();
        PredictionTopField(generations,"レベル",levelFilter.Width,levelFilter);
        oldKeys.Dispose();oldPeriod.Dispose();
        bool resizing=false;Action fit=delegate{if(resizing||top.IsDisposed)return;resizing=true;try{int height=top.Controls.Cast<Control>().Select(c=>c.Bottom+c.Margin.Bottom).DefaultIfEmpty(64).Max()+2;if(layout.RowStyles[1].Height!=height)layout.RowStyles[1].Height=height;}finally{resizing=false;}};
        top.Layout+=delegate{fit();};top.SizeChanged+=delegate{fit();};fit();
    }
    static Label TopSuffix(string text,int width){return new Label{Text=text,Width=width,Height=27,TextAlign=ContentAlignment.MiddleLeft,Margin=Padding.Empty};}
    static Panel PredictionTopField(FlowLayoutPanel host,string title,int width,params Control[] inputs){
        var group=new Panel{Width=width,Height=62,Margin=new Padding(3,0,3,0)};group.Controls.Add(new Label{Text=title,Dock=DockStyle.Top,Height=23,Margin=Padding.Empty,TextAlign=ContentAlignment.MiddleLeft});
        var row=new FlowLayoutPanel{Location=new Point(0,24),Size=new Size(width,36),WrapContents=false,Margin=Padding.Empty,Padding=Padding.Empty};foreach(var input in inputs){input.Margin=new Padding(0,0,2,0);row.Controls.Add(input);}group.Controls.Add(row);host.Controls.Add(group);return group;
    }
    void FitForecastColumns(){
        SetFixedResultColumns(forecastGrid);
    }
    void SetFixedResultColumns(DataGridView grid){
        if(grid.Columns.Count==0)return;
        grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None;
        foreach(DataGridViewColumn column in grid.Columns){
            if(column is DataGridViewButtonColumn){column.AutoSizeMode=DataGridViewAutoSizeColumnMode.None;int buttonWidth=column.Name=="RegisterIndividual"?60:TextRenderer.MeasureText(column.HeaderText,grid.Font).Width+12;column.MinimumWidth=buttonWidth;column.Width=buttonWidth;continue;}
            IEnumerable<string> samples=new[]{column.HeaderText};
            switch(column.Name){
                case "Date":samples=samples.Concat(new[]{"2050/12/31"});break;
                case "Slot":samples=samples.Concat(new[]{"10枠・最初の10人","79枠・補充69枠"});break;
                case "Name":case "Parent1":case "Parent2":samples=samples.Concat(model.Data.names);break;
                case "Antenna":samples=samples.Concat(model.Antennas()).Concat(model.BirthAntennaFamilies());break;
                case "Body":samples=samples.Concat(model.Bodies().Select(c=>c.Text));break;
                case "Color1":case "Color2":samples=samples.Concat(model.Colors().Select(c=>c.Text));break;
                case "Head":samples=samples.Concat(model.Heads().Select(c=>c.Text));break;
                case "Pattern":samples=samples.Concat(model.Patterns(true).Select(c=>c.Text));break;
                case "Generation":samples=samples.Concat(new[]{"9～9","世代数"});break;
                case "Level":samples=samples.Concat(new[]{"999"});break;
                default:if(Model.FaceKeys.Contains(column.Name))samples=samples.Concat(new[]{"99"});break;
            }
            int width=samples.Max(s=>TextRenderer.MeasureText(s??"",grid.Font).Width)+8;
            column.AutoSizeMode=DataGridViewAutoSizeColumnMode.None;column.MinimumWidth=width;column.Width=width;
        }
    }
    static void ConfigureDate(DateTimePicker picker){picker.Format=DateTimePickerFormat.Custom;picker.CustomFormat="yyyy/MM/dd";picker.Width=145;picker.MinDate=new DateTime(2000,1,1);picker.MaxDate=new DateTime(2099,12,31);}
    void ConfigureColor(ComboBox box){box.Width=150;box.DropDownStyle=ComboBoxStyle.DropDownList;box.DisplayMember="Text";box.ValueMember="Value";box.DataSource=model.Colors();box.SelectedValue=-1;}
    public void SetFaceFilter(int[] values){if(values==null||values.Length!=Model.FaceNames.Length||values.Where((value,i)=>!model.FaceChoices(i).Contains(value)).Any())throw new Exception("顔パーツの指定が不正です。");faceSyncUpdating=true;try{for(int i=0;i<values.Length;i++)faceFilters[i].SelectedValue=values[i];}finally{faceSyncUpdating=false;}SyncForecastHair(true);}
    void SyncForecastHair(bool preserve){
        if(faceSyncUpdating)return;faceSyncUpdating=true;try{
            int hair=Convert.ToInt32(faceFilters[1].SelectedValue),contour=Convert.ToInt32(faceFilters[0].SelectedValue);
            faceFilters[0].Enabled=!forecastRunning&&hair==0;
            if(hair<0)faceFilters[0].SelectedValue=-1;
            else if(hair>0&&(!preserve||!model.IsKnownHairContour(hair,contour)))faceFilters[0].SelectedValue=Model.ContourForHair(hair);
        }finally{faceSyncUpdating=false;}
    }
    int[] ReadFaceFilter(){return faceFilters.Select(box=>Convert.ToInt32(box.SelectedValue)).ToArray();}
    public void ShowIdentify(){HideManualDopingMemo();identify.Visible=true;predict.Visible=gekiPage.Visible=birthPage.Visible=lineagePage.Visible=false;identify.BringToFront();identifyNav.BackColor=Color.LightBlue;predictNav.BackColor=gekiNav.BackColor=birthNav.BackColor=lineageNav.BackColor=SystemColors.Control;}
    public void ShowPredictor(){HideManualDopingMemo();predict.Visible=true;identify.Visible=gekiPage.Visible=birthPage.Visible=lineagePage.Visible=false;predict.BringToFront();predictNav.BackColor=Color.LightBlue;identifyNav.BackColor=gekiNav.BackColor=birthNav.BackColor=lineageNav.BackColor=SystemColors.Control;}
    void ShowError(Exception error){MessageBox.Show(this,error.GetBaseException().Message,"確認",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    const string IdentificationGuidance="複数の候補が出た場合は、新しい日付を追加して絞り込んでください。\r\n候補が出なかった場合は、本体の年を大きく変更し、新しく出現した10人を入力して再検索してください。";
    void UpdateDayButtons(){remove.Enabled=!running&&tabs.SelectedIndex>=2;add.Enabled=!running&&tabs.TabCount<31;add.Visible=remove.Visible=!identifySingle.Checked;}
    ComboBox AddSingleChoice(FlowLayoutPanel row,string name,string label,Choice[] choices,int width){
        var box=new ComboBox{Name="identifySingle"+name,DropDownStyle=ComboBoxStyle.DropDownList,Width=width,DisplayMember="Text",ValueMember="Value",BindingContext=new BindingContext(),DataSource=choices};singleChoices.Add(name,box);var group=new Panel{Width=width,Height=58,Margin=new Padding(5,3,5,3)};group.Controls.Add(new Label{Text=label,Dock=DockStyle.Top,Height=23});box.Location=new Point(0,24);group.Controls.Add(box);row.Controls.Add(group);return box;
    }
    FlowLayoutPanel SingleRow(TableLayoutPanel layout,int index){var row=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true,AutoScroll=true};layout.Controls.Add(row,0,index);return row;}
    void BuildSingleIdentification(){
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,AutoScroll=true};foreach(int height in new[]{46,92,92,92,0})layout.RowStyles.Add(height==0?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.Absolute,height));singleInput.Controls.Add(layout);
        var first=SingleRow(layout,0);ConfigureDate(singleDate);singleDate.Name="identifySingleDate";singleDate.Value=new DateTime(2000,7,22);first.Controls.Add(LabelFor("日付"));first.Controls.Add(singleDate);first.Controls.Add(LabelFor("名前"));singleName.Name="identifySingleName";singleName.Width=150;singleName.AutoCompleteMode=AutoCompleteMode.SuggestAppend;singleName.AutoCompleteSource=AutoCompleteSource.CustomSource;singleName.AutoCompleteCustomSource.AddRange(model.Data.names);first.Controls.Add(singleName);
        var appearance=SingleRow(layout,1);var antennas=new[]{new Choice{Value=-1,Text="未指定"}}.Concat(model.Antennas().Select((a,i)=>new Choice{Value=i,Text=a})).ToArray();AddSingleChoice(appearance,"Antenna","アンテナ",antennas,200);AddSingleChoice(appearance,"Head","頭",model.RareHeads(),125);AddSingleChoice(appearance,"Body","体格",model.RareBodyChoices(),120);
        var colors=SingleRow(layout,2);AddSingleChoice(colors,"Color1","色1",model.SingleIdentificationColors(),120);AddSingleChoice(colors,"Color2","色2",model.SingleIdentificationColors(),120);singleReverseColors.Name="identifySingleAllowReverseColors";singleReverseColors.Text="逆順を許可";singleReverseColors.Checked=true;singleReverseColors.AutoSize=true;singleReverseColors.Margin=new Padding(5,28,5,3);colors.Controls.Add(singleReverseColors);AddSingleChoice(colors,"Pattern","柄",model.PatternFilterChoices(),125);
        AddSingleChoice(colors,"Generation","現在世代",NumberChoices(model.MaxGeneration),80);AddSingleChoice(colors,"GenerationLimit","上限世代",NumberChoices(model.MaxGenerationLimit),80);AddSingleChoice(colors,"Level","レベル",new[]{new Choice{Value=-1,Text="未指定"},new Choice{Value=10,Text="10"},new Choice{Value=16,Text="16"}},80);
        var faces=SingleRow(layout,3);for(int i=0;i<Model.FaceNames.Length;i++)AddSingleChoice(faces,"Face"+i,Model.FaceNames[i],model.FaceChoices(i).Select(v=>new Choice{Value=v,Text=v<0?"未指定":v.ToString("D2")}).ToArray(),70);
    }
    static Choice[] NumberChoices(int max){return new[]{new Choice{Value=-1,Text="未指定"}}.Concat(Enumerable.Range(1,max).Select(i=>new Choice{Value=i,Text=i.ToString()})).ToArray();}
    internal static void StackFieldLabels(FlowLayoutPanel row){
        var controls=row.Controls.Cast<Control>().ToArray();row.Controls.Clear();row.WrapContents=false;
        for(int i=0;i<controls.Length;i++){
            var label=controls[i] as Label;if(label!=null&&i+1<controls.Length){var input=controls[++i];int width=input.Width;var group=new Panel{Width=width,Height=58,Margin=new Padding(3,0,3,0)};label.Dock=DockStyle.Top;label.AutoSize=false;label.Height=23;label.Padding=Padding.Empty;label.Margin=Padding.Empty;input.Location=new Point(0,24);input.Margin=Padding.Empty;group.Controls.Add(label);group.Controls.Add(input);row.Controls.Add(group);}
            else {controls[i].Margin=new Padding(3,25,3,0);row.Controls.Add(controls[i]);}
        }
    }
    void UpdateIdentificationMode(){
        bool single=identifySingle.Checked;singleInput.Visible=single;tabs.Visible=!single;if(single)singleInput.BringToFront();else tabs.BringToFront();
        identifyDescription.Text=(single?"捕獲・補充前の10人にいた1人の情報を入力してください。":"レアキャッチに出現した10人の情報を、確認できる範囲で入力してください。")+" 体色黒は、濃淡設定が出来ません。";UpdateDayButtons();
    }
    int SingleValue(string name){return (int)singleChoices[name].SelectedValue;}
    Day[] ReadSingleDay(){var ant=singleChoices["Antenna"];var p=new Person{Name=singleName.Text,Antenna=SingleValue("Antenna")<0?"":ant.Text,Color1=SingleValue("Color1"),Color2=SingleValue("Color2"),Head=SingleValue("Head"),Body=SingleValue("Body"),Pattern=SingleValue("Pattern"),Generation=SingleValue("Generation"),GenerationLimit=SingleValue("GenerationLimit"),Level=SingleValue("Level"),Face=Enumerable.Range(0,Model.FaceNames.Length).Select(i=>SingleValue("Face"+i)).ToArray()};var days=new[]{new Day{SinglePerson=true,Date=singleDate.Value.ToString("yyyy-MM-dd"),ColorEncoding="color-query-v2",AllowReverseColors=singleReverseColors.Checked,People=new[]{p}}};model.Compile(days);return days;}
    void SetSingleDay(Day day){var p=day.People[0];singleReverseColors.Checked=day.AllowReverseColors;singleDate.Value=DateTime.ParseExact(day.Date,"yyyy-MM-dd",CultureInfo.InvariantCulture);singleName.Text=p.Name;var ant=singleChoices["Antenna"];ant.SelectedIndex=String.IsNullOrEmpty(p.Antenna)?0:Array.FindIndex(((Choice[])ant.DataSource),c=>c.Text==p.Antenna);if(ant.SelectedIndex<0)ant.SelectedIndex=0;
        foreach(var pair in new[]{new {Name="Color1",Value=p.Color1>=3&&p.Color1<=5?103:p.Color1},new {Name="Color2",Value=p.Color2>=3&&p.Color2<=5?103:p.Color2},new {Name="Head",Value=p.Head},new {Name="Body",Value=p.Body},new {Name="Pattern",Value=p.Pattern},new {Name="Generation",Value=p.Generation},new {Name="GenerationLimit",Value=p.GenerationLimit},new {Name="Level",Value=p.Level}})singleChoices[pair.Name].SelectedValue=pair.Value;
        for(int i=0;i<Model.FaceNames.Length;i++)singleChoices["Face"+i].SelectedValue=p.Face==null?-1:p.Face[i];identifySingle.Checked=true;
    }
    void AddDay(Day data){
        var page=new TabPage();var panel=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1};panel.RowStyles.Add(new RowStyle(SizeType.Absolute,36));panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var date=new DateTimePicker();ConfigureDate(date);DateTime initial=new DateTime(2000,7,22).AddDays(tabs.TabCount);if(data==null){while(dates.Values.Any(d=>d.Value.Date==initial))initial=initial.AddDays(1);}else initial=DateTime.ParseExact(data.Date,"yyyy-MM-dd",CultureInfo.InvariantCulture);date.Value=initial;page.Text=date.Value.ToString("yyyy/MM/dd");date.ValueChanged+=delegate{page.Text=date.Value.ToString("yyyy/MM/dd");};panel.Controls.Add(date,0,0);dates[page]=date;
        var grid=new DataGridView{Dock=DockStyle.Fill,Name="observations",AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,BackgroundColor=Color.White,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,EditMode=DataGridViewEditMode.EditOnEnter,ColumnHeadersHeight=34,RowTemplate={Height=29}};
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="person",HeaderText="名前",FillWeight=24});
        var ability=new DataGridViewComboBoxColumn{Name="antenna",HeaderText="アンテナ",FillWeight=38,FlatStyle=FlatStyle.Flat};ability.Items.Add("未指定");ability.Items.AddRange(model.Antennas().Cast<object>().ToArray());
        if(data!=null)foreach(string old in data.People.Select(p=>p.Antenna).Where(a=>!String.IsNullOrEmpty(a)&&a!="アンテナのねっこ").Distinct())if(!ability.Items.Contains(old))ability.Items.Add(old);
        grid.Columns.Add(ability);
        foreach(string name in new[]{"color1","color2"})grid.Columns.Add(new DataGridViewComboBoxColumn{Name=name,HeaderText=name=="color1"?"色1":"色2",FillWeight=24,FlatStyle=FlatStyle.Flat,DisplayMember="Text",ValueMember="Value",ValueType=typeof(int),DataSource=model.IdentificationColors()});
        grid.Columns.Add(new DataGridViewComboBoxColumn{Name="head",HeaderText="頭",FillWeight=24,FlatStyle=FlatStyle.Flat,DisplayMember="Text",ValueMember="Value",ValueType=typeof(int),DataSource=model.RareHeads()});
        grid.Columns.Add(new DataGridViewComboBoxColumn{Name="pattern",HeaderText="柄",FillWeight=24,FlatStyle=FlatStyle.Flat,DisplayMember="Text",ValueMember="Value",ValueType=typeof(int),DataSource=model.IdentificationPatterns()});
        string[] displayOrder={"person","antenna","head","color1","color2","pattern"};for(int i=0;i<displayOrder.Length;i++)grid.Columns[displayOrder[i]].DisplayIndex=i;
        for(int i=0;i<10;i++){var person=data==null?new Person():data.People[i];int row=grid.Rows.Add();grid.Rows[row].SetValues(person.Name,String.IsNullOrEmpty(person.Antenna)||person.Antenna=="アンテナのねっこ"?"未指定":person.Antenna,Model.IdentificationColor(person.Color1),Model.IdentificationColor(person.Color2),person.Head,person.Pattern);}
        grid.EditingControlShowing+=delegate(object sender,DataGridViewEditingControlShowingEventArgs e){var text=e.Control as TextBox;if(text!=null&&grid.CurrentCell.ColumnIndex==0){text.AutoCompleteMode=AutoCompleteMode.SuggestAppend;text.AutoCompleteSource=AutoCompleteSource.CustomSource;var source=new AutoCompleteStringCollection();source.AddRange(model.Data.names);text.AutoCompleteCustomSource=source;}var combo=e.Control as ComboBox;if(combo!=null){combo.DropDownStyle=ComboBoxStyle.DropDownList;}};
        grid.DataError+=delegate(object sender,DataGridViewDataErrorEventArgs e){e.ThrowException=false;};panel.Controls.Add(grid,0,1);page.Controls.Add(panel);tabs.TabPages.Add(page);tabs.SelectedTab=page;
    }
    public void SetDays(Day[] days){model.Compile(days);if(days.Any(d=>d.SinglePerson)){if(days.Length!=1||!days[0].SinglePerson)throw new Exception("1人モードは1日分ずつ検索してください。");SetSingleDay(days[0]);return;}identifyTen.Checked=true;foreach(TabPage page in tabs.TabPages.Cast<TabPage>().ToArray()){tabs.TabPages.Remove(page);page.Dispose();}dates.Clear();foreach(var day in days)AddDay(day);while(tabs.TabCount<2)AddDay(null);tabs.SelectedIndex=0;UpdateDayButtons();}
    public Day[] ReadDays(){
        if(identifySingle.Checked)return ReadSingleDay();
        var output=new List<Day>();foreach(TabPage page in tabs.TabPages){var grid=(DataGridView)page.Controls.Find("observations",true)[0];if(!grid.EndEdit())throw new Exception("編集中の選択肢を確定してください。");var people=new List<Person>();foreach(DataGridViewRow row in grid.Rows){string antenna=Convert.ToString(row.Cells["antenna"].Value);people.Add(new Person{Name=Convert.ToString(row.Cells["person"].Value),Antenna=antenna=="未指定"?"":antenna,Color1=Convert.ToInt32(row.Cells["color1"].Value),Color2=Convert.ToInt32(row.Cells["color2"].Value),Head=Convert.ToInt32(row.Cells["head"].Value),Pattern=Convert.ToInt32(row.Cells["pattern"].Value)});}if(people.All(p=>String.IsNullOrWhiteSpace(p.Name)&&p.Antenna==""&&p.Color1==-1&&p.Color2==-1&&p.Head==-1&&p.Pattern==-1))continue;output.Add(new Day{Date=dates[page].Value.ToString("yyyy-MM-dd"),ColorEncoding="color-query-v2",People=people.ToArray()});}var days=output.ToArray();model.Compile(days);return days;
    }
    public void ClearIdentificationHistory(){
        if(running||(task!=null&&!task.IsCompleted))throw new InvalidOperationException("中断してからリセットしてください。");
        job=null;task=null;previous=null;
        singleName.Clear();singleReverseColors.Checked=true;singleDate.Value=new DateTime(2000,7,22);foreach(var box in singleChoices.Values)box.SelectedIndex=0;identifyTen.Checked=true;
        foreach(TabPage page in tabs.TabPages.Cast<TabPage>().ToArray()){tabs.TabPages.Remove(page);page.Dispose();}dates.Clear();
        AddDay(null);AddDay(null);tabs.SelectedIndex=0;UpdateDayButtons();
        useTimeLimit.Checked=false;minutes.Value=10;workers.Value=2;
        UpdateState(new State());status.Text="未探索。10人の情報を入力して検索してください。\r\n"+IdentificationGuidance;
    }
    void SetRunning(bool value){running=value;tabs.Enabled=singleInput.Enabled=identifyTen.Enabled=identifySingle.Enabled=start.Enabled=useTimeLimit.Enabled=workers.Enabled=deleteIdentificationHistory.Enabled=!value;minutes.Enabled=!value&&useTimeLimit.Checked;stop.Enabled=value;UpdateDayButtons();}
    public void BeginSearch(){try{
        var days=ReadDays();State state;
        if(previous!=null&&previous.Engine==State.CurrentEngine&&previous.DataHash==model.DataHash&&SearchJob.CanRefine(previous.Days,days)){state=previous;var compiled=model.Compile(days);state.Keys=state.Keys.Where(key=>model.Matches(key,compiled)).ToList();state.Days=days;}else state=new State{Days=days,DataHash=model.DataHash};
        previous=state;job=new SearchJob(model,state,null);SetRunning(true);double seconds=useTimeLimit.Checked?(double)minutes.Value*60:Double.PositiveInfinity;int count=(int)workers.Value;task=Task.Factory.StartNew(()=>job.Run(seconds,count));
    }catch(Exception error){ShowError(error);}}
    public void UpdateState(State state){
        uint? selected=results.SelectedIndex>=0&&results.SelectedIndex<resultValues.Count?(uint?)resultValues[results.SelectedIndex]:null;
        progress.Value=(int)Math.Min(1000,(double)state.Next/4294967296.0*1000);
        status.Text=state.Status+"\r\n探索済み："+state.Next.ToString("N0")+" / 4,294,967,296\r\n候補："+state.Keys.Count+"件 "+(state.Next<0x100000000UL?"（探索途中・暫定）":"（全域終了・実機で再確認）")+"\r\n"+(identifySingle.Checked?"捕獲・補充前の10人すべてが照合対象です。\r\n候補が多い場合は、体格・顔パーツなどの条件を追加して再検索してください。":IdentificationGuidance);
        results.BeginUpdate();try{results.Items.Clear();resultValues.Clear();foreach(uint key in state.Keys){resultValues.Add(key);results.Items.Add(key.ToString("X8")+(state.Next<0x100000000UL?"  暫定":"  全域探索済み"));}if(resultValues.Count>0){int index=selected.HasValue?resultValues.IndexOf(selected.Value):-1;results.SelectedIndex=index<0?0:index;}}finally{results.EndUpdate();}UpdateSelectedKey();
    }
    void UpdateSelectedKey(){bool selected=results.SelectedIndex>=0&&results.SelectedIndex<resultValues.Count;if(selected)resultKey.Text=resultValues[results.SelectedIndex].ToString("X8");copy.Enabled=use.Enabled=resultKey.Text.Length==8;keyNote.Text=resultKey.Text.Length==8?"通常レア用の予測キー（16進数8桁）":"";}
    public void CopySelectedKey(){if(resultKey.Text.Length!=8)throw new Exception("結果から予測キーを選んでください。");CopyToClipboard(resultKey.Text);keyNote.Text="コピーしました。";}
    public void UseSelectedKey(){if(resultKey.Text.Length!=8)return;forecastKey.Text=resultKey.Text;SavePreferences();ShowPredictor();}
    public void SetPredictionKey(string key){forecastKey.Text=Model.ParseKey(key).ToString("X8");}
    public void SetFullSearchRange(){dateStart.Value=dateStart.MinDate;dateEnd.Value=dateEnd.MaxDate;}
    public void ResetForecastFilters(){
        if(forecastRunning)return;
        nameFilter.Clear();abilityFilter.SelectedIndex=0;headFilter.SelectedValue=-1;colorFilter1.SelectedValue=-1;colorFilter2.SelectedValue=-1;allowReverseColors.Checked=true;patternFilter.SelectedValue=-1;bodyFilter.SelectedValue=-1;
        slotLimit.Value=0;includedGenerationFilter.SelectedValue=-1;generationFilterEnabled.Checked=false;minGenerationFilter.SelectedValue=maxGenerationFilter.SelectedValue=1;levelFilter.SelectedValue=-1;SetFaceFilter(Enumerable.Repeat(-1,Model.FaceNames.Length).ToArray());
    }
    public List<ForecastRow> ForecastNow(){return model.Forecast(Model.ParseKey(forecastKey.Text),dateStart.Value,dateEnd.Value,nameFilter.Text.Trim(),abilityFilter.SelectedIndex<=0?"":abilityFilter.Text,Convert.ToInt32(colorFilter1.SelectedValue),Convert.ToInt32(colorFilter2.SelectedValue),10+(int)slotLimit.Value,Convert.ToInt32(headFilter.SelectedValue),ReadFaceFilter(),(int)minGenerationFilter.SelectedValue,(int)maxGenerationFilter.SelectedValue,Convert.ToInt32(bodyFilter.SelectedValue),Convert.ToInt32(patternFilter.SelectedValue),allowReverseColors.Checked,Convert.ToInt32(levelFilter.SelectedValue),generationFilterEnabled.Checked,Convert.ToInt32(includedGenerationFilter.SelectedValue));}
    public Task RunForecast(){return RunForecastSessionAsync(false);}
    void SetForecastControls(bool enabled){foreach(Control control in new Control[]{forecastButton,forecastResetButton,forecastKey,rememberKey,deleteKey,rememberedKeys,dateStart,dateEnd,fullRangeButton,nameFilter,abilityFilter,colorFilter1,colorFilter2,allowReverseColors,patternFilter,headFilter,bodyFilter,slotLimit,generationFilterEnabled,includedGenerationFilter,levelFilter})control.Enabled=enabled;minGenerationFilter.Enabled=maxGenerationFilter.Enabled=enabled&&generationFilterEnabled.Checked;foreach(var box in faceFilters)box.Enabled=enabled;SyncForecastHair(true);}
    void RefreshStatus(){if(job!=null){lock(job.Sync)UpdateState(job.State);}if(running&&task!=null&&task.IsCompleted){SetRunning(false);if(task.IsFaulted)ShowError(task.Exception);if(closeAfter)Close();}}
    void SavePreferences(){Json.Write(preferencesPath,new Preferences{DateRangeVersion=1,GenerationFilterVersion=2,IncludedGeneration=Convert.ToInt32(includedGenerationFilter.SelectedValue),UseGenerationFilter=generationFilterEnabled.Checked,Key=forecastKey.Text,Start=dateStart.Value.ToString("yyyy-MM-dd"),End=dateEnd.Value.ToString("yyyy-MM-dd"),Slots=10+(int)slotLimit.Value,AllowReverseColors=allowReverseColors.Checked,Head=Convert.ToInt32(headFilter.SelectedValue),Body=Convert.ToInt32(bodyFilter.SelectedValue),Pattern=Convert.ToInt32(patternFilter.SelectedValue),MinGeneration=(int)minGenerationFilter.SelectedValue,MaxGeneration=(int)maxGenerationFilter.SelectedValue,Level=Convert.ToInt32(levelFilter.SelectedValue),Face=ReadFaceFilter()});}
    void LoadPreferences(){
        if(!File.Exists(preferencesPath))return;
        try{
            var p=Json.Read<Preferences>(preferencesPath);if(!String.IsNullOrEmpty(p.Key))SetPredictionKey(p.Key);
            DateTime first,last;
            if(p.DateRangeVersion==1&&DateTime.TryParseExact(p.Start,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out first)&&DateTime.TryParseExact(p.End,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out last)&&first>=dateStart.MinDate&&last<=dateEnd.MaxDate&&first<=last){dateStart.Value=first;dateEnd.Value=last;}
            // Stored Slots remains total people for compatibility with earlier versions.
            if(p.Slots>=1&&p.Slots<=79)slotLimit.Value=Math.Max(0,p.Slots-10);
            allowReverseColors.Checked=p.AllowReverseColors;
            if(model.RareHeads().Any(h=>h.Value==p.Head))headFilter.SelectedValue=p.Head;
            if(model.BodyFilterChoices(true).Any(b=>b.Value==p.Body))bodyFilter.SelectedValue=p.Body;
            if(model.PatternFilterChoices().Any(v=>v.Value==p.Pattern))patternFilter.SelectedValue=p.Pattern;
            if(p.GenerationFilterVersion==2){includedGenerationFilter.SelectedValue=p.IncludedGeneration>=1&&p.IncludedGeneration<=model.MaxGenerationLimit?p.IncludedGeneration:-1;if(p.MinGeneration>=1&&p.MinGeneration<=model.MaxGeneration)minGenerationFilter.SelectedValue=p.MinGeneration;if(p.MaxGeneration>=1&&p.MaxGeneration<=model.MaxGenerationLimit)maxGenerationFilter.SelectedValue=p.MaxGeneration;generationFilterEnabled.Checked=p.UseGenerationFilter;}
            if(p.Level==-1||p.Level==10||p.Level==16)levelFilter.SelectedValue=p.Level;
            if(p.Face!=null)SetFaceFilter(p.Face);
        }catch(Exception error){forecastStatus.Text="保存した予測設定を読めません："+error.Message;}
    }
    void LoadRememberedKeys(){
        if(!File.Exists(rememberedKeysPath))return;
        try{var values=Json.Read<string[]>(rememberedKeysPath);if(values==null)throw new Exception("キー一覧の形式が異なります。");var normalized=values.Select(v=>Model.ParseKey(v).ToString("X8")).Distinct().ToArray();rememberedKeys.Items.AddRange(normalized);rememberedKeys.SelectedIndex=-1;}
        catch(Exception e){rememberedKeysLoadFailed=true;forecastStatus.Text="記憶したキーを読めません。元のファイルは変更していません："+e.Message;}
    }
    public void RememberPredictionKey(){
        if(forecastRunning||rememberedKeysLoadFailed)throw new Exception("検索中、または記憶一覧を読み込めなかったため変更できません。");
        string key=Model.ParseKey(forecastKey.Text).ToString("X8");int existing=rememberedKeys.Items.IndexOf(key);
        if(existing>=0){rememberedKeys.SelectedIndex=existing;forecastKey.Text=key;return;}
        var values=rememberedKeys.Items.Cast<string>().Concat(new[]{key}).ToArray();Json.Write(rememberedKeysPath,values);rememberedKeys.Items.Add(key);rememberedKeys.SelectedIndex=rememberedKeys.Items.Count-1;
    }
    public void DeleteRememberedPredictionKey(int index){
        if(forecastRunning||rememberedKeysLoadFailed)throw new Exception("検索中、または記憶一覧を読み込めなかったため変更できません。");
        if(index<0||index>=rememberedKeys.Items.Count)throw new Exception("削除する予測キーを選んでください。");
        var values=rememberedKeys.Items.Cast<string>().Where((v,i)=>i!=index).ToArray();Json.Write(rememberedKeysPath,values);string input=forecastKey.Text;rememberedKeys.Items.RemoveAt(index);rememberedKeys.SelectedIndex=-1;forecastKey.Text=input;
    }
    public void SaveScreenshot(string path){using(var image=new Bitmap(Width,Height)){DrawToBitmap(image,new Rectangle(0,0,Width,Height));image.Save(path);}}
}
static class Program {
    [STAThread]static int Main(string[] args){try{
        string root=AppDomain.CurrentDomain.BaseDirectory;var model=new Model(Path.Combine(root,"data","model.json"));
        if(args.Length==3&&args[0]=="--forward"){Json.Write(args[2],model.Forward(UInt32.Parse(args[1],CultureInfo.InvariantCulture)));return 0;}
        if(args.Length==4&&args[0]=="--sequence"){Json.Write(args[3],model.Forward(UInt32.Parse(args[1],CultureInfo.InvariantCulture),Int32.Parse(args[2],CultureInfo.InvariantCulture)));return 0;}
        if(args.Length==4&&args[0]=="--check"){Json.Write(args[3],new{matched=model.Matches(UInt32.Parse(args[2],CultureInfo.InvariantCulture),model.Compile(Json.Read<Day[]>(args[1])))});return 0;}
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new ToolForm(model,root));return 0;
    }catch(Exception error){if(args.Length==0)MessageBox.Show(error.GetBaseException().Message,"起動できませんでした",MessageBoxButtons.OK,MessageBoxIcon.Error);else Console.Error.WriteLine(error);return 1;}}
}
}
