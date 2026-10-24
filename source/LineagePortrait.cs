using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace FreeNormalFinder {
public sealed class PortraitPreview {public Bitmap Image;public string MissingParts;}
public sealed class LineagePortraitRenderer:IDisposable {
    readonly string root;readonly Dictionary<string,PortraitPreview> cache=new Dictionary<string,PortraitPreview>();readonly Queue<string> order=new Queue<string>();
    readonly Dictionary<string,Bitmap> layers=new Dictionary<string,Bitmap>();readonly Queue<string> layerOrder=new Queue<string>();
    static readonly string[] BodyColors={"#282828","#D7090C","#30D0F0","#079927","#F26522","#FCFD30","#0E24FF","#FFFFFF","#500096","#FF82AC","#A27A01","#6E7F7F"};
    static readonly string[] SkinColors={"#F8DBC7","#FFD780","#FFBEAA","#E68C50","#D26946","#903C28"};
    static readonly string[] HairColors={"#282828","#FFFFFF","#804507","#CD902A","#942A00","#FFDD20","#0059FF","#09A600","#A128FF","#FF6E80","#F75700","#F00011"};
    static readonly string[] EditorSkin={"#FFE7D2","#FFD2AF","#FFBEAA","#CB8964","#9D6044","#673C2E"};
    static readonly int[][] EditorPairs={new[]{1,2},new[]{1,3},new[]{1,4},new[]{1,5},new[]{1,6},new[]{1,7},new[]{2,3},new[]{2,4},new[]{2,5},new[]{2,6},new[]{2,7},new[]{3,4},new[]{3,5},new[]{3,6},new[]{3,7},new[]{4,5},new[]{4,6},new[]{4,7},new[]{5,6},new[]{5,7},new[]{6,7}};
    static string EditorHair(int code){return code>=0&&code<12?HairColors[code]:"#BBBBBB";}
    public LineagePortraitRenderer(string directory){root=directory;}
    public int CacheCount {get{return cache.Count;}}
    static string Part(string folder,int code){return folder+"/"+code.ToString("D2")+".png";}
    static int Family(int code){return code>=3&&code<=38?(code-3)/3:-1;}
    Bitmap Layer(string relative){
        Bitmap result;if(layers.TryGetValue(relative,out result))return result;string path=Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar));if(!File.Exists(path))return null;
        using(var stream=File.OpenRead(path))using(var image=Image.FromStream(stream))result=new Bitmap(image);
        while(layers.Count>=24){string oldest=layerOrder.Dequeue();layers[oldest].Dispose();layers.Remove(oldest);}layers.Add(relative,result);layerOrder.Enqueue(relative);return result;
    }
    void Draw(Graphics graphics,string path,List<string> missing,string label,string tint=null,string baseColor="#FFFFFF",bool optional=false){
        var image=Layer(path);if(image==null){if(!optional)missing.Add(label);return;}
        var rectangle=new Rectangle(0,0,256,256);if(tint==null){graphics.DrawImage(image,rectangle);return;}
        Color target=ColorTranslator.FromHtml(tint),source=ColorTranslator.FromHtml(baseColor);
        using(var attributes=new ImageAttributes()){var matrix=new ColorMatrix();matrix.Matrix00=(float)target.R/Math.Max(1,(int)source.R);matrix.Matrix11=(float)target.G/Math.Max(1,(int)source.G);matrix.Matrix22=(float)target.B/Math.Max(1,(int)source.B);matrix.Matrix33=matrix.Matrix44=1;attributes.SetColorMatrix(matrix);graphics.DrawImage(image,rectangle,0,0,image.Width,image.Height,GraphicsUnit.Pixel,attributes);}
    }
    public PortraitPreview Get(int[] f){return Render(f,null);}
    public PortraitPreview GetManual(int[] f,int body){return Render(f,body);}
    public PortraitPreview GetImported(string encoded){
        string key;using(var sha=SHA256.Create())key="image:"+Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(encoded)));PortraitPreview result;if(cache.TryGetValue(key,out result))return result;
        using(var stream=new MemoryStream(Convert.FromBase64String(encoded)))using(var image=Image.FromStream(stream,false,true))result=new PortraitPreview{Image=new Bitmap(image),MissingParts=""};Cache(key,result);return result;
    }
    void Cache(string key,PortraitPreview result){while(cache.Count>=256){string oldest=order.Dequeue();cache[oldest].Image.Dispose();cache.Remove(oldest);}cache.Add(key,result);order.Enqueue(key);}
    PortraitPreview Render(int[] f,int? editorBody){
        if(f==null||f.Length<0x75)return null;int family=Family(f[0x71]),skin=f[0x73]>=0&&f[0x73]<6?f[0x73]:-1,hairColor=f[0x74]>=0&&f[0x74]<12?f[0x74]:-1;
        if(editorBody.HasValue){skin=f[0x73];hairColor=f[0x74];}
        string key=(editorBody.HasValue?"editor:"+editorBody.Value+":":"")+String.Join(",",new[]{f[0x64],f[0x65],f[0x66],f[0x67],f[0x68],f[0x69],f[0x6a],f[0x6b],family,skin,hairColor});PortraitPreview result;if(cache.TryGetValue(key,out result))return result;
        var missing=new List<string>();using(var canvas=new Bitmap(256,256,PixelFormat.Format32bppArgb)){
            using(var graphics=Graphics.FromImage(canvas)){
                graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
                string body=family<0?"#D0D0D0":BodyColors[family],secondary=null;
                if(editorBody.HasValue){int code=editorBody.Value;if(code>=8&&code<=28){int[] pair=EditorPairs[code-8];body=BodyColors[pair[0]];secondary=BodyColors[pair[1]];}else body=code<8?BodyColors[Math.Max(0,code)]:code==29?BodyColors[8]:code==30?BodyColors[11]:code==31?BodyColors[10]:BodyColors[9];}
                if(f[0x64]>=0){if(secondary==null)Draw(graphics,Part("heads",f[0x64]),missing,"頭"+f[0x64],body);else{for(int band=0;band<25;band++){var clip=graphics.Save();graphics.SetClip(new RectangleF(0,band*256f/25,256,256f/25),CombineMode.Replace);Draw(graphics,Part("heads",f[0x64]),missing,"頭"+f[0x64],band%2==0?body:secondary);graphics.Restore(clip);}}}
                string skinHex=editorBody.HasValue?(skin<6?EditorSkin[Math.Max(0,skin)]:EditorHair(Math.Min(12,skin-6))):skin<0?"#D0D0D0":SkinColors[skin];
                if(f[0x65]>=0)Draw(graphics,Part("outlines",f[0x65]),missing,"輪郭"+f[0x65],skinHex,"#F8DBC7");
                if(f[0x6b]>=0)Draw(graphics,Part("cheeks",f[0x6b]),missing,"頬"+f[0x6b],optional:f[0x6b]==0);
                if(f[0x68]>=0)Draw(graphics,Part("eyes",f[0x68]),missing,"目"+f[0x68]);
                if(f[0x67]>=0)Draw(graphics,Part("brows",f[0x67]),missing,"眉"+f[0x67],optional:f[0x67]==0);
                if(f[0x69]>=0)Draw(graphics,Part("noses",f[0x69]),missing,"鼻"+f[0x69],optional:f[0x69]==0);
                if(f[0x6a]>=0){string mouth=Part("mouths",f[0x6a]);if(f[0x6a]>=25&&f[0x6a]<=27){string colored=Part("mouths-colored/"+f[0x6a].ToString("D2"),editorBody.HasValue?Math.Min(12,Math.Max(0,hairColor)):Math.Max(0,hairColor));Draw(graphics,colored,missing,"口"+f[0x6a]);}else Draw(graphics,mouth,missing,"口"+f[0x6a]);}
                int hair=f[0x66];if(hair==28)Draw(graphics,Part("hairs-common",hair),missing,"髪28");else if(hair>0){string colored=Part("hairs-colored/"+hair.ToString("D2"),editorBody.HasValue?Math.Min(12,Math.Max(0,hairColor)):Math.Max(0,hairColor));if(hairColor>=0&&File.Exists(Path.Combine(root,colored)))Draw(graphics,colored,missing,"髪"+hair);else Draw(graphics,Part("hairs",hair),missing,"髪"+hair,editorBody.HasValue?EditorHair(hairColor):hairColor<0?"#D0D0D0":HairColors[hairColor]);}
            }
            var bits=canvas.LockBits(new Rectangle(0,0,256,256),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);var pixels=new byte[bits.Stride*256];Marshal.Copy(bits.Scan0,pixels,0,pixels.Length);canvas.UnlockBits(bits);
            int left=256,top=256,right=-1,bottom=-1;for(int y=0;y<256;y++)for(int x=0;x<256;x++)if(pixels[y*bits.Stride+x*4+3]>8){left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
            if(right<left)return null;
            var portrait=new Bitmap(160,160,PixelFormat.Format32bppArgb);if(right>=left){float side=Math.Max(right-left+1,bottom-top+1)+16;var crop=new RectangleF((left+right+1-side)/2,(top+bottom+1-side)/2,side,side);using(var graphics=Graphics.FromImage(portrait)){graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;graphics.DrawImage(canvas,new RectangleF(0,0,160,160),crop,GraphicsUnit.Pixel);}}
            result=new PortraitPreview{Image=portrait,MissingParts=String.Join("・",missing)};
        }
        Cache(key,result);return result;
    }
    public void Dispose(){foreach(var item in cache.Values)item.Image.Dispose();foreach(var layer in layers.Values)layer.Dispose();cache.Clear();layers.Clear();}
}
}
