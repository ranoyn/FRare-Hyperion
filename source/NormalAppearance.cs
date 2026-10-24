using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace FreeNormalFinder {
public class AppearanceTables {
    public string schema;public int[][] heads,face_pairs,face_data,brow,eye,nose,mouth,cheek,hair_color,skin_color,row_heads,body;
    public int[] head_lut,face_lut,brow_lut,eye_lut,nose_lut,mouth_lut,cheek_lut,cheek_axes,hair_color_lut,skin_color_lut,body_lut;
    public string[] head_names;public uint[] vendor;
}
public sealed partial class Model {
    AppearanceTables appearance;
    public static readonly string[] FaceNames={"輪郭","髪","眉","目","鼻","口","頬","肌色","髪色"};
    public static readonly string[] FaceKeys={"FaceShape","Hair","Brow","Eye","Nose","Mouth","Cheek","SkinColor","HairColor"};
    // Same default pairing as the RPG3 Editor. Imported valid exceptions keep their actual contour.
    static readonly int[] HairContours={-1,0,0,4,0,7,6,1,0,2,0,7,2,7,4,1,3,1,5,3,0,8,8,0,0,0,0,0,1,0,0,3,1,0,10,3,0,4,3,0};
    public static int ContourForHair(int hair){return hair>=0&&hair<HairContours.Length?HairContours[hair]:-1;}
    public bool IsKnownHairContour(int hair,int contour){return appearance.face_data.Any(p=>p[0]==contour&&p[1]==hair);}
    void LoadAppearance(string directory){string path=Path.Combine(directory,"appearance.json");if(File.Exists(path)){appearance=Json.Read<AppearanceTables>(path);if(appearance.schema!="free-normal-appearance-corrected-v1"||appearance.row_heads.Length!=Data.rows.Length)throw new Exception("外見データの形式が異なります。");}}
    static int Pick(int[][] rows,int[] lut,int cell,uint raw){int index=lut[cell];if(index<0)return 0;int[] row=rows[index<rows.Length?index:0];return row[1+(int)(raw%(uint)row[0])];}
    int Vendor(uint key){int start=0,count=appearance.vendor.Length;while(count>0){int middle=start+count/2;uint word=appearance.vendor[middle],value=word&0xffffff;if(key==value)return (int)(word>>24);if(key>value){start=middle+1;count--;}count/=2;}return 0;}
    static uint[] AppearanceWords(ulong payload){
        byte[] bytes=new byte[6];for(int i=0;i<6;i++)bytes[i]=(byte)(payload>>(8*i));byte[] digest;using(var sha=SHA256.Create())digest=sha.ComputeHash(bytes);
        uint[] w=new uint[8];for(int i=0;i<8;i++)w[i]=(uint)(digest[i*4]|digest[i*4+1]<<8|digest[i*4+2]<<16|digest[i*4+3]<<24);
        return w;
    }
    int SelectHead(uint[] w){
        int cell=Axis(0,(w[3]>>6)&127)*3+Axis(1,(w[4]>>2)&127);
        int[] heads=appearance.heads[appearance.head_lut[Axis(8,(w[5]>>1)&127)*9+cell]];
        return heads[1+(int)(((w[1]>>1)&31)%(uint)heads[0])];
    }
    int HeadForPayload(ulong payload,int overrideHead){return overrideHead>=0?overrideHead:SelectHead(AppearanceWords(payload));}
    void ProjectAppearance(Candidate person,ulong payload,int overrideHead){
        if(appearance==null)throw new Exception("data/appearance.jsonがありません。フォルダをすべて展開してください。");
        byte[] bytes=new byte[6];for(int i=0;i<6;i++)bytes[i]=(byte)(payload>>(8*i));uint[] w=AppearanceWords(payload);
        int a=Axis(0,(w[3]>>6)&127),b=Axis(1,(w[4]>>2)&127),cell=a*3+b;
        if(appearance.body==null||appearance.body_lut==null)throw new Exception("体格データがありません。dataフォルダも更新してください。");
        int bodyRow=Axis(18,(w[7]>>15)&127)==2?30:appearance.body_lut[Axis(2,(w[4]>>9)&127)*9+cell];
        int[] bodies=appearance.body[bodyRow];person.Body=bodies[1+(int)(((w[0]>>17)&31)%(uint)bodies[0])];
        person.Head=overrideHead>=0?overrideHead:SelectHead(w);
        int pair=Pick(appearance.face_pairs,appearance.face_lut,Axis(10,(w[5]>>18)&127)*9+cell,(w[1]>>16)&63);
        int[] face=new int[9];face[0]=appearance.face_data[pair][0];face[1]=appearance.face_data[pair][1];
        face[2]=Pick(appearance.brow,appearance.brow_lut,cell,(w[2]>>18)&31);
        face[3]=Pick(appearance.eye,appearance.eye_lut,cell,(w[2]>>1)&63);
        face[4]=Pick(appearance.nose,appearance.nose_lut,cell,(w[2]>>13)&31);
        face[5]=Pick(appearance.mouth,appearance.mouth_lut,cell,(w[2]>>7)&63);
        face[6]=Axis(appearance.cheek_axes[b],(w[6]>>8)&127)==0?0:Pick(appearance.cheek,appearance.cheek_lut,cell,(w[2]>>23)&31);
        face[8]=Pick(appearance.hair_color,appearance.hair_color_lut,Axis(12,(w[6]>>1)&127)*9+cell,w[1]>>27);
        uint vendorKey=(uint)(bytes[0]<<16|bytes[1]<<8|bytes[2]);int vendor=Vendor(vendorKey);uint skin=(w[1]>>22)&31;
        face[7]=vendor==0?(int)(skin%6):Pick(appearance.skin_color,appearance.skin_color_lut,(vendor-1)*18+Axis(11,w[5]>>25)*9+cell,skin);
        person.Face=face;person.Payload=BitConverter.ToString(bytes).Replace("-","");
    }
    public string HeadText(int head){return appearance!=null&&head>=0&&head<appearance.head_names.Length?appearance.head_names[head]:"頭"+head.ToString("D2");}
    public Choice[] Heads(){if(appearance==null)return new[]{new Choice{Value=-1,Text="未指定"}};return new[]{new Choice{Value=-1,Text="未指定"}}.Concat(appearance.head_names.Select((name,id)=>new Choice{Value=id,Text=name})).ToArray();}
    public Choice[] RareHeads(){
        if(appearance==null)return new[]{new Choice{Value=-1,Text="未指定"}};
        var ids=new HashSet<int>();bool common=false;
        for(int i=0;i<Data.rows.Length;i++){
            if(Data.rows[i].weight<=0)continue;
            if(((Data.rows[i].flags>>5)&7)!=0)foreach(int head in appearance.row_heads[i])ids.Add(head);
            else common=true;
        }
        if(common){
            var a=Enumerable.Range(0,128).Select(v=>Axis(0,(uint)v)).Distinct();
            var b=Enumerable.Range(0,128).Select(v=>Axis(1,(uint)v)).Distinct();
            var tiers=Enumerable.Range(0,128).Select(v=>Axis(8,(uint)v)).Distinct();
            foreach(int x in a)foreach(int y in b)foreach(int tier in tiers){
                int[] row=appearance.heads[appearance.head_lut[tier*9+x*3+y]];
                foreach(int head in row.Skip(1).Take(Math.Min(row[0],32)))ids.Add(head);
            }
        }
        return new[]{new Choice{Value=-1,Text="未指定"}}.Concat(ids.OrderBy(i=>i).Select(i=>new Choice{Value=i,Text=HeadText(i)})).ToArray();
    }
    static readonly string[] BodyNames={"0最速","0準速","0中間","0準大","0最大","3+最速","3+準速","3+中間","3+準大","3+最大","3-最速","3-準速","3-中間","3-準大","3-最大","6+最速","6+準速","6+中間","6+準大","6+最大","10最速","6-準速","6-中間","6-準大","6-最大","15最速","15準速","15中間","10準大","10最大"};
    public static string BodyText(int id){return id>=0&&id<BodyNames.Length?BodyNames[id]:"未指定";}
    static readonly int[] BodyDisplayOrder={0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,21,22,23,24,20,28,29,25,26,27};
    public Choice[] BirthBodies(){return BodyDisplayOrder.Select(n=>new Choice{Value=n,Text=BodyText(n)}).ToArray();}
    static readonly string[] BodyAvoidanceGroups={"0","3+","3-","6+","6-","10","15"};
    static readonly string[] BodySizeGroups={"最大","準大","中間","準速","最速"};
    public static bool BodyMatches(int selection,int actual){
        if(selection<0)return true;
        if(actual<0||actual>=BodyNames.Length)return false;
        if(selection>=100&&selection<107)return BodySizeGroups.Any(s=>BodyText(actual)==BodyAvoidanceGroups[selection-100]+s);
        if(selection>=110&&selection<115)return BodyText(actual).EndsWith(BodySizeGroups[selection-110],StringComparison.Ordinal);
        return selection==actual;
    }
    public Choice[] BodyFilterChoices(bool rare){
        var exact=rare?RareBodyChoices():new[]{new Choice{Value=-1,Text="未指定"}}.Concat(BirthBodies()).ToArray();
        return exact.Concat(BodyAvoidanceGroups.Select((s,i)=>new Choice{Value=100+i,Text="回避"+s})).Concat(BodySizeGroups.Select((s,i)=>new Choice{Value=110+i,Text=s}).Reverse()).ToArray();
    }
    public Choice[] RareBodyChoices(){var available=new HashSet<int>(Bodies().Select(c=>c.Value));return new[]{new Choice{Value=-1,Text="未指定"}}.Concat(BirthBodies().Where(c=>available.Contains(c.Value))).ToArray();}
    public Choice[] Bodies(){return new[]{new Choice{Value=-1,Text="未指定"}}.Concat(appearance.body.SelectMany(r=>r.Skip(1).Take(r[0])).Distinct().OrderBy(n=>n).Select(n=>new Choice{Value=n,Text=BodyText(n)})).ToArray();}
    public int[] FaceChoices(int part){
        if(appearance==null||part<0||part>=FaceNames.Length)throw new Exception("顔パーツの指定が不正です。");
        IEnumerable<int> values;
        if(part<2)values=appearance.face_data.Select(r=>r[part]);
        else if(part==7)values=Enumerable.Range(0,6).Concat(appearance.skin_color.SelectMany(r=>r.Skip(1).Take(r[0])));
        else {int[][] rows=part==2?appearance.brow:part==3?appearance.eye:part==4?appearance.nose:part==5?appearance.mouth:part==6?appearance.cheek:appearance.hair_color;values=rows.SelectMany(r=>r.Skip(1).Take(r[0]));if(part==6)values=values.Concat(new[]{0});}
        return new[]{-1}.Concat(values.Distinct().OrderBy(x=>x)).ToArray();
    }
    public static bool FaceMatches(int[] filter,int[] face){if(filter==null)return true;if(filter.Length!=FaceNames.Length||face==null)return false;for(int i=0;i<filter.Length;i++)if(filter[i]>=0&&filter[i]!=face[i])return false;return true;}
}
}
