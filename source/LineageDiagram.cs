using System;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FreeNormalFinder {
public sealed class LineageDiagram:ScrollableControl {
    sealed class DiagramNode {public BirthLineage Record;public readonly List<DiagramNode> Children=new List<DiagramNode>();public RectangleF Bounds;public float Span;public int Generation,Row,Skipped;}
    readonly List<DiagramNode> roots=new List<DiagramNode>(),nodes=new List<DiagramNode>();readonly Dictionary<int,float> rows=new Dictionary<int,float>();
    readonly ToolTip tooltip=new ToolTip();readonly Func<BirthLineage,string> details;readonly LineagePortraitRenderer portraits;
    const float Side=104,Gap=24,RowGap=66,PaddingX=32,Gutter=70,LabelHeight=22;
    float RowSpacing {get{return RecordActions?104:RowGap;}}
    float LinkBottom(DiagramNode node){return ScreenBounds(node).Bottom+(RecordActions?(node.Record.Kind=="メモ"?54:CanHideEmpty!=null&&CanHideEmpty(node.Record)?32:0):LabelHeight)*scale;}
    float diagramWidth=300,diagramHeight=160,scale=1;int visited;bool limited,overview,dragging,moved;BirthLineage selected,hover;Point dragStart,dragScroll;long gestureDistance;Point gesturePoint;
    public event EventHandler SelectedRecordChanged,ZoomChanged;
    public bool IndependentGenerationRows,RecordActions;
    public bool FilterEmptyGenerations;
    public Func<BirthLineage,bool> CanHideEmpty,HasHiddenRight;
    public Func<BirthLineage,PortraitPreview> PreviewProvider;
    public RectangleF GetEmptyHideBounds(BirthLineage record){var node=nodes.FirstOrDefault(n=>Object.ReferenceEquals(n.Record,record));if(node==null||CanHideEmpty==null||!CanHideEmpty(record))return RectangleF.Empty;var box=ScreenBounds(node);return box.Width<70?RectangleF.Empty:new RectangleF(box.X,box.Bottom+3,box.Width,24*scale);}
    public RectangleF GetRestoreBounds(BirthLineage record){var node=nodes.FirstOrDefault(n=>Object.ReferenceEquals(n.Record,record));if(node==null||HasHiddenRight==null||!HasHiddenRight(record)||node.Children.Count!=1)return RectangleF.Empty;var a=ScreenBounds(node);var b=ScreenBounds(node.Children[0]);float side=Math.Max(18,24*scale),y=(LinkBottom(node)+b.Top)/2;return new RectangleF(a.Left+a.Width/2-side/2,y-side/2,side,side);}
    public string EmptyMessage="表示できる系統がありません。個体や表示世代を選択してください。";
    public event Action<BirthLineage,string> RecordActionRequested;
    public BirthLineage[] VisibleRecords {get{return nodes.Select(n=>n.Record).ToArray();}}
    public RectangleF GetRecordActionBounds(BirthLineage record,string action){var node=nodes.FirstOrDefault(n=>Object.ReferenceEquals(n.Record,record));if(node==null||!RecordActions||record.Kind!="メモ")return RectangleF.Empty;var box=ScreenBounds(node);if(box.Width<70)return RectangleF.Empty;return new RectangleF(box.X-33*scale+(action=="ドーピング"?0:88*scale),box.Bottom+LabelHeight*scale+3,(action=="ドーピング"?86:82)*scale,24*scale);}
    public BirthLineage SelectedRecord {get{return selected;}}
    public void SelectRecord(BirthLineage record){selected=record;if(SelectedRecordChanged!=null)SelectedRecordChanged(this,EventArgs.Empty);Invalidate();}
    public void RevealSelectedRecord(){var node=nodes.FirstOrDefault(n=>Object.ReferenceEquals(n.Record,selected));if(node==null)return;var box=ScreenBounds(node);float x=-AutoScrollPosition.X,y=-AutoScrollPosition.Y;if(box.Left<Gutter||box.Right>ClientSize.Width)x+=box.Left+box.Width/2-(ClientSize.Width+Gutter)/2;if(box.Top<0||box.Bottom>ClientSize.Height)y+=box.Top+box.Height/2-ClientSize.Height/2;SetScroll(x,y);Invalidate();}
    public int VisibleNodeCount {get{return nodes.Count;}}
    public int[] VisibleGenerations {get{return nodes.Select(n=>n.Generation).Where(g=>g>0).Distinct().OrderByDescending(g=>g).ToArray();}}
    public RectangleF[] NodeBounds {get{return nodes.Select(n=>n.Bounds).ToArray();}}
    public RectangleF[] VisibleScreenBounds {get{return nodes.Select(ScreenBounds).ToArray();}}
    public int HiddenLinkCount {get{return nodes.Count(n=>n.Skipped>0);}}
    public bool Limited {get{return limited;}}public bool IsOverview {get{return overview;}}public double ZoomPercent {get{return scale*100;}}
    public LineageDiagram(Func<BirthLineage,string> caption,Func<BirthLineage,string> details,LineagePortraitRenderer portraits=null){
        this.details=details;this.portraits=portraits;Name="lineageDiagram";Dock=DockStyle.Fill;BackColor=Color.White;AutoScroll=true;TabStop=true;DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);
    }
    public void SetLineage(BirthLineage record,bool[] visible){SetForest(record==null?new BirthLineage[0]:new[]{record},visible);}
    public void SetForest(IEnumerable<BirthLineage> records,bool[] visible){
        roots.Clear();nodes.Clear();rows.Clear();visited=0;limited=false;hover=null;tooltip.SetToolTip(this,"");
        foreach(var record in records)if(record!=null)roots.AddRange(BuildVisible(record,visible,new HashSet<BirthLineage>(),0));
        int index=0;foreach(int row in nodes.Select(n=>n.Row).Distinct().OrderBy(n=>n))rows[row]=24+index++*(Side+RowSpacing);
        float left=PaddingX;
        if(IndependentGenerationRows){foreach(var group in roots.GroupBy(n=>n.Row)){float x=PaddingX;foreach(var root in group){Measure(root);Place(root,x);x+=root.Span;}left=Math.Max(left,x);}}
        else foreach(var root in roots){Measure(root);Place(root,left);left+=root.Span;}
        diagramWidth=Math.Max(300,left+PaddingX);diagramHeight=Math.Max(160,24+rows.Count*(Side+RowSpacing));UpdateExtent();
        selected=nodes.Any(n=>Object.ReferenceEquals(n.Record,selected))?selected:roots.Count==0?null:roots[0].Record;CenterSelection();Invalidate();if(SelectedRecordChanged!=null)SelectedRecordChanged(this,EventArgs.Empty);
    }
    List<DiagramNode> BuildVisible(BirthLineage record,bool[] visible,HashSet<BirthLineage> path,int depth){
        var result=new List<DiagramNode>();if(record==null)return result;if(depth>20||visited>=4095||!path.Add(record)){limited=true;return result;}visited++;
        int generation=record.Raw!=null&&record.Raw.Length>0x82&&record.Raw[0x82]>=0&&record.Raw[0x82]<9?record.Raw[0x82]+1:0;
        var children=new List<DiagramNode>();children.AddRange(BuildVisible(record.ParentA,visible,path,depth+1));children.AddRange(BuildVisible(record.ParentB,visible,path,depth+1));path.Remove(record);
        if(generation>0&&visible!=null&&visible.Length>generation&&!visible[generation]&&(FilterEmptyGenerations||record.Kind!="追加")){foreach(var child in children)child.Skipped++;return children;}
        var node=new DiagramNode{Record=record,Generation=generation,Row=generation==0?depth:9-generation};node.Children.AddRange(children);nodes.Add(node);result.Add(node);return result;
    }
    float Measure(DiagramNode node){return node.Span=Math.Max(Side+(RecordActions?76:Gap),node.Children.Sum(n=>Measure(n)));}
    void Place(DiagramNode node,float left){node.Bounds=new RectangleF(left+(node.Span-Side)/2,rows[node.Row],Side,Side);foreach(var child in node.Children){Place(child,left);left+=child.Span;}}
    float CenterOffset {get{return Gutter+Math.Max(0,(ClientSize.Width-Gutter-diagramWidth*scale)/2);}}
    void UpdateExtent(){AutoScrollMinSize=new Size((int)Gutter+(int)Math.Ceiling(diagramWidth*scale),(int)Math.Ceiling(diagramHeight*scale));}
    public void PanBy(int dx,int dy){SetScroll(-AutoScrollPosition.X+dx,-AutoScrollPosition.Y+dy);Invalidate();}
    void SetScroll(float x,float y){AutoScrollPosition=new Point((int)Math.Max(0,Math.Min(x,Math.Max(0,AutoScrollMinSize.Width-ClientSize.Width+SystemInformation.VerticalScrollBarWidth))),(int)Math.Max(0,Math.Min(y,Math.Max(0,AutoScrollMinSize.Height-ClientSize.Height+SystemInformation.HorizontalScrollBarHeight))));}
    public void ZoomAt(double percent,Point anchor){
        float next=(float)Math.Max(.01,Math.Min(4,percent/100));if(Math.Abs(next-scale)<.00001)return;
        float x=(anchor.X-CenterOffset-AutoScrollPosition.X)/scale,y=(anchor.Y-AutoScrollPosition.Y)/scale;
        scale=next;overview=false;UpdateExtent();SetScroll(x*scale+CenterOffset-anchor.X,y*scale-anchor.Y);Invalidate();if(ZoomChanged!=null)ZoomChanged(this,EventArgs.Empty);
    }
    public void SetZoom(double percent){ZoomAt(percent,new Point(ClientSize.Width/2,ClientSize.Height/2));}
    public double FitPercent(){return Math.Max(1,Math.Min(100,100*Math.Min(Math.Max(50,ClientSize.Width-Gutter-16)/diagramWidth,Math.Max(50,ClientSize.Height-24)/diagramHeight)));}
    public void FitVerticalSpacing(){overview=true;UpdateExtent();CenterSelection();Invalidate();}
    void CenterSelection(){var target=nodes.FirstOrDefault(n=>Object.ReferenceEquals(n.Record,selected))??roots.FirstOrDefault();float center=target==null?diagramWidth/2:target.Bounds.Left+Side/2;SetScroll(center*scale-(ClientSize.Width-Gutter)/2,0);}
    RectangleF ScreenBounds(DiagramNode node){return new RectangleF(node.Bounds.X*scale+AutoScrollPosition.X+CenterOffset,node.Bounds.Y*scale+AutoScrollPosition.Y,Side*scale,Side*scale);}
    DiagramNode Hit(Point point){return point.X<Gutter?null:nodes.FirstOrDefault(n=>ScreenBounds(n).Contains(point));}
    public PointF[][] LinkPaths {get{return nodes.SelectMany(parent=>parent.Children.Select(child=>{
        var a=ScreenBounds(parent);var b=ScreenBounds(child);float middle=(LinkBottom(parent)+b.Top)/2;
        return new[]{new PointF(a.Left+a.Width/2,LinkBottom(parent)),new PointF(a.Left+a.Width/2,middle),new PointF(b.Left+b.Width/2,middle),new PointF(b.Left+b.Width/2,b.Top)};
    })).ToArray();}}
    protected override void OnMouseWheel(MouseEventArgs e){
        var handled=e as HandledMouseEventArgs;if(handled!=null)handled.Handled=true;
        if((ModifierKeys&Keys.Control)!=0)ZoomAt(ZoomPercent*Math.Pow(1.15,e.Delta/120.0),e.Location);
        else if((ModifierKeys&Keys.Shift)!=0)PanBy((int)(-e.Delta*.75),0);else PanBy(0,(int)(-e.Delta*.75));
    }
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left&&e.Button!=MouseButtons.Middle)return;Focus();
        if(e.Button==MouseButtons.Left)foreach(var item in nodes){string action=GetRestoreBounds(item.Record).Contains(e.Location)?"枠を戻す":GetEmptyHideBounds(item.Record).Contains(e.Location)?"枠を非表示":null;if(action!=null){if(RecordActionRequested!=null)RecordActionRequested(item.Record,action);return;}}
        if(RecordActions&&e.Button==MouseButtons.Left)foreach(var item in nodes)foreach(string action in new[]{"ドーピング","顔パーツ"})if(GetRecordActionBounds(item.Record,action).Contains(e.Location)){SelectRecord(item.Record);if(RecordActionRequested!=null)RecordActionRequested(item.Record,action);return;}
        dragging=true;moved=false;dragStart=e.Location;dragScroll=new Point(-AutoScrollPosition.X,-AutoScrollPosition.Y);Capture=true;var node=Hit(e.Location);if(node!=null&&e.Button==MouseButtons.Left){selected=node.Record;if(SelectedRecordChanged!=null)SelectedRecordChanged(this,EventArgs.Empty);Invalidate();}}
    protected override void OnMouseMove(MouseEventArgs e){
        base.OnMouseMove(e);if(dragging){int dx=e.X-dragStart.X,dy=e.Y-dragStart.Y;if(Math.Abs(dx)+Math.Abs(dy)>4)moved=true;if(moved){SetScroll(dragScroll.X-dx,dragScroll.Y-dy);Cursor=Cursors.SizeAll;tooltip.SetToolTip(this,"");Invalidate();}return;}
        var node=Hit(e.Location);Cursor=node==null?Cursors.Default:Cursors.Hand;var record=node==null?null:node.Record;if(!Object.ReferenceEquals(hover,record)){hover=record;string text=record==null?"":record.Kind=="追加"?"電波人間を追加":details(record);if(record!=null&&record.Kind!="追加"&&portraits!=null){var image=PreviewProvider!=null?PreviewProvider(record):portraits.Get(record.Raw);if(image!=null&&!String.IsNullOrEmpty(image.MissingParts))text+="\r\nプレビュー画像なし："+image.MissingParts;}tooltip.SetToolTip(this,text.Length>3000?text.Substring(0,3000)+"…":text);}
    }
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);dragging=false;Capture=false;Cursor=Cursors.Default;}
    protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture)dragging=false;}
    [StructLayout(LayoutKind.Sequential)]struct GestureInfo {public uint Size,Flags,Id;public IntPtr Target;public short X,Y;public uint Instance,Sequence;public ulong Arguments;public uint Extra;}
    [DllImport("user32.dll")]static extern bool GetGestureInfo(IntPtr handle,ref GestureInfo info);
    [DllImport("user32.dll")]static extern bool CloseGestureInfoHandle(IntPtr handle);
    protected override void WndProc(ref Message m){
        if(m.Msg==0x20e){int delta=(short)(((long)m.WParam>>16)&65535);PanBy((int)(delta*.75),0);m.Result=IntPtr.Zero;return;}
        if(m.Msg==0x119){var info=new GestureInfo{Size=(uint)Marshal.SizeOf(typeof(GestureInfo))};if(GetGestureInfo(m.LParam,ref info)&&(info.Id==3||info.Id==4)){
            var point=PointToClient(new Point(info.X,info.Y));bool begin=(info.Flags&1)!=0;
            if(info.Id==3){long distance=(long)(info.Arguments&0xffffffff);if(!begin&&gestureDistance>0&&distance>0)ZoomAt(ZoomPercent*distance/gestureDistance,point);gestureDistance=distance;}
            else {if(!begin)PanBy(gesturePoint.X-point.X,gesturePoint.Y-point.Y);gesturePoint=point;}
            CloseGestureInfoHandle(m.LParam);m.Result=IntPtr.Zero;return;
        }}base.WndProc(ref m);
    }
    protected override void OnResize(EventArgs e){base.OnResize(e);if(nodes!=null)UpdateExtent();}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);if(nodes.Count==0){TextRenderer.DrawText(e.Graphics,EmptyMessage,Font,ClientRectangle,Color.DimGray,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak);return;}
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var link=new Pen(Color.FromArgb(138,155,173),1.5f))using(var skipped=new Pen(Color.FromArgb(138,155,173),1.5f)){
            skipped.DashStyle=DashStyle.Dash;foreach(var parent in nodes)foreach(var child in parent.Children){var a=ScreenBounds(parent);var b=ScreenBounds(child);float middle=(LinkBottom(parent)+b.Top)/2;
                e.Graphics.DrawLines(child.Skipped>0?skipped:link,new[]{new PointF(a.Left+a.Width/2,LinkBottom(parent)),new PointF(a.Left+a.Width/2,middle),new PointF(b.Left+b.Width/2,middle),new PointF(b.Left+b.Width/2,b.Top)});
            }
        }
        using(var normal=new SolidBrush(Color.FromArgb(248,250,253)))using(var highlight=new SolidBrush(Color.FromArgb(231,244,255)))using(var border=new Pen(Color.FromArgb(151,171,190),1))using(var activeBorder=new Pen(Color.FromArgb(38,121,182),2)){
            foreach(var node in nodes){var box=ScreenBounds(node);if(!box.IntersectsWith(ClientRectangle))continue;bool active=Object.ReferenceEquals(node.Record,selected);e.Graphics.FillRectangle(active?highlight:normal,box);
                var image=node.Record.Kind=="追加"?null:PreviewProvider!=null?PreviewProvider(node.Record):portraits==null?null:portraits.Get(node.Record.Raw);if(node.Record.Kind=="追加"){using(var plus=new Font(Font.FontFamily,Math.Max(12,box.Width*.35f),FontStyle.Regular))TextRenderer.DrawText(e.Graphics,"＋",plus,Rectangle.Round(box),Color.FromArgb(38,121,182),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
                else if(image!=null&&box.Width>3){var inside=box;inside.Inflate(-Math.Min(5,box.Width*.05f),-Math.Min(5,box.Height*.05f));e.Graphics.DrawImage(image.Image,inside);if(!String.IsNullOrEmpty(image.MissingParts)&&box.Width>45)TextRenderer.DrawText(e.Graphics,"!",Font,new Rectangle((int)box.Right-18,(int)box.Top+2,16,20),Color.DarkOrange);}
                else if(box.Width>40)TextRenderer.DrawText(e.Graphics,node.Record.Kind=="メモ"?"未入力":"画像なし",Font,Rectangle.Round(box),Color.Gray,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
                e.Graphics.DrawRectangle(active?activeBorder:border,box.X,box.Y,box.Width,box.Height);
                if(box.Width>=45){string name=node.Record.Name??"";TextRenderer.DrawText(e.Graphics,name,Font,new Rectangle((int)box.X-6,(int)box.Bottom+2,(int)box.Width+12,20),Color.FromArgb(45,66,86),TextFormatFlags.HorizontalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);}
                if(RecordActions&&node.Record.Kind=="メモ")foreach(string action in new[]{"ドーピング","顔パーツ"}){var area=GetRecordActionBounds(node.Record,action);if(area.IsEmpty)continue;var rect=Rectangle.Round(area);e.Graphics.FillRectangle(SystemBrushes.Control,rect);e.Graphics.DrawRectangle(Pens.SlateGray,rect);TextRenderer.DrawText(e.Graphics,action,Font,rect,Color.FromArgb(45,66,86),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);}
                var hideArea=GetEmptyHideBounds(node.Record);if(!hideArea.IsEmpty){var rect=Rectangle.Round(hideArea);e.Graphics.FillRectangle(SystemBrushes.Control,rect);e.Graphics.DrawRectangle(Pens.SlateGray,rect);TextRenderer.DrawText(e.Graphics,"枠を非表示",Font,rect,Color.DimGray,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);}
            }
        }
        foreach(var node in nodes){var area=GetRestoreBounds(node.Record);if(area.IsEmpty)continue;e.Graphics.FillRectangle(Brushes.White,area);e.Graphics.DrawRectangle(Pens.SteelBlue,area.X,area.Y,area.Width,area.Height);TextRenderer.DrawText(e.Graphics,"＋",Font,Rectangle.Round(area),Color.SteelBlue,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
        using(var background=new SolidBrush(Color.FromArgb(242,247,252)))e.Graphics.FillRectangle(background,0,0,Gutter,ClientSize.Height);
        foreach(var row in rows){int y=(int)(row.Value*scale+AutoScrollPosition.Y);if(y<0||y>ClientSize.Height||scale*(Side+RowSpacing)<Font.Height+3)continue;bool known=nodes.Any(n=>n.Row==row.Key&&n.Generation>0);TextRenderer.DrawText(e.Graphics,known?"第"+(9-row.Key)+"世代":"世代不明",Font,new Rectangle(4,y,64,24),Color.DimGray,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);}
    }
    protected override void Dispose(bool disposing){if(disposing){tooltip.Dispose();if(portraits!=null)portraits.Dispose();}base.Dispose(disposing);}
}
}
