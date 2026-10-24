using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.Windows.Forms;
namespace FreeNormalFinder {
public sealed partial class ToolForm {
    public Func<string> ChooseManualMemoImage;
    public Func<string,bool> ConfirmManualMemoDelete;
    LineagePortraitRenderer manualMemoPortraits;
    static readonly string[] MemoBodyNames={"黒","赤","水色","緑","オレンジ","黄","青","白","赤／水色","赤／緑","赤／オレンジ","赤／黄","赤／青","赤／白","水色／緑","水色／オレンジ","水色／黄","水色／青","水色／白","オレンジ／緑","黄／緑","青／緑","白／緑","オレンジ／黄","オレンジ／青","オレンジ／白","黄／青","黄／白","白／青","紫","銀","金","桃"};
    static readonly string[] MemoHairNames={"黒","白","茶","黄茶","赤茶","黄","青","緑","紫","桃","オレンジ","赤","銀","銀(未定義13)","銀(未定義14)","銀(未定義15)"};
    static readonly int[] MemoFaceMaximum={11,39,19,47,31,39,11,18,15};
    static int MemoBodyCode(ManualMemoIndividual p){if(p.PreviewBodyColor.HasValue)return p.PreviewBodyColor.Value;if(!p.Color1.HasValue||p.Color1<3||p.Color1>38)return 0;int family=(p.Color1.Value-3)/3;return family<8?family:family==8?29:family==9?32:family==10?31:30;}
    Choice[] MemoFaceChoices(int part){return Enumerable.Range(0,MemoFaceMaximum[part]+1).Select(v=>new Choice{Value=v,Text=part==8?MemoHairNames[v]:part==7?(v<6?v.ToString():MemoHairNames[v-6]+"髪(未定義"+v+")"):v.ToString("D2")}).ToArray();}
    PortraitPreview ManualMemoPreview(ManualMemoIndividual person,LineagePortraitRenderer renderer){if(!String.IsNullOrEmpty(person.PortraitPng))try{return renderer.GetImported(person.PortraitPng);}catch(ArgumentException){}return renderer.GetManual(ManualPortraitRaw(person),MemoBodyCode(person));}
    PortraitPreview ManualRecordPreview(BirthLineage record){ManualMemoIndividual person;return manualMemoRecords.TryGetValue(record,out person)?ManualMemoPreview(person,manualMemoPortraits):null;}
    static void ValidateMemoImage(string encoded){
        if(String.IsNullOrEmpty(encoded))return;if(encoded.Length>200000)throw new Exception("メモ画像が大きすぎます。");byte[] bytes;try{bytes=Convert.FromBase64String(encoded);}catch{throw new Exception("メモ画像の形式が不正です。");}
        byte[] png={137,80,78,71,13,10,26,10};if(bytes.Length<33||!bytes.Take(8).SequenceEqual(png)||bytes[16]!=0||bytes[17]!=0||bytes[18]!=0||bytes[19]!=160||bytes[20]!=0||bytes[21]!=0||bytes[22]!=0||bytes[23]!=160)throw new Exception("メモ画像の形式が不正です。");
    }
    public static string ReadManualMemoImage(string path){
        if(new FileInfo(path).Length>16*1024*1024)throw new Exception("16MB以下の画像を選択してください。");
        using(var stream=File.OpenRead(path))using(var source=Image.FromStream(stream,false,true)){if(source.Width>16000||source.Height>16000||(long)source.Width*source.Height>20000000)throw new Exception("2000万画素以下の画像を選択してください。");
            using(var image=new Bitmap(160,160,PixelFormat.Format32bppArgb)){using(var g=Graphics.FromImage(image)){g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;float scale=Math.Min(160f/source.Width,160f/source.Height);float w=source.Width*scale,h=source.Height*scale;g.DrawImage(source,(160-w)/2,(160-h)/2,w,h);}using(var output=new MemoryStream()){image.Save(output,ImageFormat.Png);return Convert.ToBase64String(output.ToArray());}}
        }
    }
    string SelectManualMemoImage(){if(ChooseManualMemoImage!=null)return ChooseManualMemoImage();using(var dialog=new OpenFileDialog{Title="プレビューに表示する画像を選択",Filter="画像 (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif",CheckFileExists=true,Multiselect=false})return dialog.ShowDialog(this)==DialogResult.OK?dialog.FileName:null;}
    public void UpdateManualAppearance(string id,string name,int? head,int?[] face,int? bodyColor,string image){
        var original=FindManualPerson(id);var draft=original.Copy();draft.Name=String.IsNullOrWhiteSpace(name)?null:name.Trim();draft.Head=head;draft.Face=face==null?new int?[9]:(int?[])face.Clone();draft.PreviewBodyColor=bodyColor;draft.PortraitPng=image;ValidateManualPerson(draft);manualMemoPeople[manualMemoPeople.IndexOf(original)]=draft;manualMemoDirty=true;RenderManualMemo(id);
    }
    public bool DeleteSelectedManualMemo(){
        var choice=manualMemoInput.SelectedItem as ManualMemoSavedChoice;if(choice==null)throw new Exception("削除する保存結果を選択してください。");string path=ManualMemoFilePath(choice.FileName);var doc=ReadManualMemo(path);
        var info=new FileInfo(path);if(doc.Id!=choice.Id||doc.Label!=choice.Label||!String.Equals(path,manualMemoPath,StringComparison.OrdinalIgnoreCase)||info.Length!=manualMemoLength||info.LastWriteTimeUtc.Ticks!=manualMemoWriteTicks)throw new Exception("保存ファイルが変更されています。開き直してから削除してください。");
        string question="「"+choice.Label+"」の保存ファイルと現在の入力内容を完全に削除します。元に戻せません。よろしいですか？";bool accepted=ConfirmManualMemoDelete!=null?ConfirmManualMemoDelete(question):MessageBox.Show(this,question,"保存結果を削除",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes;if(!accepted)return false;
        var latest=ReadManualMemo(path);var latestInfo=new FileInfo(path);if(latest.Id!=doc.Id||latest.Label!=doc.Label||latestInfo.Length!=info.Length||latestInfo.LastWriteTimeUtc.Ticks!=info.LastWriteTimeUtc.Ticks)throw new Exception("確認中に保存ファイルが変更されたため、削除しませんでした。");
        File.Delete(path);ResetManualDopingMemo();RefreshManualMemoInputs();manualDopingSummary.Text="「"+choice.Label+"」の保存ファイルを削除しました。";return true;
    }
}
}
