using System;
using System.Collections.Generic;
using UDo.DesktopPartitions;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// No injection, elevation, shell replacement or persistent Explorer configuration.
internal static class Native {
    internal delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc cb, IntPtr data);
    [DllImport("user32.dll")] internal static extern bool EnumChildWindows(IntPtr parent,EnumProc cb,IntPtr data);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(IntPtr h, int cmd);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] internal static extern bool AttachThreadInput(uint source, uint target, bool attach);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern uint PrivateExtractIcons(string path, int index, int cx, int cy, IntPtr[] icons, uint[] ids, uint count, uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    internal delegate void WinEventProc(IntPtr hook,uint eventId,IntPtr hwnd,int objectId,int childId,uint thread,uint time);
    [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,WinEventProc callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr h, uint command);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern int GetWindowText(IntPtr h, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern int GetClassName(IntPtr h, StringBuilder text, int max);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] internal static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr h, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr SendMessageTimeout(IntPtr h,uint message,IntPtr w,IntPtr l,uint flags,uint timeout,out IntPtr result);
    [DllImport("user32.dll", EntryPoint="GetClassLongPtrW")] internal static extern IntPtr GetClassLongPtr(IntPtr h,int index);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr h, out RECT rect);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
    [DllImport("kernel32.dll")] internal static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] internal static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder text, ref int length);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] internal static extern bool SetWindowCompositionAttribute(IntPtr h, ref COMPOSITION data);
    [DllImport("gdi32.dll")] internal static extern IntPtr CreateRoundRectRgn(int l,int t,int r,int b,int ew,int eh);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] internal static extern int SetWindowRgn(IntPtr h,IntPtr region,bool redraw);
    [DllImport("user32.dll")] internal static extern int GetWindowRgn(IntPtr h,IntPtr region);
    [DllImport("gdi32.dll")] internal static extern bool PtInRegion(IntPtr region,int x,int y);
    [DllImport("shell32.dll")] internal static extern int SHGetStockIconInfo(uint id,uint flags,ref STOCKICON info);
    [StructLayout(LayoutKind.Sequential)] internal struct ACCENT { public int state,flags; public uint color; public int animation; }
    [StructLayout(LayoutKind.Sequential)] internal struct COMPOSITION { public int attribute; public IntPtr data; public int size; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct STOCKICON {
        public uint size; public IntPtr icon; public int index,id;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string path;
    }
    [DllImport("shell32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr SHGetFileInfo(string path, uint attrs, out SHFILEINFO info, uint size, uint flags);
    [DllImport("shell32.dll", CharSet=CharSet.Unicode)] internal static extern uint ExtractIconEx(string path, int index, IntPtr[] large, IntPtr[] small, uint count);
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] internal struct SHFILEINFO {
        public IntPtr hIcon; public int iIcon; public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string display;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=80)] public string type;
    }
    internal static string Class(IntPtr h) { var s=new StringBuilder(256); GetClassName(h,s,s.Capacity); return s.ToString(); }
    internal static string Title(IntPtr h) { var s=new StringBuilder(2048); GetWindowText(h,s,s.Capacity); return s.ToString(); }
    internal static string Exe(uint pid) {
        IntPtr h=OpenProcess(0x1000,false,pid); if(h==IntPtr.Zero) return "";
        try { int n=32768; var s=new StringBuilder(n); return QueryFullProcessImageName(h,0,s,ref n)?s.ToString():""; }
        finally { CloseHandle(h); }
    }
    internal static List<IntPtr> Taskbars() {
        var bars=new List<IntPtr>(); EnumWindows(delegate(IntPtr h,IntPtr p) {
            string c=Class(h); if(c=="Shell_TrayWnd" || c=="Shell_SecondaryTrayWnd") bars.Add(h); return true;
        },IntPtr.Zero); return bars;
    }
}

