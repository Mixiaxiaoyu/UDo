using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using UDo.DesktopZones;

// Two small desktop surfaces. The bridge owns their state so they remain below
// application windows and continue updating when the settings window is closed.
internal sealed class DesktopWidgets : IDisposable {
    internal sealed class Record {
        internal string Kind;
        internal bool Enabled;
        internal int X=int.MinValue,Y=int.MinValue,Width;
        internal Record(string kind,int width) {Kind=kind;Width=width;}
    }
    readonly string path;
    readonly Func<IntPtr> desktopHost;
    readonly JavaScriptSerializer json=new JavaScriptSerializer();
    readonly Dictionary<string,Record> records=new Dictionary<string,Record>();
    readonly Dictionary<string,DesktopWidgetWindow> windows=new Dictionary<string,DesktopWidgetWindow>();
    IntPtr attachedHost;
    internal string Error="";
    internal int VisibleCount {get {return windows.Values.Count(w=>!w.IsDisposed&&w.Visible&&Native.IsWindowVisible(w.Handle));}}
    internal Rectangle[] InteractiveBounds {get {return windows.Values.Where(w=>!w.IsDisposed&&w.Visible).Select(w=>w.Bounds).ToArray();}}
    internal Rectangle Bounds(string kind) {DesktopWidgetWindow window;return windows.TryGetValue(kind,out window)?window.Bounds:Rectangle.Empty;}
    internal DesktopWidgets(string data,Func<IntPtr> host) {
        path=Path.Combine(data,"desktop-widgets.json");desktopHost=host;
        records["clock"]=new Record("clock",210);
        records["keys"]=new Record("keys",290);
        Load();Reconcile();
    }
    internal object Snapshot() {return records.Values.OrderBy(r=>r.Kind).Select(r=>new {kind=r.Kind,enabled=r.Enabled,x=r.X,y=r.Y,width=r.Width,height=Height(r.Kind,r.Width)}).ToArray();}
    internal static int Minimum(string kind) {return kind=="clock"?140:190;}
    internal static int Maximum(string kind) {return kind=="clock"?420:560;}
    internal static int Height(string kind,int width) {return kind=="clock"?width:(int)Math.Round(width*0.48);}
    internal static int ClampWidth(string kind,int width) {return Math.Max(Minimum(kind),Math.Min(Maximum(kind),width));}
    void Load() {
        if(!File.Exists(path)) return;
        try {
            foreach(var item in json.Deserialize<List<Dictionary<string,object>>>(File.ReadAllText(path))) {
                string kind=Convert.ToString(item["kind"]);Record record;
                if(!records.TryGetValue(kind,out record)) continue;
                record.Enabled=Convert.ToBoolean(item["enabled"]);
                record.Width=ClampWidth(kind,Convert.ToInt32(item["width"]));
                record.X=Convert.ToInt32(item["x"]);record.Y=Convert.ToInt32(item["y"]);
            }
        } catch(Exception e) {Error="桌面组件配置读取失败："+e.Message;}
    }
    void Save() {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            DockBridge.WriteAtomic(path,json.Serialize(Snapshot()));Error="";
        } catch(Exception e) {Error="桌面组件配置保存失败："+e.Message;}
    }
    Rectangle DefaultBounds(Record record) {
        Rectangle work=Screen.PrimaryScreen.WorkingArea;
        int height=Height(record.Kind,record.Width);
        int y=work.Top+30+(record.Kind=="keys"?Math.Max(230,Height("clock",records["clock"].Width)+18):0);
        return new Rectangle(work.Right-record.Width-30,Math.Min(y,work.Bottom-height-20),record.Width,height);
    }
    Rectangle VisibleBounds(Record record) {
        if(record.X==int.MinValue||record.Y==int.MinValue) return DefaultBounds(record);
        var requested=new Rectangle(record.X,record.Y,record.Width,Height(record.Kind,record.Width));
        var screen=Screen.AllScreens.FirstOrDefault(s=>Rectangle.Intersect(s.WorkingArea,requested).Width>=40&&Rectangle.Intersect(s.WorkingArea,requested).Height>=40);
        if(screen==null) return DefaultBounds(record);
        Rectangle work=screen.WorkingArea;
        return new Rectangle(Math.Max(work.Left,Math.Min(requested.X,work.Right-requested.Width)),Math.Max(work.Top,Math.Min(requested.Y,work.Bottom-requested.Height)),requested.Width,requested.Height);
    }
    internal void Command(Dictionary<string,object> command) {
        string kind=Convert.ToString(command["kind"]);Record record;
        if(!records.TryGetValue(kind,out record)) throw new ArgumentException("未知桌面组件");
        string op=Convert.ToString(command["op"]);
        if(op=="widget_show") record.Enabled=Convert.ToBoolean(command["enabled"]);
        else if(op=="widget_size") record.Width=ClampWidth(kind,Convert.ToInt32(command["width"]));
        else if(op=="widget_reset") {record.X=int.MinValue;record.Y=int.MinValue;}
        else throw new ArgumentException("未知组件操作");
        Save();Reconcile();
    }
    internal void Commit(string kind,Rectangle bounds) {
        Record record;if(!records.TryGetValue(kind,out record)) return;
        record.Width=ClampWidth(kind,bounds.Width);record.X=bounds.X;record.Y=bounds.Y;
        Rectangle visible=VisibleBounds(record);record.X=visible.X;record.Y=visible.Y;
        DesktopWidgetWindow window;if(windows.TryGetValue(kind,out window)) window.SetBounds(visible);
        Save();
    }
    void Reconcile() {
        IntPtr host=IntPtr.Zero;
        try {host=desktopHost();} catch(Exception e) {Error="桌面组件无法附着到桌面："+e.Message;}
        if(host!=IntPtr.Zero&&Error.StartsWith("桌面组件无法附着到桌面：",StringComparison.Ordinal)) Error="";
        if(host!=attachedHost) attachedHost=host;
        if(host==IntPtr.Zero) {foreach(var window in windows.Values) window.Dispose();windows.Clear();return;}
        foreach(var record in records.Values) {
            DesktopWidgetWindow window;
            if(!record.Enabled) {if(windows.TryGetValue(record.Kind,out window)) {window.Dispose();windows.Remove(record.Kind);}continue;}
            if(!windows.TryGetValue(record.Kind,out window)) {window=new DesktopWidgetWindow(record.Kind,this);windows[record.Kind]=window;}
            Rectangle bounds=VisibleBounds(record);
            if(record.X!=bounds.X||record.Y!=bounds.Y) {record.X=bounds.X;record.Y=bounds.Y;Save();}
            window.Attach(host,bounds);
        }
    }
    internal void Tick(long today,bool recording) {
        IntPtr host=IntPtr.Zero;
        try {host=desktopHost();} catch { }
        if(host!=attachedHost) Reconcile();
        foreach(var window in windows.Values) window.UpdateValue(DateTime.Now,today,recording);
    }
    public void Dispose() {foreach(var window in windows.Values) window.Dispose();windows.Clear();}
}

