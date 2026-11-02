using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace UDo.DesktopZones {
    internal static class DesktopAppearance {
        static volatile int logicalSide=48,revision,textArgb=unchecked((int)0xffffffff);
        static volatile bool shadow=true;
        static string settings="";static readonly object gate=new object();
        internal static int Revision {get {return revision;}}
        internal static int Side(float scale) {return Math.Max(16,(int)Math.Round(logicalSide*scale));}
        internal static int CellWidth(float scale) {return Math.Max(Side(scale)+(int)(24*scale),(int)(SystemInformation.IconSpacingSize.Width*scale));}
        internal static int CellHeight(float scale) {return Math.Max(Side(scale)+(int)(48*scale),(int)(SystemInformation.IconSpacingSize.Height*scale));}
        internal static void Refresh() {
            try {
                int side,dpi;uint color;if(DesktopVisualMetrics(out side,out dpi,out color)<0) return;
                int logical=Math.Max(16,Math.Min(256,(int)Math.Round(side*96.0/Math.Max(96,dpi))));
                int argb=Color.FromArgb(255,(int)(color&255),(int)((color>>8)&255),(int)((color>>16)&255)).ToArgb();
                bool shadows=Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced","ListviewShadow",1))!=0;
                string signature=SystemFonts.IconTitleFont.ToString()+"|"+SystemInformation.IconSpacingSize+"|"+Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced","IconsOnly",0)+"|"+Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced","HideFileExt",1);
                lock(gate) if(logical!=logicalSide||argb!=textArgb||shadows!=shadow||signature!=settings) {logicalSide=logical;textArgb=argb;shadow=shadows;settings=signature;revision++;}
            }catch { }
        }
        internal static void Paint(Graphics g,Size cell,int side,float scale,string path,BoxIconCache icons) {
            Icon icon=icons.Get(path,side);
            float ratio=Math.Min((float)side/icon.Width,(float)side/icon.Height);
            int w=(int)Math.Round(icon.Width*ratio),h=(int)Math.Round(icon.Height*ratio);
            g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            var pixels=icons.Pixels(path);
            if(pixels!=null) g.DrawImageUnscaled(pixels,(cell.Width-pixels.Width)/2,side-pixels.Height);
            else g.DrawIcon(icon,new Rectangle((cell.Width-w)/2,side-h,w,h));
            using(var font=new Font(SystemFonts.IconTitleFont.FontFamily,SystemFonts.IconTitleFont.SizeInPoints*96/72*scale,SystemFonts.IconTitleFont.Style,GraphicsUnit.Pixel))
            using(var format=new StringFormat {Alignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.LineLimit}) {
                var rect=new RectangleF(1,side+3*scale,cell.Width-2,Math.Max(font.GetHeight(g),cell.Height-side-3*scale));
                string label=icons.Label(path);
                if(shadow) using(var brush=new SolidBrush(Color.FromArgb(190,0,0,0))) {var offset=rect;offset.Offset(scale,scale);g.DrawString(label,font,brush,offset,format);}
                using(var brush=new SolidBrush(Color.FromArgb(textArgb))) g.DrawString(label,font,brush,rect,format);
            }
        }
        [DllImport("DesktopShell.dll",CallingConvention=CallingConvention.Cdecl)] static extern int DesktopVisualMetrics(out int side,out int dpi,out uint textColor);
    }
}