// A separate native material surface stays immediately BELOW the Godot window.
// The material occupies only the rounded shelf, never the transparent hover space.
internal sealed class GlassShelf : Form {
    internal bool BlurEnabled;
    Rectangle previous;
    int previousRadius;
    ComposedGlass composition;
    internal string MaterialError="";
    internal bool RoundedClipActive {
        get {
            if(composition!=null) return true;
            if(!IsHandleCreated) return false;
            IntPtr region=Native.CreateRoundRectRgn(0,0,1,1,0,0);
            try {return Native.GetWindowRgn(Handle,region)>0 && !Native.PtInRegion(region,1,1) && Native.PtInRegion(region,Width/2,Height/2);}
            finally {Native.DeleteObject(region);}
        }
    }
    internal GlassShelf() {
        FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; BackColor=Color.Black;
        StartPosition=FormStartPosition.Manual; Text="UDo material";
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint,true);
    }
    protected override bool ShowWithoutActivation {get {return true;}}
    protected override CreateParams CreateParams {get {var p=base.CreateParams; p.ExStyle|=0x08200080; p.ClassStyle&=~0x00020000; return p;}}
    protected override void OnPaintBackground(PaintEventArgs e) {}
    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        // Our composition visual draws the outline; disable the OS window frame.
        int ncPolicy=1;
        Native.DwmSetWindowAttribute(Handle,2,ref ncPolicy,4);
        int border=-2;
        Native.DwmSetWindowAttribute(Handle,34,ref border,4);
        // Enable host sampling without painting an accent rectangle. The
        // backdrop visual is clipped within Windows' compositor, not by HRGN.
        var accent=new Native.ACCENT {state=5,flags=0,color=0};
        IntPtr memory=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.ACCENT)));
        try {
            Marshal.StructureToPtr(accent,memory,false);
            var data=new Native.COMPOSITION {attribute=19,data=memory,size=Marshal.SizeOf(typeof(Native.ACCENT))};
            Native.SetWindowCompositionAttribute(Handle,ref data);
            composition=new ComposedGlass(Handle);
            BlurEnabled=true;
        } catch(Exception error) {BlurEnabled=false; MaterialError=error.ToString();}
        finally {Marshal.FreeHGlobal(memory);}
    }
    internal void Place(IntPtr above,Rectangle bounds,int radius,bool show,string theme="light",float scale=1) {
        if(!show || bounds.Width<2 || bounds.Height<2) {if(IsHandleCreated) Native.ShowWindow(Handle,0); return;}
        if(!IsHandleCreated) CreateControl();
        if(composition!=null) composition.Resize(bounds.Width,bounds.Height,radius,theme,scale);
        // Godot owns input. No activation, no placement above the actual icons.
        Native.SetWindowPos(Handle,above,bounds.X,bounds.Y,bounds.Width,bounds.Height,0x0010|0x0040);
        if(composition==null && (bounds.Size!=previous.Size || radius!=previousRadius)) {
            IntPtr nativeRegion=Native.CreateRoundRectRgn(0,0,bounds.Width,bounds.Height,radius*2,radius*2);
            // SetWindowRgn owns the HRGN after success; no second blur API resets it.
            if(Native.SetWindowRgn(Handle,nativeRegion,true)==0) Native.DeleteObject(nativeRegion);
        }
        previous=bounds; previousRadius=radius;
    }
    protected override void Dispose(bool disposing) {if(disposing && composition!=null){composition.Dispose();composition=null;}base.Dispose(disposing);}
}

internal sealed class DockBridge : ApplicationContext {
    readonly JavaScriptSerializer json=new JavaScriptSerializer();
    readonly string root;
    readonly Process parent;
    readonly Dictionary<long,uint> hidden=new Dictionary<long,uint>();
    readonly Dictionary<string,string> icons=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<long,string> windowIcons=new Dictionary<long,string>();
    readonly Dictionary<string,string> resolved=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    readonly Timer timer=new Timer();
    readonly NotifyIcon tray=new NotifyIcon();
    readonly Icon applicationIcon=Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();
    readonly HotkeyWindow hotkey;
    bool hide, exiting;
    int tick, actionId;
    string action="", error="";
    List<object> windows=new List<object>();
    IntPtr dock, lastApp;
    IntPtr watchedMenu;bool menuMouseDown;
    readonly Native.WinEventProc foregroundChanged;
    readonly IntPtr foregroundHook;
    readonly GlassShelf glass=new GlassShelf();
    readonly WindowPreviews previews;
    readonly KeyStatistics statistics;
    readonly KeyVisualizer keys;
    string statisticsDate="";
    readonly TrayApps trayApps=new TrayApps();
    UDo.DesktopZones.ZoneManager zones;
    UDo.DesktopPartitions.PartitionManager partitions;
    readonly DesktopWidgets widgets;
    string partitionsError="";
    string zonesError="";
    string widgetsError="";
    DateTime nextTrayRefresh=DateTime.UtcNow.AddSeconds(2);
    Dictionary<string,object> layout;
    DateTime layoutStamp;

