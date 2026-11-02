using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace UDo.DesktopZones {
    // Serializable state is independent of windows and Shell integration.
    public sealed class Zone {
        public string Id=Guid.NewGuid().ToString("N"), Name="收纳盒", Monitor="", Theme="light", Edge="none";
        public bool ShowName=true, AutoFit=true, Collapsed=false;
        // Optional normalized position of the folded header, independent of W/H.
        public float FoldX=-1,FoldY=-1;
        public float Transparency=0;
        public float X=.1f,Y=.12f,W=.26f,H=.30f;
        public List<string> Items=new List<string>();
    }
    public sealed class ZoneDocument { public int Version=2; public List<Zone> Zones=new List<Zone>(); }
    internal sealed class ZoneStore {
        readonly string path; readonly JavaScriptSerializer json=new JavaScriptSerializer();
        internal ZoneStore(string path) {this.path=path;}
        internal ZoneDocument Load() {
            string source=path;
            if(!File.Exists(source)) source=Path.Combine(Path.GetDirectoryName(path),"desktop-zones.json");
            if(!File.Exists(source)) return new ZoneDocument();
            Exception last=null;
            foreach(string candidate in new[]{source,source+".bak"}) {
                try {
                    var doc=json.Deserialize<ZoneDocument>(File.ReadAllText(candidate));
                    if(doc==null || (doc.Version!=1&&doc.Version!=2) || doc.Zones==null) throw new InvalidDataException("收纳盒配置格式不受支持");
                    var ids=new HashSet<string>(); var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach(var zone in doc.Zones) {
                        if(zone==null || !ids.Add(zone.Id)) throw new InvalidDataException("收纳盒标识重复");
                        zone.Items=(zone.Items??new List<string>()).Where(p=>!String.IsNullOrWhiteSpace(p) && paths.Add(p)).ToList();
                        zone.Name=(zone.Name??"收纳盒").Substring(0,Math.Min(60,(zone.Name??"收纳盒").Length));
                        if(doc.Version==1 && System.Text.RegularExpressions.Regex.IsMatch(zone.Name,@"^分区 \d+$")) zone.Name=zone.Name.Replace("分区 ","收纳盒 ");
                        zone.Theme=zone.Theme=="dark"?"dark":"light";
                        if(!BoxPlacement.Edges.Contains(zone.Edge)) zone.Edge="none";
                        if(!ZoneLayout.Finite(zone.FoldX)||!ZoneLayout.Finite(zone.FoldY)||zone.FoldX<0||zone.FoldX>1||zone.FoldY<0||zone.FoldY>1) {zone.FoldX=-1;zone.FoldY=-1;}
                        zone.Transparency=ZoneLayout.Finite(zone.Transparency)?Math.Max(0,Math.Min(.9f,zone.Transparency)):0;
                        if(!ZoneLayout.Finite(zone.X)||!ZoneLayout.Finite(zone.Y)||!ZoneLayout.Finite(zone.W)||!ZoneLayout.Finite(zone.H)) throw new InvalidDataException("收纳盒位置无效");
                    }
                    doc.Version=2;return doc;
                } catch(Exception e) {last=e;}
            }
            throw new InvalidDataException("收纳盒配置无法读取，已保留原文件。",last);
        }
        internal void Save(ZoneDocument doc) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp=path+".tmp"; File.WriteAllText(temp,json.Serialize(doc));
            if(File.Exists(path)) File.Replace(temp,path,path+".bak"); else File.Move(temp,path);
        }
    }
    internal static class ZoneLayout {
        internal static bool Finite(float n) {return !Single.IsNaN(n)&&!Single.IsInfinity(n);}
        internal static Rectangle Clamp(Rectangle rect,Rectangle work) {
            int w=Math.Min(work.Width,Math.Max(180,rect.Width)), h=Math.Min(work.Height,Math.Max(150,rect.Height));
            return new Rectangle(Math.Max(work.Left,Math.Min(work.Right-w,rect.X)),Math.Max(work.Top,Math.Min(work.Bottom-h,rect.Y)),w,h);
        }
        internal static Rectangle Restore(Zone zone,Rectangle work) {
            return Clamp(new Rectangle(work.X+(int)(zone.X*work.Width),work.Y+(int)(zone.Y*work.Height),(int)(zone.W*work.Width),(int)(zone.H*work.Height)),work);
        }
        internal static void Remember(Zone zone,Rectangle rect,Rectangle work,string monitor) {
            zone.Monitor=monitor; zone.X=(float)(rect.X-work.X)/work.Width; zone.Y=(float)(rect.Y-work.Y)/work.Height;
            zone.W=(float)rect.Width/work.Width; zone.H=(float)rect.Height/work.Height;
        }
        internal static int Columns(int width,float scale) {return Math.Max(1,(int)((width-24*scale)/DesktopAppearance.CellWidth(scale)));}
        internal static int CompactWidth(int width,float scale) {
            int columns=Columns(width,scale);
            return Math.Max(180,(int)Math.Ceiling(columns*DesktopAppearance.CellWidth(scale)+24*scale));
        }
        internal static int Height(int width,int count,bool title,float scale) {
            return (int)((title?44:18)*scale)+Math.Max(1,(count+Columns(width,scale)-1)/Columns(width,scale))*(int)DesktopAppearance.CellHeight(scale)+(int)(12*scale);
        }
        internal static Rectangle Cell(int index,int width,bool title,float scale) {
            int columns=Columns(width,scale), cell=DesktopAppearance.CellWidth(scale);
            float extra=Math.Max(0,width-24*scale-columns*cell)/(columns+1);
            int column=index%columns;
            return new Rectangle((int)Math.Round(12*scale+extra+(cell+extra)*column),(int)((title?44:18)*scale)+index/columns*(int)DesktopAppearance.CellHeight(scale),cell,(int)DesktopAppearance.CellHeight(scale));
        }
        internal static void VisibleRange(int width,int height,int scroll,int count,float scale,out int first,out int end) {
            int columns=Columns(width,scale),row=Math.Max(1,(int)DesktopAppearance.CellHeight(scale));
            first=Math.Min(count,Math.Max(0,scroll/row)*columns);
            int rows=Math.Max(0,(height-(int)(44*scale)+scroll+row-1)/row);
            end=Math.Min(count,Math.Max(first,rows*columns));
        }
        internal static int Hit(Point point,int width,int height,int scroll,int count,float scale) {
            int header=(int)(44*scale),row=(int)DesktopAppearance.CellHeight(scale);
            if(point.Y<header||point.Y>=height) return -1;
            int columns=Columns(width,scale),itemRow=(point.Y-header+scroll)/row;
            for(int column=0;column<columns;column++) {
                Rectangle cell=Cell(column,width,true,scale);
                if(point.X>=cell.Left&&point.X<cell.Right) {
                    int index=itemRow*columns+column;
                    return index<count?index:-1;
                }
            }
            return -1;
        }
    }
}
