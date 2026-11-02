using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace UDo.DesktopPartitions {
    internal sealed class DesktopItem {
        internal string Path,Category;
        internal Point Position;
        internal Rectangle Bounds;
        internal bool FileReference=true;
        internal static string Classify(string path) {
            if(Directory.Exists(path)) return "文件夹";
            string ext=System.IO.Path.GetExtension(path).ToLowerInvariant();
            if(new[]{".lnk",".exe",".url",".appref-ms"}.Contains(ext)) return "应用与快捷方式";
            if(new[]{".png",".jpg",".jpeg",".gif",".webp",".svg",".psd",".ai",".bmp"}.Contains(ext)) return "图片与设计素材";
            if(new[]{".pdf",".txt",".md",".doc",".docx",".xls",".xlsx",".ppt",".pptx"}.Contains(ext)) return "文档资料";
            if(new[]{".zip",".rar",".7z",".tar",".gz"}.Contains(ext)) return "压缩文件";
            return "其他文件";
        }
    }
    // User operations are serialized with catalog reads. Completion callbacks run on UI Tick.
    internal sealed class DesktopCatalog : IDisposable {
        readonly Queue<Action> work=new Queue<Action>(),done=new Queue<Action>();
        readonly object gate=new object();readonly AutoResetEvent wake=new AutoResetEvent(false);readonly Thread worker;
        readonly Func<DesktopItem[]> read;bool queued,refreshPaused,refreshDeferred;volatile bool stopping;
        readonly List<FileSystemWatcher> watchers=new List<FileSystemWatcher>();
        internal DesktopItem[] Items=new DesktopItem[0];internal bool Ready;internal string Error="";internal int Revision;
        internal DesktopCatalog(Func<DesktopItem[]> read=null) {
            this.read=read??Read;
            worker=new Thread(Run) {IsBackground=true,Name="UDo partition catalog"};worker.SetApartmentState(ApartmentState.STA);worker.Start();
            if(read==null) foreach(string folder in new[]{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)}.Distinct()) {
                if(!Directory.Exists(folder)) continue;
                try {var watcher=new FileSystemWatcher(folder);watcher.Created+=delegate {Refresh();};watcher.Deleted+=delegate {Refresh();};watcher.Renamed+=delegate {Refresh();};watcher.EnableRaisingEvents=true;watchers.Add(watcher);} catch { }
            }
            Refresh();
        }
        internal void Refresh() {
            lock(gate) {if(stopping) return;if(refreshPaused) {refreshDeferred=true;return;}if(queued) return;queued=true;}
            Enqueue(delegate {try {long started=PerformanceProbe.Start();var result=read();PerformanceProbe.End("scan_ms",started);PerformanceProbe.Count("watcher_rescans");Complete(delegate {Items=result;Ready=true;Revision++;Error="";});} catch(Exception e) {string error=e.Message;Complete(delegate {Error=error;});} finally {lock(gate) queued=false;}},null);
        }
        internal void PauseRefresh(bool paused) {
            bool refresh=false;
            lock(gate) {refreshPaused=paused;if(!paused&&refreshDeferred) {refreshDeferred=false;refresh=true;}}
            if(refresh) Refresh();
        }
        internal void Snapshot(Action<DesktopItem[]> callback,Action<string> error) {
            DesktopItem[] result=null;
            Enqueue(delegate {result=read();},delegate(string message) {if(message.Length>0) {error(message);return;}Items=result;Ready=true;Revision++;callback(result);});
        }
        internal void Enqueue(Action action,Action<string> finish) {
            lock(gate) {if(stopping) return;work.Enqueue(delegate {string error="";try {action();} catch(Exception e) {error=e.Message;}if(finish!=null) Complete(delegate {finish(error);});});wake.Set();}
        }
        void Complete(Action action) {lock(gate) if(!stopping) done.Enqueue(action);}
        internal void Pump() {for(int i=0;i<16;i++) {Action action;lock(gate) {if(done.Count==0) break;action=done.Dequeue();}action();}}
        void Run() {
            int hr=CoInitializeEx(IntPtr.Zero,2);
            try {while(!stopping) {Action action=null;lock(gate) if(work.Count>0) action=work.Dequeue();if(action!=null) action();else wake.WaitOne();}}
            finally {if(hr>=0) CoUninitialize();}
        }
        static DesktopItem[] Read() {
            var items=new List<DesktopItem>();
            ItemCallback callback=delegate(string path,int x,int y,int left,int top,int width,int height) {bool file=Path.IsPathRooted(path)&&(File.Exists(path)||Directory.Exists(path));items.Add(new DesktopItem {Path=path,Position=new Point(x,y),Bounds=new Rectangle(left,top,width,height),FileReference=file,Category=file?DesktopItem.Classify(path):"系统图标"});};
            Marshal.ThrowExceptionForHR(PartitionDesktopItems(callback));GC.KeepAlive(callback);return items.ToArray();
        }
        public void Dispose() {if(stopping) return;stopping=true;foreach(var watcher in watchers) watcher.Dispose();wake.Set();if(worker.Join(200)) wake.Dispose();}
        [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Unicode)] delegate void ItemCallback([MarshalAs(UnmanagedType.LPWStr)] string path,int x,int y,int left,int top,int width,int height);
        [DllImport("DesktopShell.dll",CallingConvention=CallingConvention.Cdecl)] static extern int PartitionDesktopItems(ItemCallback callback);
        [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved,uint mode);
        [DllImport("ole32.dll")] static extern void CoUninitialize();
    }
}
