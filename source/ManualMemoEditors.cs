using System;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;
namespace FreeNormalFinder {
public sealed partial class ToolForm {
    ComboBox ManualOptionalChoice(IEnumerable<Choice> choices,int? value,string name,int width){var box=new ComboBox{Name=name,Width=width,DropDownStyle=ComboBoxStyle.DropDownList,DisplayMember="Text",ValueMember="Value"};box.Items.Add(new Choice{Value=-1,Text="未入力"});foreach(var item in choices.Where(c=>c.Value>=0))box.Items.Add(item);box.SelectedIndex=0;for(int i=1;i<box.Items.Count;i++)if(value.HasValue&&((Choice)box.Items[i]).Value==value.Value){box.SelectedIndex=i;break;}return box;}
    static int? ManualChoiceValue(ComboBox box){var item=box.SelectedItem as Choice;return item==null||item.Value<0?(int?)null:item.Value;}
    static void SetManualChoice(ComboBox box,int value){for(int i=0;i<box.Items.Count;i++)if(((Choice)box.Items[i]).Value==value){box.SelectedIndex=i;return;}}
    static void ManualField(FlowLayoutPanel row,string text,Control input){var panel=new Panel{Width=input.Width+8,Height=58};panel.Controls.Add(new Label{Text=text,AutoSize=true,Location=new Point(0,2)});input.Location=new Point(0,24);panel.Controls.Add(input);row.Controls.Add(panel);}
    Form ManualEditorForm(string title,int width,int height){return new Form{Text=title,Name="manualMemoEditor",ClientSize=new Size(width,height),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,Font=Font,AutoScaleMode=AutoScaleMode.Dpi};}
    static Button MemoEditorButton(string name,string text){return new Button{Name=name,Text=text,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,MinimumSize=new Size(96,36),Padding=new Padding(7,3,7,3),Margin=new Padding(4,4,4,4)};}
    void EditManualMemoAbility(string id){
        var person=FindManualPerson(id).Copy();using(var form=ManualEditorForm(person.Generation+"世代：ドーピング",800,168)){
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(10),ColumnCount=1,RowCount=3};foreach(int h in new[]{64,32,52})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));form.Controls.Add(layout);
            var row=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};layout.Controls.Add(row,0,0);var doping=new ComboBox[5];string[] stats={"HP","AP","こうげきりょく","ぼうぎょりょく","すばやさ"};int cap=DopingNotes.Limit(person.Generation);for(int i=0;i<5;i++){doping[i]=ManualOptionalChoice(Enumerable.Range(0,cap+1).Select(v=>new Choice{Value=v,Text=v.ToString()}),person.Doping[i],"memoDoping"+i,140);ManualField(row,stats[i],doping[i]);}
            var budget=new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};layout.Controls.Add(budget,0,1);var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};layout.Controls.Add(actions,0,2);var ok=MemoEditorButton("memoCommit","決定");var cancel=MemoEditorButton("memoCancel","キャンセル");cancel.DialogResult=DialogResult.Cancel;actions.Controls.AddRange(new Control[]{ok,cancel});form.CancelButton=cancel;
            Action update=delegate{int total=doping.Sum(b=>ManualChoiceValue(b)??0);budget.Text="果物の合計："+total+" / "+cap+"個";ok.Enabled=total<=cap;};foreach(var box in doping)box.SelectedIndexChanged+=delegate{update();};update();
            ok.Click+=delegate{try{UpdateManualAbility(id,person.Antenna,person.Head,person.Color1,person.Color2,person.Pattern,person.Body,person.Name,doping.Select(ManualChoiceValue).ToArray());form.DialogResult=DialogResult.OK;}catch(Exception error){MessageBox.Show(form,error.Message);}};form.ShowDialog(this);
        }
    }
    void EditManualMemoFace(string id){
        var draft=FindManualPerson(id).Copy();using(var renderer=new LineagePortraitRenderer(System.IO.Path.Combine(model.DataDirectory,"portraits")))using(var form=ManualEditorForm(draft.Generation+"世代：顔パーツ",800,498)){
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(10),ColumnCount=1,RowCount=5};foreach(int h in new[]{220,64,64,64,56})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));form.Controls.Add(layout);
            var picture=new PictureBox{Name="memoFacePreview",Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.White};layout.Controls.Add(picture,0,0);
            var identity=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};layout.Controls.Add(identity,0,1);var name=new TextBox{Name="memoName",Width=240,MaxLength=20,Text=draft.Name??""};var head=ManualOptionalChoice(model.Heads(),draft.Head,"memoHead",210);ManualField(identity,"名前",name);ManualField(identity,"頭",head);
            var boxes=new ComboBox[9];var first=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};var second=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};layout.Controls.Add(first,0,2);layout.Controls.Add(second,0,3);
            foreach(int part in new[]{1,0,2,3,4,5,6,7,8}){boxes[part]=ManualOptionalChoice(MemoFaceChoices(part),draft.Face[part],"memoFace"+part,135);ManualField(part<=4?first:second,Model.FaceNames[part],boxes[part]);}
            var body=ManualOptionalChoice(MemoBodyNames.Select((text,value)=>new Choice{Value=value,Text=text}).Where(c=>c.Value<8||c.Value>=29),draft.PreviewBodyColor??(draft.Color1.HasValue?(int?)MemoBodyCode(draft):null),"memoPreviewBody",135);ManualField(second,"体色",body);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};layout.Controls.Add(actions,0,4);var ok=MemoEditorButton("memoCommit","決定");var remove=MemoEditorButton("memoRemoveImage","画像を削除");var import=MemoEditorButton("memoImportImage","画像から反映");var cancel=MemoEditorButton("memoCancel","キャンセル");cancel.DialogResult=DialogResult.Cancel;actions.Controls.AddRange(new Control[]{ok,import,remove,cancel});form.CancelButton=cancel;
            bool syncing=false;
            Action preview=delegate{if(syncing)return;draft.Name=String.IsNullOrWhiteSpace(name.Text)?null:name.Text.Trim();draft.Head=ManualChoiceValue(head);draft.PreviewBodyColor=ManualChoiceValue(body);for(int i=0;i<9;i++)draft.Face[i]=ManualChoiceValue(boxes[i]);var rendered=ManualMemoPreview(draft,renderer);var old=picture.Image;picture.Image=rendered==null?null:new Bitmap(rendered.Image);if(old!=null)old.Dispose();remove.Enabled=!String.IsNullOrEmpty(draft.PortraitPng);};
            Action synchronize=delegate{if(syncing)return;int hair=ManualChoiceValue(boxes[1])??0;syncing=true;try{if(hair>0){int contour=Model.ContourForHair(hair);if(contour>=0)SetManualChoice(boxes[0],contour);}boxes[0].Enabled=hair==0;}finally{syncing=false;}preview();};
            foreach(var box in boxes)box.SelectedIndexChanged+=delegate{preview();};boxes[1].SelectedIndexChanged+=delegate{synchronize();};head.SelectedIndexChanged+=delegate{preview();};body.SelectedIndexChanged+=delegate{preview();};name.TextChanged+=delegate{preview();};synchronize();
            import.Click+=delegate{try{string path=SelectManualMemoImage();if(path!=null){draft.PortraitPng=ReadManualMemoImage(path);preview();}}catch(Exception error){MessageBox.Show(form,error.Message,"画像を読み込めません");}};
            remove.Click+=delegate{draft.PortraitPng=null;preview();};
            ok.Click+=delegate{try{UpdateManualAppearance(id,draft.Name,draft.Head,draft.Face,draft.PreviewBodyColor,draft.PortraitPng);form.DialogResult=DialogResult.OK;}catch(Exception error){MessageBox.Show(form,error.Message);}};
            form.ShowDialog(this);if(picture.Image!=null){picture.Image.Dispose();picture.Image=null;}
        }
    }
}
}
