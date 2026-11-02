using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Live DWM thumbnails, one card per top-level application window.
internal sealed class WindowPreviews : Form {
    [StructLayout(LayoutKind.Sequential)] struct Props {
        public uint Flags;public Native.RECT Destination;public Native.RECT Source;
        public byte Opacity;[MarshalAs(UnmanagedType.Bool)] public bool Visible;
        [MarshalAs(UnmanagedType.Bool)] public bool SourceClientOnly;
    }
    [DllImport("dwmapi.dll")] static extern int DwmRegisterThumbnail(IntPtr destination,IntPtr source,out IntPtr thumbnail);
    [DllImport("dwmapi.dll")] static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
    [DllImport("dwmapi.dll")] static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail,ref Props properties);
    internal sealed class Item {internal IntPtr Handle;internal uint Pid;internal string Title;internal Rectangle Card;internal IntPtr Thumbnail;}
    readonly List<Item> items=new List<Item>();readonly Action<IntPtr> activate;
    DateTime hideAfter=DateTime.MaxValue;
    const int CardWidth=192,CardHeight=144,ImageHeight=108,Gap=8,Inset=10,OuterRadius=14,CardRadius=9;
    internal WindowPreviews(Action<IntPtr> action) {
        activate=action;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;
        StartPosition=FormStartPosition.Manual;BackColor=Color.FromArgb(246,247,250);
        Font=new Font("Microsoft YaHei UI",9);DoubleBuffered=true;
    }
    protected override bool ShowWithoutActivation {get {return true;}}
    protected override CreateParams CreateParams {get {var p=base.CreateParams;p.ExStyle|=0x08000080;p.ClassStyle&=~0x20000;return p;}}
    static GraphicsPath Rounded(Rectangle bounds,int radius) {
        var path=new GraphicsPath();int diameter=radius*2;
        path.AddArc(bounds.Left,bounds.Top,diameter,diameter,180,90);
        path.AddArc(bounds.Right-diameter,bounds.Top,diameter,diameter,270,90);
        path.AddArc(bounds.Right-diameter,bounds.Bottom-diameter,diameter,diameter,0,90);
        path.AddArc(bounds.Left,bounds.Bottom-diameter,diameter,diameter,90,90);
        path.CloseFigure();return path;
    }
    void UpdateRoundedRegion() {
        if(Width<OuterRadius*2||Height<OuterRadius*2) return;
        using(var outline=Rounded(new Rectangle(0,0,Width,Height),OuterRadius)) {
            var previous=Region;Region=new Region(outline);if(previous!=null) previous.Dispose();
        }
    }
    protected override void OnSizeChanged(EventArgs e) {base.OnSizeChanged(e);UpdateRoundedRegion();}
    internal void ShowFor(IList<Item> requested,Rectangle icon) {
        Clear();hideAfter=DateTime.MaxValue;
        foreach(var item in requested) if(Native.IsWindow(item.Handle)&&items.Count<12) items.Add(item);
        if(items.Count==0) {Hide();return;}
        int columns=Math.Min(items.Count,4),rows=(items.Count+columns-1)/columns;
        Size=new Size(Inset*2+columns*CardWidth+(columns-1)*Gap,Inset*2+rows*CardHeight+(rows-1)*Gap);
        var screen=Screen.FromRectangle(icon).WorkingArea;
        Left=Math.Max(screen.Left,Math.Min(screen.Right-Width,icon.Left+icon.Width/2-Width/2));
        Top=icon.Top-Height-10;
        if(Top<screen.Top) Top=Math.Min(screen.Bottom-Height,icon.Bottom+10);
        for(int i=0;i<items.Count;i++) items[i].Card=new Rectangle(Inset+(i%columns)*(CardWidth+Gap),Inset+(i/columns)*(CardHeight+Gap),CardWidth,CardHeight);
        Show();Native.SetWindowPos(Handle,new IntPtr(-1),Left,Top,Width,Height,0x0010|0x0040);
        foreach(var item in items) {
            if(DwmRegisterThumbnail(Handle,item.Handle,out item.Thumbnail)!=0) continue;
            var image=Rectangle.Inflate(item.Card,-5,-5);image.Height=ImageHeight;
            var p=new Props {Flags=0x1|0x4|0x8,Destination=new Native.RECT {Left=image.Left,Top=image.Top,Right=image.Right,Bottom=image.Bottom},Opacity=255,Visible=true};
            DwmUpdateThumbnailProperties(item.Thumbnail,ref p);
        }
        Invalidate();
    }
    internal void RequestHide() {hideAfter=DateTime.UtcNow.AddMilliseconds(260);}
    internal void Tick() {
        if(!Visible) return;
        Point cursor=Cursor.Position;
        if(Bounds.Contains(cursor)) {hideAfter=DateTime.UtcNow.AddMilliseconds(260);return;}
        if(DateTime.UtcNow>=hideAfter) {Clear();Hide();}
    }
    void Clear() {foreach(var item in items) if(item.Thumbnail!=IntPtr.Zero) {DwmUnregisterThumbnail(item.Thumbnail);item.Thumbnail=IntPtr.Zero;}items.Clear();}
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var border=new Pen(Color.FromArgb(215,219,226))) using(var card=new SolidBrush(Color.White))
        foreach(var item in items) {
            using(var shape=Rounded(Rectangle.Inflate(item.Card,-1,-1),CardRadius)) {
                e.Graphics.FillPath(card,shape);e.Graphics.DrawPath(border,shape);
            }
            var title=new Rectangle(item.Card.Left+7,item.Card.Top+ImageHeight+7,item.Card.Width-14,CardHeight-ImageHeight-10);
            TextRenderer.DrawText(e.Graphics,item.Title,Font,title,Color.FromArgb(37,39,43),TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
        }
    }
    protected override void OnMouseClick(MouseEventArgs e) {
        base.OnMouseClick(e);if(e.Button!=MouseButtons.Left) return;
        foreach(var item in items) if(item.Card.Contains(e.Location)) {var target=item.Handle;Clear();Hide();activate(target);return;}
    }
    protected override void Dispose(bool disposing) {if(disposing) {Clear();Font.Dispose();}base.Dispose(disposing);}
}
