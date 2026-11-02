using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace UDo.DesktopPartitions {
    // File references only. This document has no relationship to the storage-box schema.
    public sealed class Partition {
        public string Id=Guid.NewGuid().ToString("N"),Name="新分区";
        public float X=.1f,Y=.1f,W=.3f,H=.3f;
        public PartitionStyle Style=new PartitionStyle();
        public List<string> Items=new List<string>();
    }
    public sealed class PartitionStyle {
        public bool TitleVisible=false,Border=false,FollowDesktop=true;
        public string TitleAlign="left",TitleColor="#ffffff",Background="none",Color="#e8effa",Image="",BorderColor="#b8c8df";
        public int Opacity=65,Radius=12,IconSize=40,IconGap=8,Padding=10;
        internal PartitionStyle Copy() {return (PartitionStyle)MemberwiseClone();}
    }
    public sealed class PartitionScreen {
        public string Monitor="",Template="";
        public int Gap=18;
        public List<Partition> Zones=new List<Partition>();
    }
    public sealed class PartitionDocument {
        public int Version=1;
        public List<PartitionScreen> Screens=new List<PartitionScreen>();
    }
    internal sealed class PartitionStore {
        readonly string path;
        internal static JavaScriptSerializer Json() {return new JavaScriptSerializer {MaxJsonLength=32*1024*1024,RecursionLimit=64};}
        internal PartitionStore(string path) {this.path=path;}
        internal static PartitionDocument Copy(PartitionDocument doc) {return Json().Deserialize<PartitionDocument>(Json().Serialize(doc));}
        internal static PartitionDocument Parse(string text) {
            var doc=Json().Deserialize<PartitionDocument>(text);Validate(doc);return doc;
        }
        internal PartitionDocument Load() {
            if(!File.Exists(path)) return new PartitionDocument();
            try {return Parse(File.ReadAllText(path));}
            catch {if(File.Exists(path+".bak")) return Parse(File.ReadAllText(path+".bak"));throw;}
        }
        internal static void Validate(PartitionDocument doc) {
            if(doc==null||doc.Version!=1||doc.Screens==null||doc.Screens.Count>32) throw new InvalidDataException("分区配置版本或屏幕数量无效");
            var ids=new HashSet<string>();var monitors=new HashSet<string>();var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var screen in doc.Screens) {
                if(screen==null||String.IsNullOrEmpty(screen.Monitor)||!monitors.Add(screen.Monitor)||screen.Zones==null||screen.Zones.Count>80) throw new InvalidDataException("分区显示器数据无效");
                screen.Gap=Math.Max(4,Math.Min(48,screen.Gap));
                if(!PartitionLayout.Templates.Contains(screen.Template)) screen.Template="";
                foreach(var z in screen.Zones) {
                    if(z==null||String.IsNullOrEmpty(z.Id)||!ids.Add(z.Id)||z.Style==null||z.Items==null||z.Items.Count>100000) throw new InvalidDataException("分区数据无效");
                    if(!Finite(z.X)||!Finite(z.Y)||!Finite(z.W)||!Finite(z.H)||z.X<0||z.Y<0||z.W<.01||z.H<.01||z.X+z.W>1.001||z.Y+z.H>1.001) throw new InvalidDataException("分区位置越界");
                    if(screen.Zones.Any(other=>other!=null&&other!=z&&PartitionLayout.Overlaps(z,other))) throw new InvalidDataException("分区互相重叠");
                    z.Name=String.IsNullOrWhiteSpace(z.Name)?"新分区":z.Name.Substring(0,Math.Min(60,z.Name.Length));
                    z.Items=z.Items.Where(p=>!String.IsNullOrWhiteSpace(p)&&Path.IsPathRooted(p)&&files.Add(p)).ToList();
                    var s=z.Style;
                    if(!new[]{"none","color","image"}.Contains(s.Background)) s.Background="none";
                    if(!new[]{"left","center","right"}.Contains(s.TitleAlign)) s.TitleAlign="left";
                    foreach(string color in new[]{s.TitleColor,s.Color,s.BorderColor}) if(!System.Text.RegularExpressions.Regex.IsMatch(color??"",@"^#[0-9a-fA-F]{6}$")) throw new InvalidDataException("颜色须为 #RRGGBB");
                    s.Opacity=Clamp(s.Opacity,0,100);s.Radius=Clamp(s.Radius,0,28);s.IconSize=Clamp(s.IconSize,28,64);s.IconGap=Clamp(s.IconGap,0,32);s.Padding=Clamp(s.Padding,0,32);
                    if((s.Image??"").Length>2800000) throw new InvalidDataException("背景图片不能超过 2 MB");
                    if(!String.IsNullOrEmpty(s.Image)) using(var stream=new MemoryStream(Convert.FromBase64String(s.Image))) using(var image=Image.FromStream(stream)) {
                        if(image.Width>8192||image.Height>8192||(long)image.Width*image.Height>32000000) throw new InvalidDataException("背景图片尺寸过大");
                    }
                }
            }
            CheckBudget(doc);
        }
        internal static void CheckBudget(PartitionDocument doc) {
            var zones=doc.Screens.SelectMany(s=>s.Zones);
            if(zones.Sum(z=>(long)(z.Style.Image??"").Length)>16*1024*1024||zones.Sum(z=>(long)z.Items.Count)>100000) throw new InvalidDataException("分区配置已达容量上限：图片合计 16 MB、文件引用合计十万条");
        }
        static int Clamp(int n,int lo,int hi) {return Math.Max(lo,Math.Min(hi,n));}
        static bool Finite(float n) {return !Single.IsNaN(n)&&!Single.IsInfinity(n);}
        internal void Save(PartitionDocument doc) {Write(path,Json().Serialize(doc));}
        internal static void Write(string path,string text) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+".tmp";
            File.WriteAllText(temp,text);if(File.Exists(path)) File.Replace(temp,path,path+".bak");else File.Move(temp,path);
        }
    }
    internal static class PartitionLayout {
        internal static readonly string[] Templates={"two","three","four","focus","vertical"};
        internal static bool Overlaps(Partition a,Partition b) {return a.X<b.X+b.W-.0005f&&a.X+a.W>b.X+.0005f&&a.Y<b.Y+b.H-.0005f&&a.Y+a.H>b.Y+.0005f;}
        internal static Rectangle Bounds(Partition z,Rectangle work) {return new Rectangle(work.X+(int)Math.Round(z.X*work.Width),work.Y+(int)Math.Round(z.Y*work.Height),Math.Max(1,(int)Math.Round(z.W*work.Width)),Math.Max(1,(int)Math.Round(z.H*work.Height)));}
        internal static void Remember(Partition z,Rectangle r,Rectangle work) {z.X=(float)(r.X-work.X)/work.Width;z.Y=(float)(r.Y-work.Y)/work.Height;z.W=(float)r.Width/work.Width;z.H=(float)r.Height/work.Height;}
        internal static Rectangle Clamp(Rectangle r,Rectangle work) {int w=Math.Max(1,Math.Min(work.Width,Math.Max(150,r.Width))),h=Math.Max(1,Math.Min(work.Height,Math.Max(130,r.Height)));return new Rectangle(Math.Max(work.X,Math.Min(work.Right-w,r.X)),Math.Max(work.Y,Math.Min(work.Bottom-h,r.Y)),w,h);}
        internal static List<RectangleF> Rects(string template,int count,Size work,int gap) {
            var rects=new List<RectangleF>();
            if(template=="focus") {rects.Add(new RectangleF(0,0,.66f,1));rects.Add(new RectangleF(.66f,0,.34f,.5f));rects.Add(new RectangleF(.66f,.5f,.34f,.5f));}
            else {
                int n=template=="two"?2:template=="three"||template=="vertical"?3:template=="four"?4:count;
                int cols=template=="vertical"?1:template=="two"?2:template=="three"?3:template=="four"?2:Math.Min(work.Width<work.Height?2:3,Math.Max(1,n));
                int rows=(int)Math.Ceiling((double)n/cols);
                for(int i=0;i<n;i++) rects.Add(new RectangleF((float)(i%cols)/cols,(float)(i/cols)/rows,1f/cols,1f/rows));
            }
            float gx=(float)gap/2/Math.Max(1,work.Width),gy=(float)gap/2/Math.Max(1,work.Height);
            return rects.Select(r=> {float x=Math.Min(gx,r.Width/4),y=Math.Min(gy,r.Height/4);return new RectangleF(r.X+x,r.Y+y,r.Width-x*2,r.Height-y*2);}).ToList();
        }
        internal static void Apply(PartitionScreen screen,string template,Size work) {
            var rects=Rects(template,screen.Zones.Count,work,screen.Gap);var old=screen.Zones;
            var next=rects.Select((r,i)=>new Partition {Name=i<old.Count?old[i].Name:"分区 "+(i+1),Style=i<old.Count?old[i].Style:new PartitionStyle(),X=r.X,Y=r.Y,W=r.Width,H=r.Height}).ToList();
            for(int i=0;i<old.Count;i++) next[i%next.Count].Items.AddRange(old[i].Items);
            screen.Zones=next;screen.Template=template;
        }
        internal static Grid GridFor(PartitionStyle style,Size size,bool edit,float scale,int count,Rectangle[] holes=null) {return new Grid(style,size,edit,scale,count,holes);}
        internal sealed class Grid {
            internal readonly int Columns,Rows,Visible,Overflow,CellWidth,CellHeight,Gap,Left,Top,Capacity;
            readonly List<int> slots;readonly Dictionary<int,int> slotIndex;
            internal Grid(PartitionStyle s,Size size,bool edit,float scale,int count,Rectangle[] holes) {
                Left=(int)(s.Padding*scale);Top=Left+(int)((s.TitleVisible||edit?32:2)*scale);
                Gap=(int)(s.IconGap*scale);CellWidth=s.FollowDesktop?UDo.DesktopZones.DesktopAppearance.CellWidth(scale):(int)(Math.Max(64,s.IconSize+30)*scale);CellHeight=s.FollowDesktop?UDo.DesktopZones.DesktopAppearance.CellHeight(scale):(int)((s.IconSize+38)*scale);
                Columns=Math.Max(1,(size.Width-Left*2+Gap)/Math.Max(1,CellWidth+Gap));Rows=Math.Max(0,(size.Height-Top-Left+Gap)/Math.Max(1,CellHeight+Gap));
                int capacity=Columns*Rows;
                if(holes!=null&&holes.Length>0) {
                    slots=new List<int>();slotIndex=new Dictionary<int,int>();
                    for(int i=0;i<capacity;i++) {var cell=RawCell(i);if(holes.Any(h=>h.IntersectsWith(cell))) continue;slotIndex[i]=slots.Count;slots.Add(i);}capacity=slots.Count;
                }
                Capacity=capacity;Visible=Math.Min(count,Math.Max(0,capacity-(count>capacity?1:0)));Overflow=count-Visible;
            }
            Rectangle RawCell(int i) {return new Rectangle(Left+i%Columns*(CellWidth+Gap),Top+i/Columns*(CellHeight+Gap),CellWidth,CellHeight);}
            internal Rectangle Cell(int i) {return RawCell(slots==null?i:i<slots.Count?slots[i]:0);}
            internal int Hit(Point p) {int x=p.X-Left,y=p.Y-Top;if(x<0||y<0) return -1;int col=x/(CellWidth+Gap),row=y/(CellHeight+Gap),i=row*Columns+col;if(col>=Columns) return -1;if(slotIndex!=null&&!slotIndex.TryGetValue(i,out i)) return -1;return i<Visible&&Cell(i).Contains(p)?i:-1;}
        }
    }
}
