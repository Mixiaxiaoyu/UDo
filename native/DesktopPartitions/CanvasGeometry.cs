using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace UDo.DesktopPartitions {
    internal sealed class CanvasRegion {
        internal string Id;internal Rectangle Bounds;
        internal CanvasRegion Copy() {return new CanvasRegion {Id=Id,Bounds=Bounds};}
    }
    internal sealed class CanvasSeam {
        internal bool Vertical;internal int Before,After;
        internal readonly List<int> Low=new List<int>(),High=new List<int>();
    }
    // Pure draft geometry: neither preview nor history touches Shell or persisted files.
    internal sealed class PartitionCanvasDraft {
        internal readonly Size Size;internal readonly int Gap;
        internal List<CanvasRegion> Regions;
        readonly List<List<CanvasRegion>> undo=new List<List<CanvasRegion>>(),redo=new List<List<CanvasRegion>>();
        internal bool CanUndo {get {return undo.Count>0;}}internal bool CanRedo {get {return redo.Count>0;}}
        internal PartitionCanvasDraft(Size size,IEnumerable<CanvasRegion> regions,int gap=18) {Size=size;Gap=gap;Regions=regions.Select(r=>r.Copy()).ToList();}
        internal bool Valid(IDictionary<int,Rectangle> changes,bool minimum=true) {
            foreach(var entry in changes) {
                int index=entry.Key;var r=entry.Value;
                if(index<-1||index>=Regions.Count||r.Width<(minimum?150:1)||r.Height<(minimum?130:1)||!new Rectangle(Point.Empty,Size).Contains(r)) return false;
                if(index<0&&Regions.Count>=80) return false;
                for(int i=0;i<Regions.Count;i++) {
                    Rectangle other;if(i==index) continue;if(!changes.TryGetValue(i,out other)) other=Regions[i].Bounds;
                    if(r.IntersectsWith(other)) return false;
                }
            }
            return true;
        }
        internal bool Commit(IDictionary<int,Rectangle> changes) {
            if(!Valid(changes)) return false;
            if(!changes.Any(p=>p.Key<0||Regions[p.Key].Bounds!=p.Value)) return true;
            Remember();foreach(var p in changes) if(p.Key<0) Regions.Add(new CanvasRegion {Id=Guid.NewGuid().ToString("N"),Bounds=p.Value});else Regions[p.Key].Bounds=p.Value;
            return true;
        }
        internal bool Set(int index,Rectangle bounds) {return Commit(new Dictionary<int,Rectangle>{{index,bounds}});}
        static List<CanvasRegion> Copy(List<CanvasRegion> regions) {return regions.Select(r=>r.Copy()).ToList();}
        void Remember() {undo.Add(Copy(Regions));if(undo.Count>80) undo.RemoveAt(0);redo.Clear();}
        internal void Remove(int index) {if(index<0||index>=Regions.Count) return;Remember();Regions.RemoveAt(index);}
        internal void Clear() {if(Regions.Count==0) return;Remember();Regions.Clear();}
        internal void Undo() {if(CanUndo) {redo.Add(Copy(Regions));Regions=undo[undo.Count-1];undo.RemoveAt(undo.Count-1);}}
        internal void Redo() {if(CanRedo) {undo.Add(Copy(Regions));Regions=redo[redo.Count-1];redo.RemoveAt(redo.Count-1);}}
        internal static int Edge(Rectangle r,Point p,int tolerance) {
            if(!Rectangle.Inflate(r,tolerance,tolerance).Contains(p)) return 0;
            return (Math.Abs(p.X-r.Left)<=tolerance?1:Math.Abs(p.X-r.Right)<=tolerance?2:0)|
                (Math.Abs(p.Y-r.Top)<=tolerance?4:Math.Abs(p.Y-r.Bottom)<=tolerance?8:0);
        }
        static bool SpanOverlap(Rectangle a,Rectangle b,bool vertical) {return vertical?Math.Min(a.Bottom,b.Bottom)>Math.Max(a.Top,b.Top):Math.Min(a.Right,b.Right)>Math.Max(a.Left,b.Left);}
        internal CanvasSeam SeamAt(Point p,int tolerance) {
            for(int axis=0;axis<2;axis++) for(int i=0;i<Regions.Count;i++) for(int j=0;j<Regions.Count;j++) {
                if(i==j) continue;bool vertical=axis==0;var a=Regions[i].Bounds;var b=Regions[j].Bounds;
                int before=vertical?a.Right:a.Bottom,after=vertical?b.Left:b.Top;
                if(after<before||after-before>48||!SpanOverlap(a,b,vertical)) continue;
                int coordinate=vertical?p.X:p.Y,along=vertical?p.Y:p.X;
                int lo=vertical?Math.Max(a.Top,b.Top):Math.Max(a.Left,b.Left),hi=vertical?Math.Min(a.Bottom,b.Bottom):Math.Min(a.Right,b.Right);
                if(along<lo+tolerance||along>hi-tolerance||coordinate<before-tolerance||coordinate>after+tolerance) continue;
                var seam=new CanvasSeam {Vertical=vertical,Before=before,After=after};seam.Low.Add(i);seam.High.Add(j);
                // Include connected T-junctions, but leave disconnected seams alone.
                bool added=true;while(added) {added=false;for(int k=0;k<Regions.Count;k++) {
                    var r=Regions[k].Bounds;
                    if(!seam.Low.Contains(k)&&(vertical?r.Right:r.Bottom)==before&&seam.High.Any(n=>SpanOverlap(r,Regions[n].Bounds,vertical))) {seam.Low.Add(k);added=true;}
                    if(!seam.High.Contains(k)&&(vertical?r.Left:r.Top)==after&&seam.Low.Any(n=>SpanOverlap(r,Regions[n].Bounds,vertical))) {seam.High.Add(k);added=true;}
                }}return seam;
            }
            return null;
        }
        internal Dictionary<int,Rectangle> Split(CanvasSeam seam,int delta) {
            int min=Int32.MinValue,max=Int32.MaxValue;
            foreach(int i in seam.Low) min=Math.Max(min,(seam.Vertical?150:130)-(seam.Vertical?Regions[i].Bounds.Width:Regions[i].Bounds.Height));
            foreach(int i in seam.High) max=Math.Min(max,(seam.Vertical?Regions[i].Bounds.Width:Regions[i].Bounds.Height)-(seam.Vertical?150:130));
            delta=Math.Max(min,Math.Min(max,delta));var changes=new Dictionary<int,Rectangle>();
            foreach(int i in seam.Low) {var r=Regions[i].Bounds;if(seam.Vertical) r.Width+=delta;else r.Height+=delta;changes[i]=r;}
            foreach(int i in seam.High) {var r=Regions[i].Bounds;if(seam.Vertical) {r.X+=delta;r.Width-=delta;}else {r.Y+=delta;r.Height-=delta;}changes[i]=r;}
            return changes;
        }
        internal Rectangle Snap(Rectangle r,int index,int edges,bool move,int tolerance,out int guideX,out int guideY) {
            int dx=tolerance+1,dy=tolerance+1;guideX=guideY=-1;
            var left=new List<int>{0};var right=new List<int>{Size.Width};var top=new List<int>{0};var bottom=new List<int>{Size.Height};
            for(int i=0;i<Regions.Count;i++) if(i!=index) {
                var other=Regions[i].Bounds;left.AddRange(new[]{other.Left,other.Right+Gap});right.AddRange(new[]{other.Right,other.Left-Gap});
                top.AddRange(new[]{other.Top,other.Bottom+Gap});bottom.AddRange(new[]{other.Bottom,other.Top-Gap});
            }
            if(move||(edges&1)!=0) foreach(int x in left) if(Math.Abs(x-r.Left)<Math.Abs(dx)) {dx=x-r.Left;guideX=x;}
            if(move||(edges&2)!=0) foreach(int x in right) if(Math.Abs(x-r.Right)<Math.Abs(dx)) {dx=x-r.Right;guideX=x;}
            if(move||(edges&4)!=0) foreach(int y in top) if(Math.Abs(y-r.Top)<Math.Abs(dy)) {dy=y-r.Top;guideY=y;}
            if(move||(edges&8)!=0) foreach(int y in bottom) if(Math.Abs(y-r.Bottom)<Math.Abs(dy)) {dy=y-r.Bottom;guideY=y;}
            if(Math.Abs(dx)>tolerance) {dx=0;guideX=-1;}if(Math.Abs(dy)>tolerance) {dy=0;guideY=-1;}
            if(move) r.Offset(dx,dy);
            else r=Rectangle.FromLTRB(r.Left+((edges&1)!=0?dx:0),r.Top+((edges&4)!=0?dy:0),r.Right+((edges&2)!=0?dx:0),r.Bottom+((edges&8)!=0?dy:0));
            return r;
        }
    }
}
