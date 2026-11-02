using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UDo.DesktopZones {
    // Native desktop hosting and gestures only. All editor/menu controls live in Godot.
    internal sealed class ZoneWindow : Form {
        internal const string DragFormat="UDo.DesktopZone.Item.v1";
        internal readonly Zone Model;readonly ZoneManager manager;
        ComposedGlass composition;Rectangle expanded;
        Point press,dragEnd,foldedAt,initialFolded;Rectangle initial;int pressedItem=-1,scroll;
        bool foldedPositionReady;
        bool moving,resizing,hover,revealed,sourceDragging,dropTarget,hoverSuppressed,attached,dirty=true,foldPressed,waitForPointerExit;
        internal int RenderCount;
        internal int LastPaintedItems;
        Bitmap pixels;readonly VisibleItemCache itemCache=new VisibleItemCache();
        internal int CachedItems {get {return itemCache.Count;}}
        internal long CachedBytes {get {return itemCache.Bytes;}}
        internal int TileRasterizations {get {return itemCache.Rasterizations;}}
        string pressedPath;bool waitingForVisibleIcons;int hoveredItem=-1,paintedHover=-1;float hoverBlend;
        DateTime lastMotionTick;
        int appearance=-1;
        DateTime entered,leaveAt;float scale=1;
        internal bool UiOpen;
        internal float ScaleFactor {get {return scale;}}
        internal bool Manipulating {get {return moving||resizing||sourceDragging||dropTarget;}}
        internal bool IsBar {get {return Model.Edge!="none"&&!revealed&&!moving;}}
        internal Rectangle ExpandedBounds {get {return expanded;}}
        internal Rectangle FoldedBounds {get {return BoxPlacement.Folded(expanded,BoxPlacement.WorkArea(Screen.FromPoint(foldedAt)),foldedAt,scale);}}
        internal Rectangle ScreenBounds {get {return new Rectangle(PointToScreen(Point.Empty),Size);}}
        internal ZoneWindow(Zone model,ZoneManager manager) {
            Model=model;this.manager=manager;
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
            AutoScaleMode=AutoScaleMode.None;AllowDrop=true;Text="有兜收纳盒 · "+model.Name;
            Font=new Font("Microsoft YaHei UI",9);BackColor=Color.Black;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint,true);
            DragEnter+=delegate(object s,DragEventArgs e) {e.Effect=Effect(e);if(e.Effect!=DragDropEffects.None) {dropTarget=true;hoveredItem=-1;dirty=true;Reveal();}};
            DragOver+=delegate(object s,DragEventArgs e) {e.Effect=Effect(e);};
            DragLeave+=delegate {dropTarget=false;hoveredItem=-1;dirty=true;leaveAt=DateTime.UtcNow;};
            DragDrop+=delegate(object s,DragEventArgs e) {
                dropTarget=false;StopHoverUntilMovement();int index=Hit(PointToClient(new Point(e.X,e.Y)));if(index<0) index=Model.Items.Count;
                if(e.Data.GetDataPresent(DragFormat)) manager.Transfer((string)e.Data.GetData(DragFormat),Model,index);
                else if(e.Data.GetDataPresent(DataFormats.FileDrop)) manager.Add(Model,(string[])e.Data.GetData(DataFormats.FileDrop),index);
            };
        }
        static DragDropEffects Effect(DragEventArgs e) {return e.Data.GetDataPresent(DragFormat)?DragDropEffects.Move:e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Link:DragDropEffects.None;}
        protected override bool ShowWithoutActivation {get {return true;}}
        protected override CreateParams CreateParams {get {var p=base.CreateParams;p.ExStyle|=0x08200080;p.ClassStyle&=~0x20000;return p;}}
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { /* Composition retains pixels across moves/exposure. */ }
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);int policy=1,border=-2;
            Native.DwmSetWindowAttribute(Handle,2,ref policy,4);Native.DwmSetWindowAttribute(Handle,34,ref border,4);
            var accent=new Native.ACCENT {state=5};
            IntPtr memory=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.ACCENT)));
            try {
                Marshal.StructureToPtr(accent,memory,false);
                var data=new Native.COMPOSITION {attribute=19,data=memory,size=Marshal.SizeOf(typeof(Native.ACCENT))};
                Native.SetWindowCompositionAttribute(Handle,ref data);composition=new ComposedGlass(Handle);
            } finally {Marshal.FreeHGlobal(memory);}
        }
        internal void Attach(IntPtr host,Rectangle bounds) {
            IntPtr handle=Handle;
            // Host backdrop sampling requires a top-level composition surface, as
            // used by the Dock. Keep this no-activate tool window in the bottom band.
            Native.SetWindowLongPtr(handle,-8,DesktopAdapter.GetAncestor(host,2));
            using(var g=CreateGraphics()) scale=Math.Max(.75f,g.DpiX/96f);
            bounds.Width=ZoneLayout.CompactWidth(bounds.Width,scale);
            attached=true;SetExpandedBounds(bounds);Show();
            Native.SetWindowPos(handle,new IntPtr(1),0,0,0,0,0x1|0x2|0x10|0x40);Render();
        }
        internal void SetScreenBounds(Rectangle bounds) {SetExpandedBounds(bounds);}
        internal void SetExpandedBounds(Rectangle bounds) {
            var work=BoxPlacement.WorkArea(Screen.FromRectangle(bounds));
            expanded=BoxPlacement.Anchor(bounds,work,Model.Edge);
            if(!foldedPositionReady) {
                foldedAt=Model.FoldX>=0&&Model.FoldY>=0?new Point(work.Left+(int)Math.Round(Model.FoldX*work.Width),work.Top+(int)Math.Round(Model.FoldY*work.Height)):expanded.Location;
                foldedPositionReady=true;
            }
            ApplyBounds();
        }
        internal void ResetFoldedPosition() {foldedAt=expanded.Location;Model.FoldX=Model.FoldY=-1;}
        internal void MoveFoldedTo(Point point,Rectangle work) {foldedAt=BoxPlacement.Folded(expanded,work,point,scale).Location;ApplyBounds();}
        void ApplyBounds() {
            if(!attached) return;
            Rectangle work=Model.Collapsed&&Model.Edge=="none"?BoxPlacement.WorkArea(Screen.FromPoint(foldedAt)):BoxPlacement.WorkArea(Screen.FromRectangle(expanded));
            Rectangle visible=BoxPlacement.Visible(expanded,work,Model.Edge,Model.Collapsed,revealed||moving,scale,foldedAt);
            bool resized=Size!=visible.Size;Bounds=visible;if(resized) dirty=true;
        }
        internal void RefreshAppearance() {Text="有兜收纳盒 · "+Model.Name;ApplyBounds();dirty=true;}
        internal void ContentChanged() {scroll=0;hoveredItem=-1;RefreshAppearance();}
        void ClampScroll() {scroll=Math.Max(0,Math.Min(scroll,Math.Max(0,ZoneLayout.Height(Width,Model.Items.Count,true,scale)-Height)));}
        internal void IconsReady(HashSet<string> loaded) {
            itemCache.Invalidate(loaded);
            if(IsBar||(Model.Collapsed&&Model.Edge=="none")) return;
            // A full shared queue may have rejected a visible request. Retry after
            // progress, even when this completion belongs to another box.
            if(waitingForVisibleIcons&&loaded.Count>0) dirty=true;
            int first,end;ZoneLayout.VisibleRange(Width,Height,scroll,Model.Items.Count,scale,out first,out end);
            for(int i=first;i<end;i++) if(loaded.Contains(Model.Items[i])) {dirty=true;break;}
        }
        internal void Tick() {
            if(IsDisposed||!attached) return;
            if(appearance!=DesktopAppearance.Revision) {appearance=DesktopAppearance.Revision;itemCache.Clear();dirty=true;}
            if(!Native.IsWindowVisible(Handle)) {Native.ShowWindow(Handle,4);Native.SetWindowPos(Handle,new IntPtr(1),0,0,0,0,0x1|0x2|0x10);}
            bool inside=ScreenBounds.Contains(Cursor.Position);
            if(waitForPointerExit&&!inside) {waitForPointerExit=false;hover=false;}
            if(inside&&!hover) {hover=true;entered=DateTime.UtcNow;dirty=true;}
            if(!inside&&hover) {hover=false;leaveAt=DateTime.UtcNow;dirty=true;}
            UpdateHover();
            TickHoverMotion();
            if(IsBar&&!waitForPointerExit&&hover&&(DateTime.UtcNow-entered).TotalMilliseconds>=160) Reveal();
            if(Model.Edge!="none"&&revealed&&!hover&&!UiOpen&&!Manipulating&&(DateTime.UtcNow-leaveAt).TotalMilliseconds>380) {revealed=false;ApplyBounds();}
            if(dirty) Render();
        }
        void Reveal() {if(Model.Edge!="none") {revealed=true;ApplyBounds();}}
        internal void Retract() {revealed=false;waitForPointerExit=true;ApplyBounds();}
        void TickHoverMotion() {
            DateTime now=DateTime.UtcNow;
            double seconds=lastMotionTick==DateTime.MinValue?0.016:Math.Max(0,Math.Min(0.08,(now-lastMotionTick).TotalSeconds));
            lastMotionTick=now;
            if(hoveredItem>=0&&hoveredItem!=paintedHover) {paintedHover=hoveredItem;hoverBlend=0;dirty=true;}
            float target=hoveredItem==paintedHover&&hoveredItem>=0?1f:0f;
            float next=hoverBlend+(target-hoverBlend)*(float)(1-Math.Exp(-seconds/0.07));
            if(Math.Abs(next-hoverBlend)>0.01f) {hoverBlend=next;dirty=true;}
            else if(target==0&&paintedHover>=0) {hoverBlend=0;paintedHover=-1;dirty=true;}
            else if(target==1&&hoverBlend<1) {hoverBlend=1;dirty=true;}
        }
        void Render() {
            if(composition==null||Width<1||Height<1) return;dirty=false;RenderCount++;
            composition.Resize(Width,Height,(int)(20*scale),Model.Theme,scale);
            composition.SetMaterialOpacity(IsBar?0:1-Model.Transparency);
            if(pixels==null||pixels.Size!=Size) {if(pixels!=null) pixels.Dispose();pixels=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb);}
            {
                using(var g=Graphics.FromImage(pixels)) {
                    g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;
                    g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    if(IsBar) {
                        itemCache.Clear();LastPaintedItems=0;
                        float thickness=Math.Min(Width,Height);
                        using(var pen=new Pen(Color.FromArgb(245,255,255,255),Math.Max(2,thickness-2))) {
                            pen.StartCap=LineCap.Round;pen.EndCap=LineCap.Round;
                            if(Width<Height) g.DrawLine(pen,Width/2f,thickness/2,Width/2f,Height-thickness/2);
                            else g.DrawLine(pen,thickness/2,Height/2f,Width-thickness/2,Height/2f);
                        }
                    } else PaintContent(g);
                }
                composition.SetContent(pixels);
            }
        }
        void PaintContent(Graphics g) {
            LastPaintedItems=0;
            Color ink=Model.Theme=="dark"?Color.FromArgb(240,240,245):Color.FromArgb(35,35,38);
            using(var brush=new SolidBrush(ink)) using(var format=new StringFormat {Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap}) {
                if(Model.ShowName) using(var title=new Font(Font.FontFamily,10,FontStyle.Bold)) g.DrawString(Model.Name,title,brush,new RectangleF(18*scale,11*scale,Width-58*scale,25*scale),format);
            }
            using(var pen=new Pen(Color.FromArgb(hover?210:125,ink),1.5f*scale)) {
                float x=Width-24*scale,y=20*scale,d=Model.Collapsed&&Model.Edge=="none"?-1:1;
                g.DrawLines(pen,new[]{new PointF(x-4*scale,y-d*2*scale),new PointF(x,y+d*2*scale),new PointF(x+4*scale,y-d*2*scale)});
            }
            if(Model.Collapsed&&Model.Edge=="none") {itemCache.Clear();return;}
            ClampScroll();
            int start=(int)(44*scale);var saved=g.Save();g.SetClip(new Rectangle(0,start,Width,Math.Max(0,Height-start-8)));
            int first,end;ZoneLayout.VisibleRange(Width,Height,scroll,Model.Items.Count,scale,out first,out end);
            var tileSize=new Size(DesktopAppearance.CellWidth(scale),DesktopAppearance.CellHeight(scale));
            itemCache.Begin(tileSize,DesktopAppearance.Revision+":"+scale,Model.Items,first,end);
            waitingForVisibleIcons=false;
            for(int i=first;i<end;i++) {
                Rectangle cell=Cell(i);if(cell.Bottom<start||cell.Top>Height) continue;
                string path=Model.Items[i];LastPaintedItems++;
                if(!sourceDragging&&!dropTarget&&i==paintedHover&&hoverBlend>0) using(var fill=new SolidBrush(Color.FromArgb((int)(42*hoverBlend),13,111,255))) using(var shape=Rounded(cell,6*scale)) g.FillPath(fill,shape);
                manager.Icons.Get(path,DesktopAppearance.Side(scale));
                waitingForVisibleIcons|=!manager.Icons.Contains(path);
                itemCache.Draw(g,cell.Location,path,delegate(Graphics tile,string boundPath) {
                    DesktopAppearance.Paint(tile,tileSize,DesktopAppearance.Side(scale),scale,boundPath,manager.Icons);
                });
            }
            if(Model.Items.Count==0) using(var brush=new SolidBrush(Color.FromArgb(130,ink))) using(var format=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center}) g.DrawString("拖入图标，收好桌面",Font,brush,new RectangleF(12,start,Width-24,Height-start-12),format);
            g.Restore(saved);
            if(hover) using(var pen=new Pen(Color.FromArgb(95,ink),1.3f*scale)) for(int i=0;i<2;i++) g.DrawLine(pen,Width-(8+i*4)*scale,Height-8*scale,Width-8*scale,Height-(8+i*4)*scale);
        }
        internal Rectangle Cell(int index) {var cell=ZoneLayout.Cell(index,Width,true,scale);cell.Y-=scroll;return cell;}
        void StopHoverUntilMovement() {hoveredItem=-1;hoverSuppressed=true;dragEnd=Cursor.Position;dirty=true;}
        void UpdateHover() {int next=-1;Point cursor=Cursor.Position;if(hoverSuppressed) {if(!ScreenBounds.Contains(cursor)||Math.Abs(cursor.X-dragEnd.X)+Math.Abs(cursor.Y-dragEnd.Y)>3) hoverSuppressed=false;}if(!hoverSuppressed&&!sourceDragging&&!dropTarget&&!moving&&!resizing&&!IsBar&&ScreenBounds.Contains(cursor)&&DesktopAdapter.WindowFromPoint(cursor)==Handle) next=Hit(PointToClient(cursor));if(next!=hoveredItem) {hoveredItem=next;dirty=true;}}
        static GraphicsPath Rounded(Rectangle rect,float radius) {var path=new GraphicsPath();float d=Math.Min(Math.Min(rect.Width,rect.Height),radius*2);path.AddArc(rect.Left,rect.Top,d,d,180,90);path.AddArc(rect.Right-d,rect.Top,d,d,270,90);path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90);path.AddArc(rect.Left,rect.Bottom-d,d,d,90,90);path.CloseFigure();return path;}
        int Hit(Point point) {if(IsBar||(Model.Collapsed&&Model.Edge=="none")) return -1;return ZoneLayout.Hit(point,Width,Height,scroll,Model.Items.Count,scale);}
        bool FoldHit(Point point) {return !IsBar&&point.X>Width-42*scale&&point.Y<40*scale;}
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);if(e.Button!=MouseButtons.Left) return;
            if(UiOpen) manager.DismissMenu();
            foldPressed=FoldHit(e.Location);if(foldPressed) return;
            press=Cursor.Position;initial=expanded;initialFolded=foldedAt;pressedItem=Hit(e.Location);pressedPath=pressedItem>=0?Model.Items[pressedItem]:null;
            resizing=!IsBar&&!Model.Collapsed&&e.X>Width-16*scale&&e.Y>Height-16*scale;
            moving=!resizing&&pressedItem<0;if(moving||resizing) Capture=true;
        }
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);Point now=Cursor.Position;
            UpdateHover();
            if(e.Button==MouseButtons.Left) {
                if(moving||resizing) {
                    if(Math.Abs(now.X-press.X)+Math.Abs(now.Y-press.Y)<4) return;
                    Rectangle rect=initial;Model.Edge="none";revealed=false;
                    if(moving) rect.Offset(now.X-press.X,now.Y-press.Y);
                    else {rect.Width=Math.Max(180,initial.Width+now.X-press.X);rect.Height=Math.Max(150,initial.Height+now.Y-press.Y);}
                    expanded=BoxPlacement.Anchor(rect,BoxPlacement.WorkArea(Screen.FromPoint(now)),"none");
                    if(Model.Collapsed&&moving) MoveFoldedTo(new Point(initialFolded.X+now.X-press.X,initialFolded.Y+now.Y-press.Y),BoxPlacement.WorkArea(Screen.FromPoint(now)));
                    else ApplyBounds();
                    return;
                }
                if(pressedPath!=null&&Math.Abs(now.X-press.X)+Math.Abs(now.Y-press.Y)>8&&Model.Items.Contains(pressedPath)) {
                    string path=pressedPath;pressedPath=null;pressedItem=-1;var data=new DataObject();data.SetData(DragFormat,path);
                    bool cancelled=false;QueryContinueDragEventHandler cancel=delegate(object s,QueryContinueDragEventArgs a) {if(a.EscapePressed) cancelled=true;};
                    QueryContinueDrag+=cancel;sourceDragging=true;hoveredItem=-1;dirty=true;DragDropEffects result;
                    try {result=DoDragDrop(data,DragDropEffects.Move);} finally {QueryContinueDrag-=cancel;sourceDragging=false;StopHoverUntilMovement();leaveAt=DateTime.UtcNow;}
                    if(!cancelled&&result==DragDropEffects.None&&manager.IsDesktopPoint(Cursor.Position)) manager.Release(Model,path,Cursor.Position);
                }
            }
            Cursor=!IsBar&&!Model.Collapsed&&e.X>Width-16*scale&&e.Y>Height-16*scale?Cursors.SizeNWSE:Cursors.Default;
        }
        protected override void OnMouseLeave(EventArgs e) {base.OnMouseLeave(e);if(hoveredItem!=-1) {hoveredItem=-1;dirty=true;}}
        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);
            if(e.Button==MouseButtons.Right) {manager.ShowMenu(this,PointToScreen(e.Location),Hit(e.Location));return;}
            if(foldPressed) {foldPressed=false;if(FoldHit(e.Location)) {if(Model.Edge!="none") Retract();else manager.Fold(this);}return;}
            if(moving||resizing) {
                bool changedSize=resizing;moving=resizing=false;Capture=false;
                if(changedSize) Model.AutoFit=false;
                else if(Math.Abs(Cursor.Position.X-press.X)+Math.Abs(Cursor.Position.Y-press.Y)>=4) {
                    var work=BoxPlacement.WorkArea(Screen.FromPoint(Cursor.Position));
                    // A folded header remains a full-width strip at the bottom;
                    // snapping it to the edge would replace it with a tiny handle.
                    Model.Edge=Model.Collapsed?"none":BoxPlacement.Snap(expanded,work,scale);
                    expanded=BoxPlacement.Anchor(expanded,work,Model.Edge);
                }
                revealed=false;waitForPointerExit=Model.Edge!="none";manager.Placed(this);
            }
            pressedItem=-1;pressedPath=null;
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e) {base.OnMouseDoubleClick(e);int i=Hit(e.Location);if(e.Button==MouseButtons.Left) {if(i>=0) manager.Open(Model.Items[i]);else if(Model.Edge=="none") manager.Fold(this);}}
        protected override void OnMouseWheel(MouseEventArgs e) {
            base.OnMouseWheel(e);int total=ZoneLayout.Height(Width,Model.Items.Count,true,scale);
            scroll=Math.Max(0,Math.Min(Math.Max(0,total-Height),scroll-e.Delta/120*(int)(94*scale)));dirty=true;
        }
        protected override void WndProc(ref Message message) {
            if(message.Msg==0x2E0) {scale=Math.Max(.75f,(message.WParam.ToInt64()&0xffff)/96f);dirty=true;}
            base.WndProc(ref message);
        }
        protected override void Dispose(bool disposing) {
            if(disposing) {itemCache.Dispose();if(pixels!=null) pixels.Dispose();if(composition!=null) {composition.Dispose();composition=null;}Font.Dispose();}
            base.Dispose(disposing);
        }
    }
}
