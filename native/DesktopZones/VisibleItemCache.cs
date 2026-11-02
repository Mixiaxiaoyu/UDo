using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

namespace UDo.DesktopZones {
    // The viewport owns slots; paths own bindings. Rebinding always clears pixels.
    // All methods run on the UI thread, including asynchronous icon invalidation.
    internal sealed class VisibleItemCache : IDisposable {
        readonly Dictionary<string,Bitmap> bound=new Dictionary<string,Bitmap>(StringComparer.OrdinalIgnoreCase);
        readonly Stack<Bitmap> free=new Stack<Bitmap>();
        Size size;string appearance;int limit;
        internal int Count {get {return bound.Count+free.Count;}}
        internal long Bytes {get {return (long)Count*size.Width*size.Height*4;}}
        internal int Rasterizations;
        internal void Begin(Size cell,string style,IList<string> items,int first,int end) {
            if(size!=cell||appearance!=style) {Clear();size=cell;appearance=style;}
            long bytes=Math.Max(1,(long)cell.Width*cell.Height*4);
            limit=Math.Min(Math.Max(0,end-first),Math.Min(256,(int)Math.Min(256,16*1024*1024/bytes)));
            var keep=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for(int i=first;i<Math.Min(end,first+limit);i++) keep.Add(items[i]);
            foreach(string key in new List<string>(bound.Keys)) if(!keep.Contains(key)) {free.Push(bound[key]);bound.Remove(key);}
            while(Count>limit&&free.Count>0) free.Pop().Dispose();
        }
        internal void Invalidate(HashSet<string> keys) {
            foreach(string key in keys) {Bitmap bitmap;if(bound.TryGetValue(key,out bitmap)) {bound.Remove(key);free.Push(bitmap);}}
        }
        internal void Draw(Graphics target,Point at,string path,Action<Graphics,string> paint) {
            Bitmap bitmap;bool transient=false;
            if(!bound.TryGetValue(path,out bitmap)) {
                bitmap=free.Count>0?free.Pop():new Bitmap(size.Width,size.Height,PixelFormat.Format32bppPArgb);
                using(var g=Graphics.FromImage(bitmap)) {g.Clear(Color.Transparent);g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;paint(g,path);}
                Rasterizations++;
                if(bound.Count<limit) bound[path]=bitmap;else transient=true;
            }
            target.DrawImageUnscaled(bitmap,at);if(transient) bitmap.Dispose();
        }
        internal void Clear() {foreach(var bitmap in bound.Values) bitmap.Dispose();bound.Clear();while(free.Count>0) free.Pop().Dispose();}
        public void Dispose() {Clear();}
    }
}
