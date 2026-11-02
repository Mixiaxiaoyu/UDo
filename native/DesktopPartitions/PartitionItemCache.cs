using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using UDo.DesktopZones;

namespace UDo.DesktopPartitions {
    // Cache only visible icon/label tiles. A live resize changes their positions,
    // not their pixels; avoid rerasterizing every filename on every pointer frame.
    internal sealed class PartitionItemCache : IDisposable {
        const int Limit=256;
        readonly Dictionary<string,Bitmap> tiles=new Dictionary<string,Bitmap>(StringComparer.OrdinalIgnoreCase);
        Size size;int side,appearance=-1;float scale;Font font;
        internal void Begin(Size cell,int iconSide,float dpi,Font textFont,IList<string> items,int visible) {
            if(size!=cell||side!=iconSide||scale!=dpi||font!=textFont||appearance!=DesktopAppearance.Revision) {Clear();size=cell;side=iconSide;scale=dpi;font=textFont;appearance=DesktopAppearance.Revision;}
            var keep=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for(int i=0;i<Math.Min(visible,Limit);i++) keep.Add(items[i]);
            InvalidateExcept(keep);
        }
        void InvalidateExcept(HashSet<string> keep) {foreach(string key in new List<string>(tiles.Keys)) if(!keep.Contains(key)) {tiles[key].Dispose();tiles.Remove(key);}}
        internal void Invalidate(HashSet<string> paths) {foreach(string key in paths) {Bitmap tile;if(tiles.TryGetValue(key,out tile)) {tile.Dispose();tiles.Remove(key);}}}
        internal void Draw(Graphics target,Point at,string path,BoxIconCache icons) {
            Bitmap tile;bool transient=false;
            if(!tiles.TryGetValue(path,out tile)) {
                tile=new Bitmap(size.Width,size.Height,PixelFormat.Format32bppPArgb);
                using(var g=Graphics.FromImage(tile)) {
                    g.Clear(Color.Transparent);g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    DesktopAppearance.Paint(g,size,side,scale,path,icons);
                }
                if(tiles.Count<Limit) tiles[path]=tile;else transient=true;
            }
            target.DrawImageUnscaled(tile,at);if(transient) tile.Dispose();
        }
        void Clear() {foreach(var tile in tiles.Values) tile.Dispose();tiles.Clear();}
        public void Dispose() {Clear();}
    }
}
