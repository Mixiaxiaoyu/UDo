using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using UDo.DesktopZones;

namespace UDo.DesktopPartitions {
    // Owns partition state/transactions. The box service alone owns the Shell visibility lease.
    internal sealed class PartitionManager : IDisposable {
        internal PartitionDocument Document;internal bool Editing;internal string Selected="",Error="";
        internal readonly BoxIconCache Icons=new BoxIconCache();
        internal readonly DesktopCatalog Catalog;
        readonly DesktopMenuService desktopMenu;
        readonly ZoneManager boxes;readonly PartitionStore store;readonly Action<string> present;
        readonly Func<Rectangle[]> interactiveOverlays;
        internal readonly Dictionary<string,PartitionWindow> Windows=new Dictionary<string,PartitionWindow>();
        readonly Dictionary<string,PartitionGuide> guides=new Dictionary<string,PartitionGuide>();
        readonly List<string> undo=new List<string>();
        readonly EditHotkey hotkey;
        string topology="",changeKey="";IntPtr host;DateTime checkAt,saveAt,lastChange;
        bool needsSave,disposed;
        int catalogRevision;DateTime nextCatalog;
        internal Rectangle[] NativeIconHoles=new Rectangle[0];
        Rectangle[] lastOverlayBounds=new Rectangle[0];
        internal Rectangle[] InteractiveOverlayBounds {get {return lastOverlayBounds;}}
        internal bool Manipulating {get {return (Canvas!=null&&Canvas.Drawing)||Windows.Values.Any(w=>w.Manipulating)||guides.Values.Any(g=>g.Manipulating);}}
        bool regionsPending;
        internal PartitionCanvasWindow Canvas;
        internal PartitionManager(string data,ZoneManager boxes,Action<string> present,DesktopCatalog catalog=null,Func<Point,int> showDesktopMenu=null,Func<Rectangle[]> interactiveOverlays=null) {
            this.boxes=boxes;this.present=present;this.interactiveOverlays=interactiveOverlays;store=new PartitionStore(Path.Combine(data,"desktop-partitions.json"));
            Document=store.Load();Catalog=catalog??new DesktopCatalog();
            desktopMenu=new DesktopMenuService(showDesktopMenu);
            hotkey=new EditHotkey(delegate {if(Canvas!=null) CloseCanvas();else if(Editing) ToggleEdit(false);else BeginCanvas(Screen.FromPoint(Cursor.Position));});
            boxes.ExternalClaim=DetachForBox;
            // Existing boxes win when loading a manually edited/imported duplicate reference.
            var owned=boxes.OwnedPaths;foreach(var z in All) z.Items.RemoveAll(p=>owned.Contains(p));
            Sync();
        }
        internal IEnumerable<Partition> All {get {return Document.Screens.SelectMany(s=>s.Zones);}}
        internal Partition Find(string id) {return All.FirstOrDefault(z=>z.Id==id);}
        internal PartitionScreen Owner(Partition z) {return Document.Screens.First(s=>s.Zones.Contains(z));}
        internal Screen Display(PartitionScreen s) {return Screen.AllScreens.FirstOrDefault(d=>d.DeviceName==s.Monitor);}
        PartitionScreen ScreenState(string monitor) {
            var s=Document.Screens.FirstOrDefault(d=>d.Monitor==monitor);
            if(s==null) {s=new PartitionScreen {Monitor=monitor};Document.Screens.Add(s);}return s;
        }
        internal void Tick() {
            if(disposed) return;bool busy=Manipulating||boxes.Manipulating||(Canvas!=null&&!Canvas.Applying);
            RefreshOverlayHoles();
            if(Canvas!=null) Canvas.Tick();
            if(Canvas!=null&&!Canvas.Applying) return; // Freeze icon/layout work for the entire editing session.
            // Retain completion work during pointer capture; apply once after release.
            if(!busy) {desktopMenu.Pump();Catalog.Pump();}
            if(regionsPending&&!busy) UpdateRegions();
            if(!busy&&Catalog.Revision!=catalogRevision) {catalogRevision=Catalog.Revision;UpdateRegions();}
            if(!busy&&DateTime.UtcNow>=nextCatalog) {nextCatalog=DateTime.UtcNow.AddSeconds(5);if(All.Any()||Editing) Catalog.Refresh();}
            var loaded=Icons.Pump();foreach(var w in Windows.Values) {if(loaded.Count>0) w.IconsReady(loaded);w.Tick();}
            foreach(var g in guides.Values) g.Tick();
            if(!busy&&needsSave&&DateTime.UtcNow>=saveAt) Flush();
            if(!busy&&DateTime.UtcNow>=checkAt) {checkAt=DateTime.UtcNow.AddSeconds(1);Sync();}
        }
        void Flush() {try {long started=PerformanceProbe.Start();store.Save(Document);PerformanceProbe.End("save_ms",started);PerformanceProbe.Count("config_saves");needsSave=false;} catch(Exception e) {Error=e.Message;saveAt=DateTime.UtcNow.AddSeconds(5);Notice("分区保存失败："+e.Message);}}
        void Remember(string key,string snapshot=null) {
            if(key.Length==0||key!=changeKey||(DateTime.UtcNow-lastChange).TotalMilliseconds>600) {
                undo.Add(snapshot??PartitionStore.Json().Serialize(Document));
                while(undo.Count>35||(undo.Count>1&&undo.Sum(s=>(long)s.Length)>16*1024*1024)) undo.RemoveAt(0);
            }
            changeKey=key;lastChange=DateTime.UtcNow;
        }
        internal void Change(Action action,string key="") {
            string previous=PartitionStore.Json().Serialize(Document);
            Remember(key,previous);
            try {action();PartitionStore.CheckBudget(Document);needsSave=true;saveAt=DateTime.UtcNow.AddMilliseconds(180);Sync(true);}
            catch {Document=PartitionStore.Parse(previous);Sync(true);throw;}
        }
        internal void Undo() {
            if(undo.Count==0) {Notice("没有可撤销的分区操作");return;}
            var restored=PartitionStore.Parse(undo[undo.Count-1]);undo.RemoveAt(undo.Count-1);
            boxes.ReleaseToPartition(restored.Screens.SelectMany(s=>s.Zones).SelectMany(z=>z.Items));
            Document=restored;changeKey="";needsSave=true;saveAt=DateTime.UtcNow;Selected="";Sync(true);Notice("已撤销分区操作");
        }
        void Sync(bool changed=false) {
            IntPtr nextHost=boxes.DesktopHost;if(nextHost==IntPtr.Zero) return;
            string next=String.Join("|",Screen.AllScreens.Select(s=>s.DeviceName+":"+s.WorkingArea));
            if((host!=nextHost||next!=topology)&&!Windows.Values.Any(w=>w.Manipulating)) {
                foreach(var w in Windows.Values) w.Dispose();Windows.Clear();foreach(var g in guides.Values) g.Dispose();guides.Clear();host=nextHost;topology=next;changed=true;
            }
            var visible=Document.Screens.Where(s=>Display(s)!=null).SelectMany(s=>s.Zones).ToList();
            foreach(string id in Windows.Keys.ToArray()) if(!visible.Any(z=>z.Id==id&&z==Windows[id].Model)) {Windows[id].Dispose();Windows.Remove(id);}
            foreach(var z in visible) {
                PartitionWindow w;if(!Windows.TryGetValue(z.Id,out w)) {w=new PartitionWindow(z,this);Windows[z.Id]=w;w.Attach(host,PartitionLayout.Bounds(z,Display(Owner(z)).WorkingArea));}
                if(changed) {
                    // A drag may remove an item while its source window is in the
                    // nested OLE loop. Keep geometry stable, but discard its old grid.
                    if(!w.Manipulating) w.SetBounds(PartitionLayout.Bounds(z,Display(Owner(z)).WorkingArea));
                    w.InvalidateContent();
                }
            }
            if(Editing) foreach(var screen in Screen.AllScreens) {if(!guides.ContainsKey(screen.DeviceName)) {var guide=new PartitionGuide(this,screen);guides[screen.DeviceName]=guide;guide.Attach(host,screen.WorkingArea);}if(changed) guides[screen.DeviceName].UpdateHoles();}
            else if(guides.Count>0) {foreach(var g in guides.Values) g.Dispose();guides.Clear();}
            if(changed) {boxes.SetExternalItems(visible.SelectMany(z=>z.Items).ToArray());UpdateRegions();}
        }
        void UpdateRegions() {
            // Pointer capture owns the gesture. Rebuilding whole-monitor HWND regions
            // while it is active synchronizes with DWM on every mouse message.
            if(Manipulating) {regionsPending=true;return;}regionsPending=false;
            PerformanceProbe.Count("region_rebuilds");var assigned=new HashSet<string>(Windows.Values.SelectMany(w=>w.Model.Items).Concat(boxes.OwnedPaths),StringComparer.OrdinalIgnoreCase);
            NativeIconHoles=Catalog.Items.Where(i=>!assigned.Contains(i.Path)).Select(i=>i.Bounds).ToArray();
            ApplyInteractionHoles();
        }
        void RefreshOverlayHoles() {
            Rectangle[] current=interactiveOverlays==null?boxes.InteractiveBounds:interactiveOverlays()??new Rectangle[0];
            if(lastOverlayBounds.SequenceEqual(current)) return;
            lastOverlayBounds=current;
            if(Manipulating) {regionsPending=true;return;}
            ApplyInteractionHoles();
        }
        void ApplyInteractionHoles() {
            foreach(var window in Windows.Values) window.UpdateHoles(NativeIconHoles,lastOverlayBounds);
            foreach(var guide in guides.Values) guide.UpdateHoles();
        }
        internal void ToggleEdit(bool? value=null) {Editing=value??!Editing;Sync(true);Notice(Editing?"编辑分区：拖标题移动，右下角缩放，空白处框选新建；右键完成编辑。":"已完成编辑，辅助轮廓已隐藏");}
        internal void Select(string id) {if(Selected==id) return;string previous=Selected;Selected=id;foreach(var w in Windows.Values) if(w.Model.Id==previous||w.Model.Id==id) w.InvalidateContent();}
        internal void MovingFinished() {UpdateRegions();}
        internal void BeginCanvas(Screen screen) {
            if(Canvas!=null) {Canvas.Raise();return;}
            if(Editing) ToggleEdit(false);
            var state=Document.Screens.FirstOrDefault(s=>s.Monitor==screen.DeviceName);
            Catalog.PauseRefresh(true);
            Canvas=new PartitionCanvasWindow(this,screen,state==null?new List<Partition>():state.Zones,state==null?18:state.Gap);Canvas.Attach(host,screen.WorkingArea);Canvas.Raise();
        }
        internal void CloseCanvas() {if(Canvas==null) return;var old=Canvas;Canvas=null;old.Dispose();Catalog.PauseRefresh(false);}
        internal void ApplyCanvas(PartitionCanvasWindow canvas) {
            if(canvas!=Canvas||canvas.Applying) return;canvas.Applying=true;canvas.InvalidateContent();
            var draft=canvas.Draft.Regions.Select(r=>r.Copy()).ToArray();string monitor=canvas.Display.DeviceName;
            Catalog.Snapshot(delegate(DesktopItem[] items) {
                if(Canvas!=canvas) return;
                var display=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==monitor);
                if(display==null||display.WorkingArea!=canvas.WorkArea) {CloseCanvas();Notice("显示器布局已改变，请重新打开画布");return;}
                var state=ScreenState(monitor);var old=state.Zones.ToDictionary(z=>z.Id);
                var next=draft.Select((r,i)=> {Partition previous;var z=old.TryGetValue(r.Id,out previous)?PartitionStore.Json().Deserialize<Partition>(PartitionStore.Json().Serialize(previous)):new Partition {Id=r.Id,Name="分区 "+(i+1)};var bounds=r.Bounds;bounds.Offset(display.WorkingArea.Location);PartitionLayout.Remember(z,bounds,display.WorkingArea);return z;}).ToList();
                var owned=new HashSet<string>(All.Where(z=>Owner(z).Monitor!=monitor).SelectMany(z=>z.Items).Concat(boxes.OwnedPaths).Concat(next.SelectMany(z=>z.Items)),StringComparer.OrdinalIgnoreCase);
                foreach(var item in items) if(item.FileReference&&!owned.Contains(item.Path)) {
                    int index=Array.FindIndex(draft,r=>r.Bounds.Contains(new Point(item.Position.X-display.WorkingArea.X,item.Position.Y-display.WorkingArea.Y)));
                    if(index>=0) {next[index].Items.Add(item.Path);owned.Add(item.Path);}
                }
                CloseCanvas();Change(delegate {state.Zones=next;state.Template="";Selected="";});Notice("画布布局已应用；可撤销本次整体划分，文件位置未改变");
            },delegate(string error) {if(Canvas==canvas) {canvas.Applying=false;canvas.InvalidateContent();}Notice(error);});
        }
        internal void Create(Point at) {
            ToggleEdit(true);var screen=Screen.FromPoint(at);var state=ScreenState(screen.DeviceName);
            var size=new Size(Math.Min(400,screen.WorkingArea.Width/2),Math.Min(280,screen.WorkingArea.Height/2));
            for(int y=screen.WorkingArea.Top+12;y+size.Height<=screen.WorkingArea.Bottom;y+=48) for(int x=screen.WorkingArea.Left+12;x+size.Width<=screen.WorkingArea.Right;x+=48) {
                var z=new Partition();PartitionLayout.Remember(z,new Rectangle(x,y,size.Width,size.Height),screen.WorkingArea);
                if(!state.Zones.Any(other=>PartitionLayout.Overlaps(z,other))) {CreateRect(screen,new Rectangle(x,y,size.Width,size.Height));return;}
            }
            Notice("没有足够空位，请先缩小已有分区，或在空白处框选新建。");
        }
        internal void CreateRect(Screen screen,Rectangle rectangle) {
            if(rectangle.Width<150||rectangle.Height<130) {Notice("分区至少需要 150 × 130 像素");return;}
            var state=ScreenState(screen.DeviceName);var z=new Partition {Name="分区 "+(state.Zones.Count+1)};PartitionLayout.Remember(z,rectangle,screen.WorkingArea);
            if(state.Zones.Count>=80||state.Zones.Any(other=>PartitionLayout.Overlaps(z,other))) {Notice("新分区不能与已有分区重叠");return;}
            Change(delegate {state.Zones.Add(z);state.Template="";Selected=z.Id;});
            // Take a fresh snapshot after drawing; never stall pointer release on Explorer.
            Catalog.Snapshot(delegate(DesktopItem[] items) {
                var current=Find(z.Id);if(current==null) return;var occupied=Owned();
                var display=Display(Owner(current));if(display==null) return;var captureBounds=PartitionLayout.Bounds(current,display.WorkingArea);
                var boxOwned=boxes.OwnedPaths;
                var captured=items.Where(i=>i.FileReference&&captureBounds.Contains(i.Position)&&!occupied.Contains(i.Path)&&!boxOwned.Contains(i.Path)).Select(i=>i.Path).ToArray();
                if(captured.Length>0) {current.Items.AddRange(captured);needsSave=true;saveAt=DateTime.UtcNow;Sync(true);}
            },Notice);
        }
        HashSet<string> Owned() {return new HashSet<string>(All.SelectMany(z=>z.Items),StringComparer.OrdinalIgnoreCase);}
        internal bool Place(Partition z,Rectangle rect) {
            var screen=Screen.FromRectangle(rect);var target=ScreenState(screen.DeviceName);rect=PartitionLayout.Clamp(rect,screen.WorkingArea);
            var candidate=new Partition();PartitionLayout.Remember(candidate,rect,screen.WorkingArea);
            if(target.Zones.Any(other=>other!=z&&PartitionLayout.Overlaps(other,candidate))) {Notice("分区不能重叠，已恢复原位置");Sync(true);return false;}
            Change(delegate {var old=Owner(z);if(old!=target) {old.Zones.Remove(z);target.Zones.Add(z);}old.Template=target.Template="";PartitionLayout.Remember(z,rect,screen.WorkingArea);});return true;
        }
        internal void Fit(Partition z) {
            var screen=Display(Owner(z));if(screen==null) return;var rect=PartitionLayout.Bounds(z,screen.WorkingArea);var grid=PartitionLayout.GridFor(z.Style,rect.Size,Editing,1,z.Items.Count);
            rect.Height=Math.Max(130,grid.Top+z.Style.Padding+(int)Math.Ceiling((double)Math.Max(1,z.Items.Count)/grid.Columns)*(grid.CellHeight+grid.Gap));Place(z,rect);
        }
        internal void Delete(Partition z,bool clear) {Change(delegate {z.Items.Clear();if(!clear) {var owner=Owner(z);owner.Zones.Remove(z);owner.Template="";Selected="";}});Notice(clear?"图标已移回桌面，文件保留":"分区已删除，文件保留并回到桌面");}
        void Duplicate(Partition z) {
            var state=Owner(z);var copy=PartitionStore.Json().Deserialize<Partition>(PartitionStore.Json().Serialize(z));copy.Id=Guid.NewGuid().ToString("N");copy.Items.Clear();copy.Name+=" 副本";
            for(float y=.01f;y+copy.H<=1;y+=.025f) for(float x=.01f;x+copy.W<=1;x+=.025f) {copy.X=x;copy.Y=y;if(!state.Zones.Any(other=>PartitionLayout.Overlaps(copy,other))) {Change(delegate {state.Zones.Add(copy);state.Template="";Selected=copy.Id;});return;}}
            Notice("找不到不重叠的空位，请先调整布局");
        }
        internal void Transfer(string[] paths,Partition target) {
            target=Find(target.Id);if(target==null) return;
            Change(delegate {var set=new HashSet<string>(paths,StringComparer.OrdinalIgnoreCase);foreach(var z in All) z.Items.RemoveAll(p=>set.Contains(p));target.Items.AddRange(paths.Distinct(StringComparer.OrdinalIgnoreCase));});
            boxes.ReleaseToPartition(paths);
        }
        internal void Add(string[] paths,Partition target) {
            string[] imported=null;
            Catalog.Enqueue(delegate {imported=paths.Select(boxes.Desktop.Import).ToArray();},delegate(string error) {if(error.Length>0) Notice(error);else if(Find(target.Id)!=null) Transfer(imported,target);});
        }
        void DetachForBox(string path) {if(!All.Any(z=>z.Items.Contains(path,StringComparer.OrdinalIgnoreCase))) return;Change(delegate {foreach(var z in All) z.Items.RemoveAll(p=>String.Equals(p,path,StringComparison.OrdinalIgnoreCase));});}
        internal void Release(Partition z,string path,Point? point=null) {
            Change(delegate {z.Items.RemoveAll(p=>String.Equals(p,path,StringComparison.OrdinalIgnoreCase));});
            if(point.HasValue) Catalog.Enqueue(delegate {boxes.Desktop.Position(path,point.Value);},delegate(string e) {if(e.Length>0) Notice(e);Catalog.Refresh();});
        }
        internal bool IsDesktopPoint(Point point) {IntPtr at=DesktopAdapter.WindowFromPoint(point);return boxes.IsDesktopPoint(point)||guides.Values.Any(g=>g.Handle==at);}
        internal void Open(string path) {boxes.Open(path);}
        internal void Notice(string text) {Send(new {kind="notice",message=text});}
        void Send(object value) {if(present!=null) present("partition:"+PartitionStore.Json().Serialize(value));}
        internal static object Summary(Partition z) {return new {z.Id,z.Name,z.X,z.Y,z.W,z.H,count=z.Items.Count,style=new {z.Style.TitleVisible,z.Style.FollowDesktop,z.Style.TitleAlign,z.Style.TitleColor,z.Style.Background,z.Style.Color,z.Style.Border,z.Style.BorderColor,z.Style.Opacity,z.Style.Radius,z.Style.IconSize,z.Style.IconGap,z.Style.Padding,has_image=!String.IsNullOrEmpty(z.Style.Image)}};}
        internal void Settings(string monitor=null,string tab="layout") {
            var screen=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==monitor)??Screen.FromPoint(Cursor.Position);var state=ScreenState(screen.DeviceName);
            Send(new {kind="settings",tab=tab,monitor=state.Monitor,template=state.Template,gap=state.Gap,editing=Editing,selected=Selected,zones=state.Zones.Select(Summary).ToArray(),screens=Screen.AllScreens.Select(s=>new {id=s.DeviceName,name=s.DeviceName+(s.Primary?" · 主屏":""),w=s.WorkingArea.Width,h=s.WorkingArea.Height}).ToArray(),offline=Document.Screens.Count(s=>Display(s)==null),undo=undo.Count});
        }
        internal void Menu(Partition z,Point at,int index=-1) {
            if(z!=null) Select(z.Id);
            Send(new {kind="menu",zone=z==null?null:Summary(z),item=z!=null&&index>=0&&index<z.Items.Count?z.Items[index]:"",editing=Editing,x=at.X,y=at.Y,monitor=z==null?Screen.FromPoint(at).DeviceName:Owner(z).Monitor,targets=All.Select(p=>new {p.Id,p.Name}).ToArray()});
        }
        internal void RightClick(Partition z,Point at,int index,bool custom=false) {
            Menu(z,at,index);
        }
        internal void DesktopMenu(Partition z,Point at) {
            string id=z==null?"":z.Id;
            desktopMenu.Open(at,delegate(int result,string error) {
                if(error.Length>0) {Notice("Windows 桌面菜单暂时不可用："+error);Menu(Find(id),at);}
            });
        }
        internal void Overflow(Partition z,int page=0) {page=Math.Max(0,Math.Min(Math.Max(0,(z.Items.Count-1)/40),page));Send(new {kind="overflow",zone=Summary(z),page=page,pages=(int)Math.Ceiling(z.Items.Count/40.0),items=z.Items.Skip(page*40).Take(40).ToArray(),targets=All.Where(p=>p!=z).Select(p=>new {p.Id,p.Name}).ToArray()});}
        DesktopItem[] ScreenItems(Screen screen,DesktopItem[] all) {
            var own=ScreenState(screen.DeviceName).Zones.SelectMany(z=>z.Items).ToArray();var owned=Owned();var boxOwned=boxes.OwnedPaths;
            return all.Where(i=>i.FileReference&&!boxOwned.Contains(i.Path)&&(own.Contains(i.Path,StringComparer.OrdinalIgnoreCase)||(!owned.Contains(i.Path)&&screen.WorkingArea.Contains(i.Position)))).ToArray();
        }
        void Template(string monitor,string template) {
            var screen=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==monitor);if(screen==null||!PartitionLayout.Templates.Contains(template)) return;
            Catalog.Snapshot(delegate(DesktopItem[] all) {
                var state=ScreenState(monitor);var incoming=ScreenItems(screen,all);Change(delegate {PartitionLayout.Apply(state,template,screen.WorkingArea.Size);var owned=Owned();foreach(var item in incoming) if(!owned.Contains(item.Path)) {int index=Math.Abs(Array.IndexOf(new[]{"应用与快捷方式","图片与设计素材","文档资料","压缩文件","文件夹","其他文件"},item.Category));state.Zones[index%state.Zones.Count].Items.Add(item.Path);}Selected=state.Zones[0].Id;});Settings(monitor);
            },Notice);
        }
        void Organize(string monitor,string[] categories=null) {
            var screen=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==monitor);if(screen==null) return;
            Catalog.Snapshot(delegate(DesktopItem[] all) {
                var items=ScreenItems(screen,all);
                if(categories==null) {Send(new {kind="organize",monitor=monitor,groups=items.GroupBy(i=>i.Category).Select(g=>new {name=g.Key,count=g.Count(),sample=g.Take(4).Select(i=>Path.GetFileName(i.Path)).ToArray()}).ToArray()});return;}
                var groups=items.Where(i=>categories.Contains(i.Category)).GroupBy(i=>i.Category).ToArray();if(groups.Length==0) {Notice("请选择至少一个有文件的分类");return;}
                var state=ScreenState(monitor);Change(delegate {var rects=PartitionLayout.Rects("",groups.Length,screen.WorkingArea.Size,state.Gap);state.Zones=groups.Select((g,i)=>new Partition {Name=g.Key,X=rects[i].X,Y=rects[i].Y,W=rects[i].Width,H=rects[i].Height,Style=new PartitionStyle {TitleVisible=true},Items=g.Select(item=>item.Path).ToList()}).ToList();state.Template="";Selected=state.Zones[0].Id;});Settings(monitor);
            },Notice);
        }
        static string S(Dictionary<string,object> c,string key,string fallback="") {return c.ContainsKey(key)?Convert.ToString(c[key]):fallback;}
        internal void Command(Dictionary<string,object> c) {
            try {
                string op=S(c,"op"),monitor=S(c,"monitor",Screen.FromPoint(Cursor.Position).DeviceName);var z=Find(S(c,"id"));
                if(op=="partition_desktop_menu") {DesktopMenu(z,new Point(Convert.ToInt32(c["x"]),Convert.ToInt32(c["y"])));return;}
                if(op=="partition_canvas") {BeginCanvas(Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==monitor)??Screen.FromPoint(Cursor.Position));return;}
                if(op=="partition_create") {var screen=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==monitor);Create(c.ContainsKey("x")?new Point(Convert.ToInt32(c["x"]),Convert.ToInt32(c["y"])):screen!=null?new Point(screen.WorkingArea.X+screen.WorkingArea.Width/2,screen.WorkingArea.Y+screen.WorkingArea.Height/2):Cursor.Position);return;}
                if(op=="partition_settings") {if(z!=null) {Selected=z.Id;monitor=Owner(z).Monitor;}Settings(monitor,z!=null?"style":"layout");return;}
                if(op=="partition_edit") {if(Canvas!=null) CloseCanvas();else if(Editing) ToggleEdit(false);else BeginCanvas(Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==monitor)??Screen.FromPoint(Cursor.Position));return;}
                if(op=="partition_undo") {Undo();Settings(monitor);return;}
                if(op=="partition_template") {Template(monitor,S(c,"template"));return;}
                if(op=="partition_organize") {Organize(monitor,c.ContainsKey("categories")?((System.Collections.IEnumerable)c["categories"]).Cast<object>().Select(Convert.ToString).ToArray():null);return;}
                if(op=="partition_gap") {var state=ScreenState(monitor);var screen=Display(state);if(screen!=null&&state.Template!="") Change(delegate {state.Gap=Math.Max(4,Math.Min(48,Convert.ToInt32(c["value"])));var rects=PartitionLayout.Rects(state.Template,state.Zones.Count,screen.WorkingArea.Size,state.Gap);for(int i=0;i<state.Zones.Count;i++) {var r=rects[i];var zone=state.Zones[i];zone.X=r.X;zone.Y=r.Y;zone.W=r.Width;zone.H=r.Height;}},"gap:"+monitor);return;}
                if(op=="partition_export") {string path=Path.GetFullPath(S(c,"path")),text=PartitionStore.Json().Serialize(Document);Catalog.Enqueue(delegate {using(var writer=new StreamWriter(new FileStream(path,FileMode.CreateNew,FileAccess.Write))) writer.Write(text);},delegate(string error) {Notice(error==""?"分区配置已导出":error);});return;}
                if(op=="partition_import") {string path=S(c,"path");PartitionDocument imported=null;Catalog.Enqueue(delegate {if(new FileInfo(path).Length>32*1024*1024) throw new InvalidDataException("配置文件过大");imported=PartitionStore.Parse(File.ReadAllText(path));},delegate(string error) {if(error!="") {Notice(error);return;}var boxOwned=boxes.OwnedPaths;foreach(var zone in imported.Screens.SelectMany(s=>s.Zones)) zone.Items.RemoveAll(p=>boxOwned.Contains(p));Change(delegate {Document=imported;Selected="";});Settings(monitor);Notice("已导入布局；收纳盒中已有的文件仍保留在盒子，可撤销本次导入。");});return;}
                if(z==null) return;
                if(op=="partition_delete"||op=="partition_clear") {Delete(z,op=="partition_clear");Settings(monitor);}
                else if(op=="partition_duplicate") {Duplicate(z);Settings(monitor);}
                else if(op=="partition_fit") Fit(z);
                else if(op=="partition_sort") Change(delegate {z.Items=(S(c,"by")=="type"?z.Items.OrderBy(Path.GetExtension,StringComparer.CurrentCultureIgnoreCase).ThenBy(Path.GetFileName,StringComparer.CurrentCultureIgnoreCase):z.Items.OrderBy(Path.GetFileName,StringComparer.CurrentCultureIgnoreCase)).ToList();});
                else if(op=="partition_overflow") Overflow(z,c.ContainsKey("page")?Convert.ToInt32(c["page"]):0);
                else if(op=="partition_open") {string item=S(c,"item");if(z.Items.Contains(item,StringComparer.OrdinalIgnoreCase)) Open(item);}
                else if(op=="partition_release") {string item=S(c,"item");if(z.Items.Contains(item,StringComparer.OrdinalIgnoreCase)) Release(z,item);}
                else if(op=="partition_transfer") {string item=S(c,"item");var target=Find(S(c,"target"));if(target!=null&&z.Items.Contains(item,StringComparer.OrdinalIgnoreCase)) Transfer(new[]{item},target);}
                else if(op=="partition_image") {
                    string path=S(c,"path"),encoded="";Catalog.Enqueue(delegate {if(new FileInfo(path).Length>2*1024*1024) throw new InvalidDataException("背景图片不能超过 2 MB");byte[] bytes=File.ReadAllBytes(path);using(var stream=new MemoryStream(bytes)) using(var image=Image.FromStream(stream)) {if(image.Width>8192||image.Height>8192||(long)image.Width*image.Height>32000000) throw new InvalidDataException("图片尺寸过大");}encoded=Convert.ToBase64String(bytes);},delegate(string error) {var current=Find(z.Id);if(error!="") Notice(error);else if(current!=null) {Change(delegate {current.Style.Image=encoded;current.Style.Background="image";});Settings(Owner(current).Monitor,"style");}});
                } else if(op=="partition_update") Update(z,c);
            } catch(Exception e) {Error=e.Message;Notice(e.Message);}
        }
        void Update(Partition z,Dictionary<string,object> c) {
            string field=S(c,"field"),value=S(c,"value");var s=z.Style.Copy();string name=z.Name;
            // Prepare/validate a small value object. A slider must not serialize every
            // file reference, reconcile every HWND, or refresh the Shell visibility lease.
            Action prepare=delegate {
                if(field=="name") name=String.IsNullOrWhiteSpace(value)?"新分区":value.Substring(0,Math.Min(60,value.Length));
                else if(field=="follow_desktop") s.FollowDesktop=Convert.ToBoolean(value);
                else if(field=="title_visible") s.TitleVisible=Convert.ToBoolean(c["value"]);
                else if(field=="border") s.Border=Convert.ToBoolean(c["value"]);
                else if(field=="title_align"&&new[]{"left","center","right"}.Contains(value)) s.TitleAlign=value;
                else if(field=="background"&&new[]{"none","color","image"}.Contains(value)) s.Background=value;
                else if(field=="clear_image") {s.Image="";s.Background="none";}
                else if(field=="invisible") {s.Background="none";s.Border=false;s.TitleVisible=false;}
                else if(new[]{"color","title_color","border_color"}.Contains(field)) {if(!System.Text.RegularExpressions.Regex.IsMatch(value,@"^#[0-9a-fA-F]{6}$")) throw new InvalidDataException("请输入 #RRGGBB 格式的颜色");if(field=="color") s.Color=value;else if(field=="title_color") s.TitleColor=value;else s.BorderColor=value;}
                else {int n; if(!Int32.TryParse(value,out n)) return;if(field=="opacity") s.Opacity=Math.Max(0,Math.Min(100,n));if(field=="radius") s.Radius=Math.Max(0,Math.Min(28,n));if(field=="icon_size") s.IconSize=Math.Max(28,Math.Min(64,n));if(field=="icon_gap") s.IconGap=Math.Max(0,Math.Min(32,n));if(field=="padding") s.Padding=Math.Max(0,Math.Min(32,n));}
            };
            prepare();Remember(z.Id+":"+field);z.Style=s;z.Name=name;
            needsSave=true;saveAt=DateTime.UtcNow.AddMilliseconds(180);
            PartitionWindow window;if(Windows.TryGetValue(z.Id,out window)) window.InvalidateContent();
        }
        public void Dispose() {if(disposed) return;disposed=true;CloseCanvas();desktopMenu.Dispose();if(needsSave) Flush();hotkey.Dispose();boxes.ExternalClaim=null;foreach(var w in Windows.Values) w.Dispose();foreach(var g in guides.Values) g.Dispose();Icons.Dispose();Catalog.Dispose();boxes.SetExternalItems(new string[0]);}
        sealed class EditHotkey : NativeWindow,IDisposable {
            readonly Action action;readonly bool registered;
            internal EditHotkey(Action action) {this.action=action;CreateHandle(new CreateParams());registered=Native.RegisterHotKey(Handle,2,0x4006,0x45);}
            protected override void WndProc(ref Message m) {if(m.Msg==0x312) action();base.WndProc(ref m);}
            public void Dispose() {if(registered) Native.UnregisterHotKey(Handle,2);DestroyHandle();}
        }
    }
}
