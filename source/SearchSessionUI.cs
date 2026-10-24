using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace FreeNormalFinder {
public sealed class ForecastSearchInput {
    public uint Key;public DateTime First,Last;public string Name,Antenna;public int Color1,Color2,Slots,Head,MinGeneration,MaxGeneration,Body,Pattern,Level,IncludedGeneration;public int[] Face;public bool Reverse,UseGeneration;
    public long Total {get{return Math.Max(0,(long)(Last.Date-First.Date).TotalDays+1)*Slots;}}
    public IEnumerable<ForecastRow> Steps(Model m){return m.ForecastSteps(Key,First,Last,Name,Antenna,Color1,Color2,Slots,Head,Face,MinGeneration,MaxGeneration,Body,Pattern,Reverse,Level,UseGeneration,IncludedGeneration);}
    [System.Web.Script.Serialization.ScriptIgnore] public string Signature {get{return Json.Serializer().Serialize(this);}}
}
public sealed partial class ToolForm {
    readonly Button forecastPauseResume=new Button{Name="forecastPauseResume",Text="検索中断/再開",AutoSize=true,Enabled=false},birthPauseResume=new Button{Name="birthPauseResume",Text="検索中断/再開",AutoSize=true,Enabled=false};
    ResumableSearch<ForecastRow> forecastSession;ResumableSearch<BirthResult> birthSession;string forecastSessionSignature,birthSessionSignature;
    ForecastSnapshot birthSessionA,birthSessionB;BirthParent[] birthSessionParentsA,birthSessionParentsB;
    bool birthRunning,searchCloseAfter;int forecastPublishedCount,birthPublishedCount;Control[] birthSearchInputs;Button birthRunButton;ProgressBar birthSearchProgress;Label birthSearchProgressText;
    public long ForecastSearchPosition {get{return forecastSession==null?0:forecastSession.Processed;}}public long BirthSearchPosition {get{return birthSession==null?0:birthSession.Processed;}}
    public bool ForecastSearchAtLimit {get{return forecastSession!=null&&forecastSession.LimitReached;}}public bool BirthSearchAtLimit {get{return birthSession!=null&&birthSession.LimitReached;}}
    public bool ForecastSearchRunning {get{return forecastRunning;}}public bool BirthSearchRunning {get{return birthRunning;}}
    ForecastSearchInput ReadForecastSearchInput(){return new ForecastSearchInput{Key=Model.ParseKey(forecastKey.Text),First=dateStart.Value.Date,Last=dateEnd.Value.Date,Name=nameFilter.Text.Trim(),Antenna=abilityFilter.SelectedIndex<=0?"":abilityFilter.Text,Color1=Convert.ToInt32(colorFilter1.SelectedValue),Color2=Convert.ToInt32(colorFilter2.SelectedValue),Slots=10+(int)slotLimit.Value,Head=Convert.ToInt32(headFilter.SelectedValue),Face=ReadFaceFilter(),MinGeneration=(int)minGenerationFilter.Value,MaxGeneration=(int)maxGenerationFilter.Value,Body=Convert.ToInt32(bodyFilter.SelectedValue),Pattern=Convert.ToInt32(patternFilter.SelectedValue),Reverse=allowReverseColors.Checked,Level=Convert.ToInt32(levelFilter.SelectedValue),UseGeneration=generationFilterEnabled.Checked,IncludedGeneration=Convert.ToInt32(includedGenerationFilter.SelectedValue)};}
    static string SearchState<T>(ResumableSearch<T> session)where T:class{return session.Complete?"検索完了":session.LimitReached?"10万件の上限で中断":"中断";}
    public void StopForecastSearch(){if(forecastSession!=null)forecastSession.StopRequested=true;}
    public void StopBirthSearch(){if(birthSession!=null)birthSession.StopRequested=true;}
    public async Task ToggleForecastSearchAsync(){if(forecastRunning){StopForecastSearch();return;}await RunForecastSessionAsync(true);}
    public async Task ToggleBirthSearchAsync(){if(birthRunning){StopBirthSearch();return;}await RunBirthSearchAsync(true);}
    async Task RunForecastSessionAsync(bool resume){
        if(forecastRunning||savingBirthInput||birthRunning)return;
        try{var input=ReadForecastSearchInput();string signature=input.Signature;
            if(resume){if(forecastSession==null||forecastSession.Complete)return;if(signature!=forecastSessionSignature)throw new Exception("検索条件が変更されています。「検索実行」で新しい検索を始めてください。");}
            else{if(forecastSession!=null)forecastSession.Dispose();forecastSession=new ResumableSearch<ForecastRow>(input.Steps(model),input.Total);forecastSessionSignature=signature;forecastPublishedCount=0;}
            forecastSession.PrepareResume();forecastRunning=true;SetForecastControls(false);forecastPauseResume.Enabled=true;forecastKey.Text=input.Key.ToString("X8");forecastStatus.Text="検索中…";
            await Task.Run(()=>forecastSession.Run());if(IsDisposed)return;
            var rows=forecastSession.Snapshot();forecastGrid.DataSource=rows;if(rows.Length>forecastPublishedCount){RecordForecastHistory(rows);forecastPublishedCount=rows.Length;}SavePreferences();forecastStatus.Text=SearchState(forecastSession)+"："+forecastSession.Processed.ToString("N0")+" / "+forecastSession.Total.ToString("N0")+"枠、一致 "+rows.Length.ToString("N0")+"人。"+(forecastSession.LimitReached?"再開しても上限を超える結果は追加しません。":"");
        }catch(Exception error){if(!IsDisposed)ShowError(error);}finally{forecastRunning=false;if(!IsDisposed){SetForecastControls(true);forecastPauseResume.Enabled=forecastSession!=null&&!forecastSession.Complete;if(searchCloseAfter){searchCloseAfter=false;Close();}}}
    }
    string BirthSearchSignature(){return Json.Serializer().Serialize(new{Mode=BirthMode,Name=birthNameFilter.Text,Antenna=birthAntennaFilter.SelectedIndex,Reverse=birthReverseColors.Checked,Values=birthFilters.OrderBy(p=>p.Key).Select(p=>new[]{p.Key,Convert.ToInt32(p.Value.SelectedValue)}).ToArray()});}
    IEnumerable<BirthResult> FilterSearchSteps(ForecastSnapshot batch,Func<BirthResult,bool> matches){foreach(var parent in EnumerateBirthParents(batch)){var row=BirthRowForParent(parent,false);if(matches(row)){row.Lineage=Model.CaptureBirthParent(parent);yield return row;}else yield return null;}}
    public async Task RunBirthSearchAsync(bool resume=false){
        if(birthRunning||savingBirthInput||forecastRunning||!birthModeBirth.Checked&&!birthModeFilter.Checked)return;
        try{var a=SelectedBirthBatch(0);var b=birthModeBirth.Checked?SelectedBirthBatch(1):null;await LoadBirthInputsAsync(a,b);string signature=BirthSearchSignature();
            if(resume){if(birthSession==null||birthSession.Complete)return;if(signature!=birthSessionSignature||!Object.ReferenceEquals(a,birthSessionA)||!Object.ReferenceEquals(b,birthSessionB)||!Object.ReferenceEquals(a.Parents,birthSessionParentsA)||b!=null&&!Object.ReferenceEquals(b.Parents,birthSessionParentsB))throw new Exception("入力や検索条件が変更されています。「検索実行」で新しい検索を始めてください。");}
            else{if(birthSession!=null)birthSession.Dispose();var matches=CaptureBirthResultFilter();long total=b==null?a.Count:(long)a.Count*b.Count;birthSession=new ResumableSearch<BirthResult>(b==null?FilterSearchSteps(a,matches):BirthSearchSteps(a,b,true,matches),total);birthSessionA=a;birthSessionB=b;birthSessionParentsA=a.Parents;birthSessionParentsB=b==null?null:b.Parents;birthSessionSignature=signature;birthPublishedCount=0;}
            birthSession.PrepareResume();birthRunning=true;var enabled=birthSearchInputs.Where(c=>c!=null).ToDictionary(c=>c,c=>c.Enabled);foreach(var c in enabled.Keys)c.Enabled=false;birthPauseResume.Enabled=true;
            Action update=delegate{long done=birthSession.Processed,total=birthSession.Total;birthSearchProgress.Value=total==0?0:(int)Math.Min(1000,done*1000/total);birthSearchProgressText.Text=(birthSearchProgress.Value/10.0).ToString("0.0")+"%（"+done+" / "+total+"）";};
            using(var timer=new Timer{Interval=100}){timer.Tick+=delegate{update();};timer.Start();try{await Task.Run(()=>birthSession.Run());if(IsDisposed)return;birthSearchResults=birthSession.Snapshot();birthResultGrid.DataSource=birthSearchResults;if(birthSearchResults.Length>birthPublishedCount){RecordBirthHistory();birthPublishedCount=birthSearchResults.Length;}update();birthSearchProgressText.Text=SearchState(birthSession)+"　"+birthSearchProgressText.Text+"　一致 "+birthSearchResults.Length.ToString("N0")+"件";}finally{timer.Stop();if(!IsDisposed)foreach(var pair in enabled)pair.Key.Enabled=pair.Value;}}
        }catch(Exception error){if(!IsDisposed)ShowError(error);}finally{birthRunning=false;if(!IsDisposed){birthPauseResume.Enabled=(birthModeBirth.Checked||birthModeFilter.Checked)&&birthSession!=null&&!birthSession.Complete;if(searchCloseAfter){searchCloseAfter=false;Close();}}}
    }
    void InstallSearchSessionLifetime(){FormClosing+=delegate(object sender,FormClosingEventArgs e){if(forecastRunning||birthRunning){e.Cancel=true;searchCloseAfter=true;StopForecastSearch();StopBirthSearch();}};FormClosed+=delegate{if(forecastSession!=null)forecastSession.Dispose();if(birthSession!=null)birthSession.Dispose();};}
}
}