internal sealed class DesktopWidgetWindow : Form {
    readonly string kind;readonly DesktopWidgets owner;
    ComposedGlass composition;Bitmap pixels;IntPtr host;
    DateTime displayedSecond=DateTime.MinValue;long displayedTotal=-1;bool displayedRecording;
    bool moving,resizing;Point press;Rectangle original;
    internal DesktopWidgetWindow(string kind,DesktopWidgets owner) {
        this.kind=kind;this.owner=owner;
        Text=kind=="clock"?"有兜 · 时钟组件":"有兜 · 按键次数组件";
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
        AutoScaleMode=AutoScaleMode.None;BackColor=Color.Black;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint,true);
    }
    protected override bool ShowWithoutActivation {get {return true;}}
    protected override CreateParams CreateParams {get {var p=base.CreateParams;p.ExStyle|=0x08200080;p.ClassStyle&=~0x20000;return p;}}
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }
    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);int policy=1,border=-2;
        Native.DwmSetWindowAttribute(Handle,2,ref policy,4);Native.DwmSetWindowAttribute(Handle,34,ref border,4);
        var accent=new Native.ACCENT {state=5};IntPtr memory=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.ACCENT)));
        try {Marshal.StructureToPtr(accent,memory,false);var data=new Native.COMPOSITION {attribute=19,data=memory,size=Marshal.SizeOf(typeof(Native.ACCENT))};Native.SetWindowCompositionAttribute(Handle,ref data);composition=new ComposedGlass(Handle);}
        finally {Marshal.FreeHGlobal(memory);}
    }
    internal void Attach(IntPtr nextHost,Rectangle bounds) {
        if(IsDisposed) return;
        IntPtr handle=Handle;
        if(host!=nextHost) {host=nextHost;Native.SetWindowLongPtr(handle,-8,DesktopAdapter.GetAncestor(host,2));}
        bool changed=Bounds!=bounds;Bounds=bounds;
        if(!Visible) Show();
        Native.SetWindowPos(handle,new IntPtr(1),bounds.X,bounds.Y,bounds.Width,bounds.Height,0x10|0x40);
        if(changed||pixels==null) Render(DateTime.Now,displayedTotal<0?0:displayedTotal,displayedRecording);
    }
    internal void SetBounds(Rectangle bounds) {if(Bounds!=bounds) {Bounds=bounds;Render(DateTime.Now,displayedTotal<0?0:displayedTotal,displayedRecording);}}
    internal void UpdateValue(DateTime now,long today,bool recording) {
        bool change=kind=="clock"?now.Second!=displayedSecond.Second||now.Minute!=displayedSecond.Minute||now.Date!=displayedSecond.Date:today!=displayedTotal||recording!=displayedRecording;
        if(change) Render(now,today,recording);
    }
    static GraphicsPath Round(RectangleF rect,float radius) {
        var path=new GraphicsPath();float d=Math.Min(Math.Min(rect.Width,rect.Height),radius*2);
        path.AddArc(rect.X,rect.Y,d,d,180,90);path.AddArc(rect.Right-d,rect.Y,d,d,270,90);path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90);path.AddArc(rect.X,rect.Bottom-d,d,d,90,90);path.CloseFigure();return path;
    }
    void Render(DateTime now,long today,bool recording) {
        if(composition==null||Width<2||Height<2) return;
        displayedSecond=now;displayedTotal=today;displayedRecording=recording;
        composition.Resize(Width,Height,Math.Min(28,Width/8),"light",1);
        if(pixels==null||pixels.Width!=Width||pixels.Height!=Height) {if(pixels!=null) pixels.Dispose();pixels=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb);}
        using(var g=Graphics.FromImage(pixels)) PaintPixels(g,now,today,recording);
        composition.SetContent(pixels);
    }
    internal Bitmap Preview(DateTime now,long today,bool recording) {
        var output=new Bitmap(Math.Max(2,Width),Math.Max(2,Height),PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(output)) PaintPixels(g,now,today,recording);
        return output;
    }
    void PaintPixels(Graphics g,DateTime now,long today,bool recording) {
        g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        if(kind=="clock") DrawClock(g,now);else DrawKeys(g,today,recording);
        using(var outline=Round(new RectangleF(0.5f,0.5f,Width-1,Height-1),Math.Min(28,Width/8))) using(var pen=new Pen(Color.FromArgb(70,255,255,255),1)) g.DrawPath(pen,outline);
        using(var pen=new Pen(Color.FromArgb(85,60,65,72),Math.Max(1,Width/260f))) {
            g.DrawLine(pen,Width-14,Height-7,Width-7,Height-14);
            g.DrawLine(pen,Width-10,Height-7,Width-7,Height-10);
        }
    }
    void DrawClock(Graphics g,DateTime now) {
        float side=Math.Min(Width,Height),centerX=Width/2f,centerY=Height*0.48f,radius=side*0.38f;
        using(var outline=Round(new RectangleF(0,0,Width,Height),Math.Min(28,Width/8))) using(var fill=new SolidBrush(Color.FromArgb(205,250,250,252))) g.FillPath(fill,outline);
        for(int i=0;i<12;i++) {
            double angle=i*Math.PI/6-Math.PI/2;float x=centerX+(float)Math.Cos(angle)*radius,y=centerY+(float)Math.Sin(angle)*radius;
            float dot=i%3==0?3f:1.8f;
            using(var brush=new SolidBrush(i%3==0?Color.FromArgb(225,31,34,39):Color.FromArgb(110,100,106,113))) g.FillEllipse(brush,x-dot/2,y-dot/2,dot,dot);
        }
        double hour=(now.Hour%12+now.Minute/60.0)*Math.PI/6-Math.PI/2;
        double minute=(now.Minute+now.Second/60.0)*Math.PI/30-Math.PI/2;
        double second=now.Second*Math.PI/30-Math.PI/2;
        Hand(g,centerX,centerY,hour,radius*.48f,Math.Max(2.6f,side*.019f),Color.FromArgb(245,28,31,36));
        Hand(g,centerX,centerY,minute,radius*.76f,Math.Max(2f,side*.013f),Color.FromArgb(245,28,31,36));
        Hand(g,centerX,centerY,second,radius*.91f,Math.Max(1.3f,side*.008f),Color.FromArgb(235,13,111,255));
        using(var center=new SolidBrush(Color.FromArgb(250,26,29,34))) g.FillEllipse(center,centerX-2.5f,centerY-2.5f,5,5);
        string label=now.ToString("MM月dd日  HH:mm",CultureInfo.GetCultureInfo("zh-CN"));
        var labelBounds=new RectangleF(Width*.17f,Height*.69f,Width*.66f,Height*.10f);
        using(var background=Round(labelBounds,8)) using(var fill=new SolidBrush(Color.FromArgb(230,250,250,252))) g.FillPath(fill,background);
        using(var font=new Font("Microsoft YaHei UI",Math.Max(8,side*.055f),FontStyle.Regular,GraphicsUnit.Pixel))
        using(var ink=new SolidBrush(Color.FromArgb(220,38,41,45)))
        using(var format=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center}) g.DrawString(label,font,ink,labelBounds,format);
    }
    static void Hand(Graphics g,float x,float y,double angle,float length,float thickness,Color color) {
        using(var pen=new Pen(color,thickness) {StartCap=LineCap.Round,EndCap=LineCap.Round})
            g.DrawLine(pen,x,y,x+(float)Math.Cos(angle)*length,y+(float)Math.Sin(angle)*length);
    }
    void DrawKeys(Graphics g,long today,bool recording) {
        float radius=Math.Min(28,Height*.26f),split=Width*.75f;
        using(var outline=Round(new RectangleF(0,0,Width,Height),radius)) {
            using(var fill=new SolidBrush(Color.FromArgb(225,238,240,244))) g.FillPath(fill,outline);
            var save=g.Save();g.SetClip(outline);
            using(var blue=new SolidBrush(Color.FromArgb(235,13,111,255))) g.FillRectangle(blue,0,0,split,Height);
            g.Restore(save);
        }
        string value=today.ToString(CultureInfo.InvariantCulture);
        float fontSize=Height*.57f;
        Font number=null;
        do {if(number!=null) number.Dispose();number=new Font("Segoe UI",fontSize,FontStyle.Regular,GraphicsUnit.Pixel);if(g.MeasureString(value,number).Width<=Width-24||fontSize<=20) break;fontSize-=2;} while(true);
        using(number) using(var brush=new SolidBrush(Color.FromArgb(245,18,25,35))) using(var format=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})
            g.DrawString(value,number,brush,new RectangleF(7,Height*.16f,Width-14,Height*.68f),format);
        using(var font=new Font("Microsoft YaHei UI",Math.Max(10,Height*.105f),FontStyle.Regular,GraphicsUnit.Pixel))
        using(var ink=new SolidBrush(Color.FromArgb(210,20,27,36)))
            g.DrawString(recording?"今日按键":"统计已暂停",font,ink,new PointF(18,Math.Max(7,Height*.075f)));
    }
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);if(e.Button!=MouseButtons.Left) return;
        resizing=e.X>=Width-28&&e.Y>=Height-28;moving=!resizing;press=Cursor.Position;original=Bounds;Capture=true;
    }
    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        if(moving||resizing) {
            int dx=Cursor.Position.X-press.X,dy=Cursor.Position.Y-press.Y;
            if(moving) Bounds=new Rectangle(original.X+dx,original.Y+dy,original.Width,original.Height);
            else {int width=DesktopWidgets.ClampWidth(kind,original.Width+(kind=="clock"?Math.Max(dx,dy):Math.Max(dx,(int)(dy/.48))));SetBounds(new Rectangle(original.X,original.Y,width,DesktopWidgets.Height(kind,width)));}
        } else Cursor=e.X>=Width-28&&e.Y>=Height-28?Cursors.SizeNWSE:Cursors.SizeAll;
    }
    protected override void OnMouseUp(MouseEventArgs e) {
        base.OnMouseUp(e);if(e.Button!=MouseButtons.Left) return;
        if(moving||resizing) {moving=resizing=false;Capture=false;owner.Commit(kind,Bounds);}
    }
    protected override void Dispose(bool disposing) {if(disposing) {if(pixels!=null) pixels.Dispose();if(composition!=null) composition.Dispose();}base.Dispose(disposing);}
}
