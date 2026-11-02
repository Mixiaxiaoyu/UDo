using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace UDo.DesktopZones {
    // Controller boundary: Shell, persistence and presentation have separate lifecycles.
    internal sealed class ZoneManager : IDisposable {
        readonly ZoneStore store; readonly ZoneDocument document; readonly string session;
        readonly Dictionary<string,ZoneWindow> windows=new Dictionary<string,ZoneWindow>();
        readonly bool registerMenu; readonly BoxIconLease lease; readonly Action<string> present;
        readonly DesktopRefresh refresh;
        Native.WinEventProc desktopChanged;IntPtr desktopHook,iconList;
        internal readonly IDesktopAdapter Desktop;
        internal readonly BoxIconCache Icons=new BoxIconCache();
        string[] externalItems=new string[0];
        internal Action<string> ExternalClaim;
        internal IntPtr DesktopHost {get {return refresh==null?Desktop.Host:refresh.Host;}}
        internal HashSet<string> OwnedPaths {get {return new HashSet<string>(document.Zones.SelectMany(z=>z.Items),StringComparer.OrdinalIgnoreCase);}}
        internal void SetExternalItems(string[] paths) {externalItems=paths;RefreshIcons();}
        internal void ReleaseToPartition(IEnumerable<string> paths) {
            var moved=new HashSet<string>(paths,StringComparer.OrdinalIgnoreCase);
            foreach(var zone in document.Zones) if(zone.Items.RemoveAll(p=>moved.Contains(p))>0) {if(windows.ContainsKey(zone.Id)) windows[zone.Id].ContentChanged();}
            Save();RefreshIcons();
        }
        string topology="";IntPtr host;DateTime nextCheck;
        internal string Error="";
        internal int Count {get {return document.Zones.Count;}}
        internal bool Manipulating {get {return windows.Values.Any(w=>w.Manipulating);}}
        internal Rectangle[] InteractiveBounds {get {return windows.Values.Where(w=>!w.IsDisposed&&w.Visible).Select(w=>w.ScreenBounds).ToArray();}}
        internal ZoneManager(string data,string session,Action<string> present) : this(data,session,null,true,present) { }
        internal ZoneManager(string data,string session,IDesktopAdapter desktop,bool registerMenu) : this(data,session,desktop,registerMenu,null) { }
        internal ZoneManager(string data,string session,IDesktopAdapter desktop,bool registerMenu,Action<string> present) {
            this.session=session;this.registerMenu=registerMenu;this.present=present;
            store=new ZoneStore(Path.Combine(data,"storage-boxes.json"));document=store.Load();
            Desktop=desktop??new DesktopAdapter(Path.Combine(session,"zone-shell-flags.txt"));
            string sessions=Path.Combine(data,"sessions");
            if(desktop==null&&Directory.Exists(sessions)) foreach(string previous in Directory.GetDirectories(sessions)) {
                string state=Path.Combine(previous,"state.json");
                if(previous!=session&&File.Exists(Path.Combine(previous,"box-icon-visibility.txt"))&&(!File.Exists(state)||DateTime.UtcNow-File.GetLastWriteTimeUtc(state)>TimeSpan.FromSeconds(10))) Recover(previous);
            }
            string visibilityJournal=Path.Combine(session,"box-icon-visibility.txt");
            if(desktop==null) {
                refresh=new DesktopRefresh(Desktop,visibilityJournal,document.Zones.SelectMany(z=>z.Items).ToArray());
                desktopChanged=delegate(IntPtr hook,uint kind,IntPtr window,int objectId,int childId,uint thread,uint time) {
                    if(window==iconList&&objectId==-4&&(kind==0x8000||kind==0x8001||kind==0x8002||kind==0x8003||kind==0x8004||(kind==0x800B&&childId>0))) refresh.Invalidate();
                };
                desktopHook=Native.SetWinEventHook(0x8000,0x800B,IntPtr.Zero,desktopChanged,0,0,0);
            } else lease=new BoxIconLease(visibilityJournal,Desktop);
            try {if(registerMenu) DesktopMenu.Install(session);Reconcile();} catch {Dispose();throw;}
        }
        internal void Tick() {
            var loaded=Icons.Pump();
            if(loaded.Count>0) foreach(var window in windows.Values) window.IconsReady(loaded);
            foreach(var window in windows.Values) window.Tick();
            if(DateTime.UtcNow<nextCheck) return;nextCheck=DateTime.UtcNow.AddMilliseconds(250);
            try {Reconcile();if(refresh!=null) Error=refresh.Error;} catch(Exception e) {Error=e.Message;}
        }
        internal void Reconcile() {
            if(document.Zones.Count==0) return;
            string signature=String.Join("|",Screen.AllScreens.Select(s=>s.DeviceName+":"+BoxPlacement.WorkArea(s)));IntPtr current=refresh==null?Desktop.Host:refresh.Host;
            if(current==IntPtr.Zero) return;
            if(signature==topology&&current==host&&windows.Count==document.Zones.Count) return;
            if(windows.Values.Any(w=>w.Manipulating)) return;
            if(refresh!=null&&signature!=topology) refresh.Invalidate();
            foreach(var window in windows.Values) window.Dispose();windows.Clear();host=current;topology=signature;
            iconList=FindWindowEx(host,IntPtr.Zero,"SysListView32",null);
            foreach(var model in document.Zones) {
                Screen screen=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==model.Monitor)??Screen.PrimaryScreen;
                var window=new ZoneWindow(model,this);windows[model.Id]=window;
                Rectangle work=BoxPlacement.WorkArea(screen);
                window.Attach(host,ZoneLayout.Restore(model,work));
            }
            RefreshIcons();
        }
        internal void Create(Point point) {
            try {
                Screen screen=Screen.FromPoint(point);var model=new Zone {Name="收纳盒 "+(document.Zones.Count+1)};
                var work=BoxPlacement.WorkArea(screen);
                var bounds=ZoneLayout.Clamp(new Rectangle(point.X,point.Y,360,240),work);
                ZoneLayout.Remember(model,bounds,work,screen.DeviceName);document.Zones.Add(model);
                Save();Reconcile();if(windows.ContainsKey(model.Id)) Edit(windows[model.Id]);
            } catch(Exception e) {Report(e);}
        }
        internal void Placed(ZoneWindow window) {
            try {
                Screen screen=Screen.FromRectangle(window.ScreenBounds);Rectangle work=BoxPlacement.WorkArea(screen);Rectangle rect=ZoneLayout.Clamp(window.ExpandedBounds,work);
                window.SetExpandedBounds(rect);ZoneLayout.Remember(window.Model,rect,work,screen.DeviceName);
                if(window.Model.Collapsed&&window.Model.Edge=="none") {
                    Rectangle header=window.FoldedBounds;
                    window.Model.FoldX=(float)(header.X-work.X)/work.Width;
                    window.Model.FoldY=(float)(header.Y-work.Y)/work.Height;
                } else window.ResetFoldedPosition();
                Save();
            } catch(Exception e) {Report(e);}
        }
        void RefreshIcons() {var items=document.Zones.SelectMany(z=>z.Items).Concat(externalItems).ToArray();if(refresh!=null) refresh.Request(items);else lease.Apply(items);}
        internal void Align(ZoneWindow window) {RefreshIcons();}
        internal void Dragged(ZoneWindow window) { }
        void Changed(Zone model) {
            var window=windows[model.Id];
            if(model.AutoFit) {
                Rectangle rect=window.ExpandedBounds;rect.Width=ZoneLayout.CompactWidth(rect.Width,window.ScaleFactor);rect.Height=ZoneLayout.Height(rect.Width,model.Items.Count,true,window.ScaleFactor);
                window.SetExpandedBounds(rect);
            }
            window.ContentChanged();Placed(window);RefreshIcons();
        }
        internal void Add(Zone target,string[] paths,int index) {try {foreach(string path in paths) Transfer(Desktop.Import(path),target,index++);} catch(Exception e) {Report(e);}}
        internal void Transfer(string path,Zone target,int index) {
            try {
                var source=document.Zones.FirstOrDefault(z=>z.Items.Any(p=>String.Equals(p,path,StringComparison.OrdinalIgnoreCase)));
                int previous=source==null?-1:source.Items.FindIndex(p=>String.Equals(p,path,StringComparison.OrdinalIgnoreCase));
                if(source!=null) source.Items.RemoveAt(previous);
                if(source==target&&previous<index) index--;
                int inserted=Math.Max(0,Math.Min(index,target.Items.Count));
                target.Items.Insert(inserted,path);
                // Register the box as the new owner before releasing a partition.
                // The visibility worker must never observe an unowned interval,
                // otherwise Explorer briefly paints the original desktop icon.
                try {if(ExternalClaim!=null) ExternalClaim(path);}
                catch {target.Items.RemoveAt(inserted);if(source!=null) source.Items.Insert(previous,path);RefreshIcons();throw;}
                if(source!=null&&source!=target) Changed(source);Changed(target);
            } catch(Exception e) {Report(e);}
        }
        internal void Release(Zone zone,string path,Point point) {try {if(!zone.Items.Remove(path)) return;Changed(zone);} catch(Exception e) {Report(e);}}
        internal bool IsDesktopPoint(Point point) {
            IntPtr at=DesktopAdapter.WindowFromPoint(point);string cls=Native.Class(at);
            return (cls=="SysListView32"||cls=="SHELLDLL_DefView"||cls=="Progman"||cls=="WorkerW")&&DesktopAdapter.GetAncestor(at,2)==DesktopAdapter.GetAncestor(DesktopHost,2);
        }
        internal void Open(string path) {try {Process.Start(new ProcessStartInfo(path) {UseShellExecute=true});} catch(Exception e) {Report(e);}}
        internal void Delete(Zone zone) {
            try {
                int index=document.Zones.IndexOf(zone);document.Zones.Remove(zone);
                try {Save();} catch {document.Zones.Insert(index,zone);throw;}
                var removed=windows[zone.Id];
                removed.Dispose();windows.Remove(zone.Id);
                RefreshIcons();
            } catch(Exception e) {Report(e);}
        }
        internal void Fold(ZoneWindow window) {if(window.Model.Edge!="none") window.Retract();else {window.Model.Collapsed=!window.Model.Collapsed;window.RefreshAppearance();}Save();}
        void Present(object value) {if(present!=null) present("box:"+new JavaScriptSerializer().Serialize(value));}
        static object BoxSummary(Zone box) {return new {box.Id,box.Name,box.ShowName,box.AutoFit,box.Theme,box.Transparency,box.Collapsed,box.Monitor,box.Edge};}
        internal void Edit(ZoneWindow window) {window.UiOpen=true;Present(new {kind="settings",box=BoxSummary(window.Model)});}
        internal void DismissMenu() {Present(new {kind="dismiss_menu"});}
        internal void ShowMenu(ZoneWindow window,Point point,int item) {
            window.UiOpen=true;Present(new {kind="menu",box=BoxSummary(window.Model),x=point.X,y=point.Y,item=item>=0?window.Model.Items[item]:"",screens=Screen.AllScreens.Select(s=>new {id=s.DeviceName,name=s.DeviceName+(s.Primary?"（主屏）":"")}).ToArray()});
        }
        internal void Command(Dictionary<string,object> c) {
            try {
                string op=Convert.ToString(c["op"]);
                if(op=="box_ui_closed") {foreach(var w in windows.Values) w.UiOpen=false;return;}
                ZoneWindow window;if(!windows.TryGetValue(Convert.ToString(c["id"]),out window)) return;
                if(op=="box_ui_open") {window.UiOpen=true;return;}
                var model=window.Model;
                if(op=="box_edit") {Edit(window);return;}
                if(op=="box_delete") {Delete(model);return;}
                if(op=="box_fold") {Fold(window);return;}
                if(op=="box_open"||op=="box_release") {
                    string item=Convert.ToString(c["item"]);if(!model.Items.Contains(item)) return;
                    if(op=="box_open") Open(item);else Release(model,item,new Point(window.ExpandedBounds.Right+24,window.ExpandedBounds.Top));return;
                }
                if(op=="box_sort") {model.Items=(Convert.ToString(c["by"])=="type"?model.Items.OrderBy(Path.GetExtension,StringComparer.CurrentCultureIgnoreCase).ThenBy(Path.GetFileName,StringComparer.CurrentCultureIgnoreCase):model.Items.OrderBy(Path.GetFileName,StringComparer.CurrentCultureIgnoreCase)).ToList();Changed(model);return;}
                if(op=="box_monitor") {
                    var screen=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==Convert.ToString(c["monitor"]));if(screen==null) return;
                    window.SetExpandedBounds(ZoneLayout.Restore(model,BoxPlacement.WorkArea(screen)));Placed(window);return;
                }
                if(op=="box_update") {
                    if(c.ContainsKey("name")) {string name=Convert.ToString(c["name"]).Trim();model.Name=String.IsNullOrEmpty(name)?"收纳盒":name.Substring(0,Math.Min(60,name.Length));}
                    if(c.ContainsKey("show_name")) model.ShowName=Convert.ToBoolean(c["show_name"]);
                    if(c.ContainsKey("auto_fit")) model.AutoFit=Convert.ToBoolean(c["auto_fit"]);
                    if(c.ContainsKey("theme")) model.Theme=Convert.ToString(c["theme"])=="dark"?"dark":"light";
                    if(c.ContainsKey("transparency")) {float n=Convert.ToSingle(c["transparency"]);if(ZoneLayout.Finite(n)) model.Transparency=Math.Max(0,Math.Min(.9f,n));}
                    Changed(model);
                }
            } catch(Exception e) {Report(e);}
        }
        void Save() {store.Save(document);}
        void Report(Exception error) {Error=error.Message;Present(new {kind="error",message=error.Message});}
        internal static void Recover(string session) {
            var adapter=new DesktopAdapter(Path.Combine(session,"zone-shell-flags.txt"));
            string journal=Path.Combine(session,"box-icon-visibility.txt");
            for(int i=0;i<40&&File.Exists(journal);i++) {BoxIconLease.Recover(journal,adapter);if(File.Exists(journal)) System.Threading.Thread.Sleep(250);}
            adapter.RestoreLayout();DesktopMenu.Remove(session);
        }
        [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string title);
        public void Dispose() {
            if(desktopHook!=IntPtr.Zero) {Native.UnhookWinEvent(desktopHook);desktopHook=IntPtr.Zero;}
            foreach(var window in windows.Values) window.Dispose();windows.Clear();
            Icons.Dispose();
            if(refresh!=null) refresh.Dispose();else if(lease!=null) lease.Restore();
            Desktop.RestoreLayout();if(registerMenu) DesktopMenu.Remove(session);
        }
    }
}
