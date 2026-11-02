using UDo.DesktopPartitions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace UDo.DesktopZones {
    // UI-thread LRU; Shell extraction happens only on this bounded STA queue.
    // Completion is drained by the UI so an icon cannot be disposed while drawing.
    internal sealed class BoxIconCache : IDisposable {
        sealed class Entry {
            internal Icon Icon;internal Bitmap Pixels;internal LinkedListNode<string> Node;internal string Label;internal int Side,Appearance;
            internal long Bytes {get {return (long)Icon.Width*Icon.Height*4+(Pixels==null?0:(long)Pixels.Width*Pixels.Height*4);}}
            internal void Dispose() {Icon.Dispose();if(Pixels!=null) Pixels.Dispose();}
        }
        readonly Dictionary<string,Entry> cache=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);
        readonly LinkedList<string> lru=new LinkedList<string>();
        readonly HashSet<string> pending=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Queue<KeyValuePair<string,int>> requests=new Queue<KeyValuePair<string,int>>();
        readonly Queue<KeyValuePair<string,Entry>> completed=new Queue<KeyValuePair<string,Entry>>();
        readonly object gate=new object();readonly AutoResetEvent wake=new AutoResetEvent(false);
        readonly Thread worker;readonly Func<string,Icon> extract;readonly int capacity;
        readonly Icon fallback=(Icon)SystemIcons.Application.Clone();volatile bool stopping;
        long cachedBytes;
        internal long Bytes {get {return cachedBytes;}}
        internal int Count {get {return cache.Count;}}
        internal int QueueLength {get {lock(gate) return pending.Count;}}
        internal bool Contains(string path) {return cache.ContainsKey(path);}
        internal BoxIconCache() : this(1024,null) {}
        internal string Label(string path) {Entry entry;return cache.TryGetValue(path,out entry)&&!String.IsNullOrEmpty(entry.Label)?entry.Label:System.IO.Path.GetFileName(path);}
        internal Bitmap Pixels(string path) {Entry entry;return cache.TryGetValue(path,out entry)?entry.Pixels:null;}
        internal BoxIconCache(int capacity,Func<string,Icon> extract) {
            this.capacity=capacity;this.extract=extract;
            worker=new Thread(Run) {IsBackground=true,Name="UDo visible icons"};
            worker.SetApartmentState(ApartmentState.STA);worker.Start();
        }
        internal Icon Get(string path,int side=48) {
            PerformanceProbe.Count("icon_requests");Entry entry;
            if(cache.TryGetValue(path,out entry)) {
                if(entry.Side==side&&entry.Appearance==DesktopAppearance.Revision) {PerformanceProbe.Count("icon_cache_hits");lru.Remove(entry.Node);lru.AddFirst(entry.Node);return entry.Icon;}
                cachedBytes-=entry.Bytes;entry.Dispose();lru.Remove(entry.Node);cache.Remove(path);
            }
            lock(gate) {
                if(!stopping&&pending.Count<256&&pending.Add(path)) {requests.Enqueue(new KeyValuePair<string,int>(path,side));PerformanceProbe.Peak("icon_queue_peak",pending.Count);wake.Set();}
            }
            return fallback;
        }
        internal HashSet<string> Pump() {
            var changed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for(int i=0;i<32;i++) {
                KeyValuePair<string,Entry> item;
                lock(gate) {if(completed.Count==0) break;item=completed.Dequeue();pending.Remove(item.Key);}
                Entry oldEntry;if(cache.TryGetValue(item.Key,out oldEntry)) {cachedBytes-=oldEntry.Bytes;oldEntry.Dispose();lru.Remove(oldEntry.Node);}
                item.Value.Node=lru.AddFirst(item.Key);cache[item.Key]=item.Value;cachedBytes+=item.Value.Bytes;changed.Add(item.Key);
                while(cache.Count>capacity||cachedBytes>32*1024*1024) {string old=lru.Last.Value;cachedBytes-=cache[old].Bytes;cache[old].Dispose();cache.Remove(old);lru.RemoveLast();}
            }
            return changed;
        }
        void Run() {
            int hr=CoInitializeEx(IntPtr.Zero,2);
            try {
                var refresh=DateTime.MinValue;
                while(!stopping) {
                    if(extract==null&&DateTime.UtcNow>=refresh) {DesktopAppearance.Refresh();refresh=DateTime.UtcNow.AddSeconds(5);}
                    KeyValuePair<string,int> request=default(KeyValuePair<string,int>);lock(gate) {if(requests.Count>0) request=requests.Dequeue();}
                    if(request.Key==null) {wake.WaitOne(5000);continue;}
                    Entry entry=null;
                    try {
                        entry=extract==null?Extract(request.Key,request.Value):new Entry {Icon=extract(request.Key),Side=request.Value};if(entry.Icon==null) entry.Icon=(Icon)SystemIcons.Application.Clone();
                        using(var source=entry.Icon.ToBitmap()) {
                            var visible=VisibleBounds(source);
                            // Some Shell image-list entries contain a small icon in a
                            // large transparent canvas. Match Explorer's apparent size.
                            var crop=visible.Width<source.Width/2&&visible.Height<source.Height/2?visible:new Rectangle(Point.Empty,source.Size);
                            float ratio=Math.Min((float)entry.Side/crop.Width,(float)entry.Side/crop.Height);
                            entry.Pixels=new Bitmap(Math.Max(1,(int)Math.Round(crop.Width*ratio)),Math.Max(1,(int)Math.Round(crop.Height*ratio)),System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                            using(var g=Graphics.FromImage(entry.Pixels)) {g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;g.DrawImage(source,new Rectangle(Point.Empty,entry.Pixels.Size),crop,GraphicsUnit.Pixel);}
                        }
                    }
                    catch {if(entry!=null&&entry.Icon!=null) entry.Dispose();entry=new Entry {Icon=(Icon)SystemIcons.Application.Clone(),Side=request.Value};}
                    entry.Appearance=DesktopAppearance.Revision;
                    lock(gate) {if(stopping) entry.Dispose();else completed.Enqueue(new KeyValuePair<string,Entry>(request.Key,entry));}
                }
            } finally {if(hr>=0) CoUninitialize();}
        }
        static Entry Extract(string path,int side) {
            IntPtr handle;var name=new System.Text.StringBuilder(1024);
            Marshal.ThrowExceptionForHR(DesktopItemVisual(path,side,out handle,name,name.Capacity));
            try {using(var borrowed=Icon.FromHandle(handle)) return new Entry {Icon=(Icon)borrowed.Clone(),Label=name.ToString(),Side=side};}
            finally {Native.DestroyIcon(handle);}
        }
        static Rectangle VisibleBounds(Bitmap source) {
            int left=source.Width,top=source.Height,right=-1,bottom=-1;
            for(int y=0;y<source.Height;y++) for(int x=0;x<source.Width;x++) if(source.GetPixel(x,y).A>12) {left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);}
            return right<left?new Rectangle(Point.Empty,source.Size):Rectangle.FromLTRB(left,top,right+1,bottom+1);
        }
        public void Dispose() {
            stopping=true;wake.Set();if(worker.Join(200)) wake.Dispose();
            lock(gate) {while(completed.Count>0) completed.Dequeue().Value.Dispose();}
            foreach(var entry in cache.Values) entry.Dispose();cache.Clear();cachedBytes=0;fallback.Dispose();
        }
        [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved,uint mode);
        [DllImport("DesktopShell.dll",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)] static extern int DesktopItemVisual(string path,int side,out IntPtr icon,System.Text.StringBuilder name,int capacity);
        [DllImport("ole32.dll")] static extern void CoUninitialize();
    }
}
