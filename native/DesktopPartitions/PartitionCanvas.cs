using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace UDo.DesktopPartitions {
    internal sealed class PartitionCanvasWindow : PartitionSurface {
        readonly PartitionManager manager;internal readonly Screen Display;
        internal readonly Rectangle WorkArea;internal readonly PartitionCanvasDraft Draft;
        Point start,last;Rectangle initial;int selected=-1,mode,edges;bool geometryDirty=true,dragged;
        CanvasSeam seam;Dictionary<int,Rectangle> pending=new Dictionary<int,Rectangle>();
        int guideX=-1,guideY=-1;
        Rectangle dividerGrip;
        internal bool Drawing {get {return mode!=0;}}internal bool Applying;
        internal int GeometryFrames;
        internal int Selected {get {return selected;}}
        internal Rectangle Preview(int index) {Rectangle r;return pending.TryGetValue(index,out r)?r:index<0?Rectangle.Empty:Draft.Regions[index].Bounds;}
        const string Help="空白处拖画分区 · 点击区域调整 · 拖动分隔线联动缩放 · Alt 暂停吸附";
        string hint=Help;
        readonly string[] actions={"撤销","重做","取消","完成"};
        int Tolerance {get {return Math.Max(6,(int)(8*ScaleFactor));}}
        internal PartitionCanvasWindow(PartitionManager manager,Screen screen,IEnumerable<Partition> zones,int gap=18) {
            this.manager=manager;Display=screen;WorkArea=screen.WorkingArea;Text="有兜 · 分区画布";KeyPreview=true;
            Draft=new PartitionCanvasDraft(WorkArea.Size,zones.Select(z=>{var r=PartitionLayout.Bounds(z,WorkArea);r.Offset(-WorkArea.X,-WorkArea.Y);return new CanvasRegion {Id=z.Id,Bounds=r};}),gap);
        }
        protected override bool ShowWithoutActivation {get {return false;}}
        protected override CreateParams CreateParams {get {var p=base.CreateParams;p.ExStyle&=~0x08000000;return p;}}
        internal void Raise() {Native.SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x1|0x2|0x40);Activate();}
        Rectangle Button(int i) {int width=Math.Min((int)(82*ScaleFactor),(Width-24)/4);return new Rectangle((Width-width*4)/2+i*width,12,width-4,(int)(34*ScaleFactor));}
        Rectangle Toolbar {get {var first=Button(0);var lastButton=Button(3);return Rectangle.FromLTRB(first.Left-8,first.Top-6,lastButton.Right+8,lastButton.Bottom+6);}}
        protected override void PaintSurface(Graphics g) {
            using(var wash=new SolidBrush(Color.FromArgb(32,25,35,50))) g.FillRectangle(wash,ClientRectangle);
            using(var fill=new SolidBrush(Color.FromArgb(245,247,250))) using(var shape=Round(Toolbar,10)) g.FillPath(fill,shape);
            for(int i=0;i<actions.Length;i++) {
                bool enabled=i==2||!Applying&&(i==3||i==0&&Draft.CanUndo||i==1&&Draft.CanRedo);
                var r=Button(i);using(var fill=new SolidBrush(i==3?Blue:Color.FromArgb(245,247,250))) using(var shape=Round(r,6)) g.FillPath(fill,shape);
                using(var ink=new SolidBrush(!enabled?Color.Gray:i==3?Color.White:Color.FromArgb(35,35,38))) using(var fmt=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center}) g.DrawString(Applying&&i==3?"正在应用…":actions[i],Font,ink,r,fmt);
            }
            var caption=new Rectangle(Math.Max(8,(Width-660)/2),Toolbar.Bottom+6,Math.Min(660,Width-16),28);
            using(var fill=new SolidBrush(Color.FromArgb(225,30,35,45))) using(var shape=Round(caption,6)) g.FillPath(fill,shape);
            using(var ink=new SolidBrush(Color.White)) using(var fmt=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center}) g.DrawString(hint,Font,ink,caption,fmt);
        }
        internal override void InvalidateContent() {base.InvalidateContent();geometryDirty=true;}
        internal override void Tick() {
            base.Tick();if(!geometryDirty||composition==null) return;geometryDirty=false;GeometryFrames++;
            var rectangles=new List<Rectangle>();var styles=new List<int>();
            for(int i=0;i<Draft.Regions.Count;i++) {rectangles.Add(Preview(i));styles.Add(i==selected||pending.ContainsKey(i)?1:0);}
            if(pending.ContainsKey(-1)) {rectangles.Add(pending[-1]);styles.Add(1);}
            if(selected>=0&&selected<Draft.Regions.Count) {
                var r=Preview(selected);int half=Math.Max(3,(int)(3*ScaleFactor));
                foreach(var p in new[]{new Point(r.Left,r.Top),new Point(r.Left+r.Width/2,r.Top),new Point(r.Right,r.Top),new Point(r.Right,r.Top+r.Height/2),new Point(r.Right,r.Bottom),new Point(r.Left+r.Width/2,r.Bottom),new Point(r.Left,r.Bottom),new Point(r.Left,r.Top+r.Height/2)}) {rectangles.Add(new Rectangle(p.X-half,p.Y-half,half*2,half*2));styles.Add(2);}
            }
            if(guideX>=0) {rectangles.Add(new Rectangle(guideX,0,1,Height));styles.Add(3);}
            if(guideY>=0) {rectangles.Add(new Rectangle(0,guideY,Width,1));styles.Add(3);}
            if(!dividerGrip.IsEmpty) {rectangles.Add(dividerGrip);styles.Add(2);}
            composition.SetEditorShapes(rectangles,styles);
        }
        void ShowDivider(CanvasSeam divider) {
            var grip=Rectangle.Empty;
            if(divider!=null) {
                var a=Preview(divider.Low[0]);var b=Preview(divider.High[0]);
                if(divider.Vertical) {int middle=(Math.Max(a.Top,b.Top)+Math.Min(a.Bottom,b.Bottom))/2;grip=new Rectangle((a.Right+b.Left)/2-2,middle-18,4,36);}
                else {int middle=(Math.Max(a.Left,b.Left)+Math.Min(a.Right,b.Right))/2;grip=new Rectangle(middle-18,(a.Bottom+b.Top)/2-2,36,4);}
            }
            if(grip!=dividerGrip) {dividerGrip=grip;geometryDirty=true;}
        }
        int Hit(Point p) {return Draft.Regions.FindLastIndex(r=>r.Bounds.Contains(p));}
        int EdgeAt(Point p) {return selected>=0&&selected<Draft.Regions.Count?PartitionCanvasDraft.Edge(Draft.Regions[selected].Bounds,p,Tolerance):0;}
        static Cursor EdgeCursor(int edge) {
            if(edge==5||edge==10) return Cursors.SizeNWSE;if(edge==6||edge==9) return Cursors.SizeNESW;
            return (edge&3)!=0?Cursors.SizeWE:Cursors.SizeNS;
        }
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);if(e.Button!=MouseButtons.Left) return;
            if(Button(2).Contains(e.Location)) {manager.CloseCanvas();return;}
            if(Applying) return;
            for(int i=0;i<actions.Length;i++) if(Button(i).Contains(e.Location)) {
                CancelGesture();if(i==0) Draft.Undo();if(i==1) Draft.Redo();
                if(i==3) {manager.ApplyCanvas(this);return;}selected=-1;dirty=true;geometryDirty=true;return;
            }
            if(Toolbar.Contains(e.Location)) return;
            start=last=e.Location;dragged=false;pending.Clear();guideX=guideY=-1;edges=EdgeAt(start);
            seam=Draft.SeamAt(start,Tolerance);
            if(seam!=null&&!(edges!=0&&(edges&3)!=0&&(edges&12)!=0)) {mode=4;Cursor=seam.Vertical?Cursors.SizeWE:Cursors.SizeNS;}
            else if(edges!=0) {mode=3;initial=Draft.Regions[selected].Bounds;Cursor=EdgeCursor(edges);}
            else {
                selected=Hit(start);initial=selected<0?Rectangle.Empty:Draft.Regions[selected].Bounds;mode=selected<0?1:2;
                if(mode==1&&(ModifierKeys&Keys.Alt)==0) {int gx,gy;var anchor=Draft.Snap(new Rectangle(start,Size.Empty),-1,5,false,Tolerance,out gx,out gy);start=anchor.Location;}
                Cursor=mode==1?Cursors.Cross:Cursors.SizeAll;
            }
            Capture=true;geometryDirty=true;
        }
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);last=e.Location;
            if(!Drawing) {
                if(Toolbar.Contains(e.Location)||Applying) {Cursor=Cursors.Default;ShowDivider(null);return;}
                int edge=EdgeAt(e.Location);var split=Draft.SeamAt(e.Location,Tolerance);
                ShowDivider(split);
                Cursor=edge!=0&&(edge&3)!=0&&(edge&12)!=0?EdgeCursor(edge):split!=null?(split.Vertical?Cursors.SizeWE:Cursors.SizeNS):edge!=0?EdgeCursor(edge):Hit(e.Location)>=0?Cursors.SizeAll:Cursors.Cross;return;
            }
            if(!dragged&&Math.Abs(e.X-start.X)<3&&Math.Abs(e.Y-start.Y)<3) return;dragged=true;
            var point=new Point(Math.Max(0,Math.Min(Width,e.X)),Math.Max(0,Math.Min(Height,e.Y)));
            guideX=guideY=-1;
            if(mode==4) {
                var changes=Draft.Split(seam,seam.Vertical?point.X-start.X:point.Y-start.Y);
                if(Draft.Valid(changes)) pending=changes;
                ShowDivider(seam);
            } else {
                Rectangle rect;int activeEdges=edges;int index=mode==1?-1:selected;
                if(mode==1) {rect=Rectangle.FromLTRB(Math.Min(start.X,point.X),Math.Min(start.Y,point.Y),Math.Max(start.X,point.X),Math.Max(start.Y,point.Y));activeEdges=(point.X<start.X?1:2)|(point.Y<start.Y?4:8);}
                else if(mode==2) rect=new Rectangle(Math.Max(0,Math.Min(Width-initial.Width,initial.X+point.X-start.X)),Math.Max(0,Math.Min(Height-initial.Height,initial.Y+point.Y-start.Y)),initial.Width,initial.Height);
                else rect=Rectangle.FromLTRB((edges&1)!=0?Math.Min(initial.Right-150,point.X):initial.Left,(edges&4)!=0?Math.Min(initial.Bottom-130,point.Y):initial.Top,(edges&2)!=0?Math.Max(initial.Left+150,point.X):initial.Right,(edges&8)!=0?Math.Max(initial.Top+130,point.Y):initial.Bottom);
                var raw=rect;
                if((ModifierKeys&Keys.Alt)==0) rect=Draft.Snap(rect,index,activeEdges,mode==2,Tolerance,out guideX,out guideY);
                var changes=new Dictionary<int,Rectangle>{{index,rect}};
                if(!Draft.Valid(changes,mode!=1)) {guideX=guideY=-1;changes[index]=raw;}
                if(Draft.Valid(changes,mode!=1)) pending=changes;
                else {guideX=guideY=-1;}
            }
            geometryDirty=true;
        }
        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);if(e.Button!=MouseButtons.Left||!Drawing) return;
            OnMouseMove(e);int oldMode=mode;
            if(dragged&&pending.Count>0&&Draft.Commit(pending)) {if(oldMode==1) selected=Draft.Regions.Count-1;hint=Help;}
            else if(dragged&&oldMode==1) hint="请拖出至少 150 × 130 像素的空白区域；已有分区可选中调整";
            CancelGesture();dirty=true;
        }
        void CancelGesture() {mode=0;Capture=false;pending.Clear();guideX=guideY=-1;dividerGrip=Rectangle.Empty;geometryDirty=true;Cursor=Cursors.Default;}
        protected override void OnMouseCaptureChanged(EventArgs e) {base.OnMouseCaptureChanged(e);if(!Capture&&Drawing) CancelGesture();}
        protected override void OnFormClosing(FormClosingEventArgs e) {e.Cancel=true;manager.CloseCanvas();}
        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if(e.KeyCode==Keys.Escape) {manager.CloseCanvas();return;}if(Applying) return;
            bool undo=e.Control&&e.KeyCode==Keys.Z&&!e.Shift,redo=e.Control&&(e.KeyCode==Keys.Y||e.KeyCode==Keys.Z&&e.Shift);
            if(undo||redo||e.KeyCode==Keys.Delete) {
                CancelGesture();if(undo) Draft.Undo();else if(redo) Draft.Redo();else Draft.Remove(selected);
                selected=-1;dirty=true;e.Handled=true;e.SuppressKeyPress=true;
            }
            if(e.KeyCode==Keys.Menu&&Drawing) OnMouseMove(new MouseEventArgs(MouseButtons.Left,0,last.X,last.Y,0));
        }
        protected override void OnKeyUp(KeyEventArgs e) {base.OnKeyUp(e);if(e.KeyCode==Keys.Menu&&Drawing) OnMouseMove(new MouseEventArgs(MouseButtons.Left,0,last.X,last.Y,0));}
    }
}
