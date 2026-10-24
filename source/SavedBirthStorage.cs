using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Runtime.Serialization.Json;
using System.Collections.Generic;
namespace FreeNormalFinder {
public sealed partial class ToolForm {
    string SavedInputPath(string fileName){
        if(String.IsNullOrEmpty(fileName)||Path.GetFileName(fileName)!=fileName||Path.GetExtension(fileName)!=".json")throw new Exception("保存ファイル名が不正です。");
        string directory=Path.GetFullPath(savedInputsDirectory),path=Path.GetFullPath(Path.Combine(directory,fileName));if(!String.Equals(Path.GetDirectoryName(path),directory,StringComparison.OrdinalIgnoreCase))throw new Exception("保存先が異なります。");return path;
    }
    string NewSavedInputFileName(string label){
        const string invalid="<>:\"/\\|?*";const string replacement="＜＞：＂／＼｜？＊";var text=new StringBuilder();
        foreach(char c in label.Trim()){int index=invalid.IndexOf(c);text.Append(c<32?'＿':index>=0?replacement[index]:c);}string stem=text.ToString().TrimEnd(' ','.');if(stem.Length==0)stem="保存個体";
        if(Regex.IsMatch(stem,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\.)",RegexOptions.IgnoreCase))stem="＿"+stem;
        int available=Math.Min(80,240-Path.GetFullPath(savedInputsDirectory).Length-12);if(available<8)throw new Exception("保存先のパスが長すぎます。アプリを短い場所に配置してください。");if(stem.Length>available)stem=stem.Substring(0,available).TrimEnd(' ','.');if(Char.IsHighSurrogate(stem[stem.Length-1]))stem=stem.Substring(0,stem.Length-1);
        for(int number=1;number<1000000;number++){string name=stem+(number==1?"":" ("+number+")")+".json";if(!File.Exists(SavedInputPath(name)))return name;}throw new Exception("同名の保存ファイルが多すぎます。");
    }
    SavedBirthInput ReadSavedHeader(string path){
        if(new FileInfo(path).Length>256L*1024*1024)throw new Exception("保存ファイルが大きすぎます。");
        using(var reader=new StreamReader(path,Encoding.UTF8,true)){char[] buffer=new char[65536];int count=reader.ReadBlock(buffer,0,buffer.Length);string prefix=new string(buffer,0,count);var match=Regex.Match(prefix,",\\s*\"Parents\"\\s*:");if(!match.Success)throw new Exception("保存ヘッダーが見つかりません。");var header=SavedInputSerializer().Deserialize<SavedBirthInput>(prefix.Substring(0,match.Index)+"}");Guid id;if(header.Version!=1||!Guid.TryParseExact(header.Id,"N",out id)||String.IsNullOrWhiteSpace(header.Label)||header.Label.Length>80)throw new Exception("保存ヘッダーが不正です。");return header;}
    }
    BirthParent[] ReadSavedParents(string path,string id,string label){
        if(new FileInfo(path).Length>256L*1024*1024)throw new Exception("保存ファイルが大きすぎます。");SavedBirthInput data;
        using(var stream=File.OpenRead(path))data=(SavedBirthInput)new DataContractJsonSerializer(typeof(SavedBirthInput),null,Int32.MaxValue,false,null,false).ReadObject(stream);
        ValidateSavedInput(data);if(data.Id!=id||data.Label!=label)throw new Exception("保存ファイルが一覧読込後に変更されました。アプリを開き直してください。");return data.Parents;
    }
    void LoadPersistentBirthInputs(){
        if(!Directory.Exists(savedInputsDirectory))return;int failed=0;string[] paths;try{paths=Directory.GetFiles(savedInputsDirectory,"*.json");}catch(Exception error){birthStatus.Text="保存個体の一覧を読み込めません："+error.Message;return;}
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(string original in paths.OrderBy(p=>p,StringComparer.Ordinal))try{
            string path=original;var header=ReadSavedHeader(path);if(!ids.Add(header.Id))throw new Exception("保存IDが重複しています。");
            if(String.Equals(Path.GetFileNameWithoutExtension(path),header.Id,StringComparison.OrdinalIgnoreCase)){string renamed=SavedInputPath(NewSavedInputFileName(header.Label));File.Move(path,renamed);path=renamed;}
            string storedPath=path;var info=new FileInfo(path);var batch=new ForecastSnapshot{Label=header.Label,SavedId=header.Id,SavedFileName=Path.GetFileName(path),SavedLength=info.Length,SavedWriteTicks=info.LastWriteTimeUtc.Ticks};batch.ParentsLoader=()=>ReadSavedParents(storedPath,header.Id,header.Label);persistentBirthInputs.Add(batch);AddBirthBatchChoice(batch);
        }catch{failed++;}
        if(failed>0)birthStatus.Text=failed+"件の保存一覧を読み込めませんでした。内容は削除していません。";UpdateDeleteBirthInputs();
    }
    Task queuedBirthInputLoad=Task.FromResult(true);
    async Task LoadBirthInputsAsync(params ForecastSnapshot[] batches){
        var previousLoad=queuedBirthInputLoad;var completed=new TaskCompletionSource<bool>();queuedBirthInputLoad=completed.Task;
        try{await previousLoad;var pending=batches.Where(b=>b!=null&&b.NeedsLoading).Distinct().ToArray();if(pending.Length==0)return;if(savingBirthInput)throw new Exception("読み込み・保存が終わってから操作してください。");
        SetSavingBirthInput(true);birthStatus.Text="保存した個体と系統を読み込み中…";try{await Task.Run(()=>{foreach(var batch in pending){var loaded=batch.Parents;}});}finally{SetSavingBirthInput(false);}}finally{completed.TrySetResult(true);}
    }
    ForecastSnapshot WriteSavedBirthInput(string label,BirthParent[] parents){
        if(String.IsNullOrWhiteSpace(label)||label.Trim().Length>80)throw new Exception("保存名は1～80文字で入力してください。");
        var data=new SavedBirthInput{Id=Guid.NewGuid().ToString("N"),Label=label.Trim(),Parents=parents};ValidateSavedInput(data);string json=SavedInputSerializer().Serialize(data);if(Encoding.UTF8.GetByteCount(json)>256*1024*1024)throw new Exception("保存データが大きすぎます。保存する個体を絞ってください。");
        Directory.CreateDirectory(savedInputsDirectory);string name=NewSavedInputFileName(data.Label),path=SavedInputPath(name),temporary=Path.Combine(savedInputsDirectory,data.Id+".tmp");
        try{using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write))using(var writer=new StreamWriter(stream,new UTF8Encoding(false)))writer.Write(json);File.Move(temporary,path);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        var info=new FileInfo(path);return new ForecastSnapshot{Label=data.Label,Parents=data.Parents,SavedId=data.Id,SavedFileName=name,SavedLength=info.Length,SavedWriteTicks=info.LastWriteTimeUtc.Ticks};
    }
    BirthParent[] ReplaceSavedRouteIndividual(ForecastSnapshot saved,int index,BirthParent expected,BirthParent edited){
        if(saved.BuiltIn||!persistentBirthInputs.Contains(saved)||String.IsNullOrEmpty(saved.SavedId))throw new Exception("変更する保存データを確認できません。");var parents=(BirthParent[])saved.Parents.Clone();if(index<0||index>=parents.Length||!Object.ReferenceEquals(parents[index],expected))throw new Exception("選択個体が変わりました。選び直してください。");parents[index]=edited;string path=SavedInputPath(saved.SavedFileName);Action check=delegate{var header=ReadSavedHeader(path);var info=new FileInfo(path);if(header.Id!=saved.SavedId||header.Label!=saved.Label||saved.SavedWriteTicks!=0&&(saved.SavedLength!=info.Length||saved.SavedWriteTicks!=info.LastWriteTimeUtc.Ticks))throw new Exception("保存ファイルが外部で変更されました。アプリを開き直してください。");};check();
        var data=new SavedBirthInput{Version=1,Id=saved.SavedId,Label=saved.Label,Parents=parents};ValidateSavedInput(data);string temporary=Path.Combine(savedInputsDirectory,Guid.NewGuid().ToString("N")+".tmp");
        try{using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){string prefix="{\"Version\":1,\"Id\":"+Json.Serializer().Serialize(data.Id)+",\"Label\":"+Json.Serializer().Serialize(data.Label)+",\"Parents\":";byte[] bytes=Encoding.UTF8.GetBytes(prefix);stream.Write(bytes,0,bytes.Length);new DataContractJsonSerializer(typeof(BirthParent[]),null,Int32.MaxValue,false,null,false).WriteObject(stream,parents);stream.WriteByte((byte)'}');stream.Flush(true);}if(new FileInfo(temporary).Length>256L*1024*1024)throw new Exception("保存データが大きすぎます。元の保存は変更しません。");check();File.Replace(temporary,path,null);}finally{if(File.Exists(temporary))File.Delete(temporary);}return parents;
    }
    public int PurgeLegacyDeletedBirthInputs(){
        if(savingBirthInput)throw new Exception("読み込み・保存が終わってから操作してください。");string trash=Path.GetFullPath(Path.Combine(savedInputsDirectory,"削除済み"));if(!Directory.Exists(trash))return 0;
        var directories=new List<string>();var files=new List<string>();var pending=new Stack<string>();pending.Push(trash);
        while(pending.Count>0){string current=pending.Pop();if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)continue;directories.Add(current);
            foreach(string file in Directory.GetFiles(current,"*.json")){Guid id;string name=Path.GetFileNameWithoutExtension(file);if(!Guid.TryParseExact(name,"N",out id)||(File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)continue;var header=ReadSavedHeader(file);if(header.Id==name)files.Add(file);}
            foreach(string child in Directory.GetDirectories(current)){Guid id;if(Guid.TryParseExact(Path.GetFileName(child),"N",out id))pending.Push(child);}
        }
        foreach(string file in files)File.Delete(file);
        foreach(string directory in directories.OrderByDescending(p=>p.Length))if(Directory.GetFileSystemEntries(directory).Length==0)Directory.Delete(directory,false);
        return files.Count;
    }
    public void DeleteSavedBirthInput(int target){
        if(savingBirthInput)throw new Exception("読み込み・保存が終わってから操作してください。");if(target<0||target>1)throw new ArgumentOutOfRangeException("target");var batch=SelectedBirthBatch(target);Guid id;
        if(batch.BuiltIn||!Guid.TryParseExact(batch.SavedId,"N",out id)||!persistentBirthInputs.Contains(batch))throw new Exception("eventなどの同梱データは削除できません。");
        string path=SavedInputPath(batch.SavedFileName??batch.SavedId+".json");if(!File.Exists(path))throw new Exception("保存ファイルが見つからないため削除しませんでした。");var header=ReadSavedHeader(path);if(header.Id!=batch.SavedId)throw new Exception("保存IDが異なるため削除しませんでした。");
        File.Delete(path);persistentBirthInputs.Remove(batch);foreach(var input in birthBatches){var selected=input.SelectedItem;input.Items.Remove(batch);if(Object.ReferenceEquals(selected,batch))input.SelectedIndex=-1;else input.SelectedItem=selected;}UpdateDeleteBirthInputs();birthStatus.Text="「"+batch.Label+"」の保存ファイルを完全に削除しました。出力履歴は今回の起動中のみ保持します。";
    }
}
}
