using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using UDo.DesktopZones;

namespace UDo.DesktopPartitions {
    // Composition surface only. Menus, forms, file picking and confirmations are Godot controls.
    internal abstract class PartitionSurface : Form {
        protected ComposedGlass composition;protected bool dirty=true,attached;internal float ScaleFactor=1;
        Bitmap pixels;
        Rectangle[] appliedHoles;Size regionSize;
        internal int RenderCount;
        internal int RegionUpdates;
        internal int PixelAllocations;
        protected PartitionSurface() {
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
            AutoScaleMode=AutoScaleMode.None;BackColor=Color.Black;Font=new Font("Microsoft YaHei UI",9);
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint,true);
        }
        protected override bool ShowWithoutActivation {get {return true;}}
        protected override CreateParams CreateParams {get {var p=base.CreateParams;p.ExStyle|=0x08200080;p.ClassStyle&=~0x20000;return p;}}
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);int policy=1,border=-2;Native.DwmSetWindowAttribute(Handle,2,ref policy,4);Native.DwmSetWindowAttribute(Handle,34,ref border,4);
            var accent=new Native.ACCENT {state=5};IntPtr memory=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.ACCENT)));
            try {Marshal.StructureToPtr(accent,memory,false);var data=new Native.COMPOSITION {attribute=19,data=memory,size=Marshal.SizeOf(typeof(Native.ACCENT))};Native.SetWindowCompositionAttribute(Handle,ref data);composition=new ComposedGlass(Handle,true);}
            finally {Marshal.FreeHGlobal(memory);}
        }
        internal void Attach(IntPtr host,Rectangle bounds) {
            long started=PerformanceProbe.Start();
            Native.SetWindowLongPtr(Handle,-8,DesktopAdapter.GetAncestor(host,2));using(var g=CreateGraphics()) ScaleFactor=Math.Max(.75f,g.DpiX/96f);
            attached=true;SetBounds(bounds);Show();Native.SetWindowPos(Handle,new IntPtr(1),0,0,0,0,0x1|0x2|0x10|0x40);Tick();
            PerformanceProbe.End("time_to_first_visible_ms",started);
        }
        internal void SetBounds(Rectangle bounds) {bool resize=Size!=bounds.Size;Bounds=bounds;if(resize) dirty=true;}
        internal virtual void InvalidateContent() {dirty=true;}
        protected void ApplyHoles(Rectangle[] local) {
            bool changed=appliedHoles==null||regionSize!=Size||appliedHoles.Length!=local.Length;
            if(!changed) for(int i=0;i<local.Length;i++) if(appliedHoles[i]!=local[i]) {changed=true;break;}
            if(!changed) return;
            var region=new Region(ClientRectangle);foreach(var r in local) region.Exclude(r);
            var old=Region;Region=region;if(old!=null) old.Dispose();appliedHoles=local;regionSize=Size;RegionUpdates++;
        }
        protected void BeginGeometryGesture() {
            // A fixed-size HWND region would clip growth during resizing. Capture
            // already routes input here; restore precise icon holes on release.
            var old=Region;Region=null;if(old!=null) old.Dispose();appliedHoles=null;RegionUpdates++;
        }
        internal virtual void Tick() {
            if(IsDisposed||!attached) return;
            if(!Native.IsWindowVisible(Handle)) {Native.ShowWindow(Handle,4);Native.SetWindowPos(Handle,new IntPtr(1),0,0,0,0,0x1|0x2|0x10);}
            if(!dirty||composition==null||Width<1||Height<1) return;dirty=false;RenderCount++;
            long started=PerformanceProbe.Start();
            composition.Resize(Width,Height,0,"light",ScaleFactor);
            // Match the existing GPU allocation buckets. A one-pixel resize must
            // not allocate/dispose a multi-megabyte CPU surface on every tick.
            var capacity=new Size((Width+127)/128*128,(Height+127)/128*128);
            if(pixels==null||pixels.Size!=capacity) {if(pixels!=null) pixels.Dispose();pixels=new Bitmap(capacity.Width,capacity.Height,PixelFormat.Format32bppPArgb);PixelAllocations++;}
            using(var g=Graphics.FromImage(pixels)) {g.Clear(Color.Transparent);g.SetClip(ClientRectangle);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;PaintSurface(g);}
            composition.SetContent(pixels,Width,Height);
            PerformanceProbe.End("partition_render_ms",started);
        }
        protected abstract void PaintSurface(Graphics g);
        internal static GraphicsPath Round(RectangleF r,float radius) {
            var p=new GraphicsPath();float d=Math.Min(Math.Min(r.Width,r.Height),radius*2);
            if(d<1) p.AddRectangle(r);else {p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();}return p;
        }
        internal static readonly Color Blue=Color.FromArgb(13,111,255);
        protected override void WndProc(ref Message m) {if(m.Msg==0x2E0) {ScaleFactor=Math.Max(.75f,(m.WParam.ToInt64()&0xffff)/96f);InvalidateContent();}base.WndProc(ref m);}
        protected override void Dispose(bool disposing) {if(disposing) {if(pixels!=null) pixels.Dispose();if(composition!=null) composition.Dispose();Font.Dispose();}base.Dispose(disposing);}
    }
    internal sealed class PartitionWindow : PartitionSurface {
        internal readonly Partition Model;readonly PartitionManager manager;
        Point press,dragEnd;Rectangle initial;int pressed=-1,selected=-1,hovered=-1,paintedHover=-1;float hoverBlend;DateTime lastMotionTick;string pressedPath,selectedPath;bool moving,resizing,sourceDragging,dropTarget,hoverSuppressed;string background="";Image image;
        Rectangle pendingBounds;bool geometryPending;
        internal int LastPaintedItems;
        int appearance=-1;bool waitingForIcons;
        PartitionLayout.Grid gridCache;Rectangle[] holes=new Rectangle[0];Size gridSize;int gridItemCount=-1;
        readonly PartitionItemCache itemCache=new PartitionItemCache();
        internal bool Manipulating {get {return moving||resizing||sourceDragging||dropTarget;}}
        internal PartitionWindow(Partition model,PartitionManager manager) {
            Model=model;this.manager=manager;Text="有兜无感分区 · "+model.Name;AllowDrop=true;
            DragEnter+=delegate(object s,DragEventArgs e) {e.Effect=Effect(e);if(e.Effect!=DragDropEffects.None) {dropTarget=true;hovered=-1;dirty=true;}};
            DragOver+=delegate(object s,DragEventArgs e) {e.Effect=Effect(e);};
            DragLeave+=delegate {dropTarget=false;hovered=-1;dirty=true;};
            DragDrop+=delegate(object s,DragEventArgs e) {dropTarget=false;StopHoverUntilMovement();
                if(e.Data.GetDataPresent(ZoneWindow.DragFormat)) manager.Transfer(new[]{(string)e.Data.GetData(ZoneWindow.DragFormat)},Model);
                else if(e.Data.GetDataPresent(DataFormats.FileDrop)) manager.Add((string[])e.Data.GetData(DataFormats.FileDrop),Model);
            };
        }
        static DragDropEffects Effect(DragEventArgs e) {return e.Data.GetDataPresent(ZoneWindow.DragFormat)?DragDropEffects.Move:e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Link:DragDropEffects.None;}
        internal PartitionLayout.Grid Grid {get {if(gridCache==null||gridSize!=Size||gridItemCount!=Model.Items.Count) {gridSize=Size;gridItemCount=Model.Items.Count;gridCache=PartitionLayout.GridFor(Model.Style,Size,manager.Editing,ScaleFactor,gridItemCount,holes);}return gridCache;}}
        internal override void InvalidateContent() {gridCache=null;gridItemCount=-1;if(selectedPath!=null&&!Model.Items.Contains(selectedPath,StringComparer.OrdinalIgnoreCase)) selectedPath=null;hovered=-1;base.InvalidateContent();}
        void ApplyPendingBounds() {if(!geometryPending) return;geometryPending=false;SetBounds(pendingBounds);}
        internal override void Tick() {if(appearance!=DesktopAppearance.Revision) {appearance=DesktopAppearance.Revision;InvalidateContent();}ApplyPendingBounds();UpdateHover();TickHoverMotion();base.Tick();}
        void TickHoverMotion() {
            DateTime now=DateTime.UtcNow;
            double seconds=lastMotionTick==DateTime.MinValue?0.016:Math.Max(0,Math.Min(0.08,(now-lastMotionTick).TotalSeconds));
            lastMotionTick=now;
            if(sourceDragging||dropTarget||moving||resizing) {if(paintedHover>=0||hoverBlend>0) {paintedHover=-1;hoverBlend=0;dirty=true;}return;}
            if(hovered>=0&&hovered!=paintedHover) {paintedHover=hovered;hoverBlend=0;dirty=true;}
            float target=hovered==paintedHover&&hovered>=0?1f:0f;
            float next=hoverBlend+(target-hoverBlend)*(float)(1-Math.Exp(-seconds/0.07));
            if(Math.Abs(next-hoverBlend)>0.01f) {hoverBlend=next;dirty=true;}
            else if(target==0&&paintedHover>=0) {paintedHover=-1;hoverBlend=0;dirty=true;}
            else if(target==1&&hoverBlend<1) {hoverBlend=1;dirty=true;}
        }
        void StopHoverUntilMovement() {hovered=-1;hoverSuppressed=true;dragEnd=Cursor.Position;dirty=true;}
        void UpdateHover() {int next=-1;Point cursor=Cursor.Position;if(hoverSuppressed) {if(!Bounds.Contains(cursor)||Math.Abs(cursor.X-dragEnd.X)+Math.Abs(cursor.Y-dragEnd.Y)>3) hoverSuppressed=false;}if(!hoverSuppressed&&!sourceDragging&&!dropTarget&&!moving&&!resizing&&IsHandleCreated&&Bounds.Contains(cursor)&&DesktopAdapter.WindowFromPoint(cursor)==Handle) next=Grid.Hit(PointToClient(cursor));if(next!=hovered) {hovered=next;dirty=true;}}
        internal Rectangle OverflowRect {get {var grid=Grid;if(grid.Overflow>0&&grid.Capacity>0) {var cell=grid.Cell(grid.Visible);return new Rectangle(cell.Left,cell.Top+cell.Height/2-(int)(12*ScaleFactor),cell.Width,(int)(24*ScaleFactor));}return new Rectangle(Math.Max(0,Width-(int)(92*ScaleFactor)),Math.Max(0,Height-(int)(31*ScaleFactor)),(int)(84*ScaleFactor),(int)(24*ScaleFactor));}}
        internal void IconsReady(HashSet<string> loaded) {itemCache.Invalidate(loaded);if(waitingForIcons&&loaded.Count>0) dirty=true;var grid=Grid;for(int i=0;i<Math.Min(grid.Visible,Model.Items.Count);i++) if(loaded.Contains(Model.Items[i])) {dirty=true;break;}}
        internal void UpdateHoles(Rectangle[] holes,Rectangle[] overlays=null) {
            var local=new List<Rectangle>();
            foreach(var hole in holes) {var r=hole;r.Offset(-Left,-Top);if(!r.IntersectsWith(ClientRectangle)) continue;local.Add(r);}
            bool changed=this.holes.Length!=local.Count;
            if(!changed) for(int i=0;i<local.Count;i++) if(this.holes[i]!=local[i]) {changed=true;break;}
            this.holes=local.ToArray();
            // Boxes and widgets must stay visually and interactively above the
            // partition. Do not reflow partition icons as those surfaces move.
            var regionHoles=new List<Rectangle>(local);
            if(overlays!=null) foreach(var overlay in overlays) {var r=overlay;r.Offset(-Left,-Top);if(r.IntersectsWith(ClientRectangle)) regionHoles.Add(r);}
            ApplyHoles(regionHoles.ToArray());if(changed) InvalidateContent();
        }
        protected override void PaintSurface(Graphics g) {
            var style=Model.Style;var bounds=new RectangleF(1,1,Math.Max(1,Width-2),Math.Max(1,Height-2));var grid=Grid;
            using(var outline=Round(bounds,style.Radius*ScaleFactor)) {
                if(style.Background=="color") using(var fill=new SolidBrush(Color.FromArgb(style.Opacity*255/100,ColorTranslator.FromHtml(style.Color)))) g.FillPath(fill,outline);
                string imageKey=style.Background=="image"?style.Image:"";
                if(background!=imageKey) {
                    if(image!=null) image.Dispose();image=null;background=imageKey;
                    try {if(!String.IsNullOrEmpty(background)) using(var stream=new MemoryStream(Convert.FromBase64String(background))) using(var decoded=Image.FromStream(stream)) {
                        float limit=Math.Max(256,Math.Min(4096,Math.Max(Width,Height))),ratio=Math.Min(1,limit/Math.Max(decoded.Width,decoded.Height));
                        image=new Bitmap(Math.Max(1,(int)(decoded.Width*ratio)),Math.Max(1,(int)(decoded.Height*ratio)),PixelFormat.Format32bppPArgb);
                        using(var copy=Graphics.FromImage(image)) {copy.InterpolationMode=InterpolationMode.HighQualityBicubic;copy.DrawImage(decoded,new Rectangle(0,0,image.Width,image.Height));}
                    }} catch { }
                }
                if(style.Background=="image"&&image!=null) {
                    var saved=g.Save();g.SetClip(outline);float ratio=Math.Max((float)Width/image.Width,(float)Height/image.Height);var rect=new Rectangle((int)((Width-image.Width*ratio)/2),(int)((Height-image.Height*ratio)/2),(int)(image.Width*ratio),(int)(image.Height*ratio));
                    using(var attr=new ImageAttributes()) {var matrix=new ColorMatrix();matrix.Matrix33=style.Opacity/100f;attr.SetColorMatrix(matrix);g.DrawImage(image,rect,0,0,image.Width,image.Height,GraphicsUnit.Pixel,attr);}g.Restore(saved);
                }
                if(style.Border) using(var pen=new Pen(ColorTranslator.FromHtml(style.BorderColor),ScaleFactor)) g.DrawPath(pen,outline);
                if(manager.Editing||dropTarget) {
                    if(manager.Editing) using(var fill=new SolidBrush(Color.FromArgb(10,Blue))) g.FillPath(fill,outline);
                    using(var pen=new Pen(Blue,(Model.Id==manager.Selected||dropTarget?2:1)*ScaleFactor)) {if(Model.Id!=manager.Selected&&!dropTarget) pen.DashStyle=DashStyle.Dash;g.DrawPath(pen,outline);}
                }
            }
            if(style.TitleVisible||manager.Editing) {
                if(manager.Editing) using(var fill=new SolidBrush(Color.FromArgb(243,246,251))) using(var p=Round(new RectangleF(3,3,Math.Max(1,Width-6),27*ScaleFactor),7*ScaleFactor)) g.FillPath(fill,p);
                using(var brush=new SolidBrush(manager.Editing?Color.FromArgb(35,35,38):ColorTranslator.FromHtml(style.TitleColor))) using(var format=new StringFormat {Alignment=style.TitleAlign=="center"?StringAlignment.Center:style.TitleAlign=="right"?StringAlignment.Far:StringAlignment.Near,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap}) using(var font=new Font(Font.FontFamily,9,FontStyle.Bold)) g.DrawString(Model.Name,font,brush,new RectangleF(10*ScaleFactor,7*ScaleFactor,Math.Max(1,Width-65*ScaleFactor),22*ScaleFactor),format);
                if(manager.Editing) using(var brush=new SolidBrush(Color.FromArgb(110,115,125))) g.DrawString(Model.Items.Count.ToString(),Font,brush,Width-45*ScaleFactor,7*ScaleFactor);
            }
            LastPaintedItems=Math.Min(grid.Visible,Model.Items.Count);
            int side=style.FollowDesktop?DesktopAppearance.Side(ScaleFactor):(int)(style.IconSize*ScaleFactor);
            itemCache.Begin(new Size(grid.CellWidth,grid.CellHeight),side,ScaleFactor,Font,Model.Items,LastPaintedItems);waitingForIcons=false;
            for(int i=0;i<LastPaintedItems;i++) {
                var cell=grid.Cell(i);string path=Model.Items[i];
                manager.Icons.Get(path,side);waitingForIcons|=!manager.Icons.Contains(path);
                int highlight=i==paintedHover?(int)(42*hoverBlend):0;
                if(selectedPath!=null&&String.Equals(path,selectedPath,StringComparison.OrdinalIgnoreCase)) highlight=Math.Max(55,highlight);
                if(!sourceDragging&&!dropTarget&&highlight>0) using(var fill=new SolidBrush(Color.FromArgb(highlight,Blue))) using(var p=Round(cell,6*ScaleFactor)) g.FillPath(fill,p);
                itemCache.Draw(g,cell.Location,path,manager.Icons);
            }
            if(grid.Overflow>0) {
                using(var p=Round(OverflowRect,6*ScaleFactor)) using(var fill=new SolidBrush(Color.FromArgb(245,246,248))) g.FillPath(fill,p);
                using(var brush=new SolidBrush(Color.FromArgb(35,35,38))) using(var format=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center}) g.DrawString("＋"+grid.Overflow+" 项",Font,brush,OverflowRect,format);
            }
            if(manager.Editing) using(var pen=new Pen(Blue,1.5f*ScaleFactor)) for(int i=0;i<2;i++) g.DrawLine(pen,Width-(6+i*5)*ScaleFactor,Height-6*ScaleFactor,Width-6*ScaleFactor,Height-(6+i*5)*ScaleFactor);
        }
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);if(e.Button!=MouseButtons.Left) return;
            manager.Select(Model.Id);press=PointToScreen(e.Location);initial=Bounds;pressed=Grid.Hit(e.Location);selected=pressed;pressedPath=selectedPath=pressed>=0?Model.Items[pressed]:null;dirty=true;
            resizing=manager.Editing&&e.X>=Width-20*ScaleFactor&&e.Y>=Height-20*ScaleFactor;
            moving=manager.Editing&&!resizing&&e.Y<30*ScaleFactor; if(moving||resizing) {pressed=-1;pressedPath=null;Capture=true;BeginGeometryGesture();}
        }
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);var now=PointToScreen(e.Location);
            UpdateHover();
            Cursor=manager.Editing&&e.X>Width-20*ScaleFactor&&e.Y>Height-20*ScaleFactor?Cursors.SizeNWSE:manager.Editing&&e.Y<30*ScaleFactor?Cursors.SizeAll:Cursors.Default;
            if(e.Button!=MouseButtons.Left) return;
            if(moving||resizing) {var r=initial;if(moving) r.Offset(now.X-press.X,now.Y-press.Y);else {r.Width=Math.Max(150,initial.Width+now.X-press.X);r.Height=Math.Max(130,initial.Height+now.Y-press.Y);}pendingBounds=PartitionLayout.Clamp(r,Screen.FromPoint(now).WorkingArea);geometryPending=true;return;}
            if(pressedPath!=null&&Math.Abs(now.X-press.X)+Math.Abs(now.Y-press.Y)>8&&Model.Items.Contains(pressedPath)) {
                string path=pressedPath;pressedPath=null;pressed=-1;var data=new DataObject();data.SetData(ZoneWindow.DragFormat,path);bool cancelled=false;
                QueryContinueDragEventHandler handler=delegate(object sender,QueryContinueDragEventArgs args) {if(args.EscapePressed) cancelled=true;};QueryContinueDrag+=handler;DragDropEffects result;
                sourceDragging=true;hovered=-1;selectedPath=null;dirty=true;
                try {result=DoDragDrop(data,DragDropEffects.Move);} finally {sourceDragging=false;StopHoverUntilMovement();QueryContinueDrag-=handler;}
                if(!cancelled&&result==DragDropEffects.None&&manager.IsDesktopPoint(Cursor.Position)) manager.Release(Model,path,Cursor.Position);
            }
        }
        protected override void OnMouseLeave(EventArgs e) {base.OnMouseLeave(e);if(hovered!=-1) {hovered=-1;dirty=true;}}
        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);
            if(e.Button==MouseButtons.Right) {manager.RightClick(Model,PointToScreen(e.Location),Grid.Hit(e.Location),(ModifierKeys&Keys.Control)!=0);return;}
            if(e.Button!=MouseButtons.Left) return;
            if(moving||resizing) {ApplyPendingBounds();moving=resizing=false;Capture=false;if(Bounds!=initial) manager.Place(Model,Bounds);manager.MovingFinished();}
            else if(Grid.Overflow>0&&OverflowRect.Contains(e.Location)) manager.Overflow(Model);
            pressed=-1;pressedPath=null;
        }
        protected override void OnMouseCaptureChanged(EventArgs e) {
            base.OnMouseCaptureChanged(e);
            if(!Capture&&(moving||resizing)) {moving=resizing=false;geometryPending=false;SetBounds(initial);manager.MovingFinished();}
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e) {base.OnMouseDoubleClick(e);if(e.Button!=MouseButtons.Left) return;int i=Grid.Hit(e.Location);if(i>=0) manager.Open(Model.Items[i]);else if(manager.Editing) manager.Settings(manager.Owner(Model).Monitor);}
        protected override void Dispose(bool disposing) {if(disposing) {itemCache.Dispose();if(image!=null) image.Dispose();}base.Dispose(disposing);}
    }
    internal sealed class PartitionGuide : PartitionSurface {
        readonly PartitionManager manager;readonly Screen screen;Point start;Rectangle draft;bool drawing,selectionDirty;
        internal bool Manipulating {get {return drawing;}}
        internal PartitionGuide(PartitionManager manager,Screen screen) {this.manager=manager;this.screen=screen;Text="有兜分区编辑";}
        internal void UpdateHoles() {
            if(manager.Manipulating) return;
            var local=new List<Rectangle>();foreach(var window in manager.Windows.Values) {var r=window.Bounds;r.Offset(-Left,-Top);if(r.IntersectsWith(ClientRectangle)) local.Add(r);}foreach(var hole in manager.NativeIconHoles) {var r=hole;r.Offset(-Left,-Top);if(r.IntersectsWith(ClientRectangle)) local.Add(r);}foreach(var overlay in manager.InteractiveOverlayBounds) {var r=overlay;r.Offset(-Left,-Top);if(r.IntersectsWith(ClientRectangle)) local.Add(r);}ApplyHoles(local.ToArray());
        }
        internal override void Tick() {base.Tick();if(selectionDirty&&composition!=null) {selectionDirty=false;composition.SetSelection(drawing?draft:Rectangle.Empty);}}
        protected override void PaintSurface(Graphics g) {
            using(var fill=new SolidBrush(Color.FromArgb(8,13,111,255))) g.FillRectangle(fill,ClientRectangle);
            using(var fill=new SolidBrush(Color.FromArgb(245,247,250))) using(var p=Round(new RectangleF(Math.Max(12,Width/2-260),12,520,36),10)) g.FillPath(fill,p);
            using(var brush=new SolidBrush(Color.FromArgb(35,35,38))) using(var format=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center}) g.DrawString("分区编辑 · 空白处框选新建 · 右键完成编辑",Font,brush,new RectangleF(Math.Max(12,Width/2-260),12,520,36),format);
        }
        protected override void OnMouseDown(MouseEventArgs e) {base.OnMouseDown(e);if(e.Button==MouseButtons.Left) {start=e.Location;draft=Rectangle.Empty;drawing=true;selectionDirty=true;Capture=true;}}
        protected override void OnMouseMove(MouseEventArgs e) {base.OnMouseMove(e);if(!drawing) return;var p=new Point(Math.Max(0,Math.Min(Width,e.X)),Math.Max(0,Math.Min(Height,e.Y)));draft=Rectangle.FromLTRB(Math.Min(start.X,p.X),Math.Min(start.Y,p.Y),Math.Max(start.X,p.X),Math.Max(start.Y,p.Y));selectionDirty=true;}
        protected override void OnMouseUp(MouseEventArgs e) {base.OnMouseUp(e);if(e.Button==MouseButtons.Right) {manager.Menu(null,PointToScreen(e.Location));return;}if(!drawing) return;drawing=false;Capture=false;selectionDirty=true;var rect=draft;rect.Offset(Left,Top);if(rect.Width>8&&rect.Height>8) manager.CreateRect(screen,rect);}
        protected override void OnMouseCaptureChanged(EventArgs e) {base.OnMouseCaptureChanged(e);if(!Capture&&drawing) {drawing=false;selectionDirty=true;}}
    }
}
