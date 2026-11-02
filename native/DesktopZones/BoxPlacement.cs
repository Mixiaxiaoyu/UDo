using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UDo.DesktopZones {
    // Pure geometry; expanded bounds never become the folded header or edge handle.
    internal static class BoxPlacement {
        [StructLayout(LayoutKind.Sequential)] struct AppBarData {
            internal int Size; internal IntPtr Window; internal uint Callback; internal uint Edge;
            internal Rectangle Rect; internal IntPtr Param;
        }
        [DllImport("shell32.dll")] static extern IntPtr SHAppBarMessage(uint message,ref AppBarData data);
        internal static bool TaskbarHidden;
        internal static Rectangle WorkArea(Screen screen) {
            var data=new AppBarData {Size=Marshal.SizeOf(typeof(AppBarData))};
            bool autoHide=(SHAppBarMessage(4,ref data).ToInt64()&1)!=0;
            return TaskbarHidden||autoHide?screen.Bounds:screen.WorkingArea;
        }
        internal static readonly string[] Edges={"none","left","right","top","bottom"};
        internal static string Snap(Rectangle rect,Rectangle work,float scale) {
            int distance=(int)(18*scale);
            if(rect.Left<=work.Left+distance) return "left";
            if(rect.Right>=work.Right-distance) return "right";
            if(rect.Top<=work.Top+distance) return "top";
            if(rect.Bottom>=work.Bottom-distance) return "bottom";
            return "none";
        }
        internal static Rectangle Anchor(Rectangle rect,Rectangle work,string edge) {
            rect=ZoneLayout.Clamp(rect,work);
            if(edge=="left") rect.X=work.Left;
            if(edge=="right") rect.X=work.Right-rect.Width;
            if(edge=="top") rect.Y=work.Top;
            if(edge=="bottom") rect.Y=work.Bottom-rect.Height;
            return rect;
        }
        internal static Rectangle Folded(Rectangle expanded,Rectangle work,Point at,float scale) {
            expanded=Anchor(expanded,work,"none");
            int height=Math.Min(work.Height,Math.Max(1,(int)(40*scale)));
            return new Rectangle(Math.Max(work.Left,Math.Min(work.Right-expanded.Width,at.X)),Math.Max(work.Top,Math.Min(work.Bottom-height,at.Y)),expanded.Width,height);
        }
        internal static Rectangle Visible(Rectangle expanded,Rectangle work,string edge,bool collapsed,bool revealed,float scale,Point? foldedAt=null) {
            expanded=Anchor(expanded,work,edge);
            if(edge!="none"&&!revealed) {
                int length=(int)(64*scale),thickness=Math.Max(6,(int)(6*scale));
                if(edge=="left"||edge=="right") return new Rectangle(edge=="left"?work.Left:work.Right-thickness,Math.Max(work.Top,Math.Min(work.Bottom-length,expanded.Top+(expanded.Height-length)/2)),thickness,length);
                return new Rectangle(Math.Max(work.Left,Math.Min(work.Right-length,expanded.Left+(expanded.Width-length)/2)),edge=="top"?work.Top:work.Bottom-thickness,length,thickness);
            }
            if(collapsed&&edge=="none") return Folded(expanded,work,foldedAt??expanded.Location,scale);
            return expanded;
        }
    }
}