    internal DockBridge(int pid,string directory) {
        root=directory; Directory.CreateDirectory(root); Directory.CreateDirectory(Path.Combine(root,"icons"));
        parent=Process.GetProcessById(pid);
        previews=new WindowPreviews(ActivateWindow);
        statistics=new KeyStatistics(Path.GetFullPath(Path.Combine(Application.StartupPath,"..","..","data","key-statistics")));
        keys=new KeyVisualizer(statistics);
        trayApps.Refresh();
        // Remember focus before the Dock receives the mouse press, independently
        // of the slower window-list refresh sent to Godot.
        foregroundChanged=delegate(IntPtr hook,uint eventId,IntPtr hwnd,int objectId,int childId,uint thread,uint time) { RememberForeground(hwnd); };
        foregroundHook=Native.SetWinEventHook(3,3,IntPtr.Zero,foregroundChanged,0,0,0);
        RememberForeground(Native.GetForegroundWindow());
        hotkey=new HotkeyWindow(delegate { SetHidden(false); SendAction("restore"); });
        tray.Text="有兜 · UDo · Ctrl+Alt+F12 恢复任务栏"; tray.Icon=applicationIcon;
        tray.Visible=true; tray.DoubleClick+=delegate { SendAction("settings"); };
        tray.MouseUp+=delegate(object sender,MouseEventArgs e) {if(e.Button==MouseButtons.Right) SendAction("box:"+json.Serialize(new {kind="tray",x=Cursor.Position.X,y=Cursor.Position.Y}));};
        var guard=new ProcessStartInfo(Application.ExecutablePath,"--guard "+Process.GetCurrentProcess().Id+" \""+root+"\"");
        guard.UseShellExecute=false; guard.CreateNoWindow=true; Process.Start(guard);
        try {zones=new UDo.DesktopZones.ZoneManager(Path.GetFullPath(Path.Combine(Application.StartupPath,"..","..","data")),root,SendAction);}
        catch(Exception e) {zonesError=e.Message;}
        try {if(zones!=null) partitions=new UDo.DesktopPartitions.PartitionManager(Path.GetFullPath(Path.Combine(Application.StartupPath,"..","..","data")),zones,SendAction,null,null,delegate {
            return zones.InteractiveBounds.Concat(widgets==null?new Rectangle[0]:widgets.InteractiveBounds).ToArray();
        });}
        catch(Exception e) {partitionsError=e.Message;}
        try {widgets=new DesktopWidgets(Path.GetFullPath(Path.Combine(Application.StartupPath,"..","..","data")),delegate {
            return zones!=null?zones.DesktopHost:new UDo.DesktopZones.DesktopAdapter(Path.Combine(root,"widget-shell-flags.txt")).Host;
        });} catch(Exception e) {widgetsError=e.Message;}
        timer.Interval=16; timer.Tick+=delegate { OnTick(); }; timer.Start();
    }
    void SendAction(string value) {
        action=value;actionId++;
        // UI requests bypass the slower, full telemetry snapshot.
        try {WriteAtomic(Path.Combine(root,"ui-event.json"),json.Serialize(new {action=action,action_id=actionId}));} catch(IOException) { }
    }
    static string S(Dictionary<string,object> d,string key) { object v; return d.TryGetValue(key,out v)?Convert.ToString(v):""; }
    bool windowRefreshPending, initialScanComplete;
    void OnTick() {
        long performanceStart=PerformanceProbe.Start();
        try {
            if(parent.HasExited) { ExitThread(); return; }
            RememberForeground(Native.GetForegroundWindow());
            statistics.Tick();
            if(zones!=null) zones.Tick();
            if(partitions!=null) partitions.Tick();
            if(widgets!=null&&tick%12==0) try {widgets.Tick(statistics.TotalToday(),statistics.Enabled);widgetsError="";} catch(Exception e) {widgetsError=e.Message;}
            foreach(string file in Directory.GetFiles(root,"cmd-*.json").OrderBy(x=>x)) {
                try {
                    var c=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(file));
                    // A native file picker runs a nested message loop; consume first to avoid re-entry.
                    File.Delete(file); Execute(c);
                }
                catch(Exception e) { error=e.Message; }
                finally { try { File.Delete(file); } catch {} }
            }
            WatchMenuClick();
            bool pointerBusy=(partitions!=null&&partitions.Manipulating)||(zones!=null&&zones.Manipulating);
            if(++tick%30==0) windowRefreshPending=true;
            if(windowRefreshPending&&!pointerBusy) {
                windowRefreshPending=false;
                windows=Enumerate();
                initialScanComplete=true;
                if(hide) HideTaskbars();
                if(dock!=IntPtr.Zero) {
                    long style=Native.GetWindowLongPtr(dock,-20).ToInt64();
                    Native.SetWindowLongPtr(dock,-20,new IntPtr((style|0x80)&~0x40000L));
                    // Preserve Godot's position/region; do not steal focus on timer ticks.
                }
            }
            if(!pointerBusy&&DateTime.UtcNow>=nextTrayRefresh) {nextTrayRefresh=DateTime.UtcNow.AddSeconds(2); trayApps.Refresh();}
            UpdateGlass();
            previews.Tick();
            if(tick%12!=0) return;
            Point cursor; Native.GetCursorPos(out cursor);
            var screens=Screen.AllScreens.Select(s=>(object)new {name=s.DeviceName,x=s.Bounds.X,y=s.Bounds.Y,w=s.Bounds.Width,h=s.Bounds.Height,work_x=s.WorkingArea.X,work_y=s.WorkingArea.Y,work_w=s.WorkingArea.Width,work_h=s.WorkingArea.Height,primary=s.Primary}).ToArray();
            Native.RECT r; Native.GetWindowRect(dock,out r);
            var trayItems=new List<object>();

            foreach(var item in trayApps.Snapshot) {
                uint ownerPid; Native.GetWindowThreadProcessId(item.Owner,out ownerPid);
                if(ownerPid!=item.Pid || ownerPid==parent.Id || ownerPid==Process.GetCurrentProcess().Id || !Native.IsWindow(item.Owner)) continue;
                string trayIcon; if(!icons.TryGetValue(item.Path,out trayIcon)) trayIcon=pointerBusy?"":IconFor(item.Path);
                trayItems.Add(new {id=item.Key,pid=item.Pid,path=item.Path,title=item.Title,icon=trayIcon});
            }
            WriteAtomic(Path.Combine(root,"state.json"),json.Serialize(new {pid=Process.GetCurrentProcess().Id,hidden=hide,initial_scan_complete=initialScanComplete,windows=windows,tray_apps=trayItems,tray_error=trayApps.LastError,partitions_count=partitions==null?0:partitions.All.Count(),partitions_error=partitions==null?partitionsError:partitions.Error,zones_count=zones==null?0:zones.Count,zones_error=zones==null?zonesError:zones.Error,widgets=widgets==null?null:widgets.Snapshot(),widgets_error=widgets==null||!String.IsNullOrEmpty(widgetsError)?widgetsError:widgets.Error,icons=icons,resolved=resolved,screens=screens,cursor_x=cursor.X,cursor_y=cursor.Y,action=action,action_id=actionId,error=error,hotkey=hotkey.Registered,heartbeat=DateTime.UtcNow.Ticks,dock_rect=new {x=r.Left,y=r.Top,w=r.Right-r.Left,h=r.Bottom-r.Top},key_visualizer=keys.Enabled,key_statistics=statistics.Snapshot(statistics.Today),key_statistics_view=statistics.Snapshot(String.IsNullOrEmpty(statisticsDate)?statistics.Today:statisticsDate),material_error=glass.MaterialError,material_clip=glass.RoundedClipActive,material=glass.BlurEnabled?"blur":"translucent",material_visible=glass.IsHandleCreated && Native.IsWindowVisible(glass.Handle)}));
        } catch(IOException) { /* A reader may hold state.json briefly; retry on the next tick. */ }
        catch(Exception e) { error=e.Message; if(parent.HasExited) ExitThread(); }
        finally {PerformanceProbe.End("bridge_tick_ms",performanceStart);}
    }
    void WatchMenuClick() {
        bool down=(Native.GetAsyncKeyState(1)&0x8000)!=0||(Native.GetAsyncKeyState(2)&0x8000)!=0;
        if(watchedMenu!=IntPtr.Zero&&down&&!menuMouseDown) {
            Native.RECT bounds;Point cursor;
            if(!Native.IsWindow(watchedMenu)) watchedMenu=IntPtr.Zero;
            else if(Native.GetWindowRect(watchedMenu,out bounds)&&Native.GetCursorPos(out cursor)&&
                (cursor.X<bounds.Left||cursor.X>=bounds.Right||cursor.Y<bounds.Top||cursor.Y>=bounds.Bottom)) {
                watchedMenu=IntPtr.Zero;SendAction("menu_dismiss");
            }
        }
        menuMouseDown=down;
    }
    void UpdateGlass() {
        if(dock==IntPtr.Zero || !Native.IsWindow(dock)) return;
        string file=Path.Combine(root,"layout.json");
        if(File.Exists(file)) {
            var stamp=File.GetLastWriteTimeUtc(file);
            if(stamp!=layoutStamp) {
                layout=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(file)); layoutStamp=stamp;
            }
        }
        if(layout==null) return;
        Native.RECT rect; Native.GetWindowRect(dock,out rect);
        int x=Convert.ToInt32(layout["x"]), y=Convert.ToInt32(layout["y"]);
        var bounds=new Rectangle(rect.Left+x,rect.Top+y,Convert.ToInt32(layout["w"]),Convert.ToInt32(layout["h"]));
        bounds=Rectangle.Intersect(bounds,new Rectangle(rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top));
        glass.Place(dock,bounds,Convert.ToInt32(layout["radius"]),Convert.ToBoolean(layout["visible"]) && Native.IsWindowVisible(dock),layout.ContainsKey("theme")?Convert.ToString(layout["theme"]):"light",layout.ContainsKey("scale")?Convert.ToSingle(layout["scale"]):1);
    }
    void Execute(Dictionary<string,object> c) {
        string op=S(c,"op"); error="";
        if(op=="zone_create"||op=="box_create") {
            if(zones==null) throw new InvalidOperationException("收纳盒暂不可用："+zonesError);
            zones.Create(c.ContainsKey("x")?new Point(Convert.ToInt32(c["x"]),Convert.ToInt32(c["y"])):Cursor.Position);
        }
        else if(op.StartsWith("widget_")) {if(widgets!=null) widgets.Command(c);else throw new InvalidOperationException("桌面组件暂不可用："+widgetsError);}
        else if(op.StartsWith("box_")) {if(zones!=null) zones.Command(c);}
        else if(op.StartsWith("partition_")) {if(partitions!=null) partitions.Command(c);else SendAction("partition:"+json.Serialize(new {kind="notice",message="无感分区暂不可用："+partitionsError}));}
        else if(op=="pick_app") {
            using(var picker=new OpenFileDialog {
                Title="选择要添加到 Dock 的应用或快捷方式",
                InitialDirectory=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Filter="应用程序和快捷方式|*.exe;*.lnk;*.url;*.bat;*.cmd;*.appref-ms|所有文件|*.*",
                FilterIndex=1,CheckFileExists=true,CheckPathExists=true,
                DereferenceLinks=false,Multiselect=false,RestoreDirectory=true
            }) {
                if(picker.ShowDialog()==DialogResult.OK) SendAction("pin:"+picker.FileName);
            }
        }
        else if(op=="hide") SetHidden(Convert.ToBoolean(c["value"]));
        else if(op=="key_statistics") keys.ConfigureStatistics(Convert.ToBoolean(c["enabled"]));
        else if(op=="key_statistics_date") statisticsDate=String.IsNullOrEmpty(S(c,"date"))?"":KeyStatistics.ValidateDate(S(c,"date"));
        else if(op=="key_visualizer") keys.Configure(Convert.ToBoolean(c["enabled"]),c.ContainsKey("port")?Convert.ToInt32(c["port"]):0,S(c,"token"));
        else if(op=="key_overlay") {
            IntPtr h=new IntPtr(Convert.ToInt64(c["hwnd"])); uint pid; Native.GetWindowThreadProcessId(h,out pid);
            if(pid!=parent.Id || !Native.IsWindow(h)) return;
            long style=Native.GetWindowLongPtr(h,-20).ToInt64();
            // Layered + transparent + noactivate + toolwindow: pass clicks to other apps.
            Native.SetWindowLongPtr(h,-20,new IntPtr((style|0x080800A0L)&~0x40000L));
        }
        else if(op=="register") {
            IntPtr h=new IntPtr(Convert.ToInt64(c["hwnd"])); uint pid; Native.GetWindowThreadProcessId(h,out pid);
            if(pid==parent.Id) dock=h;
        }
        else if(op=="preview") {
            if(!c.ContainsKey("windows")) {previews.RequestHide();return;}
            Native.RECT dockBounds;if(dock==IntPtr.Zero||!Native.GetWindowRect(dock,out dockBounds)) return;
            var selected=new List<WindowPreviews.Item>();
            foreach(var entry in ((System.Collections.IEnumerable)c["windows"]).Cast<Dictionary<string,object>>()) {
                IntPtr h=new IntPtr(Convert.ToInt64(entry["hwnd"]));uint pid;
                Native.GetWindowThreadProcessId(h,out pid);
                if(Native.IsWindow(h)&&pid==Convert.ToUInt32(entry["pid"])) selected.Add(new WindowPreviews.Item {Handle=h,Pid=pid,Title=S(entry,"title")});
            }
            double scaleX=(dockBounds.Right-dockBounds.Left)/Math.Max(1.0,Convert.ToDouble(c["dock_w"]));
            double scaleY=(dockBounds.Bottom-dockBounds.Top)/Math.Max(1.0,Convert.ToDouble(c["dock_h"]));
            var icon=new Rectangle(dockBounds.Left+(int)Math.Round(Convert.ToDouble(c["x"])*scaleX),dockBounds.Top+(int)Math.Round(Convert.ToDouble(c["y"])*scaleY),
                Math.Max(1,(int)Math.Round(Convert.ToDouble(c["w"])*scaleX)),Math.Max(1,(int)Math.Round(Convert.ToDouble(c["h"])*scaleY)));
            previews.ShowFor(selected,icon);
        }
        else if(op=="watch_menu") {
            IntPtr h=new IntPtr(Convert.ToInt64(c["hwnd"]));uint pid;
            if(h!=IntPtr.Zero) {Native.GetWindowThreadProcessId(h,out pid);if(pid!=parent.Id||!Native.IsWindow(h)) return;}
            watchedMenu=h;
            menuMouseDown=(Native.GetAsyncKeyState(1)&0x8000)!=0||(Native.GetAsyncKeyState(2)&0x8000)!=0;
        }
        else if(op=="snapshot") {
            Native.RECT r;
            if(dock!=IntPtr.Zero && Native.GetWindowRect(dock,out r)) {
                var capture=new Rectangle(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top);
                if(c.ContainsKey("crop_x")) capture=Rectangle.Intersect(capture,new Rectangle(r.Left+Convert.ToInt32(c["crop_x"]),r.Top+Convert.ToInt32(c["crop_y"]),Convert.ToInt32(c["crop_w"]),Convert.ToInt32(c["crop_h"])));
                using(var bitmap=new Bitmap(capture.Width,capture.Height)) {
                    using(var graphics=Graphics.FromImage(bitmap)) graphics.CopyFromScreen(capture.Location,Point.Empty,bitmap.Size);
                    bitmap.Save(Path.Combine(root,Path.GetFileName(S(c,"name"))+".png"),ImageFormat.Png);
                }
            }
        }
        else if(op=="quit") ExitThread();
        else if(op=="tray_activate") {
            if(!trayApps.Activate(S(c,"id"),Convert.ToUInt32(c["pid"]))) {
                trayApps.Refresh();
                throw new Exception("托盘图标已变化，请稍后再点一次");
            }
        }
        else if(op=="icon") { string path=S(c,"path"); IconFor(path); resolved[path]=Resolve(path); }
        else if(op=="launch") {
            string path=S(c,"path");
            if(String.IsNullOrWhiteSpace(path)) throw new Exception("没有可启动的应用路径");
            Process.Start(new ProcessStartInfo(ExpandPath(path)) {UseShellExecute=true});
        }
        else if(op=="toggle") {
            var targets=new List<IntPtr>();
            foreach(var entry in ((System.Collections.IEnumerable)c["windows"]).Cast<Dictionary<string,object>>()) {
                IntPtr h=new IntPtr(Convert.ToInt64(entry["hwnd"]));
                uint pid; Native.GetWindowThreadProcessId(h,out pid);
                if(Native.IsWindow(h) && pid==Convert.ToUInt32(entry["pid"]) && !targets.Contains(h)) targets.Add(h);
            }
            if(targets.Count==0) return;
            IntPtr active=ActiveApplication();
            if(targets.Contains(active) && !Native.IsIconic(active)) Native.ShowWindowAsync(active,6);
            else ActivateWindow(targets[0]);
            windows=Enumerate();
        }
        else if(op=="activate" || op=="close") {
            IntPtr h=new IntPtr(Convert.ToInt64(c["hwnd"]));
            uint pid; Native.GetWindowThreadProcessId(h,out pid);
            if(!Native.IsWindow(h) || pid!=Convert.ToUInt32(c["pid"])) return;
            if(op=="close") Native.PostMessage(h,0x10,IntPtr.Zero,IntPtr.Zero);
            else ActivateWindow(h);
        }
    }
    void RememberForeground(IntPtr h) {
        if(h==IntPtr.Zero) return;
        uint pid; Native.GetWindowThreadProcessId(h,out pid);
        if(pid!=parent.Id && pid!=Process.GetCurrentProcess().Id) lastApp=h;
    }
    IntPtr ActiveApplication() {
        IntPtr h=Native.GetForegroundWindow();
        uint pid; Native.GetWindowThreadProcessId(h,out pid);
        return pid==parent.Id || pid==Process.GetCurrentProcess().Id ? lastApp : h;
    }
    static void ActivateWindow(IntPtr h) {
        if(Native.IsIconic(h)) Native.ShowWindowAsync(h,9);
        uint ignored;
        uint foregroundThread=Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out ignored);
        uint currentThread=Native.GetCurrentThreadId();
        bool attached=foregroundThread!=currentThread && Native.AttachThreadInput(currentThread,foregroundThread,true);
        try { Native.SetForegroundWindow(h); }
        finally { if(attached) Native.AttachThreadInput(currentThread,foregroundThread,false); }
    }
    string Resolve(string path) {
        path=ExpandPath(path);
        if(!path.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase)) return path;
        object shell=null, link=null;
        try {
            Type t=Type.GetTypeFromProgID("WScript.Shell"); shell=Activator.CreateInstance(t);
            link=t.InvokeMember("CreateShortcut",System.Reflection.BindingFlags.InvokeMethod,null,shell,new object[]{path});
            return (string)link.GetType().InvokeMember("TargetPath",System.Reflection.BindingFlags.GetProperty,null,link,null);
        } catch { return path; }
        finally { if(link!=null) Marshal.FinalReleaseComObject(link); if(shell!=null) Marshal.FinalReleaseComObject(shell); }
    }
    List<object> Enumerate() {
        var list=new List<object>(); IntPtr active=ActiveApplication();var seen=new HashSet<long>();
        Native.EnumWindows(delegate(IntPtr h,IntPtr data) {
            uint pid; Native.GetWindowThreadProcessId(h,out pid);
            if(pid==parent.Id) { if(Native.Title(h).StartsWith("有兜 · UDo")) dock=h; return true; }
            if(pid==Process.GetCurrentProcess().Id || !Native.IsWindowVisible(h)) return true;
            long style=Native.GetWindowLongPtr(h,-20).ToInt64();
            if((style&0x80)!=0 || (Native.GetWindow(h,4)!=IntPtr.Zero && (style&0x40000)==0)) return true;
            string title=Native.Title(h), cls=Native.Class(h);
            if(title.Length==0 || cls=="Progman" || cls=="WorkerW" || cls.StartsWith("Shell_")) return true;
            int cloaked; if(Native.DwmGetWindowAttribute(h,14,out cloaked,4)==0 && cloaked!=0) return true;
            string path=Native.Exe(pid);
            if(String.IsNullOrEmpty(path)) return true;
            if(Path.GetFileName(path).Equals("ApplicationFrameHost.exe",StringComparison.OrdinalIgnoreCase)) {
                string hosted=HostedApplication(h,pid);
                if(!String.IsNullOrEmpty(hosted)) path=hosted;
                else if(title=="设置" || title.Equals("Settings",StringComparison.OrdinalIgnoreCase))
                    path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"ImmersiveControlPanel","SystemSettings.exe");
            }
            seen.Add(h.ToInt64());
            list.Add(new {hwnd=h.ToInt64().ToString(),pid=pid,title=title,path=path,icon=IconForWindow(h,path),active=h==active,minimized=Native.IsIconic(h)});
            return true;
        },IntPtr.Zero);
        foreach(long handle in windowIcons.Keys.Where(h=>!seen.Contains(h)).ToArray()) windowIcons.Remove(handle);
        return list;
    }
    static string HostedApplication(IntPtr frame,uint framePid) {
        string hosted="";
        Native.EnumChildWindows(frame,delegate(IntPtr child,IntPtr data) {
            uint childPid;Native.GetWindowThreadProcessId(child,out childPid);
            if(childPid==0 || childPid==framePid) return true;
            string candidate=Native.Exe(childPid);
            if(String.IsNullOrEmpty(candidate) || !File.Exists(candidate)) return true;
            hosted=candidate;return false;
        },IntPtr.Zero);
        return hosted;
    }
    string IconForWindow(IntPtr window,string path) {
        string cached;
        if(windowIcons.TryGetValue(window.ToInt64(),out cached)) return cached;
        if(Path.GetFileName(path).Equals("SystemSettings.exe",StringComparison.OrdinalIgnoreCase)) {
            string settings=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"ImmersiveControlPanel","images","logo.scale-200_altform-unplated.png");
            if(File.Exists(settings)) {windowIcons[window.ToInt64()]=settings;return settings;}
        }
        IntPtr icon=IntPtr.Zero;
        // Taskbar buttons use the window's icon before falling back to the
        // executable. A timeout keeps a stalled application from blocking Dock.
        foreach(int size in new[]{1,2,0}) {
            IntPtr result;
            if(Native.SendMessageTimeout(window,0x7f,new IntPtr(size),IntPtr.Zero,2,75,out result)==IntPtr.Zero) break;
            if(result!=IntPtr.Zero) {icon=result;break;}
        }
        if(icon==IntPtr.Zero) icon=Native.GetClassLongPtr(window,-14);
        if(icon==IntPtr.Zero) icon=Native.GetClassLongPtr(window,-34);
        string file="";
        if(icon!=IntPtr.Zero) {
            try {
                file=Path.Combine(root,"icons",Guid.NewGuid().ToString("N")+".png");
                using(var borrowed=Icon.FromHandle(icon)) using(var bitmap=borrowed.ToBitmap()) bitmap.Save(file,ImageFormat.Png);
            } catch { file=""; }
        }
        if(String.IsNullOrEmpty(file)) file=IconFor(path);
        windowIcons[window.ToInt64()]=file;
        return file;
    }
    string IconFor(string path) {
        string cached; if(icons.TryGetValue(path,out cached)) return cached;
        string expanded=ExpandPath(path);
        if(expanded=="explorer.exe") expanded=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe");
        IntPtr icon=IntPtr.Zero;
        try {
            if(path=="shell:RecycleBinFolder" || path=="builtin:app") {
                var stock=new Native.STOCKICON {size=(uint)Marshal.SizeOf(typeof(Native.STOCKICON))};
                if(Native.SHGetStockIconInfo(path=="builtin:app"?2u:31u,0x100,ref stock)==0) icon=stock.icon;
            }
            if(path=="builtin:settings") expanded=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"ImmersiveControlPanel","SystemSettings.exe");
            if(expanded.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)) {
                var big=new IntPtr[1]; var ids=new uint[1];
                Native.PrivateExtractIcons(expanded,0,128,128,big,ids,1,0); icon=big[0];
            }
            if(icon==IntPtr.Zero) {
                Native.SHFILEINFO info;
                Native.SHGetFileInfo(expanded,0,out info,(uint)Marshal.SizeOf(typeof(Native.SHFILEINFO)),0x100);
                icon=info.hIcon;
            }
            if(icon==IntPtr.Zero) { icons[path]=""; return ""; }
            string file=Path.Combine(root,"icons",Guid.NewGuid().ToString("N")+".png");
            using(var borrowed=Icon.FromHandle(icon)) using(var bitmap=borrowed.ToBitmap()) bitmap.Save(file,ImageFormat.Png);
            icons[path]=file; return file;
        } catch { icons[path]=""; return ""; }
        finally { if(icon!=IntPtr.Zero) Native.DestroyIcon(icon); }
    }
    void SetHidden(bool value) { hide=value; UDo.DesktopZones.BoxPlacement.TaskbarHidden=value; if(value) HideTaskbars(); else Restore(); }
    static string ExpandPath(string path) {
        string expanded=Environment.ExpandEnvironmentVariables(path);
        return expanded.Contains("://") || expanded.StartsWith("shell:") ? expanded : expanded.Replace('/','\\');
    }
    void HideTaskbars() {
        foreach(IntPtr h in Native.Taskbars()) {
            uint currentPid; Native.GetWindowThreadProcessId(h,out currentPid);
            if(hidden.ContainsKey(h.ToInt64()) && hidden[h.ToInt64()]!=currentPid) hidden.Remove(h.ToInt64());
            if(!hidden.ContainsKey(h.ToInt64()) && Native.IsWindowVisible(h)) {
                hidden[h.ToInt64()]=currentPid;
                // Persist restoration intent BEFORE hiding a window, for the independent guard.
                WriteAtomic(Path.Combine(root,"restore.json"),json.Serialize(hidden.Select(p=>new {hwnd=p.Key,pid=p.Value}).ToArray()));
            }
            if(hidden.ContainsKey(h.ToInt64())) Native.ShowWindow(h,0);
        }
    }
    void Restore() { RestoreJournal(root); hidden.Clear(); }
    internal static void RestoreJournal(string directory) {
        string file=Path.Combine(directory,"restore.json"); if(!File.Exists(file)) return;
        try {
            var items=new JavaScriptSerializer().Deserialize<List<Dictionary<string,object>>>(File.ReadAllText(file));
            foreach(var item in items) {
                IntPtr h=new IntPtr(Convert.ToInt64(item["hwnd"])); uint pid; Native.GetWindowThreadProcessId(h,out pid);
                string cls=Native.Class(h);
                if(pid==Convert.ToUInt32(item["pid"]) && (cls=="Shell_TrayWnd" || cls=="Shell_SecondaryTrayWnd")) Native.ShowWindow(h,8);
            }
            File.Delete(file);
        } catch { /* Keep journal for recovery if Explorer is temporarily inaccessible. */ }
    }
    internal static void WriteAtomic(string path,string value) {
        string temp=path+".tmp"; File.WriteAllText(temp,value,new UTF8Encoding(false));
        if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
    }
    static void MarkRestored(string session) {
        if(!File.Exists(Path.Combine(session,"box-icon-visibility.txt")) && !File.Exists(Path.Combine(session,"restore.json")) && !File.Exists(Path.Combine(session,"zone-shell-flags.txt")))
            File.WriteAllText(Path.Combine(session,"restore-complete.txt"),"ok");
    }
    protected override void ExitThreadCore() {
        if(exiting) return; exiting=true; timer.Stop(); hide=false; Restore();
        try {keys.Dispose();} catch(Exception e) {error=e.Message;}
        if(foregroundHook!=IntPtr.Zero) Native.UnhookWinEvent(foregroundHook);
        if(PerformanceProbe.Enabled) try {PerformanceProbe.Export(Path.Combine(root,"performance.json"));} catch(IOException) { }
        try {if(partitions!=null) partitions.Dispose();} catch(Exception e) {error=e.Message;}
        try {if(widgets!=null) widgets.Dispose();} catch(Exception e) {error=e.Message;}
        try {if(zones!=null) zones.Dispose();} catch(Exception e) {error=e.Message;}
        try {UDo.DesktopZones.ZoneManager.Recover(root);} catch(Exception e) {error=e.Message;}
        try {MarkRestored(root);} catch(IOException) { }
        trayApps.Dispose(); previews.Dispose(); glass.Dispose(); tray.Visible=false; tray.Dispose(); applicationIcon.Dispose(); hotkey.Dispose(); parent.Dispose(); base.ExitThreadCore();
    }
    sealed class WindowOwner : IWin32Window { public IntPtr Handle {get;private set;} internal WindowOwner(IntPtr h) {Handle=h;} }
    sealed class HotkeyWindow : NativeWindow,IDisposable {
        readonly Action restore; internal bool Registered;
        internal HotkeyWindow(Action action) {restore=action; CreateHandle(new CreateParams()); Registered=Native.RegisterHotKey(Handle,1,0x4003,0x7B);}
        protected override void WndProc(ref Message m) {if(m.Msg==0x312) restore(); base.WndProc(ref m);}
        public void Dispose() {Native.UnregisterHotKey(Handle,1); DestroyHandle();}
    }
    [STAThread] static int Main(string[] args) {
        try {Native.SetProcessDpiAwarenessContext(new IntPtr(-4));} catch(EntryPointNotFoundException) {Native.SetProcessDPIAware();}
        if(args.Length==3 && args[0]=="--guard") {
            try { using(var process=Process.GetProcessById(Int32.Parse(args[1]))) process.WaitForExit(); } catch {}
            RestoreJournal(args[2]); UDo.DesktopZones.ZoneManager.Recover(args[2]); MarkRestored(args[2]); return 0;
        }
        if(args.Length==2 && (args[0]=="--new-zone"||args[0]=="--new-partition")) {
            try {
                Point point=Cursor.Position;
                WriteAtomic(Path.Combine(args[1],"cmd-"+DateTime.UtcNow.Ticks+"-"+Guid.NewGuid().ToString("N")+".json"),new JavaScriptSerializer().Serialize(new {op=args[0]=="--new-partition"?"partition_create":"zone_create",x=point.X,y=point.Y}));return 0;
            } catch {return 1;}
        }
        if(args.Length==2 && args[0]=="--restore") {RestoreJournal(args[1]); UDo.DesktopZones.ZoneManager.Recover(args[1]); MarkRestored(args[1]); return 0;}
        if(args.Length!=2) return 2;
        try { Application.EnableVisualStyles(); using(var app=new DockBridge(Int32.Parse(args[0]),args[1])) Application.Run(app); return 0; }
        catch(Exception e) { RestoreJournal(args[1]); UDo.DesktopZones.ZoneManager.Recover(args[1]); File.WriteAllText(Path.Combine(args[1],"bridge-error.txt"),e.ToString()); return 1; }
    }
}
